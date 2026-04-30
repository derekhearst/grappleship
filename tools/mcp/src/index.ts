#!/usr/bin/env bun
import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
	CallToolRequestSchema,
	ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";
import { resolveConfig } from "./config";
import { tools, type ToolDef } from "./tools/registry";

async function main() {
	const cfg = resolveConfig();

	const server = new Server(
		{ name: "grappleship", version: "0.1.0" },
		{ capabilities: { tools: {} } },
	);

	server.setRequestHandler(ListToolsRequestSchema, async () => ({
		tools: tools.map(({ name, description, inputSchema }) => ({
			name,
			description,
			inputSchema,
		})),
	}));

	const byName = new Map<string, ToolDef>();
	for (const t of tools) byName.set(t.name, t);

	server.setRequestHandler(CallToolRequestSchema, async (req) => {
		const tool = byName.get(req.params.name);
		if (!tool) {
			return {
				isError: true,
				content: [{ type: "text", text: `Unknown tool: ${req.params.name}` }],
			};
		}
		try {
			const result = await tool.run(req.params.arguments ?? {}, cfg);
			return {
				content: [{ type: "text", text: typeof result === "string" ? result : JSON.stringify(result, null, 2) }],
			};
		} catch (err) {
			const text = formatToolError(err);
			return {
				isError: true,
				content: [{ type: "text", text }],
			};
		}
	});

	const transport = new StdioServerTransport();
	await server.connect(transport);
	process.stderr.write("[grappleship-mcp] ready\n");
}

main().catch((err) => {
	process.stderr.write(`[grappleship-mcp] fatal: ${err}\n`);
	process.exit(1);
});

function formatToolError(err: unknown): string {
	if (err && typeof err === "object" && "code" in err && "message" in err) {
		return `[${(err as { code: string }).code}] ${(err as { message: string }).message}`;
	}
	if (err && typeof err === "object" && err instanceof Error && err.name === "ZodError") {
		return `bad arguments: ${err.message}`;
	}
	if (err instanceof Error) {
		return `${err.message}\n\n${err.stack ?? ""}`;
	}
	return String(err);
}
