using System.Threading.RateLimiting;
using Duo.Api;
using Duo.Api.Accounts;
using Duo.Api.Data;
using Duo.Api.Rooms;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GuardOptions>(builder.Configuration.GetSection("Guard"));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ConnectionGuard>();
builder.Services.AddSingleton<IdentityStore>();
builder.Services.AddSingleton<RoomService>();
builder.Services.AddSingleton<AuthThrottle>();
builder.Services.AddSingleton(sp => new PasswordHasher(
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>().Value.PasswordIterations));
builder.Services.AddSingleton<AccountService>();
builder.Services.AddHostedService<RoomSweeper>();

var dataDirectory = builder.Configuration["Storage:DataDirectory"] ?? "data";
Directory.CreateDirectory(dataDirectory);

var connectionString = builder.Configuration.GetConnectionString("Duo")
    ?? $"Data Source={Path.Combine(dataDirectory, "duo.db")};Default Timeout=5";

EnsureSqliteDirectory(connectionString);

builder.Services.AddDbContextFactory<DuoDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 16 * 1024;
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumParallelInvocationsPerClient = 4;
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(45);
});

// Caddy is the only hop in front of Kestrel. Clear the known-proxy lists so the
// real client IP from X-Forwarded-For is used for rate limiting. Kestrel must
// stay bound to loopback so clients cannot spoof the header directly.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? ["http://localhost:5173"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("http", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DuoDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.Migrate();
    // WAL persists in the database file; it keeps readers from blocking the
    // single writer while a match result is being recorded.
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
}

app.UseForwardedHeaders();
app.UseWebSockets();
app.UseCors();
app.UseRateLimiter();

app.MapGet("/health", (RoomService rooms) => Results.Ok(new
{
    status = "ok",
    rooms = rooms.RoomCount,
    utc = DateTimeOffset.UtcNow,
}));

app.MapHub<GameHub>("/hub").RequireRateLimiting("http");

app.Run();

static void EnsureSqliteDirectory(string connectionString)
{
    var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
    if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:")
    {
        return;
    }

    var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }
}
