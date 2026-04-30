import { z } from "zod";
import { clearCache, defaultConfig, loadCatalog } from "../schema/loader";
import type { ToolDef } from "./registry";

export const listComponents: ToolDef = {
	name: "list_components",
	description:
		"List every known component type (built-in Sandbox.* + parsed GrappleShip.*). Returns full names, titles, and categories. Use this first to discover what components are available before writing scenes.",
	inputSchema: {
		type: "object",
		properties: {
			category: { type: "string", description: "Optional substring filter on category (e.g. 'GrappleShip', 'Rendering')" },
			source: { type: "string", enum: ["builtin", "parsed", "all"], default: "all" },
		},
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z
			.object({
				category: z.string().optional(),
				source: z.enum(["builtin", "parsed", "all"]).default("all"),
			})
			.parse(args);
		const catalog = await loadCatalog(defaultConfig(cfg.repoRoot));
		const out = Object.values(catalog.components)
			.filter((c) => parsed.source === "all" || c.source === parsed.source)
			.filter((c) => !parsed.category || (c.category ?? "").toLowerCase().includes(parsed.category.toLowerCase()))
			.map((c) => ({
				full_name: c.full_name,
				title: c.title,
				category: c.category,
				property_count: Object.keys(c.properties).length,
				source: c.source,
			}))
			.sort((a, b) => a.full_name.localeCompare(b.full_name));
		return { count: out.length, components: out };
	},
};

export const describeComponent: ToolDef = {
	name: "describe_component",
	description:
		"Full property schema for one component type. Returns every [Property]-marked field with type, range, group, defaults, etc. ALWAYS call this before adding a component or setting a property — it's how you avoid invalid scene edits.",
	inputSchema: {
		type: "object",
		properties: {
			full_name: { type: "string", description: "Fully-qualified type name, e.g. 'Sandbox.ModelRenderer' or 'GrappleShip.GrappleHook'" },
		},
		required: ["full_name"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z.object({ full_name: z.string() }).parse(args);
		const catalog = await loadCatalog(defaultConfig(cfg.repoRoot));
		const direct = catalog.components[parsed.full_name];
		if (direct) return direct;
		const lower = parsed.full_name.toLowerCase();
		const candidates = Object.values(catalog.components).filter(
			(c) => c.full_name.toLowerCase() === lower || c.full_name.toLowerCase().endsWith("." + lower),
		);
		if (candidates.length === 1) return candidates[0];
		if (candidates.length > 1) {
			return { error: "ambiguous component name", candidates: candidates.map((c) => c.full_name) };
		}
		return { error: `component not found: ${parsed.full_name}. Try list_components.` };
	},
};

export const searchProperty: ToolDef = {
	name: "search_property",
	description:
		"Find which components have a property by name. Useful when you remember a property name but not which component owns it.",
	inputSchema: {
		type: "object",
		properties: {
			property_name: { type: "string" },
			exact: { type: "boolean", default: false },
		},
		required: ["property_name"],
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z.object({ property_name: z.string(), exact: z.boolean().default(false) }).parse(args);
		const catalog = await loadCatalog(defaultConfig(cfg.repoRoot));
		const matches: Array<{ component: string; property: string; type: string }> = [];
		const needle = parsed.property_name.toLowerCase();
		for (const c of Object.values(catalog.components)) {
			for (const [name, prop] of Object.entries(c.properties)) {
				const hit = parsed.exact ? name === parsed.property_name : name.toLowerCase().includes(needle);
				if (hit) matches.push({ component: c.full_name, property: name, type: prop.type });
			}
		}
		return { count: matches.length, matches };
	},
};

export const refreshSchema: ToolDef = {
	name: "refresh_schema",
	description:
		"Force-reload the type catalog (re-read builtin-types.json and re-parse .cs files). Call after editing C# component definitions or after Derek refreshes the built-in schema export.",
	inputSchema: { type: "object", properties: {}, additionalProperties: false },
	run: async (_args, cfg) => {
		clearCache();
		const catalog = await loadCatalog(defaultConfig(cfg.repoRoot), true);
		return { reloaded: true, component_count: Object.keys(catalog.components).length };
	},
};
