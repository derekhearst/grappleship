import { readdir, readFile } from "node:fs/promises";
import { join, resolve } from "node:path";
import type { GameObjectNode, SceneFile } from "./types";

export async function listSceneFiles(scenesRoot: string): Promise<string[]> {
	const out: string[] = [];
	let entries;
	try {
		entries = await readdir(scenesRoot, { withFileTypes: true });
	} catch (err: unknown) {
		const e = err as NodeJS.ErrnoException;
		if (e.code === "ENOENT") return [];
		throw err;
	}
	for (const ent of entries) {
		const p = resolve(scenesRoot, ent.name);
		if (ent.isDirectory()) {
			out.push(...(await listSceneFiles(p)));
		} else if (ent.isFile() && ent.name.endsWith(".scene")) {
			out.push(p);
		}
	}
	return out;
}

export async function readScene(path: string): Promise<SceneFile> {
	const text = (await readFile(path, "utf8")).replace(/^﻿/, "");
	return JSON.parse(preserveBigInts(text)) as SceneFile;
}

/**
 * JS doesn't have native UInt64. Round-tripping a value like `BodyGroups:
 * 18446744073709551615` (UInt64.MaxValue, a common "all flags on" sentinel)
 * through JSON.parse + JSON.stringify silently corrupts it to
 * `18446744073709552000`. To preserve fidelity, we tag any unquoted integer
 * literal beyond Number.MAX_SAFE_INTEGER as a string sentinel during parse,
 * and unwrap it on write (see scene/write.ts).
 */
export function preserveBigInts(text: string): string {
	// Match `: <17+ digit integer>` followed by , } ] or whitespace.
	return text.replace(/:\s*(\d{16,})(?=\s*[,}\]\n\r])/g, (_match, digits) => {
		try {
			if (BigInt(digits) > BigInt(Number.MAX_SAFE_INTEGER)) {
				return `: "@bigint:${digits}"`;
			}
		} catch {
			// fall through
		}
		return _match;
	});
}

export function* walkGameObjects(scene: SceneFile): Generator<{ go: GameObjectNode; path: string[] }> {
	function* walk(nodes: GameObjectNode[], path: string[]): Generator<{ go: GameObjectNode; path: string[] }> {
		for (const go of nodes) {
			const segPath = [...path, go.Name ?? go.__guid];
			yield { go, path: segPath };
			if (go.Children?.length) {
				yield* walk(go.Children, segPath);
			}
		}
	}
	yield* walk(scene.GameObjects ?? [], []);
}

export function findGameObject(
	scene: SceneFile,
	predicate: (go: GameObjectNode, path: string[]) => boolean,
): { go: GameObjectNode; path: string[] } | null {
	for (const entry of walkGameObjects(scene)) {
		if (predicate(entry.go, entry.path)) return entry;
	}
	return null;
}

export function findScenePath(scenesRoot: string, hint: string): Promise<string | null> {
	return resolveSceneByName(scenesRoot, hint);
}

async function resolveSceneByName(scenesRoot: string, hint: string): Promise<string | null> {
	const all = await listSceneFiles(scenesRoot);
	const target = hint.toLowerCase().replace(/\\/g, "/");
	const exact = all.find((p) => p.toLowerCase().replace(/\\/g, "/").endsWith(target));
	if (exact) return exact;
	const stem = target.replace(/\.scene$/, "");
	const stemMatch = all.find((p) => {
		const name = p.split(/[\\/]/).pop()!.toLowerCase().replace(/\.scene$/, "");
		return name === stem;
	});
	return stemMatch ?? null;
}

export function parseVector3(s: string): [number, number, number] | null {
	const parts = s.split(",").map((x) => Number(x.trim()));
	if (parts.length !== 3 || parts.some((n) => !Number.isFinite(n))) return null;
	return [parts[0]!, parts[1]!, parts[2]!];
}

export function parseRotation(s: string): [number, number, number, number] | null {
	const parts = s.split(",").map((x) => Number(x.trim()));
	if (parts.length !== 4 || parts.some((n) => !Number.isFinite(n))) return null;
	return [parts[0]!, parts[1]!, parts[2]!, parts[3]!];
}

export function parseColor(s: string): number[] | null {
	const parts = s.split(",").map((x) => Number(x.trim()));
	if ((parts.length !== 3 && parts.length !== 4) || parts.some((n) => !Number.isFinite(n))) return null;
	return parts;
}
