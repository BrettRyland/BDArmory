using UnityEngine;
using System.Collections;
using System.IO;
using System.Linq;

using BDArmory.Competition;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Utils;

namespace BDArmory.VesselSpawning
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class SingleVesselSpawning : VesselSpawnerBase
    {
        public static SingleVesselSpawning Instance;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != null) Destroy(Instance);
            Instance = this;
        }

        void LogMessage(string message, bool toScreen = true, bool toLog = true) => LogMessageFrom("SingleVesselSpawning", message, toScreen, toLog);

        // This is only used by the deprecated RemoteOrchestration SpawnStrategies
        public override IEnumerator Spawn(SpawnConfig spawnConfig)
        {
            if (spawnConfig.craftFiles == null || spawnConfig.craftFiles.Count == 0)
            {
                var spawnFolder = Path.GetFullPath(Path.Combine(AutoSpawnPath, spawnConfig.folder));
                spawnConfig.craftFiles = Directory.GetFiles(spawnFolder, "*.craft").ToList();
                if (spawnConfig.craftFiles.Count == 0)
                {
                    LogMessage($"No craft files found in {spawnFolder}, aborting.");
                    spawnFailureReason = SpawnFailureReason.NoCraft;
                    vesselsSpawning = false;
                    yield break;
                }
            }
            PreSpawnInitialisation(spawnConfig);
            yield return SpawnVessel(spawnConfig.craftFiles.First(), spawnConfig.latitude, spawnConfig.longitude, spawnConfig.altitude); // FIXME This lacks initialHeading and initialPitch. Really, this should be converted to use a VesselSpawnConfig instead and the spawnConfig for the PreSpawnInitialisation generated from it.
            vesselsSpawning = false;
        }

        public override void PreSpawnInitialisation(SpawnConfig spawnConfig)
        {
            base.PreSpawnInitialisation(spawnConfig);

            vesselsSpawning = true; // Signal that we've started the spawning vessels routine.
            vesselSpawnSuccess = false; // Set our success flag to false for now.
            spawnFailureReason = SpawnFailureReason.None; // Reset the spawn failure reason.
            SpawnUtils.ResetVesselNamingDeconfliction();
        }

        public IEnumerator SpawnVessel(string craftUrl, double latitude, double longitude, double altitude, float initialHeading = 90f, float initialPitch = 0f)
        {
            // Convert the parameters to a VesselSpawnConfig.
            var spawnBody = FlightGlobals.currentMainBody;
            var terrainAltitude = spawnBody.TerrainAltitude(latitude, longitude);
            var spawnPoint = spawnBody.BDGetWorldSurfacePosition(latitude, longitude, terrainAltitude + altitude);
            var radialUnitVector = (spawnPoint - spawnBody.transform.position).normalized;
            var north = VectorUtils.GetNorthVector(spawnPoint, spawnBody);
            var direction = (Quaternion.AngleAxis(initialHeading, radialUnitVector) * north).ProjectOnPlanePreNormalized(radialUnitVector).normalized;
            var airborne = altitude > 10;
            var spawnInOrbit = altitude >= spawnBody.MinSafeAltitude(); // Min safe orbital altitude
            var withInitialVelocity = airborne && BDArmorySettings.VESSEL_SPAWN_INITIAL_VELOCITY;
            VesselSpawnConfig vesselSpawnConfig = new VesselSpawnConfig(
                craftUrl,
                spawnPoint,
                direction,
                (float)altitude,
                initialPitch,
                airborne,
                spawnInOrbit,
                reuseURLVesselName: BDATournament.Instance.tournamentStatus == TournamentStatus.Running || TournamentCoordinator.Instance.IsRunning
            );

            // Spawn vessel.
            yield return SpawnSingleVessel(vesselSpawnConfig);
            if (spawnFailureReason != SpawnFailureReason.None) yield break;
            var vessel = spawnedVessels[latestSpawnedVesselName];
            if (vessel == null)
            {
                spawnFailureReason = SpawnFailureReason.VesselFailedToSpawn;
                yield break;
            }
            var vesselName = vessel.vesselName;

            // Perform the standard post-spawn main sequence.
            yield return PostSpawnMainSequence(vessel, airborne, withInitialVelocity);
            if (spawnFailureReason != SpawnFailureReason.None) yield break;

            // If a competition is active, add them to it.
            if (BDACompetitionMode.Instance.competitionIsActive || BDACompetitionMode.Instance.competitionStarting)
            {
                // Note: it's more complicated to add craft to a competition that is starting, but not started yet, so either add them before starting, or wait until it's started.
                yield return new WaitWhile(() => BDACompetitionMode.Instance.competitionStarting);
                if (vessel == null)
                {
                    LogMessage(vesselName + " disappeared while waiting for the competition to start!");
                    spawnFailureReason = SpawnFailureReason.VesselLostParts;
                    yield break;
                }

                if (!airborne) SpawnUtils.AirborneActivation(vessel, false); // Activate ground-spawned craft (air-spawned craft are already active).
                BDACompetitionMode.Instance.AddToActiveCompetition(vessel);
            }

            vesselSpawnSuccess = true;
        }
    }
}