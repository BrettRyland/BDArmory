using BDArmory.Competition;
using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.WeaponMounts;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static BDArmory.Weapons.Missiles.MissileBase;

namespace BDArmory.Weapons.Missiles
{
    /// <summary>
    /// Add-on Module to MissileLauncher to extend Launcher functionality to include cluster missiles and multi-missile pods
    /// </summary>

    public class MultiMissileLauncher : PartModule
    {
        public static Dictionary<string, ObjectPool> mslDummyPool = new Dictionary<string, ObjectPool>();
        [KSPField(isPersistant = true)]
        Vector3 dummyScale = Vector3.one;
        Coroutine missileSalvo;

        [KSPField(isPersistant = true, guiActive = false, guiName = "#LOC_BDArmory_WeaponName", guiActiveEditor = false), UI_Label(affectSymCounterparts = UI_Scene.All, scene = UI_Scene.All)]//Weapon Name 
        public string loadedMissileName = "";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_clustermissileTriggerDistance"), UI_FloatRange(minValue = 100f, maxValue = 10000f, stepIncrement = 100f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Detonation distance override
        public float clusterMissileTriggerDist = 750;

        Transform[] launchTransforms;
        [KSPField(isPersistant = true)] public string subMunitionName; //name of missile in .cfg - e.g. "bahaAim120"
        [KSPField(isPersistant = true)] public string subMunitionPath; //model path for missile
        public float missileMass = 0.1f;
        [KSPField] public string launchTransformName; //name of transform launcTransforms are parented to - see Rocketlauncher transform hierarchy
        public string exhaustTransformName;
        public string boostTransformName;
        //[KSPField] public int salvoSize = 1; //leave blank to have salvoSize = launchTransforms.count
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_WMWindow_rippleText2"), UI_FloatRange(minValue = 1, maxValue = 10, stepIncrement = 1, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Salvo
        public float salvoSize = 1;
        [KSPField] public bool setSalvoSize = false; //allow player to edit salvo size
        [KSPField] public bool isClusterMissile = false; //cluster submunitions deployed instead of standard detonation? Fold this into warHeadType?
        public bool isLaunchedClusterMissile = false;
        [KSPField] public bool isMultiLauncher = false; //is this a pod or launcher holding multiple missiles that fire in a salvo?
        [KSPField] public bool useSymCounterpart = false; //have symmetrically placed parts fire along with this part as part of salvo? Requires isMultMissileLauncher = true;
        [KSPField] public bool overrideReferenceTransform = false; //override the missileReferenceTransform in Missilelauncher to use vessel prograde
        [KSPField] public float rippleRPM = 650;
        [KSPField] public float launcherCooldown = 0; //additional delay after firing before launcher can fire next salvo
        [KSPField] public float offset = 0; //add an offset to missile spawn position?
        [KSPField] public string deployAnimationName;
        [KSPField] public float deploySpeed = 1; //animation speed
        [KSPField] public string RailNode = "rail"; //name of attachnode for VLS MMLs to set missile loadout
        [KSPField] public float tntMass = 1; //for MissileLauncher GetInfo()
        [KSPField] public bool OverrideDropSettings = false; //allow setting eject speed/dir
        [KSPField] public bool displayOrdinance = true; //display missile dummies (for rails and the like) or hide them (bomblet dispensers, gun-launched missiles, etc)
        [KSPField] public bool permitJettison = false; //allow jettisoning of missiles for multimissile launchrails and similar
        [KSPField] public bool ignoreLauncherColliders = false; //temporarily disable missile colliders to let them clear the launcher, for large-scale VLS or similar. -WARNING- has some effect on missile flight
        AnimationState deployState;
        public ModuleMissileRearm missileSpawner = null;
        MissileLauncher missileLauncher = null;
        MissileFire wpm = null;
        private int tubesFired = 0;
        [KSPField(isPersistant = true)]
        private bool LoadoutModified = false;
        public BDTeam Team = BDTeam.Get("Neutral");

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_ArmorWidth"),// Length
    UI_FloatRange(minValue = 0.5f, maxValue = 2, stepIncrement = 0.05f, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        public float Scale = 1;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_ArmorLength"),// Length
    UI_FloatRange(minValue = 0.5f, maxValue = 2, stepIncrement = 0.05f, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        public float Length = 1;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_Offset"),// Ordinance Offset
    UI_FloatRange(minValue = -1, maxValue = 1, stepIncrement = 0.1f, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        public float attachOffset = 0;

        [KSPField]
        public float scaleMax = 2;

        [KSPField]
        public string lengthTransformName;
        Transform LengthTransform;

        [KSPField]
        public string scaleTransformName;
        Transform ScaleTransform;

        public MissileTurret turret;

        List<TargetInfo> targetsAssigned;

        public bool toggleBay = true;
        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_ToggleAnimation", active = true)]//Disable Engage Options
        public void ToggleBay()
        {
            toggleBay = !toggleBay;

            if (toggleBay == false)
            {
                Events["ToggleBay"].guiName = StringUtils.Localize("#autoLOC_502069");//"Open"
            }
            else
            {
                Events["ToggleBay"].guiName = StringUtils.Localize("#autoLOC_502051");//""Close"
            }
            if (deployState != null)
            {
                deployState.normalizedTime = HighLogic.LoadedSceneIsFlight ? 0 : toggleBay ? 1 : 0;
                using (List<Part>.Enumerator pSym = part.symmetryCounterparts.GetEnumerator())
                    while (pSym.MoveNext())
                    {
                        if (pSym.Current == null) continue;
                        if (pSym.Current != part && pSym.Current.vessel == vessel)
                        {
                            var ml = pSym.Current.FindModuleImplementing<MultiMissileLauncher>();
                            if (ml == null) continue;
                            ml.deployState.normalizedTime = toggleBay ? 1 : 0;
                        }
                    }
            }
        }

        public void Start()
        {
            MakeMissileArray();
            for (int i = 0; i < launchTransforms.Length; i++)
            {
                launchTransforms[i].localPosition = new Vector3(launchTransforms[i].localPosition.x, launchTransforms[i].localPosition.y, launchTransforms[i].localPosition.z + (attachOffset * Mathf.Max(Scale, Length)));
            }
            GameEvents.onEditorShipModified.Add(ShipModified);
            if (HighLogic.LoadedSceneIsFlight)
            {
                GameEvents.onPartDie.Add(OnPartDie);
                if (isClusterMissile && vessel.Parts.Count == 1) isLaunchedClusterMissile = true;
            }
            if (!string.IsNullOrEmpty(deployAnimationName))
            {
                Events["ToggleBay"].guiActiveEditor = true;
                deployState = GUIUtils.SetUpSingleAnimation(deployAnimationName, part);
                if (deployState != null)
                {
                    deployState.normalizedTime = HighLogic.LoadedSceneIsFlight ? 0 : toggleBay ? 1 : 0;
                    deployState.speed = 0;
                    deployState.enabled = true;
                }
            }
            targetsAssigned = new List<TargetInfo>();
            StartCoroutine(DelayedStart());
        }

        IEnumerator DelayedStart()
        {
            yield return new WaitForFixedUpdate();
            missileLauncher = part.FindModuleImplementing<MissileLauncher>();
            missileSpawner = part.FindModuleImplementing<ModuleMissileRearm>();
            turret = part.FindModuleImplementing<MissileTurret>();
            if (turret != null) turret.missilepod = missileLauncher;
            if (missileSpawner == null) //MultiMissile launchers/cluster missiles need a MMR module for spawning their submunitions, so add one if not present in case cfg not set up properly
            {
                missileSpawner = (ModuleMissileRearm)part.AddModule("ModuleMissileRearm");
                missileSpawner.maxAmmo = isClusterMissile ? 1 : salvoSize * 5;
                missileSpawner.ammoCount = isClusterMissile ? 1 : launchTransforms.Length;
                missileSpawner.MissileName = subMunitionName;
                if (!isClusterMissile) //Clustermissiles replace/generate MMR on launch, other missiles should have it in the .cfg
                    Debug.LogError($"[BDArmory.MultiMissileLauncher] no ModuleMissileRearm on {part.name}. Please fix your .cfg");
            }
            if (BDArmorySettings.LIMITED_ORDINANCE) missileSpawner.ammoCount = isClusterMissile ? 1 : launchTransforms.Length;
            missileSpawner.isMultiLauncher = isMultiLauncher;
            if (missileLauncher != null) //deal with race condition/'MissileLauncher' loading before 'MultiMissileLauncher' and 'ModuleMissilerearm' by moving all relevant flags and values to a single location
            {
                missileLauncher.reloadableRail = missileSpawner;
                missileLauncher.hasAmmo = true;
                missileLauncher.multiLauncher = this;
                missileLauncher.MissileReferenceTransform = part.FindModelTransform("missileTransform");
                if (!missileLauncher.MissileReferenceTransform)
                {
                    missileLauncher.MissileReferenceTransform = launchTransforms[0];
                }

                if (isClusterMissile)
                {
                    if (isLaunchedClusterMissile)
                    {
                        missileSpawner.MissileName = subMunitionName;
                        missileSpawner.ammoCount = launchTransforms.Length;
                        missileLauncher.DetonationDistance = clusterMissileTriggerDist;
                        missileLauncher.blastRadius = clusterMissileTriggerDist;
                    }
                    else
                    {
                        missileSpawner.MissileName = missileLauncher.missileName; //ClMsl set to base name in case of reloadable rails, reset to submuition name after launch
                        missileLauncher.DetonationDistance = 0;
                        missileLauncher.blastRadius = 0;
                    }
                    missileLauncher.Fields["DetonationDistance"].guiActive = false;
                    missileLauncher.Fields["DetonationDistance"].guiActiveEditor = false;
                    missileLauncher.DetonateAtMinimumDistance = false;
                    missileLauncher.Fields["DetonateAtMinimumDistance"].guiActive = true;
                    missileLauncher.Fields["DetonateAtMinimumDistance"].guiActiveEditor = true;
                    if (missileSpawner.maxAmmo == 1)
                    {
                        missileSpawner.Fields["ammoCount"].guiActive = false;
                        missileSpawner.Fields["ammoCount"].guiActiveEditor = false;
                    }
                }
                else
                {
                    Fields["clusterMissileTriggerDist"].guiActive = false;
                    Fields["clusterMissileTriggerDist"].guiActiveEditor = false;
                }
                Fields["salvoSize"].guiActive = setSalvoSize;
                Fields["salvoSize"].guiActiveEditor = setSalvoSize;
                if (isMultiLauncher)
                {
                    if (!string.IsNullOrEmpty(subMunitionName))
                    {
                        Fields["loadedMissileName"].guiActive = true;
                        Fields["loadedMissileName"].guiActiveEditor = true;
                        missileLauncher.missileName = subMunitionName;
                    }
                    if (!permitJettison) missileLauncher.Events["Jettison"].guiActive = false;
                    if (OverrideDropSettings)
                    {
                        missileLauncher.Fields["dropTime"].guiActive = false;
                        missileLauncher.Fields["dropTime"].guiActiveEditor = false;
                        missileLauncher.dropTime = 0;
                        missileLauncher.Fields["decoupleSpeed"].guiActive = false;
                        missileLauncher.Fields["decoupleSpeed"].guiActiveEditor = false;
                        missileLauncher.decoupleSpeed = 10;
                        missileLauncher.Fields["decoupleForward"].guiActive = false;
                        missileLauncher.Fields["decoupleForward"].guiActiveEditor = false;
                        missileLauncher.decoupleForward = true;
                    }
                    float bRadius = 0;
                    using (var parts = PartLoader.LoadedPartsList.GetEnumerator())
                        while (parts.MoveNext())
                        {
                            if (parts.Current == null) continue;
                            if (parts.Current.partConfig == null || parts.Current.partPrefab == null) continue;
                            if (parts.Current.partPrefab.partInfo.name != subMunitionName) continue;
                            var explosivePart = parts.Current.partPrefab.FindModuleImplementing<BDExplosivePart>();
                            bRadius = explosivePart != null ? explosivePart.GetBlastRadius() : 0;
                            var ML = parts.Current.partPrefab.FindModuleImplementing<MissileLauncher>();
                            if (!string.IsNullOrEmpty(subMunitionName))
                            {
                                if (ML != null)
                                {
                                    loadedMissileName = ML.GetShortName();
                                    var CLM = parts.Current.partPrefab.FindModuleImplementing<MultiMissileLauncher>();
                                    if (CLM != null)
                                        if (CLM.isClusterMissile)
                                        {
                                            Fields["clusterMissileTriggerDist"].guiActive = true;
                                            Fields["clusterMissileTriggerDist"].guiActiveEditor = true;
                                        }
                                }
                                else Debug.LogError("[BDArmory.MultiMissileLauncher] submunition MissileLauncher module null! Check subMunitionName is correct");
                            }
                            break;
                        }
                    if (bRadius == 0)
                    {
                        Debug.Log("[multiMissileLauncher.GetBlastRadius] No BDExplosivePart found! Using default value");
                        bRadius = BlastPhysicsUtils.CalculateBlastRange(tntMass);
                    }
                    missileLauncher.blastRadius = bRadius;

                    if (missileLauncher.DetonationDistance == -1)
                    {
                        if (missileLauncher.GuidanceMode == GuidanceModes.AAMLead || missileLauncher.GuidanceMode == GuidanceModes.AAMPure || missileLauncher.GuidanceMode == GuidanceModes.PN || missileLauncher.GuidanceMode == GuidanceModes.APN)
                        {
                            missileLauncher.DetonationDistance = bRadius * 0.25f;
                        }
                        else
                        {
                            //DetonationDistance = GetBlastRadius() * 0.05f;
                            missileLauncher.DetonationDistance = 0f;
                        }
                    }
                }

                GUIUtils.RefreshAssociatedWindows(part);
            }
            missileSpawner.UpdateMissileValues();

            using (var parts = PartLoader.LoadedPartsList.GetEnumerator())
                while (parts.MoveNext())
                {
                    if (parts.Current == null) continue;
                    if (parts.Current.partConfig == null || parts.Current.partPrefab == null)
                        continue;
                    if (parts.Current.partPrefab.partInfo.name != subMunitionName) continue;
                    if (LoadoutModified) UpdateFields(parts.Current.partPrefab.FindModuleImplementing<MissileLauncher>(), false);
                    missileMass = parts.Current.partPrefab.mass;
                    break;
                }
            if (string.IsNullOrEmpty(scaleTransformName))
            {
                Fields["Scale"].guiActiveEditor = false;
            }
            else
            {
                ScaleTransform = part.FindModelTransform(scaleTransformName);
                UI_FloatRange AWidth = (UI_FloatRange)Fields["Scale"].uiControlEditor;
                AWidth.maxValue = scaleMax;
                if (Scale > scaleMax) Scale = scaleMax;
                AWidth.onFieldChanged = updateScale;
            }
            if (string.IsNullOrEmpty(lengthTransformName))
            {
                Fields["Length"].guiActiveEditor = false;
            }
            else
            {
                LengthTransform = part.FindModelTransform(lengthTransformName);
                UI_FloatRange ALength = (UI_FloatRange)Fields["Length"].uiControlEditor;
                ALength.maxValue = scaleMax;
                if (Length > scaleMax) Length = scaleMax;
                ALength.onFieldChanged = updateLength;
            }
            if (!string.IsNullOrEmpty(lengthTransformName))
            {
                UI_FloatRange AOffset = (UI_FloatRange)Fields["attachOffset"].uiControlEditor;
                AOffset.onFieldChanged = updateOffset;
            }
            else Fields["attachOffset"].guiActiveEditor = false;

            UpdateLengthAndScale(Scale, Length, attachOffset);
        }

        public void updateScale(BaseField field, object obj)
        {
            ScaleTransform.localScale = new Vector3(Scale, Scale, Scale);
            using (List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator())
                while (sym.MoveNext())
                {
                    if (sym.Current == null) continue;
                    var mml = sym.Current.FindModuleImplementing<MultiMissileLauncher>();
                    if (mml == null) continue;
                    mml.Scale = Scale;
                    mml.UpdateLengthAndScale(Scale, Length, attachOffset);
                }
            if (LengthTransform) updateLength(null, null);
            else PopulateMissileDummies();
        }
        public void updateLength(BaseField field, object obj)
        {
            LengthTransform.localScale = new Vector3(1, 1, (1 / Scale) * Length);
            using (List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator())
                while (sym.MoveNext())
                {
                    if (sym.Current == null) continue;
                    var mml = sym.Current.FindModuleImplementing<MultiMissileLauncher>();
                    if (mml == null) continue;
                    mml.Length = Length;
                    mml.UpdateLengthAndScale(Scale, Length, attachOffset);

                }
            PopulateMissileDummies();
        }
        public void updateOffset(BaseField field, object obj)
        {
            for (int i = 0; i < launchTransforms.Length; i++)
            {
                launchTransforms[i].localPosition = new Vector3(launchTransforms[i].localPosition.x, launchTransforms[i].localPosition.y, attachOffset * Mathf.Max(Scale, Length));
            }
            PopulateMissileDummies(true);
            using (List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator())
                while (sym.MoveNext())
                {
                    if (sym.Current == null) continue;
                    var mml = sym.Current.FindModuleImplementing<MultiMissileLauncher>();
                    if (mml == null) continue;
                    mml.attachOffset = attachOffset;
                    mml.UpdateLengthAndScale(Scale, Length, attachOffset);
                }
        }
        public void UpdateLengthAndScale(float scale, float length, float offset)
        {
            if (ScaleTransform != null)
                ScaleTransform.localScale = new Vector3(scale, scale, scale);
            if (LengthTransform != null)
                LengthTransform.localScale = new Vector3(1, 1, (1 / scale) * length);
            if (!string.IsNullOrEmpty(lengthTransformName))
            {
                for (int i = 0; i < launchTransforms.Length; i++)
                {
                    launchTransforms[i].localPosition = new Vector3(launchTransforms[i].localPosition.x, launchTransforms[i].localPosition.y, attachOffset * Mathf.Max(Scale, Length));
                }
            }
            PopulateMissileDummies();
        }
        private void OnDestroy()
        {
            GameEvents.onEditorShipModified.Remove(ShipModified);
            GameEvents.onPartDie.Remove(OnPartDie);
        }


        void OnPartDie() { OnPartDie(part); }

        void OnPartDie(Part p)
        {
            if (p == part)
            {
                foreach (var existingDummy in part.GetComponents<MissileDummy>())
                {
                    existingDummy.Deactivate();
                }
            }
        }

        public void ShipModified(ShipConstruct data)
        {
            if (part.children.Count > 0)
            {
                using (List<AttachNode>.Enumerator stackNode = part.attachNodes.GetEnumerator())
                    while (stackNode.MoveNext())
                    {
                        if (stackNode.Current == null) continue;
                        if (stackNode.Current?.nodeType != AttachNode.NodeType.Stack) continue;
                        if (stackNode.Current.id != RailNode) continue;
                        {
                            if (stackNode.Current.attachedPart is Part missile)
                            {
                                if (missile == null) return;

                                if (missile.FindModuleImplementing<MissileLauncher>())
                                {
                                    subMunitionName = missile.name;
                                    subMunitionPath = GetMeshurl((UrlDir.UrlConfig)GameDatabase.Instance.root.GetConfig(missile.partInfo.partUrl));
                                    PopulateMissileDummies(true);
                                    MissileLauncher MLConfig = missile.FindModuleImplementing<MissileLauncher>();
                                    LoadoutModified = true;
                                    Fields["loadedMissileName"].guiActive = true;
                                    Fields["loadedMissileName"].guiActiveEditor = true;
                                    loadedMissileName = MLConfig.GetShortName();
                                    GUIUtils.RefreshAssociatedWindows(part);
                                    if (missileSpawner)
                                    {
                                        missileSpawner.MissileName = subMunitionName;
                                        missileSpawner.UpdateMissileValues();
                                    }
                                    UpdateFields(MLConfig, true);
                                    var explosivePart = missile.FindModuleImplementing<BDExplosivePart>();
                                    tntMass = explosivePart != null ? explosivePart.tntMass : 0;
                                    missileLauncher.blastRadius = BlastPhysicsUtils.CalculateBlastRange(tntMass);
                                    missileMass = missile.partInfo.partPrefab.mass;
                                    EditorLogic.DeletePart(missile);
                                    using (List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator())
                                        while (sym.MoveNext())
                                        {
                                            if (sym.Current == null) continue;
                                            var mml = sym.Current.FindModuleImplementing<MultiMissileLauncher>();
                                            if (mml == null) continue;
                                            mml.subMunitionName = subMunitionName;
                                            mml.subMunitionPath = subMunitionPath;
                                            mml.PopulateMissileDummies(true);
                                            mml.LoadoutModified = true;
                                            if (mml.missileSpawner)
                                            {
                                                mml.missileSpawner.MissileName = subMunitionName;
                                                mml.missileSpawner.UpdateMissileValues();
                                            }
                                            mml.UpdateFields(MLConfig, true);
                                            mml.missileLauncher.blastRadius = BlastPhysicsUtils.CalculateBlastRange(tntMass);
                                        }
                                }
                            }
                        }
                    }
            }
        }

        private string GetMeshurl(UrlDir.UrlConfig cfgdir)
        {
            //check if part uses a MODEL node to grab an (external?) .mu file
            string url;
            if (cfgdir.config.HasNode("MODEL"))
            {
                var MODEL = cfgdir.config.GetNode("MODEL");
                url = MODEL.GetValue("model") ?? "";
                dummyScale = Vector3.one;
                if (MODEL.HasValue("scale"))
                {
                    string[] strings = MODEL.GetValue("scale").Split(","[0]);
                    dummyScale.x = float.Parse(strings[0]);
                    dummyScale.y = float.Parse(strings[1]);
                    dummyScale.z = float.Parse(strings[2]);
                }
                else
                {
                    if (cfgdir.config.HasValue("rescaleFactor"))
                    {
                        float scale = float.Parse(cfgdir.config.GetValue("rescaleFactor"));
                        dummyScale.x = scale;
                        dummyScale.y = scale;
                        dummyScale.z = scale;
                    }
                }
                //Debug.Log($"[BDArmory.MultiMissileLauncher] Found model URL of {url} and scale {dummyScale}");
                return url;

            }
            string mesh = "model";
            //in case the mesh is not model.mu
            if (cfgdir.config.HasValue("mesh"))
            {
                mesh = cfgdir.config.GetValue("mesh");
                char[] sep = { '.' };
                string[] words = mesh.Split(sep);
                mesh = words[0];
            }
            if (cfgdir.config.HasValue("rescaleFactor"))
            {
                float scale = float.Parse(cfgdir.config.GetValue("rescaleFactor"));
                dummyScale.x = scale;
                dummyScale.y = scale;
                dummyScale.z = scale;
            }
            url = string.Format("{0}/{1}", cfgdir.parent.parent.url, mesh);
            //Debug.Log($"[BDArmory.MultiMissileLauncher] Found model URL of {url} and scale {dummyScale}");
            return url;
        }

        void UpdateFields(MissileLauncher MLConfig, bool configurableSettings)
        {
            missileLauncher.homingType = MLConfig.homingType; //these are all non-persistant, and need to be re-grabbed at launch
            missileLauncher.targetingType = MLConfig.targetingType;
            missileLauncher.missileType = MLConfig.missileType;
            missileLauncher.lockedSensorFOV = MLConfig.lockedSensorFOV;
            missileLauncher.lockedSensorFOVBias = MLConfig.lockedSensorFOVBias;
            missileLauncher.lockedSensorVelocityBias = MLConfig.lockedSensorVelocityBias;
            missileLauncher.heatThreshold = MLConfig.heatThreshold;
            missileLauncher.chaffEffectivity = MLConfig.chaffEffectivity;
            missileLauncher.allAspect = MLConfig.allAspect;
            missileLauncher.uncagedLock = MLConfig.uncagedLock;
            missileLauncher.isTimed = MLConfig.isTimed;
            missileLauncher.radarLOAL = MLConfig.radarLOAL;
            missileLauncher.activeRadarRange = MLConfig.activeRadarRange;
            missileLauncher.activeRadarLockTrackCurve = MLConfig.activeRadarLockTrackCurve;
            missileLauncher.antiradTargets = MLConfig.antiradTargets;
            missileLauncher.steerMult = MLConfig.steerMult;
            missileLauncher.thrust = MLConfig.thrust;
            missileLauncher.maxAoA = MLConfig.maxAoA;
            missileLauncher.optimumAirspeed = MLConfig.optimumAirspeed;
            missileLauncher.maxTurnRateDPS = MLConfig.maxTurnRateDPS;
            missileLauncher.proxyDetonate = MLConfig.proxyDetonate;
            missileLauncher.terminalGuidanceShouldActivate = MLConfig.terminalGuidanceShouldActivate;
            missileLauncher.terminalGuidanceType = MLConfig.terminalGuidanceType;
            missileLauncher.torpedo = MLConfig.torpedo;
            missileLauncher.loftState = LoftStates.Boost;
            missileLauncher.TimeToImpact = float.PositiveInfinity;
            missileLauncher.initMaxAoA = MLConfig.maxAoA;
            missileLauncher.homingModeTerminal = MLConfig.homingModeTerminal;
            missileLauncher.pronavGain = MLConfig.pronavGain;
            missileLauncher.kappaAngle = MLConfig.kappaAngle;
            missileLauncher.gLimit = MLConfig.gLimit;
            missileLauncher.gMargin = MLConfig.gMargin;
            missileLauncher.terminalHoming = MLConfig.terminalHoming;
            missileLauncher.terminalHomingActive = false;

            if (configurableSettings)
            {
                missileLauncher.maxStaticLaunchRange = MLConfig.maxStaticLaunchRange;
                missileLauncher.minStaticLaunchRange = MLConfig.minStaticLaunchRange;
                missileLauncher.engageRangeMin = MLConfig.minStaticLaunchRange;
                missileLauncher.engageRangeMax = MLConfig.maxStaticLaunchRange;
                if (!overrideReferenceTransform) missileLauncher.maxOffBoresight = MLConfig.maxOffBoresight; //don't overwrite e.g. VLS launcher boresights so they can launch, but still have normal boresight on fired missiles
                missileLauncher.DetonateAtMinimumDistance = MLConfig.DetonateAtMinimumDistance;

                missileLauncher.detonationTime = MLConfig.detonationTime;
                missileLauncher.DetonationDistance = MLConfig.DetonationDistance;
                missileLauncher.BallisticOverShootFactor = MLConfig.BallisticOverShootFactor;
                missileLauncher.BallisticAngle = MLConfig.BallisticAngle;
                missileLauncher.CruiseAltitude = MLConfig.CruiseAltitude;
                missileLauncher.CruiseSpeed = MLConfig.CruiseSpeed;
                missileLauncher.CruisePredictionTime = MLConfig.CruisePredictionTime;
                if (!OverrideDropSettings)
                {
                    missileLauncher.decoupleForward = MLConfig.decoupleForward;
                    missileLauncher.dropTime = MLConfig.dropTime;
                    missileLauncher.decoupleSpeed = MLConfig.decoupleSpeed;
                }
                else
                {
                    missileLauncher.decoupleForward = true;
                    missileLauncher.dropTime = 0;
                    missileLauncher.decoupleSpeed = 10;
                }
                missileLauncher.clearanceRadius = MLConfig.clearanceRadius;
                missileLauncher.clearanceLength = MLConfig.clearanceLength;
                missileLauncher.maxAltitude = MLConfig.maxAltitude;
                missileLauncher.engageAir = MLConfig.engageAir;
                missileLauncher.engageGround = MLConfig.engageGround;
                missileLauncher.engageMissile = MLConfig.engageMissile;
                missileLauncher.engageSLW = MLConfig.engageSLW;
                missileLauncher.shortName = MLConfig.shortName;
                missileLauncher.blastRadius = -1;
                missileLauncher.blastRadius = MLConfig.blastRadius;
                missileLauncher.LoftMaxAltitude = MLConfig.LoftMaxAltitude;
                missileLauncher.LoftRangeOverride = MLConfig.LoftRangeOverride;
                missileLauncher.LoftAltitudeAdvMax = MLConfig.LoftAltitudeAdvMax;
                missileLauncher.LoftMinAltitude = MLConfig.LoftMinAltitude;
                missileLauncher.LoftAngle = MLConfig.LoftAngle;
                missileLauncher.LoftTermAngle = MLConfig.LoftTermAngle;
                missileLauncher.LoftRangeFac = MLConfig.LoftRangeFac;
                missileLauncher.LoftVelComp = MLConfig.LoftVelComp;
                missileLauncher.LoftVertVelComp = MLConfig.LoftVertVelComp;
                //missileLauncher.LoftAltComp = LoftAltComp;
                missileLauncher.terminalHomingRange = MLConfig.terminalHomingRange;
            }
            missileLauncher.GetBlastRadius();
            GUIUtils.RefreshAssociatedWindows(missileLauncher.part);
            missileLauncher.SetFields();
            missileLauncher.Sublabel = $"Guidance: {Enum.GetName(typeof(TargetingModes), missileLauncher.TargetingMode)}; Max Range: {Mathf.Round(missileLauncher.engageRangeMax / 100) / 10} km; Remaining: {missileLauncher.missilecount}";
        }


        void MakeMissileArray()
        {
            Transform launchTransform = part.FindModelTransform(launchTransformName);
            int missileNum = launchTransform.childCount;
            launchTransforms = new Transform[missileNum];
            for (int i = 0; i < missileNum; i++)
            {
                string launcherName = launchTransform.GetChild(i).name;
                int launcherIndex = int.Parse(launcherName.Substring(7)) - 1; //by coincidence, this is the same offset as rocket pods, which means the existing rocketlaunchers could potentially be converted over to homing munitions...
                launchTransforms[launcherIndex] = launchTransform.GetChild(i);
            }
            salvoSize = Mathf.Min((int)salvoSize, launchTransforms.Length);
            if (subMunitionPath != "")
            {
                PopulateMissileDummies(true);
            }
            UI_FloatRange salvo = (UI_FloatRange)Fields["salvoSize"].uiControlEditor;
            salvo.maxValue = launchTransforms.Length;
        }
        public void PopulateMissileDummies(bool refresh = false)
        {
            if (refresh && displayOrdinance)
            {
                SetupMissileDummyPool(subMunitionPath);
                foreach (var existingDummy in part.GetComponentsInChildren<MissileDummy>())
                {
                    existingDummy.Deactivate(); //if changing out missiles loaded into a VLS or similar, reset missile dummies
                }
            }
            for (int i = 0; i < launchTransforms.Length; i++)
            {
                if (!refresh)
                {
                    if (missileSpawner.ammoCount > i || isClusterMissile)
                    {
                        if (launchTransforms[i].localScale != new Vector3(1 / Scale, 1 / Scale, 1 / Length))
                            launchTransforms[i].localScale = new Vector3(1 / Scale, 1 / Scale, 1 / Length);
                    }
                    tubesFired = 0;
                }
                else
                {
                    if (!displayOrdinance) return;
                    GameObject dummy = mslDummyPool[subMunitionPath].GetPooledObject();
                    MissileDummy dummyThis = dummy.GetComponentInChildren<MissileDummy>();
                    dummyThis.AttachAt(part, launchTransforms[i]);
                    dummy.transform.localScale = dummyScale;
                    var mslAnim = dummy.GetComponentInChildren<Animation>();
                    if (mslAnim != null) mslAnim.enabled = false;
                }
            }
        }
        public void fireMissile(bool killWhenDone = false)
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (isLaunchedClusterMissile) salvoSize = launchTransforms.Length;
            if (!(missileSalvo != null))
            {
                wpm = VesselModuleRegistry.GetMissileFire(missileLauncher.SourceVessel, true);
                missileSalvo = StartCoroutine(salvoFire(killWhenDone));
                if (useSymCounterpart && !killWhenDone)
                {
                    using (List<Part>.Enumerator pSym = part.symmetryCounterparts.GetEnumerator())
                        while (pSym.MoveNext())
                        {
                            if (pSym.Current == null) continue;
                            if (pSym.Current != part && pSym.Current.vessel == vessel)
                            {
                                var ml = pSym.Current.FindModuleImplementing<MissileBase>();
                                if (ml == null) continue;
                                if (wpm != null) wpm.SendTargetDataToMissile(ml, false);
                                MissileLauncher launcher = ml as MissileLauncher;
                                if (launcher != null)
                                {
                                    if (launcher.HasFired) continue;
                                    launcher.FireMissile();
                                }
                            }
                        }
                }
            }
        }
        IEnumerator salvoFire(bool LaunchThenDestroy)
        {
            int launchesThisSalvo = 0;
            float timeGap = (60 / rippleRPM) * TimeWarp.CurrentRate;
            int TargetID = 0;
            bool missileRegistry = true;
            List<TargetInfo> firedTargets = [];
            //missileSpawner.MissileName = subMunitionName;

            if (wpm != null)
            {
                if (wpm.targetsAssigned.Count > 0) targetsAssigned.Clear();
                if (wpm.multiMissileTgtNum >= 2 || (missileLauncher.engageMissile && wpm.PDMslTgts.Count > 0))
                {
                    if (missileLauncher.engageMissile && wpm.PDMslTgts.Count > 0) targetsAssigned.AddRange(wpm.PDMslTgts);
                    else if (wpm.targetsAssigned.Count > 0) targetsAssigned.AddRange(wpm.targetsAssigned);
                }
                //Debug.Log($"[BDArmory.MultiMissileLauncherDebug]: Num of targets: {targetsAssigned.Count - 1}");
                if (targetsAssigned.Count < 1)
                    if (wpm.currentTarget != null) targetsAssigned.Add(wpm.currentTarget);
            }
            //else Debug.Log($"[BDArmory.MultiMissileLauncherDebug]: weaponmanager null!");
            missileLauncher.launched = true;
            if (deployState != null)
            {
                deployState.enabled = true;
                deployState.speed = deploySpeed / deployState.length;
                yield return new WaitWhileFixed(() => deployState != null && deployState.normalizedTime < 1); //wait for animation here
                if (deployState != null)
                {
                    deployState.normalizedTime = 1;
                    deployState.speed = 0;
                    deployState.enabled = false;
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MultiMissileLauncher] deploy anim complete");
                }
            }
            if (missileSpawner == null) yield break; // Died while waiting.
            for (int m = tubesFired; m < launchTransforms.Length; m++)
            {
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MultiMissileLauncher] starting ripple launch on tube {m}, ripple delay: {timeGap:F3}");
                yield return new WaitForSecondsFixed(timeGap);
                if (missileSpawner == null) yield break; // Died while waiting.
                if (launchesThisSalvo >= (int)salvoSize) //catch if launcher is trying to launch more missiles than it has
                {
                    //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MultiMissileLauncher] oops! firing more missiles than tubes or ammo");
                    break;
                }
                if (!isLaunchedClusterMissile && (missileSpawner.ammoCount < 1 && !BDArmorySettings.INFINITE_ORDINANCE))
                {
                    tubesFired = 0;
                    break;
                }
                tubesFired++;
                launchesThisSalvo++;
                launchTransforms[m].localScale = Vector3.zero;
                //time to deduct ammo = !clustermissile or cluster missile still on plane
                //time to not deduct ammo = in-flight clMsl
                if (!missileSpawner.SpawnMissile(launchTransforms[m], offset * Length, !isLaunchedClusterMissile))
                {
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.LogWarning($"[BDArmory.MissileLauncher]: Failed to spawn a missile in {missileSpawner} on {vessel.vesselName}");
                    continue;
                }
                if (ignoreLauncherColliders)
                {
                    var missileCOL = missileSpawner.SpawnedMissile.collider;
                    if (missileCOL) missileCOL.enabled = false;
                }
                MissileLauncher ml = missileSpawner.SpawnedMissile.FindModuleImplementing<MissileLauncher>();
                MultiMissileLauncher mml = missileSpawner.SpawnedMissile.FindModuleImplementing<MultiMissileLauncher>();
                yield return new WaitUntilFixed(() => ml == null || ml.SetupComplete); // Wait until missile fully initialized.
                if (ml == null || ml.gameObject == null || !ml.gameObject.activeInHierarchy)
                {
                    if (ml is not null) Destroy(ml); // The gameObject is gone, make sure the module goes too.
                    continue; // The missile died for some reason, try the next tube.
                }
                if (mml != null && mml.isClusterMissile)
                {
                    mml.clusterMissileTriggerDist = clusterMissileTriggerDist; 
                }
                var tnt = VesselModuleRegistry.GetModule<BDExplosivePart>(vessel, true);
                if (tnt != null)
                {
                    tnt.sourcevessel = missileLauncher.SourceVessel;
                    tnt.isMissile = true;
                }
                if (ignoreLauncherColliders)
                    ml.useSimpleDragTemp = true;
                ml.Team = Team;
                ml.SourceVessel = missileLauncher.SourceVessel;
                if (string.IsNullOrEmpty(ml.GetShortName()))
                {
                    ml.shortName = missileLauncher.GetShortName() + " Missile";
                }
                if (BDArmorySettings.DEBUG_MISSILES) ml.shortName = $"{ml.SourceVessel.GetName()}'s {missileLauncher.GetShortName()} Missile";
                ml.vessel.vesselName = ml.GetShortName();
                ml.TimeFired = Time.time;
                if (turret) ml.missileTurret = turret;
                if (!isClusterMissile) ml.DetonationDistance = missileLauncher.DetonationDistance;
                ml.DetonateAtMinimumDistance = missileLauncher.DetonateAtMinimumDistance;
                ml.decoupleForward = missileLauncher.decoupleForward;
                ml.dropTime = missileLauncher.dropTime;
                ml.decoupleSpeed = missileLauncher.decoupleSpeed;
                ml.guidanceActive = true;
                ml.detonationTime = missileLauncher.detonationTime;
                ml.engageAir = missileLauncher.engageAir;
                ml.engageGround = missileLauncher.engageGround;
                ml.engageMissile = missileLauncher.engageMissile;
                ml.engageSLW = missileLauncher.engageSLW;
                ml.gLimit = missileLauncher.gLimit;
                ml.gMargin = missileLauncher.gMargin;

                if (missileLauncher.GuidanceMode == GuidanceModes.AGMBallistic)
                {
                    ml.BallisticOverShootFactor = missileLauncher.BallisticOverShootFactor;
                    ml.BallisticAngle = missileLauncher.BallisticAngle;
                }
                if (missileLauncher.GuidanceMode == GuidanceModes.Cruise)
                {
                    ml.CruiseAltitude = missileLauncher.CruiseAltitude;
                    ml.CruiseSpeed = missileLauncher.CruiseSpeed;
                    ml.CruisePredictionTime = missileLauncher.CruisePredictionTime;
                }
                if (missileLauncher.GuidanceMode == GuidanceModes.AAMLoft)
                {
                    ml.LoftMaxAltitude = missileLauncher.LoftMaxAltitude;
                    ml.LoftRangeOverride = missileLauncher.LoftRangeOverride;
                    ml.LoftAltitudeAdvMax = missileLauncher.LoftAltitudeAdvMax;
                    ml.LoftMinAltitude = missileLauncher.LoftMinAltitude;
                    ml.LoftAngle = missileLauncher.LoftAngle;
                    ml.LoftTermAngle = missileLauncher.LoftTermAngle;
                    ml.LoftRangeFac = missileLauncher.LoftRangeFac;
                    ml.LoftVelComp = missileLauncher.LoftVelComp;
                    ml.LoftVertVelComp = missileLauncher.LoftVertVelComp;
                    //ml.LoftAltComp = missileLauncher.LoftAltComp;
                    ml.terminalHomingRange = missileLauncher.terminalHomingRange;
                    ml.homingModeTerminal = missileLauncher.homingModeTerminal;
                    ml.pronavGain = missileLauncher.pronavGain;
                    ml.loftState = LoftStates.Boost;
                    ml.TimeToImpact = float.PositiveInfinity;
                    ml.initMaxAoA = missileLauncher.maxAoA;
                }
                /*if (missileLauncher.GuidanceMode == GuidanceModes.AAMHybrid)
                {
                    ml.pronavGain = missileLauncher.pronavGain;
                    ml.terminalHomingRange = missileLauncher.terminalHomingRange;
                    ml.homingModeTerminal = missileLauncher.homingModeTerminal;
                }*/
                if (missileLauncher.GuidanceMode == GuidanceModes.APN || missileLauncher.GuidanceMode == GuidanceModes.PN)
                    ml.pronavGain = missileLauncher.pronavGain;

                if (missileLauncher.GuidanceMode == GuidanceModes.Kappa)
                {
                    ml.kappaAngle = missileLauncher.kappaAngle;
                    ml.LoftAngle = missileLauncher.LoftAngle;
                    ml.LoftMaxAltitude = missileLauncher.LoftMaxAltitude;
                    ml.LoftRangeFac = missileLauncher.LoftRangeFac;
                    ml.LoftVertVelComp = missileLauncher.LoftVertVelComp;
                    ml.LoftRangeOverride = missileLauncher.LoftRangeOverride;
                    ml.loftState = LoftStates.Boost;
                    ml.LoftTermAngle = missileLauncher.LoftTermAngle;
                }


                ml.terminalHoming = missileLauncher.terminalHoming;
                if (missileLauncher.terminalHoming)
                {
                    if (missileLauncher.homingModeTerminal == GuidanceModes.AGMBallistic)
                    {
                        ml.BallisticOverShootFactor = missileLauncher.BallisticOverShootFactor; //are some of these null, and causeing this to quit? 
                        ml.BallisticAngle = missileLauncher.BallisticAngle;
                    }
                    if (missileLauncher.homingModeTerminal == GuidanceModes.Cruise)
                    {
                        ml.CruiseAltitude = missileLauncher.CruiseAltitude;
                        ml.CruiseSpeed = missileLauncher.CruiseSpeed;
                        ml.CruisePredictionTime = missileLauncher.CruisePredictionTime;
                    }
                    if (missileLauncher.homingModeTerminal == GuidanceModes.AAMLoft)
                    {
                        ml.LoftMaxAltitude = missileLauncher.LoftMaxAltitude;
                        ml.LoftRangeOverride = missileLauncher.LoftRangeOverride;
                        ml.LoftAltitudeAdvMax = missileLauncher.LoftAltitudeAdvMax;
                        ml.LoftMinAltitude = missileLauncher.LoftMinAltitude;
                        ml.LoftAngle = missileLauncher.LoftAngle;
                        ml.LoftTermAngle = missileLauncher.LoftTermAngle;
                        ml.LoftRangeFac = missileLauncher.LoftRangeFac;
                        ml.LoftVelComp = missileLauncher.LoftVelComp;
                        ml.LoftVertVelComp = missileLauncher.LoftVertVelComp;
                        //ml.LoftAltComp = missileLauncher.LoftAltComp;
                        ml.pronavGain = missileLauncher.pronavGain;
                        ml.loftState = LoftStates.Boost;
                        ml.TimeToImpact = float.PositiveInfinity;
                        ml.initMaxAoA = missileLauncher.maxAoA;
                    }
                    if (missileLauncher.homingModeTerminal == GuidanceModes.APN || missileLauncher.homingModeTerminal == GuidanceModes.PN)
                        ml.pronavGain = missileLauncher.pronavGain;

                    if (missileLauncher.homingModeTerminal == GuidanceModes.Kappa)
                    {
                        ml.kappaAngle = missileLauncher.kappaAngle;
                        ml.LoftAngle = missileLauncher.LoftAngle;
                        ml.LoftMaxAltitude = missileLauncher.LoftMaxAltitude;
                        ml.LoftRangeFac = missileLauncher.LoftRangeFac;
                        ml.LoftVertVelComp = missileLauncher.LoftVertVelComp;
                        ml.LoftRangeOverride = missileLauncher.LoftRangeOverride;
                        ml.loftState = LoftStates.Boost;
                        ml.LoftTermAngle = missileLauncher.LoftTermAngle;
                    }

                    ml.terminalHomingRange = missileLauncher.terminalHomingRange;
                    ml.homingModeTerminal = missileLauncher.homingModeTerminal;
                    ml.terminalHomingActive = false;
                }

                //ml.decoupleSpeed = 5;
                if (missileLauncher.GuidanceMode == GuidanceModes.AGM)
                    ml.maxAltitude = missileLauncher.maxAltitude;
                ml.terminalGuidanceShouldActivate = missileLauncher.terminalGuidanceShouldActivate;
                //if (isClusterMissile) ml.multiLauncher.overrideReferenceTransform = true;
                if (wpm != null)
                {
                    if (ml.TargetingMode == TargetingModes.Heat || ml.TargetingMode == TargetingModes.Radar || ml.TargetingMode == TargetingModes.Gps)
                    {
                        //Debug.Log($"[BDArmory.MultiMissileLauncherDebug]: Beginning target distribution; Num of targets: {targetsAssigned.Count - 1}; wpm targets: {wpm.targetsAssigned.Count}");
                        if (targetsAssigned.Count > 0)
                        {
                            if (TargetID <= Mathf.Min(targetsAssigned.Count - 1, wpm.multiMissileTgtNum))
                            {
                                for (int t = TargetID; t < Mathf.Min(targetsAssigned.Count - 1, wpm.multiMissileTgtNum); t++) //MML targeting independant of MissileFire target assignment,
                                {// and each MMl will be independantly working off the same targets list, iterating over the same first couple targets
                                    if (wpm.missilesAway.ContainsKey(targetsAssigned[t]))
                                    {
                                        //Debug.Log($"[MML Targeting Debug] target {t} {targetsAssigned[t].Vessel.GetName()} already has {wpm.missilesAway[targetsAssigned[t]]}/{wpm.maxMissilesOnTarget} fired on it...");
                                        if (wpm.missilesAway[targetsAssigned[t]] < wpm.maxMissilesOnTarget)
                                        {
                                            TargetID = t; //so go through and advance the target list start point based on who's already been fully engaged
                                                          //Debug.Log($"[MML Targeting Debug] advancing targetID to {TargetID}: {targetsAssigned[TargetID].Vessel.GetName()}");
                                            break;
                                        }
                                    }
                                    else
                                    {
                                        TargetID = t;
                                        //Debug.Log($"[MML Targeting Debug] setting targetID to {TargetID}: {targetsAssigned[TargetID].Vessel.GetName()}");
                                        break;
                                    }
                                }
                            }
                            //Debug.Log($"[MML Targeting Debug] TargetID is {TargetID} of {Mathf.Min((targetsAssigned.Count), wpm.multiMissileTgtNum)}");
                            if (TargetID > Mathf.Min(targetsAssigned.Count - 1, wpm.multiMissileTgtNum))
                            {
                                TargetID = 0; //if more missiles than targets, loop target list
                                missileRegistry = false;  //this isn't ignoring subsequent missiles in the salvo for some reason?
                                //Debug.Log($"[MML Targeting Debug] Reached end of target list, cycling");
                            }
                            if (targetsAssigned.Count > 0 && targetsAssigned[TargetID] != null && targetsAssigned[TargetID].Vessel != null)
                            {
                                if ((Vector3.Angle(targetsAssigned[TargetID].position - missileLauncher.MissileReferenceTransform.position, missileLauncher.GetForwardTransform()) < missileLauncher.maxOffBoresight) //is the target more-or-less in front of the missile(launcher)?
                                    && ((ml.engageAir && targetsAssigned[TargetID].isFlying) ||
                                    (ml.engageGround && targetsAssigned[TargetID].isLandedOrSurfaceSplashed) ||
                                    (ml.engageSLW && targetsAssigned[TargetID].isUnderwater) ||
                                    (ml.engageMissile && targetsAssigned[TargetID].isMissile))) //check engagement envelope
                                {
                                    if (ml.TargetingMode == TargetingModes.Heat) //need to input a heattarget, else this will just return MissileFire.CurrentTarget
                                    {
                                        Vector3 direction = (targetsAssigned[TargetID].position * targetsAssigned[TargetID].velocity.magnitude) - missileLauncher.MissileReferenceTransform.position;
                                        ml.heatTarget = BDATargetManager.GetHeatTarget(ml.SourceVessel, ml.vessel, new Ray(missileLauncher.MissileReferenceTransform.position + (5 * missileLauncher.GetForwardTransform()), direction), TargetSignatureData.noTarget, ml.lockedSensorFOV * 0.5f, ml.heatThreshold, ml.frontAspectHeatModifier, true, ml.lockedSensorFOVBias, ml.lockedSensorVelocityBias, wpm, targetsAssigned[TargetID]);
                                    }
                                    if (ml.TargetingMode == TargetingModes.Radar)
                                    {
                                        AssignRadarTarget(ml, targetsAssigned[TargetID].Vessel);
                                    }
                                    if (ml.TargetingMode == TargetingModes.Gps)
                                    {
                                        ml.targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(targetsAssigned[TargetID].Vessel.CoM, vessel.mainBody);
                                    }
                                    ml.targetVessel = targetsAssigned[TargetID];
                                    ml.TargetAcquired = true;
                                    firedTargets.Add(targetsAssigned[TargetID]);
                                    if (BDArmorySettings.DEBUG_MISSILES)
                                        Debug.Log($"[BDArmory.MultiMissileLauncher] Assigning target {TargetID}: {targetsAssigned[TargetID].Vessel.GetName()}; total possible targets {targetsAssigned.Count - 1}");
                                }
                                else //else try remaining targets on the list. 
                                {
                                    for (int t = TargetID; t < targetsAssigned.Count - 1; t++)
                                    {
                                        if (targetsAssigned[t] == null) continue;
                                        if ((ml.engageAir && !targetsAssigned[t].isFlying) ||
                                            (ml.engageGround && !targetsAssigned[t].isLandedOrSurfaceSplashed) ||
                                            (ml.engageSLW && !targetsAssigned[t].isUnderwater) ||
                                            (ml.engageMissile && !targetsAssigned[t].isMissile)) continue; //check engagement envelope

                                        if (Vector3.Angle(targetsAssigned[t].position - missileLauncher.MissileReferenceTransform.position, missileLauncher.GetForwardTransform()) < missileLauncher.maxOffBoresight) //is the target more-or-less in front of the missile(launcher)?
                                        {
                                            if (ml.TargetingMode == TargetingModes.Heat)
                                            {
                                                Vector3 direction = (targetsAssigned[t].position * targetsAssigned[t].velocity.magnitude) - missileLauncher.MissileReferenceTransform.position;
                                                ml.heatTarget = BDATargetManager.GetHeatTarget(ml.SourceVessel, ml.vessel, new Ray(missileLauncher.MissileReferenceTransform.position + (5 * missileLauncher.GetForwardTransform()), direction), TargetSignatureData.noTarget, ml.lockedSensorFOV * 0.5f, ml.heatThreshold, ml.frontAspectHeatModifier, true, ml.lockedSensorFOVBias, ml.lockedSensorVelocityBias, wpm, targetsAssigned[t]);
                                            }
                                            if (ml.TargetingMode == TargetingModes.Radar)
                                            {
                                                AssignRadarTarget(ml, targetsAssigned[t].Vessel);
                                            }
                                            if (ml.TargetingMode == TargetingModes.Gps)
                                            {
                                                ml.targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(targetsAssigned[t].Vessel.CoM, vessel.mainBody);
                                            }
                                            ml.targetVessel = targetsAssigned[t];
                                            ml.TargetAcquired = true;
                                            firedTargets.Add(targetsAssigned[t]);
                                            if (BDArmorySettings.DEBUG_MISSILES)
                                                Debug.Log($"[BDArmory.MultiMissileLauncher] Assigning backup target (targetID {TargetID}) {targetsAssigned[t].Vessel.GetName()}");
                                        }
                                    }
                                    if (BDArmorySettings.DEBUG_MISSILES)
                                        Debug.Log($"[BDArmory.MultiMissileLauncher] Couldn't assign valid target, trying from beginning of target list");
                                    if (ml.targetVessel == null) //check targets that were already assigned and passed. using the above iterator to prevent all targets outisde allowed FoV or engagement enveolpe from being assigned the firest possible target by checking later ones first
                                    {
                                        using (List<TargetInfo>.Enumerator item = targetsAssigned.GetEnumerator())
                                            while (item.MoveNext())
                                            {
                                                if (item.Current == null) continue;
                                                if (item.Current.Vessel == null) continue;
                                                if ((ml.engageAir && !item.Current.isFlying) ||
                                                    (ml.engageGround && !item.Current.isLandedOrSurfaceSplashed) ||
                                                    (ml.engageSLW && !item.Current.isUnderwater) ||
                                                    (ml.engageMissile && !item.Current.isMissile)) continue; //check engagement envelope
                                                if (Vector3.Angle(item.Current.position - missileLauncher.MissileReferenceTransform.position, missileLauncher.GetForwardTransform()) < missileLauncher.maxOffBoresight) //is the target more-or-less in front of the missile(launcher)?
                                                {
                                                    if (ml.TargetingMode == TargetingModes.Heat)
                                                    {
                                                        Vector3 direction = (item.Current.position * item.Current.velocity.magnitude) - missileLauncher.MissileReferenceTransform.position;
                                                        ml.heatTarget = BDATargetManager.GetHeatTarget(ml.SourceVessel, ml.vessel, new Ray(missileLauncher.MissileReferenceTransform.position + (5 * missileLauncher.GetForwardTransform()), direction), TargetSignatureData.noTarget, ml.lockedSensorFOV * 0.5f, ml.heatThreshold, ml.frontAspectHeatModifier, true, ml.lockedSensorFOVBias, ml.lockedSensorVelocityBias, wpm, item.Current);
                                                    }
                                                    if (ml.TargetingMode == TargetingModes.Radar)
                                                    {
                                                        AssignRadarTarget(ml, item.Current.Vessel);
                                                    }
                                                    if (ml.TargetingMode == TargetingModes.Gps)
                                                    {
                                                        ml.targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(item.Current.Vessel.CoM, vessel.mainBody);
                                                    }
                                                    ml.targetVessel = item.Current;
                                                    ml.TargetAcquired = true;
                                                    firedTargets.Add(item.Current);
                                                    if (BDArmorySettings.DEBUG_MISSILES)
                                                        Debug.Log($"[BDArmory.MultiMissileLauncher] original target out of sensor range; engaging {item.Current.Vessel.GetName()}");
                                                    break;
                                                }
                                            }
                                    }
                                }
                                TargetID++;
                                if (firedTargets.Count >= wpm.multiMissileTgtNum)
                                {
                                    targetsAssigned.Clear();
                                    targetsAssigned.AddRange(firedTargets); //we've found targets up to our target allowance; cull list down to just those for distributing remaining missiles of the salvo between, if any.
                                }
                            }
                            else wpm.SendTargetDataToMissile(ml, false);
                        }
                        else
                        {
                            if (tubesFired > 1) missileRegistry = false;
                            if (ml.TargetingMode == TargetingModes.Gps) //missileFire's GPS coords were snapshotted before anim delay (if any); refresh coords to target's current position post delay
                            {
                                Vector3d designatedGPScoords = Vector3.zero;
                                if (missileLauncher.targetVessel) designatedGPScoords = VectorUtils.WorldPositionToGeoCoords(missileLauncher.targetVessel.Vessel.CoM, vessel.mainBody);
                                if (designatedGPScoords != Vector3d.zero)
                                {
                                    ml.targetGPSCoords = designatedGPScoords;
                                    ml.targetVessel = wpm.currentTarget;
                                    ml.TargetAcquired = true;
                                }
                            }
                            else wpm.SendTargetDataToMissile(ml, false);
                        }
                    }
                    else
                    {
                        wpm.SendTargetDataToMissile(ml, false);
                    }
                    ml.GpsUpdateMax = wpm.GpsUpdateMax;
                }
                if (missileRegistry)
                {
                    BDATargetManager.FiredMissiles.Add(ml); //so multi-missile salvoes only count as a single missile fired by the WM for maxMissilesPerTarget
                }
                ml.launched = true;
                if (ml.TargetPosition == Vector3.zero) ml.TargetPosition = missileLauncher.MissileReferenceTransform.position + (missileLauncher.MissileReferenceTransform.forward * 5000); //set initial target position so if no target update, missileBase will count a miss if it nears this point or is flying post-thrust
                ml.MissileLaunch();
                if (wpm != null) wpm.heatTarget = TargetSignatureData.noTarget;
            }
            if (wpm != null)
            {
                using (List<TargetInfo>.Enumerator Tgt = targetsAssigned.GetEnumerator())
                    while (Tgt.MoveNext())
                    {
                        if (Tgt.Current == null) continue;
                        if (!firedTargets.Contains(Tgt.Current))
                            Tgt.Current.Disengage(wpm);
                    }
            }
            if (deployState != null)
            {
                yield return new WaitForSecondsFixed(0.5f); //wait for missile to clear bay
                if (deployState != null)
                {
                    deployState.enabled = true;
                    deployState.speed = -deploySpeed / deployState.length;
                    yield return new WaitWhileFixed(() => deployState != null && deployState.normalizedTime > 0);
                    if (deployState != null)
                    {
                        deployState.normalizedTime = 0;
                        deployState.speed = 0;
                        deployState.enabled = false;
                    }
                }
            }
            if (missileLauncher == null) yield break;
            if (tubesFired >= launchTransforms.Length) //add a timer for reloading a partially emptied MML if it hasn't been used for a while?
            {
                if (!isLaunchedClusterMissile && (BDArmorySettings.INFINITE_ORDINANCE || missileSpawner.ammoCount >= (int)salvoSize))
                    if (!(missileLauncher.reloadRoutine != null))
                    {
                        missileLauncher.reloadRoutine = StartCoroutine(missileLauncher.MissileReload());
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MultiMissileLauncher] all submunitions fired. Reloading");
                    }
            }
            missileLauncher.GetMissileCount();
            if (LaunchThenDestroy)
            {
                if (part != null)
                {
                    missileLauncher.DestroyMissile();
                }
            }
            else
            {
                if ((int)salvoSize < launchTransforms.Length && missileLauncher.reloadRoutine == null && (BDArmorySettings.INFINITE_ORDINANCE || missileSpawner.ammoCount > 0))
                {
                    if (launcherCooldown > 0)
                    {
                        missileLauncher.heatTimer = launcherCooldown;
                        yield return new WaitForSecondsFixed(launcherCooldown);
                        if (missileLauncher == null) yield break;
                        missileLauncher.launched = false;
                        missileLauncher.heatTimer = -1;
                    }
                    else
                    {
                        missileLauncher.heatTimer = -1;
                        missileLauncher.launched = false;
                    }
                }
                missileSalvo = null;
            }
        }

        void AssignRadarTarget(MissileLauncher ml, Vessel targetV)
        {
            ml.vrd = wpm.vesselRadarData;
            if (wpm.vesselRadarData) wpm.vesselRadarData.TryLockTarget(targetV);
            bool foundTarget = false;
            if (wpm.vesselRadarData && wpm.vesselRadarData.locked) //if we have existing radar locks, use thsoe
            {
                List<TargetSignatureData> possibleTargets = wpm.vesselRadarData.GetLockedTargets();
                TargetSignatureData lockedTarget = TargetSignatureData.noTarget;
                for (int i = 0; i < possibleTargets.Count; i++)
                {
                    if (possibleTargets[i].vessel == targetV)
                    {
                        lockedTarget = possibleTargets[i]; //send correct targetlock if firing multiple SARH missiles
                        foundTarget = true;
                        if (BDArmorySettings.DEBUG_MISSILES)
                            Debug.Log($"[BDArmory.MultiMissileLauncher] Found locked Radar target {targetV.GetName()}");
                        break;
                    }
                }
                ml.radarTarget = lockedTarget;
            }
            if (!foundTarget)
            {
                if (missileLauncher.MissileReferenceTransform.position.CloserToThan(targetV.CoM, ml.activeRadarRange))
                {
                    TargetSignatureData[] scannedTargets = new TargetSignatureData[(int)wpm.multiMissileTgtNum];
                    RadarUtils.RadarUpdateMissileLock(new Ray(ml.transform.position, ml.GetForwardTransform()), ml.maxOffBoresight / 2, ref scannedTargets, 0.4f, ml);
                    TargetSignatureData lockedTarget = TargetSignatureData.noTarget;

                    for (int i = 0; i < scannedTargets.Length; i++)
                    {
                        if (scannedTargets[i].exists && scannedTargets[i].vessel == targetV)
                        {
                            lockedTarget = scannedTargets[i];
                            if (BDArmorySettings.DEBUG_MISSILES)
                                Debug.Log($"[BDArmory.MultiMissileLauncher] Found Radar target {targetV.GetName()}");
                            break;
                        }
                    }
                    ml.radarTarget = lockedTarget;
                    if (BDArmorySettings.DEBUG_MISSILES)
                    {
                        if (!ml.radarTarget.exists)
                            Debug.Log($"[BDArmory.MultiMissileLauncher] unable to lock Radar target {targetV.GetName()}, skipping");
                    }
                }
                else
                {
                    if (ml.radarLOAL)
                    {
                        ml.TargetPosition = targetV.CoM; //set initial target position to fly towards if LOAL instead of straight from launcher
                        ml.radarTarget = TargetSignatureData.noTarget;
                    }
                }
            }
            //Debug.Log($"[BDArmory.MultiMissileLauncher] {targetV.GetName()}; assigned radar target {(ml.radarTarget.exists ? ml.radarTarget.vessel.GetName() : "null")}");
        }

        public void SetupMissileDummyPool(string modelpath)
        {
            var key = modelpath;
            if (!mslDummyPool.ContainsKey(key) || mslDummyPool[key] == null)
            {
                var Template = GameDatabase.Instance.GetModel(modelpath);
                if (Template == null)
                {
                    Debug.LogError("[BDArmory.MultiMissileLauncher]: model '" + modelpath + "' not found. Expect exceptions if trying to use this missile.");
                    return;
                }
                Template.SetActive(false);
                Template.AddComponent<MissileDummy>();
                mslDummyPool[key] = ObjectPool.CreateObjectPool(Template, 10, true, true);
            }

        }
        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();

            output.Append(Environment.NewLine);
            output.AppendLine($"Multi Missile Launcher:");
            output.AppendLine($"- Salvo Size: {salvoSize}");
            output.AppendLine($"- Cooldown: {launcherCooldown} s");
            output.AppendLine($" - Warhead:");
            AvailablePart missilePart = null;
            using (var parts = PartLoader.LoadedPartsList.GetEnumerator())
                while (parts.MoveNext())
                {
                    if (parts.Current == null) continue;
                    //Debug.Log($"[BDArmory.MML]: Looking for {subMunitionName}");
                    if (parts.Current.partConfig == null || parts.Current.partPrefab == null)
                        continue;
                    if (!parts.Current.partPrefab.partInfo.name.Contains(subMunitionName)) continue;
                    missilePart = parts.Current;
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MML]: found {missilePart.partPrefab.partInfo.name}");
                    break;
                }
            if (missilePart != null)
            {
                var MML = (missilePart.partPrefab.FindModuleImplementing<MultiMissileLauncher>());
                if (MML != null)
                {
                    if (MML.isClusterMissile)
                    {
                        output.AppendLine($"Cluster Missile:");
                        output.AppendLine($"- SubMunition Count: {MML.salvoSize} ");
                        output.AppendLine($"- Blast radius: {Math.Round(BlastPhysicsUtils.CalculateBlastRange(tntMass), 2)} m");
                        output.AppendLine($"- tnt Mass: {tntMass} kg");
                    }
                }
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MML]: has BDExplosivePart: {missilePart.partPrefab.FindModuleImplementing<BDExplosivePart>()}");
                var ExplosivePart = (missilePart.partPrefab.FindModuleImplementing<BDExplosivePart>());
                if (ExplosivePart != null)
                {
                    ExplosivePart.ParseWarheadType();
                    if (missilePart.partPrefab.FindModuleImplementing<ClusterBomb>())
                    {
                        output.AppendLine($"Cluster Bomb:");
                        output.AppendLine($"- Sub-Munition Count: {missilePart.partPrefab.FindModuleImplementing<ClusterBomb>().submunitions.Count} ");
                    }
                    output.AppendLine($"- Blast radius: {Math.Round(BlastPhysicsUtils.CalculateBlastRange(ExplosivePart.tntMass), 2)} m");
                    output.AppendLine($"- tnt Mass: {ExplosivePart.tntMass} kg");
                    output.AppendLine($"- {ExplosivePart.warheadReportingName} warhead");
                }
                var EMP = (missilePart.partPrefab.FindModuleImplementing<ModuleEMP>());
                if (EMP != null)
                {
                    output.AppendLine($"Electro-Magnetic Pulse");
                    output.AppendLine($"- EMP Blast Radius: {EMP.proximity} m");
                }
                var Nuke = (missilePart.partPrefab.FindModuleImplementing<BDModuleNuke>());
                if (Nuke != null)
                {
                    float yield = Nuke.yield;
                    float radius = Nuke.thermalRadius;
                    float EMPRadius = Nuke.isEMP ? BDAMath.Sqrt(yield) * 500 : -1;
                    output.AppendLine($"- Yield: {yield} kT");
                    output.AppendLine($"- Max radius: {radius} m");
                    if (EMPRadius > 0) output.AppendLine($"- EMP Blast Radius: {EMPRadius} m");
                }
            }
            return output.ToString();
        }
    }
}
