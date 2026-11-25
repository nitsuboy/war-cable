namespace Duo.Api.Accounts;

/// <summary>Tunables for account auth, sessions and brute-force protection.</summary>
public sealed class AuthOptions
{
    public int PasswordIterations { get; set; } = 600_000;

    public int SessionDays { get; set; } = 30;

    public int MinUsernameLength { get; set; } = 3;

    public int MaxUsernameLength { get; set; } = 20;

    public int MinPasswordLength { get; set; } = 8;

    public int MaxPasswordLength { get; set; } = 128;

    public int MaxAttemptsPerIp { get; set; } = 10;

    public int MaxAttemptsPerUsername { get; set; } = 5;

    public int AttemptWindowSeconds { get; set; } = 300;

    public int BlockSeconds { get; set; } = 300;

    public int MaxRegistrationsPerHour { get; set; } = 10;
}
