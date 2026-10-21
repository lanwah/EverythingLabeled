namespace EverythingLabeled.Client;

public interface IElApi
{
    Task<ElResult<TagApplyData>> TagAsync(
        string uri,
        string kind,
        IReadOnlyList<string> aliases,
        bool createMissing,
        CancellationToken ct = default);

    Task<ElResult<TagApplyData>> UntagAsync(
        string uri,
        IReadOnlyList<string> aliases,
        CancellationToken ct = default);

    Task<ElResult<TagsPage>> GetTagsAsync(string uri, CancellationToken ct = default);

    Task<ElResult<QueryPage>> QueryAsync(
        string q,
        string? expand,
        int? maxDepth,
        int? expandedTagLimit,
        int limit,
        int offset,
        CancellationToken ct = default);

    Task<ElResult<PreviewPage>> PreviewAsync(
        string q,
        string? expand,
        int? maxDepth,
        int? expandedTagLimit,
        int limit,
        CancellationToken ct = default);

    Task<ElResult<SuggestPage>> SuggestAsync(string prefix, int limit, CancellationToken ct = default);

    Task<ElResult<TagsPage>> ListTagsAsync(
        string? q,
        int limit,
        int offset,
        CancellationToken ct = default);

    Task<ElResult<TagInfo>> RenameTagAsync(string from, string to, CancellationToken ct = default);

    Task<ElResult<MetaData>> DeleteTagAsync(Guid id, CancellationToken ct = default);

    Task<ElResult<MetaData>> DeleteResourceAsync(Guid id, CancellationToken ct = default) =>
        Task.FromException<ElResult<MetaData>>(new NotImplementedException());

    Task<ElResult<HealthData>> HealthAsync(CancellationToken ct = default);

    Task<ElResult<MetaData>> MetaAsync(CancellationToken ct = default);
}
