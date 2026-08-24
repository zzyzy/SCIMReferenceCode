import { existsSync } from "node:fs";
import { BASE_URL, COVERAGE, COVERAGE_OUTPUT, LEG, startHost, stopHost } from "./host.js";

export async function setup(): Promise<void> {
  process.stdout.write(`\nSCIM integration tests: leg=${LEG} base=${BASE_URL} coverage=${COVERAGE}\n`);
  await startHost();
}

export async function teardown(): Promise<void> {
  await stopHost();

  // fetch pools its connections, and a pooled socket keeps this process's event loop
  // alive - which vitest reports as something preventing the Vite server from exiting.
  // globalThis.fetch is undici, but the package is not a declared dependency, so the
  // pool is reached through the agent Node already installed.
  const dispatcher = (globalThis as { [key: symbol]: unknown })[
    Symbol.for("undici.globalDispatcher.1")
  ] as { close?: () => Promise<void> } | undefined;
  await dispatcher?.close?.();

  if (COVERAGE) {
    // The collector writes the report only once the target process exits, so this
    // is the first point at which it can exist. Say so either way: a silent
    // absence reads as "coverage ran and found nothing".
    const written = existsSync(COVERAGE_OUTPUT);
    process.stdout.write(
      written
        ? `\ncoverage written to ${COVERAGE_OUTPUT}\n`
        : `\nWARNING: coverage was requested but ${COVERAGE_OUTPUT} was not written\n`,
    );
  }
}
