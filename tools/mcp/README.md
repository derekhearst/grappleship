# GrappleShip MCP

MCP server for chat-first s&box scene editing. Lives in this repo, owned by us.

Entry point: `src/index.ts`. Registered in the repo root's `.mcp.json` so Claude Code auto-starts it.

## Layout

- `src/schema/` — component-type catalog (built-in JSON + parsed `.cs`)
- `src/scene/` — scene file read/validate/mutate
- `src/assets/` — asset catalog
- `src/prefabs/` — prefab CRUD
- `src/logs/` — `logs/sbox-dev.log` tailing
- `src/tools/` — one MCP tool per file

## Setup

```
cd tools/mcp
bun install
```

That's it. Claude Code starts the server via `.mcp.json` at the repo root.

## Refreshing the built-in component schema

Most contributors never need to do this. When **Derek** upgrades s&box and a new built-in component is needed, run the editor menu: **Editor → GrappleShip → Refresh Built-in Type Schema**. This rewrites `docs/sbox/builtin-types.json`. Commit the change.

Custom `GrappleShip.*` components are read directly from `grappleship/Code/**/*.cs`, so they stay live without an export step.
