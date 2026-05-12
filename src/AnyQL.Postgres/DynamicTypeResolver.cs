using AnyQL.Postgres.Protocol;

namespace AnyQL.Postgres;

/// <summary>
/// Derives .NET and TypeScript type names for PostgreSQL OIDs that are not
/// present in the static built-in map (user-defined types, custom domains, enums, etc.).
/// </summary>
internal static class DynamicTypeResolver
{
    /// <summary>
    /// Given a <see cref="DynamicTypeInfo"/> and optionally the resolved element type info
    /// (for arrays), return suitable .NET and TypeScript type names.
    /// </summary>
    public static (string dotNetType, string tsType) Resolve(
        DynamicTypeInfo info,
        DynamicTypeInfo? elemInfo = null)
    {
        return info.TypeType switch
        {
            'e' => ResolveEnum(info),
            'c' => ResolveComposite(info),
            'd' => ResolveDomain(info),
            'r' => ResolveRange(info),
            'b' when info.TypeName.StartsWith('_') => ResolveArray(info, elemInfo),
            _ => ("string", "string"),
        };
    }

    // ── enum → string literal union would be ideal, but without knowing values
    //    we fall back to 'string'. The type name is surfaced via DbTypeName.
    private static (string, string) ResolveEnum(DynamicTypeInfo info) =>
        ("string", "string");

    // ── composite → object / Record
    private static (string, string) ResolveComposite(DynamicTypeInfo info) =>
        ("object", $"Record<string, unknown> /* {info.Namespace}.{info.TypeName} */");

    // ── domain → treat as its base type (we don't recurse here; base type
    //    is looked up upstream if needed)
    private static (string, string) ResolveDomain(DynamicTypeInfo info) =>
        ("string", "string");

    // ── range → string (e.g. "[2023-01-01,2024-01-01)")
    private static (string, string) ResolveRange(DynamicTypeInfo info) =>
        ("string", "string");

    // ── array of a dynamic element type
    private static (string, string) ResolveArray(DynamicTypeInfo _, DynamicTypeInfo? elem)
    {
        if (elem is null) return ("object[]", "unknown[]");
        var (elemDotNet, elemTs) = Resolve(elem);
        return ($"{elemDotNet}[]", $"{elemTs}[]");
    }
}
