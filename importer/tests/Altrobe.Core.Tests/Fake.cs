namespace Altrobe.Core.Tests;

static class Fake
{
    public static IReadOnlyDictionary<string, object?> R(params (string column, object? value)[] cells) =>
        cells.ToDictionary(c => c.column, c => c.value);
}
