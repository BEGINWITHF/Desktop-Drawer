using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace DesktopDrawer.Bootstrap;

internal sealed record ReleaseInfo(string Version, string SetupUrl, string? ChecksumUrl);

internal static class InstallerCore
{
    private const string StableSetupName = "DesktopDrawer-Setup.exe";

    private static readonly Uri LatestReleaseUri =
        new("https://api.github.com/repos/BEGINWITHF/Desktop-Drawer/releases?per_page=30");

    public static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopDrawer-Installer/1.0");
        return client;
    }

    public static async Task<ReleaseInfo?> FindLatestAsync(HttpClient client, bool allowPrerelease)
    {
        using HttpResponseMessage response = await client.GetAsync(LatestReleaseUri);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = document.RootElement.EnumerateArray().FirstOrDefault(release =>
            (release.TryGetProperty("tag_name", out JsonElement name) ? name.GetString() : null) is string tag &&
            tag.StartsWith("app-v", StringComparison.OrdinalIgnoreCase) &&
            !(release.TryGetProperty("prerelease", out JsonElement pre) && pre.ValueKind == JsonValueKind.True));

        if (root.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        string version = root.TryGetProperty("tag_name", out JsonElement tag)
            ? (tag.GetString() ?? string.Empty)
            : string.Empty;

        if (version.StartsWith("app-", StringComparison.OrdinalIgnoreCase))
        {
            version = version[4..];
        }

        version = version.TrimStart('v', 'V');

        bool prerelease = root.TryGetProperty("prerelease", out JsonElement pre) &&
                          pre.ValueKind == JsonValueKind.True;

        if (string.IsNullOrWhiteSpace(version) || (prerelease && !allowPrerelease))
        {
            return null;
        }

        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("assets", out JsonElement assetArray) &&
            assetArray.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement asset in assetArray.EnumerateArray())
            {
                string? name = asset.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
                string? url = asset.TryGetProperty("browser_download_url", out JsonElement u) ? u.GetString() : null;

                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
                {
                    assets[name] = url;
                }
            }
        }

        string? setupName = assets.ContainsKey(StableSetupName)
            ? StableSetupName
            : assets.Keys.FirstOrDefault(name =>
                name.StartsWith("DesktopDrawer-Setup-", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        if (setupName is null)
        {
            return null;
        }

        assets.TryGetValue(setupName + ".sha256", out string? checksum);

        return new ReleaseInfo(version, assets[setupName], checksum);
    }

    public static string SetupPath(string version) =>
        Path.Combine(Path.GetTempPath(), $"DesktopDrawer-Setup-{version}.exe");

    public static async Task DownloadAsync(
        HttpClient client,
        string url,
        string path,
        IProgress<double>? progress,
        CancellationToken token = default)
    {
        using HttpResponseMessage response = await client.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            token);

        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;

        await using Stream input = await response.Content.ReadAsStreamAsync(token);
        await using FileStream output = File.Create(path);

        byte[] buffer = new byte[81920];
        long written = 0;
        int read;

        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            written += read;

            if (total is > 0)
            {
                progress?.Report((double)written / total.Value);
            }
        }
    }

    public static async Task VerifyAsync(HttpClient client, ReleaseInfo release, string path)
    {
        if (release.ChecksumUrl is null)
        {
            return;
        }

        string text = await client.GetStringAsync(release.ChecksumUrl);
        string expected = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

        await using FileStream stream = File.OpenRead(path);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();

        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The downloaded installer failed its checksum.");
        }
    }

    public static Process StartSetup(string path, bool silent)
    {
        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            Arguments = silent
                ? "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS"
                : string.Empty
        };

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the installer.");
    }
}

