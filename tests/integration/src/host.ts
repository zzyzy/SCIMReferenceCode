import { spawn, type ChildProcess } from "node:child_process";
import { existsSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
export const REPO_ROOT = resolve(HERE, "..", "..", "..");

/**
 * Which sample host to exercise.
 *
 * The net10.0 leg is the default because coverage collection targets a managed
 * `dotnet <dll>` launch. The net48 leg is a framework executable: it can be
 * exercised, but `dotnet-coverage` is not wired for it here.
 */
export type Leg = "net10" | "net48";

export const LEG: Leg = (process.env["SCIM_LEG"] as Leg) ?? "net10";
export const COVERAGE = process.env["SCIM_COVERAGE"] === "1";

/** An external host to test instead of starting one, e.g. SCIM_BASE_URL=http://host/scim. */
const EXTERNAL_BASE_URL = process.env["SCIM_BASE_URL"];

const PORTS: Record<Leg, number> = { net10: 5180, net48: 5181 };

export const BASE_URL = EXTERNAL_BASE_URL ?? `http://localhost:${PORTS[LEG]}/scim`;

/**
 * The development signing key committed to appsettings.Development.json.
 *
 * Deliberately duplicated rather than read from the file: if the sample's key
 * changes, these tests should fail loudly rather than silently follow it, since
 * a token the tests can mint is a token anyone can mint.
 */
export const DEV_SIGNING_KEY = "A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4";
export const DEV_ISSUER = "Microsoft.Security.Bearer";
export const DEV_AUDIENCE = "Microsoft.Security.Bearer";

interface HostPaths {
  readonly workingDirectory: string;
  readonly dll: string;
  readonly executable: string;
}

function hostPaths(leg: Leg): HostPaths {
  if (leg === "net48") {
    const workingDirectory = join(
      REPO_ROOT,
      "Microsoft.SCIM.WebHostSample.Net48",
      "bin",
      "Debug",
      "net48",
    );
    return {
      workingDirectory,
      dll: "",
      executable: join(workingDirectory, "Microsoft.SCIM.WebHostSample.Net48.exe"),
    };
  }

  const workingDirectory = join(REPO_ROOT, "Microsoft.SCIM.WebHostSample");
  return {
    workingDirectory,
    dll: join(workingDirectory, "bin", "Debug", "net10.0", "Microsoft.SCIM.WebHostSample.dll"),
    executable: join(
      workingDirectory,
      "bin",
      "Debug",
      "net10.0",
      "Microsoft.SCIM.WebHostSample.exe",
    ),
  };
}

export const COVERAGE_OUTPUT = join(REPO_ROOT, "integration-coverage.xml");

/**
 * Names the collection so that it can be closed deliberately.
 *
 * dotnet-coverage writes its report when collection ends cleanly. Force-killing the
 * process tree ends it uncleanly and no report is written at all - which is what the
 * first run of this did.
 */
const COVERAGE_SESSION = "scim-integration";

let host: ChildProcess | undefined;
let collecting = false;

async function reachable(url: string): Promise<boolean> {
  try {
    // Unauthenticated: 401 proves the pipeline is up without needing a token yet.
    const response = await fetch(url, { method: "GET" });
    return response.status > 0;
  } catch {
    return false;
  }
}

async function waitUntilReachable(url: string, timeoutMs = 90_000): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  let lastError = "no attempt made";

  while (Date.now() < deadline) {
    if (host?.exitCode !== null && host?.exitCode !== undefined) {
      throw new Error(`the host exited with code ${host.exitCode} before becoming reachable`);
    }
    if (await reachable(url)) {
      return;
    }
    lastError = `not reachable yet`;
    await new Promise((done) => setTimeout(done, 400));
  }

  throw new Error(`${url} did not come up within ${timeoutMs}ms (${lastError})`);
}

export async function startHost(): Promise<void> {
  if (EXTERNAL_BASE_URL) {
    await waitUntilReachable(`${EXTERNAL_BASE_URL}/ServiceProviderConfig`);
    return;
  }

  const paths = hostPaths(LEG);
  const port = PORTS[LEG];

  if (!existsSync(paths.executable)) {
    throw new Error(
      `${paths.executable} is missing. Build the solution first: dotnet build Microsoft.SCIM.sln`,
    );
  }

  const environment = {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: "Development",
    ASPNETCORE_URLS: `http://localhost:${port}`,
  };

  let command: string;
  let args: string[];

  if (COVERAGE && LEG === "net10") {
    // The shape the task asked for. dotnet-coverage launches the target itself and
    // writes the report when that process exits, which is why stopHost has to end
    // the host politely rather than killing the collector.
    command = "dotnet-coverage";
    args = [
      "collect",
      `dotnet ${paths.dll}`,
      "--session-id",
      COVERAGE_SESSION,
      "--output-format",
      "cobertura",
      "--output",
      COVERAGE_OUTPUT,
    ];
    collecting = true;
  } else if (LEG === "net48") {
    command = paths.executable;
    args = [`http://localhost:${port}`];
  } else {
    command = paths.executable;
    args = [];
  }

  // Pipes are attached only when asked for. An open pipe to a live child keeps this
  // process's event loop alive, so vitest reports that something is preventing the
  // Vite server from exiting - and the run hangs for ten seconds at the end.
  const logging = process.env["SCIM_HOST_LOG"] === "1";

  host = spawn(command, args, {
    cwd: paths.workingDirectory,
    env: environment,
    stdio: logging ? ["ignore", "pipe", "pipe"] : "ignore",
    shell: process.platform === "win32",
  });

  if (logging) {
    host.stdout?.on("data", (chunk: Buffer) => process.stdout.write(`[host] ${chunk}`));
    host.stderr?.on("data", (chunk: Buffer) => process.stderr.write(`[host:err] ${chunk}`));
  }

  host.unref();

  await waitUntilReachable(`http://localhost:${port}/scim/ServiceProviderConfig`);
}

function shutdownCollection(): Promise<void> {
  return new Promise((done) => {
    const shutdown = spawn("dotnet-coverage", ["shutdown", COVERAGE_SESSION], {
      stdio: "ignore",
      shell: process.platform === "win32",
    });
    shutdown.once("exit", () => done());
    shutdown.once("error", () => done());
    setTimeout(done, 60_000);
  });
}

export async function stopHost(): Promise<void> {
  if (!host || host.exitCode !== null) {
    return;
  }

  const exited = new Promise<void>((done) => host?.once("exit", () => done()));

  if (collecting) {
    // Ends the collection, which stops the target and writes the report. Killing the
    // tree instead leaves the report unwritten.
    await shutdownCollection();
    await Promise.race([exited, new Promise((done) => setTimeout(done, 30_000))]);
  }

  if (host.exitCode !== null) {
    host.stdout?.destroy();
    host.stderr?.destroy();
    host = undefined;
    return;
  }

  if (process.platform === "win32") {
    // /T so the tree goes with it: under coverage the host is a grandchild of this
    // process, and killing only the collector would leave the host holding the port
    // and the report unwritten.
    const killer = spawn("taskkill", ["/PID", String(host.pid), "/T", "/F"], {
      stdio: "ignore",
    });
    killer.unref();
  } else {
    host.kill("SIGTERM");
  }

  await Promise.race([exited, new Promise((done) => setTimeout(done, 30_000))]);

  host.stdout?.destroy();
  host.stderr?.destroy();
  host = undefined;
}
