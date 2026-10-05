using System.Globalization;

namespace EverythingLabeled.Service.Services.Abstractions;

public readonly record struct Paging(int Offset, int Limit)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 1024;

    public static ServiceResult<Paging> TryParse(string? offsetRaw, string? limitRaw)
    {
        var offset = 0;
        var limit = DefaultLimit;

        if (offsetRaw is not null)
        {
            if (!int.TryParse(offsetRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset))
                return Invalid();
        }

        if (limitRaw is not null)
        {
            if (!int.TryParse(limitRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out limit))
                return Invalid();
        }

        if (offset < 0 || limit <= 0 || limit > MaxLimit)
            return Invalid();

        return ServiceResult<Paging>.Ok(new Paging(offset, limit));
    }

    private static ServiceResult<Paging> Invalid() =>
        ServiceResult<Paging>.Fail(
            ErrorCodes.BadRequest,
            "Invalid offset or limit.",
            new { reason = "param_invalid" });
}

public sealed class Paged<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Offset { get; init; }
    public required int Limit { get; init; }
    public required int Total { get; init; }
}
