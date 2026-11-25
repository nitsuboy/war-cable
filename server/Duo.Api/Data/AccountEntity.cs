namespace Duo.Api.Data;

/// <summary>
/// Registered player. The password is stored only as a versioned PBKDF2 string;
/// the rating and counters are mutated by the ranked service once T2 lands.
/// </summary>
public sealed class AccountEntity
{
    public required string Id { get; set; }

    public required string Username { get; set; }

    public required string NormalizedUsername { get; set; }

    public required string PasswordHash { get; set; }

    public int Rating { get; set; } = 1000;

    public int Games { get; set; }

    public int Wins { get; set; }

    public int Losses { get; set; }

    public int Draws { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}
