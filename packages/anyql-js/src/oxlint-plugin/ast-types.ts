import type { BaseNode, TaggedTemplateExpression } from "estree";

// OXC adds typeArguments to TaggedTemplateExpression for sql<T>`...` syntax.
export type TaggedTemplateExpressionNode = TaggedTemplateExpression & {
  typeArguments?: TSTypeParameterInstantiationNode;
};

export interface TSTypeAnnotationNode extends BaseNode {
  type: "TSTypeAnnotation";
  typeAnnotation: BaseNode;
}

export interface TSPropertySignatureNode extends BaseNode {
  type: "TSPropertySignature";
  key: BaseNode;
  typeAnnotation?: TSTypeAnnotationNode;
  optional?: boolean;
}

export interface TSTypeLiteralNode extends BaseNode {
  type: "TSTypeLiteral";
  members: BaseNode[];
}

export interface TSTypeParameterInstantiationNode extends BaseNode {
  type: "TSTypeParameterInstantiation";
  params: BaseNode[];
}

export interface TSUnionTypeNode extends BaseNode {
  type: "TSUnionType";
  types: BaseNode[];
}

export interface TSTypeReferenceNode extends BaseNode {
  type: "TSTypeReference";
  typeName: BaseNode;
  typeParameters?: TSTypeParameterInstantiationNode;
  typeArguments?: TSTypeParameterInstantiationNode;
}

export interface TSArrayTypeNode extends BaseNode {
  type: "TSArrayType";
  elementType: BaseNode;
}

export interface TSIntersectionTypeNode extends BaseNode {
  type: "TSIntersectionType";
  types: BaseNode[];
}

export interface TSTypeAliasDeclarationNode extends BaseNode {
  type: "TSTypeAliasDeclaration";
  id: BaseNode;
  typeAnnotation: BaseNode;
}

export interface TSInterfaceBodyNode extends BaseNode {
  type: "TSInterfaceBody";
  body: BaseNode[];
}

export interface TSInterfaceDeclarationNode extends BaseNode {
  type: "TSInterfaceDeclaration";
  id: BaseNode;
  body: TSInterfaceBodyNode;
}

export interface TSParserServices {
  program: {
    getTypeChecker(): {
      getTypeAtLocation(node: unknown): unknown;
      typeToString(type: unknown): string;
      getSymbolAtLocation(node: unknown):
        | {
            valueDeclaration?: unknown;
            declarations?: unknown[];
          }
        | undefined;
      getTypeOfSymbolAtLocation(symbol: unknown, node: unknown): unknown;
    };
  };
  esTreeNodeToTSNodeMap: { get(node: BaseNode): unknown };
}
