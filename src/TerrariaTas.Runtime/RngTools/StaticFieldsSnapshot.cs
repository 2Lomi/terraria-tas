using System;
using System.Linq;
using System.Reflection;
using TerrariaTas.Runtime.GameAccess;

namespace TerrariaTas.Runtime.RngTools;

/// <summary>
/// The plain static values of one game class, kept so that game code run for a prediction can be undone. Collision
/// checks, for example, report what they met through static flags (<c>Collision.up</c>, <c>honey</c>, <c>stair</c>...).
/// </summary>
internal sealed class StaticFieldsSnapshot
{
    private readonly FieldInfo[] _fields;
    private readonly object?[] _values;

    public StaticFieldsSnapshot(Type type)
    {
        _fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => !field.IsLiteral && !field.IsInitOnly && GlobalState.IsPlainValue(field.FieldType))
            .ToArray();
        _values = _fields.Select(field => field.GetValue(null)).ToArray();
    }

    public void Restore()
    {
        for (var i = 0; i < _fields.Length; i++)
            _fields[i].SetValue(null, _values[i]);
    }

    public bool IsCurrent() => _fields.Select((field, i) => Equals(field.GetValue(null), _values[i])).All(same => same);
}
