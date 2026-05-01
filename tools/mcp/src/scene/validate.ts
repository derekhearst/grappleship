import type { Catalog, ComponentSchema, PropertySchema } from "../schema/types";
import { parseColor, parseRotation, parseVector3, walkGameObjects } from "./read";
import type { ComponentNode, SceneFile } from "./types";

export interface ValidationError {
	path: string;
	message: string;
	severity: "error" | "warning";
}

const RESERVED_COMPONENT_KEYS = new Set([
	"__type",
	"__guid",
	"__enabled",
	"__version",
	"Flags",
	"OnComponentDestroy",
	"OnComponentDisabled",
	"OnComponentEnabled",
	"OnComponentFixedUpdate",
	"OnComponentStart",
	"OnComponentUpdate",
]);

export function validateScene(scene: SceneFile, catalog: Catalog): ValidationError[] {
	const errors: ValidationError[] = [];

	for (const { go, path } of walkGameObjects(scene)) {
		const goPath = path.join(" > ");
		for (const comp of go.Components ?? []) {
			const compPath = `${goPath} > ${comp.__type}`;
			const schema = catalog.components[comp.__type];
			if (!schema) {
				errors.push({
					path: compPath,
					message: `Unknown component type "${comp.__type}". Run the Refresh Built-in Type Schema editor menu, or check the type name.`,
					severity: "error",
				});
				continue;
			}
			validateComponent(comp, schema, compPath, errors);
		}
	}

	return errors;
}

function validateComponent(
	comp: ComponentNode,
	schema: ComponentSchema,
	compPath: string,
	errors: ValidationError[],
): void {
	for (const [key, value] of Object.entries(comp)) {
		if (RESERVED_COMPONENT_KEYS.has(key)) continue;
		// Any reserved-looking key (starts with __) is engine metadata.
		if (key.startsWith("__")) continue;
		const prop = schema.properties[key];
		if (!prop) {
			// Unknown properties on parsed (GrappleShip.*) components are real
			// errors — we own the schema. On built-in Sandbox.* types our
			// exported schema may be incomplete (hidden / inherited / engine-
			// internal properties), so demote to warning.
			errors.push({
				path: `${compPath}.${key}`,
				message: `Unknown property "${key}" on ${comp.__type}.`,
				severity: schema.source === "parsed" ? "error" : "warning",
			});
			continue;
		}
		const err = checkValue(value, prop);
		if (err) {
			errors.push({
				path: `${compPath}.${key}`,
				message: err,
				severity: "error",
			});
		}
	}
}

function checkValue(value: unknown, prop: PropertySchema): string | null {
	if (value === null) return null; // null is acceptable for refs/optional fields
	const t = prop.type;

	if (t === "bool") {
		return typeof value === "boolean" ? null : `expected bool, got ${describe(value)}`;
	}
	if (t === "int") {
		// Big-integer sentinels (UInt64 values like 18446744073709551615 wrapped
		// as "@bigint:N" strings to survive JS number precision — see scene/read.ts).
		if (typeof value === "string" && value.startsWith("@bigint:")) return null;
		if (typeof value !== "number" || !Number.isFinite(value)) return `expected int, got ${describe(value)}`;
		return checkRange(value, prop);
	}
	if (t === "float") {
		if (typeof value !== "number" || !Number.isFinite(value)) return `expected float, got ${describe(value)}`;
		return checkRange(value, prop);
	}
	if (t === "string") {
		return typeof value === "string" ? null : `expected string, got ${describe(value)}`;
	}
	if (t === "vector3" || t === "vector2") {
		if (typeof value !== "string") return `expected ${t} string ("x,y,z"), got ${describe(value)}`;
		return parseVector3(value) ? null : `malformed ${t}: "${value}"`;
	}
	if (t === "rotation") {
		if (typeof value !== "string") return `expected rotation string ("x,y,z,w"), got ${describe(value)}`;
		return parseRotation(value) ? null : `malformed rotation: "${value}"`;
	}
	if (t === "color") {
		if (typeof value !== "string") return `expected color string ("r,g,b,a"), got ${describe(value)}`;
		return parseColor(value) ? null : `malformed color: "${value}"`;
	}
	if (t === "angles") {
		if (typeof value !== "string") return `expected angles string ("p,y,r"), got ${describe(value)}`;
		return parseVector3(value) ? null : `malformed angles: "${value}"`;
	}
	if (t === "enum") {
		// s&box serializes flag enums as integers (e.g. ColliderFlags: 0).
		// Accept either an integer or a member name.
		if (typeof value === "number" && Number.isInteger(value)) return null;
		if (typeof value !== "string") return `expected enum string or integer, got ${describe(value)}`;
		if (prop.values && !prop.values.includes(value)) {
			return `enum value "${value}" not in [${prop.values.join(", ")}]`;
		}
		return null;
	}
	if (typeof t === "string" && t.startsWith("component_ref")) {
		return checkComponentRef(value);
	}
	if (t === "gameobject_ref") {
		return checkGameObjectRef(value);
	}
	if (t === "asset") {
		return typeof value === "string" ? null : `expected asset path string, got ${describe(value)}`;
	}
	// unknown / generic / nested object - permissive
	return null;
}

function checkComponentRef(value: unknown): string | null {
	if (typeof value !== "object" || value === null) {
		return `expected component ref object, got ${describe(value)}`;
	}
	const v = value as Record<string, unknown>;
	if (v._type !== "component") return `expected _type="component", got ${describe(v._type)}`;
	if (typeof v.component_id !== "string") return `component ref missing component_id`;
	if (typeof v.go !== "string") return `component ref missing go`;
	return null;
}

function checkGameObjectRef(value: unknown): string | null {
	if (typeof value !== "object" || value === null) {
		return `expected gameobject ref object, got ${describe(value)}`;
	}
	const v = value as Record<string, unknown>;
	if (v._type !== "gameobject") return `expected _type="gameobject", got ${describe(v._type)}`;
	if (typeof v.go !== "string") return `gameobject ref missing go`;
	return null;
}

function checkRange(n: number, prop: PropertySchema): string | null {
	if (!prop.range) return null;
	const [min, max] = prop.range;
	if (n < min || n > max) return `value ${n} out of range [${min}, ${max}]`;
	return null;
}

function describe(v: unknown): string {
	if (v === null) return "null";
	if (Array.isArray(v)) return "array";
	if (typeof v === "object") return "object";
	return `${typeof v} ${JSON.stringify(v)}`;
}
