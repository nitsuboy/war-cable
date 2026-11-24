using Duo.Domain;

namespace Duo.Domain.Tests;

public class MatchEngineTests
{
    private static RoundResult Play(MatchEngine engine, int aIndex, int bIndex)
    {
        Assert.True(engine.TrySubmitPick(Player.A, aIndex));
        Assert.True(engine.TrySubmitPick(Player.B, bIndex));
        var result = engine.TryResolveRound();
        Assert.NotNull(result);
        return result!;
    }

    [Fact]
    public void New_match_starts_at_center_with_full_hands()
    {
        var engine = MatchEngine.NewMatch();
        var state = engine.State;

        Assert.Equal(0, state.Track);
        Assert.Equal(0, state.CapturesA);
        Assert.Equal(0, state.CapturesB);
        Assert.False(state.IsOver);
        Assert.Equal(9, state.HandCount(Player.A));
        Assert.Equal(9, state.HandCount(Player.B));
        Assert.Empty(state.Discard(Player.A));
        Assert.Empty(state.Discard(Player.B));
        Assert.Equal(4, state.TargetCaptures);
    }

    [Fact]
    public void Rejects_out_of_range_and_repeated_picks()
    {
        var engine = MatchEngine.NewMatch();

        Assert.False(engine.TrySubmitPick(Player.A, -1));
        Assert.False(engine.TrySubmitPick(Player.A, 9));
        Assert.True(engine.TrySubmitPick(Player.A, 0));
        Assert.False(engine.TrySubmitPick(Player.A, 1));
        Assert.Null(engine.TryResolveRound());
    }

    [Fact]
    public void Pick_maps_to_hand_index()
    {
        var engine = MatchEngine.NewMatch();

        Assert.True(engine.TrySubmitPick(Player.A, 0));
        Assert.Equal(CardValue.X, engine.State.PendingA);
    }

    [Fact]
    public void Draw_discards_both_cards_without_moving_the_dragon()
    {
        var engine = MatchEngine.NewMatch();
        // Both play X (index 0).
        var result = Play(engine, 0, 0);

        Assert.Equal(RoundEndReason.Reveal, result.Reason);
        Assert.Equal(RoundOutcome.Draw, result.Outcome);
        Assert.Null(result.RoundWinner);
        Assert.Equal(0, result.Track);
        Assert.False(result.Captured);
        Assert.Equal(8, engine.State.HandCount(Player.A));
        Assert.Equal(8, engine.State.HandCount(Player.B));
        Assert.Single(engine.State.Discard(Player.A));
    }

    [Fact]
    public void Winner_pushes_the_dragon_one_step_towards_their_side()
    {
        var engine = MatchEngine.NewMatch();
        // A plays 6 (index 7), B plays 0 (index 1). 0 beats 6, so B wins.
        var result = Play(engine, 7, 1);

        Assert.Equal(Player.B, result.RoundWinner);
        Assert.Equal(-1, result.Track);
        Assert.False(result.Captured);
    }

    [Fact]
    public void Winning_while_the_dragon_is_on_your_side_captures_it()
    {
        // B always beats A, so B pushes the dragon left and then captures.
        var engine = BuildEngine(
            Enumerable.Repeat(CardValue.Five, MatchState.HandSize),
            Enumerable.Repeat(CardValue.Six, MatchState.HandSize));

        var first = Play(engine, 0, 0);
        Assert.Equal(Player.B, first.RoundWinner);
        Assert.Equal(-1, first.Track);

        var second = Play(engine, 0, 0);
        Assert.True(second.Captured);
        Assert.Equal(1, second.CapturesB);
        Assert.Equal(0, second.Track);
    }

    [Fact]
    public void Capturing_resets_the_dragon_to_the_center()
    {
        var engine = BuildEngine(
            Enumerable.Repeat(CardValue.Five, MatchState.HandSize),
            Enumerable.Repeat(CardValue.Six, MatchState.HandSize));

        Play(engine, 0, 0); // B wins -> -1
        var capture = Play(engine, 0, 0); // B captures
        Assert.Equal(0, capture.Track);
        Assert.Equal(1, capture.CapturesB);
    }

    [Fact]
    public void Four_captures_win_the_match()
    {
        // A always plays 6, B always plays 5: A wins every round.
        var engine = BuildEngine(
            Enumerable.Repeat(CardValue.Six, MatchState.HandSize),
            Enumerable.Repeat(CardValue.Five, MatchState.HandSize));

        RoundResult? last = null;
        for (var i = 0; i < 8; i++)
        {
            last = Play(engine, 0, 0);
        }

        Assert.NotNull(last);
        Assert.Equal(Player.A, last!.MatchWinner);
        Assert.Equal(4, last.CapturesA);
        Assert.True(engine.State.IsOver);
        Assert.False(engine.TrySubmitPick(Player.A, 0));
    }

    [Fact]
    public void Hand_recycles_when_it_runs_out()
    {
        var engine = MatchEngine.NewMatch();

        // Nine draws: both play X every round.
        for (var i = 0; i < 9; i++)
        {
            Play(engine, 0, 0);
            Assert.Equal(9, engine.State.HandCount(Player.A) + engine.State.Discard(Player.A).Count);
        }

        Assert.Equal(9, engine.State.HandCount(Player.A));
        Assert.Empty(engine.State.Discard(Player.A));
        Assert.Equal(9, engine.State.HandCount(Player.B));
        Assert.Empty(engine.State.Discard(Player.B));
    }

    [Fact]
    public void Timeout_gives_the_round_to_the_opponent_without_consuming_cards()
    {
        var engine = MatchEngine.NewMatch();

        var result = engine.ApplyTimeout(Player.A);

        Assert.Equal(RoundEndReason.Timeout, result.Reason);
        Assert.Null(result.CardA);
        Assert.Null(result.CardB);
        Assert.Equal(Player.B, result.RoundWinner);
        Assert.Equal(-1, result.Track);
        Assert.Equal(9, engine.State.HandCount(Player.A));
        Assert.Equal(9, engine.State.HandCount(Player.B));
        Assert.Empty(engine.State.Discard(Player.A));
    }

    [Fact]
    public void Timeout_while_the_dragon_is_on_the_opponent_side_captures()
    {
        var engine = MatchEngine.NewMatch();
        Play(engine, 7, 1); // B wins -> B side

        var result = engine.ApplyTimeout(Player.A);

        Assert.True(result.Captured);
        Assert.Equal(1, result.CapturesB);
        Assert.Equal(0, result.Track);
    }

    [Fact]
    public void Timeout_clears_pending_picks_without_discarding_them()
    {
        var engine = MatchEngine.NewMatch();
        Assert.True(engine.TrySubmitPick(Player.A, 0));
        Assert.True(engine.TrySubmitPick(Player.B, 1));

        engine.ApplyTimeout(Player.A);

        Assert.Null(engine.State.PendingA);
        Assert.Null(engine.State.PendingB);
        Assert.Equal(9, engine.State.HandCount(Player.A));
        Assert.Equal(9, engine.State.HandCount(Player.B));
    }

    [Fact]
    public void Double_timeout_is_a_draw_and_consumes_nothing()
    {
        var engine = MatchEngine.NewMatch();

        var result = engine.ApplyDoubleTimeout();

        Assert.Equal(RoundEndReason.Timeout, result.Reason);
        Assert.Equal(RoundOutcome.Draw, result.Outcome);
        Assert.Null(result.RoundWinner);
        Assert.False(result.Captured);
        Assert.Equal(0, result.Track);
        Assert.Equal(9, engine.State.HandCount(Player.A));
        Assert.Equal(9, engine.State.HandCount(Player.B));
        Assert.Empty(engine.State.Discard(Player.A));
    }

    [Fact]
    public void Random_matches_keep_every_invariant()
    {
        var random = new Random(20261122);

        for (var match = 0; match < 200; match++)
        {
            var engine = MatchEngine.NewMatch();
            var previousCapturesA = 0;
            var previousCapturesB = 0;

            for (var round = 0; round < 500 && !engine.State.IsOver; round++)
            {
                RoundResult result;
                if (random.NextDouble() < 0.1)
                {
                    var loser = random.Next(2) == 0 ? Player.A : Player.B;
                    result = engine.ApplyTimeout(loser);
                }
                else
                {
                    var aIndex = random.Next(engine.State.HandCount(Player.A));
                    var bIndex = random.Next(engine.State.HandCount(Player.B));
                    result = Play(engine, aIndex, bIndex);
                }

                Assert.InRange(result.Track, MatchState.TrackBSide, MatchState.TrackASide);
                Assert.True(result.CapturesA >= previousCapturesA);
                Assert.True(result.CapturesB >= previousCapturesB);
                Assert.InRange(result.CapturesA, 0, engine.State.TargetCaptures);
                Assert.InRange(result.CapturesB, 0, engine.State.TargetCaptures);

                previousCapturesA = result.CapturesA;
                previousCapturesB = result.CapturesB;

                for (var seat = 0; seat < 2; seat++)
                {
                    var player = (Player)seat;
                    Assert.Equal(
                        MatchState.HandSize,
                        engine.State.HandCount(player) + engine.State.Discard(player).Count);
                }
            }

            Assert.True(engine.State.IsOver, "Random matches should always finish.");
        }
    }

    /// <summary>Builds a match with different hands per seat for deterministic tests.</summary>
    private static MatchEngine BuildEngine(IEnumerable<CardValue> handA, IEnumerable<CardValue> handB) =>
        new(new MatchState(4, handA, handB));
}
