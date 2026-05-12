/**
 * nodeSocket.ts — node:net based socket bridge for the WASM JSImport contract.
 *
 * The .NET WASM module calls these functions via JSImport:
 *   connect(id, host, port) → Promise<void>
 *   write(id, dataBase64: string) → Promise<void>   // base64-encoded bytes
 *   read(id, maxBytes) → Promise<string>             // base64-encoded bytes
 *   close(id) → Promise<void>
 *
 * Data is exchanged as base64 strings because byte[] is not directly
 * marshallable in .NET WASM JSImport Promise return types.
 */

import * as net from "node:net";

interface SocketState {
  socket: net.Socket;
  /** Buffered incoming bytes not yet consumed by a read() call. */
  buffer: Buffer;
  /** Pending read resolvers waiting for data. */
  pendingReads: Array<{
    maxBytes: number;
    resolve: (data: Uint8Array) => void;
    reject: (err: Error) => void;
  }>;
  closed: boolean;
  error: Error | null;
}

const sockets = new Map<number, SocketState>();

function getState(id: number): SocketState {
  const state = sockets.get(id);
  if (!state) throw new Error(`No socket with id ${id}`);
  return state;
}

function drainPendingReads(state: SocketState): void {
  while (state.pendingReads.length > 0 && state.buffer.length > 0) {
    const pending = state.pendingReads.shift()!;
    const chunk = state.buffer.subarray(0, pending.maxBytes);
    state.buffer = state.buffer.subarray(chunk.length);
    pending.resolve(new Uint8Array(chunk));
  }
  // If socket is closed/errored, reject remaining reads
  if (state.closed || state.error) {
    const err = state.error ?? new Error("Socket closed");
    for (const pending of state.pendingReads) {
      pending.reject(err);
    }
    state.pendingReads = [];
  }
}

export function connect(id: number, host: string, port: number): Promise<void> {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection({ host, port });
    const state: SocketState = {
      socket,
      buffer: Buffer.alloc(0),
      pendingReads: [],
      closed: false,
      error: null,
    };
    sockets.set(id, state);

    socket.on("data", (chunk: Buffer) => {
      state.buffer = Buffer.concat([state.buffer, chunk]);
      drainPendingReads(state);
    });

    socket.on("end", () => {
      state.closed = true;
      drainPendingReads(state);
    });

    socket.on("error", (err: Error) => {
      state.error = err;
      drainPendingReads(state);
      reject(err);
    });

    socket.on("connect", () => resolve());
  });
}

export function write(id: number, dataBase64: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const state = getState(id);
    if (state.closed || state.error) {
      reject(state.error ?? new Error("Socket is closed"));
      return;
    }
    const buf = Buffer.from(dataBase64, "base64");
    state.socket.write(buf, (err) => {
      if (err) reject(err);
      else resolve();
    });
  });
}

export function read(id: number, maxBytes: number): Promise<string> {
  return new Promise((resolve, reject) => {
    const state = getState(id);
    if (state.error) {
      reject(state.error);
      return;
    }
    if (state.buffer.length > 0) {
      const chunk = state.buffer.subarray(0, maxBytes);
      state.buffer = state.buffer.subarray(chunk.length);
      resolve(chunk.toString("base64"));
      return;
    }
    if (state.closed) {
      resolve(""); // EOF
      return;
    }
    // Wrap the pending read to encode result as base64
    state.pendingReads.push({
      maxBytes,
      resolve: (data: Uint8Array) =>
        resolve(Buffer.from(data).toString("base64")),
      reject,
    });
  });
}

export function close(id: number): Promise<void> {
  return new Promise((resolve) => {
    const state = sockets.get(id);
    if (!state) {
      resolve();
      return;
    }
    state.closed = true;
    state.socket.destroy();
    sockets.delete(id);
    resolve();
  });
}
