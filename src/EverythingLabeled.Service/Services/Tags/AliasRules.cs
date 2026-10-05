namespace EverythingLabeled.Service.Services.Tags;

public static class AliasRules
{
    public static string Normalize(string alias) => alias.Trim().ToLowerInvariant();

    public static bool TryValidate(string alias, out string reason)
    {
        var trimmed = alias.Trim();
        if (trimmed.Length == 0)
        {
            reason = "alias_rejected_empty";
            return false;
        }

        if (trimmed[0] == '-')
        {
            reason = "alias_rejected_leading_dash";
            return false;
        }

        if (trimmed.Any(char.IsWhiteSpace))
        {
            reason = "alias_rejected_whitespace";
            return false;
        }

        if (trimmed.Any(IsAsciiControl))
        {
            reason = "alias_rejected_control";
            return false;
        }

        var normalized = trimmed.ToLowerInvariant();
        if (normalized is "or" or "and" or "not")
        {
            reason = "alias_rejected_reserved";
            return false;
        }

        reason = "";
        return true;
    }

    private static bool IsAsciiControl(char c) => c <= 0x1F || c == 0x7F;
}
