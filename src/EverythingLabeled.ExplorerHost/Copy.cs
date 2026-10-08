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
    public const string UntagAliasPlaceholder = "或手动输入要去除的别名…";
    public const string PathsSectionLabel = "目标路径";
    public const string PathSectionLabel = "路径";
    public const string TagsSectionLabel = "标签";
    public const string QueryLabel = "查询";
    public const string FindQueryPlaceholder = "输入标签后按回车键…";
    public const string ClearTags = "清空标签";
    public const string FindAnd = "AND";
    public const string FindOr = "OR";
    public const string SubmitTag = "打标";
    public const string SubmitUntag = "去标";
    public const string Cancel = "取消";
    public const string Open = "打开";
    public const string SwitchToTag = "切换到打标";
    public const string SwitchToUntag = "切换到去标";
    public const string UntagTagListHint = "当前标签（单击即去标该别名）";
    public const string ExcludedSuffix = "（已排除）";
    public const string CannotOpen = "不可打开";
    public const string TagsLoadFailed = "加载标签失败";
    public const string PreviewFailed = "预览失败";
    public const string PreviewEmpty = "无匹配结果";
    public const string BatchRunning = "正在提交…";

    public static string PreviewCount(int count) => $"预览 {count} 条";
    public const string WindowTitleTag = "打标 — EverythingLabeled";
    public const string WindowTitleUntag = "去标 — EverythingLabeled";
    public const string WindowTitleTags = "看标签 — EverythingLabeled";
    public const string WindowTitleFind = "按标签查找 — EverythingLabeled";

    public static string ConfigInvalid(string configPath) =>
        $"配置无效或缺失。请检查：{configPath}（baseUrl 须为 http://127.0.0.1，且需配置 token）";

    public static string IgnoredUnsupported(int count) =>
        $"已忽略 {count} 个不支持的项";

    public static string ConfirmUntag(int pathCount) =>
        $"确定要从 {pathCount} 个路径移除该标签吗？";

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
