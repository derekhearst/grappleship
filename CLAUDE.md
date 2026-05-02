# GrappleShip — project instructions for Claude

**One-line:** s&box session-based PvP. Pirate boarding crews on hoverships. Vertical contested loot POIs. Read [docs/grappleship/grappleship.md](docs/grappleship/grappleship.md) for the full design and the 10-phase build order.

This file is a **living document.** Update it whenever a workflow rule, lesson, or convention emerges. Future sessions read this first — the more we capture here, the less we re-litigate.

---

## Status

- **Current phase:** Phase 1 — Player grapple feel.
- **What exists in code:** `GrappleHook` (fire/anchor/reel/release/stamina). All tunables live as `[Property]` fields directly on the component.
- **Player controller:** s&box's **built-in** `Sandbox.PlayerController` (with `MoveModeWalk`).
- **Scene:** `Assets/scenes/minimal.scene`. Plane + 3 cubes (grapple targets) + Sun + Skybox + Player + Camera.

---

## Tech stack

- **Engine:** s&box (Source 2 + .NET / C#), released 2026-04-28.
- **Build:** Hot-reloaded by s&box editor on file save. No separate build step.
- **Input config:** `grappleship/ProjectSettings/Input.config`. Add custom actions there, don't bind raw keys in code.

---

## Workflow conventions

### Code changes

- **Always edit `.cs` files directly** with `Write`/`Edit`.
- After saving, the s&box editor hotloads in milliseconds. No manual rebuild needed.

### Scene / asset / prefab work — use the GrappleShip MCP

The `grappleship` MCP server (in [tools/mcp/](tools/mcp/), auto-started via [.mcp.json](.mcp.json)) is the canonical interface for scene work. **Always prefer MCP tools over hand-editing `.scene` JSON.** Full tool reference: [docs/mcp/mcp.md](docs/mcp/mcp.md).

Workflow:

1. `list_components` / `describe_component` first — schema is authoritative; never guess property names.
2. `read_scene` / `get_gameobject` to understand current state.
3. Use mutation tools (`add_component`, `set_property`, etc.) — every mutation is auto-validated; the file is only written if validation passes clean.
4. After saving, ask the user to reload the scene in the editor.

If `validate_scene` flags `Sandbox.*` components as unknown, the built-in catalog is stale — run the `refresh_builtin_schema` tool (needs the editor open) or ask the user to use the editor menu **Editor → GrappleShip → Refresh Built-in Type Schema**. Then commit `docs/sbox/builtin-types.json`. Custom `GrappleShip.*` components are read live from `.cs` source; no refresh step needed.

After C# edits, use `read_log` / `watch_log` with `level: ["Error", "Warning"]` to check `grappleship/logs/sbox-dev.log` for compile errors before assuming the change took effect (silent compile failure → silent revert is a known s&box gotcha — see Lessons).

### Discovering APIs

Before writing code that touches any unfamiliar s&box type, check the [cheat sheet](docs/sbox/cheat-sheet.md) first. If a pattern isn't there, look it up and add it.

### Stuff Claude can't do; ask the user

- Wiring component-reference properties (e.g. `Renderer`, `Target`) in the inspector.
- Anything that requires the editor's "Project Settings" UI (input rebinds done via UI, prefab creation, etc.).
- Confirming "this feels good" — Phase 1 is all about feel, only the human can judge that.

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

### Tunables on the component itself

Put `[Property, Range(...)]` tunable values **directly on the component that uses them**, grouped with `[Group("Tuning — ...")]`. They show up as sliders in the inspector when the user clicks that component — no separate GameObject to hunt for.

The earlier `DebugTuning` singleton pattern (separate scene-wide GameObject) was confusing in practice — users intuitively look for grapple knobs on the GrappleHook component, not on a separate scene object. Removed entirely on 2026-04-30.

When values are shared across multiple components (later: ship hover values read by both helmsman input and the hover physics), prefer a small dedicated component for that shared concern, not a generic catch-all.

### Component vs GameObject references

- For **same-GameObject** dependencies (e.g. `GrappleHook` needs `PlayerController` on the same object), use `GetComponent<T>()` in `OnStart` with `??=` so an inspector override still wins.
- For **scene-wide** singletons, use `Scene.GetAll<T>().FirstOrDefault()` — see the cheat sheet at [docs/sbox/cheat-sheet.md](docs/sbox/cheat-sheet.md).

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

- The scene is a JSON file at `grappleship/Assets/scenes/minimal.scene`. Reading and surgical-editing it via `Edit` is fine.
- Component refs in the scene file are GUID strings, **not** wrapper objects.
- After a direct-disk scene edit, ask the user to reload the scene in the editor (File → Open Recent → minimal.scene).

---

## Known issues / live observations

- **`PlayerController.ThirdPerson` defaults to `true`** in the built-in. Always check this on a fresh Player setup.
- **`PlayerController.Renderer` (SkinnedModelRenderer slot)** must be wired to the citizen body GameObject for `HideBodyInFirstPerson` to work. Drag in the inspector.

---

## Lessons learned (running list)

Append as we hit them. Don't delete — even outdated lessons explain why something is the way it is.

- **2026-04-28** — `Sandbox.PlayerController` is the built-in, distinct from `facepunch.playercontroller.PlayerController`. They're API-incompatible. The built-in is what s&box matches when you add a "PlayerController" component.
- **2026-04-28** — `Input.Down(string)` only accepts registered action names. Add custom actions to `ProjectSettings/Input.config`. Raw key strings (`"Q"`, `"q"`) silently return false.
- **2026-04-28** — When applying rope tension, apply force to the anchor object **whenever the rope is taut**, not only when the player is moving away. Otherwise the snap-back makes the cube force a one-frame ghost — the cube never actually moves.
- **2026-04-28** — The s&box editor's in-memory state doesn't auto-refresh from disk. After editing a scene file directly, ask the user to reload manually.
- **2026-04-28** — `facepunch.playercontroller` declares its `PlayerController` class **in the global namespace** (no `namespace` keyword). When you write `[Property] public PlayerController Pc` in any of our files, C# resolves to that one — not `Sandbox.PlayerController`. Always fully-qualify as `Sandbox.PlayerController` when you mean the built-in (resolved 2026-04-30 by removing the FP library).
- **2026-04-28** — `Rotation.Inverse` is a **property** on a Rotation instance, not a static method. Use `someRotation.Inverse`, not `Rotation.Inverse(someRotation)`.
- **2026-04-28** — When code fails to compile, hotload silently falls back to the **previously-working version**. Symptoms: changes don't appear to take effect. Always check the editor's Console for `Compiler CS####` errors after a code change.
- **2026-04-28** — `Sandbox.PlayerController.Velocity` is **read-only**. The writable surface is `Sandbox.CharacterController.Velocity` (or use `CharacterController.Punch(Vector3)` for impulses). PlayerController's Velocity is just a getter. To apply grapple force / external impulses, hold a `[Property] public CharacterController Cc` reference and write to `Cc.Velocity`. Check the s&box docs or cheat sheet before assuming a property is settable.
- **2026-04-28** — Default Rigidbody mass on physics props can be 500–1000 kg (cubes use `MassOverride: 500`). To make grapple-pull on cubes feel snappy, the force needs to be much higher than what we use for the player constraint — exposed as a separate `ObjectPullForce` on DebugTuning (default 8000 N). Also `Sleeping = false` before applying force, otherwise a sleeping body ignores small forces.
- **2026-04-28** — Inspector sliders come from `[Range(min, max)]` on a `[Property]`. Add it to numeric tunables for live-tweaking. Bools and strings don't need it.
- **2026-04-29** — Removed `sboxskinsgg.claudebridge` MCP bridge (too buggy/outdated). Scene edits go through direct file editing or user in the inspector.
- **2026-04-29** — When applying tension force to a `Rigidbody` resting on the floor, **`+=` on `PhysicsBody.Velocity` gets clobbered between fixed updates** by the contact-constraint solver — even huge forces (200,000 N for a 732 kg cube) saw velocity decay from 5.46 → 0.01 in one tick. The "fix" we tried (set Velocity directly + disable gravity on the cube) made it feel weird (zero-g cube floating around, snapping at max length). Superseded by the next entry.
- **2026-04-29** — **Don't reinvent rope physics.** For a tethered grapple, use a **positional distance constraint** (Branchpanic's [sbox-grapple](https://github.com/branchpanic/sbox-grapple) pattern): each tick, predict `goal = pos + vel * dt`, clamp goal to within `ropeLength` of the anchor, then set `vel = (goal - pos) / dt`. Apply on both ends. Gravity stays on for both — the rope only acts when taut, so bodies swing naturally instead of being "force-pulled". This replaces all the `Cc.Velocity += dir * F * dt` and `hitRb.Gravity = false` hacks we accumulated.
- **2026-04-29** — **Zero a Rigidbody's `Velocity` and `AngularVelocity` on grapple-fire.** Otherwise stale residual motion (cube was rolling, settling, etc.) makes the very first constraint pass project the cube hard toward the player, looking like an instant teleport. Reset both Rigidbody and PhysicsBody copies on hit.
- **2026-04-29** — **Pendulum swing requires the anchor above or laterally offset from the player.** Grappling the floor below you and jumping won't produce swing — projection just bounces you off a sphere centered at your feet. The minimal scene needs a tall pillar / overhead anchor to actually feel the rope swing.
- **2026-04-29** — **GrappleShip MCP server is live** at `tools/mcp/`, auto-started via [.mcp.json](.mcp.json). 28 tools cover schema introspection, scene CRUD (validated), assets, prefabs, log tailing, and an editor IPC bridge. Schema layer reads parsed `.cs` source live + a committed `docs/sbox/builtin-types.json` for `Sandbox.*` types. Full reference: [docs/mcp/mcp.md](docs/mcp/mcp.md). Architectural decision was to keep the MCP file-based (no live editor dependency) so contributors can work with the editor closed; the bridge only exists for the few things that genuinely need engine reflection (built-in schema export, future button-press triggers).
- **2026-04-29** — **`[Property]` attribute readers must be reflection-tolerant.** s&box's `RangeAttribute`, `GroupAttribute`, etc. don't have stable, documented public field names — early `SchemaExporter.cs` versions used `GroupAttribute.Name` and failed to compile. Fix: read the first matching string field on the attribute via `System.Reflection`, regardless of what it's actually called. Same pattern for `Min`/`Max` on Range. Don't assume the public surface; let reflection find it.
- **2026-04-29** — **Editor-side code must avoid `System.Threading.*` and static constructors that touch the editor.** First `EditorBridge.cs` used a `System.Threading.Timer` and a `static EditorBridge()` ctor — crashed the s&box editor on load. Fix: do everything inside `[EditorEvent.Frame]` (single-threaded, only fires when the editor is ready), throttled to every Nth frame so it's effectively free. No timers, no concurrent queues, no class-load-time work.
- **2026-04-29** — **`Editor.FileSystem.Content.GetFullPath("/")` is deeper than the project root.** First `SchemaExporter.ResolveOutputPath()` walked up one level and dumped the schema at `grappleship/docs/sbox/builtin-types.json`, not the repo root. Fix: walk up looking for a marker file (`.mcp.json`) instead of guessing the layout.
- **2026-04-29** — **Scene files serialize properties that `TypeLibrary.Properties` doesn't expose** (e.g. `ModelRenderer.LodOverride`, `EnvmapProbe.BakedTexture`, `CameraComponent.RenderTarget`). The exporter must do two passes: TypeLibrary first, then `Type.GetProperties(BindingFlags.Public | Instance | FlattenHierarchy)` to catch inherited / engine-internal members. Don't filter on `[Hide]` either — hidden properties still serialize.
- **2026-04-29** — **Flag enums are serialized as either named-string members OR integers** depending on the property — `RigidbodyFlags: "DisableCollisionSounds"` and `ColliderFlags: 0` both appear in real scenes. Tag flag enums (`[Flags]` attribute) as a permissive `unknown:flags:<TypeName>` so the validator passes both forms. Scalar enums stay strict.
- **2026-04-29** — **Property names in saved scenes can drift from current engine names.** `Sandbox.CameraComponent` used to expose `RenderTexture` but the current engine has `RenderTarget` — old scenes still write the old name. Treat these as real validation errors to flag for cleanup, not as schema bugs. The MCP catalog reflects the *current* engine; saved scenes can be stale.
- **2026-04-29** — **`sbox-dev.log` lives in the Steam install dir, not the project.** On this machine it's at `C:\Program Files (x86)\Steam\steamapps\common\sbox\logs\sbox-dev.log`, but Steam library locations vary per machine. The MCP discovers it at runtime via the `get_log_path` bridge action (`AppDomain.CurrentDomain.BaseDirectory` walked upwards looking for `logs/sbox-dev.log`). Override with `GRAPPLESHIP_SBOX_LOG_PATH` env var if needed.
- **2026-04-30** — **Asset search is live via the bridge, not a static dump.** First pass dumped `Editor.AssetSystem.All` (~8,270 assets) into a committed JSON; that was wrong-shaped — stale, didn't include the cloud browser, and 1.1MB to commit. Replaced with a `search_assets` bridge action that queries `AssetSystem.All` on demand and returns ranked matches. The MCP's `find_asset` / `list_assets` / `describe_asset` / `validate_asset_path` call it live; they fall back to a project-only walk when the editor isn't running.
- **2026-04-30** — **Cloud-catalog search via `Sandbox.Package.FindAsync`.** Found by probing for HTTP-related types in editor assemblies. Signature: `FindAsync(query, take, skip, ct) -> Task<Package.FindResult>`, where `FindResult.Packages` is `RemotePackage[]` (each has `Title`, `FullIdent`, `PrimaryAsset`, `TypeName`). Cheap `RemotePackage` shape mirrors `Package`. Awaiting via `GetAwaiter().GetResult()` reflectively works for both `Task<T>` and `ValueTask<T>` — don't use `.Wait()` (no return value). Search results have `path: ""` because there's no on-disk asset until install. Setting `include_cloud=true` on `find_asset` enables this path; default off because the round-trip is network-dependent.
- **2026-04-30** — **Library Manager display name lives on `Package.Title`, not `Asset.Name`.** A workshop-installed asset like "Ship Large" has `Asset.Name = "ship-large"` (file-stem-ish) and `Asset.Package.Title = "Ship Large"` (human-readable). `AssetSearcher` must score against path + asset name + package title + package ident to match what users type. Also boost score by +10 when path matches `Package.PrimaryAsset` so the canonical entry-point (e.g. the `.vmdl`, not its derived textures) sorts first. And filter `.generated.*` paths plus `IsTrivialChild` assets — they're internal/derived and clutter results.
- **2026-04-30** — **Cloud package install via `Editor.AssetSystem.InstallAsync(string, bool, Action<float>, ct)`.** Returns `Task<Editor.NativeAsset>` — the primary Asset of the installed package, NOT a Package. Read `result.Path` for the primary asset path and `result.Package.Title` for the human name. Install for `arghbeef.vikinghelmet` took ~24s and added 10 files to the engine catalog (8293 → 8303 assets). After install, `find_asset` finds the package immediately — no separate refresh needed.
- **2026-05-01** — **Terrain renders engine-magenta when a `.tmat_c` exists but holds empty texture inputs.** Symptom: `[engine/TextureCompiler] Error reading texture "" for "color" when compiling file "...dunes_sand_tmat_bcr.generated.vtex" — [FAIL]` for every recompile attempt. The `.tmat` on disk has correct `AlbedoImage`, the in-memory `Sandbox.TerrainMaterial` GameResource also reads correct paths, but compile artifacts get baked with empty inputs from a stale upstream pipeline state. **`Asset.Compile(true)` returns `true` even when texture inputs were empty** — the material compiler succeeds while the texture compiler fails silently. New `compile_asset_verified` MCP tool greps the post-compile log for the FAIL pattern; use it instead of bare `compile_asset` for materials. Once we forced fresh recompiles via the bridge with no stale state in scope, the vtex_c emitted real 270–315 KB textures.
- **2026-05-01** — **`Editor.Asset` has no `Reload()` / `Refresh()` / `UpdateState()`.** To force a registered asset to drop its cached GameResource and reread from disk: call `asset.SetInMemoryReplacement(asset.ReadJson())` then `asset.ClearInMemoryReplacement()` — round-trips disk JSON through the parser, refreshing the cache. Wired up as the `reload_asset` MCP tool. Also: the two parameterless `LoadResource()` overloads (one returns `Sandbox.Resource`, one returns generic `T`) collide via reflection — use the unambiguous `LoadResource(Type)` overload with `typeof(Sandbox.GameResource)`.
- **2026-05-01** — **The terrain ControlMap (splatmap) is NOT per-layer weights — it's a `CompactTerrainMaterial` packed `uint32`.** Bit layout (from `addons/base/Assets/shaders/terrain/TerrainSplatFormat.hlsl`): bits 0–4 = `BaseTextureId` (5 bits, 0–31, material index), bits 5–9 = `OverlayTextureId` (5 bits), bits 10–17 = `BlendFactor` (8 bits, 0=full base, 255=full overlay), bit 18 = `IsHole`. Each pixel selects TWO materials by index and blends between them — it doesn't store per-layer weights. Writing the obvious-but-wrong layout (`b0 = layer-0 weight, b1 = layer-1 weight`) sets `BaseTextureId = 31`, `OverlayTextureId = 7`, etc. — material indices that don't exist → engine renders error magenta. To paint solid material `i`, write `(uint)(i & 0x1F) | ((uint)(i & 0x1F) << 5)`. To blend material `a` over `b`, write `a | (b << 5) | (blend << 10)`. The official paint tool (`addons/tools/.../PaintTextureTool.cs`) writes through a compute shader (`terrain/cs_terrain_splat`) — direct CPU writes to `Storage.ControlMap` work but must use this format.
- **2026-05-01** — **Use `asset.TryLoadResource<TerrainMaterial>(out var material)` (the generic version) when adding terrain materials — NOT the parameterless `LoadResource()`.** The parameterless overload is ambiguous via reflection (matches both the non-generic `LoadResource() -> Resource` and the generic `LoadResource<T>() -> T`) and may return a different cached instance that isn't bound to the engine's GPU material pipeline. The official `addons/tools/.../TerrainMaterialList.cs` AddMaterials path uses the generic + `Terrain.UpdateMaterialsBuffer()` and nothing else — no `Create()`, no `SyncGPUTexture()`. Our `AddTerrainMaterial` bridge action now uses `TryLoadResource<TerrainMaterial>` via reflection on a `MakeGenericMethod`-built MethodInfo.
- **2026-05-01** — **A `Sandbox.TerrainMaterial` loaded via `Asset.LoadResource()` will have null `BCRTexture`/`NHOTexture` handles unless its parent `.tmat_c` was compiled in the same scope.** Symptom: terrain renders purple even though Storage.Materials has both layers and `inspect_tmat` shows correct AlbedoImage paths in memory. The shader samples `BCRTexture`/`NHOTexture`, not the path strings — and those GPU-side handles only get populated on the in-memory TerrainMaterial when you call `Asset.Compile(true)` immediately before `LoadResource()`. `AddTerrainMaterial` now does this preemptively. Also added an `inspect_terrains` bridge action that reports `BCRTexture_state: valid=True loaded=True 512x512` (or `null`) per layer — that's the diagnostic you want when "tmat looks fine but terrain is purple."
- **2026-05-01** — **Always write `.tmat` (and other GameResource) source JSON to disk *before* `AssetSystem.RegisterFile`.** The earlier `CreateTerrainMaterial` pattern wrote `"{}"` first, registered, then `Asset.SaveToDisk(populatedInstance)`. SaveToDisk updated the file but the engine's first-parse-from-`{}` cached an empty GameResource — and the texture compiler used that cached state. New `create_terrain_material_v2` tool inverts the order: build full JSON text → write to disk → register → force-reload-if-already-cached → verify in-memory matches disk before returning ok=true.
- **2026-04-30** — **Scene files contain UInt64 sentinels (`BodyGroups: 18446744073709551615` = "all groups on") that JS can't round-trip natively.** `JSON.parse` reads them as a `number`, losing precision (becomes `18446744073709552000`). Every MCP scene write was silently corrupting these until the engine started rejecting them with `JsonException: out of bounds for a UInt64`. Fix in `tools/mcp/src/scene/{read,write}.ts`: pre-process the raw JSON text to wrap any unquoted integer ≥ `2^53` as a `"@bigint:N"` string before parse, then unwrap on write. Lossless. Restore corrupted values manually if you find them: `18446744073709552000` → `18446744073709551615`.

---

## How to keep this updated

When you (Claude) discover a new gotcha, convention, or fact about s&box / this codebase that future-you would benefit from knowing:

1. Add a one-liner to **Lessons learned** with today's date (use the `currentDate` from session context).
2. If it's a *rule* (something we should always do or never do), add it to the relevant section above (workflow / code / input / scene).
3. If a known issue gets *fixed*, move it to "Lessons learned" with a "(resolved)" note. Don't silently delete — knowing the fix matters.

Aim for terseness. A line is better than a paragraph. The goal is "future Claude reads this in 30 seconds and knows the lay of the land."
