import { dirname } from "node:path";
import { fileURLToPath } from "node:url";

import tsParser from "@typescript-eslint/parser";
import anyqlPlugin from "@yamachu/anyql/oxlint-plugin";

const __dirname = dirname(fileURLToPath(import.meta.url));

// Connection info is loaded by the plugin from the ANYQL_EXAMPLE_CONNECTION
// environment variable as a connection URL, e.g.:
//   ANYQL_EXAMPLE_CONNECTION=postgresql://user:password@localhost:5432/mydb
// No manual parsing needed here.
// To use a different source, you can instead pass:
//   connection: { dialect: "postgresql", host: "...", ... }  — inline object
//   connectionEnvVar: "DATABASE_URL"                         — use standard DATABASE_URL
//   envFile: ".env.local"                                    — explicit .env file
//   autoLoadDotEnv: true                                     — auto-load .env from CWD

export default [
  {
    files: ["src/eslint-*.ts"],
    languageOptions: {
      parser: tsParser,
      parserOptions: {
        project: true,
        tsconfigRootDir: __dirname,
        sourceType: "module",
        ecmaVersion: "latest",
      },
    },
    plugins: {
      anyql: anyqlPlugin,
    },
    rules: {
      "anyql/valid-sql": [
        "error",
        {
          connectionEnvVar: "ANYQL_EXAMPLE_CONNECTION",
          tags: ["sql"],
          functions: [["db", "query"]],
          timeout: 30000,
          requireCastIgnoreTsTypes: ["number"],
        },
      ],
    },
  },
];
