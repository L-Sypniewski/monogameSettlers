#!/usr/bin/env python3
"""AI trait tournament: AI characters and trait combinations against each other, without a window.

Every pairing plays on every map with every people, both players of the same people (so the people
does not decide), and each pairing once from either start position. A game ends when only one player
can still fight (military building or soldier) or after --minutes; then the land share tells who was
ahead. Results go to a CSV (one row per player and game); runs can be interrupted and resumed, games
already in the CSV are skipped.

  tools/trait-tournament.py run    [--preset traits|peoples] [--entrants a,b,...] [--against x,y] [--vary path=v1/v2 ...]
                                   [--maps ...] [--peoples ...] [--seeds 1,2] [--minutes 90] [--jobs N] [--self] [--out file.csv]
  tools/trait-tournament.py report [--preset ...] [--out file.csv]

Entrants are characters from data/ai.json, traits joined by '+' (pincer+guardByThreat), either with
@path=value settings on top (standard@attack.ratio=1.2@military.strikeRange=40, see ai.json). A
people in front (egyptian:hothead) lets the entrant play only with that people.
--against: the entrants play only against these (both start positions), not each other.
--vary: every entrant once per value, e.g. --vary attack.ratio=1.2/1.5/2 --vary military.strikeRange=40/100
(several --vary: every combination).
Presets (the defaults for entrants, opponents and output file):
  traits   every trait combination and the general against each other (all peoples)
  peoples  each people's character against standard and general of its own people
Examples:
  tools/trait-tournament.py run --preset peoples
  tools/trait-tournament.py run --entrants egyptian:hothead --against standard --vary attack.commit=0/0.5/1 --out tournament-results/commit.csv
"""
import argparse
import csv
import itertools
import os
import subprocess
import sys
import tempfile
import threading
import time
from collections import defaultdict
from concurrent.futures import ThreadPoolExecutor, as_completed

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ARENA_PROJECT = os.path.join(ROOT, "tools", "Homestead.Arena")
ARENA_DLL = os.path.join(ARENA_PROJECT, "bin", "Release", "net9.0", "Homestead.Arena.dll")
RESULTS = os.path.join(ROOT, "tournament-results")

PRESETS = {
    "traits": {"entrants": ["standard", "pincer", "guardByThreat", "pincer+guardByThreat", "general"], "against": []},
    "peoples": {"entrants": ["egyptian:hothead", "asian:unyielding", "roman:legion", "amazon:skirmisher"],
                "against": ["standard", "general"]},
}
# Small two-player maps without start buildings that leave the peoples open (M256-2-Quick fixes Romans,
# on M320-2-Discovery the economy stalls).
DEFAULT_MAPS = ["A256-2-Quick3", "M256-2-Quick-2", "M320-2-Hearts", "384-2-Lakes"]
DEFAULT_PEOPLES = ["roman", "egyptian", "asian", "amazon"]


def split(text):
    return [part.strip() for part in text.split(",") if part.strip()]


def entrant(spec):
    """(people or None, character id) of an entrant like 'egyptian:hothead'."""
    people, _, character = spec.rpartition(":")
    return (people.lower() or None), character


def varied(entrants, variations):
    """Every entrant once per combination of the --vary values."""
    if not variations:
        return entrants
    axes = []
    for variation in variations:
        path, _, values = variation.partition("=")
        axes.append([f"@{path}={v}" for v in values.split("/") if v])
    return [e + "".join(combo) for e in entrants for combo in itertools.product(*axes)]


def game_tag(map_name, people, first, second, seed, minutes):
    return f"{map_name}|{people}|{first}|{second}|{seed}|{minutes}"


def read_rows(path):
    if not os.path.exists(path):
        return []
    with open(path, newline="") as f:
        return list(csv.DictReader(f))


def run(args):
    preset = PRESETS[args.preset]
    entrants = varied(split(args.entrants) if args.entrants else preset["entrants"], args.vary)
    against = split(args.against) if args.against is not None else preset["against"]
    maps = split(args.maps)
    peoples = split(args.peoples)
    seeds = [int(s) for s in split(args.seeds)]

    def plays(spec, people):
        own, _ = entrant(spec)
        return own is None or own == people.lower()

    games = []
    for people in peoples:
        mine = [entrant(e)[1] for e in entrants if plays(e, people)]
        others = [entrant(e)[1] for e in against if plays(e, people)]
        if against:
            pairs = {pair for a in mine for b in others if a != b for pair in ((a, b), (b, a))}
        else:
            pairs = {(a, b) for a, b in itertools.product(mine, repeat=2) if a != b or args.self}
        games += [(m, people, a, b, s) for m in maps for (a, b) in sorted(pairs) for s in seeds]

    out = os.path.abspath(args.out)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    logs = os.path.join(os.path.dirname(out), "logs")
    os.makedirs(logs, exist_ok=True)
    done = {row["tag"] for row in read_rows(out)}
    todo = [g for g in games if game_tag(*g, args.minutes) not in done]
    print(f"{len(games)} games ({len(entrants)} entrants, {len(against)} opponents, {len(maps)} maps, {len(peoples)} peoples, {len(seeds)} seeds), "
          f"{len(games) - len(todo)} already in {out}, {len(todo)} to play with {args.jobs} jobs", flush=True)
    if not todo:
        return

    if not args.no_build:
        build = subprocess.run(["dotnet", "build", ARENA_PROJECT, "-c", "Release", "-v", "q", "-nologo"], cwd=ROOT)
        if build.returncode != 0:
            sys.exit("build failed")

    lock = threading.Lock()
    started = time.time()

    def play(game):
        map_name, people, first, second, seed = game
        tag = game_tag(*game, args.minutes)
        safe = tag.replace("|", "_").replace("+", "-")
        with tempfile.TemporaryDirectory() as tmp:
            part = os.path.join(tmp, "game.csv")
            command = ["dotnet", ARENA_DLL, map_name, "--minutes", str(args.minutes), "--every", "15",
                       "--peoples", people, "--characters", f"{first},{second}", "--seed", str(seed),
                       "--until-decided", "--csv", part, "--tag", tag]
            with open(os.path.join(logs, safe + ".log"), "w") as log:
                result = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
            if result.returncode != 0 or not os.path.exists(part):
                return game, None
            rows = read_rows(part)
        if any(r["people"].lower() != people.lower() for r in rows):
            print(f"   warning: {map_name} fixes peoples ({', '.join(r['people'] for r in rows)}) – not a mirror match", flush=True)
        with lock:
            new = not os.path.exists(out) or os.path.getsize(out) == 0
            with open(out, "a", newline="") as f:
                writer = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
                if new:
                    writer.writeheader()
                writer.writerows(rows)
        return game, rows

    finished = 0
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        for future in as_completed([pool.submit(play, g) for g in todo]):
            game, rows = future.result()
            finished += 1
            elapsed = time.time() - started
            eta = elapsed / finished * (len(todo) - finished)
            if rows is None:
                summary = "FAILED (see log)"
            else:
                summary = ", ".join(f"P{r['player']} {r['character']} {r['result']} ({float(r['land_share']):.0%})" for r in rows)
                summary += f", {rows[0]['end_minute']} min"
            print(f"[{finished}/{len(todo)}, eta {eta / 60:.0f} min] {game[0]} {game[1]}: {summary}", flush=True)
    print(f"done in {(time.time() - started) / 60:.1f} min -> {out}")


def points(row):
    """1 for a win, 0 for a loss; an open game counts the land share."""
    return {"win": 1.0, "loss": 0.0}.get(row["result"], float(row["land_share"]))


def report(args):
    rows = read_rows(args.out)
    if not rows:
        sys.exit(f"no results in {args.out}")
    games = len({r["tag"] for r in rows})
    print(f"{games} games from {args.out}\n")
    print("points: win 1, loss 0, open game = land share at the end\n")

    def table(title, key):
        stats = defaultdict(lambda: [0, 0, 0, 0, 0.0, 0.0])  # games, wins, losses, open, points, minutes of decided games
        for r in rows:
            s = stats[key(r)]
            s[0] += 1
            s[1] += r["result"] == "win"
            s[2] += r["result"] == "loss"
            s[3] += r["result"] == "open"
            s[4] += points(r)
            if r["result"] == "win":
                s[5] += float(r["end_minute"])
        width = max(len(str(k)) for k in stats) + 2
        print(f"{title:<{width}} games  wins losses  open  points  win rate  avg min to win")
        for k, (n, w, l, o, p, m) in sorted(stats.items(), key=lambda kv: -kv[1][4] / kv[1][0]):
            print(f"{str(k):<{width}} {n:5} {w:5} {l:6} {o:5}  {p / n:6.3f}  {w / n:8.1%}  {m / w if w else 0:14.1f}")
        print()

    table("character", lambda r: r["character"])
    table("character / people", lambda r: f"{r['character']} / {r['people']}")
    table("character / map", lambda r: f"{r['character']} / {r['map']}")
    table("start position", lambda r: f"P{r['player']}")

    characters = sorted({r["character"] for r in rows})
    cell = defaultdict(lambda: [0, 0.0])
    for r in rows:
        if "|" in r["opponents"]:
            continue  # only one-on-one games in the matrix
        c = cell[(r["character"], r["opponents"])]
        c[0] += 1
        c[1] += points(r)
    width = max(len(c) for c in characters) + 2
    print("matchups (points of the row against the column, games in brackets)")
    print(" " * width + "".join(f"{c[:18]:>20}" for c in characters))
    for a in characters:
        line = f"{a:<{width}}"
        for b in characters:
            n, p = cell[(a, b)]
            line += f"{'-' if n == 0 else f'{p / n:.2f} ({n})':>20}"
        print(line)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    r = sub.add_parser("run", help="play the games (skips those already in the CSV)")
    r.add_argument("--preset", choices=PRESETS, default="traits")
    r.add_argument("--entrants", help="default: the preset's")
    r.add_argument("--against", help="opponents; default: the preset's (traits: none, everyone against everyone)")
    r.add_argument("--vary", action="append", default=[], metavar="PATH=V1/V2")
    r.add_argument("--maps", default=",".join(DEFAULT_MAPS))
    r.add_argument("--peoples", default=",".join(DEFAULT_PEOPLES))
    r.add_argument("--seeds", default="1")
    r.add_argument("--minutes", type=int, default=90)
    r.add_argument("--jobs", type=int, default=max(1, (os.cpu_count() or 2) // 2))
    r.add_argument("--self", action="store_true", help="also let each entrant play against itself")
    r.add_argument("--no-build", action="store_true")
    r.add_argument("--out")
    p = sub.add_parser("report", help="summarize the CSV")
    p.add_argument("--preset", choices=PRESETS, default="traits")
    p.add_argument("--out")
    args = parser.parse_args()
    args.out = args.out or os.path.join(RESULTS, f"{args.preset}.csv")
    run(args) if args.command == "run" else report(args)


if __name__ == "__main__":
    main()
