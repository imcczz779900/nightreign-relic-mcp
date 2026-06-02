// Milestone 1: encrypt round-trip self-check for architecture A.
//
// Decrypts a Nightreign regulation.bin, applies the relic-weight edit
// (per scope table: all entries' chanceWeight_dlc = 0, target rows = -1),
// re-encrypts to a NEW file, then re-decrypts that new file and verifies
// the edit survived a full read->write->read cycle.
//
// Usage: RoundTrip <regulation.bin> <outPath> [paramdefXml]

using Andre.Formats;
using SoulsFormats;

const string ParamName = "AttachEffectTableParam";
const string ParamFile = "AttachEffectTableParam.param";
const string DlcField = "chanceWeight_dlc";
string defaultDefPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "core", "Assets", "Defs", "AttachEffectTableParam.xml"));

// Test fixture: base-all-relics pool, targets shared across position tables.
int[] scopeTables = { 100, 200, 300, 2000000, 2100000, 3000000 };
int[] targets = { 7000000, 7000100, 7000200 };

if (args.Length < 2) { Console.Error.WriteLine("Usage: RoundTrip <regulation.bin> <outPath> [paramdefXml]"); return 1; }
string regIn = args[0], outPath = args[1];
string defPath = args.Length >= 3 && !string.IsNullOrWhiteSpace(args[2])
    ? args[2]
    : Environment.GetEnvironmentVariable("PARAMDEF_XML") ?? defaultDefPath;
if (!File.Exists(regIn)) { Console.Error.WriteLine($"not found: {regIn}"); return 1; }
if (!File.Exists(defPath)) { Console.Error.WriteLine($"paramdef not found: {defPath}"); return 1; }

PARAMDEF def = PARAMDEF.XmlDeserialize(defPath, versionAware: true);

// ---- 1. decrypt + load ----
BND4 bnd = SFUtil.DecryptNightreignRegulation(regIn);
BinderFile file = bnd.Files.First(f =>
    string.Equals(Path.GetFileName(f.Name), ParamFile, StringComparison.OrdinalIgnoreCase));
Param param = Param.Read(file.Bytes);
param.ApplyParamdef(def, ulong.MaxValue, ParamName);
Console.WriteLine($"loaded {ParamName}: {param.Rows.Count} rows");

// ---- 2. apply edit ----
int zeroed = 0, setMinus1 = 0;
var missing = new List<string>();
foreach (int table in scopeTables)
{
    var tableRows = param.Rows.Where(r => r.ID == table).ToList();
    foreach (var r in tableRows)
    {
        r[DlcField]!.Value.SetValue((short)0);
        zeroed++;
    }
    foreach (int t in targets)
    {
        var hit = tableRows.Where(r => Convert.ToInt64(r["attachEffectId"]!.Value.Value) == t).ToList();
        if (hit.Count == 0) { missing.Add($"table {table} has no attachEffectId {t}"); continue; }
        foreach (var r in hit) { r[DlcField]!.Value.SetValue((short)-1); setMinus1++; }
    }
}
Console.WriteLine($"edit: zeroed {zeroed} entries, set {setMinus1} target rows to -1");
foreach (var m in missing) Console.WriteLine($"  warn: {m}");

// ---- 3. repack + encrypt to NEW file ----
file.Bytes = param.Write();
byte[] encrypted = SFUtil.EncryptNightreignRegulation(bnd);
File.WriteAllBytes(outPath, encrypted);
Console.WriteLine($"wrote {encrypted.Length} bytes -> {outPath}");

// ---- 4. re-decrypt + verify ----
BND4 bnd2 = SFUtil.DecryptNightreignRegulation(outPath);
BinderFile file2 = bnd2.Files.First(f =>
    string.Equals(Path.GetFileName(f.Name), ParamFile, StringComparison.OrdinalIgnoreCase));
Param param2 = Param.Read(file2.Bytes);
param2.ApplyParamdef(def, ulong.MaxValue, ParamName);

bool ok = true;
if (param2.Rows.Count != param.Rows.Count) { ok = false; Console.WriteLine($"FAIL: row count {param2.Rows.Count} != {param.Rows.Count}"); }

foreach (int table in scopeTables)
{
    var tableRows = param2.Rows.Where(r => r.ID == table).ToList();
    var targetSet = new HashSet<long>(targets.Select(t => (long)t));
    foreach (var r in tableRows)
    {
        long ae = Convert.ToInt64(r["attachEffectId"]!.Value.Value);
        long dlc = Convert.ToInt64(r[DlcField]!.Value.Value);
        long expected = targetSet.Contains(ae) ? -1 : 0;
        if (dlc != expected)
        {
            ok = false;
            Console.WriteLine($"FAIL: table {table} attachEffId {ae}: dlc={dlc}, expected {expected}");
        }
    }
}

// spot-check an untouched table is unchanged from a fresh vanilla read
BND4 bndV = SFUtil.DecryptNightreignRegulation(regIn);
Param paramV = Param.Read(bndV.Files.First(f => string.Equals(Path.GetFileName(f.Name), ParamFile, StringComparison.OrdinalIgnoreCase)).Bytes);
paramV.ApplyParamdef(def, ulong.MaxValue, ParamName);
int probeTable = 3000001; // not in scope
var vRows = paramV.Rows.Where(r => r.ID == probeTable).ToList();
var eRows = param2.Rows.Where(r => r.ID == probeTable).ToList();
if (vRows.Count != eRows.Count) { ok = false; Console.WriteLine($"FAIL: untouched table {probeTable} row count changed"); }
else for (int i = 0; i < vRows.Count; i++)
{
    if (!Equals(vRows[i][DlcField]!.Value.Value, eRows[i][DlcField]!.Value.Value))
    { ok = false; Console.WriteLine($"FAIL: untouched table {probeTable} row {i} dlc changed"); break; }
}

Console.WriteLine();
Console.WriteLine(ok ? "ROUND-TRIP OK: edit survived decrypt->edit->encrypt->decrypt, untouched data intact."
                    : "ROUND-TRIP FAILED (see above).");
return ok ? 0 : 1;
