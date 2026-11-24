using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Duo.Api;

/// <summary>
/// In-memory, per-IP guardian: concurrent connections, room creation, message
/// rate and a cooldown for repeat offenders. Keyed by the client IP resolved
/// after ForwardedHeaders, so it sees the real client behind Caddy.
/// </summary>
public sealed class ConnectionGuard
{
    private sealed class IpState
    {
        public int Connections;
        public int Rooms;
        public long WindowSecond;
        public int MessagesInWindow;
        public int Violations;
        public long CooldownUntilUnix;
        public long LastSeenUnix;
    }

    private readonly ConcurrentDictionary<string, IpState> _states = new(StringComparer.Ordinal);
    private readonly GuardOptions _options;

    public ConnectionGuard(IOptions<GuardOptions> options) => _options = options.Value;

    private static long NowUnix => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public bool IsBlocked(string ip)
    {
        var state = Get(ip);
        lock (state)
        {
            return IsBlockedLocked(state);
        }
    }

    public bool TryOpenConnection(string ip)
    {
        var state = Get(ip);
        lock (state)
        {
            TouchLocked(state);
            if (IsBlockedLocked(state))
            {
                return false;
            }

            if (state.Connections >= _options.MaxConnectionsPerIp)
            {
                PenalizeLocked(state);
                return false;
            }

            state.Connections++;
            return true;
        }
    }

    public void CloseConnection(string ip)
    {
        var state = Get(ip);
        lock (state)
        {
            if (state.Connections > 0)
            {
                state.Connections--;
            }
        }
    }

    public bool TryOpenRoom(string ip)
    {
        var state = Get(ip);
        lock (state)
        {
            TouchLocked(state);
            if (IsBlockedLocked(state))
            {
                return false;
            }

            if (state.Rooms >= _options.MaxRoomsPerIp)
            {
                PenalizeLocked(state);
                return false;
            }

            state.Rooms++;
            return true;
        }
    }

    public void CloseRoom(string ip)
    {
        var state = Get(ip);
        lock (state)
        {
            if (state.Rooms > 0)
            {
                state.Rooms--;
            }
        }
    }

    /// <summary>One token per hub call. A burst above the limit counts as a violation.</summary>
    public bool TryConsumeMessage(string ip)
    {
        var state = Get(ip);
        lock (state)
        {
            TouchLocked(state);
            if (IsBlockedLocked(state))
            {
                return false;
            }

            var now = NowUnix;
            if (state.WindowSecond != now)
            {
                state.WindowSecond = now;
                state.MessagesInWindow = 0;
            }

            if (state.MessagesInWindow >= _options.MaxMessagesPerSecond)
            {
                PenalizeLocked(state);
                return false;
            }

            state.MessagesInWindow++;
            return true;
        }
    }

    public void Penalize(string ip)
    {
        var state = Get(ip);
        lock (state)
        {
            PenalizeLocked(state);
        }
    }

    public void Sweep()
    {
        var cutoff = NowUnix - 600;
        foreach (var pair in _states)
        {
            var state = pair.Value;
            lock (state)
            {
                if (state.Connections == 0
                    && state.Rooms == 0
                    && state.CooldownUntilUnix <= NowUnix
                    && state.LastSeenUnix < cutoff)
                {
                    _states.TryRemove(pair.Key, out _);
                }
            }
        }
    }

    private IpState Get(string ip) =>
        _states.GetOrAdd(ip, _ => new IpState { LastSeenUnix = NowUnix });

    private static void TouchLocked(IpState state) => state.LastSeenUnix = NowUnix;

    private bool IsBlockedLocked(IpState state) => state.CooldownUntilUnix > NowUnix;

    private void PenalizeLocked(IpState state)
    {
        state.Violations++;
        if (state.Violations < _options.MaxViolationsBeforeCooldown)
        {
            return;
        }

        state.Violations = 0;
        state.CooldownUntilUnix = NowUnix + _options.CooldownSeconds;
    }
}
