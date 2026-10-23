using System.ComponentModel;
using EverythingLabeled.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EverythingLabeled.Mcp.Tools;

[McpServerToolType]
public sealed class ElTools(ElMcpRuntime runtime)
{
    private const string ConfigMissingMessage = "Configuration is missing or invalid.";
    private const string UntagConfirmMessage = "Call el_untag again with confirm=true to remove the aliases.";

    [McpServerTool(Name = "el_tag")]
    [Description("Add aliases to a local file, folder, or URL.")]
    public async Task<CallToolResult> Tag(
        [Description("Local path or URI of the resource.")] string target,
        [Description("Aliases to add (1–100 strings).")] string[] aliases,
        [Description("Create missing tags. Default false.")] bool create_missing = false,
        CancellationToken cancellationToken = default)
    {
        if (!TryGate(ElRequestRules.ParseTarget(target), out var canonical, out var fail) ||
            !TryGate(ElRequestRules.ResolveKindForTag(canonical), out var kind, out fail) ||
            !TryGate(ElRequestRules.ParseAliases(aliases), out var parsedAliases, out fail))
        {
            return fail;
        }

        if (RequireBusinessApi() is { } missing)
            return missing;

        return From(await runtime.Api!.TagAsync(
            canonical.Uri,
            kind,
            parsedAliases,
            create_missing,
            cancellationToken).ConfigureAwait(false));
    }

    [McpServerTool(Name = "el_untag", Destructive = true)]
    [Description(
        "Remove aliases from a target (destructive). When confirmUntag is true (the default), pass confirm=true or the tool returns confirmation_required. When confirmUntag is false, aliases are removed immediately.")]
    public async Task<CallToolResult> Untag(
        [Description("Local path or URI of the resource.")] string target,
        [Description("Aliases to remove (1–100 strings).")] string[] aliases,
        [Description("Required when confirmUntag is true (the default).")] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        if (!TryGate(ElRequestRules.ParseTarget(target), out var canonical, out var fail) ||
            !TryGate(ElRequestRules.ParseAliases(aliases), out var parsedAliases, out fail))
        {
            return fail;
        }

        if (RequireBusinessApi() is { } missing)
            return missing;

        if (runtime.ConfirmUntag && confirm != true)
        {
            return ElMcpJson.Fail(new ElError(ElErrorCodes.ConfirmationRequired, UntagConfirmMessage));
        }

        return From(await runtime.Api!.UntagAsync(canonical.Uri, parsedAliases, cancellationToken)
            .ConfigureAwait(false));
    }

    [McpServerTool(Name = "el_tags", ReadOnly = true)]
    [Description("List aliases currently on a target.")]
    public async Task<CallToolResult> Tags(
        [Description("Local path or URI of the resource.")] string target,
        CancellationToken cancellationToken = default)
    {
        if (!TryGate(ElRequestRules.ParseTarget(target), out var canonical, out var fail))
            return fail;

        if (RequireBusinessApi() is { } missing)
            return missing;

        return From(await runtime.Api!.GetTagsAsync(canonical.Uri, cancellationToken).ConfigureAwait(false));
    }

    [McpServerTool(Name = "el_query", ReadOnly = true)]
    [Description(
        "Query tagged resources. For large result sets, call el_preview first. Default limit is 50.")]
    public async Task<CallToolResult> Query(
        [Description("Query expression.")] string q,
        [Description("Expansion mode; only child is allowed.")] string? expand = null,
        [Description("Max expansion depth; requires expand.")] int? max_depth = null,
        [Description("Expansion tag cap; requires expand.")] int? expanded_tag_limit = null,
        [Description("Page size; default 50.")] int? limit = null,
        [Description("Page offset; default 0.")] int? offset = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseSearch(q, expand, max_depth, expanded_tag_limit, limit, 50, offset, parseOffset: true, out var parsed, out var fail))
            return fail;

        if (RequireBusinessApi() is { } missing)
            return missing;

        return From(await runtime.Api!.QueryAsync(
            parsed.Q,
            parsed.Expand.Expand,
            parsed.Expand.MaxDepth,
            parsed.Expand.ExpandedTagLimit,
            parsed.Limit,
            parsed.Offset ?? 0,
            ct: cancellationToken).ConfigureAwait(false));
    }

    [McpServerTool(Name = "el_preview", ReadOnly = true)]
    [Description("Preview a tag query without paging totals. Default limit is 10.")]
    public async Task<CallToolResult> Preview(
        [Description("Query expression.")] string q,
        [Description("Expansion mode; only child is allowed.")] string? expand = null,
        [Description("Max expansion depth; requires expand.")] int? max_depth = null,
        [Description("Expansion tag cap; requires expand.")] int? expanded_tag_limit = null,
        [Description("Page size; default 10.")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseSearch(q, expand, max_depth, expanded_tag_limit, limit, 10, offset: null, parseOffset: false, out var parsed, out var fail))
            return fail;

        if (RequireBusinessApi() is { } missing)
            return missing;

        return From(await runtime.Api!.PreviewAsync(
            parsed.Q,
            parsed.Expand.Expand,
            parsed.Expand.MaxDepth,
            parsed.Expand.ExpandedTagLimit,
            parsed.Limit,
            ct: cancellationToken).ConfigureAwait(false));
    }

    [McpServerTool(Name = "el_suggest", ReadOnly = true)]
    [Description("Suggest tag aliases matching a prefix. Default limit is 10.")]
    public async Task<CallToolResult> Suggest(
        [Description("Alias prefix.")] string prefix,
        [Description("Page size; default 10.")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGate(ElRequestRules.ParseRequiredText(prefix, "prefix must not be empty."), out var text, out var fail) ||
            !TryGate(ElRequestRules.ParseLimit(limit, 10), out var parsedLimit, out fail))
        {
            return fail;
        }

        if (RequireBusinessApi() is { } missing)
            return missing;

        return From(await runtime.Api!.SuggestAsync(text, parsedLimit, cancellationToken).ConfigureAwait(false));
    }

    [McpServerTool(Name = "el_health", ReadOnly = true)]
    [Description("Check whether the EverythingLabeled service is reachable. Token is not required.")]
    public async Task<CallToolResult> Health(CancellationToken cancellationToken = default)
    {
        if (!runtime.HasBaseUrl || runtime.Api is null)
            return ConfigMissing();

        return From(await runtime.Api.HealthAsync(cancellationToken).ConfigureAwait(false));
    }

    private CallToolResult? RequireBusinessApi() =>
        runtime.Api is null || !runtime.HasToken ? ConfigMissing() : null;

    private static CallToolResult ConfigMissing() =>
        ElMcpJson.Fail(new ElError(ElErrorCodes.ConfigMissing, ConfigMissingMessage));

    private static CallToolResult From<T>(ElResult<T> result) =>
        result.IsSuccess
            ? ElMcpJson.Ok(result.Data!, result.Message)
            : ElMcpJson.Fail(result.Error!.Value);

    private static bool TryGate<T>(ElResult<T> result, out T value, out CallToolResult fail)
    {
        if (result.IsSuccess)
        {
            value = result.Data!;
            fail = null!;
            return true;
        }

        value = default!;
        fail = ElMcpJson.Fail(result.Error!.Value);
        return false;
    }

    private static bool TryParseSearch(
        string q,
        string? expand,
        int? maxDepth,
        int? expandedTagLimit,
        int? limit,
        int defaultLimit,
        int? offset,
        bool parseOffset,
        out SearchArgs parsed,
        out CallToolResult fail)
    {
        parsed = default;
        if (!TryGate(ElRequestRules.ParseRequiredText(q, "q must not be empty."), out var text, out fail) ||
            !TryGate(ElRequestRules.ParseExpand(expand, maxDepth, expandedTagLimit), out var parsedExpand, out fail) ||
            !TryGate(ElRequestRules.ParseLimit(limit, defaultLimit), out var parsedLimit, out fail))
        {
            return false;
        }

        int? parsedOffset = null;
        if (parseOffset)
        {
            if (!TryGate(ElRequestRules.ParseOffset(offset), out var off, out fail))
                return false;
            parsedOffset = off;
        }

        parsed = new SearchArgs(text, parsedExpand, parsedLimit, parsedOffset);
        return true;
    }

    private readonly record struct SearchArgs(string Q, ElQueryExpand Expand, int Limit, int? Offset);
}
