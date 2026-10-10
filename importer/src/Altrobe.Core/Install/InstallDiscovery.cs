namespace Altrobe.Core.Install;

public sealed record WowProduct(
    string Product,
    string Version,
    string? BuildName,
    bool Active,
    string BuildConfig,
    string CdnConfig,
    string CdnPath,
    string? Folder)
{
    // Forever has no product code of its own yet (the beta ships as wow_classic_beta, and launch is
    // expected to add a new one), so it is recognized by the build name in the local build config.
    public bool IsForever => LooksLikeForever(BuildName);

    public static bool LooksLikeForever(string? buildName) =>
        buildName != null && buildName.Contains("Forever", StringComparison.OrdinalIgnoreCase);
}

public sealed record WowInstall(string Path, IReadOnlyList<WowProduct> Products);

public static class InstallDiscovery
{
    public const string PathOverrideVariable = "ALTROBE_WOW_PATH";

    public static IReadOnlyList<string> CandidatePaths(string? overridePath, bool isWindows, bool isMac)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(overridePath)) paths.Add(overridePath);
        if (isMac) paths.Add("/Applications/World of Warcraft");
        if (isWindows)
        {
            paths.Add(@"C:\Program Files (x86)\World of Warcraft");
            paths.Add(@"C:\Program Files\World of Warcraft");
        }
        return paths;
    }

    public static IReadOnlyList<string> DefaultCandidatePaths() => CandidatePaths(
        Environment.GetEnvironmentVariable(PathOverrideVariable), OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());

    public static IReadOnlyList<WowInstall> Discover() => Discover(DefaultCandidatePaths());

    public static IReadOnlyList<WowInstall> Discover(IEnumerable<string> candidates)
    {
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var found = new List<WowInstall>();
        foreach (var candidate in candidates)
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            if (!seen.Add(full)) continue;
            if (TryRead(full) is { } install) found.Add(install);
        }
        return found;
    }

    public static WowInstall? TryRead(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var buildInfo = Path.Combine(full, ".build.info");
        if (!File.Exists(buildInfo)) return null;

        var folders = FlavorFolders(full);
        var products = BuildInfoFile.Parse(File.ReadAllText(buildInfo))
            .Where(r => !string.IsNullOrEmpty(r.GetValueOrDefault("Product")))
            .Select(r =>
            {
                var product = r["Product"];
                var buildKey = r.GetValueOrDefault("Build Key") ?? "";
                return new WowProduct(
                    product,
                    r.GetValueOrDefault("Version") ?? "",
                    ReadBuildName(full, buildKey),
                    r.GetValueOrDefault("Active") == "1",
                    buildKey,
                    r.GetValueOrDefault("CDN Key") ?? "",
                    r.GetValueOrDefault("CDN Path") ?? "",
                    folders.GetValueOrDefault(product));
            })
            .ToList();
        return new WowInstall(full, products);
    }

    // Each game flavor lives in a folder like _retail_ or _classic_beta_ that holds a .flavor.info
    // naming its product. Reading it avoids guessing folder names for products we have not seen.
    static Dictionary<string, string> FlavorFolders(string installDir)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.EnumerateDirectories(installDir))
        {
            var flavor = Path.Combine(dir, ".flavor.info");
            if (!File.Exists(flavor)) continue;
            foreach (var row in BuildInfoFile.Parse(File.ReadAllText(flavor)))
                if (row.GetValueOrDefault("Product Flavor") is { Length: > 0 } product)
                    map.TryAdd(product, Path.GetFileName(dir));
        }
        return map;
    }

    static string ConfigPath(string installDir, string key) => Path.Combine(installDir, "Data", "config", key[..2], key[2..4], key);

    // The build and CDN config files .build.info names that are not on disk. Battle.net rewrites
    // .build.info before it downloads the new configs, so a non-empty list means an update is under way.
    public static IReadOnlyList<string> MissingConfigs(WowInstall install, WowProduct product) =>
        new[] { product.BuildConfig, product.CdnConfig }
            .Where(k => k.Length >= 4)
            .Select(k => ConfigPath(install.Path, k))
            .Where(p => !File.Exists(p))
            .ToList();

    static string? ReadBuildName(string installDir, string buildKey)
    {
        if (buildKey.Length < 4) return null;
        var path = ConfigPath(installDir, buildKey);
        if (!File.Exists(path)) return null;
        foreach (var line in File.ReadLines(path))
        {
            var eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim() == "build-name") return line[(eq + 1)..].Trim();
        }
        return null;
    }
}
