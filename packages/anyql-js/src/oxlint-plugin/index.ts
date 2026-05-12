/**
 * oxlint-plugin/index.ts — AnyQL oxlint/ESLint plugin entrypoint.
 */

import { validSqlRule } from "./rule.js";
import { buildSqlFromTemplate } from "./sql-helpers.js";

const plugin = {
  meta: {
    name: "anyql",
    version: "0.1.0",
  },
  rules: {
    "valid-sql": validSqlRule,
  },
};

export default plugin;

export { buildSqlFromTemplate, validSqlRule };
export type { AnyqlConnectionOptions, ValidSqlRuleOptions } from "./options.js";
