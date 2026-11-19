using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace DesktopDrawer.Bootstrap;

/// <summary>
/// Online installer for Desktop Drawer. Resolves the latest stable GitHub
/// release, downloads the matching setup, verifies its published SHA-256 and
/// runs it. Used as the "always installs the newest version" entry point; the
/// versioned setup itself still works offline.
/// </summary>
internal static class Program
{
    private const string Owner = "BEGINWITHF";
    private const string Repository = "Desktop-Drawer";
    private const string StableSetupName = "DesktopDrawer-Setup.exe";

    private static readonly Uri LatestReleaseUri =
        new($"https://api.github.com/repos/{Owner}/{Repository}/releases/latest");

    private static async Task<int> Main(string[] args)
    {
        bool silent = args.Contains("--silent", StringComparer.OrdinalIgnoreCase);
        bool checkOnly = args.Contains("--check", StringComparer.OrdinalIgnoreCase);
        bool allowPrerelease = args.Contains("--pre", StringComparer.OrdinalIgnoreCase);

        try
        {
            using HttpClient client = CreateClient();

            ReleaseInfo? release = await FindLatestAsync(client, allowPrerelease);
            if (release is null)
            {
                Console.Error.WriteLine("No stable release is available yet.");
                return 2;
            }

            Console.WriteLine($"Latest version: {release.Version}");

            if (checkOnly)
            {
                return 0;
            }

            string setupPath = Path.Combine(
                Path.GetTempPath(),
                $"DesktopDrawer-Setup-{release.Version}.exe");

            Console.WriteLine("Downloading ...");
            await DownloadAsync(client, release.SetupUrl, setupPath);
            await VerifyAsync(client, release, setupPath);

            Console.WriteLine("Starting the installer ...");
            return Install(setupPath, silent);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Install failed: {ex.Message}");
            return 1;
        }
    }

    private sealed record ReleaseInfo(string Version, string SetupUrl, string? ChecksumUrl);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopDrawer-Installer/1.0");
        return client;
    }

    private static async Task<ReleaseInfo?> FindLatestAsync(HttpClient client, bool allowPrerelease)
    {
        using HttpResponseMessage response = await client.GetAsync(LatestReleaseUri);

        // No stable release yet; prereleases are intentionally not offered.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = document.RootElement;

        string version = root.TryGetProperty("tag_name", out JsonElement tag)
            ? (tag.GetString() ?? string.Empty).TrimStart('v', 'V')
            : string.Empty;

        bool prerelease = root.TryGetProperty("prerelease", out JsonElement pre) && pre.ValueKind == JsonValueKind.True;

        if (prerelease && !allowPrerelease)
        {
            Console.Error.WriteLine($"The newest release ({version}) is a prerelease; pass --pre to install it.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("assets", out JsonElement assetArray) &&
            assetArray.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement asset in assetArray.EnumerateArray())
            {
                string? name = asset.TryGetProperty("name", out JsonElement nameElement)
                    ? nameElement.GetString()
                    : null;
                string? url = asset.TryGetProperty("browser_download_url", out JsonElement urlElement)
                    ? urlElement.GetString()
                    : null;

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

        assets.TryGetValue(setupName + ".sha256", out string? checksumUrl);

        return new ReleaseInfo(version, assets[setupName], checksumUrl);
    }

    private static async Task DownloadAsync(HttpClient client, string url, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead);

        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;

        await using Stream input = await response.Content.ReadAsStreamAsync();
        await using FileStream output = File.Create(path);

        byte[] buffer = new byte[81920];
        long written = 0;
        int read;

        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read));
            written += read;

            if (total is > 0)
            {
                Console.Write($"\r{written * 100 / total.Value}%");
            }
        }

        Console.WriteLine();
    }

    private static async Task VerifyAsync(HttpClient client, ReleaseInfo release, string path)
    {
        if (release.ChecksumUrl is null)
        {
            Console.WriteLine("No checksum published; skipping verification.");
            return;
        }

        string text = await client.GetStringAsync(release.ChecksumUrl);
        string expected = text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

        await using FileStream stream = File.OpenRead(path);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();

        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Downloaded installer checksum does not match.");
        }

        Console.WriteLine("Checksum verified.");
    }

    private static int Install(string path, bool silent)
    {
        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            Arguments = silent
                ? "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS"
                : string.Empty
        };

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the installer.");

        process.WaitForExit();
        return process.ExitCode;
    }
}
