using Duo.Api.Accounts;
using Duo.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Duo.Api.Tests;

public class AccountServiceTests
{
    private static readonly AuthOptions TestOptions = new()
    {
        PasswordIterations = 1000,
        SessionDays = 30,
    };

    [Fact]
    public async Task Register_creates_an_account_with_a_session()
    {
        using var database = await TestDatabase.CreateAsync();
        var accounts = database.CreateAccountService(options: TestOptions);

        var outcome = await accounts.RegisterAsync("Hero", "password123");

        Assert.True(outcome.Ok);
        Assert.Null(outcome.Error);
        Assert.NotNull(outcome.Account);
        Assert.Equal("Hero", outcome.Account!.Username);
        Assert.Equal(1000, outcome.Account.Rating);
        Assert.StartsWith(AccountService.PlayerIdPrefix, outcome.Account.PlayerId);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Token));

        await using var db = database.CreateDbContext();
        var stored = await db.Accounts.SingleAsync();
        Assert.Equal("hero", stored.NormalizedUsername);
        Assert.NotEqual("password123", stored.PasswordHash);
        Assert.StartsWith("pbkdf2-sha256$", stored.PasswordHash);
        Assert.Single(await db.Sessions.ToListAsync());
    }

    [Fact]
    public async Task Register_rejects_duplicate_usernames_case_insensitively()
    {
        using var database = await TestDatabase.CreateAsync();
        var accounts = database.CreateAccountService(options: TestOptions);

        Assert.True((await accounts.RegisterAsync("Hero", "password123")).Ok);
        var duplicate = await accounts.RegisterAsync("hero", "password456");

        Assert.False(duplicate.Ok);
        Assert.Equal("username_taken", duplicate.Error);
    }

    [Theory]
    [InlineData("ab", "password123", "invalid_username")]
    [InlineData("abcdefghijklmnopqrstu", "password123", "invalid_username")]
    [InlineData("bad name", "password123", "invalid_username")]
    [InlineData("bad!", "password123", "invalid_username")]
    [InlineData("Hero", "short", "weak_password")]
    public async Task Register_validates_credentials(string username, string password, string error)
    {
        using var database = await TestDatabase.CreateAsync();
        var accounts = database.CreateAccountService(options: TestOptions);

        var outcome = await accounts.RegisterAsync(username, password);

        Assert.False(outcome.Ok);
        Assert.Equal(error, outcome.Error);
    }

    [Fact]
    public async Task Login_rejects_wrong_password_and_unknown_user_generically()
    {
        using var database = await TestDatabase.CreateAsync();
        var accounts = database.CreateAccountService(options: TestOptions);
        Assert.True((await accounts.RegisterAsync("Hero", "password123")).Ok);

        var wrongPassword = await accounts.LoginAsync("Hero", "password999");
        var unknownUser = await accounts.LoginAsync("Nobody", "password123");

        Assert.Equal("invalid_credentials", wrongPassword.Error);
        Assert.Equal("invalid_credentials", unknownUser.Error);
    }

    [Fact]
    public async Task Login_is_case_insensitive_and_issues_a_second_session()
    {
        using var database = await TestDatabase.CreateAsync();
        var accounts = database.CreateAccountService(options: TestOptions);
        var registered = await accounts.RegisterAsync("Hero", "password123");

        var login = await accounts.LoginAsync("hErO", "password123");

        Assert.True(login.Ok);
        Assert.NotEqual(registered.Token, login.Token);

        Assert.NotNull(await accounts.ResolveSessionAsync(registered.Account!.PlayerId, registered.Token));
        Assert.NotNull(await accounts.ResolveSessionAsync(login.Account!.PlayerId, login.Token));
    }

    [Fact]
    public async Task ResolveSession_requires_the_matching_player_id()
    {
        using var database = await TestDatabase.CreateAsync();
        var accounts = database.CreateAccountService(options: TestOptions);
        var registered = await accounts.RegisterAsync("Hero", "password123");

        Assert.Null(await accounts.ResolveSessionAsync("acct_someone-else", registered.Token));
        Assert.NotNull(await accounts.ResolveSessionAsync(null, registered.Token));
        Assert.NotNull(await accounts.ResolveSessionAsync(registered.Account!.PlayerId, registered.Token));
    }

    [Fact]
    public async Task Session_survives_a_service_restart()
    {
        using var database = await TestDatabase.CreateAsync();
        var first = database.CreateAccountService(options: TestOptions);
        var registered = await first.RegisterAsync("Hero", "password123");

        var restarted = database.CreateAccountService(options: TestOptions);
        var resolved = await restarted.ResolveSessionAsync(
            registered.Account!.PlayerId,
            registered.Token);

        Assert.NotNull(resolved);
        Assert.Equal(registered.Account.AccountId, resolved!.AccountId);
    }

    [Fact]
    public async Task Revoked_session_no_longer_resolves()
    {
        using var database = await TestDatabase.CreateAsync();
        var accounts = database.CreateAccountService(options: TestOptions);
        var registered = await accounts.RegisterAsync("Hero", "password123");

        await accounts.RevokeSessionAsync(registered.Token);

        Assert.Null(await accounts.ResolveSessionAsync(registered.Account!.PlayerId, registered.Token));
    }

    [Fact]
    public async Task Expired_session_no_longer_resolves()
    {
        using var database = await TestDatabase.CreateAsync();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var accounts = database.CreateAccountService(clock, TestOptions);
        var registered = await accounts.RegisterAsync("Hero", "password123");

        clock.Advance(TimeSpan.FromDays(TestOptions.SessionDays + 1));

        Assert.Null(await accounts.ResolveSessionAsync(registered.Account!.PlayerId, registered.Token));
    }

    [Fact]
    public async Task SweepSessions_removes_only_expired_rows()
    {
        using var database = await TestDatabase.CreateAsync();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var accounts = database.CreateAccountService(clock, TestOptions);
        var old = await accounts.RegisterAsync("Old", "password123");
        clock.Advance(TimeSpan.FromDays(10));
        var fresh = await accounts.RegisterAsync("Fresh", "password123");

        clock.Advance(TimeSpan.FromDays(TestOptions.SessionDays - 5));
        await accounts.SweepSessionsAsync();

        Assert.Null(await accounts.ResolveSessionAsync(old.Account!.PlayerId, old.Token));
        Assert.NotNull(await accounts.ResolveSessionAsync(fresh.Account!.PlayerId, fresh.Token));
    }

    [Fact]
    public async Task ApplyMatch_moves_both_ratings_and_counters()
    {
        using var database = await TestDatabase.CreateAsync();
        var accounts = database.CreateAccountService(options: TestOptions);
        var alpha = await accounts.RegisterAsync("Alpha", "password123");
        var beta = await accounts.RegisterAsync("Beta", "password123");

        var result = await accounts.ApplyMatchAsync(
            alpha.Account!.AccountId,
            beta.Account!.AccountId,
            alpha.Account.AccountId);

        Assert.Equal(16, result.DeltaA);
        Assert.Equal(-16, result.DeltaB);
        Assert.Equal(1016, result.RatingA);
        Assert.Equal(984, result.RatingB);

        var alphaAfter = await accounts.GetProfileAsync(alpha.Account.AccountId);
        var betaAfter = await accounts.GetProfileAsync(beta.Account.AccountId);
        Assert.Equal(1016, alphaAfter!.Rating);
        Assert.Equal(1, alphaAfter.Games);
        Assert.Equal(1, alphaAfter.Wins);
        Assert.Equal(0, alphaAfter.Losses);
        Assert.Equal(984, betaAfter!.Rating);
        Assert.Equal(1, betaAfter.Games);
        Assert.Equal(1, betaAfter.Losses);
    }

    [Fact]
    public async Task ApplyMatch_winner_b_raises_b_and_lowers_a()
    {
        using var database = await TestDatabase.CreateAsync();
        var accounts = database.CreateAccountService(options: TestOptions);
        var alpha = await accounts.RegisterAsync("Alpha", "password123");
        var beta = await accounts.RegisterAsync("Beta", "password123");

        var result = await accounts.ApplyMatchAsync(
            alpha.Account!.AccountId,
            beta.Account!.AccountId,
            beta.Account.AccountId);

        Assert.Equal(-16, result.DeltaA);
        Assert.Equal(16, result.DeltaB);
    }

    [Fact]
    public async Task ApplyMatch_without_a_winner_is_a_draw()
    {
        using var database = await TestDatabase.CreateAsync();
        var accounts = database.CreateAccountService(options: TestOptions);
        var alpha = await accounts.RegisterAsync("Alpha", "password123");
        var beta = await accounts.RegisterAsync("Beta", "password123");

        var result = await accounts.ApplyMatchAsync(
            alpha.Account!.AccountId,
            beta.Account!.AccountId,
            winnerAccountId: null);

        Assert.Equal(0, result.DeltaA);
        Assert.Equal(0, result.DeltaB);
        Assert.Equal(1, (await accounts.GetProfileAsync(alpha.Account.AccountId))!.Draws);
        Assert.Equal(1, (await accounts.GetProfileAsync(beta.Account.AccountId))!.Draws);
    }

    [Fact]
    public async Task ApplyMatch_persists_across_a_service_restart()
    {
        using var database = await TestDatabase.CreateAsync();
        var first = database.CreateAccountService(options: TestOptions);
        var alpha = await first.RegisterAsync("Alpha", "password123");
        var beta = await first.RegisterAsync("Beta", "password123");
        await first.ApplyMatchAsync(
            alpha.Account!.AccountId,
            beta.Account!.AccountId,
            alpha.Account.AccountId);

        var restarted = database.CreateAccountService(options: TestOptions);
        var profile = await restarted.GetProfileAsync(alpha.Account.AccountId);

        Assert.Equal(1016, profile!.Rating);
        Assert.Equal(1, profile.Wins);
    }

    [Fact]
    public async Task Leaderboard_orders_by_rating_and_clamps_the_limit()
    {
        using var database = await TestDatabase.CreateAsync();
        var accounts = database.CreateAccountService(options: TestOptions);
        await accounts.RegisterAsync("Low", "password123");
        await accounts.RegisterAsync("High", "password123");
        await accounts.RegisterAsync("Mid", "password123");

        await using (var db = database.CreateDbContext())
        {
            var low = await db.Accounts.SingleAsync(a => a.Username == "Low");
            var high = await db.Accounts.SingleAsync(a => a.Username == "High");
            var mid = await db.Accounts.SingleAsync(a => a.Username == "Mid");
            low.Rating = 900;
            mid.Rating = 1200;
            high.Rating = 1500;
            await db.SaveChangesAsync();
        }

        var rows = await accounts.LeaderboardAsync(2);

        Assert.Equal(2, rows.Count);
        Assert.Equal("High", rows[0].Username);
        Assert.Equal(1, rows[0].Rank);
        Assert.Equal(1500, rows[0].Rating);
        Assert.Equal("Mid", rows[1].Username);
        Assert.Equal(2, rows[1].Rank);
    }

    [Fact]
    public async Task Guest_profile_lookup_returns_null()
    {
        using var database = await TestDatabase.CreateAsync();
        var accounts = database.CreateAccountService(options: TestOptions);

        Assert.Null(await accounts.GetProfileAsync("does-not-exist"));
    }
}
