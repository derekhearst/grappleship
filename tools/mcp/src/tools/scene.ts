import { relative } from "node:path";
import { z } from "zod";
import { defaultConfig, loadCatalog } from "../schema/loader";
import { findGameObject, findScenePath, listSceneFiles, readScene, walkGameObjects } from "../scene/read";
import { validateScene } from "../scene/validate";
import type { ToolDef } from "./registry";

export const listScenes: ToolDef = {
	name: "list_scenes",
	description: "List every .scene file in the project. Paths are relative to repo root.",
	inputSchema: { type: "object", properties: {}, additionalProperties: false },
	run: async (_args, cfg) => {
		const files = await listSceneFiles(cfg.scenesRoot);
		return {
			count: files.length,
			scenes: files.map((p) => relative(cfg.repoRoot, p).replace(/\\/g, "/")),
		};
	},
};

export const readSceneTool: ToolDef = {
	name: "read_scene",
	description:
		"Read a scene file and return a flattened summary: every GameObject with its name path, transform, tags, and component type list. For full property values use get_gameobject.",
	inputSchema: {
		type: "object",
		properties: {
			scene: { type: "string", description: "Scene name (e.g. 'MainMap'), filename ('MainMap.scene'), or relative path." },
		},
		required: ["scene"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z.object({ scene: z.string() }).parse(args);
		const path = await findScenePath(cfg.scenesRoot, parsed.scene);
		if (!path) return { error: `scene not found: ${parsed.scene}` };
		const scene = await readScene(path);
		const items: Array<unknown> = [];
		for (const { go, path: namePath } of walkGameObjects(scene)) {
			items.push({
				name_path: namePath.join(" / "),
				guid: go.__guid,
				position: go.Position,
				rotation: go.Rotation,
				scale: go.Scale,
				tags: go.Tags,
				enabled: go.Enabled !== false,
				components: (go.Components ?? []).map((c) => ({
					type: c.__type,
					guid: c.__guid,
					enabled: c.__enabled !== false,
				})),
			});
		}
		return {
			scene_path: relative(cfg.repoRoot, path).replace(/\\/g, "/"),
			gameobject_count: items.length,
			gameobjects: items,
		};
	},
};

export const getGameObject: ToolDef = {
	name: "get_gameobject",
	description: "Return the full data for one GameObject (all components with all properties). Look up by GUID or by name path.",
	inputSchema: {
		type: "object",
		properties: {
			scene: { type: "string" },
			guid: { type: "string", description: "GameObject __guid" },
			name_path: { type: "string", description: "Slash-separated name path, e.g. 'Player/Camera'" },
		},
		required: ["scene"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z
			.object({
				scene: z.string(),
				guid: z.string().optional(),
				name_path: z.string().optional(),
			})
			.parse(args);
		if (!parsed.guid && !parsed.name_path) {
			return { error: "supply either guid or name_path" };
		}
		const path = await findScenePath(cfg.scenesRoot, parsed.scene);
		if (!path) return { error: `scene not found: ${parsed.scene}` };
		const scene = await readScene(path);
		const target = findGameObject(scene, (go, p) => {
			if (parsed.guid && go.__guid === parsed.guid) return true;
			if (parsed.name_path) {
				const wanted = parsed.name_path.split(/[/>]/).map((s) => s.trim()).filter(Boolean);
				if (p.length === wanted.length && p.every((seg, i) => seg === wanted[i])) return true;
			}
			return false;
		});
		if (!target) return { error: "GameObject not found" };
		return { name_path: target.path.join(" / "), gameobject: target.go };
	},
};

export const validateSceneTool: ToolDef = {
	name: "validate_scene",
	description:
		"Validate a scene against the component schema. Returns a list of errors with paths like 'Player > GrappleHook.ReelSpeed: value out of range'. Run this after every scene edit.",
	inputSchema: {
		type: "object",
		properties: {
			scene: { type: "string" },
		},
		required: ["scene"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z.object({ scene: z.string() }).parse(args);
		const path = await findScenePath(cfg.scenesRoot, parsed.scene);
		if (!path) return { error: `scene not found: ${parsed.scene}` };
		const scene = await readScene(path);
		const catalog = await loadCatalog(defaultConfig(cfg.repoRoot));
		const errors = validateScene(scene, catalog);
		const errCount = errors.filter((e) => e.severity === "error").length;
		const warnCount = errors.filter((e) => e.severity === "warning").length;
		return {
			scene_path: relative(cfg.repoRoot, path).replace(/\\/g, "/"),
			error_count: errCount,
			warning_count: warnCount,
			ok: errCount === 0,
			errors,
		};
	},
};
