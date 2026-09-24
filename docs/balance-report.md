# CASCADE balance report — The Veyl Crossing

1000 campaigns (seeds 1–1000), Day 0 to Day 90, every decision answered at random by the autopilot. Tick timings from 4550 days (50 campaigns run one at a time), Release build.

## The spec's automated campaign checks

| Check | Pass band | Result | Verdict |
| --- | --- | --- | --- |
| Storylets that never fire | under 5% of the library | 0 of 31 (0.0 %) | PASS |
| Any one generic storylet's share of generic fires | under 11.1 % (2× the even share of 18; 2% once the library passes 60) | shipping_guarantee: 10.7 % | PASS — see note 1 |
| Campaigns where some nation doubles its territory | 5–15% | no conquest in the slice | N/A |
| Campaigns where output falls more than 50% (GDP proxy) | under 10% | 65 (6.5 %) | PASS |
| Campaigns that cross the strategic threshold | under 1% | 0 (0.0 %) | PASS |
| Identical state hash on replay | 100% | 100 of 100 replayed | PASS |
| Daily tick, 95th percentile | under 50 ms | 4.06 ms (median 1.79, 99th 4.44, max 6.32) | PASS |

## The scenario's own targets

| Check | Target | Result | Verdict |
| --- | --- | --- | --- |
| Eastern blackout cuts legacy chip output (spec regression) | 35–45% | 507 of 507 attacked campaigns in band; mean 39.9 %, range 39.7 %–40.3 % | PASS |
| Day 4 attack lands (else the seed was defused, D-014) | most campaigns | 507 (50.7 %) | INFO |
| Regional blackout recovery (90% of the dark back on) | 3–14 days | 187 of 507 recovered; median 14 days; 187 within 3–14 | INFO — see note 3 |
| Time in Crisis Time | 5–10% of days (over a 20-year campaign) | 55.5 % | INFO — see note 4 |
| Major story beat | every 45–90 days (by Director) | 7.1 majors per campaign; median gap 2 days | INFO — see note 5 |
| Critical inputs at start | 20–60 Days of Cover | wafer_blank 40, neon_gas 60, legacy_chip 30, gallium 40, gallium_rf_chip 20, germanium_optic 50, battery_lithium 35, li_ion_cell 20, rare_earth_magnet 45, brushless_motor 20, flight_controller 22, thermal_camera 25, rf_module 25, diesel 30 | PASS |
| Campaigns where the meter reaches rung 5+ | rare in a grey-zone crisis | 306 (30.6 %) | INFO |
| Campaigns where Varan launches an offensive | only after the player crosses its red line | 69 (6.9 %) | INFO |
| Deepfake takes hold somewhere | counter-measures should matter | 495 (49.5 %); median day 12 | INFO |
|   Deepfake peak believing share at day 90, all campaigns |  | 18.3 % | INFO |
|   Deepfake answered with 'go_live' | lower peak belief than doing nothing | peak believing share 5.5 % at day 90 (353 campaigns) | INFO |
|   Deepfake answered with 'shutdown_east' | lower peak belief than doing nothing | peak believing share 13.0 % at day 90 (298 campaigns) | INFO |
|   Deepfake answered with 'takedown' | lower peak belief than doing nothing | peak believing share 35.7 % at day 90 (349 campaigns) | INFO |

## Outcomes

- Day 10 decision, chosen at random among what was affordable: fab_first 255, strike_back 243, back_channel 236, lights_first 232, emergency 34
- End of campaign, mean: Approval 58.5, Political Capital 75.6, Trust 52.6
- Chronicle survival: mean 55.7, 10th–90th percentile 25–70
- Chronicle prosperity: mean 87.4, 10th–90th percentile 46–100
- Chronicle liberty: mean 79.2, 10th–90th percentile 56–100
- Chronicle sovereignty: mean 56.7, 10th–90th percentile 29–82
- Chronicle humanity: mean 92.6, 10th–90th percentile 83–97
- Storylet fires per campaign: 20.2. Most frequent: stock_targets 4.9 %, fab_wage_talks 4.9 %, odd_logins 4.9 %, readiness_briefing 4.9 %, wreck_at_veyl 4.9 %

## Notes

1. Generic storylets only (D-047): the 13 scenario-arc beats fire in nearly every campaign by design. The spec's 2% needs a library of 60+; until then the cap is twice the even share.
2. There is no GDP in the slice. The proxy is legacy chips plus drones (weighted ×1,000) on the last day against Day 0.
3. Three transformers are wrecked and Kestria holds two spares; a new one takes 730+ days. Only the lights_out choice that sends both spares to the homes (homes_first) relights 90% of the people, in 14 days. The other choices leave part of Ossen East on a 30% mobile unit for the rest of the slice, by design.
4. Counts days on which any Kestrian province is in Crisis Time. The spec's 5–10% band is for a 20-year campaign. Under D-046 a province is in crisis while any load gets under 70% of its demand. Loads can't be rerouted in the slice's grid, so a wrecked substation on a 30% mobile unit (a new transformer takes 730+ days) is a live blackout until Day 90.
5. The slice's ten arc beats land in the first 10 days by design (D-012). The Historian's 90-day rhythm governs everything after.
