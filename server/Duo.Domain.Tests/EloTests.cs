using Duo.Domain;

namespace Duo.Domain.Tests;

public class EloTests
{
    [Fact]
    public void Equal_ratings_expect_half_a_point()
    {
        Assert.Equal(0.5, Elo.ExpectedScore(1000, 1000), 10);
    }

    [Fact]
    public void Equal_ratings_win_moves_them_apart_by_the_same_amount()
    {
        var result = Elo.Apply(1000, 1000, 1.0);

        Assert.Equal(16, result.DeltaA);
        Assert.Equal(-16, result.DeltaB);
        Assert.Equal(1016, result.RatingA);
        Assert.Equal(984, result.RatingB);
        Assert.Equal(0, result.DeltaA + result.DeltaB);
    }

    [Fact]
    public void Each_result_is_zero_sum()
    {
        var aWin = Elo.Apply(1200, 1000, 1.0);
        var bWin = Elo.Apply(1200, 1000, 0.0);

        Assert.Equal(0, aWin.DeltaA + aWin.DeltaB);
        Assert.Equal(0, bWin.DeltaA + bWin.DeltaB);
        Assert.True(aWin.DeltaA > 0);
        Assert.True(bWin.DeltaA < 0);
        Assert.True(aWin.DeltaA < -bWin.DeltaA);
    }

    [Fact]
    public void Beating_a_higher_rated_opponent_is_worth_more()
    {
        var upset = Elo.Apply(1000, 1400, 1.0);
        var expected = Elo.Apply(1400, 1000, 1.0);

        Assert.True(upset.DeltaA > expected.DeltaA);
    }

    [Fact]
    public void Draw_favours_the_lower_rated_player()
    {
        var result = Elo.Apply(1000, 1400, 0.5);

        Assert.True(result.DeltaA > 0);
        Assert.True(result.DeltaB < 0);
        Assert.Equal(0, result.DeltaA + result.DeltaB);
    }

    [Fact]
    public void Rating_never_drops_below_the_floor()
    {
        var result = Elo.Apply(Elo.FloorRating, 400, 0.0);

        Assert.Equal(Elo.FloorRating, result.RatingA);
        Assert.Equal(0, result.DeltaA);
        Assert.Equal(5, result.DeltaB);
    }

    [Fact]
    public void Deltas_are_symmetric_above_the_rating_floor()
    {
        // 200 is high enough that neither side is clamped at 100 for a ±32 swing.
        for (var ratingA = 200; ratingA <= 2400; ratingA += 100)
        {
            for (var ratingB = 200; ratingB <= 2400; ratingB += 100)
            {
                var win = Elo.Apply(ratingA, ratingB, 1.0);
                Assert.Equal(0, win.DeltaA + win.DeltaB);
                Assert.Equal(ratingA + win.DeltaA, win.RatingA);
                Assert.Equal(ratingB + win.DeltaB, win.RatingB);
            }
        }
    }
}
