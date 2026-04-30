import { stat } from "node:fs/promises";
import { resolve } from "node:path";
import { z } from "zod";
import { classifyKind, walkProjectAssets, type AssetEntry, type AssetKind } from "../assets/index";
import { BridgeError, callBridge } from "../bridge/transport";
import type { ServerConfig } from "../config";
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

interface BridgeAssetHit {
	path: string;
	name?: string;
	kind?: string;
	score?: number;
	origin?: string;
	package_ident?: string;
	package_title?: string;
}

interface BridgeSearchResult {
	query: string;
	local_scanned?: number;
	cloud_attempted?: boolean;
	cloud_supported?: boolean;
	cloud_hits?: number;
	match_count: number;
	matches: BridgeAssetHit[];
}

interface BridgeAssetEntry extends AssetEntry {
	name?: string;
	package_ident?: string;
	package_title?: string;
	origin?: string;
}

async function bridgeSearch(
	query: string,
	kind: AssetKind | undefined,
	limit: number,
	includeCloud: boolean,
): Promise<{
	matches: BridgeAssetEntry[];
	cloud_supported: boolean;
	cloud_hits: number;
	local_scanned: number;
} | null> {
	try {
		const res = await callBridge<BridgeSearchResult>(
			"search_assets",
			{ query, kind, limit, include_cloud: includeCloud },
			{ timeoutMs: includeCloud ? 15000 : 5000 },
		);
		return {
			matches: res.matches.map((m) => ({
				path: m.path,
				name: m.name,
				kind: classifyKind(m.kind, m.path),
				source: m.origin === "cloud-uninstalled" ? "cloud" : "engine",
				origin: m.origin,
				package_ident: m.package_ident,
				package_title: m.package_title,
			})),
			cloud_supported: !!res.cloud_supported,
			cloud_hits: res.cloud_hits ?? 0,
			local_scanned: res.local_scanned ?? 0,
		};
	} catch (err) {
		if (err instanceof BridgeError) return null; // editor not running
		throw err;
	}
}

function dedupeByPath(entries: AssetEntry[]): AssetEntry[] {
	const seen = new Map<string, AssetEntry>();
	for (const e of entries) {
		// Cloud-uninstalled hits have no path — key by package ident instead so
		// multiple uninstalled packages aren't collapsed into one.
		const ext = e as BridgeAssetEntry;
		const key = e.path || ext.package_ident || `${e.source}:${ext.name ?? Math.random()}`;
		const existing = seen.get(key);
		// Project entries always win on real path collisions.
		if (!existing || (e.source === "project" && existing.source !== "project")) {
			seen.set(key, e);
		}
	}
	return Array.from(seen.values());
}

function rankProject(entries: AssetEntry[], query: string): AssetEntry[] {
	if (!query) return entries;
	const q = query.toLowerCase();
	return entries
		.map((e) => {
			const path = e.path.toLowerCase();
			const base = path.split("/").pop() ?? path;
			let score = 0;
			if (base === q) score = 100;
			else if (base.replace(/\.[^.]+$/, "") === q) score = 95;
			else if (base.startsWith(q)) score = 80;
			else if (base.includes(q)) score = 60;
			else if (path.includes(q)) score = 40;
			return { e, score };
		})
		.filter((x) => x.score > 0)
		.sort((a, b) => b.score - a.score)
		.map((x) => x.e);
}

export const findAsset: ToolDef = {
	name: "find_asset",
	description:
		"Search for assets by name across project (grappleship/Assets/) + every mounted source the editor knows about (engine, workshop, cloud-installed packages). Live query — runs through the editor bridge so freshly-installed assets show up immediately. Falls back to project-only search when the editor isn't running.",
	inputSchema: {
		type: "object",
		properties: {
			query: { type: "string", description: "Name fragment, e.g. 'box', 'wood', 'viking helmet'" },
			kind: { type: "string", enum: KIND_VALUES },
			limit: { type: "integer", minimum: 1, maximum: 200, default: 25 },
			include_cloud: { type: "boolean", default: false, description: "Also search the asset.party catalog for uninstalled packages (slower; results show up with origin='cloud-uninstalled')." },
			project_only: { type: "boolean", default: false, description: "Skip the editor and search only project files." },
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
				include_cloud: z.boolean().default(false),
				project_only: z.boolean().default(false),
			})
			.parse(args);

		const projectAll = await walkProjectAssets(cfg.assetsRoot);
		const projectFiltered = projectAll.filter((e) => !parsed.kind || e.kind === parsed.kind);
		const projectMatches = rankProject(projectFiltered, parsed.query);

		let bridge: Awaited<ReturnType<typeof bridgeSearch>> = null;
		if (!parsed.project_only) {
			bridge = await bridgeSearch(parsed.query, parsed.kind as AssetKind | undefined, parsed.limit, parsed.include_cloud);
		}

		const merged = dedupeByPath([
			...projectMatches.map((e) => ({ ...e, source: "project" as const })),
			...(bridge?.matches ?? []),
		]);

		return {
			query: parsed.query,
			editor_available: bridge !== null,
			cloud_searched: parsed.include_cloud && bridge !== null,
			project_matches: projectMatches.length,
			engine_scanned: bridge?.local_scanned ?? 0,
			cloud_hits: bridge?.cloud_hits ?? 0,
			match_count: Math.min(merged.length, parsed.limit),
			matches: merged.slice(0, parsed.limit),
		};
	},
};

export const listAssets: ToolDef = {
	name: "list_assets",
	description:
		"List assets — by default only project files (fast). Pass include_engine=true to also pull a sample from the live editor catalog (engine + workshop + cloud-installed). For specific lookups prefer find_asset.",
	inputSchema: {
		type: "object",
		properties: {
			kind: { type: "string", enum: KIND_VALUES },
			path_contains: { type: "string" },
			limit: { type: "integer", minimum: 1, maximum: 5000, default: 200 },
			include_engine: { type: "boolean", default: false, description: "Include a sample of engine/workshop assets (uses the bridge — editor must be running)." },
		},
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z
			.object({
				kind: z.enum(KIND_VALUES as [string, ...string[]]).optional(),
				path_contains: z.string().optional(),
				limit: z.number().int().min(1).max(5000).default(200),
				include_engine: z.boolean().default(false),
			})
			.parse(args);

		const projectAll = await walkProjectAssets(cfg.assetsRoot);
		const needle = parsed.path_contains?.toLowerCase();
		let entries: AssetEntry[] = projectAll
			.filter((e) => !parsed.kind || e.kind === parsed.kind)
			.filter((e) => !needle || e.path.toLowerCase().includes(needle));

		let editor_available: boolean | "skipped" = "skipped";
		let engine_scanned = 0;
		if (parsed.include_engine) {
			const bridge = await bridgeSearch(needle ?? "", parsed.kind as AssetKind | undefined, parsed.limit, false);
			editor_available = bridge !== null;
			if (bridge) {
				engine_scanned = bridge.local_scanned;
				entries = dedupeByPath([...entries, ...bridge.matches]);
			}
		}

		return {
			editor_available,
			engine_scanned,
			project_count: projectAll.length,
			returned: Math.min(entries.length, parsed.limit),
			assets: entries.slice(0, parsed.limit),
		};
	},
};

export const describeAsset: ToolDef = {
	name: "describe_asset",
	description: "Return metadata for an asset path. Checks project first; if not found and the editor is running, asks the bridge whether the path resolves to a mounted asset.",
	inputSchema: {
		type: "object",
		properties: {
			path: { type: "string", description: "Asset path, e.g. 'materials/sand floor.vmat' or 'models/dev/box.vmdl'." },
		},
		required: ["path"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z.object({ path: z.string() }).parse(args);
		const normalized = parsed.path.replace(/\\/g, "/").replace(/^\/+/, "");

		const projectAll = await walkProjectAssets(cfg.assetsRoot);
		const local = projectAll.find((e) => e.path === normalized);
		if (local) return { exists: true, ...local };

		// Try the bridge: search by basename for an exact path match.
		const basename = normalized.split("/").pop() ?? normalized;
		const bridge = await bridgeSearch(basename, undefined, 50, false);
		const hit = bridge?.matches.find((m) => m.path === normalized);
		if (hit) return { exists: true, ...hit };

		return { exists: false, path: normalized, editor_consulted: bridge !== null };
	},
};

export const validateAssetPath: ToolDef = {
	name: "validate_asset_path",
	description:
		"Check that an asset path is reachable (project file or any mounted source) and optionally matches an expected kind. Useful before setting Model/Material/Texture properties.",
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

		// Project file?
		const full = resolve(cfg.assetsRoot, normalized);
		try {
			await stat(full);
			const kind = classifyKind(undefined, normalized);
			if (parsed.expected_kind && kind !== parsed.expected_kind) {
				return { ok: false, error: `expected kind ${parsed.expected_kind}, got ${kind}`, path: normalized, source: "project" };
			}
			return { ok: true, path: normalized, kind, source: "project" };
		} catch {
			// not in project — fall through
		}

		// Mounted asset via bridge?
		const basename = normalized.split("/").pop() ?? normalized;
		const bridge = await bridgeSearch(basename, undefined, 50, false);
		if (!bridge) {
			return { ok: false, error: "asset not in project and editor not running", path: normalized };
		}
		const hit = bridge.matches.find((m) => m.path === normalized);
		if (!hit) {
			return { ok: false, error: "asset not in project or any mounted source", path: normalized };
		}
		if (parsed.expected_kind && hit.kind !== parsed.expected_kind) {
			return { ok: false, error: `expected kind ${parsed.expected_kind}, got ${hit.kind}`, path: normalized, source: hit.source };
		}
		return { ok: true, path: normalized, kind: hit.kind, source: hit.source };
	},
};
