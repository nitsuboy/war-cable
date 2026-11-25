using Duo.Api.Accounts;
using Microsoft.Extensions.Options;

namespace Duo.Api.Tests;

public class AuthThrottleTests
{
    private static AuthThrottle Create(FakeClock clock, AuthOptions? options = null) =>
        new(
            Options.Create(options ?? new AuthOptions
            {
                MaxAttemptsPerIp = 10,
                MaxAttemptsPerUsername = 3,
                AttemptWindowSeconds = 300,
                BlockSeconds = 300,
                MaxRegistrationsPerHour = 2,
            }),
            clock);

    [Fact]
    public void Username_is_blocked_after_the_attempt_limit()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var throttle = Create(clock);

        throttle.RecordFailure("1.1.1.1", "Hero");
        throttle.RecordFailure("1.1.1.1", "Hero");
        Assert.True(throttle.IsAllowed("2.2.2.2", "Hero"));

        throttle.RecordFailure("1.1.1.1", "Hero");
        Assert.False(throttle.IsAllowed("2.2.2.2", "Hero"));
    }

    [Fact]
    public void Ip_is_blocked_after_failures_across_usernames()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var throttle = Create(clock, new AuthOptions
        {
            MaxAttemptsPerIp = 2,
            MaxAttemptsPerUsername = 100,
            AttemptWindowSeconds = 300,
            BlockSeconds = 300,
        });

        throttle.RecordFailure("1.1.1.1", "One");
        throttle.RecordFailure("1.1.1.1", "Two");

        Assert.False(throttle.IsAllowed("1.1.1.1", "Three"));
        Assert.True(throttle.IsAllowed("9.9.9.9", "Three"));
    }

    [Fact]
    public void Block_expires_after_the_configured_delay()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var throttle = Create(clock);

        for (var i = 0; i < 3; i++)
        {
            throttle.RecordFailure("1.1.1.1", "Hero");
        }

        Assert.False(throttle.IsAllowed("1.1.1.1", "Hero"));

        clock.Advance(TimeSpan.FromSeconds(400));
        Assert.True(throttle.IsAllowed("1.1.1.1", "Hero"));
    }

    [Fact]
    public void Successful_login_clears_the_username_counter()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var throttle = Create(clock);

        throttle.RecordFailure("1.1.1.1", "Hero");
        throttle.RecordFailure("1.1.1.1", "Hero");
        throttle.RecordSuccess("HeRo");

        throttle.RecordFailure("1.1.1.1", "Hero");
        throttle.RecordFailure("1.1.1.1", "Hero");

        Assert.True(throttle.IsAllowed("3.3.3.3", "Hero"));
    }

    [Fact]
    public void Registration_limit_is_per_ip_and_window()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var throttle = Create(clock);

        Assert.True(throttle.TryRegister("1.1.1.1"));
        Assert.True(throttle.TryRegister("1.1.1.1"));
        Assert.False(throttle.TryRegister("1.1.1.1"));
        Assert.True(throttle.TryRegister("2.2.2.2"));

        clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        Assert.True(throttle.TryRegister("1.1.1.1"));
    }
}
