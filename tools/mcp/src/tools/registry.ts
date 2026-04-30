import type { ServerConfig } from "../config";
import { describeAsset, findAsset, listAssets, validateAssetPath } from "./assets";
import { installPackage, pingEditor, refreshBuiltinSchema } from "./bridge";
import { readLogTool, watchLogTool } from "./logs";
import {
	addComponentTool,
	createGameObjectTool,
	deleteGameObjectTool,
	removeComponentTool,
	reparentGameObjectTool,
	setPropertiesBulkTool,
	setPropertyTool,
	setTransformTool,
} from "./mutate";
import { createPrefabFromGameObject, instantiatePrefab, listPrefabs, readPrefabTool } from "./prefabs";
import { describeComponent, listComponents, refreshSchema, searchProperty } from "./schema";
import { getGameObject, listScenes, readSceneTool, validateSceneTool } from "./scene";

export interface ToolDef {
	name: string;
	description: string;
	inputSchema: object;
	run: (args: Record<string, unknown>, cfg: ServerConfig) => Promise<unknown>;
}

export const tools: ToolDef[] = [
	// Schema / discovery
	listComponents,
	describeComponent,
	searchProperty,
	refreshSchema,
	// Logs
	readLogTool,
	watchLogTool,
	// Editor bridge
	pingEditor,
	refreshBuiltinSchema,
	installPackage,
	// Scene read
	listScenes,
	readSceneTool,
	getGameObject,
	validateSceneTool,
	// Scene mutate
	createGameObjectTool,
	deleteGameObjectTool,
	reparentGameObjectTool,
	setTransformTool,
	addComponentTool,
	removeComponentTool,
	setPropertyTool,
	setPropertiesBulkTool,
	// Assets
	listAssets,
	findAsset,
	describeAsset,
	validateAssetPath,
	// Prefabs
	listPrefabs,
	readPrefabTool,
	instantiatePrefab,
	createPrefabFromGameObject,
];
