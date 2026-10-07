namespace EverythingLabeled.ExplorerHost;

public static class Copy
{
    public const string ServiceUnreachable = "服务未运行或地址错误";
    public const string TokenInvalid = "token 无效";
    public const string PathNotFoundOrBroken = "路径不存在或无法访问";
    public const string MaxItemsExceeded = "最多 100 项";

    public static string ConfigInvalid(string configPath) =>
        $"配置无效或缺失。请检查：{configPath}（baseUrl 须为 http://127.0.0.1，且需配置 token）";

    public static string IgnoredUnsupported(int count) =>
        $"已忽略 {count} 个不支持的项";
}
