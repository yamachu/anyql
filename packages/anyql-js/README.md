# @yamachu/anyql

データベースに対してリアルタイムで SQL クエリを検証し、カラム・パラメータの型メタデータを返す型安全なライブラリです。

oxlint / ESLint プラグインとして使うことで、テンプレートリテラル SQL やクエリビルダの SQL 文字列に対して、静的解析時に型チェックを行えます。

```typescript
const users = sql<{ id: number; name: string }>`
  SELECT id, name FROM users WHERE age > $1
`;
```

上記のコードは以下をリント時に検証します
- テーブル・カラムが実際に存在するか
- パラメータの型が DB スキーマと合致しているか
- 返却カラムが宣言型と一致しているか

## Requirements

- Node.js: ≥ 22
- データベース: PostgreSQL または MySQL が実行中（リモート可）


## Installation

```bash
npm install --save-dev @yamachu/anyql
# または
pnpm add -D @yamachu/anyql
# または
yarn add --dev @yamachu/anyql
```

## Usage

### 1. oxlint プラグインとして

#### セットアップ（.oxlintrc.json）

```json
{
  "jsPlugins": ["@yamachu/anyql/oxlint-plugin"],
  "rules": {
    "anyql/valid-sql": [
      "error",
      {
        "connectionEnvVar": "DATABASE_URL",
        "tags": ["sql", "SQL"],
        "functions": [["db", "query"]],
        "timeout": 30000
      }
    ]
  }
}
```

または `oxlint.config.ts` など TypeScript 形式でも可

```typescript
export default {
  jsPlugins: ["@yamachu/anyql/oxlint-plugin"],
  rules: {
    "anyql/valid-sql": [
      "error",
      {
        connectionEnvVar: "DATABASE_URL",
        tags: ["sql", "SQL"],
        functions: [["db", "query"]],
        timeout: 30000,
      },
    ],
  },
};
```

#### 接続設定

接続先は以下の優先順位で解決されます

| 優先度 | 方法 | 説明 |
|--------|------|------|
| 1 | `connection` | インラインオブジェクトで直接指定 |
| 2 | `envFile` | `.env` ファイルから URL を読み込み |
| 3 | `autoLoadDotEnv: true` | CWD の `.env` を自動読み込み |
| 4 | 環境変数 | `connectionEnvVar`（デフォルト: `ANYQL_CONNECTION`）で指定 |

##### 接続 URL 形式

```bash
# PostgreSQL
ANYQL_CONNECTION=postgresql://user:password@localhost:5432/mydb

# MySQL
ANYQL_CONNECTION=mysql://user:password@localhost:3306/mydb
```

##### インラインオブジェクト指定

```javascript
{
  "anyql/valid-sql": [
    "error",
    {
      connection: {
        dialect: "postgresql",  // または "mysql"
        host: "localhost",
        port: 5432,
        user: "postgres",
        password: "secret",
        database: "mydb"
      }
    }
  ]
}
```

#### タグ関数の例

```typescript
// リント対象sql タグ、$1 など PostgreSQL パラメータプレースホルダ
const query = sql<{ id: number; name: string }>`
  SELECT id, name FROM users WHERE id = $1
`;
```

### 2. ESLint プラグインとして

ESLint（oxlint が不使用の場合）でも同じプラグインが使えます。

```javascript
// eslint.config.mjs
import tsParser from "@typescript-eslint/parser";
import anyqlPlugin from "@yamachu/anyql/oxlint-plugin";

export default [
  {
    files: ["src/**/*.ts"],
    languageOptions: {
      parser: tsParser,
      parserOptions: {
        project: true,
        sourceType: "module",
      },
    },
    plugins: {
      anyql: anyqlPlugin,
    },
    rules: {
      "anyql/valid-sql": [
        "error",
        {
          connectionEnvVar: "DATABASE_URL",
          tags: ["sql"],
          functions: [["db", "query"]],
        },
      ],
    },
  },
];
```

### 3. コードでの直接使用

rxlint/ESLint プラグイン以外にも、直接 API を呼び出せます。

```typescript
import { analyze } from "@yamachu/anyql";

const result = await analyze(
  "SELECT id, name FROM users WHERE id = $1",
  {
    dialect: "postgresql",
    host: "localhost",
    port: 5432,
    user: "postgres",
    password: "secret",
    database: "mydb",
  }
);

console.log(result.columns);    // [{ name: 'id', tsType: 'number', ... }, ...]
console.log(result.parameters); // [{ index: 1, dbTypeName: 'uuid', ... }]
```

## 設定オプション

### `anyql/valid-sql` ルールオプション

```typescript
interface ValidSqlRuleOptions {
  // ── 接続設定 ──
  connection?: {
    dialect: "postgresql" | "mysql";
    host: string;
    port: number;
    user: string;
    password: string;
    database: string;
  };

  connectionEnvVar?: string;  // デフォルト: "ANYQL_CONNECTION"
  envFile?: string;           // .env ファイルパス
  autoLoadDotEnv?: boolean;   // デフォルト: false

  // ── 検証スコープ ──
  tags?: string[];            // SQL タグ関数の名前
  functions?: [string, string][]; // [object, method] パターン
  timeout?: number;           // クエリタイムアウト（ms、デフォルト: 30000）

  // ── requiresCast フィルター ──
  requireCastMode?: "strict" | "off"; // デフォルト: "strict"
  requireCastIgnoreDbTypes?: string[]; // 除外する DB 型
  requireCastIgnoreTsTypes?: string[];  // 除外する TS 型
  requireCastOnlyDbTypes?: string[];    // 特定の DB 型のみ検査
  resultIgnoreInferredTsTypes?: string[]; // 戻り値の型推論から除外
}
```

### デフォルト値

```typescript
// デフォルト SQL タグ
tags: ["sql", "SQL"]

// デフォルト対象メソッド
functions: [
  ["db", "query"],
  ["pool", "query"],
  ["pool", "execute"],
  ["connection", "query"],
  ["client", "query"],
]
```

## エラー例

リント実行時に以下のエラーが報告されます。

### 存在しないテーブル

```typescript
const query = sql`SELECT * FROM nonexistent_table`;
// ❌ Invalid SQL: table "nonexistent_table" does not exist
```

### 存在しないカラム

```typescript
const query = sql`SELECT id, age FROM users`;
// ❌ Invalid SQL: column "age" does not exist in table "users"
```

### 型ミスマッチ

```typescript
const query = sql<{ id: string }>`SELECT id FROM users`;
// ❌ Column 'id': inferred TypeScript type is 'number' but 'string' was declared
```

### パラメータ型の不一致

```typescript
const query = sql`SELECT * FROM users WHERE id = $1`;
// ユーザがパラメータを渡すときに型チェックされます
```

### キャスト要求

PostgreSQL で UUID 型カラムを比較する場合など、明示的なキャストが必要

```typescript
const query = sql`SELECT * FROM users WHERE id = $1`;
// ❌ Parameter $1 requires an explicit SQL cast to 'uuid' 
//    (e.g. $1::uuid for PostgreSQL)

// 修正
const query = sql`SELECT * FROM users WHERE id = $1::uuid`;
```

## トラブルシューティング

### 接続エラー

```
AnyQL analyzer failed: connection refused
```

#### 原因

データベースが起動していない、接続情報が誤っている

#### 対応

1. データベースが実行中か確認
2. `connectionEnvVar`、`envFile`、`connection` の設定を確認
3. タイムアウト値を増やす（`timeout: 60000`）

### タイムアウト

```
AnyQL analyzer failed: operation timed out
```

#### 原因
複雑なクエリに時間がかかっている、ネットワーク遅延

#### 対応
`timeout` を増加

```javascript
"anyql/valid-sql": ["error", { timeout: 60000 }]
```

### クエリが検査されない

#### 原因
`tags` や `functions` の設定が合致していない

#### 対応

```javascript
{
  tags: ["sql", "SQL", "gql"],  // 使用しているタグを指定
  functions: [["myDb", "execute"]],  // 実際のメソッド名を指定
}
```



## LICENSE

MIT

## Links

- [GitHub リポジトリ](https://github.com/yamachu/anyql)
- [npm パッケージ](https://www.npmjs.com/package/@yamachu/anyql)
