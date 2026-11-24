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

    public string? ConnectionId { get; set; }

    public bool RematchVote { get; set; }

    public long? DisconnectedAtUnix { get; set; }

    public bool Connected => ConnectionId is not null;
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

    public long RemainingMsAtPause { get; private set; }

    public bool Paused { get; private set; }

    public long CreatedAtUnix { get; }

    public long LastActivityUnix { get; private set; }

    public string? EndReason { get; private set; }

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

    public static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public Seat? SeatOf(Player player) => player == Player.A ? Host : Guest;

    public Seat? SeatByPlayerId(string playerId) =>
        Seats.FirstOrDefault(s => string.Equals(s.PlayerId, playerId, StringComparison.Ordinal));

    public Seat? SeatByConnection(string connectionId) =>
        Seats.FirstOrDefault(s => string.Equals(s.ConnectionId, connectionId, StringComparison.Ordinal));

    public Seat? OpponentOf(Seat seat) => Seats.FirstOrDefault(s => !ReferenceEquals(s, seat));

    public void Touch() => LastActivityUnix = NowMs;

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
        RoundDeadlineUnixMs = NowMs + _turnSeconds * 1000L;
        Touch();
    }

    public void StartNextRound()
    {
        RoundNumber++;
        Paused = false;
        RoundDeadlineUnixMs = NowMs + _turnSeconds * 1000L;
        Touch();
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
