using Duo.Api.Contracts;
using Duo.Api.Data;
using Duo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Duo.Api.Accounts;

public sealed record AccountProfile(
    string AccountId,
    string Username,
    int Rating,
    int Games,
    int Wins,
    int Losses,
    int Draws)
{
    public string PlayerId => AccountService.PlayerIdFor(AccountId);

    public AccountViewDto ToView() => new(Username, Rating, Games, Wins, Losses, Draws);
}

public sealed record AuthOutcome(
    bool Ok,
    string? Error,
    AccountProfile? Account,
    string? Token)
{
    public static AuthOutcome Fail(string error) => new(false, error, null, null);

    public static AuthOutcome Success(AccountProfile account, string token) =>
        new(true, null, account, token);
}

public sealed record MatchRatingResult(int RatingA, int RatingB, int DeltaA, int DeltaB);

/// <summary>
/// Account lifecycle: registration, login, session resolution and revocation.
/// Rating changes for finished matches are added by the ranked task.
/// </summary>
public sealed class AccountService
{
    public const string PlayerIdPrefix = "acct_";

    private readonly IDbContextFactory<DuoDbContext> _factory;
    private readonly PasswordHasher _hasher;
    private readonly AuthOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<AccountService> _log;
    private readonly string _dummyHash;
    private readonly SemaphoreSlim _ratingGate = new(1, 1);

    public AccountService(
        IDbContextFactory<DuoDbContext> factory,
        PasswordHasher hasher,
        IOptions<AuthOptions> options,
        TimeProvider clock,
        ILogger<AccountService> log)
    {
        _factory = factory;
        _hasher = hasher;
        _options = options.Value;
        _clock = clock;
        _log = log;
        // Verifying against a real hash keeps the "no such user" path from being
        // measurably faster than a wrong password.
        _dummyHash = hasher.Hash("timing-equalizer-not-a-real-password");
    }

    public static string PlayerIdFor(string accountId) => PlayerIdPrefix + accountId;

    public static bool IsAccountPlayerId(string? playerId) =>
        playerId is not null && playerId.StartsWith(PlayerIdPrefix, StringComparison.Ordinal);

    public async Task<AuthOutcome> RegisterAsync(
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var name = (username ?? string.Empty).Trim();
        var validationError = ValidateUsername(name) ?? ValidatePassword(password);
        if (validationError is not null)
        {
            return AuthOutcome.Fail(validationError);
        }

        var normalized = Normalize(name);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);

        if (await db.Accounts.AnyAsync(a => a.NormalizedUsername == normalized, cancellationToken))
        {
            return AuthOutcome.Fail("username_taken");
        }

        var now = _clock.GetUtcNow();
        var account = new AccountEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = name,
            NormalizedUsername = normalized,
            PasswordHash = _hasher.Hash(password!),
            Rating = 1000,
            CreatedAt = now,
            LastSeenAt = now,
        };

        var token = SessionTokens.Create();
        db.Accounts.Add(account);
        db.Sessions.Add(NewSession(account.Id, token, now));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Unique index is the final arbiter for two concurrent registrations.
            _log.LogInformation(ex, "Registration race for {Username}", name);
            return AuthOutcome.Fail("username_taken");
        }

        return AuthOutcome.Success(Profile(account), token);
    }

    public async Task<AuthOutcome> LoginAsync(
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var name = (username ?? string.Empty).Trim();
        var candidate = password ?? string.Empty;

        if (name.Length == 0 || candidate.Length == 0)
        {
            _hasher.Verify(candidate, _dummyHash);
            return AuthOutcome.Fail("invalid_credentials");
        }

        var normalized = Normalize(name);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var account = await db.Accounts
            .FirstOrDefaultAsync(a => a.NormalizedUsername == normalized, cancellationToken);

        if (account is null)
        {
            _hasher.Verify(candidate, _dummyHash);
            return AuthOutcome.Fail("invalid_credentials");
        }

        if (!_hasher.Verify(candidate, account.PasswordHash))
        {
            return AuthOutcome.Fail("invalid_credentials");
        }

        var now = _clock.GetUtcNow();
        var token = SessionTokens.Create();
        account.LastSeenAt = now;
        db.Sessions.Add(NewSession(account.Id, token, now));
        await db.SaveChangesAsync(cancellationToken);

        return AuthOutcome.Success(Profile(account), token);
    }

    /// <summary>
    /// Resolves an account from a session token. A caller that supplies a player
    /// id must supply the one bound to that account.
    /// </summary>
    public async Task<AccountProfile?> ResolveSessionAsync(
        string? playerId,
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
        {
            return null;
        }

        var hash = SessionTokens.Hash(token);
        var now = _clock.GetUtcNow();

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var session = await db.Sessions
            .Include(s => s.Account)
            .FirstOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);

        if (session is null || session.ExpiresAt <= now)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(playerId) && !string.Equals(
                playerId,
                PlayerIdFor(session.AccountId),
                StringComparison.Ordinal))
        {
            return null;
        }

        session.LastSeenAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return Profile(session.Account);
    }

    public async Task RevokeSessionAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
        {
            return;
        }

        var hash = SessionTokens.Hash(token);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);
        if (session is not null)
        {
            db.Sessions.Remove(session);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task SweepSessionsAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        // SQLite cannot translate DateTimeOffset comparisons, so filter on the
        // client. Session counts stay tiny and this runs once per minute.
        var sessions = await db.Sessions.ToListAsync(cancellationToken);
        var expired = sessions.Where(s => s.ExpiresAt <= now).ToList();

        if (expired.Count == 0)
        {
            return;
        }

        db.Sessions.RemoveRange(expired);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AccountProfile?> GetProfileAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        return account is null ? null : Profile(account);
    }

    public async Task<IReadOnlyList<LeaderboardRowDto>> LeaderboardAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 50);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Accounts
            .AsNoTracking()
            .OrderByDescending(a => a.Rating)
            .ThenByDescending(a => a.Wins)
            .ThenBy(a => a.NormalizedUsername)
            .Take(limit)
            .Select(a => new { a.Username, a.Rating, a.Games, a.Wins })
            .ToListAsync(cancellationToken);

        return rows
            .Select((row, index) => new LeaderboardRowDto(
                index + 1,
                row.Username,
                row.Rating,
                row.Games,
                row.Wins))
            .ToArray();
    }

    /// <summary>
    /// Applies one ranked result and persists both sides in a single
    /// transaction. Serialised so two finishes can never interleave.
    /// </summary>
    public async Task<MatchRatingResult> ApplyMatchAsync(
        string accountIdA,
        string accountIdB,
        string? winnerAccountId,
        CancellationToken cancellationToken = default)
    {
        await _ratingGate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken);
            var accounts = await db.Accounts
                .Where(a => a.Id == accountIdA || a.Id == accountIdB)
                .ToListAsync(cancellationToken);

            var accountA = accounts.FirstOrDefault(a => a.Id == accountIdA)
                ?? throw new InvalidOperationException("Ranked account A is missing.");
            var accountB = accounts.FirstOrDefault(a => a.Id == accountIdB)
                ?? throw new InvalidOperationException("Ranked account B is missing.");

            var scoreA = winnerAccountId == accountIdA
                ? 1.0
                : winnerAccountId == accountIdB ? 0.0 : 0.5;

            var elo = Elo.Apply(accountA.Rating, accountB.Rating, scoreA);

            accountA.Rating = elo.RatingA;
            accountB.Rating = elo.RatingB;
            accountA.Games++;
            accountB.Games++;

            if (scoreA > 0.5)
            {
                accountA.Wins++;
                accountB.Losses++;
            }
            else if (scoreA < 0.5)
            {
                accountB.Wins++;
                accountA.Losses++;
            }
            else
            {
                accountA.Draws++;
                accountB.Draws++;
            }

            await db.SaveChangesAsync(cancellationToken);
            return new MatchRatingResult(elo.RatingA, elo.RatingB, elo.DeltaA, elo.DeltaB);
        }
        finally
        {
            _ratingGate.Release();
        }
    }

    private SessionEntity NewSession(string accountId, string token, DateTimeOffset now) => new()
    {
        TokenHash = SessionTokens.Hash(token),
        AccountId = accountId,
        CreatedAt = now,
        LastSeenAt = now,
        ExpiresAt = now.AddDays(_options.SessionDays),
    };

    private static AccountProfile Profile(AccountEntity account) => new(
        account.Id,
        account.Username,
        account.Rating,
        account.Games,
        account.Wins,
        account.Losses,
        account.Draws);

    private string? ValidateUsername(string username)
    {
        if (username.Length < _options.MinUsernameLength || username.Length > _options.MaxUsernameLength)
        {
            return "invalid_username";
        }

        foreach (var c in username)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-'))
            {
                return "invalid_username";
            }
        }

        return null;
    }

    private string? ValidatePassword(string? password)
    {
        if (password is null)
        {
            return "weak_password";
        }

        if (password.Length < _options.MinPasswordLength || password.Length > _options.MaxPasswordLength)
        {
            return "weak_password";
        }

        return null;
    }

    private static string Normalize(string username) => username.ToLowerInvariant();
}
