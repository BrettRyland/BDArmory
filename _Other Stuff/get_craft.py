# Standard library imports
import argparse
import json
import os
import re
import subprocess
import tempfile
from pathlib import Path
from shutil import which

parser = argparse.ArgumentParser(description="Grab all the craft from the API for a competition and rename them.", formatter_class=argparse.ArgumentDefaultsHelpFormatter)
parser.add_argument('compID', type=int, help="Competition ID")
parser.add_argument('--curl-command', type=str, default='curl', help="Curl command (in case it needs specifying on Windows).")
parser.add_argument('--keep', action='store_true', help="Keep the player and manifest files.")
args = parser.parse_args()

if which(args.curl_command) is None:
    print(f"Error: curl command ({args.curl_command}) not found. Please provide a valid curl command as an argument.")
    exit()

comp_url = f"https://conquertheair.com/competitions/{args.compID}/vessels/manifest.json"
players_url = f"https://conquertheair.com/players"
cwd = Path('.').absolute()
manifest_file = Path(tempfile.mkstemp(dir=cwd if args.keep else None)[1])
players_file = Path(tempfile.mkstemp(dir=cwd if args.keep else None)[1])

try:
    print("Fetching players and competition manifest...", end='', flush=True)
    subprocess.run(f"{args.curl_command} -s {players_url} -o {players_file}".split())
    subprocess.run(f"{args.curl_command} -s {comp_url} -o {manifest_file}".split())
    print("done.")

    with open(players_file, 'r') as f:
        lines = f.readlines()
    expr = re.compile('players/([0-9]+)">(.*)</a>')
    playerIDs = {}
    for line in lines:
        m = expr.search(line)
        if m:
            playerIDs[int(m.groups()[0])] = m.groups()[1]

    with open(manifest_file, 'r') as f:
        manifest = json.load(f)
        if "status" in manifest and manifest["status"] == 404:
            print(f"No competition with ID {args.compID}")
        else:
            print(f"Fetching {len(manifest)} craft files...", end='', flush=True)
            for craft in manifest:
                craft_name = f"{playerIDs[craft['player_id']]}_{craft['name']}".replace(os.path.sep, '_')
                subprocess.run([args.curl_command, "-s", f"{craft['craft_url']}", "-o", f"{craft_name}.craft"])
                with open(f"{craft_name}.craft", 'r+') as f:
                    lines = f.read().splitlines()
                    lines[0] = f"ship = {craft_name}"
                    f.seek(0, 0)
                    f.truncate()
                    f.write('\n'.join(lines))
                print('.', end='', flush=True)
            print("done.")

finally:
    if not args.keep:
        if manifest_file.exists():
            manifest_file.unlink()
        if players_file.exists():
            players_file.unlink()
    else:
        if manifest_file.exists():
            manifest_file.rename('manifest.json')
        if players_file.exists():
            players_file.rename('players.html')
