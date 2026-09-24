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

## D-025 · What M2 builds and what moves to M3

- **Built in M2:**
  - The export-control mechanism: an event that blocks the source nation's import routes for chosen goods. Cargo already at sea still arrives.
  - The countermeasure decay formula, as an extra weekly system (`WeeklyCountermeasures`), since the spec's weekly list doesn't include it.
- **Moves to M3:**
  - War-risk insurance, because it needs the escalation rung.
  - Varan's scripted Day 4 and Day 6 actions. Until then the headless runner has `--trip` and `--export-controls` to inject the same shocks.

## D-026 · Who gets stock, and what counts as burn

- **Phase 3** allocates each province's stock of each good across every claim at once: facility inputs (r_i × C, as the formula's S_i ÷ (r_i C) implies) and final demand, by priority tier.
- **Phase 5** consumes the share set aside for final demand. Phase 4 can't ship it away in between.
- **Burn** is what was actually consumed (inputs used plus final demand served plus backup diesel), not what was demanded. So a shortage shows as a falling stock, not a rising burn.
- **Days of Cover** is national, per good. Goods made at home have no replacement lead time, so they only get the critical flag (below 14 days).

## D-027 · Default priorities not in the spec

- **Cell towers** are High. The spec names hospitals, water and grid repair as Critical, industry Normal, and consumers and data centres Low, but not cell towers.
- **Refuelling backup generators** uses the same tier allocation, so services in the same tier share a truck shortage proportionally instead of the first in the list taking it all.
- **Example:** Ossen East's trucks (1.5 t/h) cover half of the two water pumps' and the hospital's burn, so all three drain at half speed and the cell towers get nothing.

## D-028 · Grid details the spec leaves open

- **Repair crews:**
  - A spare-transformer or mobile-unit job goes at full speed with 300 grid linemen.
  - With fewer linemen in the province, every job there slows proportionally. This is how mobilizing reservists (M3) slows grid repair.
  - A new transformer is manufacturing time and needs no crew.
- **Collapse:** a hit that knocks out at least **75%** of a region's load collapses it. The region then restores 25% of its load per day if it has a black-start plant; otherwise only while a tie-line neighbour is energized.
  - The 75% keeps the Day 4 attack (68% of Ossen's load) a 4.1-million-person blackout, as in the concept, rather than a full regional collapse.
- **Tie-lines** trade surplus in tie-line ID order in a single pass. There is no multi-hop transit.
- **Mobile units:**
  - A mobile unit gives 30% capacity once installed.
  - It stays until a spare or new transformer finishes, then returns to the national pool.
- **Repair timing:** repairs advance at the start of each province's day: in the hour-0 pass for a crisis province, in the daily pass otherwise. A substation finished today carries load all day.

## D-029 · Import ordering

- Each open import route orders EMA burn + (target stock − position) ÷ 14 days, up to its capacity.
- Position is the nation's stock plus everything in transit. Target is the doctrine's (7 + 83j) days of burn.
- The 14 days is authored; the spec only says flows follow cached routes.

## D-030 · Countermeasure details

- The design **cap** decays at half e's weekly rate. If it didn't decay, firmware patches would always restore 1.0 and hardware revisions would never be worth it.
- A **hardware revision** takes 30 days of Design Bureau time (the spec gives none). It sets both effectiveness and cap to 1.0, and retools every line building the design at similarity 0.8.
- The Design Bureau works on one patch or revision at a time.
- Varan's adaptation A is a single balance value (1.0) for now. The part driven by how often the design is used arrives with military sorties in M3.

## D-031 · The Just-in-Time efficiency bonus

- The bonus "+3% × (1 − j)" multiplies E, and the result is capped at 1.0.
- **Why:** a line can never run above its capacity or consume more input than its allocation.

## D-032 · Scrapped work in process

- A steadily running fab holds capacity × 42 days of wafer starts.
- While it ramps back after an interruption it holds that amount × (ramp days done ÷ 21).
- An interruption scraps what the line holds, so a fab that stays dark scraps once, not every day.

## D-033 · Domestic distribution

- Along each edge, each way, stock moves to level the two provinces' local Days of Cover, within the edge's daily tonnes for that good's class.
- A province that doesn't use a good passes all of it on.
- **Why:** the first rule tried, "ship above your target stock", never moved anything. The port province's stock never reached target while imports were still counted as in transit, so Ossen East ran out of wafer blanks in a quiet world.

## D-034 · Society numbers the spec leaves open

All of these are in `balance.yaml` under `society`.
- **Prices need:**
  - Wage growth (2.5%) and m − g (2%) are fixed; the slice has no money supply or growth model.
  - Import prices rise 2% per point of war-risk multiplier above 1.
  - The shortage share is the % of goods in critical shortage.
- **Jobs:** unemployment is the segment's baseline plus workers idled by facilities running below capacity in its province, over a labour force of 50% of the population.
- **Safety:** violent incidents and deaths per 100,000 are a decaying monthly sum that settles back to a peacetime baseline of 6. Combat deaths are booked to the brigade's home segment.
- **Services:** the average of water, hospital and transit availability, where transit is 90% × the segment's power.
- **Dignity:** 50 + 0.2 × (information axis − 50) + the segment's identity offset − normalized-precedent penalties − the curfew penalty.
- **Satisfaction smoothing** uses exponential averages with half-lives of 30 days, or 7 for Power and Safety.
- **Align and Trust:**
  - Narrative belief lowers each by the narrative's align/trust hit × the believing share.
  - Precedents and choices shift Align temporarily; the shift fades 2% a day.
  - Trust drifts back to its start at the same rate.
- **Starting readouts:** Trust starts at 58 (the concept's figure). Approval comes out near 70 with these needs; it isn't set by the concept, and M6 can tune it.

## D-035 · Factions and Political Capital

- **Faction approval** moves halfway each week toward 50 + its agenda terms + its standing.
  - Agenda terms: rung above 2, mobilization level, emergency in force, civil-liberties precedents used, jobs and power of member segments.
  - Standing comes from storylets and narratives, and fades 10% a week.
- **Faction actions:**
  - A strike idles 20% of the members' provinces' workers for 7 days.
  - A protest adds 5 incidents per 100,000 to member segments.
  - Leaks and no-confidence motions are logged for the narrative engine (M4).
- **Political Capital:** "plus any rally effect" is + 0.1 × Rally per week.
- **Rally:** "when you are attacked" means any action of weight 6 or more against you. It sets Rally to at least 20 and Legitimacy to at least 70 (answering an attack).
- **Precedents:** a civil-liberties precedent also costs the Civil Liberties faction 10 standing × 0.6^(n−1).

## D-036 · Information costs

- A takedown request's compliance cost c is 20.
- The Political Capital spent as pressure is gone even if the company refuses.
- Takedowns apply to platforms a company owns (Brightline).
- Prebunking a narrative costs 5 Political Capital.
- The counter-narrative (going live) is free, but refused while national Trust is 50 or below.

## D-037 · Military details

- **Drone cover:** full drone surveillance cover needs 30 drones per km per day (the spec's locking density), scaled by the design's effectiveness.
- **Morale:** the brigade's morale × (1 + (war support − 50) ÷ 100), clamped 0.5 to 1.2.
- **Supply fill:**
  - Diesel stock at the front ÷ 7 days of the brigades' burn (2 tonnes per 1,000 soldiers a day), less trucks lost in the enemy's kill zone.
  - The player burns it only while the front is active.
- **Drones:** the player flies drones from its national stock, the front province's first. Varan's drones, diesel and supply aren't simulated, so they're unlimited.
- **Medical supply** for returning wounded is the average availability of hospital loads.
- **Clash deaths:** a clash with N deaths costs N ÷ 0.25 strength (one killed to three wounded).
- **Mobilization:**
  - Only levels 0 to 2 in this slice.
  - Only skilled reservists leave labour pools (the concept's 1,800 linemen and technicians, plus 100 fab engineers).
  - 25% of the called-up reservists form a reserve brigade at Veyl.
  - A level change takes effect in phase 6, so production sees it the next day.
- **The front** stays active 3 days after an attack order.

## D-038 · Narrative speed (a contradiction between the docs)

- **The conflict:**
  - The spec's η = 0.5 per day caps how fast exposed people become believers: at most about 30% of the exposed a day.
  - So no Virality can make a deepfake tip in the concept's "about five hours".
- **What was done:**
  - The spec's formulas are kept, and the deepfake's Virality is 20 (lowered from 50 in M6, see D-043), so left alone it tips about 5 days after release (Day 4, 06:00), around Day 9.
  - Rumor Velocity reports that forecast.
- **Hourly steps:** when any segment's province is in Crisis Time, every narrative steps hourly that day (the model couples all segments).

## D-039 · Escalation and Varan

- **The meter:**
  - The meter adds the victim's perceived weight (×1.5 with civilian harm), then rises to the action's floor.
  - Weekly decay may take it below a floor reached earlier.
- **The AI answers only what's done to it.**
  - Its red line counts only the rival's actions. Its own scripted moves don't provoke it.
  - It answers each crossing once, until the meter decays back below the red line.
  - A crossing is answered with the cheapest action whose floor reaches the next rung.
  - The reactive answer (weight ≤ e × r, 1 to 14 days later) is never an offensive.
- **D-014 fallback:** the caught attempt is recorded as a detected intrusion (+4). The fallback disruption of the Interior substations reuses the grid disruption effect with no escalation of its own, exactly as asked. Counting it again (+6) pushed the defused path to rung 5 while the undefused path stayed at rung 4, which punished the player for defusing.
- **Systems added outside the spec's lists:**
  - Weekly: war exhaustion (the spec gives it per week) and escalation decay.
  - Monthly: cyber access and detection, and the red-line estimate.
- **Not simulated:** Varan's own politics (its war support is 50).

## D-040 · The narrative engine

- **Director phase timing:**
  - The narrative phase (10) also runs hourly on crisis days, so arc beats land in the hour their event happens (the survey team at 11:00, lights out at 02:00, the deepfake at 06:00). The spec's "one major per 12 hours in Crisis Time" implies hourly firing.
  - The Director's own generic picks happen once a day.
- **Caps:**
  - Arc beats ignore the caps (D-012), but they count in the weekly tallies that cap the Director's generic picks.
  - A generic storylet fires only if its utility reaches 12.
  - The Director samples a softmax over the top 3 with the Historian's temperature (5).
- **Tension:** T = 0.3 K + 0.2 (100 − Approval) + 2.8 Rung + 0.3 A.
  - K (crisis load) = the share of the player's provinces in Crisis Time, × 100.
  - A (arc intensity) = 20 per arc beat in the last 14 days, up to 100.
- **Utility:** U = w_T (T* − T) ι + w_P d/d* + w_D π ND − ρ n + b, with weights 1, 20, 0.5, 10 and 10 × the storylet's weight.
  - n counts uses of the same storylet in the last 30 days.
- **Seeds:**
  - A seed's monthly payoff chance is applied daily as a hazard of p ÷ 30.
  - Seeds are planted by choices. The Director doesn't plant its own seeds in the slice.
  - Narrative Debt is the sum of live and ripe seed weights, up to 100.
- **Casting:**
  - fit = 50 for the role, + 30 for a matching portfolio, + 20 for province, + 20 for faction (up to 100).
  - H (history) = 10 per memory the character holds.
  - U (recent use) = 10 per appearance in the last 90 days.
  - A character fills one role per storylet.
- **Memory:**
  - A memory of strength 50 or more (either way) is a grave act (τ = 730 days); others fade with τ = 90.
  - Salience is 1 in the slice.
- **Not built:**
  - The fact-change index. Every storylet is re-checked every pass; with about 30 storylets that's cheap.
  - Black swans. The Historian's 0.2% monthly chance has no black-swan storylets to draw on in the slice.
- **Choice effects** carry out orders through the same order classes the player uses. A refused order is logged, not silently dropped.
- **Majors** wait for the player. Minors wait in the Brief until they expire, when their default applies. With no player (`run`, batch), the autopilot answers.

## D-041 · The Chronicle's verdict

Five scores, each 0 to 100 (balance.yaml `chronicle`):

| Score | Formula |
| --- | --- |
| Survival | 100 − 15 per rung above the start, − 30 if the rung reached conventional war |
| Prosperity | 50 × (chips made ÷ Day 0 rate × days) + 50 × (the same for drones) |
| Liberty | 100 − 2 × Backsliding − 8 per civil-liberties precedent use − 10 if an emergency is still in force |
| Sovereignty | 50 × magnet cover ÷ 30 days (capped) + 50 × drone design effectiveness |
| Humanity | 100 − 200 × the average share of people in the dark − 1 per death |

- Each historian weights the five scores (`chronicle.yaml`).
- A weighted score of 65 or more gets their high verdict, 45 or more their middle one, otherwise the low one.
- The Declassified epilogue reveals:
  - the rival's real red line, against your estimate
  - who really ran each cyber attack, and how sure your analysts ended up
  - whether a missed seed could have been defused
  - every character's secrets

## D-042 · The Godot UI

- **Architecture:**
  - `SimController` owns the simulation and its clock.
  - The UI reads `SimSnapshot`, `CascadeGraph` and `DecisionView`, and sends orders (including `ChooseStoryletOrder`) through the controller. The game project never writes sim state.
- **Speeds** 1 to 3 are 4, 2 and 1 seconds per day (D-011). In Crisis Time each step is an hour (one twenty-fourth of that).
- **Major decisions** pause the clock and open the dialog; closing it resumes at speed 1.
- **The map** is a schematic, not geography: each province is a rectangle placed by content (`map` in scenario.yaml).
- **The Cascade graph** is laid out by longest path from a source: power and imports on the left, the front on the right.
  - Health: facilities are green at 95%+ of their capacity after the fab ramp, amber at 50%+, red below.
  - Goods are red under 14 days of cover and amber below their replacement lead time. Substations are red when damaged, amber on a mobile unit.
- **Screenshot mode** (`--screenshot FILE --screenshot-day D --tab N`) and **autopilot mode** (`--auto MODE`) let a build be checked without a person at the keyboard.

## D-043 · M6 balance pass

These changes were made after running 1,000 seeds with random choices.

- **Spare transformers:** Kestria now holds 2, not 1.
  - With 1, the Day 17 blackout's median recovery was over 30 days, so the rest of the slice was dominated by one repair queue.
  - With 2, the median is about 14 days.
  - `lights_out` hands both spares out in each option.
- **Deepfake Virality 20:** see D-038.
  - In the spec's model nearly every susceptible person is exposed in the end, and a Plausibility share of them (0.6 here) come to believe. After γ's slow fade, belief settles at about 40% whatever the counter-measure. Counters only delay the tipping point.
  - At 50 it tipped within a day or two of any counter, so choosing one felt meaningless.
  - At 20, the four options spread out. Left alone, it tips around Day 9. Going live also tips it around Day 9. A takedown delays it to about Day 11, and a shutdown to about Day 13.
  - This is a finding for the designer, not a fix. See ideas.md.
- **Storylet repetition:**
  - `generator_fuel` and `towers_dark` have their intensity lowered by 0.2.
  - Cooldown is 45 days for `design_bureau_patch`, `border_town_fear`, `bond_jitters`, `magnet_hoarding` and `shipping_guarantee`.
  - Every storylet now fires in some campaign. The most-used one takes about 9% of all firings.
  - The spec's "no storylet above 2%" can't be met with 28 storylets, since the average is 3.6%.
- **Varan is Cautious, not Pragmatic** (red line mean 70, r = 0.8).
  - With Pragmatic, the meter sits at about 56 after Varan's own script, which is close to the red line of about N(60, 10).
  - So random play reached a Varan offensive in 27% of campaigns, and rung 5+ in 37%. The spec calls war rare in a grey-zone crisis.
  - With Cautious, random play reaches an offensive in 6.5% of campaigns. Forcing the Day 10 strike-back raises that to 24%.
  - To switch back, change one line: `personality:` in `conflict.yaml`.
- **Blackout recovery metric:** a province counts as recovered when served demand is back to 90% of its pre-blackout level.
