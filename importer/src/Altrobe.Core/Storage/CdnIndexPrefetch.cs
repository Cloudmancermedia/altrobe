namespace Altrobe.Core.Storage;

// Downloads a build's archive indexes before TACTSharp loads it. TACTSharp's GroupIndex fetches all of
// them at once (a Parallel.For over well over a thousand archives), and with that many connections at
// once each one can stall until HttpClient's 100 s timeout. It reads an index from
// {cache}/{cdnPath}/data/{archive}.index when the file is there (TACTSharp CDN.DownloadFile), so
// fetching them a few at a time into that path lets it skip its own downloads.
public static class CdnIndexPrefetch
{
    // Returns how many indexes it downloaded; ones already cached are skipped. Throws if any index
    // fails on every host after the retries, since TACTSharp would stall on it again.
    public static async Task<int> RunAsync(HttpClient http, IReadOnlyList<string> hosts, string cdnPath, IReadOnlyList<string> archives,
        string cacheDir, int parallel = 16, int attempts = 3, TimeSpan? retryDelay = null, CancellationToken ct = default)
    {
        if (hosts.Count == 0) throw new ArgumentException("No CDN hosts to download indexes from.", nameof(hosts));
        var dir = Path.Combine(cacheDir, cdnPath, "data");
        Directory.CreateDirectory(dir);
        var missing = archives.Where(a => !File.Exists(Path.Combine(dir, a + ".index"))).ToList();
        var failed = new List<string>();
        var fetched = 0;
        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = ct }, async (archive, token) =>
        {
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                foreach (var host in hosts)
                {
                    var url = $"https://{host}/{cdnPath}/data/{archive[..2]}/{archive[2..4]}/{archive}.index";
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                        timeout.CancelAfter(TimeSpan.FromSeconds(30));
                        using var response = await http.GetAsync(url, timeout.Token);
                        if (!response.IsSuccessStatusCode) continue;
                        var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
                        // Written whole, then moved into place, so TACTSharp never reads a partial index.
                        var temp = Path.Combine(dir, $"{archive}.{Guid.NewGuid():N}.tmp");
                        await File.WriteAllBytesAsync(temp, bytes, token);
                        File.Move(temp, Path.Combine(dir, archive + ".index"), true);
                        Interlocked.Increment(ref fetched);
                        return;
                    }
                    catch (Exception e) when (e is HttpRequestException or OperationCanceledException && !token.IsCancellationRequested) { }
                }
                await Task.Delay(retryDelay ?? TimeSpan.FromSeconds(2 << attempt), token);
            }
            lock (failed) failed.Add(archive);
        });
        if (failed.Count > 0)
            throw new InvalidOperationException($"{failed.Count} archive indexes could not be downloaded from {string.Join(", ", hosts)}: {string.Join(", ", failed.Order().Take(5))}{(failed.Count > 5 ? ", ..." : "")}");
        return fetched;
    }
}
