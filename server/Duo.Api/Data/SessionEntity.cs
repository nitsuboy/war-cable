namespace Duo.Api.Data;

/// <summary>
/// One issued session token. Only the SHA-256 of the token is stored, so a
/// database leak cannot be replayed against the hub.
/// </summary>
public sealed class SessionEntity
{
    public required string TokenHash { get; set; }

    public required string AccountId { get; set; }

    public AccountEntity Account { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
