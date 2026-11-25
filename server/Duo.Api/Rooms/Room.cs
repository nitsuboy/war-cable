using Duo.Domain;

namespace Duo.Api.Rooms;

public enum GamePhase
{
    Waiting,
    Choosing,
    Over,
}

public sealed class Seat
{
    public required Player Player { get; init; }

    public required string PlayerId { get; init; }

    public required string Token { get; init; }

    public required string Ip { get; init; }

    /// <summary>Set for registered players; null for guests.</summary>
    public string? AccountId { get; init; }

    public string? Username { get; init; }

    /// <summary>Server-driven seat. Bots never own a connection.</summary>
    public bool IsBot { get; init; }

    /// <summary>Snapshot used for display; refreshed when a ranked match ends.</summary>
    public int? Rating { get; set; }

    public string? ConnectionId { get; set; }

    public bool RematchVote { get; set; }

    public long? DisconnectedAtUnix { get; set; }

    /// <summary>Bots count as present so their rounds never pause.</summary>
    public bool Connected => IsBot || ConnectionId is not null;
}

/// <summary>
/// One match room. All mutation happens under <see cref="Gate"/>; reads for
/// broadcasts also happen under it so a client never sees a half-applied round.
/// </summary>
public sealed class Room
{
    private readonly int _turnSeconds;

    public Room(string code, Seat host, int turnSeconds)
    {
        Code = code;
        Host = host;
        _turnSeconds = turnSeconds;
        CreatedAtUnix = NowMs;
        Touch();
    }

    public string Code { get; }

    public int TurnSeconds => _turnSeconds;

    public Seat Host { get; }

    public Seat? Guest { get; private set; }

    public MatchEngine Engine { get; private set; } = MatchEngine.NewMatch();

    public GamePhase Phase { get; private set; } = GamePhase.Waiting;

    public int RoundNumber { get; private set; }

    public long RoundDeadlineUnixMs { get; private set; }

    /// <summary>When the bot seat should pick. Zero when the room has no bot.</summary>
    public long BotPickAtUnixMs { get; private set; }

    public long RemainingMsAtPause { get; private set; }

    public bool Paused { get; private set; }

    public long CreatedAtUnix { get; }

    public long LastActivityUnix { get; private set; }

    public string? EndReason { get; private set; }

    /// <summary>True when both seats are registered and the room came from the quick-match queue.</summary>
    public bool Ranked { get; private set; }

    /// <summary>Guards the Elo write so a match can never be rated twice.</summary>
    public bool ResultRecorded { get; private set; }

    public SemaphoreSlim Gate { get; } = new(1, 1);

    public IEnumerable<Seat> Seats
    {
        get
        {
            yield return Host;
            if (Guest is not null)
            {
                yield return Guest;
            }
        }
    }

    public bool IsFull => Guest is not null;

    public bool HasBot => Host.IsBot || Guest is { IsBot: true };

    public Seat? BotSeat => Host.IsBot ? Host : Guest is { IsBot: true } ? Guest : null;

    public static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public Seat? SeatOf(Player player) => player == Player.A ? Host : Guest;

    public Seat? SeatByPlayerId(string playerId) =>
        Seats.FirstOrDefault(s => string.Equals(s.PlayerId, playerId, StringComparison.Ordinal));

    public Seat? SeatByConnection(string connectionId) =>
        Seats.FirstOrDefault(s => string.Equals(s.ConnectionId, connectionId, StringComparison.Ordinal));

    public Seat? OpponentOf(Seat seat) => Seats.FirstOrDefault(s => !ReferenceEquals(s, seat));

    public void Touch() => LastActivityUnix = NowMs;

    public void MarkRanked() => Ranked = true;

    public bool TryMarkResultRecorded()
    {
        if (ResultRecorded)
        {
            return false;
        }

        ResultRecorded = true;
        return true;
    }

    public void AddGuest(Seat guest)
    {
        if (Guest is not null)
        {
            throw new InvalidOperationException("The room is already full.");
        }

        Guest = guest;
        StartMatch();
    }

    public void StartMatch()
    {
        Engine = MatchEngine.NewMatch();
        Host.RematchVote = false;
        if (Guest is not null)
        {
            Guest.RematchVote = false;
        }

        RoundNumber = 1;
        Phase = GamePhase.Choosing;
        Paused = false;
        EndReason = null;
        ResultRecorded = false;
        RoundDeadlineUnixMs = NowMs + _turnSeconds * 1000L;
        BotPickAtUnixMs = HasBot ? NowMs + BotDelayMs(_turnSeconds) : 0;
        Touch();
    }

    public void StartNextRound()
    {
        RoundNumber++;
        Paused = false;
        RoundDeadlineUnixMs = NowMs + _turnSeconds * 1000L;
        BotPickAtUnixMs = HasBot ? NowMs + BotDelayMs(_turnSeconds) : 0;
        Touch();
    }

    /// <summary>
    /// Mirrors the old browser bot: about half the turn budget, jittered, so
    /// the server bot feels like a person rather than an instant oracle.
    /// </summary>
    private static int BotDelayMs(int turnSeconds)
    {
        var budget = Math.Clamp(turnSeconds * 1000.0 * 0.45, 200, 1400);
        return (int)(budget * (0.4 + Random.Shared.NextDouble() * 0.8));
    }

    public void PauseRound()
    {
        if (Phase != GamePhase.Choosing || Paused)
        {
            return;
        }

        RemainingMsAtPause = Math.Max(0, RoundDeadlineUnixMs - NowMs);
        Paused = true;
        Touch();
    }

    public void ResumeRound()
    {
        if (!Paused)
        {
            return;
        }

        RoundDeadlineUnixMs = NowMs + RemainingMsAtPause;
        Paused = false;
        Touch();
    }

    public void End(string reason)
    {
        Phase = GamePhase.Over;
        EndReason = reason;
        Paused = false;
        Touch();
    }

    public bool IsTurnExpired() => Phase == GamePhase.Choosing && !Paused && NowMs >= RoundDeadlineUnixMs;

    public int DeadlineInMs() =>
        Phase != GamePhase.Choosing
            ? 0
            : (int)Math.Max(0, (Paused ? RemainingMsAtPause : RoundDeadlineUnixMs - NowMs));
}
