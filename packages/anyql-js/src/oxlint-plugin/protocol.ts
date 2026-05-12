/**
 * Shared types for the anyql oxlint-plugin worker protocol.
 *
 * Both the plugin rule (index.ts) and the CLI worker (worker.ts) use these
 * types to serialize/deserialize the stdin/stdout JSON exchange.
 *
 * Protocol:
 *   stdin:  JSON array of AnalysisRequest
 *   stdout: JSON array of AnalysisResponse
 */

import type { ConnectionInfo, AnalyzeResult } from "../types.js";

export interface AnalysisRequest {
  sql: string;
  conn: ConnectionInfo;
}

export type AnalysisResponse =
  | {
      ok: true;
      result: AnalyzeResult;
    }
  | {
      ok: false;
      error: string;
    };
