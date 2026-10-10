using System.Collections.Concurrent;
using Altrobe.Core.Convert;

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

// Converted files for one build live in <build dir>/v<OutputVersion>/. The build's game index
// (tact/) sits beside them and survives a converter change.
public static class ConvertedCache
{
    // Returns the folder for the current converter version, after deleting output of other
    // versions and from before versioning (models/, textures/ and anims/ at the top level).
    public static string Prepare(string buildDir)
    {
        var current = $"v{AssetConverter.OutputVersion}";
        if (Directory.Exists(buildDir))
        {
            foreach (var dir in Directory.GetDirectories(buildDir))
            {
                var name = Path.GetFileName(dir);
                var stale = name is "models" or "textures" or "anims" || (name != current && name.Length > 1 && name[0] == 'v' && name[1..].All(char.IsAsciiDigit));
                if (stale) Directory.Delete(dir, true);
            }
        }
        var path = Path.Combine(buildDir, current);
        Directory.CreateDirectory(path);
        return path;
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

    public const string ConfigOverrideVariable = "ALTROBE_CONFIG_DIR";

    // Settings, such as the prompt box's API key. Kept apart from the cache, which players are told
    // they can delete.
    public static string ConfigRoot() => ConfigRoot(
        Environment.GetEnvironmentVariable(ConfigOverrideVariable),
        OperatingSystem.IsWindows(),
        OperatingSystem.IsMacOS(),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static string ConfigRoot(string? overridePath, bool isWindows, bool isMac, string? appData, string? xdgConfigHome, string home)
    {
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;
        if (isWindows && !string.IsNullOrEmpty(appData)) return Path.Combine(appData, "Altrobe");
        if (isMac) return Path.Combine(home, "Library", "Application Support", "Altrobe");
        return Path.Combine(string.IsNullOrWhiteSpace(xdgConfigHome) ? Path.Combine(home, ".config") : xdgConfigHome, "altrobe");
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
