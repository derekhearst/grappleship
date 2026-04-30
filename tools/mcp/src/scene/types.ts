export interface SceneFile {
	__guid: string;
	GameObjects: GameObjectNode[];
	[k: string]: unknown;
}

export interface GameObjectNode {
	__guid: string;
	Name?: string;
	Position?: string;
	Rotation?: string;
	Scale?: string;
	Tags?: string;
	Enabled?: boolean;
	Components?: ComponentNode[];
	Children?: GameObjectNode[];
	[k: string]: unknown;
}

export interface ComponentNode {
	__type: string;
	__guid: string;
	__enabled?: boolean;
	[k: string]: unknown;
}

export interface ComponentRef {
	_type: "component";
	component_id: string;
	go: string;
	component_type?: string;
}

export interface GameObjectRef {
	_type: "gameobject";
	go: string;
}
