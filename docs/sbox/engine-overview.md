# s&box — Engine Overview

s&box is **scene-based**, not map-based like the original Source engine. If you've used Unity or Godot, the model will feel familiar.

## The scene system

### Scene

A **Scene** is your game world. Everything that renders and updates at one point in time lives inside a scene. Scenes:

- Are saved as JSON files on disk.
- Are fast to load and swap.
- Can be loaded additively (multiple scenes overlaid at once).

```csharp
var slo = new SceneLoadOptions();
slo.IsAdditive = true;
slo.SetScene( "scenes/engine.scene" );
Scene.Load( slo );
```

### GameObject

A **GameObject** is any world object — it has position, rotation, scale. GameObjects can have parents and children, and a child's transform is relative to its parent.

A GameObject by itself does nothing. You attach **Components** to give it behavior.

### Component

A **Component** provides modular functionality. Examples:

- `ModelRenderer` — renders a 3D model.
- `BoxCollider` / `SphereCollider` / `MeshCollider` — physics shapes.
- `Rigidbody` — physics simulation.
- `CameraComponent` — view into the scene.
- Your own custom components written in C#.

You build a game primarily by **writing custom Components** and arranging GameObjects in scenes. This is the same pattern as Unity's `MonoBehaviour` or Godot's nodes-with-scripts.

### Component lifecycle

```csharp
public sealed class MyComponent : Component
{
    protected override void OnAwake() { }       // Created
    protected override void OnStart() { }       // First frame active
    protected override void OnEnabled() { }     // Enabled (incl. on start)
    protected override void OnDisabled() { }
    protected override void OnUpdate() { }      // Every frame
    protected override void OnFixedUpdate() { } // Physics tick
    protected override void OnDestroy() { }
}
```

## Hotload

Save any `.cs` file in your project. The editor recompiles and reloads your changes within milliseconds — no editor restart, no scene reload, your running game state is preserved where possible. This is one of the engine's biggest productivity wins; lean on it.

## ActionGraph

A node-based **visual scripting** system. Use it for designer-friendly logic, prototyping, or bridging C# code to designer-tweakable graphs. ActionGraphs and C# interoperate freely.

## UI system

s&box's UI is built like **HTML with C# inside** — basically Blazor. You write Razor-flavored components and the engine renders them. Common for menus, HUDs, inventories.

## Game project structure

A **Game Project** is the top-level project type. It defines a **startup scene** in its project settings — that's the first scene loaded when the game launches. Common shapes:

- Game launches → main menu scene → user picks "Play" → menu loads game scene.
- Game launches → straight into the game scene.
- Game launches → loading screen → loads a map (which is itself a scene).

### Maps and managers

If your game can load maps from a menu, the map replaces your startup scene — meaning the GameObjects you wired up there don't exist any more. To re-spawn HUD, game manager, etc., create a `GameObjectSystem` that runs on host init and additively loads your engine scene:

```csharp
public sealed class GrappleShipManager : GameObjectSystem<GrappleShipManager>, ISceneStartup
{
    public GrappleShipManager( Scene scene ) : base( scene ) { }

    void ISceneStartup.OnHostInitialize()
    {
        var slo = new SceneLoadOptions();
        slo.IsAdditive = true;
        slo.SetScene( "scenes/engine.scene" );
        Scene.Load( slo );
    }
}
```

## The testbed

Facepunch ships a game called **`testbed`** alongside s&box. Launch the *game* (not the editor) and pick `testbed` to see scenes that demonstrate engine features in isolation. You can clone the source and open it in the editor to read the code:

```bash
git clone https://github.com/Facepunch/sbox-scenestaging.git
```

Then open the `.sbproj`. The Asset Browser inside the editor will show every scene; double-click to enter and edit.

## Major engine subsystems (live docs)

You'll touch these as your game grows. Each is its own section in the official docs:

- **[Code](https://sbox.game/dev/doc/code/index.md)** — basics, libraries, advanced topics, hotload internals.
- **[Editor](https://sbox.game/dev/doc/editor/index.md)** — mapping (Hammer), model editor, ActionGraph, custom tools.
- **[Gameplay](https://sbox.game/dev/doc/gameplay/index.md)** — input (kb/mouse/controller/raw), navigation (navmesh), terrain, VR, clutter.
- **[Assets](https://sbox.game/dev/doc/assets/index.md)** — file system, UGC storage, custom resources, citizen characters, FP weapons, clothing.
- **[Animation](https://sbox.game/dev/doc/animation/index.md)** — animgraph, blending.
- **[Media](https://sbox.game/dev/doc/media/index.md)** — audio, video.
- **[Movie Maker](https://sbox.game/dev/doc/movie-maker/getting-started.md)** — in-engine cinematics.
- **Networking, Rendering** — see the live docs index.

## Common patterns to know about

- **Find a GameObject by name:** `Scene.Directory.FindByName( "Player" ).First()`
- **Get every component of a type in the scene:** `Scene.GetAll<CameraComponent>()`
- **Tag-based queries:** `go.Tags.Add("player")`, then filter elsewhere.
- **Cloning prefabs at runtime:** `var newGo = template.Clone()`.

See [cheat-sheet.md](cheat-sheet.md) for the full list.
