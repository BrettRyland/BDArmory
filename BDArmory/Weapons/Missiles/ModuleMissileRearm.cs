using System;
using System.Collections;
using System.Collections.Generic;
using KSP.UI.Screens;
using UnityEngine;

using BDArmory.Settings;
using System.Text;
using BDArmory.Utils;
using BDArmory.Extensions;

namespace BDArmory.Weapons.Missiles
{
    public class ModuleMissileRearm : BDAPartModule, IPartMassModifier, IPartCostModifier
    {
        public float GetModuleMass(float baseMass, ModifierStagingSituation situation) => Mathf.Max((isMultiLauncher ? (int)railAmmo : (int)railAmmo - 1), 0) * missileMass;

        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;
        public float GetModuleCost(float baseCost, ModifierStagingSituation situation) => Mathf.Max((isMultiLauncher ? (int)railAmmo : (int)railAmmo - 1), 0) * missileCost;
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;

        private float missileMass = 0;
        private float missileCost = 0;

        public bool isMultiLauncher = false;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_OrdnanceAvailable"),//Ordnance Available
UI_FloatRange(minValue = 1f, maxValue = 4, stepIncrement = 1f, scene = UI_Scene.Editor)]
        public float railAmmo = 1; //munitions included with/loaded in the launcher (VLS/CLS pods, etc)
        public int magazineAmmo = 0;
        public int ammoCount => magazineAmmo + (int)railAmmo;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_OrdnanceAvailable"),//Ordnance Available
UI_ProgressBar(affectSymCounterparts = UI_Scene.None, controlEnabled = false, scene = UI_Scene.Flight, maxValue = 100, minValue = 0, requireFullControl = false)]
        public float ammoRemaining = 1;

        [KSPField(isPersistant = true)]
        public string MissileName = "bahaAim120";

        [KSPField] public float reloadTime = 5f;
        [KSPField] public bool AccountForAmmo = true;
        [KSPField] public float maxAmmo = -1;

        public List<ModuleMissileMagazine> linkedMagazines;
        //public float tntmass = 1;
        AvailablePart missilePart;
        public Part SpawnedMissile;
        public string spawnVariantName = ""; // Variant to select on spawned missiles with MissileDummyVariant
        public bool SpawnMissile(Transform MissileTransform, float offset = 0, float vOffset = 0, bool deductAmmo = true)
        {
            if (railAmmo >= 1 || BDArmorySettings.INFINITE_ORDINANCE)
            {
                if (missilePart != null)
                {
                    if (MissileTransform == null) MissileTransform = part.partTransform;

                    foreach (PartModule m in missilePart.partPrefab.Modules)
                    {
                        if (m.moduleName == "MissileLauncher")
                        {
                            var partNode = new ConfigNode();
                            PartSnapshot(missilePart.partPrefab).CopyTo(partNode);
                            if (!string.IsNullOrEmpty(spawnVariantName)) MissileDummyVariant.ApplyToSnapshot(partNode, missilePart.partPrefab, spawnVariantName);
                            var position = MissileTransform.position;
                            position += offset * MissileTransform.forward;
                            position += vOffset * MissileTransform.right;
                            SpawnedMissile = CreatePart(partNode, position, MissileTransform.rotation, this.part);
                            ModuleMissileRearm MMR = SpawnedMissile.FindModuleImplementing<ModuleMissileRearm>();
                            if (MMR != null) SpawnedMissile.RemoveModule(MMR);
                            if (!BDArmorySettings.INFINITE_ORDINANCE && deductAmmo)
                            {
                                railAmmo--;
                                ammoRemaining--;
                            }
                            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.ModuleMissileRearm] spawned " + SpawnedMissile.name + "; ammo remaining: " + railAmmo);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        public void loadOrdnance(int tubesToReload)
        {
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.ModuleMissileRearm] reloading {tubesToReload} launchrails, {railAmmo} ordnance in launcher, queuing {tubesToReload - railAmmo} new munitions from magazine");
            if (linkedMagazines.Count > 0 && ammoCount > 0 && railAmmo < tubesToReload) //no/not enough ammo internal to launcher? grab some from magazine
            {
                int neededReloads = (int)(tubesToReload - railAmmo);
                for (int t2r = 0; t2r < neededReloads; t2r++)
                {
                    ModuleMissileMagazine priorityMagazine = null;
                    float lastPriority = -1;
                    float lastAmmoQty = -1;
                    foreach (var mag in linkedMagazines)
                    {
                        if (mag == null || mag.ammoCount < 1) continue; // Ignore broken or empty mags.
                        if (mag.priority < lastPriority) continue; // Ignore lower priority mags.
                        if (mag.priority > lastPriority)
                        {
                            lastAmmoQty = -1; // Reset the ammo quantity, so that the quantity of lower priority mags aren't considered.
                            lastPriority = mag.priority;
                        }
                        if (mag.ammoCount < lastAmmoQty) continue; // Ignore mags with the same priority but less ammo.
                        lastAmmoQty = mag.ammoCount;
                        priorityMagazine = mag;
                    }
                    if (priorityMagazine != null)
                    {
                        priorityMagazine.ammoCount--; //transfer ammo from mag to launcher
                        priorityMagazine.ammoRemaining--;
                        railAmmo++;
                        ammoRemaining++;
                        using (var mmr = VesselModuleRegistry.GetModules<ModuleMissileRearm>(vessel).GetEnumerator())
                            while (mmr.MoveNext())
                            {
                                if (mmr.Current == null) continue;
                                if (mmr.Current.MissileName != MissileName) continue;
                                mmr.Current.magazineAmmo--; //syncronize magazine count across all launchers using that ammo
                            }
                    }
                }
            }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (HighLogic.LoadedSceneIsFlight) part.force_activate();
            enabled = true;
            MultiMissileLauncher MML = part.FindModuleImplementing<MultiMissileLauncher>();
            if (MML == null || MML && MML.isClusterMissile && !MML.isLaunchedClusterMissile) MissileName = part.name;
            if (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)
                StartCoroutine(GetMissileValues(MML));
            //GameEvents.onEditorShipModified.Add(ShipModified);
            if (maxAmmo < 0) maxAmmo = railAmmo;
            if (maxAmmo == 1) Fields[nameof(railAmmo)].guiActiveEditor = false;
            else
            {
                UI_FloatRange Ammo = (UI_FloatRange)Fields[nameof(railAmmo)].uiControlEditor;
                Ammo.maxValue = maxAmmo;
            }
            if (HighLogic.LoadedSceneIsFlight)
            {
                using (var mmm = VesselModuleRegistry.GetModules<ModuleMissileMagazine>(vessel).GetEnumerator())
                    while (mmm.MoveNext())
                    {
                        if (mmm.Current == null) continue;
                        if (mmm.Current.MissileName != MissileName) continue;
                        linkedMagazines.Add(mmm.Current);
                        magazineAmmo += (int)mmm.Current.ammoCount;
                    }
                UI_ProgressBar ordnance = (UI_ProgressBar)Fields[nameof(ammoRemaining)].uiControlFlight;
                ordnance.maxValue = railAmmo;
                ammoRemaining = railAmmo;
            }
        }

        public void UpdateMissileValues()
        {
            StartCoroutine(GetMissileValues());
        }
        IEnumerator GetMissileValues(MultiMissileLauncher MML = null)
        {
            yield return new WaitForFixedUpdate();
            MissileLauncher ml = part.FindModuleImplementing<MissileLauncher>();
            ml.reloadableRail = this;
            using (var parts = PartLoader.LoadedPartsList.GetEnumerator())
                while (parts.MoveNext())
                {
                    if (parts.Current.partConfig == null || parts.Current.partPrefab == null)
                        continue;
                    if (parts.Current.partPrefab.partInfo.name != MissileName) continue;
                    if (parts.Current.partPrefab.FindModuleImplementing<MissileLauncher>() == null)
                    {
                        Debug.LogError($"[BDArmory.ModuleMissileRearm] ERROR: Found part with sharing the name {MissileName} which does not contain a MissileLauncher module!");
                        continue;
                    }
                    missilePart = parts.Current;
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.ModuleMissileRearm]: found {missilePart.partPrefab.partInfo.name}");
                    break;
                }
            if (missilePart == null)
            {
                if (!(HighLogic.LoadedSceneIsEditor && !part.ConnectedToShip())) // Don't spam logs when the part isn't attached to the ship yet.
                    Debug.LogWarning($"[BDArmory.ModuleMissileRearm]: Failed to find missile part ({MissileName}) on {part.partInfo.name}");
                missileCost = 0;
                missileMass = 0;
                yield break;
            }
            if (AccountForAmmo)
            {
                missileCost = missilePart.partPrefab.partInfo.cost;
                missileMass = missilePart.partPrefab.mass;
            }
            else
            {
                missileCost = 0;
                missileMass = 0;
            }

            if (MML != null && !MML.isClusterMissile)
            {
                // Parse maxOffboresight here instead of in MML since we're getting the partPrefab here anyways
                string maxOffboresightString = ConfigNodeUtils.FindPartModuleConfigNodeValue(missilePart.partPrefab.partInfo.partConfig, "MissileLauncher", "maxOffBoresight");
                if (!string.IsNullOrEmpty(maxOffboresightString)) // Use the default value from the MM patch.
                {
                    try
                    {
                        float maxOffboresight = float.Parse(maxOffboresightString);
                        MML.updateMaxOffBoresight(maxOffboresight);
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.ModuleMissileRearm]: setting maxOffBoresight of " + part + " to " + maxOffboresight);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[BDArmory.ModuleMissileRearm]: Failed to parse maxOffBoresight configNode: " + e.Message);
                    }
                }
                UrlDir.UrlConfig partConfigTemp = (UrlDir.UrlConfig)GameDatabase.Instance.root.GetConfig(missilePart.partPrefab.partInfo.partUrl);
                if (partConfigTemp == null)
                {
                    partConfigTemp = missilePart.partPrefab.partInfo.partUrlConfig;
                    Debug.LogWarning($"[BDArmory.ModuleMissileRearm]: GetConfig from partURL {missilePart.partPrefab.partInfo.partUrl} failed! Using missilePart.partPrefab.partInfo.partUrlConfig. Potentially multiple parts in one file!");
                }

                MML.subMunitionPath = MML.GetMeshurl(partConfigTemp);
            }
        }

        static IEnumerator FinalizeMissile(Part missile, Part launcher)
        {
            //Debug.Log("[BDArmory.ModuleMissileRearm]: Creating " + missile);
            string originatingVesselName = missile.vessel.vesselName;
            missile.physicalSignificance = Part.PhysicalSignificance.NONE;
            missile.PromoteToPhysicalPart();
            var childColliders = missile.GetComponentsInChildren<Collider>(includeInactive: false);
            CollisionManager.IgnoreCollidersOnVessel(launcher.vessel, childColliders);
            foreach (var col in childColliders)
                col.enabled = false;
            missile.Unpack();
            missile.InitializeModules();
            Vessel newVessel = missile.gameObject.AddComponent<Vessel>();
            newVessel.id = Guid.NewGuid();
            if (newVessel.Initialize(false))
            {
                newVessel.vesselName = Vessel.AutoRename(newVessel, originatingVesselName);
                newVessel.IgnoreGForces(10);
                newVessel.currentStage = StageManager.RecalculateVesselStaging(newVessel);
                missile.setParent(null);
            }
            yield return new WaitWhile(() => !missile.started && missile.State != PartStates.DEAD);
            if (missile.State == PartStates.DEAD)
            {
                Debug.Log("[BDArmory.ModuleMissileRearm]: Error; " + missile + " died before being fully initialized");
                yield break;
            }
        }

        public static ConfigNode PartSnapshot(Part part)
        {
            var node = new ConfigNode("PART");
            var snapshot = new ProtoPartSnapshot(part, null);

            snapshot.attachNodes = new List<AttachNodeSnapshot>();
            snapshot.srfAttachNode = new AttachNodeSnapshot("attach,-1");
            snapshot.symLinks = new List<ProtoPartSnapshot>();
            snapshot.symLinkIdxs = new List<int>();
            snapshot.Save(node);

            // Prune unimportant data
            node.RemoveValues("parent");
            node.RemoveValues("position");
            node.RemoveValues("rotation");
            node.RemoveValues("istg");
            node.RemoveValues("dstg");
            node.RemoveValues("sqor");
            node.RemoveValues("sidx");
            node.RemoveValues("attm");
            node.RemoveValues("srfN");
            node.RemoveValues("attN");
            node.RemoveValues("connected");
            node.RemoveValues("attached");
            node.RemoveValues("flag");
            node.RemoveNodes("ACTIONS");

            var module_nodes = node.GetNodes("MODULE");
            var prefab_modules = part.partInfo.partPrefab.GetComponents<PartModule>();
            node.RemoveNodes("MODULE");

            for (int i = 0; i < prefab_modules.Length && i < module_nodes.Length; i++)
            {
                var module = module_nodes[i];
                var name = module.GetValue("name") ?? "";

                node.AddNode(module);
                module.RemoveNodes("ACTIONS");
            }
            return node;
        }

        public delegate void OnPartReady(Part affectedPart);

        /// <summary>Creates a new part from the config.</summary>
        /// <param name="partConfig">Config to read part from.</param>
        /// <param name="position">Initial position of the new part.</param>
        /// <param name="rotation">Initial rotation of the new part.</param>
        /// <param name="fromPart"></param>

        public static Part CreatePart(
            ConfigNode partConfig,
            Vector3 position,
            Quaternion rotation,
            Part launcherPart)
        {
            var refVessel = launcherPart.vessel;
            var partNodeCopy = new ConfigNode();
            partConfig.CopyTo(partNodeCopy);
            var snapshot =
                new ProtoPartSnapshot(partNodeCopy, refVessel.protoVessel, HighLogic.CurrentGame);
            if (HighLogic.CurrentGame.flightState.ContainsFlightID(snapshot.flightID)
                || snapshot.flightID == 0)
            {
                snapshot.flightID = ShipConstruction.GetUniqueFlightID(HighLogic.CurrentGame.flightState);
            }
            snapshot.parentIdx = 0;
            snapshot.position = position;
            snapshot.rotation = rotation;
            snapshot.stageIndex = 0;
            snapshot.defaultInverseStage = 0;
            snapshot.seqOverride = -1;
            snapshot.inStageIndex = -1;
            snapshot.attachMode = (int)AttachModes.SRF_ATTACH;
            snapshot.attached = false;

            var newPart = snapshot.Load(refVessel, false);
            newPart.transform.position = position;
            newPart.transform.rotation = rotation;
            if (newPart.rb != null)
            {
                newPart.rb.velocity = launcherPart.Rigidbody.velocity;
                newPart.rb.angularVelocity = launcherPart.Rigidbody.angularVelocity;
            }
            newPart.missionID = launcherPart.missionID;
            newPart.UpdateOrgPosAndRot(newPart.vessel.rootPart);

            newPart.StartCoroutine(FinalizeMissile(newPart, launcherPart));
            return newPart;
        }
        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();

            output.Append(Environment.NewLine);
            output.AppendLine($"Missile Rearming");
            output.AppendLine($"- Reload Time: {reloadTime} s");
            output.AppendLine($"- Maximum Ordnance: {maxAmmo}");
            output.AppendLine($"- Ammo Mass/Cost: {AccountForAmmo}");
            return output.ToString();
        }
    }
}