namespace Duo.Domain;

/// <summary>
/// Drives one match: picks, round resolution, track movement, captures,
/// hand recycling and timeouts. Pure logic, no clocks or I/O.
/// </summary>
public sealed class MatchEngine
{
    public MatchEngine(MatchState state)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public MatchState State { get; }

    public static MatchEngine NewMatch(int targetCaptures = 4) =>
        new(new MatchState(targetCaptures));

    public bool CanResolve => State.HasPicked(Player.A) && State.HasPicked(Player.B);

    /// <summary>
    /// Records a pick. The index refers to the player's current hand, which is
    /// how the protocol disambiguates the two sixes. Returns false when the pick
    /// is invalid or the player already picked this round.
    /// </summary>
    public bool TrySubmitPick(Player player, int handIndex)
    {
        if (State.IsOver || State.HasPicked(player))
        {
            return false;
        }

        var hand = State.Hand(player);
        if (handIndex < 0 || handIndex >= hand.Count)
        {
            return false;
        }

        State.SetPending(player, hand[handIndex]);
        return true;
    }

    /// <summary>Resolves the round once both players have picked.</summary>
    public RoundResult? TryResolveRound()
    {
        if (!CanResolve)
        {
            return null;
        }

        var cardA = State.PendingA!.Value;
        var cardB = State.PendingB!.Value;
        var outcome = RoundResolver.Resolve(cardA, cardB);
        var winner = outcome switch
        {
            RoundOutcome.PlayerAWins => Player.A,
            RoundOutcome.PlayerBWins => Player.B,
            _ => (Player?)null,
        };

        var captured = ApplyRoundWinner(winner);
        ConsumeCards(cardA, cardB);

        return BuildResult(RoundEndReason.Reveal, cardA, cardB, outcome, winner, captured);
    }

    /// <summary>
    /// The timed-out player loses the round. No card is revealed or consumed:
    /// the opponent simply gets the track movement / capture.
    /// </summary>
    public RoundResult ApplyTimeout(Player timedOutPlayer)
    {
        if (State.IsOver)
        {
            throw new InvalidOperationException("The match is already over.");
        }

        var winner = timedOutPlayer.Opponent();
        var captured = ApplyRoundWinner(winner);
        State.ClearPending();

        var outcome = winner == Player.A ? RoundOutcome.PlayerAWins : RoundOutcome.PlayerBWins;
        return BuildResult(RoundEndReason.Timeout, null, null, outcome, winner, captured);
    }

    /// <summary>
    /// Nobody picked before the deadline. Treated as a draw: no movement and no
    /// cards consumed, so two stalling players cannot gain anything.
    /// </summary>
    public RoundResult ApplyDoubleTimeout()
    {
        if (State.IsOver)
        {
            throw new InvalidOperationException("The match is already over.");
        }

        State.ClearPending();
        return BuildResult(RoundEndReason.Timeout, null, null, RoundOutcome.Draw, null, false);
    }

    /// <summary>Moves the dragon or captures, then checks the win condition.</summary>
    private bool ApplyRoundWinner(Player? winner)
    {
        if (winner is null)
        {
            return false;
        }

        var side = winner == Player.A ? MatchState.TrackASide : MatchState.TrackBSide;
        var captured = State.Track == side;

        if (captured)
        {
            State.AddCaptures(winner.Value, 1);
            State.Track = MatchState.TrackCenter;
        }
        else
        {
            State.Track += side;
        }

        if (State.Captures(winner.Value) >= State.TargetCaptures)
        {
            State.Winner = winner;
        }

        return captured;
    }

    private void ConsumeCards(CardValue cardA, CardValue cardB)
    {
        State.HandList(Player.A).Remove(cardA);
        State.DiscardList(Player.A).Add(cardA);

        State.HandList(Player.B).Remove(cardB);
        State.DiscardList(Player.B).Add(cardB);

        State.ClearPending();

        RecycleIfEmpty(Player.A);
        RecycleIfEmpty(Player.B);
    }

    /// <summary>When a hand runs out, the whole discard pile comes back.</summary>
    private void RecycleIfEmpty(Player player)
    {
        var hand = State.HandList(player);
        if (hand.Count > 0)
        {
            return;
        }

        var discard = State.DiscardList(player);
        hand.AddRange(discard);
        discard.Clear();
    }

    private RoundResult BuildResult(
        RoundEndReason reason,
        CardValue? cardA,
        CardValue? cardB,
        RoundOutcome outcome,
        Player? roundWinner,
        bool captured) => new()
    {
        Reason = reason,
        CardA = cardA,
        CardB = cardB,
        Outcome = outcome,
        RoundWinner = roundWinner,
        Captured = captured,
        Track = State.Track,
        CapturesA = State.CapturesA,
        CapturesB = State.CapturesB,
        MatchWinner = State.Winner,
    };
}
