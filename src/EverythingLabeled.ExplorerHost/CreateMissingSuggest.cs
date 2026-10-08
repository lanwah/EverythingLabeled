namespace EverythingLabeled.ExplorerHost;

public static class CreateMissingSuggest
{
    public static bool ShouldCheck(string? normalizedAlias, IEnumerable<string> suggestAliases)
    {
        if (string.IsNullOrEmpty(normalizedAlias))
            return false;

        return !suggestAliases.Any(a =>
            string.Equals(a, normalizedAlias, StringComparison.OrdinalIgnoreCase));
    }
}
