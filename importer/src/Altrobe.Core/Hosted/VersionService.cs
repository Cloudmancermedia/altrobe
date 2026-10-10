namespace Altrobe.Core.Hosted;

// One region's row from Blizzard's version service: which build and CDN configs make up the
// product's current build there.
public sealed record VersionEntry(string Region, string BuildConfig, string CdnConfig, int BuildId, string VersionsName);

// One region's row from the cdns answer: the CDN folder for the product and its hosts, in the
// order Blizzard lists them.
public sealed record CdnHosts(string Path, IReadOnlyList<string> Hosts);

// Reads the version service's answer for a product (us.version.battle.net/v2/products/{product}/versions).
// It is BPSV: a header row of Name!TYPE:size columns, "##" comment lines, then pipe-separated rows.
// Columns are found by name, since nothing promises their order. Parsing only; no network here.
public static class VersionService
{
    public const string Host = "us.version.battle.net";

    public static string VersionsUrl(string product) => $"https://{Host}/v2/products/{Uri.EscapeDataString(product)}/versions";

    // HTTPS: some networks refuse plain HTTP to the version service.
    public static string CdnsUrl(string product) => $"https://{Host}/v2/products/{Uri.EscapeDataString(product)}/cdns";

    public static IReadOnlyList<VersionEntry> Parse(string text)
    {
        var (column, rows) = Bpsv(text);
        int region = column("Region"), build = column("BuildConfig"), cdn = column("CDNConfig"), buildId = column("BuildId"), name = column("VersionsName");
        return rows.Select(f => new VersionEntry(f[region], f[build], f[cdn], int.TryParse(f[buildId], out var id) ? id : 0, f[name])).ToList();
    }

    // The cdns answer (.../cdns): rows named by region, with the product's CDN folder and its hosts.
    public static CdnHosts ParseCdns(string text, string region)
    {
        var (column, rows) = Bpsv(text);
        int name = column("Name"), path = column("Path"), hosts = column("Hosts");
        var row = rows.FirstOrDefault(f => f[name].Equals(region, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"The cdns answer lists no CDN for region {region} (has: {string.Join(", ", rows.Select(f => f[name]))}).");
        return new CdnHosts(row[path], row[hosts].Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    static (Func<string, int> Column, List<string[]> Rows) Bpsv(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r').Trim()).Where(l => l.Length > 0 && !l.StartsWith("##")).ToList();
        if (lines.Count == 0) throw new FormatException("The version service answered with no header row.");
        var columns = lines[0].Split('|').Select(c => c.Split('!')[0]).ToList();
        int Column(string name)
        {
            var i = columns.FindIndex(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 ? i : throw new FormatException($"The version service answer has no {name} column.");
        }
        return (Column, lines.Skip(1).Select(l => l.Split('|')).Where(f => f.Length == columns.Count).ToList());
    }

    public static VersionEntry ForRegion(IReadOnlyList<VersionEntry> entries, string region) =>
        entries.FirstOrDefault(e => e.Region.Equals(region, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"The version service lists no build for region {region} (has: {string.Join(", ", entries.Select(e => e.Region))}).");
}
