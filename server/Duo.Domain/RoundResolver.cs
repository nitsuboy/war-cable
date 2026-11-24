namespace Duo.Domain;

/// <summary>
/// Pure, single-round card comparison.
///
/// Rules (from the project spec):
///   * X on either side, or equal cards -> draw.
///   * 0 vs 6 -> the 0 wins.
///   * 4 on either side -> the lower card wins (4 beats 5 and 6, loses to 0..3).
///   * otherwise -> the higher card wins.
/// </summary>
public static class RoundResolver
{
    public static RoundOutcome Resolve(CardValue a, CardValue b)
    {
        if (a == CardValue.X || b == CardValue.X || a == b)
        {
            return RoundOutcome.Draw;
        }

        if (a == CardValue.Zero || b == CardValue.Zero)
        {
            if (a == CardValue.Six || b == CardValue.Six)
            {
                return a == CardValue.Zero ? RoundOutcome.PlayerAWins : RoundOutcome.PlayerBWins;
            }
        }

        if (a == CardValue.Four || b == CardValue.Four)
        {
            return (int)a < (int)b ? RoundOutcome.PlayerAWins : RoundOutcome.PlayerBWins;
        }

        return (int)a > (int)b ? RoundOutcome.PlayerAWins : RoundOutcome.PlayerBWins;
    }
}
