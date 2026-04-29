# s&box

> Synced from the official docs at https://sbox.game/dev/doc on 2026-04-28. Authoritative source: [Facepunch/sbox-docs](https://github.com/Facepunch/sbox-docs) (CC-BY-4.0). When in doubt, check the live site — these notes are a snapshot.

## Overview

**s&box** is Facepunch's modern game creation platform — a spiritual successor to Garry's Mod. It's built on a heavily modified Valve **Source 2** engine (the same engine powering CS2, Half-Life: Alyx, and DOTA 2) plus the latest **.NET / C#** runtime.

Games are written in C#. The engine ships with a hotload system that compiles and applies code changes within a few milliseconds, so iteration is near-instant — you don't restart the editor to see changes.

The engine and editor are **free** and available to everyone through the developer preview on Steam (released 2026-04-28). Games you make can be exported and released as standalone Steam titles with **no royalties**.

## Key concepts

| Concept | What it is |
|---|---|
| **Scene** | Your game world. Everything that renders or updates lives in a scene. Scenes are JSON files on disk and are fast to load/swap. |
| **GameObject** | A world object with position, rotation, scale. Arranged in parent/child hierarchies. |
| **Component** | Modular functionality you attach to a GameObject. `ModelRenderer` makes it render a model; `BoxCollider` makes it solid. You author games primarily by writing custom Components in C#. |
| **ActionGraph** | Visual scripting system — wire up logic without writing code. Interops with C#. |
| **Hotload** | Save a `.cs` file → engine recompiles and applies in milliseconds. No restart. |
| **UI system** | HTML-with-C# (similar to Blazor). |

## Project types

- **Game project** — the base level of project. Contains a game with a startup scene (and optional menu/intro scene). This is what you make to ship a game.
- **Addon project** — extensions/content that plug into other games or the engine.

See [getting-started.md](getting-started.md) for the install + first-project walkthrough.

## What's in this folder

- [getting-started.md](getting-started.md) — install s&box, system requirements, create your first project, open it in an IDE.
- [engine-overview.md](engine-overview.md) — the scene system, GameObjects, Components, hotload, the testbed.
- [cheat-sheet.md](cheat-sheet.md) — copy/paste C# snippets for common engine operations.
- [sources.md](sources.md) — links back to every official doc page these notes were derived from.

## Editor at a glance

The editor (`sbox-dev.exe`) is your workspace. It bundles:

- **Scene editor** — build and arrange GameObjects/Components.
- **Hammer mapping** — brushwork, props, lights, GameObjects (Source-style level editing).
- **ModelDoc model editor** — model setup and configuration.
- **ActionGraph** — visual scripting.
- **Asset browser** — manage textures, models, sounds, scenes.
- **Custom editor tools / widgets / apps** — extend the editor in C#.

## Community & support

- Forums: linked from sbox.game.
- Discord: beginner's channel for new devs.
- Bug reports / feature requests: [Facepunch/sbox-public](https://github.com/Facepunch/sbox-public) issues.
- Sample game source: [Facepunch/sbox-scenestaging](https://github.com/Facepunch/sbox-scenestaging) — open the `.sbproj` in the editor to explore the testbed scenes.

## Monetization

- Standalone Steam release with no royalties.
- "Play Fund" and other in-platform revenue systems for games hosted on s&box itself.
