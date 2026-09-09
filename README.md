# Sphere

A spherical-planet god-game where the "NPCs" are actually thinking for themselves. Each creature is
driven by a local LLM (via [llama.cpp](https://github.com/ggml-org/llama.cpp), no cloud API, no
internet connection required) that decides what to do, what to say, and how to feel about the world
and each other — while you, the player, nudge their fate from above with a set of god powers.

Built solo in Unity, currently in active development.

## Concept

You don't control the creatures directly. You watch, and occasionally intervene. Each creature has:

- **A personality** (`Cautious`, `Zealous`, `Skeptical`, `Social`, `Pragmatic`) that colors how it
  interprets the world and what the LLM tends to choose for it.
- **Needs** (Hunger, Thirst, Safety, Social, Awe) that drive urgency — a starving Zealous creature
  will hunt or raid another camp; a lonely Social one will go looking for company.
- **A voice** — the LLM produces short in-character lines ("I need to gather more wood and stone to
  build a safe home") alongside its chosen action, so you can actually follow what it's "thinking."
- **A life** — creatures forage, build and upgrade shelters, form camps, reproduce, raise the next
  generation, and can die to predators, starvation, or exposure.

Every world is a full sphere: a procedurally generated planet with its own terrain, hydrology
(rivers and lakes carved from simulated water flow, not just noise), biomes, weather, and day/night
cycle, wrapped in an orbiting camera rather than a flat map.

### God powers

You have a Favor economy that regenerates over time (faster the more the population's collective Awe
is) and seven powers to spend it on:

| Power | Cost | Effect |
|---|---|---|
| Smite | 12 | Fear/threat in an area — doesn't kill, just scares |
| Bless | 10 | Boosts a creature's needs |
| Feed | 8 | Restores hunger in an area |
| Meteor | 30 | Smite's lethal escalation — real damage |
| Ward | 20 | Temporary safe zone, predators kept out |
| Fertility | 25 | Cuts time to the next birth in an area |
| Shrine | 35 | Permanent, world-shaping structure (capped count) |

Arm a power with `1`-`7`, then click in the world to cast it. `Esc` cancels an armed power (or opens
the pause menu if nothing's armed).

### Story progression

The world moves through eras — **Arrival → Settlement → Society → Crisis → Legacy** — gated by
population and civilization milestones rather than a fixed timer, with Society/Legacy able to repeat
across further "Ages" if the population survives long enough.

## Tech stack

- **Unity 6.3 LTS** (`6000.3.6f1`), Universal Render Pipeline, DX11
- **C#**, no external gameplay frameworks
- **[LLM for Unity](https://github.com/undreamai/LLMUnity)** (Undream AI) running
  [llama.cpp](https://github.com/ggml-org/llama.cpp) in-process, GPU-accelerated (CUDA/cuBLAS) —
  every creature's decisions come from local inference, nothing leaves your machine
- Procedural spherical terrain: icosphere subdivision + per-vertex noise height sampling, with a
  separate hydrology pass (flow accumulation → rivers → lakes) carved in afterward

## Getting started

1. Install **Unity 6000.3.6f1** (or newer 6.3 LTS) via Unity Hub.
2. Clone this repo and open it as a Unity project.
3. A few things are intentionally **not** in this repo (too large for GitHub, or third-party and
   easily re-acquired) — see [`THIRD_PARTY_ASSETS.md`](THIRD_PARTY_ASSETS.md) for what's missing and
   where to get it. At minimum you'll need:
   - A `.gguf` model file dropped in `Models/` (any size that fits your GPU/VRAM works — smaller
     models respond faster, larger ones role-play better)
   - The LLMUnity native binaries, which the package re-downloads on first setup
4. Open the scene, hit Play, and **New Game**.

## Controls

- **Right-mouse drag** — orbit camera
- **Scroll** — zoom
- **1-7** — arm a god power, then click in the world to cast it
- **Esc** — cancel an armed power / pause menu
- **F3** — toggle the debug overlay (FPS, GPU, LLM status), **F2** to expand it

## Status

Solo project, actively evolving. Expect rough edges — this is a passion project built and iterated on
in the open, not a finished product.
