using Altrobe.Core.Cache;

namespace Altrobe.Core.Tests;

public class AssetCacheTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "altrobe-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task ConcurrentRequestsForOneFileConvertItOnce()
    {
        var cache = new AssetCache(_dir);
        var calls = 0;
        var gate = new TaskCompletionSource();

        var requests = Enumerable.Range(0, 20).Select(_ => Task.Run(() => cache.GetOrCreateAsync("models/1", ["models/1.json", "models/1.glb"], () =>
        {
            Interlocked.Increment(ref calls);
            gate.Task.Wait();
            return [[1], [2, 2]];
        }))).ToList();
        await Task.Delay(50);
        gate.SetResult();
        var paths = await Task.WhenAll(requests);

        Assert.Equal(1, calls);
        Assert.All(paths, p => Assert.Equal(Path.Combine(_dir, "models", "1.json"), p[0]));
        Assert.Equal([2, 2], File.ReadAllBytes(Path.Combine(_dir, "models", "1.glb")));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_dir, "models")).Length);
    }

    [Fact]
    public async Task FilesAlreadyOnDiskAreNotConvertedAgain()
    {
        var first = new AssetCache(_dir);
        await first.GetOrCreateAsync("textures/5", ["textures/5.png"], () => [[9]]);

        var second = new AssetCache(_dir);
        var paths = await second.GetOrCreateAsync("textures/5", ["textures/5.png"], () => throw new InvalidOperationException("should not convert"));
        Assert.Equal([9], File.ReadAllBytes(paths[0]));
    }

    [Fact]
    public async Task AFailedConversionWritesNothingAndCanBeRetried()
    {
        var cache = new AssetCache(_dir);
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.GetOrCreateAsync("textures/6", ["textures/6.png"], () => throw new InvalidDataException("bad")));
        Assert.False(File.Exists(Path.Combine(_dir, "textures", "6.png")));

        var paths = await cache.GetOrCreateAsync("textures/6", ["textures/6.png"], () => [[1]]);
        Assert.True(File.Exists(paths[0]));
    }

    [Fact]
    public async Task APartialSetOfOutputsIsRebuilt()
    {
        var cache = new AssetCache(_dir);
        Directory.CreateDirectory(Path.Combine(_dir, "models"));
        File.WriteAllBytes(Path.Combine(_dir, "models", "3.json"), [0]);

        var calls = 0;
        await cache.GetOrCreateAsync("models/3", ["models/3.json", "models/3.glb"], () => { calls++; return [[1], [2]]; });

        Assert.Equal(1, calls);
        Assert.Equal([1], File.ReadAllBytes(Path.Combine(_dir, "models", "3.json")));
    }

    [Fact]
    public void CacheRootFollowsTheOverrideThenThePlatformDefault()
    {
        Assert.Equal("/tmp/x", AppPaths.CacheRoot("/tmp/x", isWindows: false, localAppData: null, home: "/Users/me"));
        Assert.Equal(Path.Combine("/Users/me", ".altrobe"), AppPaths.CacheRoot(null, isWindows: false, localAppData: null, home: "/Users/me"));
        Assert.Equal(Path.Combine(@"C:\Users\me\AppData\Local", "Altrobe"), AppPaths.CacheRoot("", isWindows: true, localAppData: @"C:\Users\me\AppData\Local", home: @"C:\Users\me"));
    }

    [Fact]
    public void BuildFolderIsPerProductAndBuild()
    {
        Assert.Equal(Path.Combine("/root", "wow_classic_beta", "1.60.1.70009"), AppPaths.BuildDir("/root", "wow_classic_beta", "1.60.1.70009"));
        Assert.Throws<ArgumentException>(() => AppPaths.BuildDir("/root", "../evil", "1.0"));
    }
}
