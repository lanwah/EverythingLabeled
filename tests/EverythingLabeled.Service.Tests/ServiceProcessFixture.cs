using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using EverythingLabeled.Service.Hosting;

namespace EverythingLabeled.Service.Tests;

internal sealed class ServiceProcessFixture : IAsyncDisposable
{
    private Process? _process;

    public required string AppDataRoot { get; init; }
    public required string MutexName { get; init; }
    public string ConfigPath => Path.Combine(AppDataRoot, "config.json");
    public string DatabasePath => Path.Combine(AppDataRoot, "data", "el.db");
    public string LogsDirectory => Path.Combine(AppDataRoot, "logs");
    public AppConfig? Config { get; private set; }
    public string BaseUrl => Config?.BaseUrl ?? throw new InvalidOperationException("Service has no config.");

    public static ServiceProcessFixture CreateIsolated()
    {
        var root = Path.Combine(Path.GetTempPath(), "el-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new ServiceProcessFixture
        {
            AppDataRoot = root,
            MutexName = @"Local\EverythingLabeled.Service.Test." + Guid.NewGuid().ToString("N"),
        };
    }

    public void WriteConfigForFreePort()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(Path.Combine(AppDataRoot, "data"));
        Directory.CreateDirectory(Path.Combine(AppDataRoot, "logs"));

        var port = FindEphemeralFreePort();
        var config = new AppConfig
        {
            BaseUrl = $"http://127.0.0.1:{port}",
            Token = ConfigBootstrap.GenerateToken(),
            Port = port,
        };
        ConfigBootstrap.WriteAtomic(ConfigPath, config);
        Config = config;
    }

    public async Task StartAsync(
        TimeSpan? readyTimeout = null,
        IReadOnlyDictionary<string, string>? extraEnv = null)
    {
        if (_process is not null)
            throw new InvalidOperationException("Already started.");

        var dll = FindServiceDll();
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { dll },
            WorkingDirectory = Path.GetDirectoryName(dll)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.Environment["EL_APPDATA"] = AppDataRoot;
        startInfo.Environment["EL_MUTEX_NAME"] = MutexName;
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment.Remove("ASPNETCORE_URLS");
        ApplyExtraEnv(startInfo, extraEnv);

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (stdout) stdout.AppendLine(e.Data);
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (stderr) stderr.AppendLine(e.Data);
        };

        if (!_process.Start())
            throw new InvalidOperationException("Failed to start service process.");

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var timeout = readyTimeout ?? TimeSpan.FromSeconds(20);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Service exited early with code {_process.ExitCode}.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
            }

            if (File.Exists(ConfigPath))
            {
                Config = ConfigBootstrap.LoadExisting(ConfigPath);
                if (await TryHealthAsync())
                    return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"Service did not become ready within {timeout}.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    public async Task<int> RunExpectExitAsync(
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string>? extraEnv = null)
    {
        var dll = FindServiceDll();
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { dll },
            WorkingDirectory = Path.GetDirectoryName(dll)!,
            UseShellExecute = false,
            // Drain pipes so Serilog Console + CloseAndFlush cannot deadlock on a full buffer.
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.Environment["EL_APPDATA"] = AppDataRoot;
        startInfo.Environment["EL_MUTEX_NAME"] = MutexName;
        startInfo.Environment.Remove("ASPNETCORE_URLS");
        ApplyExtraEnv(startInfo, extraEnv);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var exited = await WaitForExitAsync(process, timeout ?? TimeSpan.FromSeconds(15));
        if (!exited)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask), Task.Delay(1000));
            throw new TimeoutException(
                $"Process did not exit in time.\nSTDOUT:\n{await SafeRead(stdoutTask)}\nSTDERR:\n{await SafeRead(stderrTask)}");
        }

        await Task.WhenAll(stdoutTask, stderrTask);
        return process.ExitCode;
    }

    private static async Task<string> SafeRead(Task<string> readTask)
    {
        try
        {
            return await readTask;
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task<HttpResponseMessage> GetAsync(string path, string? bearerToken = null)
    {
        using var client = CreateClient(bearerToken);
        return await client.GetAsync(path);
    }

    public HttpClient CreateClient(string? bearerToken = null)
    {
        var client = new HttpClient { BaseAddress = new Uri(BaseUrl + "/") };
        if (bearerToken is not null)
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
        return client;
    }

    public async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<JsonElement>(stream);
    }

    public static OccupiedPort HoldPort(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        return new OccupiedPort(listener);
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync();
                }
            }
            catch
            {
                // ignore cleanup failures
            }

            _process.Dispose();
            _process = null;
        }

        try
        {
            if (Directory.Exists(AppDataRoot))
                Directory.Delete(AppDataRoot, recursive: true);
        }
        catch
        {
            // temp cleanup best-effort
        }
    }

    private static void ApplyExtraEnv(ProcessStartInfo startInfo, IReadOnlyDictionary<string, string>? extraEnv)
    {
        if (extraEnv is null)
            return;
        foreach (var (key, value) in extraEnv)
            startInfo.Environment[key] = value;
    }

    private async Task<bool> TryHealthAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            using var response = await client.GetAsync($"{Config!.BaseUrl}/v1/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static int FindEphemeralFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindServiceDll()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "EverythingLabeled.Service", "bin", "Debug", "net8.0",
                "EverythingLabeled.Service.dll")),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "EverythingLabeled.Service", "bin", "Release", "net8.0",
                "EverythingLabeled.Service.dll")),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            "EverythingLabeled.Service.dll not found. Build the Service project before running tests.");
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    internal sealed class OccupiedPort(TcpListener listener) : IDisposable
    {
        public void Dispose() => listener.Stop();
    }
}
