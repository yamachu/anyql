/**
 * Vitest global setup: starts a PostgreSQL container and initializes the test schema.
 * Connection info is passed to test workers via environment variables.
 */

import {
  PostgreSqlContainer,
  StartedPostgreSqlContainer,
} from "@testcontainers/postgresql";

let container: StartedPostgreSqlContainer;

export async function setup(): Promise<void> {
  container = await new PostgreSqlContainer("postgres:16")
    .withDatabase("testdb")
    .withUsername("testuser")
    .withPassword("testpass")
    .start();

  const ddl = `
    CREATE TABLE products (
      id          serial       PRIMARY KEY,
      name        text         NOT NULL,
      price       numeric      NOT NULL,
      tag         text,
      category_id int
    );
    CREATE TABLE categories (
      id    serial PRIMARY KEY,
      label text   NOT NULL
    );
    ALTER TABLE products
      ADD CONSTRAINT fk_category
      FOREIGN KEY (category_id) REFERENCES categories(id);
  `;

  const result = await container.exec([
    "psql",
    "-v",
    "ON_ERROR_STOP=1",
    "-U",
    container.getUsername(),
    "-d",
    container.getDatabase(),
    "-c",
    ddl,
  ]);
  if (result.exitCode !== 0) {
    throw new Error(
      `Schema init failed (exit ${result.exitCode}): ${result.output}`,
    );
  }

  // Propagate connection info to test workers via environment variables
  process.env.TEST_PG_HOST = container.getHost();
  process.env.TEST_PG_PORT = String(container.getPort());
  process.env.TEST_PG_USER = container.getUsername();
  process.env.TEST_PG_PASS = container.getPassword();
  process.env.TEST_PG_DB = container.getDatabase();
}

export async function teardown(): Promise<void> {
  await container.stop();
}
