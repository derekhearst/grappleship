# GrappleShip

Session-based competitive PvP for s&box. Pirate boarding crews on hoverships fight over loot at vertical contested POIs. Grapples for players, momentum-and-grapple for ships, directional melee on the deck. 2–4 crews of 2–4 players, ~20–30 minute matches, self-hosted dedicated server.

> Working title — final name TBD. See [docs/grappleship/grappleship.md](docs/grappleship/grappleship.md) for the full design brief, the three layers of gameplay, the match loop, the build-phase order, and the explicit out-of-scope list.

## Tech stack

- **Engine:** [s&box](https://sbox.game/) — Facepunch's modern game platform built on Source 2.
- **Language:** C# (.NET, hotloaded by the editor).
- **Editor:** s&box editor (`sbox-dev.exe`) — installed via Steam.
- **Visual scripting:** ActionGraph (optional, interops with C#).
- **UI:** s&box's HTML+C# UI system (Blazor-like).

## Getting started

1. Install s&box from Steam — both the game and editor apps. See [docs/sbox/getting-started.md](docs/sbox/getting-started.md) for system requirements, install, and IDE setup.
2. Launch the editor, **New Project → Minimal Game**, and save it inside this folder (recommended location: `./game/`).
3. The editor will open `.cs` files in your default IDE (Visual Studio by default; Rider or VS Code also work).
4. Save a `.cs` file → engine hotloads in milliseconds. No restart.

For the scene/GameObject/Component model, read [docs/sbox/engine-overview.md](docs/sbox/engine-overview.md). Common API snippets are in [docs/sbox/cheat-sheet.md](docs/sbox/cheat-sheet.md).

## Repo layout

```
GrappleShip/
├── README.md                 # this file
├── docs/
│   ├── sbox/                 # platform reference (s&box itself)
│   │   ├── sbox.md
│   │   ├── getting-started.md
│   │   ├── engine-overview.md
│   │   ├── cheat-sheet.md
│   │   └── sources.md
│   └── grappleship/          # game-specific docs (create when design lands)
│       └── grappleship.md
└── grappleship/                     # the .sbproj and game code (created by editor)
```

## Domain docs

- [GrappleShip design](docs/grappleship/grappleship.md) — genre, core fantasy, three layers (player / ship / crew), match loop, 10-phase build order, what's explicitly out of scope.
- [s&box platform reference](docs/sbox/sbox.md) — the engine we're building on.

## Useful external links

- Live docs: <https://sbox.game/dev/doc>
- Sample game (testbed): <https://github.com/Facepunch/sbox-scenestaging>
- Steam install: <steam://run/2129370>
