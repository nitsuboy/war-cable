using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Duo.Api.Accounts;

/// <summary>
/// In-memory brute-force guard for auth calls, keyed by IP and by normalized
/// username, plus a per-IP registration cap. Deliberately not persisted: a
/// restart clearing short cooldowns is harmless.
/// </summary>
public sealed class AuthThrottle
{
    private sealed class Window
    {
        public long WindowStartUnix;
        public int Count;
        public long BlockedUntilUnix;
        public long LastSeenUnix;
    }

    private readonly ConcurrentDictionary<string, Window> _entries = new(StringComparer.Ordinal);
    private readonly AuthOptions _options;
    private readonly TimeProvider _clock;

    public AuthThrottle(IOptions<AuthOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    private long NowUnix => _clock.GetUtcNow().ToUnixTimeSeconds();

    public bool IsAllowed(string ip, string? username)
    {
        var now = NowUnix;
        if (!Check($"ip:{ip}", _options.MaxAttemptsPerIp, now))
        {
            return false;
        }

        var normalized = Normalize(username);
        return normalized.Length == 0
            || Check($"user:{normalized}", _options.MaxAttemptsPerUsername, now);
    }

    public void RecordFailure(string ip, string? username)
    {
        var now = NowUnix;
        Increment($"ip:{ip}", _options.MaxAttemptsPerIp, now);

        var normalized = Normalize(username);
        if (normalized.Length > 0)
        {
            Increment($"user:{normalized}", _options.MaxAttemptsPerUsername, now);
        }
    }

    public void RecordSuccess(string? username)
    {
        var normalized = Normalize(username);
        if (normalized.Length > 0)
        {
            _entries.TryRemove($"user:{normalized}", out _);
        }
    }

    /// <summary>Counts registrations per IP in a rolling hour. Returns false when over.</summary>
    public bool TryRegister(string ip)
    {
        var now = NowUnix;
        var entry = _entries.GetOrAdd($"reg:{ip}", _ => new Window { WindowStartUnix = now });
        lock (entry)
        {
            Roll(entry, now, 3600);
            entry.LastSeenUnix = now;
            if (entry.Count >= _options.MaxRegistrationsPerHour)
            {
                return false;
            }

            entry.Count++;
            return true;
        }
    }

    public void Sweep()
    {
        var cutoff = NowUnix - 3600;
        foreach (var pair in _entries)
        {
            var entry = pair.Value;
            lock (entry)
            {
                if (entry.LastSeenUnix < cutoff && entry.BlockedUntilUnix <= NowUnix)
                {
                    _entries.TryRemove(pair.Key, out _);
                }
            }
        }
    }

    private bool Check(string key, int limit, long now)
    {
        var entry = _entries.GetOrAdd(key, _ => new Window { WindowStartUnix = now });
        lock (entry)
        {
            Roll(entry, now, _options.AttemptWindowSeconds);
            entry.LastSeenUnix = now;

            if (entry.BlockedUntilUnix > now)
            {
                return false;
            }

            if (entry.BlockedUntilUnix != 0)
            {
                entry.BlockedUntilUnix = 0;
                entry.Count = 0;
            }

            if (entry.Count < limit)
            {
                return true;
            }

            entry.BlockedUntilUnix = now + _options.BlockSeconds;
            return false;
        }
    }

    private void Increment(string key, int limit, long now)
    {
        var entry = _entries.GetOrAdd(key, _ => new Window { WindowStartUnix = now });
        lock (entry)
        {
            Roll(entry, now, _options.AttemptWindowSeconds);
            entry.LastSeenUnix = now;
            entry.Count++;

            if (entry.Count >= limit)
            {
                entry.BlockedUntilUnix = now + _options.BlockSeconds;
            }
        }
    }

    private static void Roll(Window entry, long now, int windowSeconds)
    {
        if (now - entry.WindowStartUnix >= windowSeconds)
        {
            entry.WindowStartUnix = now;
            entry.Count = 0;
        }
    }

    private static string Normalize(string? username) => (username ?? string.Empty).Trim().ToLowerInvariant();
}
