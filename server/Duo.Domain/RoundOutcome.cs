namespace Duo.Domain;

/// <summary>Result of a single simultaneous reveal, from seat A's point of view.</summary>
public enum RoundOutcome
{
    Draw = 0,
    PlayerAWins = 1,
    PlayerBWins = 2,
}

public enum RoundEndReason
{
    Reveal = 0,
    Timeout = 1,
}
