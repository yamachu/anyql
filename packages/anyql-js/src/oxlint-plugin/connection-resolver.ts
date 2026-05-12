import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

import type { ConnectionInfo } from "../types.js";
import type { ValidSqlRuleOptions } from "./options.js";

const DEFAULT_CONNECTION_ENV_VAR = "ANYQL_CONNECTION";

/**
 * Minimal .env parser.
 * Supports KEY=VALUE lines, # comments, and single/double-quoted values.
 * Does NOT support multi-line values or variable expansion.
 */
function parseDotEnv(content: string): Record<string, string> {
  const result: Record<string, string> = {};
  for (const line of content.split(/\r?\n/)) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith("#")) continue;
    const eqIdx = trimmed.indexOf("=");
    if (eqIdx === -1) continue;
    const key = trimmed.slice(0, eqIdx).trim();
    if (!key) continue;
    let value = trimmed.slice(eqIdx + 1).trim();
    if (
      (value.startsWith('"') && value.endsWith('"')) ||
      (value.startsWith("'") && value.endsWith("'"))
    ) {
      value = value.slice(1, -1);
    }
    result[key] = value;
  }
  return result;
}

/**
 * Parses a connection string URL into a ConnectionInfo object.
 *
 * Supported schemes: postgresql://, postgres://, mysql://
 *
 * Example: postgresql://user:password@host:5432/database
 */
function parseConnectionUrl(value: string, source: string): ConnectionInfo {
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    throw new Error(
      `AnyQL: Failed to parse connection URL from ${source}. ` +
        `Expected format: postgresql://user:password@host:5432/database`,
    );
  }

  const proto = url.protocol.replace(/:$/, "");
  let dialect: "postgresql" | "mysql";
  if (proto === "postgresql" || proto === "postgres") {
    dialect = "postgresql";
  } else if (proto === "mysql") {
    dialect = "mysql";
  } else {
    throw new Error(
      `AnyQL: Unsupported connection URL scheme "${proto}" from ${source}. ` +
        `Use "postgresql://" or "mysql://".`,
    );
  }

  const host = url.hostname;
  const defaultPort = dialect === "postgresql" ? 5432 : 3306;
  const port = url.port ? parseInt(url.port, 10) : defaultPort;
  const user = decodeURIComponent(url.username);
  const password = decodeURIComponent(url.password);
  const database = url.pathname.replace(/^\//, "");

  if (!host) {
    throw new Error(`AnyQL: Missing host in connection URL from ${source}.`);
  }
  if (!user) {
    throw new Error(
      `AnyQL: Missing username in connection URL from ${source}.`,
    );
  }
  if (!database) {
    throw new Error(
      `AnyQL: Missing database name in connection URL from ${source}.`,
    );
  }

  return { dialect, host, port, user, password, database };
}

function resolveFromEnvRecord(
  env: Record<string, string | undefined>,
  envVarName: string,
  source: string,
): ConnectionInfo {
  const url = env[envVarName];
  if (!url) {
    throw new Error(`AnyQL: "${envVarName}" is not set in ${source}.`);
  }
  return parseConnectionUrl(url, `${source} (${envVarName})`);
}

/**
 * Resolves the ConnectionInfo from one of the supported sources, in priority order:
 *
 * 1. Inline `connection` option (highest priority)
 * 2. `envFile` — load specified .env file and read the connection URL env var
 * 3. `autoLoadDotEnv: true` — auto-load `.env` from CWD if it exists
 * 4. `process.env` — read the connection URL env var from the current environment
 *
 * The env var is expected to contain a connection URL such as:
 *   postgresql://user:password@host:5432/database
 *
 * The env var name defaults to "ANYQL_CONNECTION" and can be overridden
 * with the `connectionEnvVar` option (e.g. "DATABASE_URL").
 */
export function resolveConnection(opts: ValidSqlRuleOptions): ConnectionInfo {
  // Priority 1: inline connection
  if (opts.connection) {
    return opts.connection;
  }

  const envVarName = opts.connectionEnvVar ?? DEFAULT_CONNECTION_ENV_VAR;

  // Priority 2: explicit envFile
  if (opts.envFile) {
    const envPath = resolve(process.cwd(), opts.envFile);
    let content: string;
    try {
      content = readFileSync(envPath, "utf-8");
    } catch {
      throw new Error(
        `AnyQL: Could not read envFile "${opts.envFile}" (resolved to "${envPath}").`,
      );
    }
    return resolveFromEnvRecord(
      parseDotEnv(content),
      envVarName,
      `envFile "${opts.envFile}"`,
    );
  }

  // Priority 3: auto-load CWD/.env
  if (opts.autoLoadDotEnv) {
    const dotEnvPath = resolve(process.cwd(), ".env");
    if (existsSync(dotEnvPath)) {
      const content = readFileSync(dotEnvPath, "utf-8");
      return resolveFromEnvRecord(parseDotEnv(content), envVarName, ".env");
    }
  }

  // Priority 4: process.env
  const url = process.env[envVarName];
  if (url) {
    return parseConnectionUrl(url, `environment variable "${envVarName}"`);
  }

  throw new Error(
    `AnyQL: No connection info found. ` +
      `Provide an inline "connection" object, set the "${envVarName}" environment variable ` +
      `(e.g. postgresql://user:password@host:5432/db), ` +
      `or configure "envFile" / "autoLoadDotEnv" in the rule options.`,
  );
}
