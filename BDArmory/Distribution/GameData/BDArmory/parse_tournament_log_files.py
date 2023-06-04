#!/usr/bin/env python3

# Standard library imports
import argparse
import json
import re
import sys
from base64 import b64decode, b64encode
from collections import Counter
from datetime import datetime, timedelta
from pathlib import Path
from typing import Dict, List, Tuple, Union

VERSION = "1.22.0"

parser = argparse.ArgumentParser(description="Tournament log parser", formatter_class=argparse.ArgumentDefaultsHelpFormatter)
parser.add_argument('tournament', type=str, nargs='*', help="Tournament folder to parse.")
parser.add_argument('-q', '--quiet', action='store_true', help="Don't print results summary to console.")
parser.add_argument('-n', '--no-files', action='store_true', help="Don't create summary files.")
parser.add_argument('-s', '--score', action='store_false', help="Compute scores.")
parser.add_argument('-so', '--scores-only', action='store_true', help="Only display the scores in the summary on the console.")
parser.add_argument('-w', '--weights', type=str, default="1,0,0,-1,1,2e-3,3,1.5,4e-3,0,1e-4,4e-5,0.035,0,6e-4,0,1.5e-4,5e-5,0.15,0,0.002,0,3e-5,1.5e-5,0.075,0,0,0,0,0,0,10,-1,-1", help="Score weights (in order of main columns from 'Wins' to 'Ram', plus others). Use --show-weights to see them.")
parser.add_argument('-c', '--current-dir', action='store_true', help="Parse the logs in the current directory as if it was a tournament without the folder structure.")
parser.add_argument('-nc', '--no-cumulative', action='store_true', help="Don't display cumulative scores at the end.")
parser.add_argument('-nh', '--no-header', action='store_true', help="Don't display the header.")
parser.add_argument('-N', type=int, help="Only the first N logs in the folder (in -c mode).")
parser.add_argument('-z', '--zero-lowest-score', action='store_true', help="Shift the scores so that the lowest is 0.")
parser.add_argument('-sw', '--show-weights', action='store_true', help="Display the score weights.")
parser.add_argument('-wp', '--waypoint-scores', action='store_true', help="Use the default waypoint scores.")
parser.add_argument("--version", action='store_true', help="Show the script version, then exit.")
args = parser.parse_args()
args.score = args.score or args.scores_only

if args.version:
    print(f"Version: {VERSION}")
    sys.exit()


def naturalSortKey(key: Union[str, Path]):
    if isinstance(key, Path):
        key = key.name
    try:
        return int(key.rsplit(' ')[1])  # If the key ends in an integer, split that off and use that as the sort key.
    except:
        return key  # Otherwise, just use the key.


if args.current_dir and len(args.tournament) == 0:
    tournamentDirs = [Path('')]
else:
    if len(args.tournament) == 0:
        tournamentDirs = None
        logsDir = Path(__file__).parent / "Logs"
        if logsDir.exists():
            tournamentFolders = list(logsDir.resolve().glob("Tournament*"))
            if len(tournamentFolders) > 0:
                tournamentFolders = sorted(list(dir for dir in tournamentFolders if dir.is_dir()), key=naturalSortKey)
            if len(tournamentFolders) > 0:
                tournamentDirs = [tournamentFolders[-1]]  # Latest tournament dir
        if tournamentDirs is None:  # Didn't find a tournament dir, revert to current-dir
            tournamentDirs = [Path('')]
            args.current_dir = True
    else:
        tournamentDirs = [Path(tournamentDir) for tournamentDir in args.tournament]  # Specified tournament dir

if args.waypoint_scores:
    args.weights = "0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,-0.02,-0.003"

score_fields = ('wins', 'survivedCount', 'miaCount', 'deathCount', 'deathOrder', 'deathTime', 'cleanKills', 'assists', 'hits', 'hitsTaken', 'bulletDamage', 'bulletDamageTaken', 'rocketHits', 'rocketHitsTaken', 'rocketPartsHit', 'rocketPartsHitTaken', 'rocketDamage', 'rocketDamageTaken', 'missileHits', 'missileHitsTaken', 'missilePartsHit', 'missilePartsHitTaken', 'missileDamage', 'missileDamageTaken', 'ramScore', 'ramScoreTaken', 'battleDamage', 'partsLostToAsteroids', 'HPremaining', 'accuracy', 'rocket_accuracy', 'waypointCount', 'waypointTime', 'waypointDeviation')
try:
    weights = list(float(w) for w in args.weights.split(','))
except:
    weights = []

if args.show_weights:
    field_width = max(len(f) for f in score_fields)
    for w, f in zip(weights, score_fields):
        print(f"{f}:{' '*(field_width - len(f))} {w}")
    sys.exit()


def CalculateAccuracy(hits, shots): return 100 * hits / shots if shots > 0 else 0


def CalculateAvgHP(hp, heats): return hp / heats if heats > 0 else 0


def cumsum(l):
    v = 0
    for i in l:
        v += i
        yield v


def encode_names(log_lines: List[str]) -> Tuple[Dict[str, str], List[str]]:
    """ Encode the craft names in base64 to avoid issues with naming.

    Args:
        log_lines (List[str]): The log lines.

    Returns:
        Tuple[Dict[str, str], List[str]]: The dictionary of encoded names to actual names and the modified log lines.
    """
    craft_names = set()
    for line in log_lines:
        if 'BDArmory.BDACompetitionMode' not in line:
            continue
        _, line = line.split(' ', 1)
        field, entry = line.split(':', 1)
        if field not in ('DEAD', 'MIA', 'ALIVE'):
            continue
        if field == 'DEAD':
            order, time, craft = entry.split(':', 2)
            craft_names.add(craft)
        if field == 'MIA':
            craft_names.add(entry)
        if field == 'ALIVE':
            craft_names.add(entry)
    craft_names.update({json.dumps(name)[1:-1] for name in craft_names})
    craft_names = {cn: b64encode(cn.encode()) for cn in craft_names}
    sorted_craft_names = list(sorted(craft_names, key=lambda k: len(k), reverse=True))  # Sort the craft names from longest to shortest to avoid accidentally replacing substrings.
    for i in range(1, len(log_lines)):  # The first line doesn't contain craft names
        for name in sorted_craft_names:
            log_lines[i] = log_lines[i].replace(name, craft_names[name].decode())
    encoded_craft_names = {v.decode(): k for k, v in craft_names.items()}
    return encoded_craft_names, log_lines


for tournamentNumber, tournamentDir in enumerate(tournamentDirs):
    if tournamentNumber > 0 and not args.quiet:
        print("")
    tournamentData = {}
    tournamentMetadata = {}
    m = re.search('Tournament (\\d+)', str(tournamentDir))
    if m is not None and len(m.groups()) > 0:
        tournamentMetadata['ID'] = m.groups()[0]
    tournamentMetadata['rounds'] = len([roundDir for roundDir in tournamentDir.iterdir() if roundDir.is_dir() and roundDir.name.startswith('Round')])
    for round in sorted((roundDir for roundDir in tournamentDir.iterdir() if roundDir.is_dir()), key=naturalSortKey) if not args.current_dir else (tournamentDir,):
        if not args.current_dir and len(round.name) == 0:
            continue
        tournamentData[round.name] = {}
        logFiles = sorted(round.glob("[0-9]*.log"))
        if len(logFiles) == 0:
            del tournamentData[round.name]
            continue
        for heat in logFiles if args.N == None else logFiles[:args.N]:
            with open(heat, "r", encoding="utf-8") as logFile:
                log_lines = [line.strip() for line in logFile]
            tournamentData[round.name][heat.name] = {'result': None, 'duration': 0, 'craft': {}}
            encoded_craft_names, log_lines = encode_names(log_lines)
            for line in log_lines:
                if 'BDArmory.BDACompetitionMode' not in line:
                    continue  # Ignore irrelevant lines
                _, field = line.split(' ', 1)
                if field.startswith('Dumping Results'):
                    duration = float(field[field.find('(') + 4:field.find(')') - 1])
                    timestamp = datetime.fromisoformat(field[field.find(' at ') + 4:])
                    tournamentData[round.name][heat.name]['duration'] = duration
                    tournamentMetadata['duration'] = (min(tournamentMetadata['duration'][0], timestamp), max(tournamentMetadata['duration'][1], timestamp + timedelta(seconds=duration))) if 'duration' in tournamentMetadata else (timestamp, timestamp + timedelta(seconds=duration))
                elif field.startswith('ALIVE:'):
                    state, craft = field.split(':', 1)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]] = {'state': state}
                elif field.startswith('DEAD:'):
                    state, order, time, craft = field.split(':', 3)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]] = {'state': state, 'deathOrder': int(order), 'deathTime': float(time)}
                elif field.startswith('MIA:'):
                    state, craft = field.split(':', 1)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]] = {'state': state}
                elif field.startswith('WHOSHOTWHOWITHGUNS:'):
                    _, craft, shooters = field.split(':', 2)
                    data = shooters.split(':')
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'hitsBy': {encoded_craft_names[player]: int(hits) for player, hits in zip(data[1::2], data[::2])}})
                elif field.startswith('WHODAMAGEDWHOWITHGUNS:'):
                    _, craft, shooters = field.split(':', 2)
                    data = shooters.split(':')
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'bulletDamageBy': {encoded_craft_names[player]: float(damage) for player, damage in zip(data[1::2], data[::2])}})
                elif field.startswith('WHOHITWHOWITHMISSILES:'):
                    _, craft, shooters = field.split(':', 2)
                    data = shooters.split(':')
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'missileHitsBy': {encoded_craft_names[player]: int(hits) for player, hits in zip(data[1::2], data[::2])}})
                elif field.startswith('WHOPARTSHITWHOWITHMISSILES:'):
                    _, craft, shooters = field.split(':', 2)
                    data = shooters.split(':')
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'missilePartsHitBy': {encoded_craft_names[player]: int(hits) for player, hits in zip(data[1::2], data[::2])}})
                elif field.startswith('WHODAMAGEDWHOWITHMISSILES:'):
                    _, craft, shooters = field.split(':', 2)
                    data = shooters.split(':')
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'missileDamageBy': {encoded_craft_names[player]: float(damage) for player, damage in zip(data[1::2], data[::2])}})
                elif field.startswith('WHOHITWHOWITHROCKETS:'):
                    _, craft, shooters = field.split(':', 2)
                    data = shooters.split(':')
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'rocketHitsBy': {encoded_craft_names[player]: int(hits) for player, hits in zip(data[1::2], data[::2])}})
                elif field.startswith('WHOPARTSHITWHOWITHROCKETS:'):
                    _, craft, shooters = field.split(':', 2)
                    data = shooters.split(':')
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'rocketPartsHitBy': {encoded_craft_names[player]: int(hits) for player, hits in zip(data[1::2], data[::2])}})
                elif field.startswith('WHODAMAGEDWHOWITHROCKETS:'):
                    _, craft, shooters = field.split(':', 2)
                    data = shooters.split(':')
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'rocketDamageBy': {encoded_craft_names[player]: float(damage) for player, damage in zip(data[1::2], data[::2])}})
                elif field.startswith('WHORAMMEDWHO:'):
                    _, craft, rammers = field.split(':', 2)
                    data = rammers.split(':')
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'rammedPartsLostBy': {encoded_craft_names[player]: int(partsLost) for player, partsLost in zip(data[1::2], data[::2])}})
                elif field.startswith('WHODAMAGEDWHOWITHBATTLEDAMAGE'):
                    _, craft, rammers = field.split(':', 2)
                    data = rammers.split(':')
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'battleDamageBy': {encoded_craft_names[player]: float(damage) for player, damage in zip(data[1::2], data[::2])}})
                elif field.startswith('CLEANKILLGUNS:'):
                    _, craft, killer = field.split(':', 2)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'cleanKillBy': encoded_craft_names[killer]})
                elif field.startswith('CLEANKILLROCKETS:'):
                    _, craft, killer = field.split(':', 2)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'cleanRocketKillBy': encoded_craft_names[killer]})
                elif field.startswith('CLEANKILLMISSILES:'):
                    _, craft, killer = field.split(':', 2)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'cleanMissileKillBy': encoded_craft_names[killer]})
                elif field.startswith('CLEANKILLRAMMING:'):
                    _, craft, killer = field.split(':', 2)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'cleanRamKillBy': encoded_craft_names[killer]})
                elif field.startswith('HEADSHOTGUNS:'):  # FIXME make head-shots separate from clean-kills
                    _, craft, killer = field.split(':', 2)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'cleanKillBy': encoded_craft_names[killer]})
                elif field.startswith('HEADSHOTROCKETS:'):
                    _, craft, killer = field.split(':', 2)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'cleanRocketKillBy': encoded_craft_names[killer]})
                elif field.startswith('HEADSHOTMISSILES:'):
                    _, craft, killer = field.split(':', 2)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'cleanMissileKillBy': encoded_craft_names[killer]})
                elif field.startswith('HEADSHOTRAMMING:'):
                    _, craft, killer = field.split(':', 2)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'cleanRamKillBy': encoded_craft_names[killer]})
                elif field.startswith('KILLSTEALGUNS:'):  # FIXME make kill-steals separate from clean-kills
                    _, craft, killer = field.split(':', 2)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'cleanKillBy': encoded_craft_names[killer]})
                elif field.startswith('KILLSTEALROCKETS:'):
                    _, craft, killer = field.split(':', 2)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'cleanRocketKillBy': encoded_craft_names[killer]})
                elif field.startswith('KILLSTEALMISSILES:'):
                    _, craft, killer = field.split(':', 2)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'cleanMissileKillBy': encoded_craft_names[killer]})
                elif field.startswith('KILLSTEALRAMMING:'):
                    _, craft, killer = field.split(':', 2)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'cleanRamKillBy': encoded_craft_names[killer]})
                elif field.startswith('GMKILL'):
                    _, craft, reason = field.split(':', 2)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'GMKillReason': reason})
                elif field.startswith('PARTSLOSTTOASTEROIDS:'):
                    _, craft, partsLost = field.split(':', 2)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'partsLostToAsteroids': int(partsLost)})
                elif field.startswith('HPLEFT:'):
                    _, craft, hp = field.split(':', 2)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'HPremaining': float(hp)})
                elif field.startswith('ACCURACY:'):
                    _, craft, accuracy, rocket_accuracy = field.split(':', 3)
                    hits, shots = accuracy.split('/')
                    rocket_strikes, rockets_fired = rocket_accuracy.split('/')
                    accuracy = CalculateAccuracy(int(hits), int(shots))
                    rocket_accuracy = CalculateAccuracy(int(rocket_strikes), int(rockets_fired))
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'accuracy': accuracy, 'hits': int(hits), 'shots': int(shots), 'rocket_accuracy': rocket_accuracy, 'rocket_strikes': int(rocket_strikes), 'rockets_fired': int(rockets_fired)})
                elif field.startswith('RESULT:'):
                    heat_result = field.split(':', 2)
                    result_type = heat_result[1]
                    if (len(heat_result) > 2):
                        teams = json.loads(heat_result[2])
                        if isinstance(teams, dict):  # Win, single team
                            tournamentData[round.name][heat.name]['result'] = {'result': result_type, 'teams': {encoded_craft_names.get(teams['team'], teams['team']): ', '.join((encoded_craft_names[craft] for craft in teams['members']))}}
                        elif isinstance(teams, list):  # Draw, multiple teams
                            tournamentData[round.name][heat.name]['result'] = {'result': result_type, 'teams': {encoded_craft_names.get(team['team'], team['team']): ', '.join((encoded_craft_names[craft] for craft in team['members'])) for team in teams}}
                    else:  # Mutual Annihilation
                        tournamentData[round.name][heat.name]['result'] = {'result': result_type}
                elif field.startswith('DEADTEAMS:'):
                    dead_teams = json.loads(field.split(':', 1)[1])
                    if len(dead_teams) > 0:
                        tournamentData[round.name][heat.name]['result'].update({'dead teams': {encoded_craft_names.get(team['team'], team['team']): ', '.join((encoded_craft_names[craft] for craft in team['members'])) for team in dead_teams}})
                # Ignore Tag mode for now.
                elif field.startswith('WAYPOINTS:'):
                    _, craft, waypoints_str = field.split(':', 2)
                    tournamentData[round.name][heat.name]['craft'][encoded_craft_names[craft]].update({'waypoints': [waypoint.split(':') for waypoint in waypoints_str.split(';')]})  # List[Tuple[int, float, float]] = [(index, deviation, timestamp),]

    if not args.no_files and len(tournamentData) > 0:
        with open(tournamentDir / 'results.json', 'w', encoding="utf-8") as outFile:
            json.dump(tournamentData, outFile, indent=2)

    craftNames = sorted(list(set(craft for round in tournamentData.values() for heat in round.values() for craft in heat['craft'].keys())))
    teamWins = Counter([team for round in tournamentData.values() for heat in round.values() if heat['result']['result'] == "Win" for team in heat['result']['teams']])
    teamDraws = Counter([team for round in tournamentData.values() for heat in round.values() if heat['result']['result'] == "Draw" for team in heat['result']['teams']])
    teamDeaths = Counter([team for round in tournamentData.values() for heat in round.values() if 'dead teams' in heat['result'] for team in heat['result']['dead teams']])
    teams = {team: members for round in tournamentData.values() for heat in round.values() if 'teams' in heat['result'] for team, members in heat['result']['teams'].items()}
    teams.update({team: members for round in tournamentData.values() for heat in round.values() if 'dead teams' in heat['result'] for team, members in heat['result']['dead teams'].items()})
    summary = {
        'meta': {
            'ID': tournamentMetadata.get('ID', 'unknown'),
            'duration': [ts.isoformat() for ts in tournamentMetadata.get('duration', (datetime.now(), datetime.now()))],
            'rounds': tournamentMetadata.get('rounds', -1),
            'score weights': {f: w for f, w in zip(score_fields, weights)},
        },
        'craft': {
            craft: {
                'wins': len([1 for round in tournamentData.values() for heat in round.values() if heat['result']['result'] == "Win" and craft in next(iter(heat['result']['teams'].values())).split(", ")]),
                'survivedCount': len([1 for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'ALIVE']),
                'miaCount': len([1 for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'MIA']),
                'deathCount': (
                    len([1 for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'DEAD']),  # Total
                    len([1 for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'DEAD' and 'cleanKillBy' in heat['craft'][craft]]),  # Bullets
                    len([1 for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'DEAD' and 'cleanRocketKillBy' in heat['craft'][craft]]),  # Rockets
                    len([1 for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'DEAD' and 'cleanMissileKillBy' in heat['craft'][craft]]),  # Missiles
                    len([1 for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'DEAD' and 'cleanRamKillBy' in heat['craft'][craft]]),  # Rams
                    len([1 for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'DEAD' and not any(field in heat['craft'][craft] for field in ('cleanKillBy', 'cleanRocketKillBy', 'cleanMissileKillBy', 'cleanRamKillBy')) and any(field in heat['craft'][craft] for field in ('hitsBy', 'rocketPartsHitBy', 'missilePartsHitBy', 'rammedPartsLostBy'))]),  # Dirty kill
                    len([1 for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'DEAD' and not any(field in heat['craft'][craft] for field in ('hitsBy', 'rocketPartsHitBy', 'missilePartsHitBy', 'rammedPartsLostBy')) and not any('rammedPartsLostBy' in data and craft in data['rammedPartsLostBy'] for data in heat['craft'].values())]),  # Suicide (died without being hit or ramming anyone).
                ),
                'deathOrder': sum([heat['craft'][craft]['deathOrder'] / len(heat['craft']) if 'deathOrder' in heat['craft'][craft] else 1 for round in tournamentData.values() for heat in round.values() if craft in heat['craft']]),
                'deathTime': sum([heat['craft'][craft]['deathTime'] if 'deathTime' in heat['craft'][craft] else heat['duration'] for round in tournamentData.values() for heat in round.values() if craft in heat['craft']]),
                'cleanKills': (
                    len([1 for round in tournamentData.values() for heat in round.values() for data in heat['craft'].values() if any((field in data and data[field] == craft) for field in ('cleanKillBy', 'cleanRocketKillBy', 'cleanMissileKillBy', 'cleanRamKillBy'))]),  # Total
                    len([1 for round in tournamentData.values() for heat in round.values() for data in heat['craft'].values() if 'cleanKillBy' in data and data['cleanKillBy'] == craft]),  # Bullets
                    len([1 for round in tournamentData.values() for heat in round.values() for data in heat['craft'].values() if 'cleanRocketKillBy' in data and data['cleanRocketKillBy'] == craft]),  # Rockets
                    len([1 for round in tournamentData.values() for heat in round.values() for data in heat['craft'].values() if 'cleanMissileKillBy' in data and data['cleanMissileKillBy'] == craft]),  # Missiles
                    len([1 for round in tournamentData.values() for heat in round.values() for data in heat['craft'].values() if 'cleanRamKillBy' in data and data['cleanRamKillBy'] == craft]),  # Rams
                ),
                'assists': len([1 for round in tournamentData.values() for heat in round.values() for data in heat['craft'].values() if data['state'] == 'DEAD' and any(field in data and craft in data[field] for field in ('hitsBy', 'rocketPartsHitBy', 'missilePartsHitBy', 'rammedPartsLostBy')) and not any((field in data) for field in ('cleanKillBy', 'cleanRocketKillBy', 'cleanMissileKillBy', 'cleanRamKillBy'))]),
                'hits': sum([heat['craft'][craft]['hits'] for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'hits' in heat['craft'][craft]]),
                'hitsTaken': sum([sum(heat['craft'][craft]['hitsBy'].values()) for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'hitsBy' in heat['craft'][craft]]),
                'bulletDamage': sum([data[field][craft] for round in tournamentData.values() for heat in round.values() for data in heat['craft'].values() for field in ('bulletDamageBy',) if field in data and craft in data[field]]),
                'bulletDamageTaken': sum([sum(heat['craft'][craft]['bulletDamageBy'].values()) for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'bulletDamageBy' in heat['craft'][craft]]),
                'rocketHits': sum([data[field][craft] for round in tournamentData.values() for heat in round.values() for data in heat['craft'].values() for field in ('rocketHitsBy',) if field in data and craft in data[field]]),
                'rocketHitsTaken': sum([sum(heat['craft'][craft]['rocketHitsBy'].values()) for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'rocketHitsBy' in heat['craft'][craft]]),
                'rocketPartsHit': sum([data[field][craft] for round in tournamentData.values() for heat in round.values() for data in heat['craft'].values() for field in ('rocketPartsHitBy',) if field in data and craft in data[field]]),
                'rocketPartsHitTaken': sum([sum(heat['craft'][craft]['rocketPartsHitBy'].values()) for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'rocketPartsHitBy' in heat['craft'][craft]]),
                'rocketDamage': sum([data[field][craft] for round in tournamentData.values() for heat in round.values() for data in heat['craft'].values() for field in ('rocketDamageBy',) if field in data and craft in data[field]]),
                'rocketDamageTaken': sum([sum(heat['craft'][craft]['rocketDamageBy'].values()) for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'rocketDamageBy' in heat['craft'][craft]]),
                'missileHits': sum([data[field][craft] for round in tournamentData.values() for heat in round.values() for data in heat['craft'].values() for field in ('missileHitsBy',) if field in data and craft in data[field]]),
                'missileHitsTaken': sum([sum(heat['craft'][craft]['missileHitsBy'].values()) for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'missileHitsBy' in heat['craft'][craft]]),
                'missilePartsHit': sum([data[field][craft] for round in tournamentData.values() for heat in round.values() for data in heat['craft'].values() for field in ('missilePartsHitBy',) if field in data and craft in data[field]]),
                'missilePartsHitTaken': sum([sum(heat['craft'][craft]['missilePartsHitBy'].values()) for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'missilePartsHitBy' in heat['craft'][craft]]),
                'missileDamage': sum([data[field][craft] for round in tournamentData.values() for heat in round.values() for data in heat['craft'].values() for field in ('missileDamageBy',) if field in data and craft in data[field]]),
                'missileDamageTaken': sum([sum(heat['craft'][craft]['missileDamageBy'].values()) for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'missileDamageBy' in heat['craft'][craft]]),
                'ramScore': sum([data[field][craft] for round in tournamentData.values() for heat in round.values() for data in heat['craft'].values() for field in ('rammedPartsLostBy',) if field in data and craft in data[field]]),
                'ramScoreTaken': sum([sum(heat['craft'][craft]['rammedPartsLostBy'].values()) for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'rammedPartsLostBy' in heat['craft'][craft]]),
                'battleDamage': sum([data[field][craft] for round in tournamentData.values() for heat in round.values() for player, data in heat['craft'].items() if player != craft for field in ('battleDamageBy',) if field in data and craft in data[field]]),
                'battleDamageTaken': sum([sum(heat['craft'][craft]['battleDamageBy'].values()) for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'battleDamageBy' in heat['craft'][craft]]),
                'partsLostToAsteroids': sum([heat['craft'][craft]['partsLostToAsteroids'] for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'partsLostToAsteroids' in heat['craft'][craft]]),
                'HPremaining': CalculateAvgHP(sum([heat['craft'][craft]['HPremaining'] for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'HPremaining' in heat['craft'][craft] and heat['craft'][craft]['state'] == 'ALIVE']), len([1 for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'ALIVE'])),
                'accuracy': CalculateAccuracy(sum([heat['craft'][craft]['hits'] for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'hits' in heat['craft'][craft]]), sum([heat['craft'][craft]['shots'] for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'shots' in heat['craft'][craft]])),
                'rocket_accuracy': CalculateAccuracy(sum([heat['craft'][craft]['rocket_strikes'] for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'rocket_strikes' in heat['craft'][craft]]), sum([heat['craft'][craft]['rockets_fired'] for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'rockets_fired' in heat['craft'][craft]])),
            }
            for craft in craftNames
        },
        'team results': {
            'wins': teamWins,
            'draws': teamDraws,
            'deaths': teamDeaths
        },
        'teams': teams
    }

    for craft in summary['craft'].values():
        spawns = craft['survivedCount'] + craft['deathCount'][0]
        craft.update({
            'damage/hit': craft['bulletDamage'] / craft['hits'] if craft['hits'] > 0 else 0,
            'hits/spawn': craft['hits'] / spawns if spawns > 0 else 0,
            'damage/spawn': craft['bulletDamage'] / spawns if spawns > 0 else 0,
        })

    per_round_summary = {  # Compute this here, since we need the per-round waypoint info to avoid negative scores.
        craft: [
            {
                'wins': len([1 for heat in round.values() if heat['result']['result'] == "Win" and craft in next(iter(heat['result']['teams'].values())).split(", ")]),
                'survivedCount': len([1 for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'ALIVE']),
                'miaCount': len([1 for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'MIA']),
                'deathCount': (
                    len([1 for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'DEAD']),  # Total
                    len([1 for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'DEAD' and 'cleanKillBy' in heat['craft'][craft]]),  # Bullets
                    len([1 for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'DEAD' and 'cleanRocketKillBy' in heat['craft'][craft]]),  # Rockets
                    len([1 for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'DEAD' and 'cleanMissileKillBy' in heat['craft'][craft]]),  # Missiles
                    len([1 for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'DEAD' and 'cleanRamKillBy' in heat['craft'][craft]]),  # Rams
                    len([1 for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'DEAD' and not any(field in heat['craft'][craft] for field in ('cleanKillBy', 'cleanRocketKillBy', 'cleanMissileKillBy', 'cleanRamKillBy')) and any(field in heat['craft'][craft] for field in ('hitsBy', 'rocketPartsHitBy', 'missilePartsHitBy', 'rammedPartsLostBy'))]),  # Dirty kill
                    len([1 for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'DEAD' and not any(field in heat['craft'][craft] for field in ('hitsBy', 'rocketPartsHitBy', 'missilePartsHitBy', 'rammedPartsLostBy')) and not any('rammedPartsLostBy' in data and craft in data['rammedPartsLostBy'] for data in heat['craft'].values())]),  # Suicide (died without being hit or ramming anyone).
                ),
                'deathOrder': sum([heat['craft'][craft]['deathOrder'] / len(heat['craft']) if 'deathOrder' in heat['craft'][craft] else 1 for heat in round.values() if craft in heat['craft']]),
                'deathTime': sum([heat['craft'][craft]['deathTime'] if 'deathTime' in heat['craft'][craft] else heat['duration'] for heat in round.values() if craft in heat['craft']]),
                'cleanKills': (
                    len([1 for heat in round.values() for data in heat['craft'].values() if any((field in data and data[field] == craft) for field in ('cleanKillBy', 'cleanRocketKillBy', 'cleanMissileKillBy', 'cleanRamKillBy'))]),  # Total
                    len([1 for heat in round.values() for data in heat['craft'].values() if 'cleanKillBy' in data and data['cleanKillBy'] == craft]),  # Bullets
                    len([1 for heat in round.values() for data in heat['craft'].values() if 'cleanRocketKillBy' in data and data['cleanRocketKillBy'] == craft]),  # Rockets
                    len([1 for heat in round.values() for data in heat['craft'].values() if 'cleanMissileKillBy' in data and data['cleanMissileKillBy'] == craft]),  # Missiles
                    len([1 for heat in round.values() for data in heat['craft'].values() if 'cleanRamKillBy' in data and data['cleanRamKillBy'] == craft]),  # Rams
                ),
                'assists': len([1 for heat in round.values() for data in heat['craft'].values() if data['state'] == 'DEAD' and any(field in data and craft in data[field] for field in ('hitsBy', 'rocketPartsHitBy', 'missilePartsHitBy', 'rammedPartsLostBy')) and not any((field in data) for field in ('cleanKillBy', 'cleanRocketKillBy', 'cleanMissileKillBy', 'cleanRamKillBy'))]),
                'hits': sum([heat['craft'][craft]['hits'] for heat in round.values() if craft in heat['craft'] and 'hits' in heat['craft'][craft]]),
                'hitsTaken': sum([sum(heat['craft'][craft]['hitsBy'].values()) for heat in round.values() if craft in heat['craft'] and 'hitsBy' in heat['craft'][craft]]),
                'bulletDamage': sum([data[field][craft] for heat in round.values() for data in heat['craft'].values() for field in ('bulletDamageBy',) if field in data and craft in data[field]]),
                'bulletDamageTaken': sum([sum(heat['craft'][craft]['bulletDamageBy'].values()) for heat in round.values() if craft in heat['craft'] and 'bulletDamageBy' in heat['craft'][craft]]),
                'rocketHits': sum([data[field][craft] for heat in round.values() for data in heat['craft'].values() for field in ('rocketHitsBy',) if field in data and craft in data[field]]),
                'rocketHitsTaken': sum([sum(heat['craft'][craft]['rocketHitsBy'].values()) for heat in round.values() if craft in heat['craft'] and 'rocketHitsBy' in heat['craft'][craft]]),
                'rocketPartsHit': sum([data[field][craft] for heat in round.values() for data in heat['craft'].values() for field in ('rocketPartsHitBy',) if field in data and craft in data[field]]),
                'rocketPartsHitTaken': sum([sum(heat['craft'][craft]['rocketPartsHitBy'].values()) for heat in round.values() if craft in heat['craft'] and 'rocketPartsHitBy' in heat['craft'][craft]]),
                'rocketDamage': sum([data[field][craft] for heat in round.values() for data in heat['craft'].values() for field in ('rocketDamageBy',) if field in data and craft in data[field]]),
                'rocketDamageTaken': sum([sum(heat['craft'][craft]['rocketDamageBy'].values()) for heat in round.values() if craft in heat['craft'] and 'rocketDamageBy' in heat['craft'][craft]]),
                'missileHits': sum([data[field][craft] for heat in round.values() for data in heat['craft'].values() for field in ('missileHitsBy',) if field in data and craft in data[field]]),
                'missileHitsTaken': sum([sum(heat['craft'][craft]['missileHitsBy'].values()) for heat in round.values() if craft in heat['craft'] and 'missileHitsBy' in heat['craft'][craft]]),
                'missilePartsHit': sum([data[field][craft] for heat in round.values() for data in heat['craft'].values() for field in ('missilePartsHitBy',) if field in data and craft in data[field]]),
                'missilePartsHitTaken': sum([sum(heat['craft'][craft]['missilePartsHitBy'].values()) for heat in round.values() if craft in heat['craft'] and 'missilePartsHitBy' in heat['craft'][craft]]),
                'missileDamage': sum([data[field][craft] for heat in round.values() for data in heat['craft'].values() for field in ('missileDamageBy',) if field in data and craft in data[field]]),
                'missileDamageTaken': sum([sum(heat['craft'][craft]['missileDamageBy'].values()) for heat in round.values() if craft in heat['craft'] and 'missileDamageBy' in heat['craft'][craft]]),
                'ramScore': sum([data[field][craft] for heat in round.values() for data in heat['craft'].values() for field in ('rammedPartsLostBy',) if field in data and craft in data[field]]),
                'ramScoreTaken': sum([sum(heat['craft'][craft]['rammedPartsLostBy'].values()) for heat in round.values() if craft in heat['craft'] and 'rammedPartsLostBy' in heat['craft'][craft]]),
                'battleDamage': sum([data[field][craft] for heat in round.values() for player, data in heat['craft'].items() if player != craft for field in ('battleDamageBy',) if field in data and craft in data[field]]),
                'battleDamageTaken': sum([sum(heat['craft'][craft]['battleDamageBy'].values()) for heat in round.values() if craft in heat['craft'] and 'battleDamageBy' in heat['craft'][craft]]),
                'partsLostToAsteroids': sum([heat['craft'][craft]['partsLostToAsteroids'] for heat in round.values() if craft in heat['craft'] and 'partsLostToAsteroids' in heat['craft'][craft]]),
                'HPremaining': CalculateAvgHP(sum([heat['craft'][craft]['HPremaining'] for heat in round.values() if craft in heat['craft'] and 'HPremaining' in heat['craft'][craft] and heat['craft'][craft]['state'] == 'ALIVE']), len([1 for heat in round.values() if craft in heat['craft'] and heat['craft'][craft]['state'] == 'ALIVE'])),
                'accuracy': CalculateAccuracy(sum([heat['craft'][craft]['hits'] for heat in round.values() if craft in heat['craft'] and 'hits' in heat['craft'][craft]]), sum([heat['craft'][craft]['shots'] for heat in round.values() if craft in heat['craft'] and 'shots' in heat['craft'][craft]])),
                'rocket_accuracy': CalculateAccuracy(sum([heat['craft'][craft]['rocket_strikes'] for heat in round.values() if craft in heat['craft'] and 'rocket_strikes' in heat['craft'][craft]]), sum([heat['craft'][craft]['rockets_fired'] for heat in round.values() if craft in heat['craft'] and 'rockets_fired' in heat['craft'][craft]])),
                'waypointCount': sum(len(heat['craft'][craft]['waypoints']) for heat in round.values() if craft in heat['craft'] and 'waypoints' in heat['craft'][craft]),
                'waypointTime': sum(float(heat['craft'][craft]['waypoints'][-1][2]) - float(heat['craft'][craft]['waypoints'][0][2]) for heat in round.values() if craft in heat['craft'] and 'waypoints' in heat['craft'][craft]),
                'waypointDeviation': sum(float(waypoint[1]) for heat in round.values() if craft in heat['craft'] and 'waypoints' in heat['craft'][craft] for waypoint in heat['craft'][craft]['waypoints']),
            } for round in tournamentData.values()
        ] for craft in craftNames
    }

    hasWaypoints = False
    if any('waypoints' in heat['craft'][craft].keys() for round in tournamentData.values() for heat in round.values() for craft in craftNames if craft in heat['craft']):
        hasWaypoints = True
        for craft in craftNames:
            WPbestCount = max((len(heat['craft'][craft]['waypoints']) for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'waypoints' in heat['craft'][craft]), default=0)
            summary['craft'][craft].update({
                'waypointCount': sum(len(heat['craft'][craft]['waypoints']) for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'waypoints' in heat['craft'][craft]),
                'waypointTime': sum((float(heat['craft'][craft]['waypoints'][-1][2]) - float(heat['craft'][craft]['waypoints'][0][2])) for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'waypoints' in heat['craft'][craft]),
                'waypointDeviation': sum(sum(float(waypoint[1]) for waypoint in heat['craft'][craft]['waypoints']) for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'waypoints' in heat['craft'][craft]),
                'waypointBestCount': WPbestCount,
                'waypointBestTime': min(((float(heat['craft'][craft]['waypoints'][-1][2]) - float(heat['craft'][craft]['waypoints'][0][2])) for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'waypoints' in heat['craft'][craft] and len(heat['craft'][craft]['waypoints']) == WPbestCount), default=0),
                'waypointBestDeviation': min((sum(float(waypoint[1]) for waypoint in heat['craft'][craft]['waypoints']) for round in tournamentData.values() for heat in round.values() if craft in heat['craft'] and 'waypoints' in heat['craft'][craft] and len(heat['craft'][craft]['waypoints']) == WPbestCount), default=0),
                })

    if args.score:
        for craftName, summary_data in summary['craft'].items():
            score = sum(w * summary_data[f][0] if isinstance(summary_data[f], tuple) else w * summary_data[f] for w, f in zip(weights, score_fields) if f in summary_data and not f.startswith('waypoint'))  # Treat waypoints separately so we can avoid non-negative scores for waypoints.
            waypoint_data = per_round_summary[craftName]
            score += sum(max(0, sum(
                            w * waypoint_data[round][f][0] if isinstance(waypoint_data[round][f], tuple) else w * waypoint_data[round][f] for w, f in zip(weights, score_fields) if f.startswith('waypoint')
                        )) for round in range(len(waypoint_data)))
            summary_data.update({'score': score})
        if args.zero_lowest_score and len(summary['craft']) > 0:
            offset = min(summary_data['score'] for summary_data in summary['craft'].values())
            for summary_data in summary['craft'].values():
                summary_data['score'] -= offset

    if not args.no_files and len(summary['craft']) > 0:
        with open(tournamentDir / 'summary.json', 'w', encoding="utf-8") as outFile:
            json.dump(summary, outFile, indent=2)

    if len(summary['craft']) > 0:
        if not args.no_files:
            headers = (["score", ] if args.score else []) + [k for k in next(iter(summary['craft'].values())).keys() if k not in ('score',)]
            csv_summary = ["craft," + ",".join(
                ",".join(('deathCount', 'dcB', 'dcR', 'dcM', 'dcR', 'dcA', 'dcS')) if k == 'deathCount' else
                ",".join(('cleanKills', 'ckB', 'ckR', 'ckM', 'ckR')) if k == 'cleanKills' else
                k for k in headers), ]
            for craft, score in sorted(summary['craft'].items(), key=lambda i: i[1]['score'], reverse=True):
                csv_summary.append(craft + "," + ",".join(
                    ",".join(str(int(100 * sf) / 100) for sf in score[h]) if isinstance(score[h], tuple)
                    else ",".join(str(int(100 * sf) / 100) for sf in score[h].values()) if isinstance(score[h], dict)
                    else str(int(100 * score[h]) / 100)
                for h in headers))
            # Write main summary results to the summary.csv file.
            with open(tournamentDir / 'summary.csv', 'w', encoding="utf-8") as outFile:
                outFile.write("\n".join(csv_summary))

        teamNames = sorted(list(set([team for result_type in summary['team results'].values() for team in result_type])))
        default_team_names = [chr(k) for k in range(ord('A'), ord('A') + len(summary['craft']))]

        if args.score and not args.no_cumulative:  # Per round scores.
            per_round_scores = {
                craft: [
                    sum(
                        w * scores[round][f][0] if isinstance(scores[round][f], tuple) else w * scores[round][f] for w, f in zip(weights, score_fields) if not f.startswith('waypoint')
                    )
                    + max(0, sum(
                        w * scores[round][f][0] if isinstance(scores[round][f], tuple) else w * scores[round][f] for w, f in zip(weights, score_fields) if f.startswith('waypoint')  # Compute waypoint score separately to avoid non-negative values.
                    ))
                    for round in range(len(scores))
                ] for craft, scores in per_round_summary.items()
            }
        else:
            per_round_scores = {}  # Silence Pylance warnings.

        if not args.quiet:  # Write results to console
            strings = []
            if not args.no_header and not args.current_dir and 'duration' in tournamentMetadata:
                strings.append(f"Tournament {tournamentMetadata.get('ID', '???')} of duration {tournamentMetadata['duration'][1]-tournamentMetadata['duration'][0]} with {tournamentMetadata['rounds']} rounds starting at {tournamentMetadata['duration'][0]}")
            headers = ['Name', 'Wins', 'Survive', 'MIA', 'Deaths (BRMRAS)', 'D.Order', 'D.Time', 'Kills (BRMR)', 'Assists', 'Hits', 'Damage', 'DmgTaken', 'RocHits', 'RocParts', 'RocDmg', 'HitByRoc', 'MisHits', 'MisParts', 'MisDmg', 'HitByMis', 'Ram', 'BD dealt', 'BD taken', 'Ast.', 'Acc%', 'RktAcc%', 'HP%', 'Dmg/Hit', 'Hits/Sp', 'Dmg/Sp'] if not args.scores_only else ['Name']
            if hasWaypoints and not args.scores_only:
                headers.extend(['WPcount', 'WPtime', 'WPdev', 'WPbestC', 'WPbestT', 'WPbestD'])
            if args.score:
                headers.insert(1, 'Score')
            summary_strings = {'header': {field: field for field in headers}}
            for craft in sorted(summary['craft']):
                tmp = summary['craft'][craft]
                spawns = tmp['survivedCount'] + tmp['deathCount'][0]
                summary_strings.update({
                    craft: {
                        'Name': craft,
                        'Wins': f"{tmp['wins']}",
                        'Survive': f"{tmp['survivedCount']}",
                        'MIA': f"{tmp['miaCount']}",
                        'Deaths (BRMRAS)': f"{tmp['deathCount'][0]} ({' '.join(str(s) for s in tmp['deathCount'][1:])})",
                        'D.Order': f"{tmp['deathOrder']:.3f}",
                        'D.Time': f"{tmp['deathTime']:.1f}",
                        'Kills (BRMR)': f"{tmp['cleanKills'][0]} ({' '.join(str(s) for s in tmp['cleanKills'][1:])})",
                        'Assists': f"{tmp['assists']}",
                        'Hits': f"{tmp['hits']}",
                        'Damage': f"{tmp['bulletDamage']:.0f}",
                        'DmgTaken': f"{tmp['bulletDamageTaken']:.0f}",
                        'RocHits': f"{tmp['rocketHits']}",
                        'RocParts': f"{tmp['rocketPartsHit']}",
                        'RocDmg': f"{tmp['rocketDamage']:.0f}",
                        'HitByRoc': f"{tmp['rocketHitsTaken']}",
                        'MisHits': f"{tmp['missileHits']}",
                        'MisParts': f"{tmp['missilePartsHit']}",
                        'MisDmg': f"{tmp['missileDamage']:.0f}",
                        'HitByMis': f"{tmp['missileHitsTaken']}",
                        'Ram': f"{tmp['ramScore']}",
                        'BD dealt': f"{tmp['battleDamage']:.0f}",
                        'BD taken': f"{tmp['battleDamageTaken']:.0f}",
                        'Ast.': f"{tmp['partsLostToAsteroids']}",
                        'Acc%': f"{tmp['accuracy']:.3g}",
                        'RktAcc%': f"{tmp['rocket_accuracy']:.3g}",
                        'HP%': f"{tmp['HPremaining']:.3g}",
                        'Dmg/Hit': f"{tmp['damage/hit']:.1f}",
                        'Hits/Sp': f"{tmp['hits/spawn']:.1f}",
                        'Dmg/Sp': f"{tmp['damage/spawn']:.1f}",
                    }
                })
                if hasWaypoints:
                    summary_strings[craft].update({
                        'WPcount': f"{tmp['waypointCount']}",
                        'WPtime': f"{tmp['waypointTime']:.1f}",
                        'WPdev': f"{tmp['waypointDeviation']:.1f}",
                        'WPbestC': f"{tmp['waypointBestCount']}",
                        'WPbestT': f"{tmp['waypointBestTime']:.1f}",
                        'WPbestD': f"{tmp['waypointBestDeviation']:.1f}",
                    })
                if args.score:
                    summary_strings[craft]['Score'] = f"{tmp['score']:.3f}"
            columns_to_show = [header for header in headers if not all(craft[header] == "0" for craft in list(summary_strings.values())[1:])]
            column_widths = {column: max(len(craft[column]) + 2 for craft in summary_strings.values()) for column in headers}
            strings.append(''.join(f"{header:{column_widths[header]}s}" for header in columns_to_show))
            for craft in sorted(summary['craft'], key=None if not args.score else lambda craft: summary['craft'][craft]['score'], reverse=False if not args.score else True):
                strings.append(''.join(f"{summary_strings[craft][header]:{column_widths[header]}s}" for header in columns_to_show))

            # Teams summary
            if len(teamNames) > 0 and not all(name in default_team_names for name in teamNames):  # Don't do teams if they're assigned as 'A', 'B', ... as they won't be consistent between rounds.
                name_length = max([len(team) for team in teamNames])
                strings.append(f"\nTeam{' '*(name_length-4)}\tWins\tDraws\tDeaths\tVessels")
                for team in sorted(teamNames, key=lambda team: teamWins[team], reverse=True):
                    strings.append(f"{team}{' '*(name_length-len(team))}\t{teamWins[team]}\t{teamDraws[team]}\t{teamDeaths[team]}\t{summary['teams'][team]}")

            # Per round cumulative score
            if args.score and not args.no_cumulative:
                name_length = max([len(name) for name in per_round_scores.keys()] + [23])
                strings.append(f"\nName \\ Cumulative Score{' '*(name_length-22)}\t" + "\t".join(f"{r:>7d}" for r in range(len(next(iter(per_round_scores.values()))))))
                strings.append('\n'.join(f"{craft}:{' '*(name_length-len(craft))}\t" + "\t".join(f"{s:>7.2f}" for s in cumsum(per_round_scores[craft])) for craft in sorted(per_round_scores, key=lambda craft: summary['craft'][craft]['score'], reverse=True)))

            # Print stuff to the console.
            for string in strings:
                print(string)

        # Write teams results to the summary.csv file.
        if not args.no_files:
            with open(tournamentDir / 'summary.csv', 'a', encoding="utf-8") as f:
                f.write('\n\nTeam,Wins,Draws,Deaths,Vessels')
                for team in sorted(teamNames, key=lambda team: teamWins[team], reverse=True):
                    f.write('\n' + ','.join([str(v) for v in (team, teamWins[team], teamDraws[team], teamDeaths[team], summary['teams'][team].replace(", ", ","))]))

                # Write per round cumulative score results to summary.csv file.
                if args.score and not args.no_cumulative:
                    f.write(f"\n\nName \\ Cumulative Score Per Round," + ",".join(f"{r:>7d}" for r in range(len(next(iter(per_round_scores.values()))))))
                    for craft in sorted(per_round_scores, key=lambda craft: summary['craft'][craft]['score'], reverse=True):
                        f.write(f"\n{craft}," + ",".join(f"{s:.2f}" for s in cumsum(per_round_scores[craft])))

    else:
        print(f"No valid log files found in {tournamentDir}.")
