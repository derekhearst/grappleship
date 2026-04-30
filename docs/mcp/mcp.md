# GrappleShip MCP — chat-first scene tooling

## Overview

The GrappleShip MCP is a local server that runs alongside Claude Code and gives the AI type-safe access to the project's scenes, components, assets, and prefabs. It exists so that contributors who don't know s&box (or even C#) can build out the game by chatting with Claude — the MCP enforces correctness, so Claude can't accidentally produce a scene that won't open.

It lives at [`tools/mcp/`](../../tools/mcp/) in this repo and starts automatically when you open the project in Claude Code (wired via [`.mcp.json`](../../.mcp.json) at the repo root).

## Who uses it

- **Contributors who only chat** — they never run the MCP themselves; it just works in the background.
- **Claude** — the AI calls MCP tools to make safe, validated edits instead of hand-writing JSON.
- **Derek** — only person who needs to know the implementation details, e.g. when refreshing the built-in component schema after upgrading s&box.

## What it can do (tool surface)

### Schema & discovery (4)

| Tool | Purpose |
|---|---|
| `list_components` | List every known component type — built-in `Sandbox.*` plus custom `GrappleShip.*` parsed from `.cs`. |
| `describe_component` | Full property schema for one type (every `[Property]` field, with type, range, group, defaults). |
| `search_property` | Reverse lookup: find which components have a property by name. |
| `refresh_schema` | Reload the catalog from disk (re-read `builtin-types.json`, re-parse `.cs`). |

### Editor logs (2)

| Tool | Purpose |
|---|---|
| `read_log` | Tail recent lines from `grappleship/logs/sbox-dev.log`. |
| `watch_log` | Return only lines added since the previous call. |

### Scene read (4)

| Tool | Purpose |
|---|---|
| `list_scenes` | Every `.scene` file in the project. |
| `read_scene` | Flattened summary of every GameObject (name path, transform, component types). |
| `get_gameobject` | Full data for one GameObject (all components, all property values). |
| `validate_scene` | Type-check a scene against the schema; returns errors with paths. |

### Scene mutate (8) — every write is validated and atomic

| Tool | Purpose |
|---|---|
| `create_gameobject` | New GameObject at root or under a parent. |
| `delete_gameobject` | Remove by GUID. |
| `reparent_gameobject` | Move under a different parent (cycle-safe). |
| `set_transform` | Update position / rotation / scale. |
| `add_component` | Attach a component, optionally with initial properties. |
| `remove_component` | Detach by component GUID. |
| `set_property` | Set one property — validated against schema. |
| `set_properties_bulk` | Set many properties atomically. |

### Assets (4)

| Tool | Purpose |
|---|---|
| `list_assets` | Walk `grappleship/Assets/` (project) with optional kind filter. Set `include_engine=true` to also pull a sample from the live mounted catalog. |
| `find_asset` | Fuzzy-find by name fragment across project + every mounted source the editor knows about (engine, workshop, installed cloud packages). Score matches path AND asset name AND package title — matches the Library Manager's displayed name. Pass `include_cloud=true` to ALSO query asset.party for **uninstalled** packages — results come back with `origin="cloud-uninstalled"` and a `package_ident` you can install. |
| `describe_asset` | Metadata (kind, size, mtime, source). |
| `validate_asset_path` | Existence check across project + mounted sources, optional kind verification. |

### Prefabs (4)

| Tool | Purpose |
|---|---|
| `list_prefabs` | Every `.prefab` file. |
| `read_prefab` | Full prefab data. |
| `instantiate_prefab` | Drop a prefab into a scene with fresh GUIDs and remapped refs. |
| `create_prefab_from_gameobject` | Extract a subtree to a new `.prefab`. |

### Editor bridge (3) — only works when the s&box editor is running

| Tool | Purpose |
|---|---|
| `ping_editor` | Health-check the bridge. |
| `refresh_builtin_schema` | Re-export `docs/sbox/builtin-types.json` from live engine reflection. |
| `install_package` | Install a cloud package by ident (e.g. `arghbeef.vikinghelmet`). Pulls from asset.party, mounts it into the project. Same effect as the Asset Browser → Install button. |

**Total: 29 tools.**

> The asset tools above (`find_asset`, `list_assets`, `describe_asset`, `validate_asset_path`) also use the bridge for live mounted-asset queries (no static cache, no manual refresh). They fall back to project-only search when the editor isn't running.

## Architecture

```
┌─────────────────┐  stdio   ┌──────────────────┐  file I/O   ┌─────────────────┐
│  Claude Code    │ ◄──────► │  GrappleShip MCP │ ◄──────────►│  Repo files     │
│  (contributor)  │          │  (Bun/TS, ours)  │             │  scenes, .cs,   │
└─────────────────┘          └──────────────────┘             │  assets, schema │
                                      ▲                       └─────────────────┘
                                      │ optional file IPC
                                      ▼
                           ┌──────────────────────┐
                           │  EditorBridge.cs     │   only runs when editor open
                           │  (in s&box editor)   │   used for ping + schema refresh
                           └──────────────────────┘
```

The MCP is **file-first**: scene files are the source of truth, not the editor. That means contributors can do real work even with the editor closed — they only need it open when they want to *test* the changes or when they need a fresh built-in schema dump.

The IPC bridge is a single C# file ([`grappleship/Editor/EditorBridge.cs`](../../grappleship/Editor/EditorBridge.cs)) that watches a folder in `%TEMP%/grappleship-bridge/` for request files and processes one per editor frame on the main thread. ~150 lines total, no widgets, no docks.

## Schema layer in detail

The catalog Claude consults has two sources, merged at runtime:

1. **`docs/sbox/builtin-types.json`** — committed to the repo. Generated by the editor menu **Editor → GrappleShip → Refresh Built-in Type Schema** (or via the `refresh_builtin_schema` MCP tool). Refresh after upgrading s&box.
2. **C# source under `grappleship/Code/`** — parsed live by [`tools/mcp/src/schema/csharp-parser.ts`](../../tools/mcp/src/schema/csharp-parser.ts) every time a `.cs` file changes. No manual step. When Claude adds a `[Property]` to a component and saves, the next MCP call sees it.

If the same type appears in both sources, parsed source wins (always more current).

## Validation guarantees

Every mutation goes through this loop:

```
load scene → apply mutation in memory → validate against catalog → atomic write (only if clean)
```

If validation fails, the file on disk is unchanged and the tool returns an error like:

```
[validation_failed] refusing to write — validation found 1 error(s):
  TestRoot > GrappleShip.GrappleHook.ReelSpeed: value 99999 out of range [100, 3000]
```

Type checks include:
- Unknown component types
- Unknown properties
- Type mismatches (string where bool expected, etc.)
- Numeric range violations from `[Range(min, max)]`
- Enum values outside the declared set
- Component / GameObject reference shape (must have `_type`, `go`, `component_id`)
- Vector / rotation / color string format (`"x,y,z"` etc.)

## Day-to-day workflow

For Claude (and humans following along):

1. Want to know what a component does? → `describe_component`
2. Want to know which components have property X? → `search_property`
3. Want to find a model file? → `find_asset` with `kind: "model"`
4. Making any scene change? → mutation tool, automatically validated
5. Wrote some C# and want to check it compiled? → `read_log` with `level: ["Error"]`
6. After upgrading s&box, schema feels stale? → `refresh_builtin_schema` (needs editor running)

## Maintenance notes

- `node_modules` and `bun.lockb` are gitignored under `tools/mcp/`. Each dev runs `bun install` once.
- The C# parser ([`csharp-parser.ts`](../../tools/mcp/src/schema/csharp-parser.ts)) covers the conventions GrappleShip uses (single `[Property]` per line, attributes on the same line). Edge cases emit warnings to stderr; those go to Claude Code's MCP server log.
- The bridge's IPC dir (`%TEMP%/grappleship-bridge/`) is auto-created. Old request files older than ~5 minutes can be safely deleted manually if anything ever wedges.
- If `validate_scene` flags every `Sandbox.*` component as unknown, the built-in schema file is missing — run `refresh_builtin_schema` (or click the editor menu).
