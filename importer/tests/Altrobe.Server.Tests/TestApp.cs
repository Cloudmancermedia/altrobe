using System.Buffers.Binary;
using Altrobe.Core.Builds;
using Altrobe.Core.Install;
using Altrobe.Core.Storage;
using Altrobe.Core.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Altrobe.Server.Tests;

// The server over a fake install folder and fake game data. Nothing here reads a real install.
public sealed class TestApp : WebApplicationFactory<Program>
{
    public const string Build = "1.60.1.70009";
    public const uint TextureFdid = 9001;
    readonly string _root = Path.Combine(Path.GetTempPath(), "altrobe-server-tests", Guid.NewGuid().ToString("N"));

    readonly bool _openBrowser;

    public TestApp(bool openBrowser = false)
    {
        _openBrowser = openBrowser;
        InstallDir = Path.Combine(_root, "World of Warcraft");
        CacheRoot = Path.Combine(_root, "cache");
        WebRoot = Path.Combine(_root, "web");
        Directory.CreateDirectory(InstallDir);
        File.WriteAllText(Path.Combine(InstallDir, ".build.info"),
            "Branch!STRING:0|Active!DEC:1|Build Key!HEX:16|CDN Key!HEX:16|Install Key!HEX:16|IM Size!DEC:4|CDN Path!STRING:0|CDN Hosts!STRING:0|CDN Servers!STRING:0|Tags!STRING:0|Armadillo!STRING:0|Last Activated!STRING:0|Version!STRING:0|KeyRing!HEX:16|Product!STRING:0\n" +
            $"us|1|aaaa0000000000000000000000000001|cccc0000000000000000000000000001|||tpr/wow|host.invalid|http://host.invalid|enUS|||{Build}||wow_classic_beta\n");
        var config = Path.Combine(InstallDir, "Data", "config", "aa", "aa");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "aaaa0000000000000000000000000001"), "build-name = WOW-70009patch1.60.1_ForeverBeta\n");
        CdnConfigFile = Path.Combine(InstallDir, "Data", "config", "cc", "cc", "cccc0000000000000000000000000001");
        Directory.CreateDirectory(Path.GetDirectoryName(CdnConfigFile)!);
        File.WriteAllText(CdnConfigFile, "# CDN Configuration\n");
        Directory.CreateDirectory(WebRoot);
        File.WriteAllText(Path.Combine(WebRoot, "index.html"), "<!doctype html><title>test web app</title>");
        Directory.CreateDirectory(Path.Combine(WebRoot, "static"));
        File.WriteAllText(Path.Combine(WebRoot, "static", "app.js"), "console.log(1)");
    }

    public string InstallDir { get; }
    public string CdnConfigFile { get; }
    public string CacheRoot { get; }
    public string WebRoot { get; }
    public FakeFiles Files { get; } = new();
    public List<string> OpenedUrls { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<IInstallSource>(new FixedInstalls([InstallDir]));
            s.AddSingleton<IBuildSessionFactory>(new FakeSessions(this));
            s.AddSingleton<IBrowserLauncher>(new RecordingBrowser(OpenedUrls));
            s.AddSingleton(new ServerSettings { CacheRoot = CacheRoot, WebRoot = WebRoot, OpenBrowser = _openBrowser });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    public HttpClient Local() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1:5161") });

    public async Task<HttpClient> Installed()
    {
        var client = Local();
        var r = await client.PostAsync("/api/v1/install", Json("""{"product":"wow_classic_beta"}"""));
        r.EnsureSuccessStatusCode();
        return client;
    }

    public static StringContent Json(string body) => new(body, System.Text.Encoding.UTF8, "application/json");

    sealed class FixedInstalls(string[] paths) : IInstallSource
    {
        public IReadOnlyList<WowInstall> Discover() => InstallDiscovery.Discover(paths);
    }

    sealed class RecordingBrowser(List<string> urls) : IBrowserLauncher
    {
        public void Open(string url) => urls.Add(url);
    }

    sealed class FakeSessions(TestApp app) : IBuildSessionFactory
    {
        public BuildSession Open(WowInstall install, WowProduct product, string cacheRoot) =>
            new(FakeTables(), app.Files, product.Product, product.Version, product.BuildName, Path.Combine(cacheRoot, product.Product, product.Version));
    }

    public sealed class FakeFiles : IGameFiles
    {
        int _opens;
        public int Opens => _opens;

        public bool Exists(uint fileDataId) => fileDataId == TextureFdid;

        public byte[] Open(uint fileDataId)
        {
            Interlocked.Increment(ref _opens);
            if (fileDataId != TextureFdid) throw new FileNotFoundException($"FileDataID {fileDataId} is not in root");
            // 1x1 uncompressed BGRA BLP2.
            var d = new byte[148 + 1024 + 4];
            "BLP2"u8.CopyTo(d);
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(4), 1);
            d[8] = 3;
            d[9] = 8;
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(12), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(16), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(20), 148 + 1024);
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(84), 4);
            d[^4] = 1; d[^3] = 2; d[^2] = 3; d[^1] = 255;
            return d;
        }
    }

    static IReadOnlyDictionary<string, object?> R(params (string c, object? v)[] cells) => cells.ToDictionary(x => x.c, x => x.v);

    static InMemoryTables FakeTables()
    {
        var t = new InMemoryTables()
            .Add(GameTableNames.ChrRaces, R(("ID", 2), ("Name_lang", "Orc"), ("Name_female_lang", "Orc"), ("ClientFileString", "Orc")))
            .Add(GameTableNames.ChrClasses, R(("ID", 1), ("Name_lang", "Warrior")))
            .Add(GameTableNames.CharBaseInfo, R(("ID", 1), ("RaceID", 2), ("ClassID", 1)))
            .Add(GameTableNames.ChrRaceXChrModel, R(("ID", 1), ("ChrRacesID", 2), ("Sex", 0), ("ChrModelID", 3)))
            .Add(GameTableNames.ChrModelAltVariant, R(("ID", 1), ("SourceChrModelID", 3), ("VariantChrModelID", 259)));
        foreach (var (id, name, invType, quality) in new[] { (1, "Robe of the Archmage", 20, 4), (2, "Thunderfury", 13, 5), (3, "Frostweave Robe", 20, 2), (4, "Plain Robe", 20, 1) })
        {
            t.Add(GameTableNames.ItemSparse, R(("ID", id), ("Display_lang", name), ("InventoryType", (byte)invType), ("OverallQualityID", (byte)quality)));
            t.Add(GameTableNames.Item, R(("ID", id), ("IconFileDataID", 1000 + id)));
            t.Add(GameTableNames.ItemModifiedAppearance, R(("ID", id), ("ItemID", id), ("ItemAppearanceModifierID", 0), ("OrderIndex", 0), ("ItemAppearanceID", id)));
            t.Add(GameTableNames.ItemAppearance, R(("ID", id), ("ItemDisplayInfoID", 100 + id)));
            t.Add(GameTableNames.ItemDisplayInfo, R(("ID", 100 + id), ("ModelResourcesID", new[] { id == 2 ? 50 : 0, 0 }), ("ModelMaterialResourcesID", new[] { id == 2 ? 60 : 0, 0 }),
                ("GeosetGroup", new int[6]), ("AttachmentGeosetGroup", new int[6]), ("HelmetGeosetVis", new int[2])));
        }
        t.Add(GameTableNames.ModelFileData, R(("FileDataID", 148234), ("ModelResourcesID", 50)));
        t.Add(GameTableNames.TextureFileData, R(("FileDataID", 148236), ("MaterialResourcesID", 60), ("UsageType", 0)));
        return t;
    }
}
