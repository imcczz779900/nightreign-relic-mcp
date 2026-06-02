namespace RelicAffix;

/// <summary>
/// Resolves the bundled PARAMDEF and row-name files. Defaults point at the Smithbox repo;
/// override with the DEFS_DIR and ROWNAMES environment variables.
/// </summary>
public static class Config
{
    private const string RepoDefs = @"C:\Users\32445\Desktop\Smithbox\src\Smithbox.Data\Assets\PARAM\NR\Defs";
    private const string RepoRowNames = @"C:\Users\32445\Desktop\Smithbox\src\Smithbox.Data\Assets\PARAM\NR\Param Row Names\English\AttachEffectTableParam.json";

    public static string DefXml =>
        Path.Combine(Environment.GetEnvironmentVariable("DEFS_DIR") ?? RepoDefs, "AttachEffectTableParam.xml");

    public static string RowNames =>
        Environment.GetEnvironmentVariable("ROWNAMES") ?? RepoRowNames;
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
