#!/usr/bin/env bun
// Generate a tileable sand-colored albedo texture as RGB PNG.
// Output: grappleship/Assets/textures/terrain/sand_albedo.png
//
// Usage: bun tools/scripts/generate-sand-albedo.ts [size]

import { mkdir, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { deflateSync } from "node:zlib";

const SIZE = Number(process.argv[2] ?? 512);
const outPath = resolve("grappleship/Assets/textures/terrain/sand_albedo.png");

// Same value-noise helpers as the heightmap generator.
function hash(x: number, y: number, s: number): number {
	let h = (Math.imul(x | 0, 374761393) + Math.imul(y | 0, 668265263) + Math.imul(s, 982451653)) | 0;
	h = (h ^ (h >>> 13)) | 0;
	h = Math.imul(h, 1274126177) | 0;
	h = (h ^ (h >>> 16)) >>> 0;
	return h / 0xffffffff;
}
function smooth(t: number): number { return t * t * (3 - 2 * t); }
function valueNoise(x: number, y: number, s: number): number {
	const xi = Math.floor(x), yi = Math.floor(y);
	const xf = x - xi, yf = y - yi;
	const u = smooth(xf), v = smooth(yf);
	const a = hash(xi, yi, s), b = hash(xi + 1, yi, s);
	const c = hash(xi, yi + 1, s), d = hash(xi + 1, yi + 1, s);
	return (a * (1 - u) + b * u) * (1 - v) + (c * (1 - u) + d * u) * v;
}

// Sand base color (warm tan).
const baseR = 218, baseG = 188, baseB = 134;

const rgb = new Uint8Array(SIZE * SIZE * 3);
for (let y = 0; y < SIZE; y++) {
	for (let x = 0; x < SIZE; x++) {
		// Multi-octave noise → variation between -0.2 and +0.2 of base color
		const fx = x / SIZE * 12;
		const fy = y / SIZE * 12;
		const n = valueNoise(fx, fy, 7) * 0.6
			+ valueNoise(fx * 3, fy * 3, 8) * 0.3
			+ valueNoise(fx * 8, fy * 8, 9) * 0.1;
		const factor = 0.85 + n * 0.30;
		const i = (y * SIZE + x) * 3;
		rgb[i + 0] = Math.max(0, Math.min(255, Math.round(baseR * factor)));
		rgb[i + 1] = Math.max(0, Math.min(255, Math.round(baseG * factor)));
		rgb[i + 2] = Math.max(0, Math.min(255, Math.round(baseB * factor * 0.95)));
	}
}

// PNG encode (24-bit RGB).
const PNG_SIG = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
const ihdr = Buffer.alloc(13);
ihdr.writeUInt32BE(SIZE, 0);
ihdr.writeUInt32BE(SIZE, 4);
ihdr[8] = 8; // bit depth
ihdr[9] = 2; // color type: RGB
const stride = SIZE * 3 + 1;
const raw = Buffer.alloc(stride * SIZE);
for (let y = 0; y < SIZE; y++) {
	raw[y * stride] = 0;
	raw.set(rgb.subarray(y * SIZE * 3, (y + 1) * SIZE * 3), y * stride + 1);
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
function crc32(d: Uint8Array): number {
	let c = 0xffffffff >>> 0;
	for (const b of d) c = (crcTable[(c ^ b) & 0xff]! ^ (c >>> 8)) >>> 0;
	return (c ^ 0xffffffff) >>> 0;
}
function chunk(type: string, data: Buffer): Buffer {
	const len = Buffer.alloc(4); len.writeUInt32BE(data.length, 0);
	const tBuf = Buffer.from(type, "ascii");
	const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(Buffer.concat([tBuf, data])), 0);
	return Buffer.concat([len, tBuf, data, crc]);
}
const png = Buffer.concat([
	PNG_SIG,
	chunk("IHDR", ihdr),
	chunk("IDAT", idat),
	chunk("IEND", Buffer.alloc(0)),
]);

await mkdir(dirname(outPath), { recursive: true });
await writeFile(outPath, png);
console.log(`wrote ${outPath} (${png.length.toLocaleString()} bytes, ${SIZE}×${SIZE} RGB sand)`);
