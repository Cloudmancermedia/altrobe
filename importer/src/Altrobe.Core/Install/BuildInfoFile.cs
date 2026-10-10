namespace Altrobe.Core.Install;

// Battle.net's pipe-separated table format, used by .build.info and .flavor.info. The header row
// names each column with a type suffix ("Build Key!HEX:16") that we drop.
public static class BuildInfoFile
{
    public static List<Dictionary<string, string>> Parse(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        if (lines.Count == 0) return [];
        var columns = lines[0].Split('|').Select(c => c.Split('!')[0]).ToArray();
        var rows = new List<Dictionary<string, string>>();
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split('|');
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columns.Length; i++) row[columns[i]] = i < cells.Length ? cells[i] : "";
            rows.Add(row);
        }
        return rows;
    }
}
