namespace Altrobe.Core.Cache;

// Writes to a unique temp file in the target folder, then renames it into place, so a reader never
// sees a half-written file and two writers never interleave. Same-folder temp keeps the rename on
// one volume, where it is atomic.
public static class AtomicFile
{
    public static void WriteAllBytes(string path, ReadOnlySpan<byte> bytes)
    {
        var temp = TempPathFor(path);
        try
        {
            using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                f.Write(bytes);
                f.Flush(flushToDisk: true);
            }
            MoveIntoPlace(temp, path);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    // On Windows, replacing a file fails with a sharing violation while another writer is replacing
    // it or a scanner has it open. Those locks are brief, so retry before giving up.
    static void MoveIntoPlace(string temp, string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception e) when (attempt < 20 && e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(10 * attempt);
            }
        }
    }

    public static void WriteAllText(string path, string text) => WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes(text));

    static string TempPathFor(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
