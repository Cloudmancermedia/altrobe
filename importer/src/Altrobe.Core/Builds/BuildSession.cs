using Altrobe.Core.Cache;
using Altrobe.Core.Catalog;
using Altrobe.Core.Convert;
using Altrobe.Core.Install;
using Altrobe.Core.Resolve;
using Altrobe.Core.Storage;
using Altrobe.Core.Tables;

namespace Altrobe.Core.Builds;

// Everything the server needs for one selected product build. Tables, catalogs and resolvers are
// built on first use; converted assets are cached on disk under CacheDir, in the current converter
// version's folder (ConvertedCache).
public sealed class BuildSession
{
    readonly IGameFiles _files;
    readonly AssetCache _assets;
    readonly Memo<ItemCatalog> _items;
    readonly Memo<SetCatalog> _sets;
    readonly Memo<IdentityCatalog> _identity;
    readonly Memo<CharacterCatalog> _characters;
    readonly Memo<ItemResolver> _itemResolver;
    readonly Memo<LookResolver> _looks;

    // classicItems names the Classic items the game files leave unnamed (ClassicItemNames); none by default.
    public BuildSession(ITables tables, IGameFiles files, string product, string build, string? buildName, string cacheDir,
        IReadOnlyDictionary<int, ClassicItem>? classicItems = null)
    {
        _files = files;
        Product = product;
        Build = build;
        BuildName = buildName;
        CacheDir = cacheDir;
        _assets = new AssetCache(ConvertedCache.Prepare(cacheDir));
        _items = new(() => new ItemCatalog(tables, classicItems));
        _sets = new(() => new SetCatalog(tables, _items.Value));
        _identity = new(() => new IdentityCatalog(tables));
        _characters = new(() => new CharacterCatalog(tables));
        _itemResolver = new(() => new ItemResolver(tables));
        _looks = new(() => new LookResolver(tables));
    }

    public string Product { get; }
    public string Build { get; }
    public string? BuildName { get; }
    public string CacheDir { get; }

    public ItemCatalog Items => _items.Value;
    public SetCatalog Sets => _sets.Value;
    public IdentityCatalog Identity => _identity.Value;
    public CharacterCatalog Characters => _characters.Value;
    public ItemResolver ItemResolver => _itemResolver.Value;

    public static BuildSession Open(WowInstall install, WowProduct product, string cacheRoot, IReadOnlyDictionary<int, ClassicItem>? classicItems = null)
    {
        var cacheDir = AppPaths.BuildDir(cacheRoot, product.Product, product.Version);
        var storage = GameStorage.Open(install, product, Path.Combine(cacheDir, "tact"));
        var definitions = new DefinitionProvider(Path.Combine(cacheRoot, "definitions"));
        var hotfixes = product.Folder != null ? Path.Combine(install.Path, product.Folder, "Cache", "ADB", "enUS", "DBCache.bin") : null;
        var tables = new Db2Tables(storage, definitions, product.Version, hotfixes);
        return new BuildSession(tables, storage, product.Product, product.Version, storage.BuildName, cacheDir, classicItems);
    }

    // Returns [json, glb] paths. Throws FileNotFoundException when the model is not on local disk.
    public Task<string[]> ModelAsync(uint fdid) => _assets.GetOrCreateAsync($"models/{fdid}", [$"models/{fdid}.json", $"models/{fdid}.glb"], () =>
    {
        var m = AssetConverter.ConvertModel(fdid, _files.Open);
        return [m.MetaJson, m.Glb];
    });

    // Returns the .glb path. Throws FileNotFoundException when the model has no such animation.
    public async Task<string> AnimationAsync(uint fdid, int animId) =>
        (await _assets.GetOrCreateAsync($"anims/{fdid}/{animId}", [$"anims/{fdid}/{animId}.glb"], () => [AssetConverter.ConvertAnimation(fdid, animId, _files.Open)]))[0];

    public async Task<string> TextureAsync(uint fdid) =>
        (await _assets.GetOrCreateAsync($"textures/{fdid}", [$"textures/{fdid}.png"], () => [AssetConverter.ConvertTexture(_files.Open(fdid))]))[0];

    // Null when the race/sex has no body of that kind. Converts the body model (once) to learn
    // which geosets its mesh has.
    public async Task<CharacterLook?> LookAsync(int raceId, int sex, int classId, ModelSet set)
    {
        if (Characters.ChrModelFor(raceId, sex, set) == null) return null;
        if (_looks.Value.Body(raceId, sex, set) is not var (_, _, bodyFdid)) return null;
        var meta = await File.ReadAllBytesAsync((await ModelAsync((uint)bodyFdid))[0]);
        var geosets = AssetConverter.GeosetIds(meta);
        var animations = AssetConverter.SequenceIds(meta).Where(AnimationNames.IsClassic).Select(id => new AnimationInfo(id, AnimationNames.Name(id))).ToList();
        return _looks.Value.Resolve(raceId, sex, classId, set, _ => geosets) is { } look ? look.ForPlayer() with { Build = Build, Animations = animations } : null;
    }

    // An item with no ItemSparse row goes by its catalog name: cmangos' (ClassicItemNames) or the one the
    // catalog made from its set and slot. The resolver alone only knows the game files.
    public ResolvedItem ResolveItem(int itemId, int raceId, int sex, ModelSet set)
    {
        var r = ItemResolver.Resolve(itemId, raceId, sex) with { Build = Build, Race = raceId, Sex = sex, ModelSet = set == ModelSet.Sd ? "sd" : "hd" };
        return r.Error == null && r.Name == ItemResolver.NoName && Items.Get(itemId) is { } summary ? r with { Name = summary.Name } : r;
    }
}
