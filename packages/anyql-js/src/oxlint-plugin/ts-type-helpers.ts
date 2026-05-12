import type { BaseNode, Identifier as IdentifierNode } from "estree";

import type {
  TSArrayTypeNode,
  TSInterfaceBodyNode,
  TSParserServices,
  TSIntersectionTypeNode,
  TSPropertySignatureNode,
  TSTypeLiteralNode,
  TSTypeParameterInstantiationNode,
  TSTypeReferenceNode,
  TSUnionTypeNode,
} from "./ast-types.js";

function extractTsTypeName(typeNode: BaseNode): string {
  switch (typeNode.type) {
    case "TSNumberKeyword":
      return "number";
    case "TSStringKeyword":
      return "string";
    case "TSBooleanKeyword":
      return "boolean";
    case "TSBigIntKeyword":
      return "bigint";
    case "TSUnknownKeyword":
      return "unknown";
    case "TSVoidKeyword":
      return "void";
    case "TSArrayType": {
      const arr = typeNode as TSArrayTypeNode;
      const elem = extractTsTypeName(arr.elementType);
      return `${elem}[]`;
    }
    case "TSTypeReference": {
      const ref = typeNode as TSTypeReferenceNode;
      if (ref.typeName.type !== "Identifier") return "unknown";
      const refName = (ref.typeName as IdentifierNode).name;
      const typeParams = ref.typeParameters ?? ref.typeArguments;
      if (typeParams && typeParams.params.length > 0) {
        const paramNames = typeParams.params.map(extractTsTypeName);
        return `${refName}<${paramNames.join(", ")}>`;
      }
      return refName;
    }
    case "TSUnionType": {
      const union = typeNode as TSUnionTypeNode;
      const nonNullTypes = union.types.filter(
        (t) => t.type !== "TSNullKeyword" && t.type !== "TSUndefinedKeyword",
      );
      if (nonNullTypes.length === 1) return extractTsTypeName(nonNullTypes[0]);
      return "unknown";
    }
    default:
      return "unknown";
  }
}

function collectPropsFromMembers(
  members: BaseNode[],
  out: Map<string, string>,
): void {
  for (const member of members) {
    if (member.type !== "TSPropertySignature") continue;
    const prop = member as TSPropertySignatureNode;
    if (prop.key.type !== "Identifier") continue;
    const keyName = (prop.key as IdentifierNode).name;
    if (!prop.typeAnnotation) continue;
    out.set(keyName, extractTsTypeName(prop.typeAnnotation.typeAnnotation));
  }
}

function collectPropsFromTypeNode(
  typeNode: BaseNode,
  out: Map<string, string>,
  typeAliasMap: Map<string, BaseNode>,
  visited = new Set<string>(),
): void {
  if (typeNode.type === "TSTypeLiteral") {
    collectPropsFromMembers((typeNode as TSTypeLiteralNode).members, out);
  } else if (typeNode.type === "TSIntersectionType") {
    for (const t of (typeNode as TSIntersectionTypeNode).types) {
      collectPropsFromTypeNode(t, out, typeAliasMap, visited);
    }
  } else if (typeNode.type === "TSInterfaceBody") {
    collectPropsFromMembers((typeNode as TSInterfaceBodyNode).body, out);
  } else if (typeNode.type === "TSTypeReference") {
    const ref = typeNode as TSTypeReferenceNode;
    if (ref.typeName.type !== "Identifier") return;
    const name = (ref.typeName as IdentifierNode).name;
    if (visited.has(name)) return;
    const resolved = typeAliasMap.get(name);
    if (!resolved) return;
    visited.add(name);
    collectPropsFromTypeNode(resolved, out, typeAliasMap, visited);
    visited.delete(name);
  }
}

export function extractGenericTypeProps(
  typeParameters: TSTypeParameterInstantiationNode,
  typeAliasMap: Map<string, BaseNode>,
): Map<string, string> | null {
  let firstParam = typeParameters.params[0];
  if (!firstParam) return null;
  // Unwrap array type: Type[] -> Type
  if (firstParam.type === "TSArrayType") {
    firstParam = (firstParam as TSArrayTypeNode).elementType;
  }
  const props = new Map<string, string>();
  collectPropsFromTypeNode(firstParam, props, typeAliasMap);
  return props.size > 0 ? props : null;
}

export function normalizeTsType(tsType: string): string {
  return tsType.replace(/\s*\/\*.*?\*\//g, "").trim();
}

export function getTSParserServices(
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  context: Record<string, any>,
): TSParserServices | null {
  const ps: unknown =
    context["parserServices"] ?? context["sourceCode"]?.["parserServices"];
  if (!ps || typeof ps !== "object") return null;
  const p = ps as Record<string, unknown>;
  if (
    typeof (p["program"] as Record<string, unknown>)?.["getTypeChecker"] !==
    "function"
  ) {
    return null;
  }
  if (!p["esTreeNodeToTSNodeMap"]) return null;
  return ps as TSParserServices;
}

function normalizeTsTypeFromString(typeStr: string): string | null {
  const parts = typeStr
    .split("|")
    .map((s) => s.trim())
    .filter((s) => s !== "null" && s !== "undefined");
  if (parts.length !== 1) return null;

  const t = parts[0];
  if (t === "string") return "string";
  if (t === "number") return "number";
  if (t === "boolean") return "boolean";
  if (t === "bigint") return "bigint";
  if (t === "Date") return "Date";
  if (t.startsWith('"') || t.startsWith("'") || t.startsWith("`")) {
    return "string";
  }
  if (/^-?\d/.test(t)) return "number";
  if (t === "true" || t === "false") return "boolean";

  return null;
}

export function resolveTsTypeOfExpr(
  node: BaseNode,
  ps: TSParserServices,
): string | null {
  const tsNode = ps.esTreeNodeToTSNodeMap.get(node);
  if (!tsNode) return null;

  const checker = ps.program.getTypeChecker();
  const directType = checker.getTypeAtLocation(tsNode);
  const direct = normalizeTsTypeFromString(checker.typeToString(directType));

  if (node.type === "Identifier") {
    const symbol = checker.getSymbolAtLocation(tsNode);
    const decl = symbol?.valueDeclaration ?? symbol?.declarations?.[0];
    if (symbol && decl) {
      const symbolType = checker.getTypeOfSymbolAtLocation(symbol, decl);
      const declared = normalizeTsTypeFromString(
        checker.typeToString(symbolType),
      );
      if (declared !== null) return declared;
    }
  }

  return direct;
}
