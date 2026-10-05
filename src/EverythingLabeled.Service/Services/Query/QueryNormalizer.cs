using System.Text;

namespace EverythingLabeled.Service.Services.Query;

public static class QueryNormalizer
{
    public static string Normalize(string q)
    {
        var tokens = q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return string.Empty;

        var sb = new StringBuilder();
        var atClauseStart = true;

        foreach (var token in tokens)
        {
            if (token.Equals("or", StringComparison.OrdinalIgnoreCase))
            {
                if (sb.Length > 0)
                    sb.Append(" OR ");
                atClauseStart = true;
                continue;
            }

            if (!atClauseStart)
                sb.Append(' ');

            if (token.Length > 1 && token[0] == '-')
            {
                sb.Append('-');
                sb.Append(token.AsSpan(1).ToString().ToLowerInvariant());
            }
            else
            {
                sb.Append(token.ToLowerInvariant());
            }

            atClauseStart = false;
        }

        return sb.ToString();
    }
}
