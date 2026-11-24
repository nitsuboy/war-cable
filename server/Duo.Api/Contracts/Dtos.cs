namespace Duo.Api.Contracts;

public sealed record HelloResult(string PlayerId, string SessionToken);

public sealed record JoinResult(
    bool Ok,
    string? Error,
    string? RoomCode,
    bool Queued = false,
    bool Reattached = false);

public sealed record SeatViewDto(
    string Seat,
    string[] Hand,
    bool Picked,
    bool Connected,
    int Captures,
    bool RematchVote);

public sealed record OpponentViewDto(
    string Seat,
    int HandCount,
    bool Picked,
    bool Connected,
    int Captures,
    bool RematchVote);

public sealed record RoomStateDto(
    string Phase,
    string Code,
    int Track,
    int TargetCaptures,
    int TurnSeconds,
    int RoundNumber,
    int DeadlineMs,
    bool Paused,
    string YourSeat,
    SeatViewDto You,
    OpponentViewDto Opponent,
    string? WinnerSeat,
    string? EndReason);

public sealed record RoundResultDto(
    string? YourCard,
    string? OpponentCard,
    string Outcome,
    bool Captured,
    bool OpponentCaptured,
    int Track,
    int YourCaptures,
    int OpponentCaptures,
    string Reason,
    string? MatchWinner);

public sealed record MatchOverDto(string Winner, string Reason);
