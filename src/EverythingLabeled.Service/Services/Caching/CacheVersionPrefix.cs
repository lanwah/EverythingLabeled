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
}
