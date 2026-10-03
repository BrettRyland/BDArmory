using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

using BDArmory.Competition;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;

namespace BDArmory.VesselSpawning
{
    /// <summary>
    /// Spawning of a group of craft in a ring.
    /// 
    /// This is the default spawning code for RWP competitions currently and is essentially what the CircularSpawnStrategy needs to perform before it can take over as the default.
    /// 
    /// TODO:
    /// The central block of the SpawnAllVesselsOnce function should eventually switch to using SingleVesselSpawning.Instance.SpawnVessel (plus local coroutines for the extra stuff) to do the actual spawning of the vessels once that's ready.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CircularSpawning : VesselSpawnerBase
    {
        public static CircularSpawning Instance;
        protected override void Awake()
        {
            base.Awake();
            if (Instance != null) Destroy(Instance);
            Instance = this;
        }

        void LogMessage(string message, bool toScreen = true, bool toLog = true) => LogMessageFrom("CircularSpawning", message, toScreen, toLog);

        public override IEnumerator Spawn(SpawnConfig spawnConfig)
        {
            var circularSpawnConfig = spawnConfig as CircularSpawnConfig;
            if (circularSpawnConfig == null)
            {
                Debug.LogError($"[BDArmory.CircularSpawning]: SpawnConfig wasn't a valid CircularSpawnConfig");
                yield break;
            }
            yield return SpawnAllVesselsOnceAsCoroutine(circularSpawnConfig);
        }
        public void CancelSpawning()
        {
            // Single spawn
            if (vesselsSpawning)
            {
                vesselsSpawning = false;
                LogMessage("Vessel spawning cancelled.");
            }
            if (spawnAllVesselsOnceCoroutine != null)
            {
                StopCoroutine(spawnAllVesselsOnceCoroutine);
                spawnAllVesselsOnceCoroutine = null;
            }

            // Continuous single spawn
            if (vesselsSpawningOnceContinuously)
            {
                vesselsSpawningOnceContinuously = false;
                LogMessage("Continuous single spawning cancelled.");
            }
            if (spawnAllVesselsOnceContinuouslyCoroutine != null)
            {
                StopCoroutine(spawnAllVesselsOnceContinuouslyCoroutine);
                spawnAllVesselsOnceContinuouslyCoroutine = null;
            }

            // Team spawn
            if (teamSpawnCoroutine != null)
            {
                StopCoroutine(teamSpawnCoroutine);
                teamSpawnCoroutine = null;
            }
        }

        #region Single spawning
        /// <summary>
        /// Prespawn initialisation to handle camera and body changes and to ensure that only a single spawning coroutine is running.
        /// Note: This currently has some specifics to the SpawnAllVesselsOnceCoroutine, so it may not be suitable for other spawning strategies yet.
        /// </summary>
        /// <param name="spawnConfig">The spawn config for the new spawning.</param>
        public override void PreSpawnInitialisation(SpawnConfig spawnConfig)
        {
            base.PreSpawnInitialisation(spawnConfig);

            vesselsSpawning = true; // Signal that we've started the spawning vessels routine.
            vesselSpawnSuccess = false; // Set our success flag to false for now.
            spawnFailureReason = SpawnFailureReason.None; // Reset the spawn failure reason.
            if (spawnAllVesselsOnceCoroutine != null)
                StopCoroutine(spawnAllVesselsOnceCoroutine);
        }

        public void SpawnAllVesselsOnce(int worldIndex, double latitude, double longitude, double altitude = 0, float distance = 10f, bool absDistanceOrFactor = false, float intraTeamSeparation = 0, float refHeading = 0, bool killEverythingFirst = true, bool assignTeams = true, int numberOfTeams = 0, List<int> teamCounts = null, List<List<string>> teamsSpecific = null, string spawnFolder = null, List<string> craftFiles = null)
        {
            SpawnAllVesselsOnce(new CircularSpawnConfig(new SpawnConfig(worldIndex, latitude, longitude, altitude, killEverythingFirst, assignTeams, numberOfTeams, teamCounts, teamsSpecific, spawnFolder, craftFiles), distance, absDistanceOrFactor, intraTeamSeparation, refHeading));
        }

        public void SpawnAllVesselsOnce(CircularSpawnConfig spawnConfig)
        {
            PreSpawnInitialisation(spawnConfig);
            spawnAllVesselsOnceCoroutine = StartCoroutine(SpawnAllVesselsOnceCoroutine(spawnConfig));
            LogMessage("Triggering vessel spawning at " + spawnConfig.latitude.ToString("G6") + ", " + spawnConfig.longitude.ToString("G6") + ", with altitude " + spawnConfig.altitude + "m.", false);
        }

        /// <summary>
        /// A coroutine version of the SpawnAllVesselsOnce function that performs the required prespawn initialisation.
        /// 
        /// Note: this is only called via the deprecated RemoteOrchestration SpawnStrategies.
        /// </summary>
        /// <param name="spawnConfig">The spawn config to use.</param>
        public IEnumerator SpawnAllVesselsOnceAsCoroutine(CircularSpawnConfig spawnConfig)
        {
            PreSpawnInitialisation(spawnConfig);
            SpawnUtils.ResetVesselNamingDeconfliction();
            LogMessage("Triggering vessel spawning at " + spawnConfig.latitude.ToString("G6") + ", " + spawnConfig.longitude.ToString("G6") + ", with altitude " + spawnConfig.altitude + "m.", false);
            yield return SpawnAllVesselsOnceCoroutine(spawnConfig);
        }

        private Coroutine spawnAllVesselsOnceCoroutine;
        // Spawns all vessels in an outward facing ring and lowers them to the ground. An altitude of 5m should be suitable for most cases.
        private IEnumerator SpawnAllVesselsOnceCoroutine(CircularSpawnConfig spawnConfig)
        {
            #region Initialisation and sanity checks
            // Tally up the craft to spawn and figure out teams.
            if (spawnConfig.teamsSpecific == null)
            {
                var spawnFolder = Path.Combine(AutoSpawnPath, spawnConfig.folder);
                if (!Directory.Exists(spawnFolder))
                {
                    LogMessage($"Spawn folder {spawnFolder} doesn't exist!");
                    vesselsSpawning = false;
                    spawnFailureReason = SpawnFailureReason.NoCraft;
                    SpawnUtils.RevertSpawnLocationCamera(true, true);
                    yield break;
                }
                if (spawnConfig.numberOfTeams == 1) // Scan subfolders
                {
                    spawnConfig.teamsSpecific = new List<List<string>>();
                    var teamDirs = Directory.GetDirectories(spawnFolder);
                    if (teamDirs.Length < 2) // Make teams from each vessel in the spawn folder. Allow for a single subfolder for putting bad craft or other tmp things in.
                    {
                        spawnConfig.numberOfTeams = -1; // Flag for treating craft files as folder names.
                        spawnConfig.craftFiles = Directory.GetFiles(Path.GetFullPath(spawnFolder)).Where(f => f.EndsWith(".craft")).ToList();
                        spawnConfig.teamsSpecific = spawnConfig.craftFiles.Select(f => new List<string> { f }).ToList();
                    }
                    else
                    {
                        LogMessage("Spawning teams from folders " + string.Join(", ", teamDirs.Select(d => d.Substring(AutoSpawnPath.Length))), false);
                        foreach (var teamDir in teamDirs)
                        {
                            spawnConfig.teamsSpecific.Add(Directory.GetFiles(Path.GetFullPath(teamDir), "*.craft").ToList());
                        }
                        spawnConfig.craftFiles = spawnConfig.teamsSpecific.SelectMany(v => v.ToList()).ToList();
                    }
                }
                else // Just the specified folder.
                {
                    if (spawnConfig.craftFiles == null) // Prioritise the list of craftFiles if we're given them.
                        spawnConfig.craftFiles = Directory.GetFiles(Path.GetFullPath(spawnFolder)).Where(f => f.EndsWith(".craft")).ToList();
                }
            }
            else // Spawn the specific vessels.
            {
                spawnConfig.craftFiles = spawnConfig.teamsSpecific.SelectMany(v => v.ToList()).ToList();
            }
            if (spawnConfig.craftFiles.Count == 0)
            {
                LogMessage("Vessel spawning: found no craft files in " + Path.Combine(AutoSpawnPath, spawnConfig.folder));
                vesselsSpawning = false;
                spawnFailureReason = SpawnFailureReason.NoCraft;
                SpawnUtils.RevertSpawnLocationCamera(true, true);
                yield break;
            }
            bool useOriginalTeamNames = spawnConfig.assignTeams && (spawnConfig.numberOfTeams == 1 || spawnConfig.numberOfTeams == -1); // We'll be using the folders or craft filenames as team names in the originalTeams dictionary.
            if (spawnConfig.teamsSpecific != null && !useOriginalTeamNames)
            {
                spawnConfig.teamCounts = spawnConfig.teamsSpecific.Select(tl => tl.Count).ToList();
            }
            if (BDArmorySettings.VESSEL_SPAWN_RANDOM_ORDER) spawnConfig.craftFiles.Shuffle(); // Randomise the spawn order.
            int spawnedVesselCount = 0; // Reset our spawned vessel count.
            var spawnAirborne = spawnConfig.altitude > 10;
            var spawnBody = FlightGlobals.Bodies[spawnConfig.worldIndex];
            var spawnInOrbit = spawnConfig.altitude >= spawnBody.MinSafeAltitude(); // Min safe orbital altitude
            var withInitialVelocity = spawnAirborne && BDArmorySettings.VESSEL_SPAWN_INITIAL_VELOCITY;
            var spawnPitch = (withInitialVelocity || spawnInOrbit) ? 0f : -80f;
            bool PinataMode = false;
            foreach (var craftUrl in spawnConfig.craftFiles)
            {
                if (!string.IsNullOrEmpty(BDArmorySettings.PINATA_NAME) && craftUrl.Contains(BDArmorySettings.PINATA_NAME)) PinataMode = true;
            }
            var spawnDistance = spawnConfig.craftFiles.Count > 1 ? (spawnConfig.absDistanceOrFactor ? spawnConfig.distance : (spawnConfig.distance + spawnConfig.distance * (spawnConfig.craftFiles.Count - (PinataMode ? 1 : 0)))) : 0f; // If it's a single craft, spawn it at the spawn point.

            LogMessage($"Spawning {spawnConfig.craftFiles.Count - (PinataMode ? 1 : 0)} vessels at an altitude of {(spawnConfig.altitude < 1000 ? $"{spawnConfig.altitude:G5}m" : $"{spawnConfig.altitude / 1000:G5}km")} ({(spawnInOrbit ? "in orbit" : spawnAirborne ? "airborne" : "landed")}){(spawnConfig.craftFiles.Count > 8 ? ", this may take some time..." : ".")}");
            #endregion

            yield return AcquireSpawnPoint(spawnConfig, spawnDistance, spawnAirborne);
            if (spawnFailureReason != SpawnFailureReason.None)
            {
                vesselsSpawning = false;
                SpawnUtils.RevertSpawnLocationCamera(true, true);
                yield break;
            }

            #region Spawn layout configuration
            // Spawn the craft in an outward facing ring. If using teams, cluster the craft around each team spawn point.
            var radialUnitVector = (spawnPoint - FlightGlobals.currentMainBody.transform.position).normalized;
            var refDirection = new Func<Vector3>(() =>
            { // Uses https://www.movable-type.co.uk/scripts/latlong.html to calculate the GPS point at a distance of 100m along the requested heading from the spawn point.
              // This loses accuracy near the poles, but is correct within ±89° latitude.
                float delta = 100f / (float)FlightGlobals.currentMainBody.Radius;
                float theta = Mathf.Deg2Rad * spawnConfig.refHeading;
                float phi1 = Mathf.Deg2Rad * (float)spawnConfig.latitude;
                float phi2 = Mathf.Asin(Mathf.Sin(phi1) * Mathf.Cos(delta) + Mathf.Cos(phi1) * Mathf.Sin(delta) * Mathf.Cos(theta));
                var lat = Mathf.Rad2Deg * phi2;
                var lon = spawnConfig.longitude + Mathf.Rad2Deg * Mathf.Atan2(Mathf.Sin(theta) * Mathf.Sin(delta) * Mathf.Cos(phi1), Mathf.Cos(delta) - Mathf.Sin(phi1) * Mathf.Sin(phi2));
                var offset = FlightGlobals.currentMainBody.GetWorldSurfacePosition(lat, lon, FlightGlobals.currentMainBody.GetAltitude(spawnPoint));
                return (offset - spawnPoint).ProjectOnPlanePreNormalized(radialUnitVector).normalized;
            })();
            if (refDirection == Vector3.zero) refDirection = Vector3.forward; // This only happens within 0.02° of planetary poles (~100m).
            var vesselSpawnConfigs = new List<VesselSpawnConfig>();
            if (BDArmorySettings.WAYPOINTS_MODE && !BDArmorySettings.WAYPOINTS_ONE_AT_A_TIME && !spawnAirborne)
            {
                //TODO - move over to SpawnCustomTemplate once TournamentCoordinator deprecated
                var direction = (Quaternion.AngleAxis(0, radialUnitVector) * refDirection).ProjectOnPlanePreNormalized(radialUnitVector).normalized;

                float craftSeparation = spawnConfig.intraTeamSeparation > 0 ? spawnConfig.intraTeamSeparation / 1.05409255f : 3f * BDAMath.Sqrt(spawnDistance); // 1.05409255f factor accounts for forward separation to give the asked for separation between craft.
                var spreadDirection = Vector3.Cross(radialUnitVector, direction);
                var facingDirection = direction;
                int rankCount = 0;
                float gridAdjustedSpawnCount = 0;
                foreach (var craftUrl in spawnConfig.craftFiles)
                {
                    ++gridAdjustedSpawnCount;
                    if (gridAdjustedSpawnCount > 3)
                    {
                        gridAdjustedSpawnCount = 1;
                        rankCount++;
                    }
                    var position = spawnPoint
                        + craftSeparation * (gridAdjustedSpawnCount == 1 ? 0 : gridAdjustedSpawnCount == 2 ? -gridAdjustedSpawnCount / 2 : gridAdjustedSpawnCount / 2) * spreadDirection
                        + craftSeparation * rankCount * -facingDirection;
                    vesselSpawnConfigs.Add(new VesselSpawnConfig(
                        craftUrl,
                        position,
                        facingDirection,
                        (float)spawnConfig.altitude,
                        spawnPitch,
                        spawnAirborne,
                        spawnInOrbit,
                        reuseURLVesselName: (BDATournament.Instance.tournamentStatus == TournamentStatus.Running && !BDATournament.Instance.fullTeams) || TournamentCoordinator.Instance.IsRunning
                    ));
                    ++spawnedVesselCount;
                }
            }
            else if (spawnConfig.teamsSpecific == null)
            {
                foreach (var craftUrl in spawnConfig.craftFiles)
                {
                    // Figure out spawn point and orientation
                    var heading = 360f * spawnedVesselCount / Mathf.Max(1, spawnConfig.craftFiles.Count - (PinataMode ? 1 : 0));
                    var direction = (Quaternion.AngleAxis(heading, radialUnitVector) * refDirection).ProjectOnPlanePreNormalized(radialUnitVector).normalized;
                    Vector3 position = spawnPoint;
                    if (!PinataMode || (PinataMode && !craftUrl.Contains(BDArmorySettings.PINATA_NAME)))//leave pinata craft at center
                    {
                        position = spawnPoint + spawnDistance * direction;
                        ++spawnedVesselCount;
                    }
                    if (!spawnInOrbit && spawnDistance > BDArmorySettings.COMPETITION_DISTANCE / 2f / Mathf.Sin(Mathf.PI / spawnConfig.craftFiles.Count)) direction *= -1f; //have vessels spawning further than comp dist spawn pointing inwards instead of outwards
                    if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 67 && craftUrl.Contains(BDArmorySettings.PINATA_NAME))
                        vesselSpawnConfigs.Add(new VesselSpawnConfig(
                            craftUrl,
                            position,
                            direction,
                            altitude: 25000,
                            pitch: spawnPitch,
                            airborne: true,
                            inOrbit: false,
                            reuseURLVesselName: (BDATournament.Instance.tournamentStatus == TournamentStatus.Running && !BDATournament.Instance.fullTeams) || TournamentCoordinator.Instance.IsRunning
                        ));
                    else
                        vesselSpawnConfigs.Add(new VesselSpawnConfig(
                            craftUrl,
                            position,
                            direction,
                            (float)spawnConfig.altitude,
                            spawnPitch,
                            spawnAirborne,
                            spawnInOrbit,
                            reuseURLVesselName: (BDATournament.Instance.tournamentStatus == TournamentStatus.Running && !BDATournament.Instance.fullTeams) || TournamentCoordinator.Instance.IsRunning
                        ));
                }
            }
            else
            {
                if (BDArmorySettings.VESSEL_SPAWN_RANDOM_ORDER) spawnConfig.teamsSpecific.Shuffle(); // Randomise the team spawn order.
                int spawnedTeamCount = 0;
                Vector3 teamSpawnPosition;
                foreach (var team in spawnConfig.teamsSpecific)
                {
                    if (BDArmorySettings.VESSEL_SPAWN_RANDOM_ORDER) team.Shuffle(); // Randomise the spawn order within the team.
                    var teamHeading = 360f * spawnedTeamCount / spawnConfig.teamsSpecific.Count;
                    var teamDirection = (Quaternion.AngleAxis(teamHeading, radialUnitVector) * refDirection).ProjectOnPlanePreNormalized(radialUnitVector).normalized;
                    teamSpawnPosition = spawnPoint + spawnDistance * teamDirection;
                    int teamSpawnCount = 0;
                    float intraTeamSeparation = spawnConfig.intraTeamSeparation > 0 ? spawnConfig.intraTeamSeparation / 1.05409255f : 3f * BDAMath.Sqrt(spawnDistance); // 1.05409255f factor accounts for forward separation to give the asked for separation between craft.
                    var spreadDirection = Vector3.Cross(radialUnitVector, teamDirection);
                    var facingDirection = (!spawnInOrbit && spawnDistance > BDArmorySettings.COMPETITION_DISTANCE / 2f / Mathf.Sin(Mathf.PI / spawnConfig.teamsSpecific.Count)) ? -teamDirection : teamDirection; // Spawn facing inwards if competition distance is closer than spawning distance.

                    foreach (var craftUrl in team)
                    {
                        // Figure out spawn point and orientation (staggered similarly to formation and slightly spread depending on how closely starting to each other).
                        ++teamSpawnCount;
                        var position = teamSpawnPosition
                            + intraTeamSeparation * (teamSpawnCount % 2 == 1 ? -teamSpawnCount / 2 : teamSpawnCount / 2) * spreadDirection
                            + intraTeamSeparation / 3f * (team.Count / 2 - teamSpawnCount / 2) * facingDirection;
                        var individualFacingDirection = Quaternion.AngleAxis((teamSpawnCount % 2 == 1 ? -teamSpawnCount / 2 : teamSpawnCount / 2) * 200f / (20f + intraTeamSeparation), radialUnitVector) * facingDirection;
                        vesselSpawnConfigs.Add(new VesselSpawnConfig(
                            craftUrl,
                            position,
                            individualFacingDirection,
                            (float)spawnConfig.altitude,
                            spawnPitch,
                            spawnAirborne,
                            spawnInOrbit,
                            reuseURLVesselName: (BDATournament.Instance.tournamentStatus == TournamentStatus.Running && !BDATournament.Instance.fullTeams) || TournamentCoordinator.Instance.IsRunning
                        ));
                        ++spawnedVesselCount;
                    }
                    ++spawnedTeamCount;
                }
            }
            #endregion

            yield return SpawnVessels(vesselSpawnConfigs);
            if (spawnFailureReason != SpawnFailureReason.None)
            {
                vesselsSpawning = false;
                SpawnUtils.RevertSpawnLocationCamera(true, true);
                yield break;
            }

            #region Post-spawning
            // Spawning has succeeded, vessels have been renamed where necessary and vessels are ready. Time to assign teams and any other stuff.
            List<List<string>> teamVesselNames = null;
            if (spawnConfig.teamsSpecific != null)
            {
                if (spawnConfig.assignTeams) // Configure team names. We'll do the actual team assignment later.
                {
                    switch (spawnConfig.numberOfTeams)
                    {
                        case 1: // Assign team names based on folders.
                            {
                                foreach (var vesselName in SpawnUtils.SpawnedVesselURLs.Keys)
                                    SpawnUtils.originalTeams[vesselName] = Path.GetFileName(Path.GetDirectoryName(SpawnUtils.SpawnedVesselURLs[vesselName]));
                                break;
                            }
                        case -1: // Assign team names based on craft filename. We can't use vessel name as that can get adjusted above to avoid conflicts.
                            {
                                foreach (var vesselName in SpawnUtils.SpawnedVesselURLs.Keys)
                                    SpawnUtils.originalTeams[vesselName] = Path.GetFileNameWithoutExtension(SpawnUtils.SpawnedVesselURLs[vesselName]);
                                break;
                            }
                        default: // Specific team assignments.
                            {
                                teamVesselNames = new List<List<string>>();
                                for (int i = 0; i < spawnedVesselsTeamIndex.Max(kvp => kvp.Value); ++i)
                                    teamVesselNames.Add(spawnedVesselsTeamIndex.Where(kvp => kvp.Value == i).Select(kvp => kvp.Key).ToList());
                                break;
                            }
                    }
                }
            }

            // Revert back to the KSP's proper camera.
            SpawnUtils.RevertSpawnLocationCamera(true);

            yield return PostSpawnMainSequence(spawnConfig, spawnAirborne, withInitialVelocity);
            if (spawnFailureReason != SpawnFailureReason.None)
            {
                LogMessage("Vessel spawning FAILED! " + spawnFailureReason);
                vesselsSpawning = false;
                SpawnUtils.RevertSpawnLocationCamera(true, true);
                yield break;
            }

            if ((FlightGlobals.ActiveVessel == null || FlightGlobals.ActiveVessel.state == Vessel.State.DEAD) && spawnedVessels.Count > 0)
            {
                yield return LoadedVesselSwitcher.Instance.SwitchToVesselWhenPossible(spawnedVessels.Take(UnityEngine.Random.Range(1, spawnedVessels.Count)).Last().Value); // Update the camera.
            }
            FlightCamera.fetch.SetDistance(50);

            if (spawnConfig.assignTeams)
            {
                // Assign the vessels to teams.
                LogMessage("Assigning vessels to teams.", false);
                if (spawnConfig.teamsSpecific == null && spawnConfig.teamCounts == null && spawnConfig.numberOfTeams > 1)
                {
                    int numberPerTeam = spawnedVessels.Count / spawnConfig.numberOfTeams;
                    int residue = spawnedVessels.Count - numberPerTeam * spawnConfig.numberOfTeams;
                    spawnConfig.teamCounts = new List<int>();
                    for (int team = 0; team < spawnConfig.numberOfTeams; ++team)
                        spawnConfig.teamCounts.Add(numberPerTeam + (team < residue ? 1 : 0));
                }
                LoadedVesselSwitcher.Instance.MassTeamSwitch(true, useOriginalTeamNames, spawnConfig.teamCounts, teamVesselNames);
            }
            #endregion

            LogMessage("Vessel spawning SUCCEEDED!", true, BDArmorySettings.DEBUG_SPAWNING);
            vesselSpawnSuccess = true;
            vesselsSpawning = false;
        }
        #endregion

        // TODO Continuous Single Spawning and Team Spawning should probably, at some point, be separated into their own spawn strategies that make use of the above spawning functions.
        #region Continuous Single Spawning
        public bool vesselsSpawningOnceContinuously = false;
        public Coroutine spawnAllVesselsOnceContinuouslyCoroutine = null;

        public void SpawnAllVesselsOnceContinuously(int worldIndex, double latitude, double longitude, double altitude = 0, float distance = 10f, bool absDistanceOrFactor = false, float intraTeamSeparation = 0, float refHeading = 0, bool killEverythingFirst = true, bool assignTeams = true, int numberOfTeams = 0, List<int> teamCounts = null, List<List<string>> teamsSpecific = null, string spawnFolder = null, List<string> craftFiles = null)
        {
            SpawnAllVesselsOnceContinuously(new CircularSpawnConfig(new SpawnConfig(worldIndex, latitude, longitude, altitude, killEverythingFirst, assignTeams, numberOfTeams, teamCounts, teamsSpecific, spawnFolder, craftFiles), distance, absDistanceOrFactor, intraTeamSeparation, refHeading));
        }
        public void SpawnAllVesselsOnceContinuously(CircularSpawnConfig spawnConfig)
        {
            vesselsSpawningOnceContinuously = true;
            if (spawnAllVesselsOnceContinuouslyCoroutine != null)
                StopCoroutine(spawnAllVesselsOnceContinuouslyCoroutine);
            spawnAllVesselsOnceContinuouslyCoroutine = StartCoroutine(SpawnAllVesselsOnceContinuouslyCoroutine(spawnConfig));
            LogMessage("Triggering vessel spawning (continuous single) at " + spawnConfig.latitude.ToString("G6") + ", " + spawnConfig.longitude.ToString("G6") + ", with altitude " + spawnConfig.altitude + "m.", false);
        }

        public IEnumerator SpawnAllVesselsOnceContinuouslyCoroutine(CircularSpawnConfig spawnConfig)
        {
            while (vesselsSpawningOnceContinuously && BDArmorySettings.VESSEL_SPAWN_CONTINUE_SINGLE_SPAWNING)
            {
                SpawnUtils.ResetVesselNamingDeconfliction();
                SpawnAllVesselsOnce(spawnConfig);
                while (vesselsSpawning)
                    yield return waitForFixedUpdate;
                if (!vesselSpawnSuccess)
                {
                    vesselsSpawningOnceContinuously = false;
                    yield break;
                }
                yield return waitForFixedUpdate;

                // NOTE: runs in separate coroutine
                BDACompetitionMode.Instance.StartCompetitionMode(BDArmorySettings.COMPETITION_DISTANCE, BDArmorySettings.COMPETITION_START_DESPITE_FAILURES);
                yield return waitForFixedUpdate; // Give the competition start a frame to get going.

                // start timer coroutine for the duration specified in settings UI
                var duration = BDArmorySettings.COMPETITION_DURATION * 60f;
                LogMessage("Starting " + (duration > 0 ? "a " + duration.ToString("F0") + "s" : "an unlimited") + " duration competition.");
                while (BDACompetitionMode.Instance.competitionStarting)
                    yield return waitForFixedUpdate; // Wait for the competition to actually start.
                if (!BDACompetitionMode.Instance.competitionIsActive)
                {
                    LogMessage("Competition failed to start.");
                    vesselsSpawningOnceContinuously = false;
                    yield break;
                }
                while (BDACompetitionMode.Instance.competitionIsActive) // Wait for the competition to finish (limited duration and log dumping is handled directly by the competition now).
                    yield return new WaitForSeconds(1);

                // Wait 10s for any user action
                double startTime = Planetarium.GetUniversalTime();
                if (BDArmorySettings.VESSEL_SPAWN_CONTINUE_SINGLE_SPAWNING)
                {
                    while (vesselsSpawningOnceContinuously && Planetarium.GetUniversalTime() - startTime < BDArmorySettings.TOURNAMENT_DELAY_BETWEEN_HEATS)
                    {
                        LogMessage("Waiting " + (BDArmorySettings.TOURNAMENT_DELAY_BETWEEN_HEATS - (Planetarium.GetUniversalTime() - startTime)).ToString("0") + "s, then respawning pilots", true, false);
                        yield return new WaitForSeconds(1);
                    }
                }
            }
            vesselsSpawningOnceContinuously = false; // For when VESSEL_SPAWN_CONTINUE_SINGLE_SPAWNING gets toggled.
        }
        #endregion

        #region Team Spawning
        /// <summary>
        /// Spawn multiple groups of vessels using the CircularSpawning using multiple SpawnConfigs.
        /// </summary>
        /// <param name="spawnConfigs"></param>
        /// <param name="startCompetition"></param>
        /// <param name="competitionStartDelay"></param>
        /// <param name="startCompetitionNow"></param>
        public void TeamSpawn(List<CircularSpawnConfig> spawnConfigs, bool startCompetition = false, double competitionStartDelay = 0d, bool startCompetitionNow = false)
        {
            vesselsSpawning = true; // Indicate that vessels are spawning here to avoid timing issues with Update in other modules.
            SpawnUtils.RevertSpawnLocationCamera(true);
            if (teamSpawnCoroutine != null)
                StopCoroutine(teamSpawnCoroutine);
            teamSpawnCoroutine = StartCoroutine(TeamsSpawnCoroutine(spawnConfigs, startCompetition, competitionStartDelay, startCompetitionNow));
        }
        private Coroutine teamSpawnCoroutine;
        public IEnumerator TeamsSpawnCoroutine(List<CircularSpawnConfig> spawnConfigs, bool startCompetition = false, double competitionStartDelay = 0d, bool startCompetitionNow = false)
        {
            bool killAllFirst = true;
            List<int> spawnCounts = new List<int>();
            spawnFailureReason = SpawnFailureReason.None;
            SpawnUtils.ResetVesselNamingDeconfliction();
            // Spawn each team.
            foreach (var spawnConfig in spawnConfigs)
            {
                vesselsSpawning = true; // Gets set to false each time spawning is finished, so we need to re-enable it again.
                vesselSpawnSuccess = false;
                spawnConfig.killEverythingFirst = killAllFirst;
                yield return SpawnAllVesselsOnceCoroutine(spawnConfig);
                if (!vesselSpawnSuccess)
                {
                    LogMessage("Vessel spawning failed, aborting.");
                    yield break;
                }
                spawnCounts.Add(spawnedVessels.Count);
                // LoadedVesselSwitcher.Instance.MassTeamSwitch(false); // Reset everyone to team 'A' so that the order doesn't get messed up.
                killAllFirst = false;
            }
            yield return waitForFixedUpdate;
            SpawnUtils.SaveTeams(); // Save the teams in case they've been pre-configured.
            LoadedVesselSwitcher.Instance.MassTeamSwitch(false, false, spawnCounts); // Assign teams based on the groups of spawns. Right click the 'T' to revert to the original team names if they were defined.
            if (startCompetition) // Start the competition.
            {
                var competitionStartDelayStart = Planetarium.GetUniversalTime();
                while (Planetarium.GetUniversalTime() - competitionStartDelayStart < competitionStartDelay - Time.fixedDeltaTime)
                {
                    var timeLeft = competitionStartDelay - (Planetarium.GetUniversalTime() - competitionStartDelayStart);
                    if ((int)(timeLeft - Time.fixedDeltaTime) < (int)timeLeft)
                        LogMessage("Competition starting in T-" + timeLeft.ToString("0") + "s", true, false);
                    yield return waitForFixedUpdate;
                }
                BDACompetitionMode.Instance.StartCompetitionMode(BDArmorySettings.COMPETITION_DISTANCE, BDArmorySettings.COMPETITION_START_DESPITE_FAILURES);
                if (startCompetitionNow)
                {
                    yield return waitForFixedUpdate;
                    BDACompetitionMode.Instance.StartCompetitionNow();
                }
            }
        }
        #endregion
    }
}