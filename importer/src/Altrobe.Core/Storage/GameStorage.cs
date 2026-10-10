using Altrobe.Core.Install;
using TACTSharp;

namespace Altrobe.Core.Storage;

public interface IGameFiles
{
    // Throws FileNotFoundException when the file is not in the local install.
    byte[] Open(uint fileDataId);

    // Whether the build lists the file at all.
    bool Exists(uint fileDataId);
}

// Local-only view of one product's CASC storage. Never downloads from Blizzard's CDN.
public sealed class GameStorage : IGameFiles
{
    readonly BuildInstance _build;
    readonly Lock _lock = new();

    GameStorage(BuildInstance build, string buildName)
    {
        _build = build;
        BuildName = buildName;
    }

    public string BuildName { get; }

    public static BuildInstance CreateOfflineBuild(string installDir, string product, string tactCacheDir)
    {
        Settings.LogLevel = TSLogLevel.Warn;
        var build = new BuildInstance();
        build.Settings.BaseDir = installDir;
        build.Settings.Product = product;
        build.Settings.TryCDN = false;
        build.Settings.ListfileFallback = false;
        build.Settings.CacheDir = tactCacheDir;
        // Full mode keeps every root entry per file. Normal mode keeps one, and for textures with an
        // optional HD version it keeps only the HD entry, which is on disk only with the HD pack.
        build.Settings.RootMode = RootInstance.LoadMode.Full;
        // TryCDN=false alone is not enough: for a file missing locally, TACTSharp first asks Blizzard's
        // version service for a CDN list and pings the servers, and only then checks TryCDN. A
        // non-empty placeholder list skips that lookup, so a missing file fails without network.
        build.cdn.SetCDNs(["no-cdn.invalid"]);
        return build;
    }

    // For the hosted pipeline only: reads every file from Blizzard's CDN, caching under tactCacheDir.
    // The local app never uses this.
    public static BuildInstance CreateCdnBuild(CdnSource source, string tactCacheDir)
    {
        Settings.LogLevel = TSLogLevel.Warn;
        var build = new BuildInstance();
        build.Settings.Product = source.Product;
        build.Settings.Region = source.Region;
        build.Settings.TryCDN = true;
        build.Settings.ListfileFallback = false;
        build.Settings.CacheDir = tactCacheDir;
        build.Settings.RootMode = RootInstance.LoadMode.Full;
        return build;
    }

    // NOT RUN by any test or by the local app: this downloads the build's indexes (tens of MB) and then
    // each file it is asked for. Only the hosted pipeline calls it. The indexes come first through
    // CdnIndexPrefetch, a few at a time, because TACTSharp requests them all at once and those requests stall.
    public static async Task<GameStorage> OpenCdnAsync(CdnSource source, string tactCacheDir, IReadOnlyList<string> hosts, HttpClient http, Action<string>? log = null)
    {
        Directory.CreateDirectory(tactCacheDir);
        var build = CreateCdnBuild(source, tactCacheDir);
        build.cdn.ProductDirectory = source.CdnPath;
        build.LoadConfigs(source.BuildConfig, source.CdnConfig);
        var archives = build.CDNConfig!.Values["archives"];
        var fetched = await CdnIndexPrefetch.RunAsync(http, hosts, source.CdnPath, archives, tactCacheDir);
        log?.Invoke($"{archives.Length} archive indexes ready ({fetched} downloaded).");
        build.Load();
        return new GameStorage(build, build.BuildConfig!.Values["build-name"][0]);
    }

    public static GameStorage Open(WowInstall install, WowProduct product, string tactCacheDir)
    {
        Directory.CreateDirectory(tactCacheDir);
        var build = CreateOfflineBuild(install.Path, product.Product, tactCacheDir);
        var buildInfo = new BuildInfo(Path.Combine(install.Path, ".build.info"), build.Settings, build.cdn);
        var entry = buildInfo.Entries.FirstOrDefault(e => e.Product == product.Product);
        if (entry.Product == null) throw new InvalidOperationException($"Product {product.Product} is not in {install.Path}/.build.info");
        build.cdn.ProductDirectory = entry.CDNPath;
        build.LoadConfigs(entry.BuildConfig, entry.CDNConfig);
        build.Load();
        return new GameStorage(build, build.BuildConfig!.Values["build-name"][0]);
    }

    // TACTSharp does not document thread safety, and conversions run on request threads.
    public byte[] Open(uint fileDataId)
    {
        lock (_lock) return LocalFiles.Open(_build, fileDataId).bytes;
    }

    public bool Exists(uint fileDataId)
    {
        lock (_lock) return _build.Root!.FileExists(fileDataId);
    }
}
