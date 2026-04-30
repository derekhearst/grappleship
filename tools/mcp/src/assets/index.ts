import { readdir, stat } from "node:fs/promises";
import { extname, relative, resolve } from "node:path";

export type AssetKind =
	| "model"
	| "material"
	| "texture"
	| "sound"
	| "sound_event"
	| "scene"
	| "prefab"
	| "shader"
	| "animation"
	| "particle"
	| "ui"
	| "other";

const EXT_MAP: Record<string, AssetKind> = {
	".vmdl": "model",
	".vmat": "material",
	".vtex": "texture",
	".png": "texture",
	".jpg": "texture",
	".jpeg": "texture",
	".tga": "texture",
	".vsnd": "sound",
	".wav": "sound",
	".mp3": "sound",
	".ogg": "sound",
	".vsndevts": "sound_event",
	".scene": "scene",
	".prefab": "prefab",
	".shader": "shader",
	".vfx": "shader",
	".vanmgrph": "animation",
	".vanm": "animation",
	".vpcf": "particle",
	".razor": "ui",
	".scss": "ui",
};

export interface AssetEntry {
	path: string; // forward-slash relative to assets root, lowercased extension
	kind: AssetKind;
	size: number;
	mtime: number;
}

export interface AssetIndex {
	root: string;
	entries: AssetEntry[];
}

let cached: { rootKey: string; mtime: number; index: AssetIndex } | null = null;

export async function buildAssetIndex(assetsRoot: string, force = false): Promise<AssetIndex> {
	let topMtime = 0;
	try {
		const s = await stat(assetsRoot);
		topMtime = s.mtimeMs;
	} catch {
		return { root: assetsRoot, entries: [] };
	}
	if (!force && cached && cached.rootKey === assetsRoot && cached.mtime === topMtime) {
		return cached.index;
	}
	const entries: AssetEntry[] = [];
	await walk(assetsRoot, assetsRoot, entries);
	const index = { root: assetsRoot, entries };
	cached = { rootKey: assetsRoot, mtime: topMtime, index };
	return index;
}

// Compiled outputs / temp / metadata files we don't want to expose to Claude.
// Authoring assets only.
const SKIP_EXT = new Set([
	".scene_c",
	".scene_d",
	".vmdl_c",
	".vmat_c",
	".vtex_c",
	".vsnd_c",
	".vfx_c",
	".vpcf_c",
	".vsndevts_c",
	".tmp",
	".bak",
]);
const SKIP_DIRS = new Set([".sbox-cache", "compiled", ".vs", "obj", "bin"]);

async function walk(root: string, dir: string, out: AssetEntry[]): Promise<void> {
	let ents;
	try {
		ents = await readdir(dir, { withFileTypes: true });
	} catch {
		return;
	}
	for (const ent of ents) {
		const p = resolve(dir, ent.name);
		if (ent.isDirectory()) {
			if (SKIP_DIRS.has(ent.name)) continue;
			await walk(root, p, out);
			continue;
		}
		if (!ent.isFile()) continue;
		const ext = extname(ent.name).toLowerCase();
		if (SKIP_EXT.has(ext)) continue;
		const kind = EXT_MAP[ext] ?? "other";
		let size = 0;
		let mtime = 0;
		try {
			const s = await stat(p);
			size = s.size;
			mtime = s.mtimeMs;
		} catch {
			// ignore
		}
		out.push({
			path: relative(root, p).replace(/\\/g, "/"),
			kind,
			size,
			mtime,
		});
	}
}

export function clearAssetCache(): void {
	cached = null;
}
