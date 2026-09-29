using Altrobe.Core.Storage;
using static TACTSharp.RootInstance;

namespace Altrobe.Core.Tests;

public class LocalFilesTests
{
    record FakeEntry(string Name, ContentFlags Flags);

    static readonly FakeEntry Standard = new("standard", ContentFlags.None);
    static readonly FakeEntry Hd = new("hd", ContentFlags.F00000001);
    static readonly FakeEntry LowViolence = new("low-violence", ContentFlags.LowViolence);
    static readonly FakeEntry HdLowViolence = new("hd-low-violence", ContentFlags.F00000001 | ContentFlags.LowViolence);

    [Fact]
    public void OrdersHdFirstThenStandardWithLowViolenceLast()
    {
        var ordered = LocalFiles.Order([LowViolence, Standard, HdLowViolence, Hd], e => e.Flags);
        Assert.Equal(["hd", "hd-low-violence", "standard", "low-violence"], ordered.Select(e => e.Name));
    }

    [Fact]
    public void ReturnsTheFirstEntryThatIsOnDisk()
    {
        var tried = new List<string>();
        var (bytes, entry) = LocalFiles.Open([Standard, Hd, LowViolence], e => e.Flags, e =>
        {
            tried.Add(e.Name);
            if (e == Hd) throw new FileNotFoundException("HD pack not installed");
            return [1, 2, 3];
        }, 42);

        Assert.Equal([1, 2, 3], bytes);
        Assert.Same(Standard, entry);
        Assert.Equal(["hd", "standard"], tried);
    }

    [Fact]
    public void ThrowsFileNotFoundWhenNoEntryIsOnDisk()
    {
        var ex = Assert.Throws<FileNotFoundException>(() =>
            LocalFiles.Open([Hd, Standard], e => e.Flags, _ => throw new FileNotFoundException(), 42));
        Assert.Contains("42", ex.Message);
    }

    [Fact]
    public void ThrowsFileNotFoundWhenTheFileIsNotInRoot()
    {
        Assert.Throws<FileNotFoundException>(() =>
            LocalFiles.Open(Array.Empty<FakeEntry>(), e => e.Flags, _ => [], 7));
    }

    [Fact]
    public void OtherReadErrorsAreNotSwallowed()
    {
        Assert.Throws<InvalidDataException>(() =>
            LocalFiles.Open([Hd, Standard], e => e.Flags, _ => throw new InvalidDataException("corrupt"), 42));
    }
}
