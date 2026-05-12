// eslint-valid.ts: should pass anyql/valid-sql under ESLint + type-aware parser.

function sql<T = unknown>(
  strings: TemplateStringsArray,
  ...values: unknown[]
): T {
  return strings.reduce<string>(
    (acc, part, i) => acc + part + (values[i] ?? ""),
    "",
  ) as unknown as T;
}

const userName: string = "alice";

// Explicit cast keeps requiresCast=false and allows TS-vs-DB type comparison.
const _ok = sql`SELECT name FROM users WHERE name = ${userName}::text`;
