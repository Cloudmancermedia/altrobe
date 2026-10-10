using System.Net;
using Altrobe.Core.Storage;

namespace Altrobe.Core.Tests;

// CdnIndexPrefetch over a fake HTTP handler: no request leaves the machine.
public sealed class CdnIndexPrefetchTests : IDisposable
{
    readonly string _cache = Path.Combine(Path.GetTempPath(), "altrobe-prefetch-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cache)) Directory.Delete(_cache, true);
    }

    // Answers each index with its own name as bytes. Hosts in Down fail; it records the most
    // requests in flight at once.
    sealed class FakeCdn : HttpMessageHandler
    {
        public HashSet<string> Down { get; init; } = [];
        public List<Uri> Asked { get; } = [];
        public int MaxInFlight;
        int _inFlight;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Asked) Asked.Add(request.RequestUri!);
            var now = Interlocked.Increment(ref _inFlight);
            int seen;
            while ((seen = MaxInFlight) < now && Interlocked.CompareExchange(ref MaxInFlight, now, seen) != seen) { }
            try
            {
                await Task.Delay(20, ct);
                if (Down.Contains(request.RequestUri!.Host)) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes(request.RequestUri.Segments[^1])) };
            }
            finally { Interlocked.Decrement(ref _inFlight); }
        }
    }

    static string Archive(int n) => n.ToString("x32");
    string IndexPath(string archive) => Path.Combine(_cache, "tpr/wow", "data", archive + ".index");

    [Fact]
    public async Task DownloadsEachMissingIndexWhereTactSharpLooksAtMostNAtATime()
    {
        var archives = Enumerable.Range(1, 40).Select(Archive).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(IndexPath(archives[0]))!);
        File.WriteAllText(IndexPath(archives[0]), "already here");
        var cdn = new FakeCdn();

        var fetched = await CdnIndexPrefetch.RunAsync(new HttpClient(cdn), ["us.cdn.blizzard.com"], "tpr/wow", archives, _cache, parallel: 8);

        Assert.Equal(39, fetched);
        Assert.InRange(cdn.MaxInFlight, 2, 8);
        Assert.Equal("already here", File.ReadAllText(IndexPath(archives[0]))); // kept, not asked for
        Assert.DoesNotContain(cdn.Asked, u => u.AbsolutePath.Contains(archives[0]));
        var a = archives[1];
        Assert.Equal($"https://us.cdn.blizzard.com/tpr/wow/data/{a[..2]}/{a[2..4]}/{a}.index", cdn.Asked.Single(u => u.AbsolutePath.Contains(a)).ToString());
        Assert.Equal($"{a}.index", File.ReadAllText(IndexPath(a)));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(IndexPath(a))!, "*.tmp"));
    }

    [Fact]
    public async Task ADownHostFallsBackToTheNext()
    {
        var cdn = new FakeCdn { Down = ["level3.blizzard.com"] };
        var fetched = await CdnIndexPrefetch.RunAsync(new HttpClient(cdn), ["level3.blizzard.com", "us.cdn.blizzard.com"], "tpr/wow", [Archive(7)], _cache);
        Assert.Equal(1, fetched);
        Assert.True(File.Exists(IndexPath(Archive(7))));
    }

    [Fact]
    public async Task AnIndexNoHostServesFailsTheRunAndLeavesNothingBehind()
    {
        var cdn = new FakeCdn { Down = ["us.cdn.blizzard.com"] };
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CdnIndexPrefetch.RunAsync(new HttpClient(cdn), ["us.cdn.blizzard.com"], "tpr/wow", [Archive(9)], _cache, retryDelay: TimeSpan.Zero));
        Assert.Contains(Archive(9), e.Message);
        Assert.False(File.Exists(IndexPath(Archive(9))));
    }
}
