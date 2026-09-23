# CASCADE

A near-future grand strategy game with deep simulation and a story engine on top. This repository builds the first playable slice, **The Veyl Crossing**: Kestria (you) against Varan (AI), from Day 0 to Day 90.

- **Design:** [docs/concept.md](docs/concept.md) (the game concept) and [docs/spec.md](docs/spec.md) (the rules and formulas).
- **Decisions:** [docs/decisions.md](docs/decisions.md). Where it disagrees with the spec, decisions.md wins.
- **Parked ideas:** [docs/ideas.md](docs/ideas.md).

## Status

| Milestone | What it adds | State |
| --- | --- | --- |
| M0 Setup | Tools, folder layout, this README | Done |
| M1 Sim core | Fixed-point math, RNG, scheduler, content loader, state hash | Next |
| M2 Economy and grid | Production, stockpiles, transport, fab, grid, daily readout | |
| M3 Society and conflict | Politics, information, cyber, military (light), escalation | |
| M4 Narrative | Storylets, Director, Chronicle; playable as text | |
| M5 Godot UI | Map, Cascade view, Brief, storylet dialog, readouts | |
| M6 Balance | 1,000-seed batch runs and metrics | |

## What you need

| Tool | Version | Check it with |
| --- | --- | --- |
| .NET SDK | 10.0 | `dotnet --version` (should print 10.0.something) |
| Godot, .NET edition | 4.7.2 | Installed at `/Applications/Godot_mono.app` |
| git | any recent | `git --version` |

If `dotnet` says "command not found", open a **new** Terminal window. The installer only updates windows opened after it ran.

## How to run things

Run every command from the project folder. Open Terminal and go there first:

```bash
cd ~/Desktop/Cascade
```

**Build everything** (sim, tests, tools and the Godot project). It should end with "Build succeeded" and 0 errors:

```bash
dotnet build
```

**Run the tests.** It should say "Passed!" with 0 failed:

```bash
dotnet test
```

**Run the sim in the terminal** (the headless runner). At M0 it only prints a banner:

```bash
dotnet run --project tools
```

**Open the game in the Godot editor:**

```bash
open -a /Applications/Godot_mono.app --args --path ~/Desktop/Cascade/game --editor
```

Then press the ▶ Play button at the top right. At M0 you'll see a single line of placeholder text.

**Run the game without the editor:**

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --path ~/Desktop/Cascade/game
```

## Folder layout

| Folder | What's in it |
| --- | --- |
| `sim/` | **Cascade.Sim**, the simulation: a plain C# library with no Godot code in it |
| `sim.tests/` | Automated tests (xUnit) for the sim |
| `content/` | Game data as YAML: goods, recipes, facilities, characters, storylets, the scenario, and `balance.yaml` for every tunable number |
| `tools/` | A console app that runs the sim without graphics: text play mode and batch runs |
| `game/` | The Godot project. It only reads snapshots from the sim and sends orders to it; it never changes sim state directly. |
| `docs/` | Design docs, decisions and ideas |

## Ground rules for the code

1. **Deterministic.** The same seed and the same choices always give the same result.
   - Whole-number (fixed-point) math only in the sim.
   - Every random roll is derived from (seed, tick, system, entity, roll number).
   - A state hash checks it.
2. **No magic numbers.** Every tunable value lives in `content/balance.yaml` or another content file.
3. **The spec is the source of truth,** except where `docs/decisions.md` says otherwise.
