import { rename, writeFile } from "node:fs/promises";
import type { SceneFile } from "./types";

/**
 * Atomic write: serialize, write to a sibling temp file, fsync via close, rename.
 * Preserves UTF-8 without BOM. s&box scene files use tab indentation.
 */
export async function writeScene(path: string, scene: SceneFile): Promise<void> {
	const json = restoreBigInts(JSON.stringify(scene, null, "\t"));
	const tmp = `${path}.tmp-${process.pid}-${Date.now()}`;
	await writeFile(tmp, json, { encoding: "utf8" });
	await rename(tmp, path);
}

/** Counterpart to scene/read.ts:preserveBigInts. Unwrap "@bigint:N" → N. */
export function restoreBigInts(text: string): string {
	return text.replace(/"@bigint:(\d+)"/g, "$1");
}

export function newGuid(): string {
	return crypto.randomUUID();
}

export function vec3ToString(v: [number, number, number] | { x: number; y: number; z: number }): string {
	if (Array.isArray(v)) return `${v[0]},${v[1]},${v[2]}`;
	return `${v.x},${v.y},${v.z}`;
}

export function rotToString(r: [number, number, number, number] | { x: number; y: number; z: number; w: number }): string {
	if (Array.isArray(r)) return `${r[0]},${r[1]},${r[2]},${r[3]}`;
	return `${r.x},${r.y},${r.z},${r.w}`;
}
