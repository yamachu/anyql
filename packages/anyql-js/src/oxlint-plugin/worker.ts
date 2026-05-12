/**
 * worker.ts — Worker thread for batch SQL analysis (worker_threads edition).
 *
 * Loaded by the oxlint plugin rule as a `worker_threads` Worker. Shares a
 * SharedArrayBuffer with the main (plugin) thread for synchronous IPC:
 * the main thread blocks on Atomics.wait while this thread processes queries.
 *
 * Lifecycle
 * ─────────
 * 1. Worker starts; receives the SharedArrayBuffer via workerData and a
 *    MessagePort (port2) via a one-shot parentPort message.
 * 2. Worker sets up the port message listener, then signals "ready" by storing
 *    1 in sharedArray[0] and calling Atomics.notify.
 * 3. Main thread unblocks from Atomics.wait and proceeds with linting.
 * 4. For each file the plugin lints, the main thread:
 *      a. Stores 0 in sharedArray[0] (reset).
 *      b. Posts { queries } to port1.
 *      c. Blocks on Atomics.wait again.
 * 5. This worker receives the message, runs all queries (WASM is loaded on the
 *    first batch and stays resident for the whole lint run), posts { responses }
 *    back, and signals done via sharedArray[0] = 1 + Atomics.notify.
 * 6. Main thread unblocks, reads the result via receiveMessageOnPort.
 *
 * stdout isolation
 * ────────────────
 * The .NET WASM runtime may write diagnostics to stdout. We redirect
 * stdout → stderr for the entire worker lifetime so the MessagePort channel
 * is never polluted by WASM noise.
 */

import { parentPort, workerData } from "node:worker_threads";
import type { MessagePort } from "node:worker_threads";
import { analyze } from "../index.js";
import type { AnalysisRequest, AnalysisResponse } from "./protocol.js";

// Redirect stdout → stderr before WASM loads.
process.stdout.write = process.stderr.write.bind(
  process.stderr,
) as typeof process.stdout.write;

const { sharedBuffer } = workerData as { sharedBuffer: SharedArrayBuffer };
const sharedArray = new Int32Array(sharedBuffer);

// ── Receive MessagePort from main thread ──────────────────────────────────────
// The main thread transfers port2 via worker.postMessage({ port: port2 }, [port2]).

const { port } = await new Promise<{ port: MessagePort }>((resolve) => {
  parentPort!.once("message", resolve);
});

// ── Signal ready ──────────────────────────────────────────────────────────────
// WASM loads lazily on the first analyze() call. The "ready" signal means the
// message listener is active — WASM startup cost is paid once on the first
// query batch, then the runtime stays resident for the whole lint run.

Atomics.store(sharedArray, 0, 1);
Atomics.notify(sharedArray, 0, 1);

// ── Handle query batches ──────────────────────────────────────────────────────

port.on("message", async ({ queries }: { queries: AnalysisRequest[] }) => {
  const responses: AnalysisResponse[] = await Promise.all(
    queries.map(({ sql, conn }) =>
      analyze(sql, conn)
        .then((result): AnalysisResponse => ({ ok: true, result }))
        .catch(
          (err: Error): AnalysisResponse => ({ ok: false, error: err.message }),
        ),
    ),
  );

  // Post result first, then signal — main thread reads via receiveMessageOnPort
  // after Atomics.wait returns, so the message must be queued before the notify.
  port.postMessage({ responses });
  Atomics.store(sharedArray, 0, 1);
  Atomics.notify(sharedArray, 0, 1);
});
