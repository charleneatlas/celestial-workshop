# CC0 constellation pipeline: investigation and design

Status: investigation and prototype only, on local branch `cc0-constellations-investigation`
(based on `MetaVRGlasses_DevCompetition2026` @ b2ff8aa). Nothing under `Assets/`, scenes,
prefabs, ProjectSettings or Packages has been changed. No game integration without approval.

## 1. How Cassiopeia actually works (verified in code and scene)

Data: `ConstellationGenerator` (Assets/Scripts/CW_Scripts/ConstellationGenerator.cs) on the scene
object **"Constellation Generator"** (CelestialWorkshop_VRGlasses_Prototype.unity, fileID 65651445):

| starName | affinityPosition (px, y down) | distanceLightYears |
|---|---|---|
| Segin | (299.9, 161.9) | 412 |
| Ruchbah | (596.4, 541.0) | 99 |
| Gamma Cassiopeia | (949.3, 492.5) | 549 |
| Schedar | (1198.5, 887.8) | 228 |
| Caph | (1562.1, 559.9) | 55 |

`artboardSize` 1920 x 1080, `maximumRadius` 1, `angularSpread` 0.7, `useLogarithmicDistance` off.
The three Starter Pack sculptures (`Sculpture 1/2/3 (Cassiopeia)`, TitleLetterMini custom mode) carry
copies of the same list and use this generator as `customShapeSource`. 15 older `CassiopeiaRoot`
objects still carry Segin's pre-fix y = 59.7 (fixed to 161.9 in 19cb587 "to match the Affinity
source"); they are leftovers and not read by the puzzle. `Assets/CW_Art/Cassiopeia_Blueprint.png`
(2048 x 1152) is the exported tracing; no .af/.svg source is in the repo.

Geometry (`GenerateConstellation`):

1. Artboard px -> normalized: `n = (px - W/2, H/2 - py) / (min(W,H)/2)` (so +-1 = 540 px).
2. Ray: `dir = normalize(n.x * spread * angularScale, n.y * spread * angularScale, 1)`, then rotated by
   `Euler(-elevation, azimuth, 0)`. This is the inverse of a **gnomonic (pinhole camera) projection**:
   the artboard is an image plane at z = 1 with `tan(angle) = n * 0.7 * angularScale`.
3. Radius: `r = d / d_max * maximumRadius` (linear; per-constellation normalization by the farthest star).
4. Sky copy (observer given): `world = observer + dir * lerp(shell, r * distanceScale, depthScale)`,
   `shell = maximumRadius * distanceScale`.
5. Rods: one cylinder per consecutive pair `i -> i+1` (polyline only; no branching).

Observer origin: `ConstellationSetup.skyObserverReference`; scene values `skyDistanceScale` 800,
`skyDepthScale` 0.5, `skyElevationDegrees` 15, `skyAzimuthDegrees` 0, `skyAngularScale` 0.3,
`miniMatchesSky` on, `miniLocalReach` 0.5. `EarthViewCamera` renders from the observer with
FOV `2 atan(halfExtentPx / (min(W,H)/2) * 0.7 * 0.3)` (EarthViewCamera.cs ~l.236-246).

Cross-check against Wikidata (build_geometry.py): the hand tracing matches a north-up, east-left
gnomonic projection of the real stars to **<= 3 px (<= 0.03 deg on the sky)** after a 2.6 deg rotation
and scale. The traced distances equal the van Leeuwen 2007 Hipparcos parallax distances
(411.8, 99.4, 549.1, 228.2, 54.7 ly). In-game the sky copy is **2.18x larger in angle** than the real
sky (Segin-Caph 28.8 deg vs 13.3 deg): a uniform zoom, so the shape is still exact.

## 2. Depth/spatial compression and the projection

Stages, in order (all from the observer or uniform):

| Stage | Where | Formula | Keeps Earth-view projection? |
|---|---|---|---|
| Normalize | ConstellationGenerator.CalculateRadialDistance | `r = d/d_max` (log option: lerp(0.2,1, inverse-lerp of log10 d)) | yes (radial) |
| **Depth compression** | ConstellationGenerator (observer branch) | `R = 800 * (1 - k + k * d/d_max)`, **k = skyDepthScale = 0.5** -> radii 0.5..1.0 of the shell (Cassiopeia 440-800 m, real ratio 10:1 becomes 1.8:1) | yes: affine in d, applied along each star's own ray from the observer |
| Globe fit | NearFieldSkyProxy.Place (SkyAnchor.Observer) | whole sky group (observer, pivot, refs) scaled uniformly: `scale = (0.22*0.95 + 0.157)/(|obs->pivot| + reach)` | yes: similarity transform incl. the observer |
| Mini copy | ConstellationSetup.GenerateMini (miniMatchesSky) | same rays/depths with the mini root as observer, uniform rescale to reach 0.5 | yes (similar copy) |
| Intro / reveal flatten | SightlineCompression.FlattenAlongSightline | slide along the ray onto a plane | yes (temporary) |
| Starter Pack sculpture | TitleLetterMini.BuildVisual | no observer, angularScale 1, depthScale ignored (full linear depth), sized by widest side | its own origin only (different zoom; not the sky shape in 3D) |

Conclusion: generated constellations must **not** pre-compress or alter distances. Feed real
light-years; the generator compresses radially and the Earth view stays correct. Checked numerically:
max ray deviation 1.5e-6 deg for depthScale 1, 0.5 and 0, and the solved image is a pure similarity
of the true gnomonic coordinates (relative residual < 1e-4) for all three constellations.

Caveat found: `NearFieldSkyProxy` measures the content only once, at startup, for the scene's main
constellation; `LoadConstellation` reuses that scale. A swapped-in constellation whose
`|obs->pivot| + reach` is larger overflows the globe: Big Dipper is 1.12-1.15x Cassiopeia's
(~1.2 R from the centre in some rotations), Orion 1.00x. Needs either a per-constellation fit
(zoom/fit fraction chosen to match) or a re-fit after `ConstellationLoaded` (scale about the observer,
which preserves the projection).

## 3. Pipeline

```
constellations.json (our QIDs + stick figures)      settings (artboard, fit)
          |                                                  |
 (a) ingest_wikidata.py  fetch -> raw entity JSON (own empty dir, untrusted)
                         normalize -> star_table.json (id, name, QID, RA/Dec, distance+sigma+kind,
                                      statement id, rank, references, V mag, flags)
          |
 (b) build_geometry.py   star_table + connections -> <id>.constellation.json
                         { displayName, artboardSize, stars[{starName, affinityPosition, distanceLightYears,
                           provenance}], connections[[i,j]], projection } + geometry_report.json (checks)
```

(a) never touches geometry; (b) never touches the network. (b) projects gnomonically about the mean
direction of the stars (north up, east left), then applies one uniform zoom per constellation so the
figure fills `fit_fraction` of the artboard half extents. Output is exactly the existing
`ConstellationGenerator.StarData` shape, so the existing generator, Earth view, chart, mini, sculpture,
stamp and solve code all consume it unchanged. Prototype numbers (fit 0.85): sky zoom vs real
Big Dipper 1.32, Cassiopeia 2.58, Orion 1.18. Fit 0.73 reproduces the current Cassiopeia size.

Source policy in (a): statements whose references cite Gaia, HYG, Stellarium, d3-celestial or NASA are
kept in the table but never selected. Distance = the van Leeuwen 2007 Hipparcos parallax statement for
every star (one consistent source; many are bot-ranked "deprecated" after Gaia was added, flagged).
Strict mode cannot place **Merak** and **Segin**: their only RA/Dec statements cite Gaia (E)DR3.
`--allow-excluded-positions` uses them, flagged `NEEDS_APPROVAL` (used for the prototype output).

## 4. Required game changes (proposal, not done)

Minimal, in order:

1. `ConstellationGenerator`: add `List<Vector2Int> connections` (empty = legacy i->i+1 polyline), used by
   `GenerateConstellation`. Big Dipper's closed bowl and Orion need branching/loops.
2. `StarChartLayout.CollectStarsAndSegments` (l.~790-808): read the same connection list.
3. Carry connections with the star set: `ConstellationSetup.LoadConstellation`,
   `GameFlow.StartConstellation/StartPractice/TryGetMainConstellation`, `TitleLetterMini`
   (`customStars` + `UseConstellation`/`CopyCustomStars`), BoxSelection's placed handler.
   Cleanest: a `ConstellationDefinition` ScriptableObject (name, artboard, stars, connections,
   provenance text) imported from `<id>.constellation.json` by an Editor menu; sculptures and the
   main generator reference assets instead of inline copies.
4. Globe fit for swapped constellations: `NearFieldSkyProxy` re-fit on `ConstellationSetup.ConstellationLoaded`
   (scale about the observer), or pick per-constellation fit fractions so `|obs->pivot| + reach` <= Cassiopeia's.
5. Data: Starter Pack sculptures 1/2/3 -> Big Dipper / Cassiopeia / Orion (scene edit).
6. Optional: `Editor/FieldNoteArt.cs` draws polylines (l.314, 338) - only matters if field-note art is
   regenerated for new constellations. `ConstellationGeometry.Collect`, SkyStamp3DVFX,
   EarthViewPulseAnchor already handle arbitrary rods.

Untouched: Cassiopeia can stay exactly as is (hand data); the pipeline output for it is only a cross-check.

## 5. Starter Pack order

Order today = which sculpture object holds which star set, and where it sits. Sculptures are authored
children of "Starter Pack Contents": Sculpture 1 at x -0.058 (left), 2 at x 0 (centre, raised),
3 at x +0.058 (right); `BoxSelection.sculptures` lists them 1, 2, 3; `GuidanceSculpture` points at the
middle one (`sculptures[Length/2]`). There is no play-order/unlock logic; "Choose another
constellation" just reopens the pack. Minimal change for Big Dipper -> Cassiopeia -> Orion: give
Sculpture 1 the Big Dipper set and Sculpture 3 Orion (scene data only); Cassiopeia stays in the centre,
so guidance keeps pointing at the known-good puzzle. For a configurable order: add
`ConstellationDefinition[] packOrder` to BoxSelection that calls `UseConstellation` + `Rebuild` on
sculptures[i] in OnEnable (slots stay fixed, content follows the list).

## 6. Validation plan

Automated (no headset): `build_geometry.py` checks (ray deviation over depth scales, similarity of the
solved image to true gnomonic coordinates, Cassiopeia vs existing residuals, reach vs Cassiopeia);
after integration, an EditMode test that generates each definition with the scene settings and asserts
(i) every star's direction from the observer matches its catalog direction rotated into the view frame
(< 0.01 deg), (ii) rod count/pairs equal the definition, (iii) `EarthViewCamera`'s "Verify Earth View"
passes for each constellation, (iv) far reach after `NearFieldSkyProxy` <= skyFill * volumeRadius in
24 sampled rotations, (v) chart segments == rods. Headset: readability of each solved figure in the
Earth view and globe, depth legibility of the mini (Big Dipper is shallow: 80-123 ly), Orion's
extreme depth (Alnilam), sculpture readability in the pack, globe overflow, guidance targets.

## 7. Risks

- Merak/Segin positions only cite Gaia in Wikidata (approval needed, or keep the hand-traced Segin).
- Orion depth is dominated by uncertain distances (Alnilam 1977 +- 539 ly, Betelgeuse 498 +- 63 ly); the
  figure's 3D shape (not its projection) depends on which value is chosen.
- Big Dipper depths are nearly equal (5 of 7 stars are the Ursa Major moving group, 80-86 ly): with
  depth compression the mini is close to flat; the puzzle may be too easy/hard to read.
- Connection-list change touches several systems a day before the deadline.
- Globe overflow for Big Dipper without a re-fit.
