using Duo.Api.Accounts;
using Duo.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Duo.Api.Tests;

/// <summary>
/// One throwaway SQLite file per test, migrated with the real migration so the
/// schema is exercised end to end.
/// </summary>
internal sealed class TestDatabase : IDbContextFactory<DuoDbContext>, IDisposable
{
    private readonly string _path;
    private readonly DbContextOptions<DuoDbContext> _options;

    private TestDatabase(string path)
    {
        _path = path;
        _options = new DbContextOptionsBuilder<DuoDbContext>()
            .UseSqlite($"Data Source={path};Default Timeout=5")
            .Options;
    }

    public static async Task<TestDatabase> CreateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "duo-api-tests");
        Directory.CreateDirectory(directory);

        var database = new TestDatabase(Path.Combine(directory, $"{Guid.NewGuid():N}.db"));
        await using var context = database.CreateDbContext();
        await context.Database.MigrateAsync();
        return database;
    }

    public DuoDbContext CreateDbContext() => new(_options);

    public Task<DuoDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());

    public AccountService CreateAccountService(
        TimeProvider? clock = null,
        AuthOptions? options = null,
        int iterations = 1000)
    {
        var resolved = options ?? new AuthOptions { PasswordIterations = iterations };
        return new AccountService(
            this,
            new PasswordHasher(resolved.PasswordIterations),
            Options.Create(resolved),
            clock ?? TimeProvider.System,
            NullLogger<AccountService>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                File.Delete(_path + suffix);
            }
            catch (IOException)
            {
                // Best-effort cleanup only.
            }
        }
    }
}

internal sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan delta) => Now += delta;
}
