using System.Collections.Concurrent;
using AnyQL.Core.Models;
using AnyQL.Postgres.Protocol;

namespace AnyQL.Postgres;

/// <summary>
/// Process-wide TTL cache for PostgreSQL schema metadata.
///
/// Cached per connection key (host:port/database/user):
///   • attnotnull map  : (tableOid, attrNum) → notNull
///   • dynamic OID map : oid → (typname, typtype, elemOid)
///
/// Thread-safe via ConcurrentDictionary; individual entries are replaced atomically.
/// </summary>
public sealed class PgSchemaCache
{
    /// <summary>Shared default instance (used when no custom cache is supplied).</summary>
    public static readonly PgSchemaCache Default = new();

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    // ── Not-null cache ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the cached NOT NULL map for this connection, or null if the
    /// cache is missing / expired.
    /// </summary>
    internal IReadOnlyDictionary<(uint tableOid, short attrNum), bool>? GetNotNullMap(
        ConnectionInfo conn, AnalyzeOptions opts)
    {
        string key = CacheKey(conn);
        if (_entries.TryGetValue(key, out var entry) && !entry.IsExpired(opts.SchemaCacheTtl))
            return entry.NotNullMap;
        return null;
    }

    /// <summary>Stores or merges a NOT NULL map for this connection.</summary>
    internal void MergeNotNullMap(
        ConnectionInfo conn,
        IReadOnlyDictionary<(uint, short), bool> newEntries)
    {
        string key = CacheKey(conn);
        _entries.AddOrUpdate(
            key,
            _ => new CacheEntry(new Dictionary<(uint, short), bool>(newEntries),
                                new Dictionary<uint, DynamicTypeInfo>()),
            (_, existing) =>
            {
                var merged = new Dictionary<(uint, short), bool>(existing.NotNullMap);
                foreach (var kv in newEntries)
                    merged[kv.Key] = kv.Value;
                return new CacheEntry(merged, existing.DynamicTypeMap, existing.CreatedAt);
            });
    }

    // ── Dynamic OID cache ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the dynamic type map for this connection, or null if missing/expired.
    /// </summary>
    internal IReadOnlyDictionary<uint, DynamicTypeInfo>? GetDynamicTypeMap(
        ConnectionInfo conn, AnalyzeOptions opts)
    {
        string key = CacheKey(conn);
        if (_entries.TryGetValue(key, out var entry) && !entry.IsExpired(opts.SchemaCacheTtl))
            return entry.DynamicTypeMap;
        return null;
    }

    /// <summary>Stores or merges newly resolved dynamic OID entries.</summary>
    internal void MergeDynamicTypes(
        ConnectionInfo conn,
        IReadOnlyDictionary<uint, DynamicTypeInfo> newTypes)
    {
        string key = CacheKey(conn);
        _entries.AddOrUpdate(
            key,
            _ => new CacheEntry(new Dictionary<(uint, short), bool>(),
                                new Dictionary<uint, DynamicTypeInfo>(newTypes)),
            (_, existing) =>
            {
                var merged = new Dictionary<uint, DynamicTypeInfo>(existing.DynamicTypeMap);
                foreach (var kv in newTypes)
                    merged[kv.Key] = kv.Value;
                return new CacheEntry(existing.NotNullMap, merged, existing.CreatedAt);
            });
    }

    /// <summary>Evicts the cache entry for this connection.</summary>
    public void Invalidate(ConnectionInfo conn) => _entries.TryRemove(CacheKey(conn), out _);

    /// <summary>Removes all cached entries.</summary>
    public void InvalidateAll() => _entries.Clear();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string CacheKey(ConnectionInfo conn) =>
        $"{conn.Host}:{conn.Port}/{conn.Database}/{conn.User}";

    private sealed class CacheEntry(
        IReadOnlyDictionary<(uint, short), bool> notNullMap,
        IReadOnlyDictionary<uint, DynamicTypeInfo> dynamicTypeMap,
        DateTimeOffset? createdAt = null)
    {
        public IReadOnlyDictionary<(uint tableOid, short attrNum), bool> NotNullMap { get; } = notNullMap;
        public IReadOnlyDictionary<uint, DynamicTypeInfo> DynamicTypeMap { get; } = dynamicTypeMap;
        public DateTimeOffset CreatedAt { get; } = createdAt ?? DateTimeOffset.UtcNow;

        public bool IsExpired(TimeSpan ttl) =>
            // ttl == Zero means caching is disabled → always expired
            ttl == TimeSpan.Zero || DateTimeOffset.UtcNow - CreatedAt > ttl;
    }
}
