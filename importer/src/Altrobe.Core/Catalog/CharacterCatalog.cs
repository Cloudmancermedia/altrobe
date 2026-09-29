using Altrobe.Core.Tables;
using T = Altrobe.Core.Tables.GameTableNames;

namespace Altrobe.Core.Catalog;

public enum ModelSet { Hd, Sd }

public sealed record ClassInfo(int ClassId, string Name);

public sealed record SexAvailability(int Sex, bool Hd, bool Sd, int ChrModelId, int? SdChrModelId);

public sealed record RaceInfo(int Race, string Name, string FemaleName, string ClientFileString, IReadOnlyList<SexAvailability> Sexes, IReadOnlyList<ClassInfo> Classes);

// A race is playable iff it has a CharBaseInfo row (a race/class pair offered at character
// creation). HD bodies come from ChrRaceXChrModel; SD bodies from ChrModelAltVariant, a table only
// Forever has, which maps each HD ChrModel to its SD variant.
public sealed class CharacterCatalog
{
    readonly IReadOnlyList<RaceInfo> _races;

    // HD ChrModel -> SD ChrModel. Builds without the table (every product but Forever) have no SD bodies.
    public static Dictionary<int, int> SdVariants(ITables tables) =>
        tables.Has(T.ChrModelAltVariant)
            ? tables.Get(T.ChrModelAltVariant).ById("SourceChrModelID").ToDictionary(kv => kv.Key, kv => kv.Value.Int("VariantChrModelID"))
            : [];

    public CharacterCatalog(ITables tables)
    {
        var races = tables.Get(T.ChrRaces).ById();
        var classes = tables.Get(T.ChrClasses).ById();
        var baseInfo = tables.Get(T.CharBaseInfo).GroupByColumn("RaceID");
        var raceModels = tables.Get(T.ChrRaceXChrModel);
        var altVariant = SdVariants(tables);

        _races = baseInfo.Keys.Order().Select(raceId =>
        {
            races.TryGetValue(raceId, out var race);
            var sexes = new List<SexAvailability>();
            foreach (var sex in new[] { 0, 1 })
            {
                var link = raceModels.FirstOrDefault(x => x.Int("ChrRacesID") == raceId && x.Int("Sex") == sex);
                if (link == null) continue;
                var model = link.Int("ChrModelID");
                int? sd = altVariant.TryGetValue(model, out var v) ? v : null;
                sexes.Add(new SexAvailability(sex, true, sd != null, model, sd));
            }
            var raceClasses = baseInfo[raceId].Select(r => r.Int("ClassID")).Distinct().Order()
                .Select(c => new ClassInfo(c, classes.TryGetValue(c, out var cls) ? cls.Str("Name_lang") : "")).ToList();
            return new RaceInfo(raceId, race?.Str("Name_lang") ?? "", race?.Str("Name_female_lang") ?? "", race?.Str("ClientFileString") ?? "", sexes, raceClasses);
        }).ToList();
    }

    public IReadOnlyList<RaceInfo> Races() => _races;

    public RaceInfo? Race(int raceId) => _races.FirstOrDefault(r => r.Race == raceId);

    // Null when the race is not playable or has no body of that kind.
    public int? ChrModelFor(int raceId, int sex, ModelSet set)
    {
        var s = Race(raceId)?.Sexes.FirstOrDefault(x => x.Sex == sex);
        if (s == null) return null;
        return set == ModelSet.Hd ? s.ChrModelId : s.SdChrModelId;
    }
}
