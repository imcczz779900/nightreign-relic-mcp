using System.Text.Json;
using Andre.Formats;
using SoulsFormats;

namespace RelicAffix;

/// <summary>One AttachEffectTableParam entry, joined with its bundled display name.</summary>
public sealed record Entry(int TableId, int IndexInTable, long AttachEffectId, string? Name, int BaseWeight, int DlcWeight)
{
    /// <summary>Display name without the leading "&lt;Scene&gt; " prefix, e.g. "Vigor +1".</summary>
    public string? BareName => Name is null ? null : RelicRegulation.StripPrefix(Name);
}

/// <summary>
/// Loads/decrypts a Nightreign regulation.bin, exposes AttachEffectTableParam, and
/// joins each row with the bundled row-name list. Edits are made in memory; SaveAs
/// re-encrypts to a new file.
/// </summary>
public sealed class RelicRegulation
{
    public const string ParamName = "AttachEffectTableParam";
    public const string ParamFileName = "AttachEffectTableParam.param";
    public const string BaseField = "chanceWeight";
    public const string DlcField = "chanceWeight_dlc";
    public const string AeField = "attachEffectId";

    private readonly BND4 _bnd;
    private readonly BinderFile _paramFile;
    public Param Param { get; }
    public string SourcePath { get; }

    /// <summary>table id -> ordered display names (from bundled row-name JSON).</summary>
    private readonly Dictionary<int, List<string>> _rowNames;

    /// <summary>table id -> rows with that ID, in file order.</summary>
    private readonly Dictionary<int, List<Param.Row>> _rowsByTable;

    public List<string> Warnings { get; } = new();

    private RelicRegulation(string sourcePath, BND4 bnd, BinderFile paramFile, Param param, Dictionary<int, List<string>> rowNames)
    {
        SourcePath = Path.GetFullPath(sourcePath);
        _bnd = bnd;
        _paramFile = paramFile;
        Param = param;
        _rowNames = rowNames;
        _rowsByTable = new();
        foreach (var r in param.Rows)
        {
            if (!_rowsByTable.TryGetValue(r.ID, out var list))
                _rowsByTable[r.ID] = list = new();
            list.Add(r);
        }
    }

    public static RelicRegulation Load(string regulationPath, string defXmlPath, string rowNamesJsonPath)
    {
        if (!File.Exists(regulationPath)) throw new FileNotFoundException("regulation.bin not found", regulationPath);
        if (!File.Exists(defXmlPath)) throw new FileNotFoundException("PARAMDEF xml not found", defXmlPath);

        PARAMDEF def = PARAMDEF.XmlDeserialize(defXmlPath, versionAware: true);
        BND4 bnd = SFUtil.DecryptNightreignRegulation(regulationPath);
        BinderFile file = bnd.Files.FirstOrDefault(f =>
            string.Equals(Path.GetFileName(f.Name), ParamFileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"{ParamFileName} not present in regulation binder.");

        Param param = Param.Read(file.Bytes);
        param.ApplyParamdef(def, ulong.MaxValue, ParamName);

        var rowNames = LoadRowNames(rowNamesJsonPath);
        return new RelicRegulation(regulationPath, bnd, file, param, rowNames);
    }

    private static Dictionary<int, List<string>> LoadRowNames(string path)
    {
        var result = new Dictionary<int, List<string>>();
        if (!File.Exists(path)) return result; // names are optional; attachEffectId targeting still works
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        if (doc.RootElement.TryGetProperty("Entries", out var entries))
        {
            foreach (var e in entries.EnumerateArray())
            {
                int id = e.GetProperty("ID").GetInt32();
                var names = e.GetProperty("Entries").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                result[id] = names;
            }
        }
        return result;
    }

    public static string StripPrefix(string name)
    {
        int i = name.IndexOf("> ", StringComparison.Ordinal);
        return i >= 0 ? name[(i + 2)..] : name;
    }

    public static string StripPrefixSafe(string? name) => name is null ? "" : StripPrefix(name);

    private static long GetLong(Param.Row r, string field) => Convert.ToInt64(r[field]!.Value.Value);
    private static void SetShort(Param.Row r, string field, short v) => r[field]!.Value.SetValue(v);

    /// <summary>Rows of a table joined with bundled names (by file order). Empty if table absent.</summary>
    public IReadOnlyList<Entry> EntriesForTable(int tableId)
    {
        if (!_rowsByTable.TryGetValue(tableId, out var rows)) return Array.Empty<Entry>();
        _rowNames.TryGetValue(tableId, out var names);
        var list = new List<Entry>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            string? name = names != null && i < names.Count ? names[i] : null;
            list.Add(new Entry(
                tableId, i,
                GetLong(rows[i], AeField),
                name,
                (int)GetLong(rows[i], BaseField),
                (int)GetLong(rows[i], DlcField)));
        }
        return list;
    }

    private Dictionary<string, HashSet<long>>? _nameIndex;

    /// <summary>
    /// Global map: bare affix name -> attachEffectIds that bear it (across every table).
    /// Lets a name target resolve to attachEffectIds even for tables whose own row-names are
    /// unlabeled (e.g. the deep-night tables that show "(0% weight)").
    /// </summary>
    private Dictionary<string, HashSet<long>> NameIndex()
    {
        if (_nameIndex != null) return _nameIndex;
        _nameIndex = new(StringComparer.OrdinalIgnoreCase);
        foreach (int tableId in _rowsByTable.Keys)
            foreach (var e in EntriesForTable(tableId))
            {
                if (e.BareName is null) continue;
                string bare = e.BareName;
                if (bare.Length == 0 || bare.StartsWith("(0%")) continue; // skip unlabeled placeholders
                if (!_nameIndex.TryGetValue(bare, out var set))
                    _nameIndex[bare] = set = new();
                set.Add(e.AttachEffectId);
            }
        return _nameIndex;
    }

    /// <summary>attachEffectIds that match a bare affix name (case-insensitive). Empty if unknown.</summary>
    public IReadOnlySet<long> ResolveName(string bareName) =>
        NameIndex().TryGetValue(bareName.Trim(), out var set) ? set : (IReadOnlySet<long>)new HashSet<long>();

    public Param.Row RowAt(int tableId, int index) => _rowsByTable[tableId][index];

    /// <summary>Null if the table's bundled names align with its rows; otherwise a warning string.</summary>
    public string? NameMisalignment(int tableId)
    {
        if (!_rowsByTable.TryGetValue(tableId, out var rows)) return null;
        if (_rowNames.TryGetValue(tableId, out var names) && names.Count != rows.Count)
            return $"table {tableId}: name count {names.Count} != row count {rows.Count}; names may be misaligned (use attachEffectId to be safe).";
        return null;
    }

    public void SetDlc(int tableId, int index, short value) => SetShort(_rowsByTable[tableId][index], DlcField, value);

    /// <summary>Re-pack the edited param and encrypt to a new regulation.bin. Never overwrites the source.</summary>
    public void SaveAs(string outPath)
    {
        string fullOut = Path.GetFullPath(outPath);
        if (string.Equals(fullOut, SourcePath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Refusing to overwrite the source regulation.bin. Choose a different output path.");
        if (File.Exists(fullOut))
            throw new IOException($"Refusing to overwrite existing output file: {fullOut}");
        string? dir = Path.GetDirectoryName(fullOut);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _paramFile.Bytes = Param.Write();
        byte[] encrypted = SFUtil.EncryptNightreignRegulation(_bnd);
        File.WriteAllBytes(fullOut, encrypted);
    }
}
