using System.Text.Json;
using Duo.Domain;

namespace Duo.Domain.Tests;

public class RoundResolverTests
{
    private sealed record Fixture(List<string> Cards, List<Row> Rows);

    private sealed record Row(string A, string B, string Result);

    private static Fixture LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rules-matrix.json");
        Assert.True(File.Exists(path), $"Fixture not found at {path}");
        var json = File.ReadAllText(path);
        var fixture = JsonSerializer.Deserialize<Fixture>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(fixture);
        return fixture!;
    }

    [Fact]
    public void Fixture_covers_every_card_pair_exactly_once()
    {
        var fixture = LoadFixture();
        Assert.Equal(8, fixture.Cards.Count);
        Assert.Equal(64, fixture.Rows.Count);
        Assert.Equal(64, fixture.Rows.Select(r => (r.A, r.B)).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public void Matches_shared_rules_matrix(string a, string b, string expected)
    {
        Assert.True(CardValueExtensions.TryParseSymbol(a, out var cardA));
        Assert.True(CardValueExtensions.TryParseSymbol(b, out var cardB));

        var outcome = RoundResolver.Resolve(cardA, cardB);

        Assert.Equal(ParseExpected(expected), outcome);
    }

    public static TheoryData<string, string, string> MatrixCases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var row in LoadFixture().Rows)
        {
            data.Add(row.A, row.B, row.Result);
        }

        return data;
    }

    [Theory]
    // X draws against everything, including itself.
    [InlineData("X", "6", RoundOutcome.Draw)]
    [InlineData("6", "X", RoundOutcome.Draw)]
    [InlineData("X", "X", RoundOutcome.Draw)]
    // 0 beats 6 and 4, loses to 1, 2, 3 and 5.
    [InlineData("0", "6", RoundOutcome.PlayerAWins)]
    [InlineData("6", "0", RoundOutcome.PlayerBWins)]
    [InlineData("0", "4", RoundOutcome.PlayerAWins)]
    [InlineData("4", "0", RoundOutcome.PlayerBWins)]
    [InlineData("0", "5", RoundOutcome.PlayerBWins)]
    [InlineData("5", "0", RoundOutcome.PlayerAWins)]
    // 4 inverts the comparison: it beats 5 and 6, loses to 1..3.
    [InlineData("4", "5", RoundOutcome.PlayerAWins)]
    [InlineData("5", "4", RoundOutcome.PlayerBWins)]
    [InlineData("4", "6", RoundOutcome.PlayerAWins)]
    [InlineData("6", "4", RoundOutcome.PlayerBWins)]
    [InlineData("4", "3", RoundOutcome.PlayerBWins)]
    [InlineData("3", "4", RoundOutcome.PlayerAWins)]
    // Default: higher card wins.
    [InlineData("3", "2", RoundOutcome.PlayerAWins)]
    [InlineData("2", "3", RoundOutcome.PlayerBWins)]
    [InlineData("6", "6", RoundOutcome.Draw)]
    public void Resolves_special_cases(string a, string b, RoundOutcome expected)
    {
        Assert.True(CardValueExtensions.TryParseSymbol(a, out var cardA));
        Assert.True(CardValueExtensions.TryParseSymbol(b, out var cardB));

        Assert.Equal(expected, RoundResolver.Resolve(cardA, cardB));
    }

    [Theory]
    [InlineData("x", CardValue.X)]
    [InlineData(" X ", CardValue.X)]
    [InlineData("0", CardValue.Zero)]
    [InlineData("6", CardValue.Six)]
    public void Parses_symbols(string symbol, CardValue expected)
    {
        Assert.True(CardValueExtensions.TryParseSymbol(symbol, out var parsed));
        Assert.Equal(expected, parsed);
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("7")]
    [InlineData("-1")]
    [InlineData("Y")]
    public void Rejects_invalid_symbols(string? symbol)
    {
        Assert.False(CardValueExtensions.TryParseSymbol(symbol, out _));
    }

    private static RoundOutcome ParseExpected(string value) => value switch
    {
        "a" => RoundOutcome.PlayerAWins,
        "b" => RoundOutcome.PlayerBWins,
        "draw" => RoundOutcome.Draw,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown fixture result."),
    };
}
