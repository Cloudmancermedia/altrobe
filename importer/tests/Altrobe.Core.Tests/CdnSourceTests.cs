using Altrobe.Core.Hosted;
using Altrobe.Core.Storage;

namespace Altrobe.Core.Tests;

// Configuration only. Opening a CDN build downloads from Blizzard, so no test here does it.
public class CdnSourceTests
{
    static readonly VersionEntry Us = new("us", "aaaa0000000000000000000000000001", "cccc0000000000000000000000000001", 70235, "1.60.1.70235");

    [Fact]
    public void ABuildFromTheVersionServiceCarriesItsConfigs()
    {
        var source = CdnSource.From("wow_classic_beta", Us);
        Assert.Equal(("wow_classic_beta", "us", "aaaa0000000000000000000000000001", "cccc0000000000000000000000000001", "1.60.1.70235"),
            (source.Product, source.Region, source.BuildConfig, source.CdnConfig, source.Version));
        Assert.Equal("tpr/wow", source.CdnPath);
    }

    [Theory]
    [InlineData("", "cccc0000000000000000000000000001")]
    [InlineData("not-a-hash", "cccc0000000000000000000000000001")]
    [InlineData("aaaa0000000000000000000000000001", "CCCC")]
    public void ConfigHashesMustBe32HexDigits(string build, string cdn)
    {
        Assert.Throws<ArgumentException>(() => new CdnSource("wow_classic_beta", "us", build, cdn, "1.60.1.70235"));
    }

    [Fact]
    public void TheCdnBuildReadsFromTheCdnWithNoInstallFolder()
    {
        var cache = Path.Combine(Path.GetTempPath(), "altrobe-cdn-test", Guid.NewGuid().ToString("N"));
        var build = GameStorage.CreateCdnBuild(CdnSource.From("wow_classic_beta", Us), cache);
        Assert.True(build.Settings.TryCDN);
        Assert.True(string.IsNullOrEmpty(build.Settings.BaseDir));
        Assert.Equal("wow_classic_beta", build.Settings.Product);
        Assert.Equal(cache, build.Settings.CacheDir);
        Assert.False(build.Settings.ListfileFallback);
        Assert.False(Directory.Exists(cache)); // nothing is fetched or written until the build is opened
    }
}
