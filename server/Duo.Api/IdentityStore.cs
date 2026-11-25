using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Duo.Api.Accounts;

namespace Duo.Api;

/// <summary>
/// Issues and validates anonymous guest identities. Registered accounts are
/// resolved by <see cref="AccountService"/> through their session token instead.
/// Guests are still kept in memory: losing them on restart costs a reconnect,
/// not an account.
/// </summary>
public sealed class IdentityStore
{
    public sealed record Identity(string PlayerId, string Token);

    private sealed class Entry
    {
        public required string Token { get; init; }
        public long LastSeenUnix { get; set; }
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private static long NowUnix => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>
    /// Returns the guest identity for the given credentials, creating a new one
    /// when the client has none, or null when the credentials are invalid.
    /// Account-shaped player ids are rejected so a guest can never shadow one.
    /// </summary>
    public Identity? ResolveGuest(string? playerId, string? token)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return Create();
        }

        playerId = playerId.Trim();
        if (AccountService.IsAccountPlayerId(playerId))
        {
            return null;
        }

        if (playerId.Length is < 8 or > 64 || !playerId.All(IsSafeChar))
        {
            return null;
        }

        if (_entries.TryGetValue(playerId, out var entry))
        {
            if (string.IsNullOrEmpty(token) || !TokenEquals(entry.Token, token))
            {
                return null;
            }

            entry.LastSeenUnix = NowUnix;
            return new Identity(playerId, entry.Token);
        }

        // Unknown id: this happens after a server restart. Accept a reasonably
        // strong token supplied by the client so reconnection keeps working.
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 16 or > 128)
        {
            return null;
        }

        var created = new Entry { Token = token, LastSeenUnix = NowUnix };
        if (!_entries.TryAdd(playerId, created))
        {
            return ResolveGuest(playerId, token);
        }

        return new Identity(playerId, token);
    }

    public void Sweep()
    {
        var cutoff = NowUnix - (int)TimeSpan.FromHours(24).TotalSeconds;
        foreach (var pair in _entries)
        {
            if (pair.Value.LastSeenUnix < cutoff)
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }
    }

    private Identity Create()
    {
        var playerId = Guid.NewGuid().ToString("N");
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        _entries[playerId] = new Entry { Token = token, LastSeenUnix = NowUnix };
        return new Identity(playerId, token);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TokenEquals(string expected, string actual)
    {
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(actual);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static bool IsSafeChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '-' or '_';
}
