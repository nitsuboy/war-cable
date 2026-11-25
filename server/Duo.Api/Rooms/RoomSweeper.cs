using Duo.Api;
using Duo.Api.Accounts;

namespace Duo.Api.Rooms;

/// <summary>
/// Drives turn timers, reconnect grace and idle cleanup. One ticker for all
/// rooms keeps the design simple and bounded.
/// </summary>
public sealed class RoomSweeper : BackgroundService
{
    private readonly RoomService _rooms;
    private readonly ConnectionGuard _guard;
    private readonly IdentityStore _identity;
    private readonly AccountService _accounts;
    private readonly AuthThrottle _throttle;
    private readonly ILogger<RoomSweeper> _log;
    private long _lastSweepUnixMs;

    public RoomSweeper(
        RoomService rooms,
        ConnectionGuard guard,
        IdentityStore identity,
        AccountService accounts,
        AuthThrottle throttle,
        ILogger<RoomSweeper> log)
    {
        _rooms = rooms;
        _guard = guard;
        _identity = identity;
        _accounts = accounts;
        _throttle = throttle;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _rooms.TickAsync();

                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (now - _lastSweepUnixMs > 60_000)
                {
                    _lastSweepUnixMs = now;
                    _guard.Sweep();
                    _identity.Sweep();
                    _throttle.Sweep();
                    await _accounts.SweepSessionsAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Room sweeper tick failed");
            }
        }
    }
}
