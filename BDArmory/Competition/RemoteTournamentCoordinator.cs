using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using BDArmory.Competition.OrchestrationStrategies;
using BDArmory.Competition.RemoteOrchestration;
using BDArmory.GameModes.Waypoints;
using BDArmory.Settings;
using BDArmory.VesselSpawning.SpawnStrategies;
using BDArmory.VesselSpawning;
using static BDArmory.Competition.OrchestrationStrategies.WaypointFollowingStrategy;

namespace BDArmory.Competition
{
    public class RemoteTournamentCoordinator
    {
        private SpawnStrategy spawnStrategy;
        private OrchestrationStrategy orchestrator;
        private VesselSpawnerBase vesselSpawner;

        public RemoteTournamentCoordinator(SpawnStrategy spawner, OrchestrationStrategy orchestrator, VesselSpawnerBase vesselSpawner)
        {
            this.spawnStrategy = spawner;
            this.orchestrator = orchestrator;
            this.vesselSpawner = vesselSpawner;
        }

        public IEnumerator Execute()
        {
            // clear all vessels
            yield return SpawnUtils.RemoveAllVessels();

            // first, spawn vessels
            yield return spawnStrategy.Spawn(vesselSpawner);

            if (!spawnStrategy.DidComplete())
            {
                Debug.Log("[BDArmory.BDAScoreService] TournamentCoordinator spawn failed");
                yield break;
            }

            // now, hand off to orchestrator
            yield return orchestrator.Execute(BDAScoreService.Instance.client, BDAScoreService.Instance);
        }

        public static RemoteTournamentCoordinator BuildFromDescriptor(CompetitionModel competitionModel)
        {
            switch (competitionModel.mode)
            {
                case "ffa":
                    return BuildFFA();
                case "path":
                    return BuildWaypoint();
                case "chase":
                    return BuildChase();
            }
            return null;
        }

        private static RemoteTournamentCoordinator BuildFFA()
        {
            var scoreService = BDAScoreService.Instance;
            var scoreClient = scoreService.client;
            var vesselRegistry = scoreClient.vessels;
            var activeVesselModels = scoreClient.activeVessels.ToList().Select(e => vesselRegistry[e]);
            var activeVesselIds = scoreClient.activeVessels.ToList();
            var craftUrls = activeVesselModels.Select(e => e.craft_url);
            // TODO: need coords from descriptor, or fallback to local settings
            var bodyIndex = BDArmorySettings.VESSEL_SPAWN_WORLDINDEX;
            var latitude = BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x;
            var longitude = BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y;
            var altitude = BDArmorySettings.VESSEL_SPAWN_ALTITUDE;
            var spawnRadius = BDArmorySettings.VESSEL_SPAWN_DISTANCE;
            var spawnStrategy = new CircularSpawnStrategy(scoreClient.AsVesselSource(), activeVesselIds, bodyIndex, latitude, longitude, altitude, spawnRadius);
            var orchestrationStrategy = new RankedFreeForAllStrategy();
            var vesselSpawner = CircularSpawning.Instance;
            return new RemoteTournamentCoordinator(spawnStrategy, orchestrationStrategy, vesselSpawner);
        }

        private static RemoteTournamentCoordinator BuildWaypoint()
        {
            var scoreService = BDAScoreService.Instance;
            var scoreClient = scoreService.client;
            var vesselSource = scoreClient.AsVesselSource();
            var vesselRegistry = scoreClient.vessels;
            var activeVesselModels = scoreClient.activeVessels.ToList().Select(e => vesselRegistry[e]);
            var craftUrl = activeVesselModels.Select(e => vesselSource.GetLocalPath(e.id)).First();
            // TODO: need coords from descriptor, or fallback to local settings
            //var latitude = BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x;
            //var longitude = BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y;
            var worldIndex = WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].worldIndex;
            var latitude = WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].spawnPoint.x;
            var longitude = WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].spawnPoint.y;
            var altitude = BDArmorySettings.VESSEL_SPAWN_ALTITUDE;
            var spawnRadius = BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR;
            // var spawnStrategy = new PointSpawnStrategy(craftUrl, latitude, longitude, 2*altitude, 315.0f);
            Debug.Log("[BDArmory.RemoteTournamentCoordinator] Creating Spawn Strategy - WorldIndex: " + worldIndex + "; course name: " + WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].name);
            var spawnStrategy = new SpawnConfigStrategy(
                new CircularSpawnConfig(
                    new SpawnConfig(
                        worldIndex,
                        latitude,
                        longitude,
                        altitude,
                        true,
                        true,
                        0,
                        null,
                        null,
                        "",
                        activeVesselModels.Select(m => vesselSource.GetLocalPath(m.id)).ToList()
                    ),
                    spawnRadius,
                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                    BDArmorySettings.VESSEL_SPAWN_INTRATEAM_DISTANCE,
                    BDArmorySettings.VESSEL_SPAWN_REF_HEADING
                )
            );
            var waypoints = WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].waypoints;
            var orchestrationStrategy = new WaypointFollowingStrategy(waypoints);
            // var vesselSpawner = SingleVesselSpawning.Instance;
            var vesselSpawner = CircularSpawning.Instance; // The CircularSpawning spawner handles single-vessel spawning using the SpawnConfig strategy and the SingleVesselSpawning spawner is not ready yet.
            return new RemoteTournamentCoordinator(spawnStrategy, orchestrationStrategy, vesselSpawner);
        }
        /*
        private static RemoteTournamentCoordinator BuildLongCanyonWaypoint()
        {
            var scoreService = BDAScoreService.Instance;
            var scoreClient = scoreService.client;
            var vesselSource = scoreClient.AsVesselSource();
            var vesselRegistry = scoreClient.vessels;
            var activeVesselModels = scoreClient.activeVessels.ToList().Select(e => vesselRegistry[e]);
            var craftUrl = activeVesselModels.Select(e => vesselSource.GetLocalPath(e.id)).First();
            // TODO: need coords from descriptor, or fallback to local settings
            //var latitude = BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x;
            //var longitude = BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y;
            var latitude = 23.0f;
            var longitude = -40.1f;
            var altitude = BDArmorySettings.VESSEL_SPAWN_ALTITUDE;
            var spawnRadius = BDArmorySettings.VESSEL_SPAWN_DISTANCE;
            var spawnStrategy = new PointSpawnStrategy(craftUrl, latitude, longitude, altitude, 315.0f);
            // kerbin-canyon1
            // 23.3,-40.0
            // 24.47,-40.46
            // 24.95,-40.88
            // 25.91,-41.4
            // 26.23,-41.11
            // 26.8,-40.16
            // 27.05,-39.85
            // 27.15,-39.67
            // 27.58,-39.4
            // 28.33,-39.11
            // 28.83,-38.06
            // 29.54,-38.68
            // 30.15,-38.6
            // 30.83,-38.87
            // 30.73,-39.6
            // 30.9,-40.23
            // 30.83,-41.26
            var waypoints = new List<Waypoint> {
                new Waypoint(23.2f, -40.0f, altitude),
                new Waypoint(24.47f, -40.46f, altitude),
                new Waypoint(24.95f, -40.88f, altitude),
                new Waypoint(25.91f, -41.4f, altitude),
                new Waypoint(26.23f, -41.11f, altitude),
                new Waypoint(26.8f, -40.16f, altitude),
                new Waypoint(27.05f, -39.85f, altitude),
                new Waypoint(27.15f, -39.67f, altitude),
                new Waypoint(27.58f, -39.4f, altitude),
                new Waypoint(28.33f, -39.11f, altitude),
                new Waypoint(28.83f, -38.06f, altitude),
                new Waypoint(29.54f, -38.68f, altitude),
                new Waypoint(30.15f, -38.6f, altitude),
                new Waypoint(30.83f, -38.87f, altitude),
                new Waypoint(30.73f, -39.6f, altitude),
                new Waypoint(30.9f, -40.23f, altitude),
                new Waypoint(30.83f, -41.26f, altitude),
            };
            var orchestrationStrategy = new WaypointFollowingStrategy(waypoints);
            var vesselSpawner = SingleVesselSpawning.Instance;
            return new RemoteTournamentCoordinator(spawnStrategy, orchestrationStrategy, vesselSpawner);
        }
        */
        private static RemoteTournamentCoordinator BuildGauntletCanyonWaypoint()
        {
            var scoreService = BDAScoreService.Instance;
            var scoreClient = scoreService.client;
            var vesselSource = scoreClient.AsVesselSource();

            Func<int, bool> isHumanComparator = e =>
            {
                var vessel = scoreClient.vessels[e];
                if (vessel == null)
                {
                    return false;
                }
                var player = scoreClient.players[vessel.player_id];
                if (player == null)
                {
                    return false;
                }
                return player.is_human;
            };
            Func<int, bool> isNotHumanComparator = e => !isHumanComparator(e);
            Func<int, string> craftUrlMapper = e => vesselSource.GetLocalPath(e);
            var activeVessels = scoreClient.activeVessels.ToList();
            var playerCraftUrl = activeVessels.Where(isHumanComparator).Select(craftUrlMapper).First();
            var npcCraftUrl = activeVessels.Where(isNotHumanComparator).Select(craftUrlMapper).First();

            // TODO: need coords from descriptor, or fallback to local settings
            //var latitude = BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x;
            //var longitude = BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y;
            var latitude = 28.3f;
            var longitude = -39.2f;
            var altitude = BDArmorySettings.VESSEL_SPAWN_ALTITUDE;
            var spawnRadius = BDArmorySettings.VESSEL_SPAWN_DISTANCE;
            var playerStrategy = new PointSpawnStrategy(playerCraftUrl, latitude, longitude, 3 * altitude, 0.0f);

            // kerbin-canyon2
            // 28.33,-39.11
            // 28.83,-38.06
            // 29.54,-38.68
            // 30.15,-38.6
            // 30.83,-38.87
            // 30.73,-39.6
            // 30.9,-40.23
            // 30.83,-41.26

            List<SpawnStrategy> strategies = new List<SpawnStrategy>();

            if (npcCraftUrl != null)
            {
                // turret locations (all spawned at 0m)
                // 29.861150,-38.608205,0
                // 30.888611,-40.152778,90
                // 30.840590,-40.713150,90
                var turretLatitudes = new float[]
                {
                    29.858020f,
                    30.888611f,
                    //30.840590f,
                };
                var turretLongitudes = new float[]
                {
                    -38.602660f,
                    -40.152778f,
                    //-40.713150f,
                };
                var turretHeadings = new float[]
                {
                    0f,
                    90f,
                    //90f,
                };
                for (int k = 0; k < turretLatitudes.Count(); k++)
                {
                    var turretStrategy = new PointSpawnStrategy(npcCraftUrl, turretLatitudes[k], turretLongitudes[k], 0.0f, turretHeadings[k], 0);
                    strategies.Add(turretStrategy);
                }
            }

            // add player after turrets, so the spawn doesn't leave the player orbiting during the turret spawning
            strategies.Add(playerStrategy);


            var waypoints = WaypointCourses.CourseLocations[0].waypoints; //Canyon Waypoint course
            var orchestrationStrategy = new WaypointFollowingStrategy(waypoints);
            var listStrategy = new ListSpawnStrategy(strategies);
            var vesselSpawner = SingleVesselSpawning.Instance;
            return new RemoteTournamentCoordinator(listStrategy, orchestrationStrategy, vesselSpawner);
        }

        private static RemoteTournamentCoordinator BuildChase()
        {
            return null;
        }
    }

}
