namespace Duo.Domain;

public readonly record struct EloResult(int DeltaA, int DeltaB, int RatingA, int RatingB);

/// <summary>
/// Classic Elo for one match. Keeping this pure makes the ranking rules
/// testable without the API or the database.
/// </summary>
public static class Elo
{
    public const int DefaultK = 32;
    public const int DefaultRating = 1000;
    public const int FloorRating = 100;

    /// <summary>Probability that <paramref name="rating"/> beats the opponent.</summary>
    public static double ExpectedScore(int rating, int opponentRating) =>
        1.0 / (1.0 + Math.Pow(10.0, (opponentRating - rating) / 400.0));

    /// <summary>
    /// Applies a result. <paramref name="scoreA"/> is 1 for an A win, 0 for a B
    /// win and 0.5 for a draw. Deltas are taken from the actual clamping so a
    /// player parked at the floor never drops below it.
    /// </summary>
    public static EloResult Apply(int ratingA, int ratingB, double scoreA, int k = DefaultK)
    {
        var expectedA = ExpectedScore(ratingA, ratingB);
        var rawDelta = (int)Math.Round(k * (scoreA - expectedA), MidpointRounding.AwayFromZero);

        var newA = Math.Max(FloorRating, ratingA + rawDelta);
        var newB = Math.Max(FloorRating, ratingB - rawDelta);

        return new EloResult(newA - ratingA, newB - ratingB, newA, newB);
    }
}
