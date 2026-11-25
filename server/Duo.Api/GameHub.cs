using Duo.Api.Accounts;
using Duo.Api.Contracts;
using Duo.Api.Rooms;
using Microsoft.AspNetCore.SignalR;

namespace Duo.Api;

/// <summary>
/// SignalR surface. Every call is rate limited per IP and, except for account
/// auth, requires an identity established through <see cref="Hello"/>.
/// </summary>
public sealed class GameHub : Hub
{
    private const string PlayerIdKey = "playerId";
    private const string TokenKey = "token";
    private const string AccountIdKey = "accountId";

    private readonly RoomService _rooms;
    private readonly IdentityStore _identity;
    private readonly AccountService _accounts;
    private readonly AuthThrottle _throttle;
    private readonly ConnectionGuard _guard;
    private readonly ILogger<GameHub> _log;

    public GameHub(
        RoomService rooms,
        IdentityStore identity,
        AccountService accounts,
        AuthThrottle throttle,
        ConnectionGuard guard,
        ILogger<GameHub> log)
    {
        _rooms = rooms;
        _identity = identity;
        _accounts = accounts;
        _throttle = throttle;
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
    /// Account sessions are resolved first; otherwise a guest identity is used.
    /// </summary>
    public async Task<HelloResult> Hello(string? playerId, string? sessionToken)
    {
        AllowMessage();

        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            var account = await _accounts.ResolveSessionAsync(playerId, sessionToken);
            if (account is not null)
            {
                await AttachAccountAsync(account, sessionToken);
                return new HelloResult(account.PlayerId, sessionToken, account.ToView());
            }
        }

        if (AccountService.IsAccountPlayerId(playerId))
        {
            _guard.Penalize(Ip);
            throw new HubException("invalid_identity");
        }

        var identity = _identity.ResolveGuest(playerId, sessionToken);
        if (identity is null)
        {
            _guard.Penalize(Ip);
            throw new HubException("invalid_identity");
        }

        Context.Items[PlayerIdKey] = identity.PlayerId;
        Context.Items[TokenKey] = identity.Token;
        Context.Items.Remove(AccountIdKey);

        await _rooms.ReattachAsync(identity.PlayerId, identity.Token, Context.ConnectionId, Ip);

        return new HelloResult(identity.PlayerId, identity.Token);
    }

    public async Task<AuthResult> Register(string? username, string? password)
    {
        AllowMessage();

        if (!_throttle.TryRegister(Ip) || !_throttle.IsAllowed(Ip, username))
        {
            throw new HubException("auth_rate_limited");
        }

        var outcome = await _accounts.RegisterAsync(username, password);
        if (!outcome.Ok)
        {
            return new AuthResult(false, outcome.Error, null, null, null);
        }

        await AttachAccountAsync(outcome.Account!, outcome.Token!);
        return new AuthResult(
            true,
            null,
            outcome.Account!.PlayerId,
            outcome.Token,
            outcome.Account.ToView());
    }

    public async Task<AuthResult> Login(string? username, string? password)
    {
        AllowMessage();

        if (!_throttle.IsAllowed(Ip, username))
        {
            throw new HubException("auth_rate_limited");
        }

        var outcome = await _accounts.LoginAsync(username, password);
        if (!outcome.Ok)
        {
            _throttle.RecordFailure(Ip, username);
            return new AuthResult(false, outcome.Error, null, null, null);
        }

        _throttle.RecordSuccess(username);
        await AttachAccountAsync(outcome.Account!, outcome.Token!);
        return new AuthResult(
            true,
            null,
            outcome.Account!.PlayerId,
            outcome.Token,
            outcome.Account.ToView());
    }

    public async Task Logout()
    {
        AllowMessage();

        var accountId = Context.Items[AccountIdKey] as string;
        var token = Context.Items[TokenKey] as string;
        var playerId = Context.Items[PlayerIdKey] as string;

        if (accountId is not null)
        {
            await _accounts.RevokeSessionAsync(token);
        }

        if (playerId is not null)
        {
            _rooms.CancelQueue(playerId);
            await _rooms.LeaveAsync(Context.ConnectionId);
        }

        Context.Items.Remove(PlayerIdKey);
        Context.Items.Remove(TokenKey);
        Context.Items.Remove(AccountIdKey);
    }

    public async Task<JoinResult> CreateRoom()
    {
        AllowMessage();
        return await _rooms.CreateRoomAsync(await CredentialsAsync());
    }

    public async Task<JoinResult> JoinRoom(string code)
    {
        AllowMessage();
        return await _rooms.JoinRoomAsync(code, await CredentialsAsync());
    }

    public async Task<JoinResult> QuickMatch()
    {
        AllowMessage();
        return await _rooms.QuickMatchAsync(await CredentialsAsync());
    }

    public async Task<JoinResult> PlayBot()
    {
        AllowMessage();
        return await _rooms.PlayBotAsync(await CredentialsAsync());
    }

    public async Task<AccountViewDto?> Profile()
    {
        AllowMessage();
        var accountId = Context.Items[AccountIdKey] as string;
        if (accountId is null)
        {
            return null;
        }

        var profile = await _accounts.GetProfileAsync(accountId);
        return profile?.ToView();
    }

    public async Task<LeaderboardRowDto[]> Leaderboard(int limit = 10)
    {
        AllowMessage();
        return (await _accounts.LeaderboardAsync(limit)).ToArray();
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

    private async Task AttachAccountAsync(AccountProfile account, string token)
    {
        var previousPlayerId = Context.Items[PlayerIdKey] as string;
        if (previousPlayerId is not null
            && !string.Equals(previousPlayerId, account.PlayerId, StringComparison.Ordinal))
        {
            // A guest (or another account) was bound to this connection: leave
            // any room and queue under that old identity before switching.
            _rooms.CancelQueue(previousPlayerId);
            await _rooms.LeaveAsync(Context.ConnectionId);
        }

        Context.Items[PlayerIdKey] = account.PlayerId;
        Context.Items[TokenKey] = token;
        Context.Items[AccountIdKey] = account.AccountId;

        await _rooms.ReattachAsync(account.PlayerId, token, Context.ConnectionId, Ip);
    }

    private void AllowMessage()
    {
        if (!_guard.TryConsumeMessage(Ip))
        {
            throw new HubException("rate_limited");
        }
    }

    private async Task<SeatCredentials> CredentialsAsync()
    {
        var playerId = Context.Items[PlayerIdKey] as string;
        var token = Context.Items[TokenKey] as string;
        if (playerId is null || token is null)
        {
            throw new HubException("hello_required");
        }

        var accountId = Context.Items[AccountIdKey] as string;
        AccountProfile? profile = null;
        if (accountId is not null)
        {
            profile = await _accounts.GetProfileAsync(accountId);
        }

        return new SeatCredentials(
            playerId,
            token,
            Ip,
            Context.ConnectionId,
            profile?.AccountId,
            profile?.Username,
            profile?.Rating);
    }
}
