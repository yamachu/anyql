using System.Text.RegularExpressions;

namespace AnyQL.Postgres;

/// <summary>
/// Detects whether a given parameter index already has an explicit cast in the SQL text.
/// Example: "$1::uuid" → index 1 has a cast.
/// </summary>
internal static partial class SqlCastDetector
{
    // Matches $N::typename (e.g. $1::uuid, $2::timestamptz, $3::int4[])
    [GeneratedRegex(@"\$(\d+)::([a-zA-Z_][a-zA-Z0-9_\[\]]*)", RegexOptions.Compiled)]
    private static partial Regex CastPattern();

    /// <summary>
    /// Returns a set of 1-based parameter indices that already carry an explicit
    /// PostgreSQL cast operator (::) in the SQL text.
    /// </summary>
    public static HashSet<int> FindExplicitlyCastParameters(string sql)
    {
        var result = new HashSet<int>();
        foreach (Match m in CastPattern().Matches(sql))
            result.Add(int.Parse(m.Groups[1].Value));
        return result;
    }
}
