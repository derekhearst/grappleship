import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

export interface ServerConfig {
	repoRoot: string;
	scenesRoot: string;
	assetsRoot: string;
	csSourceRoot: string;
	builtinJsonPath: string;
	logPath: string;
}

export function resolveConfig(): ServerConfig {
	// tools/mcp/src/config.ts → up 3 → repo root.
	const here = dirname(fileURLToPath(import.meta.url));
	const repoRoot = resolve(here, "..", "..", "..");
	return {
		repoRoot,
		scenesRoot: resolve(repoRoot, "grappleship", "Assets", "scenes"),
		assetsRoot: resolve(repoRoot, "grappleship", "Assets"),
		csSourceRoot: resolve(repoRoot, "grappleship", "Code"),
		builtinJsonPath: resolve(repoRoot, "docs", "sbox", "builtin-types.json"),
		logPath: resolve(repoRoot, "grappleship", "logs", "sbox-dev.log"),
	};
}
