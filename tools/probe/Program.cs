// Spike probe: open a local WoW install and read DB2 tables.
// Reads only local files. Never downloads game data from Blizzard's CDN.
//
// Usage:
//   dotnet run -- show <table>              print a table's columns and first rows
//   dotnet run -- export <table> [table...] write tables to output/tables/<table>.json
//   dotnet run -- check <fileDataId...>     confirm files exist and report their format
//   dotnet run -- convert-model <fdid...>   M2 + first skin -> skinned output/models/<fdid>.glb (with Stand) and .json
//   dotnet run -- anim-info <m2Fdid> [skelFdid] where an M2's bones, sequences and .anim files live
//   dotnet run -- convert-texture <fdid...> BLP -> output/textures/<fdid>.png
//   dotnet run -- convert-all [modelFdid...] everything in output/resolved/*/*.json, plus extra models
//
// Environment overrides: ALTROBE_INSTALL (default "/Applications/World of Warcraft"),
// ALTROBE_PRODUCT (default wow_classic_beta), ALTROBE_TABLES_DIR (default "tables", under output/).

using System.Text.Json;
using DBCD;
using DBCD.IO;
using DBCD.Providers;
using TACTSharp;

var mode = args.ElementAtOrDefault(0) ?? "show";
var tables = args.Skip(1).ToArray();
if (tables.Length == 0) tables = ["ChrRaces"];

var installDir = Environment.GetEnvironmentVariable("ALTROBE_INSTALL") ?? "/Applications/World of Warcraft";
var product = Environment.GetEnvironmentVariable("ALTROBE_PRODUCT") ?? "wow_classic_beta";

// Everything this tool writes goes under output/, which .gitignore excludes.
var outputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../output"));
Directory.CreateDirectory(outputDir);

var build = new BuildInstance();
build.Settings.BaseDir = installDir;
build.Settings.Product = product;
build.Settings.TryCDN = false;
build.Settings.ListfileFallback = false;
build.Settings.CacheDir = Path.Combine(outputDir, "tact-cache");
// Full mode keeps every root entry per file. Normal mode keeps one, and for textures that have an
// optional HD version it keeps the HD entry, which is only on disk if the HD pack is installed.
build.Settings.RootMode = RootInstance.LoadMode.Full;
// TryCDN=false alone is not enough: when a file is missing locally, TACTSharp first queries
// Blizzard's version service for a CDN list and pings the servers, and only then checks TryCDN.
// A non-empty placeholder list skips that lookup, so a missing file fails without any network call.
build.cdn.SetCDNs(["no-cdn.invalid"]);

var buildInfo = new BuildInfo(Path.Combine(installDir, ".build.info"), build.Settings, build.cdn);
var entry = buildInfo.Entries.FirstOrDefault(e => e.Product == product);
if (entry.Product == null)
{
    Console.Error.WriteLine($"Product {product} not found in .build.info. Found: {string.Join(", ", buildInfo.Entries.Select(e => e.Product))}");
    return 1;
}

build.cdn.ProductDirectory = entry.CDNPath;
build.LoadConfigs(entry.BuildConfig, entry.CDNConfig);
build.Load();
var buildName = build.BuildConfig!.Values["build-name"][0];
Console.WriteLine($"Product: {entry.Product}  Version: {entry.Version}  Build: {buildName}");

// tags mode: list the install manifest's tags and how many files each covers.
if (mode == "tags")
{
    foreach (var tag in build.Install!.Tags.OrderBy(t => t.type).ThenBy(t => t.name))
    {
        var count = 0;
        for (var i = 0; i < tag.files.Length; i++) if (tag.files[i]) count++;
        Console.WriteLine($"type {tag.type,3}  {tag.name,-24} {count} files");
    }
    return 0;
}

// where mode: show how far a FileDataID gets through root -> encoding -> local archives.
if (mode == "where")
{
    foreach (var arg in args.Skip(1))
    {
        var fdid = uint.Parse(arg);
        var entries = build.Root!.GetEntriesByFDID(fdid);
        Console.WriteLine($"{fdid}: {entries.Count} root entries");
        foreach (var e in entries)
        {
            var enc = build.Encoding!.FindContentKey(e.md5.AsSpan());
            Console.WriteLine($"   content={e.contentFlags} locale={e.localeFlags} encodingKeys={enc.Length} size={(enc.Length > 0 ? enc.DecodedFileSize : 0)} ekey={(enc.Length > 0 ? Convert.ToHexStringLower(enc[0]) : "-")}");
        }
    }
    return 0;
}

// stats mode: sample files from root, grouped by content flags, and count how many are readable locally.
if (mode == "stats")
{
    var perGroup = int.Parse(args.ElementAtOrDefault(1) ?? "100");
    var rng = new Random(1);
    var groups = build.Root!.GetAvailableFDIDs()
        .Select(f => (fdid: f, flags: build.Root.GetEntriesByFDID(f)[0].contentFlags))
        .GroupBy(x => x.flags);
    foreach (var g in groups.OrderByDescending(g => g.Count()))
    {
        var sample = g.OrderBy(_ => rng.Next()).Take(perGroup).ToList();
        var ok = sample.Count(x => { try { LocalFiles.Open(build, x.fdid); return true; } catch { return false; } });
        Console.WriteLine($"{g.Key}: {g.Count()} files, {ok}/{sample.Count} sampled readable locally");
    }
    return 0;
}

// check mode: open each FileDataID in the install and report size and format. Writes nothing.
if (mode == "check")
{
    var bad = 0;
    foreach (var arg in args.Skip(1))
    {
        var fdid = uint.Parse(arg);
        try
        {
            var (bytes, flags) = LocalFiles.Open(build, fdid);
            var magic = bytes.Length >= 4 ? System.Text.Encoding.ASCII.GetString(bytes, 0, 4) : "";
            Console.WriteLine($"{fdid}: {bytes.Length} bytes, magic {magic}, entry {flags}");
        }
        catch (Exception e)
        {
            bad++;
            Console.WriteLine($"{fdid}: FAILED ({e.GetType().Name}: {e.Message})");
        }
    }
    return bad > 0 ? 2 : 0;
}

// convert modes: turn local M2/BLP files into .glb/.json/.png under output/. See Convert/.
if (mode == "anim-info")
{
    Altrobe.Convert.ConvertCommands.AnimInfo(fdid => LocalFiles.Open(build, fdid).bytes, uint.Parse(args[1]), args.Length > 2 ? uint.Parse(args[2]) : 0);
    return 0;
}

if (mode is "convert-model" or "convert-texture" or "convert-all")
{
    Func<uint, byte[]> open = fdid => LocalFiles.Open(build, fdid).bytes;
    var ids = args.Skip(1).Select(uint.Parse).ToList();
    var failures = mode switch
    {
        "convert-model" => ids.Count(id => Altrobe.Convert.ConvertCommands.Model(open, outputDir, id) == null),
        "convert-texture" => ids.Count(id => !Altrobe.Convert.ConvertCommands.Texture(open, outputDir, id)),
        _ => Altrobe.Convert.ConvertCommands.All(open, outputDir, ids),
    };
    return failures > 0 ? 2 : 0;
}

var manifest = await LoadManifest(outputDir);
var dbcd = new DBCD.DBCD(new InstallDBCProvider(build, manifest), new GithubDBDProvider(useCache: true));

var hotfixPath = Path.Combine(installDir, entry.Folder ?? "", "Cache", "ADB", "enUS", "DBCache.bin");
var hotfixes = File.Exists(hotfixPath) ? new HotfixReader(hotfixPath) : null;
Console.WriteLine(hotfixes != null ? $"Hotfix cache: {hotfixPath} (build {hotfixes.BuildId})" : $"No hotfix cache at {hotfixPath}");

var failed = new List<string>();
foreach (var table in tables)
{
    IDBCDStorage storage;
    try
    {
        storage = dbcd.Load(table, entry.Version);
        if (hotfixes != null) storage.ApplyingHotfixes(hotfixes);
    }
    catch (Exception e)
    {
        Console.WriteLine($"{table}: FAILED ({e.GetType().Name}: {e.Message})");
        failed.Add(table);
        continue;
    }

    if (mode == "export")
    {
        var tablesDir = Path.Combine(outputDir, Environment.GetEnvironmentVariable("ALTROBE_TABLES_DIR") ?? "tables");
        Directory.CreateDirectory(tablesDir);
        var rows = storage.Keys.OrderBy(k => k).Select(id =>
        {
            var row = storage[id];
            return storage.AvailableColumns.ToDictionary(c => c, c => row[c]);
        });
        var doc = new { table, product, version = entry.Version, build = buildName, hotfixBuild = hotfixes?.BuildId, columns = storage.AvailableColumns, rows };
        await File.WriteAllTextAsync(Path.Combine(tablesDir, $"{table}.json"), JsonSerializer.Serialize(doc));
        Console.WriteLine($"{table}: {storage.Count} rows exported");
    }
    else
    {
        Console.WriteLine($"{table}: {storage.Count} rows, layout hash {storage.LayoutHash:X8}");
        Console.WriteLine($"Columns: {string.Join(", ", storage.AvailableColumns)}");
        foreach (var id in storage.Keys.OrderBy(k => k).Take(10))
        {
            var row = storage[id];
            Console.WriteLine("  " + string.Join(" | ", storage.AvailableColumns.Take(8).Select(c => Format(row[c]))));
        }
    }
}

if (failed.Count > 0) Console.WriteLine($"Failed tables: {string.Join(", ", failed)}");
return failed.Count > 0 ? 2 : 0;

static string Format(object? value) => value is Array a ? "[" + string.Join(",", a.Cast<object>()) + "]" : value?.ToString() ?? "";

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
        return new MemoryStream(LocalFiles.Open(build, fdid).bytes);
    }
}

static class LocalFiles
{
    // Opens a file from local storage. Tries standard entries before HD-texture and low-violence
    // variants, and returns the first one whose data is actually on disk.
    public static (byte[] bytes, RootInstance.ContentFlags flags) Open(BuildInstance build, uint fdid)
    {
        const RootInstance.ContentFlags HighResTexture = RootInstance.ContentFlags.F00000001;
        var entries = build.Root!.GetEntriesByFDID(fdid)
            .OrderBy(e => (e.contentFlags & HighResTexture) != 0)
            .ThenBy(e => (e.contentFlags & RootInstance.ContentFlags.LowViolence) != 0)
            .ToList();
        if (entries.Count == 0) throw new FileNotFoundException($"FileDataID {fdid} is not in root");
        foreach (var e in entries)
        {
            try { return (build.OpenFileByCKey(e.md5.AsSpan()), e.contentFlags); }
            catch (FileNotFoundException) { }
        }
        throw new FileNotFoundException($"FileDataID {fdid}: none of {entries.Count} root entries is on local disk");
    }
}
