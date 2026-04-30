import type { Catalog, ComponentSchema } from "../schema/types";
import { walkGameObjects } from "./read";
import type { ComponentNode, GameObjectNode, SceneFile } from "./types";
import { newGuid } from "./write";

export interface MutationError {
	code: string;
	message: string;
}

export class MutateError extends Error {
	code: string;
	constructor(code: string, message: string) {
		super(message);
		this.code = code;
	}
}

export function findGameObjectByGuid(scene: SceneFile, guid: string): GameObjectNode | null {
	for (const { go } of walkGameObjects(scene)) {
		if (go.__guid === guid) return go;
	}
	return null;
}

export function findGameObjectByPath(scene: SceneFile, namePath: string): GameObjectNode | null {
	const wanted = namePath.split(/[/>]/).map((s) => s.trim()).filter(Boolean);
	for (const { go, path } of walkGameObjects(scene)) {
		if (path.length !== wanted.length) continue;
		if (path.every((seg, i) => seg === wanted[i])) return go;
	}
	return null;
}

export function resolveGameObject(scene: SceneFile, ref: { guid?: string; name_path?: string }): GameObjectNode {
	if (ref.guid) {
		const go = findGameObjectByGuid(scene, ref.guid);
		if (!go) throw new MutateError("not_found", `GameObject not found: guid=${ref.guid}`);
		return go;
	}
	if (ref.name_path) {
		const go = findGameObjectByPath(scene, ref.name_path);
		if (!go) throw new MutateError("not_found", `GameObject not found: name_path=${ref.name_path}`);
		return go;
	}
	throw new MutateError("bad_args", "supply guid or name_path");
}

function findParentArray(scene: SceneFile, childGuid: string): GameObjectNode[] | null {
	if ((scene.GameObjects ?? []).some((g) => g.__guid === childGuid)) return scene.GameObjects;
	for (const { go } of walkGameObjects(scene)) {
		if ((go.Children ?? []).some((g) => g.__guid === childGuid)) return go.Children!;
	}
	return null;
}

function findParentOf(scene: SceneFile, childGuid: string): GameObjectNode | null {
	for (const { go } of walkGameObjects(scene)) {
		if ((go.Children ?? []).some((g) => g.__guid === childGuid)) return go;
	}
	return null;
}

// --- Mutations -------------------------------------------------------------

export interface CreateGameObjectInput {
	name: string;
	position?: string;
	rotation?: string;
	scale?: string;
	tags?: string;
	enabled?: boolean;
	parent?: { guid?: string; name_path?: string };
}

export function createGameObject(scene: SceneFile, input: CreateGameObjectInput): GameObjectNode {
	const node: GameObjectNode = {
		__guid: newGuid(),
		__version: 2,
		Flags: 0,
		Name: input.name,
		Position: input.position ?? "0,0,0",
		Rotation: input.rotation ?? "0,0,0,1",
		Scale: input.scale ?? "1,1,1",
		Tags: input.tags ?? "",
		Enabled: input.enabled ?? true,
		NetworkMode: 2,
		NetworkFlags: 0,
		NetworkOrphaned: 0,
		NetworkTransmit: true,
		OwnerTransfer: 1,
		Components: [],
		Children: [],
	};

	if (input.parent && (input.parent.guid || input.parent.name_path)) {
		const parent = resolveGameObject(scene, input.parent);
		parent.Children = parent.Children ?? [];
		parent.Children.push(node);
	} else {
		scene.GameObjects = scene.GameObjects ?? [];
		scene.GameObjects.push(node);
	}
	return node;
}

export function deleteGameObject(scene: SceneFile, guid: string): void {
	const arr = findParentArray(scene, guid);
	if (!arr) throw new MutateError("not_found", `GameObject not found: ${guid}`);
	const i = arr.findIndex((g) => g.__guid === guid);
	if (i < 0) throw new MutateError("not_found", `GameObject not found: ${guid}`);
	arr.splice(i, 1);
}

export interface ReparentInput {
	child_guid: string;
	new_parent?: { guid?: string; name_path?: string };
	keep_local_transform?: boolean;
}

export function reparentGameObject(scene: SceneFile, input: ReparentInput): void {
	const node = findGameObjectByGuid(scene, input.child_guid);
	if (!node) throw new MutateError("not_found", `GameObject not found: ${input.child_guid}`);

	let parent: GameObjectNode | null = null;
	if (input.new_parent && (input.new_parent.guid || input.new_parent.name_path)) {
		parent = resolveGameObject(scene, input.new_parent);
		if (parent.__guid === input.child_guid) {
			throw new MutateError("cycle", "cannot parent a GameObject to itself");
		}
		// Cycle: would-be parent is inside the moved subtree.
		for (const { go } of walkGameObjects({ ...scene, GameObjects: [node] })) {
			if (go.__guid === parent.__guid) {
				throw new MutateError("cycle", "reparent would create a cycle");
			}
		}
	}

	const fromArr = findParentArray(scene, input.child_guid)!;
	const i = fromArr.findIndex((g) => g.__guid === input.child_guid);
	fromArr.splice(i, 1);

	if (parent) {
		parent.Children = parent.Children ?? [];
		parent.Children.push(node);
	} else {
		scene.GameObjects = scene.GameObjects ?? [];
		scene.GameObjects.push(node);
	}
}

export interface SetTransformInput {
	guid: string;
	position?: string;
	rotation?: string;
	scale?: string;
}

export function setTransform(scene: SceneFile, input: SetTransformInput): void {
	const go = findGameObjectByGuid(scene, input.guid);
	if (!go) throw new MutateError("not_found", `GameObject not found: ${input.guid}`);
	if (input.position !== undefined) go.Position = input.position;
	if (input.rotation !== undefined) go.Rotation = input.rotation;
	if (input.scale !== undefined) go.Scale = input.scale;
}

export interface AddComponentInput {
	target: { guid?: string; name_path?: string };
	component_type: string;
	initial_properties?: Record<string, unknown>;
}

export function addComponent(
	scene: SceneFile,
	catalog: Catalog,
	input: AddComponentInput,
): ComponentNode {
	const go = resolveGameObject(scene, input.target);
	const schema = catalog.components[input.component_type];
	if (!schema) {
		throw new MutateError(
			"unknown_component",
			`Unknown component type: ${input.component_type}. Use list_components to see what's available.`,
		);
	}
	validateInitialProps(schema, input.initial_properties ?? {});

	const node: ComponentNode = {
		__type: schema.full_name,
		__guid: newGuid(),
		__enabled: true,
		Flags: 0,
		...(input.initial_properties ?? {}),
	};
	go.Components = go.Components ?? [];
	go.Components.push(node);
	return node;
}

export function removeComponent(scene: SceneFile, componentGuid: string): void {
	for (const { go } of walkGameObjects(scene)) {
		const arr = go.Components;
		if (!arr) continue;
		const i = arr.findIndex((c) => c.__guid === componentGuid);
		if (i >= 0) {
			arr.splice(i, 1);
			return;
		}
	}
	throw new MutateError("not_found", `Component not found: ${componentGuid}`);
}

export interface SetPropertyInput {
	component_guid: string;
	property: string;
	value: unknown;
}

export function setProperty(
	scene: SceneFile,
	catalog: Catalog,
	input: SetPropertyInput,
): void {
	const comp = findComponentByGuid(scene, input.component_guid);
	if (!comp) throw new MutateError("not_found", `Component not found: ${input.component_guid}`);
	const schema = catalog.components[comp.__type];
	if (!schema) {
		throw new MutateError("unknown_component", `Component type ${comp.__type} not in catalog.`);
	}
	if (!schema.properties[input.property]) {
		throw new MutateError(
			"unknown_property",
			`Property "${input.property}" not on ${comp.__type}. Use describe_component to see valid properties.`,
		);
	}
	comp[input.property] = input.value;
}

export interface SetPropertiesBulkInput {
	component_guid: string;
	properties: Record<string, unknown>;
}

export function setPropertiesBulk(
	scene: SceneFile,
	catalog: Catalog,
	input: SetPropertiesBulkInput,
): void {
	const comp = findComponentByGuid(scene, input.component_guid);
	if (!comp) throw new MutateError("not_found", `Component not found: ${input.component_guid}`);
	const schema = catalog.components[comp.__type];
	if (!schema) {
		throw new MutateError("unknown_component", `Component type ${comp.__type} not in catalog.`);
	}
	const unknownKeys = Object.keys(input.properties).filter((k) => !schema.properties[k]);
	if (unknownKeys.length) {
		throw new MutateError(
			"unknown_property",
			`Unknown properties on ${comp.__type}: ${unknownKeys.join(", ")}`,
		);
	}
	for (const [k, v] of Object.entries(input.properties)) comp[k] = v;
}

export function findComponentByGuid(scene: SceneFile, guid: string): ComponentNode | null {
	for (const { go } of walkGameObjects(scene)) {
		const m = (go.Components ?? []).find((c) => c.__guid === guid);
		if (m) return m;
	}
	return null;
}

function validateInitialProps(schema: ComponentSchema, props: Record<string, unknown>): void {
	const unknown = Object.keys(props).filter((k) => !schema.properties[k]);
	if (unknown.length) {
		throw new MutateError(
			"unknown_property",
			`Unknown properties on ${schema.full_name}: ${unknown.join(", ")}`,
		);
	}
}

// Suppress unused in some configs
void findParentOf;
