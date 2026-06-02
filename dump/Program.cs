// Read-only verification tool.
// Decrypts a Nightreign regulation.bin, loads AttachEffectTableParam, and prints
// chanceWeight (Roll Weight: Base, u16) and chanceWeight_dlc (Roll Weight: DLC, s16)
// for the requested row IDs. Writes nothing.
//
// Usage:
//   Dump <regulation.bin> [defsDir] [id,id,id...]
//
//   regulation.bin : path to the Nightreign regulation.bin to inspect
//   defsDir        : folder containing AttachEffectTableParam.xml PARAMDEF
//                    (default: the Smithbox repo's NR Defs folder)
//   ids            : comma-separated row IDs to dump
//                    (default: the two preset pools)

using Andre.Formats;
using SoulsFormats;

const string ParamName = "AttachEffectTableParam";
const string ParamFile = "AttachEffectTableParam.param";

string repoDefs = @"C:\Users\32445\Desktop\Smithbox\src\Smithbox.Data\Assets\PARAM\NR\Defs";

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: Dump <regulation.bin> [defsDir] [id,id,...]");
    return 1;
}

string regPath = args[0];
string defsDir = args.Length >= 2 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : repoDefs;

// Default: both preset pools combined (base all-relics + base/DLC all-relics).
int[] defaultIds =
{
    100, 200, 300, 2000000, 2100000, 3000000,   // base all relics
    110, 210, 310, 2200000                       // base + DLC all relics
};
int[] ids = args.Length >= 3 && !string.IsNullOrWhiteSpace(args[2])
    ? args[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Select(int.Parse).ToArray()
    : defaultIds;

if (!File.Exists(regPath))
{
    Console.Error.WriteLine($"regulation.bin not found: {regPath}");
    return 1;
}

string defPath = Path.Combine(defsDir, ParamName + ".xml");
if (!File.Exists(defPath))
{
    Console.Error.WriteLine($"PARAMDEF not found: {defPath}");
    return 1;
}

Console.WriteLine($"regulation : {regPath}");
Console.WriteLine($"paramdef   : {defPath}");
Console.WriteLine();

// 1. Decrypt + unpack the Nightreign regulation BND4.
BND4 bnd = SFUtil.DecryptNightreignRegulation(regPath);

// 2. Pull the AttachEffectTableParam file out of the binder.
BinderFile? file = bnd.Files.FirstOrDefault(f =>
    string.Equals(Path.GetFileName(f.Name), ParamFile, StringComparison.OrdinalIgnoreCase));
if (file is null)
{
    Console.Error.WriteLine($"{ParamFile} not present in regulation binder.");
    return 1;
}

// 3. Read the param and apply the paramdef. These fields are not version-gated,
//    so ulong.MaxValue (latest) is safe.
PARAMDEF def = PARAMDEF.XmlDeserialize(defPath, versionAware: true);
Param param = Param.Read(file.Bytes);
param.ApplyParamdef(def, ulong.MaxValue, ParamName);

// ---- EXPLORATORY: understand the param structure ----
{
    var allRows = param.Rows;
    long minId = allRows.Min(r => (long)r.ID);
    long maxId = allRows.Max(r => (long)r.ID);
    Console.WriteLine($"EXPLORE: {allRows.Count} rows, RowID range [{minId} .. {maxId}]");
    Console.WriteLine();
    Console.WriteLine("First 25 rows:");
    Console.WriteLine($"{"RowID",10}  {"attachEffId",12}  {"chanceWeight",14}  {"chanceWeight_dlc",18}  Name");
    Console.WriteLine(new string('-', 80));
    foreach (var r in allRows.Take(25))
        Console.WriteLine($"{r.ID,10}  {r["attachEffectId"]?.Value,12}  {r["chanceWeight"]?.Value,14}  {r["chanceWeight_dlc"]?.Value,18}  {r.Name}");
    Console.WriteLine();
    Console.WriteLine("Do the supplied numbers exist as ROW IDs?");
    foreach (int id in ids)
    {
        var exact = allRows.Where(r => r.ID == id).ToList();
        Console.WriteLine($"  RowID {id,9}: {exact.Count} match(es)" +
            (exact.Count > 0 ? $"  -> attachEffId={exact[0]["attachEffectId"]?.Value}, base={exact[0]["chanceWeight"]?.Value}, dlc={exact[0]["chanceWeight_dlc"]?.Value}, name={exact[0].Name}" : ""));
    }
    Console.WriteLine();
    Console.WriteLine("Per-table: entry count, distinct attachEffectId count, dup attachEffIds:");
    foreach (int tableId in ids)
    {
        var tableRows = allRows.Where(r => r.ID == tableId).ToList();
        if (tableRows.Count == 0) { Console.WriteLine($"  table {tableId,9}: (none)"); continue; }
        var byAe = tableRows.GroupBy(r => Convert.ToInt64(r["attachEffectId"]?.Value ?? -1L)).ToList();
        int dupAe = byAe.Count(g => g.Count() > 1);
        var dlcVals = tableRows.Select(r => Convert.ToInt64(r["chanceWeight_dlc"]?.Value ?? 0)).Distinct().OrderBy(x => x).ToList();
        Console.WriteLine($"  table {tableId,9}: {tableRows.Count} entries, {byAe.Count} distinct attachEffId, {dupAe} of them duplicated; dlc distinct=[{string.Join(",", dlcVals.Take(8))}]");
    }
    Console.WriteLine();
    Console.WriteLine("Is attachEffId 7000000 shared across the base pool tables?");
    foreach (int tableId in new[] { 100, 200, 300, 110, 210, 310 })
    {
        int c = allRows.Count(r => r.ID == tableId && Convert.ToInt64(r["attachEffectId"]?.Value ?? -1L) == 7000000);
        Console.WriteLine($"  table {tableId,9}: attachEffId 7000000 appears {c} time(s)");
    }
    Console.WriteLine();
    Console.WriteLine(new string('=', 80));
    Console.WriteLine();
}

Console.WriteLine($"{ParamName}: {param.Rows.Count} rows total");
Console.WriteLine("(matching the supplied values against the attachEffectId column)");
Console.WriteLine();
Console.WriteLine($"{"attachEffId",12}  {"RowID",10}  {"chanceWeight",14}  {"chanceWeight_dlc",18}  Name");
Console.WriteLine(new string('-', 80));

foreach (int id in ids)
{
    var rows = param.Rows
        .Where(r => Convert.ToInt64(r["attachEffectId"]?.Value ?? -1L) == id)
        .ToList();
    if (rows.Count == 0)
    {
        Console.WriteLine($"{id,12}  {"(no row)",10}  {"",14}  {"",18}");
        continue;
    }
    foreach (Param.Row row in rows)
    {
        object? baseW = row["chanceWeight"]?.Value;
        object? dlcW = row["chanceWeight_dlc"]?.Value;
        Console.WriteLine($"{id,12}  {row.ID,10}  {baseW,14}  {dlcW,18}  {row.Name}");
    }
}

return 0;
