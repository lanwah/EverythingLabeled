namespace EverythingLabeled.ExplorerHost;

public static class Copy
{
    public const string ServiceUnreachable = "服务未运行或地址错误";
    public const string TokenInvalid = "token 无效";
    public const string PathNotFoundOrBroken = "路径不存在或无法访问";
    public const string MaxItemsExceeded = "最多 100 项";
    public const string EmptyAlias = "请输入一个别名";
    public const string EmptyQuery = "请输入查询";
    public const string NoIncludedPaths = "没有可提交的路径";
    public const string TagsNeedSinglePath = "看标签需要恰好选中 1 个路径";
    public const string CreateMissingLabel = "创建缺失标签";
    public const string AliasLabel = "别名";
    public const string AliasPlaceholder = "输入别名…";
    public const string TagAliasContinuePlaceholder = "继续输入…";
    public const string CurrentTagsLabel = "当前标签";
    public const string UntagFilterPlaceholder = "检索当前标签，支持拼首（可选）";
    public const string UntagChipHint = "点叉号去除；标签多时可检索";
    public const string UntagFilterMiss = "没有匹配的当前标签";
    public const string UntagNoCurrentTags = "当前没有可去除的标签";
    public const string UntagTagsEmpty = "暂无标签";
    public const string UntagNeedsOnePath = "去标请只选中一个路径";
    public const string PathsSectionLabel = "目标路径";
    public const string PathSectionLabel = "路径";
    public const string TagsSectionLabel = "标签";
    public const string QueryLabel = "查询";
    public const string FindQueryPlaceholder = "输入标签后按回车键…";
    public const string ClearTags = "清空标签";
    public const string SubmitTag = "打标";
    public const string SubmitUntag = "去标";
    public const string Cancel = "取消";
    public const string Open = "打开";
    public const string SwitchToTag = "切换到打标";
    public const string SwitchToUntag = "切换到去标";
    public const string UntagTagListHint = "当前标签（点叉号去除）";
    public const string ExcludedSuffix = "（已排除）";
    public const string CannotOpen = "不可打开";
    public const string TagsLoadFailed = "加载标签失败";
    public const string PreviewFailed = "预览失败";
    public const string PreviewEmpty = "无匹配结果";
    public const string BatchRunning = "正在提交…";
    public const string TagReady = "准备就绪";

    public static string PreviewCount(int count) => $"预览 {count} 条";

    public static string SuggestCount(int count) => $"建议 {count} 条";

    public static string TagWillApply(int pathCount, string alias) =>
        $"将为 {pathCount} 项打上「{alias}」";
    public const string WindowTitleTag = "打标 — EverythingLabeled";
    public const string WindowTitleUntag = "去标 — EverythingLabeled";
    public const string WindowTitleTags = "看标签 — EverythingLabeled";
    public const string WindowTitleFind = "按标签查找 — EverythingLabeled";
    public const string WindowTitleLibrary = "管理标签 — EverythingLabeled";
    public const string WindowTitleResources = "管理资源 — EverythingLabeled";
    public const string ResourcesEmpty = "添加标签后，这里会列出匹配的资源";
    public const string ResourcesNoMatch = "没有匹配的资源";
    public const string ResourcesNeedTags = "先添加标签";
    public const string ResourcesQueryPlaceholder = "输入标签后按回车键...";
    public const string ResourcesQueryMore = "继续输入…";
    public const string ReplaceAddressRequired = "请填写新地址";
    public const string ReplaceKindMismatch = "类型与地址不匹配";
    public const string ReplaceSameResource = "新地址与原资源相同";
    public const string ReplaceDeleteFailed = "标签已复制到新地址，但删除原资源失败：";
    public const string LibrarySearchPlaceholder = "按名称前缀或拼首筛选…";
    public const string LibraryEmpty = "还没有标签";
    public const string LibraryNoMatch = "没有匹配的标签";
    public const string LibraryRename = "改名";
    public const string LibraryDelete = "删除";
    public const string LibrarySave = "保存";
    public const string LibraryRenameHint = "文件和网页上的绑定仍留在这个标签上。只替换与名称相同的别名。";
    public const string LibraryDeleteTitle = "删除标签";
    public const string LibraryLoadFailed = "加载标签失败";
    public const string LibraryRenameFailed = "改名失败";
    public const string LibraryDeleteFailed = "删除失败";
    public const string LibraryEmptyName = "请输入新名称";

    public static string LibraryCount(int count) => $"{count} 个标签";

    public static string LibraryExtraAliases(IEnumerable<string> aliases, string name)
    {
        var extras = aliases
            .Where(a => !string.Equals(a, name, StringComparison.Ordinal))
            .ToList();
        return extras.Count == 0 ? string.Empty : "另有别名：" + string.Join("、", extras);
    }

    public static string LibraryDeleteConfirm(string name) =>
        $"将删除标签「{name}」。所有文件和网页上的这个标签都会去掉，且不能撤销。";

    public static string ResourceDeleteConfirm(string name, string uri) =>
        $"将删除「{name}」（{uri}）。它上面的标签绑定会去掉，标签本身保留，且不能撤销。";

    public static string ResourceReplaceLead(string name) =>
        $"原资源「{name}」。新地址会打上同样的标签，然后删除原资源。标签本身保留。";

    public static string ConfigInvalid(string configPath) =>
        $"配置无效或缺失。请检查：{configPath}（baseUrl 须为 http://127.0.0.1，且需配置 token）";

    public static string IgnoredUnsupported(int count) =>
        $"已忽略 {count} 个不支持的项";

    public static string ConfirmUntag(int pathCount) =>
        $"确定要从 {pathCount} 个路径移除该标签吗？";

    public static string UntagFilterMatch(int shown, int total) =>
        $"匹配 {CurrentTagFilter.CountText(shown, total)}，点叉号去除";

    public static string BatchSummary(int success, int failed, int cancelled) =>
        $"完成：成功 {success}，失败 {failed}，已取消未发送 {cancelled}";

    public static string BatchProgress(int done, int total) =>
        $"进度 {done}/{total}";

    public static string PathLine(string path, bool included, string? excludeReason)
    {
        if (included)
            return path;
        var reason = string.IsNullOrEmpty(excludeReason) ? ExcludedSuffix : $" — {excludeReason}";
        return path + reason;
    }

    public static string PreviewLine(string uri, string kind, string? displayName, bool canOpen)
    {
        var label = string.IsNullOrWhiteSpace(displayName) ? uri : $"{displayName}  ({uri})";
        return canOpen ? $"{kind}: {label}" : $"{kind}: {label}  [{CannotOpen}]";
    }
}
