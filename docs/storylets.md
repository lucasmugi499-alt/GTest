# Writing storylets

Storylets live in `content/scenarios/<scenario>/storylets.yaml`, and characters in `characters.yaml` next to it. The loader checks every fact, effect and id when the game starts, and stops with the file and line if something is wrong.

## A storylet

```yaml
- id: wreck_at_veyl
  arc: veyl                # scenario beat: fires when its preconditions hold, ignores the Director's caps (D-012)
  tier: major              # major pauses the game; minor waits in the Brief
  weight: 1                # the Director's base weight b_k
  intensity: 0.6           # ι: −1 calming … +1 escalating
  cooldown_days: 3650      # once per campaign
  expires_days: 2          # optional: after this many days the default choice applies on its own
  default: ignore          # required if expires_days is set
  title: "The wreck at Veyl"
  text: >
    {finder} secured the drone before Varan could ask for it back.
  preconditions: [ "rival.drone_ops_near_border >= 1", "rung in [1, 3]" ]
  roles:
    finder: { role: border_officer, province: veyl }   # also: portfolio, faction, opinion_below
  choices:
    - id: study_it
      text: "Hold it and study it."
      hint: "What the player is told before choosing."
      requires: [ "pc >= 5" ]                           # optional: the choice is offered only if these hold
      effects: { design.fpv_strike_mk2: 0.1, escalation: 5, flag.wreck_studied: 1 }
      memories: { finder: 15 }                          # how the cast character remembers it (−100 … 100)
      seeds: [ chips_in_wreck ]                         # optional: seeds this choice plants
```

## Conditions

Conditions take the form `fact op value`, where `op` is one of `>= <= == != > <`, or the form `fact in [a, b]`.

**Campaign and politics**

| Fact | Meaning |
| --- | --- |
| `day`, `hour` | Campaign day, crisis hour |
| `rung`, `meter` | Escalation with the rival (rung 1–7, D-003) |
| `approval`, `trust`, `pc`, `war_support`, `rally`, `legitimacy`, `inflation`, `backsliding` | The player nation's readouts |
| `mobilization`, `emergency`, `kia_total`, `lines_calling`, `people_dark`, `tension`, `narrative_debt` | More readouts; `emergency` is 0 or 1 |
| `rival.drone_ops_near_border`, `rival.red_line_estimate` | About the rival |
| `front.active`, `front.locked` | The Veyl front, 0 or 1 |

**Grid, economy and production**

| Fact | Meaning |
| --- | --- |
| `province.<id>.dark` / `.dark_share` / `.crisis` / `.subs_down` | Grid state in a province |
| `doc.<good>`, `produced.<good>` | National Days of Cover; output today |
| `facility.<id>.power` / `.ramp` / `.interruptions` / `.run` | A facility |
| `service.<load>.fuel_hours` / `.availability` | A backed-up service's blackout clock |
| `import.<good>.controlled` / `.closed` | Export controls on a good; whether every route for it is shut |

**Society, narratives and cyber**

| Fact | Meaning |
| --- | --- |
| `narrative.<id>.active` / `.belief` / `.established` / `.rumor_hours` | A narrative; `.belief` is the highest believing share |
| `op.<id>.state` / `.attribution` / `.access` | A cyber operation; `.state` is 0 ready, 1 used, 2 detected, 3 patched |
| `faction.<id>.approval` / `.leverage` | A faction |
| `segment.<id>.satisfaction` / `.trust` / `.power` | A population segment |
| `design.<id>.effectiveness` / `.cap` | A weapon design |

**Narrative state**

| Fact | Meaning |
| --- | --- |
| `character.<id>.opinion` | Opinion of the player (spec Character memory) |
| `cast_opinion_min.<role>` | Lowest opinion among characters with that role |
| `flag.<name>` | A flag set by a choice; 0 until set |
| `seed.<id>.ripe` / `.live` | A seed's state; a storylet with `seed.X.ripe == 1` is that seed's payoff |
| `event.<subject>` / `event_day.<subject>` | How many log entries have this subject, and the day of the last one |

## Effects

**Numbers and flags**

| Effect | What it does |
| --- | --- |
| `escalation: N` | Adds N to the meter with the rival (the rival may then judge its red line crossed) |
| `rally`, `legitimacy`, `pc` | Adds to the player's value |
| `trust`, `align` | Adds to every segment's trust or Align |
| `faction.<id>: N` | Adds to that faction's standing |
| `flag.<name>: V` | Sets a flag |
| `design.<id>: N` | Adds to a design's effectiveness and cap |
| `narrative.<id>: { segment: share }` | Seeds a narrative |
| `headline: "text"` | Adds a Chronicle headline |

**The rival**

| Effect | What it does |
| --- | --- |
| `rival.must_respond: W` | The rival answers as if hit by an action of weight W |
| `rival.lift_export_controls: D` | The rival lifts its export controls after D days |

**Orders**

These carry out an order immediately, exactly as the player could give it. A refused order is logged.

| Effect | Order |
| --- | --- |
| `order.forensic_sweep: province` | Sweep a province's grid systems |
| `order.mobilize: level` | Set mobilization |
| `order.exempt: [pools]` | Exempt labour pools from call-up |
| `order.repair: [{ substation, method: spare \| mobile \| new }]` | Repair substations |
| `order.priority: [{ facility or load, tier: critical \| high \| normal \| low \| default }]` | Reorder the priority list |
| `order.priority_demand: { good, tier }` | Reorder all final demand for a good |
| `order.declare_emergency: true`, `order.end_emergency: true` | Start or end an emergency |
| `order.power: [power or { power, province }]` | Use an emergency power |
| `order.nationalize: corporation` | Nationalize a company |
| `order.takedown: { narrative, pressure, contract }` | Ask the platform to take a narrative down |
| `order.counter: narrative` or `{ narrative, segments: [..], live: true }` | Counter a narrative: belief fades 3× faster for 7 days, and Plausibility × 0.6 for 30 days in the target segments (all if omitted). `live: true` is the President on TV: × 0.5 while Trust is 50+ (D-045) |
| `order.launch_cyber: operation` | Use a cyber operation |
| `order.open_import: { good, from }` | Open an import route |
| `order.doctrine: j` | Set stockpile doctrine |
| `order.firmware: design`, `order.revision: design` | Update a design |
| `order.front_attack: true` | Attack at Veyl |

## Seeds

```yaml
seeds:
  - { id: mara_protest, weight: 20, min_delay_days: 30, monthly_chance: 0.3, defuse: [ "province.kestria_east_ossen.dark == 0" ] }
```

Every seed needs a payoff storylet whose preconditions include `seed.<id>.ripe == 1`.
