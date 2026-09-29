namespace Altrobe.Core.Tests;

static class Fake
{
    public static IReadOnlyDictionary<string, object?> R(params (string column, object? value)[] cells) =>
        cells.ToDictionary(c => c.column, c => c.value);
}

// Behaves like the real table loader: a table the build doesn't have throws on Get, and Has says so.
sealed class StrictTables(Altrobe.Core.Tables.InMemoryTables inner, params string[] present) : Altrobe.Core.Tables.ITables
{
    public IReadOnlyList<Altrobe.Core.Tables.Row> Get(string table) =>
        Has(table) ? inner.Get(table) : throw new FileNotFoundException($"Table {table} is not in this build");

    public bool Has(string table) => present.Contains(table, StringComparer.OrdinalIgnoreCase);
}
