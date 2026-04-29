# s&box Docs — Local Index

Local mirror of the [Facepunch/sbox-docs](https://github.com/Facepunch/sbox-docs) markdown (CC-BY-4.0). Fetched 2026-04-28. Use these instead of web-fetching from `sbox.game/dev/doc` — they're the same source.

For topics not mirrored here, fetch the single file from the upstream URL pattern at the bottom of this file.

## Curated summaries (top level)

Hand-written condensed references. Read these first when getting oriented.

- [sbox.md](sbox.md) — what s&box is
- [getting-started.md](getting-started.md) — install + first project
- [engine-overview.md](engine-overview.md) — Scene / GameObject / Component architecture
- [cheat-sheet.md](cheat-sheet.md) — common C# snippets

## Code

- [code/index.md](code/index.md)
- [code/code-basics/index.md](code/code-basics/index.md)
- [code/code-basics/api-whitelist.md](code/code-basics/api-whitelist.md) — sandboxed .NET API surface
- [code/code-basics/console-variables.md](code/code-basics/console-variables.md) — ConVars
- [code/code-basics/math-types.md](code/code-basics/math-types.md) — Vector3, Angles, Rotation
- [code/advanced-topics/index.md](code/advanced-topics/index.md)
- [code/advanced-topics/hotloading.md](code/advanced-topics/hotloading.md) — hot-reload semantics
- [code/advanced-topics/code-generation.md](code/advanced-topics/code-generation.md) — `[Sync]`, `[Property]` source-gen
- [code/advanced-topics/unit-tests.md](code/advanced-topics/unit-tests.md)
- [code/libraries.md](code/libraries.md) — how `Libraries/` packages work

## Scene

- [scene/index.md](scene/index.md)
- [scene/gameobject.md](scene/gameobject.md)
- [scene/gameobjectsystem.md](scene/gameobjectsystem.md)

## Physics

- [physics/index.md](physics/index.md)
- [physics/tracing.md](physics/tracing.md) — raycasts (used by grapple)
- [physics/physics-events.md](physics/physics-events.md) — collision callbacks

## Networking

- [networking/index.md](networking/index.md)
- [networking/connection-permissions.md](networking/connection-permissions.md)
- [networking/custom-snapshot-data.md](networking/custom-snapshot-data.md)
- [networking/http-requests.md](networking/http-requests.md)
- [networking/network-events.md](networking/network-events.md)
- [networking/network-helper.md](networking/network-helper.md)
- [networking/network-visibility.md](networking/network-visibility.md)
- [networking/networked-objects.md](networking/networked-objects.md)
- [networking/ownership.md](networking/ownership.md)
- [networking/rpc-messages.md](networking/rpc-messages.md)
- [networking/sync-properties.md](networking/sync-properties.md) — `[Sync]` attribute
- [networking/testing-multiplayer.md](networking/testing-multiplayer.md)
- [networking/websockets.md](networking/websockets.md)

## Gameplay

- [gameplay/index.md](gameplay/index.md)
- [gameplay/input/index.md](gameplay/input/index.md)
- [gameplay/input/controller-input.md](gameplay/input/controller-input.md)
- [gameplay/input/glyphs.md](gameplay/input/glyphs.md)
- [gameplay/input/raw-input.md](gameplay/input/raw-input.md)
- [gameplay/terrain/index.md](gameplay/terrain/index.md)
- [gameplay/terrain/creating-terrain.md](gameplay/terrain/creating-terrain.md)
- [gameplay/terrain/terrain-materials.md](gameplay/terrain/terrain-materials.md)
- [gameplay/navigation/index.md](gameplay/navigation/index.md) — NPC pathfinding
- [gameplay/navigation/navmesh-agent.md](gameplay/navigation/navmesh-agent.md)
- [gameplay/navigation/navmesh-links.md](gameplay/navigation/navmesh-links.md)
- [gameplay/navigation/navmesh-areas/index.md](gameplay/navigation/navmesh-areas/index.md)
- [gameplay/navigation/navmesh-areas/costs-filters.md](gameplay/navigation/navmesh-areas/costs-filters.md)
- [gameplay/navigation/navmesh-areas/obstacles.md](gameplay/navigation/navmesh-areas/obstacles.md)

## Assets

- [assets/index.md](assets/index.md)
- [assets/file-system.md](assets/file-system.md)
- [assets/resources/index.md](assets/resources/index.md)
- [assets/resources/custom-assets.md](assets/resources/custom-assets.md)
- [assets/resources/gameresource-extensions.md](assets/resources/gameresource-extensions.md)

## Animation

- [animation/index.md](animation/index.md)

## Media

- [media/audio.md](media/audio.md)

## UI

- [ui/index.md](ui/index.md)
- [ui/hudpainter.md](ui/hudpainter.md)
- [ui/localization.md](ui/localization.md)

## Editor

- [editor/property-attributes.md](editor/property-attributes.md) — `[Property]`, `[Group]`, `[ReadOnly]`
- [editor/editor-events.md](editor/editor-events.md)
- [editor/undo-system.md](editor/undo-system.md)
- [editor/editor-shortcuts.md](editor/editor-shortcuts.md)

## Getting Started (mirrored)

- [getting-started/faq.md](getting-started/faq.md)
- [getting-started/status.md](getting-started/status.md)
- [getting-started/reporting-errors.md](getting-started/reporting-errors.md)

## Not mirrored (fetch on demand)

These are deliberately out of scope for GrappleShip. If you need one, fetch with the URL pattern below and add it here.

- `clothing/*`, `assets/clothing/*`, `assets/ready-to-use-assets/*` — no character customization in v1
- `movie-maker/*` — not gameplay
- `actiongraph/*` — we write C#, not visual scripts
- `game-mounts/*`, `exporting/*` — not relevant in current phases
- `gameplay/vr.md` — not in scope
- Most of `editor/*` (custom editors, widgets, mapping, model editor, texture generators) — fetch on demand

## Re-fetching

Upstream raw URL pattern:

```
https://raw.githubusercontent.com/Facepunch/sbox-docs/master/docs/<path>
```

To refresh a single file (Bash):

```bash
curl -fsSL "https://raw.githubusercontent.com/Facepunch/sbox-docs/master/docs/networking/sync-properties.md" \
  -o "docs/sbox/networking/sync-properties.md"
```

Full file tree: <https://api.github.com/repos/Facepunch/sbox-docs/git/trees/master?recursive=1>

## External references

- Live docs (always current): <https://sbox.game/dev/doc>
- Sample testbed game: <https://github.com/Facepunch/sbox-scenestaging>
- Engine repo (advanced): <https://github.com/Facepunch/sbox-public>
- Steam store: <https://store.steampowered.com/app/590830/sbox/>
- Discord: ground truth for the latest API churn
