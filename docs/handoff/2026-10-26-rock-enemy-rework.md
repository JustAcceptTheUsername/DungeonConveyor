# Handoff: rock-critter enemy rework (branch `alex/enemy-rework`, 2026-10-26)

Written for Dido and Dido's Claude agent. Read this before touching anything in `Assets/Scripts/Enemies/`,
`Combat.cs` (death path), `Fx.cs`, `EnemyCatalogSync.cs` or `Resources/EnemyCatalog.asset`.
Base: `main` at 99121c0 ("Mazalo"). 10 commits, not pushed yet. Alex will push and open a PR when ready; Dido reviews.

**Status (Alex, 2026-10-26):** "looked fine for now". This is a first full pass. **Models and animations need more
polish.** Alex also says **the sound effects are very good** and only need **timing and general polish** against the
new animations (see §6).

## 1. What changed, in one paragraph
The 5 bug enemies became **rock critters / ore golems** (Alex's art direction). Each is a low-poly glb built from
**separate rigid parts** (no skinning). A small code rig poses the parts from clips baked in Blender: **poses step at
12 fps ("on twos"), travel stays smooth**, and walk cycles are **locked to distance walked**. They **hatch from the
meteor**, show **hit flash, rock chips and magenta cracks**, **crumble into rubble** on death, and the saboteurs and
tank get **attack tells** (chew, lob, slam, dive). **Ids and stats are unchanged**, so waves, saves and balance keep
working. Everything sits behind two toggles; with both off, the game behaves as on `main`.

| Id (unchanged) | New name | Model / clips | Attack tell |
|---|---|---|---|
| `Crawler` | Pebble Skitterer | `PebbleSkitterer.glb`, Walk | none |
| `CrystalSpider` | Ore-eater | `OreEater.glb`, Walk + Chew | chews a belt; the wreck happens on `Bite` |
| `Wasp` | Rock Bat | `RockBat.glb`, Fly (time-based) + Dive | dives at the Core; damage on `Impact` |
| `AcidSpitter` | Magma Spitter | `MagmaSpitter.glb`, Walk + Lob | stops 2 cells short, lobs a lava blob; the wreck happens where the blob lands |
| `Brute` | Ore Golem | `OreGolem.glb`, Walk + Slam | double-fist slam; Core damage on `Impact` |
| `Brute` boss | (boss) | `OreGolem_Boss.glb` (modelled at 2×), Walk + Slam + Enrage | 4 armour plates break off at 72/48/23% health; each break plays Enrage |

The old bug glbs/objs are still in `Assets/Models/Enemies/` for rollback. They are not linked any more.

## 2. Commits
| Commit | What |
|---|---|
| `ee2100f` | `EnemyLook`: ink outline, hit flash, chips, cracks, status tint on the body, contact shadow, 0.6 s crumble, 5 px bars, crowd desync. New shader `Resources/Shaders/EnemyLook`. |
| `27c3320` | Bigger Crawler/Wasp, Wasp hover fix (superseded by the new models' scales). |
| `f40f92b` | `EnemyRig`, `EnemyGait`, `MeteorHatch`: rigid-part animation, stepped poses, meteor hatch. |
| `b685c5b` | Pebble Skitterer replaces the Crawler; Meteor shell model (`Models/Fx/Meteor.glb`). |
| `a946cc0` | `EnemyRig`: clip sets, parented tracks, fliers, armour plates. |
| `999252c` | `Enemy`: attack tells (Chew, Lob, Slam/Dive), boss model swap, flier banking. `Fx.Lob` lava blob. `WaveManager.Wreck` uses `Enemy.WreckCell`. |
| `aedf2b0` | `EnemyCatalogSync`: `Replaces` map, `BossModels` map, clip auto-linking. |
| `ac7ca26` | The 4 rollout models + boss, prefab variants, 11 clip assets, catalog names/flags. |
| `b9b4f74` | Fliers ease into turns (`Rig.FlyTurnResponse`). |
| `cca2aaf` | Tuning: scales, bank, Enrage plays out, bigger lava blob. |

## 3. Files
- **New:** `Assets/Scripts/Enemies/EnemyLook.cs`, `EnemyRig.cs`, `EnemyGait.cs`, `MeteorHatch.cs`;
  `Assets/Resources/Shaders/EnemyLook.shader`; models `Assets/Models/Enemies/{PebbleSkitterer,OreEater,RockBat,MagmaSpitter,OreGolem,OreGolem_Boss}.glb`,
  clip assets `Assets/Models/Enemies/<Model>_<Action>.asset`, `Assets/Models/Fx/Meteor.glb`, prefab variants under `Assets/Prefabs/Models/{Enemies,Fx}/`.
- **Changed:** `Enemy.cs` (most hooks), `EnemyCatalog.cs` (toggles, knobs, `Entry.Gait/Clips/BossModel/BossGait/BossClips`),
  `WaveManager.cs` (`Wreck` takes the cell from the enemy), `Combat.cs` (`Hit(..., quiet)` so DoT ticks don't flash),
  `Fx.cs` (`Lob`), `Editor/EnemyCatalogSync.cs`, `Resources/EnemyCatalog.asset`.
- **Not in the repo** (Alex's machine): the Blender sources and tools in `DC/blender-work/` (`_tools/dc_enemies.py`,
  `_tools/enemies/<Id>_rig.py`, `_tools/dc_enemy_export.py`, `Enemies/<Id>.blend`) and the full review notes in
  `DC/notes/enemy-review/` (REVIEW.md decisions, CODE.md API, INTEGRATION.md, GIFs). Ask Alex for any of them.

## 4. How it works
**Toggles** (`EnemyCatalog.asset`, Inspector):
- `EnemyFeedback`: the `EnemyLook` layer (outline, flash, cracks, shadow, crumble, bars). Off = old look, instant destroy.
- `RigAnimation`: the part rig, clips, hatch and attack tells. Off, or with a clip missing, means the enemy behaves as before.

**Runtime flow:**
- `Enemy.Init` instantiates the model and creates `EnemyLook` + `EnemyRig`. `Enemy.Place` ticks the rig; the rig has no Update of its own.
- Poses update at `Rig.PoseFps` (12) and are held in between. The walk phase follows the distance walked (it slows with chill and stops when the enemy stops). Fliers run their loop on time.
- **Attacks:** `Enemy.Arrive` at a belt or Core cell starts the clip and keeps reporting `Moving`. On the clip event (`Bite`/`Impact`/blob landing), or when the dwell runs out, `Step` returns `ReachedBelt` or `ReachedCore`, and **WaveManager handles it exactly as before** (Wreck, or Core damage + Leaked + Remove).
- **Death mid-attack:** `Remove()` cancels the attack and fizzles a blob in flight. Nothing is wrecked or damaged; bounty, kills and splitting go through Combat as before.
- **Death:** `Enemy.Remove()` still takes the enemy out of `Enemy.All` immediately (targeting, counts and events unchanged). The object then crumbles for about 0.8 s (parts unparented, tumbling, sinking) and is destroyed.
- **Boss:** `MakeBoss` swaps in `Entry.BossModel` before landing and **does not apply the 2× body scale** to it (the model is already built at 2×). Speed ×0.6, health, damage and bounty work as before.

**Clip data** (`EnemyGait`): per key, deltas from each part's rest pose in Unity space, local to the part's parent. Fields:
`Loop`, `Events[{Time, Name}]`, `StrideLength`, `TimeBased`/`CyclesPerSecond`, `AbsoluteRotation`, per-clip amplitude
overrides, `Raw` (play exactly as keyed; used by the Golem, boss and Bat). Event names in use: `Bite`, `Launch`, `Impact`,
`Roar`, plus `End` when a one-shot finishes. Callback: `rig.OnEvent`.

**Part naming contract** (case-sensitive; the code finds parts by name anywhere in the tree):
`Body`, `Torso` (get bounce/squash), limbs `Leg_L1..n`/`Leg_R1..n`, `Foot_*`, `Wing_*`, `Arm_*`, `Forearm_*`, `Fist_*` (get swing),
others `Head`, `Jaw`, `Eye`, `Vent`, `Core` (glows as it swells), `Crystal_n`, `Muzzle` (empty: lava blob origin), `Plate_n` (boss armour).
L = the creature's left = Unity −X. Forward = +Z (`YawOffset` stays 0). Glow materials contain `Glow` in the name.

**Adding or re-exporting an enemy:**
1. Alex exports the glb + `<Id>_<Action>.json` from Blender.
2. Copy the glb to `Assets/Models/Enemies/`, import with ForceSynchronousImport, then run `ModelPrefabs.CreateAll()`.
3. Turn each json into a clip: `ScriptableObject.CreateInstance<EnemyGait>()`, then `JsonUtility.FromJsonOverwrite`, then `AssetDatabase.CreateAsset` as `Assets/Models/Enemies/<Model>_<Action>.asset`.
4. Run `EnemyCatalogSync.Sync()`. It links the model to the old Id through `Replaces`, sets the boss model, links Walk/Fly into `Gait` and adds the other clips. It never overwrites a link that is already set.
5. Set Scale/Hover in the catalog, play-test, and commit the glb + .meta + prefab + clip assets + catalog.

Re-exporting a glb changes its node fileIDs, so check the prefab variants after each re-export.

## 5. Knobs and gameplay changes (`EnemyCatalog` → `Rig`, `Feedback`)
**These change gameplay timing.** Each is a knob, and 0 restores the old instant behaviour. Please review them:

| Knob | Value | Effect |
|---|---|---|
| `HatchHold` | 0.35 s | Each enemy waits in the meteor shell before it walks. |
| `ChewDwell` | 0.5 s | The Ore-eater stands at a belt; the wreck happens on `Bite` (+0.25 s). |
| `LobDwell` / `LobRange` | 0.6 s / 2 cells | The Spitter stops short and lobs; the blob flies `LobFlight` 0.45 s. |
| `SlamDwell` | 0.6 s | The wind-up before Core damage (Golem; the Bat dives). |
| `EnrageHold` | 1.0 s | The boss stands for each plate break. |
| `PlateBreakAt` | 0.75/0.5/0.25 | Boss plate thresholds. With 4 plates over 3 thresholds the middle one drops 2. Add a 4th value for one plate per beat. |

Look/feel knobs: `PoseFps` 12, `BodyBounce` 1.8, `BodySquash` 2.2, `LegSwing` 1.25 (not used by Raw clips), `FlapRate`,
`BankMax`/`BankPerTurn` 0.12/`BankResponse`/`FlyTurnResponse` 6, `CrumbleSeconds` 0.8, `Feedback.*` (ink width 0.018, flash 65 ms,
cracks at 66/33%, shadow, jitter, bar height). Catalog scales: Skitterer 1.2, Ore-eater 1.0, Bat 1.15 (Hover 0.1),
Spitter 1.1, Golem 1.2 (the old Brute was 0.8).

**Perf:** 150 mixed enemies cost 1.31 ms/frame with the rig on (0.86 off) and allocate 0 B per frame. Measured in the editor on a shared machine, so treat it as a ballpark.

## 6. Sound: timing and polish (Alex: the SFX are very good, only timing)
The existing calls were left where they were. The animations now add new moments, so some sounds fire before or
after what you see. Suggested hook points; every event comes through `rig.OnEvent` or a clear spot in `Enemy.cs`:

| Moment | Now | Suggestion |
|---|---|---|
| Meteor lands | `MeteorImpact` + `LandFor(def)` at impact (`Enemy.cs` ~575) | Keep the impact. The critter only pops out after `CrackAt` 0.15 s + `PopSeconds`, so a shell-crack / pop sound there would sync better. `LandFor` may fit the pop moment. |
| Death | `DeathFor(def)` / `BossDeath` at the kill (`Combat.cs` ~526) | Good as the hit. The crumble then runs 0.8 s with no tail; a short rubble/debris settle at the end would close it. |
| Ore-eater chew | `BeltWrecked` + `BuildingDestroyed` at the wreck (now on `Bite`) | Add a grind/crunch on each `Bite` (2 per Chew loop). The wreck sound now lands on the bite, which is correct. |
| Magma lob | nothing at launch; `BeltWrecked` when the blob lands | Wind-up hiss while the core swells, a spit on `Launch`, a sizzle on landing. |
| Golem slam / Bat dive | `CoreHit` on `Impact` | Correct timing. A wind-up whoosh before `Impact` would sell the tell. |
| Boss plate break | nothing | A rock crack per plate, then the roar on the Enrage `Roar` event (about 0.38 s into the 1 s stand). |
| Footsteps / wing flaps | none | Loop events don't fire yet (only one-shots fire events). The Spitter's Walk already has `Land` events at 0 and 0.5. Firing loop events at pose steps would give footstep/flap hooks; keep them quiet and throttled with 150 enemies. |

The per-enemy sound sets are still keyed by the old Ids (`EnemySounds.EnemyId`), so each rock critter plays its bug
predecessor's sounds. Alex is happy with how they sound; this is only a note in case some of them should feel more "stone".

## 7. Known issues / polish backlog
- **Models and animation need more polish** (Alex). Blender sources are on Alex's machine. Blockouts are 324 to 2,280 tris.
- **Bloom lifts small dark objects.** The stone is exported with compensated colours (`GAME_STONE` in Alex's exporter), so it reads navy/violet and the #FF2E63 glow reads pink. A proper fix would be in the post stack (bloom over the pale ground), which is yours to decide; it affects buildings too.
- **Feet slide** (about 1/3 on the Skitterer, about 2× on the Ore-eater). True no-slide strides alias at 12 fps poses.
- Slammers and divers still vanish on `Impact` at the Core (as all enemies did). Holding the pose for 2–3 frames would read better, but that changes flow.
- One-shots snap in and out with no blend (in keeping with poses on twos).
- The lava blob is a soft sprite, not a lit rock blob, and its splash is small at default zoom.
- `EnemyLook` only covers `MeshFilter` models (no `SkinnedMeshRenderer`).
- **Recompiling during play mode** leaves enemies without their layer (NREs in `Enemy.Update`); dev-only, so stop play first. FMOD also crashed the editor once on a recompile after an audio device was unplugged.
- Boss plates still in flight vanish when the boss is destroyed.

## 8. How to check it
Play `GameScene` (from `MainMenu`). Every wave type now arrives as rock critters. To see them all quickly in an eval or debug
script: `Enemy.Spawn(EnemyCatalog.Instance.Entries[i], 1f, map, site, root, material)` near the Core, then
`MakeBoss(...)` on a Brute for the boss. To compare with `main`, switch `RigAnimation` and `EnemyFeedback` off on
`EnemyCatalog.asset` (re-link the old models if you want the bugs back).

Local-only files that are never committed: `Packages/manifest.json`, `Packages/packages-lock.json` (the `com.unity.pipeline` CLI package),
`ProjectSettings/ProjectSettings.asset`, FMOD cache/plist churn and `fmod_editor.log`.

## 9. Element palettes (Dido's swappable colours, added in the polish pass)
Every enemy material is named after its colour **role** (`DCX_Stone`, `DCX_StoneDark`, `DCX_Accent`, `DCX_Accent2`,
`DCX_AccentDark`, `DCX_Metal`, `DCX_Plate`, `DCX_GlowCore`, `DCX_GlowHostile`), and the stone bands are grey vertex
colours, so any role can be recoloured.
- **Per enemy:** `Assets/Models/Enemies/<Model>_Palette.asset` (`EnemyPalette`): role → authored sRGB colour, linked
  as `EnemyCatalog.Entry.Palette` (`BossPalette` for the boss model). Edit `Color` to recolour that enemy.
- **Per element:** `EnemyCatalog` → `Palette.Elements` (shared): per element, the roles it recolours. The starting
  colours (Accent / Accent2 / AccentDark / GlowCore: Fire orange-red, Frost icy blue, Lightning yellow-white, Poison
  green) are **placeholders** to tune. An `EnemyPalette.Elements` entry overrides the shared one for that enemy.
- **API:** `enemy.SetElement(EnemyElement.Frost)` or `enemy.SetElement(Element.Frost)`; `None` = the authored look
  (then the enemy carries no property blocks at all). Debug: `Enemy.SetElementAll(e, andNewOnes)`.
- **Bloom compensation:** `EnemyPalette.ExportColor` is the exporter's `export_color`, ported (it reproduces every
  exported material exactly). `Palette.Compensate` (on) applies it to new colours; turn it off once the bloom
  threshold goes up (~1.1) and the glbs are exported with `--raw-colors`. Colours outside the calibrated tables use
  a fitted model; glow colours outside `Glows` are unmeasured (measure in game, add them).
- **Rendering:** `EnemyPaint` owns all property blocks on a model and merges the palette with the rig's glows (Core
  swell, plate seams, crumble cooling). `EnemyLook` (ink, flash, cracks, status) draws on its own meshes and is
  unaffected. Note: Unity takes renderers with a property block out of the SRP Batcher, so only recoloured enemies
  pay that cost (CPU 150 mixed, 4/5 recoloured: ~1.6 ms/frame, 0 B/frame, as before).
- **New enemy:** export it (the sidecar `<Id>_palette.json` lands next to the glb), copy the glb in, then
  `Dungeon/Import Enemy Palettes...` on the sidecar folder (creates/updates `<Id>_Palette.asset`, keeps edited
  colours, then runs the catalog sync).
