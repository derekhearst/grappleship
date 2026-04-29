# GrappleShip

> Working title — final name TBD. Source brief: 2026-04-28.

## Overview

**GrappleShip** is a session-based competitive PvP game where 2–4 crews of 2–4 players each compete over loot at vertical contested points of interest. Matches run roughly 20–30 minutes. Players pilot **hoverships** between POIs, fight other crews in transit with cannons and harpoons, then **grapple-climb** vertical structures (towers, ruins, sky-temples) on foot to grab loot at the top. Most loot at match end wins.

The **core fantasy** is pirate boarding crews on hoverships fighting over loot at vertical POIs. **Movement is the soul of the game** — grapples for players, momentum-and-grapple for ships, directional melee on the deck.

- **Engine:** s&box (Source 2 + .NET / C#).
- **Hosting:** Self-hosted dedicated server on TrueNAS.
- **Match length:** 20–30 minutes.
- **Crew size:** 2–4 players per ship; 2–4 crews per match.

## The three layers of gameplay

| Layer | Mechanics |
|---|---|
| **Player** | Personal grapple hook with stamina-gated reeling. Directional melee combat (attacks + blocks). **No ranged personal weapons.** |
| **Ship** | Hoverships glide on terrain with low friction. Slopes affect speed (downhill fast, uphill slow). Ships have their own grapple to pull themselves up steep terrain. Ranged combat is **ship-mounted cannons**. |
| **Crew** | 2–4 players per ship. **No fixed classes** — anyone can man any station. Roles emerge from positions: helmsman, gunners, boarder/defender. |

## The match loop

1. Crews spawn at the edges of the map.
2. **POIs are scattered around the map.** Each POI is a vertical structure with loot at the top.
3. Crews **sail between POIs**, fighting in transit with cannons and harpoons.
4. Arriving at a POI, players **grapple-climb** the structure, fight other crews in melee, grab loot, and escape.
5. Match ends on a timer or score threshold. **Most loot wins.**

## Out of scope (do not build)

These are tempting but actively wrong to build now. Anything in this list is a "no" until the core game proves itself.

- **Account systems / persistent progression** — not for v1; maybe never.
- **Base building** — decided against. Probably never.
- **Multiple ship variants** — one ship for the prototype.
- **Multiple weapon variants** — cutlass only for v1. More weapons after melee is dialed in.
- **AI / PvE** — pure PvP first. Bots and PvE later.
- **Lobby / matchmaking systems** — "join by IP" is fine for v1. You're playing with friends.
- **Cosmetics** — way later.

## Build phases

Build in order. Don't skip steps. Don't combine phases until the previous one feels good.

### Phase 1 — Player grapple feel

> Single player, flat ground, no enemies. **Goal: prove the grapple is fun in isolation.** This is everything — the whole game lives or dies on this feeling good.

- Player character with basic FPS movement (walk, jump).
- Grapple fires from the crosshair to whatever you're aiming at.
- Hits a surface and anchors there. Visible line between player and anchor.
- **Reel-in input** (e.g. mouse wheel up or hold Shift): pulls player toward anchor.
- **Reel-out input** (e.g. mouse wheel down or hold Ctrl): extends max line length, lets you fall/swing.
- **Passive swing** when line is at max length — gravity does the work, no stamina cost.
- **Stamina bar** drains when reeling, regenerates when not.
- **Release input** detaches the grapple.

**Tunable parameters** (exposed as `[Property]` fields on a debug component):

- Reel speed
- Stamina max / drain rate / regen rate
- Maximum grapple range
- Line tension force
- Air drag

> Spend 1–2 weekends here. If it feels bad, tune until it doesn't. If you can't make it feel good, the design is wrong and we adjust before building anything else.

### Phase 2 — Directional melee combat (in isolation)

> Two players on flat ground. No movement systems beyond walking. **Goal: prove the combat is fun in isolation.**

- **Mount-and-Blade-style 4-direction attacks** — attack direction follows mouse movement during wind-up.
- **4-direction blocks** — block direction follows mouse position when blocking is held.
- Wind-up animation telegraphs attack direction.
- Successful block negates damage, costs stamina.
- Mismatched block takes full damage.
- Attacks cost stamina.
- Single one-handed weapon (cutlass).
- Health bar.

> Spend 1–2 weekends. If this isn't fun in a flat-ground duel, no amount of grappling will save it.

### Phase 3 — Combine player movement and combat

> Single player. Both systems together. **Goal: tune the integration rules.**

- Light attacks work while attached to a grapple.
- Heavy attacks and blocks **require ground contact**.
- Attacks share the **stamina pool** with reeling.
- **Cutting another player's grapple line:** melee swing at the line cuts it (deferred — needs multiplayer first).

> Spend 1 weekend tuning. The interplay between movement and combat is where the game's feel lives.

### Phase 4 — Hovership physics

> Single ship, no combat, hilly terrain. **Goal: prove the ships are fun to drive.**

- Ship hovers ~1 m above terrain. Apply forces at four corners pointing down, raycast for surface, push the ship up based on distance.
- **Low friction** along the ground plane.
- **Gravity along the slope** — downhill accelerates, uphill decelerates.
- Driver controls: throttle (forward thrust), turning, optional pitch up/down for jumping off ridges.

**Tunables:** hover height, hover stiffness, friction coefficient, max speed, turn rate, mass.

> Spend 2–3 weekends. The terrain feel is a real engineering problem. Get it stable before adding anything else.

### Phase 5 — Ship grapple

> Same single ship, hilly terrain. Add the ship grapple.

- Helmsman fires grapple from ship toward terrain.
- Anchors to point. Pulls ship toward anchor with tunable force.
- Designed to **climb slopes** that are too steep for normal hover.
- Release: ship now has momentum from being pulled up. Slopes down the other side at speed.

> Spend 1 weekend. **This is the magic moment of ship gameplay.** If pulling yourself up a hill and rocketing down the other side feels good, you have the skeleton of a great game.

### Phase 6 — Cannons and ship damage

> Two ships, hilly terrain. Add ranged ship combat.

- Cannon mounted on ship in fixed firing arc (~60° swing).
- **Long reload** (~10 seconds).
- **Lead-target manual aim.**
- **Systems-based damage:** hits separately to sails, helm, hull, individual cannons. Each system has its own health.
  - Sails damaged → speed reduced.
  - Helm damaged → control reduced.
  - Hull damaged → altitude / integrity reduced.
- **Repairs:** crewmember stops to fix it (~15–20 seconds).

> Spend 2–3 weekends. This is where ship-vs-ship combat starts existing.

### Phase 7 — Multiplayer

> Standalone phase. **Goal: get everything we have working over the network.**

s&box networking is built-in. Add `[Sync]` attributes to relevant Component properties. Test over LAN first, then over the TrueNAS server.

- Player position / rotation / animation sync.
- Grapple state sync (anchored, line length, stamina).
- Combat sync (attack direction, blocks, damage application).
- Ship state sync (position, rotation, velocity, system damage).

> Spend 2–4 weekends — possibly more. Networking is where every game project hits unexpected complexity. **Use s&box's built-in tools — don't roll your own networking.**

### Phase 8 — POI structure

> Build **one** POI. Just one. A spire ~60 m tall with grappleable surfaces and loot at the top.

- **Loot trigger:** player at top → score points → particle effect → respawn timer on the loot.
- **Climb surfaces:** define which surfaces are grappleable vs not.
- Test with two crews trying to climb simultaneously.

> Spend 2–3 weekends. The first POI is the test of whether the contested-loot loop is fun.

### Phase 9 — Match flow

> Wrap everything in a match structure.

- Match start: crews spawn at edges of map.
- Map has 3–4 POIs marked.
- Loot at POIs respawns on a timer or single-claim.
- Match end: timer expires or score threshold hit.
- Scoreboard at end.

> Spend 1–2 weekends.

### Phase 10 — Iteration and polish

Everything from here is content and tuning: more POI archetypes, more cannon types, more maps, visual polish, sound, UI. The work never ends but the game exists.

## Key tuning parameters (consolidated)

These are the dials that will get touched constantly. Build a single **DebugTuning** component that exposes them all as `[Property]` fields so values can be adjusted live in the editor without recompiling.

| Layer | Parameter |
|---|---|
| Player grapple | reel speed, stamina max, drain rate, regen rate, max range, tension force, air drag |
| Player melee | attack stamina cost, block stamina cost, wind-up duration, weapon damage, health max |
| Ship hover | hover height, hover stiffness, friction coefficient, max speed, turn rate, mass |
| Ship grapple | pull force, anchor max range, release momentum bonus |
| Ship cannon | firing arc, reload time, projectile speed, damage per system |
| Match | match duration, loot respawn time, score threshold |

## Working notes for AI-assisted development

The bridge between Claude and the s&box editor is live (see [working-with-ai.md](working-with-ai.md) if added later). A few rules of engagement that apply on this project:

- **Iterate in small chunks.** "Implement the grapple firing logic" is a good ask. "Build the entire grapple system" is a bad ask. Each chunk should be small enough to run, observe, and decide what to do next.
- **The Scene API is the workhorse.** `scene.CreateObject()`, `go.AddComponent<T>()`, `scene.Trace.Ray(...)` — these get used constantly.
- **Build the DebugTuning component early.** First-hour task. Live-tunable values are how the player-grapple-feel iteration works.
- **Read the code before accepting it.** s&box's API surface is specific and AI training data on it is sparse. Compile errors are normal — work through them.
- **Use the JSON scene format directly when needed.** `.scene` files are JSON; they can be read and reasoned about.
- **The engine is brand new (released 2026-04-28).** Doc gaps will exist. The s&box Discord is the ground truth when docs fail.

## Expectations

- **The first weekend will feel slow.** Project setup, learning quirks, getting AI productive in the new context. The second weekend will feel three times faster.
- **Stamina tuning will take longer than expected.** "30 stamina per second of reeling" feels wrong the first time, fine the third time, perfect the tenth time. Live tuning UI is non-negotiable.
- **Resist the urge to add features before the previous phase is solid.** The phases are ordered for a reason. Adding ship physics before the player grapple feels good means two unsolved problems instead of one.
- **A second pair of eyes on Phase 1 is essential.** If a friend doesn't have fun grappling around for 5 minutes with no goal, the mechanic isn't there yet.

## Glossary

- **POI** — Point of interest. A vertical structure (tower, ruin, sky-temple) on the map with loot at the top. Crews fight over POIs.
- **Hovership** — The pilotable craft. Glides ~1 m above terrain on cushion forces. Has its own grapple for traversing steep terrain.
- **Crew** — 2–4 players sharing one hovership. No fixed classes — roles emerge from who's at which station (helm, cannon, deck).
- **Boarding** — When players from one crew leave their ship to attack another (typically by grappling across).
- **Grapple-climb** — Player traversal up a vertical surface using the personal grapple hook.
- **Stamina** — Shared pool that gates both grapple reeling and melee attacks.
- **Loot** — Scored objects at the top of POIs. The match-winning currency.
