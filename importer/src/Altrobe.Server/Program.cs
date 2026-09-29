using System.Net;
using Altrobe.Server;
using Microsoft.Extensions.FileProviders;

var settings = ServerSettings.FromArgs(args);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
// Loopback IPv4 only: never reachable from another machine.
builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, settings.Port));
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<AppState>();
builder.Services.AddSingleton<IInstallSource, DiscoveredInstalls>();
builder.Services.AddSingleton<IBuildSessionFactory, LocalBuildSessions>();
builder.Services.AddSingleton<IBrowserLauncher, SystemBrowser>();
builder.Services.AddSingleton<TabSession>();
// MCP for Claude Code and other clients, at /mcp. Stateless: each request stands alone.
builder.Services.AddMcpServer(o => o.ServerInfo = new() { Name = "altrobe", Version = Api.AppVersion })
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<McpTools>();

var app = builder.Build();
settings = app.Services.GetRequiredService<ServerSettings>();

app.UseMiddleware<LocalOnlyMiddleware>();
app.UseMiddleware<ErrorMiddleware>();
app.UseWebSockets();

// The web app's bundle lives under /static/, so it never collides with /assets/{build}/.
if (settings.WebRoot is { } webRoot && Directory.Exists(webRoot))
{
    var files = new PhysicalFileProvider(Path.GetFullPath(webRoot));
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
    app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = files });
}
else
{
    app.MapGet("/", () => ApiErrors.NotFound("no_web_app", "No web app found. Build web/ (npm run build) or pass --web-root <folder>."));
}

Api.Map(app);
app.MapMcp("/mcp");

app.Lifetime.ApplicationStarted.Register(() =>
{
    var url = $"http://127.0.0.1:{settings.Port}/";
    app.Logger.LogInformation("Altrobe is running at {Url} (web app: {WebRoot})", url, settings.WebRoot ?? "none");
    if (settings.OpenBrowser) app.Services.GetRequiredService<IBrowserLauncher>().Open(url);
});

app.Run();

public partial class Program;
