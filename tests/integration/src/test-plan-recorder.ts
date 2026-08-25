import { writeFileSync } from "node:fs";
import { join } from "node:path";
import { LEG, REPO_ROOT } from "./host.js";
import { edupass, type ScimRequestOptions, type ScimResponse } from "./client.js";

/**
 * Records what each test-plan case sent and received, and writes it out in the shape the
 * plan's own execution sheet uses.
 *
 * The columns are the plan's: S/N, Datetime, Input, Output, Status, Remarks. Input and
 * Output keep the sheet's layout too - a request line, then the body indented under it -
 * so a row here can be read next to a row there without translation.
 *
 * State is module-level, which is safe because `fileParallelism: false` means one file
 * runs at a time and the cases within it run in order.
 */

/** How much of a body is written before it is cut off. */
const MaximumBodyLength = 2_000;

interface Exchange {
  readonly method: string;
  readonly path: string;
  readonly requestBody?: string;
  readonly status: number;
  readonly responseBody: string;
}

interface Case {
  readonly serialNumber: string;
  readonly testCase: string;
  readonly startedAt: string;
  readonly exchanges: Exchange[];
  readonly remarks: string[];
  status: "Pass" | "Fail" | "Not run";
}

const cases: Case[] = [];
let current: Case | undefined;

function truncate(text: string): string {
  if (text.length <= MaximumBodyLength) {
    return text;
  }

  return `${text.slice(0, MaximumBodyLength)} ...(truncated, ${text.length} characters in total)`;
}

/**
 * Starts a case. The S/N and title are the plan's, verbatim, so that a row here and a row
 * there are the same case.
 */
export function beginCase(serialNumber: string, testCase: string, ...remarks: string[]): void {
  current = {
    serialNumber,
    testCase,
    startedAt: new Date().toISOString(),
    exchanges: [],
    remarks: [...remarks],
    status: "Not run",
  };
  cases.push(current);
}

/** Adds a remark. Used for what the SCIM endpoint cannot show, and why. */
export function note(remark: string): void {
  current?.remarks.push(remark);
}

/**
 * Sends to the Edupass host and records the exchange.
 *
 * Every request a case makes goes through here, so the Input column is the case's actual
 * traffic rather than a description of it.
 */
export async function call<T = any>(
  method: string,
  path: string,
  body?: unknown,
  options: ScimRequestOptions = {},
): Promise<ScimResponse<T>> {
  const response = await edupass<T>(method, path, body, options);

  current?.exchanges.push({
    method,
    path,
    requestBody:
      options.raw !== undefined
        ? options.raw
        : body === undefined
          ? undefined
          : JSON.stringify(body),
    status: response.status,
    responseBody: response.text,
  });

  return response;
}

/** Closes the case with the outcome vitest observed. */
export function endCase(status: "Pass" | "Fail", failure?: string): void {
  if (!current) {
    return;
  }

  current.status = status;

  if (failure) {
    current.remarks.push(`Failure: ${failure}`);
  }

  current = undefined;
}

function composeInput(item: Case): string {
  return item.exchanges
    .map((exchange) => {
      const line = `${exchange.method} ${exchange.path}`;
      return exchange.requestBody === undefined
        ? line
        : `${line}\n    ${truncate(exchange.requestBody)}`;
    })
    .join("\n");
}

function composeOutput(item: Case): string {
  return item.exchanges
    .map((exchange) => {
      const line = `${exchange.method} ${exchange.path}  ->  ${exchange.status}`;
      return exchange.responseBody.trim().length === 0
        ? line
        : `${line}\n    ${truncate(exchange.responseBody)}`;
    })
    .join("\n");
}

/** RFC 4180: quote anything carrying a comma, a quote or a newline, and double the quotes. */
function escape(value: string): string {
  if (!/[",\r\n]/u.test(value)) {
    return value;
  }

  return `"${value.replace(/"/gu, '""')}"`;
}

/**
 * Writes the results file.
 *
 * One per hosting leg, because the leg is what the run was against and two runs should not
 * overwrite one another. A byte-order mark so that Excel opens it as UTF-8 rather than
 * guessing - the file exists to sit beside a spreadsheet.
 */
export function writeResults(): string {
  const header = ["S/N", "Datetime", "Input", "Output", "Status", "Remarks"];

  const rows = cases.map((item) => [
    item.serialNumber,
    item.startedAt,
    composeInput(item),
    composeOutput(item),
    item.status,
    item.remarks.join("\n"),
  ]);

  const csv = [header, ...rows].map((row) => row.map(escape).join(",")).join("\r\n");

  const destination = join(REPO_ROOT, `edupass-test-plan-results.${LEG}.csv`);
  writeFileSync(destination, `﻿${csv}\r\n`, "utf8");

  return destination;
}

/** The cases recorded so far, for a summary line. */
export function summary(): { total: number; passed: number; failed: number } {
  return {
    total: cases.length,
    passed: cases.filter((item) => item.status === "Pass").length,
    failed: cases.filter((item) => item.status === "Fail").length,
  };
}
