using TACTSharp;
using static TACTSharp.RootInstance;

namespace Altrobe.Core.Storage;

public static class LocalFiles
{
    const ContentFlags HighResTexture = ContentFlags.F00000001;

    // HD-texture entries first, then standard ones, low-violence variants last. The HD texture pack
    // is optional, so without it an HD entry is not on disk and the standard one is used.
    public static IReadOnlyList<T> Order<T>(IEnumerable<T> entries, Func<T, ContentFlags> flags) => entries
        .OrderByDescending(e => (flags(e) & HighResTexture) != 0)
        .ThenBy(e => (flags(e) & ContentFlags.LowViolence) != 0)
        .ToList();

    // Returns the first entry, in Order, whose data is actually on local disk.
    public static (byte[] bytes, T entry) Open<T>(IReadOnlyCollection<T> entries, Func<T, ContentFlags> flags, Func<T, byte[]> read, uint fdid)
    {
        if (entries.Count == 0) throw new FileNotFoundException($"FileDataID {fdid} is not in root");
        foreach (var e in Order(entries, flags))
        {
            try { return (read(e), e); }
            catch (FileNotFoundException) { }
        }
        throw new FileNotFoundException($"FileDataID {fdid}: none of {entries.Count} root entries is on local disk");
    }

    public static (byte[] bytes, ContentFlags flags) Open(BuildInstance build, uint fdid)
    {
        var entries = build.Root!.GetEntriesByFDID(fdid);
        var (bytes, entry) = Open(entries, e => e.contentFlags, e => build.OpenFileByCKey(e.md5.AsSpan()), fdid);
        return (bytes, entry.contentFlags);
    }
}
