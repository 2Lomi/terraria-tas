using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.Achievements;
using Terraria.GameContent.Events;
using Terraria.UI;

namespace TerrariaTas.Runtime.GameAccess;

/// <summary>
/// The game classes whose static fields hold global state, and which of those fields are plain values: numbers,
/// flags, enums, strings, structs made only of those, and one-dimensional arrays of them. Savestates restore every
/// plain static value of these classes; the --verify audit reads the same set.
/// </summary>
internal static class GlobalState
{
    /// <summary>Classes the game has certainly initialized once it runs: reading them triggers no static constructor.</summary>
    public static readonly Type[] Types =
    {
        typeof(Main), typeof(WorldGen), typeof(NPC), typeof(Player), typeof(Projectile), typeof(Item),
        typeof(Dust), typeof(Gore), typeof(Rain), typeof(Cloud), typeof(Star), typeof(Lighting), typeof(Liquid),
        typeof(Wiring), typeof(Collision), typeof(Sandstorm), typeof(BirthdayParty), typeof(DD2Event),
        typeof(LanternNight), typeof(CultistRitual), typeof(SoundEngine),

        // Achievement progress kept outside the achievements (Determinism/AchievementPins has the rest).
        typeof(AchievementsHelper),

        // Holders of game settings a movie fixes (TerrariaTas.Core.Movies.GameSettings), loaded with the configuration.
        typeof(ChildSafety), typeof(Player.SmartCursorSettings), typeof(Player.Settings), typeof(ItemSlot.Options), typeof(DoorOpeningHelper),
    };

    private static readonly Dictionary<Type, bool> PlainCache = new();

    public static IEnumerable<FieldInfo> StaticFields() =>
        Types.SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(field => !field.IsLiteral);

    /// <summary>"Main.numClouds"; auto-property backing fields read as the property.</summary>
    public static string NameOf(FieldInfo field)
    {
        var name = field.Name;
        if (name.StartsWith("<", StringComparison.Ordinal) && name.IndexOf('>') is var end and > 0)
            name = name.Substring(1, end - 1);
        return field.DeclaringType!.Name + "." + name;
    }

    /// <summary>Numbers, flags, enums and structs made only of those: copying the value copies the whole state.</summary>
    public static bool IsPlainValue(Type type)
    {
        if (type.IsEnum || type == typeof(decimal))
            return true;
        if (type.IsPrimitive)
            return type != typeof(IntPtr) && type != typeof(UIntPtr); // handles belong to the machine, not the game
        if (!type.IsValueType || type.IsPointer || type.IsByRef)
            return false;

        lock (PlainCache)
        {
            if (PlainCache.TryGetValue(type, out var known))
                return known;
            PlainCache[type] = false; // a struct containing itself cannot be plain
        }

        var plain = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).All(field => IsPlainValue(field.FieldType));
        lock (PlainCache)
            PlainCache[type] = plain;
        return plain;
    }

    public static bool IsPlainArray(Type type) => type.IsArray && type.GetArrayRank() == 1 && IsPlainValue(type.GetElementType()!);
}
