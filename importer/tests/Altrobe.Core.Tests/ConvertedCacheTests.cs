using Altrobe.Core.Cache;
using Altrobe.Core.Convert;

namespace Altrobe.Core.Tests;

public class ConvertedCacheTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "altrobe-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    void Touch(string relative)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    [Fact]
    public void ConvertedFilesLiveUnderTheConverterVersion()
    {
        var dir = ConvertedCache.Prepare(_dir);
        Assert.Equal(Path.Combine(_dir, $"v{AssetConverter.OutputVersion}"), dir);
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void OlderConverterOutputIsDeletedAndTheGameIndexKept()
    {
        var current = $"v{AssetConverter.OutputVersion}";
        foreach (var f in new[] { "models/1.glb", "textures/2.png", "anims/3/4.glb", "v0/models/1.glb", $"{current}/models/5.glb", "tact/tpr/wow/data/index" })
            Touch(f);

        ConvertedCache.Prepare(_dir);

        Assert.Equal(new[] { "tact", current }, Directory.GetDirectories(_dir).Select(Path.GetFileName).Order().ToArray());
        Assert.True(File.Exists(Path.Combine(_dir, current, "models", "5.glb")));
    }
}
