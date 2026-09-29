using System.Collections.Concurrent;
using Altrobe.Core.Storage;
using DBCD.IO;
using DBCD.Providers;

namespace Altrobe.Core.Tables;

// DB2 tables read from the local install, with the client's DBCache.bin hotfixes applied. Each
// table loads on first use and stays in memory for the life of the build.
public sealed class Db2Tables : ITables
{
    readonly DBCD.DBCD _dbcd;
    readonly IGameFiles _files;
    readonly DefinitionProvider _definitions;
    readonly string _build;
    readonly Lazy<HotfixReader?> _hotfixes;
    readonly ConcurrentDictionary<string, Memo<IReadOnlyList<Row>>> _tables = new(StringComparer.OrdinalIgnoreCase);
    // DBCD and HotfixReader make no thread-safety promises, so tables load one at a time.
    readonly Lock _loadLock = new();

    public Db2Tables(IGameFiles files, DefinitionProvider definitions, string build, string? hotfixPath)
    {
        _dbcd = new DBCD.DBCD(new GameDbcProvider(files, definitions), definitions);
        _files = files;
        _definitions = definitions;
        _build = build;
        _hotfixes = new(() => hotfixPath != null && File.Exists(hotfixPath) ? new HotfixReader(hotfixPath) : null);
    }

    public int? HotfixBuild => _hotfixes.Value?.BuildId;

    public IReadOnlyList<Row> Get(string table) => _tables.GetOrAdd(table, t => new Memo<IReadOnlyList<Row>>(() => Load(t))).Value;

    public bool Has(string table)
    {
        uint fdid;
        try { fdid = _definitions.FileDataIdFor(table); }
        catch (KeyNotFoundException) { return false; }
        return _files.Exists(fdid);
    }

    IReadOnlyList<Row> Load(string table)
    {
        lock (_loadLock)
        {
            var storage = _dbcd.Load(table, _build);
            if (_hotfixes.Value is { } hotfixes) storage.ApplyingHotfixes(hotfixes);
            var columns = new HashSet<string>(storage.AvailableColumns);
            return storage.Keys.OrderBy(k => k).Select(k =>
            {
                var row = storage[k];
                return new Row(c => columns.Contains(c) ? row[c] : null);
            }).ToList();
        }
    }

    sealed class GameDbcProvider(IGameFiles files, DefinitionProvider definitions) : IDBCProvider
    {
        public Stream StreamForTableName(string tableName, string build) => new MemoryStream(files.Open(definitions.FileDataIdFor(tableName)));
    }
}
