using System.Text.Json;
using Altrobe.Core.Catalog;
using Altrobe.Core.Convert;
using Altrobe.Core.Hosted;
using Altrobe.Core.Resolve;

namespace Altrobe.Core.Tests;

public class BundleBakerTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "altrobe-bake-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    // One race with a male HD and SD body, three items and a set. Models are small JSON + GLB files
    // the fake writes itself; model 7000 names texture 9100, and model 7402 is missing. Bodies have
    // Stand (0), Run (5) and Fly (135), which is not on the bake's short list.
    sealed class FakeSource(string dir) : IBundleSource
    {
        public string Product => "wow_classic_beta";
        public string Build => "1.60.1.70235";
        public string? BuildName => "WOW-70235patch1.60.1_ForeverBeta";
        public IReadOnlyList<RaceInfo> Races { get; } = [
            new(2, "Orc", "Orc", "Orc", [new SexAvailability(0, true, true, 3, 259)], [new ClassInfo(1, "Warrior")], "horde"),
            // No bodies, so it adds no looks or items; it brings a class the Orc can't be.
            new(5, "Undead", "Undead", "Scourge", [], [new ClassInfo(5, "Priest")], "horde"),
        ];
        // Item 2's name comes from cmangos (ClassicItemNames), so the catalog carries the credit.
        public IReadOnlyList<ItemSummary> Items { get; } = [Item(1, 9201), Item(2, 9202) with { NameSource = "cmangos" }, Item(3, 0)];
        public IReadOnlyList<ItemSetInfo> Sets { get; } = [new(500, "Battlegear", [new SetPiece("chest", Item(3, 0))], [], false)];
        public List<uint> ModelsAsked { get; } = [];
        // With Delay set, each model conversion waits that long, so overlapping calls show in MaxRunning.
        public int Delay { get; init; }
        public int MaxRunning;
        int _running;

        static ItemSummary Item(int id, int icon) => new(id, $"Item {id}", "chest", 5, 2, icon);

        public Task<CharacterLook?> LookAsync(int race, int sex, ModelSet set) => Task.FromResult<CharacterLook?>(new CharacterLook
        {
            Race = race, Sex = sex, ModelFileDataId = set == ModelSet.Hd ? 7000 : 7001,
            Layers = [new TextureLayer { FileDataId = 9300 }],
            // A choice the player can switch to paints texture 9301.
            Options = [new OptionInfo(1, "Skin Color", 0, 0, 10, [], [new ChoiceInfo(11, "Dark", 1, null, false, [], [new TextureLayer { FileDataId = 9301 }])])],
            Animations = [new AnimationInfo(0, "Stand"), new AnimationInfo(5, "Run"), new AnimationInfo(135, "Fly")],
        });

        public List<(uint, int)> AnimationsAsked { get; } = [];

        public Task<string> AnimationAsync(uint fdid, int animId)
        {
            lock (AnimationsAsked) AnimationsAsked.Add((fdid, animId));
            Directory.CreateDirectory(dir);
            var glb = Path.Combine(dir, $"anim-{fdid}-{animId}.glb");
            File.WriteAllBytes(glb, [6, 7, 8]);
            return Task.FromResult(glb);
        }

        // The Orc warrior's notable sets: set 500 as its tier 2.
        public IReadOnlyList<SetGroup> Notable(string? faction, IReadOnlyCollection<int> classIds) =>
            faction == "horde" && classIds.Contains(1) ? [new SetGroup("Tier 2", [Sets[0]])] : [];

        public IReadOnlyList<int> SetPieceItemIds => [3];

        public IReadOnlyList<TitleInfo> Titles => [new(1017, "%s of the Magram", "%s of the Magram")];

        public IReadOnlyDictionary<int, RaceNames> Names => new Dictionary<int, RaceNames> { [2] = new(["Grok"], ["Moga"], ["Bloodrage"]) };

        public ResolvedItem ResolveItem(int itemId, int race, int sex, ModelSet set) => new()
        {
            ItemId = itemId, Race = race, Sex = sex, ModelSet = set == ModelSet.Hd ? "hd" : "sd",
            Attachments = [new Attachment(5, 0, 7400 + itemId, 9400 + itemId)],
            Effects = [new ItemEffect(0, 7600 + itemId)],
        };

        public async Task<string[]> ModelAsync(uint fdid)
        {
            lock (ModelsAsked) ModelsAsked.Add(fdid);
            var running = Interlocked.Increment(ref _running);
            int seen;
            while ((seen = MaxRunning) < running && Interlocked.CompareExchange(ref MaxRunning, running, seen) != seen) { }
            try { if (Delay > 0) await Task.Delay(Delay); }
            finally { Interlocked.Decrement(ref _running); }
            if (fdid == 7402) throw new FileNotFoundException($"FileDataID {fdid} is not in root");
            Directory.CreateDirectory(dir);
            var json = Path.Combine(dir, $"{fdid}.json");
            File.WriteAllText(json, fdid == 7000 ? """{"textures":[{"type":0,"fileDataId":9100}]}""" : """{"textures":[]}""");
            var glb = Path.Combine(dir, $"{fdid}.glb");
            File.WriteAllBytes(glb, [1, 2, 3]);
            return [json, glb];
        }

        public Task<string> TextureAsync(uint fdid)
        {
            Directory.CreateDirectory(dir);
            var png = Path.Combine(dir, $"{fdid}.png");
            File.WriteAllBytes(png, [4, 5]);
            return Task.FromResult(png);
        }
    }

    FakeSource Source() => new(Path.Combine(_root, "converted"));
    string Out => Path.Combine(_root, "bundle");
    string BuildDir => Path.Combine(Out, "wow_classic_beta", "1.60.1.70235");
    // Converted assets sit under the converter version, like the local cache.
    string AssetDir => Path.Combine(BuildDir, $"v{AssetConverter.OutputVersion}");
    static JsonElement Read(string path) => JsonDocument.Parse(File.ReadAllText(path)).RootElement;

    [Fact]
    public async Task WritesTheBundleLayoutAndPointsCurrentAtTheBuild()
    {
        var r = await BundleBaker.BakeAsync(Source(), new BakeOptions(Out));

        Assert.True(r.Ok);
        Assert.Equal("1.60.1.70235", Read(Path.Combine(Out, "wow_classic_beta", "current.json")).GetProperty("build").GetString());
        var characters = Read(Path.Combine(BuildDir, "characters.json"));
        Assert.Equal("Orc", characters.GetProperty("races")[0].GetProperty("name").GetString());
        Assert.Equal(7000, characters.GetProperty("looks").GetProperty("2-0-hd").GetProperty("modelFileDataId").GetInt32());
        Assert.Equal(3, Read(Path.Combine(BuildDir, "catalog.json")).GetProperty("items").GetArrayLength());
        Assert.Equal(500, Read(Path.Combine(BuildDir, "sets.json")).GetProperty("sets")[0].GetProperty("setId").GetInt32());
        // Every item, for every race, sex and body kind.
        var item = Read(Path.Combine(BuildDir, "items", "1.json"));
        Assert.Equal(["2-0-hd", "2-0-sd"], item.GetProperty("variants").EnumerateObject().Select(p => p.Name));
        // Bodies, item models, their textures, layers and icons.
        foreach (var f in new[] { "models/7000.glb", "models/7000.json", "models/7001.glb", "models/7401.glb", "textures/9100.png", "textures/9300.png", "textures/9301.png", "textures/9401.png", "textures/9201.png" })
            Assert.True(File.Exists(Path.Combine(AssetDir, f)), f);
        var counts = r.Manifest.Counts;
        Assert.Equal((2, 2, 3, 1, 3), (counts["races"], counts["looks"], counts["items"], counts["sets"], counts["resolvedItems"]));
    }

    [Fact]
    public async Task AssetsSitUnderTheConverterVersionWhichCurrentAndTheManifestRecord()
    {
        var r = await BundleBaker.BakeAsync(Source(), new BakeOptions(Out));

        Assert.Equal(AssetConverter.OutputVersion, r.Manifest.AssetVersion);
        var current = Read(Path.Combine(Out, "wow_classic_beta", "current.json"));
        Assert.Equal(AssetConverter.OutputVersion, current.GetProperty("assetVersion").GetInt32());
        Assert.Equal($"1.60.1.70235/v{AssetConverter.OutputVersion}/", current.GetProperty("assetPath").GetString());
        Assert.False(Directory.Exists(Path.Combine(BuildDir, "models"))); // not beside the JSON
        Assert.Equal(AssetConverter.OutputVersion, Read(Path.Combine(BuildDir, "manifest.json")).GetProperty("assetVersion").GetInt32());
    }

    [Fact]
    public async Task ConversionsRunInParallelUpToTheLimit()
    {
        var parallel = new FakeSource(Path.Combine(_root, "converted")) { Delay = 50 };
        var r = await BundleBaker.BakeAsync(parallel, new BakeOptions(Out, Parallelism: 4));
        Assert.InRange(parallel.MaxRunning, 2, 4);
        Assert.Equal([7402], r.Manifest.MissingModels);

        var serial = new FakeSource(Path.Combine(_root, "converted")) { Delay = 50 };
        await BundleBaker.BakeAsync(serial, new BakeOptions(Path.Combine(_root, "bundle-serial"), Parallelism: 1));
        Assert.Equal(1, serial.MaxRunning);
    }

    [Fact]
    public void TheAnimationListCoversCastingMeleeSpecialsMovementJumpAndMounted()
    {
        // The bake's short list: every cast, all melee, special melee attacks, run and walk,
        // the mounted pose and the space bar (jump).
        var names = BundleBaker.Animations.Select(AnimationNames.Name).ToHashSet();
        foreach (var n in new[] { "SpellCastDirected", "SpellCastOmni", "SpellCastArea", "ChannelCastDirected", "ReadySpellOmni",
                     "Attack1H", "Attack2H", "AttackOff", "Parry1H", "Ready2H", "ShieldBlock",
                     "Special1H", "Special2H", "ShieldBash", "Kick", "Whirlwind",
                     "Walk", "Run", "Sprint", "JumpStart", "Jump", "JumpEnd", "Mount", "MountSpecial" })
            Assert.Contains(n, names);
        Assert.Equal(BundleBaker.Animations.Count, BundleBaker.Animations.Distinct().Count());
    }

    [Fact]
    public async Task OnlyTheShortListOfAnimationsIsBakedAndOffered()
    {
        var source = Source();
        var r = await BundleBaker.BakeAsync(source, new BakeOptions(Out));

        // Fly is not on the short list, so the site never offers an animation it can't load.
        var look = Read(Path.Combine(BuildDir, "characters.json")).GetProperty("looks").GetProperty("2-0-hd");
        Assert.Equal([0, 5], look.GetProperty("animations").EnumerateArray().Select(a => a.GetProperty("id").GetInt32()));
        foreach (var f in new[] { "anims/7000/0.glb", "anims/7000/5.glb", "anims/7001/0.glb", "anims/7001/5.glb" })
            Assert.True(File.Exists(Path.Combine(AssetDir, f)), f);
        Assert.DoesNotContain(source.AnimationsAsked, a => a.Item2 == 135);
        Assert.Equal(4, r.Manifest.Counts["animations"]);
    }

    [Fact]
    public async Task NotableSetsPerRaceAndClassAndEverySetPieceAreBaked()
    {
        await BundleBaker.BakeAsync(Source(), new BakeOptions(Out));

        // Mirrors /api/v1/sets/notable?race=&class=: "all" is the race's classes together.
        var orc = Read(Path.Combine(BuildDir, "notable.json")).GetProperty("2");
        Assert.Equal("Tier 2", orc.GetProperty("all")[0].GetProperty("group").GetString());
        Assert.Equal(500, orc.GetProperty("1")[0].GetProperty("sets")[0].GetProperty("setId").GetInt32());
        Assert.Equal([3], Read(Path.Combine(BuildDir, "set-pieces.json")).EnumerateArray().Select(e => e.GetInt32()));
        // Every class is baked for every race, so the site can show a Tauren in priest sets ("Any class").
        Assert.True(orc.TryGetProperty("5", out _));
    }

    [Fact]
    public async Task TheCatalogCreditsCmangosWhenItNamesItems()
    {
        await BundleBaker.BakeAsync(Source(), new BakeOptions(Out));
        var catalog = Read(Path.Combine(BuildDir, "catalog.json"));
        Assert.Equal(ClassicItemNames.Credit, catalog.GetProperty("credits")[0].GetString());
        Assert.Equal("cmangos", catalog.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("itemId").GetInt32() == 2).GetProperty("nameSource").GetString());
    }

    [Fact]
    public async Task TitlesAndNamesAreBakedBesideTheBuild()
    {
        await BundleBaker.BakeAsync(Source(), new BakeOptions(Out));
        // Mirrors /api/v1/titles and /api/v1/names.
        Assert.Equal(1017, Read(Path.Combine(BuildDir, "titles.json"))[0].GetProperty("titleId").GetInt32());
        Assert.Equal("Bloodrage", Read(Path.Combine(BuildDir, "names.json")).GetProperty("2").GetProperty("last")[0].GetString());
    }

    [Fact]
    public async Task AMissingModelIsRecordedAndTheRunGoesOn()
    {
        var r = await BundleBaker.BakeAsync(Source(), new BakeOptions(Out));
        Assert.Contains(7402, r.Manifest.MissingModels);
        Assert.True(File.Exists(Path.Combine(AssetDir, "models", "7403.glb")));
    }

    [Fact]
    public async Task AnItemsEffectModelsAreBakedWithItsOtherModels()
    {
        await BundleBaker.BakeAsync(Source(), new BakeOptions(Out));
        Assert.True(File.Exists(Path.Combine(AssetDir, "models", "7603.glb")));
    }

    [Fact]
    public async Task ALimitedBakeKeepsTheLimitedSetsPieces()
    {
        var r = await BundleBaker.BakeAsync(Source(), new BakeOptions(Out, LimitItems: 1, LimitSets: 1));
        Assert.Equal(2, r.Manifest.Counts["items"]); // item 1, plus item 3 from set 500
        Assert.True(File.Exists(Path.Combine(BuildDir, "items", "3.json")));
        Assert.False(File.Exists(Path.Combine(BuildDir, "items", "2.json")));
    }

    [Fact]
    public async Task ALargeDropAgainstThePreviousBuildFailsAndLeavesCurrentAlone()
    {
        var current = Path.Combine(Out, "wow_classic_beta", "current.json");
        Directory.CreateDirectory(Path.GetDirectoryName(current)!);
        File.WriteAllText(current, """{"build":"1.60.1.70205"}""");
        var previous = Path.Combine(_root, "previous-manifest.json");
        File.WriteAllText(previous, """{"counts":{"items":100,"sets":1}}""");

        var r = await BundleBaker.BakeAsync(Source(), new BakeOptions(Out, PreviousManifest: previous, MaxDrop: 0.05));

        Assert.False(r.Ok);
        Assert.Contains(r.Failures, f => f.Contains("items") && f.Contains("100") && f.Contains('3'));
        Assert.Equal("1.60.1.70205", Read(current).GetProperty("build").GetString());
        Assert.True(File.Exists(Path.Combine(BuildDir, "manifest.json"))); // kept for a look
    }

    [Fact]
    public void CountsWithinTheAllowedDropPass()
    {
        Assert.Empty(BundleBaker.CompareCounts(new() { ["items"] = 100 }, new() { ["items"] = 96 }, 0.05));
        Assert.Single(BundleBaker.CompareCounts(new() { ["items"] = 100 }, new() { ["items"] = 94 }, 0.05));
        Assert.Single(BundleBaker.CompareCounts(new() { ["models"] = 10 }, new(), 0.05)); // a count that vanished
    }
}
