using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Utils;

public static class AutoUpdater
{
    private const string RepoOwner = "candaysa";
    private const string RepoName = "CS2_Admin";
    
    // Using a static client to avoid socket exhaustion
    private static readonly HttpClient HttpClient = new();

    static AutoUpdater()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CS2_Admin_Updater", "1.0"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public static async Task CheckForUpdatesAsync(ISwiftlyCore core, string currentVersion)
    {
        try
        {
            core.Logger.LogInformationIfEnabled("[CS2Admin] Checking for updates on GitHub...");

            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            var response = await HttpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to check for updates. GitHub API responded with: {Status}", response.StatusCode);
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GithubRelease>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (release == null || string.IsNullOrEmpty(release.TagName))
            {
                core.Logger.LogWarningIfEnabled("[CS2Admin] Failed to parse GitHub release data.");
                return;
            }

            // Normalizing versions (removing 'v' prefix if exists)
            var latestVer = release.TagName.TrimStart('v', 'V');
            var currVer = currentVersion.TrimStart('v', 'V');

            if (Version.TryParse(latestVer, out var vLatest) && Version.TryParse(currVer, out var vCurrent))
            {
                if (vLatest <= vCurrent)
                {
                    core.Logger.LogInformationIfEnabled("[CS2Admin] You are running the latest version: {Version}", currVer);
                    return;
                }
            }
            else if (latestVer == currVer)
            {
                core.Logger.LogInformationIfEnabled("[CS2Admin] You are running the latest version: {Version}", currVer);
                return;
            }

            core.Logger.LogInformationIfEnabled("[CS2Admin] New version available! {OldVersion} -> {NewVersion}. Downloading...", currVer, latestVer);

            var asset = release.Assets?.FirstOrDefault(a => a.BrowserDownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (asset == null || string.IsNullOrEmpty(asset.BrowserDownloadUrl))
            {
                core.Logger.LogWarningIfEnabled("[CS2Admin] Could not find a .zip asset in the latest release.");
                return;
            }

            await ApplyUpdateAsync(core, asset.BrowserDownloadUrl);
        }
        catch (Exception ex)
        {
            core.Logger.LogWarningIfEnabled("[CS2Admin] Auto-Updater encountered an error: {Message}", ex.Message);
        }
    }

    private static async Task ApplyUpdateAsync(ISwiftlyCore core, string downloadUrl)
    {
        var parentDir = Directory.GetParent(core.PluginPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName ?? core.PluginPath;
        var tempZipPath = Path.Combine(parentDir, "cs2admin_update_temp.zip");
        var tempExtractPath = Path.Combine(parentDir, "cs2admin_update_extracted");

        try
        {
            // Download the zip
            var zipBytes = await HttpClient.GetByteArrayAsync(downloadUrl);
            await File.WriteAllBytesAsync(tempZipPath, zipBytes);

            // Clean existing extract dir if any
            if (Directory.Exists(tempExtractPath))
            {
                Directory.Delete(tempExtractPath, true);
            }

            ZipFile.ExtractToDirectory(tempZipPath, tempExtractPath, overwriteFiles: true);

            // Find the actual plugin files inside the zip (in case it is wrapped in an inner folder)
            // We search for the dll file.
            var dllPath = Directory.GetFiles(tempExtractPath, "CS2_Admin.dll", SearchOption.AllDirectories).FirstOrDefault();
            
            if (dllPath == null)
            {
                core.Logger.LogWarningIfEnabled("[CS2Admin] Downloaded update is invalid (CS2_Admin.dll not found).");
                return;
            }

            var sourcePluginDir = Path.GetDirectoryName(dllPath);
            if (sourcePluginDir == null) return;

            // Copy all files from the source directory to the actual plugin path
            CopyDirectory(sourcePluginDir, core.PluginPath);

            core.Logger.LogInformationIfEnabled("[CS2Admin] Update applied successfully! Swiftly will reload the plugin automatically.");
        }
        finally
        {
            // Clean up temporary files
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
            if (Directory.Exists(tempExtractPath)) Directory.Delete(tempExtractPath, true);
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(targetDir, fileName);
            File.Copy(file, destFile, true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(dir);
            CopyDirectory(dir, Path.Combine(targetDir, dirName));
        }
    }

    private class GithubRelease
    {
        public string TagName { get; set; } = string.Empty;
        public GithubAsset[] Assets { get; set; } = Array.Empty<GithubAsset>();
    }

    private class GithubAsset
    {
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
