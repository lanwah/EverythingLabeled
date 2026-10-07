using System.IO.Pipes;
using System.Text;

namespace EverythingLabeled.ExplorerShared;

public static class FindHostIpc
{
    public const string DefaultMutexName = @"Local\EverythingLabeled.FindHost";
    public const string DefaultPipeName = "EverythingLabeled.FindHost";
    public const string ActivateLine = "Activate\n";

    private const string MutexEnvKey = "EL_FIND_MUTEX_NAME";
    private const string PipeEnvKey = "EL_FIND_PIPE_NAME";

    public static string ResolveMutexName(IReadOnlyDictionary<string, string?>? env = null)
    {
        var overrideName = GetEnv(env, MutexEnvKey);
        return string.IsNullOrWhiteSpace(overrideName) ? DefaultMutexName : overrideName;
    }

    public static string ResolvePipeName(IReadOnlyDictionary<string, string?>? env = null)
    {
        var overrideName = GetEnv(env, PipeEnvKey);
        return string.IsNullOrWhiteSpace(overrideName) ? DefaultPipeName : overrideName;
    }

    /// <summary>连接命名管道并写 ActivateLine；成功 true。超时/失败 false。</summary>
    public static bool TrySendActivate(string pipeName, TimeSpan timeout)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.Out,
                PipeOptions.None);
            var ms = timeout.TotalMilliseconds;
            var timeoutMs = ms <= 0 ? 1 : (ms >= int.MaxValue ? int.MaxValue : (int)ms);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
                NewLine = "\n",
            };
            writer.Write(ActivateLine);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetEnv(IReadOnlyDictionary<string, string?>? env, string key)
    {
        if (env is not null)
            return env.TryGetValue(key, out var value) ? value : null;

        return Environment.GetEnvironmentVariable(key);
    }
}
