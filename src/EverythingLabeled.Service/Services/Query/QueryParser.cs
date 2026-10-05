using EverythingLabeled.Service.Services.Abstractions;

namespace EverythingLabeled.Service.Services.Query;

public sealed class QueryParser
{
    public ServiceResult<ParsedQuery> Parse(string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Fail("Query is required.");

        var tokens = q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return Fail("Query is required.");

        if (tokens[0].Equals("or", StringComparison.OrdinalIgnoreCase)
            || tokens[^1].Equals("or", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("Invalid query structure.");
        }

        var clauses = new List<QueryClause>();
        var positives = new List<string>();
        var negatives = new List<string>();

        foreach (var token in tokens)
        {
            if (token.Equals("or", StringComparison.OrdinalIgnoreCase))
            {
                if (positives.Count == 0)
                    return Fail("Invalid query structure.");

                clauses.Add(new QueryClause
                {
                    PositiveAliasNormalized = positives.ToList(),
                    NegativeAliasNormalized = negatives.ToList(),
                });
                positives.Clear();
                negatives.Clear();
                continue;
            }

            if (token == "-")
                return Fail("Invalid query structure.");

            if (token.Length > 1 && token[0] == '-')
            {
                negatives.Add(token.AsSpan(1).ToString().ToLowerInvariant());
                continue;
            }

            positives.Add(token.ToLowerInvariant());
        }

        if (positives.Count == 0)
            return Fail("Invalid query structure.");

        clauses.Add(new QueryClause
        {
            PositiveAliasNormalized = positives.ToList(),
            NegativeAliasNormalized = negatives.ToList(),
        });

        return ServiceResult<ParsedQuery>.Ok(new ParsedQuery
        {
            Normalized = QueryNormalizer.Normalize(q),
            Clauses = clauses,
        });
    }

    private static ServiceResult<ParsedQuery> Fail(string message) =>
        ServiceResult<ParsedQuery>.Fail(
            ErrorCodes.BadRequest,
            message,
            new { reason = "param_invalid" });
}
