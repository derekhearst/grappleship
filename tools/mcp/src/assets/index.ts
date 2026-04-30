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

export type AssetSource = "project" | "engine" | "workshop" | "cloud" | "unknown";

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
	".prefab_c",
	".tmp",
	".bak",
]);
const SKIP_DIRS = new Set([".sbox-cache", "compiled", ".vs", "obj", "bin"]);

export interface AssetEntry {
	path: string; // forward-slash, relative to its source root
	kind: AssetKind;
	source: AssetSource;
	size?: number;
	mtime?: number;
}

/**
 * Walks the project's grappleship/Assets/ tree only. Engine + workshop +
 * cloud-mounted assets come from the live editor bridge — see
 * tools/mcp/src/tools/assets.ts.
 */
export async function walkProjectAssets(assetsRoot: string): Promise<AssetEntry[]> {
	try {
		await stat(assetsRoot);
	} catch {
		return [];
	}
	const out: AssetEntry[] = [];
	await walk(assetsRoot, assetsRoot, out);
	return out;
}

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
			source: "project",
			size,
			mtime,
		});
	}
}

/** Map a kind string (engine-reported asset type or extension) to our enum. */
export function classifyKind(kindOrExt: string | undefined, fallbackPath?: string): AssetKind {
	if (kindOrExt) {
		const lower = kindOrExt.toLowerCase().replace(/^\./, "");
		if (lower === "vmdl" || lower === "fbx" || lower.includes("model")) return "model";
		if (lower === "vmat" || lower.includes("material")) return "material";
		if (lower === "vtex" || lower === "png" || lower === "jpg" || lower === "jpeg" || lower === "tga" || lower.includes("texture")) return "texture";
		if (lower === "vsndevts" || (lower.includes("sound") && lower.includes("event"))) return "sound_event";
		if (lower === "vsnd" || lower === "wav" || lower === "mp3" || lower === "ogg" || lower.includes("sound")) return "sound";
		if (lower === "scene") return "scene";
		if (lower === "prefab") return "prefab";
		if (lower === "shader" || lower === "vfx") return "shader";
		if (lower === "vanmgrph" || lower === "vanm" || lower.includes("anim")) return "animation";
		if (lower === "vpcf" || lower.includes("particle")) return "particle";
		if (lower === "razor" || lower === "scss") return "ui";
	}
	if (fallbackPath) {
		const ext = fallbackPath.lastIndexOf(".") >= 0 ? fallbackPath.slice(fallbackPath.lastIndexOf(".")).toLowerCase() : "";
		if (EXT_MAP[ext]) return EXT_MAP[ext]!;
	}
	return "other";
}
