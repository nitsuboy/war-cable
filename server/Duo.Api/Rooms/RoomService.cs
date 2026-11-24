using System.Collections.Concurrent;
using System.Security.Cryptography;
using Duo.Api.Contracts;
using Duo.Domain;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Duo.Api.Rooms;

public sealed record SeatCredentials(string PlayerId, string Token, string Ip, string ConnectionId);

public sealed record WaitingPlayer(
    string PlayerId,
    string Token,
    string Ip,
    string ConnectionId,
    long EnqueuedAtUnixMs);

/// <summary>
/// Owns every room and the quick-match queue. All room mutation is serialised by
/// <see cref="Room.Gate"/>; the membership dictionaries use a short global lock
/// that never awaits.
/// </summary>
public sealed class RoomService
{
    private static readonly char[] CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _roomByPlayer = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _roomByConnection = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _connections = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<WaitingPlayer> _queue = new();
    private readonly object _membership = new();

    private readonly IHubContext<GameHub> _hub;
    private readonly ConnectionGuard _guard;
    private readonly GuardOptions _options;
    private readonly ILogger<RoomService> _log;

    public RoomService(
        IHubContext<GameHub> hub,
        ConnectionGuard guard,
        IOptions<GuardOptions> options,
        ILogger<RoomService> log)
    {
        _hub = hub;
        _guard = guard;
        _options = options.Value;
        _log = log;
    }

    public int RoomCount => _rooms.Count;

    public void RegisterConnection(string connectionId) => _connections[connectionId] = 0;

    public void UnregisterConnection(string connectionId) => _connections.TryRemove(connectionId, out _);

    public async Task<JoinResult> CreateRoomAsync(SeatCredentials credentials)
    {
        if (_roomByPlayer.ContainsKey(credentials.PlayerId))
        {
            return new JoinResult(false, "already_in_room", null);
        }

        if (!_guard.TryOpenRoom(credentials.Ip))
        {
            return new JoinResult(false, "rate_limited", null);
        }

        Room room;
        lock (_membership)
        {
            if (_roomByPlayer.ContainsKey(credentials.PlayerId))
            {
                _guard.CloseRoom(credentials.Ip);
                return new JoinResult(false, "already_in_room", null);
            }

            var code = NextCode();
            var host = new Seat
            {
                Player = Player.A,
                PlayerId = credentials.PlayerId,
                Token = credentials.Token,
                Ip = credentials.Ip,
                ConnectionId = credentials.ConnectionId,
            };

            room = new Room(code, host, _options.TurnSeconds);
            _rooms[code] = room;
            _roomByPlayer[credentials.PlayerId] = code;
            _roomByConnection[credentials.ConnectionId] = code;
        }

        await BroadcastStateAsync(room);
        return new JoinResult(true, null, room.Code);
    }

    public async Task<JoinResult> JoinRoomAsync(string code, SeatCredentials credentials)
    {
        code = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length != 6 || !_rooms.TryGetValue(code, out var room))
        {
            return new JoinResult(false, "room_not_found", null);
        }

        await room.Gate.WaitAsync();
        try
        {
            var existing = room.SeatByPlayerId(credentials.PlayerId);
            if (existing is not null)
            {
                await AttachLockedAsync(room, existing, credentials);
                return new JoinResult(true, null, room.Code, Reattached: true);
            }

            if (_roomByPlayer.ContainsKey(credentials.PlayerId))
            {
                return new JoinResult(false, "already_in_room", null);
            }

            if (room.IsFull)
            {
                return new JoinResult(false, "room_full", null);
            }

            if (room.Phase != GamePhase.Waiting)
            {
                return new JoinResult(false, "match_started", null);
            }

            if (!room.Host.Connected)
            {
                RemoveRoom(room);
                return new JoinResult(false, "room_not_found", null);
            }

            var guest = new Seat
            {
                Player = Player.B,
                PlayerId = credentials.PlayerId,
                Token = credentials.Token,
                Ip = credentials.Ip,
                ConnectionId = credentials.ConnectionId,
            };

            room.AddGuest(guest);
            lock (_membership)
            {
                _roomByPlayer[credentials.PlayerId] = room.Code;
                _roomByConnection[credentials.ConnectionId] = room.Code;
            }

            await BroadcastStateAsync(room);
            return new JoinResult(true, null, room.Code);
        }
        finally
        {
            room.Gate.Release();
        }
    }

    public async Task<JoinResult> QuickMatchAsync(SeatCredentials credentials)
    {
        if (_roomByPlayer.ContainsKey(credentials.PlayerId))
        {
            return new JoinResult(false, "already_in_room", null);
        }

        var waiting = new WaitingPlayer(
            credentials.PlayerId,
            credentials.Token,
            credentials.Ip,
            credentials.ConnectionId,
            Room.NowMs);

        while (_queue.TryDequeue(out var other))
        {
            if (string.Equals(other.PlayerId, credentials.PlayerId, StringComparison.Ordinal))
            {
                continue;
            }

            if (Room.NowMs - other.EnqueuedAtUnixMs > 60_000
                || _roomByPlayer.ContainsKey(other.PlayerId)
                || !_connections.ContainsKey(other.ConnectionId))
            {
                continue;
            }

            if (!_guard.TryOpenRoom(other.Ip))
            {
                _queue.Enqueue(other);
                return new JoinResult(false, "rate_limited", null);
            }

            var room = TryCreateMatchedRoom(other, credentials);
            if (room is null)
            {
                _guard.CloseRoom(other.Ip);
                _queue.Enqueue(other);
                return new JoinResult(false, "already_in_room", null);
            }

            await BroadcastStateAsync(room);
            return new JoinResult(true, null, room.Code);
        }

        _queue.Enqueue(waiting);
        return new JoinResult(true, null, null, Queued: true);
    }

    public async Task ReattachAsync(string playerId, string token, string connectionId, string ip)
    {
        if (!_roomByPlayer.TryGetValue(playerId, out var code)
            || !_rooms.TryGetValue(code, out var room))
        {
            return;
        }

        await room.Gate.WaitAsync();
        try
        {
            var seat = room.SeatByPlayerId(playerId);
            if (seat is null)
            {
                return;
            }

            var credentials = new SeatCredentials(playerId, token, ip, connectionId);
            await AttachLockedAsync(room, seat, credentials);
        }
        finally
        {
            room.Gate.Release();
        }
    }

    public async Task SubmitPickAsync(string connectionId, int handIndex)
    {
        var room = FindRoomByConnection(connectionId);
        if (room is null)
        {
            throw new HubException("not_in_room");
        }

        await room.Gate.WaitAsync();
        try
        {
            if (room.Phase != GamePhase.Choosing)
            {
                throw new HubException("not_choosing");
            }

            var seat = room.SeatByConnection(connectionId)
                ?? throw new HubException("not_in_room");

            if (!room.Engine.TrySubmitPick(seat.Player, handIndex))
            {
                throw new HubException("invalid_pick");
            }

            room.Touch();

            if (room.Engine.CanResolve)
            {
                var result = room.Engine.TryResolveRound()
                    ?? throw new HubException("round_not_ready");
                await BroadcastRoundAsync(room, result);
                await AfterRoundAsync(room);
            }
            else
            {
                await BroadcastStateAsync(room);
            }
        }
        finally
        {
            room.Gate.Release();
        }
    }

    public async Task RematchAsync(string connectionId)
    {
        var room = FindRoomByConnection(connectionId);
        if (room is null)
        {
            throw new HubException("not_in_room");
        }

        await room.Gate.WaitAsync();
        try
        {
            if (room.Phase != GamePhase.Over)
            {
                throw new HubException("not_over");
            }

            var seat = room.SeatByConnection(connectionId)
                ?? throw new HubException("not_in_room");
            seat.RematchVote = true;
            room.Touch();

            var everyoneVoted = room.Seats.Count() == 2
                && room.Seats.All(s => s.Connected && s.RematchVote);

            if (everyoneVoted)
            {
                room.StartMatch();
            }

            await BroadcastStateAsync(room);
        }
        finally
        {
            room.Gate.Release();
        }
    }

    public void CancelQueue(string playerId)
    {
        var kept = new List<WaitingPlayer>();
        while (_queue.TryDequeue(out var waiting))
        {
            if (!string.Equals(waiting.PlayerId, playerId, StringComparison.Ordinal))
            {
                kept.Add(waiting);
            }
        }

        foreach (var waiting in kept)
        {
            _queue.Enqueue(waiting);
        }
    }

    public async Task LeaveAsync(string connectionId)
    {
        var room = FindRoomByConnection(connectionId);
        if (room is null)
        {
            return;
        }

        await room.Gate.WaitAsync();
        try
        {
            var seat = room.SeatByConnection(connectionId);
            if (seat is null)
            {
                return;
            }

            if (room.Phase == GamePhase.Waiting)
            {
                RemoveRoom(room);
                return;
            }

            if (room.Phase == GamePhase.Choosing)
            {
                room.End("abandoned");
                var winner = seat.Player.Opponent();
                await BroadcastMatchOverAsync(room, winner, "abandoned");
            }

            seat.ConnectionId = null;
            seat.DisconnectedAtUnix = Room.NowMs;
            _roomByConnection.TryRemove(connectionId, out _);
            _roomByPlayer.TryRemove(seat.PlayerId, out _);
            UpdatePauseState(room);
            await BroadcastStateAsync(room);
        }
        finally
        {
            room.Gate.Release();
        }
    }

    public async Task HandleDisconnectAsync(string connectionId)
    {
        var room = FindRoomByConnection(connectionId);
        if (room is null)
        {
            _roomByConnection.TryRemove(connectionId, out _);
            return;
        }

        await room.Gate.WaitAsync();
        try
        {
            var seat = room.SeatByConnection(connectionId);
            _roomByConnection.TryRemove(connectionId, out _);
            if (seat is null)
            {
                return;
            }

            seat.ConnectionId = null;
            seat.DisconnectedAtUnix = Room.NowMs;
            UpdatePauseState(room);
            await BroadcastStateAsync(room);
        }
        finally
        {
            room.Gate.Release();
        }
    }

    /// <summary>Called by the sweeper roughly four times per second.</summary>
    public async Task TickAsync()
    {
        foreach (var room in _rooms.Values)
        {
            if (!await room.Gate.WaitAsync(0))
            {
                continue;
            }

            try
            {
                await TickRoomAsync(room);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Tick failed for room {RoomCode}", room.Code);
            }
            finally
            {
                room.Gate.Release();
            }
        }
    }

    private async Task TickRoomAsync(Room room)
    {
        var now = Room.NowMs;
        var graceMs = _options.ReconnectGraceSeconds * 1000L;

        if (room.Phase == GamePhase.Waiting)
        {
            var idle = now - room.CreatedAtUnix > _options.WaitingRoomIdleMinutes * 60_000L;
            var hostGone = room.Host.DisconnectedAtUnix is { } at && now - at > graceMs;
            if (idle || hostGone)
            {
                RemoveRoom(room);
            }

            return;
        }

        if (room.Phase == GamePhase.Choosing)
        {
            foreach (var seat in room.Seats)
            {
                if (seat.DisconnectedAtUnix is { } at && now - at > graceMs)
                {
                    room.End("wo");
                    await BroadcastMatchOverAsync(room, seat.Player.Opponent(), "wo");
                    await BroadcastStateAsync(room);
                    return;
                }
            }

            if (!room.IsTurnExpired())
            {
                return;
            }

            var aPicked = room.Engine.State.HasPicked(Player.A);
            var bPicked = room.Engine.State.HasPicked(Player.B);

            RoundResult result;
            if (aPicked && bPicked)
            {
                result = room.Engine.TryResolveRound()!;
            }
            else if (aPicked)
            {
                result = room.Engine.ApplyTimeout(Player.B);
            }
            else if (bPicked)
            {
                result = room.Engine.ApplyTimeout(Player.A);
            }
            else
            {
                result = room.Engine.ApplyDoubleTimeout();
            }

            await BroadcastRoundAsync(room, result);
            await AfterRoundAsync(room);
            return;
        }

        if (room.Phase == GamePhase.Over
            && now - room.LastActivityUnix > _options.FinishedRoomIdleSeconds * 1000L)
        {
            RemoveRoom(room);
        }
    }

    private async Task AfterRoundAsync(Room room)
    {
        if (room.Engine.State.IsOver)
        {
            var winner = room.Engine.State.Winner!.Value;
            room.End("captures");
            await BroadcastMatchOverAsync(room, winner, "captures");
            await BroadcastStateAsync(room);
            return;
        }

        room.StartNextRound();
        await BroadcastStateAsync(room);
    }

    private async Task AttachLockedAsync(Room room, Seat seat, SeatCredentials credentials)
    {
        if (seat.ConnectionId is { } previous && previous != credentials.ConnectionId)
        {
            _roomByConnection.TryRemove(previous, out _);
        }

        seat.ConnectionId = credentials.ConnectionId;
        seat.DisconnectedAtUnix = null;

        lock (_membership)
        {
            _roomByConnection[credentials.ConnectionId] = room.Code;
            _roomByPlayer[seat.PlayerId] = room.Code;
        }

        UpdatePauseState(room);
        await BroadcastStateAsync(room);
    }

    private Room? TryCreateMatchedRoom(WaitingPlayer host, SeatCredentials guestCredentials)
    {
        lock (_membership)
        {
            if (_roomByPlayer.ContainsKey(host.PlayerId)
                || _roomByPlayer.ContainsKey(guestCredentials.PlayerId))
            {
                return null;
            }

            var code = NextCode();
            var hostSeat = new Seat
            {
                Player = Player.A,
                PlayerId = host.PlayerId,
                Token = host.Token,
                Ip = host.Ip,
                ConnectionId = host.ConnectionId,
            };

            var guestSeat = new Seat
            {
                Player = Player.B,
                PlayerId = guestCredentials.PlayerId,
                Token = guestCredentials.Token,
                Ip = guestCredentials.Ip,
                ConnectionId = guestCredentials.ConnectionId,
            };

            var room = new Room(code, hostSeat, _options.TurnSeconds);
            room.AddGuest(guestSeat);

            _rooms[code] = room;
            _roomByPlayer[host.PlayerId] = code;
            _roomByPlayer[guestCredentials.PlayerId] = code;
            _roomByConnection[host.ConnectionId] = code;
            _roomByConnection[guestCredentials.ConnectionId] = code;

            return room;
        }
    }

    private static void UpdatePauseState(Room room)
    {
        if (room.Phase != GamePhase.Choosing)
        {
            return;
        }

        if (room.Seats.All(s => s.Connected))
        {
            room.ResumeRound();
        }
        else
        {
            room.PauseRound();
        }
    }

    private async Task BroadcastStateAsync(Room room)
    {
        foreach (var seat in room.Seats)
        {
            if (seat.ConnectionId is not { } connectionId)
            {
                continue;
            }

            await _hub.Clients.Client(connectionId)
                .SendAsync("RoomState", DtoMapper.State(room, seat.Player));
        }
    }

    private async Task BroadcastRoundAsync(Room room, RoundResult result)
    {
        foreach (var seat in room.Seats)
        {
            if (seat.ConnectionId is not { } connectionId)
            {
                continue;
            }

            await _hub.Clients.Client(connectionId)
                .SendAsync("RoundResult", DtoMapper.Round(result, seat.Player));
        }
    }

    private async Task BroadcastMatchOverAsync(Room room, Player winner, string reason)
    {
        foreach (var seat in room.Seats)
        {
            if (seat.ConnectionId is not { } connectionId)
            {
                continue;
            }

            var view = seat.Player == winner ? "you" : "opponent";
            await _hub.Clients.Client(connectionId)
                .SendAsync("MatchOver", new MatchOverDto(view, reason));
        }
    }

    private Room? FindRoomByConnection(string connectionId) =>
        _roomByConnection.TryGetValue(connectionId, out var code)
        && _rooms.TryGetValue(code, out var room)
            ? room
            : null;

    private void RemoveRoom(Room room)
    {
        if (!_rooms.TryRemove(room.Code, out _))
        {
            return;
        }

        foreach (var seat in room.Seats)
        {
            _roomByPlayer.TryRemove(seat.PlayerId, out _);
            if (seat.ConnectionId is { } connectionId)
            {
                _roomByConnection.TryRemove(connectionId, out _);
            }
        }

        _guard.CloseRoom(room.Host.Ip);
    }

    private string NextCode()
    {
        Span<char> buffer = stackalloc char[6];
        while (true)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
            }

            var code = new string(buffer);
            if (!_rooms.ContainsKey(code))
            {
                return code;
            }
        }
    }
}
