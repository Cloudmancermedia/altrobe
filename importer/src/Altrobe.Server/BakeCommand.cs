using System.Globalization;
using Altrobe.Core.Builds;
using Altrobe.Core.Cache;
using Altrobe.Core.Catalog;
using Altrobe.Core.Hosted;
using Altrobe.Core.Install;
using Altrobe.Core.Storage;
using Altrobe.Core.Tables;

namespace Altrobe.Server;

// `Altrobe.Server bake ...`: writes one build as the hosted site's static bundle (BundleBaker).
//   Local:  bake --out <dir> --install <WoW folder> --product <product> [--limit-items N] [--limit-sets N]
//   CDN:    bake --out <dir> --cdn --product <product> [--region us] [--versions-file <saved answer>]
//   Both:   [--previous <manifest.json>] [--max-drop 0.05] [--cache <dir>] [--parallel N]
// The CDN mode downloads from Blizzard's public CDN (docs/hosted-pipeline.md).
public sealed record BakeArgs(
    string Out, string Product, bool Cdn, string? Install, string Region, string? VersionsFile,
    int? LimitItems, int? LimitSets, string? Previous, double MaxDrop, string? Cache, int? Parallel = null)
{
    public static BakeArgs Parse(IReadOnlyList<string> args)
    {
        string? output = null, product = null, install = null, versions = null, previous = null, cache = null;
        var region = "us";
        var cdn = false;
        int? limitItems = null, limitSets = null, parallel = null;
        var maxDrop = 0.05;
        for (var i = 0; i < args.Count; i++)
        {
            string Value() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
            int Count() => int.TryParse(Value(), out var n) && n > 0 ? n : throw new ArgumentException($"{args[i - 1]} needs a positive whole number.");
            switch (args[i])
            {
                case "--out": output = Value(); break;
                case "--product": product = Value(); break;
                case "--install": install = Value(); break;
                case "--cdn": cdn = true; break;
                case "--region": region = Value(); break;
                case "--versions-file": versions = Value(); break;
                case "--limit-items": limitItems = Count(); break;
                case "--limit-sets": limitSets = Count(); break;
                case "--parallel": parallel = Count(); break;
                case "--previous": previous = Value(); break;
                case "--cache": cache = Value(); break;
                case "--max-drop":
                    maxDrop = double.TryParse(Value(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d is >= 0 and < 1
                        ? d : throw new ArgumentException("--max-drop needs a fraction from 0 to 1, such as 0.05.");
                    break;
                default: throw new ArgumentException($"Unknown option {args[i]}.");
            }
        }
        if (output == null) throw new ArgumentException("--out is required: the folder the bundle is written to.");
        if (product == null) throw new ArgumentException("--product is required, such as wow_classic_beta.");
        if (!cdn && install == null) throw new ArgumentException("Pass --install <WoW folder> for a local bake, or --cdn to read from Blizzard's CDN.");
        return new(output, product, cdn, install, region, versions, limitItems, limitSets, previous, maxDrop, cache, parallel);
    }
}

public static class BakeCommand
{
    public const string Usage = """
        Usage:
          bake --out <dir> --install <WoW folder> --product <product> [--limit-items N] [--limit-sets N]
          bake --out <dir> --cdn --product <product> [--region us] [--versions-file <file>]
        Both: [--previous <manifest.json>] [--max-drop 0.05] [--cache <dir>] [--parallel N, default one per CPU core]
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output)
    {
        BakeArgs a;
        try { a = BakeArgs.Parse(args); }
        catch (ArgumentException e)
        {
            await output.WriteLineAsync(e.Message + "\n\n" + Usage);
            return 2;
        }
        var cacheRoot = a.Cache ?? Path.Combine(Path.GetTempPath(), "altrobe-bake");
        using var namesHttp = new HttpClient();
        var classic = await ClassicItemNames.LoadAsync(cacheRoot, namesHttp, m => output.WriteLine(m));
        var session = a.Cdn ? await OpenCdnAsync(a, cacheRoot, classic, output) : OpenLocal(a, cacheRoot, classic);
        await output.WriteLineAsync($"Baking {session.Product} {session.Build} ({session.BuildName}) into {a.Out}");
        var result = await BundleBaker.BakeAsync(new BuildSessionSource(session), new BakeOptions(a.Out, a.LimitItems, a.LimitSets, a.Previous, a.MaxDrop, a.Parallel), m => output.WriteLine(m));
        foreach (var (key, n) in result.Manifest.Counts) await output.WriteLineAsync($"  {key}: {n}");
        if (result.Ok)
        {
            await output.WriteLineAsync("current.json points at this build.");
            return 0;
        }
        foreach (var f in result.Failures) await output.WriteLineAsync($"FAILED: {f}");
        await output.WriteLineAsync("current.json was left as it was.");
        return 1;
    }

    static BuildSession OpenLocal(BakeArgs a, string cacheRoot, IReadOnlyDictionary<int, ClassicItem> classic)
    {
        var install = InstallDiscovery.Discover([a.Install!]).FirstOrDefault() ?? throw new InvalidOperationException($"No WoW install found at {a.Install}.");
        var product = install.Products.FirstOrDefault(p => p.Product == a.Product)
            ?? throw new InvalidOperationException($"{a.Install} has no {a.Product} (has: {string.Join(", ", install.Products.Select(p => p.Product))}).");
        return BuildSession.Open(install, product, cacheRoot, classic);
    }

    // NOT RUN by tests: asks Blizzard's version service (unless --versions-file is given) and then
    // downloads the build's indexes and files from the CDN.
    static async Task<BuildSession> OpenCdnAsync(BakeArgs a, string cacheRoot, IReadOnlyDictionary<int, ClassicItem> classic, TextWriter output)
    {
        using var http = new HttpClient();
        var versions = a.VersionsFile is { } file ? await File.ReadAllTextAsync(file) : await http.GetStringAsync(VersionService.VersionsUrl(a.Product));
        var source = CdnSource.From(a.Product, VersionService.ForRegion(VersionService.Parse(versions), a.Region));
        var cdns = VersionService.ParseCdns(await http.GetStringAsync(VersionService.CdnsUrl(a.Product)), a.Region);
        await output.WriteLineAsync($"Reading {a.Product} {source.Version} from Blizzard's CDN ({a.Region}: {string.Join(", ", cdns.Hosts)}).");
        var cacheDir = AppPaths.BuildDir(cacheRoot, a.Product, source.Version);
        var storage = await GameStorage.OpenCdnAsync(source, Path.Combine(cacheDir, "tact"), cdns.Hosts, http, m => output.WriteLine(m));
        var tables = new Db2Tables(storage, new DefinitionProvider(Path.Combine(cacheRoot, "definitions")), source.Version, null);
        return new BuildSession(tables, storage, a.Product, source.Version, storage.BuildName, cacheDir, classic);
    }
}
