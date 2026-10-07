namespace EverythingLabeled.ExplorerHost;

public static class Copy
{
    public const string ServiceUnreachable = "服务未运行或地址错误";
    public const string TokenInvalid = "token 无效";

    public static string ConfigInvalid(string configPath) =>
        $"配置无效或缺失。请检查：{configPath}（baseUrl 须为 http://127.0.0.1，且需配置 token）";
}
