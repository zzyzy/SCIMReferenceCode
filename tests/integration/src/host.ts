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

/**
 * An external Edupass host, for the `edupass*` suites specifically.
 *
 * Needed because SCIM_BASE_URL alone cannot aim those suites anywhere. The `edupass`
 * client helper defaults to EDUPASS_BASE_URL, which is a separate sample process on its
 * own port, so overriding BASE_URL redirects only the core suites - and those bind
 * /Users to Core2User and are not Edupass-shaped. A relying party wanting to run the
 * Edupass conformance suite against its own implementation had no way to do it.
 *
 * Falls back to SCIM_BASE_URL, because a relying party that serves the Edupass resource
 * types serves them at its one base address. The two-process split is an artefact of the
 * sample host, where two providers cannot share one /Users route.
 */
const EXTERNAL_EDUPASS_BASE_URL =
  process.env["SCIM_EDUPASS_BASE_URL"] ?? EXTERNAL_BASE_URL;

/**
 * Whether any external host is configured, and therefore no sample host should be
 * started or torn down.
 */
const EXTERNAL = EXTERNAL_BASE_URL ?? EXTERNAL_EDUPASS_BASE_URL;

/**
 * A credential to present to an external host, instead of the sample's dev bearer token.
 *
 * A real relying party does not accept a token minted from the committed development
 * key, so without this the suites can only ever observe its 401. Set both variables:
 *
 *   SCIM_AUTH_HEADER=x-dsapi-key SCIM_AUTH_VALUE=<key>
 *
 * Applied only where a test has not set an Authorization header of its own, so the
 * negative authentication cases still assert what they were written to assert rather
 * than being handed a valid credential.
 */
export const EXTERNAL_AUTH: { readonly header: string; readonly value: string } | undefined =
  process.env["SCIM_AUTH_HEADER"] && process.env["SCIM_AUTH_VALUE"]
    ? {
        header: process.env["SCIM_AUTH_HEADER"] as string,
        value: process.env["SCIM_AUTH_VALUE"] as string,
      }
    : undefined;

const PORTS: Record<Leg, number> = { net10: 5180, net48: 5181 };

/**
 * The Edupass host, run alongside the core one.
 *
 * Edupass's User resource type carries an extension schema, so it needs its own
 * `AddScim<EduPassUser>` registration and cannot share a process with the plain
 * core provider - two providers cannot serve one `/Users` route. The sample host
 * selects it with SCIM_PROVIDER=edupass; see WebHostSample/Program.cs.
 */
const EDUPASS_PORTS: Record<Leg, number> = { net10: 5183, net48: 5184 };

/**
 * A host whose provider implements nothing.
 *
 * The only way to see what the shared handlers do with an operation no provider has
 * written yet - which has to be 501, not 500. A working provider never throws
 * NotImplementedException, so no other host can show it.
 */
const UNIMPLEMENTED_PORTS: Record<Leg, number> = { net10: 5185, net48: 5186 };

/**
 * A host whose provider throws from everything, discovery included.
 *
 * The only way to see the shape of the answer when a provider fails for a reason the
 * library knows nothing about - which has to be the RFC 7644 3.12 error body, not an
 * ASP.NET one and not a stack trace.
 */
const FAULTY_PORTS: Record<Leg, number> = { net10: 5187, net48: 5188 };

/**
 * The Edupass host as a relying party that stores UIN/FIN.
 *
 * One flag drives both what the extension schema advertises and what validation
 * requires, so the two halves of that behaviour can only be seen together - and only
 * on a host constructed with it on.
 */
const EDUPASS_UINFIN_PORTS: Record<Leg, number> = { net10: 5189, net48: 5190 };

/**
 * The Edupass host with JWT validation enforced.
 *
 * The sample turns issuer, audience, lifetime and signing-key validation off in
 * Development, and Development is what the harness has to run to start a host at
 * all - the Release branch resolves its keys over OIDC metadata. So a rejection of
 * an expired token, a wrong issuer or a wrong audience cannot be seen on the
 * ordinary Edupass host, only here: SCIM_ENFORCE_JWT=1 turns those four checks back
 * on over the same committed symmetric key.
 */
const EDUPASS_STRICT_PORTS: Record<Leg, number> = { net10: 5191, net48: 5192 };

const configuredPrefix = process.env["SCIM_PATH_PREFIX"];
const pathPrefix = (configuredPrefix ?? "scim").trim().replace(/^\/+|\/+$/gu, "");
const routeBase = pathPrefix.length === 0 ? "" : `/${pathPrefix}`;

export const BASE_URL = EXTERNAL_BASE_URL ?? `http://localhost:${PORTS[LEG]}${routeBase}`;

/**
 * The location code the Edupass suites build group displayNames from.
 *
 * A displayName is `<location>_<app>_<role>`, and in at least one relying party
 * membership is scoped to the *location* rather than to the group - so every group on a
 * location lists every user holding it. A fixed code means each run inherits every group
 * earlier runs left there, and an assertion that a user holds exactly one role stops
 * being about the provider at all. Overridable so a run can have a location to itself;
 * 1001 by default, which is what the Edupass test plan's sample data uses.
 */
export const EDUPASS_LOCATION = process.env["SCIM_EDUPASS_LOCATION"] ?? "1001";

export const EDUPASS_BASE_URL =
  EXTERNAL_EDUPASS_BASE_URL ?? `http://localhost:${EDUPASS_PORTS[LEG]}${routeBase}`;

export const UNIMPLEMENTED_BASE_URL = `http://localhost:${UNIMPLEMENTED_PORTS[LEG]}${routeBase}`;

export const FAULTY_BASE_URL = `http://localhost:${FAULTY_PORTS[LEG]}${routeBase}`;

export const EDUPASS_UINFIN_BASE_URL = `http://localhost:${EDUPASS_UINFIN_PORTS[LEG]}${routeBase}`;

export const EDUPASS_STRICT_BASE_URL = `http://localhost:${EDUPASS_STRICT_PORTS[LEG]}${routeBase}`;

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
 * Keeps plumbing out of the denominator.
 *
 * The library carries a client-side request builder, a Newtonsoft deserializer family
 * the ASP.NET Core host bypasses, SET/event-token types no host wires up, and the
 * usual utility and configuration scaffolding. None of it is SCIM behaviour a request
 * can exercise, so counting it only makes the number say less than it looks like it
 * says. Every exclusion in this file names why it is there.
 */
const COVERAGE_SETTINGS = resolve(HERE, "..", "coverage.settings.xml");

/**
 * Names the collection so that it can be closed deliberately.
 *
 * dotnet-coverage writes its report when collection ends cleanly. Force-killing the
 * process tree ends it uncleanly and no report is written at all - which is what the
 * first run of this did.
 */
const COVERAGE_SESSION = "scim-integration";

let children: ChildProcess[] = [];
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

async function waitUntilReachable(url: string, timeoutMs = 120_000): Promise<void> {
  const deadline = Date.now() + timeoutMs;

  while (Date.now() < deadline) {
    if (await reachable(url)) {
      return;
    }
    await new Promise((done) => setTimeout(done, 400));
  }

  throw new Error(`${url} did not come up within ${timeoutMs}ms`);
}

interface HostSpec {
  readonly port: number;
  /** Extra environment for this host, e.g. the provider selection. */
  readonly environment: Record<string, string>;
}

function specs(): HostSpec[] {
  return [
    { port: PORTS[LEG], environment: {} },
    { port: EDUPASS_PORTS[LEG], environment: { SCIM_PROVIDER: "edupass" } },
    { port: UNIMPLEMENTED_PORTS[LEG], environment: { SCIM_PROVIDER: "unimplemented" } },
    { port: FAULTY_PORTS[LEG], environment: { SCIM_PROVIDER: "faulty" } },
    {
      port: EDUPASS_UINFIN_PORTS[LEG],
      environment: { SCIM_PROVIDER: "edupass", SCIM_EDUPASS_REQUIRE_UINFIN: "1" },
    },
    {
      port: EDUPASS_STRICT_PORTS[LEG],
      environment: { SCIM_PROVIDER: "edupass", SCIM_ENFORCE_JWT: "1" },
    },
  ];
}

function spawnChild(command: string, args: string[], paths: HostPaths, environment: NodeJS.ProcessEnv): ChildProcess {
  // Pipes are attached only when asked for. An open pipe to a live child keeps this
  // process's event loop alive, so vitest reports that something is preventing the
  // Vite server from exiting - and the run hangs for ten seconds at the end.
  const logging = process.env["SCIM_HOST_LOG"] === "1";

  const child = spawn(command, args, {
    cwd: paths.workingDirectory,
    env: environment,
    stdio: logging ? ["ignore", "pipe", "pipe"] : "ignore",
    shell: process.platform === "win32",
  });

  if (logging) {
    child.stdout?.on("data", (chunk: Buffer) => process.stdout.write(`[host] ${chunk}`));
    child.stderr?.on("data", (chunk: Buffer) => process.stderr.write(`[host:err] ${chunk}`));
  }

  child.unref();
  children.push(child);
  return child;
}

export async function startHost(): Promise<void> {
  if (EXTERNAL) {
    // Probed rather than assumed reachable, so a misconfigured address fails with "did
    // not come up" here instead of as every assertion in the run.
    await waitUntilReachable(`${EXTERNAL}/ServiceProviderConfig`);
    return;
  }

  const paths = hostPaths(LEG);

  if (!existsSync(paths.executable)) {
    throw new Error(
      `${paths.executable} is missing. Build the solution first: dotnet build Microsoft.SCIM.sln`,
    );
  }

  const hosts = specs();

  if (COVERAGE && LEG === "net10") {
    // Server mode, then one `connect` per host. `collect <command>` measures a single
    // launched process; the Edupass provider needs a second one, and both have to land
    // in the same report or the library's coverage is split across two files.
    spawnChild(
      "dotnet-coverage",
      [
        "collect",
        "--server-mode",
        "--session-id",
        COVERAGE_SESSION,
        "--settings",
        COVERAGE_SETTINGS,
        "--output-format",
        "cobertura",
        "--output",
        COVERAGE_OUTPUT,
      ],
      paths,
      process.env,
    );
    collecting = true;

    // The server has to be listening before a connect can find it.
    await new Promise((done) => setTimeout(done, 3_000));
  }

  for (const host of hosts) {
    const environment = {
      ...process.env,
      ...host.environment,
      ASPNETCORE_ENVIRONMENT: "Development",
      ASPNETCORE_URLS: `http://localhost:${host.port}`,
    };

    if (collecting) {
      spawnChild(
        "dotnet-coverage",
        ["connect", COVERAGE_SESSION, "dotnet", paths.dll],
        paths,
        environment,
      );
    } else if (LEG === "net48") {
      spawnChild(paths.executable, [`http://localhost:${host.port}`], paths, environment);
    } else {
      spawnChild(paths.executable, [], paths, environment);
    }
  }

  for (const host of hosts) {
    await waitUntilReachable(`http://localhost:${host.port}/scim/ServiceProviderConfig`);
  }
}

function shutdownCollection(): Promise<void> {
  return new Promise((done) => {
    const shutdown = spawn("dotnet-coverage", ["shutdown", COVERAGE_SESSION], {
      stdio: "ignore",
      shell: process.platform === "win32",
    });
    shutdown.once("exit", () => done());
    shutdown.once("error", () => done());
    setTimeout(done, 120_000);
  });
}

function kill(child: ChildProcess): void {
  if (child.exitCode !== null || child.pid === undefined) {
    return;
  }

  if (process.platform === "win32") {
    // /T so the tree goes with it: under coverage each host is a grandchild of this
    // process, and killing only the collector would leave a host holding its port.
    const killer = spawn("taskkill", ["/PID", String(child.pid), "/T", "/F"], { stdio: "ignore" });
    killer.unref();
  } else {
    child.kill("SIGTERM");
  }
}

/**
 * Kills whatever is listening on a port.
 *
 * `shell: true` on Windows means the tracked child is the shell, not the host, and a
 * tree kill through it is unreliable enough that hosts survived the run - holding the
 * port and locking the assembly the next build has to overwrite. The port is the one
 * handle that identifies the process we actually started.
 */
function killByPort(port: number): Promise<void> {
  if (process.platform !== "win32") {
    return new Promise((done) => {
      const killer = spawn("sh", ["-c", `fuser -k ${port}/tcp 2>/dev/null || true`], {
        stdio: "ignore",
      });
      killer.once("exit", () => done());
      killer.once("error", () => done());
    });
  }

  const script =
    `Get-NetTCPConnection -LocalPort ${port} -State Listen -ErrorAction SilentlyContinue | ` +
    `ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }`;

  return new Promise((done) => {
    const killer = spawn("powershell", ["-NoProfile", "-Command", script], { stdio: "ignore" });
    killer.once("exit", () => done());
    killer.once("error", () => done());
    setTimeout(done, 20_000);
  });
}

export async function stopHost(): Promise<void> {
  const running = children;
  children = [];

  if (collecting) {
    // Ends the collection, which stops the connected hosts and writes the report.
    // Killing the tree instead leaves the report unwritten.
    await shutdownCollection();
    await new Promise((done) => setTimeout(done, 2_000));
    collecting = false;
  }

  for (const child of running) {
    kill(child);
    child.stdout?.destroy();
    child.stderr?.destroy();
  }

  if (!EXTERNAL) {
    for (const host of specs()) {
      await killByPort(host.port);
    }
  }
}
