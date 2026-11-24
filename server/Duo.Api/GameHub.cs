using Duo.Api.Contracts;
using Duo.Api.Rooms;
using Microsoft.AspNetCore.SignalR;

namespace Duo.Api;

/// <summary>
/// SignalR surface. Every call is rate limited per IP and requires a valid
/// identity established through <see cref="Hello"/>.
/// </summary>
public sealed class GameHub : Hub
{
    private const string PlayerIdKey = "playerId";
    private const string TokenKey = "token";

    private readonly RoomService _rooms;
    private readonly IdentityStore _identity;
    private readonly ConnectionGuard _guard;
    private readonly ILogger<GameHub> _log;

    public GameHub(
        RoomService rooms,
        IdentityStore identity,
        ConnectionGuard guard,
        ILogger<GameHub> log)
    {
        _rooms = rooms;
        _identity = identity;
        _guard = guard;
        _log = log;
    }

    private string Ip =>
        Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public override async Task OnConnectedAsync()
    {
        if (_guard.IsBlocked(Ip) || !_guard.TryOpenConnection(Ip))
        {
            _log.LogWarning("Rejected connection from {Ip}", Ip);
            Context.Abort();
            return;
        }

        _rooms.RegisterConnection(Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _rooms.UnregisterConnection(Context.ConnectionId);
        await _rooms.HandleDisconnectAsync(Context.ConnectionId);
        _guard.CloseConnection(Ip);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Establishes (or restores) the player's identity and re-attaches to an
    /// active room if one exists for this player. Safe to call on reconnect.
    /// </summary>
    public async Task<HelloResult> Hello(string? playerId, string? sessionToken)
    {
        AllowMessage();

        var identity = _identity.Resolve(playerId, sessionToken);
        if (identity is null)
        {
            _guard.Penalize(Ip);
            throw new HubException("invalid_identity");
        }

        Context.Items[PlayerIdKey] = identity.PlayerId;
        Context.Items[TokenKey] = identity.Token;

        await _rooms.ReattachAsync(identity.PlayerId, identity.Token, Context.ConnectionId, Ip);

        return new HelloResult(identity.PlayerId, identity.Token);
    }

    public async Task<JoinResult> CreateRoom()
    {
        AllowMessage();
        return await _rooms.CreateRoomAsync(Credentials());
    }

    public async Task<JoinResult> JoinRoom(string code)
    {
        AllowMessage();
        return await _rooms.JoinRoomAsync(code, Credentials());
    }

    public async Task<JoinResult> QuickMatch()
    {
        AllowMessage();
        return await _rooms.QuickMatchAsync(Credentials());
    }

    public async Task SubmitPick(int handIndex)
    {
        AllowMessage();
        await _rooms.SubmitPickAsync(Context.ConnectionId, handIndex);
    }

    public async Task Rematch()
    {
        AllowMessage();
        await _rooms.RematchAsync(Context.ConnectionId);
    }

    public async Task Leave()
    {
        AllowMessage();
        await _rooms.LeaveAsync(Context.ConnectionId);
    }

    public Task CancelQueue()
    {
        AllowMessage();
        var playerId = Context.Items[PlayerIdKey] as string;
        if (playerId is not null)
        {
            _rooms.CancelQueue(playerId);
        }

        return Task.CompletedTask;
    }

    private void AllowMessage()
    {
        if (!_guard.TryConsumeMessage(Ip))
        {
            throw new HubException("rate_limited");
        }
    }

    private SeatCredentials Credentials()
    {
        var playerId = Context.Items[PlayerIdKey] as string;
        var token = Context.Items[TokenKey] as string;
        if (playerId is null || token is null)
        {
            throw new HubException("hello_required");
        }

        return new SeatCredentials(playerId, token, Ip, Context.ConnectionId);
    }
}
