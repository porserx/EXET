using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using MicaBrowser.Models;

namespace MicaBrowser.Services;

/// <summary>
/// Runs the bundled mihomo core with a user supplied Clash configuration.
/// The runtime copy only overrides ports/controller so the browser can point
/// its WebView2 proxy at a predictable local mixed port.
/// </summary>
public sealed class ClashService : IDisposable
{
    private Process? _process;
    private string _runtimeConfig = string.Empty;
    private string _logFile = string.Empty;

    public bool IsRunning => _process is { HasExited: false };

    public string LastError { get; private set; } = string.Empty;

    public static string BundledCore =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "Clash", "verge-mihomo.exe");

    public static string ResolveCore(AppSettings settings)
    {
        var configured = settings.ClashCorePath?.Trim() ?? string.Empty;
        if (configured.Length > 0 && File.Exists(configured)) return configured;
        return BundledCore;
    }

    public static string ResolveConfig(AppSettings settings)
    {
        var configured = settings.ClashConfigPath?.Trim() ?? string.Empty;
        if (configured.Length > 0) return configured;

        var verge = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "io.github.clash-verge-rev.clash-verge-rev",
            "clash-verge.yaml");
        return File.Exists(verge) ? verge : string.Empty;
    }

    public async Task StartAsync(AppSettings settings)
    {
        Stop();
        LastError = string.Empty;

        var core = ResolveCore(settings);
        var config = ResolveConfig(settings);
        if (!File.Exists(core))
        {
            throw new FileNotFoundException("找不到内置 Clash 内核：" + core);
        }
        if (!File.Exists(config))
        {
            throw new FileNotFoundException("找不到 Clash 配置文件，请在设置中选择。");
        }

        var configDir = Path.GetDirectoryName(config) ?? AppContext.BaseDirectory;
        _runtimeConfig = Path.Combine(configDir, "exet-runtime.yaml");
        _logFile = Path.Combine(configDir, "exet-mihomo.log");

        var yaml = await File.ReadAllTextAsync(config);
        yaml = OverrideScalar(yaml, "mixed-port", settings.ClashMixedPort.ToString());
        yaml = OverrideScalar(yaml, "external-controller", $"127.0.0.1:{settings.ClashApiPort}");
        if (!string.IsNullOrWhiteSpace(settings.ClashSecret))
        {
            yaml = OverrideScalar(yaml, "secret", settings.ClashSecret.Trim());
        }
        await File.WriteAllTextAsync(_runtimeConfig, yaml, new UTF8Encoding(false));

        var info = new ProcessStartInfo
        {
            FileName = core,
            WorkingDirectory = configDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        info.ArgumentList.Add("-d");
        info.ArgumentList.Add(configDir);
        info.ArgumentList.Add("-f");
        info.ArgumentList.Add(_runtimeConfig);

        _process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var log = new StringBuilder();
        void Append(string? line)
        {
            if (line is null) return;
            lock (log)
            {
                log.AppendLine(line);
                if (log.Length > 20000) log.Remove(0, log.Length - 12000);
            }
        }
        _process.OutputDataReceived += (_, e) => Append(e.Data);
        _process.ErrorDataReceived += (_, e) => Append(e.Data);
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await Task.Delay(1200);
        if (_process.HasExited)
        {
            var tail = log.ToString();
            try { await File.WriteAllTextAsync(_logFile, tail); } catch { }
            throw new InvalidOperationException(
                "Clash 内核启动失败：" + (tail.Length > 0 ? tail.Trim() : "进程已退出"));
        }
        Logger.Info($"Clash core started: pid={_process.Id}, mixed={settings.ClashMixedPort}");
    }

    public void Stop()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }
        }
        catch
        {
            // Best effort.
        }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }

    public string StatusText(AppSettings settings)
    {
        if (IsRunning)
        {
            return $"运行中 · 混合端口 {settings.ClashMixedPort}";
        }
        return LastError.Length > 0 ? "已停止 · " + LastError : "未运行";
    }

    private static string OverrideScalar(string yaml, string key, string value)
    {
        var pattern = @"^" + Regex.Escape(key) + @":.*$";
        if (Regex.IsMatch(yaml, pattern, RegexOptions.Multiline))
        {
            return Regex.Replace(yaml, pattern, key + ": " + value, RegexOptions.Multiline);
        }
        return key + ": " + value + Environment.NewLine + yaml;
    }

    public void Dispose() => Stop();
}
