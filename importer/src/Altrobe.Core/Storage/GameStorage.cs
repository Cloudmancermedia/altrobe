using Altrobe.Core.Install;
using TACTSharp;

namespace Altrobe.Core.Storage;

public interface IGameFiles
{
    // Throws FileNotFoundException when the file is not in the local install.
    byte[] Open(uint fileDataId);
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
