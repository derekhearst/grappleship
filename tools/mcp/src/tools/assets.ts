import { stat } from "node:fs/promises";
import { resolve } from "node:path";
import { z } from "zod";
import { buildAssetIndex, clearAssetCache, type AssetKind } from "../assets/index";
import type { ToolDef } from "./registry";

const KIND_VALUES: AssetKind[] = [
	"model",
	"material",
	"texture",
	"sound",
	"sound_event",
	"scene",
	"prefab",
	"shader",
	"animation",
	"particle",
	"ui",
	"other",
];

export const listAssets: ToolDef = {
	name: "list_assets",
	description:
		"List assets under grappleship/Assets/. Optionally filter by kind (model, material, texture, sound, scene, prefab, shader, animation, particle, ui, other) and a path substring. Use this to discover what models/materials/etc exist before adding components that reference them.",
	inputSchema: {
		type: "object",
		properties: {
			kind: { type: "string", enum: KIND_VALUES },
			path_contains: { type: "string", description: "Optional substring filter on the path." },
			limit: { type: "integer", minimum: 1, maximum: 5000, default: 500 },
		},
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z
			.object({
				kind: z.enum(KIND_VALUES as [string, ...string[]]).optional(),
				path_contains: z.string().optional(),
				limit: z.number().int().min(1).max(5000).default(500),
			})
			.parse(args);
		const idx = await buildAssetIndex(cfg.assetsRoot);
		const needle = parsed.path_contains?.toLowerCase();
		const filtered = idx.entries
			.filter((e) => !parsed.kind || e.kind === parsed.kind)
			.filter((e) => !needle || e.path.toLowerCase().includes(needle))
			.slice(0, parsed.limit);
		return {
			total_in_repo: idx.entries.length,
			returned: filtered.length,
			assets: filtered.map((e) => ({ path: e.path, kind: e.kind, size: e.size })),
		};
	},
};

export const findAsset: ToolDef = {
	name: "find_asset",
	description:
		"Fuzzy-find assets by name fragment. Returns a ranked list — exact basename matches first, then path-contains. Use when you know roughly what you want but not the exact path.",
	inputSchema: {
		type: "object",
		properties: {
			query: { type: "string" },
			kind: { type: "string", enum: KIND_VALUES },
			limit: { type: "integer", minimum: 1, maximum: 200, default: 25 },
		},
		required: ["query"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z
			.object({
				query: z.string(),
				kind: z.enum(KIND_VALUES as [string, ...string[]]).optional(),
				limit: z.number().int().min(1).max(200).default(25),
			})
			.parse(args);
		const idx = await buildAssetIndex(cfg.assetsRoot);
		const q = parsed.query.toLowerCase();
		const candidates = idx.entries.filter((e) => !parsed.kind || e.kind === parsed.kind);
		const ranked = candidates
			.map((e) => {
				const path = e.path.toLowerCase();
				const base = path.split("/").pop() ?? path;
				let score = 0;
				if (base === q) score = 100;
				else if (base.replace(/\.[^.]+$/, "") === q) score = 95;
				else if (base.startsWith(q)) score = 80;
				else if (base.includes(q)) score = 60;
				else if (path.includes(q)) score = 40;
				return { entry: e, score };
			})
			.filter((x) => x.score > 0)
			.sort((a, b) => b.score - a.score)
			.slice(0, parsed.limit);
		return {
			query: parsed.query,
			matches: ranked.map((x) => ({ path: x.entry.path, kind: x.entry.kind, score: x.score })),
		};
	},
};

export const describeAsset: ToolDef = {
	name: "describe_asset",
	description: "Return metadata for an asset path (kind, size, mtime, exists).",
	inputSchema: {
		type: "object",
		properties: {
			path: { type: "string", description: "Path relative to grappleship/Assets/, e.g. 'materials/sand floor.vmat'." },
		},
		required: ["path"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z.object({ path: z.string() }).parse(args);
		const idx = await buildAssetIndex(cfg.assetsRoot);
		const normalized = parsed.path.replace(/\\/g, "/").replace(/^\/+/, "");
		const hit = idx.entries.find((e) => e.path === normalized);
		if (!hit) return { exists: false, path: normalized };
		return { exists: true, ...hit };
	},
};

export const validateAssetPath: ToolDef = {
	name: "validate_asset_path",
	description:
		"Check that an asset path exists and (optionally) matches an expected kind. Useful before setting a Model/Material/Texture property on a component.",
	inputSchema: {
		type: "object",
		properties: {
			path: { type: "string" },
			expected_kind: { type: "string", enum: KIND_VALUES },
		},
		required: ["path"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z
			.object({
				path: z.string(),
				expected_kind: z.enum(KIND_VALUES as [string, ...string[]]).optional(),
			})
			.parse(args);
		const normalized = parsed.path.replace(/\\/g, "/").replace(/^\/+/, "");
		const full = resolve(cfg.assetsRoot, normalized);
		try {
			await stat(full);
		} catch {
			return { ok: false, error: "file does not exist", path: normalized };
		}
		const idx = await buildAssetIndex(cfg.assetsRoot);
		const hit = idx.entries.find((e) => e.path === normalized);
		if (parsed.expected_kind && hit && hit.kind !== parsed.expected_kind) {
			return { ok: false, error: `expected kind ${parsed.expected_kind}, got ${hit.kind}`, path: normalized };
		}
		return { ok: true, path: normalized, kind: hit?.kind };
	},
};

void clearAssetCache;
