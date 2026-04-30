import { open, stat } from "node:fs/promises";
import { join } from "node:path";

export interface LogConfig {
	repoRoot: string;
}

export interface LogLine {
	level?: string;
	text: string;
}

const watchOffsets = new Map<string, number>();

function logPath(cfg: LogConfig): string {
	return join(cfg.repoRoot, "grappleship", "logs", "sbox-dev.log");
}

export async function readLog(cfg: LogConfig, lines: number, levelFilter?: string[]): Promise<LogLine[]> {
	const path = logPath(cfg);
	let st;
	try {
		st = await stat(path);
	} catch {
		return [];
	}
	const all = await readTail(path, st.size, Math.max(lines * 200, 16384));
	return parseAndFilter(all, levelFilter).slice(-lines);
}

export async function watchLog(cfg: LogConfig, levelFilter?: string[]): Promise<LogLine[]> {
	const path = logPath(cfg);
	let st;
	try {
		st = await stat(path);
	} catch {
		return [];
	}
	const previous = watchOffsets.get(path) ?? st.size;
	if (st.size < previous) {
		// File was truncated/rotated. Reset.
		watchOffsets.set(path, st.size);
		return [];
	}
	const newBytes = st.size - previous;
	if (newBytes === 0) return [];
	const text = await readRange(path, previous, newBytes);
	watchOffsets.set(path, st.size);
	return parseAndFilter(text, levelFilter);
}

async function readTail(path: string, fileSize: number, maxBytes: number): Promise<string> {
	const start = Math.max(0, fileSize - maxBytes);
	return readRange(path, start, fileSize - start);
}

async function readRange(path: string, offset: number, length: number): Promise<string> {
	const fh = await open(path, "r");
	try {
		const buf = Buffer.alloc(length);
		await fh.read(buf, 0, length, offset);
		return buf.toString("utf8");
	} finally {
		await fh.close();
	}
}

function parseAndFilter(text: string, levelFilter?: string[]): LogLine[] {
	const lines = text.split(/\r?\n/).filter((l) => l.length > 0);
	const filterSet = levelFilter?.length ? new Set(levelFilter.map((l) => l.toLowerCase())) : null;
	const out: LogLine[] = [];
	for (const text of lines) {
		const level = detectLevel(text);
		if (filterSet && (!level || !filterSet.has(level.toLowerCase()))) continue;
		out.push({ level, text });
	}
	return out;
}

function detectLevel(line: string): string | undefined {
	const m = line.match(/\b(Error|Warning|Info|Debug|Trace|Fatal|Compiler\s+CS\d+)\b/);
	return m?.[1];
}
