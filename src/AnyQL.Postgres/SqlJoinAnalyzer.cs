using System.Text.RegularExpressions;

namespace AnyQL.Postgres;

/// <summary>
/// Inspects SQL text to determine which tables appear on the nullable side of JOINs.
/// Used to override pg_attribute.attnotnull for columns that may be NULL due to
/// LEFT / RIGHT / FULL OUTER JOIN semantics.
/// </summary>
internal static partial class SqlJoinAnalyzer
{
    // Matches LEFT [OUTER] JOIN [schema.]table  — captures only the table name.
    // We stop at word boundaries; the table name is always the first identifier after JOIN.
    [GeneratedRegex(
        @"\bLEFT\s+(?:OUTER\s+)?JOIN\s+(?:\w+\.)?(?<table>\w+)",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex LeftJoinRegex();

    // Matches [schema.]table [[AS] alias] in the FROM clause only
    // Used to resolve "all tables on left side" for RIGHT JOIN
    [GeneratedRegex(
        @"\bFROM\s+(?:\w+\.)?(\w+)(?:\s+(?:AS\s+)?(\w+))?",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex FromClauseRegex();

    // Matches RIGHT [OUTER] JOIN
    [GeneratedRegex(
        @"\bRIGHT\s+(?:OUTER\s+)?JOIN\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex RightJoinRegex();

    // Matches FULL [OUTER] JOIN
    [GeneratedRegex(
        @"\bFULL\s+(?:OUTER\s+)?JOIN\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex FullJoinRegex();

    /// <summary>
    /// Returns the set of unqualified table names that are on the nullable side
    /// of any JOIN in the given SQL.
    ///
    /// Rules:
    ///   LEFT JOIN  → right-side table is nullable
    ///   RIGHT JOIN → all tables accumulated so far (FROM + previous JOINs) are nullable
    ///   FULL JOIN  → all tables on both sides are nullable
    ///
    /// Returns an empty set when there are no outer JOINs.
    /// </summary>
    public static NullableJoinInfo Analyze(string sql)
    {
        bool hasRightOrFull = RightJoinRegex().IsMatch(sql) || FullJoinRegex().IsMatch(sql);

        // For RIGHT / FULL JOIN we conservatively return "all table names are nullable"
        // (nullability becomes indeterminate for any joined column → IsNullable = null)
        if (hasRightOrFull)
            return new NullableJoinInfo(AllNullable: true, NullableTableNames: null);

        // Collect right-side table names from LEFT JOINs
        var nullableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in LeftJoinRegex().Matches(sql))
        {
            nullableNames.Add(m.Groups["table"].Value); // unqualified table name
        }

        return new NullableJoinInfo(AllNullable: false, NullableTableNames: nullableNames);
    }
}

/// <summary>
/// Result of <see cref="SqlJoinAnalyzer.Analyze"/>.
/// </summary>
internal sealed record NullableJoinInfo(
    /// <summary>
    /// When true, every column from a joined table should be treated as
    /// nullable = null (unknown) because RIGHT / FULL JOIN semantics are too
    /// complex to determine statically without a full SQL parser.
    /// </summary>
    bool AllNullable,
    /// <summary>
    /// Unqualified table names whose columns should be forced to IsNullable = true
    /// (nullable because they are on the right-hand side of a LEFT JOIN).
    /// Null when <see cref="AllNullable"/> is true.
    /// </summary>
    IReadOnlySet<string>? NullableTableNames)
{
    public bool HasOuterJoins => AllNullable || (NullableTableNames?.Count > 0);
}
