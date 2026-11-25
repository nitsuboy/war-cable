using Duo.Api.Rooms;
using Duo.Domain;

namespace Duo.Api.Tests;

public class BotRoomTests
{
    private static Seat Human(Player player) => new()
    {
        Player = player,
        PlayerId = "human-1",
        Token = "token",
        Ip = "127.0.0.1",
        ConnectionId = "conn-1",
    };

    private static Seat Bot(Player player) => new()
    {
        Player = player,
        PlayerId = "bot_1",
        Token = "bot-token",
        Ip = "127.0.0.1",
        ConnectionId = null,
        IsBot = true,
        Username = "Bot",
    };

    [Fact]
    public void Bot_seat_counts_as_present_without_a_connection()
    {
        var room = new Room("ABC123", Human(Player.A), 20);
        room.AddGuest(Bot(Player.B));

        Assert.True(room.HasBot);
        Assert.Same(room.Guest, room.BotSeat);
        Assert.True(room.Guest!.Connected);
        Assert.Equal(GamePhase.Choosing, room.Phase);
    }

    [Fact]
    public void Bot_rooms_schedule_a_pick_for_the_new_round()
    {
        var room = new Room("ABC123", Human(Player.A), 20);
        room.AddGuest(Bot(Player.B));

        var firstPickAt = room.BotPickAtUnixMs;
        Assert.True(firstPickAt > 0);
        Assert.True(firstPickAt <= Room.NowMs + 20_000);

        room.StartNextRound();
        Assert.True(room.BotPickAtUnixMs > 0);
    }

    [Fact]
    public void Human_only_rooms_have_no_bot_pick()
    {
        var room = new Room("ABC123", Human(Player.A), 20);

        Assert.False(room.HasBot);
        Assert.Null(room.BotSeat);
        Assert.Equal(0, room.BotPickAtUnixMs);
    }
}
