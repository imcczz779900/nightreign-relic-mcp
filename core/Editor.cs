namespace RelicAffix;

/// <summary>A target resolved against a regulation: a set of attachEffectIds to force.</summary>
public sealed class ResolvedTarget
{
    public required string Raw { get; init; }
    public required HashSet<long> AttachEffectIds { get; init; }
    public string? UnresolvedName { get; init; }

    public bool Matches(Entry e) => AttachEffectIds.Contains(e.AttachEffectId);

    public override string ToString() =>
        UnresolvedName != null ? $"\"{UnresolvedName}\" (UNRESOLVED)" :
        long.TryParse(Raw, out _) ? $"attachEffectId {Raw}" :
        $"\"{Raw}\" -> attachEffectId {string.Join("/", AttachEffectIds)}";

    /// <summary>
    /// Parse a raw target. A number is an attachEffectId; anything else is an affix name resolved
    /// to attachEffectIds via the regulation's global name index (so it also covers unlabeled
    /// deep-night tables that share the same attachEffectIds).
    /// </summary>
    public static ResolvedTarget Resolve(string raw, RelicRegulation reg)
    {
        raw = raw.Trim();
        if (long.TryParse(raw, out var id))
            return new ResolvedTarget { Raw = raw, AttachEffectIds = new() { id } };

        var ids = reg.ResolveName(raw).ToHashSet();
        return new ResolvedTarget
        {
            Raw = raw,
            AttachEffectIds = ids,
            UnresolvedName = ids.Count == 0 ? raw : null
        };
    }
}

public sealed record HitInfo(int TableId, int Index, long AttachEffectId, string? Name, int OldDlc, int BaseWeight);

public sealed record TableReport(int TableId, int EntriesZeroed, bool Present, int BaseSampleWeight);

public sealed record ForceReport(
    string PoolId,
    IReadOnlyList<string> Targets,
    IReadOnlyList<TableReport> Tables,
    IReadOnlyList<HitInfo> Hits,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public int TotalZeroed => Tables.Sum(t => t.EntriesZeroed);
    public int TotalMinusOne => Hits.Count;
    public bool CanApply => Errors.Count == 0;
}

public static class WeightEditor
{
    /// <summary>
    /// Force-roll plan: in every table of the pool, set all entries' DLC weight to 0,
    /// then set each matched target's DLC weight to -1 (fall back to base weight).
    /// When apply == false this only computes the report (dry run, no mutation).
    /// </summary>
    public static ForceReport ForceTargets(RelicRegulation reg, Pool pool, IReadOnlyList<string> rawTargets, bool apply)
    {
        rawTargets ??= Array.Empty<string>();
        var cleanTargets = rawTargets.Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        var targets = cleanTargets.Select(t => ResolvedTarget.Resolve(t, reg)).ToList();
        var hits = new List<HitInfo>();
        var tableReports = new List<TableReport>();
        var warnings = new List<string>();
        var errors = new List<string>();
        var tablesToZero = new Dictionary<int, int>();
        var targetRows = new List<(int TableId, int Index)>();

        if (cleanTargets.Count == 0)
            errors.Add("Provide at least one target affix name or attachEffectId.");

        foreach (var t in targets.Where(t => t.UnresolvedName != null))
        {
            warnings.Add($"target \"{t.UnresolvedName}\": no affix with that name found; cannot resolve to an attachEffectId. " +
                         "Use the exact name (see list_affixes) or an attachEffectId number.");
            errors.Add($"target \"{t.UnresolvedName}\": unresolved affix name.");
        }

        foreach (int table in pool.Tables)
        {
            var entries = reg.EntriesForTable(table);
            if (entries.Count == 0)
            {
                tableReports.Add(new TableReport(table, 0, Present: false, 0));
                warnings.Add($"table {table}: not present in this regulation; skipped.");
                continue;
            }

            if (reg.NameMisalignment(table) is string mis) warnings.Add(mis);

            tablesToZero[table] = entries.Count;

            foreach (var t in targets)
                foreach (var e in entries.Where(t.Matches))
                {
                    hits.Add(new HitInfo(table, e.IndexInTable, e.AttachEffectId, e.Name, e.DlcWeight, e.BaseWeight));
                    targetRows.Add((table, e.IndexInTable));
                    if (e.BaseWeight == 0)
                        warnings.Add($"table {table} attachEffId {e.AttachEffectId} ({RelicRegulation.StripPrefixSafe(e.Name)}): " +
                                     "base weight is 0, so dlc=-1 falls back to 0 and it will NOT roll here " +
                                     "(this affix is not native to this relic table).");
                }

            tableReports.Add(new TableReport(table, entries.Count, Present: true, entries[0].BaseWeight));
        }

        foreach (var t in targets.Where(t => t.UnresolvedName == null))
        {
            var coveredTables = pool.Tables.Where(tb => reg.EntriesForTable(tb).Any(t.Matches)).ToList();
            if (coveredTables.Count == 0)
            {
                warnings.Add($"target {t}: matched NO entry in any table of pool '{pool.Id}'.");
                errors.Add($"target {t}: matched no entry in pool '{pool.Id}'.");
            }
            else
            {
                var missing = pool.Tables.Except(coveredTables).ToList();
                if (missing.Count > 0)
                    warnings.Add($"target {t}: not found in tables [{string.Join(",", missing)}] (covered [{string.Join(",", coveredTables)}]).");
            }
        }

        if (apply)
        {
            if (errors.Count > 0)
                throw new InvalidOperationException("Refusing to write because the requested edit is invalid: " + string.Join(" ", errors.Distinct()));

            foreach (var entry in tablesToZero)
                for (int i = 0; i < entry.Value; i++) reg.SetDlc(entry.Key, i, 0);

            foreach (var row in targetRows)
                reg.SetDlc(row.TableId, row.Index, -1);
        }

        return new ForceReport(pool.Id, cleanTargets, tableReports, hits, warnings.Distinct().ToList(), errors.Distinct().ToList());
    }
}
