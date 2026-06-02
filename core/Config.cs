namespace RelicAffix;

/// <summary>
/// Resolves the bundled PARAMDEF and row-name files. Environment variables can override
/// the built-in assets when testing a different Smithbox data set.
/// </summary>
public static class Config
{
    private static string AssetRoot => Path.Combine(AppContext.BaseDirectory, "Assets");

    public static string DefXml =>
        Path.Combine(Environment.GetEnvironmentVariable("DEFS_DIR") ?? Path.Combine(AssetRoot, "Defs"),
            "AttachEffectTableParam.xml");

    public static string RowNames =>
        Environment.GetEnvironmentVariable("ROWNAMES") ?? Path.Combine(AssetRoot, "RowNames", "AttachEffectTableParam.json");
}

public static class Verifier
{
    /// <summary>
    /// Re-loads a written regulation and confirms every scope-table entry has the intended DLC weight:
    /// -1 for rows that were targeted, 0 for all others.
    /// </summary>
    public static bool SelfCheck(string outPath, Pool pool, ForceReport report, out string detail)
    {
        var reg = RelicRegulation.Load(outPath, Config.DefXml, Config.RowNames);
        var minusOne = report.Hits.Select(h => (h.TableId, h.AttachEffectId)).ToHashSet();
        foreach (int table in pool.Tables)
            foreach (var e in reg.EntriesForTable(table))
            {
                int expected = minusOne.Contains((table, e.AttachEffectId)) ? -1 : 0;
                if (e.DlcWeight != expected)
                {
                    detail = $"table {table} attachEffId {e.AttachEffectId}: dlc={e.DlcWeight}, expected {expected}";
                    return false;
                }
            }
        detail = "";
        return true;
    }
}
