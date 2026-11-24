namespace Duo.Domain;

/// <summary>Snapshot of what happened in one round.</summary>
public sealed record RoundResult
{
    public required RoundEndReason Reason { get; init; }

    /// <summary>Null on timeout (cards are never revealed).</summary>
    public CardValue? CardA { get; init; }

    /// <summary>Null on timeout (cards are never revealed).</summary>
    public CardValue? CardB { get; init; }

    /// <summary>Outcome from seat A's point of view.</summary>
    public required RoundOutcome Outcome { get; init; }

    /// <summary>Null when the round was a draw.</summary>
    public Player? RoundWinner { get; init; }

    public bool Captured { get; init; }

    public required int Track { get; init; }

    public required int CapturesA { get; init; }

    public required int CapturesB { get; init; }

    public Player? MatchWinner { get; init; }
}
