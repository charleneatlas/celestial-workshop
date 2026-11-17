# Data provenance: real-star constellations (Big Dipper, Cassiopeia, Orion)

Verified / accessed: **2026-11-17**. Scope: the investigation prototype in `Tools/ConstellationPipeline/`
(branch `cc0-constellations-investigation`). Nothing here is in the game yet. This is a provenance
record, not legal advice.

## 1. Sources used

| Source | What we take | License declaration (verified 2026-11-17) |
|---|---|---|
| Wikidata structured data (items listed below), via the Action API `wbgetentities` at https://www.wikidata.org/w/api.php (SPARQL at https://query.wikidata.org/sparql only to find QIDs) | RA (P6257), Dec (P6258), parallax (P2214), distance from Earth (P2583), apparent magnitude (P1215) with filter (P1227), instance of (P31), has part (P527), statement ids, ranks and references | https://www.wikidata.org/wiki/Wikidata:Copyright : "All structured data from the main, Property, Lexeme, and EntitySchema namespaces is available under the Creative Commons CC0 License; text in the other namespaces is available under the Creative Commons Attribution-ShareAlike License." https://www.wikidata.org/wiki/Wikidata:Licensing : structured data is released "into the public domain under Creative Commons Zero"; every contributor applies CC0 to their contribution. https://www.wikidata.org/wiki/Wikidata:Data_access : data is "CC0, 'No rights reserved'", "no requirement for attribution", attribution to Wikidata appreciated. CC0 legal code: https://creativecommons.org/publicdomain/zero/1.0/ |
| Project-authored data | Which stars form each figure, the QIDs we picked, and the stick-figure connections (`Tools/ConstellationPipeline/constellations.json`) | Our own work |
| Existing in-game Cassiopeia (hand-traced artboard positions + distances, scene "Constellation Generator") | Used only as a cross-check | Project's own data. The chart image it was traced over is not in the repo and its source is unknown (see section 6). |

No reference images were used. Courtesy credit (not required by CC0, requested by Wikidata):
"Star positions and distances: Wikidata (CC0)."

Access etiquette: generic User-Agent `constellation-research-script/0.1` (no personal or project
details, per the project's privacy rule; Wikimedia's User-Agent policy asks for contact details, so
the user may want to add a generic contact address before any regular use). Two HTTP 429 responses
were honoured with Retry-After back-off. Raw responses were saved to a fresh scratch directory and
are not committed.

## 2. Statement references and the forbidden-source list

Wikidata statements cite their origin (P248 "stated in", often with P12132 "stated in source
according to" = SIMBAD, i.e. imported via the CDS SIMBAD aggregator). What we found for these 25 items:

- Positions: "Validation of the new Hipparcos reduction" (van Leeuwen 2007, A&A 474, 653, item
  Q28315126; called vL2007 below) for most stars; Gaia DR2/EDR3/DR3 for some; the Tycho double star
  catalogue / Tycho-2 for some component items.
- Parallaxes: vL2007, the 1997 Hipparcos Catalogue (Q66310660), the Yale General Catalogue of
  Trigonometric Stellar Parallaxes, Gaia DR2/EDR3.
- Distances: Gaia, "The interstellar Ca II distance scale", individual papers, some unreferenced.
- Magnitudes: Ducati 2002 (VizieR), "UBVRIJKL photometry of the bright stars" (1966), and in one case
  **The HYG Database** (Betelgeuse's V magnitude).

Assessment (conservative):

1. Wikidata declares the statements CC0. Single numeric measurements (a position, a parallax) are facts;
   we take about 4 values per star for 20 stars, which is not a substantial extraction from any
   source database.
2. Even so, the user's brief forbids Gaia, HYG, Stellarium, d3-celestial and NASA catalogs. Statements
   whose references cite any of these are **kept in the star table for transparency but never selected**
   (`EXCLUDED_REFERENCE_PATTERNS` in ingest_wikidata.py). This is a project policy, not a finding that
   Wikidata's CC0 declaration is wrong.
3. Hipparcos (ESA) values reach us only as CC0 Wikidata statements. We did not verify the Hipparcos
   catalogue's own terms and do not need to for CC0 statements; we still name vL2007 as the cited
   origin. Whether Hipparcos-cited values are acceptable is **for the user to approve** (Hipparcos is not
   on the forbidden list).
4. SIMBAD appears only as the import route ("according to"); no data was taken from SIMBAD directly.

## 3. Rejected sources and why

| Source | Reason |
|---|---|
| HYG database | Forbidden by the brief. Its one appearance (Betelgeuse V mag statement) is excluded. |
| Gaia DR2 / EDR3 / DR3 (directly, or as the only reference of a Wikidata statement) | Forbidden by the brief. Also technically unreliable for these naked-eye stars (saturation), e.g. Schedar's Gaia DR2 parallax 1.69 +- 1.28 mas = 1924 ly vs 228 ly from Hipparcos. |
| Stellarium sky cultures, d3-celestial line data | Forbidden; we author our own stick figures. |
| NASA catalogs / archives | Forbidden. None were needed. |
| Wikipedia infobox values / images | Text is CC BY-SA, not CC0; not used. |
| Any constellation chart image | No image individually verified as CC0 was needed; none used. |

## 4. Per-star measurements (prototype table, `Tools/ConstellationPipeline/output/star_table.prototype.json`)

Distance = 1000 / parallax(mas) pc x 3.26156 ly/pc, from the vL2007 parallax statement for every star.
Sigma = parallax sigma propagated (lowerBound/upperBound half-width). Statement ids are Wikidata GUIDs
(open `https://www.wikidata.org/wiki/<QID>` and search the GUID). RA/Dec in degrees, ICRS as stored;
no epoch qualifier on any statement (treated as J2000; proper-motion effects are far below the game's
precision).

| Star | QID | RA | Dec | position cited to | distance ly (+-) | parallax mas | statement (rank) | V mag | flags |
|---|---|---|---|---|---|---|---|---|---|
| Alkaid | Q13093 | 206.88516 | 49.31327 | vL2007 | 104 (1) | 31.38 | Q13093$d08a8b30-ef25-4edb-a242-d4cd0fb776c6 (normal) | 1.86 | 3 distance values agree |
| Mizar | Q66477109 | 200.98142 | 54.92535 | vL2007 | 86 (4) | 38.01 | Q66477109$c9ed5f0e-ce11-4db0-858d-6efda8cae5a2 (normal) | missing | system item (A+B); no magnitude; 1997 Hipparcos gives 78 ly |
| Alioth | Q13091 | 193.50729 | 55.95982 | vL2007 | 83 (0.4) | 39.51 | Q13091$6d6a7944-775b-4977-be7b-e93b22216d4c (deprecated) | 1.77 | rank deprecated (Gaia added) |
| Megrez | Q850779 | 183.85650 | 57.03262 | vL2007 | 81 (0.3) | 40.51 | Q850779$e4d8a601-4ad6-4605-b61d-b15843447994 (deprecated) | 3.32 | rank deprecated |
| Phecda | Q13099 | 178.45770 | 53.69476 | vL2007 | 83 (1) | 39.21 | Q13099$134567fd-cc45-4b5c-9f04-f3eb5f1d2027 (deprecated) | 2.44 | rank deprecated; Gaia DR2 says 111 ly (excluded) |
| Merak | Q13096 | 165.46033 | 56.38243 | **Gaia DR3 only** | 80 (0.3) | 40.9 | Q13096$ef94736e-0bd6-46f5-b8d2-c70fdc6d01fe (deprecated) | 2.37 | **position needs approval** |
| Dubhe | Q13084 | 165.93196 | 61.75103 | vL2007 | 123 (2) | 26.54 | Q13084$1dac4d4d-756b-437a-85ad-b4876c8c7234 (normal) | 1.79 | system item; component item Q66477223 has no distance |
| Segin | Q13602 | 28.59889 | 63.67010 | **Gaia EDR3 only** | 412 (22) | 7.92 | Q13602$4eb4e7f1-07d2-400a-a2da-318f5b0605c5 (deprecated) | 3.37 | **position needs approval** (hand-traced Segin exists) |
| Ruchbah | Q13597 | 21.45396 | 60.23528 | vL2007 | 99 (0.4) | 32.81 | Q13597$c7023e7c-784a-40fc-bf24-278bc453e496 (deprecated) | 2.68 | eclipsing binary |
| Gamma Cas | Q13584 | 14.17721 | 60.71674 | vL2007 | 549 (11) | 5.94 | Q13584$88de8044-ef03-4907-9c8a-794c21d5ef85 (normal) | 2.39 | values 382-620 ly (Ca II 382, Hipparcos 1997 613); Be-star variable |
| Schedar | Q13108 | 10.12684 | 56.53733 | vL2007 | 228 (2) | 14.29 | Q13108$cc118fd6-c4e2-44f7-b110-6b966acb46fb (deprecated) | 2.23 | rank deprecated; Gaia DR2 outlier 1924 ly |
| Caph | Q13594 | 2.29452 | 59.14978 | vL2007 | 55 (0.4) | 59.58 | Q13594$e37436bf-6113-4b53-9a5f-084f3393da64 (normal) | 2.27 | |
| Betelgeuse | Q12124 | 88.79294 | 7.40706 | vL2007 | 498 (63) | 6.55 | Q12124$09497601-418e-bf7f-cd90-bbadd6dc1f44 (normal) | excluded | **unreliable**: 13 % error; 427-548 ly across statements; V mag only HYG-cited; strongly variable |
| Bellatrix | Q13066 | 81.28276 | 6.34970 | vL2007 | 252 (10) | 12.92 | Q13066$61245e80-8fd2-4d2e-9fc5-9455705ee046 (normal) | 1.64 | Ca II distance 395 ly disagrees |
| Mintaka | Q680341 | 83.00167 | -0.29910 | vL2007 | 692 (85) | 4.71 | Q680341$795c8c18-4471-4f35-8d39-7fa78de53864 (normal) | 2.41 | 12 % error; 692-916 ly; multiple system |
| Alnilam | Q13070 | 84.05339 | -1.20192 | vL2007 | 1977 (539) | 1.65 | Q13070$5a78f796-9717-43ae-8552-dedce8b22dd3 (normal) | 1.69 | **unreliable**: 27 % error; Ca II 1194 ly; sets Orion's depth scale |
| Alnitak | Q13076 | 85.18969 | -1.94257 | vL2007 | 736 (106) | 4.43 | Q13076$23313544-442c-408e-b4bb-6762a5c5a277 (normal) | 1.77 | 14 % error; unreferenced 1260 ly statement; component item Q66477108 says 969 ly |
| Saiph | Q14028 | 86.93912 | -9.66960 | vL2007 | 647 (28) | 5.04 | Q14028$a1275329-87c5-464f-8ac0-946ed2265c3c (normal) | 2.06 | Ca II 799 ly |
| Rigel | Q12126 | 78.63447 | -8.20164 | vL2007 | 863 (78) | 3.78 | Q12126$275fdc5e-4393-38b0-a138-ed1b38a6287d (normal) | 0.13 | 9 % error; multiple system |
| Meissa (optional) | Q14219 | 83.78449 | 9.93416 | vL2007 | 1098 (203) | 2.97 | Q14219$29f72d38-eb88-4628-b434-e9be1f88e2b7 (normal) | 3.66 | 19 % error |

Common flags not repeated above: every star has 2-7 distance statements; nearly all are typed as
variable and/or double/multiple systems in Wikidata. Component/system ambiguity checked for Mizar
(Q66477109 system, Q13086 Mizar A, Q12064840 Zeta2 UMa), Dubhe (Q13084 vs Q66477223 "Alpha UMa A"),
Alnitak (Q13076 vs Q66477108 Zeta Ori A), Meissa (Q14219 vs Q66530986 Lambda Ori A). For a
naked-eye point of light we use the system item; positions differ by < 0.005 deg.

Strict mode (no excluded-source positions) leaves Merak and Segin without a usable position; the
prototype geometry uses them, flagged. The existing Cassiopeia distances (412, 99, 549, 228, 55 ly)
equal the vL2007 values above.

## 5. Transformations applied

1. Selection (ingest_wikidata.py): vL2007 parallax statement regardless of rank; else best
   non-excluded statement (preferred > normal; referenced first; smallest relative sigma). Positions:
   best non-excluded RA/Dec statement. Magnitude: V band only, non-excluded.
2. Distance: `ly = 1000 / parallax_mas * 3.261563777`; sigma propagated to first order.
3. Projection (build_geometry.py): mean unit vector of the figure's stars as centre; gnomonic standard
   coordinates (xi, eta); image x = -xi (east left), y = eta (north up); one uniform scale per
   constellation so the figure fills 85 % of the artboard's half extents (1920 x 1080 px, y down,
   matching the existing Affinity convention); px rounded to 0.1, ly rounded to 1.
4. No depth compression is applied in the pipeline; the game's generator compresses radially at runtime.
5. Connections: project-authored (constellations.json).

## 6. Open provenance items for the user

- Approve or reject Gaia-cited positions for Merak and Segin (alternatives: keep the hand-traced
  Cassiopeia as is; for Merak there is no non-Gaia position in Wikidata today).
- Approve Hipparcos (vL2007)-cited values as CC0 Wikidata statements.
- The chart image the existing Cassiopeia was traced over is undocumented. The tracing matches the
  real sky to <= 0.03 deg, so it can be replaced by the CC0-derived positions if its source is in doubt.
