using System.Collections;
using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using EverythingLabeled.Client;
using EverythingLabeled.PathUri;
using System.CommandLine;

namespace EverythingLabeled.Cli;

public sealed class ElInvocation
{
    public required string[] Args { get; init; }
    public required TextWriter Stdout { get; init; }
    public required TextWriter Stderr { get; init; }
    public TextReader Stdin { get; init; } = TextReader.Null;
    public bool IsTty { get; init; }
    public IElApi? Api { get; init; }
    public Func<ElConfigOverrides, ElResult<ElConfig>>? LoadConfig { get; init; }
    public string DefaultConfigPath { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EverythingLabeled", "config.json");
    public CancellationToken CancellationToken { get; init; }
}

public static class ElApp
{
    public static async Task<int> RunAsync(ElInvocation inv)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var english = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentCulture = english;
        CultureInfo.CurrentUICulture = english;
        try
        {
            inv.CancellationToken.ThrowIfCancellationRequested();

            // VersionOption cannot be combined with other tokens (e.g. --json); handle offline.
            if (HasToken(inv.Args, "--version"))
            {
                inv.Stdout.WriteLine(GetInformationalVersion());
                return ExitCodes.Ok;
            }

            var root = BuildRoot(inv);
            var parseResult = root.Parse(inv.Args);
            var json = HasToken(inv.Args, "--json") || parseResult.GetValue(JsonOption);

            if (parseResult.Errors.Count > 0 && json)
            {
                var message = parseResult.Errors[0].Message;
                WriteError(inv, json: true, new ElError(ElErrorCodes.BadRequest, message));
                return ExitCodes.Usage;
            }

            var invocation = new InvocationConfiguration
            {
                Output = inv.Stdout,
                Error = inv.Stderr,
                EnableDefaultExceptionHandler = false,
            };

            return await parseResult.InvokeAsync(invocation, inv.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            WriteError(
                inv,
                HasToken(inv.Args, "--json"),
                new ElError(ElErrorCodes.ConnectionFailed, "Unable to reach the service."));
            return ExitCodes.ConfigOrConnection;
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    internal static readonly Option<bool> JsonOption = new("--json")
    {
        Recursive = true,
        Description = "Write machine-readable JSON on stdout (errors on stderr).",
    };

    internal static readonly Option<string?> BaseUrlOption = new("--base-url")
    {
        Recursive = true,
        Description = "Service base URL (overrides config and EL_BASE_URL).",
    };

    internal static readonly Option<string?> TokenOption = new("--token")
    {
        Recursive = true,
        Description = "Bearer token (overrides config and EL_TOKEN).",
    };

    internal static readonly Option<string?> ConfigOption = new("--config")
    {
        Recursive = true,
        Description = "Path to config.json (overrides EL_CONFIG).",
    };

    internal static readonly Argument<string> TargetArgument = new("target")
    {
        Description = "File path, file:// URI, or http(s) URL.",
    };

    internal static readonly Argument<string[]> AliasesArgument = new("aliases")
    {
        Arity = ArgumentArity.ZeroOrMore,
        Description = "Tag aliases (1–100).",
    };

    internal static readonly Option<bool> CreateOption = new("--create", "-c")
    {
        Description = "Create missing tags.",
    };

    internal static readonly Option<bool> YesOption = new("--yes", "-y")
    {
        Description = "Skip untag confirmation.",
    };

    internal static readonly Option<string?> ExpandOption = new("--expand")
    {
        Description = "Expand related tags (child only).",
    };

    internal static readonly Option<string?> LimitOption = new("--limit")
    {
        Description = "Page size.",
    };

    internal static readonly Option<string?> OffsetOption = new("--offset")
    {
        Description = "Page offset (query only).",
    };

    internal static readonly Option<string?> MaxDepthOption = new("--max-depth")
    {
        Description = "Expansion max depth (requires --expand).",
    };

    internal static readonly Option<string?> ExpandedTagLimitOption = new("--expanded-tag-limit")
    {
        Description = "Expansion tag cap (requires --expand).",
    };

    private static RootCommand BuildRoot(ElInvocation inv)
    {
        var root = new RootCommand("Tag local files and URLs via the EverythingLabeled service.");
        root.Options.Add(JsonOption);
        root.Options.Add(BaseUrlOption);
        root.Options.Add(TokenOption);
        root.Options.Add(ConfigOption);
        root.Options.Add(new VersionOption());

        var tag = new Command("tag", "Add aliases to a target.");
        tag.Arguments.Add(TargetArgument);
        tag.Arguments.Add(AliasesArgument);
        tag.Options.Add(CreateOption);
        tag.SetAction((parseResult, _) => HandleTagAsync(inv, parseResult));

        var untag = new Command("untag", "Remove aliases from a target.");
        untag.Arguments.Add(TargetArgument);
        untag.Arguments.Add(AliasesArgument);
        untag.Options.Add(YesOption);
        untag.SetAction((parseResult, _) => HandleUntagAsync(inv, parseResult));

        var tags = new Command("tags", "List aliases on a target.");
        tags.Arguments.Add(TargetArgument);
        tags.SetAction((parseResult, _) => HandleTagsAsync(inv, parseResult));

        root.Subcommands.Add(tag);
        root.Subcommands.Add(untag);
        root.Subcommands.Add(tags);

        var queryText = new Argument<string>("q") { Description = "Query expression." };
        var query = new Command("query", "Search resources by tag query.");
        query.Arguments.Add(queryText);
        query.Options.Add(ExpandOption);
        query.Options.Add(LimitOption);
        query.Options.Add(OffsetOption);
        query.Options.Add(MaxDepthOption);
        query.Options.Add(ExpandedTagLimitOption);
        query.SetAction((parseResult, _) => HandleQueryAsync(inv, parseResult, queryText));

        var previewText = new Argument<string>("q") { Description = "Query expression." };
        var preview = new Command("preview", "Preview a tag query without paging totals.");
        preview.Arguments.Add(previewText);
        preview.Options.Add(ExpandOption);
        preview.Options.Add(LimitOption);
        preview.Options.Add(MaxDepthOption);
        preview.Options.Add(ExpandedTagLimitOption);
        preview.SetAction((parseResult, _) => HandlePreviewAsync(inv, parseResult, previewText));

        var suggestText = new Argument<string>("prefix") { Description = "Alias prefix." };
        var suggest = new Command("suggest", "Suggest tag aliases.");
        suggest.Arguments.Add(suggestText);
        suggest.Options.Add(LimitOption);
        suggest.SetAction((parseResult, _) => HandleSuggestAsync(inv, parseResult, suggestText));

        var shellArg = new Argument<string>("shell")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Shell name (powershell).",
        };
        var completion = new Command("completion", "Print a shell completion script.");
        completion.Arguments.Add(shellArg);
        completion.SetAction(parseResult =>
        {
            var json = parseResult.GetValue(JsonOption);
            var shell = parseResult.GetValue(shellArg);
            if (!string.Equals(shell, "powershell", StringComparison.Ordinal))
            {
                WriteError(
                    inv,
                    json,
                    new ElError(ElErrorCodes.BadRequest, "Only completion powershell is supported."));
                return ExitCodes.Usage;
            }

            inv.Stdout.Write(CompletionScript.PowerShell);
            if (!CompletionScript.PowerShell.EndsWith('\n'))
                inv.Stdout.WriteLine();
            return ExitCodes.Ok;
        });

        root.Subcommands.Add(query);
        root.Subcommands.Add(preview);
        root.Subcommands.Add(suggest);
        root.Subcommands.Add(completion);
        return root;
    }

    private static async Task<int> HandleTagAsync(ElInvocation inv, ParseResult parseResult)
    {
        var json = parseResult.GetValue(JsonOption);
        var parsed = ParseTargetAndAliases(parseResult, json, inv, out var canonical, out var aliases);
        if (parsed is { } localCode)
            return localCode;

        var kind = CommandSupport.ResolveKind(canonical);
        if (!kind.IsSuccess)
        {
            WriteError(inv, json, kind.Error!.Value);
            return ExitCodeFor(kind.Error.Value);
        }

        var createMissing = parseResult.GetValue(CreateOption);
        return await RunNeedingNetworkAsync(
            inv,
            ConfigOverrides(parseResult),
            json,
            async (api, _, token) =>
            {
                var result = await api.TagAsync(canonical.Uri, kind.Data!, aliases, createMissing, token)
                    .ConfigureAwait(false);
                return WriteApply(inv, json, aliases, result, untag: false);
            }).ConfigureAwait(false);
    }

    private static async Task<int> HandleUntagAsync(ElInvocation inv, ParseResult parseResult)
    {
        var json = parseResult.GetValue(JsonOption);
        var parsed = ParseTargetAndAliases(parseResult, json, inv, out var canonical, out var aliases);
        if (parsed is { } localCode)
            return localCode;

        return await RunNeedingNetworkAsync(
            inv,
            ConfigOverrides(parseResult),
            json,
            async (api, config, token) =>
            {
                var yes = parseResult.GetValue(YesOption);
                if (config.ConfirmUntag && !yes)
                {
                    var confirm = ConfirmUntag(inv, json, aliases, canonical.Uri);
                    if (confirm is { } confirmCode)
                        return confirmCode;
                }

                var result = await api.UntagAsync(canonical.Uri, aliases, token).ConfigureAwait(false);
                return WriteApply(inv, json, aliases, result, untag: true);
            }).ConfigureAwait(false);
    }

    private static async Task<int> HandleTagsAsync(ElInvocation inv, ParseResult parseResult)
    {
        var json = parseResult.GetValue(JsonOption);
        var target = CommandSupport.ParseTarget(parseResult.GetValue(TargetArgument));
        if (!target.IsSuccess)
        {
            WriteError(inv, json, target.Error!.Value);
            return ExitCodes.Usage;
        }

        var canonical = target.Data;
        return await RunNeedingNetworkAsync(
            inv,
            ConfigOverrides(parseResult),
            json,
            async (api, _, token) =>
            {
                var result = await api.GetTagsAsync(canonical.Uri, token).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    WriteError(inv, json, result.Error!.Value);
                    return ExitCodeFor(result.Error.Value);
                }

                WriteSuccessMessage(inv, result.Message);
                if (json)
                    inv.Stdout.Write(JsonSerializer.Serialize(result.Data, ElJson.Options));
                else
                    HumanFormatter.WriteTags(inv.Stdout, result.Data!);
                return ExitCodes.Ok;
            }).ConfigureAwait(false);
    }

    private static Task<int> HandleQueryAsync(ElInvocation inv, ParseResult parseResult, Argument<string> qArg)
    {
        var json = parseResult.GetValue(JsonOption);
        if (!TryParseSearchArgs(inv, parseResult, json, qArg, defaultLimit: 50, includeOffset: true,
                out var q, out var expand, out var limit, out var offset, out var localCode))
            return Task.FromResult(localCode);

        return RunNeedingNetworkAsync(
            inv,
            ConfigOverrides(parseResult),
            json,
            async (api, _, token) =>
            {
                var result = await api.QueryAsync(
                        q, expand.Expand, expand.MaxDepth, expand.ExpandedTagLimit, limit, offset, token)
                    .ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    WriteError(inv, json, result.Error!.Value);
                    return ExitCodeFor(result.Error.Value);
                }

                WriteSuccessMessage(inv, result.Message);
                if (json)
                    inv.Stdout.Write(JsonSerializer.Serialize(result.Data, ElJson.Options));
                else
                {
                    HumanFormatter.WriteHits(inv.Stdout, result.Data!.Items);
                    HumanFormatter.WriteQueryMeta(
                        inv.Stderr, result.Data.Offset, result.Data.Limit, result.Data.Total);
                }

                return ExitCodes.Ok;
            });
    }

    private static Task<int> HandlePreviewAsync(ElInvocation inv, ParseResult parseResult, Argument<string> qArg)
    {
        var json = parseResult.GetValue(JsonOption);
        if (!TryParseSearchArgs(inv, parseResult, json, qArg, defaultLimit: 10, includeOffset: false,
                out var q, out var expand, out var limit, out _, out var localCode))
            return Task.FromResult(localCode);

        return RunNeedingNetworkAsync(
            inv,
            ConfigOverrides(parseResult),
            json,
            async (api, _, token) =>
            {
                var result = await api.PreviewAsync(
                        q, expand.Expand, expand.MaxDepth, expand.ExpandedTagLimit, limit, token)
                    .ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    WriteError(inv, json, result.Error!.Value);
                    return ExitCodeFor(result.Error.Value);
                }

                WriteSuccessMessage(inv, result.Message);
                if (json)
                    inv.Stdout.Write(JsonSerializer.Serialize(result.Data, ElJson.Options));
                else
                {
                    HumanFormatter.WriteHits(inv.Stdout, result.Data!.Items);
                    HumanFormatter.WritePreviewMeta(inv.Stderr, result.Data.Offset, result.Data.Limit);
                }

                return ExitCodes.Ok;
            });
    }

    private static Task<int> HandleSuggestAsync(ElInvocation inv, ParseResult parseResult, Argument<string> prefixArg)
    {
        var json = parseResult.GetValue(JsonOption);
        var prefix = CommandSupport.ParseRequiredText(parseResult.GetValue(prefixArg), "prefix must not be empty.");
        if (!prefix.IsSuccess)
        {
            WriteError(inv, json, prefix.Error!.Value);
            return Task.FromResult(ExitCodes.Usage);
        }

        var limit = CommandSupport.ParseLimit(parseResult.GetValue(LimitOption), 10);
        if (!limit.IsSuccess)
        {
            WriteError(inv, json, limit.Error!.Value);
            return Task.FromResult(ExitCodes.Usage);
        }

        return RunNeedingNetworkAsync(
            inv,
            ConfigOverrides(parseResult),
            json,
            async (api, _, token) =>
            {
                var result = await api.SuggestAsync(prefix.Data!, limit.Data, token).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    WriteError(inv, json, result.Error!.Value);
                    return ExitCodeFor(result.Error.Value);
                }

                WriteSuccessMessage(inv, result.Message);
                if (json)
                    inv.Stdout.Write(JsonSerializer.Serialize(result.Data, ElJson.Options));
                else
                    HumanFormatter.WriteSuggest(inv.Stdout, result.Data!);
                return ExitCodes.Ok;
            });
    }

    private static bool TryParseSearchArgs(
        ElInvocation inv,
        ParseResult parseResult,
        bool json,
        Argument<string> qArg,
        int defaultLimit,
        bool includeOffset,
        out string q,
        out ElQueryExpand expand,
        out int limit,
        out int offset,
        out int localCode)
    {
        q = "";
        expand = new ElQueryExpand(null, null, null);
        limit = defaultLimit;
        offset = 0;
        localCode = ExitCodes.Usage;

        var parsedQ = CommandSupport.ParseRequiredText(parseResult.GetValue(qArg), "q must not be empty.");
        if (!parsedQ.IsSuccess)
        {
            WriteError(inv, json, parsedQ.Error!.Value);
            return false;
        }

        var parsedExpand = CommandSupport.ParseExpand(
            parseResult.GetValue(ExpandOption),
            parseResult.GetValue(MaxDepthOption),
            parseResult.GetValue(ExpandedTagLimitOption));
        if (!parsedExpand.IsSuccess)
        {
            WriteError(inv, json, parsedExpand.Error!.Value);
            return false;
        }

        var parsedLimit = CommandSupport.ParseLimit(parseResult.GetValue(LimitOption), defaultLimit);
        if (!parsedLimit.IsSuccess)
        {
            WriteError(inv, json, parsedLimit.Error!.Value);
            return false;
        }

        var parsedOffset = includeOffset
            ? CommandSupport.ParseOffset(parseResult.GetValue(OffsetOption))
            : ElResult<int>.Ok(0);
        if (!parsedOffset.IsSuccess)
        {
            WriteError(inv, json, parsedOffset.Error!.Value);
            return false;
        }

        q = parsedQ.Data!;
        expand = parsedExpand.Data;
        limit = parsedLimit.Data;
        offset = parsedOffset.Data;
        return true;
    }

    private static int? ParseTargetAndAliases(
        ParseResult parseResult,
        bool json,
        ElInvocation inv,
        out CanonicalTarget canonical,
        out IReadOnlyList<string> aliases)
    {
        canonical = default;
        aliases = [];
        var target = CommandSupport.ParseTarget(parseResult.GetValue(TargetArgument));
        if (!target.IsSuccess)
        {
            WriteError(inv, json, target.Error!.Value);
            return ExitCodes.Usage;
        }

        var parsedAliases = CommandSupport.ParseAliases(parseResult.GetValue(AliasesArgument));
        if (!parsedAliases.IsSuccess)
        {
            WriteError(inv, json, parsedAliases.Error!.Value);
            return ExitCodes.Usage;
        }

        canonical = target.Data;
        aliases = parsedAliases.Data!;
        return null;
    }

    private static int? ConfirmUntag(ElInvocation inv, bool json, IReadOnlyList<string> aliases, string uri)
    {
        if (!inv.IsTty)
        {
            WriteError(
                inv,
                json,
                new ElError(ElErrorCodes.BadRequest, "Refusing to untag without --yes when stdin is not a TTY."));
            return ExitCodes.Usage;
        }

        inv.Stderr.WriteLine($"Remove alias(es) [{string.Join(", ", aliases)}] from {uri}? [y/N]");
        var line = inv.Stdin.ReadLine();
        if (line is "y" or "Y")
            return null;

        WriteError(inv, json, new ElError(ElErrorCodes.BadRequest, "Untag cancelled."));
        return ExitCodes.Usage;
    }

    private static int WriteApply(
        ElInvocation inv,
        bool json,
        IReadOnlyList<string> aliases,
        ElResult<TagApplyData> result,
        bool untag)
    {
        if (!result.IsSuccess)
        {
            WriteError(inv, json, result.Error!.Value);
            return ExitCodeFor(result.Error.Value);
        }

        WriteSuccessMessage(inv, result.Message);
        if (json)
            inv.Stdout.Write(JsonSerializer.Serialize(result.Data, ElJson.Options));
        else
            HumanFormatter.WriteTagApply(inv.Stdout, aliases, result.Data!, untag);
        return ExitCodes.Ok;
    }

    private static void WriteSuccessMessage(ElInvocation inv, string? message)
    {
        if (!string.IsNullOrEmpty(message))
            inv.Stderr.WriteLine(message);
    }

    private static ElConfigOverrides ConfigOverrides(ParseResult parseResult) =>
        new(parseResult.GetValue(ConfigOption), parseResult.GetValue(BaseUrlOption), parseResult.GetValue(TokenOption));

    internal static int ExitCodeFor(ElError error)
    {
        if (error.HttpStatus is >= 500)
            return ExitCodes.Http5xx;
        if (error.HttpStatus is >= 400)
            return ExitCodes.Http4xx;
        return error.Code is ElErrorCodes.ConfigMissing or ElErrorCodes.ConnectionFailed
            ? ExitCodes.ConfigOrConnection
            : ExitCodes.Usage;
    }

    internal static void WriteError(ElInvocation inv, bool json, ElError error)
    {
        if (json)
        {
            var payload = new JsonErrorEnvelope(new JsonErrorBody(error.Code, error.Message));
            inv.Stderr.Write(JsonSerializer.Serialize(payload, ElJson.Options));
            return;
        }

        inv.Stderr.WriteLine(error.Message);
    }

    internal static ElResult<ElConfig> LoadConfig(ElInvocation inv, ElConfigOverrides overrides)
    {
        var load = inv.LoadConfig ?? (o => ElConfigLoader.Load(o, CaptureEnv(), inv.DefaultConfigPath));
        return load(overrides);
    }

    internal static async Task<int> RunNeedingNetworkAsync(
        ElInvocation inv,
        ElConfigOverrides overrides,
        bool json,
        Func<IElApi, ElConfig, CancellationToken, Task<int>> action)
    {
        inv.CancellationToken.ThrowIfCancellationRequested();
        var loaded = LoadConfig(inv, overrides);
        if (!loaded.IsSuccess)
        {
            WriteError(inv, json, loaded.Error!.Value);
            return ExitCodes.ConfigOrConnection;
        }

        var config = loaded.Data!;
        if (inv.Api is not null)
            return await action(inv.Api, config, inv.CancellationToken).ConfigureAwait(false);

        using var http = new HttpClient
        {
            BaseAddress = new Uri(config.BaseUrl),
            Timeout = TimeSpan.FromSeconds(30),
        };
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        IElApi api = new HttpElApi(http, config.Token);
        return await action(api, config, inv.CancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, string?> CaptureEnv()
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key)
                map[key] = entry.Value as string;
        }

        return map;
    }

    private static bool HasToken(string[] args, string token)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, token, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string GetInformationalVersion()
    {
        var informational = typeof(ElApp).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        var version = typeof(ElApp).Assembly.GetName().Version;
        return version is null ? "0.1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private sealed record JsonErrorEnvelope(JsonErrorBody Error);

    private sealed record JsonErrorBody(string Code, string Message);
}
