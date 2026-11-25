using Duo.Domain;

namespace Duo.Domain.Tests;

public class BotStrategyTests
{
    [Fact]
    public void Empty_hand_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            BotStrategy.ChooseCardIndex(Array.Empty<CardValue>(), capturing: false));
    }

    [Fact]
    public void Single_card_is_the_only_choice()
    {
        Assert.Equal(0, BotStrategy.ChooseCardIndex(new[] { CardValue.Four }, capturing: true));
    }

    [Fact]
    public void Avoids_the_four_when_a_safer_card_is_available()
    {
        // Four only beats 5 and 6 and loses to 0..3, so X (a draw against
        // everything) scores better.
        var hand = new[] { CardValue.Four, CardValue.X };

        Assert.Equal(1, BotStrategy.ChooseCardIndex(hand, capturing: false));
    }

    [Fact]
    public void Picks_a_strong_card_from_the_full_hand()
    {
        var hand = MatchState.DefaultHand();

        // Five and Six tie on raw score and both beat the rest of the hand.
        var index = BotStrategy.ChooseCardIndex(hand, capturing: false, random: () => 0.0);

        Assert.InRange(index, 0, hand.Count - 1);
        Assert.Contains(hand[index], new[] { CardValue.Five, CardValue.Six, CardValue.Three });
    }

    [Fact]
    public void Index_always_points_at_a_real_card()
    {
        var hand = new[] { CardValue.Zero, CardValue.X, CardValue.Six };
        var random = new Random(7);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var index = BotStrategy.ChooseCardIndex(hand, capturing: true, random.NextDouble);
            Assert.InRange(index, 0, hand.Length - 1);
        }
    }
}
