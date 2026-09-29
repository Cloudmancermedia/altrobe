using System.Net;
using System.Text.Json;

namespace Altrobe.Server.Tests;

public class ApiTests : IClassFixture<ApiTests.Fixture>
{
    public sealed class Fixture : IDisposable
    {
        public TestApp App { get; } = new();
        public void Dispose() => App.Dispose();
    }

    readonly TestApp _app;

    public ApiTests(Fixture f) => _app = f.App;

    static async Task<JsonElement> Body(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    static async Task AssertError(HttpResponseMessage r, HttpStatusCode status, string code)
    {
        Assert.Equal(status, r.StatusCode);
        Assert.StartsWith("application/json", r.Content.Headers.ContentType?.ToString());
        var error = (await Body(r)).GetProperty("error");
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(error.GetProperty("message").GetString()));
    }

    [Theory]
    [InlineData("evil.com")]
    [InlineData("evil.com:5161")]
    [InlineData("localhost.evil.com")]
    [InlineData("127.0.0.1.nip.io")]
    public async Task RejectsRequestsWhoseHostIsNotLocal(string host)
    {
        var client = _app.Local();
        foreach (var path in new[] { "/api/v1/status", "/assets/1.60.1.70009/textures/1.png", "/" })
        {
            var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.Host = host;
            await AssertError(await client.SendAsync(req), HttpStatusCode.Forbidden, "forbidden_host");
        }
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost:5161")]
    [InlineData("127.0.0.1:5161")]
    [InlineData("LOCALHOST:5161")]
    public async Task AcceptsLocalHosts(string host)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/status");
        req.Headers.Host = host;
        Assert.Equal(HttpStatusCode.OK, (await _app.Local().SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task RejectsCrossSiteOriginsAndSendsNoCorsHeaders()
    {
        var client = _app.Local();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/status");
        req.Headers.Add("Origin", "https://evil.com");
        await AssertError(await client.SendAsync(req), HttpStatusCode.Forbidden, "forbidden_origin");

        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/install");
        preflight.Headers.Add("Origin", "http://127.0.0.1:5161");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        var r = await client.SendAsync(preflight);
        Assert.False(r.Headers.Contains("Access-Control-Allow-Origin"));

        var same = new HttpRequestMessage(HttpMethod.Get, "/api/v1/status");
        same.Headers.Add("Origin", "http://localhost:5161");
        var ok = await client.SendAsync(same);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.False(ok.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task StatusListsInstallsProductsAndTheCacheFolder()
    {
        var s = await Body(await _app.Local().GetAsync("/api/v1/status"));

        Assert.False(string.IsNullOrEmpty(s.GetProperty("version").GetString()));
        Assert.Equal(_app.CacheRoot, s.GetProperty("cacheFolder").GetString());
        var install = s.GetProperty("installs").EnumerateArray().Single();
        Assert.Equal(_app.InstallDir, install.GetProperty("path").GetString());
        var product = install.GetProperty("products").EnumerateArray().Single();
        Assert.Equal("wow_classic_beta", product.GetProperty("product").GetString());
        Assert.Equal(TestApp.Build, product.GetProperty("build").GetString());
        Assert.Equal("WOW-70009patch1.60.1_ForeverBeta", product.GetProperty("buildName").GetString());
        Assert.True(product.GetProperty("isForever").GetBoolean());
    }

    [Fact]
    public async Task InstallValidatesItsBody()
    {
        using var app = new TestApp();
        var client = app.Local();

        await AssertError(await client.PostAsync("/api/v1/install", new StringContent("""{"product":"wow_classic_beta"}""")), HttpStatusCode.UnsupportedMediaType, "unsupported_media_type");
        await AssertError(await client.PostAsync("/api/v1/install", TestApp.Json("{not json")), HttpStatusCode.BadRequest, "bad_request");
        await AssertError(await client.PostAsync("/api/v1/install", TestApp.Json("{}")), HttpStatusCode.BadRequest, "bad_request");
        await AssertError(await client.PostAsync("/api/v1/install", TestApp.Json("""{"product":"wow_nope"}""")), HttpStatusCode.NotFound, "product_not_found");
        await AssertError(await client.PostAsync("/api/v1/install", TestApp.Json($$"""{"path":"{{Path.Combine(app.InstallDir, "missing").Replace("\\", "\\\\")}}","product":"wow_classic_beta"}""")), HttpStatusCode.NotFound, "install_not_found");
    }

    [Fact]
    public async Task DataEndpointsReturn409UntilAnInstallIsSelected()
    {
        using var app = new TestApp();
        var client = app.Local();
        foreach (var path in new[]
        {
            "/api/v1/characters", "/api/v1/characters/2/0?models=hd", "/api/v1/items/search?q=robe",
            "/api/v1/items/1/resolved?race=2&sex=0", $"/assets/{TestApp.Build}/textures/{TestApp.TextureFdid}.png", $"/assets/{TestApp.Build}/models/1.glb",
        })
            await AssertError(await client.GetAsync(path), HttpStatusCode.Conflict, "no_install");

        var r = await client.PostAsync("/api/v1/install", TestApp.Json("""{"product":"wow_classic_beta"}"""));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var active = (await Body(r)).GetProperty("active");
        Assert.Equal("wow_classic_beta", active.GetProperty("product").GetString());
        Assert.Equal(TestApp.Build, active.GetProperty("build").GetString());
        Assert.Equal(app.InstallDir, active.GetProperty("path").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/characters")).StatusCode);
    }

    [Fact]
    public async Task SearchReturnsAPageOfItemsWithTheTotalInAHeader()
    {
        var client = await _app.Installed();

        var r = await client.GetAsync("/api/v1/items/search?q=ROBE&slot=chest&limit=2&offset=1");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("3", r.Headers.GetValues("X-Total-Count").Single());
        var items = (await Body(r)).EnumerateArray().ToList();
        Assert.Equal(["Plain Robe", "Robe of the Archmage"], items.Select(i => i.GetProperty("name").GetString()));
        var first = items[1];
        Assert.Equal(1, first.GetProperty("itemId").GetInt32());
        Assert.Equal("chest", first.GetProperty("slot").GetString());
        Assert.Equal(20, first.GetProperty("inventoryType").GetInt32());
        Assert.Equal(4, first.GetProperty("quality").GetInt32());
        Assert.Equal(1001, first.GetProperty("iconFileDataId").GetInt32());

        var byQuality = await Body(await client.GetAsync("/api/v1/items/search?quality=5,4"));
        Assert.Equal([1, 2], byQuality.EnumerateArray().Select(i => i.GetProperty("itemId").GetInt32()).Order());

        await AssertError(await client.GetAsync("/api/v1/items/search?limit=abc"), HttpStatusCode.BadRequest, "bad_request");
    }

    [Fact]
    public async Task CharactersListsPlayableRacesWithHdAndSd()
    {
        var client = await _app.Installed();
        var body = await Body(await client.GetAsync("/api/v1/characters"));

        Assert.Equal(TestApp.Build, body.GetProperty("build").GetString());
        var orc = body.GetProperty("races").EnumerateArray().Single();
        Assert.Equal(2, orc.GetProperty("race").GetInt32());
        var male = orc.GetProperty("sexes").EnumerateArray().Single();
        Assert.True(male.GetProperty("hd").GetBoolean());
        Assert.True(male.GetProperty("sd").GetBoolean());
        Assert.Equal("Warrior", orc.GetProperty("classes")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task CharacterLookValidatesItsParameters()
    {
        var client = await _app.Installed();
        await AssertError(await client.GetAsync("/api/v1/characters/2/0?models=xd"), HttpStatusCode.BadRequest, "bad_request");
        await AssertError(await client.GetAsync("/api/v1/characters/2/5"), HttpStatusCode.BadRequest, "bad_request");
        await AssertError(await client.GetAsync("/api/v1/characters/9/0"), HttpStatusCode.NotFound, "character_not_found");
    }

    [Fact]
    public async Task ResolvedItemHasTheSpikeShape()
    {
        var client = await _app.Installed();
        var r = await Body(await client.GetAsync("/api/v1/items/2/resolved?race=2&sex=0&models=sd"));

        Assert.Equal(TestApp.Build, r.GetProperty("build").GetString());
        Assert.Equal(2, r.GetProperty("itemId").GetInt32());
        Assert.Equal("sd", r.GetProperty("modelSet").GetString());
        Assert.Equal(13, r.GetProperty("inventoryType").GetInt32());
        foreach (var key in new[] { "itemAppearanceId", "itemDisplayInfoId", "geosetGroup", "attachmentGeosetGroup", "helmHideGeosetGroups", "models", "bodyTextures", "geosets", "attachments" })
            Assert.True(r.TryGetProperty(key, out _), key);
        var model = r.GetProperty("models")[0];
        Assert.Equal(148234, model.GetProperty("models")[0].GetProperty("fileDataId").GetInt32());
        Assert.Equal("neutral", model.GetProperty("models")[0].GetProperty("match").GetString());
        Assert.False(model.GetProperty("models")[0].TryGetProperty("position", out _));
        Assert.Equal(1, r.GetProperty("attachments")[0].GetProperty("attachmentId").GetInt32());

        await AssertError(await client.GetAsync("/api/v1/items/2/resolved?race=2"), HttpStatusCode.BadRequest, "bad_request");
        await AssertError(await client.GetAsync("/api/v1/items/2/resolved?race=2&sex=0&models=xd"), HttpStatusCode.BadRequest, "bad_request");
        await AssertError(await client.GetAsync("/api/v1/items/999/resolved?race=2&sex=0"), HttpStatusCode.NotFound, "item_not_found");
    }

    [Fact]
    public async Task TexturesConvertOnceAndAreServedImmutable()
    {
        using var app = new TestApp();
        var client = await app.Installed();
        var url = $"/assets/{TestApp.Build}/textures/{TestApp.TextureFdid}.png";

        var first = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("image/png", first.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", first.Headers.CacheControl?.ToString());
        Assert.Equal(TimeSpan.FromDays(365), first.Headers.CacheControl?.MaxAge);
        var png = await first.Content.ReadAsByteArrayAsync();
        Assert.Equal(0x89, png[0]);

        var opens = app.Files.Opens;
        var second = await client.GetAsync(url);
        Assert.Equal(png, await second.Content.ReadAsByteArrayAsync());
        Assert.Equal(opens, app.Files.Opens);
        Assert.True(File.Exists(Path.Combine(app.CacheRoot, "wow_classic_beta", TestApp.Build, "textures", $"{TestApp.TextureFdid}.png")));
    }

    [Fact]
    public async Task AssetErrorsAreJson()
    {
        var client = await _app.Installed();
        await AssertError(await client.GetAsync("/assets/1.0.0.1/textures/9001.png"), HttpStatusCode.NotFound, "unknown_build");
        await AssertError(await client.GetAsync($"/assets/{TestApp.Build}/textures/12.png"), HttpStatusCode.NotFound, "file_not_found");
        await AssertError(await client.GetAsync($"/assets/{TestApp.Build}/models/12.glb"), HttpStatusCode.NotFound, "file_not_found");
        await AssertError(await client.GetAsync($"/assets/{TestApp.Build}/models/12.txt"), HttpStatusCode.NotFound, "not_found");
        await AssertError(await client.GetAsync("/api/v1/nope"), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task ServesTheWebAppWithAFallbackToIndex()
    {
        var client = _app.Local();
        foreach (var path in new[] { "/", "/look/abc" })
        {
            var r = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Contains("test web app", await r.Content.ReadAsStringAsync());
        }
        var js = await client.GetAsync("/static/app.js");
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
        Assert.Equal("console.log(1)", await js.Content.ReadAsStringAsync());
    }
}
