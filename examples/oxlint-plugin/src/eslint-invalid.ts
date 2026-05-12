// eslint-invalid.ts: should fail anyql/valid-sql with paramTypeMismatch.

function sql<T = unknown>(
  strings: TemplateStringsArray,
  ...values: unknown[]
): T {
  return strings.reduce<string>(
    (acc, part, i) => acc + part + (values[i] ?? ""),
    "",
  ) as unknown as T;
}

const userId: string = "not-a-number";

// Explicit cast keeps requiresCast=false; ESLint type-aware check should report
// string vs expected number mismatch for the parameter expression.
const _ng = sql`SELECT id FROM users WHERE id = ${userId}::int4`;
