import { z } from "zod";
import { callBridge } from "../bridge/transport";
import type { ToolDef } from "./registry";

/**
 * Terrain & material bridge tools. These are thin wrappers around the
 * GrappleShip editor bridge actions defined in
 * grappleship/Editor/TerrainGenerator.cs.
 *
 * The original `create_terrain_material` action had a known cache-drift
 * problem (Activator.CreateInstance + reflection-set + SaveToDisk left the
 * engine's in-memory TerrainMaterial GameResource stuck on the empty
 * placeholder JSON it had read at registration time). The V2 tool below
 * writes the JSON to disk *first* and only then registers/refreshes — so
 * the engine's first parse sees populated fields, and the texture compiler
 * stops emitting "Error reading texture \"\" for \"color\"" FAILs.
 */

export const inspectTmatTool: ToolDef = {
	name: "inspect_tmat",
	description:
		"Diagnostic. Returns both the on-disk JSON and the live in-memory GameResource property values for a .tmat (or any GameResource asset). If they differ, the engine has stale cached state and the compiler will emit a broken artifact. Use this when terrain renders as engine-magenta despite correct .tmat content on disk.",
	inputSchema: {
		type: "object",
		properties: {
			path: { type: "string", description: "Project-relative asset path, e.g. 'maps/dunes_sand.tmat'." },
		},
		required: ["path"],
		additionalProperties: false,
	},
	run: async (args) => {
		const parsed = z.object({ path: z.string().min(1) }).parse(args);
		return await callBridge("inspect_tmat", { path: parsed.path }, { timeoutMs: 8000 });
	},
};

export const reloadAssetTool: ToolDef = {
	name: "reload_asset",
	description:
		"Force a registered asset to drop its cached GameResource and reread from disk. Use after editing a resource source file outside the engine's I/O path. Internally calls Editor.Asset.SetInMemoryReplacement(diskJson) followed by ClearInMemoryReplacement() — pushes the disk JSON back through the parser, refreshing the cache.",
	inputSchema: {
		type: "object",
		properties: {
			path: { type: "string", description: "Project-relative asset path, e.g. 'maps/dunes_sand.tmat'." },
		},
		required: ["path"],
		additionalProperties: false,
	},
	run: async (args) => {
		const parsed = z.object({ path: z.string().min(1) }).parse(args);
		return await callBridge("reload_asset", { path: parsed.path }, { timeoutMs: 8000 });
	},
};

export const compileAssetVerifiedTool: ToolDef = {
	name: "compile_asset_verified",
	description:
		"Force a recompile and surface any TextureCompiler [FAIL] entries from sbox-dev.log that mention this asset's vtex children. Returns ok=false if the compile call returned true but the log contains fresh FAILs (a common silent-failure mode where the .tmat_c is emitted with empty texture inputs). Prefer this over compile_asset for terrain materials.",
	inputSchema: {
		type: "object",
		properties: {
			path: { type: "string", description: "Project-relative asset path." },
		},
		required: ["path"],
		additionalProperties: false,
	},
	run: async (args) => {
		const parsed = z.object({ path: z.string().min(1) }).parse(args);
		return await callBridge("compile_asset_verified", { path: parsed.path }, { timeoutMs: 30000 });
	},
};

export const createTerrainMaterialV2Tool: ToolDef = {
	name: "create_terrain_material_v2",
	description:
		"Cache-safe TerrainMaterial creator. Writes the full .tmat JSON to disk first, then registers with AssetSystem (so the first GameResource parse sees populated fields), then force-reloads the cached resource if the asset was already registered, then verifies in-memory AlbedoImage matches what was written. Returns ok=true only if in-memory state matches disk; ok=false flags cache drift you should investigate before compiling. Pair with compile_asset_verified to catch silent texture-compile failures.",
	inputSchema: {
		type: "object",
		properties: {
			out_path: { type: "string", description: "Project-relative .tmat output path, e.g. 'maps/dunes_sand.tmat'.", default: "maps/dunes_sand.tmat" },
			albedo: { type: "string", description: "Project-relative path to the basecolor image (.png/.jpg/.tga). Required." },
			roughness: { type: "string", description: "Optional. Defaults to 'materials/default/default_rough_s1import.tga'." },
			normal: { type: "string", description: "Optional. Defaults to 'materials/default/default_normal.tga'." },
			height: { type: "string", description: "Optional. Defaults to 'materials/default/default_ao.tga' (flat). Used for height-blend between layers." },
			ao: { type: "string", description: "Optional. Defaults to 'materials/default/default_ao.tga'." },
			uv_scale: { type: "number", description: "Texture tile density multiplier. 1.0 = engine default; lower = larger texels (less visible tiling)." },
		},
		required: ["albedo"],
		additionalProperties: false,
	},
	run: async (args) => {
		const parsed = z.object({
			out_path: z.string().optional(),
			albedo: z.string().min(1),
			roughness: z.string().optional(),
			normal: z.string().optional(),
			height: z.string().optional(),
			ao: z.string().optional(),
			uv_scale: z.number().optional(),
		}).parse(args);
		return await callBridge("create_terrain_material_v2", parsed, { timeoutMs: 15000 });
	},
};
