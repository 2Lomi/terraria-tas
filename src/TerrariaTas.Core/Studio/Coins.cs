using System.Collections.Generic;

namespace TerrariaTas.Core.Studio;

/// <summary>Terraria money: 100 copper make a silver, 100 silver a gold, 100 gold a platinum.</summary>
public static class Coins
{
    private const long Silver = 100;
    private const long Gold = 100 * Silver;
    private const long Platinum = 100 * Gold;

    /// <summary>"1p 2g 3s 4c", leaving out zero denominations.</summary>
    public static string Format(long copper)
    {
        if (copper <= 0)
            return "0c";

        var parts = new List<string>();
        Add(parts, copper / Platinum, "p");
        Add(parts, copper % Platinum / Gold, "g");
        Add(parts, copper % Gold / Silver, "s");
        Add(parts, copper % Silver, "c");
        return string.Join(" ", parts);
    }

    private static void Add(List<string> parts, long amount, string unit)
    {
        if (amount > 0)
            parts.Add(amount + unit);
    }
}
