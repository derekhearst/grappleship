import { z } from "zod";
import { callBridge } from "../bridge/transport";
import { clearCache, defaultConfig, loadCatalog } from "../schema/loader";
import type { ToolDef } from "./registry";

export const pingEditor: ToolDef = {
	name: "ping_editor",
	description:
		"Health-check the editor bridge. Returns within ~100ms if the s&box editor is running and listening, otherwise times out. Use this to confirm the bridge is alive before calling editor-side tools.",
	inputSchema: { type: "object", properties: {}, additionalProperties: false },
	run: async () => {
		const t0 = Date.now();
		const result = await callBridge<{ pong: boolean; time: string }>("ping", {}, { timeoutMs: 4000 });
		return { ok: true, round_trip_ms: Date.now() - t0, editor_time: result.time };
	},
};

export const refreshBuiltinSchema: ToolDef = {
	name: "refresh_builtin_schema",
	description:
		"Tell the s&box editor to re-export the built-in component schema (writes docs/sbox/builtin-types.json). Reload the in-process catalog after. Use after upgrading s&box or when the validator flags built-in components as unknown. Requires the editor to be running.",
	inputSchema: { type: "object", properties: {}, additionalProperties: false },
	run: async (_args, cfg) => {
		await callBridge("refresh_schema", {}, { timeoutMs: 30000 });
		clearCache();
		const catalog = await loadCatalog(defaultConfig(cfg.repoRoot), true);
		return {
			ok: true,
			message: "Schema refreshed via editor. Don't forget to commit docs/sbox/builtin-types.json.",
			component_count: Object.keys(catalog.components).length,
		};
	},
};

export const installPackage: ToolDef = {
	name: "install_package",
	description:
		"Install a cloud package (e.g. 'arghbeef.vikinghelmet') into the project. Equivalent to the editor's Asset Browser → Install button. Pull the ident from a `find_asset` result with `origin='cloud-uninstalled'`. Returns the primary asset path so you can immediately reference the new asset in scenes. Network round-trip; can take several seconds for large packages.",
	inputSchema: {
		type: "object",
		properties: {
			ident: { type: "string", description: "Package identifier, e.g. 'arghbeef.vikinghelmet' or 'kenneynl.shiplarge'." },
		},
		required: ["ident"],
		additionalProperties: false,
	},
	run: async (args) => {
		const parsed = z.object({ ident: z.string().min(1) }).parse(args);
		const res = await callBridge<{
			ok: boolean;
			ident: string;
			package_title?: string;
			primary_asset?: string;
			package_type?: string;
			error?: string;
		}>("install_package", { ident: parsed.ident }, { timeoutMs: 120000 });
		return res;
	},
};

