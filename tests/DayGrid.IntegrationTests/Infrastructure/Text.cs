namespace DayGrid.IntegrationTests.Infrastructure;

/// <summary>Boundary strings. Postgres varchar(n) counts characters (code points), not UTF-16 units.</summary>
public static class Text
{
    private const string Seed = "Ünïcødé✓漢字·"; // no spaces: varchar(n) silently drops excess *trailing spaces*

    /// <summary>Exactly <paramref name="length"/> BMP characters.</summary>
    public static string Of(int length) => string.Concat(Enumerable.Repeat(Seed, length / Seed.Length + 1))[..length];

    /// <summary><paramref name="count"/> astral characters — <paramref name="count"/> Postgres chars, 2×count UTF-16 units.</summary>
    public static string Emoji(int count) => string.Concat(Enumerable.Repeat("😀", count));
}
