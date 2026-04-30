import { readdir, readFile, stat } from "node:fs/promises";
import { join, resolve } from "node:path";
import { parseCsFile } from "./csharp-parser";
import type { Catalog, ComponentSchema } from "./types";

export interface LoaderConfig {
	repoRoot: string;
	builtinJsonPath: string;
	csSourceRoot: string;
}

interface CacheEntry {
	catalog: Catalog;
	mtimes: Map<string, number>;
}

let cache: CacheEntry | null = null;

export function defaultConfig(repoRoot: string): LoaderConfig {
	return {
		repoRoot,
		builtinJsonPath: join(repoRoot, "docs", "sbox", "builtin-types.json"),
		csSourceRoot: join(repoRoot, "grappleship", "Code"),
	};
}

export async function loadCatalog(cfg: LoaderConfig, force = false): Promise<Catalog> {
	const mtimes = await collectMtimes(cfg);
	if (!force && cache && sameMtimes(cache.mtimes, mtimes)) {
		return cache.catalog;
	}

	const builtin = await readBuiltin(cfg.builtinJsonPath);
	const parsed = await readParsed(cfg.csSourceRoot);

	const components: Record<string, ComponentSchema> = {};
	for (const c of builtin) components[c.full_name] = c;
	// Parsed source wins over builtin on conflict (more current).
	for (const c of parsed) components[c.full_name] = c;

	const catalog: Catalog = {
		generated_at: new Date().toISOString(),
		components,
	};
	cache = { catalog, mtimes };
	return catalog;
}

async function readBuiltin(path: string): Promise<ComponentSchema[]> {
	try {
		const text = await readFile(path, "utf8");
		const stripped = text.replace(/^﻿/, "");
		const raw = JSON.parse(stripped) as Record<string, RawBuiltinEntry>;
		return Object.entries(raw).map(([fullName, entry]) => normalizeBuiltin(fullName, entry));
	} catch (err: unknown) {
		const e = err as NodeJS.ErrnoException;
		if (e.code === "ENOENT") return [];
		throw err;
	}
}

interface RawBuiltinEntry {
	title?: string;
	category?: string;
	icon?: string;
	properties?: Record<string, RawProp>;
}
interface RawProp {
	type?: string;
	range?: [number, number];
	group?: string;
	readonly?: boolean;
	values?: string[];
	asset_kind?: string;
	default?: unknown;
}

function normalizeBuiltin(fullName: string, raw: RawBuiltinEntry): ComponentSchema {
	const props: ComponentSchema["properties"] = {};
	for (const [name, p] of Object.entries(raw.properties ?? {})) {
		props[name] = {
			type: p.type ?? "unknown:undeclared",
			...(p.range ? { range: p.range } : {}),
			...(p.group ? { group: p.group } : {}),
			...(p.readonly ? { readonly: true } : {}),
			...(p.values ? { values: p.values } : {}),
			...(p.asset_kind ? { asset_kind: p.asset_kind } : {}),
			...(p.default !== undefined ? { default: p.default } : {}),
			source: "builtin",
		};
	}
	return {
		full_name: fullName,
		title: raw.title,
		category: raw.category,
		icon: raw.icon,
		properties: props,
		source: "builtin",
	};
}

async function readParsed(root: string): Promise<ComponentSchema[]> {
	const all: ComponentSchema[] = [];
	const files = await walkCs(root);
	for (const f of files) {
		try {
			const src = await readFile(f, "utf8");
			const cs = parseCsFile(src, f);
			all.push(...cs);
		} catch (err) {
			console.error(`[mcp] parse failed: ${f}`, err);
		}
	}
	return all;
}

async function walkCs(dir: string): Promise<string[]> {
	const out: string[] = [];
	let entries;
	try {
		entries = await readdir(dir, { withFileTypes: true });
	} catch (err: unknown) {
		const e = err as NodeJS.ErrnoException;
		if (e.code === "ENOENT") return [];
		throw err;
	}
	for (const ent of entries) {
		const p = resolve(dir, ent.name);
		if (ent.isDirectory()) {
			out.push(...(await walkCs(p)));
		} else if (ent.isFile() && ent.name.endsWith(".cs")) {
			out.push(p);
		}
	}
	return out;
}

async function collectMtimes(cfg: LoaderConfig): Promise<Map<string, number>> {
	const m = new Map<string, number>();
	try {
		const s = await stat(cfg.builtinJsonPath);
		m.set(cfg.builtinJsonPath, s.mtimeMs);
	} catch {
		// missing — fine
	}
	const files = await walkCs(cfg.csSourceRoot);
	for (const f of files) {
		try {
			const s = await stat(f);
			m.set(f, s.mtimeMs);
		} catch {
			// race; skip
		}
	}
	return m;
}

function sameMtimes(a: Map<string, number>, b: Map<string, number>): boolean {
	if (a.size !== b.size) return false;
	for (const [k, v] of a) {
		if (b.get(k) !== v) return false;
	}
	return true;
}

export function clearCache(): void {
	cache = null;
}
