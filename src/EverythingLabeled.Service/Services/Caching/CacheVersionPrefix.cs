using System.Globalization;

namespace EverythingLabeled.Service.Services.Caching;

public static class CacheVersionPrefix
{
    public static bool TryParse(string prefix, out int version)
    {
        version = 0;
        if (prefix.Length < 3 || prefix[0] != 'v' || prefix[^1] != ':')
            return false;
        return int.TryParse(
            prefix.AsSpan(1, prefix.Length - 2),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out version);
    }

    public static bool TryGetLogicalPrefix(string logicalKey, out string prefix)
    {
        prefix = "";
        var colon = logicalKey.IndexOf(':');
        if (colon <= 1) return false;
        var candidate = logicalKey[..(colon + 1)];
        if (!TryParse(candidate, out _)) return false;
        prefix = candidate;
        return true;
    }
}
