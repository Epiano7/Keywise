using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace DesktopUsageAnalytics;

public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseUrl,
    string? InstallerDownloadUrl);

public static class UpdateService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/Epiano7/Keywise/releases/latest";
    private const string ReleasesUrl = "https://github.com/Epiano7/Keywise/releases";

    public static async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = GetCurrentVersion();
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Keywise-update-check");
        using var response = await httpClient.GetAsync(LatestReleaseApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var latestTag = root.GetProperty("tag_name").GetString();
        var releaseUrl = root.TryGetProperty("html_url", out var urlElement)
            ? urlElement.GetString()
            : ReleasesUrl;
        string? installerDownloadUrl = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                if (name is not null && name.StartsWith("Keywise-Setup-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    installerDownloadUrl = asset.TryGetProperty("browser_download_url", out var downloadElement)
                        ? downloadElement.GetString()
                        : null;
                    break;
                }
            }
        }

        var latestVersionText = latestTag?.TrimStart('v', 'V');
        var hasUpdate = Version.TryParse(currentVersion, out var currentVersionValue)
            && Version.TryParse(latestVersionText, out var latestVersionValue)
            && latestVersionValue > currentVersionValue;

        return new UpdateCheckResult(hasUpdate, currentVersion, latestTag, releaseUrl, installerDownloadUrl);
    }

    public static async Task<string> DownloadInstallerAsync(
        UpdateCheckResult update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.InstallerDownloadUrl))
        {
            throw new InvalidOperationException("This release does not include a Keywise installer asset.");
        }

        var downloadDirectory = Path.Combine(Path.GetTempPath(), "Keywise", "Updates");
        Directory.CreateDirectory(downloadDirectory);
        var version = update.LatestVersion?.TrimStart('v', 'V') ?? "latest";
        var destinationPath = Path.Combine(downloadDirectory, $"Keywise-Setup-{version}.exe");

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Keywise-update-download");
        using var response = await httpClient.GetAsync(update.InstallerDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = File.Create(destinationPath);
        var buffer = new byte[81920];
        long downloadedBytes = 0;
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            downloadedBytes += bytesRead;
            if (totalBytes is > 0)
            {
                progress?.Report(downloadedBytes * 100.0 / totalBytes.Value);
            }
        }

        progress?.Report(100);
        return destinationPath;
    }

    public static void RunInstallerAndExit(string installerPath)
    {
        var installedAppPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Keywise",
            "Keywise.exe");
        var runnerPath = Path.Combine(Path.GetTempPath(), "Keywise", "Updates", "run-keywise-update.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(runnerPath)!);
        File.WriteAllText(runnerPath, $"""
@echo off
start /wait "" "{installerPath}" /SILENT /NORESTART
start "" "{installedAppPath}"
del "%~f0"
""");

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/C \"{runnerPath}\"",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        System.Windows.Application.Current.Shutdown();
    }

    public static void OpenReleasesPage(string? releaseUrl = null)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = releaseUrl ?? ReleasesUrl,
            UseShellExecute = true
        });
    }

    private static string GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
