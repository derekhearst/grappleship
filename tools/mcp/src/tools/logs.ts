import { z } from "zod";
import { readLog, watchLog } from "../logs/index";
import type { ToolDef } from "./registry";

export const readLogTool: ToolDef = {
	name: "read_log",
	description:
		"Tail recent lines from grappleship/logs/sbox-dev.log. Use this to check for compile errors and runtime warnings after making changes.",
	inputSchema: {
		type: "object",
		properties: {
			lines: { type: "integer", minimum: 1, maximum: 2000, default: 100 },
			level: {
				type: "array",
				items: { type: "string" },
				description: "Optional level filter, e.g. ['Error', 'Warning']",
			},
		},
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z
			.object({
				lines: z.number().int().min(1).max(2000).default(100),
				level: z.array(z.string()).optional(),
			})
			.parse(args);
		const out = await readLog({ repoRoot: cfg.repoRoot }, parsed.lines, parsed.level);
		return { count: out.length, lines: out };
	},
};

export const watchLogTool: ToolDef = {
	name: "watch_log",
	description:
		"Return new log lines since the previous watch_log call (or from current end on first call). Lets you check for fresh compile errors after a code edit.",
	inputSchema: {
		type: "object",
		properties: {
			level: { type: "array", items: { type: "string" } },
		},
		additionalProperties: false,
	},
	run: async (args, cfg) => {
		const parsed = z.object({ level: z.array(z.string()).optional() }).parse(args);
		const out = await watchLog({ repoRoot: cfg.repoRoot }, parsed.level);
		return { count: out.length, lines: out };
	},
};
