export type PropertyType =
	| "bool"
	| "int"
	| "float"
	| "string"
	| "vector2"
	| "vector3"
	| "rotation"
	| "color"
	| "angles"
	| "enum"
	| "asset"
	| "gameobject_ref"
	| `component_ref:${string}`
	| `unknown:${string}`
	| `generic:${string}`;

export interface PropertySchema {
	type: PropertyType | string;
	default?: unknown;
	range?: [number, number];
	group?: string;
	readonly?: boolean;
	values?: string[];
	asset_kind?: string;
	source: "builtin" | "parsed";
}

export interface ComponentSchema {
	full_name: string;
	title?: string;
	category?: string;
	icon?: string;
	properties: Record<string, PropertySchema>;
	source: "builtin" | "parsed";
}

export interface Catalog {
	generated_at?: string;
	components: Record<string, ComponentSchema>;
}
