/**
 * invalid.ts — SQL queries that should trigger the anyql/valid-sql lint rule.
 *
 * Each query contains an intentional error:
 *   - referencing a non-existent table
 *   - referencing a non-existent column
 *   - SQL syntax error
 */

// Minimal stubs so TypeScript compiles without a real DB library
const db = {
  query: (_sql: string, ..._args: unknown[]) => Promise.resolve<unknown[]>([]),
};
function sql<T = unknown>(
  strings: TemplateStringsArray,
  ...values: unknown[]
): T {
  return strings.reduce<string>(
    (acc, part, i) => acc + part + (values[i] ?? ""),
    "",
  ) as unknown as T;
}

// ── Tagged template literals ──────────────────────────────────────────────────

// ❌ Non-existent table
const _q1 = sql`SELECT * FROM nonexistent_table`;

// ❌ Non-existent column (users table has no "age" column)
const _q2 = sql`SELECT id, age FROM users`;

// ❌ Non-existent column in JOIN
const _q3 = sql`
  SELECT u.id, p.views
  FROM users u
  JOIN posts p ON p.user_id = u.id
`;

// ── Method calls: db.query(...) ───────────────────────────────────────────────

// ❌ Non-existent table in method call
await db.query("SELECT id FROM orders WHERE id = $1");

// ❌ Non-existent column in method call
await db.query("SELECT id, created_at FROM users WHERE id = $1");

// ── Generic type annotations ────────────────────────────────────────────────

// ❌ Type mismatch — id is number but string was declared
const _typed1 = sql<{ id: string }>`SELECT id FROM users`;

// ❌ Missing result column — the query does not return 'age'
const _typed2 = sql<{ id: number; age: number }>`SELECT id FROM users`;

// ❌ AS alias mismatch — declared the original name instead of the alias
const _typed3 = sql<{ id: number }>`SELECT id AS userId FROM users`;

// ❌ Wildcard with wrong declared type
const _typed4 = sql<{ createdAt: Date }>`SELECT * FROM users`;

// ── Aggregate function type mismatches ───────────────────────────────────────

// ❌ COUNT(*) is int8 → tsType "string", not number
const _agg1 = sql<{ total: number }>`SELECT COUNT(*) AS total FROM users`;

// ❌ SUM returns int8 → string, not number
const _agg2 = sql<{ sumid: number }>`SELECT SUM(id) AS sumid FROM users`;

// ❌ AVG returns numeric → string, not number
const _agg3 = sql<{ avgid: number }>`SELECT AVG(id) AS avgid FROM users`;

// ❌ NOW() is timestamptz → Date, not string
const _agg4 = sql<{ ts: string }>`SELECT NOW() AS ts`;

// ── Array / misc type mismatches ─────────────────────────────────────────────

// ❌ int4[] → number[], declared string[]
const _arr1 = sql<{ scores: string[] }>`SELECT scores FROM users LIMIT 1`;

// ❌ text[] → string[], declared number[]
const _arr2 = sql<{ tags: number[] }>`SELECT tags FROM users LIMIT 1`;

// ── Intersection type mismatches ─────────────────────────────────────────────

// ❌ Inline intersection — right part has wrong type (id is number, not string)
const _int1 = sql<
  { name: string } & { id: string }
>`SELECT id, name FROM users LIMIT 1`;

// ❌ Named type has wrong type — resolved from same-file alias (id is number, not string)
type WrongUser = { id: string };
const _int2 = sql<WrongUser>`SELECT id FROM users LIMIT 1`;

// ── interface mismatches ──────────────────────────────────────────────────────

// ❌ interface with wrong type — id is number, not string
interface WrongIface {
  id: string;
}
const _iface1 = sql<WrongIface>`SELECT id FROM users LIMIT 1`;

// ── Record mismatches ─────────────────────────────────────────────────────────

// ❌ composite type → Record<string, unknown>, but string was declared
const _rec1 = sql<{ addr: string }>`SELECT addr FROM users LIMIT 1`;
