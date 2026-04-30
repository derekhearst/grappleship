import { readdir, readFile, stat, writeFile } from "node:fs/promises";
import { dirname, relative, resolve } from "node:path";
import type { ComponentNode, GameObjectNode } from "../scene/types";
import { newGuid } from "../scene/write";

export interface PrefabFile {
	__guid?: string;
	__version?: number;
	GameObjects?: GameObjectNode[]; // some prefabs use this top-level shape
	Object?: GameObjectNode; // others use a single Object key
	[k: string]: unknown;
}

export async function listPrefabFiles(assetsRoot: string): Promise<string[]> {
	const out: string[] = [];
	await walk(assetsRoot, out);
	return out;
}

async function walk(dir: string, out: string[]): Promise<void> {
	let ents;
	try {
		ents = await readdir(dir, { withFileTypes: true });
	} catch {
		return;
	}
	for (const ent of ents) {
		const p = resolve(dir, ent.name);
		if (ent.isDirectory()) {
			await walk(p, out);
		} else if (ent.isFile() && ent.name.endsWith(".prefab")) {
			out.push(p);
		}
	}
}

export async function readPrefab(path: string): Promise<PrefabFile> {
	const text = (await readFile(path, "utf8")).replace(/^﻿/, "");
	return JSON.parse(text) as PrefabFile;
}

export async function writePrefab(path: string, data: PrefabFile): Promise<void> {
	const json = JSON.stringify(data, null, "\t");
	await writeFile(path, json, { encoding: "utf8" });
}

export function getPrefabRoot(prefab: PrefabFile): GameObjectNode | null {
	if (prefab.Object) return prefab.Object;
	if (prefab.GameObjects && prefab.GameObjects.length > 0) return prefab.GameObjects[0]!;
	return null;
}

/**
 * Deep-clone a GameObject subtree, generating fresh GUIDs everywhere and
 * remapping internal component_ref / gameobject_ref values to point at the
 * new GUIDs. References that point outside the subtree are left as-is and
 * reported in `external_refs`.
 */
export function cloneSubtreeWithNewGuids(root: GameObjectNode): {
	clone: GameObjectNode;
	external_refs: Array<{ where: string; original_guid: string }>;
} {
	const guidMap = new Map<string, string>();

	const collect = (go: GameObjectNode): void => {
		guidMap.set(go.__guid, newGuid());
		for (const c of go.Components ?? []) {
			guidMap.set(c.__guid, newGuid());
		}
		for (const child of go.Children ?? []) collect(child);
	};
	collect(root);

	const externalRefs: Array<{ where: string; original_guid: string }> = [];

	const remapValue = (val: unknown, where: string): unknown => {
		if (val === null) return null;
		if (Array.isArray(val)) return val.map((v, i) => remapValue(v, `${where}[${i}]`));
		if (typeof val !== "object") return val;
		const obj = val as Record<string, unknown>;
		if (obj._type === "component" || obj._type === "gameobject") {
			const result: Record<string, unknown> = { ...obj };
			if (typeof obj.go === "string") {
				const mapped = guidMap.get(obj.go);
				if (mapped) result.go = mapped;
				else externalRefs.push({ where, original_guid: obj.go });
			}
			if (typeof obj.component_id === "string") {
				const mapped = guidMap.get(obj.component_id);
				if (mapped) result.component_id = mapped;
				else externalRefs.push({ where: `${where}.component_id`, original_guid: obj.component_id });
			}
			return result;
		}
		const remapped: Record<string, unknown> = {};
		for (const [k, v] of Object.entries(obj)) {
			remapped[k] = remapValue(v, `${where}.${k}`);
		}
		return remapped;
	};

	const cloneComponent = (c: ComponentNode, ownerName: string): ComponentNode => {
		const out: ComponentNode = { __type: c.__type, __guid: guidMap.get(c.__guid)! };
		for (const [k, v] of Object.entries(c)) {
			if (k === "__guid" || k === "__type") continue;
			out[k] = remapValue(v, `${ownerName}.${c.__type}.${k}`);
		}
		return out;
	};

	const cloneGo = (go: GameObjectNode): GameObjectNode => {
		const newName = go.Name ?? go.__guid;
		const out: GameObjectNode = { __guid: guidMap.get(go.__guid)! };
		for (const [k, v] of Object.entries(go)) {
			if (k === "__guid" || k === "Components" || k === "Children") continue;
			out[k] = remapValue(v, `${newName}.${k}`);
		}
		out.Components = (go.Components ?? []).map((c) => cloneComponent(c, newName));
		out.Children = (go.Children ?? []).map(cloneGo);
		return out;
	};

	return { clone: cloneGo(root), external_refs: externalRefs };
}

export async function statPrefab(path: string): Promise<{ size: number; mtime: number } | null> {
	try {
		const s = await stat(path);
		return { size: s.size, mtime: s.mtimeMs };
	} catch {
		return null;
	}
}

export function relativeAssetPath(assetsRoot: string, fullPath: string): string {
	return relative(assetsRoot, fullPath).replace(/\\/g, "/");
}

export function ensurePrefabExtension(p: string): string {
	return p.endsWith(".prefab") ? p : `${p}.prefab`;
}

export function prefabAbsolutePath(assetsRoot: string, relPath: string): string {
	const norm = relPath.replace(/\\/g, "/").replace(/^\/+/, "");
	return resolve(assetsRoot, ensurePrefabExtension(norm));
}

export { dirname };
