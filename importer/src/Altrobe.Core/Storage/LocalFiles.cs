using TACTSharp;
using static TACTSharp.RootInstance;

namespace Altrobe.Core.Storage;

public static class LocalFiles
{
    const ContentFlags HighResTexture = ContentFlags.F00000001;

    // Entries for the wanted locale (or for every locale) first; then HD-texture entries, then standard
    // ones, low-violence variants last. A local install holds only its own locale's files, so the
    // others fail to open; from the CDN every locale is there, and the first in root was Korean.
    // The HD texture pack is optional, so without it an HD entry is not on disk and the standard one is used.
    public static IReadOnlyList<T> Order<T>(IEnumerable<T> entries, Func<T, ContentFlags> flags, Func<T, LocaleFlags>? locale = null, LocaleFlags want = LocaleFlags.enUS) => entries
        .OrderByDescending(e => locale == null || (locale(e) & want) != 0) // an every-locale entry carries the wanted bit too
        .ThenByDescending(e => (flags(e) & HighResTexture) != 0)
        .ThenBy(e => (flags(e) & ContentFlags.LowViolence) != 0)
        .ToList();

    // Returns the first entry, in Order, whose data is actually on local disk.
    public static (byte[] bytes, T entry) Open<T>(IReadOnlyCollection<T> entries, Func<T, ContentFlags> flags, Func<T, byte[]> read, uint fdid, Func<T, LocaleFlags>? locale = null)
    {
        if (entries.Count == 0) throw new FileNotFoundException($"FileDataID {fdid} is not in root");
        foreach (var e in Order(entries, flags, locale))
        {
            try { return (read(e), e); }
            catch (FileNotFoundException) { }
        }
        throw new FileNotFoundException($"FileDataID {fdid}: none of {entries.Count} root entries is on local disk");
    }

    public static (byte[] bytes, ContentFlags flags) Open(BuildInstance build, uint fdid)
    {
        var entries = build.Root!.GetEntriesByFDID(fdid);
        var (bytes, entry) = Open(entries, e => e.contentFlags, e => build.OpenFileByCKey(e.md5.AsSpan()), fdid, e => e.localeFlags);
        return (bytes, entry.contentFlags);
    }
}
