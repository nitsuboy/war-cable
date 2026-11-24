namespace Duo.Domain;

/// <summary>Seat in a match. A is the host, B is the guest.</summary>
public enum Player
{
    A = 0,
    B = 1,
}

public static class PlayerExtensions
{
    public static Player Opponent(this Player player) =>
        player == Player.A ? Player.B : Player.A;
}
