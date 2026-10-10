using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Altrobe.Core.Catalog;

// An item as cmangos' Classic 1.12.1 database knows it.
public sealed record ClassicItem(string Name, int Quality, int ItemLevel, int RequiredLevel, int AllowableClass);

// Names for the Classic items the Forever beta's game files leave unnamed (no ItemSparse row:
// Perdition's Blade and every tier 1 and 2 piece among them). They come from cmangos
// classic-db (https://github.com/cmangos/classic-db, GPL-3.0), a community database of the 1.12.1
// client, at a pinned commit. The data is downloaded and cached at run time, never kept in this
// repository; the catalog uses it only where the game files have no name. THIRD_PARTY_NOTICES.md.
public static class ClassicItemNames
{
    public const string Commit = "28ef6259c782928b08a8dc9cacf6bc02e64f2b29";
    public const string Url = $"https://raw.githubusercontent.com/cmangos/classic-db/{Commit}/Full_DB/ClassicDB_1_12_1_z2815.sql.gz";
    public const string Credit = "Names of Classic items the game files leave unnamed: cmangos classic-db (https://github.com/cmangos/classic-db), GPL-3.0.";

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // The extracted names, cached as JSON beside the other caches; downloaded on first use. Offline or on
    // any failure the catalog keeps those items unnamed, and `log` says why.
    public static async Task<IReadOnlyDictionary<int, ClassicItem>> LoadAsync(string cacheRoot, HttpClient http, Action<string>? log = null, CancellationToken ct = default)
    {
        var path = Path.Combine(cacheRoot, "classic-items", $"{Commit}.json");
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<int, ClassicItem>>(await File.ReadAllTextAsync(path, ct), Json) ?? [];
            log?.Invoke("Downloading Classic item names (cmangos classic-db, about 13 MB, once)...");
            await using var gz = new GZipStream(await http.GetStreamAsync(Url, ct), CompressionMode.Decompress);
            using var reader = new StreamReader(gz, Encoding.UTF8);
            var items = Parse(await reader.ReadToEndAsync(ct));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(items, Json), ct);
            File.Move(tmp, path, overwrite: true);
            log?.Invoke($"{items.Count} Classic item names ready.");
            return items;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or JsonException or TaskCanceledException)
        {
            log?.Invoke($"Classic item names are unavailable ({e.Message}); items the game files leave unnamed stay unnamed.");
            return new Dictionary<int, ClassicItem>();
        }
    }

    // Reads item_template from a MySQL dump: its column list, then every row of its INSERT statements.
    public static Dictionary<int, ClassicItem> Parse(string sql)
    {
        var create = sql.IndexOf("CREATE TABLE `item_template`", StringComparison.Ordinal);
        if (create < 0) throw new InvalidDataException("no item_template table in the dump");
        var columns = new List<string>();
        foreach (var line in sql[create..sql.IndexOf(") ENGINE", create, StringComparison.Ordinal)].Split('\n').Skip(1))
        {
            var t = line.Trim();
            if (t.StartsWith('`')) columns.Add(t[1..t.IndexOf('`', 1)]);
        }
        int Col(string name) => columns.IndexOf(name) is >= 0 and var i ? i : throw new InvalidDataException($"item_template has no {name} column");
        var (entry, name, quality, cls, ilvl, req) = (Col("entry"), Col("name"), Col("Quality"), Col("AllowableClass"), Col("ItemLevel"), Col("RequiredLevel"));

        var items = new Dictionary<int, ClassicItem>();
        const string insert = "INSERT INTO `item_template` VALUES ";
        for (var at = sql.IndexOf(insert, StringComparison.Ordinal); at >= 0; at = sql.IndexOf(insert, at, StringComparison.Ordinal))
        {
            at += insert.Length;
            foreach (var row in Rows(sql, ref at))
            {
                int Int(int i) => int.Parse(row[i], System.Globalization.CultureInfo.InvariantCulture);
                items[Int(entry)] = new ClassicItem(row[name], Int(quality), Int(ilvl), Int(req), Int(cls));
            }
        }
        return items;
    }

    // The value tuples of one INSERT, from `at` to its closing semicolon. Strings are single-quoted with
    // backslash escapes.
    static IEnumerable<List<string>> Rows(string sql, ref int at)
    {
        var rows = new List<List<string>>();
        List<string>? row = null;
        var value = new StringBuilder();
        var quoted = false;
        for (; at < sql.Length; at++)
        {
            var c = sql[at];
            if (quoted)
            {
                if (c == '\\' && at + 1 < sql.Length) value.Append(sql[++at]);
                else if (c == '\'') quoted = false;
                else value.Append(c);
                continue;
            }
            switch (c)
            {
                case '\'': quoted = true; break;
                case '(': row = []; value.Clear(); break;
                case ',' when row != null: row.Add(value.ToString()); value.Clear(); break;
                case ')' when row != null: row.Add(value.ToString()); rows.Add(row); row = null; break;
                case ';' when row == null: return rows;
                default: if (row != null && !char.IsWhiteSpace(c)) value.Append(c); break;
            }
        }
        return rows;
    }
}
