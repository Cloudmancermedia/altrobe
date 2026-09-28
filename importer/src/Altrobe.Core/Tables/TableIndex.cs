namespace Altrobe.Core.Tables;

public static class TableIndex
{
    // Last row wins on a duplicate key, as in the spike's JS Map-based lookups.
    public static Dictionary<int, Row> ById(this IEnumerable<Row> rows, string column = "ID")
    {
        var map = new Dictionary<int, Row>();
        foreach (var r in rows) map[r.Int(column)] = r;
        return map;
    }

    // Groups keep table order.
    public static Dictionary<int, List<Row>> GroupByColumn(this IEnumerable<Row> rows, string column)
    {
        var map = new Dictionary<int, List<Row>>();
        foreach (var r in rows)
        {
            var k = r.Int(column);
            if (!map.TryGetValue(k, out var list)) map[k] = list = [];
            list.Add(r);
        }
        return map;
    }

    public static IReadOnlyList<Row> Of(this Dictionary<int, List<Row>> groups, int key) =>
        groups.TryGetValue(key, out var list) ? list : [];
}
