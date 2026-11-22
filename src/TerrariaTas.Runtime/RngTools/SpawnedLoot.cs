using Terraria;
using Terraria.ID;
using TerrariaTas.Core.Studio;

namespace TerrariaTas.Runtime.RngTools;

/// <summary>
/// Reads what loot code spawned. Item.NewItem always stores a new object in the item table, so
/// comparing references with an earlier copy of the table finds exactly the new items.
/// </summary>
internal static class SpawnedLoot
{
    public static WorldItem[] CopyItemTable() => (WorldItem[])Main.item.Clone();

    public static DropOutcome Since(WorldItem[] before)
    {
        var outcome = new DropOutcome();
        for (var i = 0; i < Main.item.Length; i++)
        {
            var item = Main.item[i];
            if (item == before[i] || item is not { active: true })
                continue;

            var coinValue = CoinValue(item.type);
            if (coinValue > 0)
                outcome.Copper += coinValue * item.stack;
            else
                outcome.Items.Add(new DroppedItem
                {
                    ItemId = item.type,
                    Name = item.inner.AffixName(),
                    Stack = item.stack,
                    Prefix = item.inner.prefix,
                    PrefixName = item.inner.prefix > 0 ? Lang.prefix[item.inner.prefix].Value : "",
                });
        }

        // Slots depend on what else lies around; sort so that identical loot always reads the same.
        outcome.Items.Sort((a, b) => a.ItemId != b.ItemId ? a.ItemId.CompareTo(b.ItemId) : a.Prefix.CompareTo(b.Prefix));
        return outcome;
    }

    private static long CoinValue(int itemType) => itemType switch
    {
        ItemID.CopperCoin => 1,
        ItemID.SilverCoin => 100,
        ItemID.GoldCoin => 10_000,
        ItemID.PlatinumCoin => 1_000_000,
        _ => 0,
    };
}
