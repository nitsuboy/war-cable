using System.Globalization;

namespace Duo.Domain;

/// <summary>
/// A card face. Numeric values are the card's strength; <see cref="X"/> is the
/// special card that draws against everything, so it sits outside the numeric
/// range on purpose (-1) to avoid being caught by max/min comparisons.
/// </summary>
public enum CardValue
{
    X = -1,
    Zero = 0,
    One = 1,
    Two = 2,
    Three = 3,
    Four = 4,
    Five = 5,
    Six = 6,
}

public static class CardValueExtensions
{
    public static string Symbol(this CardValue value) =>
        value == CardValue.X
            ? "X"
            : ((int)value).ToString(CultureInfo.InvariantCulture);

    public static bool TryParseSymbol(string? symbol, out CardValue value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        var trimmed = symbol.Trim();
        if (trimmed.Equals("X", StringComparison.OrdinalIgnoreCase))
        {
            value = CardValue.X;
            return true;
        }

        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number < (int)CardValue.Zero
            || number > (int)CardValue.Six)
        {
            return false;
        }

        value = (CardValue)number;
        return true;
    }
}
