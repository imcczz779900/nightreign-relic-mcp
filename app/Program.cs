using RelicAffix;

// CLI for the relic-affix weight editor (architecture A: offline regulation.bin edit).
//
//   relic-affix list-pools
//   relic-affix dump-table   <regulation.bin> <tableId>
//   relic-affix list-affixes <regulation.bin> <dlc|nodlc> <normal|deep_night|both> [query]
//   relic-affix preview      <regulation.bin> <dlc|nodlc> <normal|deep_night|both> <target> [target...]
//   relic-affix apply        <regulation.bin> <dlc|nodlc> <normal|deep_night|both> <outPath> <target> [target...]
//
// <target> : an attachEffectId (number) or an affix name (e.g. "Vigor +1")
//
// Bundled data paths default to the Smithbox repo; override with DEFS_DIR / ROWNAMES env vars.

string defXml = Config.DefXml;
string rowNames = Config.RowNames;

if (args.Length == 0) { Usage(); return 1; }

try
{
    switch (args[0])
    {
        case "list-pools":
            foreach (var p in Pools.AllCombos())
                Console.WriteLine($"{p.Id,-20} {p.Label}\n  tables: {string.Join(", ", p.Tables)}");
            return 0;

        case "dump-table":
        {
            if (args.Length < 3) { Usage(); return 1; }
            var reg = RelicRegulation.Load(args[1], defXml, rowNames);
            int tableId = int.Parse(args[2]);
            var entries = reg.EntriesForTable(tableId);
            Console.WriteLine($"table {tableId}: {entries.Count} entries");
            Console.WriteLine($"  base distinct : {string.Join(",", entries.Select(e => e.BaseWeight).Distinct().OrderBy(x => x).Take(12))}");
            Console.WriteLine($"  dlc  distinct : {string.Join(",", entries.Select(e => e.DlcWeight).Distinct().OrderBy(x => x).Take(12))}");
            Console.WriteLine($"  base>0 count  : {entries.Count(e => e.BaseWeight > 0)} ; dlc>0 count: {entries.Count(e => e.DlcWeight > 0)}");
            Console.WriteLine($"{"idx",4} {"attachEffId",12} {"base",6} {"dlc",6}  name");
            foreach (var e in entries.Take(12))
                Console.WriteLine($"{e.IndexInTable,4} {e.AttachEffectId,12} {e.BaseWeight,6} {e.DlcWeight,6}  {e.Name}");
            return 0;
        }

        case "list-affixes":
        {
            if (args.Length < 4) { Usage(); return 1; }
            var reg = RelicRegulation.Load(args[1], defXml, rowNames);
            var pool = ResolvePool(args[2], args[3]);
            string? query = args.Length >= 5 ? args[4] : null;
            var seen = new HashSet<string>();
            Console.WriteLine($"pool {pool.Id}  tables: {string.Join(",", pool.Tables)}");
            Console.WriteLine($"{"attachEffId",12}  {"base%",8}  {"dlc",5}  name");
            Console.WriteLine(new string('-', 70));
            foreach (int table in pool.Tables)
                foreach (var e in reg.EntriesForTable(table))
                {
                    string key = e.BareName ?? e.AttachEffectId.ToString();
                    if (!seen.Add(key)) continue;
                    if (query != null &&
                        !(e.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) &&
                        !e.AttachEffectId.ToString().Contains(query)) continue;
                    Console.WriteLine($"{e.AttachEffectId,12}  {e.BaseWeight / 100.0,7:0.00}%  {e.DlcWeight,5}  {e.BareName}");
                }
            foreach (var w in reg.Warnings) Console.WriteLine($"warn: {w}");
            return 0;
        }

        case "preview":
        {
            if (args.Length < 5) { Usage(); return 1; }
            var reg = RelicRegulation.Load(args[1], defXml, rowNames);
            var pool = ResolvePool(args[2], args[3]);
            var targets = args.Skip(4).ToList();
            PrintReport(WeightEditor.ForceTargets(reg, pool, targets, apply: false), null);
            return 0;
        }

        case "apply":
        {
            if (args.Length < 6) { Usage(); return 1; }
            var reg = RelicRegulation.Load(args[1], defXml, rowNames);
            var pool = ResolvePool(args[2], args[3]);
            string outPath = args[4];
            var targets = args.Skip(5).ToList();

            var report = WeightEditor.ForceTargets(reg, pool, targets, apply: true);
            reg.SaveAs(outPath);

            bool ok = Verifier.SelfCheck(outPath, pool, report, out string detail);
            PrintReport(report, outPath);
            Console.WriteLine();
            Console.WriteLine(ok ? "SELF-CHECK OK (re-decrypted output matches intended edit)."
                                 : $"SELF-CHECK FAILED: {detail}");
            return ok ? 0 : 2;
        }

        default:
            Usage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

Pool ResolvePool(string dlcToken, string typeToken)
{
    bool hasDlc = dlcToken.Trim().ToLowerInvariant() switch
    {
        "dlc" or "yes" or "true" or "1" => true,
        "nodlc" or "no" or "false" or "0" => false,
        _ => throw new ArgumentException($"DLC flag must be 'dlc' or 'nodlc' (got '{dlcToken}').")
    };
    return Pools.Resolve(hasDlc, Pools.ParseType(typeToken));
}

void Usage()
{
    Console.WriteLine("usage:");
    Console.WriteLine("  relic-affix list-pools");
    Console.WriteLine("  relic-affix dump-table   <regulation.bin> <tableId>");
    Console.WriteLine("  relic-affix list-affixes <regulation.bin> <dlc|nodlc> <normal|deep_night|both> [query]");
    Console.WriteLine("  relic-affix preview      <regulation.bin> <dlc|nodlc> <normal|deep_night|both> <target> [target...]");
    Console.WriteLine("  relic-affix apply        <regulation.bin> <dlc|nodlc> <normal|deep_night|both> <outPath> <target> [target...]");
    Console.WriteLine("  target: an attachEffectId (number) or an affix name (e.g. \"Vigor +1\")");
}

static void PrintReport(ForceReport r, string? outPath)
{
    Console.WriteLine($"pool    : {r.PoolId}");
    Console.WriteLine($"targets : {string.Join(", ", r.Targets)}");
    if (outPath != null) Console.WriteLine($"output  : {outPath}");
    Console.WriteLine($"summary : {r.TotalZeroed} entries -> dlc 0, {r.TotalMinusOne} target rows -> dlc -1");
    Console.WriteLine();
    Console.WriteLine("tables:");
    foreach (var t in r.Tables)
        Console.WriteLine(t.Present
            ? $"  {t.TableId,9}: {t.EntriesZeroed} entries zeroed (base sample {t.BaseSampleWeight})"
            : $"  {t.TableId,9}: ABSENT");
    Console.WriteLine();
    Console.WriteLine("target rows set to -1:");
    foreach (var h in r.Hits)
        Console.WriteLine($"  table {h.TableId,9} idx {h.Index,4}  attachEffId {h.AttachEffectId,9}  base {h.BaseWeight,4}{(h.BaseWeight == 0 ? " (WON'T ROLL)" : "")}  {RelicRegulation.StripPrefixSafe(h.Name)}");
    if (r.Warnings.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("warnings:");
        foreach (var w in r.Warnings) Console.WriteLine($"  - {w}");
    }
}
