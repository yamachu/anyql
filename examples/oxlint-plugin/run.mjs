#!/usr/bin/env node
/**
 * run.mjs — AnyQL oxlint plugin example
 *
 * 1. Starts a PostgreSQL container via Testcontainers
 * 2. Seeds the schema
 * 3. Writes .oxlintrc.json with the container's dynamic connection info
 * 4. Runs oxlint on src/valid.ts  → expects: no errors
 * 5. Runs oxlint on src/invalid.ts → expects: errors reported
 * 6. Prints summary and cleans up
 */

import { PostgreSqlContainer } from "@testcontainers/postgresql";
import { spawnSync } from "node:child_process";
import { writeFileSync, rmSync, existsSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const rcPath = resolve(__dirname, ".oxlintrc.json");

// ── 1. Start container ─────────────────────────────────────────────────────

console.log("▶ Starting PostgreSQL container…");
const container = await new PostgreSqlContainer("postgres:16")
  .withDatabase("exampledb")
  .withUsername("exampleuser")
  .withPassword("examplepass")
  .start();

console.log(
  `  Connected: postgresql://exampleuser@${container.getHost()}:${container.getMappedPort(5432)}/exampledb`,
);

try {
  // ── 2. Seed schema ──────────────────────────────────────────────────────────

  console.log("\n▶ Seeding schema…");
  await container.exec([
    "psql",
    "-U",
    "exampleuser",
    "-d",
    "exampledb",
    "-c",
    `CREATE TYPE address AS (city text, zip text);
     CREATE TABLE users (
       id         serial        PRIMARY KEY,
       name       varchar(100)  NOT NULL,
       email      varchar(255)  NOT NULL,
       scores     int[]         NOT NULL DEFAULT '{}',
       tags       text[]        NOT NULL DEFAULT '{}',
       metadata   jsonb,
       avatar     bytea,
       addr       address
     );
     CREATE TABLE posts (
       id      serial  PRIMARY KEY,
       user_id int     NOT NULL,
       title   text    NOT NULL,
       body    text
     );`,
  ]);
  console.log("  Schema ready.");

  // ── 3. Write .oxlintrc.json ─────────────────────────────────────────────────

  const config = {
    jsPlugins: ["@yamachu/anyql/oxlint-plugin"],
    rules: {
      "anyql/valid-sql": [
        "error",
        {
          connection: {
            dialect: "postgresql",
            host: container.getHost(),
            port: container.getMappedPort(5432),
            user: "exampleuser",
            password: "examplepass",
            database: "exampledb",
          },
          tags: ["sql"],
          functions: [["db", "query"]],
          timeout: 30000,
          requireCastIgnoreTsTypes: ["number"],
        },
      ],
    },
  };

  writeFileSync(rcPath, JSON.stringify(config, null, 2));
  console.log(`\n▶ Wrote ${rcPath}`);

  // ── 4. Run oxlint ────────────────────────────────────────────────────────────

  const oxlint = resolve(__dirname, "node_modules/.bin/oxlint");
  let allPassed = true;

  console.log("\n──────────────────────────────────────────");
  console.log("▶ Linting src/valid.ts  (expect: ✅ no errors)");
  console.log("──────────────────────────────────────────");
  const validResult = spawnSync(oxlint, ["--config", rcPath, "src/valid.ts"], {
    cwd: __dirname,
    encoding: "utf8",
    env: { ...process.env },
  });
  if (validResult.stdout) process.stdout.write(validResult.stdout);
  if (validResult.stderr) process.stderr.write(validResult.stderr);

  if (validResult.status === 0) {
    console.log("✅ valid.ts: no lint errors (as expected)");
  } else {
    console.error(
      "❌ valid.ts: unexpected lint errors (exit " + validResult.status + ")",
    );
    allPassed = false;
  }

  console.log("\n──────────────────────────────────────────");
  console.log("▶ Linting src/invalid.ts (expect: ❌ errors reported)");
  console.log("──────────────────────────────────────────");
  const invalidResult = spawnSync(
    oxlint,
    ["--config", rcPath, "src/invalid.ts"],
    { cwd: __dirname, encoding: "utf8", env: { ...process.env } },
  );
  if (invalidResult.stdout) process.stdout.write(invalidResult.stdout);
  if (invalidResult.stderr) process.stderr.write(invalidResult.stderr);

  if (invalidResult.status !== 0) {
    console.log("✅ invalid.ts: lint errors reported (as expected)");
  } else {
    console.error("❌ invalid.ts: no errors reported (expected errors!)");
    allPassed = false;
  }

  console.log("\n══════════════════════════════════════════");
  console.log(allPassed ? "✅ All checks passed!" : "❌ Some checks failed.");
  console.log("══════════════════════════════════════════");
} finally {
  // ── 5. Clean up ──────────────────────────────────────────────────────────────
  if (existsSync(rcPath)) rmSync(rcPath);
  console.log("\n▶ Stopping container…");
  await container.stop();
  console.log("Done.");
}
