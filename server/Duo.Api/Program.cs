using System.Threading.RateLimiting;
using Duo.Api;
using Duo.Api.Rooms;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GuardOptions>(builder.Configuration.GetSection("Guard"));
builder.Services.AddSingleton<ConnectionGuard>();
builder.Services.AddSingleton<IdentityStore>();
builder.Services.AddSingleton<RoomService>();
builder.Services.AddHostedService<RoomSweeper>();

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
