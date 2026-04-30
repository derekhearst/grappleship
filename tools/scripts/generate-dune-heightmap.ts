#!/usr/bin/env bun
// Generate a sand-dune heightmap PNG (8-bit grayscale) using value noise.
// Drop the result anywhere s&box can pick it up; setting it as a Sandbox.Terrain
// HeightMap will produce a real terrain mesh with rolling dunes.
//
// Usage:
//   bun tools/scripts/generate-dune-heightmap.ts [output-path] [size] [seed]
//
// Defaults: grappleship/Assets/textures/terrain/dune_heightmap.png  512  42

import { mkdir, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { deflateSync } from "node:zlib";

const outPath = resolve(process.argv[2] ?? "grappleship/Assets/textures/terrain/dune_heightmap.png");
const SIZE = Number(process.argv[3] ?? 512);
const SEED = Number(process.argv[4] ?? 42);

// Hash-based pseudo-random in [0, 1).
function hash(x: number, y: number, s: number): number {
	let h = (Math.imul(x | 0, 374761393) + Math.imul(y | 0, 668265263) + Math.imul(s, 982451653)) | 0;
	h = (h ^ (h >>> 13)) | 0;
	h = Math.imul(h, 1274126177) | 0;
	h = (h ^ (h >>> 16)) >>> 0;
	return h / 0xffffffff;
}

function smooth(t: number): number {
	return t * t * (3 - 2 * t);
}

function valueNoise(x: number, y: number, s: number): number {
	const xi = Math.floor(x);
	const yi = Math.floor(y);
	const xf = x - xi;
	const yf = y - yi;
	const u = smooth(xf);
	const v = smooth(yf);
	const a = hash(xi, yi, s);
	const b = hash(xi + 1, yi, s);
	const c = hash(xi, yi + 1, s);
	const d = hash(xi + 1, yi + 1, s);
	return (a * (1 - u) + b * u) * (1 - v) + (c * (1 - u) + d * u) * v;
}

// Compute heights into a Float32Array, then normalize + quantize at the end.
const heights = new Float32Array(SIZE * SIZE);
let mn = Infinity;
let mx = -Infinity;
for (let y = 0; y < SIZE; y++) {
	for (let x = 0; x < SIZE; x++) {
		// Map to ~6 dune cycles across the map.
		const fx = (x / SIZE) * 6;
		const fy = (y / SIZE) * 6;

		// Big rolling base (low frequency).
		const big = valueNoise(fx, fy, SEED);
		// Mid-frequency variation.
		const mid = valueNoise(fx * 2.3, fy * 2.3, SEED + 1) * 0.45;
		// Small bumps.
		const small = valueNoise(fx * 5.7, fy * 5.7, SEED + 2) * 0.18;
		// Anisotropic stretch — fakes wind-blown dune ridges along one axis.
		const stretched = valueNoise(fx * 0.6, fy * 2.8, SEED + 3) * 0.55;

		const v = big + mid + small + stretched;
		heights[y * SIZE + x] = v;
		if (v < mn) mn = v;
		if (v > mx) mx = v;
	}
}

// Normalize to [0, 1], then bias upward so dunes are mostly above the baseline,
// then quantize to 8 bits.
const gray = new Uint8Array(SIZE * SIZE);
const range = mx - mn;
for (let i = 0; i < heights.length; i++) {
	const norm = (heights[i]! - mn) / range;
	// Soft contrast curve so most of the map is "low" with peaks for dunes.
	const curved = Math.pow(norm, 1.3);
	gray[i] = Math.max(0, Math.min(255, Math.round(curved * 255)));
}

// --- Minimal PNG encoder ----------------------------------------------------
// 8-bit grayscale, single IDAT chunk, deflate-compressed via zlib.

const PNG_SIG = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

const ihdr = Buffer.alloc(13);
ihdr.writeUInt32BE(SIZE, 0);
ihdr.writeUInt32BE(SIZE, 4);
ihdr[8] = 8; // bit depth
ihdr[9] = 0; // color type: grayscale
ihdr[10] = 0; // compression
ihdr[11] = 0; // filter
ihdr[12] = 0; // interlace

const stride = SIZE + 1; // 1 filter byte per scanline
const raw = Buffer.alloc(stride * SIZE);
for (let y = 0; y < SIZE; y++) {
	raw[y * stride] = 0; // filter type: None
	raw.set(gray.subarray(y * SIZE, (y + 1) * SIZE), y * stride + 1);
}
const idat = deflateSync(raw);

const crcTable = (() => {
	const t = new Uint32Array(256);
	for (let i = 0; i < 256; i++) {
		let c = i;
		for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
		t[i] = c >>> 0;
	}
	return t;
})();
function crc32(data: Uint8Array): number {
	let c = 0xffffffff >>> 0;
	for (const b of data) c = (crcTable[(c ^ b) & 0xff]! ^ (c >>> 8)) >>> 0;
	return (c ^ 0xffffffff) >>> 0;
}
function chunk(type: string, data: Buffer): Buffer {
	const len = Buffer.alloc(4);
	len.writeUInt32BE(data.length, 0);
	const typeBuf = Buffer.from(type, "ascii");
	const crc = Buffer.alloc(4);
	crc.writeUInt32BE(crc32(Buffer.concat([typeBuf, data])), 0);
	return Buffer.concat([len, typeBuf, data, crc]);
}

const png = Buffer.concat([
	PNG_SIG,
	chunk("IHDR", ihdr),
	chunk("IDAT", idat),
	chunk("IEND", Buffer.alloc(0)),
]);

await mkdir(dirname(outPath), { recursive: true });
await writeFile(outPath, png);
console.log(
	`wrote ${outPath} (${png.length.toLocaleString()} bytes, ${SIZE}×${SIZE}, raw range ${mn.toFixed(3)}..${mx.toFixed(3)})`,
);
