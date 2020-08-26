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

namespace BDArmory.Competition
{

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class BDAScoreService : MonoBehaviour
    {
        public static BDAScoreService Instance;

        private HashSet<string> activePlayers = new HashSet<string>();
        public Dictionary<string, Dictionary<string, int>> hitsOnTarget = new Dictionary<string, Dictionary<string, int>>();
        public Dictionary<string, Dictionary<string, int>> killsOnTarget = new Dictionary<string, Dictionary<string, int>>();
        public Dictionary<string, int> deaths = new Dictionary<string, int>();
        public Dictionary<string, string> longestHitWeapon = new Dictionary<string, string>();
        public Dictionary<string, double> longestHitDistance = new Dictionary<string, double>();

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
            [Description("Invalid")]
            Invalid
        }

        private bool pendingSync = false;
        private StatusType status = StatusType.Offline;

        private Coroutine syncCoroutine;

        //        protected CompetitionModel competition = null;

        //        protected HeatModel activeHeat = null;


        public BDAScoreClient client;

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
                StopCoroutine(syncCoroutine);
                return;
            }
        }

        public void Configure(string vesselPath, string hash)
        {
            this.client = new BDAScoreClient(this, vesselPath, hash);
            syncCoroutine = StartCoroutine(SynchronizeWithService(hash));
        }

        public void Cancel()
        {
            if (syncCoroutine != null)
                StopCoroutine(syncCoroutine);
            BDACompetitionMode.Instance.StopCompetition();
            pendingSync = false;
            status = StatusType.Cancelled;
            Debug.Log("[BDAScoreService] Cancelling the heat");
            // FIXME What else needs to be done to cancel a heat?
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
                syncCoroutine = null;
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

        private IEnumerator RetryFind(string hash)
        {
            yield return new WaitForSeconds(30);
            yield return FindNextHeat(hash);
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
            yield return ExecuteHeat(hash, model);

            status = StatusType.ReportingResults;
            // report scores
            yield return SendScores(hash, model);

            status = StatusType.StoppingHeat;
            // notify web service to stop heat
            yield return client.StopHeat(hash, model);

            status = StatusType.FindingNextHeat;
            yield return RetryFind(hash);
        }

        private IEnumerator ExecuteHeat(string hash, HeatModel model)
        {
            Debug.Log(string.Format("[BDAScoreService] Running heat {0}/{1}", hash, model.order));
            UI.VesselSpawner spawner = UI.VesselSpawner.Instance;

            // orchestrate the match
            activePlayers.Clear();
            hitsOnTarget.Clear();
            killsOnTarget.Clear();
            deaths.Clear();

            status = StatusType.SpawningVessels;
            spawner.SpawnAllVesselsOnce(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS, 1, true, hash); // FIXME If geo-coords are included in the heat model, then use those instead.
            while (spawner.vesselsSpawning)
                yield return new WaitForFixedUpdate();
            if (!spawner.vesselSpawnSuccess)
            {
                Debug.Log("[BDAScoreService] Vessel spawning failed."); // FIXME Now what?
                yield break;
            }
            // if (CompetitionHub != null) // Example of how to spawn extra vessels from another folder.
            // {
            //     spawner.SpawnAllVesselsOnce(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS, 1, false, hash+"/"+hubCraftPath);
            //     while (spawner.vesselsSpawning)
            //         yield return new WaitForFixedUpdate();
            //     if (!spawner.vesselSpawnSuccess)
            //     {
            //         Debug.Log("[BDAScoreService] Vessel spawning failed for CompetitionHub."); // FIXME Now what?
            //         yield break;
            //     }
            // }
            UI.LoadedVesselSwitcher.Instance.DoPostVesselSpawn();
            yield return new WaitForSeconds(2);

            // set all players on unique teams
            AssignTeams();
            yield return new WaitForSeconds(2);

            // focus on a vehicle before starting
            var vessel = FlightGlobals.Vessels.FirstOrDefault();
            if (vessel != null)
            {
                FlightGlobals.ForceSetActiveVessel(vessel);
                FlightInputHandler.ResumeVesselCtrlState(vessel);
            }

            // wait 3sec grace
            yield return new WaitForSeconds(3);

            status = StatusType.RunningHeat;
            // NOTE: runs in separate coroutine
            BDACompetitionMode.Instance.StartCompetitionMode(1000);

            // start timer coroutine for the duration specified in settings UI
            var duration = Core.BDArmorySettings.COMPETITION_DURATION * 60f;
            Debug.Log("[BDAScoreService] Starting a " + duration.ToString("F0") + "s duration competition.");
            var startTime = Planetarium.GetUniversalTime();
            while ((BDACompetitionMode.Instance.competitionStarting || BDACompetitionMode.Instance.competitionIsActive) && Planetarium.GetUniversalTime() - startTime < duration) // Allow exiting if the competition finishes early.
                yield return new WaitForSeconds(1);

            // stop competition
            BDACompetitionMode.Instance.StopCompetition();
            BDACompetitionMode.Instance.LogResults("for BDAScoreService"); // Make sure the results are dumped to the log.

            // status = StatusType.RemovingVessels;
            // // remove all spawned vehicles // Note: Vessel and debris clean-up happens during vessel spawning (also the currently focussed vessel doesn't get killed when telling it to Die...)
            // foreach (Vessel v in FlightGlobals.Vessels.Where(e => !e.vesselName.Equals("CompetitionHub")))
            // {
            //     v.Die();
            // }
        }

        private void AssignTeams()
        {
            char T = 'A';
            // switch everyone to their own teams
            var allPilots = new List<Modules.MissileFire>(FlightGlobals.Vessels.Count());
            using (var allVessels = FlightGlobals.Vessels.GetEnumerator())
                while (allVessels.MoveNext())
                    using (var parts = allVessels.Current.Parts.GetEnumerator())
                        while (parts.MoveNext())
                            using (var modules = parts.Current.Modules.GetEnumerator())
                                while( modules.MoveNext())
                            {
                                if (modules.Current == null || modules.Current.name != "MissileFire") continue;
                                allPilots.Add(modules.Current as Modules.MissileFire);
                            }

            foreach (var pilot in allPilots)
            {
                pilot.SetTeam(Misc.BDTeam.Get(T.ToString()));
                T++;
            }
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
                record.hits = ComputeTotalHits(player.name);
                record.kills = ComputeTotalKills(player.name);
                record.deaths = ComputeTotalDeaths(player.name);
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

        private int ComputeTotalHits(string playerName)
        {
            int result = 0;
            if (hitsOnTarget.ContainsKey(playerName))
            {
                result = hitsOnTarget[playerName].Values.Sum();
            }
            return result;
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

        public void TrackHit(string attacker, string target, string weaponName, double hitDistance)
        {
            Debug.Log(string.Format("[BDAScoreService] TrackHit {0} by {1} with {2} at {3}m", target, attacker, weaponName, hitDistance));
            activePlayers.Add(attacker);
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
            if (!longestHitDistance.ContainsKey(attacker) || hitDistance > longestHitDistance[attacker])
            {
                Debug.Log(string.Format("[BDACompetitionMode] Tracked hit for {0} with {1} at {2}", attacker, weaponName, hitDistance));
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
        }

        public void TrackKill(string attacker, string target)
        {
            List<string> list = new List<string>();
            list.Add(attacker);
            TrackKill(list, target);
        }

        public void TrackKill(List<string> attackers, string target)
        {
            Debug.Log(string.Format("[BDAScoreService] TrackKill {0} by {1}", target, string.Join(", ", attackers)));
            attackers.ForEach(e => activePlayers.Add(e));
            if (deaths.ContainsKey(target))
            {
                Debug.Log(string.Format("[BDAScoreService] IncrementDeaths for {0}", target));
                ++deaths[target];
            }
            else
            {
                Debug.Log(string.Format("[BDAScoreService] FirstDeath for {0}", target));
                deaths.Add(target, 1);
            }

            foreach (string attacker in attackers)
            {
                if (killsOnTarget.ContainsKey(attacker))
                {
                    if (killsOnTarget[attacker].ContainsKey(target))
                    {
                        Debug.Log(string.Format("[BDAScoreService] IncrementKills for {0} on {1}", attacker, target));
                        ++killsOnTarget[attacker][target];
                    }
                    else
                    {
                        Debug.Log(string.Format("[BDAScoreService] Kill for {0} on {1}", attacker, target));
                        killsOnTarget[attacker].Add(target, 1);
                    }
                }
                else
                {
                    Debug.Log(string.Format("[BDAScoreService] FirstKill for {0} on {1}", attacker, target));
                    var newKills = new Dictionary<string, int>();
                    newKills.Add(target, 1);
                    killsOnTarget.Add(attacker, newKills);
                }
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
