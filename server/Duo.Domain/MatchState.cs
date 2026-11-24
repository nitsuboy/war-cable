namespace Duo.Domain;

/// <summary>
/// Mutable match state. Only <see cref="MatchEngine"/> mutates it; the API layer
/// reads it to build per-player views. Not thread-safe: the server serialises
/// access per room.
/// </summary>
public sealed class MatchState
{
    public const int HandSize = 9;

    /// <summary>Track is a tug of war: +1 is seat A's side, -1 is seat B's side.</summary>
    public const int TrackASide = 1;
    public const int TrackCenter = 0;
    public const int TrackBSide = -1;

    private readonly List<CardValue> _handA;
    private readonly List<CardValue> _handB;
    private readonly List<CardValue> _discardA = new(HandSize);
    private readonly List<CardValue> _discardB = new(HandSize);

    public MatchState(
        int targetCaptures = 4,
        IEnumerable<CardValue>? handA = null,
        IEnumerable<CardValue>? handB = null)
    {
        if (targetCaptures < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(targetCaptures));
        }

        var seatA = (handA ?? DefaultHand()).ToArray();
        var seatB = (handB ?? handA ?? DefaultHand()).ToArray();
        if (seatA.Length != HandSize || seatB.Length != HandSize)
        {
            throw new ArgumentException($"Each starting hand must contain exactly {HandSize} cards.");
        }

        TargetCaptures = targetCaptures;
        _handA = new List<CardValue>(seatA);
        _handB = new List<CardValue>(seatB);
    }

    public static IReadOnlyList<CardValue> DefaultHand() => new[]
    {
        CardValue.X,
        CardValue.Zero,
        CardValue.One,
        CardValue.Two,
        CardValue.Three,
        CardValue.Four,
        CardValue.Five,
        CardValue.Six,
        CardValue.Six,
    };

    public int TargetCaptures { get; }

    public int Track { get; internal set; } = TrackCenter;

    public int CapturesA { get; internal set; }

    public int CapturesB { get; internal set; }

    public CardValue? PendingA { get; internal set; }

    public CardValue? PendingB { get; internal set; }

    public Player? Winner { get; internal set; }

    public bool IsOver => Winner is not null;

    public IReadOnlyList<CardValue> Hand(Player player) => player == Player.A ? _handA : _handB;

    public IReadOnlyList<CardValue> Discard(Player player) => player == Player.A ? _discardA : _discardB;

    public int HandCount(Player player) => Hand(player).Count;

    public int Captures(Player player) => player == Player.A ? CapturesA : CapturesB;

    public bool HasPicked(Player player) => Pending(player) is not null;

    internal CardValue? Pending(Player player) => player == Player.A ? PendingA : PendingB;

    internal void SetPending(Player player, CardValue? card)
    {
        if (player == Player.A)
        {
            PendingA = card;
        }
        else
        {
            PendingB = card;
        }
    }

    internal void ClearPending()
    {
        PendingA = null;
        PendingB = null;
    }

    internal List<CardValue> HandList(Player player) => player == Player.A ? _handA : _handB;

    internal List<CardValue> DiscardList(Player player) => player == Player.A ? _discardA : _discardB;

    internal void AddCaptures(Player player, int amount)
    {
        if (player == Player.A)
        {
            CapturesA += amount;
        }
        else
        {
            CapturesB += amount;
        }
    }
}
