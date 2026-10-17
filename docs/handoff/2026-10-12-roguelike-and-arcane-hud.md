# Handoff: roguelike loop + Arcane HUD (branch `alex/roguelike`, 2026-10-12)

Written for Dido and Dido's Claude agent. Read this before touching `Run.cs`, `WaveManager.cs`, `Combat.cs`,
`Enemy.cs` or anything in `Assets/Scripts/UI/`. Base: `main` at 79f3d72 ("Update SampleScene.unity"), which already
contains Alex's art-rework commits and Dido's `341b2ca` roguelike skeleton. Dido ran out of credits mid-roguelike,
so Alex's session took over **on top of Dido's `Run.cs`** (his stages, card idea and boss constant are kept and extended).

## 1. Roguelike loop (commit "Roguelike: ...")
| Feature | Where | Notes |
|---|---|---|
| Wave cap per map | `Run.StageConfig.Waves` (10 each), `Run.FirstWave/LastWave` (cumulative across stages) | After the map's last wave is cleared: "MAP CLEARED" screen → `Run.Advance(gold)` → scene reload with the next stage. |
| Bigger maps | `Run.Stages` sizes ×1.5: 30, 45, 60, 75, 90, 105 (repeats); `MaxMapSize = 105` | **Perf:** 105×105 ran ~16 fps in the editor. Needs profiling or a lower cap. |
| Boss every 10th wave | `Run.IsBossWave`, `WaveManager.StartWave` | Only the boss spawns: the toughest unlocked kind via `Enemy.MakeBoss` (scale ×2, speed ×0.6, health = `BossHealthShare` 0.7 × the normal wave's total HP, bounty = the wave's total, Core damage = max(own, half the wave's)). No dedicated boss model yet. |
| 3 cards after **every** cleared wave | `WaveManager.CardsOwed/Offer/Pick/PickingFor`, `Run.Draw/Roll/Apply` | A wave counts as cleared when nothing is pending or alive. Overlapping waves each owe a pick. The game pauses while picking. `WaveManager.AutoPickCards` (used by PlaytestBot) picks automatically. |
| Card pool | `Run.Roll` | Mix: elements for all towers (+3) or one tower type (+6, `Rare = true`, biased to built tower types), stats (+% damage, +% attack speed, +range; global or per type), +20% crystal gold. `Run.Mods` holds per-tower and `Run.Global` bonuses; `Run.For(id)` merges them. |
| Elements | `Combat.ElementalHit`, `Enemy.Ignite/Chill/Poison/TickEffects` | Each element adds its points as damage per hit, plus: Fire = burn, Lightning = arc to the nearest enemy (`Fx.Bolt`), Frost = slow, Poison = stacks. Continuous attacks (laser, flame) apply them per second. Tuning lives on the **Combat** component in the Inspector (Burn/Chill/Poison/Arc fields). Enemies tint by status. |
| Upgrades applied | `Combat.Run` (damage, rate, `RangeOf`), `ItemFlow` (`Run.IncomeMultiplier`) | Tooltips show upgraded stats (`HUD.Stats`). |
| Game speed | `GameSpeed` (in `Run.cs`): .5x / 1x / 2x / 3x | **The HUD owns `Time.timeScale`** (`GameSpeed.Apply(paused)` every frame). Don't set timeScale elsewhere; PlaytestBot sets `GameSpeed.Speed`. |
| Gold carries over | `BuildController.Start`: `StartingGold + Run.CarriedGold` | Game over → "NEW RUN" (`Run.Reset()`). |

Events added for the HUD (same commit):
- `BuildController.GoldChanged(int delta, Vector3? worldFrom)`; `Earn(int, Vector3? from = null)`. Every Gold change goes through it (spend, refund, belts as one event).
- `CoreBuilding.Damaged(float amount, bool fromBoss)`; `TakeDamage(float, bool fromBoss = false)`.
- `WaveManager.WaveStats Current/LastCleared` (kills, bounty gold, leaked), `Boss`, `BossHealthAtLoss` (-1 if no boss).
- `Run.Card.Icon` (`CardIcon`) and `Run.Card.Rare`.

## 2. Arcane HUD (commit "Arcane HUD: ...")
Approved mockup and spec are in Alex's notes (not in the repo). Summary:
- Still **IMGUI** (`HUD.cs` rewritten, `UIStyle.cs` extended). `UIStyle.Nine()` draws 9-sliced frames scaled by
  `Screen.height / 1080`. If the kit fails to load (`ArcaneSkin.Loaded == false`), the old procedural look is the fallback.
- Art kit: `Assets/Resources/UI/Arcane/*.png` (28 pieces, generated with Nano Banana Pro and cut out), loaded by
  `ArcaneSkin.cs`. Fonts in `Assets/Resources/UI/Fonts/`: Cinzel (titles) and Alegreya Sans (body), both SIL OFL (licences included).
- `HudFx.cs`: tweens on **unscaled** time (they run while paused): timers, count-up counters, fly-ins, shake.
- Reward feedback (Alex's "Gunhop" rules: rewards come to you, reactions sized to the event, honest near-misses,
  summaries never auto-close, don't overdo it):
  - gold "+N" flies from the kill or Core to the counter, which counts up and bumps (bigger for bigger payouts, gold flash for boss bounty); spending ticks down red
  - build slot glints when gold first covers its cost; unaffordable slots dim with red cost
  - Core hit: red flash, ghost segment of the loss, shake (bigger from a boss)
  - boss ≤ 10 s: panel pulse + "BOSS APPROACHING"; boss bar while it lives
  - wave tally (kills, gold, leaked, FLAWLESS) → cards drop in and flip; hover lift; keys 1/2/3; rare cards get a blue gem; the picked card flies into an upgrade row under the wave panel
  - map cleared tally; game-over recap with the boss's real HP left; NEW RUN or **R**
  - **FX** toggle (end of the speed row) = reduced motion, stored in PlayerPrefs `dc.reducedMotion`
- `HUD.IsPointerOverUI` blockers cover every new panel; keep that when adding UI.

## 3. Not in these commits / open
- Multi-Core stages (`Cores = 2/3` in `Run.Stages`): IsoGrid makes the bases but only one Core is placed (Dido's WIP, untouched).
- The body font draws old-style figures (small zeros); card text is small at 720p. Easy follow-ups.
- No audio exists yet; the HUD has natural hook points (fly-in landing, card pick, stamps).
- The 3 economy models (MineShaft, Collector, Conveyor icon) are built in Blender but not exported yet; separate art work.
- Local-only files never committed: `Packages/manifest.json`, `packages-lock.json`, `ProjectSettings/ProjectSettings.asset`.

## 4. How to check it
Play `SampleScene`: send a wave early, clear it → tally + cards; pick with 1/2/3. To jump to a boss in an eval/debug
script, set WaveManager's `<Wave>k__BackingField`, `clearedUpTo` and `picked` to 9, then `CallEarly()`.
