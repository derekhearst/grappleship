import { mkdir, readFile, rename, rm, stat, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";

const IPC_DIR_NAME = "grappleship-bridge";

export interface BridgeResponse<T = unknown> {
	id: string;
	ok: boolean;
	result?: T;
	error?: string;
}

export interface BridgeRequestOptions {
	timeoutMs?: number;
	pollIntervalMs?: number;
}

export class BridgeError extends Error {
	code: string;
	constructor(code: string, message: string) {
		super(message);
		this.code = code;
	}
}

export async function callBridge<T = unknown>(
	action: string,
	args: Record<string, unknown> = {},
	opts: BridgeRequestOptions = {},
): Promise<T> {
	const dir = join(tmpdir(), IPC_DIR_NAME);
	await mkdir(dir, { recursive: true });
	const id = crypto.randomUUID();
	const reqPath = join(dir, `req-${id}.json`);
	const resPath = join(dir, `res-${id}.json`);

	const payload = JSON.stringify({ id, action, ...args });
	const tmp = `${reqPath}.tmp`;
	await writeFile(tmp, payload, { encoding: "utf8" });
	await rename(tmp, reqPath);

	const timeoutMs = opts.timeoutMs ?? 8000;
	const pollMs = opts.pollIntervalMs ?? 100;
	const deadline = Date.now() + timeoutMs;

	while (Date.now() < deadline) {
		try {
			await stat(resPath);
			const text = (await readFile(resPath, "utf8")).replace(/^﻿/, "");
			await rm(resPath, { force: true });
			const parsed = JSON.parse(text) as BridgeResponse<T>;
			if (!parsed.ok) {
				throw new BridgeError("bridge_error", parsed.error ?? "unknown bridge error");
			}
			return parsed.result as T;
		} catch (err: unknown) {
			const e = err as NodeJS.ErrnoException;
			if (e.code === "ENOENT") {
				await sleep(pollMs);
				continue;
			}
			if (err instanceof BridgeError) throw err;
			throw err;
		}
	}

	// Timeout — try to clean up the request so the editor doesn't pick it up later.
	await rm(reqPath, { force: true });
	throw new BridgeError(
		"timeout",
		`Editor bridge did not respond within ${timeoutMs}ms. Is the s&box editor running with the GrappleShip project loaded? (The bridge logs '[EditorBridge] watching ...' on startup.)`,
	);
}

function sleep(ms: number): Promise<void> {
	return new Promise((res) => setTimeout(res, ms));
}
