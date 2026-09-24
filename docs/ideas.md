# Ideas (out of scope for the Veyl slice)

A parking lot for things we deliberately aren't building yet. Each line says where the idea comes from.

## Out of scope by decision

These came from the original slice brief:

- **Conquest and territory:** province stages, Grievance and Consent, insurgency, captured industry, client states.
- **Shadow economy:** smuggling routes, Heat, counterfeits, poisoned supply, Small Arms Density, the home-front black market.
- **Orbital:** constellations, the Debris meter, anti-satellite weapons.
- **Elections:** the voting model and seat allocation.
- **The full opponent AI planner:** goal scoring, target ranking and 90-day war forecasting. Varan uses a script plus reactive retaliation for now.
- **Multiplayer:** lockstep and the desync check every 30 ticks. The state hash is already built with this in mind.
- **Art, audio and UI polish.**

## Parked during planning

- **Varan's schedule, ±1 day variation:** seeded jitter on the scripted days (3, 4 and 6), once tests no longer depend on fixed days. See D-013.
- **Markets:** bond yields, currency, downgrade storylets at 6% and 9%, and inflation. See D-009.
- **Transport:** min-cost flow per good class, and weekly route recomputation. See D-007.
- **Forecast:** the weekly 90-day forecast copy, and the 30 ms daily re-run after shocks for the Cascade lens. See D-008.
- **Threading:** running phases in parallel by region. See D-010.
- **Sovereignty Score:** the share of each product line's value sourced at home, walked through the recipe tree (spec Sovereignty Score). It's cheap to add once goods have origins; not needed for the slice's decisions.
- **Holding cost of stockpiles:** 1.5% of stock value per month (spec doctrine). There is no budget in the slice yet.
- **Multi-hop tie-lines:** transit through a neighbour's grid (D-028).
- **Per-facility input stocks:** the spec's sample record keeps stock at the facility; the slice keeps it per province (D-026).
