using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace TerrariaTas.Runtime.RngTools;

/// <summary>Compiles a copy of every writable instance field, inherited and private ones included, from one object to another.</summary>
internal static class FieldCopier
{
    public static Action<T, T> Create<T>() where T : class
    {
        var source = Expression.Parameter(typeof(T), "source");
        var target = Expression.Parameter(typeof(T), "target");
        var copies = InstanceFields(typeof(T))
            .Where(field => !field.IsInitOnly)
            .Select(field => (Expression)Expression.Assign(Expression.Field(target, field), Expression.Field(source, field)))
            .ToArray();
        return Expression.Lambda<Action<T, T>>(Expression.Block(copies), source, target).Compile();
    }

    private static IEnumerable<FieldInfo> InstanceFields(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            foreach (var field in current.GetFields(flags))
                yield return field;
        }
    }
}
