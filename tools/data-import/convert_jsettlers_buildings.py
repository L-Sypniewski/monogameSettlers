#!/usr/bin/env python3
"""Convert JSettlers building definitions (MIT) into Homestead's JSON building data.

Usage: convert_jsettlers_buildings.py <settlers-remake checkout> <output dir>

Reads jsettlers.common/src/main/resources/jsettlers/common/buildings/<civ>/*.xml and writes
<output dir>/<civ>.json. Positions are relative to the building's anchor point (dx, dy in grid
units, same axes as the map grid). Run again to refresh; manual changes belong in a separate
override file, not in the generated JSON.
"""
import json
import os
import sys
import xml.etree.ElementTree as ET

POSITION_LISTS = {"workcenter", "smokePosition", "healspot", "pigFeedPosition", "donkeyFeedPosition",
                  "sawmillerWorkPosition", "ovenPosition", "animalPosition", "moltenMetal", "meltInput",
                  "meltOutput", "anvilPosition", "smithDropPosition", "bricklayer", "buildmark"}


def pos(e):
    return [int(e.get("dx")), int(e.get("dy"))]


def convert(path):
    root = ET.parse(path).getroot()
    b = {
        "worker": root.get("worker") or None,
        "ground": [g.get("groundtype") for g in root.findall("ground")],
        "blocked": [pos(e) for e in root.findall("blocked") if e.get("block", "true") == "true"],
        "protected": [pos(e) for e in root.findall("blocked") if e.get("block", "true") == "false"],
        "door": pos(root.find("door")),
        "flag": pos(root.find("flag")) if root.find("flag") is not None else None,
        "constructionStacks": [{"at": pos(e), "material": e.get("material"), "required": int(e.get("buildrequired"))}
                               for e in root.findall("constructionStack")],
        "requestStacks": [{"at": pos(e), "material": e.get("material")} for e in root.findall("requestStack")],
        "offerStacks": [{"at": pos(e), "material": e.get("material")} for e in root.findall("offerStack")],
        "images": {},
        "positions": {},
    }
    for key in ("workradius", "viewdistance", "mine"):
        if root.get(key) is not None:
            b[key] = root.get(key)
    for img in root.findall("image"):
        b["images"].setdefault(img.get("for").lower(), []).append(
            {"file": int(img.get("file", "-1")), "sequence": int(img.get("sequence", "-1")),
             "image": int(img.get("image")), "type": img.get("type")})
    for child in root:
        if child.tag in POSITION_LISTS:
            entry = {k: (int(v) if k in ("dx", "dy") else v) for k, v in child.attrib.items()}
            b["positions"].setdefault(child.tag, []).append(entry)
    return b


def main(checkout, out_dir):
    base = os.path.join(checkout, "jsettlers.common/src/main/resources/jsettlers/common/buildings")
    for civ in sorted(d for d in os.listdir(base) if os.path.isdir(os.path.join(base, d))):
        buildings = {}
        for name in sorted(os.listdir(os.path.join(base, civ))):
            if name.endswith(".xml"):
                buildings[name[:-4]] = convert(os.path.join(base, civ, name))
        data = {
            "_source": "Converted from JSettlers (MIT, https://github.com/paulwedeck/settlers-remake) "
                       "by tools/data-import/convert_jsettlers_buildings.py",
            "buildings": buildings,
        }
        with open(os.path.join(out_dir, civ + ".json"), "w") as f:
            json.dump(data, f, indent=1)
        print(civ, len(buildings))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
