using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace TerrariaTas.Core.Watch;

/// <summary>
/// Reads <see cref="WatchPath"/>s with reflection. Fields and array or list elements are read as they are, which
/// never runs game code; a property only when the path calls it (<c>Name()</c>), apart from the lengths of arrays and
/// collections. The path starts with a type the given finder knows, whose static fields it reads, or with an alias.
/// </summary>
public sealed class WatchReader
{
    private const BindingFlags AnyVisibility = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly Func<string, Type?> _findType;
    private readonly IReadOnlyDictionary<string, Func<object?>> _aliases;
    private readonly Dictionary<(Type Type, string Name, bool Static), MemberInfo?> _members = new();

    /// <param name="findType">The type of a short or full name, or null. Its static fields must be safe to read at any time.</param>
    /// <param name="aliases">Names that stand for a value, e.g. "player" for the local player.</param>
    public WatchReader(Func<string, Type?> findType, IReadOnlyDictionary<string, Func<object?>>? aliases = null)
    {
        _findType = findType;
        _aliases = aliases ?? new Dictionary<string, Func<object?>>();
    }

    /// <summary>The value at the end of <paramref name="path"/>, written out; or why it cannot be read, starting with "!".</summary>
    public string Read(WatchPath path)
    {
        try
        {
            return WatchFormat.Value(Evaluate(path));
        }
        catch (WatchException e)
        {
            return "! " + e.Message;
        }
        catch (TargetInvocationException e)
        {
            return "! the property threw " + e.InnerException?.GetType().Name;
        }
    }

    /// <exception cref="WatchException">The path does not lead to a value right now.</exception>
    public object? Evaluate(WatchPath path)
    {
        var steps = path.Steps;
        var next = Root(steps, out var value, out var staticType);
        for (; next < steps.Count; next++)
        {
            var step = steps[next];
            if (step.Indices != null)
            {
                if (staticType != null)
                    throw new WatchException($"{Upto(steps, next)} is a type: name one of its fields before indexing.");
                value = Index(value, step.Indices, steps, next);
                continue;
            }

            var owner = staticType ?? value?.GetType() ?? throw new WatchException($"{Upto(steps, next)} is null.");
            value = Member(owner, staticType != null ? null : value, step, steps, next);
            staticType = null;
        }

        if (staticType != null)
            throw new WatchException($"{path.Text} is a type: name one of its fields, e.g. {path.Text}.{FirstFieldName(staticType)}.");
        return value;
    }

    /// <summary>The alias or type the path starts with: the shortest run of leading names that is one. Returns the next step.</summary>
    private int Root(IReadOnlyList<WatchStep> steps, out object? value, out Type? staticType)
    {
        value = null;
        staticType = null;
        if (!steps[0].IsCall && _aliases.TryGetValue(steps[0].Member!, out var alias))
        {
            value = alias();
            return 1;
        }

        var name = "";
        for (var i = 0; i < steps.Count && steps[i].Member != null && !steps[i].IsCall; i++)
        {
            name = i == 0 ? steps[0].Member! : name + "." + steps[i].Member;
            if (_findType(name) is { } type)
            {
                staticType = type;
                return i + 1;
            }
        }

        var aliases = _aliases.Count > 0 ? $" or {string.Join(", ", _aliases.Keys)}" : "";
        throw new WatchException($"'{steps[0].Member}' is not a class watches can start from{aliases}.");
    }

    private object? Member(Type owner, object? instance, WatchStep step, IReadOnlyList<WatchStep> steps, int index)
    {
        var name = step.Member!;
        var isStatic = instance == null;
        if (!isStatic && name is "Length" or "Count" && !step.IsCall && instance is Array or ICollection)
            return instance is Array array ? array.Length : ((ICollection)instance).Count;

        switch (Find(owner, name, isStatic))
        {
            case FieldInfo field:
                return field.GetValue(instance);
            case PropertyInfo property when step.IsCall:
                return property.GetValue(instance);
            case PropertyInfo:
                throw new WatchException($"{name} is a property: reading it runs game code, which a field read never does. Write {name}() to read it anyway.");
            default:
                throw new WatchException($"{Upto(steps, index)} ({Describe(owner)}) has no {(isStatic ? "static " : "")}field or property '{name}'.");
        }
    }

    /// <summary>A field or a property without parameters, declared by <paramref name="owner"/> or a base class.</summary>
    private MemberInfo? Find(Type owner, string name, bool isStatic)
    {
        if (_members.TryGetValue((owner, name, isStatic), out var known))
            return known;

        MemberInfo? found = null;
        var flags = AnyVisibility | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        for (var type = owner; type != null && found == null; type = type.BaseType)
        {
            found = (MemberInfo?)type.GetField(name, flags)
                    ?? type.GetProperties(flags).FirstOrDefault(property => property.Name == name && property.GetIndexParameters().Length == 0 && property.GetMethod != null);
        }

        _members[(owner, name, isStatic)] = found;
        return found;
    }

    private static object? Index(object? value, int[] indices, IReadOnlyList<WatchStep> steps, int index)
    {
        switch (value)
        {
            case null:
                throw new WatchException($"{Upto(steps, index)} is null.");
            case Array array when array.Rank == indices.Length:
                for (var dimension = 0; dimension < indices.Length; dimension++)
                {
                    if (indices[dimension] >= array.GetLength(dimension))
                        throw new WatchException($"{Upto(steps, index)} has {array.GetLength(dimension)} elements{(array.Rank > 1 ? $" in dimension {dimension + 1}" : "")}: no [{string.Join(",", indices)}].");
                }

                return array.GetValue(indices);
            case Array array:
                throw new WatchException($"{Upto(steps, index)} has {array.Rank} dimension{(array.Rank > 1 ? "s" : "")}: write [{string.Join(",", Enumerable.Repeat("i", array.Rank))}].");
            case IList list when indices.Length == 1:
                if (indices[0] >= list.Count)
                    throw new WatchException($"{Upto(steps, index)} has {list.Count} elements: no [{indices[0]}].");
                return list[indices[0]];
            default:
                throw new WatchException($"{Upto(steps, index)} ({Describe(value.GetType())}) cannot be indexed.");
        }
    }

    private static string Upto(IReadOnlyList<WatchStep> steps, int count)
    {
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (steps[i].Member != null && i > 0)
                text.Append('.');
            text.Append(steps[i]);
        }

        return text.ToString();
    }

    private static string Describe(Type type) => type.IsArray ? type.GetElementType()!.Name + "[]" : type.Name;

    private static string FirstFieldName(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => field.Name).FirstOrDefault() ?? "field";
}

/// <summary>A watch path that does not lead to a value; the message says where it stops.</summary>
public sealed class WatchException : Exception
{
    public WatchException(string message)
        : base(message)
    {
    }
}

/// <summary>Writes watched values: floats with every digit they hold, vectors and small structs field by field.</summary>
public static class WatchFormat
{
    public static string Value(object? value) => value switch
    {
        null => "null",
        float single => single.ToString("G9", CultureInfo.InvariantCulture),
        double number => number.ToString("G17", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        string text => "\"" + text + "\"",
        Enum choice => choice.ToString(),
        IFormattable number when value.GetType().IsPrimitive => number.ToString(null, CultureInfo.InvariantCulture),
        Array array => $"{Name(array.GetType())} ({array.Length} elements)",
        _ when value.GetType().IsValueType => Struct(value),
        _ => $"<{Name(value.GetType())}>",
    };

    /// <summary>"(x, y)" for two-field structs such as vectors and points, otherwise "{a=1, b=2}" (first level only).</summary>
    private static string Struct(object value)
    {
        var fields = value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
        if (fields.Length == 0)
            return $"<{Name(value.GetType())}>";

        var values = fields.Select(field => field.GetValue(value)).Select(field => field != null && field.GetType().IsValueType && !field.GetType().IsPrimitive && !(field is Enum) ? $"<{Name(field.GetType())}>" : Value(field));
        return fields.Length == 2 && fields.All(field => field.Name.Length == 1)
            ? $"({string.Join(", ", values)})"
            : "{" + string.Join(", ", fields.Zip(values, (field, text) => $"{field.Name}={text}")) + "}";
    }

    private static string Name(Type type) => type.IsArray ? Name(type.GetElementType()!) + "[]" : type.Name;
}
