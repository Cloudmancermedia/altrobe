namespace Altrobe.Server.Tests;

public class BakeArgsTests
{
    [Fact]
    public void ALocalBakeNeedsAnOutputFolderAndTakesTheInstallAndProduct()
    {
        var a = BakeArgs.Parse(["--out", "bundle", "--install", "/Applications/World of Warcraft", "--product", "wow_classic_beta", "--limit-items", "5", "--limit-sets", "2"]);
        Assert.Equal(("bundle", "/Applications/World of Warcraft", "wow_classic_beta", false), (a.Out, a.Install, a.Product, a.Cdn));
        Assert.Equal((5, 2), (a.LimitItems, a.LimitSets));
        Assert.Equal(0.05, a.MaxDrop);
        Assert.Null(a.Parallel); // one per CPU core
        Assert.Equal(8, BakeArgs.Parse(["--out", "b", "--install", "x", "--product", "p", "--parallel", "8"]).Parallel);
    }

    [Fact]
    public void ACdnBakeTakesARegionAndOptionallyASavedVersionsAnswer()
    {
        var a = BakeArgs.Parse(["--cdn", "--product", "wow_classic_beta", "--region", "eu", "--versions-file", "versions.txt", "--out", "b", "--previous", "old/manifest.json", "--max-drop", "0.1"]);
        Assert.Equal((true, "wow_classic_beta", "eu", "versions.txt"), (a.Cdn, a.Product, a.Region, a.VersionsFile));
        Assert.Equal(("old/manifest.json", 0.1), (a.Previous, a.MaxDrop));
        Assert.Equal("us", BakeArgs.Parse(["--cdn", "--product", "p", "--out", "b"]).Region);
    }

    public static TheoryData<string[]> BadArgs => new()
    {
        new[] { "--install", "x", "--product", "p" }, // no --out
        new[] { "--out", "b", "--product", "p" }, // local without --install
        new[] { "--out", "b", "--install", "x" }, // no --product
        new[] { "--out", "b", "--install", "x", "--product", "p", "--limit-items", "many" },
        new[] { "--out", "b", "--install", "x", "--product", "p", "--wat" },
        new[] { "--out", "b", "--install", "x", "--product", "p", "--parallel", "0" },
    };

    [Theory]
    [MemberData(nameof(BadArgs))]
    public void BadArgumentsAreRefusedWithAMessage(string[] args)
    {
        var e = Assert.Throws<ArgumentException>(() => BakeArgs.Parse(args));
        Assert.False(string.IsNullOrWhiteSpace(e.Message));
    }
}
