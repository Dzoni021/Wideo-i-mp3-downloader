using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OmniFetch;

/// <summary>
/// Updates the application and its two external tools from their HTTPS release
/// pages. Tool downloads are verified against the upstream SHA-256 manifest
/// whenever one is published.
/// </summary>
internal static class AppUpdater
{
    private const string ApplicationRepository = "Dzoni021/Wideo-i-mp3-downloader";
    private const string YtDlpRepository = "yt-dlp/yt-dlp";
    private const string FfmpegX64Repository = "BtbN/FFmpeg-Builds";
    private const string FfmpegX86Repository = "defisym/FFmpeg-Builds-Win32";
    private static readonly HttpClient Http = CreateClient();

    private sealed record ReleaseInfo(string Tag, JsonElement Root);
    private sealed record ToolPlan(string YtDlpAsset, string FfmpegRepository, string FfmpegAsset);
    internal sealed record ApplicationUpdate(string Tag, string DownloadUrl);
    internal sealed record ToolUpdateResult(bool YtDlpUpdated, bool FfmpegUpdated);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OmniFetch", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    internal static async Task<ToolUpdateResult> UpdateToolsAsync(Action<string> report, CancellationToken token)
    {
        ToolPlan plan = GetToolPlan();
        ReleaseInfo ytRelease = await GetReleaseAsync(YtDlpRepository, token);
        ReleaseInfo ffmpegRelease = await GetReleaseAsync(plan.FfmpegRepository, token);
        ToolManifest local = ReadManifest();
        bool ytChanged = false;
        bool ffmpegChanged = false;

        string? ytUrl = FindAssetUrl(ytRelease.Root, plan.YtDlpAsset);
        if (ytUrl is not null &&
            (!string.Equals(local.YtDlpVersion, ytRelease.Tag, StringComparison.OrdinalIgnoreCase) ||
             !File.Exists(Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe"))))
        {
            report($"Ažuriram yt-dlp na verziju {ytRelease.Tag}…");
            string downloaded = await DownloadAssetAsync(ytUrl, plan.YtDlpAsset, token);
            await VerifyAssetAsync(ytRelease.Root, plan.YtDlpAsset, downloaded, token);
            ReplaceFile(downloaded, Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe"));
            ytChanged = true;
        }

        string? ffmpegUrl = FindAssetUrl(ffmpegRelease.Root, plan.FfmpegAsset);
        if (ffmpegUrl is not null &&
            (!string.Equals(local.FfmpegVersion, ffmpegRelease.Tag, StringComparison.OrdinalIgnoreCase) ||
             !File.Exists(Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe")) ||
             !File.Exists(Path.Combine(AppContext.BaseDirectory, "ffprobe.exe"))))
        {
            report($"Ažuriram FFmpeg na verziju {ffmpegRelease.Tag}…");
            string archive = await DownloadAssetAsync(ffmpegUrl, plan.FfmpegAsset, token);
            await VerifyAssetAsync(ffmpegRelease.Root, plan.FfmpegAsset, archive, token);
            ExtractFfmpeg(archive);
            ffmpegChanged = true;
        }

        if (ytChanged || ffmpegChanged || !File.Exists(Path.Combine(AppContext.BaseDirectory, "tools.json")))
        {
            var manifest = new Dictionary<string, string>
            {
                ["ytDlpVersion"] = ytRelease.Tag,
                ["ffmpegVersion"] = ffmpegRelease.Tag,
                ["updatedAtUtc"] = DateTime.UtcNow.ToString("O"),
            };
            await File.WriteAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "tools.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
                token);
        }

        return new ToolUpdateResult(ytChanged, ffmpegChanged);
    }

    internal static async Task<ApplicationUpdate?> CheckApplicationUpdateAsync(CancellationToken token)
    {
        ReleaseInfo release;
        try
        {
            release = await GetReleaseAsync(ApplicationRepository, token);
        }
        catch (HttpRequestException)
        {
            return null;
        }

        Version latest = ParseVersion(release.Tag);
        Version current = ParseVersion(Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0");
        if (latest <= current)
            return null;

        string runtime = GetRuntimeName();
        string assetName = $"OmniFetch-{runtime}.zip";
        string? url = FindAssetUrl(release.Root, assetName);
        return url is null ? null : new ApplicationUpdate(release.Tag, url);
    }

    internal static async Task InstallApplicationUpdateAsync(ApplicationUpdate update, CancellationToken token)
    {
        string archive = await DownloadAssetAsync(update.DownloadUrl, $"OmniFetch-{GetRuntimeName()}.zip", token);
        string extractionDirectory = Path.Combine(Path.GetTempPath(), "OmniFetch-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractionDirectory);
        ZipFile.ExtractToDirectory(archive, extractionDirectory);
        string script = Path.Combine(Path.GetTempPath(), "OmniFetch-update-" + Guid.NewGuid().ToString("N") + ".ps1");
        string source = extractionDirectory.Replace("'", "''");
        string target = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar).Replace("'", "''");
        string executable = Path.Combine(AppContext.BaseDirectory, "OmniFetch.exe").Replace("'", "''");
        string scriptText =
            "$ErrorActionPreference = 'Stop'\n" +
            "$processId = " + Environment.ProcessId + "\n" +
            "while (Get-Process -Id $processId -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }\n" +
            $"Copy-Item -Path '{source}\\*' -Destination '{target}' -Recurse -Force\n" +
            $"Start-Process -FilePath '{executable}'\n" +
            $"Remove-Item -Path '{source}' -Recurse -Force -ErrorAction SilentlyContinue\n" +
            "Remove-Item -Path $PSCommandPath -Force -ErrorAction SilentlyContinue\n";
        await File.WriteAllTextAsync(script, scriptText, token);
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        Process.Start(start);
    }

    private static async Task<ReleaseInfo> GetReleaseAsync(string repository, CancellationToken token)
    {
        using HttpResponseMessage response = await Http.GetAsync(
            $"https://api.github.com/repos/{repository}/releases/latest", token);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
        JsonElement root = document.RootElement.Clone();
        return new ReleaseInfo(root.GetProperty("tag_name").GetString() ?? "", root);
    }

    private static async Task<string> DownloadAssetAsync(string url, string fileName, CancellationToken token)
    {
        string destination = Path.Combine(Path.GetTempPath(), "OmniFetch-" + Guid.NewGuid().ToString("N") + "-" + fileName);
        using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using Stream input = await response.Content.ReadAsStreamAsync(token);
        await using FileStream output = File.Create(destination);
        await input.CopyToAsync(output, token);
        return destination;
    }

    private static async Task VerifyAssetAsync(JsonElement release, string assetName, string filePath, CancellationToken token)
    {
        JsonElement? asset = FindAsset(release, assetName);
        string? digest = asset?.TryGetProperty("digest", out JsonElement digestValue) == true
            ? digestValue.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(digest))
        {
            string? sumsUrl = FindAssetUrl(release, "SHA2-256SUMS") ?? FindAssetUrl(release, "checksums.sha256");
            if (sumsUrl is not null)
            {
                string sumsPath = await DownloadAssetAsync(sumsUrl, "checksums.txt", token);
                string sums = await File.ReadAllTextAsync(sumsPath, token);
                Match match = Regex.Match(sums, $"(?im)^(?<hash>[0-9a-f]{{64}})\\s+[* ]?{Regex.Escape(assetName)}\\s*$");
                if (match.Success)
                    digest = "sha256:" + match.Groups["hash"].Value;
                TryDelete(sumsPath);
            }
        }
        if (string.IsNullOrWhiteSpace(digest))
            return;
        string expected = digest.Split(':', 2).Last().Trim().ToLowerInvariant();
        using SHA256 sha = SHA256.Create();
        await using FileStream stream = File.OpenRead(filePath);
        string actual = Convert.ToHexString(await sha.ComputeHashAsync(stream, token)).ToLowerInvariant();
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Provera integriteta fajla {assetName} nije uspela.");
    }

    private static void ExtractFfmpeg(string archive)
    {
        string extraction = Path.Combine(Path.GetTempPath(), "OmniFetch-ffmpeg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extraction);
        try
        {
            ZipFile.ExtractToDirectory(archive, extraction);
            foreach (string name in new[] { "ffmpeg.exe", "ffprobe.exe" })
            {
                string? source = Directory.GetFiles(extraction, name, SearchOption.AllDirectories).FirstOrDefault();
                if (source is null)
                    throw new InvalidDataException($"FFmpeg paket ne sadrži {name}.");
                ReplaceFile(source, Path.Combine(AppContext.BaseDirectory, name));
            }
        }
        finally
        {
            TryDelete(archive);
            try { Directory.Delete(extraction, true); } catch { }
        }
    }

    private static JsonElement? FindAsset(JsonElement release, string name)
    {
        foreach (JsonElement asset in release.GetProperty("assets").EnumerateArray())
        {
            if (string.Equals(asset.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
                return asset;
        }
        return null;
    }

    private static string? FindAssetUrl(JsonElement release, string name) =>
        FindAsset(release, name)?.GetProperty("browser_download_url").GetString();

    private static ToolPlan GetToolPlan()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => new ToolPlan("yt-dlp_x86.exe", FfmpegX86Repository, "ffmpeg-master-latest-win32-gpl.zip"),
            Architecture.Arm64 => new ToolPlan("yt-dlp_arm64.exe", FfmpegX64Repository, "ffmpeg-master-latest-winarm64-gpl.zip"),
            _ => new ToolPlan("yt-dlp.exe", FfmpegX64Repository, "ffmpeg-master-latest-win64-gpl.zip"),
        };
    }

    private static string GetRuntimeName() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => "win-x86",
        Architecture.Arm64 => "win-arm64",
        _ => "win-x64",
    };

    private static Version ParseVersion(string value)
    {
        string cleaned = value.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(cleaned, out Version? version) ? version : new Version(0, 0, 0);
    }

    private static ToolManifest ReadManifest()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "tools.json");
            if (File.Exists(path))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                return new ToolManifest(
                    document.RootElement.TryGetProperty("ytDlpVersion", out JsonElement yt) ? yt.GetString() : null,
                    document.RootElement.TryGetProperty("ffmpegVersion", out JsonElement ffmpeg) ? ffmpeg.GetString() : null);
            }
        }
        catch (JsonException) { }
        return new ToolManifest(null, null);
    }

    private sealed record ToolManifest(string? YtDlpVersion, string? FfmpegVersion);

    private static void ReplaceFile(string source, string target)
    {
        string backup = target + ".old";
        TryDelete(backup);
        if (File.Exists(target))
            File.Move(target, backup);
        try
        {
            File.Move(source, target, true);
            TryDelete(backup);
        }
        catch
        {
            if (File.Exists(backup)) File.Move(backup, target, true);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
