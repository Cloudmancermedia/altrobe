using System.Reflection;
using System.Text.Json;
using Altrobe.Core.Cache;
using DBCD.Providers;

namespace Altrobe.Core.Tables;

// WoWDBDefs files shipped inside this assembly (see Definitions/README.md), so a first run works
// without network access.
public static class VendoredDefinitions
{
    static readonly Assembly Assembly = typeof(VendoredDefinitions).Assembly;

    public static bool HasDefinition(string table) => Assembly.GetManifestResourceInfo($"Definitions/{table}.dbd") != null;

    public static string? Read(string table)
    {
        using var s = Assembly.GetManifestResourceStream($"Definitions/{table}.dbd");
        return s == null ? null : new StreamReader(s).ReadToEnd();
    }

    public static IReadOnlyDictionary<string, uint> Manifest { get; } = LoadManifest();

    static Dictionary<string, uint> LoadManifest()
    {
        using var s = Assembly.GetManifestResourceStream("Definitions/manifest.json")!;
        return ParseManifest(s);
    }

    internal static Dictionary<string, uint> ParseManifest(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in doc.RootElement.EnumerateArray())
            if (t.TryGetProperty("db2FileDataID", out var fdid) && fdid.ValueKind == JsonValueKind.Number)
                map[t.GetProperty("tableName").GetString()!] = fdid.GetUInt32();
        return map;
    }
}

public static class DbdBuilds
{
    // True if any BUILD line names the build, either exactly or inside an "a-b" range.
    public static bool Lists(string dbd, string build)
    {
        if (!Version.TryParse(build, out var target)) return false;
        foreach (var raw in dbd.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("BUILD ", StringComparison.Ordinal)) continue;
            foreach (var part in line[6..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var dash = part.IndexOf('-');
                if (dash < 0)
                {
                    if (Version.TryParse(part, out var v) && v == target) return true;
                }
                else if (Version.TryParse(part[..dash], out var lo) && Version.TryParse(part[(dash + 1)..], out var hi) && lo <= target && target <= hi)
                {
                    return true;
                }
            }
        }
        return false;
    }
}

// Definition lookup order: vendored copy if it lists the build, then the cached download if it
// does, then a fresh download from WoWDBDefs on GitHub (cached for next time). Offline, the best
// copy on hand is used and DBCD reports if it has no layout for the build.
public sealed class DefinitionProvider : IDBDProvider
{
    const string BaseUrl = "https://raw.githubusercontent.com/wowdev/WoWDBDefs/master/";
    readonly string _cacheDir;
    readonly HttpClient _http;

    public DefinitionProvider(string cacheDir, HttpMessageHandler? handler = null)
    {
        _cacheDir = cacheDir;
        _http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(20) };
    }

    public Stream StreamForTableName(string tableName, string build) => ToStream(Definition(tableName, build));

    public string Definition(string table, string build)
    {
        var vendored = VendoredDefinitions.Read(table);
        if (vendored != null && DbdBuilds.Lists(vendored, build)) return vendored;

        var cachedPath = Path.Combine(_cacheDir, $"{table}.dbd");
        var cached = File.Exists(cachedPath) ? File.ReadAllText(cachedPath) : null;
        if (cached != null && DbdBuilds.Lists(cached, build)) return cached;

        var downloaded = TryDownload($"definitions/{table}.dbd");
        if (downloaded != null)
        {
            AtomicFile.WriteAllText(cachedPath, downloaded);
            return downloaded;
        }
        return cached ?? vendored ?? throw new FileNotFoundException($"No WoWDBDefs definition for table {table} (not vendored, not cached, download failed)");
    }

    public uint FileDataIdFor(string table)
    {
        if (VendoredDefinitions.Manifest.TryGetValue(table, out var fdid)) return fdid;
        var cachedPath = Path.Combine(_cacheDir, "manifest.json");
        if (!File.Exists(cachedPath))
        {
            var downloaded = TryDownload("manifest.json") ?? throw new KeyNotFoundException($"Table {table} is not in the vendored WoWDBDefs manifest and the full manifest could not be downloaded");
            AtomicFile.WriteAllText(cachedPath, downloaded);
        }
        using var s = File.OpenRead(cachedPath);
        return VendoredDefinitions.ParseManifest(s).TryGetValue(table, out fdid) ? fdid : throw new KeyNotFoundException($"Table {table} is not in the WoWDBDefs manifest");
    }

    string? TryDownload(string path)
    {
        try
        {
            using var response = _http.GetAsync(BaseUrl + path).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return null;
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    static MemoryStream ToStream(string text) => new(System.Text.Encoding.UTF8.GetBytes(text));
}
