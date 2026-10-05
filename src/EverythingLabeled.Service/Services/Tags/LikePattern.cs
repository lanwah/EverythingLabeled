namespace EverythingLabeled.Service.Services.Tags;

internal static class LikePattern
{
    public const string EscapeCharacter = "\\";

    public static string Prefix(string normalizedValue) => Escape(normalizedValue) + "%";

    public static string Escape(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
