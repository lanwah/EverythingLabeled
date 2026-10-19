using System.Globalization;
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

    /// <summary>
    /// Connect, write ActivateLine, read HWND reply; optionally bring that window to the foreground.
    /// Returns true when the activate round-trip succeeds.
    /// </summary>
    public static bool TrySendActivate(
        string pipeName,
        TimeSpan timeout,
        bool bringToForeground = true)
    {
        try
        {
            // Caller (Tray after hotkey) typically holds foreground rights; grant Host as well.
            WindowForeground.AllowSetForegroundAny();

            using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.None);
            var ms = timeout.TotalMilliseconds;
            var timeoutMs = ms <= 0 ? 1 : (ms >= int.MaxValue ? int.MaxValue : (int)ms);
            client.Connect(timeoutMs);

            using var writer = new StreamWriter(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n",
            };
            writer.Write(ActivateLine);

            using var reader = new StreamReader(
                client,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 256,
                leaveOpen: true);
            var line = reader.ReadLine();
            if (line is null)
                return false;

            if (!long.TryParse(line.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hwndVal))
                return true;

            if (bringToForeground && hwndVal != 0)
                WindowForeground.TryBringToForeground(new IntPtr(hwndVal));

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
