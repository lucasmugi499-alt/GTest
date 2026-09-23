# Engine & Mechanics Spec

This tab turns the concept into build-ready rules: the daily tick order, the data model, and the formulas and starting numbers behind every system. Every number is a starting value for tuning, not final balance. UI screens are out of scope for now.

## Conventions

Time runs in days, most soft stats run from 0 to 100, and every random roll is seeded so any save replays identically.

| Quantity | Unit | Range | Notes |
| --- | --- | --- | --- |
| Strategic tick | 1 day | — | All daily systems |
| Crisis sub-tick | 1 hour | 24 per day | Only in regions flagged as in crisis |
| Weekly / monthly systems | Calendar | Mondays / the 1st | Run after that day's daily tick |
| Soft stats | Points | 0 to 100 | Trust, Consent, Grievance, Morale, Readiness, Legitimacy, Threat, Corruption, Satisfaction; 50 is neutral where one exists |
| Rates and probabilities | Fraction | 0 to 1 | Per tick unless labelled |
| Money | M (1 million currency units) | — | One global unit; national currencies convert through an exchange rate |
| Goods | Units per day | — | Each good defines its unit: wafers, cells, drones, tonnes |
| Power | MW capacity, MWh energy | — | Per grid node |
| Distance | km | — | Fronts are measured in km of width |

### Notation

- clamp(x, a, b) limits x to the range a to b.
- σ(x) is the logistic curve 1 / (1 + e^(−x)), used for most probabilities.
- EMA(x, h) is an exponential moving average with a half-life of h days.
- Δ means change per tick unless the formula says per week or per month.

### Determinism rules

- **Fixed-point math** in the sim core: values stored as 64-bit integers at 4 decimal places. Floats appear only in the UI.
- **Counter-based randomness:** every roll is hash(campaign seed, tick, system, entity, roll index). Thread order can never change a result.
- **Ordered merges:** parallel work is merged in entity ID order.
- **Desync check:** multiplayer peers compare a hash of full state every 30 ticks; replays verify the same hash.

## Tick pipeline

Each day runs twelve phases in a fixed order within a 50 ms budget. A phase reads only results committed by earlier phases and writes to a next-state buffer, so work inside a phase can run in parallel by region without changing outcomes.

| # | Phase | Reads | Writes | Budget (ms) |
| --- | --- | --- | --- | --- |
| 0 | Orders | Queued player and AI orders | Policy changes, construction starts, unit orders | 1 |
| 1 | Scheduled events | Event queue: deliveries, operation effects, storylet outcomes | Damage flags, state changes | 2 |
| 2 | Grid dispatch | Generation, transmission, demand, priority lists, damage | Power ratio per node, blackout flags | 4 |
| 3 | Production | Stockpiles, recipes, power ratio, labour, efficiency | Facility outputs, efficiency, fab yield | 6 |
| 4 | Logistics | Outputs, demand, transport graph, smuggling routes | Shipments in transit, deliveries due | 12 |
| 5 | Consumption | Stockpiles, civilian and military demand | Stockpiles, Days of Cover, shortage flags | 2 |
| 6 | Military | Units, supply, fronts, detection | Losses, control of territory, equipment | 12 |
| 7 | Operations | Cyber, electronic warfare, orbital and influence ops | Access levels, queued effects, attribution | 2 |
| 8 | Information | Narratives, platform graph, trust | Belief shares per segment | 4 |
| 9 | Society | Needs, shortages, casualties, beliefs | Satisfaction, Grievance, unrest events | 2 |
| 10 | Narrative | Blackboard of derived facts | Storylets fired, seeds, character memories | 2 |
| 11 | Record | All phase outputs | Event log, UI snapshot, forecast triggers | 1 |

### Weekly and monthly phases

These run after phase 11 on their day.

- **Weekly (Mondays):** markets and exchange rates, bond yields, factions, Political Capital, corporations, Threat and Recognition, opponent AI replanning, and the 90-day forecast refresh.
- **Monthly (the 1st):** research, construction progress, demographics, training graduations, budget and debt, drift of government axes, and insurgency cell spawning.

### Crisis sub-ticks

A region is flagged as in crisis when any grid node drops below 70%, a cyber effect is active, a narrative is within 12 hours of tipping, or a front breaks through. The flag clears after 48 stable hours.

In a crisis region, phases 2, 5 (blackout clocks only), 7 and 8 run 24 times at one-hour resolution before the daily phases. The daily phases then read the day's hourly totals, so crisis regions stay consistent with the rest of the world.

## Data model

The world is sixteen entity types. Goods, recipes, templates and storylets load from moddable files; everything else is live state stored as columnar arrays with 32-bit IDs.

| Entity | Key fields | Updated |
| --- | --- | --- |
| Nation | 4 government axes, budget, debt, Political Capital, emergency powers, stockpile doctrine, recruitment law, mobilization level, Threat and Recognition toward each nation | Daily and weekly |
| Province | Owner, controller, integration stage, terrain, infrastructure levels, Small Arms Density, Corruption | Daily |
| Facility | Province, recipe, capacity, efficiency, power draw, labour by pool, input and output stock, owner corporation, damage | Daily |
| Good | Tier, unit, storable, decay rate, base price | Static |
| Recipe | Inputs, outputs, days per batch, line type | Static |
| Edge | From, to, network (transport, power, digital, water, shadow), capacity, cost, lead days, damage, hidden flag, Heat | Daily |
| Segment | Province, population, livelihood, identity tags, 7 needs, Satisfaction, Trust, Grievance, Consent, Mobilization, belief shares | Daily |
| Faction | Leader, agenda, Leverage, approval of government | Weekly |
| Corporation | CEO, assets, exposure by nation, Loyalty, compliance threshold | Weekly |
| Character | Role, traits, competence by field, 4 loyalties, ambition, secrets, relationships, memories | On events |
| Brigade | Template, strength, equipment fill, Training, Experience, Cohesion, real and reported Readiness, supply fill, home region | Daily |
| Front segment | Sides, width, terrain, fortification, drone density per side, locked flag, kill zone depth | Daily |
| Narrative | Origin, Virality, Plausibility, resonance by segment tag, spread state per segment | Daily or hourly |
| Operation | Type, target node, access level, payload, detection risk, attribution confidence | Daily |
| Storylet | Preconditions, roles, choices, outcomes, weight, cooldown | Static |
| Precedent | Type, use count, last use | On events |

### Sample record

A fab as the engine stores it:

```yaml
facility: tessera_fab_3
province: kestria_east_ossen
owner: corp_tessera
recipe: legacy_wafer_28nm
capacity: 1200            # wafer starts per day
efficiency: 0.94
yield:
  current: 0.88
  experience_days: 780    # cumulative starts / daily capacity
cycle_days: 42            # wafers in process at any time
power_draw_mw: 90
power_sensitivity: fab    # any interruption scraps work in process
labour:
  process_engineers: 850
  technicians: 2400
stock_in:
  wafer_blank: 38000
  neon_gas: 610
damage: 0.0
```

## Economy and dependency web

The scarcest input sets every facility's output, shortages are filled by priority tier, and every buffer turns into a countdown the player can see.

### Production

```latex
Q = C \cdot E \cdot \min\left(1,\ \min_i \frac{S_i}{r_i C}\right) \cdot P \cdot L \cdot (1 - D)
```

Q is output per day, C capacity, E efficiency (0.1 to 1), S\_i the stock of input i, r\_i the amount of input i per unit, P the power ratio, L the labour ratio (the lowest pool's available ÷ required), and D damage (0 to 1).

### Efficiency and retooling

```latex
E_{t+1} = E_t + g\,(E_{max} - E_t)
```

g is 0.01 per day. E\_max is 1.0, or 0.9 for converted dual-use lines. Switching recipe sets E to max(0.1, E × similarity): 0.8 for a revision on the same line type, 0.3 for a new line type.

### Fab yield

```latex
Y(X) = Y_{max} - (Y_{max} - Y_0)\, e^{-X / 285}
```

X is experience in days at full capacity (cumulative wafer starts ÷ daily capacity). Y\_0 is 0.30; Y\_max is 0.92 for legacy and 0.80 for leading-edge. That reaches 85% of the gap in about 18 months.

- Losing engineers multiplies X by (1 − 0.5 × share lost).
- Any power interruption scraps all work in process, capacity × 42 days of starts, then output ramps linearly back over 21 days.

### Shortage allocation

Demand sits in four priority tiers: Critical, High, Normal and Low. Tiers fill in order; the tier where supply runs out is shared proportionally.

```latex
a_j = d_j \cdot \min\left(1,\ \frac{R_k}{D_k}\right)
```

a\_j is what consumer j receives, d\_j its demand, R\_k the supply left when tier k is reached, D\_k that tier's total demand. Defaults: hospitals, water and grid repair are Critical; the military is High from mobilization level 2; industry is Normal; consumers and data centres are Low. The player can reorder any of it.

### Transport

Goods move by min-cost flow over the transport graph, per good class (bulk, container, fuel, high-value), solved at region level. Routes are recomputed weekly or when an edge's capacity changes by more than 10%; daily ticks push flow along cached routes. Shipments arrive after the summed lead days of their path.

### Days of Cover and doctrine

```latex
DoC_g = \frac{S_g + T_g^{(30)}}{\mathrm{EMA}(b_g,\ 7)}
```

S\_g is stock, T\_g^(30) is stock in transit arriving within 30 days, and b\_g is daily burn. The UI warns when Days of Cover falls below the replacement lead time and turns critical under 14 days.

The doctrine slider j runs from 0 (Just-in-Time) to 1 (Just-in-Case). Target stock is (7 + 83j) days of burn. Holding costs 1.5% of stock value per month, and industry gets +3% × (1 − j) efficiency.

### Cascades and forecasting

A shock reaches a downstream node after its input's Days of Cover runs out, plus transit time. Every Monday a forecast copy of the state runs 90 days ahead in weekly steps with policies frozen. After any shock, affected regions re-run at daily steps within 30 ms, which feeds the Cascade lens.

### Sovereignty Score

```latex
SS = \frac{\sum_i v_i\, s_i}{\sum_i v_i}
```

v\_i is component i's share of value, walked recursively through the recipe tree. s\_i is 1.0 for domestic, 0.7 allied, 0.3 neutral and 0 rival sources.

### Countermeasure decay

```latex
e_{w+1} = e_w\,(1 - \delta A)
```

e is a design's effectiveness, δ is 0.03 per week, and A is adversary adaptation (0.5 to 2.0, from enemy electronic warfare tech and how often the design has been used against them). A firmware patch adds 0.10 up to the design cap and takes 5 days of Design Bureau time. A hardware revision resets the cap to 1.0 but forces a retool at similarity 0.8.

### Grid and blackouts

- **Dispatch.** Each region balances available generation plus tie-line imports against demand, hourly in a crisis. If supply falls short, load is shed down the priority list. A node's power ratio P is the share of hours it was powered.
- **Substations.** A damaged substation has zero capacity. A spare from reserve restores it in 14 days; a mobile emergency unit gives 30% in 7 days; a new transformer takes 730 to 1,460 days.
- **Black start.** Regions with black-start plants restore 25% of load per day; others depend on tie-lines from neighbours.
- **Blackout clocks.** Each backed-up service holds fuel hours = fuel stock ÷ burn, counted down hourly. Defaults: cell towers 6 hours, water pumps 18 hours, hospitals 72 hours. Refuelling follows the fuel priority list and needs road access.

## Army and mobilization

Recruitment law sets how many people you can call up, training law and time set how good they are, and mobilization level sets how hard the economy is pushed to arm them.

### Manpower pool

```latex
M = N_{pop} \cdot \epsilon_{law} \cdot \left(0.5 + \frac{WS}{200}\right)
```

N\_pop is total population and WS is war support (0 to 100). ε\_law is 0.01 for volunteer, 0.03 for selective service, 0.08 for universal conscription and 0.15 for total mobilization. Maren's 7 million under selective service at war support 60 gives a pool of about 168,000.

Every soldier leaves a labour pool, in proportion to the skill mix of their home segment. An exemption list protects chosen pools, such as grid linemen, and removes them from M.

### Training and experience

| Unit type | Days to train | Training cap: professional / selective / universal / total |
| --- | --- | --- |
| Infantry | 60 | 80 / 65 / 50 / 35 |
| Mechanized, armour | 120 | 80 / 65 / 50 / 35 |
| Drones, electronic warfare, air defence | 150 | 85 / 70 / 55 / 40 |

Foreign trainers or contractors cut training time by 25% and raise the cap by 10. Experience grows 0.5 per day in combat and 0.1 per day in hard exercises (which burn fuel and ammunition), up to 100. It decays 0.1 per day after 90 idle days.

### Unit quality and cohesion

```latex
Q = 0.35\,T + 0.25\,Eq + 0.20\,Co + 0.20\,X
```

T is Training, Eq equipment quality (tech level × fill), Co Cohesion and X Experience, all 0 to 100. Cohesion drops by 150 × the fraction of strength lost in a day, and recovers 1 per day in reserve.

### Real and reported readiness

```latex
R_{real} = 100 \cdot f_{eq} \cdot f_{parts} \cdot \left(1 - \frac{Corr}{250}\right)
```

```latex
R_{rep} = \min(100,\ R_{real} + 0.6\,Corr)
```

f\_eq is equipment fill, f\_parts is spare parts and fuel fill, and Corr is the unit's regional Corruption. An audit costs 15 Political Capital and 5 approval with the Security Establishment. It sets reported equal to real and halves the corruption leak for 12 months.

### Upkeep and the parts leash

Monthly cost per brigade is base × (0.4 + 0.6 × readiness target ÷ 100). Reserve brigades cost 25% of base. Starting bases are 4 M for infantry, 9 M mechanized, 7 M air defence and 3 M for a drone battalion plus consumables.

Imported systems use spare parts every month. If the supplier embargoes you and stock runs out, f\_parts for those units drops 3% per week.

### Mobilization levels

| Level | GDP diverted | Military output | War exhaustion per week | Time to reach |
| --- | --- | --- | --- | --- |
| 0. Peacetime | 0% | ×1.0 | 0 | — |
| 1. Heightened readiness | 1% | ×1.2 | 0 | 7 days |
| 2. Partial mobilization | 5% | ×1.8 | 0.2 | 14 days |
| 3. Full mobilization | 15% | ×3.0 | 0.5 | 30 days |
| 4. Total war | 30% | ×4.5 | 1.2 | 30 days, needs Emergency Powers |

Stepping down takes 30 days per level, and each step fires a demobilization storylet about veterans and factory reconversion.

### Buyer to builder

| Stage | Requirement | Lead time | Parts dependency |
| --- | --- | --- | --- |
| Buy | Supplier relations 40+, export licence | 90 to 365 days per delivery | 100% |
| License | Supplier consent, matching factory line | 180 days to first output | 50% |
| Adapt | Design Bureau plus a licensed or captured design | 120 days per variant | 25% |
| Design | Required tech level, Design Bureau size 3+ | 2 to 4 years | 0 to 10% |
| Export | An indigenous design, buyer relations 30+ | 90 days per deal | You now hold the leash |

Captured enemy equipment adds 50% progress toward an Adapt variant of that system.

### Deterrence

Rivals estimate your Deterrence as the combat power of your forces, times the readiness their intelligence believes, plus noise that shrinks as their intel quality rises. The opponent AI picks targets partly by comparing its own Deterrence with the target's.

## Combat and fronts

Combat resolves once a day per front segment of 20 to 40 km. Detection multiplies everything, so the side that sees first usually wins, even outnumbered.

### Detection

```latex
p_A = \sigma\left(\frac{Rec_A - Con_B}{10}\right)
```

Rec\_A is A's recon score: min(100, 40 × drone surveillance cover + 30 × satellite access + 20 × signals intelligence + 10 × human intelligence) × (1 − B's jamming suppression), each input 0 to 1. Con\_B is B's concealment (0 to 100) from fortification, dispersion, terrain and its own electronic warfare.

### Combat power and force ratio

```latex
CP = N \cdot \frac{Q}{50} \cdot S \cdot M
```

```latex
\rho = \frac{CP_{att}\, p_{att}}{CP_{def}\, p_{def}\, \phi}
```

N is strength in personnel equivalents, Q unit quality, S supply fill (0 to 1) and M morale (0.5 to 1.2). φ is fortification: 1.0 open, 1.5 prepared, 2.0 fortified, 2.5 urban.

### Losses and advance

```latex
L_B = k \cdot \frac{CP_A\, p_A}{CP_B} \cdot (1 - \pi_B)
```

```latex
v = v_{max}\left(1 - e^{-(\rho - 1.5)}\right) \lambda \quad \text{for } \rho > 1.5
```

L\_B is the fraction of B's strength lost per day, with k = 0.01. π\_B is protection (0 to 0.8) from air defence, jamming, armour and fortification. v is advance in km per day: v\_max is 3 for mechanized and 1 for infantry, and λ is 0.1 on a locked front, otherwise 1. Below ρ = 1.5 nobody advances.

### Locked fronts

A segment locks when both sides field at least 30 drones per km per day and both detection values are at least 0.6. The attacker opens a five-day window (λ = 1) by holding jamming suppression of 0.7+, counter-drone cover of 0.6+ and three times the defender's fires on that segment for three straight days.

### Kill zone

```latex
K = \min(25,\ 5 + 0.3\,\rho_{drone})
```

K is kill zone depth in km and ρ\_drone is enemy drone density per km of front. Each supply truck trip through it has a 0.02 × ρ\_drone ÷ 10 chance of loss. Uncrewed vehicles multiply that by 0.3, night movement by 0.6 (but halve throughput), and dispersed caches by 0.7 (but add 20% stock).

### Air and casualties

Air control runs from −100 to 100 per region, set by air defence density against enemy suppression and fighters. Deep strike success is σ((air control + strike quality − target air defence) ÷ 15).

Losses split one killed to three wounded. Half the wounded return after 60 days if medical supply is at least 0.7. Every casualty is booked to the brigade's home region for the society systems.

### Worked example

Day 3 of the Maren Gambit: a Maren mechanized brigade attacks a Dravek conscript brigade in prepared positions (φ = 1.5).

| Value | Maren (attacker) | Dravek (defender) |
| --- | --- | --- |
| Strength N | 4,000 | 10,000 |
| Quality Q | 70 | 35 |
| Supply S | 0.9 | 0.6 |
| Morale M | 1.1 | 0.8 |
| Combat power CP | 5,544 | 3,360 |
| Recon vs enemy concealment | 75 vs 40 | 40 vs 60 |
| Detection p | 0.97 | 0.12 |
| Protection π | 0.4 | 0.2 |

Force ratio ρ is about 8.9, so Maren advances near its 3 km per day maximum. Dravek loses about 1.3% of strength a day, roughly 128 soldiers; Maren loses about 2.

Maren wins by seeing first, not by numbers. If Dravek's jamming cut Maren's recon from 75 to 45, Maren's detection falls to 0.62 and Dravek's daily losses fall by about a third.

## War, peace and conquest

Legitimacy and exhaustion decide how long a nation can fight. War Score decides what it can take. Threat, Overreach and Recognition decide what taking it costs.

### Legitimacy

| Pretext | Base Legitimacy |
| --- | --- |
| Answering an attack | 70 |
| Alliance obligation | 60 |
| Protecting citizens abroad | 45 |
| Pre-emption | 35 |
| Territorial claim | 30 |

Published evidence adds 0 to 15, scaled by intel quality, and a great power's endorsement adds 10. A manufactured incident adds 20 while hidden. Its monthly exposure chance is 0.02 × (1 + rival counter-intelligence ÷ 50). Exposure costs 40 Legitimacy, sets a precedent, and multiplies this war's Threat by 1.5.

### Exhaustion and war support

```latex
\Delta X_{week} = 3\,\frac{KIA_{week}}{N_{pop}/10^5} + 5\,h_{black} + 2\,r + e_{mob} - 3\,V
```

```latex
WS = \mathrm{clamp}\left(0.5\,L + 0.5\,Appr + Rally - X,\ 0,\ 100\right)
```

X is war exhaustion (0 to 100), KIA\_week deaths that week, N\_pop population, h\_black the share of people in blackout, r the rationing level (0 to 3), e\_mob the weekly value from the mobilization table, and V decisive victories that week. Dividing by population per 100,000 is what makes a small nation's losses hurt more. Rally starts at 20 when you are attacked and decays 1 per week.

- Below 40 war support: protest storylets fire.
- Below 25: desertion rises 50%, coalition partners may leave, and mobilization cannot be raised.
- Below 15: a monthly government-collapse check.

### War Score

```latex
WSc_A = 50\,G_A + 30\,\frac{X_B}{100} + 20\,\frac{Loss_B}{Loss_A + Loss_B}
```

G\_A is the fraction of war goals achieved (goal provinces held for 14 days), X\_B the enemy's exhaustion, and Loss the cumulative losses of each side.

| Peace term | War Score cost |
| --- | --- |
| Annex a goal province | 40, plus 10 per extra province |
| Client government | 60 |
| Neutrality treaty | 25 |
| Resource concession, 30 years | 20 |
| Reparations | 2 per 1% of enemy GDP, up to 20 |
| Demilitarized zone | 15 |
| White peace or prisoner exchange | 0 |

The AI accepts terms that cost no more than the demander's War Score if its exhaustion is 50+, or if continuing looks worse to its planner. If a front stays locked for 60 days with both War Scores under 40, the AI offers a ceasefire. Refused, it becomes a frozen conflict held at escalation rung 3 or 4.

### Threat and coalitions

```latex
\Delta Thr_{N \to A} = 15\, w \cdot m \cdot \frac{1}{1 + h}
```

This is how much nation N's fear of conqueror A rises per province taken. w is the province's weight (0.5 minor, 1 average, 2 major resource hub). m is the method: 0.3 retaking your own core, 0.5 client state, 1.0 negotiated annexation, 1.5 manufactured pretext, 2.0 atrocity event. h is how many borders separate N from the province.

Threat decays 2 per month in peace and not at all during war. At 30, N raises defence spending; at 50, it signs defence pacts and invites foreign bases; at 70, it sanctions. At 85, a monthly coalition check runs: war is declared if at least two nations are past 85, their combined combat power is at least 1.2 times A's, and each has war support of 40+.

Taking Saltmere (w = 2) adds 30 Threat with every direct neighbour.

### Overreach

```latex
O = \frac{\sum_p \ell_p \cdot pop_p}{Cap}
```

pop\_p is each non-core province's population in millions. ℓ\_p is 3 when occupied, 2 administered, 1 integrated and 0 once core. Cap starts at 3 and rises by 1 per bureaucracy tech level, plus administration spending.

Above O = 1, industrial efficiency falls 20% × (O − 1) nationwide. Corruption in non-core provinces rises 2 × (O − 1) per month, and unrest storylets grow more likely. Occupied Saltmere alone (1.2 million people) puts Maren at O = 1.2.

### Recognition

```latex
\Delta R_N = 0.5 + 0.05\,(\bar{C} - 50) - 0.01\, Thr_{N \to A}
```

R\_N is nation N's recognition of the annexation, updated monthly. C̄ is average Consent in the province. A credible monitored referendum adds 25 once; a rigged one that is exposed costs 15. Diplomatic trades add 10 to 30 once. Recognition is granted at 60, and sanctions apply below 40.

## Territory and insurgency

Every conquered segment carries Grievance and Consent, updated monthly. Consent moves the province through its stages; Grievance feeds the insurgency.

### Grievance and Consent

```latex
\Delta G = 5\,ML + 3\left(1 - \frac{Sat}{100}\right) + 0.5\,\frac{KIA_{loc}}{pop/10^5} + 2\,CR + 2\,n_{raid} - 2\,Aut - 0.5\,Inv
```

```latex
\Delta C = 0.06\,(Sat - 50) + 2\,Aut + 1.5\,Cit + 0.5\,Inv - 0.03\,G
```

ML is martial law (0 or 1), Sat the segment's Satisfaction, KIA\_loc local deaths that month, CR restrictive citizenship rules (0 to 1), n\_raid raids that month, Aut autonomy (0 to 1), Inv investment tier (0 to 3) and Cit generous citizenship (0 to 1).

### Stage transitions

| Transition | Condition |
| --- | --- |
| Occupied to Administered | Control 80+ for 3 months and a governor appointed |
| Administered to Integrated | Consent 50+ and Grievance 40 or less, held for 12 months |
| Integrated to Core | Consent 70+, held for 36 months |
| Back to Occupied | Control falls below 50 |

Control = 100 × security strength ÷ (security strength + insurgent strength) in the province.

### Insurgency

```latex
I = \frac{G}{100} \cdot \frac{SAD}{100} \cdot (1 + OS) \cdot u
```

SAD is Small Arms Density, OS outside support (0 to 2, delivered through smuggling routes), and u the terrain factor: 0.8 rural, 1.0 mixed, 1.3 urban. Above I = 0.1, a new cell of 50 to 500 fighters spawns each month with probability I, hidden until found.

- **Detection** per cell per week: σ((intel + 0.3 × Consent − cover) ÷ 10). Intel comes from informants and counterinsurgency spending; cell cover runs 30 to 70.
- **Raids** destroy a detected cell with probability 0.7 × security quality ÷ 70. Each raid adds Grievance and has a 10% × (1 − rules-of-engagement strictness) chance of a civilian-harm event.
- **Cell actions** each week: 5% chance to sabotage the nearest facility (0.1 to 0.3 damage), 3% chance to hit a convoy, and 10% strength growth while Grievance is above 60.
- **Amnesty** dissolves 20% × (Consent ÷ 50) of cell fighters per offer, and costs Nationalist approval.

### Captured industry

Captured facilities restart at efficiency 0.15 × (1 − damage). Their specialists return at a monthly rate of:

```latex
r = 0.02 \cdot \frac{C}{50} \cdot w \cdot (1 - I)
```

w is the wage premium you pay (1.0 to 2.0). Replacements from your own training pipeline take 18 months. Foreign-built equipment under sanctions gains 0.02 damage per month unless you source parts, usually through the grey market.

### Client states

A client government's Loyalty drifts 1 point per month toward your aid level plus its trade share with you, minus your interference in its politics. Below 30, it may switch blocs.

## Shadow economy

Smuggling routes are hidden edges in the dependency graph with a price markup, a Heat level and a chance of discovery. Using them leaves Corruption and loose weapons behind.

### Routes, markup and Heat

Price through a route = legal price × μ, where μ = μ\_0 × (1 + 0.5 × sanctions level). μ\_0 is 1.3 to 2.0 on the grey market and 2.0 to 4.0 on the black market.

```latex
\Delta H_{week} = 5\,u - 3
```

```latex
p_{disc} = 0.002 \cdot H \cdot \frac{CI}{50}
```

H is Heat (0 to 100), u the route's utilisation (0 to 1), and CI the opposing counter-intelligence (0 to 100). Running above 60% utilisation heats a route up. At Heat 50 against average counter-intelligence, discovery is 10% per week. Discovery seizes the cargo and fires a storylet: the broker is arrested, flips, or talks to a journalist.

### Counterfeits and poisoned supply

The counterfeit rate is φ = φ\_base × (1 − broker reputation ÷ 150), with φ\_base of 3 to 8% on the grey market and 8 to 20% on the black market. Each product built with those parts fails in the field with probability φ.

To poison an enemy route, your intelligence service spends 60 days penetrating it. For the next 90 days, that route's counterfeit rate becomes 30 to 50%. If discovered, the enemy abandons the route and escalation rises.

### Corruption

```latex
\Delta Corr = 10\,s_{sh} + 2\max(0,\ O - 1) + E - 3\,n_{aud} - \frac{RoL - 50}{25}
```

Updated monthly per province and ministry. s\_sh is the share of trade by value moving through shadow routes, O is Overreach, E is 1 while Emergency Powers are active, n\_aud is audits that month, and RoL is the rule-of-law axis (0 to 100).

Effects: characters in that province or ministry leak secrets with monthly probability 0.005 × Corr, a share of Corr ÷ 200 of local spending is skimmed, and military readiness gaps widen.

### The home-front black market

When a price ceiling sits below the market-clearing price, black market volume = shortage × (1 − enforcement), at a price of the clearing price × (1 + 0.5 × (1 − enforcement)). Enforcement (0 to 1) consumes police capacity. The black market lowers Satisfaction for low-income segments and raises crime.

### Small Arms Density

```latex
\Delta SAD = 20\,n_{loot} + 0.5\,A_{bm} + A_{proxy} - 1 - 2\,BB - BC
```

Updated monthly, clamped 0 to 100. n\_loot is depots looted that month, A\_bm the black market arms volume index, A\_proxy spillover from proxies, BB a buyback programme (0 or 1) and BC border control (0 or 1). Crime scales by 1 + SAD ÷ 100.

**Proxy blowback.** Each month, 2% of weapons supplied to a proxy leak into neighbouring shadow networks. When the proxy's war ends, 20% of its stock becomes loose Small Arms Density in its region and every neighbour, including yours.

## Cyber, orbital and information

Cyber access grows slowly and vanishes on detection, payloads burn on use, and narratives spread through population segments like an epidemic that trust can slow.

### Cyber access and detection

```latex
\Delta A_{month} = \frac{Sk}{10} \cdot v
```

```latex
p_{det} = 0.03 \cdot \frac{A}{50} \cdot \frac{Def}{50}
```

A is access to one target (0 to 100), Sk your cyber skill (0 to 100), v the target's vulnerability (0.2 hardened to 1.5 legacy systems) and Def its defence (0 to 100). Detection is checked monthly; it resets A to 0, raises the target's defence by 10, and fires an attribution event.

### Payloads, effects and attribution

- **Development** takes 30 to 180 days. Minimum access: 40 to disrupt, 60 to manipulate data, 70 to destroy equipment.
- **Grid disruption** lasts 12 × (A ÷ 50) × payload power (0.5 to 2) × (1 − analog reserve, 0 to 0.8) hours.
- **Burn:** after use, every target running the same vendor systems patches with 50% chance within 30 days.

```latex
c(t) = c_{max}\left(1 - e^{-t/14}\right)
```

c is attribution confidence t days after the attack. c\_max is 0.9 for a direct operation and 0.6 through a proxy. A false flag gives a 10 to 30% chance the target blames the framed nation instead.

### Electronic warfare and autonomy

A jammer projects suppression s (0 to 1) over a radius. Link-dependent drones perform at (1 − s); autonomous drones at (1 − 0.3 s). GPS spoofing multiplies precision munition miss distance by (1 + 4 s).

Autonomy incidents per 1,000 sorties: 0.1 with a human in the loop, 0.5 on the loop, 2 out of the loop. Each incident fires a misidentification, civilian-harm or friendly-fire storylet.

### Orbital

Constellation service = 1 − (lost ÷ total)² for large constellations of 500+ satellites, and 1 − lost ÷ total for small ones. Losing 10% of a large constellation costs about 1% of service.

The global Debris meter (0 to 100) gains 5 per kinetic kill in low orbit and decays 0.5 per year. Above 30, low-orbit satellites fail 1% more per year. Above 60, they fail 5% more and launches cost 50% more. Above 85, low orbit is unusable for decades.

### Narrative spread

Each narrative runs a contagion model per segment s. The segment's population splits into susceptible (S), exposed (E), believing (B) and rejecting (R) shares that sum to 1.

```latex
\frac{dE_s}{dt} = \beta\, V R_s \left(1 - \frac{T_s}{150}\right) S_s \sum_{s'} w_{ss'} B_{s'} - \eta\, E_s
```

```latex
\frac{dB_s}{dt} = \eta\, Pl\, E_s - \gamma\, B_s
```

V is Virality, R\_s resonance with the segment, Pl Plausibility, T\_s the segment's Trust, and w the platform-weighted contact between segments. β is 0.6 per day, η 0.5 per day and γ 0.02 per day. Exposed people who don't believe move to rejecting at η(1 − Pl). Steps are daily, hourly in a crisis.

| Counter-measure | Effect |
| --- | --- |
| Prebunking | Moves 1% of targeted susceptible people to rejecting per day |
| Counter-narrative | Triples γ for 7 days |
| Platform takedown | Cuts that platform's contact weight to 30%, if the company complies |
| Regional internet shutdown | Sets contact weight to 0 in the region; Connectivity need drops to 0 |

When believers pass 25% of a segment, the narrative is **established** there: it shifts votes and faction approval, and γ falls to 0.005. The Rumor Velocity window shown to the player is the forecast time until any segment crosses 25%.

## Politics, population and markets

Seven needs set each segment's Satisfaction, Satisfaction sets Approval, Approval earns Political Capital, and markets price the risk of everything you do.

### Needs and Satisfaction

Satisfaction is a weighted sum of seven needs, each scored 0 to 100, with weights set by the segment's livelihood. It is smoothed over 30 days, or 7 days for Power and Safety, because people feel blackouts and bombs immediately.

| Need | Score |
| --- | --- |
| Power | 100 × hours powered ÷ 24 |
| Prices | 50 + 10 × (wage growth − inflation, in points), clamped |
| Jobs | 100 − 5 × unemployment rate in % |
| Safety | 100 − 2 × (violent incidents + deaths per 100,000 this month) |
| Connectivity | 100 × network availability |
| Services | 100 × average availability of water, health care and transit |
| Dignity | 50, plus the civil-liberties axis offset and identity treatment, minus precedent penalties |

### Approval and Political Capital

```latex
Appr = \frac{\sum_s pop_s\,(0.6\,Sat_s + 0.4\,Align_s)}{\sum_s pop_s}
```

Align\_s is how closely the segment's beliefs match government positions (0 to 100). Political Capital grows each week by 2 + 0.1 × (Approval − 50), plus 15 per Mandate goal completed, plus any rally effect, up to a cap of 300.

| Action | Political Capital cost |
| --- | --- |
| Pass a law or decree | 10 to 50 |
| Appoint a minister or general | 5 to 20 |
| Audit readiness | 15 |
| Declare an emergency | 30 |
| End an emergency | 10 per month it lasted, up to 100 |
| Nationalize a company | 60 |

### Factions

Leverage = 100 × members' population share × Mobilization ÷ 100, plus 0 to 30 for institutions the faction controls. Faction approval of the government is its agenda alignment, smoothed weekly. When approval is under 30 and Leverage over 20, a faction action fires each week with probability 0.1 × Leverage ÷ 50: strikes, leaks, protests, or a no-confidence motion.

In authoritarian states, elite loyalty is the weighted approval of the Security, Industry and party factions. Below 30, a monthly coup check fires with probability 0.05 × (30 − loyalty) ÷ 10 × Security Leverage ÷ 50.

### Elections

```latex
P(p \mid s) = \frac{e^{u_{s,p}}}{\sum_q e^{u_{s,q}}}
```

The chance segment s votes for party p. u\_{s,p} = 0.05 × alignment of s with p's platform, plus 0.02 × (Approval − 50) for the incumbent, plus campaign and narrative effects. Seats are allocated by first-past-the-post or proportional representation, set per nation.

### Emergency Powers and drift

The Backsliding meter (0 to 100) rises 1 per month per active emergency power and 5 on each first-time precedent, and falls 0.5 per month when none are active. Each month, every government axis drifts by 0.02 × Backsliding toward closed information, centralization and rule by decree.

### Markets

```latex
\pi = m - g + 0.3\,\Delta p_{imp} + 0.5\,s_{short}
```

```latex
y = 3 + 0.03\max\left(0,\ \frac{Debt}{GDP} - 60\right) + 0.5\,Rung + \frac{50 - Cred}{25}
```

π is inflation in %, m money growth from monetized deficits, g real growth, Δp\_imp the change in import prices, and s\_short the share of goods in critical shortage. y is the 10-year bond yield in %, with debt-to-GDP in %, Rung the escalation rung and Cred fiscal credibility (0 to 100). A downgrade storylet fires when y crosses 6% and again at 9%.

- **Currency:** falls 0.5% for each point the yield spread widens, and 3% per new sanctions package that week. A weaker currency feeds import prices into inflation.
- **War-risk insurance:** at escalation rung 2+, port premiums multiply by (1 + Rung). From a multiplier of 4, each shipping line has a 20% weekly chance of skipping the port.

### Corporations

A corporation complies with a government demand when:

```latex
K + \frac{Loy}{2} + P > 100\,x_r + c
```

K is contract value offered (0 to 50 points), Loy its Loyalty (0 to 100), P Political Capital spent to pressure it (up to 50), x\_r its share of revenue from the rival nation (0 to 1), and c the cost of complying (0 to 50).

When regulatory burden exceeds 60 and Loyalty is under 30, it considers relocating each month with probability 0.02 × (burden − 60) ÷ 10. Nationalization makes it a state asset, cuts fiscal credibility by 15, lowers every other corporation's Loyalty by 10, and sets a precedent.

## Narrative engine

Storylets are data records with preconditions, roles and effects. The Director scores every eligible storylet against a target tension curve, and characters remember what you did to them with fading salience.

### Storylet record

```yaml
storylet: wreck_at_veyl
tier: major
weight: 1.0
cooldown_days: 3650          # once per campaign
preconditions:
  - rival.drone_ops_near_border >= 1
  - province(border).controller == player
  - escalation.rung(player, rival) in [1, 3]
roles:
  finder: { role: border_officer, province: border }
  dove:   { role: minister, portfolio: foreign }
choices:
  return_quietly:
    effects: { escalation: -5, faction.nationalists: -8 }
  study_it:
    effects: { design_bureau.ew_insight(rival): +0.10, escalation: +5 }
    seeds: [ chips_in_wreck ]
  televise:
    effects: { rally: +6, escalation: +10, rival.must_respond: true }
memories:
  finder: { return_quietly: -20, study_it: +15, televise: +10 }
```

A storylet is eligible when every precondition holds, its cooldown has passed and every role can be cast. Only storylets whose precondition facts changed this tick are re-checked, using an index keyed by fact.

### Tension and Director scoring

```latex
T = 0.3\,K + 0.2\,(100 - Appr) + 2.8\,Rung + 0.3\,A
```

```latex
U_k = w_T\,(T^{*} - T)\,\iota_k + w_P\,\frac{d}{d^{*}} + w_D\,\pi_k\,ND - \rho\, n_k + b_k
```

T is current tension (0 to 100): K is crisis load, Rung the highest escalation rung, and A the intensity of active arcs. For storylet k, ι\_k is its intensity (−1 calming to +1 escalating), d days since the last major beat, π\_k is 1 if it pays off a seed, ND is Narrative Debt, n\_k recent uses of similar storylets, and b\_k its base weight. The Director samples from the top scores with a seeded softmax.

| Director | Target tension T\* | Major beat every | Black swan chance per month | Randomness |
| --- | --- | --- | --- | --- |
| The Historian | 35, slow waves | 90 days | 0.2% | Low |
| The Novelist | 40, citizen storylets doubled | 60 days | 0.3% | Medium |
| The Chaos Engine | 50 | 50 days | 1.5% | High |
| The Thriller Writer | 55, sharp spikes | 45 days | 0.5% | Medium, seeds pay off more |

Caps: at most one major and three minor storylets per week in Strategic Time, and one major per 12 hours in Crisis Time.

### Seeds and Narrative Debt

A seed has a minimum delay of 30 to 240 days, then a monthly payoff chance of 10 to 30%, and defuse conditions such as a forensic sweep. Narrative Debt is the summed weight of live seeds (0 to 100). The Director plants new seeds when debt is low and tension is under target.

### Casting

```latex
score(c, r) = fit(c, r) + 0.5\,H_c + 0.3\,Dr_c - 0.5\,U_c
```

fit is how well character c matches role r (0 to 100), H\_c their history with the player, Dr\_c drama potential from conflicting loyalties, secrets and ambition, and U\_c recent use (10 per appearance in the last 90 days).

### Precedents

The n-th use of a precedent costs base × 0.6^(n − 1): 100%, 60%, 36%, 22%, then 13%. From the fourth use the precedent is normalized: its crisis storylet stops firing, and each normalized civil-liberties precedent permanently lowers the Dignity need by 3.

### Character memory

```latex
Op_{c} = \sum_m v_m\, s_m\, e^{-t_m / \tau_m}
```

Op\_c is character c's opinion of the player, summed over memories m. v\_m is valence (−100 to 100), s\_m salience (0 to 1), t\_m days since the event, and τ\_m 730 days for grave acts or 90 days for minor ones. Below −40, betrayal, leak, defection and resignation storylets become eligible.

### Citizen Lenses

At campaign start, five citizens are drawn from segments chosen for maximum spread of region, livelihood and identity. Their life events come from segment-conditioned storylets. A citizen who takes three public actions is promoted into the Cast.

## Escalation and opponent AI

Each pair of nations shares an escalation meter from 0 to 100. Every action adds weight as each audience perceives it, and each AI hides a red line the player can only estimate.

### Escalation meter

The meter E for a pair of nations adds the perceived weight of every action between them, and decays 1 per week when no action of weight 3+ occurs. Rungs sit at 0 to 14 (competition), 15 to 29 (grey zone), 30 to 44 (covert action), 45 to 59 (limited kinetic), 60 to 74 (conventional war), 75 to 89 (strategic strikes) and 90+ (strategic threshold).

| Action | Weight | Meter floor |
| --- | --- | --- |
| Strike on homeland infrastructure | 10 | 75 |
| Declare war | 10 | 60 |
| Anti-satellite weapon use | 9 | — |
| Border clash with deaths | 8 | 45 |
| Drone strike on a military target | 7 | — |
| Disruptive cyber attack or sabotage | 6 | — |
| Cyber intrusion detected | 4 | — |
| Influence campaign detected | 3 | — |
| Export controls | 2 | — |

### Perception

Each audience scales an action's weight by its own factor κ. The rival uses 1.0, or 1.5 if civilians were harmed. Allies use 0.5 when your Legitimacy is 50+, otherwise 1.0. Your own public uses 0.3 to 1.5 depending on how visible the action is, and the wider world uses 0.8. The rival's perception drives retaliation, allies' drives support, your public's drives war support, and the world's drives Threat.

### Red lines

Every AI nation draws a hidden red line RL at campaign start, from a normal distribution with a standard deviation of 10 around its personality's mean. Domestic politics move it: −10 when war support or nationalists are strong, +10 when its economy is weak.

The player sees RL plus noise with a standard deviation of 20 × (1 − intel quality), refreshed monthly. When E crosses RL, the AI escalates by one rung.

Below its red line, the AI answers each action within 1 to 14 days with one of its own, of weight e × r, where r runs 0.8 to 1.3 by personality and is limited by what it can actually do.

### Personalities

| Personality | Goal weights: security / prosperity / survival / expansion / prestige | Red line mean | Retaliation r |
| --- | --- | --- | --- |
| Cautious | 0.35 / 0.30 / 0.25 / 0.00 / 0.10 | 70 | 0.8 |
| Pragmatic | 0.25 / 0.35 / 0.20 / 0.10 / 0.10 | 60 | 1.0 |
| Aggressive | 0.20 / 0.20 / 0.20 / 0.25 / 0.15 | 50 | 1.2 |
| Reckless | 0.15 / 0.10 / 0.20 / 0.35 / 0.20 | 40 | 1.3 |

### Strategic planning

Each week, the AI scores the world against its goal weights and breaks its top goal into a plan. For example, expansion becomes: isolate the target, build forces, find a pretext, go to war. Each step becomes concrete orders for production, diplomacy and grey-zone operations. Reactive rules handle crises within the same tick.

```latex
score(t) = V_t \cdot \frac{D_{self}}{D_t} \cdot (1 - \hat{B}_t) \cdot \frac{1}{1 + h_t}
```

This is how the AI ranks expansion targets. V\_t is the target's resource value, D\_self and D\_t the two Deterrence estimates, B̂\_t its forecast Threat backlash (0 to 1), and h\_t borders between them. Before declaring war, the AI runs the same 90-day forecast engine the player's Cascade lens uses. It only attacks if the forecast shows a War Score of 40+ by day 90.

### Fair play

The AI sees only what its sensors and intelligence detect, with the same noise the player gets. Higher difficulty raises its intel quality and planning depth, never its resources.

## Balance targets and test plan

These targets are what the formulas above are tuned toward. Automated campaigns check them every night, so a change to one system can't quietly break another.

### Design targets

| Metric | Target |
| --- | --- |
| Campaign length, 2031 to 2050 | 30 to 50 hours including pauses |
| Game speed | Speed 1: 4 s per day; speed 3: 1 s per day; speed 5: as fast as the tick allows |
| Time spent in Crisis Time | 5 to 10% of in-game days |
| Major story beat | Every 45 to 90 days, by Director |
| Hot wars involving the player | 1 to 3 per campaign, plus 3 to 6 grey-zone confrontations |
| Small-versus-big war | 1 to 6 months to ceasefire or locked front |
| Locked fronts | 50 to 70% of wars between drone-capable sides lock by month 3 |
| Occupied to Core | 5 to 12 years with good policy; never reached through martial law alone |
| Coalition against a snowballer | Forms after 2 to 3 major conquests within 10 years |
| Rising Power build-up | A nation of 7 million fields 40,000 to 50,000 troops by year 3 at mobilization level 1, with GDP still growing |
| Regional blackout recovery | 3 to 14 days, depending on black-start capacity and spare transformers |
| Critical inputs at start | 20 to 60 Days of Cover under default doctrine |
| Fab construction | 1,100 to 1,800 days |
| Transformer replacement | 730 to 1,460 days |

### Automated campaign testing

Every night, each build runs 1,000 headless 20-year campaigns across random seeds, all nation archetypes and all four Directors. At the 50 ms tick budget, one campaign takes about six minutes headless.

| Check | Pass band |
| --- | --- |
| Storylets that never fire | Under 5% of the library |
| Any one storylet's share of all fires | Under 2% |
| Campaigns where some nation doubles its territory | 5 to 15% |
| Campaigns where a nation's GDP falls more than 50% | Under 10% |
| Campaigns that cross the strategic threshold | Under 1% |
| Identical state hash at day 3,650 on replay | 100% |
| Daily tick, 95th percentile, reference hardware | Under 50 ms |

### Scenario regression tests

Both sample scenarios replay with fixed choices, and key outcomes must land in range. In The Veyl Crossing, the eastern blackout must cut legacy chip output by 35 to 45%. In The Maren Gambit, the Dravek lowland front must collapse in 7 to 15 days.

### Human playtests

At each roadmap phase gate, new players must correctly explain the root cause of a shortage in at least 80% of cases. If they can't, the simulation is too opaque, however accurate it is.
