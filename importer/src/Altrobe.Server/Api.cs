using System.Reflection;
using System.Text.Json;
using Altrobe.Core.Catalog;
using Altrobe.Core.Convert;
using Altrobe.Core.Install;
using Altrobe.Core.Resolve;

namespace Altrobe.Server;

public static class Api
{
    // A set as the web app and MCP clients see it: pieces flattened to item fields plus the slot.
    public static object SetJson(ItemSetInfo s) => new
    {
        s.SetId, s.Name, s.Internal, s.Unnamed, s.Quality,
        pieces = s.Pieces.Select(p => new { p.Slot, p.Item.ItemId, p.Item.Name, p.Item.Quality, p.Item.IconFileDataId, p.Item.Unnamed }),
        skipped = s.Skipped,
    };

    public static readonly string AppVersion = typeof(Api).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/v1");

        // The viewer tab's command channel (TabSession). Host and Origin are checked by LocalOnlyMiddleware.
        api.Map("/session", async (HttpContext ctx, TabSession tab) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) return ApiErrors.BadRequest("Connect with a WebSocket.");
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            await tab.RunAsync(ws, ctx.RequestAborted);
            return Results.Empty;
        });

        api.MapGet("/status", (IInstallSource installs, AppState state, ServerSettings settings) => Results.Json(Status(installs, state, settings)));
        AssistantApi.Map(api);
        AssistantPromptApi.Map(api);

        api.MapPost("/install", async (HttpRequest req, IInstallSource installs, IBuildSessionFactory sessions, AppState state, ServerSettings settings, ILogger<AppState> log) =>
        {
            // Requiring JSON also means a cross-site form post cannot select an install.
            if (!req.HasJsonContentType()) return ApiErrors.Result(StatusCodes.Status415UnsupportedMediaType, "unsupported_media_type", "Send the body as application/json.");
            string? path, productName;
            try
            {
                using var doc = await JsonDocument.ParseAsync(req.Body);
                path = doc.RootElement.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                productName = doc.RootElement.TryGetProperty("product", out var pr) && pr.ValueKind == JsonValueKind.String ? pr.GetString() : null;
            }
            catch (JsonException e)
            {
                return ApiErrors.BadRequest($"Body is not valid JSON: {e.Message}");
            }
            if (string.IsNullOrWhiteSpace(productName)) return ApiErrors.BadRequest("\"product\" is required.");

            WowInstall? install;
            if (!string.IsNullOrWhiteSpace(path))
            {
                install = InstallDiscovery.TryRead(path);
                if (install == null) return ApiErrors.NotFound("install_not_found", $"No .build.info in {path}.");
            }
            else
            {
                install = installs.Discover().FirstOrDefault();
                if (install == null) return ApiErrors.NotFound("install_not_found", $"No WoW install found. Set {InstallDiscovery.PathOverrideVariable} or pass \"path\".");
            }
            var product = install.Products.FirstOrDefault(p => p.Product == productName);
            if (product == null) return ApiErrors.NotFound("product_not_found", $"{install.Path} has no product {productName}. Found: {string.Join(", ", install.Products.Select(p => p.Product))}.");

            if (InstallDiscovery.MissingConfigs(install, product) is [_, ..] missing)
            {
                log.LogWarning("{Product} in {Path} names config files that are not on disk yet: {Missing}", product.Product, install.Path, string.Join(", ", missing));
                return ApiErrors.Result(StatusCodes.Status409Conflict, "install_updating", "Battle.net is still updating this install. Let the update finish, then try again.");
            }
            try
            {
                state.Select(new Selection(install, product, sessions.Open(install, product, settings.CacheRoot)));
            }
            catch (Exception e)
            {
                log.LogError(e, "Opening {Product} in {Path} failed", product.Product, install.Path);
                return ApiErrors.Result(StatusCodes.Status500InternalServerError, "open_failed", $"Could not open {product.Product}: {e.Message}");
            }
            return Results.Json(Status(installs, state, settings));
        });

        api.MapGet("/characters", (AppState state) => state.Current is not { } sel
            ? ApiErrors.NoInstall()
            : Results.Json(new { build = sel.Session.Build, races = sel.Session.Characters.Races() }));

        api.MapGet("/characters/{race}/{sex}", async (string race, string sex, HttpRequest req, AppState state) =>
        {
            if (state.Current is not { } sel) return ApiErrors.NoInstall();
            if (!int.TryParse(race, out var raceId)) return ApiErrors.BadRequest("race must be a number.");
            if (sex is not ("0" or "1")) return ApiErrors.BadRequest("sex must be 0 (male) or 1 (female).");
            if (ParseModelSet(req.Query["models"]) is not { } set) return ApiErrors.BadRequest("models must be hd or sd.");
            var classId = LookResolver.DefaultClassId;
            if (req.Query["classId"] is { Count: > 0 } c && !int.TryParse(c, out classId)) return ApiErrors.BadRequest("classId must be a number.");

            var look = await sel.Session.LookAsync(raceId, int.Parse(sex), classId, set);
            return look == null
                ? ApiErrors.NotFound("character_not_found", $"No playable {(set == ModelSet.Sd ? "SD" : "HD")} character for race {raceId} sex {sex}.")
                : Results.Json(look);
        });

        api.MapGet("/sets/search", (HttpRequest req, HttpResponse res, AppState state) =>
        {
            if (state.Current is not { } sel) return ApiErrors.NoInstall();
            if (!TryInt(req.Query["limit"], 20, out var limit) || !TryInt(req.Query["offset"], 0, out var offset)) return ApiErrors.BadRequest("limit and offset must be numbers.");
            // The search panel lists dev and NPC sets too, last; MCP tools leave them out (McpTools).
            var page = sel.Session.Sets.Search(new SetQuery(req.Query["q"], limit, offset, IncludeInternal: true));
            res.Headers["X-Total-Count"] = page.Total.ToString();
            return Results.Json(page.Sets.Select(SetJson));
        });

        // The sets panel's first view: the notable sets (NotableSets) a race can wear, for one of its
        // classes or, without class, for every class it can be.
        api.MapGet("/sets/notable", (HttpRequest req, AppState state) =>
        {
            if (state.Current is not { } sel) return ApiErrors.NoInstall();
            if (!int.TryParse(req.Query["race"], out var raceId) || sel.Session.Characters.Race(raceId) is not { } race)
                return ApiErrors.BadRequest("race must be a playable race ID from /characters.");
            int[] classes = int.TryParse(req.Query["class"], out var classId) ? [classId] : [.. race.Classes.Select(c => c.ClassId)];
            return Results.Json(sel.Session.Sets.Notable(race.Faction, classes).Select(g => new { g.Group, sets = g.Sets.Select(SetJson) }));
        });

        // Every item that is a piece of some set, so equipping a set can replace the last one.
        api.MapGet("/sets/pieces", (AppState state) =>
            state.Current is not { } sel ? ApiErrors.NoInstall() : Results.Json(sel.Session.Sets.PieceItemIds()));

        // Titles and names for the character's identity: CharTitles and NameGen, as the game has them.
        api.MapGet("/titles", (AppState state) =>
            state.Current is not { } sel ? ApiErrors.NoInstall() : Results.Json(sel.Session.Identity.Titles));

        api.MapGet("/names", (AppState state) =>
            state.Current is not { } sel ? ApiErrors.NoInstall() : Results.Json(sel.Session.Identity.Names));

        api.MapGet("/items/search", (HttpRequest req, HttpResponse res, AppState state) =>
        {
            if (state.Current is not { } sel) return ApiErrors.NoInstall();
            var q = req.Query;
            if (!TryInt(q["limit"], 50, out var limit) || !TryInt(q["offset"], 0, out var offset)) return ApiErrors.BadRequest("limit and offset must be numbers.");
            var qualities = new List<int>();
            foreach (var part in Split(q["quality"]))
            {
                if (!int.TryParse(part, out var v)) return ApiErrors.BadRequest("quality must be a number or a comma-separated list.");
                qualities.Add(v);
            }
            // The search panel lists dev and NPC items too, last; MCP tools leave them out (McpTools).
            // unnamed=1 also lists items with no name; without it an exact ID still finds them.
            var page = sel.Session.Items.Search(new ItemQuery(q["q"], Split(q["slot"]).ToList(), qualities, limit, offset, IncludeInternal: true, IncludeUnnamed: q["unnamed"] == "1"));
            res.Headers["X-Total-Count"] = page.Total.ToString();
            return Results.Json(page.Items);
        });

        api.MapGet("/items/{itemId}/resolved", (string itemId, HttpRequest req, AppState state) =>
        {
            if (state.Current is not { } sel) return ApiErrors.NoInstall();
            if (!int.TryParse(itemId, out var id)) return ApiErrors.BadRequest("itemId must be a number.");
            if (!int.TryParse(req.Query["race"], out var race)) return ApiErrors.BadRequest("race is required.");
            if (req.Query["sex"] is not { Count: 1 } s || s[0] is not ("0" or "1")) return ApiErrors.BadRequest("sex is required: 0 (male) or 1 (female).");
            if (ParseModelSet(req.Query["models"]) is not { } set) return ApiErrors.BadRequest("models must be hd or sd.");

            var r = sel.Session.ResolveItem(id, race, int.Parse(s[0]!), set);
            // BuildSession.ResolveItem names items the game files don't, from the catalog.
            if (r.Error == null) return Results.Json(r);
            return r.Name == ItemResolver.NoName
                ? ApiErrors.NotFound("item_not_found", $"Item {id} is not in this build.")
                : ApiErrors.NotFound("no_visual", $"Item {id} ({r.Name}) has no visual: {r.Error}.");
        });

        api.MapFallback(() => ApiErrors.NotFound("not_found", "No such API endpoint."));

        // One animation for a model's bones: converted on first request, cached per build.
        app.MapGet("/assets/{build}/anims/{model}/{file}", async (string build, string model, string file, HttpResponse res, AppState state) =>
        {
            if (state.Current is not { } sel) return ApiErrors.NoInstall();
            if (build != sel.Session.Build) return ApiErrors.NotFound("unknown_build", $"Build {build} is not the selected build ({sel.Session.Build}).");
            if (!uint.TryParse(model, out var fdid) || !file.EndsWith(".glb") || !int.TryParse(file[..^4], out var animId) || animId < 0)
                return ApiErrors.NotFound("not_found", $"No asset anims/{model}/{file}.");
            var path = await sel.Session.AnimationAsync(fdid, animId);
            res.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(path, "model/gltf-binary");
        });

        app.MapGet("/assets/{build}/{kind}/{file}", async (string build, string kind, string file, HttpResponse res, AppState state) =>
        {
            if (state.Current is not { } sel) return ApiErrors.NoInstall();
            if (build != sel.Session.Build) return ApiErrors.NotFound("unknown_build", $"Build {build} is not the selected build ({sel.Session.Build}).");
            var dot = file.LastIndexOf('.');
            if (dot <= 0 || !uint.TryParse(file[..dot], out var fdid)) return ApiErrors.NotFound("not_found", $"No asset {kind}/{file}.");
            var ext = file[(dot + 1)..];
            string path, type;
            switch (kind, ext)
            {
                case ("models", "glb"):
                    path = (await sel.Session.ModelAsync(fdid))[1];
                    type = "model/gltf-binary";
                    break;
                case ("models", "json"):
                    path = (await sel.Session.ModelAsync(fdid))[0];
                    type = "application/json";
                    break;
                case ("textures", "png"):
                    path = await sel.Session.TextureAsync(fdid);
                    type = "image/png";
                    break;
                default:
                    return ApiErrors.NotFound("not_found", $"No asset {kind}/{file}.");
            }
            // The URL names the build, and a build's files never change.
            res.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(path, type);
        });
        app.MapFallback("/assets/{**rest}", () => ApiErrors.NotFound("not_found", "No such asset."));
    }

    static object Status(IInstallSource installs, AppState state, ServerSettings settings)
    {
        var sel = state.Current;
        return new
        {
            version = AppVersion,
            installs = installs.Discover().Select(i => new
            {
                path = i.Path,
                products = i.Products.Select(p => new { product = p.Product, build = p.Version, buildName = p.BuildName, isForever = p.IsForever, active = p.Active, folder = p.Folder }),
            }),
            active = sel == null ? null : new
            {
                path = sel.Install.Path,
                product = sel.Product.Product,
                build = sel.Session.Build,
                buildName = sel.Session.BuildName,
                isForever = sel.Product.IsForever,
                // Asset URLs carry this (?v=) so converter changes get past the browser cache.
                assetVersion = AssetConverter.OutputVersion,
            },
            cacheFolder = settings.CacheRoot,
        };
    }

    static ModelSet? ParseModelSet(string? value) => value switch
    {
        null or "" or "hd" => ModelSet.Hd,
        "sd" => ModelSet.Sd,
        _ => null,
    };

    static bool TryInt(string? value, int fallback, out int result)
    {
        result = fallback;
        return string.IsNullOrEmpty(value) || int.TryParse(value, out result);
    }

    static IEnumerable<string> Split(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
