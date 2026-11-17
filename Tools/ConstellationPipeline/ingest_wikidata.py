"""Step (a) of the CC0 constellation pipeline: Wikidata -> normalized star table.

Ingestion only. No geometry, no game settings. Reads our own constellations.json
(QIDs + connections), downloads the entities from Wikidata (CC0 structured data)
with wbgetentities, and writes a normalized star table with every candidate
measurement, its statement rank, uncertainty and reference, plus quality flags.

Usage (run with -I; keep raw downloads in their own new empty directory):
    python -I ingest_wikidata.py fetch     --config constellations.json --raw RAW_DIR
    python -I ingest_wikidata.py normalize --config constellations.json --raw RAW_DIR --out star_table.json

Only the Python standard library is used. Downloaded JSON is treated as data:
it is parsed with json.load and never executed or used to build paths.
"""

import argparse
import datetime
import json
import math
import os
import re
import sys
import time
import urllib.parse
import urllib.error
import urllib.request

API = "https://www.wikidata.org/w/api.php"
USER_AGENT = "constellation-research-script/0.1"

# Properties we read (all Wikidata structured data, CC0).
P_INSTANCE_OF = "P31"
P_CONSTELLATION = "P59"
P_RA = "P6257"            # right ascension (degrees)
P_DEC = "P6258"           # declination (degrees)
P_EPOCH = "P6259"         # epoch qualifier
P_PARALLAX = "P2214"      # parallax (mas)
P_DISTANCE = "P2583"      # distance from Earth
P_APP_MAG = "P1215"       # apparent magnitude
P_FILTER = "P1227"        # astronomical filter qualifier
P_PART_OF = "P361"
P_HAS_PART = "P527"
P_STATED_IN = "P248"
P_REF_URL = "P854"
P_RETRIEVED = "P813"
P_CATALOG_CODE = "P528"
P_CATALOG = "P972"

UNIT_LY = "Q531"
UNIT_PC = "Q12129"
UNIT_KPC = "Q11929860"
UNIT_MAS = "Q21500224"
UNIT_DEG = "Q28390"

LY_PER_PC = 3.261563777

QID_RE = re.compile(r"^Q[1-9][0-9]{0,11}$")

# Conservative source policy for statements (see DATA_PROVENANCE.md). A
# statement whose reference names one of these is kept in the table for
# transparency but is never selected as the value we use.
#
# Matched against the statement's references (stated-in labels and URLs).
# Wikidata item for van Leeuwen 2007, "Validation of the new Hipparcos
# reduction" (A&A 474, 653), cited by the parallax statements we prefer.
PREFERRED_PARALLAX_SOURCE = "Q28315126"
ALLOW_EXCLUDED_POSITIONS = False  # set by --allow-excluded-positions (prototype only, flagged)

# Wikidata labels for source items that have no English label (mul only).
SOURCE_LABEL_FALLBACK = {
    "Q28315126": "van Leeuwen 2007, Validation of the new Hipparcos reduction (A&A 474, 653)",
    "Q66310660": "Perryman et al. 1997, The Hipparcos Catalogue (A&A 323, L49)",
}

EXCLUDED_REFERENCE_PATTERNS = [
    r"\bgaia\b",
    r"\bnasa\b",
    r"exoplanet archive",
    r"\bhyg\b",
    r"stellarium",
    r"d3-celestial",
]


def http_get_json(params, out_path):
    query = urllib.parse.urlencode(params)
    request = urllib.request.Request(API + "?" + query, headers={"User-Agent": USER_AGENT})
    for attempt in range(6):
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                body = response.read()
            break
        except urllib.error.HTTPError as error:
            if error.code not in (429, 503) or attempt == 5:
                raise
            retry = error.headers.get("Retry-After", "")
            wait = min(120, int(retry)) if retry.isdigit() else 10 * (attempt + 1)
            print("HTTP %d, waiting %d s" % (error.code, wait))
            time.sleep(wait)
    data = json.loads(body.decode("utf-8"))
    if "error" in data:
        raise RuntimeError("API error: %s" % data["error"].get("info"))
    with open(out_path, "wb") as handle:
        handle.write(body)
    return data


def load_config(path):
    with open(path, encoding="utf-8") as handle:
        config = json.load(handle)
    for constellation in config["constellations"]:
        for star in constellation["stars"] + constellation.get("optionalStars", []):
            for qid in [star["qid"]] + star.get("alternates", []):
                if not QID_RE.match(qid):
                    raise ValueError("bad QID in config: " + qid)
    return config


def config_qids(config):
    qids = []
    for constellation in config["constellations"]:
        for star in constellation["stars"] + constellation.get("optionalStars", []):
            for qid in [star["qid"]] + star.get("alternates", []):
                if qid not in qids:
                    qids.append(qid)
    return qids


def fetch(args):
    config = load_config(args.config)
    os.makedirs(args.raw, exist_ok=True)
    if os.listdir(args.raw):
        sys.exit("raw directory must be new and empty: " + args.raw)

    qids = config_qids(config)
    entities = {}
    for start in range(0, len(qids), 40):
        chunk = qids[start:start + 40]
        data = http_get_json(
            {"action": "wbgetentities", "ids": "|".join(chunk), "format": "json",
             "props": "labels|descriptions|claims|sitelinks/urls", "languages": "en|mul"},
            os.path.join(args.raw, "entities_%02d.json" % (start // 40)))
        entities.update(data.get("entities", {}))
        time.sleep(3.0)

    # Labels for every item the statements point at (units, filters, sources, catalogs).
    referenced = set()
    for entity in entities.values():
        for claims in entity.get("claims", {}).values():
            for claim in claims:
                referenced.update(_items_in_claim(claim))
    referenced = sorted(q for q in referenced if QID_RE.match(q) and q not in entities)
    for start in range(0, len(referenced), 25):
        chunk = referenced[start:start + 25]
        http_get_json(
            {"action": "wbgetentities", "ids": "|".join(chunk), "format": "json",
             "props": "labels", "languages": "en|mul"},
            os.path.join(args.raw, "labels_%02d.json" % (start // 25)))
        time.sleep(3.0)

    with open(os.path.join(args.raw, "fetch_info.json"), "w", encoding="utf-8") as handle:
        json.dump({"fetched_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                   "api": API, "user_agent": USER_AGENT, "qids": qids}, handle, indent=2)
    print("fetched %d entities, %d referenced labels into %s" % (len(entities), len(referenced), args.raw))


def _items_in_snak(snak):
    out = []
    value = snak.get("datavalue", {}).get("value")
    if isinstance(value, dict):
        if value.get("entity-type") == "item" and "id" in value:
            out.append(value["id"])
        unit = value.get("unit", "")
        if isinstance(unit, str) and "/entity/" in unit:
            out.append(unit.rsplit("/", 1)[-1])
    return out


def _items_in_claim(claim):
    out = _items_in_snak(claim.get("mainsnak", {}))
    for snaks in claim.get("qualifiers", {}).values():
        for snak in snaks:
            out += _items_in_snak(snak)
    for reference in claim.get("references", []):
        for snaks in reference.get("snaks", {}).values():
            for snak in snaks:
                out += _items_in_snak(snak)
    return out


def _read_raw(raw_dir):
    entities, labels = {}, {}
    for name in sorted(os.listdir(raw_dir)):
        if not re.match(r"^(entities|labels)_[0-9]{2}\.json$", name):
            continue
        with open(os.path.join(raw_dir, name), encoding="utf-8") as handle:
            data = json.load(handle)
        for qid, entity in data.get("entities", {}).items():
            label = entity.get("labels", {}).get("en", {}).get("value")
            labels[qid] = label or entity.get("labels", {}).get("mul", {}).get("value") or SOURCE_LABEL_FALLBACK.get(qid)
            if name.startswith("entities"):
                entities[qid] = entity
    info = {}
    info_path = os.path.join(raw_dir, "fetch_info.json")
    if os.path.exists(info_path):
        with open(info_path, encoding="utf-8") as handle:
            info = json.load(handle)
    return entities, labels, info


def _quantity(snak):
    value = snak.get("datavalue", {}).get("value")
    if not isinstance(value, dict) or "amount" not in value:
        return None
    amount = float(value["amount"])
    unit = value.get("unit", "1").rsplit("/", 1)[-1]
    lower = value.get("lowerBound")
    upper = value.get("upperBound")
    sigma = None
    if lower is not None and upper is not None:
        sigma = (float(upper) - float(lower)) / 2.0
    return {"amount": amount, "unit": unit, "sigma": sigma}


def _reference_summary(claim, labels):
    refs = []
    for reference in claim.get("references", []):
        entry = {}
        for pid, snaks in reference.get("snaks", {}).items():
            for snak in snaks:
                value = snak.get("datavalue", {}).get("value")
                if pid == P_STATED_IN and isinstance(value, dict):
                    entry.setdefault("stated_in", []).append(
                        {"qid": value.get("id"), "label": labels.get(value.get("id"))})
                elif pid == P_REF_URL and isinstance(value, str):
                    entry.setdefault("url", []).append(value)
                elif pid == P_RETRIEVED and isinstance(value, dict):
                    entry["retrieved"] = value.get("time")
                elif pid == "P1065" and isinstance(value, str):
                    entry.setdefault("archive_url", []).append(value)
        refs.append(entry)
    return refs


def _qualifier_items(claim, pid, labels):
    out = []
    for snak in claim.get("qualifiers", {}).get(pid, []):
        value = snak.get("datavalue", {}).get("value")
        if isinstance(value, dict) and "id" in value:
            out.append({"qid": value["id"], "label": labels.get(value["id"])})
    return out


def _excluded(refs):
    text = json.dumps(refs).lower()
    return any(re.search(pattern, text) for pattern in EXCLUDED_REFERENCE_PATTERNS)


def _statements(entity, pid, labels):
    out = []
    for claim in entity.get("claims", {}).get(pid, []):
        snak = claim.get("mainsnak", {})
        if snak.get("snaktype") != "value":
            continue
        quantity = _quantity(snak)
        if quantity is None:
            continue
        refs = _reference_summary(claim, labels)
        out.append({
            "statement_id": claim.get("id"),
            "rank": claim.get("rank"),
            "value": quantity["amount"],
            "unit": quantity["unit"],
            "unit_label": labels.get(quantity["unit"]),
            "sigma": quantity["sigma"],
            "filter": _qualifier_items(claim, P_FILTER, labels),
            "epoch": _qualifier_items(claim, P_EPOCH, labels),
            "references": refs,
            "unreferenced": len(refs) == 0,
            "excluded_by_policy": _excluded(refs),
        })
    return out


def _to_ly(statement):
    unit, value, sigma = statement["unit"], statement["value"], statement["sigma"]
    if unit == UNIT_LY:
        return value, sigma
    if unit == UNIT_PC:
        return value * LY_PER_PC, None if sigma is None else sigma * LY_PER_PC
    if unit == UNIT_KPC:
        return value * 1000 * LY_PER_PC, None if sigma is None else sigma * 1000 * LY_PER_PC
    return None, None


def _source_names(statement):
    if not statement:
        return "?"
    names = sorted({(s.get("label") or s.get("qid") or "?") for r in statement["references"]
                    for s in r.get("stated_in", [])})
    return ", ".join(names) or "unreferenced"


def _choose(statements, allow_excluded=False, allow_deprecated=False):
    """Preferred rank first, then normal; deprecated and policy-excluded only when allowed.
    Among equals: referenced before unreferenced, then smallest relative sigma."""
    usable = [s for s in statements
              if (allow_deprecated or s["rank"] != "deprecated")
              and (allow_excluded or not s["excluded_by_policy"])]
    if not usable:
        return None
    def key(s):
        rank = 0 if s["rank"] == "preferred" else 1
        rel = (s["sigma"] / abs(s["value"])) if (s["sigma"] and s["value"]) else 9.0
        return (rank, 1 if s["unreferenced"] else 0, rel)
    return sorted(usable, key=key)[0]


def _is_v_band(statement):
    labels = " ".join((f.get("label") or "") for f in statement["filter"]).lower()
    return (not statement["filter"]) or "v band" in labels or "visual" in labels or labels.strip() in ("v",)


def normalize_star(star, entity, labels, constellation_id, optional):
    flags = []
    name = star["name"]
    if entity is None or "missing" in entity:
        return {"id": star["id"], "name": name, "qid": star["qid"], "flags": ["MISSING_ENTITY"]}

    instance_of = [labels.get(c["mainsnak"]["datavalue"]["value"]["id"]) or c["mainsnak"]["datavalue"]["value"]["id"]
                   for c in entity.get("claims", {}).get(P_INSTANCE_OF, [])
                   if c.get("mainsnak", {}).get("datavalue")]
    has_parts = [c["mainsnak"]["datavalue"]["value"]["id"]
                 for c in entity.get("claims", {}).get(P_HAS_PART, []) if c.get("mainsnak", {}).get("datavalue")]
    part_of = [c["mainsnak"]["datavalue"]["value"]["id"]
               for c in entity.get("claims", {}).get(P_PART_OF, []) if c.get("mainsnak", {}).get("datavalue")]

    ra = _statements(entity, P_RA, labels)
    dec = _statements(entity, P_DEC, labels)
    plx = _statements(entity, P_PARALLAX, labels)
    dist = _statements(entity, P_DISTANCE, labels)
    mag = _statements(entity, P_APP_MAG, labels)

    ra_c, dec_c = _choose(ra), _choose(dec)
    if ra_c is None or dec_c is None:
        if not (ra and dec):
            flags.append("MISSING_RA_DEC")
        elif ALLOW_EXCLUDED_POSITIONS:
            ra_c = ra_c or _choose(ra, allow_excluded=True)
            dec_c = dec_c or _choose(dec, allow_excluded=True)
            flags.append("POSITION_FROM_EXCLUDED_SOURCE(%s)_NEEDS_APPROVAL" % _source_names(ra_c))
        else:
            flags.append("RA_DEC_ONLY_FROM_EXCLUDED_SOURCE(%s)" % _source_names(ra[0]))
    if len(ra) > 1 or len(dec) > 1:
        spread = max(abs(a["value"] - b["value"]) for a in ra for b in ra) if ra else 0
        flags.append("MULTIPLE_RA_DEC(spread %.4f deg)" % spread)

    # Distance candidates: direct distance statements and 1/parallax.
    candidates = []
    for s in dist:
        ly, sigma = _to_ly(s)
        if ly:
            candidates.append(dict(s, kind="distance", ly=ly, ly_sigma=sigma))
    for s in plx:
        if s["unit"] == UNIT_MAS and s["value"] > 0:
            ly = 1000.0 / s["value"] * LY_PER_PC
            sigma = None
            if s["sigma"]:
                sigma = ly * s["sigma"] / s["value"]
            candidates.append(dict(s, kind="parallax", ly=ly, ly_sigma=sigma))
    if not candidates:
        flags.append("MISSING_DISTANCE")

    # One consistent measurement for every star where possible: the parallax
    # statement that cites PREFERRED_PARALLAX_SOURCE (whatever its rank; on many
    # items a bot deprecated it when a Gaia value was added, not because it is
    # wrong). Otherwise the best non-excluded statement (parallax first when its
    # relative error is < 10 %, else a distance statement), deprecated allowed
    # as a last resort and flagged.
    chosen = None
    for c in candidates:
        if c["kind"] == "parallax" and not c["excluded_by_policy"] and \
                PREFERRED_PARALLAX_SOURCE in json.dumps(c["references"]):
            chosen = c
            break
    if chosen is None:
        plx_choice = _choose([c for c in candidates if c["kind"] == "parallax"])
        dist_choice = _choose([c for c in candidates if c["kind"] == "distance"])
        if plx_choice and plx_choice["sigma"] and plx_choice["sigma"] / plx_choice["value"] < 0.10:
            chosen = plx_choice
        elif dist_choice:
            chosen = dist_choice
        elif plx_choice:
            chosen = plx_choice
        else:
            chosen = _choose(candidates, allow_deprecated=True)
        if chosen is not None:
            flags.append("DISTANCE_NOT_FROM_PREFERRED_SOURCE")
    if chosen is not None and chosen["rank"] == "deprecated":
        flags.append("CHOSEN_DISTANCE_STATEMENT_IS_DEPRECATED_RANK")
    if candidates:
        values = [c["ly"] for c in candidates if not c["excluded_by_policy"]
                  and "General Catalogue of Trigonometric" not in json.dumps(c["references"])] \
                 or [c["ly"] for c in candidates]
        lo, hi = min(values), max(values)
        if len(values) > 1:
            flags.append("MULTIPLE_DISTANCE_VALUES(%d)" % len(values))
        if lo > 0 and hi / lo > 1.15:
            flags.append("DISTANCE_DISAGREEMENT(%.0f-%.0f ly, x%.2f)" % (lo, hi, hi / lo))
        if chosen and chosen.get("ly_sigma") and chosen["ly_sigma"] / chosen["ly"] > 0.10:
            flags.append("DISTANCE_UNCERTAINTY>10%%(%.0f%%)" % (100 * chosen["ly_sigma"] / chosen["ly"]))
        if chosen and not chosen.get("ly_sigma"):
            flags.append("DISTANCE_NO_UNCERTAINTY")
        if chosen and chosen["unreferenced"]:
            flags.append("CHOSEN_DISTANCE_UNREFERENCED")
        if any(c["excluded_by_policy"] for c in candidates):
            flags.append("SOME_DISTANCE_STATEMENTS_EXCLUDED_BY_SOURCE_POLICY")
        if chosen is None:
            flags.append("DISTANCE_ONLY_FROM_EXCLUDED_SOURCE")

    v_mags = [m for m in mag if _is_v_band(m)]
    mag_choice = _choose(v_mags)
    if mag and mag_choice is None:
        flags.append("V_MAGNITUDE_ONLY_FROM_EXCLUDED_SOURCE(%s)" % "; ".join(_source_names(m) for m in v_mags))
    if not mag:
        flags.append("MISSING_MAGNITUDE")
    elif len(v_mags) > 1 and (max(m["value"] for m in v_mags) - min(m["value"] for m in v_mags)) > 0.1:
        flags.append("MULTIPLE_V_MAGNITUDES")

    lowered = " ".join(i or "" for i in instance_of).lower()
    if any(word in lowered for word in ("binary", "multiple", "triple", "quadruple", "system", "double")) or has_parts:
        flags.append("STAR_SYSTEM(%s)" % ", ".join(i or "?" for i in instance_of))
    if any(word in lowered.replace("suspected variable", "") for word in ("variable", "cepheid", "eruptive")):
        flags.append("VARIABLE")

    def pick(c, extra=None):
        if c is None:
            return None
        out = {"statement_id": c["statement_id"], "rank": c["rank"], "value": c["value"],
               "unit": c["unit_label"] or c["unit"], "sigma": c["sigma"], "references": c["references"]}
        if extra:
            out.update(extra)
        return out

    return {
        "constellation": constellation_id,
        "id": star["id"],
        "name": name,
        "optional": optional,
        "qid": star["qid"],
        "wikidata_url": "https://www.wikidata.org/wiki/" + star["qid"],
        "wikidata_label": labels.get(star["qid"]),
        "instance_of": instance_of,
        "part_of": [{"qid": q, "label": labels.get(q)} for q in part_of],
        "has_part": [{"qid": q, "label": labels.get(q)} for q in has_parts],
        "ra_deg": pick(ra_c, {"epoch": ra_c["epoch"]} if ra_c else None),
        "dec_deg": pick(dec_c, {"epoch": dec_c["epoch"]} if dec_c else None),
        "distance": None if chosen is None else pick(chosen, {
            "kind": chosen["kind"], "ly": round(chosen["ly"], 2),
            "ly_sigma": None if chosen.get("ly_sigma") is None else round(chosen["ly_sigma"], 2)}),
        "apparent_magnitude": pick(mag_choice, {"filter": mag_choice["filter"]} if mag_choice else None),
        "all_distance_candidates": [
            {"kind": c["kind"], "statement_id": c["statement_id"], "rank": c["rank"],
             "raw": "%s %s" % (c["value"], c["unit_label"] or c["unit"]), "sigma": c["sigma"],
             "ly": round(c["ly"], 1), "excluded_by_policy": c["excluded_by_policy"],
             "sources": sorted({(s.get("label") or s.get("qid") or "?") for r in c["references"] for s in r.get("stated_in", [])}
                               | {u for r in c["references"] for u in r.get("url", [])})}
            for c in candidates],
        "all_magnitudes": [
            {"value": m["value"], "rank": m["rank"], "filter": [f["label"] or f["qid"] for f in m["filter"]],
             "excluded_by_policy": m["excluded_by_policy"]} for m in mag],
        "flags": flags,
    }


def normalize(args):
    config = load_config(args.config)
    entities, labels, info = _read_raw(args.raw)
    rows, alternates = [], []
    for constellation in config["constellations"]:
        for optional, stars in ((False, constellation["stars"]), (True, constellation.get("optionalStars", []))):
            for star in stars:
                row = normalize_star(star, entities.get(star["qid"]), labels, constellation["id"], optional)
                rows.append(row)
                for alt in star.get("alternates", []):
                    alt_row = normalize_star({"id": star["id"] + "@" + alt, "name": star["name"] + " (alternate item)", "qid": alt},
                                             entities.get(alt), labels, constellation["id"], True)
                    alternates.append(alt_row)
    table = {
        "schema": "cw-star-table/1",
        "source": "Wikidata (structured data, CC0 1.0) via " + info.get("api", API),
        "fetched_utc": info.get("fetched_utc"),
        "source_policy_excluded_reference_patterns": EXCLUDED_REFERENCE_PATTERNS,
        "stars": rows,
        "alternate_items": alternates,
    }
    with open(args.out, "w", encoding="utf-8") as handle:
        json.dump(table, handle, indent=2, ensure_ascii=False)
    for row in rows + alternates:
        d = row.get("distance") or {}
        m = row.get("apparent_magnitude") or {}
        ra = (row.get("ra_deg") or {}).get("value")
        dec = (row.get("dec_deg") or {}).get("value")
        print("%-11s %-30s %-10s RA %-10s Dec %-10s d %-8s ly (%s) m %-6s %s" % (
            row["constellation"] if "constellation" in row else "", row["name"][:30], row["qid"],
            "%.4f" % ra if ra is not None else "-", "%.4f" % dec if dec is not None else "-",
            d.get("ly", "-"), d.get("kind", "-"), m.get("value", "-"), "; ".join(row["flags"])))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="cmd", required=True)
    f = sub.add_parser("fetch")
    f.add_argument("--config", required=True)
    f.add_argument("--raw", required=True)
    n = sub.add_parser("normalize")
    n.add_argument("--config", required=True)
    n.add_argument("--raw", required=True)
    n.add_argument("--out", required=True)
    n.add_argument("--allow-excluded-positions", action="store_true",
                   help="prototype only: use RA/Dec cited only to an excluded source, flagged NEEDS_APPROVAL")
    args = parser.parse_args()
    global ALLOW_EXCLUDED_POSITIONS
    ALLOW_EXCLUDED_POSITIONS = getattr(args, "allow_excluded_positions", False)
    fetch(args) if args.cmd == "fetch" else normalize(args)


if __name__ == "__main__":
    main()
