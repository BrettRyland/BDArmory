using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Runtime.CompilerServices;

using BDArmory.Competition;
using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Weapons;
using BDArmory.Weapons.Missiles;
using BDArmory.UI;
using BDArmory.VesselSpawning;
using BDArmory.Modules;

namespace BDArmory.Utils
{
    /// <summary>
    /// A registry over all the asked for modules in all the asked for vessels.
    /// The lists are automatically updated whenever needed.
    /// Querying for a vessel or module that isn't yet in the registry causes the vessel or module to be added and tracked.
    /// 
    /// This removes the need for each module to scan for such modules, which often causes GC allocations and performance losses.
    /// The exception to this is that there is a race condition for functions triggering on the onVesselPartCountChanged event.
    /// Other functions that trigger on onVesselPartCountChanged or onPartJointBreak events should call OnVesselModified first before performing their own actions.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class VesselModuleRegistry : MonoBehaviour
    {
        #region Fields
        static public VesselModuleRegistry Instance;
        static public Dictionary<Vessel, Dictionary<Type, List<UnityEngine.Object>>> registry;
        static public Dictionary<Type, System.Reflection.MethodInfo> updateModuleCallbacks;
        public static readonly HashSet<VesselType> IgnoredVesselTypes = [VesselType.Debris, VesselType.SpaceObject];
        public static readonly HashSet<VesselType> ValidVesselTypes = [VesselType.Plane, VesselType.Ship, VesselType.Rover, VesselType.Lander, VesselType.Base]; // Valid vessel types for competitions.
        static readonly HashSet<Type> ModuleTypesToSortByProximityToRoot = [
            typeof(BDModulePilotAI),
            typeof(BDModuleSurfaceAI),
            typeof(BDModuleVTOLAI),
            typeof(BDModuleOrbitalAI),
            typeof(MissileFire),
            typeof(IBDAIControl)
        ];

        // Specialised registries to avoid the boxing/unboxing GC allocations on frequently used module types.
        static public Dictionary<Vessel, List<MissileFire>> registryMissileFire;
        static public Dictionary<Vessel, List<MissileBase>> registryMissileBase;
        static public Dictionary<Vessel, List<BDModulePilotAI>> registryBDModulePilotAI;
        static public Dictionary<Vessel, List<BDModuleSurfaceAI>> registryBDModuleSurfaceAI;
        static public Dictionary<Vessel, List<IBDAIControl>> registryIBDAIControl;
        static public Dictionary<Vessel, List<ModuleWeapon>> registryModuleWeapon;
        static public Dictionary<Vessel, List<IBDWeapon>> registryIBDWeapon;
        static public Dictionary<Vessel, List<ModuleEngines>> registryModuleEngines;
        static public Dictionary<Vessel, List<ModuleResourceIntake>> registryModuleIntakes;
        static public Dictionary<Vessel, List<ModuleCommand>> registryModuleCommand;
        static public Dictionary<Vessel, List<KerbalSeat>> registryKerbalSeat;
        static public Dictionary<Vessel, List<KerbalEVA>> registryKerbalEVA;
        static public Dictionary<Vessel, List<ModuleWheelBase>> registryRepulsorModule;
        static public Dictionary<Vessel, List<ModuleHMD>> registryModuleHMD;

        // Named Modules (where the modules are only known by name, we don't actually have instances of them; they come from DLLs that aren't dependencies).
        static public Dictionary<Vessel, Dictionary<string, List<Part>>> registryNamedModuleParts; // Parts per vessel containing the named module.

        static Dictionary<Vessel, int> vesselPartCounts;
        #endregion

        #region Monobehaviour methods
        void Awake()
        {
            if (Instance != null) { Destroy(Instance); }
            Instance = this;

            registry ??= [];
            registryMissileFire ??= [];
            registryMissileBase ??= [];
            registryModuleWeapon ??= [];
            registryIBDWeapon ??= [];
            registryModuleEngines ??= [];
            registryModuleIntakes ??= [];
            registryBDModulePilotAI ??= [];
            registryBDModuleSurfaceAI ??= [];
            registryIBDAIControl ??= [];
            registryModuleCommand ??= [];
            registryKerbalSeat ??= [];
            registryKerbalEVA ??= [];
            registryRepulsorModule ??= [];
            registryModuleHMD ??= [];
            updateModuleCallbacks ??= [];
            vesselPartCounts ??= [];
            registryNamedModuleParts ??= [];
        }

        void Start()
        {
            GameEvents.onVesselPartCountChanged.Add(OnVesselModifiedHandler);
            GameEvents.onVesselLoaded.Add(OnVesselLoaded);
        }

        void OnDestroy()
        {
            GameEvents.onVesselPartCountChanged.Remove(OnVesselModifiedHandler);
            GameEvents.onVesselLoaded.Remove(OnVesselLoaded);

            registry.Clear();
            registryMissileFire.Clear();
            registryMissileBase.Clear();
            registryModuleWeapon.Clear();
            registryIBDWeapon.Clear();
            registryModuleEngines.Clear();
            registryModuleIntakes.Clear();
            registryBDModulePilotAI.Clear();
            registryBDModuleSurfaceAI.Clear();
            registryIBDAIControl.Clear();
            registryModuleCommand.Clear();
            registryKerbalSeat.Clear();
            registryKerbalEVA.Clear();
            registryRepulsorModule.Clear();
            registryModuleHMD.Clear();
            registryNamedModuleParts.Clear();

            updateModuleCallbacks.Clear();
            vesselPartCounts.Clear();
        }
        #endregion

        #region Private methods
        /// <summary>
        /// Add a vessel to track to the registry.
        /// </summary>
        /// <param name="vessel">The vessel.</param>
        void AddVesselToRegistry(Vessel vessel)
        {
            registry.Add(vessel, []);
            vesselPartCounts[vessel] = vessel.Parts.Count;
            if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Vessel {vessel.vesselName} added to registry.");
        }

        /// <summary>
        /// Add a module type to track to a vessel in the registry.
        /// </summary>
        /// <typeparam name="T">The module type to track.</typeparam>
        /// <param name="vessel">The vessel.</param>
        void AddVesselModuleTypeToRegistry<T>(Vessel vessel) where T : class
        {
            if (!registry[vessel].ContainsKey(typeof(T)))
            {
                registry[vessel].Add(typeof(T), []);
                updateModuleCallbacks[typeof(T)] = typeof(VesselModuleRegistry).GetMethod(nameof(UpdateVesselModulesInRegistry), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).MakeGenericMethod(typeof(T));
            }
        }

        /// <summary>
        /// Update the list of modules of the given type in the registry for the given vessel.
        /// </summary>
        /// <typeparam name="T">The module type.</typeparam>
        /// <param name="vessel">The vessel.</param>
        void UpdateVesselModulesInRegistry<T>(Vessel vessel) where T : class
        {
            if (!registry.ContainsKey(vessel)) { AddVesselToRegistry(vessel); }
            if (!registry[vessel].ContainsKey(typeof(T))) { AddVesselModuleTypeToRegistry<T>(vessel); }
            if (ModuleTypesToSortByProximityToRoot.Contains(typeof(T)))
            {
                if (typeof(T) == typeof(IBDAIControl)) // Specialisation due to IBDAI being an interface instead of a proper class.
                {
                    var modules = vessel.FindPartModulesImplementing<IBDAIControl>();
                    registry[vessel][typeof(T)] = SortByProximityToRootIBDAI(ref modules).ConvertAll(m => m as UnityEngine.Object);
                }
                else
                {
                    var modules = vessel.FindPartModulesImplementing<T>().ConvertAll(m => m as PartModule);
                    registry[vessel][typeof(T)] = SortByProximityToRoot(ref modules).ConvertAll(m => m as UnityEngine.Object);
                }
            }
            else { registry[vessel][typeof(T)] = vessel.FindPartModulesImplementing<T>().ConvertAll(m => m as UnityEngine.Object); }
            if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Registry entry for {vessel.vesselName} updated to have {registry[vessel][typeof(T)].Count} modules of type {typeof(T).Name}.");
        }

        /// <summary>
        /// Add a named module type to track to a vessel in the named modules registry.
        /// </summary>
        /// <param name="vessel">The vessel.</param>
        /// <param name="moduleName">The name of the module type to track.</param>
        void AddVesselNamedModuleTypeToRegistry(Vessel vessel, string moduleName)
        {
            if (!registryNamedModuleParts.ContainsKey(vessel)) registryNamedModuleParts.Add(vessel, []);
            if (!registryNamedModuleParts[vessel].ContainsKey(moduleName)) { registryNamedModuleParts[vessel].Add(moduleName, []); }
        }

        /// <summary>
        /// Update the list of parts in the registry containing the named module type for the given vessel.
        /// </summary>
        /// <param name="vessel">The vessel.</param>
        /// <param name="moduleName">The named module type.</param>
        void UpdateVesselModulesInNamedModuleRegistry(Vessel vessel, string moduleName)
        {
            if (!registryNamedModuleParts.ContainsKey(vessel) || !registryNamedModuleParts[vessel].ContainsKey(moduleName)) { AddVesselNamedModuleTypeToRegistry(vessel, moduleName); }
            registryNamedModuleParts[vessel][moduleName] = vessel.Parts.Where(part => part.Modules.Contains(moduleName)).ToList();
            if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Registry entry for {vessel.vesselName} updated to have {registryNamedModuleParts[vessel][moduleName].Count} parts with modules of type {moduleName}.");
        }

        /// <summary>
        /// Update the registry entries when a tracked vessel gets modified.
        /// </summary>
        /// <param name="vessel">The vessel that was modified.</param>
        void OnVesselModifiedHandler(Vessel vessel)
        {
            if (vessel == null || !vessel.loaded) return;
            if (vesselPartCounts.ContainsKey(vessel) && vessel.Parts.Count == vesselPartCounts[vessel]) return; // Already done.

            var partsAdded = vesselPartCounts.ContainsKey(vessel) && vessel.Parts.Count > vesselPartCounts[vessel];
            vesselPartCounts[vessel] = vessel.Parts.Count;

            if (registry.ContainsKey(vessel))
            {
                foreach (var moduleType in registry[vessel].Keys.ToList())
                {
                    if (!partsAdded && registry[vessel][moduleType].Count == 0) continue; // Part loss shouldn't give more modules.
                    // Invoke the specific callback to update the registry for this type of module.
                    updateModuleCallbacks[moduleType].Invoke(this, [vessel]);
                }
            }

            // Specialised registries.
            if (registryMissileFire.ContainsKey(vessel) && (partsAdded || registryMissileFire[vessel].Count > 0))
            {
                var missileFires = vessel.FindPartModulesImplementing<MissileFire>();
                registryMissileFire[vessel] = SortByProximityToRoot(ref missileFires);
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Specialised registry entry for {vessel.vesselName} updated to have {registryMissileFire[vessel].Count} modules of type {typeof(MissileFire).Name}.");
            }
            if (registryMissileBase.ContainsKey(vessel) && (partsAdded || registryMissileBase[vessel].Count > 0))
            {
                registryMissileBase[vessel] = vessel.FindPartModulesImplementing<MissileBase>();
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Specialised registry entry for {vessel.vesselName} updated to have {registryMissileBase[vessel].Count} modules of type {typeof(MissileBase).Name}.");
            }
            if (registryBDModulePilotAI.ContainsKey(vessel) && (partsAdded || registryBDModulePilotAI[vessel].Count > 0))
            {
                var pilotAIModules = vessel.FindPartModulesImplementing<BDModulePilotAI>();
                registryBDModulePilotAI[vessel] = SortByProximityToRoot(ref pilotAIModules);
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Specialised registry entry for {vessel.vesselName} updated to have {registryBDModulePilotAI[vessel].Count} modules of type {typeof(BDModulePilotAI).Name}.");
            }
            if (registryBDModuleSurfaceAI.ContainsKey(vessel) && (partsAdded || registryBDModuleSurfaceAI[vessel].Count > 0))
            {
                var surfaceAIModules = vessel.FindPartModulesImplementing<BDModuleSurfaceAI>();
                registryBDModuleSurfaceAI[vessel] = SortByProximityToRoot(ref surfaceAIModules);
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Specialised registry entry for {vessel.vesselName} updated to have {registryBDModuleSurfaceAI[vessel].Count} modules of type {typeof(BDModuleSurfaceAI).Name}.");
            }
            if (registryIBDAIControl.ContainsKey(vessel) && (partsAdded || registryIBDAIControl[vessel].Count > 0))
            {
                var IBDAIControls = vessel.FindPartModulesImplementing<IBDAIControl>();
                registryIBDAIControl[vessel] = SortByProximityToRootIBDAI(ref IBDAIControls);
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Specialised registry entry for {vessel.vesselName} updated to have {registryIBDAIControl[vessel].Count} modules of type {typeof(IBDAIControl).Name}.");
            }
            if (registryModuleWeapon.ContainsKey(vessel) && (partsAdded || registryModuleWeapon[vessel].Count > 0))
            {
                registryModuleWeapon[vessel] = vessel.FindPartModulesImplementing<ModuleWeapon>();
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Specialised registry entry for {vessel.vesselName} updated to have {registryModuleWeapon[vessel].Count} modules of type {typeof(ModuleWeapon).Name}.");
            }
            if (registryIBDWeapon.ContainsKey(vessel) && (partsAdded || registryIBDWeapon[vessel].Count > 0))
            {
                registryIBDWeapon[vessel] = vessel.FindPartModulesImplementing<IBDWeapon>();
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Specialised registry entry for {vessel.vesselName} updated to have {registryIBDWeapon[vessel].Count} modules of type {typeof(IBDWeapon).Name}.");
            }
            if (registryModuleEngines.ContainsKey(vessel) && (partsAdded || registryModuleEngines[vessel].Count > 0))
            {
                registryModuleEngines[vessel] = vessel.FindPartModulesImplementing<ModuleEngines>();
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Specialised registry entry for {vessel.vesselName} updated to have {registryModuleEngines[vessel].Count} modules of type {typeof(ModuleEngines).Name}.");
            }
            if (registryModuleIntakes.ContainsKey(vessel) && (partsAdded || registryModuleIntakes[vessel].Count > 0))
            {
                registryModuleIntakes[vessel] = vessel.FindPartModulesImplementing<ModuleResourceIntake>();
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Specialised registry entry for {vessel.vesselName} updated to have {registryModuleIntakes[vessel].Count} modules of type {typeof(ModuleResourceIntake).Name}.");
            }
            if (registryModuleCommand.ContainsKey(vessel) && (partsAdded || registryModuleCommand[vessel].Count > 0))
            {
                registryModuleCommand[vessel] = vessel.FindPartModulesImplementing<ModuleCommand>();
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Specialised registry entry for {vessel.vesselName} updated to have {registryModuleCommand[vessel].Count} modules of type {typeof(ModuleCommand).Name}.");
            }
            if (registryKerbalSeat.ContainsKey(vessel) && (partsAdded || registryKerbalSeat[vessel].Count > 0))
            {
                registryKerbalSeat[vessel] = vessel.FindPartModulesImplementing<KerbalSeat>();
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Specialised registry entry for {vessel.vesselName} updated to have {registryKerbalSeat[vessel].Count} modules of type {typeof(KerbalSeat).Name}.");
            }
            if (registryKerbalEVA.ContainsKey(vessel) && (partsAdded || registryKerbalEVA[vessel].Count > 0))
            {
                registryKerbalEVA[vessel] = vessel.FindPartModulesImplementing<KerbalEVA>();
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Specialised registry entry for {vessel.vesselName} updated to have {registryKerbalEVA[vessel].Count} modules of type {typeof(KerbalEVA).Name}.");
            }
            if (registryRepulsorModule.ContainsKey(vessel) && (partsAdded || registryRepulsorModule[vessel].Count > 0))
            {
                registryRepulsorModule[vessel] = vessel.FindPartModulesImplementing<ModuleWheelBase>();
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Specialised registry entry for {vessel.vesselName} updated to have {registryRepulsorModule[vessel].Count} modules of type {typeof(ModuleWheelBase).Name}.");
            }
            if (registryModuleHMD.ContainsKey(vessel) && (partsAdded || registryModuleHMD[vessel].Count > 0))
            {
                registryModuleHMD[vessel] = vessel.FindPartModulesImplementing<ModuleHMD>();
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Specialised registry entry for {vessel.vesselName} updated to have {registryModuleHMD[vessel].Count} modules of type {typeof(ModuleHMD).Name}.");
            }

            // Named module registry.
            if (registryNamedModuleParts.ContainsKey(vessel))
            {
                foreach (var moduleName in registryNamedModuleParts[vessel].Keys.ToList())
                {
                    if (!partsAdded && registryNamedModuleParts[vessel][moduleName].Count == 0) continue; // Part loss shouldn't give more modules.
                    UpdateVesselModulesInNamedModuleRegistry(vessel, moduleName);
                }
            }
        }

        public void OnVesselLoaded(Vessel vessel)
        {
            if (vessel == null || !registry.ContainsKey(vessel)) return; // If the vessel is null or isn't in the registry, ignore it.
            OnVesselModified(vessel, true); // Force re-scanning the vessel.
        }
        #endregion

        #region Public methods
        /// <summary>
        /// Static interface to triggering the OnVesselModified handler.
        /// </summary>
        /// <param name="vessel">The vessel that was modified.</param>
        /// <param name="force">Update the registry even if the part count hasn't changed.</param>
        public static void OnVesselModified(Vessel vessel, bool force = false)
        {
            if (vessel == null || !vessel.loaded) return;
            if (force) { vesselPartCounts[vessel] = -1; }
            Instance.OnVesselModifiedHandler(vessel);
        }

        /// <summary>
        /// Get an enumerable over the modules of the specified type in the specified vessel.
        /// This is about 15-30 times faster than FindPartModulesImplementing, but still requires around the same amount of GC allocations due to boxing/unboxing.
        /// </summary>
        /// <typeparam name="T">The module type to get.</typeparam>
        /// <param name="vessel">The vessel to get the modules from.</param>
        /// <returns>An enumerable for use in foreach loops or .ToList.</returns>
        public static List<T> GetModules<T>(Vessel vessel) where T : class
        {
            if (vessel == null || !vessel.loaded) return []; // Return empty list.

            if (typeof(T) == typeof(MissileFire)) { return GetMissileFires(vessel) as List<T>; }
            if (typeof(T) == typeof(MissileBase)) { return GetMissileBases(vessel) as List<T>; }
            if (typeof(T) == typeof(BDModulePilotAI)) { return GetBDModulePilotAIs(vessel) as List<T>; }
            if (typeof(T) == typeof(IBDAIControl)) { return GetIBDAIControls(vessel) as List<T>; }
            if (typeof(T) == typeof(BDModuleSurfaceAI)) { return GetBDModuleSurfaceAIs(vessel) as List<T>; }
            if (typeof(T) == typeof(ModuleWeapon)) { return GetModuleWeapons(vessel) as List<T>; }
            if (typeof(T) == typeof(IBDWeapon)) { return GetIBDWeapons(vessel) as List<T>; }
            if (typeof(T) == typeof(ModuleEngines)) { return GetModuleEngines(vessel) as List<T>; }
            if (typeof(T) == typeof(ModuleResourceIntake)) { return GetModuleIntakes(vessel) as List<T>; }
            if (typeof(T) == typeof(ModuleCommand)) { return GetModuleCommands(vessel) as List<T>; }
            if (typeof(T) == typeof(KerbalSeat)) { return GetKerbalSeats(vessel) as List<T>; }
            if (typeof(T) == typeof(KerbalEVA)) { return GetKerbalEVAs(vessel) as List<T>; }
            if (typeof(T) == typeof(ModuleWheelBase)) { return GetRepulsorModules(vessel) as List<T>; }
            if (typeof(T) == typeof(ModuleHMD)) { return GetModuleHMDs(vessel) as List<T>; }

            if (!registry.ContainsKey(vessel))
            { Instance.AddVesselToRegistry(vessel); }

            if (!registry[vessel].ContainsKey(typeof(T)))
            { Instance.UpdateVesselModulesInRegistry<T>(vessel); }

            return registry[vessel][typeof(T)].ConvertAll(m => m as T);
        }

        /// <summary>
        /// Get the first module of the specified type in the specified vessel.
        /// </summary>
        /// <typeparam name="T">The module type.</typeparam>
        /// <param name="vessel">The vessel.</param>
        /// <param name="firstNonNull">The first module or the first non-null module (may still be null if none are found).</param>
        /// <returns>The first module if it exists, else null.</returns>
        public static T GetModule<T>(Vessel vessel, bool firstNonNull = false) where T : class
        {
            var modules = GetModules<T>(vessel);
            if (modules == null) return null;
            if (!firstNonNull) return modules.FirstOrDefault();
            foreach (var module in modules)
            { if (module != null) return module; }
            return null;
        }

        /// <summary>
        /// Get the number of modules of the given type on the vessel.
        /// </summary>
        /// <typeparam name="T">The module type.</typeparam>
        /// <param name="vessel">The vessel.</param>
        /// <returns>The number of modules of that type on the vessel.</returns>
        public static int GetModuleCount<T>(Vessel vessel) where T : class
        {
            if (vessel == null || !vessel.loaded) return 0;
            if (typeof(T) == typeof(MissileFire)) { return GetMissileFires(vessel).Count; }
            if (typeof(T) == typeof(MissileBase)) { return GetMissileBases(vessel).Count; }
            if (typeof(T) == typeof(BDModulePilotAI)) { return GetBDModulePilotAIs(vessel).Count; }
            if (typeof(T) == typeof(BDModuleSurfaceAI)) { return GetBDModuleSurfaceAIs(vessel).Count; }
            if (typeof(T) == typeof(IBDAIControl)) { return GetIBDAIControls(vessel).Count; }
            if (typeof(T) == typeof(ModuleWeapon)) { return GetModuleWeapons(vessel).Count; }
            if (typeof(T) == typeof(IBDWeapon)) { return GetIBDWeapons(vessel).Count; }
            if (typeof(T) == typeof(ModuleEngines)) { return GetModuleEngines(vessel).Count; }
            if (typeof(T) == typeof(ModuleResourceIntake)) { return GetModuleIntakes(vessel).Count; }
            if (typeof(T) == typeof(ModuleCommand)) { return GetModuleCommands(vessel).Count; }
            if (typeof(T) == typeof(KerbalSeat)) { return GetKerbalSeats(vessel).Count; }
            if (typeof(T) == typeof(KerbalEVA)) { return GetKerbalEVAs(vessel).Count; }
            if (typeof(T) == typeof(ModuleWheelBase)) { return GetRepulsorModules(vessel).Count; }
            if (typeof(T) == typeof(ModuleHMD)) { return GetModuleHMDs(vessel).Count; }
            if (!registry.ContainsKey(vessel) || !registry[vessel].ContainsKey(typeof(T))) { Instance.UpdateVesselModulesInRegistry<T>(vessel); }
            return registry[vessel][typeof(T)].Count;
        }

        /// <summary>
        /// Get the number of parts containing the given named module on the vessel.
        /// Notes:
        ///   Parts with multiple modules of the same type only count as 1.
        ///   Named modules are those that come from DLLs that we don't have a dependency on.
        /// </summary>
        /// <param name="vessel">The vessel.</param>
        /// <param name="moduleName">The named module.</param>
        /// <returns>The number of parts containing the named module.</returns>
        public static int GetModuleCount(Vessel vessel, string moduleName)
        {
            if (vessel == null || !vessel.loaded) return 0;
            if (!registryNamedModuleParts.ContainsKey(vessel) || !registryNamedModuleParts[vessel].ContainsKey(moduleName)) { Instance.UpdateVesselModulesInNamedModuleRegistry(vessel, moduleName); }
            return registryNamedModuleParts[vessel][moduleName].Count;
        }

        /// <summary>
        /// Get the list of parts on the vessel that contain the named module.
        /// Note: Named modules are those that come from DLLs that we don't have a dependency on.
        /// </summary>
        /// <param name="vessel">The vessel.</param>
        /// <param name="moduleName">The named module.</param>
        /// <returns>The list of parts containing the named module.</returns>
        public static List<Part> GetModuleParts(Vessel vessel, string moduleName)
        {
            if (vessel == null || !vessel.loaded) return [];
            if (!registryNamedModuleParts.ContainsKey(vessel) || !registryNamedModuleParts[vessel].ContainsKey(moduleName)) { Instance.UpdateVesselModulesInNamedModuleRegistry(vessel, moduleName); }
            return registryNamedModuleParts[vessel][moduleName];
        }

        /// <summary>
        /// Clean out the registries and drop null vessels.
        /// </summary>
        public static void CleanRegistries()
        {
            if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Cleaning registries.");
            // Unity's null comparison also matches destroyed vessel objects.
            foreach (var vessel in vesselPartCounts.Keys.Where(vessel => vessel == null).ToList())
                vesselPartCounts.Remove(vessel);
            // General registry.
            foreach (var vessel in registry.Keys.ToList()) { registry[vessel] = registry[vessel].Where(kvp => kvp.Value.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); } // Remove empty module lists.
            registry = registry.Where(kvp => kvp.Key != null && kvp.Value.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove null and empty vessel entries.
            // Specialised registries.
            registryMissileFire = registryMissileFire.Where(kvp => kvp.Key != null && kvp.Value.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove null and empty vessel entries.
            registryMissileBase = registryMissileBase.Where(kvp => kvp.Key != null && kvp.Value.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove null and empty vessel entries.
            registryBDModulePilotAI = registryBDModulePilotAI.Where(kvp => kvp.Key != null && kvp.Value.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove null and empty vessel entries.
            registryBDModuleSurfaceAI = registryBDModuleSurfaceAI.Where(kvp => kvp.Key != null && kvp.Value.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove null and empty vessel entries.
            registryIBDAIControl = registryIBDAIControl.Where(kvp => kvp.Key != null && kvp.Value.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove null and empty vessel entries.
            registryModuleWeapon = registryModuleWeapon.Where(kvp => kvp.Key != null && kvp.Value.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove null and empty vessel entries.
            registryIBDWeapon = registryIBDWeapon.Where(kvp => kvp.Key != null && kvp.Value.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove null and empty vessel entries.
            registryModuleEngines = registryModuleEngines.Where(kvp => kvp.Key != null && kvp.Value.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove null and empty vessel entries.
            registryModuleIntakes = registryModuleIntakes.Where(kvp => kvp.Key != null && kvp.Value.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove null and empty vessel entries.
            registryModuleCommand = registryModuleCommand.Where(kvp => kvp.Key != null && kvp.Value.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove null and empty vessel entries.
            registryKerbalSeat = registryKerbalSeat.Where(kvp => kvp.Key != null && kvp.Value.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove null and empty vessel entries.
            registryKerbalEVA = registryKerbalEVA.Where(kvp => kvp.Key != null && kvp.Value.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove null and empty vessel entries.
            registryRepulsorModule = registryRepulsorModule.Where(kvp => kvp.Key != null && kvp.Value.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove null and empty vessel entries.
            registryModuleHMD = registryModuleHMD.Where(kvp => kvp.Key != null && kvp.Value.Count > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove null and empty vessel entries.
            // Named module registry.
            registryNamedModuleParts = registryNamedModuleParts.Where(kvp => kvp.Key != null).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove null vessel entries. We can't clear the empty entries as we want to know if there are none.
        }

        /// <summary>
        /// Sort a list of part modules by their proximity to the root part.
        /// Note: we use OrderBy here to get a stable sort.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="modules"></param>
        public static List<T> SortByProximityToRoot<T>(ref List<T> modules, Part root = null) where T : PartModule
        {
            if (modules.Count == 0) return modules;
            if (root == null) root = modules.First().vessel.rootPart;
            modules = [.. modules.OrderBy(m => ProximityToRoot(m.part, root))];
            return modules;
        }

        /// <summary>
        /// Specialisation for IBDAI due to it being an interface.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="modules"></param>
        /// <returns></returns>
        public static List<T> SortByProximityToRootIBDAI<T>(ref List<T> modules, Part root = null) where T : IBDAIControl
        {
            if (modules.Count == 0) return modules;
            if (root == null) root = modules.First().vessel.rootPart;
            modules = [.. modules.OrderBy(m => ProximityToRoot(m.part, root))];
            return modules;
        }

        /// <summary>
        /// Get the proximity to the root part.
        /// </summary>
        /// <param name="part"></param>
        /// <returns>Proximity to the root part or int.MaxValue if no root part was found.</returns>
        public static int ProximityToRoot(Part part, Part root)
        {
            int proximity = 0;
            Part currentPart = part;
            while (currentPart is not null && currentPart != root)
            {
                currentPart = currentPart.parent;
                ++proximity;
            }
            if (currentPart is null)
                return int.MaxValue;
            return proximity;
        }

        #region Specialised methods
        // This would be much easier if C# implemented proper C++ style template specialisation.
        // These specialised methods give an extra speed boost by avoiding the boxing/unboxing associated with storing the modules as objects in the main registry.
        // They will be automatically used via the general method, but even more speed can be obtained by accessing them directly, particularly the ones returning a single item.

        public static List<MissileFire> GetMissileFires(Vessel vessel)
        {
            if (vessel == null || !vessel.loaded) return [];
            if (!registryMissileFire.ContainsKey(vessel))
            {
                var missileFires = vessel.FindPartModulesImplementing<MissileFire>();
                registryMissileFire.Add(vessel, SortByProximityToRoot(ref missileFires));
                vesselPartCounts[vessel] = vessel.Parts.Count;
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Vessel {vessel.vesselName} added to specialised {typeof(MissileFire).Name} registry with {registryMissileFire[vessel].Count} modules.");
            }
            return registryMissileFire[vessel];
        }
        public static MissileFire GetMissileFire(Vessel vessel, bool firstNonNull = false)
        {
            if (vessel == null || !vessel.loaded) return null;
            if (firstNonNull)
            {
                foreach (var module in GetMissileFires(vessel))
                { if (module != null) return module; }
                return null;
            }
            if (!registryMissileFire.ContainsKey(vessel)) { return GetMissileFires(vessel).FirstOrDefault(); }
            return registryMissileFire[vessel].FirstOrDefault();
        }

        public static List<MissileBase> GetMissileBases(Vessel vessel)
        {
            if (vessel == null || !vessel.loaded) return [];
            if (!registryMissileBase.ContainsKey(vessel))
            {
                registryMissileBase.Add(vessel, vessel.FindPartModulesImplementing<MissileBase>());
                vesselPartCounts[vessel] = vessel.Parts.Count;
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Vessel {vessel.vesselName} added to specialised {typeof(MissileBase).Name} registry with {registryMissileBase[vessel].Count} modules.");
            }
            return registryMissileBase[vessel];
        }
        public static MissileBase GetMissileBase(Vessel vessel, bool firstNonNull = false)
        {
            if (vessel == null || !vessel.loaded) return null;
            if (firstNonNull)
            {
                foreach (var module in GetMissileBases(vessel))
                { if (module != null) return module; }
                return null;
            }
            if (!registryMissileBase.ContainsKey(vessel)) { return GetMissileBases(vessel).FirstOrDefault(); }
            return registryMissileBase[vessel].FirstOrDefault();
        }

        public static List<BDModulePilotAI> GetBDModulePilotAIs(Vessel vessel)
        {
            if (vessel == null || !vessel.loaded) return [];
            if (!registryBDModulePilotAI.ContainsKey(vessel))
            {
                var pilotAIModules = vessel.FindPartModulesImplementing<BDModulePilotAI>();
                registryBDModulePilotAI.Add(vessel, SortByProximityToRoot(ref pilotAIModules));
                vesselPartCounts[vessel] = vessel.Parts.Count;
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Vessel {vessel.vesselName} added to specialised {typeof(BDModulePilotAI).Name} registry with {registryBDModulePilotAI[vessel].Count} modules.");
            }
            return registryBDModulePilotAI[vessel];
        }
        public static BDModulePilotAI GetBDModulePilotAI(Vessel vessel, bool firstNonNull = false)
        {
            if (vessel == null || !vessel.loaded) return null;
            if (firstNonNull)
            {
                foreach (var module in GetBDModulePilotAIs(vessel))
                { if (module != null) return module; }
                return null;
            }
            if (!registryBDModulePilotAI.ContainsKey(vessel)) { return GetBDModulePilotAIs(vessel).FirstOrDefault(); }
            return registryBDModulePilotAI[vessel].FirstOrDefault();
        }

        public static List<BDModuleSurfaceAI> GetBDModuleSurfaceAIs(Vessel vessel)
        {
            if (vessel == null || !vessel.loaded) return [];
            if (!registryBDModuleSurfaceAI.ContainsKey(vessel))
            {
                var surfaceAIModules = vessel.FindPartModulesImplementing<BDModuleSurfaceAI>();
                registryBDModuleSurfaceAI.Add(vessel, SortByProximityToRoot(ref surfaceAIModules));
                vesselPartCounts[vessel] = vessel.Parts.Count;
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Vessel {vessel.vesselName} added to specialised {typeof(BDModuleSurfaceAI).Name} registry with {registryBDModuleSurfaceAI[vessel].Count} modules.");
            }
            return registryBDModuleSurfaceAI[vessel];
        }
        public static BDModuleSurfaceAI GetBDModuleSurfaceAI(Vessel vessel, bool firstNonNull = false)
        {
            if (vessel == null || !vessel.loaded) return null;
            if (firstNonNull)
            {
                foreach (var module in GetBDModuleSurfaceAIs(vessel))
                { if (module != null) return module; }
                return null;
            }
            if (!registryBDModuleSurfaceAI.ContainsKey(vessel)) { return GetBDModuleSurfaceAIs(vessel).FirstOrDefault(); }
            return registryBDModuleSurfaceAI[vessel].FirstOrDefault();
        }

        public static List<IBDAIControl> GetIBDAIControls(Vessel vessel)
        {
            if (vessel == null || !vessel.loaded) return [];
            if (!registryIBDAIControl.ContainsKey(vessel))
            {
                var IBDAIControls = vessel.FindPartModulesImplementing<IBDAIControl>();
                registryIBDAIControl.Add(vessel, SortByProximityToRootIBDAI(ref IBDAIControls));
                vesselPartCounts[vessel] = vessel.Parts.Count;
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Vessel {vessel.vesselName} added to specialised {typeof(IBDAIControl).Name} registry with {registryIBDAIControl[vessel].Count} modules.");
            }
            return registryIBDAIControl[vessel];
        }
        public static IBDAIControl GetIBDAIControl(Vessel vessel, bool firstNonNull = false)
        {
            if (vessel == null || !vessel.loaded) return null;
            if (firstNonNull)
            {
                foreach (var module in GetIBDAIControls(vessel))
                { if (module != null) return module; }
                return null;
            }
            if (!registryIBDAIControl.ContainsKey(vessel)) { return GetIBDAIControls(vessel).FirstOrDefault(); }
            return registryIBDAIControl[vessel].FirstOrDefault();
        }

        public static List<ModuleWeapon> GetModuleWeapons(Vessel vessel)
        {
            if (vessel == null || !vessel.loaded) return [];
            if (!registryModuleWeapon.ContainsKey(vessel))
            {
                registryModuleWeapon.Add(vessel, vessel.FindPartModulesImplementing<ModuleWeapon>());
                vesselPartCounts[vessel] = vessel.Parts.Count;
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Vessel {vessel.vesselName} added to specialised {typeof(ModuleWeapon).Name} registry with {registryModuleWeapon[vessel].Count} modules.");
            }
            return registryModuleWeapon[vessel];
        }

        public static List<IBDWeapon> GetIBDWeapons(Vessel vessel)
        {
            if (vessel == null || !vessel.loaded) return [];
            if (!registryIBDWeapon.ContainsKey(vessel))
            {
                registryIBDWeapon.Add(vessel, vessel.FindPartModulesImplementing<IBDWeapon>());
                vesselPartCounts[vessel] = vessel.Parts.Count;
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Vessel {vessel.vesselName} added to specialised {typeof(IBDWeapon).Name} registry with {registryIBDWeapon[vessel].Count} modules.");
            }
            return registryIBDWeapon[vessel];
        }

        public static List<ModuleEngines> GetModuleEngines(Vessel vessel)
        {
            if (vessel == null || !vessel.loaded) return [];
            if (!registryModuleEngines.ContainsKey(vessel))
            {
                registryModuleEngines.Add(vessel, vessel.FindPartModulesImplementing<ModuleEngines>());
                vesselPartCounts[vessel] = vessel.Parts.Count;
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Vessel {vessel.vesselName} added to specialised {typeof(ModuleEngines).Name} registry with {registryModuleEngines[vessel].Count} modules.");
            }
            return registryModuleEngines[vessel];
        }

        public static List<ModuleResourceIntake> GetModuleIntakes(Vessel vessel)
        {
            if (vessel == null || !vessel.loaded) return [];
            if (!registryModuleIntakes.ContainsKey(vessel))
            {
                registryModuleIntakes.Add(vessel, vessel.FindPartModulesImplementing<ModuleResourceIntake>());
                vesselPartCounts[vessel] = vessel.Parts.Count;
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Vessel {vessel.vesselName} added to specialised {typeof(ModuleResourceIntake).Name} registry with {registryModuleIntakes[vessel].Count} modules.");
            }
            return registryModuleIntakes[vessel];
        }
        public static List<ModuleCommand> GetModuleCommands(Vessel vessel)
        {
            if (vessel == null || !vessel.loaded) return [];
            if (!registryModuleCommand.ContainsKey(vessel))
            {
                registryModuleCommand.Add(vessel, vessel.FindPartModulesImplementing<ModuleCommand>());
                vesselPartCounts[vessel] = vessel.Parts.Count;
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Vessel {vessel.vesselName} added to specialised {typeof(ModuleCommand).Name} registry with {registryModuleCommand[vessel].Count} modules.");
            }
            return registryModuleCommand[vessel];
        }
        public static ModuleCommand GetModuleCommand(Vessel vessel, bool firstNonNull = false)
        {
            if (vessel == null || !vessel.loaded) return null;
            if (firstNonNull)
            {
                foreach (var module in GetModuleCommands(vessel))
                { if (module != null) return module; }
                return null;
            }
            if (!registryModuleCommand.ContainsKey(vessel)) { return GetModuleCommands(vessel).FirstOrDefault(); }
            return registryModuleCommand[vessel].FirstOrDefault();
        }

        public static List<KerbalSeat> GetKerbalSeats(Vessel vessel)
        {
            if (vessel == null || !vessel.loaded) return [];
            if (!registryKerbalSeat.ContainsKey(vessel))
            {
                registryKerbalSeat.Add(vessel, vessel.FindPartModulesImplementing<KerbalSeat>());
                vesselPartCounts[vessel] = vessel.Parts.Count;
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Vessel {vessel.vesselName} added to specialised {typeof(KerbalSeat).Name} registry with {registryKerbalSeat[vessel].Count} modules.");
            }
            return registryKerbalSeat[vessel];
        }
        public static KerbalSeat GetKerbalSeat(Vessel vessel, bool firstNonNull = false)
        {
            if (vessel == null || !vessel.loaded) return null;
            if (firstNonNull)
            {
                foreach (var module in GetKerbalSeats(vessel))
                { if (module != null) return module; }
                return null;
            }
            if (!registryKerbalSeat.ContainsKey(vessel)) { return GetKerbalSeats(vessel).FirstOrDefault(); }
            return registryKerbalSeat[vessel].FirstOrDefault();
        }

        public static List<KerbalEVA> GetKerbalEVAs(Vessel vessel)
        {
            if (vessel == null || !vessel.loaded) return [];
            if (!registryKerbalEVA.ContainsKey(vessel))
            {
                registryKerbalEVA.Add(vessel, vessel.FindPartModulesImplementing<KerbalEVA>());
                vesselPartCounts[vessel] = vessel.Parts.Count;
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Vessel {vessel.vesselName} added to specialised {typeof(KerbalEVA).Name} registry with {registryKerbalEVA[vessel].Count} modules.");
            }
            return registryKerbalEVA[vessel];
        }
        public static KerbalEVA GetKerbalEVA(Vessel vessel, bool firstNonNull = false)
        {
            if (vessel == null || !vessel.loaded) return null;
            if (firstNonNull)
            {
                foreach (var module in GetKerbalEVAs(vessel))
                { if (module != null) return module; }
                return null;
            }
            if (!registryKerbalEVA.ContainsKey(vessel)) { return GetKerbalEVAs(vessel).FirstOrDefault(); }
            return registryKerbalEVA[vessel].FirstOrDefault();
        }
        public static List<ModuleWheelBase> GetRepulsorModules(Vessel vessel)
        {
            if (vessel == null || !vessel.loaded) return [];
            if (!registryRepulsorModule.ContainsKey(vessel))
            {
                registryRepulsorModule.Add(vessel, vessel.FindPartModulesImplementing<ModuleWheelBase>());
                vesselPartCounts[vessel] = vessel.Parts.Count;
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Vessel {vessel.vesselName} added to specialised {typeof(ModuleWheelBase).Name} registry with {registryRepulsorModule[vessel].Count} modules.");
            }
            return registryRepulsorModule[vessel];
        }

        public static List<ModuleHMD> GetModuleHMDs(Vessel vessel)
        {
            if (vessel == null || !vessel.loaded) return [];
            if (!registryModuleHMD.ContainsKey(vessel))
            {
                registryModuleHMD.Add(vessel, vessel.FindPartModulesImplementing<ModuleHMD>());
                vesselPartCounts[vessel] = vessel.Parts.Count;
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.VesselModuleRegistry]: Vessel {vessel.vesselName} added to specialised {typeof(ModuleHMD).Name} registry with {registryModuleHMD[vessel].Count} modules.");
            }
            return registryModuleHMD[vessel];
        }
        #endregion

#if DEBUG
        public IEnumerator PerformanceTest()
        {
            var wait = new WaitForSeconds(0.1f);
            {
                // Note: this test has significant GC allocations due to the allocation of an intermediate list.
                int count = 0;
                int iters = 100000;
                var startTime = Time.realtimeSinceStartup;
                for (int i = 0; i < iters; ++i) { foreach (var mf in FlightGlobals.ActiveVessel.FindPartModulesImplementing<MissileFire>()) ++count; }
                Debug.Log($"DEBUG {FlightGlobals.ActiveVessel} has {count / iters} weapon managers, checked at {iters / (Time.realtimeSinceStartup - startTime)}/s via vessel.FindPartModulesImplementing<MissileFire>()");
            }
            yield return wait;
            {
                int count = 0;
                int iters = 100000;
                var startTime = Time.realtimeSinceStartup;
                for (int i = 0; i < iters; ++i) { if (FlightGlobals.ActiveVessel.FindPartModuleImplementing<MissileFire>() != null) ++count; }
                Debug.Log($"DEBUG {FlightGlobals.ActiveVessel} has {count / iters} weapon managers, checked at {iters / (Time.realtimeSinceStartup - startTime)}/s via vessel.FindPartModuleImplementing<MissileFire>()");
            }
            yield return wait;
            {
                int count = 0;
                int iters = 10000000;
                var startTime = Time.realtimeSinceStartup;
                for (int i = 0; i < iters; ++i) { foreach (var mf in VesselModuleRegistry.GetModules<MissileFire>(FlightGlobals.ActiveVessel)) ++count; }
                Debug.Log($"DEBUG {FlightGlobals.ActiveVessel} has {count / iters} weapon managers, checked at {iters / (Time.realtimeSinceStartup - startTime)}/s via VesselModuleRegistry.GetModules<MissileFire>(vessel)");
            }
            yield return wait;
            {
                int count = 0;
                int iters = 10000000;
                var startTime = Time.realtimeSinceStartup;
                for (int i = 0; i < iters; ++i) { foreach (var mf in VesselModuleRegistry.GetMissileFires(FlightGlobals.ActiveVessel)) ++count; }
                Debug.Log($"DEBUG {FlightGlobals.ActiveVessel} has {count / iters} weapon managers, checked at {iters / (Time.realtimeSinceStartup - startTime)}/s via VesselModuleRegistry.GetMissileFires(vessel)");
            }
            yield return wait;
            {
                int count = 0;
                int iters = 10000000;
                var startTime = Time.realtimeSinceStartup;
                for (int i = 0; i < iters; ++i) { if (VesselModuleRegistry.GetModule<MissileFire>(FlightGlobals.ActiveVessel) != null) ++count; }
                Debug.Log($"DEBUG {FlightGlobals.ActiveVessel} has {count / iters} weapon managers, checked at {iters / (Time.realtimeSinceStartup - startTime)}/s via VesselModuleRegistry.GetModule<MissileFire>(vessel)");
            }
            yield return wait;
            {
                int count = 0;
                int iters = 10000000;
                var startTime = Time.realtimeSinceStartup;
                for (int i = 0; i < iters; ++i) { if (VesselModuleRegistry.GetModule<MissileFire>(FlightGlobals.ActiveVessel, true) != null) ++count; }
                Debug.Log($"DEBUG {FlightGlobals.ActiveVessel} has {count / iters} weapon managers, checked at {iters / (Time.realtimeSinceStartup - startTime)}/s via VesselModuleRegistry.GetModule<MissileFire>(vessel, true)");
            }
            yield return wait;
            {
                int count = 0;
                int iters = 10000000;
                var startTime = Time.realtimeSinceStartup;
                for (int i = 0; i < iters; ++i) { if (VesselModuleRegistry.GetMissileFire(FlightGlobals.ActiveVessel) != null) ++count; }
                Debug.Log($"DEBUG {FlightGlobals.ActiveVessel} has {count / iters} weapon managers, checked at {iters / (Time.realtimeSinceStartup - startTime)}/s via VesselModuleRegistry.GetMissileFire(vessel)");
            }
            yield return wait;
            {
                int count = 0;
                int iters = 10000000;
                var startTime = Time.realtimeSinceStartup;
                for (int i = 0; i < iters; ++i) { if (VesselModuleRegistry.GetMissileFire(FlightGlobals.ActiveVessel, true) != null) ++count; }
                Debug.Log($"DEBUG {FlightGlobals.ActiveVessel} has {count / iters} weapon managers, checked at {iters / (Time.realtimeSinceStartup - startTime)}/s via VesselModuleRegistry.GetMissileFire(vessel, true)");
            }
            BDACompetitionMode.Instance.competitionStatus.Add("VesselModuleRegistry performance test complete.");
        }

        public void DumpRegistriesFor(Vessel vessel)
        {
            if (registry.ContainsKey(vessel))
            {
                foreach (var type in registry[vessel].Keys)
                    Debug.Log($"DEBUG {vessel.vesselName} has {registry[vessel][type].Count} {type} modules in the general registry");
            }
            else { Debug.Log($"DEBUG {vessel.vesselName} isn't in the general registry"); }

            if (registryBDModulePilotAI.ContainsKey(vessel)) { var moduleCount = GetModuleCount<BDModulePilotAI>(vessel); var modules = vessel.FindPartModulesImplementing<BDModulePilotAI>(); Debug.Log($"DEBUG {vessel.vesselName} has {moduleCount} BDModulePilotAI special registry modules" + (modules.Count != moduleCount ? $", but {modules.Count} modules found" : "")); }
            if (registryBDModuleSurfaceAI.ContainsKey(vessel)) { var moduleCount = GetModuleCount<BDModuleSurfaceAI>(vessel); var modules = vessel.FindPartModulesImplementing<BDModuleSurfaceAI>(); Debug.Log($"DEBUG {vessel.vesselName} has {moduleCount} BDModuleSurfaceAI special registry modules" + (modules.Count != moduleCount ? $", but {modules.Count} modules found" : "")); }
            if (registryIBDAIControl.ContainsKey(vessel)) { var moduleCount = GetModuleCount<IBDAIControl>(vessel); var modules = vessel.FindPartModulesImplementing<IBDAIControl>(); Debug.Log($"DEBUG {vessel.vesselName} has {moduleCount} IBDAIControl special registry modules" + (modules.Count != moduleCount ? $", but {modules.Count} modules found" : "")); }
            if (registryIBDWeapon.ContainsKey(vessel)) { var moduleCount = GetModuleCount<IBDWeapon>(vessel); var modules = vessel.FindPartModulesImplementing<IBDWeapon>(); Debug.Log($"DEBUG {vessel.vesselName} has {moduleCount} IBDWeapon special registry modules" + (modules.Count != moduleCount ? $", but {modules.Count} modules found" : "")); }
            if (registryKerbalEVA.ContainsKey(vessel)) { var moduleCount = GetModuleCount<KerbalEVA>(vessel); var modules = vessel.FindPartModulesImplementing<KerbalEVA>(); Debug.Log($"DEBUG {vessel.vesselName} has {moduleCount} KerbalEVA special registry modules" + (modules.Count != moduleCount ? $", but {modules.Count} modules found" : "")); }
            if (registryKerbalSeat.ContainsKey(vessel)) { var moduleCount = GetModuleCount<KerbalSeat>(vessel); var modules = vessel.FindPartModulesImplementing<KerbalSeat>(); Debug.Log($"DEBUG {vessel.vesselName} has {moduleCount} KerbalSeat special registry modules" + (modules.Count != moduleCount ? $", but {modules.Count} modules found" : "")); }
            if (registryMissileBase.ContainsKey(vessel)) { var moduleCount = GetModuleCount<MissileBase>(vessel); var modules = vessel.FindPartModulesImplementing<MissileBase>(); Debug.Log($"DEBUG {vessel.vesselName} has {moduleCount} MissileBase special registry modules" + (modules.Count != moduleCount ? $", but {modules.Count} modules found" : "")); }
            if (registryMissileFire.ContainsKey(vessel)) { var moduleCount = GetModuleCount<MissileFire>(vessel); var modules = vessel.FindPartModulesImplementing<MissileFire>(); Debug.Log($"DEBUG {vessel.vesselName} has {moduleCount} MissileFire special registry modules" + (modules.Count != moduleCount ? $", but {modules.Count} modules found" : "")); }
            if (registryModuleCommand.ContainsKey(vessel)) { var moduleCount = GetModuleCount<ModuleCommand>(vessel); var modules = vessel.FindPartModulesImplementing<ModuleCommand>(); Debug.Log($"DEBUG {vessel.vesselName} has {moduleCount} ModuleCommand special registry modules" + (modules.Count != moduleCount ? $", but {modules.Count} modules found" : "")); }
            if (registryModuleEngines.ContainsKey(vessel)) { var moduleCount = GetModuleCount<ModuleEngines>(vessel); var modules = vessel.FindPartModulesImplementing<ModuleEngines>(); Debug.Log($"DEBUG {vessel.vesselName} has {moduleCount} ModuleEngines special registry modules" + (modules.Count != moduleCount ? $", but {modules.Count} modules found" : "")); }
            if (registryModuleIntakes.ContainsKey(vessel)) { var moduleCount = GetModuleCount<ModuleResourceIntake>(vessel); var modules = vessel.FindPartModulesImplementing<ModuleResourceIntake>(); Debug.Log($"DEBUG {vessel.vesselName} has {moduleCount} ModuleIntakes special registry modules" + (modules.Count != moduleCount ? $", but {modules.Count} modules found" : "")); }
            if (registryModuleWeapon.ContainsKey(vessel)) { var moduleCount = GetModuleCount<ModuleWeapon>(vessel); var modules = vessel.FindPartModulesImplementing<ModuleWeapon>(); Debug.Log($"DEBUG {vessel.vesselName} has {moduleCount} ModuleWeapon special registry modules" + (modules.Count != moduleCount ? $", but {modules.Count} modules found" : "")); }

            if (registryNamedModuleParts.ContainsKey(vessel))
            {
                foreach (var moduleName in registryNamedModuleParts[vessel].Keys)
                    Debug.Log($"DEBUG {vessel.vesselName} has {GetModuleCount(vessel, moduleName)} parts with module {moduleName} in the named module registry.");
            }
            else { Debug.Log($"DEBUG {vessel.vesselName} isn't in the named module registry"); }
        }
#endif
        #endregion
    }

    /// <summary>
    /// This class maintains an overview and control of which WM and AI modules are the primary ones controlling a vessel.
    /// The primary AI is either the one that was most recently activated or the one closest to the root of the vessel.
    /// 
    /// Usage tips:
    /// 1. Access pattern for parts that need the primary WM every frame (about 6x faster than querying vessel.ActiveController().WM each time):
    ///   MissileFire WeaponManager
    ///   {
    ///       get
    ///       {
    ///           if (field == null || !field.IsPrimaryWM || field.vessel != vessel)
    ///               field = (vessel != null && vessel.loaded) ? vessel.ActiveController().WM : null;
    ///           if (field != null && field.vessel != vessel) field = null;
    ///           return field;
    ///       }
    ///   }
    /// Note: Take a local copy if accessing it repeatedly without the possibility of it changing.
    /// Note: The secondary check is necessary if vessel is FlightGlobals.ActiveVessel due to the DeathCam switch delay while the vessel is being removed.
    ///   
    /// 2. Access pattern for parts that need the primary AI every frame:
    ///   public IBDAIControl AI
    ///   {
    ///     get
    ///     {
    ///       if (field == null || !field.pilotEnabled || field.vessel != vessel) field = vessel.ActiveController().AI;
    ///       return field;
    ///     }
    ///   }
    /// Note: Take a local copy if accessing it repeatedly without the possibility of it changing.
    ///   
    /// 3. Accessing a field of the active AI, depending on the AI's type:
    ///   var ai = vessel.ActiveController().AI;
    ///   var myField = ai != null && ai.pilotEnabled ? ai.aiType switch
    ///   {
    ///     AIType.PilotAI => (ai as BDModulePilotAI).myField,
    ///     AIType.SurfaceAI => (ai as BDModuleSurfaceAI).myField,
    ///     AIType.VTOLAI => (ai as BDModuleVTOLAI).myField,
    ///     AIType.OrbitalAI => (ai as BDModuleOrbitalAI).myField,
    ///     _ => default
    ///   } : default;
    /// 
    /// 4. Accessing the active AI as a specific type:
    ///   var ai = vessel.ActiveController().AI;
    ///   var pilotAI = ai != null && ai.pilotEnabled && ai.aiType == AIType.PilotAI ? ai as BDModulePilotAI : null;
    ///   
    /// 5. Accessing the active AI as multiple types when switching on AI.aiType isn't appropriate:
    ///   var ai = vessel.ActiveController().AI;
    ///   BDModulePilotAI pilotAI = null;
    ///   BDModuleSurfaceAI surfaceAI = null;
    ///   BDModuleVTOLAI vtolAI = null;
    ///   BDModuleOrbitalAI orbitalAI = null;
    ///   if (ai != null && ai.pilotEnabled) switch(ai.aiType)
    ///     {
    ///       case AIType.PilotAI: pilotAI = ai as BDModulePilotAI; break;
    ///       case AIType.SurfaceAI: surfaceAI = ai as BDModuleSurfaceAI; break;
    ///       case AIType.VTOLAI: vtolAI = ai as BDModuleVTOLAI; break;
    ///       case AIType.OrbitalAI: orbitalAI = ai as BDModuleOrbitalAI; break;
    ///     }
    ///   
    /// 6. Accessing the primary AI of a certain type (regardless of which type is active):
    ///   var pilotAI = vessel.ActiveController().PilotAI;
    ///   if (pilotAI && pilotAI.pilotEnabled) {}
    /// </summary>
    public class ActiveController : VesselModule
    {
        /// <summary>
        /// Get the active controller vessel module for the vessel.
        /// Note: there is an extension method vessel.GetActiveController().
        /// This is slightly faster than going via VesselModuleRegistry.GetMissileFire(vessel).
        /// </summary>
        /// <param name="vessel"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ActiveController GetActiveController(Vessel vessel)
        {
            if (vessel == null) return null;
            if (!registry.ContainsKey(vessel))
                registry.Add(vessel, vessel.gameObject.GetComponent<ActiveController>());
            return registry[vessel];
        }

        static Dictionary<Vessel, ActiveController> registry = [];

        public MissileFire WM { get; private set; } // Use this for accessing the primary WM. Use VesselModuleRegistry.GetMissileFires to get all WMs on a craft.
        public IBDAIControl AI { get; private set; } // The active AI (if any are active) or the closest AI to the root.
        public bool VesselNamingDeconflictionHasBeenApplied { get; set; } = false; // Whether vessel naming deconfliction has been applied to this vessel or not.
        public string VesselName { get; set; } = null; // The vesselName of this vessel. This is to revert KSP's automatic renaming of vessels when we don't want it to.

        // Note: If using these below, check that ai.pilotEnabled is true to see if it's the active AI.
        public BDModulePilotAI PilotAI { get; private set; } // The primary or most recently active pilot AI.
        public BDModuleSurfaceAI SurfaceAI { get; private set; } // The primary or most recently active surface AI.
        public BDModuleVTOLAI VTOLAI { get; private set; } // The primary or most recently active VTOL AI.
        public BDModuleOrbitalAI OrbitalAI { get; private set; } // The primary or most recently active orbital AI.

        bool updateRequired = true;
        public bool IsFighter = false; // Whether the vessel is a detached fighter.

        // Activate module on valid vessels during flight.
        public override Activation GetActivation() => Vessel.vesselType == VesselType.SpaceObject ? Activation.Never : Activation.FlightScene;

        void UpdateModules()
        {
            if (!updateRequired) return;

            // Make sure the registry is up-to-date.
            VesselModuleRegistry.OnVesselModified(Vessel);

            // Set only the closest WM to the root part as the active WM.
            WM = VesselModuleRegistry.GetMissileFire(Vessel);
            foreach (var wm in VesselModuleRegistry.GetMissileFires(Vessel))
            {
                wm.IsPrimaryWM = wm == WM;
                if (!wm.IsPrimaryWM) wm.ParentWM = WM;
            }

            // Update the AIs.
            // Find the primary of each type of AI: the first active one or the first one, sorted by proximity to the root.
            // Then disable all but the overall primary (the first active primary in the order: pilot, surface, VTOL, orbital), reactivating it if necessary (as deactivating the others may have side effects).
            PilotAI = VesselModuleRegistry.GetBDModulePilotAIs(Vessel).FirstOrDefault(ai => ai.pilotEnabled); // Select the first active one.
            if (PilotAI == null) PilotAI = VesselModuleRegistry.GetBDModulePilotAI(Vessel); // Or default to the first one.
            SurfaceAI = VesselModuleRegistry.GetBDModuleSurfaceAIs(Vessel).FirstOrDefault(ai => ai.pilotEnabled);
            if (SurfaceAI == null) SurfaceAI = VesselModuleRegistry.GetBDModuleSurfaceAI(Vessel);
            VTOLAI = VesselModuleRegistry.GetModules<BDModuleVTOLAI>(Vessel).FirstOrDefault(ai => ai.pilotEnabled);
            if (VTOLAI == null) VTOLAI = VesselModuleRegistry.GetModule<BDModuleVTOLAI>(Vessel);
            OrbitalAI = VesselModuleRegistry.GetModules<BDModuleOrbitalAI>(Vessel).FirstOrDefault(ai => ai.pilotEnabled);
            if (OrbitalAI == null) OrbitalAI = VesselModuleRegistry.GetModule<BDModuleOrbitalAI>(Vessel);
            UpdateAIModules(true);

            // Update the registry.
            registry = registry.Where(kvp => kvp.Key != null && kvp.Key == kvp.Value.Vessel).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove any null or non-matching vessels.

            updateRequired = false;
            if (BDArmorySettings.DEBUG_OTHER)
            {
                var vesselName = Vessel.GetName();
                if (string.IsNullOrEmpty(vesselName)) vesselName = "new vessel";
                Debug.Log($"[BDArmory.ActiveController]: ActiveController modules updated on {(string.IsNullOrEmpty(vesselName) ? Vessel.rootPart.partInfo.name : vesselName)} ({Vessel.persistentId}, {Vessel.vesselType}), WM: {WM != null}, PilotAI: {PilotAI != null}, SurfaceAI: {SurfaceAI != null}, VTOLAI: {VTOLAI != null}, OrbitalAI: {OrbitalAI != null}, AI: {AI}");
            }
            LoadedVesselSwitcher.Instance.UpdateWMs(); // Flag that the WMs in the VS need refreshing.
        }

        /// <summary>
        /// Set AI to the first active AI in the order Pilot, Surface, VTOL, Orbital, otherwise the AI closest to the root part on the vessel.
        /// AIs other than the primary one get deactivated.
        /// In order to activate a lower priority AI, the higher priority ones need to be disabled first. SetActiveAI below takes care of this.
        /// <param name="reactivate">Reactivate the active AI in case deactivating others disables some stuff.</param>
        /// </summary>
        public void UpdateAIModules(bool reactivate = false)
        {
            var vesselName = Vessel.GetName();
            if (string.IsNullOrEmpty(vesselName)) vesselName = "new vessel";
            if (PilotAI != null && PilotAI.pilotEnabled) AI = PilotAI;
            else if (SurfaceAI != null && SurfaceAI.pilotEnabled) AI = SurfaceAI;
            else if (VTOLAI != null && VTOLAI.pilotEnabled) AI = VTOLAI;
            else if (OrbitalAI != null && OrbitalAI.pilotEnabled) AI = OrbitalAI;
            else AI = VesselModuleRegistry.GetIBDAIControl(Vessel);
            if (AI != null) // Then, deactivate any other AIs to avoid any control conflicts.
            {
                foreach (var ai in VesselModuleRegistry.GetIBDAIControls(Vessel))
                {
                    if (ai == null || ai == AI || !ai.pilotEnabled) continue;
                    ScreenMessages.PostScreenMessage($"Deactivating non-primary {ai.aiType} on {vesselName}", 3);
                    Debug.Log($"[BDArmory.ActiveController]: Deactivating non-primary {ai.aiType} ({ai.part.persistentId}) on {vesselName}");
                    ai.DeactivatePilot();
                }
                if (reactivate && AI.pilotEnabled)
                {
                    AI.ActivatePilot(); // Reactivate the AI in case deactivating the others disabled any common stuff.
                }
            }
            if (BDArmoryAIGUI.Instance != null) BDArmoryAIGUI.Instance.checkForAI = true; // Update the AI GUI on the next frame.
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Precalc, UpdateVesselType); // Reclassify the vessel if needed on the next frame.
        }

        /// <summary>
        /// Set a specific AI as the active one, disabling all the rest.
        /// This sets the AI as the primary of this AI type so long as it remains active through any vessel modifications.
        /// </summary>
        /// <param name="ai"></param>
        public void SetActiveAI(IBDAIControl ai)
        {
            if (ai == null) return;
            if (ai.vessel != Vessel) return;
            foreach (var otherAI in VesselModuleRegistry.GetIBDAIControls(Vessel))
            {
                if (otherAI == ai) continue;
                if (otherAI.pilotEnabled) otherAI.DeactivatePilot();
            }
            switch (ai.aiType) // Switch the primary of this type of AI to this one.
            {
                case AIType.PilotAI: PilotAI = ai as BDModulePilotAI; break;
                case AIType.SurfaceAI: SurfaceAI = ai as BDModuleSurfaceAI; break;
                case AIType.VTOLAI: VTOLAI = ai as BDModuleVTOLAI; break;
                case AIType.OrbitalAI: OrbitalAI = ai as BDModuleOrbitalAI; break;
            }
            ai.ActivatePilot();
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Precalc, UpdateVesselType); // Reclassify the vessel if needed on the next frame.
        }

        /// <summary>
        /// Update a vessel's type to match its AI (or lack thereof).
        /// Vessels with a WM must be one of VesselModuleRegistry.ValidVesselTypes.
        /// Note: unmanned probes are not considered a separate type from their manned equivalents.
        /// </summary>
        void UpdateVesselType()
        {
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Precalc, UpdateVesselType); // Do it only once.
            if (Vessel == null) return;
            if (Vessel.vesselType == VesselType.EVA) return; // Don't mess with EVA kerbals as it breaks them.
            var origType = Vessel.vesselType;
            Vessel.StripTypeFromName();
            if (AI != null)
            {
                switch (AI.aiType)
                {
                    case AIType.PilotAI:
                    case AIType.VTOLAI:
                        Vessel.vesselType = VesselType.Plane;
                        break;
                    case AIType.OrbitalAI:
                        Vessel.vesselType = VesselType.Ship;
                        break;
                    case AIType.SurfaceAI:
                        switch ((AI as BDModuleSurfaceAI).SurfaceType)
                        {
                            case AIUtils.VehicleMovementType.Land:
                            case AIUtils.VehicleMovementType.Amphibious:
                                Vessel.vesselType = VesselType.Rover;
                                break;
                            case AIUtils.VehicleMovementType.Stationary:
                                Vessel.vesselType = VesselType.Lander;
                                break;
                            case AIUtils.VehicleMovementType.Water:
                            case AIUtils.VehicleMovementType.Submarine:
                                Vessel.vesselType = VesselType.Ship;
                                break;
                        }
                        break;
                }
            }
            else if (WM != null)
            {
                Vessel.vesselType = VesselType.Base; // Fixed weapon emplacement.
            }
            if (BDArmorySettings.DEBUG_OTHER && origType != Vessel.vesselType) Debug.Log($"[BDArmory.ActiveController]: Reclassifying vessel type of {Vessel.GetName()} from {origType} to {Vessel.vesselType}.");
        }

        /// <summary>
        /// Set the craft file that all the WMs on this craft originate from.
        /// This is set via BDA's spawner. Craft spawned otherwise won't have this set.
        /// </summary>
        /// <param name="sourceURL">The URL of the craft file.</param>
        public void SetSourceURL(string sourceURL)
        {
            foreach (var wm in VesselModuleRegistry.GetMissileFires(Vessel))
                wm.SourceVesselURL = sourceURL;
        }

        /// <summary>
        /// This is called whenever a new vessel is created (both spawning and undocking / parts falling off / firing missiles / etc.).
        /// </summary>
        public override void OnLoadVessel()
        {
            base.OnLoadVessel();
            GameEvents.onVesselPartCountChanged.Add(OnVesselPartCountChanged);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Precalc, UpdateModules);
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.ObscenelyEarly, GetVesselName);
            updateRequired = true;
            UpdateModules();

            if (WM != null)
            {
                // If the vessel detached from a craft that had an active WM/AI, then we should activate the current WM/AI (if it has one).
                if (WM.ParentWM != null && WM.ParentWM.IsPrimaryWM)
                {
                    // Set the vessels AI and WM state based on what the parent was doing.
                    // Note: we don't need to assign the team as doing so for the parent applies it to all WM on the vessel.
                    if (WM.guardMode) WM.ToggleGuardMode();
                    if (WM.ParentWM.guardMode) WM.ToggleGuardMode();
                    if (AI != null && WM.ParentWM.AI != null)
                    {
                        if (WM.ParentWM.AI.pilotEnabled)
                        {
                            AI.ActivatePilot();
                            switch (WM.ParentWM.AI.currentCommand)
                            {
                                case PilotCommands.Free:
                                    AI.ReleaseCommand();
                                    break;
                                case PilotCommands.Attack:
                                    AI.CommandAttack(WM.ParentWM.AI.commandGPS);
                                    break;
                                case PilotCommands.FlyTo: // Not planning on attacking something, so just follow the leader.
                                case PilotCommands.Waypoints: // If the parent was running waypoints, then just follow them.
                                case PilotCommands.Follow: // Parent was following someone, we'll follow the parent as a sub-formation.
                                    AI.CommandFollow(WM.ParentWM.wingCommander, WM.ParentWM.wingCommander.GetFreeWingIndex());
                                    break;
                                default:
                                    Debug.LogError($"[BDArmory.VesselModuleRegistry]: Invalid PilotCommand!");
                                    break;
                            }
                        }
                        else AI.DeactivatePilot();
                    }
                    if (BDACompetitionMode.Instance.competitionIsActive || BDACompetitionMode.Instance.competitionStarting)
                        BDACompetitionMode.Instance.AddToCompetitionWhenReady(WM, false); // We've already set the AI/WM state, so don't go weapons-free when adding them to the competition.
                    IsFighter = true; // Detached craft are "fighters".
                    WM.CheckMissiles();
                    WM.ParentWM = null; // Clear the parent WM at the end of frame in case the WM is not on the root part since losing the root part will trigger OnLoadVessel again.
                }
            }
        }

        /// <summary>
        /// This is called when parts fall off or when docking occurs.
        /// </summary>
        /// <param name="vessel"></param>
        void OnVesselPartCountChanged(Vessel vessel)
        {
            if (vessel != Vessel) return;
            updateRequired = true;
            if (WM != null && !string.IsNullOrEmpty(VesselName) && vessel.vesselName != VesselName)
            {
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.ActiveController]: Reverting name change of {VesselName} ({vessel.persistentId}) from {vessel.vesselName}");
                vessel.vesselName = VesselName;
            }
        }

        // The vessel name of new vessels gets assigned during the ObscenelyEarly timing phase.
        void GetVesselName()
        {
            VesselName = vessel.vesselName;
            if (!string.IsNullOrEmpty(VesselName))
            {
                TimingManager.FixedUpdateRemove(TimingManager.TimingStage.ObscenelyEarly, GetVesselName);
            }
        }

        /// <summary>
        /// Clean up the event handlers.
        /// </summary>
        public void RemoveHandlers()
        {
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Precalc, UpdateModules);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.ObscenelyEarly, GetVesselName);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Precalc, UpdateVesselType);
            GameEvents.onVesselPartCountChanged.Remove(OnVesselPartCountChanged);
        }

        public override void OnUnloadVessel()
        {
            RemoveHandlers();
            base.OnUnloadVessel();
        }

        void OnDestroy() // Make sure stuff gets removed if the vessel module is destroyed without unloading the vessel (e.g., docking, fast quit, etc.).
        {
            RemoveHandlers();
        }
    }
}