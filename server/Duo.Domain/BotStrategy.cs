namespace Duo.Domain;

/// <summary>
/// Heuristic opponent. Scores every card in hand by how many faces it beats
/// minus how many beat it, weighting wins while the bot is capturing. A small
/// jitter keeps it from replaying the same hand order every round.
///
/// Pure on purpose: the server drives it from the room ticker, and tests can
/// pin the randomness.
/// </summary>
public static class BotStrategy
{
    private static readonly CardValue[] AllCards = Enum.GetValues<CardValue>();

    public static int ChooseCardIndex(
        IReadOnlyList<CardValue> hand,
        bool capturing,
        Func<double>? random = null)
    {
        ArgumentNullException.ThrowIfNull(hand);
        if (hand.Count == 0)
        {
            throw new ArgumentException("Cannot pick from an empty hand.", nameof(hand));
        }

        random ??= Random.Shared.NextDouble;

        var bestIndex = 0;
        var bestScore = double.NegativeInfinity;

        for (var index = 0; index < hand.Count; index++)
        {
            var wins = 0;
            var losses = 0;
            foreach (var other in AllCards)
            {
                switch (RoundResolver.Resolve(hand[index], other))
                {
                    case RoundOutcome.PlayerAWins:
                        wins++;
                        break;
                    case RoundOutcome.PlayerBWins:
                        losses++;
                        break;
                }
            }

            var score = (capturing ? wins * 1.35 : wins) - losses + random() * 0.8;
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = index;
            }
        }

        return bestIndex;
    }
}
