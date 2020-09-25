using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using BDArmory.Control;
using BDArmory.Core;
using BDArmory.UI;

namespace BDArmory.Competition
{

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class BDAScoreService : MonoBehaviour
    {
        public static BDAScoreService Instance;

        private HashSet<string> activePlayers = new HashSet<string>();
        public Dictionary<string, Dictionary<string, double>> timeOfLastHitOnTarget = new Dictionary<string, Dictionary<string, double>>();
        public Dictionary<string, Dictionary<string, int>> hitsOnTarget = new Dictionary<string, Dictionary<string, int>>();
        public Dictionary<string, int> hitsOut = new Dictionary<string, int>();
        public Dictionary<string, int> hitsIn = new Dictionary<string, int>();
        public Dictionary<string, double> damageOut = new Dictionary<string, double>();
        public Dictionary<string, double> damageIn = new Dictionary<string, double>();
        public Dictionary<string, Dictionary<string, int>> killsOnTarget = new Dictionary<string, Dictionary<string, int>>();
        public Dictionary<string, int> assists = new Dictionary<string, int>();
        public Dictionary<string, int> deaths = new Dictionary<string, int>();
        public Dictionary<string, string> longestHitWeapon = new Dictionary<string, string>();
        public Dictionary<string, double> longestHitDistance = new Dictionary<string, double>();
        public Dictionary<string, int> rammedPartsOut = new Dictionary<string, int>();
        public Dictionary<string, int> rammedPartsIn = new Dictionary<string, int>();
        public Dictionary<string, int> missilePartsOut = new Dictionary<string, int>();
        public Dictionary<string, int> missilePartsIn = new Dictionary<string, int>();
        public Dictionary<string, double> missileDamageOut = new Dictionary<string, double>();
        public Dictionary<string, double> missileDamageIn = new Dictionary<string, double>();

        public enum StatusType
        {
            [Description("Offline")]
            Offline,
            [Description("Fetching Competition")]
            FetchingCompetition,
            [Description("Fetching Players")]
            FetchingPlayers,
            [Description("Waiting for Players")]
            PendingPlayers,
            [Description("Selecting a Heat")]
            FindingNextHeat,
            [Description("Fetching Heat")]
            FetchingHeat,
            [Description("Fetching Vessels")]
            FetchingVessels,
            [Description("Downloading Craft Files")]
            DownloadingCraftFiles,
            [Description("Starting Heat")]
            StartingHeat,
            [Description("Spawning Vessels")]
            SpawningVessels,
            [Description("Running Heat")]
            RunningHeat,
            [Description("Removing Vessels")]
            RemovingVessels,
            [Description("Stopping Heat")]
            StoppingHeat,
            [Description("Reporting Results")]
            ReportingResults,
            [Description("No Pending Heats")]
            StalledNoPendingHeats,
            [Description("Completed")]
            Completed,
            [Description("Cancelled")]
            Cancelled,
            [Description("Waiting")]
            Waiting,
            [Description("Stopped")]
            Stopped,
            [Description("Invalid")]
            Invalid
        }

        private bool pendingSync = false;
        private bool competitionStarted = false;
        public StatusType status = StatusType.Offline;

        private Coroutine syncCoroutine;

        //        protected CompetitionModel competition = null;

        //        protected HeatModel activeHeat = null;


        public BDAScoreClient client;

        public string vesselPath;

        void Awake()
        {
            if (Instance)
            {
                Destroy(Instance);
            }

            Instance = this;
        }

        void Update()
        {
            if (pendingSync && !Core.BDArmorySettings.REMOTE_LOGGING_ENABLED)
            {
                Debug.Log("[BDAScoreService] Cancel due to disable");
                pendingSync = false;
                if (syncCoroutine != null)
                    StopCoroutine(syncCoroutine);
                return;
            }
        }

        public void Configure(string vesselPath, string hash)
        {
            this.vesselPath = vesselPath;
            this.client = new BDAScoreClient(this, vesselPath, hash);
            if (syncCoroutine != null)
                StopCoroutine(syncCoroutine);
            syncCoroutine = StartCoroutine(SynchronizeWithService(hash));
            RemoteOrchestrationWindow.Instance.ShowWindow();
        }

        public void Cancel()
        {
            if (syncCoroutine != null)
                StopCoroutine(syncCoroutine);
            BDACompetitionMode.Instance.StopCompetition();
            pendingSync = false;
            status = status == StatusType.Waiting ? StatusType.Stopped : StatusType.Cancelled;
            if (status == StatusType.Cancelled)
            {
                Debug.Log("[BDAScoreService]: Cancelling the heat");
                // FIXME What else needs to be done to cancel a heat?
            }
            else
            {
                Debug.Log("[BDAScoreService]: Stopping score service.");
            }
        }

        public IEnumerator SynchronizeWithService(string hash)
        {
            if (pendingSync)
            {
                Debug.Log("[BDAScoreService] Sync in progress");
                yield break;
            }
            pendingSync = true;

            Debug.Log(string.Format("[BDAScoreService] Sync started {0}", hash));

            status = StatusType.FetchingCompetition;
            // first, get competition metadata
            yield return client.GetCompetition(hash);

            status = StatusType.FetchingPlayers;
            // next, get player metadata
            yield return client.GetPlayers(hash);

            // abort if we didn't receive a valid competition
            if (client.competition == null)
            {
                status = StatusType.Invalid;
                pendingSync = false;
                yield break;
            }

            switch (client.competition.status)
            {
                case 0:
                    status = StatusType.PendingPlayers;
                    // waiting for players; nothing to do
                    Debug.Log(string.Format("[BDAScoreService] Waiting for players {0}", hash));
                    break;
                case 1:
                    status = StatusType.FindingNextHeat;
                    // heats generated; find next heat
                    yield return FindNextHeat(hash);
                    break;
                case 2:
                    status = StatusType.StalledNoPendingHeats;
                    Debug.Log(string.Format("[BDAScoreService] Competition status 2 {0}", hash));
                    break;
            }

            pendingSync = false;
            Debug.Log(string.Format("[BDAScoreService] Sync completed {0}", hash));
        }

        private IEnumerator FindNextHeat(string hash)
        {
            Debug.Log(string.Format("[BDAScoreService] Find next heat for {0}", hash));

            status = StatusType.FetchingHeat;
            // fetch heat metadata
            yield return client.GetHeats(hash);

            // find an unstarted heat
            HeatModel model = client.heats.Values.FirstOrDefault(e => e.Available());
            if (model == null)
            {
                status = StatusType.StalledNoPendingHeats;
                Debug.Log(string.Format("[BDAScoreService] No inactive heat found {0}", hash));
                yield return RetryFind(hash);
            }
            else
            {
                Debug.Log(string.Format("[BDAScoreService] Found heat {1} in {0}", hash, model.order));
                yield return FetchAndExecuteHeat(hash, model);
            }
        }

        public double retryFindStartedAt = -1;
        private IEnumerator RetryFind(string hash)
        {
            retryFindStartedAt = Planetarium.GetUniversalTime();
            yield return new WaitWhile(() => Planetarium.GetUniversalTime() - retryFindStartedAt < 30);
            if (status != StatusType.Cancelled)
            {
                status = StatusType.FindingNextHeat;
                yield return FindNextHeat(hash);
            }
        }

        private IEnumerator FetchAndExecuteHeat(string hash, HeatModel model)
        {
            status = StatusType.FetchingVessels;
            // fetch vessel metadata for heat
            yield return client.GetVessels(hash, model);

            status = StatusType.DownloadingCraftFiles;
            // fetch craft files for vessels
            yield return client.GetCraftFiles(hash, model);

            status = StatusType.StartingHeat;
            // notify web service to start heat
            yield return client.StartHeat(hash, model);

            // execute heat
            int attempts = 0;
            competitionStarted = false;
            while (!competitionStarted && attempts++ < 3) // 3 attempts is plenty
            {
                yield return ExecuteHeat(hash, model);
                if (!competitionStarted)
                    switch (UI.VesselSpawner.Instance.spawnFailureReason)
                    {
                        case UI.VesselSpawner.SpawnFailureReason.None: // Successful spawning, but competition failed to start for some reason.
                        case UI.VesselSpawner.SpawnFailureReason.VesselLostParts: // Recoverable spawning failure.
                        case UI.VesselSpawner.SpawnFailureReason.TimedOut: // Recoverable spawning failure.
                            BDACompetitionMode.Instance.competitionStatus.Add("Failed to start heat due to " + UI.VesselSpawner.Instance.spawnFailureReason + ", trying again.");
                            break;
                        default: // Spawning is unrecoverable.
                            BDACompetitionMode.Instance.competitionStatus.Add("Failed to start heat due to " + UI.VesselSpawner.Instance.spawnFailureReason + ", aborting.");
                            attempts = 3;
                            break;
                    }
            }

            status = StatusType.ReportingResults;
            // report scores
            yield return SendScores(hash, model);

            status = StatusType.StoppingHeat;
            // notify web service to stop heat
            yield return client.StopHeat(hash, model);

            status = StatusType.Waiting;
            yield return RetryFind(hash);
        }

        private IEnumerator ExecuteHeat(string hash, HeatModel model)
        {
            Debug.Log(string.Format("[BDAScoreService] Running heat {0}/{1}", hash, model.order));
            UI.VesselSpawner spawner = UI.VesselSpawner.Instance;

            // orchestrate the match
            activePlayers.Clear();
            hitsOnTarget.Clear();
            hitsOut.Clear();
            hitsIn.Clear();
            damageOut.Clear();
            damageIn.Clear();
            killsOnTarget.Clear();
            deaths.Clear();
            assists.Clear();
            longestHitDistance.Clear();
            longestHitWeapon.Clear();

            status = StatusType.SpawningVessels;
            spawner.SpawnAllVesselsOnce(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS, BDArmorySettings.VESSEL_SPAWN_ALTITUDE, BDArmorySettings.VESSEL_SPAWN_DISTANCE, BDArmorySettings.VESSEL_SPAWN_EASE_IN_SPEED, true, hash);
            while (spawner.vesselsSpawning)
                yield return new WaitForFixedUpdate();
            if (!spawner.vesselSpawnSuccess)
            {
                Debug.Log("[BDAScoreService] Vessel spawning failed."); // FIXME Now what?
                yield break;
            }
            yield return new WaitForFixedUpdate();

            status = StatusType.RunningHeat;
            // NOTE: runs in separate coroutine
            BDACompetitionMode.Instance.StartCompetitionMode(BDArmorySettings.COMPETITION_DISTANCE);
            yield return new WaitForFixedUpdate(); // Give the competition start a frame to get going.

            // start timer coroutine for the duration specified in settings UI
            var duration = Core.BDArmorySettings.COMPETITION_DURATION * 60d;
            var message = "Starting " + (duration > 0 ? "a " + duration.ToString("F0") + "s" : "an unlimited") + " duration competition.";
            Debug.Log("[BDAScoreService]: " + message);
            BDACompetitionMode.Instance.competitionStatus.Add(message);
            while (BDACompetitionMode.Instance.competitionStarting)
                yield return new WaitForFixedUpdate(); // Wait for the competition to actually start.
            if (!BDACompetitionMode.Instance.competitionIsActive)
            {
                message = "Competition failed to start for heat " + hash + ".";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.Log("[BDAScoreService]: " + message);
                yield break;
            }
            competitionStarted = true;
            while (BDACompetitionMode.Instance.competitionIsActive) // Wait for the competition to finish (limited duration and log dumping is handled directly by the competition now).
                yield return new WaitForSeconds(1);
        }

        private IEnumerator SendScores(string hash, HeatModel heat)
        {
            var records = BuildRecords(hash, heat);
            yield return client.PostRecords(hash, heat.id, records.ToList());
        }

        private List<RecordModel> BuildRecords(string hash, HeatModel heat)
        {
            List<RecordModel> results = new List<RecordModel>();
            var playerNames = activePlayers;
            Debug.Log(string.Format("[BDAScoreService] Building records for {0} players", playerNames.Count));
            foreach (string playerName in playerNames)
            {
                PlayerModel player = client.players.Values.FirstOrDefault(e => e.name == playerName);
                if (player == null)
                {
                    Debug.Log(string.Format("[BDAScoreService] Unmatched player {0}", playerName));
                    Debug.Log("DEBUG players were " + string.Join(", ", client.players.Values));
                    continue;
                }
                VesselModel vessel = client.vessels.Values.FirstOrDefault(e => e.player_id == player.id);
                if (vessel == null)
                {
                    Debug.Log(string.Format("[BDAScoreService] Unmatched vessel for playerId {0}", player.id));
                    Debug.Log("DEBUG vessels were " + string.Join(", ", client.vessels.Values.Select(p => p.id)));
                    continue;
                }
                RecordModel record = new RecordModel();
                record.vessel_id = vessel.id;
                record.competition_id = int.Parse(hash);
                record.heat_id = heat.id;
                record.hits_out = ComputeTotalHitsOut(player.name);
                record.hits_in = ComputeTotalHitsIn(player.name);
                record.dmg_out = ComputeTotalDamageOut(player.name);
                record.dmg_in = ComputeTotalDamageIn(player.name);
                record.ram_parts_out = ComputeTotalRammedPartsOut(player.name);
                record.ram_parts_in = ComputeTotalRammedPartsIn(player.name);
                record.mis_parts_out = ComputeTotalMissilePartsOut(player.name);
                record.mis_parts_in = ComputeTotalMissilePartsIn(player.name);
                record.mis_dmg_out = ComputeTotalMissileDamageOut(player.name);
                record.mis_dmg_in = ComputeTotalMissileDamageIn(player.name);
                record.kills = ComputeTotalKills(player.name);
                record.deaths = ComputeTotalDeaths(player.name);
                record.assists = ComputeTotalAssists(player.name);
                if (longestHitDistance.ContainsKey(player.name))
                {
                    record.distance = (float)longestHitDistance[player.name];
                    record.weapon = longestHitWeapon[player.name];
                }
                results.Add(record);
            }
            Debug.Log(string.Format("[BDAScoreService] Built records for {0} players", results.Count));
            return results;
        }

        private int ComputeTotalHitsOut(string playerName)
        {
            int result = 0;
            if (hitsOut.ContainsKey(playerName))
            {
                result = hitsOut[playerName];
            }
            return result;
        }

        private int ComputeTotalHitsIn(string playerName)
        {
            int result = 0;
            if (hitsIn.ContainsKey(playerName))
            {
                result = hitsIn[playerName];
            }
            return result;
        }

        private double ComputeTotalDamageOut(string playerName)
        {
            double result = 0;
            if (damageOut.ContainsKey(playerName))
            {
                result = damageOut[playerName];
            }
            return result;
        }

        private double ComputeTotalDamageIn(string playerName)
        {
            double result = 0;
            if (damageIn.ContainsKey(playerName))
            {
                result = damageIn[playerName];
            }
            return result;
        }

        private int ComputeTotalRammedPartsOut(string playerName)
        {
            if (rammedPartsOut.ContainsKey(playerName))
                return rammedPartsOut[playerName];
            return 0;
        }

        private int ComputeTotalRammedPartsIn(string playerName)
        {
            if (rammedPartsIn.ContainsKey(playerName))
                return rammedPartsIn[playerName];
            return 0;
        }

        private int ComputeTotalMissilePartsOut(string playerName)
        {
            if (missilePartsOut.ContainsKey(playerName))
                return missilePartsOut[playerName];
            return 0;
        }

        private int ComputeTotalMissilePartsIn(string playerName)
        {
            if (missilePartsIn.ContainsKey(playerName))
                return missilePartsIn[playerName];
            return 0;
        }

        private double ComputeTotalMissileDamageOut(string playerName)
        {
            if (missileDamageOut.ContainsKey(playerName))
                return missileDamageOut[playerName];
            return 0;
        }

        private double ComputeTotalMissileDamageIn(string playerName)
        {
            if (missileDamageIn.ContainsKey(playerName))
                return missileDamageIn[playerName];
            return 0;
        }

        private int ComputeTotalKills(string playerName)
        {
            int result = 0;
            if (killsOnTarget.ContainsKey(playerName))
            {
                result = killsOnTarget[playerName].Values.Sum();
            }
            return result;
        }

        private int ComputeTotalDeaths(string playerName)
        {
            int result = 0;
            if (deaths.ContainsKey(playerName))
            {
                result = deaths[playerName];
            }
            return result;
        }

        private int ComputeTotalAssists(string playerName)
        {
            int result = 0;
            if (assists.ContainsKey(playerName))
            {
                result = assists[playerName];
            }
            return result;
        }

        public void TrackDamage(string attacker, string target, double damage)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
            {
                Debug.Log(string.Format("[BDAScoreService] TrackDamage by {0} on {1} for {2}hp", target, attacker, damage));
            }
            activePlayers.Add(attacker);
            activePlayers.Add(target);
            if (damageOut.ContainsKey(attacker))
            {
                damageOut[attacker] += damage;
            }
            else
            {
                damageOut.Add(attacker, damage);
            }
            if (damageIn.ContainsKey(target))
            {
                damageIn[target] += damage;
            }
            else
            {
                damageIn.Add(target, damage);
            }
        }

        public void TrackMissileDamage(string attacker, string target, double damage)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
            {
                Debug.Log(string.Format("[BDAScoreService] TrackMissileDamage by {0} on {1} for {2}hp", target, attacker, damage));
            }
            activePlayers.Add(attacker);
            activePlayers.Add(target);
            if (missileDamageOut.ContainsKey(attacker))
            {
                missileDamageOut[attacker] += damage;
            }
            else
            {
                missileDamageOut.Add(attacker, damage);
            }
            if (missileDamageIn.ContainsKey(target))
            {
                missileDamageIn[target] += damage;
            }
            else
            {
                missileDamageIn.Add(target, damage);
            }
        }

        public void TrackMissileParts(string attacker, string target, int count)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
                Debug.Log(string.Format("[BDAScoreService] TrackMissileParts by {0} on {1} for {2}parts", target, attacker, count));

            double now = Planetarium.GetUniversalTime();
            activePlayers.Add(attacker);
            activePlayers.Add(target);
            if (missilePartsOut.ContainsKey(attacker))
                missilePartsOut[attacker] += count;
            else
                missilePartsOut.Add(attacker, count);
            if (missilePartsIn.ContainsKey(target))
                missilePartsIn[target] += count;
            else
                missilePartsIn.Add(target, count);

            if (timeOfLastHitOnTarget.ContainsKey(attacker))
            {
                if (timeOfLastHitOnTarget[attacker].ContainsKey(target))
                    timeOfLastHitOnTarget[attacker][target] = now;
                else
                    timeOfLastHitOnTarget[attacker].Add(target, now);
            }
            else
            {
                timeOfLastHitOnTarget.Add(attacker, new Dictionary<string, double> { { target, now } });
            }
        }

        public void TrackRammedParts(string attacker, string target, int count)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
                Debug.Log(string.Format("[BDAScoreService] TrackRammedParts by {0} on {1} for {2}parts", target, attacker, count));

            double now = Planetarium.GetUniversalTime();
            activePlayers.Add(attacker);
            activePlayers.Add(target);
            if (rammedPartsOut.ContainsKey(attacker))
                rammedPartsOut[attacker] += count;
            else
                rammedPartsOut.Add(attacker, count);
            if (rammedPartsIn.ContainsKey(target))
                rammedPartsIn[target] += count;
            else
                rammedPartsIn.Add(target, count);

            if (timeOfLastHitOnTarget.ContainsKey(attacker))
            {
                if (timeOfLastHitOnTarget[attacker].ContainsKey(target))
                    timeOfLastHitOnTarget[attacker][target] = now;
                else
                    timeOfLastHitOnTarget[attacker].Add(target, now);
            }
            else
            {
                timeOfLastHitOnTarget.Add(attacker, new Dictionary<string, double> { { target, now } });
            }
        }

        public void TrackHit(string attacker, string target, string weaponName, double hitDistance)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
            {
                Debug.Log(string.Format("[BDAScoreService] TrackHit by {0} on {1} with {2} at {3}m", target, attacker, weaponName, hitDistance));
            }
            double now = Planetarium.GetUniversalTime();
            activePlayers.Add(attacker);
            activePlayers.Add(target);
            if (hitsOnTarget.ContainsKey(attacker))
            {
                if (hitsOnTarget[attacker].ContainsKey(target))
                {
                    ++hitsOnTarget[attacker][target];
                }
                else
                {
                    hitsOnTarget[attacker].Add(target, 1);
                }
            }
            else
            {
                var newHits = new Dictionary<string, int>();
                newHits.Add(target, 1);
                hitsOnTarget.Add(attacker, newHits);
            }
            if (hitsOut.ContainsKey(attacker))
            {
                ++hitsOut[attacker];
            }
            else
            {
                hitsOut.Add(attacker, 1);
            }
            if (hitsIn.ContainsKey(target))
            {
                ++hitsIn[target];
            }
            else
            {
                hitsIn.Add(target, 1);
            }
            if (!longestHitDistance.ContainsKey(attacker) || hitDistance > longestHitDistance[attacker])
            {
                Debug.Log(string.Format("[BDACompetitionMode] Tracked longest hit for {0} with {1} at {2}m", attacker, weaponName, hitDistance));
                if (longestHitDistance.ContainsKey(attacker))
                {
                    longestHitWeapon[attacker] = weaponName;
                    longestHitDistance[attacker] = hitDistance;
                }
                else
                {
                    longestHitWeapon.Add(attacker, weaponName);
                    longestHitDistance.Add(attacker, hitDistance);
                }
            }
            if (timeOfLastHitOnTarget.ContainsKey(attacker))
            {
                if (timeOfLastHitOnTarget[attacker].ContainsKey(target))
                {
                    timeOfLastHitOnTarget[attacker][target] = now;
                }
                else
                {
                    timeOfLastHitOnTarget[attacker].Add(target, now);
                }
            }
            else
            {
                var newTimeOfLast = new Dictionary<string, double>();
                newTimeOfLast.Add(target, now);
                timeOfLastHitOnTarget.Add(attacker, newTimeOfLast);
            }
        }

        public void ComputeAssists(string target, string killer = "", double timeLimit = 30)
        {
            var now = Planetarium.GetUniversalTime();
            var thresholdTime = now - timeLimit; // anyone who hit this target within the last 30sec

            foreach (var attacker in timeOfLastHitOnTarget.Keys)
            {
                if (attacker == killer) continue; // Don't award assists to the killer.
                if (timeOfLastHitOnTarget[attacker].ContainsKey(target) && timeOfLastHitOnTarget[attacker][target] > thresholdTime)
                {
                    if (assists.ContainsKey(attacker))
                    {
                        ++assists[attacker];
                    }
                    else
                    {
                        assists.Add(attacker, 1);
                    }
                }
            }
        }

        /**
         * Tracks an unattributed death, where no clear attacker exists.
         */
        public void TrackDeath(string target)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
            {
                Debug.Log(string.Format("[BDAScoreService] TrackDeath for {0}", target));
            }
            activePlayers.Add(target);
            IncrementDeath(target);
        }

        private void IncrementDeath(string target)
        {
            if (deaths.ContainsKey(target))
            {
                if (BDArmorySettings.DRAW_DEBUG_LABELS)
                {
                    Debug.Log(string.Format("[BDAScoreService] IncrementDeaths for {0}", target));
                }
                ++deaths[target];
            }
            else
            {
                if (BDArmorySettings.DRAW_DEBUG_LABELS)
                {
                    Debug.Log(string.Format("[BDAScoreService] FirstDeath for {0}", target));
                }
                deaths.Add(target, 1);
            }
        }

        /**
         * Tracks a clean kill, when an attacker decisively kills the target.
         */
        public void TrackKill(string attacker, string target)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
            {
                Debug.Log(string.Format("[BDAScoreService] TrackKill {0} by {1}", target, attacker));
            }
            activePlayers.Add(attacker);
            activePlayers.Add(target);

            IncrementKill(attacker, target);
            ComputeAssists(target, attacker, 30);
        }

        private void IncrementKill(string attacker, string target)
        {
            // increment kill counter
            if (killsOnTarget.ContainsKey(attacker))
            {
                if (killsOnTarget[attacker].ContainsKey(target))
                {
                    if (BDArmorySettings.DRAW_DEBUG_LABELS)
                    {
                        Debug.Log(string.Format("[BDAScoreService] IncrementKills for {0} on {1}", attacker, target));
                    }
                    ++killsOnTarget[attacker][target];
                }
                else
                {
                    if (BDArmorySettings.DRAW_DEBUG_LABELS)
                    {
                        Debug.Log(string.Format("[BDAScoreService] Kill for {0} on {1}", attacker, target));
                    }
                    killsOnTarget[attacker].Add(target, 1);
                }
            }
            else
            {
                if (BDArmorySettings.DRAW_DEBUG_LABELS)
                {
                    Debug.Log(string.Format("[BDAScoreService] FirstKill for {0} on {1}", attacker, target));
                }
                var newKills = new Dictionary<string, int>();
                newKills.Add(target, 1);
                killsOnTarget.Add(attacker, newKills);
            }
        }

        public string Status()
        {
            return status.ToString();
        }

        public class JsonListHelper<T>
        {
            [Serializable]
            private class Wrapper<S>
            {
                public S[] items;
            }
            public List<T> FromJSON(string json)
            {
                if (json == null)
                {
                    return new List<T>();
                }
                //string wrappedJson = string.Format("{{\"items\":{0}}}", json);
                Wrapper<T> wrapper = new Wrapper<T>();
                wrapper.items = JsonUtility.FromJson<T[]>(json);
                if (wrapper == null || wrapper.items == null)
                {
                    Debug.Log(string.Format("[BDAScoreService] Failed to decode {0}", json));
                    return new List<T>();
                }
                else
                {
                    return new List<T>(wrapper.items);
                }
            }
        }
    }


}
