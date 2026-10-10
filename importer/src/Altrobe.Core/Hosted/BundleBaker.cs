using System.Collections.Concurrent;
using System.Text.Json;
using Altrobe.Core.Builds;
using Altrobe.Core.Catalog;
using Altrobe.Core.Convert;
using Altrobe.Core.Resolve;

namespace Altrobe.Core.Hosted;

// What a bake reads: one build's catalogs, looks, resolved items and converted assets. BuildSession
// provides it (BuildSessionSource); tests use a fake.
public interface IBundleSource
{
    string Product { get; }
    string Build { get; }
    string? BuildName { get; }
    IReadOnlyList<RaceInfo> Races { get; }
    IReadOnlyList<ItemSummary> Items { get; }
    IReadOnlyList<ItemSetInfo> Sets { get; }
    Task<CharacterLook?> LookAsync(int race, int sex, ModelSet set);
    ResolvedItem ResolveItem(int itemId, int race, int sex, ModelSet set);
    // [meta json, glb] paths of the converted model; throws FileNotFoundException when it is missing.
    Task<string[]> ModelAsync(uint fdid);
    Task<string> TextureAsync(uint fdid);
    Task<string> AnimationAsync(uint fdid, int animId);
    // What /api/v1/sets/notable and /api/v1/sets/pieces serve.
    IReadOnlyList<SetGroup> Notable(string? faction, IReadOnlyCollection<int> classIds);
    IReadOnlyList<int> SetPieceItemIds { get; }
    // What /api/v1/titles and /api/v1/names serve.
    IReadOnlyList<TitleInfo> Titles { get; }
    IReadOnlyDictionary<int, RaceNames> Names { get; }
}

public sealed class BuildSessionSource(BuildSession session) : IBundleSource
{
    public string Product => session.Product;
    public string Build => session.Build;
    public string? BuildName => session.BuildName;
    public IReadOnlyList<RaceInfo> Races => session.Characters.Races();
    public IReadOnlyList<ItemSummary> Items => session.Items.All;
    public IReadOnlyList<ItemSetInfo> Sets => session.Sets.All;
    public Task<CharacterLook?> LookAsync(int race, int sex, ModelSet set) => session.LookAsync(race, sex, LookResolver.DefaultClassId, set);
    public ResolvedItem ResolveItem(int itemId, int race, int sex, ModelSet set) => session.ResolveItem(itemId, race, sex, set);
    public Task<string[]> ModelAsync(uint fdid) => session.ModelAsync(fdid);
    public Task<string> TextureAsync(uint fdid) => session.TextureAsync(fdid);
    public Task<string> AnimationAsync(uint fdid, int animId) => session.AnimationAsync(fdid, animId);
    public IReadOnlyList<SetGroup> Notable(string? faction, IReadOnlyCollection<int> classIds) => session.Sets.Notable(faction, classIds);
    public IReadOnlyList<int> SetPieceItemIds => session.Sets.PieceItemIds();
    public IReadOnlyList<TitleInfo> Titles => session.Identity.Titles;
    public IReadOnlyDictionary<int, RaceNames> Names => session.Identity.Names;
}

// LimitItems / LimitSets: bake only the first N (a limited set still brings its pieces), for a quick
// end-to-end check. PreviousManifest: the last published build's manifest.json; a count that drops by
// more than MaxDrop (a fraction) fails the run, so a silent loss never reaches the site.
// Parallelism: how many files convert at once; null for one per CPU core.
public sealed record BakeOptions(string OutputDir, int? LimitItems = null, int? LimitSets = null, string? PreviousManifest = null, double MaxDrop = 0.05, int? Parallelism = null);

// AssetVersion: AssetConverter.OutputVersion, the folder the converted assets are under.
public sealed record BundleManifest(
    string Product, string Build, string? BuildName, int AssetVersion,
    Dictionary<string, int> Counts, IReadOnlyList<int> MissingModels, IReadOnlyList<int> MissingTextures, IReadOnlyList<string> MissingLooks,
    IReadOnlyList<string> MissingAnimations);

public sealed record BakeResult(BundleManifest Manifest, IReadOnlyList<string> Failures)
{
    public bool Ok => Failures.Count == 0;
}

// Writes one build as the static bundle the hosted site reads (docs/hosted-pipeline.md, "Bundle layout"):
//   {product}/current.json                       the build the site shows, and its asset folder
//   {product}/{build}/manifest.json              counts and anything that failed to convert
//   {product}/{build}/characters.json            races and every base look ("race-sex-hd|sd")
//   {product}/{build}/catalog.json, sets.json    for search in the browser
//   {product}/{build}/notable.json               notable sets per race: "all" and each class ID
//   {product}/{build}/set-pieces.json            every item that is a piece of some set
//   {product}/{build}/items/{itemId}.json        the item resolved for every race, sex and body kind
//   {product}/{build}/v{N}/models/{fdid}.glb|.json, v{N}/textures/{fdid}.png, v{N}/anims/{model}/{id}.glb
// Converted assets sit under the converter version (AssetConverter.OutputVersion), as in the local
// cache, so a converter change publishes new files instead of overwriting cached ones; the JSON comes
// from the resolvers and catalogs, not the converter, so it stays beside them. Below v{N} the paths
// keep the local server's shape. current.json moves only when the count check passes.
public static class BundleBaker
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    static readonly (ModelSet set, string key)[] Bodies = [(ModelSet.Hd, "hd"), (ModelSet.Sd, "sd")];

    // The animations the site offers, by AnimationNames name: every
    // cast, all melee and special melee attacks, ranged attacks, movement, the space bar (jump), the
    // mounted pose and a few emotes. A body that lacks one simply doesn't offer it; looks list only
    // these. All animations would be about 1.7 GB per build, most of a bundle.
    static readonly string[] AnimationList =
    [
        // Movement and jump
        "Stand", "Walk", "Run", "Walkbackwards", "ShuffleLeft", "ShuffleRight", "Sprint", "JumpStart", "Jump", "JumpEnd", "JumpLandRun",
        // Casting
        "Spell", "SpellPrecast", "SpellCast", "SpellCastArea", "ReadySpellDirected", "ReadySpellOmni", "SpellCastDirected", "SpellCastOmni",
        "ChannelCastDirected", "ChannelCastOmni",
        // Melee: attacks, ready stances, parries and blocks
        "AttackUnarmed", "Attack1H", "Attack2H", "Attack2HL", "AttackOff", "Attack1HPierce", "Attack2HLoosePierce", "AttackUnarmedOff",
        "ReadyUnarmed", "Ready1H", "Ready2H", "Ready2HL", "ParryUnarmed", "Parry1H", "Parry2H", "Parry2HL", "ShieldBlock", "Dodge",
        // Special melee
        "Special1H", "Special2H", "SpecialUnarmed", "ShieldBash", "Kick", "Whirlwind", "BattleRoar",
        // Ranged (Classic crossbows use the rifle animations; AttackCrossbow came later)
        "ReadyBow", "AttackBow", "ReadyRifle", "AttackRifle", "ReadyThrown", "AttackThrown",
        // Mounted (the rider's pose; the mount's own animations come with mounts) and emotes
        "Mount", "MountSpecial", "EmoteWave", "EmoteCheer", "EmoteDance", "EmoteLaugh",
    ];
    public static readonly IReadOnlyList<int> Animations =
        AnimationList.Select(n => Enumerable.Range(0, AnimationNames.Count).First(i => AnimationNames.Name(i) == n)).ToList();

    public static async Task<BakeResult> BakeAsync(IBundleSource source, BakeOptions options, Action<string>? log = null)
    {
        var productDir = Path.Combine(options.OutputDir, source.Product);
        var buildDir = Path.Combine(productDir, source.Build);
        var assetDir = Path.Combine(buildDir, $"v{AssetConverter.OutputVersion}");
        Directory.CreateDirectory(buildDir);
        var anims = new SortedSet<(int model, int anim)>();
        var models = new SortedSet<int>();
        var textures = new SortedSet<int>();

        // Characters: every race, sex and body kind the build has.
        var looks = new Dictionary<string, CharacterLook>();
        var missingLooks = new List<string>();
        foreach (var race in source.Races)
            foreach (var sex in race.Sexes)
                foreach (var (set, key) in Bodies)
                {
                    if (set == ModelSet.Hd ? !sex.Hd : !sex.Sd) continue;
                    var id = $"{race.Race}-{sex.Sex}-{key}";
                    try
                    {
                        if (await source.LookAsync(race.Race, sex.Sex, set) is not { } look) { missingLooks.Add(id); continue; }
                        var offered = look.Animations.Where(a => Animations.Contains(a.Id)).ToList();
                        looks[id] = look with { Animations = offered };
                        models.Add(look.ModelFileDataId);
                        foreach (var a in offered) anims.Add((look.ModelFileDataId, a.Id));
                        foreach (var l in look.Layers) textures.Add(l.FileDataId);
                        foreach (var l in look.Options.SelectMany(o => o.Choices).SelectMany(c => c.Layers)) textures.Add(l.FileDataId);
                    }
                    catch (FileNotFoundException) { missingLooks.Add(id); }
                }
        Write(Path.Combine(buildDir, "characters.json"), new { build = source.Build, races = source.Races, looks });
        log?.Invoke($"{looks.Count} looks");

        // Catalog and sets. A limited bake keeps the limited sets' pieces, so each set is whole.
        var sets = options.LimitSets is { } ls ? source.Sets.Take(ls).ToList() : source.Sets;
        var items = options.LimitItems is { } li
            ? source.Items.Take(li).Concat(source.Items.Where(i => sets.SelectMany(s => s.Pieces).Any(p => p.Item.ItemId == i.ItemId))).DistinctBy(i => i.ItemId).ToList()
            : source.Items;
        // Names from cmangos (GPL-3.0) travel with their credit.
        string[] credits = items.Any(i => i.NameSource == "cmangos") ? [ClassicItemNames.Credit] : [];
        Write(Path.Combine(buildDir, "catalog.json"), new { build = source.Build, credits, items });
        Write(Path.Combine(buildDir, "sets.json"), new { build = source.Build, sets });
        // "all" is the race's own classes together; each class ID is baked for every race, its own or not,
        // so "Any class" can show a Tauren in priest sets. The faction still follows the race (PvP sets).
        var everyClass = source.Races.SelectMany(r => r.Classes).Select(c => c.ClassId).Distinct().Order().ToList();
        var notable = source.Races.ToDictionary(r => r.Race.ToString(), r =>
        {
            var groups = new Dictionary<string, IReadOnlyList<SetGroup>> { ["all"] = source.Notable(r.Faction, [.. r.Classes.Select(c => c.ClassId)]) };
            foreach (var c in everyClass) groups[c.ToString()] = source.Notable(r.Faction, [c]);
            return groups;
        });
        Write(Path.Combine(buildDir, "notable.json"), notable);
        Write(Path.Combine(buildDir, "set-pieces.json"), source.SetPieceItemIds);
        Write(Path.Combine(buildDir, "titles.json"), source.Titles);
        Write(Path.Combine(buildDir, "names.json"), source.Names);
        foreach (var i in items) if (i.IconFileDataId > 0) textures.Add(i.IconFileDataId);

        // Each item for every body.
        var bodies = source.Races.SelectMany(r => r.Sexes.SelectMany(s => Bodies.Where(b => b.set == ModelSet.Hd ? s.Hd : s.Sd).Select(b => (r.Race, s.Sex, b.set, b.key)))).ToList();
        Directory.CreateDirectory(Path.Combine(buildDir, "items"));
        var resolved = 0;
        foreach (var item in items)
        {
            var variants = new Dictionary<string, ResolvedItem>();
            foreach (var (race, sex, set, key) in bodies)
            {
                var r = source.ResolveItem(item.ItemId, race, sex, set);
                variants[$"{race}-{sex}-{key}"] = r;
                foreach (var a in r.Attachments)
                {
                    models.Add(a.ModelFileDataId);
                    if (a.TextureFileDataId is { } t) textures.Add(t);
                }
                foreach (var e in r.Effects) models.Add(e.ModelFileDataId);
                foreach (var m in r.Models)
                {
                    foreach (var p in m.Models) models.Add(p.FileDataId);
                    foreach (var p in m.Textures) textures.Add(p.FileDataId);
                }
                foreach (var p in r.BodyTextures.SelectMany(b => b.Textures)) textures.Add(p.FileDataId);
                foreach (var t in r.BodyReplaceableTextures.Values) textures.Add(t);
            }
            Write(Path.Combine(buildDir, "items", $"{item.ItemId}.json"), new { itemId = item.ItemId, variants });
            resolved++;
        }
        log?.Invoke($"{resolved} items resolved for {bodies.Count} bodies");

        // Models first, since each names textures of its own. Conversions run in parallel: the asset
        // cache shares an in-flight conversion between callers, and game file reads take GameStorage's
        // lock. Missing lists are sorted afterwards so the manifest is the same from run to run.
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = options.Parallelism ?? Environment.ProcessorCount };
        Directory.CreateDirectory(Path.Combine(assetDir, "models"));
        var missingModels = new ConcurrentBag<int>();
        var copiedModels = 0;
        await Parallel.ForEachAsync(models.Where(f => f > 0).ToList(), parallel, async (fdid, _) =>
        {
            try
            {
                var paths = await source.ModelAsync((uint)fdid);
                File.Copy(paths[0], Path.Combine(assetDir, "models", $"{fdid}.json"), true);
                File.Copy(paths[1], Path.Combine(assetDir, "models", $"{fdid}.glb"), true);
                var named = AssetConverter.TextureIds(File.ReadAllBytes(paths[0]));
                lock (textures) foreach (var t in named) textures.Add(t);
                Interlocked.Increment(ref copiedModels);
            }
            catch (Exception e) when (e is FileNotFoundException or InvalidDataException) { missingModels.Add(fdid); }
        });
        Directory.CreateDirectory(Path.Combine(assetDir, "textures"));
        var missingTextures = new ConcurrentBag<int>();
        var copiedTextures = 0;
        await Parallel.ForEachAsync(textures.Where(f => f > 0).ToList(), parallel, async (fdid, _) =>
        {
            try
            {
                File.Copy(await source.TextureAsync((uint)fdid), Path.Combine(assetDir, "textures", $"{fdid}.png"), true);
                Interlocked.Increment(ref copiedTextures);
            }
            catch (Exception e) when (e is FileNotFoundException or InvalidDataException) { missingTextures.Add(fdid); }
        });
        var missingAnims = new ConcurrentBag<string>();
        var copiedAnims = 0;
        await Parallel.ForEachAsync(anims.ToList(), parallel, async (a, _) =>
        {
            try
            {
                var dir = Path.Combine(assetDir, "anims", a.model.ToString());
                Directory.CreateDirectory(dir);
                File.Copy(await source.AnimationAsync((uint)a.model, a.anim), Path.Combine(dir, $"{a.anim}.glb"), true);
                Interlocked.Increment(ref copiedAnims);
            }
            catch (Exception e) when (e is FileNotFoundException or InvalidDataException) { missingAnims.Add($"{a.model}/{a.anim}"); }
        });
        log?.Invoke($"{copiedModels} models, {copiedTextures} textures, {copiedAnims} animations ({missingModels.Count}, {missingTextures.Count} and {missingAnims.Count} missing)");

        var counts = new Dictionary<string, int>
        {
            ["races"] = source.Races.Count, ["looks"] = looks.Count, ["items"] = items.Count, ["sets"] = sets.Count,
            ["resolvedItems"] = resolved, ["models"] = copiedModels, ["textures"] = copiedTextures, ["animations"] = copiedAnims,
        };
        var manifest = new BundleManifest(source.Product, source.Build, source.BuildName, AssetConverter.OutputVersion, counts,
            [.. missingModels.Order()], [.. missingTextures.Order()], missingLooks, [.. missingAnims.Order(StringComparer.Ordinal)]);
        Write(Path.Combine(buildDir, "manifest.json"), manifest);

        var failures = options.PreviousManifest is { } prev ? CompareCounts(ReadCounts(prev), counts, options.MaxDrop) : [];
        if (failures.Count == 0)
            Write(Path.Combine(productDir, "current.json"), new
            {
                product = source.Product, build = source.Build, buildName = source.BuildName, path = $"{source.Build}/",
                assetVersion = AssetConverter.OutputVersion, assetPath = $"{source.Build}/v{AssetConverter.OutputVersion}/",
            });
        return new BakeResult(manifest, failures);
    }

    // One failure per count that fell by more than maxDrop, including counts that vanished.
    public static List<string> CompareCounts(Dictionary<string, int> previous, Dictionary<string, int> current, double maxDrop)
    {
        var failures = new List<string>();
        foreach (var (key, before) in previous)
        {
            if (before <= 0) continue;
            var now = current.GetValueOrDefault(key);
            var drop = (before - now) / (double)before;
            if (drop > maxDrop) failures.Add($"{key} dropped from {before} to {now} ({drop:P0}; the limit is {maxDrop:P0})");
        }
        return failures;
    }

    static Dictionary<string, int> ReadCounts(string manifestPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return doc.RootElement.GetProperty("counts").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
    }

    static void Write(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    }
}
