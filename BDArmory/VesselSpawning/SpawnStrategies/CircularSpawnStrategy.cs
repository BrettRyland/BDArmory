using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using BDArmory.Competition.RemoteOrchestration;
using BDArmory.Settings;

namespace BDArmory.VesselSpawning.SpawnStrategies
{
    public class CircularSpawnStrategy : SpawnStrategy
    {
        private VesselSource vesselSource;
        private List<int> vesselIds;
        private int bodyIndex;
        private double latitude;
        private double longitude;
        private double altitude;
        private float radius;
        private bool success = false;

        public CircularSpawnStrategy(VesselSource vesselSource, List<int> vesselIds, int bodyIndex, double latitude, double longitude, double altitude, float radius)
        {
            this.vesselSource = vesselSource;
            this.vesselIds = vesselIds;
            this.bodyIndex = bodyIndex;
            this.latitude = latitude;
            this.longitude = longitude;
            this.altitude = altitude;
            this.radius = radius;
        }

        public IEnumerator Spawn(VesselSpawnerBase spawner)
        {
            // use vesselSource to resolve local paths for active vessels
            var craftUrls = vesselIds.Select(e => vesselSource.GetLocalPath(e));
            // spawn all craftUrls in a circle around the center point
            CircularSpawnConfig spawnConfig = new CircularSpawnConfig(
                new SpawnConfig(
                    bodyIndex,
                    latitude,
                    longitude,
                    altitude,
                    true,
                    craftFiles: new List<string>(craftUrls)
                ),
                radius,
                BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                BDArmorySettings.VESSEL_SPAWN_INTRATEAM_DISTANCE,
                BDArmorySettings.VESSEL_SPAWN_REF_HEADING
            );
            yield return spawner.Spawn(spawnConfig);

            if (!spawner.vesselSpawnSuccess)
            {
                Debug.Log("[BDArmory.BDAScoreService] Vessel spawning failed.");
                yield break;
            }

            success = true;
        }

        public bool DidComplete()
        {
            return success;
        }
    }
}
