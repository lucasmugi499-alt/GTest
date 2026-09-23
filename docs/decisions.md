# Decisions

Where this file and `spec.md` disagree, **this file wins**. Each entry says what was decided, why, and which part of the spec or concept it touches. New entries go at the bottom with the next number.

## D-001 · Tools and target framework

- Godot 4.7.2 (.NET edition), C#.
- .NET 10 SDK. All projects target `net10.0`.
- **Why:** .NET 8 reaches end of support on 10 November 2026. .NET 10 is the current long-term support release, and Godot 4.7 supports it.

## D-002 · Fixed-point numbers

Refines the spec's Conventions: Determinism rules.

- **Stored values:** `Fixed`, a 64-bit integer at scale 10,000 (4 decimal places), as the spec says.
- **Population shares and probabilities:** `Fine`, a 64-bit integer at scale 100,000,000 (8 decimal places). This covers narrative S/E/B/R shares, belief shares and per-hour rates.
- **Intermediate products and divisions:** 128-bit integers (`Int128`), rounded once at the end.
- **Rounding:** to nearest, with ties rounded away from zero.
- **No floats in the sim.** `exp` and `σ` are implemented in integer math.
- **Why:** at 4 decimal places, hourly narrative rates round to zero. For example, γ = 0.02 per day is about 0.0008 per hour, and early contagion steps are around 0.000001. At that precision a narrative could never spread.

## D-003 · Escalation rungs

Rungs are numbered **1 to 7**, as in the concept doc, and mapped onto the spec's meter bands:

| Rung | Meter | Name |
| --- | --- | --- |
| 1 | 0–14 | Competition |
| 2 | 15–29 | Grey zone |
| 3 | 30–44 | Covert action |
| 4 | 45–59 | Limited kinetic |
| 5 | 60–74 | Conventional war |
| 6 | 75–89 | Strategic strikes |
| 7 | 90+ | Strategic threshold |

- The Veyl Crossing starts with the meter at **20** (rung 2).
- Any formula that uses "Rung" (Director tension, bond yield, war-risk insurance) uses this 1–7 number.

## D-004 · Calendar

Day 0 is **Monday 3 March 2031**.

- Weekly hooks run on Mondays.
- Monthly hooks run on the 1st.

## D-005 · Map for the slice

- **Full detail:** Ossen East (`kestria_east_ossen`) and Veyl (`veyl`).
- **Blocks:** Kestria Interior (the rest of Kestria) is one aggregate block. Varan is one aggregate block.
- **Population:** Kestria's 6 population segments are spread across Ossen East, Veyl and Kestria Interior.
- **Why:** Approval and Political Capital are national, so the rest of the country has to exist, but it doesn't need facility-level detail.

## D-006 · Numbers the spec doesn't give

- Capacities, MW, populations, stocks and similar numbers are authored in `/content` so the scenario starts on the concept's stated readouts:
  - Escalation rung 2
  - Magnet Days of Cover 45
  - Legacy chip output 100%
  - Public trust 58%
  - Political Capital 120
- Every tunable number lives in `content/balance.yaml` or another content file, never in code.

## D-007 · Transport

- Transport uses fixed lead times and capacities per edge between provinces.
- There is no min-cost flow solver in this slice (see ideas.md).

## D-008 · Weekly forecast

The 90-day forecast keeps its slot in the weekly phase as a stub.

## D-009 · Markets

- **Built:** war-risk insurance only, as spec'd: premium × (1 + Rung) at rung 2+, and a 20% weekly chance of skipping the port from ×4.
- **Not built:** bond yields and currency (see ideas.md).
- The Day 6 "currency drops 8%" beat appears as Brief text only.

## D-010 · Threading

- Phases run single-threaded in this slice.
- They still read committed state and write to a next-state buffer, so they can run in parallel by region later without changing results.

## D-011 · Game speeds

| Speed | Real time per in-game day |
| --- | --- |
| 1 | 4 s |
| 2 | 2 s |
| 3 | 1 s |

## D-012 · Scenario arc pacing

Resolves a contradiction: the Historian's major-beat-every-90-days rhythm and the 1-major-per-week cap versus about 8 major Veyl beats in 10 days.

- Veyl storylets are tagged `arc: veyl`.
- **They fire when their preconditions hold and ignore the Director's caps.**
- They still count toward pacing (days since the last major beat) and toward similar-use counts.
- **The Director governs** the generic minor storylets and seed payoffs.

## D-013 · Varan's schedule

Varan's scripted actions happen on **fixed days**, so tests are reproducible:

| Day | Action |
| --- | --- |
| 3 | Survey team clash at Veyl |
| 4, 02:14 | Cyber attack on 3 Ossen East substations |
| 6 | Export licensing review on magnets and gallium |

- Varan's reactive retaliation rule from the spec runs on top of this schedule.
- ±1 day variation is logged in ideas.md.

## D-014 · Defusing the substation seed

- A Brief card offers a **forensic sweep for 15 Political Capital**, available between Day 0 and Day 2. It is deliberately easy to miss.
- **If the player defuses the seed,** Day 4 still happens:
  - Varan's intrusion is detected and attributed to Varan: a "Cyber intrusion detected" action, escalation +4.
  - Varan hits a weaker target instead: **two substations in Kestria Interior**, using the same grid disruption effect.
  - The blackout is shorter and does not reach the fab.

## D-015 · Regression test: legacy chip output

Defines "the eastern blackout must cut legacy chip output by 35 to 45%".

- **Metric:** national legacy chip output averaged over the 7 days after the trip, compared with the 7 days before.
- **Setup:** Tessera Fab 3 is about 40% of national legacy capacity.

## D-016 · Git workflow

- Small commits as work goes.
- One commit at the end of each milestone.
- **Push to `main` on GitHub only after the owner has said OK to that milestone.**

## D-017 · Random roll coordinates

Refines the spec's Determinism rules.

- **tick** = day × 32 + slot. Slots 0–23 are crisis hours, 24 is the daily pass, 25 weekly and 26 monthly. So an hourly roll and a daily roll on the same day never share a number.
- **system** is a fixed number per system (`SystemId`): phases use their phase number, weekly systems 100+, monthly 200+.
- **entity** packs (entity kind, ID).
- **roll index** counts up within one (system, entity) stream.
- **The hash** is a chain of SplitMix64 finalizers. It is locked by a test; changing it invalidates every replay.
- **Normal draws** (for red lines and noise) use the sum of 12 uniform draws minus 6, which caps them at ±6 standard deviations.

## D-018 · Slice length

"Day 0 to Day 90" is inclusive: the slice plays 91 daily ticks. The M1 hash test compares state after Day 90 completes.

## D-019 · Crisis Time details

Refines the spec's Crisis sub-ticks.

- **When a province enters crisis:** at the start of a day, if its crisis flag is set, any crisis condition holds, or a timed event (one with an hour) is due there that day. So the Day 4 02:14 attack plays out hour by hour on Day 4, not from Day 5.
- **What runs each hour:** besides the spec's phases 2, 5, 7 and 8, the orders phase (0) and timed events (1) also run. Player decisions in the Situation Room therefore take effect the next hour, and a 02:14 event lands in the 02:00 hour.
- **Clearing:** every hour, a province in crisis either resets its stable-hour count (any condition still holds) or adds one. At the end of the day, provinces with 48 or more stable hours clear.
- **Stepping hour by hour or day by day** gives identical results (tested).

## D-020 · Next-state buffer

- Every changing field is a double-buffered column.
- A phase reads the committed value and writes the next value.
- The scheduler commits after every phase, every hourly phase and every weekly or monthly system.
- Code that updates the same entity twice in one phase must read its own pending write (`Pending`), not the committed value.

## D-021 · Strict content

- The YAML loader fails, naming the file and line, on any of these:
  - a missing key
  - an unknown key
  - a malformed number
  - a stored value (`Fixed`) with more than 4 decimals
  - a `Fine` value with more than 8 decimals
- Numbers are parsed from their text, never through floats.
- **Why:** a typo in balance.yaml can't silently fall back to a default.

## D-022 · Entity IDs

- IDs are dense 32-bit indexes assigned in the order entities appear in the content files.
- The state hash walks every store in ID order.

## D-023 · Integer exp and σ

- `exp` works internally at 18 decimals in 128-bit integers and rounds once to 8 decimals.
- The input range is −25 to 25: below −25 the result is 0, and above 25 is an error.
- σ returns exactly 0 or 1 beyond ±25.
- Tests check both against the spec's worked numbers: detection 0.97 and 0.12, fab yield 0.88 at 780 days, and attribution confidence.

## D-024 · State hash

- The hash is 64-bit XxHash3.
- It covers the scenario ID, the seed, the day and hour position, every store column, pending orders and scheduled events.
- `cascade run` prints it every 30 days (spec: the desync check every 30 ticks).
