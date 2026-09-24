# CASCADE balance report — The Veyl Crossing

1000 campaigns (seeds 1–1000), Day 0 to Day 90, every decision answered at random by the autopilot. Tick timings from 4550 days (50 campaigns run one at a time), Release build.

## The spec's automated campaign checks

| Check | Pass band | Result | Verdict |
| --- | --- | --- | --- |
| Storylets that never fire | under 5% of the library | 0 of 28 (0.0 %) | PASS |
| Any one storylet's share of all fires | under 2% | design_bureau_patch: 9.1 % | FAIL — see note 1 |
| Campaigns where some nation doubles its territory | 5–15% | no conquest in the slice | N/A |
| Campaigns where output falls more than 50% (GDP proxy) | under 10% | 58 (5.8 %) | PASS |
| Campaigns that cross the strategic threshold | under 1% | 0 (0.0 %) | PASS |
| Identical state hash on replay | 100% | 100 of 100 replayed | PASS |
| Daily tick, 95th percentile | under 50 ms | 1.91 ms (median 0.23, 99th 2.61, max 3.48) | PASS |

## The scenario's own targets

| Check | Target | Result | Verdict |
| --- | --- | --- | --- |
| Eastern blackout cuts legacy chip output (spec regression) | 35–45% | 482 of 482 attacked campaigns in band; mean 39.9 %, range 39.7 %–40.3 % | PASS |
| Day 4 attack lands (else the seed was defused, D-014) | most campaigns | 482 (48.2 %) | INFO |
| Regional blackout recovery (90% of the dark back on) | 3–14 days | 143 of 482 recovered; median 14 days; 143 within 3–14 | INFO — see note 3 |
| Time in Crisis Time | 5–10% of days (over a 20-year campaign) | 53.0 % | INFO — see note 4 |
| Major story beat | every 45–90 days (by Director) | 7.0 majors per campaign; median gap 2 days | INFO — see note 5 |
| Critical inputs at start | 20–60 Days of Cover | wafer_blank 40, neon_gas 60, legacy_chip 30, gallium 40, gallium_rf_chip 20, germanium_optic 50, battery_lithium 35, li_ion_cell 20, rare_earth_magnet 45, brushless_motor 20, flight_controller 22, thermal_camera 25, rf_module 25, diesel 30 | PASS |
| Campaigns where the meter reaches rung 5+ | rare in a grey-zone crisis | 285 (28.5 %) | INFO |
| Campaigns where Varan launches an offensive | only after the player crosses its red line | 61 (6.1 %) | INFO |
| Deepfake takes hold somewhere | counter-measures should matter | 1000 (100.0 %); median day 11 | INFO |
|   Deepfake peak believing share at day 90, all campaigns |  | 40.5 % | INFO |
|   Deepfake answered with 'go_live' | lower peak belief than doing nothing | peak believing share 39.7 % at day 90 (366 campaigns) | INFO |
|   Deepfake answered with 'shutdown_east' | lower peak belief than doing nothing | peak believing share 41.1 % at day 90 (302 campaigns) | INFO |
|   Deepfake answered with 'takedown' | lower peak belief than doing nothing | peak believing share 40.7 % at day 90 (332 campaigns) | INFO |

## Outcomes

- Day 10 decision, chosen at random among what was affordable: lights_first 240, fab_first 235, strike_back 221, back_channel 218, emergency 86
- End of campaign, mean: Approval 53.7, Political Capital 75.8, Trust 48.9
- Chronicle survival: mean 56.8, 10th–90th percentile 25–70
- Chronicle prosperity: mean 87.4, 10th–90th percentile 46–100
- Chronicle liberty: mean 75.5, 10th–90th percentile 40–100
- Chronicle sovereignty: mean 57.5, 10th–90th percentile 32–87
- Chronicle humanity: mean 92.6, 10th–90th percentile 83–97
- Storylet fires per campaign: 21.8. Most frequent: design_bureau_patch 9.1 %, border_town_fear 8.5 %, bond_jitters 8.0 %, shipping_guarantee 7.1 %, sunset_petition 6.6 %

## Notes

1. The 2% cap assumes the spec's full library of thousands of storylets. With 28 storylets, an even spread is already 3.6 % each, and the ten arc beats fire in nearly every campaign by design.
2. There is no GDP in the slice. The proxy is legacy chips plus drones (weighted ×1,000) on the last day against Day 0.
3. Three transformers are wrecked and Kestria holds two spares; a new one takes 730+ days. Only the lights_out choice that sends both spares to the homes (homes_first) relights 90% of the people, in 14 days. The other choices leave part of Ossen East on a 30% mobile unit for the rest of the slice, by design.
4. The slice is the crisis itself: 91 days that open with an attack. The spec's 5–10% band is for a 20-year campaign, so it isn't comparable here.
5. The slice's ten arc beats land in the first 10 days by design (D-012). The Historian's 90-day rhythm governs everything after.
