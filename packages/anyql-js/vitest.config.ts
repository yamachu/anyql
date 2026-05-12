import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    // Use the globalSetup file to spin up / tear down the PostgreSQL container
    globalSetup: "./test/globalSetup.ts",
    // E2E tests can be slow (container startup + WASM init)
    testTimeout: 60_000,
    // Run serially so all tests share the same container started in globalSetup
    pool: "forks",
    forks: {
      singleFork: true,
    },
  },
});
