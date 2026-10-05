namespace EverythingLabeled.Service.Services.Query;

public static class QueryExpansionLimits
{
    // 与查询语言 §3.1 同源：默认 = 服务端上限
    public const int DefaultMaxDepth = 8;
    public const int ServerMaxDepth = 8;
    public const int DefaultExpandedTagLimit = 512;
    public const int ServerMaxExpandedTagLimit = 512;
}
