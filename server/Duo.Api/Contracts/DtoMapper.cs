using Duo.Api.Rooms;
using Duo.Domain;

namespace Duo.Api.Contracts;

/// <summary>
/// Builds per-player views. The opponent's hand is only ever exposed as a count,
/// and their picked card never leaves the server before the reveal.
/// </summary>
public static class DtoMapper
{
    public static RoomStateDto State(Room room, Player viewer)
    {
        var state = room.Engine.State;
        var you = room.SeatOf(viewer);
        var opponent = room.SeatOf(viewer.Opponent());

        var yourSeat = new SeatViewDto(
            Seat: SeatName(viewer),
            Hand: state.Hand(viewer).Select(c => c.Symbol()).ToArray(),
            Picked: state.HasPicked(viewer),
            Connected: you?.Connected ?? false,
            Captures: state.Captures(viewer),
            RematchVote: you?.RematchVote ?? false);

        var opponentSeat = new OpponentViewDto(
            Seat: SeatName(viewer.Opponent()),
            HandCount: state.HandCount(viewer.Opponent()),
            Picked: state.HasPicked(viewer.Opponent()),
            Connected: opponent?.Connected ?? false,
            Captures: state.Captures(viewer.Opponent()),
            RematchVote: opponent?.RematchVote ?? false);

        return new RoomStateDto(
            Phase: PhaseName(room.Phase),
            Code: room.Code,
            Track: state.Track,
            TargetCaptures: state.TargetCaptures,
            TurnSeconds: room.TurnSeconds,
            RoundNumber: room.RoundNumber,
            DeadlineMs: room.DeadlineInMs(),
            Paused: room.Paused,
            YourSeat: SeatName(viewer),
            You: yourSeat,
            Opponent: opponentSeat,
            WinnerSeat: state.Winner is { } winner ? SeatName(winner) : null,
            EndReason: room.EndReason);
    }

    public static RoundResultDto Round(RoundResult result, Player viewer)
    {
        var isA = viewer == Player.A;
        var outcome = result.Outcome switch
        {
            RoundOutcome.Draw => "draw",
            RoundOutcome.PlayerAWins => isA ? "win" : "lose",
            _ => isA ? "lose" : "win",
        };

        var captured = result.Captured && result.RoundWinner == viewer;
        var opponentCaptured =
            result.Captured && result.RoundWinner is not null && result.RoundWinner != viewer;
        var matchWinner = result.MatchWinner is null
            ? null
            : result.MatchWinner == viewer ? "you" : "opponent";

        return new RoundResultDto(
            YourCard: (isA ? result.CardA : result.CardB)?.Symbol(),
            OpponentCard: (isA ? result.CardB : result.CardA)?.Symbol(),
            Outcome: outcome,
            Captured: captured,
            OpponentCaptured: opponentCaptured,
            Track: result.Track,
            YourCaptures: isA ? result.CapturesA : result.CapturesB,
            OpponentCaptures: isA ? result.CapturesB : result.CapturesA,
            Reason: result.Reason == RoundEndReason.Timeout ? "timeout" : "reveal",
            MatchWinner: matchWinner);
    }

    public static string SeatName(Player player) => player == Player.A ? "A" : "B";

    public static string PhaseName(GamePhase phase) => phase switch
    {
        GamePhase.Waiting => "waiting",
        GamePhase.Choosing => "choosing",
        _ => "over",
    };
}
