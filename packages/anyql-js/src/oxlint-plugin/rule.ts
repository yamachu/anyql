import type {
  BaseNode,
  CallExpression as CallExpressionNode,
  Identifier as IdentifierNode,
  TemplateLiteral as TemplateLiteralNode,
} from "estree";

import type {
  TaggedTemplateExpressionNode,
  TSTypeAliasDeclarationNode,
  TSInterfaceDeclarationNode,
} from "./ast-types.js";
import {
  RULE_SCHEMA,
  DEFAULT_FUNCTIONS,
  DEFAULT_TAGS,
  createRequireCastFilter,
  shouldReportRequiresCast,
} from "./options.js";
import {
  buildSqlFromTemplate,
  extractStaticString,
  getTagName,
  matchesFunctionPattern,
} from "./sql-helpers.js";
import { analyzeAllSync } from "./worker-client.js";
import {
  extractGenericTypeProps,
  getTSParserServices,
  normalizeTsType,
  resolveTsTypeOfExpr,
} from "./ts-type-helpers.js";
import type { ConnectionInfo } from "../types.js";
import type { AnalysisResponse } from "./protocol.js";
import type { ValidSqlRuleOptions } from "./options.js";
import { resolveConnection } from "./connection-resolver.js";

export const validSqlRule = {
  meta: {
    type: "problem" as const,
    docs: {
      description:
        "Validate SQL queries against a live database using AnyQL. " +
        "Catches syntax errors, missing columns, and type mismatches at lint time.",
      url: "https://github.com/yamachu/anyql",
    },
    messages: {
      invalidSql: "Invalid SQL: {{error}}",
      analyzerError:
        "AnyQL analyzer failed: {{error}}. Check connection settings.",
      typeMismatch:
        "Column '{{column}}': inferred TypeScript type is '{{actual}}' but '{{expected}}' was declared",
      missingResultColumn:
        "Column '{{column}}' declared in generic type does not exist in the query result",
      requiresCast:
        "Parameter ${{index}} requires an explicit SQL cast to '{{dbType}}' (e.g. ${{index}}::{{dbType}} for PostgreSQL)",
      paramTypeMismatch:
        "Parameter ${{index}}: TypeScript type '{{actual}}' is not assignable to the expected database type '{{expected}}'",
    },
    schema: RULE_SCHEMA,
  },

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  create(context: any) {
    const opts = (context.options[0] ?? {}) as ValidSqlRuleOptions;

    let conn: ConnectionInfo;
    let connError: string | null = null;
    try {
      conn = resolveConnection(opts);
    } catch (err) {
      conn = null!;
      connError = (err as Error).message;
    }

    const tags = opts.tags ? new Set(opts.tags) : DEFAULT_TAGS;
    const functions = opts.functions ?? DEFAULT_FUNCTIONS;
    const timeout = opts.timeout ?? 30_000;
    const requireCastFilter = createRequireCastFilter(opts);
    const resultIgnoreInferredTsTypes = new Set(
      (opts.resultIgnoreInferredTsTypes ?? []).map((t) => t.toLowerCase()),
    );

    type PendingQuery = {
      sql: string;
      node: BaseNode;
      expectedTypes?: Map<string, string>;
      expressions?: BaseNode[];
    };
    const pending: PendingQuery[] = [];
    const typeAliasMap = new Map<string, BaseNode>();

    return {
      TSTypeAliasDeclaration(node: BaseNode) {
        const decl = node as TSTypeAliasDeclarationNode;
        if (decl.id.type !== "Identifier") return;
        typeAliasMap.set((decl.id as IdentifierNode).name, decl.typeAnnotation);
      },

      TSInterfaceDeclaration(node: BaseNode) {
        const decl = node as TSInterfaceDeclarationNode;
        if (decl.id.type !== "Identifier") return;
        typeAliasMap.set((decl.id as IdentifierNode).name, decl.body);
      },

      TaggedTemplateExpression(node: TaggedTemplateExpressionNode) {
        const tagName = getTagName(node.tag);
        if (!tagName || !tags.has(tagName)) return;
        if (connError) {
          pending.push({ sql: "", node });
          return;
        }
        const sql = buildSqlFromTemplate(node.quasi, conn.dialect);
        if (!sql) return;

        const expectedTypes = node.typeArguments
          ? (extractGenericTypeProps(node.typeArguments, typeAliasMap) ??
            undefined)
          : undefined;
        const expressions =
          node.quasi.expressions.length > 0
            ? (node.quasi.expressions as BaseNode[])
            : undefined;

        pending.push({ sql, node, expectedTypes, expressions });
      },

      CallExpression(node: CallExpressionNode) {
        if (!matchesFunctionPattern(node.callee, functions)) return;
        const firstArg = node.arguments[0];
        if (!firstArg) return;
        if (connError) {
          pending.push({ sql: "", node });
          return;
        }

        if (firstArg.type === "TemplateLiteral") {
          const sql = buildSqlFromTemplate(
            firstArg as TemplateLiteralNode,
            conn.dialect,
          );
          if (sql) pending.push({ sql, node });
          return;
        }

        const sql = extractStaticString(firstArg);
        if (sql) pending.push({ sql, node });
      },

      "Program:exit"(_programNode: BaseNode) {
        if (pending.length === 0) return;

        if (connError) {
          context.report({
            node: pending[0].node,
            messageId: "analyzerError",
            data: { error: connError },
          });
          return;
        }

        const tsServices = getTSParserServices(
          context as unknown as Record<string, unknown>,
        );

        let results: AnalysisResponse[];
        try {
          results = analyzeAllSync(
            pending.map((p) => ({ sql: p.sql, conn })),
            timeout,
          );
        } catch (err) {
          context.report({
            node: pending[0].node,
            messageId: "analyzerError",
            data: { error: (err as Error).message },
          });
          return;
        }

        for (let i = 0; i < pending.length; i++) {
          const res = results[i];
          const { node, expectedTypes, expressions } = pending[i];

          if (res && !res.ok) {
            context.report({
              node,
              messageId: "invalidSql",
              data: { error: res.error ?? "Unknown error" },
            });
            continue;
          }

          if (res?.ok && res.result && expressions) {
            for (const param of res.result.parameters) {
              const exprNode = expressions[param.index - 1] ?? node;

              if (shouldReportRequiresCast(param, requireCastFilter)) {
                context.report({
                  node: exprNode,
                  messageId: "requiresCast",
                  data: {
                    index: String(param.index),
                    dbType: param.dbTypeName,
                  },
                });
                continue;
              }

              if (tsServices) {
                const actualTsType = resolveTsTypeOfExpr(exprNode, tsServices);
                if (actualTsType !== null) {
                  const expectedTsType = normalizeTsType(param.tsType);
                  if (actualTsType !== expectedTsType) {
                    context.report({
                      node: exprNode,
                      messageId: "paramTypeMismatch",
                      data: {
                        index: String(param.index),
                        actual: actualTsType,
                        expected: expectedTsType,
                      },
                    });
                  }
                }
              }
            }
          }

          if (res?.ok && res.result && expectedTypes) {
            const columnMapLower = new Map(
              res.result.columns.map((c) => [
                c.name.toLowerCase(),
                normalizeTsType(c.tsType),
              ]),
            );

            for (const [propName, expectedTsType] of expectedTypes) {
              const actualTsType = columnMapLower.get(propName.toLowerCase());
              if (actualTsType === undefined) {
                context.report({
                  node,
                  messageId: "missingResultColumn",
                  data: { column: propName },
                });
              } else if (
                expectedTsType !== "unknown" &&
                actualTsType !== expectedTsType &&
                !resultIgnoreInferredTsTypes.has(actualTsType.toLowerCase())
              ) {
                context.report({
                  node,
                  messageId: "typeMismatch",
                  data: {
                    column: propName,
                    actual: actualTsType,
                    expected: expectedTsType,
                  },
                });
              }
            }
          }
        }
      },
    };
  },
};
