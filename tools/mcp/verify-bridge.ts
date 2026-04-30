// Live verification: ping_editor → refresh_builtin_schema → validate_scene MainMap
import { spawn } from "node:child_process";
import { resolve } from "node:path";

const proc = spawn("bun", ["run", resolve(import.meta.dir, "src/index.ts")], {
	stdio: ["pipe", "pipe", "inherit"],
});

let nextId = 1;
const pending = new Map<number, (msg: unknown) => void>();
let buffer = "";
proc.stdout!.on("data", (chunk: Buffer) => {
	buffer += chunk.toString("utf8");
	let idx;
	while ((idx = buffer.indexOf("\n")) !== -1) {
		const line = buffer.slice(0, idx);
		buffer = buffer.slice(idx + 1);
		if (!line.trim()) continue;
		const msg = JSON.parse(line);
		if (typeof msg.id === "number" && pending.has(msg.id)) {
			pending.get(msg.id)!(msg);
			pending.delete(msg.id);
		}
	}
});

function send(method: string, params: object): Promise<unknown> {
	const id = nextId++;
	return new Promise((res) => {
		pending.set(id, (msg) => res(msg));
		proc.stdin!.write(JSON.stringify({ jsonrpc: "2.0", id, method, params }) + "\n");
	});
}

async function call(name: string, args: object) {
	const r = (await send("tools/call", { name, arguments: args })) as {
		result: { content: { text: string }[]; isError?: boolean };
	};
	const text = r.result.content[0]?.text ?? "";
	return { isError: r.result.isError ?? false, text };
}

function show(label: string, val: { isError: boolean; text: string }) {
	const head = val.isError ? "FAIL" : "OK";
	console.log(`\n=== [${head}] ${label} ===`);
	console.log(val.text.slice(0, 1500));
}

async function main() {
	await send("initialize", {
		protocolVersion: "2024-11-05",
		capabilities: {},
		clientInfo: { name: "verify", version: "0" },
	});
	proc.stdin!.write(JSON.stringify({ jsonrpc: "2.0", method: "notifications/initialized" }) + "\n");

	show("ping_editor", await call("ping_editor", {}));
	show("refresh_builtin_schema", await call("refresh_builtin_schema", {}));
	show("validate_scene MainMap (post-refresh)", await call("validate_scene", { scene: "MainMap" }));

	proc.kill();
	process.exit(0);
}

main().catch((err) => {
	console.error("verify failed:", err);
	proc.kill();
	process.exit(1);
});
