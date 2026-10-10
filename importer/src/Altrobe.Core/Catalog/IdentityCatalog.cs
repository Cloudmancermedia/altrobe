using Altrobe.Core.Tables;
using T = Altrobe.Core.Tables.GameTableNames;

namespace Altrobe.Core.Catalog;

// A CharTitles row: the title's wording for male and female characters, "%s" where the name goes, and
// the faction that can hold it (PvP ranks), or null for either.
public sealed record TitleInfo(int TitleId, string Male, string Female, string? Faction = null);

// One race's names from NameGen, the list the game's random-name button draws from. First names are
// by sex; last names are shared by both sexes.
public sealed record RaceNames(IReadOnlyList<string> Male, IReadOnlyList<string> Female, IReadOnlyList<string> Last);

// Titles and names for the character's identity (name, title, guild). Rows are kept in ID order so the
// name generator, which picks by position, gives the same name for the same seed. Builds without the
// tables have neither.
public sealed class IdentityCatalog
{
    public IReadOnlyList<TitleInfo> Titles { get; }
    public IReadOnlyDictionary<int, RaceNames> Names { get; }

    // CharTitles marks the PvP ranks (Flags 4) but not their faction, so it is kept here by ID, as the
    // Forever beta numbers them: Private to Grand Marshal and Knight-Captain for the Alliance, Scout to
    // High Warlord for the Horde. Sergeant is one row per faction (1260, 1273).
    static string? FactionOf(int titleId) => titleId switch
    {
        >= 1258 and <= 1270 or 1309 => "alliance",
        >= 1271 and <= 1284 => "horde",
        _ => null,
    };

    public IdentityCatalog(ITables tables)
    {
        Titles = tables.Has(T.CharTitles)
            ? tables.Get(T.CharTitles).OrderBy(r => r.Int("ID"))
                .Where(r => !r.Str("Name_lang").Contains("DNT", StringComparison.Ordinal))
                .Select(r => new TitleInfo(r.Int("ID"), r.Str("Name_lang"), r.Str("Name1_lang") is { Length: > 0 } f ? f : r.Str("Name_lang"), FactionOf(r.Int("ID"))))
                .ToList()
            : [];

        var rows = tables.Has(T.NameGen) ? tables.Get(T.NameGen).OrderBy(r => r.Int("ID")).ToList() : [];
        List<string> Pick(Func<Row, bool> where) => rows.Where(where).Select(r => r.Str("Name")).Where(n => n.Length > 0).Distinct().ToList();
        Names = rows.Select(r => r.Int("RaceID")).Distinct().Order().ToDictionary(race => race, race => new RaceNames(
            Pick(r => r.Int("RaceID") == race && r.Int("NameType") == 0 && r.Int("Sex") == 0),
            Pick(r => r.Int("RaceID") == race && r.Int("NameType") == 0 && r.Int("Sex") == 1),
            Pick(r => r.Int("RaceID") == race && r.Int("NameType") == 1)));
    }
}
