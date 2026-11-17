"""Read-only audit: chart dot (BlueprintStar target) -> 3D GeneratedConstellationStar mapping.

Mirrors (as of b2ff8aa):
  LetterGlyphs.cs / LetterConstellation.Build   -> star names "<L><i+1>", affinity positions
  StarChartLayout.EnsureGeneratedTargets        -> one chart target per rounded affinity px; first name wins;
                                                   authored targets (scene BlueprintStar.starId) skip generation
  ConstellationSetup.SetStarHighlighted         -> highlights ONLY stars whose StarId == target id (OrdinalIgnoreCase)
Usage: python -I audit.py <repo_root> <out_csv>
"""
import csv, re, sys, os

repo, out_csv = sys.argv[1], sys.argv[2]
glyph_src = open(os.path.join(repo, 'Assets/CW_Title/LetterGlyphs.cs'), encoding='utf-8').read()
scene = open(os.path.join(repo, 'Assets/Scenes/CelestialWorkshop_VRGlasses_Prototype.unity'), encoding='utf-8').read()
prefab = open(os.path.join(repo, 'Assets/CW_Title/ConstellationTitle.prefab'), encoding='utf-8').read()

glyphs = {}
for m in re.finditer(r"\['([A-Z])'\]\s*=\s*P\(([^)]*)\)", glyph_src):
    nums = [float(v.strip().rstrip('f')) for v in m.group(2).split(',')]
    glyphs[m.group(1)] = [(nums[i], nums[i + 1]) for i in range(0, len(nums), 2)]

def letter_stars(c):
    g = glyphs[c]
    xs = [p[0] for p in g]; ys = [p[1] for p in g]
    cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
    ppu = 800.0 / max(1e-4, max(ys) - min(ys))
    return [(f"{c}{i + 1}", (930 + (x - cx) * ppu, 525 - (y - cy) * ppu)) for i, (x, y) in enumerate(g)]

def csround(v):  # Mathf.RoundToInt = banker's rounding
    return int(round(v))

# Title rows (prefab; the scene has no rows override)
rows = re.search(r"\n  rows:\n((?:  - .*\n)+)", prefab).group(1)
rows = [r.strip()[2:] for r in rows.strip().splitlines()]
title_letters = sorted(set(''.join(rows)))

# Star lists in the build scene
def parse_lists(text, key):
    res = []
    for m in re.finditer(r"\n  %s:\n((?:  - starName: .*\n    affinityPosition: .*\n    distanceLightYears: .*\n)+)" % key, text):
        stars = []
        for s in re.finditer(r"- starName: (.*)\n    affinityPosition: \{x: ([-\d.e]+), y: ([-\d.e]+)\}", m.group(1)):
            stars.append((s.group(1).strip(), (float(s.group(2)), float(s.group(3)))))
        res.append(stars)
    return res

gen_lists = parse_lists(scene, 'stars')
custom_lists = parse_lists(scene, 'customStars')
authored = sorted(set(m.group(1).strip() for m in re.finditer(r"\n  starId: (.*)", scene)))

puzzles = {}
for c in title_letters:
    puzzles[f"Practice letter {c}"] = letter_stars(c)
uniq = {tuple(l) for l in gen_lists}
for i, l in enumerate(sorted(uniq)):
    puzzles[f"Scene generator star list #{i + 1} (main Cassiopeia)"] = list(l)
for i, l in enumerate(custom_lists):
    puzzles[f"Starter Pack sculpture #{i + 1} (customStars)"] = l

rows_out = []
summary = []
for pname, stars in puzzles.items():
    authored_lc = set(authored)  # existing.Contains is case-SENSITIVE
    seen_pos = {}
    targets = []  # (target id, pos)
    for name, pos in stars:
        key = (csround(pos[0]), csround(pos[1]))
        if key in seen_pos:
            continue
        seen_pos[key] = name
        targets.append((name, pos, key))
    # authored targets that resolve (TryGetStarData is case-insensitive)
    names_ci = {n.lower() for n, _ in stars}
    bad = 0
    for tid, pos, key in targets:
        is_authored = tid in authored_lc
        lit = [n for n, _ in stars if n.lower() == tid.lower()]
        coloc = [n for n, p in stars if (csround(p[0]), csround(p[1])) == key]
        unlit = [n for n in coloc if n.lower() != tid.lower()]
        dup_name = len(lit) > 1
        status, reason = 'OK', ''
        if unlit:
            status = 'BROKEN'
            reason = (f"co-located twin sphere(s) {unlit} share this dot but are NOT highlighted "
                      f"(SetStarHighlighted matches by exact id); twin keeps normal material and "
                      f"overdraws/additively washes the highlight")
        elif dup_name:
            status = 'SUSPICIOUS'; reason = 'star name used more than once'
        if status != 'OK':
            bad += 1
        rows_out.append([pname, tid, 'authored' if is_authored else 'generated',
                         f"{pos[0]:.1f},{pos[1]:.1f}", ';'.join(lit), ';'.join(unlit), status, reason])
    # stars with no chart dot of their own
    for name, pos in stars:
        if not any(name == t[0] for t in targets):
            rows_out.append([pname, '(no dot)', '-', f"{pos[0]:.1f},{pos[1]:.1f}", '', name, 'INFO',
                             f"{name} is a revisit; reachable only via the dot of the first star at that point"])
    # authored targets not in this star set are hidden (OK) ; name-case mismatch check
    for a in authored:
        if a.lower() in names_ci and a not in [n for n, _ in stars]:
            rows_out.append([pname, a, 'authored', '', '', '', 'SUSPICIOUS', 'case-only name mismatch'])
    summary.append((pname, len(stars), len(targets), bad))

with open(out_csv, 'w', newline='', encoding='utf-8') as f:
    w = csv.writer(f)
    w.writerow(['puzzle', 'chart_dot_id', 'dot_source', 'affinity_px', 'stars_highlighted',
                'colocated_stars_NOT_highlighted', 'status', 'reason'])
    w.writerows(rows_out)

print('title rows:', rows, 'letters:', ''.join(title_letters))
print('authored chart targets:', authored)
print('scene generator lists:', len(gen_lists), 'unique:', len(uniq), ' custom lists:', len(custom_lists))
print('glyphs with revisits:', [c for c, g in glyphs.items() if len(set(g)) != len(g)])
for s in summary:
    print(f"{s[0]:<55} stars={s[1]:>2} dots={s[2]:>2} broken={s[3]}")
print()
for r in rows_out:
    if r[6] != 'OK':
        print(r)
