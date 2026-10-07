using EverythingLabeled.Client;

namespace EverythingLabeled.ExplorerHost;

public sealed record BatchItemResult(
    string Uri,
    bool Ok,
    string? ErrorCode,
    string? ErrorMessage,
    bool CancelledBeforeSend);

public sealed record BatchSummary(
    int Success,
    int Failed,
    int CancelledNotSent,
    IReadOnlyList<BatchItemResult> Items);

public static class BatchTagOps
{
    public static Task<BatchSummary> TagAsync(
        IElApi api,
        IReadOnlyList<(string Uri, string Kind)> targets,
        string alias,
        bool createMissing,
        int maxConcurrency,
        CancellationToken ct) =>
        RunAsync(
            targets.Select(t => t.Uri).ToList(),
            maxConcurrency,
            ct,
            async (index, sendCt) =>
            {
                var (uri, kind) = targets[index];
                var result = await api.TagAsync(uri, kind, [alias], createMissing, sendCt);
                return (ToItem(uri, result), IsUnauthorized(result));
            });

    public static Task<BatchSummary> UntagAsync(
        IElApi api,
        IReadOnlyList<string> uris,
        string alias,
        int maxConcurrency,
        CancellationToken ct) =>
        RunAsync(
            uris,
            maxConcurrency,
            ct,
            async (index, sendCt) =>
            {
                var uri = uris[index];
                var result = await api.UntagAsync(uri, [alias], sendCt);
                return (ToItem(uri, result), IsUnauthorized(result));
            });

    private static async Task<BatchSummary> RunAsync(
        IReadOnlyList<string> uris,
        int maxConcurrency,
        CancellationToken ct,
        Func<int, CancellationToken, Task<(BatchItemResult Item, bool Unauthorized)>> send)
    {
        var count = uris.Count;
        var items = new BatchItemResult[count];
        if (count == 0)
            return Summarize(items);

        using var abort = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = new Task[count];

        for (var i = 0; i < count; i++)
        {
            var index = i;
            tasks[i] = ProcessOneAsync(index, uris[index], items, gate, abort, send);
        }

        await Task.WhenAll(tasks);
        return Summarize(items);
    }

    private static async Task ProcessOneAsync(
        int index,
        string uri,
        BatchItemResult[] items,
        SemaphoreSlim gate,
        CancellationTokenSource abort,
        Func<int, CancellationToken, Task<(BatchItemResult Item, bool Unauthorized)>> send)
    {
        try
        {
            await gate.WaitAsync(abort.Token);
        }
        catch (OperationCanceledException)
        {
            items[index] = Cancelled(uri);
            return;
        }

        try
        {
            if (abort.IsCancellationRequested)
            {
                items[index] = Cancelled(uri);
                return;
            }

            // Send without abort/user CT: in-flight calls finish and count as ok/fail.
            var (item, unauthorized) = await send(index, CancellationToken.None);
            items[index] = item;

            if (unauthorized)
                abort.Cancel();
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsUnauthorized(ElResult<TagApplyData> result) =>
        !result.IsSuccess && result.Error is { HttpStatus: 401 };

    private static BatchItemResult Cancelled(string uri) =>
        new(uri, Ok: false, ErrorCode: null, ErrorMessage: null, CancelledBeforeSend: true);

    private static BatchItemResult ToItem(string uri, ElResult<TagApplyData> result)
    {
        if (result.IsSuccess)
            return new(uri, Ok: true, ErrorCode: null, ErrorMessage: null, CancelledBeforeSend: false);

        var err = result.Error;
        return new(
            uri,
            Ok: false,
            ErrorCode: err?.Code,
            ErrorMessage: err?.Message,
            CancelledBeforeSend: false);
    }

    private static BatchSummary Summarize(IReadOnlyList<BatchItemResult> items)
    {
        var success = 0;
        var failed = 0;
        var cancelled = 0;
        foreach (var item in items)
        {
            if (item.CancelledBeforeSend)
                cancelled++;
            else if (item.Ok)
                success++;
            else
                failed++;
        }

        return new BatchSummary(success, failed, cancelled, items);
    }
}
