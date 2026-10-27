#!/usr/bin/env python3

# Standard Library
import argparse
import re
import sys
from pathlib import Path
from typing import Any

VERSION = "6.0"

parser = argparse.ArgumentParser(description="Log file parser for continuous spawning logs.", formatter_class=argparse.ArgumentDefaultsHelpFormatter)
parser.add_argument("logs", nargs='*', help="Log files to parse. If none are given, the latest log file is parsed.")
parser.add_argument("-n", "--no-file", action='store_true', help="Don't create a csv file.")
parser.add_argument("-w", "--weights", type=str, default="3,1.5,-1,4e-3,1e-4,4e-5,0.01,5e-4,1e-4,4e-5,0.15,2e-3,3e-5,1.5e-5,0.075,0,0,0", help="Score weights.")
parser.add_argument("--show-weights", action='store_true', help="Show the score weights.")
parser.add_argument("-s", "--separately", action='store_true', help="Show the results of each log separately (for multiple logs).")
parser.add_argument("-a", "--all", action='store_true', help="Glob all the cts-* files in the folder as input.")
parser.add_argument("--plot", type=str, nargs='?', const='tmp', help="Plot a graph of the scores over time and save it to the specified file.")
parser.add_argument("--version", action='store_true', help="Show the script version, then exit.")
args = parser.parse_args()

if args.version:
    print(f"Version: {VERSION}")
    sys.exit()

log_dir = Path(__file__).parent.parent / "Logs" if len(args.logs) == 0 else Path('.')

fields = ["kills", "assists", "deaths", "hits", "bullet damage", "bullet damage taken", "rocket strikes", "rocket parts hit", "rocket damage",
    "rocket damage taken", "missile strikes", "missile parts hit", "missile damage", "missile damage taken", "rammed parts", "parts lost to asteroids", "accuracy", "rocket accuracy"]
fields_short = {field: field_short for field, field_short in zip(fields, ["Kills", "Assists", "Deaths", "Hits", "Damage", "DmgTkn", "RktHits", "RktParts", "RktDmg", "RktDmgTkn", "MisHits", "MisParts", "MisDmg", "MisDmgTkn", "Ram", "Asteroids", "Acc%", "RktAcc%"])}
try:
    weights = {field: float(w) for field, w in zip(fields, args.weights.split(','))}
    if len(weights) != len(fields):
        raise ValueError("Invalid number of weights.")
except:
    raise ValueError("Failed to parse input weights")

if args.show_weights:
    field_width = max(len(f) for f in fields)
    for f, w in weights.items():
        print(f"{f}:{' ' * (field_width - len(f))} {w}")
    sys.exit()

if len(args.logs) == 1 and Path(args.logs[0]).is_dir():  # We were passed a folder instead of log files -> treat it as the log_dir.
    log_dir = Path(args.logs[0])
    args.logs = []
if len(args.logs) > 0:
    competition_files = [Path(filename) for filename in args.logs if filename.endswith(".log")]
else:
    competition_files = list(sorted(list(log_dir.glob("cts-*.log"))))
    if not args.all and len(competition_files) > 0:
        competition_files = competition_files[-1:]

data: dict[Path, dict[str, dict[str, Any]]] = {}
for filename in competition_files:
    with open(filename, "r", encoding="utf-8") as file_data:
        data[filename] = {}
        Craft_Name = ""
        for line in file_data:
            if not "BDArmory.VesselSpawner" in line:  # Identifier for continuous spawn logs.
                continue
            if " Name:" in line:  # Next craft
                Craft_Name = line.split(" Name:")[-1].replace("\n", "")
                data[filename][Craft_Name] = {"kills": 0, "assists": 0, "deaths": 0, "hits": 0, "bullet damage": 0, "acc hits": 0, "shots": 0, "accuracy": 0, "rocket strikes": 0, "rocket parts hit": 0,
                    "rocket damage": 0, "acc rocket strikes": 0, "rockets fired": 0, "rocket accuracy": 0, "missile strikes": 0, "missile parts hit": 0, "missile damage": 0, "score": 0, "damage/spawn": 0}
            elif " DEATHCOUNT:" in line:  # Counts up deaths
                data[filename][Craft_Name]["deaths"] = int(line.split("DEATHCOUNT:")[-1].replace("\n", ""))
            elif " DEATHTIMES:" in line:  # Record time-of-death
                data[filename][Craft_Name]["death times"] = {int(life): float(time) for life, _, time in (entry.split(':') for entry in line.split("DEATHTIMES:")[-1].split(";"))}
            elif (m := re.match(".*CLEAN[^:]*:", line)) is not None:  # Counts up clean kills, frags, explodes and rams
                killedby = {int(nr): killer for nr, killer in (cleanKill.split(":") for cleanKill in m.string[m.end():].replace("\n", "").split(", "))}
                if "killed by" in data[filename][Craft_Name]:
                    data[filename][Craft_Name]["killed by"].update(killedby)
                else:
                    data[filename][Craft_Name]["killed by"] = killedby  # {death_nr: killer}
            elif " GMKILL:" in line:  # GM kills
                data[filename][Craft_Name]["GM kills"] = {int(nr): killer for nr, killer in (gmKill.split(":") for gmKill in line.split(":  GMKILL:")[-1].replace("\n", "").split(", ")) if killer not in ("LandedTooLong", "Asteroids")}
            elif " WHOSHOTME:" in line:  # Counts up hits
                data[filename][Craft_Name]["shot by"] = {int(life): {by: int(hits) for hits, by in (entry.split(":", 1) for entry in hitsby.split(";"))}
                                                                  for life, hitsby in (entry.split(":", 1) for entry in line.split(":  WHOSHOTME:")[-1].replace("\n", "").split(", "))}
            elif " WHOSTRUCKMEWITHROCKETS:" in line:  # Counts up hits
                data[filename][Craft_Name]["rocket strike by"] = {int(life): {by: int(hits) for hits, by in (entry.split(":", 1) for entry in hitsby.split(";"))}
                                                                           for life, hitsby in (entry.split(":", 1) for entry in line.split(":  WHOSTRUCKMEWITHROCKETS:")[-1].replace("\n", "").split(", "))}
            elif " WHOSTRUCKMEWITHMISSILES:" in line:  # Counts up hits
                data[filename][Craft_Name]["missile strike by"] = {int(life): {by: int(hits) for hits, by in (entry.split(":", 1) for entry in hitsby.split(";"))}
                                                                            for life, hitsby in (entry.split(":", 1) for entry in line.split(":  WHOSTRUCKMEWITHMISSILES:")[-1].replace("\n", "").split(", "))}
            elif " WHOPARTSHITMEWITHROCKETS:" in line:  # Counts up parts hit
                data[filename][Craft_Name]["rocket parts hit by"] = {int(life): {by: int(parts) for parts, by in (entry.split(":", 1) for entry in partshitby.split(";"))}
                                                                              for life, partshitby in (entry.split(":", 1) for entry in line.split(":  WHOPARTSHITMEWITHROCKETS:")[-1].replace("\n", "").split(", "))}
            elif " WHOPARTSHITMEWITHMISSILES:" in line:  # Counts up parts hit
                data[filename][Craft_Name]["missile parts hit by"] = {int(life): {by: int(parts) for parts, by in (entry.split(":", 1) for entry in partshitby.split(";"))}
                                                                               for life, partshitby in (entry.split(":", 1) for entry in line.split(":  WHOPARTSHITMEWITHMISSILES:")[-1].replace("\n", "").split(", "))}
            elif " WHODAMAGEDMEWITHBULLETS:" in line:  # Counts up damage
                data[filename][Craft_Name]["bullet damage by"] = {int(life): {by: float(damage) for damage, by in (entry.split(":", 1) for entry in damageby.split(";"))}
                                                                           for life, damageby in (entry.split(":", 1) for entry in line.split(":  WHODAMAGEDMEWITHBULLETS:")[-1].replace("\n", "").split(", "))}
            elif " WHODAMAGEDMEWITHROCKETS:" in line:  # Counts up damage
                data[filename][Craft_Name]["rocket damage by"] = {int(life): {by: float(damage) for damage, by in (entry.split(":", 1) for entry in damageby.split(";"))}
                                                                           for life, damageby in (entry.split(":", 1) for entry in line.split(":  WHODAMAGEDMEWITHROCKETS:")[-1].replace("\n", "").split(", "))}
            elif " WHODAMAGEDMEWITHMISSILES:" in line:  # Counts up damage
                data[filename][Craft_Name]["missile damage by"] = {int(life): {by: float(damage) for damage, by in (entry.split(":", 1) for entry in damageby.split(";"))}
                                                                            for life, damageby in (entry.split(":", 1) for entry in line.split(":  WHODAMAGEDMEWITHMISSILES:")[-1].replace("\n", "").split(", "))}
            elif " WHORAMMEDME:" in line:  # Counts up rams
                data[filename][Craft_Name]["rammed by"] = {int(life): {by: int(parts) for parts, by in (entry.split(":", 1) for entry in partshitby.split(";"))}
                                                                    for life, partshitby in (entry.split(":", 1) for entry in line.split(":  WHORAMMEDME:")[-1].replace("\n", "").split(", "))}
            elif " PARTSLOSTTOASTEROIDS:" in line:  # Count up parts
                data[filename][Craft_Name]["parts lost to asteroids"] = {int(life): int(partsLost) for life, partsLost in (entry.split(":", 1) for entry in line.split(":  PARTSLOSTTOASTEROIDS:")[-1].replace("\n", "").split(", "))}
            elif " ACCURACY:" in line:
                for item in line.split(" ACCURACY:")[-1].replace("\n", "").split(","):
                    _, hits, shots, rocketStrikes, rocketsFired = re.split('[:/]', item)
                    data[filename][Craft_Name]["acc hits"] += int(hits)
                    data[filename][Craft_Name]["shots"] += int(shots)
                    data[filename][Craft_Name]["acc rocket strikes"] += int(rocketStrikes)
                    data[filename][Craft_Name]["rockets fired"] += int(rocketsFired)
                data[filename][Craft_Name]["accuracy"] = 100 * data[filename][Craft_Name]["acc hits"] / data[filename][Craft_Name]["shots"] if data[filename][Craft_Name]["shots"] > 0 else 0
                data[filename][Craft_Name]["rocket accuracy"] = 100 * data[filename][Craft_Name]["acc rocket strikes"] / data[filename][Craft_Name]["rockets fired"] if data[filename][Craft_Name]["rockets fired"] > 0 else 0

        for Craft_Name in data[filename]:
            data[filename][Craft_Name]["hits"] = sum(hitby[life][Craft_Name] for hitby in (data[filename][other]["shot by"] for other in data[filename]
                                                          if other != Craft_Name and "shot by" in data[filename][other]) for life in hitby if Craft_Name in hitby[life])
            data[filename][Craft_Name]["rocket strikes"] = sum(hitby[life][Craft_Name] for hitby in (data[filename][other]["rocket strike by"] for other in data[filename]
                                                                    if other != Craft_Name and "rocket strike by" in data[filename][other]) for life in hitby if Craft_Name in hitby[life])
            data[filename][Craft_Name]["missile strikes"] = sum(hitby[life][Craft_Name] for hitby in (data[filename][other]["missile strike by"] for other in data[filename]
                                                                     if other != Craft_Name and "missile strike by" in data[filename][other]) for life in hitby if Craft_Name in hitby[life])

            data[filename][Craft_Name]["rocket parts hit"] = sum(partshitby[life][Craft_Name] for partshitby in (data[filename][other]["rocket parts hit by"] for other in data[filename]
                                                                      if other != Craft_Name and "rocket parts hit by" in data[filename][other]) for life in partshitby if Craft_Name in partshitby[life])
            data[filename][Craft_Name]["missile parts hit"] = sum(partshitby[life][Craft_Name] for partshitby in (data[filename][other]["missile parts hit by"]
                                                                       for other in data[filename] if other != Craft_Name and "missile parts hit by" in data[filename][other]) for life in partshitby if Craft_Name in partshitby[life])

            data[filename][Craft_Name]["bullet damage"] = sum(damageby[life][Craft_Name] for damageby in (data[filename][other]["bullet damage by"] for other in data[filename]
                                                                   if other != Craft_Name and "bullet damage by" in data[filename][other]) for life in damageby if Craft_Name in damageby[life])
            data[filename][Craft_Name]["rocket damage"] = sum(damageby[life][Craft_Name] for damageby in (data[filename][other]["rocket damage by"] for other in data[filename]
                                                                   if other != Craft_Name and "rocket damage by" in data[filename][other]) for life in damageby if Craft_Name in damageby[life])
            data[filename][Craft_Name]["missile damage"] = sum(damageby[life][Craft_Name] for damageby in (data[filename][other]["missile damage by"] for other in data[filename]
                                                                    if other != Craft_Name and "missile damage by" in data[filename][other]) for life in damageby if Craft_Name in damageby[life])

            data[filename][Craft_Name]["bullet damage taken"] = sum(damage for damageby in data[filename][Craft_Name]['bullet damage by'].values() for damage in damageby.values()) if 'bullet damage by' in data[filename][Craft_Name] else 0
            data[filename][Craft_Name]["rocket damage taken"] = sum(damage for damageby in data[filename][Craft_Name]['rocket damage by'].values() for damage in damageby.values()) if 'rocket damage by' in data[filename][Craft_Name] else 0
            data[filename][Craft_Name]["missile damage taken"] = sum(damage for damageby in data[filename][Craft_Name]['missile damage by'].values()
                                                                          for damage in damageby.values()) if 'missile damage by' in data[filename][Craft_Name] else 0

            data[filename][Craft_Name]["rammed parts"] = sum(partshitby[life][Craft_Name] for partshitby in (data[filename][other]["rammed by"] for other in data[filename]
                                                                  if other != Craft_Name and "rammed by" in data[filename][other]) for life in partshitby if Craft_Name in partshitby[life])

            data[filename][Craft_Name]["damage/spawn"] = (data[filename][Craft_Name]["bullet damage"] + data[filename][Craft_Name]["rocket damage"] +
                                                               data[filename][Craft_Name]["missile damage"]) / (1 + data[filename][Craft_Name]["deaths"])

            data[filename][Craft_Name]["kills"] = sum(1 for kill in (data[filename][other]["killed by"] for other in data[filename] if other !=
                                                           Craft_Name and "killed by" in data[filename][other]) for life in kill if Craft_Name == kill[life])
            data[filename][Craft_Name]["parts lost to asteroids"] = sum(data[filename][Craft_Name]["parts lost to asteroids"].values()) if "parts lost to asteroids" in data[filename][Craft_Name] else 0

            # Aggregate the damagers for computing assists later.
            data[filename][Craft_Name]['damaged by'] = {}
            if 'bullet damage by' in data[filename][Craft_Name]:
                data[filename][Craft_Name]['damaged by'] = {k: set(v) for k, v in data[filename][Craft_Name]['bullet damage by'].items() if k < data[filename][Craft_Name]['deaths']}
            if 'rocket damage by' in data[filename][Craft_Name]:
                for k, v in data[filename][Craft_Name]['rocket damage by'].items():
                    if k < data[filename][Craft_Name]['deaths']:
                        if k in data[filename][Craft_Name]['damaged by']:
                            data[filename][Craft_Name]['damaged by'][k] = data[filename][Craft_Name]['damaged by'][k].union(set(v))
                        else:
                            data[filename][Craft_Name]['damaged by'][k] = set(v)
            if 'missile damage by' in data[filename][Craft_Name]:
                for k, v in data[filename][Craft_Name]['missile damage by'].items():
                    if k < data[filename][Craft_Name]['deaths']:
                        if k in data[filename][Craft_Name]['damaged by']:
                            data[filename][Craft_Name]['damaged by'][k] = data[filename][Craft_Name]['damaged by'][k].union(set(v))
                        else:
                            data[filename][Craft_Name]['damaged by'][k] = set(v)
            if 'rammed by' in data[filename][Craft_Name]:
                for k, v in data[filename][Craft_Name]['rammed by'].items():
                    if k < data[filename][Craft_Name]['deaths']:
                        if k in data[filename][Craft_Name]['damaged by']:
                            data[filename][Craft_Name]['damaged by'][k] = data[filename][Craft_Name]['damaged by'][k].union(set(v))
                        else:
                            data[filename][Craft_Name]['damaged by'][k] = set(v)

            # Sanity check
            if data[filename][Craft_Name]["hits"] != data[filename][Craft_Name]["acc hits"]:
                print(f"Warning: inconsistency in hit counting {data[filename][Craft_Name]['hits']} vs {data[filename][Craft_Name]['acc hits']} for log {filename}")
            if data[filename][Craft_Name]["rocket strikes"] != data[filename][Craft_Name]["acc rocket strikes"]:
                print(f"Warning: inconsistency in rocket strike counting {data[filename][Craft_Name]['rocket strikes']} vs {data[filename][Craft_Name]['acc rocket strikes']} for log {filename}")

        # Compute assists and scores.
        for Craft_Name in data[filename]:
            data[filename][Craft_Name]["assists"] = sum(1 for other in data[filename] for life, damagedby in data[filename][other]['damaged by'].items() if Craft_Name in damagedby and not (
                'killed by' in data[filename][other] and life in data[filename][other]['killed by']) and not ('GM kills' in data[filename][other] and life in data[filename][other]['GM kills']))

            data[filename][Craft_Name]["score"] = sum(weights[field] * data[filename][Craft_Name][field] for field in fields)

if len(data) > 0:
    # Write results to console
    if args.separately:
        for filename, summary in data.items():
            print(f"Results for {filename}:")
            name_length = max([len(craft) for craft in summary])
            field_lengths = {field: max(len(fields_short[field]) + 2, 8) for field in fields}
            fields_to_show = [field for field in fields if not all(summary[craft][field] == 0 for craft in summary)]
            print(f"Name{' ' * (name_length - 4)}     score" + "".join(f"{fields_short[field]:>{field_lengths[field]}}" for field in fields_to_show))
            for craft in sorted(summary, key=lambda c: summary[c]["score"], reverse=True):
                print(f"{craft}{' ' * (name_length - len(craft))}  {summary[craft]['score']:8.2f}" +
                      "".join(f"{summary[craft][field]:>{field_lengths[field]}.0f}" if 'accuracy' not in field else f"{summary[craft][field]:>{field_lengths[field] - 1}.1f}%" for field in fields_to_show))
            print("")

            if not args.no_file:
                # Write results to file
                with open(log_dir / f"results-{Path(filename).stem}.csv", "w", encoding="utf-8") as results_data:
                    results_data.write("Name,Score," + ",".join(fields_to_show) + "\n")
                    for craft in sorted(summary, key=lambda c: summary[c]["score"], reverse=True):
                        results_data.write(f"{craft},{summary[craft]['score']:.2f}," + ",".join(f"{summary[craft][field]:.2f}" for field in fields_to_show) + "\n")
    else:
        # Merge the results from each log into a single summary.
        summary = {}
        for filename, entry in data.items():
            for craft, results in entry.items():
                if craft not in summary:
                    summary[craft] = {f: results[f] for f in fields + ['acc hits', 'shots', 'acc rocket strikes', 'rockets fired', 'score']}
                else:
                    for f in fields + ['acc hits', 'shots', 'acc rocket strikes', 'rockets fired', 'score']:
                        summary[craft][f] = summary[craft].get(f, 0) + results[f]
        for craft, entry in summary.items():
            entry['accuracy'] = entry['hits'] / entry['shots'] * 100 if entry['shots'] > 0 else 0
            entry['rocket accuracy'] = entry['rocket strikes'] / entry['rockets fired'] * 100 if entry['rockets fired'] > 0 else 0

        name_length = max([len(craft) for craft in summary])
        field_lengths = {field: max(len(fields_short[field]) + 2, 8) for field in fields}
        fields_to_show = [field for field in fields if not all(summary[craft][field] == 0 for craft in summary)]
        print(f"Name{' ' * (name_length - 4)}     score" + "".join(f"{fields_short[field]:>{field_lengths[field]}}" for field in fields_to_show))
        for craft in sorted(summary, key=lambda c: summary[c]["score"], reverse=True):
            print(f"{craft}{' ' * (name_length - len(craft))}  {summary[craft]['score']:8.2f}" +
                  "".join(f"{summary[craft][field]:>{field_lengths[field]}.0f}" if 'accuracy' not in field else f"{summary[craft][field]:>{field_lengths[field] - 1}.1f}%" for field in fields_to_show))

        if not args.no_file:
            # Write results to file
            with open(log_dir / f"results.csv", "w", encoding="utf-8") as results_data:
                results_data.write("Name,Score," + ",".join(fields_to_show) + "\n")
                for craft in sorted(summary, key=lambda c: summary[c]["score"], reverse=True):
                    results_data.write(f"{craft},{summary[craft]['score']:.2f}," + ",".join(f"{summary[craft][field]:.2f}" for field in fields_to_show) + "\n")
    if args.plot and len(competition_files) == 1:  # plot graph of score vs time for cts spawn with new node whenever someone dies
        import subprocess

        import matplotlib.pyplot as plt
        import numpy

        cdata = data[competition_files[0]]
        names = [name for name, _ in sorted({name: cdata[name]['score'] for name in cdata}.items(), key=lambda item: item[1], reverse=True)]  # Sorted by final score
        times: list[float] = sorted(t for name in names for t in cdata[name]['death times'].values())
        scores: dict[str, dict[float, float]] = {name: {} for name in names}
        base_fields = ["death times", "killed by", "GM kills", "shot by", "rocket strike by", "missile strike by", "rocket parts hit by", "missile parts hit by", "bullet damage by", "rocket damage by", "missile damage by", "rammed by", "parts los to asteroids"]
        base_data = {name: {f: craft_data[f] for f in base_fields if f in craft_data} for name, craft_data in cdata.items()}
        for cutoff in times:
            lives = {name: [l for l, v in base_data[name]['death times'].items() if v <= cutoff] for name in names}
            tdata: dict[str, dict[str, int | float | dict[int, Any]]] = {name: {f: {l: d for l, d in v.items() if l in lives[name]} for f, v in td.items()} for name, td in base_data.items()}
            for name in names:
                for field in ("accuracy", "rocket accuracy"):  # Assume the same accuracy over time to simplify scoring if accuracy score weights are non-zero.
                    tdata[name][field] = cdata[name][field]

            # Recompute derived data for the cutoff
            for Craft_Name in tdata:
                tdata[Craft_Name]["deaths"] = len(tdata[Craft_Name]['death times'])

                tdata[Craft_Name]["hits"] = sum(hitby[life][Craft_Name] for hitby in (tdata[other]["shot by"] for other in tdata
                    if other != Craft_Name and "shot by" in tdata[other]) for life in hitby if Craft_Name in hitby[life])
                tdata[Craft_Name]["rocket strikes"] = sum(hitby[life][Craft_Name] for hitby in (tdata[other]["rocket strike by"] for other in tdata
                    if other != Craft_Name and "rocket strike by" in tdata[other]) for life in hitby if Craft_Name in hitby[life])
                tdata[Craft_Name]["missile strikes"] = sum(hitby[life][Craft_Name] for hitby in (tdata[other]["missile strike by"] for other in tdata
                    if other != Craft_Name and "missile strike by" in tdata[other]) for life in hitby if Craft_Name in hitby[life])

                tdata[Craft_Name]["rocket parts hit"] = sum(partshitby[life][Craft_Name] for partshitby in (tdata[other]["rocket parts hit by"] for other in tdata
                    if other != Craft_Name and "rocket parts hit by" in tdata[other]) for life in partshitby if Craft_Name in partshitby[life])
                tdata[Craft_Name]["missile parts hit"] = sum(partshitby[life][Craft_Name] for partshitby in (tdata[other]["missile parts hit by"]
                    for other in tdata if other != Craft_Name and "missile parts hit by" in tdata[other]) for life in partshitby if Craft_Name in partshitby[life])

                tdata[Craft_Name]["bullet damage"] = sum(damageby[life][Craft_Name] for damageby in (tdata[other]["bullet damage by"] for other in tdata
                    if other != Craft_Name and "bullet damage by" in tdata[other]) for life in damageby if Craft_Name in damageby[life])
                tdata[Craft_Name]["rocket damage"] = sum(damageby[life][Craft_Name] for damageby in (tdata[other]["rocket damage by"] for other in tdata
                    if other != Craft_Name and "rocket damage by" in tdata[other]) for life in damageby if Craft_Name in damageby[life])
                tdata[Craft_Name]["missile damage"] = sum(damageby[life][Craft_Name] for damageby in (tdata[other]["missile damage by"] for other in tdata
                    if other != Craft_Name and "missile damage by" in tdata[other]) for life in damageby if Craft_Name in damageby[life])

                tdata[Craft_Name]["bullet damage taken"] = sum(damage for damageby in tdata[Craft_Name]['bullet damage by'].values() for damage in damageby.values()) if 'bullet damage by' in tdata[Craft_Name] else 0
                tdata[Craft_Name]["rocket damage taken"] = sum(damage for damageby in tdata[Craft_Name]['rocket damage by'].values() for damage in damageby.values()) if 'rocket damage by' in tdata[Craft_Name] else 0
                tdata[Craft_Name]["missile damage taken"] = sum(damage for damageby in tdata[Craft_Name]['missile damage by'].values()
                    for damage in damageby.values()) if 'missile damage by' in tdata[Craft_Name] else 0

                tdata[Craft_Name]["rammed parts"] = sum(partshitby[life][Craft_Name] for partshitby in (tdata[other]["rammed by"] for other in tdata
                    if other != Craft_Name and "rammed by" in tdata[other]) for life in partshitby if Craft_Name in partshitby[life])

                tdata[Craft_Name]["kills"] = sum(1 for kill in (tdata[other]["killed by"] for other in tdata if other !=
                    Craft_Name and "killed by" in tdata[other]) for life in kill if Craft_Name == kill[life])
                tdata[Craft_Name]["parts lost to asteroids"] = sum(tdata[Craft_Name]["parts lost to asteroids"].values()) if "parts lost to asteroids" in tdata[Craft_Name] else 0

                # Aggregate the damagers for computing assists.
                tdata[Craft_Name]['damaged by'] = {}
                if 'bullet damage by' in tdata[Craft_Name]:
                    tdata[Craft_Name]['damaged by'] = {k: set(v) for k, v in tdata[Craft_Name]['bullet damage by'].items() if k < tdata[Craft_Name]['deaths']}
                if 'rocket damage by' in tdata[Craft_Name]:
                    for k, v in tdata[Craft_Name]['rocket damage by'].items():
                        if k < tdata[Craft_Name]['deaths']:
                            if k in tdata[Craft_Name]['damaged by']:
                                tdata[Craft_Name]['damaged by'][k] = tdata[Craft_Name]['damaged by'][k].union(set(v))
                            else:
                                tdata[Craft_Name]['damaged by'][k] = set(v)
                if 'missile damage by' in tdata[Craft_Name]:
                    for k, v in tdata[Craft_Name]['missile damage by'].items():
                        if k < tdata[Craft_Name]['deaths']:
                            if k in tdata[Craft_Name]['damaged by']:
                                tdata[Craft_Name]['damaged by'][k] = tdata[Craft_Name]['damaged by'][k].union(set(v))
                            else:
                                tdata[Craft_Name]['damaged by'][k] = set(v)
                if 'rammed by' in tdata[Craft_Name]:
                    for k, v in tdata[Craft_Name]['rammed by'].items():
                        if k < tdata[Craft_Name]['deaths']:
                            if k in tdata[Craft_Name]['damaged by']:
                                tdata[Craft_Name]['damaged by'][k] = tdata[Craft_Name]['damaged by'][k].union(set(v))
                            else:
                                tdata[Craft_Name]['damaged by'][k] = set(v)

            # Compute assists and scores.
            for Craft_Name in tdata:
                tdata[Craft_Name]["assists"] = sum(1 for other in tdata for life, damagedby in tdata[other]['damaged by'].items() if Craft_Name in damagedby and not (
                    'killed by' in tdata[other] and life in tdata[other]['killed by']) and not ('GM kills' in tdata[other] and life in tdata[other]['GM kills']))

                tdata[Craft_Name]["score"] = sum(weights[field] * tdata[Craft_Name][field] for field in fields)
                scores[Craft_Name][cutoff] = tdata[Craft_Name]["score"]

        plt.figure(figsize=(16, 10), dpi=120)
        plt.plot(times, numpy.array([[s[t] for s in scores.values()] for t in times]), linewidth=5)
        plt.axhline(color='black')
        plt.xlabel('time (s)')
        plt.ylabel('score')
        plt.legend(names, loc='upper left')
        plt.autoscale(enable=True, tight=True)
        plt.tight_layout()
        if args.plot != 'tmp':
            filename = args.plot
            plt.savefig(filename, dpi='figure', bbox_inches='tight')
            print(f"Image saved to {filename}")
            try:
                subprocess.run(['display', filename])
            except:
                pass
        else:
            plt.show(block=True)

else:
    print(f"No valid log files found.")
