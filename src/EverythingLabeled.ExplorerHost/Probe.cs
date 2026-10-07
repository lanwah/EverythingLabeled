using EverythingLabeled.Client;

namespace EverythingLabeled.ExplorerHost;

public static class Probe
{
    public static async Task<ElResult<Unit>> RunAsync(IElApi api, CancellationToken ct)
    {
        var health = await api.HealthAsync(ct).ConfigureAwait(false);
        if (!health.IsSuccess)
        {
            var err = health.Error!.Value;
            return ElResult<Unit>.Fail(new ElError(err.Code, Copy.ServiceUnreachable, err.HttpStatus));
        }

        var meta = await api.MetaAsync(ct).ConfigureAwait(false);
        if (!meta.IsSuccess)
        {
            var err = meta.Error!.Value;
            return ElResult<Unit>.Fail(new ElError(err.Code, Copy.TokenInvalid, err.HttpStatus));
        }

        return ElResult<Unit>.Ok(Unit.Value);
    }
}
