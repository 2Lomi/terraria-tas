using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using TerrariaTas.Runtime.GameAccess;

namespace TerrariaTas.Runtime.Verification;

/// <summary>
/// The value of every static field of the game classes that hold global state, as text. Comparing two
/// censuses finds what savestates do not restore (taken when saving vs right after loading) and what differs
/// between launches (taken at the movie start of two launches). Fields holding objects are skipped, except
/// arrays of plain values, which are hashed.
/// </summary>
internal static class StaticCensus
{
    private const int MaxArrayLength = 1 << 20;
    private const int MaxStructDepth = 3;

    /// <summary>Every static field of <see cref="GlobalState.Types"/> that holds a value (structs partly, when they also hold objects).</summary>
    public static SortedDictionary<string, string> Take()
    {
        var census = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in GlobalState.StaticFields())
        {
            if (field.IsInitOnly && field.FieldType.IsValueType)
                continue;
            if (TryRead(field, null, out var value) && Describe(field.FieldType, value, 0) is { } text)
                census[GlobalState.NameOf(field)] = text;
        }

        return census;
    }

    /// <summary>"name: before -> after" for each field present in both censuses with a different value.</summary>
    public static List<string> Differences(IDictionary<string, string> before, IDictionary<string, string> after) =>
        before.Where(entry => !Ignored.Contains(entry.Key) && after.TryGetValue(entry.Key, out var value) && value != entry.Value)
            .Select(entry => $"{entry.Key}: {entry.Value} -> {after[entry.Key]}")
            .ToList();

    // Measured from the real clock: the frame-rate display and automatic quality (pinned to High, which overrides
    // it). And the title logo, rolled at startup. No gameplay reads them.
    // The sizes of the background textures loaded so far, which depend on what the title screen drew before the world
    // loaded: only Main's background drawing reads them, and it loads a background at once when it first needs one.
    // Savestates leave them alone for that reason (SaveStates/StaticFieldState).
    private static readonly HashSet<string> Ignored = new()
    {
        "Main.fpsCount", "Main.drawsCountedForFPS", "Main.updatesCountedForFPS", "Main.uCount",
        "Main.frameRate", "Main.updateRate", "Main.gfxRate", "Main.lo",
        "Main.backgroundWidth", "Main.backgroundHeight",
    };

    public static string Write(IDictionary<string, string> census) =>
        string.Join("\n", census.Select(entry => entry.Key + "=" + entry.Value.Replace("\n", " ")));

    public static Dictionary<string, string> Parse(string text) =>
        text.Split('\n')
            .Select(line => line.Split(new[] { '=' }, 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1]);

    // Pointers, typed references and similar fields cannot be read through reflection; the census skips them.
    private static bool TryRead(FieldInfo field, object? owner, out object? value)
    {
        value = null;
        if (field.FieldType.IsPointer || field.FieldType.IsByRef)
            return false;
        try
        {
            value = field.GetValue(owner);
            return true;
        }
        catch (Exception e) when (e is NotSupportedException or FieldAccessException or TargetException or ArgumentException)
        {
            return false;
        }
    }

    private static string? Describe(Type type, object? value, int depth)
    {
        if (value == null)
            return type.IsValueType || type.IsArray || type == typeof(string) ? "null" : null; // empty Nullable<T> too
        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal))
            return Plain(value!);
        if (type == typeof(string))
            return value as string ?? "null";
        if (type.IsArray)
            return value is Array array ? DescribeArray(array) : "null";
        if (type.IsValueType && depth < MaxStructDepth)
            return DescribeStruct(type, value!, depth);
        return null;
    }

    private static string Plain(object value) => value switch
    {
        float single => single.ToString("R", CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    private static string? DescribeStruct(Type type, object value, int depth)
    {
        var parts = new List<string>();
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (TryRead(field, value, out var fieldValue) && Describe(field.FieldType, fieldValue, depth + 1) is { } text)
                parts.Add(text);
        }

        return parts.Count == 0 ? null : "{" + string.Join(" ", parts) + "}";
    }

    private static string? DescribeArray(Array array)
    {
        var element = array.GetType().GetElementType()!;
        if (array.Rank != 1 || array.Length > MaxArrayLength)
            return null;
        if (!element.IsPrimitive && !element.IsEnum && !(element.IsValueType && DescribeStruct(element, Activator.CreateInstance(element), 0) != null))
            return null;

        var hash = 14695981039346656037UL;
        void Add(byte value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        if (element.IsPrimitive)
        {
            var bytes = new byte[Buffer.ByteLength(array)];
            Buffer.BlockCopy(array, 0, bytes, 0, bytes.Length);
            foreach (var value in bytes)
                Add(value);
        }
        else
        {
            foreach (var item in array)
            {
                foreach (var character in element.IsEnum ? Plain(item) : DescribeStruct(element, item, 0) ?? "")
                    Add((byte)character);
                Add((byte)'|');
            }
        }

        return $"[{array.Length}] {hash:x16}";
    }
}
