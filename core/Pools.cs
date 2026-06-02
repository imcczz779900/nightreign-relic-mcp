namespace RelicAffix;

/// <summary>Relic category. Normal = Delicate/Polished/Grand Scene; DeepNight = the "深夜" debuff relics.</summary>
public enum RelicType { Normal, DeepNight, Both }

/// <summary>A named set of AttachEffectTableParam table-group row IDs to operate on.</summary>
public sealed record Pool(string Id, string Label, int[] Tables);

public static class Pools
{
    // Table groups by (DLC owned?) x (relic type).
    // Normal   = Delicate/Polished/Grand Scene relics.
    // DeepNight= 深夜 relics (the debuff/curse pool; 3000000 is shared across DLC states).
    private static readonly int[] NormalNoDlc = { 100, 200, 300 };
    private static readonly int[] NormalDlc = { 110, 210, 310 };
    private static readonly int[] DeepNightNoDlc = { 2000000, 2100000, 3000000 };
    private static readonly int[] DeepNightDlc = { 2200000, 3000000 };

    /// <summary>
    /// Build the pool for a given DLC ownership and relic type.
    /// Always ask the user whether they own the DLC and whether they want normal / deep-night / both.
    /// </summary>
    public static Pool Resolve(bool hasDlc, RelicType type)
    {
        int[] normal = hasDlc ? NormalDlc : NormalNoDlc;
        int[] deep = hasDlc ? DeepNightDlc : DeepNightNoDlc;
        int[] tables = type switch
        {
            RelicType.Normal => normal,
            RelicType.DeepNight => deep,
            _ => normal.Concat(deep).Distinct().ToArray()
        };
        string dlcTag = hasDlc ? "DLC" : "no-DLC";
        return new Pool($"{type.ToString().ToLowerInvariant()}_{(hasDlc ? "dlc" : "no_dlc")}",
            $"{type} relics, {dlcTag}", tables);
    }

    public static RelicType ParseType(string s) => s.Trim().ToLowerInvariant() switch
    {
        "normal" or "普通" => RelicType.Normal,
        "deep_night" or "deepnight" or "深夜" => RelicType.DeepNight,
        "both" or "all" or "全部" => RelicType.Both,
        _ => throw new ArgumentException($"unknown relic type '{s}'. Use: normal | deep_night | both")
    };

    /// <summary>All six (dlc x type) combinations, for listing/help.</summary>
    public static IEnumerable<Pool> AllCombos()
    {
        foreach (bool dlc in new[] { false, true })
            foreach (var t in new[] { RelicType.Normal, RelicType.DeepNight, RelicType.Both })
                yield return Resolve(dlc, t);
    }
}
