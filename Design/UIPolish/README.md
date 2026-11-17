# UI polish pass — preserved inputs (2026-11-17)

Nothing in this folder is integrated. It lives outside `Assets/`, so Unity does not import it.

## Icons/ — original workshop icon set (to replace ALL Meta OCUI sample icons)
- `svg/` hand-authored originals (48 grid, single colour, ours, no attribution needed).
- `png_white/` Unity-ready white glyphs, 256 px and `_128`, for tinting by the WorkshopButton animator.
- `png_brown_preview/` ink-coloured previews. `preview.html` = the review sheet.
- `tools/` generator scripts (`icons.py`, `raster.py`, `preview.py`) + inventory scripts; `images_all.txt` = Image dump.

| Icon | Button(s) |
|---|---|
| cw_icon_start | Start (currently speech-bubble placeholder) |
| cw_icon_recenter | Recenter |
| cw_icon_raise / cw_icon_lower | Raise / Lower |
| cw_icon_credits | Credits |
| cw_icon_back | Back (Credits board, Field Note reader) |
| cw_icon_back_to_title | Back to title (box row, Puzzle Back Button) |
| cw_icon_field_note | Field Note (both rows) |
| cw_icon_all_boxes | All boxes |
| cw_icon_try_another | Choose Another Constellation / Try Another Letter |
| cw_icon_another_box | Choose Another Box / Enter the Workshop |
| cw_icon_view_constellations | View Constellations (hidden) |

Import: Sprite (2D and UI), Single, Full Rect, sRGB, Alpha Is Transparency, Clamp,
**mipmaps ON + Trilinear** (the OCUI sheet has mips off → shimmer), max 128–256, ASTC 4x4.
Assign to `<Btn>/Content/Background/Elements/Icon` › Image › Source Image; leave colour (animator-driven).
Post-Puzzle buttons are ISDK prefab instances → instance override only, never edit the package prefab.

**Pitfall:** these editor builders re-assign OCUI icons by name and would undo the swap if re-run:
`Assets/Scripts/CW_Scripts/Editor/PlacementControlsHookup.cs`, `Assets/CW_Credits/Editor/CreditsBoardBuilder.cs`,
`Assets/CW_FieldNote/Editor/SelectionTableBuilder.cs` (full list being confirmed in the rights audit).

Headset checks: legibility at ~9.6 mm / 0.5 m (fletches, three crates, stars), mip shimmer, pressed-state contrast,
Start reading as astrolabe not stopwatch.

## Audits/typography/ — read-only TMP audit
`flat2.py` resolves nested prefab instances/overrides; `rows2.txt` = full TMP dump of the build scene.
Thresholds (Meta): cap ≥ 0.64° legible, ~0.82° comfortable.

| Pri | Element | Now | Proposed |
|---|---|---|---|
| P1 | Title row buttons + Credits Back | cap 0.37° (autosize 9–16 lands ~11.5, rect h 16) | autosize off, fs 14, rect h 20, Menu Canvas 0.0005→~0.00075 |
| P1 | Box-row + Field Note Reader Back | cap 0.34–0.47° | same label fix, canvas → 0.0007 |
| P1 | Puzzle Back Button | cap 0.32° (worst) | canvas ~0.0007 + label fix |
| P2 | Post-puzzle menu | static parse says autosize min 9; **no overflow seen in playtest video** (ISDK layout widens label at runtime) | size check in headset |
| P2 | Star chart labels | cap 0.53° | `labelCapHeight` 0.0045→0.006 |
| P2 | Certificate Meddon title / Caveat lines | Meddon x 0.35° | fs +25% |
| P2 | Certificate signed name | gold on parchment ~1.1–1.6:1 | darker ink underlay, keep gold face |
| P2 | Noticeboard bodies | at minimum | fs ~+13% |
| P2 | Box "Coming soon" | x 0.28° | fs 0.2→0.28, autosize min 0.2 |
| P3 | Field Note captions/subtitle, credits text, RecenterPrompt (no backing), chart distance label | slightly small / washes out | small bumps; HintLabel-style backing |

## Audits/star-highlight/
`audit.py` + `star_highlight_audit.csv`: every playable dot → star mapping. Only E3 and O1 broken
(co-located twin stars E5/O7). Fix on branch `fix/star-highlight-twins`.
