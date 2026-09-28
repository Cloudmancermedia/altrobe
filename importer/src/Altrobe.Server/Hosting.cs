using System.Diagnostics;
using Altrobe.Core.Builds;
using Altrobe.Core.Cache;
using Altrobe.Core.Install;

namespace Altrobe.Server;

public sealed class ServerSettings
{
    public const int DefaultPort = 5161;

    public int Port { get; init; } = DefaultPort;
    public string? WebRoot { get; init; }
    public bool OpenBrowser { get; init; } = true;
    public string CacheRoot { get; init; } = AppPaths.CacheRoot();

    // Flags: --port <n>, --web-root <dir>, --no-browser. Environment: ALTROBE_PORT,
    // ALTROBE_WEB_ROOT, ALTROBE_NO_BROWSER=1. Flags win.
    public static ServerSettings FromArgs(string[] args)
    {
        string? Arg(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        var port = Arg("--port") ?? Environment.GetEnvironmentVariable("ALTROBE_PORT");
        return new ServerSettings
        {
            Port = int.TryParse(port, out var p) && p is > 0 and < 65536 ? p : DefaultPort,
            WebRoot = Arg("--web-root") ?? Environment.GetEnvironmentVariable("ALTROBE_WEB_ROOT") ?? FindWebRoot(),
            OpenBrowser = !args.Contains("--no-browser") && Environment.GetEnvironmentVariable("ALTROBE_NO_BROWSER") != "1",
        };
    }

    // A wwwroot next to the binary (a published app), else web/dist in the repo this binary was
    // built from (development). Null when neither exists yet.
    static string? FindWebRoot()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (Directory.Exists(beside)) return beside;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var dist = Path.Combine(dir.FullName, "web", "dist");
            if (Directory.Exists(Path.Combine(dir.FullName, "importer")) && Directory.Exists(dist)) return dist;
        }
        return null;
    }
}

public interface IInstallSource
{
    IReadOnlyList<WowInstall> Discover();
}

public interface IBuildSessionFactory
{
    BuildSession Open(WowInstall install, WowProduct product, string cacheRoot);
}

public interface IBrowserLauncher
{
    void Open(string url);
}

sealed class DiscoveredInstalls : IInstallSource
{
    public IReadOnlyList<WowInstall> Discover() => InstallDiscovery.Discover();
}

sealed class LocalBuildSessions : IBuildSessionFactory
{
    public BuildSession Open(WowInstall install, WowProduct product, string cacheRoot) => BuildSession.Open(install, product, cacheRoot);
}

sealed class SystemBrowser(ILogger<SystemBrowser> log) : IBrowserLauncher
{
    public void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) { log.LogWarning("Could not open a browser ({Message}). Open {Url} yourself.", e.Message, url); }
    }
}

public sealed record Selection(WowInstall Install, WowProduct Product, BuildSession Session);

// The only server-side state: which install/product is selected. Looks live in the browser.
public sealed class AppState
{
    volatile Selection? _current;

    public Selection? Current => _current;

    public void Select(Selection selection) => _current = selection;
}
