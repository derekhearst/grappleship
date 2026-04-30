import type { ComponentSchema, PropertySchema } from "./types";

/**
 * Minimal C# parser for the GrappleShip codebase. We don't need a real C# parser —
 * the [Property] surface follows a tight, predictable shape:
 *
 *   [Property, Group("Tuning"), Range(0f, 100f)] public float Foo { get; set; } = 5f;
 *
 * We extract: the class declaration line (with [Title]/[Category]/[Icon]), and each
 * [Property]-tagged member's name, type, attributes, and default. Anything weird
 * gets a warning and is skipped — partial schema is fine.
 */

interface ClassMeta {
	namespace: string;
	className: string;
	title?: string;
	category?: string;
	icon?: string;
}

const TYPE_MAP: Record<string, string> = {
	bool: "bool",
	int: "int",
	uint: "int",
	long: "int",
	short: "int",
	byte: "int",
	float: "float",
	double: "float",
	string: "string",
	Vector2: "vector2",
	Vector3: "vector3",
	Vector4: "vector3",
	Rotation: "rotation",
	Color: "color",
	Angles: "angles",
	GameObject: "gameobject_ref",
};

export function parseCsFile(source: string, filePath: string): ComponentSchema[] {
	const stripped = stripCommentsAndStrings(source);
	const namespaceMatch = stripped.match(/\bnamespace\s+([A-Za-z_][\w.]*)/);
	const namespace = namespaceMatch?.[1] ?? "";

	const components: ComponentSchema[] = [];

	const classRe = /((?:\[[^\]]*\]\s*)*)\b(?:public\s+)?(?:sealed\s+|partial\s+|abstract\s+|static\s+)*class\s+([A-Za-z_]\w*)\s*(?::\s*([^{]+))?\{/g;

	for (const match of stripped.matchAll(classRe)) {
		const attrBlock = match[1] ?? "";
		const className = match[2]!;
		const baseClause = match[3] ?? "";

		const inheritsComponent =
			/\bComponent\b/.test(baseClause) || /\b\w*Component\b/.test(baseClause);
		if (!inheritsComponent) continue;

		const meta: ClassMeta = {
			namespace,
			className,
			title: extractAttrString(attrBlock, "Title"),
			category: extractAttrString(attrBlock, "Category"),
			icon: extractAttrString(attrBlock, "Icon"),
		};

		const classBodyStart = match.index! + match[0].length;
		const classBody = extractBalancedBlock(stripped, classBodyStart - 1);
		if (!classBody) continue;

		const properties = parseProperties(classBody, filePath);

		components.push({
			full_name: namespace ? `${namespace}.${className}` : className,
			title: meta.title,
			category: meta.category,
			icon: meta.icon,
			properties,
			source: "parsed",
		});
	}

	return components;
}

function parseProperties(classBody: string, filePath: string): Record<string, PropertySchema> {
	const out: Record<string, PropertySchema> = {};

	// Match: [attrs...] modifiers Type Name { get; set; } [= default];
	// Modifiers: public|private|protected|internal|static|readonly|virtual|override
	const propRe =
		/((?:\[[^\]]*\][\s\r\n]*)+)\s*(?:public|private|protected|internal)?\s*(?:static\s+|readonly\s+|virtual\s+|override\s+)*([A-Za-z_][\w<>\[\],\s.?]*?)\s+([A-Za-z_]\w*)\s*\{[^}]*\}\s*(?:=\s*([^;]+);)?/g;

	for (const m of classBody.matchAll(propRe)) {
		const attrs = m[1]!;
		if (!/\[[\s,]*Property\b/.test(attrs)) continue;
		if (/\bHide\b/.test(attrs)) continue;

		const rawType = (m[2] ?? "").trim();
		const name = m[3]!;
		const defaultExpr = m[4]?.trim();

		const propSchema: PropertySchema = {
			type: mapCsharpType(rawType),
			source: "parsed",
		};

		const range = extractAttrNumbers(attrs, "Range");
		if (range && range.length >= 2) {
			propSchema.range = [range[0]!, range[1]!];
		}

		const group = extractAttrString(attrs, "Group");
		if (group) propSchema.group = group;

		if (/\[[\s,]*ReadOnly\b/.test(attrs)) propSchema.readonly = true;

		if (defaultExpr) {
			propSchema.default = parseDefault(defaultExpr);
		}

		out[name] = propSchema;
	}

	return out;
}

function mapCsharpType(rawType: string): string {
	const cleaned = rawType.replace(/\?\s*$/, "").trim();
	if (TYPE_MAP[cleaned]) return TYPE_MAP[cleaned]!;
	if (cleaned.startsWith("List<") || cleaned.startsWith("IList<") || cleaned.startsWith("IEnumerable<")) {
		return `generic:${cleaned}`;
	}
	// Component-typed properties → component_ref. We can't know definitively that
	// the type derives from Component without a full type graph, but by convention
	// in s&box [Property] members of class types are component refs unless they're
	// data classes. We tag as component_ref:<TypeName>; the validator can still
	// accept GameObject refs against this if we want to be permissive.
	if (/^[A-Z]/.test(cleaned)) return `component_ref:${cleaned}`;
	return `unknown:${cleaned}`;
}

function parseDefault(expr: string): unknown {
	const e = expr.trim().replace(/[fFmMdD]$/, "");
	if (e === "true") return true;
	if (e === "false") return false;
	if (e === "null") return null;
	if (/^-?\d+(\.\d+)?$/.test(e)) return Number(e);
	const stringMatch = e.match(/^"((?:[^"\\]|\\.)*)"$/);
	if (stringMatch) return stringMatch[1];
	return expr;
}

function extractAttrString(attrBlock: string, attrName: string): string | undefined {
	const re = new RegExp(`\\b${attrName}\\s*\\(\\s*"((?:[^"\\\\]|\\\\.)*)"`);
	const m = attrBlock.match(re);
	return m?.[1];
}

function extractAttrNumbers(attrBlock: string, attrName: string): number[] | undefined {
	const re = new RegExp(`\\b${attrName}\\s*\\(([^)]*)\\)`);
	const m = attrBlock.match(re);
	if (!m) return undefined;
	const args = m[1]!
		.split(",")
		.map((a) => a.trim().replace(/[fFmMdD]$/, ""))
		.map((a) => Number(a))
		.filter((n) => Number.isFinite(n));
	return args.length ? args : undefined;
}

function stripCommentsAndStrings(src: string): string {
	let out = "";
	let i = 0;
	while (i < src.length) {
		const c = src[i];
		const n = src[i + 1];
		if (c === "/" && n === "/") {
			while (i < src.length && src[i] !== "\n") i++;
			continue;
		}
		if (c === "/" && n === "*") {
			i += 2;
			while (i < src.length - 1 && !(src[i] === "*" && src[i + 1] === "/")) i++;
			i += 2;
			continue;
		}
		// Preserve strings (we want their content for [Title("X")] etc.)
		out += c;
		i++;
	}
	return out;
}

function extractBalancedBlock(src: string, openBraceIndex: number): string | null {
	if (src[openBraceIndex] !== "{") return null;
	let depth = 0;
	for (let i = openBraceIndex; i < src.length; i++) {
		if (src[i] === "{") depth++;
		else if (src[i] === "}") {
			depth--;
			if (depth === 0) return src.slice(openBraceIndex + 1, i);
		}
	}
	return null;
}
