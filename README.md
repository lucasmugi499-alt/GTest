# CASCADE

A near-future grand strategy game with deep simulation and a story engine on top. This repository builds the first playable slice, **The Veyl Crossing**: Kestria (you) against Varan (AI), from Day 0 to Day 90.

- **Design:** [docs/concept.md](docs/concept.md) (the game concept) and [docs/spec.md](docs/spec.md) (the rules and formulas).
- **Decisions:** [docs/decisions.md](docs/decisions.md). Where it disagrees with the spec, decisions.md wins.
- **Parked ideas:** [docs/ideas.md](docs/ideas.md).
- **Writing storylets:** [docs/storylets.md](docs/storylets.md) lists every fact and effect.

## Status

| Milestone | What it adds | State |
| --- | --- | --- |
| M0 Setup | Tools, folder layout, this README | Done |
| M1 Sim core | Fixed-point math, RNG, scheduler, content loader, state hash | Done |
| M2 Economy and grid | Production, stockpiles, transport, fab, grid, daily readout | Done |
| M3 Society and conflict | Politics, information, cyber, military (light), escalation | Done |
| M4 Narrative | Storylets, Director, Chronicle; playable as text | Done |
| M5 Godot UI | Map, Cascade view, Brief, storylet dialog, readouts | Done |
| M6 Balance | 1,000-seed batch runs and metrics | Next |

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

**Play The Veyl Crossing in the terminal.** You make every decision by typing its number:

```bash
dotnet run --project tools -- play
```

- Major decisions stop the clock and wait for your answer. Minor ones wait in your Brief (`b`) and decide themselves if you ignore them.
- Press Enter to move the clock on: an hour at a time in Crisis Time, otherwise a day. `d` jumps to the end of the day, `w` a week.
- `o` opens the orders menu (repairs, priorities, mobilization, emergency powers, the information war and more). `s` shows full status, `h` lists the commands.
- The Chronicle prints at the end.

**Save a game and replay it exactly.** `--record` saves everything you type:

```bash
dotnet run --project tools -- play --record mygame.txt
```

Replay that file and the game plays out identically, down to the same final state hash:

```bash
dotnet run --project tools -- play < mygame.txt
```

**Let the autopilot play** (`first`, `default` or `random` choices):

```bash
dotnet run --project tools -- play --auto random
```

**Watch the world with no decisions at all.** This plays Day 0 to Day 90 and prints one line per day:
- legacy chip output and drones built
- Days of Cover for magnets and flight controllers
- people without power, substations down, the hospital's generator fuel
- Approval, Political Capital, trust and the escalation rung
- how far the deepfake has spread
- which provinces are in Crisis Time

```bash
dotnet run --project tools -- run
```

To inject the Day 4 attack's damage and repairs into that no-decision run, add `--trip 4:2 --repair both --export-controls 6`. Add `--verbose` for grid, fab, society and front detail every day.

**Check determinism yourself.** Run this twice: both runs must print the same hash. A different `--seed` gives a different hash.

```bash
dotnet run --project tools -- hash --seed 42
```

**See all the runner's commands:**

```bash
dotnet run --project tools
```

**Play the game in Godot.** Open the project in the Godot editor:

```bash
open -a /Applications/Godot_mono.app --args --path ~/Desktop/Cascade/game --editor
```

Then press ▶ Play at the top right. Or run the game straight away, without the editor:

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --path ~/Desktop/Cascade/game
```

How to play:
- **The clock** starts paused. ⏸ pauses; ▶ 1, ▶▶ 2 and ▶▶▶ 3 run at 4, 2 and 1 seconds per day. In Crisis Time the clock moves an hour at a time. **Step** moves one step by hand.
- **Big decisions** pause the game and open a card; click a choice. Greyed-out choices aren't available (usually not enough Political Capital). Smaller cards wait in the **Brief** (top right).
- **Map** shows the provinces: darker means more people without power, a red outline means Crisis Time. The Veyl front is on the border; the escalation gauge and your estimate of Varan's red line are bottom left.
- **Cascade** draws the supply chain from power and imports to the front, green, amber or red. Hover any box to see why.
- **Society** has segments, factions, narratives and cyber. The **Chronicle** opens at the end of Day 90.
- **Orders** (right) cover mobilization, emergency powers, the forensic sweep, firmware patches, attacking at Veyl, and repairs for each damaged substation.

**Watch Godot play itself** (for checking a build). This runs to Day 90, prints the final hash and scores, and quits:

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path ~/Desktop/Cascade/game -- --auto random
```

## Folder layout

| Folder | What's in it |
| --- | --- |
| `sim/` | **Cascade.Sim**, the simulation: a plain C# library with no Godot code in it |
| `sim.tests/` | Automated tests (xUnit) for the sim |
| `content/` | Game data as YAML: goods, recipes, designs and `balance.yaml` (every tunable number); each scenario's world, society, conflict, characters, storylets and Chronicle voices are in `content/scenarios/<name>/` |
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
