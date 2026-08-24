import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    globals: true,
    include: ["suites/**/*.spec.ts"],
    globalSetup: ["./src/global-setup.ts"],

    // One host, one in-memory store. Suites that count resources or walk pages
    // cannot tolerate another file mutating the store underneath them, so files
    // run one at a time. Within a file, tests still run in order.
    fileParallelism: false,

    testTimeout: 60_000,
    hookTimeout: 120_000,
    reporters: ["verbose"],
  },
});
