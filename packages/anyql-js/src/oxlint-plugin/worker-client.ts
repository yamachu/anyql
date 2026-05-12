import {
  Worker,
  receiveMessageOnPort,
  MessageChannel,
} from "node:worker_threads";
import type { MessagePort } from "node:worker_threads";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

import type { AnalysisRequest, AnalysisResponse } from "./protocol.js";

const __dirname = dirname(fileURLToPath(import.meta.url));
const WORKER_PATH = join(__dirname, "worker.js");

interface WorkerHandle {
  worker: Worker;
  port1: MessagePort;
  sharedArray: Int32Array;
}

let workerHandle: WorkerHandle | undefined;

function getOrCreateWorker(): WorkerHandle {
  if (workerHandle) return workerHandle;

  const sharedBuffer = new SharedArrayBuffer(4);
  const sharedArray = new Int32Array(sharedBuffer);

  const { port1, port2 } = new MessageChannel();
  const worker = new Worker(WORKER_PATH, { workerData: { sharedBuffer } });

  worker.unref();
  port1.unref();
  worker.postMessage({ port: port2 }, [port2]);

  const initResult = Atomics.wait(sharedArray, 0, 0, 30_000);
  if (initResult === "timed-out") {
    worker.terminate();
    throw new Error("[anyql] Worker thread failed to initialize within 30 s");
  }

  const invalidate = () => {
    workerHandle = undefined;
  };
  worker.on("error", invalidate);
  worker.on("exit", invalidate);

  const terminate = () => worker.terminate();
  process.on("exit", terminate);
  process.once("SIGINT", () => {
    worker.terminate();
    process.exit(130);
  });
  process.once("SIGTERM", () => {
    worker.terminate();
    process.exit(143);
  });

  workerHandle = { worker, port1, sharedArray };
  return workerHandle;
}

export function analyzeAllSync(
  queries: AnalysisRequest[],
  timeoutMs: number,
): AnalysisResponse[] {
  const { port1, sharedArray } = getOrCreateWorker();

  Atomics.store(sharedArray, 0, 0);
  port1.postMessage({ queries });

  const totalTimeout = timeoutMs * queries.length + 15_000;
  const waitResult = Atomics.wait(sharedArray, 0, 0, totalTimeout);
  if (waitResult === "timed-out") {
    throw new Error(
      `[anyql] Worker thread did not respond within ${totalTimeout} ms`,
    );
  }

  const received = receiveMessageOnPort(port1);
  if (!received) {
    throw new Error("[anyql] Worker thread signaled done but sent no message");
  }

  return (received.message as { responses: AnalysisResponse[] }).responses;
}
