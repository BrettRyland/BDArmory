using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BDArmory.Misc
{
    /// <summary>
    /// This class supports reloading the partModule info blocks when the editor loads.  This allows us to obtain current data on the module configurations.
    /// due to changes in the environment after part loading at game start.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    internal class BDAModuleInfos : MonoBehaviour
    {
        public static Dictionary<string, string> Modules = new Dictionary<string, string>()
        {
            //{"WeaponModule", "Weapon"},
            { "BDModuleSurfaceAI", "BDModule Surface AI"},
            { "BDModulePilotAI", "BDModule Pilot AI"},
        };

        public void Start()
        {
            StartCoroutine(ReloadModuleInfos());
        }

        internal static IEnumerator ReloadModuleInfos()
        {
            while (Bullets.BulletInfo.bullets == null) // Wait for the field to be non-null to avoid crashes on startup in ModuleWeapon.GetInfo().
                yield return null;
            yield return null;

            IEnumerator<AvailablePart> loadedParts = PartLoader.LoadedPartsList.GetEnumerator();
            while (loadedParts.MoveNext())
            {
                if (loadedParts.Current == null) continue;
                foreach (string key in Modules.Keys)
                {
                    if (!loadedParts.Current.partPrefab.Modules.Contains(key)) continue;
                    IEnumerator<PartModule> partModules = loadedParts.Current.partPrefab.Modules.GetEnumerator();
                    while (partModules.MoveNext())
                    {
                        if (partModules.Current == null) continue;
                        if (partModules.Current.moduleName != key) continue;
                        string info = partModules.Current.GetInfo();
                        for (int y = 0; y < loadedParts.Current.moduleInfos.Count; y++)
                        {
                            Debug.Log($"moduleName:  {loadedParts.Current.moduleInfos[y].moduleName}");
                            Debug.Log($"KeyValue:  {Modules[key]}");
                            if (loadedParts.Current.moduleInfos[y].moduleName != Modules[key]) continue;
                            loadedParts.Current.moduleInfos[y].info = info;
                            break;
                        }
                    }
                    partModules.Dispose();
                }
            }

            loadedParts.Dispose();
        }
    }
}
