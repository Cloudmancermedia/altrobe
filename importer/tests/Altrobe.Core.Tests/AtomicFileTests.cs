using Altrobe.Core.Cache;

namespace Altrobe.Core.Tests;

public class AtomicFileTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "altrobe-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void CreatesMissingFoldersAndLeavesNoTempFiles()
    {
        var path = Path.Combine(_dir, "a", "b", "file.bin");
        AtomicFile.WriteAllBytes(path, [1, 2, 3]);

        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
        Assert.Equal(["file.bin"], Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName));
    }

    [Fact]
    public void ConcurrentWritersNeverLeaveAMixedFile()
    {
        var path = Path.Combine(_dir, "shared.bin");
        var payloads = Enumerable.Range(1, 16).Select(i => Enumerable.Repeat((byte)i, 256 * 1024).ToArray()).ToArray();

        Parallel.ForEach(payloads, p => AtomicFile.WriteAllBytes(path, p));

        var result = File.ReadAllBytes(path);
        Assert.Equal(256 * 1024, result.Length);
        Assert.All(result, b => Assert.Equal(result[0], b));
        Assert.Single(Directory.GetFiles(_dir));
    }
}
