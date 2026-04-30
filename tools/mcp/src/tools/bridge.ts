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
