using Altrobe.Core.Hosted;

namespace Altrobe.Core.Tests;

public class VersionServiceTests
{
    // Hand-written in the shape us.version.battle.net/v2/products/{product}/versions answers with
    // (BPSV: a typed header row, a "## seqn" line, then one pipe-separated row per region).
    const string Versions = """
        Region!STRING:0|BuildConfig!HEX:16|CDNConfig!HEX:16|KeyRing!HEX:16|BuildId!DEC:4|VersionsName!String:0|ProductConfig!HEX:16
        ## seqn = 3184412
        us|aaaa0000000000000000000000000001|cccc0000000000000000000000000001||70235|1.60.1.70235|eeee0000000000000000000000000001
        eu|aaaa0000000000000000000000000002|cccc0000000000000000000000000002||70235|1.60.1.70235|eeee0000000000000000000000000002

        """;

    [Fact]
    public void ParsesOneEntryPerRegionByColumnName()
    {
        var entries = VersionService.Parse(Versions);
        Assert.Equal(["us", "eu"], entries.Select(e => e.Region));
        var us = entries[0];
        Assert.Equal("aaaa0000000000000000000000000001", us.BuildConfig);
        Assert.Equal("cccc0000000000000000000000000001", us.CdnConfig);
        Assert.Equal(70235, us.BuildId);
        Assert.Equal("1.60.1.70235", us.VersionsName);
    }

    [Fact]
    public void ColumnsAreFoundByNameNotPosition()
    {
        const string reordered = "BuildId!DEC:4|Region!STRING:0|CDNConfig!HEX:16|BuildConfig!HEX:16|VersionsName!String:0\n"
            + "70235|us|cccc0000000000000000000000000001|aaaa0000000000000000000000000001|1.60.1.70235\n";
        var us = Assert.Single(VersionService.Parse(reordered));
        Assert.Equal("aaaa0000000000000000000000000001", us.BuildConfig);
        Assert.Equal("cccc0000000000000000000000000001", us.CdnConfig);
    }

    // The cdns answer, in the shape us.version.battle.net/v2/products/wow_classic_beta/cdns answers with.
    const string Cdns = """
        Name!STRING:0|Path!STRING:0|Hosts!STRING:0|Servers!STRING:0|ConfigPath!STRING:0
        ## seqn = 3184400
        us|tpr/wow|level3.blizzard.com us.cdn.blizzard.com|http://level3.blizzard.com/?maxhosts=8|tpr/configs/data
        eu|tpr/wow|eu.cdn.blizzard.com level3.blizzard.com|http://eu.cdn.blizzard.com/?maxhosts=4|tpr/configs/data

        """;

    [Fact]
    public void ReadsTheRegionsCdnHostsAndPath()
    {
        var us = VersionService.ParseCdns(Cdns, "us");
        Assert.Equal("tpr/wow", us.Path);
        Assert.Equal(["level3.blizzard.com", "us.cdn.blizzard.com"], us.Hosts);
        Assert.Equal(["eu.cdn.blizzard.com", "level3.blizzard.com"], VersionService.ParseCdns(Cdns, "eu").Hosts);
        Assert.Throws<InvalidDataException>(() => VersionService.ParseCdns(Cdns, "kr"));
    }

    [Fact]
    public void PicksTheRequestedRegion()
    {
        var entries = VersionService.Parse(Versions);
        Assert.Equal("eu", VersionService.ForRegion(entries, "EU").Region);
        Assert.Throws<InvalidOperationException>(() => VersionService.ForRegion(entries, "kr"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Region!STRING:0|BuildConfig!HEX:16\nus|aaaa\n")] // no CDNConfig column
    public void RejectsResponsesWithoutTheColumnsABuildNeeds(string text)
    {
        Assert.Throws<FormatException>(() => VersionService.Parse(text));
    }
}
