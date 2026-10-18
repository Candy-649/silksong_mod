"""Lists the FSM action types with which SSMP's synced creatures look at a player, and whether SSMP points them at the
player the creature goes after: the action types that read a variable named like a player target (Hero, Target,
Target Object, TargetObject, Player) in some field, with how many actions do, per field, and a few creatures each.

SSMP replaces such a field with the player a creature goes after only for the types marked targeted_fsm or
targeted_where_player in SSMP/Resource/action-registry.json (GamePatcher.EnemyTargeting); any other type goes on
reading the FSM's own variable, which is the player of the game that runs the creature. A creature that goes after the
other player then faces one player and measures, chases or aims at the other.

Input is the output of FsmScan --params with every action and every combination printed (about three minutes):

  dotnet run -c Release --project tools/FsmScan -- --params "." --combinations 1000000 > params_all.txt
  py -I tools/aim_readers.py params_all.txt [--registry SSMP/SSMP/Resource/action-registry.json] [--all]

Each line: T for targeted_fsm, W for targeted_where_player, - for neither; the type, the field, the count of actions
and examples. Without --all only the types SSMP leaves alone are printed. Only the categories "entity" and
"entity-child" (FSMs of registered creatures and of their parts) are read. Which of them look at the player to aim,
and which act on the player itself (move, push, grab, take silk), is for the reader to decide: the latter must stay on
the player of each game.
"""
import argparse
import collections
import json
import re
import sys

TARGET_NAMES = {"hero", "target", "target object", "targetobject", "player"}
CATEGORIES = ("entity", "entity-child")
LINE = re.compile(r"^\s+(\d+)\s+(\w+): (.*?)\s+e\.g\. (.*)$")
FIELD_SPLIT = re.compile(r", (?=\w+=)")


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("params", help="the output of FsmScan --params \".\" --combinations 1000000")
    parser.add_argument("--registry", default=r"D:/programming/silksong_mod/SSMP/SSMP/Resource/action-registry.json")
    parser.add_argument("--all", action="store_true", help="also print the types SSMP points at the target")
    args = parser.parse_args()

    registry = json.load(open(args.registry, encoding="utf-8"))
    targeted = {entry["type"] for entry in registry if entry.get("targeted_fsm")}
    where_player = {entry["type"] for entry in registry if entry.get("targeted_where_player")}

    counts = collections.defaultdict(collections.Counter)
    examples = collections.defaultdict(set)
    category = None
    for line in open(args.params, encoding="utf-8"):
        if line.startswith("== "):
            category = line[3:].split(":")[0]
            continue

        if category not in CATEGORIES:
            continue

        match = LINE.match(line)
        if not match:
            continue

        count, action, params, found_in = int(match.group(1)), match.group(2), match.group(3), match.group(4)
        for part in FIELD_SPLIT.split(params):
            field, _, value = part.partition("=")
            if value.startswith("var ") and value[4:].strip().lower() in TARGET_NAMES:
                counts[action][field] += count
                examples[(action, field)].update(found_in.split("; ")[:3])

    rows = []
    for action, fields in counts.items():
        mark = "T" if action in targeted else "W" if action in where_player else "-"
        if mark != "-" and not args.all:
            continue

        for field, count in fields.items():
            rows.append((mark, action, field, count, sorted(examples[(action, field)])[:4]))

    rows.sort(key=lambda row: (row[0], -row[3]))
    for mark, action, field, count, found_in in rows:
        print(f"{mark} {action:40s} {field:22s} {count:6d}  {'; '.join(found_in)}")


if __name__ == "__main__":
    main()
