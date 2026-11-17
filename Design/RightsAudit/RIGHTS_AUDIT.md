# Rights / asset audit — competition build (2026-11-17, read-only)

Evidence: Unity Build Report in `%LOCALAPPDATA%\Unity\Editor\Editor-prev.log` for
`Builds\Meta Competition 2026\CelestialWorkshop_Competition2026_Playtest_Nov16_4.apk` (16:55 Nov 16)
→ `buildreport_assets.txt` (6,258 entries). APK zip listing confirms `assets/Licenses/Fonts/*` (4 OFL texts).
Template = Meta Hands-First Accelerator (github.com/Meta-Horizon-Start-Program/Hand-Tracking-Template, initial commit 8230298) — **no LICENSE file**.
Inactive template roots (INTERACTABLES, ENVIRONMENT/ParkEnvironment, Back/Front Trees, WORKSHOP/Gazebo-URP) are never enabled by script → **shipped, not seen**.

## Must resolve before submission
1. **`Assets/Textures/California Dreaming Song Image.jpeg`** — commercial album cover (The Mamas & the Papas), no permission, **in the APK** via inactive INTERACTABLES/Table 5/Quest Display UI/Audio Player/Song Image. Clear that sprite or delete the Audio Player / INTERACTABLES, rebuild, confirm absent from Build Report.
2. **Credits.txt lacks the Meta notice** (Meta Platform Technologies SDK License Agreement §1.3 — verify exact wording at https://developers.meta.com/horizon/licenses/oculussdk/). Still required after the icon swap: ISDK sample audio (heard; `Assets/Audio/CW_Audio/ISDK/*.wav` are byte-identical copies), ISDK button prefabs/materials, OCUI sheet still pulled in by INTERACTABLES (145 Images) and `Assets/Prefabs/Quest Meta Display UI and Input .prefab`, ShowcaseSamples.
3. **Icon-restoring builders** (menu-only, no auto-run): `PlacementControlsHookup.cs` (Recenter/Raise/Lower), `CreditsBoardBuilder.cs` (Credits/Back), `SelectionTableBuilder.cs` (Field Note/Back). Repoint their `IconSheet`/name tables at the new sprites; don't run them until then. Start / Back to title / All boxes / Post-Puzzle icons are set by no script.

## Can wait
- Delete template tables, ParkEnvironment, Gazebo (Blue Olive Studio), Pine Pack trees (TreeMaker), StarterAssets ThirdPersonController, `Assets/Ring/**` (unknown licence), `Assets/Realistic Hand Material/**` (unknown), ChatGPT/`ai.png` images, camera placeholders → ~100 MB smaller.
- Confirm sources: `constellation_dropping_on_table.wav` (unknown, active, heard), Pixabay SFX (`dragon-studio-holy-spell-cast-450460.wav`, `universfield-magic-teleport-whoosh-352764_SHORTER.wav`), `interaction_hammer_hit_anvil.wav` (template, unused cue).
- EmojiOne (TMP default sprite asset, packed via Resources): credit or remove.
- `StreamingAssets/Licenses/ThirdPartyNotices.txt` concatenating: core NOTICE.txt, mrutilitykit NOTICE.txt, audio/haptics THIRD_PARTY_NOTICES.txt, interaction InterfaceSupport LICENSE (MIT), voice Wit.ai LICENSE + third-party, openxr `Third Party Notices.md` (Khronos Apache-2.0).
- Delete untracked `Assets/Resources/PerformanceTestRun*.json` (packed via Resources).

## Fine as is
Fonts (OFL texts already shipped), Kevin MacLeod CC BY 4.0 (Credits already correct), Starfield skybox (Asset Store EULA; confirm in My Assets), Proper Motion logo (own), Unity splash off (allowed in Unity 6).

## Proposed Credits.txt addition (only the Meta block is required)
```
## Built with
Meta XR SDKs (Core, Interaction, MR Utility Kit, Platform, Audio, Haptics, Voice)
and sample materials from the Meta XR Interaction SDK and Interaction SDK Samples.
Copyright © Meta Platform Technologies, LLC and its affiliates. All rights reserved.

## Fonts
Architects Daughter © 2010 Kimberly Geswein
Caveat © 2014 The Caveat Project Authors
Meddon © 2010, 2011 Vernon Adams
Liberation Sans © 2012 Red Hat, Inc.; digitized data © 2010 Google Corporation
Licensed under the SIL Open Font License 1.1 (full texts included with the app)

## Art and sound
Starfield skybox: "Vibrant 4K Starfield Skybox Pack" by Parallel Cascades
Sound effects: Pixabay (Dragon Studio, Universfield)
Emoji artwork provided by EmojiOne
```
