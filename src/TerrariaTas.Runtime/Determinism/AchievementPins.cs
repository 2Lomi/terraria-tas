using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria;
using Terraria.Achievements;
using Terraria.UI;
using TerrariaTas.Runtime.GameAccess;

namespace TerrariaTas.Runtime.Determinism;

/// <summary>
/// Achievements are part of what a movie does: the inventory shows a card for an achievement not completed yet
/// (hovering it keeps clicks from the world, clicking it opens the achievements menu), and completing one shows a
/// popup that catches clicks for a few seconds. Terraria keeps them in the data folder's achievements.dat and writes
/// it as they complete, so a movie behaved differently once earlier sessions had completed something. Movies start
/// with none completed, as on a fresh install; TAS sessions never write the file; savestates keep the progress and
/// the popups.
/// </summary>
internal sealed class AchievementPins
{
    private static readonly FieldInfo Conditions = AccessTools.Field(typeof(Achievement), "_conditions");
    private static readonly FieldInfo AchievementTracker = AccessTools.Field(typeof(Achievement), "_tracker");
    private static readonly FieldInfo ConditionTracker = AccessTools.Field(typeof(AchievementCondition), "_tracker");
    private static readonly FieldInfo Notifications = AccessTools.Field(typeof(InGameNotificationsTracker), "_notifications");
    private static readonly MethodInfo ShallowCopy = AccessTools.Method(typeof(object), "MemberwiseClone");

    private (object Owner, FieldInfo Field)[]? _progress;

    public void Install(Harmony harmony) => harmony.CreateClassProcessor(typeof(Hooks)).Patch();

    /// <summary>No achievement completed and no popup showing, as in a fresh data folder.</summary>
    public void ResetForMovie()
    {
        var achievements = Main.Achievements.CreateAchievementsList();
        var completed = achievements.Count(achievement => achievement.IsCompleted);
        foreach (var achievement in achievements)
            achievement.ClearProgress();
        InGameNotificationsTracker.Clear();
        if (completed > 0)
            Log.Info($"The movie starts with no achievement completed ({completed} are in this data folder's achievements.dat, which TAS sessions never write).");
    }

    /// <summary>Savestate support: every completion flag, count and progress value, and copies of the popups showing.</summary>
    public object Capture() =>
        (Progress().Select(entry => entry.Field.GetValue(entry.Owner)).ToArray(), Popups().Cast<object>().Select(Copy).ToArray());

    public void Restore(object? saved)
    {
        var (values, popups) = ((object?[], object[]))saved!;
        var progress = Progress();
        for (var i = 0; i < progress.Length; i++)
            progress[i].Field.SetValue(progress[i].Owner, values[i]);

        var live = Popups();
        live.Clear();
        foreach (var popup in popups)
            live.Add(Copy(popup)); // copied again so that a slot can be loaded any number of times
    }

    private static IList Popups() => (IList)Notifications.GetValue(null);

    private static object Copy(object popup) => ShallowCopy.Invoke(popup, null);

    /// <summary>The plain fields of every achievement, condition and tracker (they are created once, at boot).</summary>
    private (object Owner, FieldInfo Field)[] Progress()
    {
        if (_progress != null)
            return _progress;

        var owners = new List<object>();
        foreach (var achievement in Main.Achievements.CreateAchievementsList())
        {
            owners.Add(achievement);
            foreach (AchievementCondition condition in ((IDictionary)Conditions.GetValue(achievement)).Values)
            {
                owners.Add(condition);
                if (ConditionTracker.GetValue(condition) is { } conditionTracker)
                    owners.Add(conditionTracker);
            }

            if (AchievementTracker.GetValue(achievement) is { } tracker)
                owners.Add(tracker);
        }

        _progress = owners.Distinct().SelectMany(owner => PlainFields(owner.GetType()).Select(field => (owner, field))).ToArray();
        return _progress;
    }

    private static IEnumerable<FieldInfo> PlainFields(Type? type)
    {
        for (; type != null && type != typeof(object); type = type.BaseType)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!field.IsInitOnly && GlobalState.IsPlainValue(field.FieldType))
                    yield return field;
            }
        }
    }

    [HarmonyPatch]
    private static class Hooks
    {
        [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.Save), new Type[0]), HarmonyPrefix]
        private static bool NoAchievementsFile() => false;
    }
}
