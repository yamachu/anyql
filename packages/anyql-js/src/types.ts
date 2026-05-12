/**
 * Public types for the anyql package.
 * These match the JSON DTOs serialized by AnyQL.Wasm.WasmExports.
 */

export interface ConnectionInfo {
  dialect: "postgresql" | "mysql";
  host: string;
  port: number;
  user: string;
  password: string;
  database: string;
}

export interface ColumnInfo {
  /** Column name as it appears in the result set (alias applied). */
  name: string;
  /** Database-native type name (e.g. "int4", "uuid", "varchar"). */
  dbTypeName: string;
  /** Database-specific type code (OID for PG; column_type for MySQL). */
  typeCode: number;
  /**
   * Whether the column can be NULL.
   * null means unknown (expression columns, or the DB did not return nullability info).
   */
  isNullable: boolean | null;
  /** Suggested .NET type name (e.g. "int", "string", "Guid"). */
  dotNetType: string;
  /** Suggested TypeScript type name (e.g. "number", "string", "Date"). */
  tsType: string;
  /** OID of the source table (PostgreSQL only, 0 otherwise). */
  sourceTableOid: number;
  /** Attribute number of the source column (PostgreSQL only, 0 otherwise). */
  sourceColumnAttributeNumber: number;
}

export interface ParameterInfo {
  /** 1-based parameter index ($1, $2, ... for PG; ? position for MySQL). */
  index: number;
  /** Database-native type name inferred by the server. */
  dbTypeName: string;
  /** Database-specific type code. */
  typeCode: number;
  /** Suggested TypeScript type for this parameter. */
  tsType: string;
  /** Suggested .NET type for this parameter. */
  dotNetType: string;
  /**
   * When true, the caller should add an explicit SQL cast (e.g. $1::uuid)
   * because the server inferred a non-string-compatible type and no cast was found.
   * Always false for MySQL.
   */
  requiresCast: boolean;
}

export interface AnalyzeResult {
  columns: ColumnInfo[];
  parameters: ParameterInfo[];
}

export interface AnalyzeErrorResult {
  error: string;
}
