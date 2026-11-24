namespace Duo.Api;

/// <summary>Tunables for abuse protection and match timing (config section "Guard").</summary>
public sealed class GuardOptions
{
    public int TurnSeconds { get; set; } = 20;

    public int ReconnectGraceSeconds { get; set; } = 30;

    public int MaxConnectionsPerIp { get; set; } = 5;

    public int MaxMessagesPerSecond { get; set; } = 20;

    public int MaxRoomsPerIp { get; set; } = 3;

    public int MaxViolationsBeforeCooldown { get; set; } = 5;

    public int CooldownSeconds { get; set; } = 30;

    public int WaitingRoomIdleMinutes { get; set; } = 10;

    public int FinishedRoomIdleSeconds { get; set; } = 120;
}
