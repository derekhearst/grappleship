import { mkdir } from "node:fs/promises";
import { dirname, relative, resolve } from "node:path";
import { z } from "zod";
import {
	cloneSubtreeWithNewGuids,
	getPrefabRoot,
	listPrefabFiles,
	prefabAbsolutePath,
	readPrefab,
	statPrefab,
	writePrefab,
} from "../prefabs/index";
import { defaultConfig, loadCatalog } from "../schema/loader";
import { findGameObjectByGuid, MutateError } from "../scene/mutate";
import { findScenePath, readScene } from "../scene/read";
import { validateScene } from "../scene/validate";
import { newGuid, writeScene } from "../scene/write";
import type { GameObjectNode, SceneFile } from "../scene/types";
import type { ToolDef } from "./registry";

export const listPrefabs: ToolDef = {
	name: "list_prefabs",
	description: "List every .prefab file under grappleship/Assets/. Paths are relative to the assets root.",
	inputSchema: { type: "object", properties: {}, additionalProperties: false },
	run: async (_args, cfg) => {
		const files = await listPrefabFiles(cfg.assetsRoot);
		const out = await Promise.all(
			files.map(async (f) => {
				const meta = await statPrefab(f);
				return {
					path: relative(cfg.assetsRoot, f).replace(/\\/g, "/"),
					size: meta?.size,
					mtime: meta?.mtime,
				};
			}),
		);
		return { count: out.length, prefabs: out };
	},
};

export const readPrefabTool: ToolDef = {
	name: "read_prefab",
	description: "Read a prefab file and return its full data. Path is relative to grappleship/Assets/.",
	inputSchema: {
		type: "object",
		properties: { path: { type: "string" } },
		required: ["path"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z.object({ path: z.string() }).parse(args);
		const full = prefabAbsolutePath(cfg.assetsRoot, parsed.path);
		const data = await readPrefab(full);
		const root = getPrefabRoot(data);
		return {
			path: relative(cfg.assetsRoot, full).replace(/\\/g, "/"),
			root_name: root?.Name,
			root_guid: root?.__guid,
			data,
		};
	},
};

export const instantiatePrefab: ToolDef = {
	name: "instantiate_prefab",
	description:
		"Instantiate a prefab into a scene. Generates fresh GUIDs throughout, remaps internal references, and writes the modified scene atomically. External references (pointing outside the prefab tree) are reported but left alone.",
	inputSchema: {
		type: "object",
		properties: {
			prefab_path: { type: "string", description: "Path relative to grappleship/Assets/." },
			scene: { type: "string" },
			position: { type: "string", description: '"x,y,z" override; defaults to prefab\'s root position.' },
			rotation: { type: "string" },
			scale: { type: "string" },
			parent: {
				type: "object",
				properties: {
					guid: { type: "string" },
					name_path: { type: "string" },
				},
				additionalProperties: false,
			},
			name_override: { type: "string" },
		},
		required: ["prefab_path", "scene"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z
			.object({
				prefab_path: z.string(),
				scene: z.string(),
				position: z.string().optional(),
				rotation: z.string().optional(),
				scale: z.string().optional(),
				parent: z
					.object({ guid: z.string().optional(), name_path: z.string().optional() })
					.optional(),
				name_override: z.string().optional(),
			})
			.parse(args);

		const prefabFull = prefabAbsolutePath(cfg.assetsRoot, parsed.prefab_path);
		const prefab = await readPrefab(prefabFull);
		const root = getPrefabRoot(prefab);
		if (!root) {
			throw new MutateError("bad_prefab", `prefab has no root GameObject: ${parsed.prefab_path}`);
		}

		const { clone, external_refs } = cloneSubtreeWithNewGuids(root);
		if (parsed.name_override) clone.Name = parsed.name_override;
		if (parsed.position) clone.Position = parsed.position;
		if (parsed.rotation) clone.Rotation = parsed.rotation;
		if (parsed.scale) clone.Scale = parsed.scale;

		const scenePath = await findScenePath(cfg.scenesRoot, parsed.scene);
		if (!scenePath) throw new MutateError("scene_not_found", `scene not found: ${parsed.scene}`);
		const scene = await readScene(scenePath);

		if (parsed.parent && (parsed.parent.guid || parsed.parent.name_path)) {
			const parent = parsed.parent.guid
				? findGameObjectByGuid(scene, parsed.parent.guid)
				: findByPath(scene, parsed.parent.name_path!);
			if (!parent) throw new MutateError("not_found", `parent not found`);
			parent.Children = parent.Children ?? [];
			parent.Children.push(clone);
		} else {
			scene.GameObjects = scene.GameObjects ?? [];
			scene.GameObjects.push(clone);
		}

		const catalog = await loadCatalog(defaultConfig(cfg.repoRoot));
		const errors = validateScene(scene, catalog);
		const blocking = errors.filter((e) => e.severity === "error");
		if (blocking.length) {
			throw new MutateError(
				"validation_failed",
				`refusing to write — ${blocking.length} validation error(s) after instantiation:\n` +
					blocking.slice(0, 10).map((e) => `  ${e.path}: ${e.message}`).join("\n"),
			);
		}
		await writeScene(scenePath, scene);
		return {
			scene_path: relative(cfg.repoRoot, scenePath).replace(/\\/g, "/"),
			gameobject_guid: clone.__guid,
			external_refs,
		};
	},
};

export const createPrefabFromGameObject: ToolDef = {
	name: "create_prefab_from_gameobject",
	description:
		"Extract a GameObject (and its descendants) from a scene and write it as a new .prefab file. The original GameObject in the scene is left in place. References that point outside the extracted subtree are reported.",
	inputSchema: {
		type: "object",
		properties: {
			scene: { type: "string" },
			gameobject_guid: { type: "string" },
			prefab_path: { type: "string", description: "Output path relative to grappleship/Assets/, with or without .prefab extension." },
			overwrite: { type: "boolean", default: false },
		},
		required: ["scene", "gameobject_guid", "prefab_path"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z
			.object({
				scene: z.string(),
				gameobject_guid: z.string(),
				prefab_path: z.string(),
				overwrite: z.boolean().default(false),
			})
			.parse(args);

		const scenePath = await findScenePath(cfg.scenesRoot, parsed.scene);
		if (!scenePath) throw new MutateError("scene_not_found", `scene not found: ${parsed.scene}`);
		const scene = await readScene(scenePath);
		const go = findGameObjectByGuid(scene, parsed.gameobject_guid);
		if (!go) throw new MutateError("not_found", `GameObject not found: ${parsed.gameobject_guid}`);

		const { clone, external_refs } = cloneSubtreeWithNewGuids(go);

		const outFull = prefabAbsolutePath(cfg.assetsRoot, parsed.prefab_path);
		const existing = await statPrefab(outFull);
		if (existing && !parsed.overwrite) {
			throw new MutateError(
				"exists",
				`prefab already exists at ${parsed.prefab_path}. Pass overwrite=true to replace.`,
			);
		}
		await mkdir(dirname(outFull), { recursive: true });

		const prefab = {
			__guid: newGuid(),
			__version: 2,
			Object: clone,
		};
		await writePrefab(outFull, prefab);

		return {
			prefab_path: relative(cfg.assetsRoot, outFull).replace(/\\/g, "/"),
			external_refs,
		};
	},
};

function findByPath(scene: SceneFile, namePath: string): GameObjectNode | null {
	const wanted = namePath.split(/[/>]/).map((s) => s.trim()).filter(Boolean);
	function walk(nodes: GameObjectNode[], path: string[]): GameObjectNode | null {
		for (const go of nodes) {
			const seg = [...path, go.Name ?? go.__guid];
			if (seg.length === wanted.length && seg.every((s, i) => s === wanted[i])) return go;
			if (go.Children) {
				const hit = walk(go.Children, seg);
				if (hit) return hit;
			}
		}
		return null;
	}
	return walk(scene.GameObjects ?? [], []);
}

void resolve;
