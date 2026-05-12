import type { ConnectionInfo } from "../types.js";

/** Public alias for the connection options accepted by the rule. */
export type AnyqlConnectionOptions = ConnectionInfo;

export interface ValidSqlRuleOptions {
  /** Inline connection object (highest priority). */
  connection?: AnyqlConnectionOptions;
  /**
   * Name of the environment variable containing the database connection URL.
   * Defaults to "ANYQL_CONNECTION".
   * The value must be a connection URL, e.g.:
   *   postgresql://user:password@host:5432/database
   *   mysql://user:password@host:3306/database
   * Used when reading from process.env, envFile, or autoLoadDotEnv.
   * To use the common DATABASE_URL convention, set this to "DATABASE_URL".
   */
  connectionEnvVar?: string;
  /**
   * Path to a .env file (relative to CWD or absolute) to load connection info from.
   * Takes priority over autoLoadDotEnv and process.env.
   */
  envFile?: string;
  /**
   * When true, automatically load a ".env" file from CWD if it exists.
   * Defaults to false.
   */
  autoLoadDotEnv?: boolean;
  tags?: string[];
  functions?: [string, string][];
  timeout?: number;
  requireCastMode?: "strict" | "off";
  requireCastIgnoreDbTypes?: string[];
  requireCastIgnoreTsTypes?: string[];
  requireCastOnlyDbTypes?: string[];
  resultIgnoreInferredTsTypes?: string[];
}

export interface RequiresCastFilterOptions {
  mode: "strict" | "off";
  ignoreDbTypes: Set<string>;
  ignoreTsTypes: Set<string>;
  onlyDbTypes?: Set<string>;
}

export const DEFAULT_TAGS = new Set(["sql", "SQL"]);
export const DEFAULT_FUNCTIONS: [string, string][] = [
  ["db", "query"],
  ["pool", "query"],
  ["pool", "execute"],
  ["connection", "query"],
  ["client", "query"],
];

export function createRequireCastFilter(
  opts: ValidSqlRuleOptions,
): RequiresCastFilterOptions {
  const mode = opts.requireCastMode ?? "strict";
  return {
    mode,
    ignoreDbTypes: new Set(
      (opts.requireCastIgnoreDbTypes ?? []).map((t) => t.toLowerCase()),
    ),
    ignoreTsTypes: new Set(
      (opts.requireCastIgnoreTsTypes ?? []).map((t) => t.toLowerCase()),
    ),
    onlyDbTypes: opts.requireCastOnlyDbTypes
      ? new Set(opts.requireCastOnlyDbTypes.map((t) => t.toLowerCase()))
      : undefined,
  };
}

function normalizeTypeLabel(typeLabel: string): string {
  return typeLabel
    .replace(/\s*\/\*.*?\*\//g, "")
    .trim()
    .toLowerCase();
}

export function shouldReportRequiresCast(
  param: { requiresCast: boolean; dbTypeName: string; tsType: string },
  opts: RequiresCastFilterOptions,
): boolean {
  if (opts.mode === "off") return false;
  if (!param.requiresCast) return false;

  const dbType = param.dbTypeName.toLowerCase();
  const tsType = normalizeTypeLabel(param.tsType);

  if (opts.onlyDbTypes && !opts.onlyDbTypes.has(dbType)) return false;
  if (opts.ignoreDbTypes.has(dbType)) return false;
  if (opts.ignoreTsTypes.has(tsType)) return false;

  return true;
}

export const RULE_SCHEMA = [
  {
    type: "object",
    properties: {
      connection: {
        type: "object",
        properties: {
          dialect: { type: "string", enum: ["postgresql", "mysql"] },
          host: { type: "string" },
          port: { type: "number" },
          user: { type: "string" },
          password: { type: "string" },
          database: { type: "string" },
        },
        required: ["dialect", "host", "port", "user", "password", "database"],
        additionalProperties: false,
      },
      connectionEnvVar: { type: "string" },
      envFile: { type: "string" },
      autoLoadDotEnv: { type: "boolean" },
      tags: { type: "array", items: { type: "string" } },
      functions: {
        type: "array",
        items: {
          type: "array",
          items: { type: "string" },
          minItems: 2,
          maxItems: 2,
        },
      },
      timeout: { type: "number", minimum: 1 },
      requireCastMode: { type: "string", enum: ["strict", "off"] },
      requireCastIgnoreDbTypes: {
        type: "array",
        items: { type: "string" },
      },
      requireCastIgnoreTsTypes: {
        type: "array",
        items: { type: "string" },
      },
      requireCastOnlyDbTypes: {
        type: "array",
        items: { type: "string" },
      },
      resultIgnoreInferredTsTypes: {
        type: "array",
        items: { type: "string" },
      },
    },
    required: [],
    additionalProperties: false,
  },
] as const;
