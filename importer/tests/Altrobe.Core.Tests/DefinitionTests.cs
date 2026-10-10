using System.Net;
using Altrobe.Core.Tables;

namespace Altrobe.Core.Tests;

public class DefinitionTests : IDisposable
{
    readonly string _cache = Path.Combine(Path.GetTempPath(), "altrobe-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cache)) Directory.Delete(_cache, true);
    }

    sealed class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    static string Read(Stream s) => new StreamReader(s).ReadToEnd();

    [Fact]
    public void EveryTableTheAppLoadsIsVendored()
    {
        foreach (var table in GameTableNames.All)
        {
            Assert.True(VendoredDefinitions.HasDefinition(table), $"{table}.dbd is not vendored");
            Assert.True(VendoredDefinitions.Manifest.ContainsKey(table), $"{table} is not in the vendored manifest");
        }
    }

    [Fact]
    public void UsesTheVendoredDefinitionWhenItListsTheBuild()
    {
        var http = new CountingHandler(_ => throw new InvalidOperationException("no network expected"));
        var provider = new DefinitionProvider(_cache, http);

        var text = Read(provider.StreamForTableName("ChrModelAltVariant", "1.60.1.70009"));

        Assert.Contains("VariantChrModelID", text);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public void DownloadsAndCachesADefinitionThatIsNotVendored()
    {
        var http = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("COLUMNS\nint ID\n\nBUILD 1.60.1.70009\n") });
        var provider = new DefinitionProvider(_cache, http);

        Assert.Contains("COLUMNS", Read(provider.StreamForTableName("SomeNewTable", "1.60.1.70009")));
        Assert.Single(http.Requests);
        Assert.Equal("raw.githubusercontent.com", http.Requests[0].Host);
        Assert.EndsWith("/definitions/SomeNewTable.dbd", http.Requests[0].AbsolutePath);

        // Second read comes from the cache folder.
        var again = new DefinitionProvider(_cache, http);
        Assert.Contains("COLUMNS", Read(again.StreamForTableName("SomeNewTable", "1.60.1.70009")));
        Assert.Single(http.Requests);
    }

    [Fact]
    public void RefreshesAVendoredDefinitionThatDoesNotListTheBuild()
    {
        var http = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("BUILD 9.9.9.99999\nfresh") });
        var provider = new DefinitionProvider(_cache, http);

        Assert.Contains("fresh", Read(provider.StreamForTableName("ChrModelAltVariant", "9.9.9.99999")));
        Assert.Single(http.Requests);
    }

    [Fact]
    public void FallsBackToTheVendoredDefinitionWhenOffline()
    {
        var http = new CountingHandler(_ => throw new HttpRequestException("offline"));
        var provider = new DefinitionProvider(_cache, http);

        Assert.Contains("VariantChrModelID", Read(provider.StreamForTableName("ChrModelAltVariant", "9.9.9.99999")));
    }

    [Fact]
    public void MissingEverywhereIsAClearError()
    {
        var http = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var provider = new DefinitionProvider(_cache, http);

        var ex = Assert.Throws<FileNotFoundException>(() => provider.StreamForTableName("NoSuchTable", "1.60.1.70009"));
        Assert.Contains("NoSuchTable", ex.Message);
    }

    [Theory]
    [InlineData("BUILD 1.60.1.70009", "1.60.1.70009", true)]
    [InlineData("BUILD 1.60.1.69876, 1.60.1.70009", "1.60.1.70009", true)]
    [InlineData("BUILD 1.60.1.69876, 1.60.1.69893", "1.60.1.70009", false)]
    [InlineData("BUILD 1.13.0.28211-1.13.7.39605", "1.13.2.30000", true)]
    [InlineData("BUILD 1.13.0.28211-1.13.7.39605", "1.14.0.40000", false)]
    [InlineData("LAYOUT 1\nBUILD 9.0.1.1-9.0.1.5\n$id$ID<32>\n\nLAYOUT 2\nBUILD 1.60.1.70009", "1.60.1.70009", true)]
    public void ReadsBuildLinesAndRanges(string dbd, string build, bool expected)
    {
        Assert.Equal(expected, DbdBuilds.Lists(dbd, build));
    }

    [Fact]
    public void RowsConvertNumericTypesAndArrays()
    {
        var row = new Row(new Dictionary<string, object?>
        {
            ["ID"] = 5,
            ["Flags"] = (uint)0x80000000,
            ["Small"] = (byte)3,
            ["Signed"] = (sbyte)-1,
            ["Name_lang"] = "Orc",
            ["Pair"] = new ushort[] { 7, 8 },
        });

        Assert.Equal(5, row.Int("ID"));
        Assert.Equal(0x80000000L, row.Long("Flags"));
        Assert.Equal(3, row.Int("Small"));
        Assert.Equal(-1, row.Int("Signed"));
        Assert.Equal("Orc", row.Str("Name_lang"));
        Assert.Equal([7, 8], row.Ints("Pair"));
        Assert.Equal(8, row.Int("Pair", 1));
        Assert.Equal(0, row.Int("Missing"));
        Assert.Equal("", row.Str("Missing"));
        Assert.Empty(row.Ints("Missing"));
    }
}
