using Altrobe.Core.Install;

namespace Altrobe.Core.Tests;

public class InstallDiscoveryTests : IDisposable
{
    // Same column layout as a real Battle.net .build.info, with made-up keys.
    const string BuildInfoText =
        "Branch!STRING:0|Active!DEC:1|Build Key!HEX:16|CDN Key!HEX:16|Install Key!HEX:16|IM Size!DEC:4|CDN Path!STRING:0|CDN Hosts!STRING:0|CDN Servers!STRING:0|Tags!STRING:0|Armadillo!STRING:0|Last Activated!STRING:0|Version!STRING:0|KeyRing!HEX:16|Product!STRING:0\n" +
        "us|1|aaaa0000000000000000000000000001|cccc0000000000000000000000000001|||tpr/wow|host.invalid|http://host.invalid|enUS|||1.60.1.70009||wow_classic_beta\n" +
        "us|1|aaaa0000000000000000000000000002|cccc0000000000000000000000000001|||tpr/wow|host.invalid|http://host.invalid|enUS|||12.1.0.69933|ffff|wow\n" +
        "us|0|aaaa0000000000000000000000000003|cccc0000000000000000000000000001|||tpr/wow|host.invalid|http://host.invalid|enUS|||1.61.0.71000||wow_forever\n";

    readonly string _dir = Path.Combine(Path.GetTempPath(), "altrobe-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void ParsesBuildInfoColumnsWithoutTypeSuffixes()
    {
        var rows = BuildInfoFile.Parse(BuildInfoText);

        Assert.Equal(3, rows.Count);
        Assert.Equal("wow_classic_beta", rows[0]["Product"]);
        Assert.Equal("aaaa0000000000000000000000000001", rows[0]["Build Key"]);
        Assert.Equal("tpr/wow", rows[0]["CDN Path"]);
        Assert.Equal("1.60.1.70009", rows[0]["Version"]);
        Assert.Equal("ffff", rows[1]["KeyRing"]);
        Assert.Equal("0", rows[2]["Active"]);
    }

    [Fact]
    public void ParseIgnoresBlankLinesAndWindowsLineEndings()
    {
        var rows = BuildInfoFile.Parse(BuildInfoText.Replace("\n", "\r\n") + "\r\n\r\n");
        Assert.Equal(3, rows.Count);
        Assert.Equal("wow_forever", rows[2]["Product"]);
    }

    [Fact]
    public void ReadsProductsWithFolderAndBuildNameFromDisk()
    {
        MakeInstall(_dir);

        var install = InstallDiscovery.TryRead(_dir);

        Assert.NotNull(install);
        Assert.Equal(3, install.Products.Count);
        var beta = install.Products.Single(p => p.Product == "wow_classic_beta");
        Assert.Equal("_classic_beta_", beta.Folder);
        Assert.Equal("WOW-70009patch1.60.1_ForeverBeta", beta.BuildName);
        Assert.True(beta.IsForever);
        Assert.True(beta.Active);

        var retail = install.Products.Single(p => p.Product == "wow");
        Assert.Equal("_retail_", retail.Folder);
        Assert.False(retail.IsForever);

        // No flavor folder and no local build config: still listed, just without those details.
        var future = install.Products.Single(p => p.Product == "wow_forever");
        Assert.Null(future.Folder);
        Assert.Null(future.BuildName);
        Assert.False(future.Active);
    }

    [Fact]
    public void ForeverIsIdentifiedFromTheBuildNameNotTheProductCode()
    {
        Assert.True(WowProduct.LooksLikeForever("WOW-80000patch1.61.0_Forever"));
        Assert.True(WowProduct.LooksLikeForever("WOW-70009patch1.60.1_ForeverBeta"));
        Assert.False(WowProduct.LooksLikeForever("WOW-69933patch12.1.0_Retail"));
        Assert.False(WowProduct.LooksLikeForever(null));
    }

    [Fact]
    public void ReturnsNullForAFolderWithoutBuildInfo()
    {
        Directory.CreateDirectory(_dir);
        Assert.Null(InstallDiscovery.TryRead(_dir));
        Assert.Null(InstallDiscovery.TryRead(Path.Combine(_dir, "missing")));
    }

    [Fact]
    public void OverridePathComesFirstThenPlatformDefaults()
    {
        var mac = InstallDiscovery.CandidatePaths("/custom/wow", isWindows: false, isMac: true);
        Assert.Equal(["/custom/wow", "/Applications/World of Warcraft"], mac);

        var win = InstallDiscovery.CandidatePaths(null, isWindows: true, isMac: false);
        Assert.Equal([@"C:\Program Files (x86)\World of Warcraft", @"C:\Program Files\World of Warcraft"], win);

        var linux = InstallDiscovery.CandidatePaths("", isWindows: false, isMac: false);
        Assert.Empty(linux);
    }

    [Fact]
    public void DiscoverSkipsMissingAndDuplicatePaths()
    {
        MakeInstall(_dir);
        var found = InstallDiscovery.Discover([Path.Combine(_dir, "nope"), _dir, _dir + Path.DirectorySeparatorChar]);
        Assert.Single(found);
        Assert.Equal(Path.GetFullPath(_dir), found[0].Path);
    }

    [Fact]
    public void ListsConfigFilesThatBuildInfoNamesButTheInstallLacks()
    {
        // Mid-update, Battle.net points .build.info at configs it has not downloaded yet.
        MakeInstall(_dir);
        var install = InstallDiscovery.TryRead(_dir)!;
        var beta = install.Products.Single(p => p.Product == "wow_classic_beta");
        var cdnConfig = Path.Combine(_dir, "Data", "config", "cc", "cc", "cccc0000000000000000000000000001");

        Assert.Equal([cdnConfig], InstallDiscovery.MissingConfigs(install, beta));

        Directory.CreateDirectory(Path.GetDirectoryName(cdnConfig)!);
        File.WriteAllText(cdnConfig, "# CDN Configuration\n");
        Assert.Empty(InstallDiscovery.MissingConfigs(install, beta));

        var forever = install.Products.Single(p => p.Product == "wow_forever");
        Assert.Equal([Path.Combine(_dir, "Data", "config", "aa", "aa", "aaaa0000000000000000000000000003")], InstallDiscovery.MissingConfigs(install, forever));
    }

    static void MakeInstall(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ".build.info"), BuildInfoText);
        foreach (var (folder, product) in new[] { ("_classic_beta_", "wow_classic_beta"), ("_retail_", "wow") })
        {
            Directory.CreateDirectory(Path.Combine(dir, folder));
            File.WriteAllText(Path.Combine(dir, folder, ".flavor.info"), $"Product Flavor!STRING:0\n{product}\n");
        }
        WriteBuildConfig(dir, "aaaa0000000000000000000000000001", "WOW-70009patch1.60.1_ForeverBeta");
        WriteBuildConfig(dir, "aaaa0000000000000000000000000002", "WOW-69933patch12.1.0_Retail");
    }

    static void WriteBuildConfig(string dir, string key, string name)
    {
        var path = Path.Combine(dir, "Data", "config", key[..2], key[2..4]);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, key), $"# Build Configuration\n\nroot = 00\nbuild-name = {name}\nbuild-uid = x\n");
    }
}
