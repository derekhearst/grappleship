# GrappleShip — project instructions for Claude

**One-line:** s&box session-based PvP. Pirate boarding crews on hoverships. Vertical contested loot POIs. Read [docs/grappleship/grappleship.md](docs/grappleship/grappleship.md) for the full design and the 10-phase build order.

This file is a **living document.** Update it whenever a workflow rule, lesson, or convention emerges. Future sessions read this first — the more we capture here, the less we re-litigate.

---

## Status

- **Current phase:** Phase 1 — Player grapple feel.
- **What exists in code:** `DebugTuning` (live tunables), `GrappleHook` (fire/anchor/reel/release/stamina). `MyComponent.cs` from the s&box template is empty and unused.
- **Player controller:** s&box's **built-in** `Sandbox.PlayerController` (with `MoveModeWalk`). Not `facepunch.playercontroller` — that's installed as a library but unused. May remove later.
- **Scene:** `Assets/scenes/minimal.scene`. Plane + 3 cubes (grapple targets) + Sun + Skybox + Player + Camera + DebugTuning singleton.

---

## Tech stack

- **Engine:** s&box (Source 2 + .NET / C#), released 2026-04-28.
- **Build:** Hot-reloaded by s&box editor on file save. No separate build step.
- **Input config:** `grappleship/ProjectSettings/Input.config`. Add custom actions there, don't bind raw keys in code.
- **Bridge:** [Sbox-Claude](https://github.com/LouSputthole/Sbox-Claude) MCP server lets Claude drive the editor. Useful but buggy — see "Bridge gotchas" below.

---

## Workflow conventions

### Code changes

- **Always edit `.cs` files directly** with `Write`/`Edit`. Never use the bridge's `create_script` — it ignores the `content` parameter and writes empty stubs.
- After saving, the s&box editor hotloads in milliseconds. No manual rebuild needed.
- If you need to confirm hotload picked something up, call `mcp__sbox__trigger_hotload` — that one works.

### Scene structure changes

Two paths, pick by case:

| Change | Path |
|---|---|
| Create GameObject, set parent, add component (no value tweaks), assign model, add physics | **Bridge** — these tools are reliable. |
| Set a bool / number / `Vector3` / component-reference property | **Edit `Assets/scenes/minimal.scene` directly** OR ask the user to drag/check in the inspector. The bridge's `set_property` only accepts string values; passing `false`/`0`/component refs errors with `'requires String, but target has type False/Number'`. |
| Reload scene from disk after a manual edit | The bridge's `load_scene` does **not** force-discard the editor's in-memory state. Ask the user to reload, or include the change in the next `save_scene` flow carefully. |
| Inspect what's on a component | `mcp__sbox__get_all_properties` — works perfectly, names + types + values. **Always use this before guessing an API.** |

### Discovering APIs

Before writing code that touches any unfamiliar s&box type, call `mcp__sbox__describe_type` and/or `mcp__sbox__search_types`. This is how we learned that `Sandbox.PlayerController` is the built-in (not `facepunch.playercontroller`'s) and that `Sandbox.Input` only takes registered action names — not raw key names.

### Stuff Claude can't do; ask the user

- Toggling bool properties on components (bridge limitation — `false` errors out).
- Wiring component-reference properties (e.g. `Renderer`, `Target`) — bridge accepts the value but doesn't always persist.
- Anything that requires the editor's "Project Settings" UI (input rebinds done via UI, prefab creation, etc.).
- Confirming "this feels good" — Phase 1 is all about feel, only the human can judge that.

---

## Bridge gotchas

Found through pain. If you hit one of these, don't burn time on it — work around.

| Tool | Bug | Workaround |
|---|---|---|
| `create_script` | Ignores `content` param, writes empty stub. | Use `Write` directly to `Code/<Name>.cs`. |
| `set_property` | Only accepts string values. Bool / number / component-ref → "requires String" error. Strings work. | Edit scene JSON directly OR ask user to set in inspector. |
| `load_scene` | Doesn't discard the editor's in-memory state when scene is already loaded. Disk edits are silently ignored. | Ask user to reload scene manually (File → Open Recent), or restart editor. |
| `get_compile_errors` | Returns "Unknown command". | Use `list_available_components` with a filter — if the component shows up, it compiled. |
| `add_component_with_properties` | Sometimes auto-spawns dependent components (e.g. adding `CharacterController` adds `Rigidbody` + `MoveModeWalk` + a `Colliders` child). Not a bug per se, just surprising. | Inspect with `get_scene_hierarchy` after, expect extras. |

The static-ctor bug in the bridge addon was already patched locally in `grappleship/Libraries/sboxskinsgg.claudebridge/Editor/MyEditorMenu.cs` lines 33–55. The marker comment is there — don't undo it.

---

## Code conventions

### Component file template

```csharp
using Sandbox;
// other usings as needed

namespace GrappleShip;

/// <summary>
/// One-paragraph description: what this component does and where it lives.
/// </summary>
[Title( "Pretty Name" )]
[Category( "GrappleShip" )]
[Icon( "icon_name" )]   // material icon names
public sealed class MyThing : Component
{
    [Property] public Foo SomeRef { get; set; }
    [Property, Group( "Tuning" )] public float SomeNumber { get; set; } = 42f;
    [Property, ReadOnly] public bool SomeRuntimeFlag { get; set; }   // visible in inspector while playing

    protected override void OnStart() { /* one-time init */ }
    protected override void OnUpdate() { /* every frame */ }
    protected override void OnFixedUpdate() { /* physics tick */ }
}
```

- Namespace: `GrappleShip` for everything. Keep it flat for now; sub-namespaces if it grows.
- Always set `[Title]`, `[Category]` (= `"GrappleShip"`), `[Icon]`. Makes the inspector readable and groups our components together.
- Use `[Group("...")]` to organize `[Property]` fields.
- Use `[ReadOnly]` for fields you want visible in the inspector for debugging but never settable.
- Prefer `sealed` unless we explicitly need inheritance.

### DebugTuning singleton pattern

Tunable values that multiple components read live on a single `DebugTuning` GameObject in the scene. Components query it via `DebugTuning.GetCurrent(Scene)`. This lets us tweak grapple/movement/etc. parameters live in the editor while the game runs without recompiling.

When adding a new tunable, add it to `Code/DebugTuning.cs` with a sensible default and a `[Group(...)]`.

### Component vs GameObject references

- For **same-GameObject** dependencies (e.g. `GrappleHook` needs `PlayerController` on the same object), use `GetComponent<T>()` in `OnStart` with `??=` so an inspector override still wins.
- For **scene-wide** singletons (DebugTuning), use `Scene.GetAll<T>().FirstOrDefault()` — see the cheat sheet at [docs/sbox/cheat-sheet.md](docs/sbox/cheat-sheet.md).

### When in doubt, follow the cheat sheet

[docs/sbox/cheat-sheet.md](docs/sbox/cheat-sheet.md) has the canonical s&box C# API snippets. If a pattern isn't there and you have to look it up, **add it to the cheat sheet** afterwards.

---

## Input bindings (Phase 1 — live)

Defined in [grappleship/ProjectSettings/Input.config](grappleship/ProjectSettings/Input.config) under the `Grapple` group:

| Key | Action name | Behavior |
|---|---|---|
| `F` | `GrappleFire` | Fire grapple / release if attached |
| `E` | `GrappleReelIn` | Reel in (drains stamina) |
| `Q` | `GrappleReelOut` | Reel out / loosen (free) |

**LMB and RMB are off-limits.** Reserved for melee combat (Phase 2: directional swings + blocks) and ship cannons / aiming (Phase 6).

We cleared the default `Menu` action's keyboard binding (was on Q) to avoid the conflict. Esc is engine-level and still works for stop-play.

When adding a new in-game input, **always** declare it as a real action in `Input.config` with a `GroupName` matching its layer:

- `GroupName: "Grapple"` — player grapple (Phase 1)
- `GroupName: "Combat"` — melee (Phase 2)
- `GroupName: "Ship"` — hovership (Phase 4–6)
- `GroupName: "Crew"` — crew/role (Phase 7+)

Then read it via `Input.Down("ActionName")` / `Input.Pressed("ActionName")`. **Do not pass raw key strings** — `Input.Down("Q")` returns false; the engine doesn't fall through to keyboard origins. We learned this the hard way.

---

## Scene editing conventions

- The scene is a JSON file at `grappleship/Assets/scenes/minimal.scene`. Reading and surgical-editing it via `Edit` is fine and sometimes the only way (see "set_property bug" above).
- Component refs in the scene file are GUID strings, **not** wrapper objects.
- After a direct-disk scene edit, ask the user to reload the scene in the editor (File → Open Recent → minimal.scene). `mcp__sbox__load_scene` is unreliable for this.
- Don't `mcp__sbox__save_scene` after a direct-disk edit — it'll overwrite your edit with the in-memory state.

---

## Known issues / live observations

- **`facepunch.playercontroller` library is installed but unused.** When I added a "PlayerController" component, s&box matched the built-in `Sandbox.PlayerController` (newer, has MoveMode system, ThirdPerson toggle, etc.). The FP library has obsolete-API warnings (`Transform.Position` → `WorldPosition`, etc.) — not an error, just noise. Likely safe to remove the library.
- **Bridge addon has a fragile static constructor.** Patched locally; flagged for upstream report. See `grappleship/Libraries/sboxskinsgg.claudebridge/Editor/MyEditorMenu.cs` lines 33–55.
- **`PlayerController.ThirdPerson` defaults to `true`** in the built-in. Always check this on a fresh Player setup.
- **`PlayerController.Renderer` (SkinnedModelRenderer slot)** must be wired to the citizen body GameObject for `HideBodyInFirstPerson` to work. Bridge can't reliably set this — drag in the inspector.

---

## Lessons learned (running list)

Append as we hit them. Don't delete — even outdated lessons explain why something is the way it is.

- **2026-04-28** — `Sandbox.PlayerController` is the built-in, distinct from `facepunch.playercontroller.PlayerController`. They're API-incompatible. The built-in is what s&box matches when you add a "PlayerController" component.
- **2026-04-28** — `Input.Down(string)` only accepts registered action names. Add custom actions to `ProjectSettings/Input.config`. Raw key strings (`"Q"`, `"q"`) silently return false.
- **2026-04-28** — When applying rope tension, apply force to the anchor object **whenever the rope is taut**, not only when the player is moving away. Otherwise the snap-back makes the cube force a one-frame ghost — the cube never actually moves.
- **2026-04-28** — The s&box editor's in-memory state survives `load_scene`. Disk-edit-then-reload is racy; prefer in-memory edits via the bridge OR ask the user to reload manually.
- **2026-04-28** — `facepunch.playercontroller` declares its `PlayerController` class **in the global namespace** (no `namespace` keyword). When you write `[Property] public PlayerController Pc` in any of our files, C# resolves to that one — not `Sandbox.PlayerController`. Always fully-qualify as `Sandbox.PlayerController` when you mean the built-in (or remove the FP library).
- **2026-04-28** — `Rotation.Inverse` is a **property** on a Rotation instance, not a static method. Use `someRotation.Inverse`, not `Rotation.Inverse(someRotation)`.
- **2026-04-28** — When code fails to compile, hotload silently falls back to the **previously-working version**. Symptoms: changes don't appear to take effect. Always check the editor's Console for `Compiler CS####` errors after a code change.
- **2026-04-28** — `Sandbox.PlayerController.Velocity` is **read-only**. The writable surface is `Sandbox.CharacterController.Velocity` (or use `CharacterController.Punch(Vector3)` for impulses). PlayerController's Velocity is just a getter. To apply grapple force / external impulses, hold a `[Property] public CharacterController Cc` reference and write to `Cc.Velocity`. Use `mcp__sbox__describe_type` (with `canWrite` field) before assuming a property is settable.
- **2026-04-28** — Default Rigidbody mass on physics props can be 500–1000 kg (cubes use `MassOverride: 500`). To make grapple-pull on cubes feel snappy, the force needs to be much higher than what we use for the player constraint — exposed as a separate `ObjectPullForce` on DebugTuning (default 8000 N). Also `Sleeping = false` before applying force, otherwise a sleeping body ignores small forces.
- **2026-04-28** — Inspector sliders come from `[Range(min, max)]` on a `[Property]`. Add it to numeric tunables for live-tweaking. Bools and strings don't need it.

---

## How to keep this updated

When you (Claude) discover a new gotcha, convention, or fact about s&box / the bridge / this codebase that future-you would benefit from knowing:

1. Add a one-liner to **Lessons learned** with today's date (use the `currentDate` from session context).
2. If it's a *rule* (something we should always do or never do), add it to the relevant section above (workflow / code / input / scene).
3. If a known issue gets *fixed*, move it to "Lessons learned" with a "(resolved)" note. Don't silently delete — knowing the fix matters.

Aim for terseness. A line is better than a paragraph. The goal is "future Claude reads this in 30 seconds and knows the lay of the land."
