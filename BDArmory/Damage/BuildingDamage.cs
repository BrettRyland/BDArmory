using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using BDArmory.Settings;

namespace BDArmory.Damage
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class BuildingDamage : MonoBehaviour
    {
        static Dictionary<DestructibleBuilding, float> buildingsDamaged = new Dictionary<DestructibleBuilding, float>();

        public static void RegisterDamage(DestructibleBuilding building)
        {
            if (!buildingsDamaged.ContainsKey(building))
            {
                buildingsDamaged.Add(building, building.FacilityDamageFraction);
                //Debug.Log("[BDArmory.BuildingDamage] registered " + building.name + " tracking " + buildingsDamaged.Count + " buildings");
            }
        }

        void OnDestroy()
        {
            buildingsDamaged.Clear(); // Clear the damaged building tracker when leaving the flight scene to clear references to building objects.
        }

        float buildingRegenTimer = 1; //regen 1 HP per second
        float RegenFactor = 0.1f; //could always turn these into customizable settings if you want faster/slower healing buildings. 0.08f is enough for the browning to destroy some buildings but not others.
        void FixedUpdate()
        {
            if (UI.BDArmorySetup.GameIsPaused) return;

            if (buildingsDamaged.Count > 0)
            {
                buildingRegenTimer -= Time.fixedDeltaTime;
                if (buildingRegenTimer < 0)
                {
                    foreach (var building in buildingsDamaged.Keys.ToList()) // Take a copy of the keys so we can modify the dictionary in the loop.
                    {
                        if (building == null) // Clear out any null references.
                        {
                            buildingsDamaged.Remove(building);
                            continue;
                        }
                        if (!building.IsIntact)
                        {
                            buildingsDamaged.Remove(building);
                            if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.BuildingDamage] building {building.name} destroyed! Removing");
                            continue;
                        }
                        if (building.FacilityDamageFraction > buildingsDamaged[building])
                        {
                            building.FacilityDamageFraction = Math.Max(building.FacilityDamageFraction - buildingsDamaged[building] * RegenFactor, buildingsDamaged[building]); // Heal up to the initial damage value.
                            if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.BuildingDamage] {building.name} current HP: {building.FacilityDamageFraction}");
                        }
                        else
                        {
                            if (BDArmorySettings.DEBUG_DAMAGE) Debug.Log($"[BDArmory.BuildingDamage] {building.name} regenned to full HP, removing from list");
                            buildingsDamaged.Remove(building);
                        }
                    }
                    buildingRegenTimer = 1;
                }
            }
        }
    }
}
