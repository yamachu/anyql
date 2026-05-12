import type {
  BaseNode,
  Identifier as IdentifierNode,
  Literal as LiteralNode,
  MemberExpression as MemberExpressionNode,
  TemplateLiteral as TemplateLiteralNode,
} from "estree";

export function buildSqlFromTemplate(
  quasi: TemplateLiteralNode,
  dialect: "postgresql" | "mysql",
): string | null {
  let sql = "";
  for (let i = 0; i < quasi.quasis.length; i++) {
    const cooked = quasi.quasis[i].value.cooked;
    if (cooked === null) return null;
    sql += cooked;
    if (i < quasi.expressions.length) {
      sql += dialect === "mysql" ? "?" : `$${i + 1}`;
    }
  }
  return sql.trim() || null;
}

export function extractStaticString(node: BaseNode): string | null {
  if (node.type === "Literal") {
    const lit = node as LiteralNode;
    return typeof lit.value === "string" ? lit.value.trim() || null : null;
  }
  if (node.type === "TemplateLiteral") {
    const tpl = node as TemplateLiteralNode;
    if (tpl.expressions.length !== 0) return null;
    const cooked = tpl.quasis[0]?.value.cooked ?? null;
    return cooked?.trim() || null;
  }
  return null;
}

export function getTagName(tag: BaseNode): string | null {
  if (tag.type === "Identifier") return (tag as IdentifierNode).name;
  if (tag.type === "MemberExpression") {
    const mem = tag as MemberExpressionNode;
    if (!mem.computed && mem.property.type === "Identifier") {
      return (mem.property as IdentifierNode).name;
    }
  }
  return null;
}

export function matchesFunctionPattern(
  callee: BaseNode,
  patterns: [string, string][],
): boolean {
  if (callee.type !== "MemberExpression") return false;
  const mem = callee as MemberExpressionNode;
  if (mem.computed) return false;
  if (mem.object.type !== "Identifier" || mem.property.type !== "Identifier") {
    return false;
  }
  const objName = (mem.object as IdentifierNode).name;
  const methodName = (mem.property as IdentifierNode).name;
  return patterns.some(([o, m]) => o === objName && m === methodName);
}
