namespace DayGrid.Api.Endpoints;

internal static class SearchPattern
{
    /// <summary>Escape character for <see cref="Contains"/>; pass it to EF.Functions.ILike —
    /// without it Npgsql emits <c>ESCAPE ''</c> and the escaping below would be literal text.</summary>
    public const string Escape = @"\";

    /// <summary>
    /// ILIKE pattern matching <paramref name="text"/> literally anywhere in the value. Without
    /// escaping, a search for "100%" or "a_b" treated % and _ as wildcards (and "_" alone matched
    /// every row).
    /// </summary>
    public static string Contains(string text) =>
        "%" + text.Replace(Escape, Escape + Escape).Replace("%", Escape + "%").Replace("_", Escape + "_") + "%";
}
