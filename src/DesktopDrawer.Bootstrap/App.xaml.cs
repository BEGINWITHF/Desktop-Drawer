using System.Net.Http;
using System.Windows;

namespace DesktopDrawer.Bootstrap;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Headless modes keep working for scripts and CI.
        if (e.Args.Any(arg => arg is "--check" or "--silent"))
        {
            int code = RunHeadless(e.Args).GetAwaiter().GetResult();
            Shutdown(code);
            return;
        }

        new MainWindow().Show();
    }

    private static async Task<int> RunHeadless(string[] args)
    {
        bool silent = args.Contains("--silent", StringComparer.OrdinalIgnoreCase);
        bool checkOnly = args.Contains("--check", StringComparer.OrdinalIgnoreCase);

        try
        {
            using HttpClient client = InstallerCore.CreateClient();
            ReleaseInfo? release = await InstallerCore.FindLatestAsync(client, allowPrerelease: false);

            if (release is null)
            {
                return 2;
            }

            Console.WriteLine($"Latest version: {release.Version}");

            if (checkOnly)
            {
                return 0;
            }

            string path = InstallerCore.SetupPath(release.Version);
            await InstallerCore.DownloadAsync(client, release.SetupUrl, path, null);
            await InstallerCore.VerifyAsync(client, release, path);

            using System.Diagnostics.Process setup = InstallerCore.StartSetup(path, silent);
            setup.WaitForExit();
            return setup.ExitCode;
        }
        catch
        {
            return 1;
        }
    }
}

