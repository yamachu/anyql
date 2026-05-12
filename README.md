# AnyQL

SQL クエリをライブ DB に対して解析し、カラム・パラメータの型メタデータを返すライブラリです。  
oxlint / ESLint プラグインとして使うことで、テンプレートリテラル SQL の型チェックをリント時に行えます。

## 前提ツール

| ツール | バージョン | 用途 |
|---|---|---|
| .NET SDK | 10.0.x | C# コアライブラリ・WASM ビルド |
| Node.js | ≥ 22 | JS パッケージ・テスト |
| Docker | — | Testcontainers (テスト用 PostgreSQL) |

### macOS で Docker を使う場合（Colima）

macOS では Docker Desktop の代わりに [Colima](https://github.com/abiosoft/colima) が使われることがあります。  
Colima のソケットパスは標準の `/var/run/docker.sock` **ではなく** ユーザーホーム配下になります。

```
unix://$HOME/.colima/default/docker.sock
```

後述のコマンド例ではこのパスを使います。Linux / Docker Desktop の場合は
`unix:///var/run/docker.sock` に読み替えてください。

---

## ビルド

### .NET

```bash
dotnet workload install wasm-tools wasm-experimental
dotnet restore
dotnet build -c Release
```

### WASM → npm パッケージ

```bash
cd packages/anyql-js
pnpm install
pnpm run build   # typecheck + vite build
```

`build` スクリプトは TypeScript の型チェックとビルドを実行します。
WASM の生成・連携は `@yamachu/vite-plugin-dotnet-wasm` 経由で行われます。

---

## テスト実行

テストは [Testcontainers](https://testcontainers.com/) で PostgreSQL コンテナを自動起動します。  
**Docker ソケットパス（`DOCKER_HOST`）が必要です。**

Ryuk 無効化（`TESTCONTAINERS_RYUK_DISABLED=true`）は任意設定ですが、
Docker Hub レート制限や Colima 環境での不安定さを避けるため、このリポジトリでは既定で有効化しています。
- .NET: `tests/AnyQL.Tests/testcontainers.runsettings` で設定済み
- JS: `packages/anyql-js/scripts/test.sh` と `examples/oxlint-plugin/scripts/run.sh` で設定済み

### 環境変数

| 変数 | 必須 | 説明 |
|---|---|---|
| `DOCKER_HOST` | 必須 | Docker ソケットの URI。macOS(Colima) は下記参照 |
| `TESTCONTAINERS_RYUK_DISABLED` | 任意（推奨） | `true` にすると Ryuk（リソース回収コンテナ）を起動しない。CIやColimaでの pull 失敗やレート制限回避に有効 |

### .NET テスト（全体）

以下は `dotnet build -c Release` 実行後を前提にした、CIと同じオプション構成です。

```bash
# macOS + Colima
DOCKER_HOST=unix://$HOME/.colima/default/docker.sock \
dotnet test --no-build -c Release --settings tests/AnyQL.Tests/testcontainers.runsettings --logger "console;verbosity=normal"

# Linux / Docker Desktop
DOCKER_HOST=unix:///var/run/docker.sock \
dotnet test --no-build -c Release --settings tests/AnyQL.Tests/testcontainers.runsettings --logger "console;verbosity=normal"
```

特定のテストクラスだけ実行する場合:

```bash
DOCKER_HOST=unix://$HOME/.colima/default/docker.sock \
dotnet test tests/AnyQL.Tests --no-build -c Release --settings tests/AnyQL.Tests/testcontainers.runsettings --filter "FullyQualifiedName~AnyQLClientTests" --logger "console;verbosity=normal"
```

### JS / TypeScript テスト（vitest）

```bash
cd packages/anyql-js
pnpm run test
```

`scripts/test.sh` が Docker ソケットを自動検出し、`TESTCONTAINERS_RYUK_DISABLED=true` を設定して vitest を実行します。

---

## サンプル（oxlint-plugin）

```bash
cd examples/oxlint-plugin
pnpm install
pnpm run start
```

`run.mjs` はテスト用 PostgreSQL コンテナを起動し、`src/valid.ts` と `src/invalid.ts` に対して oxlint を実行します。

### ESLint デモ

```bash
cd examples/oxlint-plugin
pnpm run eslint
```

#### 接続設定

`anyql/valid-sql` の接続先は以下の優先順位で解決されます。

| 優先度 | オプション | 説明 |
|---|---|---|
| 1 | `connection` | インラインオブジェクトで直接指定（最優先） |
| 2 | `envFile` | 指定した `.env` ファイルから URL を読み込む |
| 3 | `autoLoadDotEnv: true` | CWD の `.env` を自動読み込み |
| 4 | 環境変数 | `connectionEnvVar`（デフォルト: `ANYQL_CONNECTION`）で指定した環境変数 |

環境変数や `.env` ファイルには **接続 URL 形式**で値を設定します（JSON オブジェクトは不可）。

```
ANYQL_CONNECTION=postgresql://user:password@host:5432/database
ANYQL_CONNECTION=mysql://user:password@host:3306/database
```

`DATABASE_URL` など別の変数名を使う場合は `connectionEnvVar` で指定します。

```json
{
    "rules": {
        "anyql/valid-sql": ["error", { "connectionEnvVar": "DATABASE_URL" }]
    }
}
```

`.env` ファイルを使う場合:

```json
{
    "rules": {
        "anyql/valid-sql": ["error", { "envFile": ".env.local" }]
    }
}
```

インラインオブジェクトで指定する場合:

```json
{
    "rules": {
        "anyql/valid-sql": [
            "error",
            {
                "connection": {
                    "dialect": "postgresql",
                    "host": "localhost",
                    "port": 5432,
                    "user": "postgres",
                    "password": "secret",
                    "database": "mydb"
                }
            }
        ]
    }
}
```

#### requiresCast オプション

`anyql/valid-sql` は `requiresCast` の報告範囲を Options で調整できます。

```json
{
    "rules": {
        "anyql/valid-sql": [
            "error",
            {
                "connection": { "...": "..." },
                "requireCastMode": "strict",
                "requireCastIgnoreTsTypes": ["number"],
                "requireCastIgnoreDbTypes": ["int4", "numeric"],
                "requireCastOnlyDbTypes": ["uuid", "jsonb"]
            }
        ]
    }
}
```

### サンプルを網羅実行（oxlint + ESLint）

```bash
cd examples/oxlint-plugin
pnpm test
```

### ワークスペース全体の JS 側テスト（anyql-js + examples）

```bash
pnpm test
```

---

## プロジェクト構成

```
anyql/
├── src/
│   ├── AnyQL.Client/     # .NET クライアントライブラリ
│   ├── AnyQL.Core/       # コア型・インターフェース
│   ├── AnyQL.Postgres/   # PostgreSQL バックエンド
│   ├── AnyQL.MySql/      # MySQL バックエンド
│   └── AnyQL.Wasm/       # Blazor WASM エントリポイント
├── tests/
│   └── AnyQL.Tests/      # .NET E2E テスト
├── packages/
│   └── anyql-js/         # npm パッケージ (WASM ラッパー + oxlint プラグイン)
└── examples/
    └── oxlint-plugin/    # oxlint プラグインのデモ
```
