/**
 * valid.ts — SQL queries that should pass the anyql/valid-sql lint rule.
 *
 * All queries reference tables and columns that exist in the schema:
 *   users(id, name, email)
 *   posts(id, user_id, title, body)
 */

// Minimal stubs so TypeScript compiles without a real DB library
const db = {
  query: (_sql: string, ..._args: unknown[]) => Promise.resolve<unknown[]>([]),
};
// Generic-capable sql tag stub — the type parameter is used only for lint-time
// column-type checking via the anyql/valid-sql rule.
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

// Simple SELECT — all columns exist in the schema
const _q1 = sql`SELECT id, name, email FROM users`;

// SELECT with a parameter expression
const userId = 1;
const _q2 = sql`SELECT id, name FROM users WHERE id = ${userId}`;

// INNER JOIN — valid columns from both tables
const _q3 = sql`
  SELECT u.id, u.name, p.title
  FROM users u
  JOIN posts p ON p.user_id = u.id
`;

// LEFT JOIN — right side columns nullable at runtime, but schema is valid
const _q4 = sql`
  SELECT u.id, u.name, p.title, p.body
  FROM users u
  LEFT JOIN posts p ON p.user_id = u.id
`;

// Aggregate query
const _q5 = sql`SELECT COUNT(*) AS total FROM users`;

// ── Method calls: db.query(...) ───────────────────────────────────────────────

// Plain string literal
await db.query("SELECT id, email FROM users WHERE id = $1");

// Template literal with expressions → $1, $2 parameters
const name = "Alice";
const email = "alice@example.com";
await db.query(
  `SELECT id FROM users WHERE name = ${name} AND email = ${email}`,
);

// Multi-param query
await db.query("SELECT id, title FROM posts WHERE user_id = $1 AND id > $2");

// ── Generic type annotations ────────────────────────────────────────────────

// Declared type matches what the DB actually returns
const _typed1 = sql<{
  id: number;
  name: string;
  email: string;
}>`SELECT id, name, email FROM users`;

// Subset of result columns is fine — you can select more than you declare
const _typed2 = sql<{
  id: number;
  name: string;
}>`SELECT id, name, email FROM users`;

// Column type with parameter
const _typed3 = sql<{
  id: number;
  name: string;
}>`SELECT id, name FROM users WHERE id = ${userId}::int4`;

// Nullable column from LEFT JOIN declared as string | null
const _typed4 = sql<{ id: number; title: string | null }>`
  SELECT u.id, p.title
  FROM users u
  LEFT JOIN posts p ON p.user_id = u.id
`;

// AS alias — declare the alias name, not the original column name
const _typed5 = sql<{ userId: number; userName: string }>`
  SELECT id AS userId, name AS userName FROM users
`;

// Wildcard — partial declaration is fine, DB resolves * to all columns
const _typed6 = sql<{ id: number; name: string }>`SELECT * FROM users`;

// Wildcard with full declaration
const _typed7 = sql<{
  id: number;
  name: string;
  email: string;
}>`SELECT * FROM users`;

// ── Aggregate functions ────────────────────────────────────────────────
// PostgreSQL int8 (bigint) cannot fit in a JS number, so COUNT/SUM return
// tsType "string". AVG returns numeric → also "string".

// COUNT(*) → string (int8/bigint)
const _agg1 = sql<{ total: string }>`SELECT COUNT(*) AS total FROM users`;

// MAX/MIN on int4 column → number
const _agg2 = sql<{ cnt: string; maxid: number; minid: number }>`
  SELECT COUNT(*) AS cnt, MAX(id) AS maxid, MIN(id) AS minid FROM users
`;

// SUM(int4) → int8 → string; AVG(int4) → numeric → string
const _agg3 = sql<{ sumid: string; avgid: string }>`
  SELECT SUM(id) AS sumid, AVG(id) AS avgid FROM users
`;

// MAX on text column → string
const _agg4 = sql<{ maxname: string }>`SELECT MAX(name) AS maxname FROM users`;

// COALESCE makes nullable column non-nullable → string
const _agg5 = sql<{
  body: string;
}>`SELECT COALESCE(body, '') AS body FROM posts`;

// CAST / :: operator
const _agg6 = sql<{ idstr: string }>`SELECT id::text AS idstr FROM users`;

// Timestamp functions → Date
const _agg7 = sql<{ ts: Date }>`SELECT NOW() AS ts`;
const _agg8 = sql<{ ts: Date }>`SELECT CURRENT_TIMESTAMP AS ts`;

// GROUP BY aggregate
const _agg9 = sql<{ total: string; maxid: number }>`
  SELECT COUNT(*) AS total, MAX(id) AS maxid FROM users GROUP BY name
`;

// ── Array types ─────────────────────────────────────────────────────────────

// int4[] → number[]
const _arr1 = sql<{ scores: number[] }>`SELECT scores FROM users LIMIT 1`;

// text[] → string[]
const _arr2 = sql<{ tags: string[] }>`SELECT tags FROM users LIMIT 1`;

// ── unknown / misc types ────────────────────────────────────────────────────

// jsonb → unknown
const _misc1 = sql<{ metadata: unknown }>`SELECT metadata FROM users LIMIT 1`;

// Uint8Array (bytea)
const _misc2 = sql<{ avatar: Uint8Array }>`SELECT avatar FROM users LIMIT 1`;

// ── Intersection types ───────────────────────────────────────────────────────

// Inline & inline intersection — both parts checked
const _int1 = sql<
  { id: number } & { name: string }
>`SELECT id, name FROM users LIMIT 1`;

// Named type & inline — UserBase is resolved from the same-file type alias,
// so both id: number (from UserBase) and name: string (inline) are checked.
type UserBase = { id: number };
const _int2 = sql<
  UserBase & { name: string }
>`SELECT id, name FROM users LIMIT 1`;

// Named type only — sql<UserBase> fully resolved via same-file alias
const _int3 = sql<UserBase>`SELECT id FROM users LIMIT 1`;

// ── interface declarations ───────────────────────────────────────────────────

// interface resolved from same-file declaration
interface UserInterface {
  id: number;
  name: string;
}
const _iface1 = sql<UserInterface>`SELECT id, name FROM users LIMIT 1`;

// interface & inline intersection
const _iface2 = sql<
  UserInterface & { email: string }
>`SELECT id, name, email FROM users LIMIT 1`;

// ── Record (composite type) ──────────────────────────────────────────────────

// PostgreSQL composite type → Record<string, unknown>
const _rec1 = sql<{
  addr: Record<string, unknown>;
}>`SELECT addr FROM users LIMIT 1`;
