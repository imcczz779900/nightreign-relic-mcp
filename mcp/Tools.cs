using System.ComponentModel;
using ModelContextProtocol.Server;
using RelicAffix;

namespace RelicAffix.Mcp;

[McpServerToolType]
public static class RelicAffixTools
{
    private const string ScopeHelp =
        "Scope is chosen by TWO things you must confirm with the user first: " +
        "(1) hasDlc - does the player own the DLC? (2) relicType - 'normal' (普通: Delicate/Polished/Grand Scene) " +
        "or 'deep_night' (深夜, the debuff/curse relics) or 'both'.";

    [McpServerTool, Description(
        "List the relic pools as (DLC ownership x relic type) combinations and the AttachEffectTableParam " +
        "table-group IDs each covers. " + ScopeHelp + " " +
        "Always ASK the user for hasDlc and relicType before editing.")]
    public static object ListPools()
        => new { pools = Pools.AllCombos().Select(p => new { id = p.Id, label = p.Label, tables = p.Tables }) };

    [McpServerTool, Description(
        "List the relic affixes available for the chosen scope together with current weights. " +
        "Affixes repeat across tables, so results are de-duplicated by affix name. " +
        "baseWeightPercent is how Smithbox shows the Roll Weight; dlcWeight is raw chanceWeight_dlc (-1 = use base). " +
        "Note: deep-night tables are unlabeled in the bundled data, so prefer listing 'normal' to see names. " + ScopeHelp)]
    public static object ListAffixes(
        [Description("Path to the regulation.bin to read (decrypted in memory; not modified).")] string regulation,
        [Description("Does the player own the DLC?")] bool hasDlc,
        [Description("Relic type: normal | deep_night | both")] string relicType,
        [Description("Optional filter: substring of the affix name or attachEffectId.")] string? query = null)
    {
        var reg = RelicRegulation.Load(regulation, Config.DefXml, Config.RowNames);
        var pool = Pools.Resolve(hasDlc, Pools.ParseType(relicType));
        var seen = new HashSet<string>();
        var affixes = new List<object>();
        foreach (int table in pool.Tables)
            foreach (var e in reg.EntriesForTable(table))
            {
                string key = e.BareName ?? e.AttachEffectId.ToString();
                if (!seen.Add(key)) continue;
                if (query != null
                    && !(e.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                    && !e.AttachEffectId.ToString().Contains(query)) continue;
                affixes.Add(new
                {
                    attachEffectId = e.AttachEffectId,
                    name = e.BareName,
                    baseWeightPercent = Math.Round(e.BaseWeight / 100.0, 3),
                    dlcWeight = e.DlcWeight
                });
            }
        return new { pool = pool.Id, tables = pool.Tables, count = affixes.Count, affixes, warnings = reg.Warnings };
    }

    [McpServerTool, Description(
        "Dry run. Preview the force-roll edit WITHOUT writing any file. For every table in the scope it would set all " +
        "entries' chanceWeight_dlc to 0 and each matched target to -1 (so only the targets can roll). " +
        "A target is an affix name (e.g. 'Vigor +1', resolved across tables incl. deep-night) or an attachEffectId number. " +
        "Heed warnings: a target with base weight 0 in a table will NOT roll there even at dlc=-1. " + ScopeHelp)]
    public static object PreviewChange(
        [Description("Path to the regulation.bin to read.")] string regulation,
        [Description("Does the player own the DLC?")] bool hasDlc,
        [Description("Relic type: normal | deep_night | both")] string relicType,
        [Description("Targets to force: affix names and/or attachEffectId numbers.")] string[] targets)
    {
        var reg = RelicRegulation.Load(regulation, Config.DefXml, Config.RowNames);
        var pool = Pools.Resolve(hasDlc, Pools.ParseType(relicType));
        var report = WeightEditor.ForceTargets(reg, pool, targets, apply: false);
        return ToDto(report, outPath: null, selfCheck: null);
    }

    [McpServerTool, Description(
        "Apply the force-roll edit for ONE relic and write a NEW regulation.bin. The input is NEVER overwritten. " +
        "ONE call = ONE relic (the given targets are the only affixes that can roll). " +
        "If the user wants MULTIPLE different relics, DO NOT merge their affixes into a single call - " +
        "use apply_relics instead (one output file per relic). " +
        "Confirm hasDlc and relicType with the user before calling. " +
        "After writing, the output is re-decrypted and verified (self-check). " +
        "Returns the output path, a change summary, warnings, and the self-check result. " +
        "Replace your game/mod regulation.bin with the output file manually. " + ScopeHelp)]
    public static object ApplyChange(
        [Description("Path to the source regulation.bin (read only).")] string regulation,
        [Description("Does the player own the DLC?")] bool hasDlc,
        [Description("Relic type: normal | deep_night | both")] string relicType,
        [Description("Targets to force: affix names and/or attachEffectId numbers.")] string[] targets,
        [Description("Optional output path. Defaults to '<input>.edited.bin' next to the source.")] string? outPath = null)
    {
        var reg = RelicRegulation.Load(regulation, Config.DefXml, Config.RowNames);
        var pool = Pools.Resolve(hasDlc, Pools.ParseType(relicType));
        var report = WeightEditor.ForceTargets(reg, pool, targets, apply: true);

        outPath ??= Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(regulation))!,
            Path.GetFileNameWithoutExtension(regulation) + ".edited.bin");
        reg.SaveAs(outPath);

        bool ok = Verifier.SelfCheck(outPath, pool, report, out string detail);
        return ToDto(report, outPath, ok ? "ok" : $"FAILED: {detail}");
    }

    [McpServerTool, Description(
        "Generate ONE regulation.bin PER relic. USE THIS whenever the user wants MORE THAN ONE relic. " +
        "Each item in 'relics' is a separate relic = its own set of target affixes, written to its own output file. " +
        "This is required because a single regulation can only force one relic's affix set: if you mixed several " +
        "relics' affixes into one file, every affix slot could roll ANY of them, so you could not obtain a specific " +
        "relic. Each output is built FRESH from the source regulation (source never modified) and self-checked. " +
        "All relics in one call share the same scope (hasDlc, relicType). " +
        "The user uses the files one at a time: rename a relic's file to regulation.bin, roll that relic, then swap " +
        "in the next. " + ScopeHelp)]
    public static object ApplyRelics(
        [Description("Path to the source regulation.bin (read only).")] string regulation,
        [Description("Does the player own the DLC?")] bool hasDlc,
        [Description("Relic type: normal | deep_night | both")] string relicType,
        [Description("One entry per relic. Each has 'targets' (affix names/attachEffectIds) and an optional 'name' for the file.")] RelicRequest[] relics,
        [Description("Optional output directory. Defaults to the source regulation's folder.")] string? outDir = null)
    {
        if (relics is null || relics.Length == 0)
            throw new ArgumentException("Provide at least one relic in 'relics'.");

        var pool = Pools.Resolve(hasDlc, Pools.ParseType(relicType));
        string stem = Path.GetFileNameWithoutExtension(regulation);
        string dir = outDir ?? Path.GetDirectoryName(Path.GetFullPath(regulation))!;

        var results = new List<object>();
        for (int i = 0; i < relics.Length; i++)
        {
            var relic = relics[i];
            var reg = RelicRegulation.Load(regulation, Config.DefXml, Config.RowNames); // fresh per relic
            var report = WeightEditor.ForceTargets(reg, pool, relic.Targets ?? Array.Empty<string>(), apply: true);

            string label = Sanitize(string.IsNullOrWhiteSpace(relic.Name) ? $"relic{i + 1}" : relic.Name!);
            string outPath = Path.Combine(dir, $"{stem}.{label}.bin");
            reg.SaveAs(outPath);

            bool ok = Verifier.SelfCheck(outPath, pool, report, out string detail);
            results.Add(ToDto(report, outPath, ok ? "ok" : $"FAILED: {detail}"));
        }
        return new { pool = pool.Id, relicCount = relics.Length, note = "one output file per relic; use them one at a time", relics = results };
    }

    private static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim().Replace(' ', '_');
    }

    private static object ToDto(ForceReport r, string? outPath, string? selfCheck) => new
    {
        pool = r.PoolId,
        targets = r.Targets,
        outputPath = outPath,
        selfCheck,
        summary = new { entriesZeroed = r.TotalZeroed, targetRowsSetToMinus1 = r.TotalMinusOne },
        tables = r.Tables.Select(t => new
        {
            tableId = t.TableId,
            present = t.Present,
            entriesZeroed = t.EntriesZeroed,
            baseSampleWeight = t.BaseSampleWeight
        }),
        targetRows = r.Hits.Select(h => new
        {
            tableId = h.TableId,
            index = h.Index,
            attachEffectId = h.AttachEffectId,
            name = RelicRegulation.StripPrefixSafe(h.Name),
            baseWeight = h.BaseWeight,
            willRollHere = h.BaseWeight > 0,
            oldDlcWeight = h.OldDlc,
            newDlcWeight = -1
        }),
        warnings = r.Warnings
    };
}

/// <summary>One relic for apply_relics: a set of target affixes and an optional name for its output file.</summary>
public sealed class RelicRequest
{
    [System.ComponentModel.Description("Affix names and/or attachEffectId numbers that this relic should force.")]
    public string[] Targets { get; set; } = Array.Empty<string>();

    [System.ComponentModel.Description("Optional short name used in the output filename (e.g. 'tank', 'relic1').")]
    public string? Name { get; set; }
}
