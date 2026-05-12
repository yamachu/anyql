import dotnetWasm from "@yamachu/vite-plugin-dotnet-wasm";

import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

import { defineConfig } from "vite";
import dts from "vite-plugin-dts";

const __dirname = fileURLToPath(new URL(".", import.meta.url));

export default defineConfig({
  build: {
    lib: {
      entry: {
        index: resolve(__dirname, "src/index.ts"),
        nodeSocket: resolve(__dirname, "src/nodeSocket.ts"),
        "oxlint-plugin/index": resolve(__dirname, "src/oxlint-plugin/index.ts"),
        "oxlint-plugin/worker": resolve(
          __dirname,
          "src/oxlint-plugin/worker.ts",
        ),
      },
      formats: ["es"],
    },
    rollupOptions: {
      // Externalize all Node.js built-in modules
      external: [/^node:/],
    },
    target: "node22",
    outDir: "dist",
    assetsDir: "wasm",
  },
  plugins: [
    dotnetWasm({
      projectPath: "../../src/AnyQL.Wasm/AnyQL.Wasm.csproj",
      configuration: "Release",
      watch: false,
      publish: true,
    }),
    dts({
      include: ["src"],
    }),
  ],
});
