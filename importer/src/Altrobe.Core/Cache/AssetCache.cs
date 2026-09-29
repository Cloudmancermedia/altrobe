using System.Collections.Concurrent;

namespace Altrobe.Core.Cache;

// Per-build disk cache of converted files. Each key converts at most once at a time: concurrent
// requests for the same key share one conversion, and outputs land via AtomicFile.
public sealed class AssetCache(string root)
{
    readonly ConcurrentDictionary<string, Lazy<Task<string[]>>> _inFlight = new();

    public string Root { get; } = root;

    // Returns the absolute paths of `outputs` (relative to Root), producing them if any is missing.
    // `produce` returns one byte array per output, in the same order.
    public Task<string[]> GetOrCreateAsync(string key, IReadOnlyList<string> outputs, Func<byte[][]> produce)
    {
        // Callers name outputs with '/', e.g. "models/1.glb"; build the path from segments so Windows
        // gets one separator style and the returned paths compare equal to Path.Combine results.
        var paths = outputs.Select(o => Path.Combine([Root, .. o.Split('/')])).ToArray();
        if (paths.All(File.Exists)) return Task.FromResult(paths);

        var lazy = _inFlight.GetOrAdd(key, _ => new Lazy<Task<string[]>>(() => Task.Run(() => Produce(paths, produce))));
        return Finish(key, lazy);
    }

    async Task<string[]> Finish(string key, Lazy<Task<string[]>> lazy)
    {
        try
        {
            return await lazy.Value;
        }
        finally
        {
            // Drop the entry so a failure can be retried and memory does not grow with every key.
            _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<string[]>>>(key, lazy));
        }
    }

    static string[] Produce(string[] paths, Func<byte[][]> produce)
    {
        if (paths.All(File.Exists)) return paths;
        var bytes = produce();
        if (bytes.Length != paths.Length) throw new InvalidOperationException($"Expected {paths.Length} outputs, got {bytes.Length}");
        for (var i = 0; i < paths.Length; i++) AtomicFile.WriteAllBytes(paths[i], bytes[i]);
        return paths;
    }
}

public static class AppPaths
{
    public const string CacheOverrideVariable = "ALTROBE_CACHE_DIR";

    public static string CacheRoot() => CacheRoot(
        Environment.GetEnvironmentVariable(CacheOverrideVariable),
        OperatingSystem.IsWindows(),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static string CacheRoot(string? overridePath, bool isWindows, string? localAppData, string home)
    {
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;
        return isWindows && !string.IsNullOrEmpty(localAppData) ? Path.Combine(localAppData, "Altrobe") : Path.Combine(home, ".altrobe");
    }

    public static string BuildDir(string cacheRoot, string product, string build) =>
        Path.Combine(cacheRoot, SafeSegment(product), SafeSegment(build));

    static string SafeSegment(string s)
    {
        if (s.Length == 0 || s is "." or ".." || s.IndexOfAny([.. Path.GetInvalidFileNameChars(), '/', '\\']) >= 0)
            throw new ArgumentException($"Not a safe folder name: '{s}'");
        return s;
    }
}
