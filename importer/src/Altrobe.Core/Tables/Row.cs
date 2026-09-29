using System.Collections;

namespace Altrobe.Core.Tables;

// One DB2 row, read by column name. Missing columns read as 0, "" or an empty array, which matches
// how the spike's JSON-based scripts treated absent fields.
public sealed class Row
{
    readonly Func<string, object?> _get;

    public Row(Func<string, object?> get) => _get = get;

    public Row(IReadOnlyDictionary<string, object?> values) => _get = c => values.GetValueOrDefault(c);

    public object? this[string column] => _get(column);

    public int Int(string column) => (int)Long(column);

    public long Long(string column) => ToLong(_get(column));

    public string Str(string column) => _get(column) as string ?? "";

    public int[] Ints(string column) => _get(column) switch
    {
        null => [],
        string => [],
        IEnumerable e => e.Cast<object?>().Select(v => (int)ToLong(v)).ToArray(),
        var v => [(int)ToLong(v)],
    };

    public int Int(string column, int index)
    {
        var values = Ints(column);
        return index >= 0 && index < values.Length ? values[index] : 0;
    }

    static long ToLong(object? v) => v switch
    {
        null => 0,
        long l => l,
        ulong u => unchecked((long)u),
        IConvertible c when v is not string => System.Convert.ToInt64(c),
        _ => 0,
    };
}

public interface ITables
{
    // Throws when a table cannot be loaded; callers treat that as a data error, not an empty table.
    IReadOnlyList<Row> Get(string table);

    // Whether this build has the table at all. Some tables exist only in some products.
    bool Has(string table);
}

public sealed class InMemoryTables : ITables
{
    readonly Dictionary<string, List<Row>> _tables = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryTables Add(string table, params IReadOnlyDictionary<string, object?>[] rows)
    {
        if (!_tables.TryGetValue(table, out var list)) _tables[table] = list = [];
        list.AddRange(rows.Select(r => new Row(r)));
        return this;
    }

    public IReadOnlyList<Row> Get(string table) => _tables.TryGetValue(table, out var rows) ? rows : [];

    public bool Has(string table) => _tables.ContainsKey(table);
}
