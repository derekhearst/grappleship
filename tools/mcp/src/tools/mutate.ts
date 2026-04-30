import { relative } from "node:path";
import { z } from "zod";
import { defaultConfig, loadCatalog } from "../schema/loader";
import {
	addComponent,
	createGameObject,
	deleteGameObject,
	findComponentByGuid,
	MutateError,
	removeComponent,
	reparentGameObject,
	resolveGameObject,
	setProperty,
	setPropertiesBulk,
	setTransform,
} from "../scene/mutate";
import { findScenePath, readScene } from "../scene/read";
import { validateScene } from "../scene/validate";
import { writeScene } from "../scene/write";
import type { ServerConfig } from "../config";
import type { SceneFile } from "../scene/types";
import type { ToolDef } from "./registry";

const refSchema = z
	.object({ guid: z.string().optional(), name_path: z.string().optional() })
	.refine((r) => r.guid || r.name_path, { message: "supply guid or name_path" });

async function loadSceneOrError(cfg: ServerConfig, hint: string): Promise<{ path: string; scene: SceneFile }> {
	const path = await findScenePath(cfg.scenesRoot, hint);
	if (!path) throw new MutateError("scene_not_found", `scene not found: ${hint}`);
	return { path, scene: await readScene(path) };
}

async function commit(cfg: ServerConfig, path: string, scene: SceneFile): Promise<{ scene_path: string; validation_errors: number }> {
	const catalog = await loadCatalog(defaultConfig(cfg.repoRoot));
	const errors = validateScene(scene, catalog);
	const blocking = errors.filter((e) => e.severity === "error");
	if (blocking.length) {
		throw new MutateError(
			"validation_failed",
			`refusing to write — validation found ${blocking.length} error(s):\n` +
				blocking.slice(0, 10).map((e) => `  ${e.path}: ${e.message}`).join("\n"),
		);
	}
	await writeScene(path, scene);
	return {
		scene_path: relative(cfg.repoRoot, path).replace(/\\/g, "/"),
		validation_errors: errors.length,
	};
}

export const createGameObjectTool: ToolDef = {
	name: "create_gameobject",
	description:
		"Create a new GameObject in a scene. Optionally nest under a parent. Returns the new GUID. The scene is re-validated and written atomically; if validation fails, no write happens.",
	inputSchema: {
		type: "object",
		properties: {
			scene: { type: "string" },
			name: { type: "string" },
			position: { type: "string", description: '"x,y,z"' },
			rotation: { type: "string", description: '"x,y,z,w" quaternion' },
			scale: { type: "string", description: '"x,y,z"' },
			tags: { type: "string", description: "comma-separated" },
			enabled: { type: "boolean", default: true },
			parent: {
				type: "object",
				properties: {
					guid: { type: "string" },
					name_path: { type: "string" },
				},
				additionalProperties: false,
			},
		},
		required: ["scene", "name"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z
			.object({
				scene: z.string(),
				name: z.string(),
				position: z.string().optional(),
				rotation: z.string().optional(),
				scale: z.string().optional(),
				tags: z.string().optional(),
				enabled: z.boolean().optional(),
				parent: refSchema.optional(),
			})
			.parse(args);
		const { path, scene } = await loadSceneOrError(cfg, parsed.scene);
		const node = createGameObject(scene, parsed);
		const result = await commit(cfg, path, scene);
		return { ...result, gameobject_guid: node.__guid };
	},
};

export const deleteGameObjectTool: ToolDef = {
	name: "delete_gameobject",
	description: "Remove a GameObject (and all descendants) from a scene by GUID.",
	inputSchema: {
		type: "object",
		properties: {
			scene: { type: "string" },
			guid: { type: "string" },
		},
		required: ["scene", "guid"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z.object({ scene: z.string(), guid: z.string() }).parse(args);
		const { path, scene } = await loadSceneOrError(cfg, parsed.scene);
		deleteGameObject(scene, parsed.guid);
		return await commit(cfg, path, scene);
	},
};

export const reparentGameObjectTool: ToolDef = {
	name: "reparent_gameobject",
	description: "Move a GameObject under a different parent (or to root if new_parent is omitted). Local transform is preserved as-is.",
	inputSchema: {
		type: "object",
		properties: {
			scene: { type: "string" },
			child_guid: { type: "string" },
			new_parent: {
				type: "object",
				properties: {
					guid: { type: "string" },
					name_path: { type: "string" },
				},
				additionalProperties: false,
			},
		},
		required: ["scene", "child_guid"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z
			.object({
				scene: z.string(),
				child_guid: z.string(),
				new_parent: refSchema.optional(),
			})
			.parse(args);
		const { path, scene } = await loadSceneOrError(cfg, parsed.scene);
		reparentGameObject(scene, { child_guid: parsed.child_guid, new_parent: parsed.new_parent });
		return await commit(cfg, path, scene);
	},
};

export const setTransformTool: ToolDef = {
	name: "set_transform",
	description: "Update Position / Rotation / Scale on a GameObject. Pass only the fields you want to change.",
	inputSchema: {
		type: "object",
		properties: {
			scene: { type: "string" },
			guid: { type: "string" },
			position: { type: "string" },
			rotation: { type: "string" },
			scale: { type: "string" },
		},
		required: ["scene", "guid"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z
			.object({
				scene: z.string(),
				guid: z.string(),
				position: z.string().optional(),
				rotation: z.string().optional(),
				scale: z.string().optional(),
			})
			.parse(args);
		const { path, scene } = await loadSceneOrError(cfg, parsed.scene);
		setTransform(scene, parsed);
		return await commit(cfg, path, scene);
	},
};

export const addComponentTool: ToolDef = {
	name: "add_component",
	description:
		"Add a component to a GameObject. ALWAYS call describe_component first to know what initial_properties are valid. Returns the new component GUID.",
	inputSchema: {
		type: "object",
		properties: {
			scene: { type: "string" },
			target: {
				type: "object",
				properties: {
					guid: { type: "string" },
					name_path: { type: "string" },
				},
				additionalProperties: false,
			},
			component_type: { type: "string", description: "Fully-qualified type name, e.g. 'Sandbox.ModelRenderer'" },
			initial_properties: {
				type: "object",
				description: "Optional initial property values. Validated against schema.",
				additionalProperties: true,
			},
		},
		required: ["scene", "target", "component_type"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z
			.object({
				scene: z.string(),
				target: refSchema,
				component_type: z.string(),
				initial_properties: z.record(z.unknown()).optional(),
			})
			.parse(args);
		const { path, scene } = await loadSceneOrError(cfg, parsed.scene);
		const catalog = await loadCatalog(defaultConfig(cfg.repoRoot));
		// Touch resolveGameObject early for a clean error before mutating.
		resolveGameObject(scene, parsed.target);
		const node = addComponent(scene, catalog, {
			target: parsed.target,
			component_type: parsed.component_type,
			initial_properties: parsed.initial_properties,
		});
		const result = await commit(cfg, path, scene);
		return { ...result, component_guid: node.__guid, component_type: node.__type };
	},
};

export const removeComponentTool: ToolDef = {
	name: "remove_component",
	description: "Remove a component from its GameObject by component GUID.",
	inputSchema: {
		type: "object",
		properties: {
			scene: { type: "string" },
			component_guid: { type: "string" },
		},
		required: ["scene", "component_guid"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z.object({ scene: z.string(), component_guid: z.string() }).parse(args);
		const { path, scene } = await loadSceneOrError(cfg, parsed.scene);
		removeComponent(scene, parsed.component_guid);
		return await commit(cfg, path, scene);
	},
};

export const setPropertyTool: ToolDef = {
	name: "set_property",
	description:
		"Set a single property on a component. Validated against the schema; the scene file is only written if the result validates clean.",
	inputSchema: {
		type: "object",
		properties: {
			scene: { type: "string" },
			component_guid: { type: "string" },
			property: { type: "string" },
			value: { description: "Any JSON value matching the property's schema type." },
		},
		required: ["scene", "component_guid", "property", "value"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z
			.object({
				scene: z.string(),
				component_guid: z.string(),
				property: z.string(),
				value: z.unknown(),
			})
			.parse(args);
		const { path, scene } = await loadSceneOrError(cfg, parsed.scene);
		const catalog = await loadCatalog(defaultConfig(cfg.repoRoot));
		setProperty(scene, catalog, {
			component_guid: parsed.component_guid,
			property: parsed.property,
			value: parsed.value,
		});
		return await commit(cfg, path, scene);
	},
};

export const setPropertiesBulkTool: ToolDef = {
	name: "set_properties_bulk",
	description: "Set multiple properties on a component atomically. Either all apply or none do.",
	inputSchema: {
		type: "object",
		properties: {
			scene: { type: "string" },
			component_guid: { type: "string" },
			properties: {
				type: "object",
				additionalProperties: true,
			},
		},
		required: ["scene", "component_guid", "properties"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z
			.object({
				scene: z.string(),
				component_guid: z.string(),
				properties: z.record(z.unknown()),
			})
			.parse(args);
		const { path, scene } = await loadSceneOrError(cfg, parsed.scene);
		const catalog = await loadCatalog(defaultConfig(cfg.repoRoot));
		setPropertiesBulk(scene, catalog, {
			component_guid: parsed.component_guid,
			properties: parsed.properties,
		});
		return await commit(cfg, path, scene);
	},
};

// suppress unused
void findComponentByGuid;
