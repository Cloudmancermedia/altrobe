namespace Altrobe.Server.Tests;

public class StartupTests
{
    [Fact]
    public async Task OpensTheBrowserOnStartUnlessDisabled()
    {
        using (var app = new TestApp(openBrowser: true))
        {
            await app.Local().GetAsync("/api/v1/status");
            Assert.Equal(["http://127.0.0.1:5161/"], app.OpenedUrls);
        }
        using (var app = new TestApp(openBrowser: false))
        {
            await app.Local().GetAsync("/api/v1/status");
            Assert.Empty(app.OpenedUrls);
        }
    }

    [Fact]
    public void ReadsPortWebRootAndNoBrowserFlags()
    {
        var s = ServerSettings.FromArgs(["--port", "6000", "--web-root", "/tmp/web", "--no-browser"]);
        Assert.Equal((6000, "/tmp/web", false), (s.Port, s.WebRoot, s.OpenBrowser));

        var bad = ServerSettings.FromArgs(["--port", "99999"]);
        Assert.Equal(ServerSettings.DefaultPort, bad.Port);
    }
}
