/**
 * E2E tests for the anyql npm package using a real PostgreSQL container.
 * The container is started/stopped by test/globalSetup.ts.
 */

import { describe, it, expect, beforeAll } from "vitest";
import { analyze } from "../dist/index.js";
import type { ConnectionInfo } from "../dist/types.js";

let conn: ConnectionInfo;

beforeAll(() => {
  conn = {
    dialect: "postgresql",
    host: process.env.TEST_PG_HOST!,
    port: Number(process.env.TEST_PG_PORT),
    user: process.env.TEST_PG_USER!,
    password: process.env.TEST_PG_PASS!,
    database: process.env.TEST_PG_DB!,
  };
});

// ── Column metadata ──────────────────────────────────────────────────────────

describe("column metadata", () => {
  it("basic SELECT returns correct column types and nullability", async () => {
    const result = await analyze(
      "SELECT id, name, price, tag FROM products",
      conn,
    );

    expect(result.columns).toHaveLength(4);
    expect(result.columns[0].name).toBe("id");
    expect(result.columns[1].name).toBe("name");
    expect(result.columns[0].isNullable).toBe(false); // serial PK NOT NULL
    expect(result.columns[1].isNullable).toBe(false); // NOT NULL
    expect(result.columns[3].isNullable).toBe(true); // tag is nullable
    expect(result.parameters).toHaveLength(0);
  });

  it("table alias + wildcard expands to all columns with correct nullability", async () => {
    const result = await analyze("SELECT p.* FROM products p", conn);

    // id, name, price, tag, category_id  → 5 cols
    expect(result.columns).toHaveLength(5);
    expect(result.columns[0].name).toBe("id");
    expect(result.columns[0].isNullable).toBe(false);
    expect(result.columns[1].isNullable).toBe(false); // name NOT NULL
    expect(result.columns[3].isNullable).toBe(true); // tag nullable
    expect(result.columns[4].isNullable).toBe(true); // category_id nullable
  });

  it("aggregate expressions have unknown nullability", async () => {
    const result = await analyze(
      "SELECT COUNT(*) AS cnt, MAX(price) AS max_price FROM products",
      conn,
    );

    expect(result.columns).toHaveLength(2);
    expect(result.columns[0].name).toBe("cnt");
    expect(result.columns[0].isNullable).toBeNull();
    expect(result.columns[1].isNullable).toBeNull();
  });
});

// ── INNER JOIN ───────────────────────────────────────────────────────────────

describe("INNER JOIN", () => {
  it("preserves original nullability from pg_attribute", async () => {
    const result = await analyze(
      `SELECT p.id AS product_id, p.name AS product_name, c.label AS category
       FROM products p
       JOIN categories c ON c.id = p.category_id`,
      conn,
    );

    expect(result.columns).toHaveLength(3);
    expect(result.columns[0].name).toBe("product_id");
    expect(result.columns[1].name).toBe("product_name");
    expect(result.columns[2].name).toBe("category");
    // INNER JOIN does not affect nullability
    expect(result.columns[0].isNullable).toBe(false); // serial PK
    expect(result.columns[1].isNullable).toBe(false); // NOT NULL
    expect(result.columns[2].isNullable).toBe(false); // categories.label NOT NULL
  });

  it("wildcard on both JOIN sides returns all columns", async () => {
    const result = await analyze(
      `SELECT p.*, c.*
       FROM products p
       JOIN categories c ON c.id = p.category_id`,
      conn,
    );

    // products: id, name, price, tag, category_id (5) + categories: id, label (2) = 7
    expect(result.columns).toHaveLength(7);
    expect(result.columns[6].name).toBe("label");
    expect(result.columns[6].isNullable).toBe(false);
  });
});

// ── LEFT JOIN nullability ────────────────────────────────────────────────────

describe("LEFT JOIN nullability", () => {
  it("right-side columns are forced nullable even if schema says NOT NULL", async () => {
    const result = await analyze(
      `SELECT p.id AS product_id, p.name, c.label
       FROM products p
       LEFT JOIN categories c ON c.id = p.category_id`,
      conn,
    );

    expect(result.columns).toHaveLength(3);
    // left side (products) — nullability from pg_attribute
    expect(result.columns[0].isNullable).toBe(false); // products.id PK
    expect(result.columns[1].isNullable).toBe(false); // products.name NOT NULL
    // right side (categories) — forced nullable by LEFT JOIN
    expect(result.columns[2].isNullable).toBe(true); // categories.label NOT NULL in schema → nullable
  });

  it("wildcard on right side: all columns are nullable", async () => {
    const result = await analyze(
      `SELECT p.id, c.*
       FROM products p
       LEFT JOIN categories c ON c.id = p.category_id`,
      conn,
    );

    // c.* expands to categories: id, label  → both forced nullable
    const categoryColumns = result.columns.slice(1); // skip p.id
    for (const col of categoryColumns) {
      expect(col.isNullable).toBe(true);
    }
  });
});

// ── Parameter type inference ─────────────────────────────────────────────────

describe("parameter type inference", () => {
  it("parameters matched to typed columns have requiresCast = true", async () => {
    const result = await analyze(
      "SELECT id, name FROM products WHERE id = $1 AND price > $2",
      conn,
    );

    expect(result.parameters).toHaveLength(2);
    // $1 matched against id (int4) — text literal needs cast
    expect(result.parameters[0].requiresCast).toBe(true);
    // $2 matched against price (numeric) — text literal needs cast
    expect(result.parameters[1].requiresCast).toBe(true);
  });

  it("explicit SQL cast removes requiresCast for int parameter", async () => {
    const result = await analyze(
      "SELECT id FROM products WHERE id = $1::int4",
      conn,
    );

    expect(result.parameters).toHaveLength(1);
    expect(result.parameters[0].index).toBe(1);
    expect(result.parameters[0].tsType).toBe("number");
    expect(result.parameters[0].requiresCast).toBe(false);
  });

  it("explicit SQL cast removes requiresCast for text parameter", async () => {
    const result = await analyze(
      "SELECT name FROM products WHERE name = $1::text",
      conn,
    );

    expect(result.parameters).toHaveLength(1);
    expect(result.parameters[0].index).toBe(1);
    expect(result.parameters[0].tsType).toBe("string");
    expect(result.parameters[0].requiresCast).toBe(false);
  });
});

// ── Schema cache ─────────────────────────────────────────────────────────────

describe("schema cache", () => {
  it("second identical call returns consistent results", async () => {
    const r1 = await analyze("SELECT id, name FROM products", conn);
    const r2 = await analyze("SELECT id, name FROM products", conn);

    expect(r2.columns).toHaveLength(r1.columns.length);
    expect(r2.columns[0].name).toBe(r1.columns[0].name);
    expect(r2.columns[0].isNullable).toBe(r1.columns[0].isNullable);
  });
});
