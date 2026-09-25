// Spike Step 1: open a local WoW install and read one DB2 table.
// Reads only local files. Never downloads game data from Blizzard's CDN.
//
// Usage: dotnet run -- [installDir] [product] [table]
// Defaults: "/Applications/World of Warcraft" wow_classic_beta ChrRaces

using System.Text.Json;
using DBCD;
using DBCD.IO;
using DBCD.Providers;
using TACTSharp;

var installDir = args.ElementAtOrDefault(0) ?? "/Applications/World of Warcraft";
var product = args.ElementAtOrDefault(1) ?? "wow_classic_beta";
var table = args.ElementAtOrDefault(2) ?? "ChrRaces";

// Everything this tool writes goes under output/, which .gitignore excludes.
var outputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../output"));
Directory.CreateDirectory(outputDir);

var build = new BuildInstance();
build.Settings.BaseDir = installDir;
build.Settings.Product = product;
build.Settings.TryCDN = false;
build.Settings.ListfileFallback = false;
build.Settings.CacheDir = Path.Combine(outputDir, "tact-cache");

var buildInfo = new BuildInfo(Path.Combine(installDir, ".build.info"), build.Settings, build.cdn);
var entry = buildInfo.Entries.FirstOrDefault(e => e.Product == product);
if (entry.Product == null)
{
    Console.Error.WriteLine($"Product {product} not found in .build.info. Found: {string.Join(", ", buildInfo.Entries.Select(e => e.Product))}");
    return 1;
}

Console.WriteLine($"Product: {entry.Product}  Version: {entry.Version}  Folder: {entry.Folder}");
build.cdn.ProductDirectory = entry.CDNPath;
build.LoadConfigs(entry.BuildConfig, entry.CDNConfig);
build.Load();
Console.WriteLine($"Build loaded: {build.BuildConfig!.Values["build-name"][0]}");

var manifest = await LoadManifest(outputDir);
var dbcd = new DBCD.DBCD(new InstallDBCProvider(build, manifest), new GithubDBDProvider(useCache: true));
var storage = dbcd.Load(table, entry.Version);
Console.WriteLine($"{table}: {storage.Count} rows, layout hash {storage.LayoutHash:X8}");

var hotfixPath = Path.Combine(installDir, entry.Folder ?? "", "Cache", "ADB", "enUS", "DBCache.bin");
if (File.Exists(hotfixPath))
{
    var hotfixes = new HotfixReader(hotfixPath);
    storage.ApplyingHotfixes(hotfixes);
    Console.WriteLine($"Hotfixes applied from {hotfixPath} (hotfix build {hotfixes.BuildId}); now {storage.Count} rows");
}
else
{
    Console.WriteLine($"No hotfix cache at {hotfixPath}");
}

Console.WriteLine($"Columns: {string.Join(", ", storage.AvailableColumns)}");
foreach (var id in storage.Keys.OrderBy(k => k).Take(15))
{
    var row = storage[id];
    var name = storage.AvailableColumns.Contains("Name_lang") ? row["Name_lang"] : "";
    var clientPrefix = storage.AvailableColumns.Contains("ClientPrefix") ? row["ClientPrefix"] : "";
    Console.WriteLine($"  {id,4}  {name}  {clientPrefix}");
}
return 0;

// WoWDBDefs manifest maps table names to their DB2 FileDataIDs. Cached under output/.
static async Task<Dictionary<string, uint>> LoadManifest(string outputDir)
{
    var path = Path.Combine(outputDir, "wowdbdefs-manifest.json");
    if (!File.Exists(path))
    {
        using var http = new HttpClient();
        await File.WriteAllTextAsync(path, await http.GetStringAsync("https://raw.githubusercontent.com/wowdev/WoWDBDefs/master/manifest.json"));
    }
    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
    var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
    foreach (var t in doc.RootElement.EnumerateArray())
        if (t.TryGetProperty("db2FileDataID", out var fdid))
            map[t.GetProperty("tableName").GetString()!] = fdid.GetUInt32();
    return map;
}

// Serves DB2 files straight out of the local install's CASC storage.
class InstallDBCProvider(BuildInstance build, Dictionary<string, uint> manifest) : IDBCProvider
{
    public Stream StreamForTableName(string tableName, string build_)
    {
        if (!manifest.TryGetValue(tableName, out var fdid))
            throw new KeyNotFoundException($"No FileDataID for table {tableName} in the WoWDBDefs manifest");
        return new MemoryStream(build.OpenFileByFDID(fdid));
    }
}
