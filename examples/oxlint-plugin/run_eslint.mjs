#!/usr/bin/env node
/**
 * run_eslint.mjs — AnyQL ESLint plugin example (type-aware).
 *
 * 1. Starts a PostgreSQL container
 * 2. Seeds schema
 * 3. Runs ESLint on eslint-valid.ts (expect: pass)
 * 4. Runs ESLint on eslint-invalid.ts (expect: paramTypeMismatch)
 */

import { PostgreSqlContainer } from "@testcontainers/postgresql";
import { spawnSync } from "node:child_process";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));

console.log("▶ Starting PostgreSQL container for ESLint demo...");
const container = await new PostgreSqlContainer("postgres:16")
  .withDatabase("exampledb")
  .withUsername("exampleuser")
  .withPassword("examplepass")
  .start();

const connection = {
  dialect: "postgresql",
  host: container.getHost(),
  port: container.getMappedPort(5432),
  user: "exampleuser",
  password: "examplepass",
  database: "exampledb",
};

try {
  console.log("\n▶ Seeding schema...");
  const seed = await container.exec([
    "psql",
    "-U",
    "exampleuser",
    "-d",
    "exampledb",
    "-c",
    `CREATE TABLE users (
       id    serial       PRIMARY KEY,
       name  varchar(100) NOT NULL,
       email varchar(255) NOT NULL
     );`,
  ]);

  if (seed.exitCode !== 0) {
    throw new Error(`schema seed failed: ${seed.output}`);
  }

  const eslintBin = resolve(__dirname, "node_modules/.bin/eslint");
  const connectionUrl = `postgresql://${connection.user}:${connection.password}@${connection.host}:${connection.port}/${connection.database}`;
  const env = {
    ...process.env,
    ANYQL_EXAMPLE_CONNECTION: connectionUrl,
  };

  console.log("\n──────────────────────────────────────────");
  console.log("▶ ESLint src/eslint-valid.ts   (expect: ✅ no errors)");
  console.log("──────────────────────────────────────────");
  const valid = spawnSync(eslintBin, ["src/eslint-valid.ts"], {
    cwd: __dirname,
    encoding: "utf8",
    env,
  });
  if (valid.error) {
    console.error("valid.ts spawn error:", valid.error.message);
  }
  if (valid.stdout) process.stdout.write(valid.stdout);
  if (valid.stderr) process.stderr.write(valid.stderr);

  console.log("\n──────────────────────────────────────────");
  console.log("▶ ESLint src/eslint-invalid.ts (expect: ❌ paramTypeMismatch)");
  console.log("──────────────────────────────────────────");
  const invalid = spawnSync(eslintBin, ["src/eslint-invalid.ts"], {
    cwd: __dirname,
    encoding: "utf8",
    env,
  });
  if (invalid.error) {
    console.error("invalid.ts spawn error:", invalid.error.message);
  }
  if (invalid.stdout) process.stdout.write(invalid.stdout);
  if (invalid.stderr) process.stderr.write(invalid.stderr);

  const validPass = valid.status === 0;
  const invalidFail = invalid.status !== 0;

  console.log("\n══════════════════════════════════════════");
  if (validPass && invalidFail) {
    console.log("✅ ESLint demo passed.");
  } else {
    console.log("❌ ESLint demo failed.");
    console.log(`  valid.ts exit: ${valid.status}`);
    console.log(`  invalid.ts exit: ${invalid.status}`);
    process.exitCode = 1;
  }
  console.log("══════════════════════════════════════════");
} finally {
  console.log("\n▶ Stopping container...");
  await container.stop();
  console.log("Done.");
}
