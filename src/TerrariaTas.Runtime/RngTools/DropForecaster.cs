using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using TerrariaTas.Core.Studio;
using TerrariaTas.Runtime.GameAccess;

namespace TerrariaTas.Runtime.RngTools;

/// <summary>
/// Drop forecast: for each offset k of a stream, what an NPC would drop if its loot code started k draws
/// after the current frame boundary. It runs the game's own drop code on copies of the stream
/// (<see cref="LootSandbox"/>), so no drop table is re-implemented.
/// </summary>
internal sealed class DropForecaster
{
    /// <summary>Where kills roll their loot: weapon swings, projectiles, and the NPC's own update (lava, debuffs).</summary>
    public static readonly IReadOnlyList<string> KillStreams = new[] { "UpdatePlayers", "UpdateProjectiles", "UpdateNPCs" };

    private const int MaxRows = 20_000;

    private readonly RngTracker _rng;

    public DropForecaster(RngTracker rng)
    {
        _rng = rng;
    }

    /// <summary>Call at a paused frame boundary inside a world.</summary>
    public DropForecast Forecast(ForecastRequest request, int frame)
    {
        var forecast = new DropForecast { RequestId = request.RequestId, Frame = frame, NpcSlot = request.NpcSlot };
        if (request.NpcSlot < 0 || request.NpcSlot >= Main.maxNPCs || Main.npc[request.NpcSlot] is not { active: true } npc)
        {
            forecast.Error = $"There is no NPC in slot {request.NpcSlot} any more.";
            return forecast;
        }

        var player = Main.player[Player.FindClosest(npc.position, npc.width, npc.height)];
        forecast.NetId = npc.netID;
        forecast.NpcName = npc.FullName;
        forecast.Luck = player.luck;
        forecast.Notes = NotesFor(npc);
        forecast.PossibleDrops = PossibleDrops(npc);

        var rows = Math.Max(1, Math.Min(request.Rows, MaxRows));
        var outcomeIds = new Dictionary<string, int>();
        var sandbox = new LootSandbox(npc, player);
        foreach (var stream in request.Streams.Count > 0 ? request.Streams : KillStreams)
            forecast.Columns.Add(Column(stream, rows, sandbox, forecast.Outcomes, outcomeIds));

        sandbox.EnsureUndone();
        return forecast;
    }

    private ForecastColumn Column(string name, int rows, LootSandbox sandbox, List<DropOutcome> outcomes, Dictionary<string, int> outcomeIds)
    {
        var column = new ForecastColumn { Stream = name, StartPosition = _rng.StartPositionOf(name) };
        var cursor = GameRng.CloneForNextFrame(name);
        column.Cells = new int[rows];
        for (var offset = 0; offset < rows; offset++)
        {
            var outcome = sandbox.Roll(GameRng.Clone(cursor));
            if (!outcomeIds.TryGetValue(outcome.Key, out var id))
            {
                id = outcomes.Count;
                outcomes.Add(outcome);
                outcomeIds[outcome.Key] = id;
            }

            column.Cells[offset] = id;
            cursor.Next();
        }

        return column;
    }

    private static string NotesFor(NPC npc)
    {
        var notes = new List<string>();
        if (npc.boss)
            notes.Add("Boss: the death events (potions, hearts, celebrations) are not simulated and draw before the coins, so coins and hearts can differ. Items are exact.");
        if (npc.SpawnedFromStatue)
            notes.Add("Statue enemy: its first draw decides whether it drops anything at all.");
        return string.Join(" ", notes);
    }

    /// <summary>The NPC's drop rules as the bestiary reports them.</summary>
    private static List<PossibleDrop> PossibleDrops(NPC npc)
    {
        var rates = new List<DropRateInfo>();
        var chain = new DropRateInfoChainFeed(1f);
        foreach (var rule in Main.ItemDropsDB.GetRulesForNPCID(npc.netID))
            rule.ReportDroprates(rates, chain);

        return rates
            .GroupBy(rate => rate.itemId)
            .Select(group => new PossibleDrop
            {
                ItemId = group.Key,
                Name = Lang.GetItemNameValue(group.Key),
                Chance = group.Max(rate => rate.dropRate),
                StackMin = group.Min(rate => rate.stackMin),
                StackMax = group.Max(rate => rate.stackMax),
            })
            .OrderByDescending(drop => drop.Chance)
            .ToList();
    }
}
