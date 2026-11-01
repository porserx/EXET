using System.Diagnostics;
using System.Text.Json;

namespace MicaBrowser.Services;

public sealed record GhostTaskInfo(string TaskId, string Name, string Folder);
public sealed record GhostTaskProgress(string Status, double Progress, long FileSize, string? Error);

public static class GhostDownloaderService
{
    public static string ResolveExecutable(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return "ghd";
        var path = configured.Trim();
        if (!File.Exists(path)) return path;

        if (!string.Equals(Path.GetFileName(path), "ghd.exe", StringComparison.OrdinalIgnoreCase))
        {
            var sibling = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, "bin", "ghd.exe");
            if (File.Exists(sibling)) return sibling;
        }
        return path;
    }

    public static async Task<GhostTaskInfo?> AddAsync(string executable, string url, string folder)
    {
        var output = await RunAsync(executable, new[] { "add", url, "-o", folder });
        if (string.IsNullOrWhiteSpace(output)) return null;
        try
        {
            using var doc = JsonDocument.Parse(output);
            var first = doc.RootElement.GetProperty("result")[0];
            var id = first.GetProperty("taskId").GetString();
            var name = first.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(id)) return null;
            return new GhostTaskInfo(id, name ?? "download", folder);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<GhostTaskProgress?> GetProgressAsync(string executable, string taskId)
    {
        var output = await RunAsync(executable, new[] { "task", taskId });
        if (string.IsNullOrWhiteSpace(output)) return null;
        try
        {
            using var doc = JsonDocument.Parse(output);
            var result = doc.RootElement.GetProperty("result");
            var status = result.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString() ?? "unknown"
                : "unknown";
            var progress = result.TryGetProperty("progress", out var progressElement)
                ? progressElement.GetDouble()
                : 0;
            var size = result.TryGetProperty("fileSize", out var sizeElement)
                ? sizeElement.GetInt64()
                : 0;
            var error = result.TryGetProperty("error", out var errorElement) && errorElement.ValueKind != JsonValueKind.Null
                ? errorElement.ToString()
                : null;
            return new GhostTaskProgress(status, progress, size, error);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> RunAsync(string executable, string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info);
            if (process is null) return null;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output.Trim();
        }
        catch (Exception ex)
        {
            Logger.Warn("Ghost Downloader command failed.", ex);
            return null;
        }
    }
}
