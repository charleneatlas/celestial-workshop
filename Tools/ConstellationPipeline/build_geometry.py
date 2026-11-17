"""Step (b) of the CC0 constellation pipeline: star table -> game geometry.

Reads the normalized star table (ingest_wikidata.py) and our own connection
definitions (constellations.json), and writes, per constellation, the data the
existing puzzle consumes: ConstellationGenerator.StarData (starName,
affinityPosition in artboard px with y down, distanceLightYears) plus an
explicit connection list. No catalog access here; no Unity needed.

Projection: the generator turns an artboard point into the ray
    normalize(nx * spread, ny * spread, 1),  nx, ny = (px - W/2, H/2 - py) / (min(W, H) / 2)
which is the inverse of a gnomonic (tangent-plane, pinhole camera) projection.
So we project each star's RA/Dec gnomonically about the constellation's centre
(north up, east left, as seen looking up at the sky) and convert tangent-plane
coordinates to artboard px with one uniform zoom per constellation. A uniform
zoom of tangent-plane coordinates is exactly a change of camera field of view,
so the solved Earth view shows the true sky shape. Depth is NOT compressed
here: the real distance in light-years is written and the generator applies
the game's radial compression along the observer's rays, which leaves the
projection unchanged.

It also re-implements ConstellationGenerator's sky placement to check, numerically:
  * every generated star lies on its catalog sight line from the observer
    (angular error), before and after the game's depth compression;
  * the existing hand-traced Cassiopeia against the catalog-derived one
    (best-fit similarity, residuals in px and degrees).

Usage:
    python -I build_geometry.py --config constellations.json --stars star_table.json --out OUT_DIR
"""

import argparse
import json
import math
import os

# Scene values (CelestialWorkshop_VRGlasses_Prototype.unity, branch
# MetaVRGlasses_DevCompetition2026 @ b2ff8aa). Kept here only for the checks.
ARTBOARD = (1920.0, 1080.0)
ANGULAR_SPREAD = 0.7         # ConstellationGenerator.angularSpread
SKY_ANGULAR_SCALE = 0.3      # ConstellationSetup.skyAngularScale
SKY_DEPTH_SCALE = 0.5        # ConstellationSetup.skyDepthScale
SKY_DISTANCE_SCALE = 800.0   # ConstellationSetup.skyDistanceScale
SKY_ELEVATION_DEG = 15.0     # ConstellationSetup.skyElevationDegrees
MAXIMUM_RADIUS = 1.0         # ConstellationGenerator.maximumRadius

# Existing in-game Cassiopeia ("Constellation Generator" in the scene).
EXISTING_CASSIOPEIA = [
    ("Segin", 299.9, 161.9, 412.0),
    ("Ruchbah", 596.4, 541.0, 99.0),
    ("Gamma Cassiopeia", 949.3, 492.5, 549.0),
    ("Schedar", 1198.5, 887.8, 228.0),
    ("Caph", 1562.1, 559.9, 55.0),
]


# ------------------------------------------------------------------ vectors

def unit_from_radec(ra_deg, dec_deg):
    ra, dec = math.radians(ra_deg), math.radians(dec_deg)
    return (math.cos(dec) * math.cos(ra), math.cos(dec) * math.sin(ra), math.sin(dec))


def norm(v):
    length = math.sqrt(sum(c * c for c in v))
    return tuple(c / length for c in v)


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def angle_deg(a, b):
    return math.degrees(math.acos(max(-1.0, min(1.0, dot(norm(a), norm(b))))))


def gnomonic(ra_deg, dec_deg, ra0_deg, dec0_deg):
    """Standard coordinates (xi east, eta north) on the tangent plane at (ra0, dec0)."""
    ra, dec = math.radians(ra_deg), math.radians(dec_deg)
    ra0, dec0 = math.radians(ra0_deg), math.radians(dec0_deg)
    cos_c = math.sin(dec0) * math.sin(dec) + math.cos(dec0) * math.cos(dec) * math.cos(ra - ra0)
    xi = math.cos(dec) * math.sin(ra - ra0) / cos_c
    eta = (math.cos(dec0) * math.sin(dec) - math.sin(dec0) * math.cos(dec) * math.cos(ra - ra0)) / cos_c
    return xi, eta


def centre_of(stars):
    s = [0.0, 0.0, 0.0]
    for star in stars:
        u = unit_from_radec(star["ra"], star["dec"])
        s = [a + b for a, b in zip(s, u)]
    x, y, z = norm(s)
    return math.degrees(math.atan2(y, x)) % 360.0, math.degrees(math.asin(z))


# ---------------------------------------------------- generator re-implementation

def artboard_to_normalized(px, py, artboard=ARTBOARD):
    common = min(artboard) * 0.5
    return (px - artboard[0] * 0.5) / common, (artboard[1] * 0.5 - py) / common


def euler_x(deg, v):
    # Unity Quaternion.Euler(x, 0, 0) applied to v (rotation about +X, left-handed).
    a = math.radians(deg)
    x, y, z = v
    return (x, y * math.cos(a) - z * math.sin(a), y * math.sin(a) + z * math.cos(a))


def sky_positions(stars_px_ly, spread=ANGULAR_SPREAD, angular_scale=SKY_ANGULAR_SCALE,
                  depth_scale=SKY_DEPTH_SCALE, distance_scale=SKY_DISTANCE_SCALE,
                  elevation=SKY_ELEVATION_DEG):
    """ConstellationGenerator.GenerateConstellation with an observer at the origin
    (linear distance mode, as in the scene). Returns world offsets from the observer."""
    d_max = max(d for _, _, d in stars_px_ly)
    out = []
    for px, py, d in stars_px_ly:
        nx, ny = artboard_to_normalized(px, py)
        direction = norm((nx * spread * angular_scale, ny * spread * angular_scale, 1.0))
        direction = euler_x(-elevation, direction)
        radial = d / d_max * MAXIMUM_RADIUS
        scaled = radial * distance_scale
        shell = MAXIMUM_RADIUS * distance_scale
        compressed = shell + (scaled - shell) * depth_scale
        out.append(tuple(c * compressed for c in direction))
    return out


# ------------------------------------------------------------ 2D similarity fit

def fit_similarity(src, dst):
    """Least-squares dst ~ s R src + t (2D). Returns scale, rotation_deg, residuals."""
    n = len(src)
    sx = sum(p[0] for p in src) / n
    sy = sum(p[1] for p in src) / n
    dx = sum(p[0] for p in dst) / n
    dy = sum(p[1] for p in dst) / n
    a = b = den = 0.0
    for (x, y), (u, v) in zip(src, dst):
        x, y, u, v = x - sx, y - sy, u - dx, v - dy
        a += x * u + y * v
        b += x * v - y * u
        den += x * x + y * y
    a, b = a / den, b / den
    scale = math.hypot(a, b)
    rot = math.degrees(math.atan2(b, a))
    residuals = []
    for (x, y), (u, v) in zip(src, dst):
        x, y = x - sx, y - sy
        fx = a * x - b * y + dx
        fy = b * x + a * y + dy
        residuals.append(math.hypot(fx - u, fy - v))
    return scale, rot, residuals


# ------------------------------------------------------------------- building

def load_star_rows(path):
    with open(path, encoding="utf-8") as handle:
        table = json.load(handle)
    rows = {}
    for row in table["stars"]:
        rows[(row["constellation"], row["id"])] = row
    return table, rows


def build(constellation, rows, fit_fraction, include_optional):
    stars = []
    defs = list(constellation["stars"]) + (constellation.get("optionalStars", []) if include_optional else [])
    problems = []
    for star in defs:
        row = rows.get((constellation["id"], star["id"]))
        if row is None or not row.get("ra_deg") or not row.get("dec_deg") or not row.get("distance"):
            problems.append(star["id"])
            continue
        stars.append({
            "id": star["id"], "name": star["name"], "qid": star["qid"],
            "ra": row["ra_deg"]["value"], "dec": row["dec_deg"]["value"],
            "ly": row["distance"]["ly"], "ly_sigma": row["distance"].get("ly_sigma"),
            "distance_statement": row["distance"]["statement_id"],
            "position_statement": row["ra_deg"]["statement_id"],
            "flags": row["flags"],
        })
    if problems:
        return None, problems

    ra0, dec0 = centre_of(stars)
    for s in stars:
        xi, eta = gnomonic(s["ra"], s["dec"], ra0, dec0)
        s["tan_x"], s["tan_y"] = -xi, eta   # east to the left, north up (looking up at the sky)

    # One zoom per constellation: the figure's bounding box (centred) fills
    # fit_fraction of the artboard's half extents.
    half_w, half_h = ARTBOARD[0] * 0.5, ARTBOARD[1] * 0.5
    max_x = max(abs(s["tan_x"]) for s in stars)
    max_y = max(abs(s["tan_y"]) for s in stars)
    px_per_tan = fit_fraction * min(half_w / max_x, half_h / max_y)
    for s in stars:
        s["px"] = half_w + s["tan_x"] * px_per_tan
        s["py"] = half_h - s["tan_y"] * px_per_tan

    ids = [s["id"] for s in stars]
    connections = []
    for pair in constellation["connections"] + (constellation.get("optionalConnections", []) if include_optional else []):
        if pair[0] in ids and pair[1] in ids:
            connections.append([ids.index(pair[0]), ids.index(pair[1])])

    # Angular size the generator gives the sky copy with the scene's settings:
    # tan(angle) = n * spread * skyAngularScale, n = px offset / (min(W,H)/2).
    game_tan_per_px = ANGULAR_SPREAD * SKY_ANGULAR_SCALE / (min(ARTBOARD) * 0.5)
    zoom = px_per_tan * game_tan_per_px   # game tangent / true tangent

    return {
        "displayName": constellation["displayName"],
        "artboardSize": {"x": ARTBOARD[0], "y": ARTBOARD[1]},
        "projection": {
            "type": "gnomonic", "centre_ra_deg": round(ra0, 6), "centre_dec_deg": round(dec0, 6),
            "orientation": "north up, east left", "px_per_tangent_unit": round(px_per_tan, 3),
            "fit_fraction": fit_fraction,
            "sky_zoom_vs_real": round(zoom, 3),
        },
        "stars": [{
            "starName": s["name"], "affinityPosition": {"x": round(s["px"], 1), "y": round(s["py"], 1)},
            "distanceLightYears": round(s["ly"]),
            "provenance": {"qid": s["qid"], "ra_deg": s["ra"], "dec_deg": s["dec"],
                           "position_statement": s["position_statement"],
                           "distance_ly": s["ly"], "distance_ly_sigma": s["ly_sigma"],
                           "distance_statement": s["distance_statement"], "flags": s["flags"]},
        } for s in stars],
        "connections": connections,
    }, stars


def check_projection(built, stars):
    """Each star's sky position must lie on the sight line the gnomonic image
    implies, at any depth compression: compare the ray from the observer to the
    generated position against the ray implied by its tangent-plane point."""
    pts = [(s["affinityPosition"]["x"], s["affinityPosition"]["y"], s["distanceLightYears"]) for s in built["stars"]]
    worst = 0.0
    for depth in (1.0, SKY_DEPTH_SCALE, 0.0):
        positions = sky_positions(pts, depth_scale=depth)
        flat = sky_positions([(x, y, 1.0) for x, y, _ in pts], depth_scale=1.0)
        for p, q in zip(positions, flat):
            worst = max(worst, angle_deg(p, q))
    # Shape check: the projected sky image (tangent coords seen from the observer)
    # must be a similarity of the true gnomonic coordinates.
    positions = sky_positions(pts)
    img = []
    for p in positions:
        v = euler_x(SKY_ELEVATION_DEG, p)   # undo the elevation: back into the view frame
        img.append((v[0] / v[2], v[1] / v[2]))
    truth = [(s["tan_x"], s["tan_y"]) for s in stars]
    scale, rot, res = fit_similarity(truth, img)
    rel = max(res) / (scale * max(math.hypot(x, y) for x, y in truth))
    return {"max_ray_deviation_deg_over_depth_scales": worst,
            "image_vs_true_similarity": {"scale": scale, "rotation_deg": rot, "max_rel_residual": rel}}


def compare_cassiopeia(built, stars):
    existing = {name: (x, y, d) for name, x, y, d in EXISTING_CASSIOPEIA}
    src, dst, names = [], [], []
    for s in built["stars"]:
        if s["starName"] in existing:
            names.append(s["starName"])
            src.append((s["affinityPosition"]["x"], -s["affinityPosition"]["y"]))
            ex = existing[s["starName"]]
            dst.append((ex[0], -ex[1]))
    scale, rot, res = fit_similarity(src, dst)
    # Residuals in degrees on the true sky: px -> tangent units of the generated layout.
    px_per_tan = built["projection"]["px_per_tangent_unit"]
    res_deg = [math.degrees(math.atan(r / scale / px_per_tan)) for r in res]

    # Angular size: separations in the existing in-game sky vs the real sky.
    ex_pts = [(x, y, d) for _, x, y, d in EXISTING_CASSIOPEIA]
    ex_sky = sky_positions(ex_pts)
    real = {s["name"]: unit_from_radec(s["ra"], s["dec"]) for s in stars}
    pairs = []
    for i in range(len(EXISTING_CASSIOPEIA)):
        for j in range(i + 1, len(EXISTING_CASSIOPEIA)):
            a, b = EXISTING_CASSIOPEIA[i][0], EXISTING_CASSIOPEIA[j][0]
            pairs.append((a, b, angle_deg(ex_sky[i], ex_sky[j]), angle_deg(real[a], real[b])))
    ratio = sum(p[2] for p in pairs) / sum(p[3] for p in pairs)

    # 3D: existing vs generated sky positions (metres from the observer, before NearFieldSkyProxy).
    gen_sky = sky_positions([(s["affinityPosition"]["x"], s["affinityPosition"]["y"], s["distanceLightYears"]) for s in built["stars"]])
    return {
        "similarity_fit_generated_to_existing": {
            "scale": round(scale, 4), "rotation_deg": round(rot, 2),
            "residual_px": {n: round(r, 1) for n, r in zip(names, res)},
            "residual_true_sky_deg": {n: round(r, 3) for n, r in zip(names, res_deg)},
        },
        "existing_angular_size_vs_real": round(ratio, 3),
        "pair_separations_deg_existing_vs_real": [(a, b, round(g, 2), round(r, 2)) for a, b, g, r in pairs],
        "existing_distances_ly": {n: d for n, _, _, d in EXISTING_CASSIOPEIA},
        "generated_distances_ly": {s["starName"]: s["distanceLightYears"] for s in built["stars"]},
        "sky_radius_m_existing": [round(math.sqrt(dot(p, p)), 1) for p in ex_sky],
        "sky_radius_m_generated": [round(math.sqrt(dot(p, p)), 1) for p in gen_sky],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--config", required=True)
    parser.add_argument("--stars", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--fit-fraction", type=float, default=0.85,
                        help="share of the artboard's half extents the figure fills (Cassiopeia's tracing: about 0.66 x 0.69)")
    parser.add_argument("--include-optional", action="store_true")
    args = parser.parse_args()

    with open(args.config, encoding="utf-8") as handle:
        config = json.load(handle)
    _, rows = load_star_rows(args.stars)
    os.makedirs(args.out, exist_ok=True)
    report = {}
    for constellation in config["constellations"]:
        built, stars = build(constellation, rows, args.fit_fraction, args.include_optional)
        if built is None:
            report[constellation["id"]] = {"error": "missing data for " + ", ".join(stars)}
            continue
        entry = {"projection": built["projection"], "checks": check_projection(built, stars),
                 "stars": [(s["starName"], s["affinityPosition"]["x"], s["affinityPosition"]["y"], s["distanceLightYears"]) for s in built["stars"]],
                 "connections": built["connections"]}
        if constellation["id"] == "cassiopeia":
            entry["cassiopeia_vs_existing"] = compare_cassiopeia(built, stars)
        report[constellation["id"]] = entry
        with open(os.path.join(args.out, constellation["id"] + ".constellation.json"), "w", encoding="utf-8") as handle:
            json.dump(built, handle, indent=2, ensure_ascii=False)
    with open(os.path.join(args.out, "geometry_report.json"), "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    print(json.dumps(report, indent=1))


if __name__ == "__main__":
    main()
