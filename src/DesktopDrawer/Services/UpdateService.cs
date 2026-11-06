using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopDrawer.Helpers;

namespace DesktopDrawer.Services;

internal enum UpdateCheckStatus
{
    UpToDate,
    NoPackage,
    Available
}

internal sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdatePackage? Package);

internal sealed record UpdatePackage(Version Version, string DownloadUrl, string? ChecksumUrl);

internal sealed record StagedUpdate(string StageDirectory, string RootDirectory, Version Version);

/// <summary>
/// Checks the public GitHub releases for a newer build, downloads it and stages
/// it for the helper process that replaces the running installation.
/// </summary>
internal static class UpdateService
{
    private const string Owner = "BEGINWITHF";
    private const string Repository = "Desktop-Drawer";
    private const string AssetName = "DesktopDrawer-win-x64.zip";
    private const string UserAgent = "DesktopDrawer-Updater/1.0";

    private static readonly Uri ReleasesUri =
        new($"https://api.github.com/repos/{Owner}/{Repository}/releases/latest");

    public static Version CurrentVersion { get; } = ReadCurrentVersion();

    public static string CurrentVersionText => Normalize(CurrentVersion).ToString(3);

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using HttpClient client = CreateClient(TimeSpan.FromSeconds(20));
        using HttpResponseMessage response = await client.GetAsync(ReleasesUri, cancellationToken);

        // A repository without releases has nothing to offer yet.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate, null);
        }

        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));

        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("tag_name", out JsonElement tagElement) ||
            !TryParseVersion(tagElement.GetString(), out Version latest))
        {
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate, null);
        }

        if (Normalize(latest) <= Normalize(CurrentVersion))
        {
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate, null);
        }

        string? downloadUrl = null;
        string? checksumUrl = null;
        string checksumName = AssetName + ".sha256";

        if (root.TryGetProperty("assets", out JsonElement assets) &&
            assets.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string? name = asset.TryGetProperty("name", out JsonElement nameElement)
                    ? nameElement.GetString()
                    : null;
                string? url = asset.TryGetProperty("browser_download_url", out JsonElement urlElement)
                    ? urlElement.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                if (name.Equals(AssetName, StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = url;
                }
                else if (name.Equals(checksumName, StringComparison.OrdinalIgnoreCase))
                {
                    checksumUrl = url;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return new UpdateCheckResult(UpdateCheckStatus.NoPackage, null);
        }

        return new UpdateCheckResult(
            UpdateCheckStatus.Available,
            new UpdatePackage(Normalize(latest), downloadUrl, checksumUrl));
    }

    public static async Task<StagedUpdate> DownloadAndStageAsync(
        UpdatePackage package,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "DesktopDrawer",
            "update-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);
        string zipPath = Path.Combine(root, AssetName);

        try
        {
            using (HttpClient client = CreateClient(TimeSpan.FromMinutes(10)))
            using (HttpResponseMessage response = await client.GetAsync(
                       package.DownloadUrl,
                       HttpCompletionOption.ResponseHeadersRead,
                       cancellationToken))
            {
                response.EnsureSuccessStatusCode();

                long? total = response.Content.Headers.ContentLength;
                await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using FileStream destination = new(
                    zipPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);

                byte[] buffer = new byte[81920];
                long received = 0;
                int read;

                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;

                    if (total is > 0)
                    {
                        progress?.Report(Math.Clamp((double)received / total.Value, 0, 1));
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(package.ChecksumUrl))
            {
                await VerifyChecksumAsync(package.ChecksumUrl, zipPath, cancellationToken);
            }

            string stage = Path.Combine(root, "stage");
            Directory.CreateDirectory(stage);
            ZipFile.ExtractToDirectory(zipPath, stage, overwriteFiles: true);

            if (!File.Exists(Path.Combine(stage, "DesktopDrawer.exe")))
            {
                throw new InvalidDataException("The update package does not contain DesktopDrawer.exe.");
            }

            progress?.Report(1);
            return new StagedUpdate(stage, root, package.Version);
        }
        catch
        {
            TryDelete(root);
            throw;
        }
    }

    public static void LaunchInstaller(StagedUpdate update)
    {
        string scriptPath = Path.Combine(update.RootDirectory, "apply-update.ps1");
        File.WriteAllText(scriptPath, HelperScript, new UTF8Encoding(false));

        string installDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string executable = Path.Combine(installDirectory, "DesktopDrawer.exe");
        string logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesktopDrawer",
            "update.log");

        string powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        if (!File.Exists(powershell))
        {
            powershell = "powershell.exe";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = powershell,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = update.RootDirectory
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-AppPid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-Stage");
        startInfo.ArgumentList.Add(update.StageDirectory);
        startInfo.ArgumentList.Add("-InstallDir");
        startInfo.ArgumentList.Add(installDirectory);
        startInfo.ArgumentList.Add("-ExePath");
        startInfo.ArgumentList.Add(executable);
        startInfo.ArgumentList.Add("-LogPath");
        startInfo.ArgumentList.Add(logPath);

        Process.Start(startInfo);
    }

    private static async Task VerifyChecksumAsync(
        string checksumUrl,
        string zipPath,
        CancellationToken cancellationToken)
    {
        using HttpClient client = CreateClient(TimeSpan.FromSeconds(20));
        string text = await client.GetStringAsync(checksumUrl, cancellationToken);
        string expected = text
            .Trim()
            .Split(new[] { ' ', '\t', '*' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

        if (expected.Length != 64)
        {
            throw new InvalidDataException("The update checksum is invalid.");
        }

        await using FileStream stream = File.OpenRead(zipPath);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        string actual = Convert.ToHexString(hash);

        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The update checksum does not match.");
        }
    }

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static Version ReadCurrentVersion()
    {
        Version? version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? new Version(1, 0, 0) : Normalize(version);
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(1, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text.Trim().TrimStart('v', 'V');
        int suffix = value.IndexOfAny(new[] { '-', '+' });
        if (suffix >= 0)
        {
            value = value[..suffix];
        }

        if (!Version.TryParse(value, out Version? parsed))
        {
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // The helper process mirrors this cleanup; a leftover temp folder is harmless.
        }
    }

    private const string HelperScript = """
        param(
            [int]$AppPid,
            [string]$Stage,
            [string]$InstallDir,
            [string]$ExePath,
            [string]$LogPath
        )

        function Write-UpdateLog {
            param([string]$Message)
            try {
                $directory = Split-Path -Parent $LogPath
                if ($directory -and -not (Test-Path $directory)) {
                    New-Item -ItemType Directory -Path $directory -Force | Out-Null
                }
                Add-Content -Path $LogPath -Value ("[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message)
            } catch { }
        }

        Write-UpdateLog "Update helper started for process $AppPid."

        try {
            $process = Get-Process -Id $AppPid -ErrorAction SilentlyContinue
            if ($process) {
                $process.WaitForExit(60000) | Out-Null
            }

            $deadline = (Get-Date).AddSeconds(60)
            while ((Get-Process -Name 'DesktopDrawer.Watchdog' -ErrorAction SilentlyContinue) -and
                   ((Get-Date) -lt $deadline)) {
                Start-Sleep -Milliseconds 300
            }

            $copied = $false
            for ($attempt = 1; $attempt -le 30; $attempt++) {
                try {
                    Copy-Item -Path (Join-Path $Stage '*') -Destination $InstallDir -Recurse -Force -ErrorAction Stop
                    $copied = $true
                    break
                } catch {
                    Write-UpdateLog "Copy attempt $attempt failed: $($_.Exception.Message)"
                    Start-Sleep -Seconds 1
                }
            }

            if (-not $copied) {
                throw "Could not copy the update files into $InstallDir."
            }

            Write-UpdateLog "Update copied. Starting $ExePath."
            Start-Process -FilePath $ExePath -WorkingDirectory $InstallDir
        } catch {
            Write-UpdateLog "Update failed: $($_.Exception.Message)"
        } finally {
            try {
                Remove-Item -Path (Split-Path -Parent $Stage) -Recurse -Force -ErrorAction SilentlyContinue
            } catch { }
        }
        """;
}
