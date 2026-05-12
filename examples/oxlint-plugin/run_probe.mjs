import { PostgreSqlContainer } from "@testcontainers/postgresql";
import { analyze } from "../../packages/anyql-js/dist/index.js";

const container = await new PostgreSqlContainer("postgres:16")
  .withDatabase("exampledb")
  .withUsername("exampleuser")
  .withPassword("examplepass")
  .start();

await container.exec([
  "psql",
  "-U",
  "exampleuser",
  "-d",
  "exampledb",
  "-c",
  `CREATE TABLE users (id serial PRIMARY KEY, name varchar(100) NOT NULL, email varchar(255) NOT NULL);
   CREATE TABLE posts (id serial PRIMARY KEY, user_id int NOT NULL, title text NOT NULL, body text);`,
]);

const conn = {
  dialect: "postgresql",
  host: container.getHost(),
  port: container.getMappedPort(5432),
  user: "exampleuser",
  password: "examplepass",
  database: "exampledb",
};

const queries = [
  "SELECT COUNT(*) AS total FROM users",
  "SELECT COUNT(*) AS cnt, MAX(id) AS maxid, MIN(id) AS minid FROM users",
  "SELECT SUM(id) AS sumid, AVG(id) AS avgid FROM users",
  "SELECT MAX(name) AS maxname FROM users",
  "SELECT COALESCE(body, '') AS body FROM posts",
  "SELECT CAST(id AS text) AS idstr FROM users",
  "SELECT id::text AS idstr FROM users",
  "SELECT NOW() AS ts",
  "SELECT CURRENT_TIMESTAMP AS ts",
  "SELECT COUNT(*) AS total, MAX(id) AS maxid FROM users GROUP BY name",
];

for (let i = 0; i < queries.length; i++) {
  console.log(`\n[${i + 1}] ${queries[i]}`);
  try {
    const result = await analyze(queries[i], conn);
    for (const c of result.columns)
      console.log(
        `  ${c.name}: tsType=${c.tsType}  dbType=${c.dbTypeName}  nullable=${c.isNullable}`,
      );
  } catch (err) {
    console.log(`  ERROR: ${err.message}`);
  }
}

await container.stop();
