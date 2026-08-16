using BDArmory.Competition;
using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Guidances;
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

    public class MultiMissileLauncher : BDAPartModule
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
        public int launchTubes;
        [KSPField(isPersistant = true)] public string subMunitionName; //name of missile in .cfg - e.g. "bahaAim120"
        [KSPField(isPersistant = true)] public string subMunitionPath; //model path for missile
        public float missileMass = 0.1f;
        [KSPField] public string launchTransformName; //name of transform launcTransforms are parented to - see Rocketlauncher transform hierarchy
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
        [KSPField] public bool displayOrdinanceHasColliders = true; //should missile dummies have colliders? (offers somewhat of a performance boost if disabled)
        [KSPField] public bool permitJettison = false; //allow jettisoning of missiles for multimissile launchrails and similar
        [KSPField] public bool ignoreLauncherColliders = false; //temporarily disable missile colliders to let them clear the launcher, for large-scale VLS or similar. -WARNING- has some effect on missile flight
        AnimationState deployState;
        public ModuleMissileRearm missileSpawner = null;
        MissileLauncher missileLauncher = null;
        [KSPField] public bool adjustMissileVOffset = false; //should missile vertical offset dynamically adjust based on missile diameter (for MMLs on adjustable rails, etc)
        [KSPField(isPersistant = true)] float attachedMissileDiameter = 0;
        MissileFire FiredByWM = null; // Assigned when fired and then not updated even if the parent craft changes their primary WM.
        private int tubesFired = 0;
        [KSPField(isPersistant = true)]
        private bool LoadoutModified = false;
        public BDTeam Team = null;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_ArmorWidth"),// Length
    UI_FloatRange(minValue = 0.5f, maxValue = 2, stepIncrement = 0.05f, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.Editor)]
        public float Scale = 1;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_ArmorLength"),// Length
    UI_FloatRange(minValue = 0.5f, maxValue = 2, stepIncrement = 0.05f, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.Editor)]
        public float Length = 1;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_Offset"),// Ordnance Offset
    UI_FloatRange(minValue = -1, maxValue = 1, stepIncrement = 0.1f, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.Editor)]
        public float attachOffset = 0;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_Deploy_Time"),// Deploy Time
    UI_FloatRange(minValue = 0, maxValue = 5, stepIncrement = 0.1f, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.Editor)]
        public float deployTime = 0.5f;

        [KSPField]
        public float scaleMax = 2;

        [KSPField]
        public float offsetMax = 1;

        [KSPField]
        public string lengthTransformName;
        Transform LengthTransform;
        Vector3 LengthTransformOrigScale;

        [KSPField]
        public string scaleTransformName;
        Transform ScaleTransform;
        Vector3 ScaleTransformOrigScale;

        public MissileTurret turret;

        List<TargetInfo> targetsAssigned;

        public bool toggleBay = true;
        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_ToggleAnimation", active = true)]//Disable Engage Options
        public void ToggleBay()
        {
            toggleBay = !toggleBay;

            if (toggleBay == false)
            {
                Events[nameof(ToggleBay)].guiName = StringUtils.Localize("#autoLOC_502069");//"Open"
            }
            else
            {
                Events[nameof(ToggleBay)].guiName = StringUtils.Localize("#autoLOC_502051");//""Close"
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
        public bool IsDeployed => deployState == null || deployState.normalizedTime == 1; // Either doesn't have a deploy anim, or it's deployed.
        public IEnumerator SetDeployed(bool deployed)
        {
            if (deployState != null)
            {
                deployState.enabled = true;
                if (deployed)
                {
                    deployState.speed = deploySpeed / deployState.length;
                    yield return new WaitWhileFixed(() => deployState != null && deployState.normalizedTime < 1);
                }
                else
                {
                    deployState.speed = -deploySpeed / deployState.length;
                    yield return new WaitWhileFixed(() => deployState != null && deployState.normalizedTime > 0);
                }
                if (deployState != null)
                {
                    deployState.normalizedTime = deployed ? 1 : 0;
                    deployState.speed = 0;
                    deployState.enabled = false;
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
                Events[nameof(ToggleBay)].guiActiveEditor = true;
                deployState = GUIUtils.SetUpSingleAnimation(deployAnimationName, part);
                if (deployState != null)
                {
                    deployState.normalizedTime = HighLogic.LoadedSceneIsFlight ? 0 : toggleBay ? 1 : 0;
                    deployState.speed = 0;
                    deployState.enabled = true;
                }
            }
            targetsAssigned = new List<TargetInfo>();
            launchTubes = launchTransforms.Length;
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
                //missileSpawner.maxAmmo = isClusterMissile ? 1 : salvoSize * 5;
                missileSpawner.railAmmo = isClusterMissile ? 1 : launchTubes;
                missileSpawner.maxAmmo = isClusterMissile ? 1 : launchTubes;
                missileSpawner.Fields[nameof(missileSpawner.railAmmo)].guiActiveEditor = false;
                missileSpawner.MissileName = subMunitionName;
                if (!isClusterMissile) //Clustermissiles replace/generate MMR on launch, other missiles should have it in the .cfg
                    Debug.LogError($"[BDArmory.MultiMissileLauncher]: no ModuleMissileRearm on {part.name}. Please fix your .cfg");
            }
            if (BDArmorySettings.LIMITED_ORDINANCE) missileSpawner.railAmmo = isClusterMissile ? 1 : launchTubes;
            missileSpawner.isMultiLauncher = isMultiLauncher;
            if (missileLauncher != null) //deal with race condition/'MissileLauncher' loading before 'MultiMissileLauncher' and 'ModuleMissilerearm' by moving all relevant flags and values to a single location
            {
                missileLauncher.reloadableRail = missileSpawner;
                missileLauncher.hasAmmo = true;
                missileLauncher.multiLauncher = this;

                if (isClusterMissile)
                {
                    if (isLaunchedClusterMissile)
                    {
                        missileSpawner.MissileName = subMunitionName;
                        missileSpawner.railAmmo = launchTransforms.Length;
                        missileLauncher.DetonationDistance = clusterMissileTriggerDist;
                        missileLauncher.blastRadius = clusterMissileTriggerDist;
                    }
                    else
                    {
                        missileSpawner.MissileName = missileLauncher.missileName; //ClMsl set to base name in case of reloadable rails, reset to submuition name after launch
                        missileLauncher.DetonationDistance = 0;
                        missileLauncher.blastRadius = 0;
                    }
                    missileLauncher.Fields[nameof(missileLauncher.DetonationDistance)].guiActive = false;
                    missileLauncher.Fields[nameof(missileLauncher.DetonationDistance)].guiActiveEditor = false;
                    missileLauncher.DetonateAtMinimumDistance = false;
                    missileLauncher.Fields[nameof(missileLauncher.DetonateAtMinimumDistance)].guiActive = true;
                    missileLauncher.Fields[nameof(missileLauncher.DetonateAtMinimumDistance)].guiActiveEditor = true;
                    if (missileSpawner.maxAmmo == 1)
                    {
                        missileSpawner.Fields[nameof(missileSpawner.railAmmo)].guiActive = false;
                        missileSpawner.Fields[nameof(missileSpawner.railAmmo)].guiActiveEditor = false;
                    }
                }
                else
                {
                    Fields[nameof(clusterMissileTriggerDist)].guiActive = false;
                    Fields[nameof(clusterMissileTriggerDist)].guiActiveEditor = false;
                    missileLauncher.MissileReferenceTransform = launchTransforms[0];
                }
                Fields[nameof(salvoSize)].guiActive = setSalvoSize;
                Fields[nameof(salvoSize)].guiActiveEditor = setSalvoSize;
                if (isMultiLauncher)
                {
                    if (!string.IsNullOrEmpty(subMunitionName))
                    {
                        Fields[nameof(loadedMissileName)].guiActive = true;
                        Fields[nameof(loadedMissileName)].guiActiveEditor = true;
                        missileLauncher.missileName = subMunitionName;
                    }
                    if (!permitJettison) missileLauncher.Events[nameof(missileLauncher.Jettison)].guiActive = false;
                    if (OverrideDropSettings)
                    {
                        missileLauncher.Fields[nameof(missileLauncher.dropTime)].guiActive = false;
                        missileLauncher.Fields[nameof(missileLauncher.dropTime)].guiActiveEditor = false;
                        missileLauncher.dropTime = 0;
                        missileLauncher.Fields[nameof(missileLauncher.decoupleSpeed)].guiActive = false;
                        missileLauncher.Fields[nameof(missileLauncher.decoupleSpeed)].guiActiveEditor = false;
                        missileLauncher.decoupleSpeed = 10;
                        missileLauncher.Fields[nameof(missileLauncher.decoupleForward)].guiActive = false;
                        missileLauncher.Fields[nameof(missileLauncher.decoupleForward)].guiActiveEditor = false;
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
                                            Fields[nameof(clusterMissileTriggerDist)].guiActive = true;
                                            Fields[nameof(clusterMissileTriggerDist)].guiActiveEditor = true;
                                        }
                                }
                                else Debug.LogError("[BDArmory.MultiMissileLauncher]: submunition MissileLauncher module null! Check subMunitionName is correct");
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
                        missileLauncher.DetonationDistance = missileLauncher.GetInitialDetonationDistance(bRadius);
                    }
                }
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
            if (missileSpawner.maxAmmo > 1)
            {
                UI_FloatRange Ammo = (UI_FloatRange)missileSpawner.Fields[nameof(missileSpawner.railAmmo)].uiControlEditor;
                Ammo.onFieldChanged = updateMaxAmmo;
            }

            if (string.IsNullOrEmpty(scaleTransformName))
            {
                Fields[nameof(Scale)].guiActiveEditor = false;
            }
            else
            {
                ScaleTransform = part.FindModelTransform(scaleTransformName);
                if (ScaleTransform != null)
                {
                    ScaleTransformOrigScale = part.partInfo.partPrefab.FindModelTransform(scaleTransformName).localScale; //baseConfig ? ScaleTransform.localScale * (baseConfig.Scale / Scale) : ScaleTransform.localScale;
                    UI_FloatRange AWidth = (UI_FloatRange)Fields[nameof(Scale)].uiControlEditor;
                    AWidth.maxValue = scaleMax;
                    if (Scale > scaleMax) Scale = scaleMax;
                    AWidth.onFieldChanged = updateScale;
                }
            }
            if (string.IsNullOrEmpty(lengthTransformName))
            {
                Fields[nameof(Length)].guiActiveEditor = false;
            }
            else
            {
                LengthTransform = part.FindModelTransform(lengthTransformName);
                if (LengthTransform != null)
                {
                    LengthTransformOrigScale = part.partInfo.partPrefab.FindModelTransform(scaleTransformName).localScale;
                    UI_FloatRange ALength = (UI_FloatRange)Fields[nameof(Length)].uiControlEditor;
                    ALength.maxValue = scaleMax;
                    if (Length > scaleMax) Length = scaleMax;
                    ALength.onFieldChanged = updateLength;
                }
            }
            if (adjustMissileVOffset || !string.IsNullOrEmpty(lengthTransformName))
            {
                UI_FloatRange AOffset = (UI_FloatRange)Fields[nameof(attachOffset)].uiControlEditor;
                AOffset.maxValue = offsetMax;
                AOffset.minValue = -offsetMax;
                AOffset.onFieldChanged = updateOffset;
            }
            else Fields[nameof(attachOffset)].guiActiveEditor = false;

            UpdateLengthAndScale(Scale, Length, attachOffset);
            if (HighLogic.LoadedSceneIsFlight)
            {
                DragCube DragCube = DragCubeSystem.Instance.RenderProceduralDragCube(part);
                part.DragCubes.Procedural = true;
                part.DragCubes.ClearCubes();
                part.DragCubes.Cubes.Add(DragCube);
                part.DragCubes.ResetCubeWeights();
                part.DragCubes.ForceUpdate(true, true, false);
                part.DragCubes.SetDragWeights();
            }
        }

        public void updateMaxOffBoresight(float maxOffBoresight)
        {
            // Need to update the maxOffBoresight value with the value from ModuleMissileRearm
            // except for in the case of this being a cluster missile or we're overriding the reference
            // transform, as is the case with VLS cells
            if (isClusterMissile || overrideReferenceTransform)
                return;

            // Have to wait until missileLauncher is loaded before doing this
            StartCoroutine(UpdateMaxOffBoresightRoutine(maxOffBoresight));
        }

        public IEnumerator UpdateMaxOffBoresightRoutine(float maxOffBoresight)
        {
            yield return new WaitUntil(() => missileLauncher != null);

            missileLauncher.maxOffBoresight = maxOffBoresight;
        }

        public void updateScale(BaseField field, object obj)
        {
            ScaleTransform.localScale = ScaleTransformOrigScale * Scale;
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
            PopulateMissileDummies();
        }
        public void updateLength(BaseField field, object obj)
        {
            LengthTransform.localScale = new Vector3(LengthTransformOrigScale.x, LengthTransformOrigScale.y, LengthTransformOrigScale.z * (ScaleTransform ? (Length / Scale) : Length));
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
            float delta = attachOffset - (float)obj;
            for (int i = 0; i < launchTransforms.Length; i++)
            {
                launchTransforms[i].localPosition = new Vector3(launchTransforms[i].localPosition.x, launchTransforms[i].localPosition.y, launchTransforms[i].localPosition.z + delta * Mathf.Max(Scale, Length));
            }
            PopulateMissileDummies(true);
            using List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator();
            while (sym.MoveNext())
            {
                if (sym.Current == null) continue;
                var mml = sym.Current.FindModuleImplementing<MultiMissileLauncher>();
                if (mml == null) continue;
                mml.attachOffset = attachOffset;
                mml.UpdateLengthAndScale(Scale, Length, attachOffset);
            }
        }
        public void updateMaxAmmo(BaseField field, object obj)
        {
            PopulateMissileDummies(true);
            using List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator();
            while (sym.MoveNext())
            {
                if (sym.Current == null) continue;
                var mml = sym.Current.FindModuleImplementing<MultiMissileLauncher>();
                if (mml == null) continue;
                mml.PopulateMissileDummies(true);
            }
        }
        public void UpdateLengthAndScale(float scale, float length, float offset)
        {
            if (ScaleTransform != null)
                ScaleTransform.localScale = ScaleTransformOrigScale * scale;
            if (LengthTransform != null)
                LengthTransform.localScale = new Vector3(LengthTransformOrigScale.x, LengthTransformOrigScale.y, (LengthTransformOrigScale.z / scale) * length);
            if (!string.IsNullOrEmpty(lengthTransformName))
            {
                for (int i = 0; i < launchTransforms.Length; i++)
                {
                    launchTransforms[i].localPosition = new Vector3(launchTransforms[i].localPosition.x, launchTransforms[i].localPosition.y, attachOffset * Mathf.Max(Scale, Length));
                }
            }
            PopulateMissileDummies(true);
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
                        if (stackNode.Current.id.Contains(RailNode))
                        {
                            if (stackNode.Current.attachedPart is Part missile)
                            {
                                if (missile == null) return;

                                if (missile.FindModuleImplementing<MissileLauncher>())
                                {
                                    subMunitionName = missile.name;

                                    UrlDir.UrlConfig partConfigTemp = (UrlDir.UrlConfig)GameDatabase.Instance.root.GetConfig(missile.partInfo.partUrl);
                                    if (partConfigTemp == null)
                                    {
                                        partConfigTemp = missile.partInfo.partUrlConfig;
                                        Debug.LogWarning($"[BDArmory.MultiMissileLauncher]: GetConfig from partURL {missile.partInfo.partUrl} failed! Using missilePart.partPrefab.partInfo.partUrlConfig. Potentially multiple parts in one file!");
                                    }

                                    subMunitionPath = GetMeshurl(partConfigTemp);
                                    if (adjustMissileVOffset)
                                    {
                                        var missileCOL = missile.GetComponentInChildren<Collider>();
                                        if (missileCOL) attachedMissileDiameter = Mathf.Min(missileCOL.bounds.size.x, missileCOL.bounds.size.y, missileCOL.bounds.size.z);
                                    }
                                    PopulateMissileDummies(true);
                                    MissileLauncher MLConfig = missile.FindModuleImplementing<MissileLauncher>();
                                    LoadoutModified = true;
                                    Fields[nameof(loadedMissileName)].guiActive = true;
                                    Fields[nameof(loadedMissileName)].guiActiveEditor = true;
                                    loadedMissileName = MLConfig.GetShortName();
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
                                            mml.loadedMissileName = MLConfig.GetShortName();
                                            if (mml.missileSpawner)
                                            {
                                                mml.missileSpawner.MissileName = subMunitionName;
                                                mml.missileSpawner.UpdateMissileValues();
                                            }
                                            mml.UpdateFields(MLConfig, true);
                                            mml.tntMass = tntMass;
                                            mml.missileMass = missileMass;
                                            mml.missileLauncher.blastRadius = BlastPhysicsUtils.CalculateBlastRange(tntMass);
                                        }
                                }
                            }
                        }
                    }
            }
        }

        public string GetMeshurl(UrlDir.UrlConfig cfgdir)
        {
            //check if part uses a MODEL node to grab an (external?) .mu file
            string url;
            //float invRescaleFactor = 1f / part.rescaleFactor;
            //dummyScale = Vector3.one; //new Vector3(invRescaleFactor, invRescaleFactor, invRescaleFactor);
            if (cfgdir == null)
            {
                Debug.Log($"[BDArmory.MultiMissileLauncher] cfgdir is null!");
                dummyScale = Vector3.one;
                return "";
            }

            if (cfgdir.config.HasValue("rescaleFactor"))
            {
                float scale = float.Parse(cfgdir.config.GetValue("rescaleFactor"));
                dummyScale = new Vector3(scale, scale, scale);
            }
            else
            {
                dummyScale = new Vector3(1.25f, 1.25f, 1.25f);
            }

            if (cfgdir.config.HasNode("MODEL"))
            {
                var MODEL = cfgdir.config.GetNode("MODEL");
                url = MODEL.GetValue("model") ?? "";
                if (MODEL.HasValue("scale"))
                {
                    string[] strings = MODEL.GetValue("scale").Split(","[0]);
                    dummyScale.x *= float.Parse(strings[0]);
                    dummyScale.y *= float.Parse(strings[1]);
                    dummyScale.z *= float.Parse(strings[2]);
                }
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MultiMissileLauncher]: Found model URL of {url} and scale {dummyScale} via ConfigNode");
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
            url = string.Format("{0}/{1}", cfgdir.parent.parent.url, mesh);
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MultiMissileLauncher]: Found model URL of {url} and scale {dummyScale} via mesh");
            return url;
        }

        void UpdateFields(MissileLauncher MLConfig, bool configurableSettings)
        {
            missileLauncher.guidanceActive = MLConfig.guidanceActive;
            missileLauncher.homingType = MLConfig.homingType; //these are all non-persistant, and need to be re-grabbed at launch
            missileLauncher.targetingType = MLConfig.targetingType;
            missileLauncher.missileType = MLConfig.missileType.ToLower();
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
            missileLauncher.antiradTargetTypes = MLConfig.antiradTargetTypes;
            missileLauncher.steerMult = MLConfig.steerMult;
            missileLauncher.thrust = MLConfig.thrust;
            missileLauncher.cruiseThrust = MLConfig.cruiseThrust;
            missileLauncher.boostTime = MLConfig.boostTime;
            missileLauncher.cruiseTime = MLConfig.cruiseTime;
            missileLauncher.cruiseDelay = MLConfig.cruiseDelay;
            missileLauncher.cruiseRangeTrigger = MLConfig.cruiseRangeTrigger;
            missileLauncher.maxAoA = MLConfig.maxAoA;
            missileLauncher.optimumAirspeed = MLConfig.optimumAirspeed;
            missileLauncher.maxTurnRateDPS = MLConfig.maxTurnRateDPS;
            missileLauncher.proxyDetonate = MLConfig.proxyDetonate;
            missileLauncher.terminalGuidanceShouldActivate = MLConfig.terminalGuidanceShouldActivate;
            missileLauncher.terminalGuidanceType = MLConfig.terminalGuidanceType;
            missileLauncher.torpedo = MLConfig.torpedo;
            missileLauncher.pronavGain = MLConfig.pronavGain;
            missileLauncher.kappaAngle = MLConfig.kappaAngle;
            missileLauncher.gLimit = MLConfig.gLimit;
            missileLauncher.gMargin = MLConfig.gMargin;
            missileLauncher.terminalHoming = MLConfig.terminalHoming;
            missileLauncher.terminalHomingType = MLConfig.terminalHomingType;
            missileLauncher.liftArea = MLConfig.liftArea;
            missileLauncher.dragArea = MLConfig.dragArea;
            missileLauncher.simpleDrag = MLConfig.simpleDrag;
            missileLauncher.useSimpleDrag = MLConfig.useSimpleDrag;
            missileLauncher.simpleCoD = MLConfig.simpleCoD;
            missileLauncher.maxTorque = MLConfig.maxTorque;
            missileLauncher.simpleStableTorque = MLConfig.simpleStableTorque;
            missileLauncher.deployedDrag = MLConfig.deployedDrag;
            missileLauncher.rndAngVel = MLConfig.rndAngVel;
            missileLauncher.maneuvergLimit = MLConfig.maneuvergLimit;
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
            missileLauncher.maxCruiseSpeed = MLConfig.CruiseSpeed;
            missileLauncher.canCruisePopup = MLConfig.CruisePopup;
            missileLauncher.canDetMinDist = MLConfig.DetonateAtMinimumDistance;
            missileLauncher.defaultDetDist = MLConfig.DetonationDistance;
            missileLauncher.adjustableProxyFuze = MLConfig.adjustableProxyFuze;
            if (!missileLauncher.adjustableProxyFuze)
            {
                missileLauncher.DetonationDistance = MLConfig.DetonationDistance;
            }
            if (!overrideReferenceTransform) missileLauncher.maxOffBoresight = MLConfig.maxOffBoresight; //don't overwrite e.g. VLS launcher boresights so they can launch, but still have normal boresight on fired missiles

            if (configurableSettings)
            {
                missileLauncher.maxStaticLaunchRange = MLConfig.maxStaticLaunchRange;
                missileLauncher.minStaticLaunchRange = MLConfig.minStaticLaunchRange;
                missileLauncher.engageRangeMin = MLConfig.minStaticLaunchRange;
                missileLauncher.engageRangeMax = MLConfig.maxStaticLaunchRange;
                missileLauncher.DetonateAtMinimumDistance = missileLauncher.canDetMinDist && MLConfig.DetonateAtMinimumDistance;
                missileLauncher.detonationTime = MLConfig.detonationTime;
                if (missileLauncher.adjustableProxyFuze)
                {
                    missileLauncher.DetonationDistance = MLConfig.DetonationDistance;
                }
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
                missileLauncher.blastRadius = MLConfig.blastRadius;
            }
            missileLauncher.GetBlastRadius();
            missileLauncher.ParseLiftDragSteerTorque();
            missileLauncher.ParseManeuvergLim();
            // Because we already set the values from the true base config, we do **not** check the base config in SetFields
            missileLauncher.ParseMissileType();
            missileLauncher.SetFields(false);
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
            UI_FloatRange salvo = (UI_FloatRange)Fields[nameof(salvoSize)].uiControlEditor;
            salvo.maxValue = launchTransforms.Length;
        }
        public void PopulateMissileDummies(bool refresh = false)
        {
            bool populateDummies = true;
            if (refresh && displayOrdinance)
            {
                populateDummies = SetupMissileDummyPool(subMunitionPath);
                foreach (var existingDummy in part.GetComponentsInChildren<MissileDummy>())
                {
                    existingDummy.Deactivate(); //if changing out missiles loaded into a VLS or similar, reset missile dummies
                }
            }
            int loadedOrdnance = (BDArmorySettings.INFINITE_ORDINANCE ? launchTransforms.Length : missileSpawner != null ? Math.Min((int)missileSpawner.railAmmo, launchTransforms.Length) : launchTransforms.Length);
            for (int i = 0; i < loadedOrdnance; i++)
            {
                if (!refresh)
                {
                    //if (missileSpawner.ammoCount > i || isClusterMissile)
                    //{
                    // No point in checking since this already creates a new Vector3, may as well just set it...
                    //if (launchTransforms[i].localScale != new Vector3(1 / Scale, 1 / Scale, 1 / (Scale * Length)))
                    launchTransforms[i].localScale = new Vector3(1f / Scale, 1f / Scale, 1f / (LengthTransform != null ? Length : Scale));
                    //}
                    tubesFired = 0;
                }
                else
                {
                    if (!displayOrdinance) return;
                    if (!populateDummies)
                    {
                        if (HighLogic.LoadedSceneIsEditor && !part.ConnectedToShip()) return; // Don't spam logs when the part isn't attached to the ship yet.
                        Debug.LogError($"[BDArmory.MultiMissileLauncher]: Reminder! Model {subMunitionPath} not found. Cannot populate missile dummies!");
                        return;
                    }
                    GameObject dummy = mslDummyPool[subMunitionPath].GetPooledObject();
                    MissileDummy dummyThis = dummy.GetComponentInChildren<MissileDummy>();

                    launchTransforms[i].localScale = new Vector3(1f / Scale, 1f / Scale, 1f / (LengthTransform != null ? Length : Scale));

                    dummy.transform.parent = null;
                    dummy.transform.localScale = dummyScale;
                    dummyThis.AttachAt(part, launchTransforms[i]);
                    if (adjustMissileVOffset && attachedMissileDiameter > 0) dummyThis.transform.localPosition = new Vector3(attachedMissileDiameter / 2, 0, 0);
                    var mslAnim = dummy.GetComponentInChildren<Animation>();
                    if (mslAnim != null) mslAnim.enabled = false;
                    if (!displayOrdinanceHasColliders)
                    {
                        var childColliders = dummy.GetComponentsInChildren<Collider>(includeInactive: false);
                        foreach (var col in childColliders)
                            col.enabled = false;
                    }
                }
            }
        }
        public void fireMissile(bool killWhenDone = false)
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (isLaunchedClusterMissile) salvoSize = launchTransforms.Length;
            if (missileSalvo == null)
            {
                FiredByWM = missileLauncher.FiredByWM;
                missileSalvo = StartCoroutine(salvoFire(killWhenDone));
                if (useSymCounterpart && !killWhenDone)
                {
                    MissileFire.TargetData targetData = missileLauncher.TargetAcquired ? new MissileFire.TargetData(missileLauncher.targetGPSCoords, missileLauncher.TimeOfLastINS, missileLauncher.INStimetogo) : null;
                    using (List<Part>.Enumerator pSym = part.symmetryCounterparts.GetEnumerator())
                        while (pSym.MoveNext())
                        {
                            if (pSym.Current == null) continue;
                            if (pSym.Current != part && pSym.Current.vessel == vessel)
                            {
                                var ml = pSym.Current.FindModuleImplementing<MissileBase>();
                                if (ml == null) continue;
                                if (FiredByWM != null) FiredByWM.SendTargetDataToMissile(ml, missileLauncher.targetVessel != null ? missileLauncher.targetVessel.Vessel : null, false, targetData, true);
                                MissileLauncher launcher = ml as MissileLauncher;
                                if (launcher != null)
                                {
                                    if (launcher.HasFired || launcher.launched) continue;
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
            bool removeFromQueue = !isLaunchedClusterMissile;
            List<TargetInfo> firedTargets = [];
            //missileSpawner.MissileName = subMunitionName;
            salvoLaunching = true;

            if (FiredByWM != null)
            {
                if (FiredByWM.targetsAssigned.Count > 0) targetsAssigned.Clear();
                if (FiredByWM.multiMissileTgtNum >= 2 || (missileLauncher.engageMissile && FiredByWM.PDMslTgts.Count > 0))
                {
                    if (missileLauncher.engageMissile && FiredByWM.PDMslTgts.Count > 0) targetsAssigned.AddRange(FiredByWM.PDMslTgts);
                    else if (FiredByWM.targetsAssigned.Count > 0) targetsAssigned.AddRange(FiredByWM.targetsAssigned);
                }
                //Debug.Log($"[BDArmory.MultiMissileLauncherDebug]: Num of targets: {targetsAssigned.Count - 1}");
                if (targetsAssigned.Count < 1)
                    if (FiredByWM.currentTarget != null) targetsAssigned.Add(FiredByWM.currentTarget);

                Team = FiredByWM.Team;
            }
            else
            {
                if (BDArmorySettings.DEBUG_MISSILES)
                    Debug.Log($"[BDArmory.MultiMissileLauncher]: {vessel.vesselName}'s multilauncher could not acquire the firing vessel's WM and as a result targets are not assigned correctly! Team also set to Neutral!");
                Team = BDTeam.Get("Neutral");
            }

            //else Debug.Log($"[BDArmory.MultiMissileLauncherDebug]: weaponmanager null!"); 
            yield return SetDeployed(true);
            if (missileSpawner == null)
            {
                missileSalvo = null;
                salvoLaunching = false;
                yield break;
            } // Died while waiting.
            for (int m = tubesFired; m < launchTransforms.Length; m++)
            {
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MultiMissileLauncher]: starting ripple launch on tube {m}, ripple delay: {timeGap:F3}");
                yield return new WaitForSecondsFixed(timeGap);
                if (missileSpawner == null)
                {
                    missileSalvo = null;
                    salvoLaunching = false;
                    yield break;
                } // Died while waiting.
                if (launchesThisSalvo >= (int)salvoSize) //catch if launcher is trying to launch more missiles than it has
                {
                    //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MultiMissileLauncher]: oops! firing more missiles than tubes or ammo");
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
                if (!missileSpawner.SpawnMissile(launchTransforms[m], offset * Length, adjustMissileVOffset ? attachedMissileDiameter / 2 : 0, !isLaunchedClusterMissile))
                {
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.LogWarning($"[BDArmory.MissileLauncher]: Failed to spawn a missile in {missileSpawner} on {vessel.vesselName}");
                    continue;
                }
                if (!ignoreLauncherColliders)
                {
                    var childColliders = missileSpawner.SpawnedMissile.GetComponentsInChildren<Collider>(includeInactive: false);
                    foreach (var col in childColliders)
                        col.enabled = true;
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
                {
                    ml.useSimpleDragTemp = true;
                    ml.clearanceLength = Mathf.Max(missileSpawner.SpawnedMissile.collider.bounds.size.x, missileSpawner.SpawnedMissile.collider.bounds.size.y, missileSpawner.SpawnedMissile.collider.bounds.size.z);
                }
                ml.Team = Team;
                ml.SourceVessel = missileLauncher.SourceVessel;
                if (string.IsNullOrEmpty(ml.GetShortName()))
                {
                    ml.shortName = missileLauncher.GetShortName() + " Missile";
                }
                // Already applied in MissileLauncher making this redundant
                //if (BDArmorySettings.DEBUG_MISSILES) ml.shortName = $"{ml.SourceVessel.GetName()}'s {missileLauncher.GetShortName()} Missile";
                ml.vessel.vesselName = ml.GetShortName();
                ml.TimeFired = Time.time;
                if (turret) ml.missileTurret = turret;
                if (!isClusterMissile)
                {
                    if (missileLauncher.adjustableProxyFuze)
                    {
                        ml.DetonationDistance = missileLauncher.DetonationDistance;
                    }
                    ml.decoupleForward = missileLauncher.decoupleForward;
                    ml.dropTime = missileLauncher.dropTime;
                    ml.decoupleSpeed = missileLauncher.decoupleSpeed;
                    ml.rndAngVel = missileLauncher.rndAngVel;
                }
                ml.DetonateAtMinimumDistance = missileLauncher.DetonateAtMinimumDistance && missileLauncher.canDetMinDist;
                ml.detonationTime = missileLauncher.detonationTime;
                ml.engageAir = missileLauncher.engageAir;
                ml.engageGround = missileLauncher.engageGround;
                ml.engageMissile = missileLauncher.engageMissile;
                ml.engageSLW = missileLauncher.engageSLW;

                if (missileLauncher.GuidanceMode == GuidanceModes.AGMBallistic)
                {
                    ml.BallisticOverShootFactor = missileLauncher.BallisticOverShootFactor;
                    ml.BallisticAngle = missileLauncher.BallisticAngle;
                }
                if (missileLauncher.GuidanceMode == GuidanceModes.Cruise)
                {
                    ml.CruiseAltitude = missileLauncher.CruiseAltitude;
                    ml.CruiseSpeed = Mathf.Min(missileLauncher.CruiseSpeed, missileLauncher.maxCruiseSpeed);
                    ml.CruisePredictionTime = missileLauncher.CruisePredictionTime;
                    ml.CruisePopup = missileLauncher.CruisePopup && missileLauncher.canCruisePopup;
                }

                if (BDArmorySettings.DEBUG_MISSILES)
                {
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
                        ml.loftState = LoftStates.PreLaunch;
                        ml.TimeToImpact = float.PositiveInfinity;
                    }
                    /*if (missileLauncher.GuidanceMode == GuidanceModes.AAMHybrid)
                    {
                        ml.pronavGain = missileLauncher.pronavGain;
                        ml.terminalHomingRange = missileLauncher.terminalHomingRange;
                        ml.homingModeTerminal = missileLauncher.homingModeTerminal;
                    }*/

                    if (missileLauncher.GuidanceMode == GuidanceModes.Kappa)
                    {
                        ml.kappaAngle = missileLauncher.kappaAngle;
                        ml.LoftAngle = missileLauncher.LoftAngle;
                        ml.LoftMaxAltitude = missileLauncher.LoftMaxAltitude;
                        ml.LoftRangeOverride = missileLauncher.LoftRangeOverride;
                        ml.LoftTermAngle = missileLauncher.LoftTermAngle;
                        ml.loftState = LoftStates.PreLaunch;
                    }

                    if (missileLauncher.terminalHoming)
                    {
                        ml.terminalHomingRange = missileLauncher.terminalHomingRange;
                    }
                }

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
                    if (BDArmorySettings.DEBUG_MISSILES)
                    {
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
                            ml.loftState = LoftStates.PreLaunch;
                            ml.TimeToImpact = float.PositiveInfinity;
                        }

                        if (missileLauncher.homingModeTerminal == GuidanceModes.Kappa)
                        {
                            ml.kappaAngle = missileLauncher.kappaAngle;
                            ml.LoftAngle = missileLauncher.LoftAngle;
                            ml.LoftMaxAltitude = missileLauncher.LoftMaxAltitude;
                            ml.LoftRangeOverride = missileLauncher.LoftRangeOverride;
                            ml.LoftTermAngle = missileLauncher.LoftTermAngle;
                            ml.loftState = LoftStates.PreLaunch;
                        }
                    }
                }

                //ml.decoupleSpeed = 5;
                if (missileLauncher.GuidanceMode == GuidanceModes.AGM)
                    ml.maxAltitude = missileLauncher.maxAltitude;
                ml.terminalGuidanceShouldActivate = missileLauncher.terminalGuidanceShouldActivate;
                //if (isClusterMissile) ml.multiLauncher.overrideReferenceTransform = true;
                if (FiredByWM != null)
                {
                    if (ml.TargetingMode == TargetingModes.Heat || ml.TargetingMode == TargetingModes.Radar || ml.TargetingMode == TargetingModes.Gps || ml.TargetingMode == TargetingModes.Inertial)
                    {
                        //Debug.Log($"[BDArmory.MultiMissileLauncherDebug]: Beginning target distribution; Num of targets: {targetsAssigned.Count - 1}; wpm targets: {wpm.targetsAssigned.Count}");
                        if (targetsAssigned.Count > 0 && salvoSize > 1)
                        {
                            if (TargetID <= Mathf.Min(targetsAssigned.Count - 1, FiredByWM.multiMissileTgtNum))
                            {
                                for (int t = TargetID; t < Mathf.Min(targetsAssigned.Count - 1, FiredByWM.multiMissileTgtNum); t++) //MML targeting independant of MissileFire target assignment,
                                {// and each MMl will be independantly working off the same targets list, iterating over the same first couple targets
                                    if (FiredByWM.missilesAway.ContainsKey(targetsAssigned[t]))
                                    {
                                        //Debug.Log($"[MML Targeting Debug] target {t} {targetsAssigned[t].Vessel.GetName()} already has {wpm.missilesAway[targetsAssigned[t]]}/{wpm.maxMissilesOnTarget} fired on it...");
                                        if (FiredByWM.missilesAway[targetsAssigned[t]][0] < FiredByWM.maxMissilesOnTarget)
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
                            if (TargetID > Mathf.Min(targetsAssigned.Count - 1, FiredByWM.multiMissileTgtNum))
                            {
                                TargetID = 0; //if more missiles than targets, loop target list
                                if (salvoSize > 1)
                                    missileRegistry = false;  //this isn't ignoring subsequent missiles in the salvo for some reason?
                                                              //Debug.Log($"[MML Targeting Debug] Reached end of target list, cycling");
                            }
                            if (targetsAssigned.Count > 0 && targetsAssigned[TargetID] != null && targetsAssigned[TargetID].Vessel != null && (!ml.hasIFF || !Team.IsFriendly(targetsAssigned[TargetID].Team)))
                            {
                                if ((VectorUtils.Angle(targetsAssigned[TargetID].position - missileLauncher.MissileReferenceTransform.position, missileLauncher.GetForwardTransform()) < missileLauncher.maxOffBoresight) //is the target more-or-less in front of the missile(launcher)?
                                    && ((ml.engageAir && targetsAssigned[TargetID].isFlying) ||
                                    (ml.engageGround && targetsAssigned[TargetID].isLandedOrSurfaceSplashed) ||
                                    (ml.engageSLW && targetsAssigned[TargetID].isUnderwater) ||
                                    (ml.engageMissile && targetsAssigned[TargetID].isMissile))) //check engagement envelope
                                {
                                    if (ml.TargetingMode == TargetingModes.Heat) //need to input a heattarget, else this will just return MissileFire.CurrentTarget
                                    {
                                        Vector3 adjustedPos = missileLauncher.MissileReferenceTransform.position + (5f * missileLauncher.GetForwardTransform());
                                        Vector3 direction = (targetsAssigned[TargetID].position * targetsAssigned[TargetID].velocity.magnitude) - adjustedPos;
                                        // Uncaged lock for if we're a launched cluster missile and we're launching submunitions or we're a multi-launcher and the missile is has such capabilities 
                                        ml.heatTarget = BDATargetManager.GetHeatTarget(ml.SourceVessel, ml.vessel, new Ray(adjustedPos, direction), TargetSignatureData.noTarget, ml.lockedSensorFOV * 0.5f, ml.heatThreshold, ml.frontAspectHeatModifier, ml.targetCoM, ml.uncagedLock || isClusterMissile, ml.lockedSensorFOVBias, ml.lockedSensorVelocityBias, ml.lockedSensorVelocityMagnitudeBias, ml.lockedSensorMinAngularVelocity, FiredByWM, targetsAssigned[TargetID], IFF: ml.hasIFF);
                                        ml.TargetAcquired = ml.heatTarget.exists;
                                    }
                                    if (ml.TargetingMode == TargetingModes.Radar)
                                    {
                                        AssignRadarTarget(ml, targetsAssigned[TargetID].Vessel);
                                    }
                                    if (ml.TargetingMode == TargetingModes.Gps)
                                    {
                                        //ml.targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(targetsAssigned[TargetID].Vessel.CoM, vessel.mainBody);
                                        if (targetsAssigned.Count == 1 && FiredByWM.multiMissileTgtNum > 1 && salvoSize > 1 && !FiredByWM.targetCoM)
                                        {
                                            int targetNum = launchesThisSalvo - 1;
                                            if (targetNum >= FiredByWM.targetParts.Count) targetNum -= FiredByWM.targetParts.Count * Mathf.FloorToInt((targetNum / FiredByWM.targetParts.Count));
                                            ml.targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(FiredByWM.targetParts[targetNum].transform.position, vessel.mainBody);
                                        }
                                        else ml.targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(targetsAssigned[TargetID].Vessel.CoM, vessel.mainBody);
                                        ml.TargetAcquired = true;
                                    }
                                    if (ml.TargetingMode == TargetingModes.Inertial)
                                    {
                                        AssignInertialTarget(ml, targetsAssigned[TargetID].Vessel);
                                    }
                                    ml.targetVessel = targetsAssigned[TargetID];
                                    //ml.TargetAcquired = true;
                                    firedTargets.Add(targetsAssigned[TargetID]);
                                    if (BDArmorySettings.DEBUG_MISSILES)
                                        Debug.Log($"[BDArmory.MultiMissileLauncher]: Assigning target {TargetID}: {targetsAssigned[TargetID].Vessel.GetName()}; total possible targets {targetsAssigned.Count - 1}");
                                }
                                else //else try remaining targets on the list. 
                                {
                                    for (int t = TargetID; t < targetsAssigned.Count - 1; t++)
                                    {
                                        if (targetsAssigned[t] == null) continue;
                                        if (ml.hasIFF && Team.IsFriendly(targetsAssigned[t].Team)) continue;
                                        if ((ml.engageAir && !targetsAssigned[t].isFlying) ||
                                            (ml.engageGround && !targetsAssigned[t].isLandedOrSurfaceSplashed) ||
                                            (ml.engageSLW && !targetsAssigned[t].isUnderwater) ||
                                            (ml.engageMissile && !targetsAssigned[t].isMissile)) continue; //check engagement envelope

                                        if (VectorUtils.Angle(targetsAssigned[t].position - missileLauncher.MissileReferenceTransform.position, missileLauncher.GetForwardTransform()) < missileLauncher.maxOffBoresight) //is the target more-or-less in front of the missile(launcher)?
                                        {
                                            if (ml.TargetingMode == TargetingModes.Heat)
                                            {
                                                Vector3 adjustedPos = missileLauncher.MissileReferenceTransform.position + (5f * missileLauncher.GetForwardTransform());
                                                Vector3 direction = (targetsAssigned[t].position * targetsAssigned[t].velocity.magnitude) - adjustedPos;
                                                // Uncaged lock for if we're a launched cluster missile and we're launching submunitions or we're a multi-launcher and the missile is has such capabilities 
                                                ml.heatTarget = BDATargetManager.GetHeatTarget(ml.SourceVessel, ml.vessel, new Ray(adjustedPos, direction), TargetSignatureData.noTarget, ml.lockedSensorFOV * 0.5f, ml.heatThreshold, ml.frontAspectHeatModifier, ml.uncagedLock || isClusterMissile, ml.targetCoM, ml.lockedSensorFOVBias, ml.lockedSensorVelocityBias, ml.lockedSensorVelocityMagnitudeBias, ml.lockedSensorMinAngularVelocity, FiredByWM, targetsAssigned[t], IFF: ml.hasIFF);
                                                ml.TargetAcquired = ml.heatTarget.exists;
                                            }
                                            if (ml.TargetingMode == TargetingModes.Radar)
                                            {
                                                AssignRadarTarget(ml, targetsAssigned[t].Vessel);
                                            }
                                            if (ml.TargetingMode == TargetingModes.Gps)
                                            {
                                                if (targetsAssigned.Count == 1 && FiredByWM.multiMissileTgtNum > 1 && salvoSize > 1 && !FiredByWM.targetCoM)
                                                {
                                                    int targetNum = launchesThisSalvo - 1;
                                                    if (targetNum >= FiredByWM.targetParts.Count) targetNum -= FiredByWM.targetParts.Count * Mathf.FloorToInt((targetNum / FiredByWM.targetParts.Count));
                                                    ml.targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(FiredByWM.targetParts[targetNum].transform.position, vessel.mainBody);
                                                }
                                                else ml.targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(targetsAssigned[t].Vessel.CoM, vessel.mainBody);
                                                ml.TargetAcquired = true;
                                            }
                                            if (ml.TargetingMode == TargetingModes.Inertial)
                                            {
                                                AssignInertialTarget(ml, targetsAssigned[t].Vessel);
                                            }
                                            ml.targetVessel = targetsAssigned[t];
                                            //ml.TargetAcquired = true;
                                            firedTargets.Add(targetsAssigned[t]);
                                            if (BDArmorySettings.DEBUG_MISSILES)
                                                Debug.Log($"[BDArmory.MultiMissileLauncher]: Assigning backup target (targetID {TargetID}) {targetsAssigned[t].Vessel.GetName()}");
                                            break;
                                        }
                                    }
                                    if (BDArmorySettings.DEBUG_MISSILES)
                                        Debug.Log($"[BDArmory.MultiMissileLauncher]: Couldn't assign valid target, trying from beginning of target list");
                                    if (ml.targetVessel == null) //check targets that were already assigned and passed. using the above iterator to prevent all targets outisde allowed FoV or engagement enveolpe from being assigned the firest possible target by checking later ones first
                                    {
                                        using (List<TargetInfo>.Enumerator item = targetsAssigned.GetEnumerator())
                                            while (item.MoveNext())
                                            {
                                                if (item.Current == null) continue;
                                                if (item.Current.Vessel == null) continue;
                                                if (ml.hasIFF && Team.IsFriendly(item.Current.Team)) continue;
                                                if ((ml.engageAir && !item.Current.isFlying) ||
                                                    (ml.engageGround && !item.Current.isLandedOrSurfaceSplashed) ||
                                                    (ml.engageSLW && !item.Current.isUnderwater) ||
                                                    (ml.engageMissile && !item.Current.isMissile)) continue; //check engagement envelope
                                                if (VectorUtils.Angle(item.Current.position - missileLauncher.MissileReferenceTransform.position, missileLauncher.GetForwardTransform()) < missileLauncher.maxOffBoresight) //is the target more-or-less in front of the missile(launcher)?
                                                {
                                                    if (ml.TargetingMode == TargetingModes.Heat)
                                                    {
                                                        Vector3 adjustedPos = missileLauncher.MissileReferenceTransform.position + (5f * missileLauncher.GetForwardTransform());
                                                        Vector3 direction = (item.Current.position * item.Current.velocity.magnitude) - adjustedPos;
                                                        // Uncaged lock for if we're a launched cluster missile and we're launching submunitions or we're a multi-launcher and the missile is has such capabilities 
                                                        ml.heatTarget = BDATargetManager.GetHeatTarget(ml.SourceVessel, ml.vessel, new Ray(adjustedPos, direction), TargetSignatureData.noTarget, ml.lockedSensorFOV * 0.5f, ml.heatThreshold, ml.frontAspectHeatModifier, ml.uncagedLock || isClusterMissile, ml.targetCoM, ml.lockedSensorFOVBias, ml.lockedSensorVelocityBias, ml.lockedSensorVelocityMagnitudeBias, ml.lockedSensorMinAngularVelocity, FiredByWM, item.Current, IFF: ml.hasIFF);
                                                        ml.TargetAcquired = ml.heatTarget.exists;
                                                    }
                                                    if (ml.TargetingMode == TargetingModes.Radar)
                                                    {
                                                        AssignRadarTarget(ml, item.Current.Vessel);
                                                    }
                                                    if (ml.TargetingMode == TargetingModes.Gps)
                                                    {
                                                        if (targetsAssigned.Count == 1 && FiredByWM.multiMissileTgtNum > 1 && salvoSize > 1 && !FiredByWM.targetCoM)
                                                        {
                                                            int targetNum = launchesThisSalvo - 1;
                                                            if (targetNum >= FiredByWM.targetParts.Count) targetNum -= FiredByWM.targetParts.Count * Mathf.FloorToInt((targetNum / FiredByWM.targetParts.Count));
                                                            ml.targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(FiredByWM.targetParts[targetNum].transform.position, vessel.mainBody);
                                                        }
                                                        else ml.targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(item.Current.Vessel.CoM, vessel.mainBody);
                                                        ml.TargetAcquired = true;
                                                    }
                                                    if (ml.TargetingMode == TargetingModes.Inertial)
                                                    {
                                                        AssignInertialTarget(ml, item.Current.Vessel);
                                                    }
                                                    ml.targetVessel = item.Current;
                                                    //ml.TargetAcquired = true;
                                                    firedTargets.Add(item.Current);
                                                    if (BDArmorySettings.DEBUG_MISSILES)
                                                        Debug.Log($"[BDArmory.MultiMissileLauncher]: original target out of sensor range; engaging {item.Current.Vessel.GetName()}");
                                                    break;
                                                }
                                            }
                                    }
                                }
                                TargetID++;
                                if (firedTargets.Count >= FiredByWM.multiMissileTgtNum)
                                {
                                    targetsAssigned.Clear();
                                    targetsAssigned.AddRange(firedTargets); //we've found targets up to our target allowance; cull list down to just those for distributing remaining missiles of the salvo between, if any.
                                }
                            }
                            else FiredByWM.SendTargetDataToMissile(ml, missileLauncher.targetVessel != null ? missileLauncher.targetVessel.Vessel : null, false);
                        }
                        else
                        {
                            //if (tubesFired > 1) missileRegistry = false;
                            Vector3 targetGEOPos = Vector3.zero;
                            Vector3 targetINScoords = Vector3.zero;
                            float TimeOfLastINS = -1f;
                            float INStimetogo = -1f;
                            switch (missileLauncher.TargetingMode)
                            {
                                case TargetingModes.Heat:
                                    {
                                        targetGEOPos = missileLauncher.heatTarget.geoPos;
                                        targetINScoords = VectorUtils.WorldPositionToGeoCoords(missileLauncher.heatTarget.predictedPosition, FlightGlobals.currentMainBody);
                                        TimeOfLastINS = missileLauncher.heatTarget.timeAcquired;
                                        INStimetogo = missileLauncher.heatTarget.age;
                                        break;
                                    }
                                case TargetingModes.Radar:
                                    {
                                        targetGEOPos = missileLauncher.radarTarget.geoPos;
                                        targetINScoords = VectorUtils.WorldPositionToGeoCoords(missileLauncher.heatTarget.predictedPosition, FlightGlobals.currentMainBody);
                                        TimeOfLastINS = missileLauncher.radarTarget.timeAcquired;
                                        INStimetogo = missileLauncher.radarTarget.age;
                                        break;
                                    }
                                case TargetingModes.Laser:
                                    {
                                        targetGEOPos = VectorUtils.WorldPositionToGeoCoords(missileLauncher.TargetPosition, FlightGlobals.currentMainBody);
                                        break;
                                    }
                                case TargetingModes.Inertial:
                                    {
                                        targetGEOPos = missileLauncher.targetGPSCoords;
                                        targetINScoords = missileLauncher.TargetINSCoords;
                                        TimeOfLastINS = missileLauncher.TimeOfLastINS;
                                        INStimetogo = missileLauncher.INStimetogo;
                                        break;
                                    }
                                case TargetingModes.None:
                                    break;
                                default:
                                    {
                                        targetGEOPos = missileLauncher.targetGPSCoords;
                                        break;
                                    }
                            }

                            ml.TargetPosition = missileLauncher.TargetPosition;

                            switch (ml.TargetingMode)
                            {
                                case TargetingModes.Laser:
                                    {
                                        ml.targetGPSCoords = targetGEOPos;
                                        ml.lockedCamera = missileLauncher.lockedCamera;
                                        ml.TargetAcquired = (ml.lockedCamera != null);
                                        break;
                                    }
                                case TargetingModes.Gps:
                                    {
                                        if (missileLauncher.lockedCamera != null)
                                        {
                                            targetGEOPos = VectorUtils.WorldPositionToGeoCoords(missileLauncher.lockedCamera.groundTargetPosition, FlightGlobals.currentMainBody);
                                        }
                                        else if (FiredByWM.vesselRadarData && missileLauncher.targetVessel != null)
                                        {
                                            if (FiredByWM.vesselRadarData.locked)
                                            {
                                                List<TargetSignatureData> possibleTargets = FiredByWM.vesselRadarData.GetLockedTargets();
                                                for (int i = 0; i < possibleTargets.Count; i++)
                                                {
                                                    if (possibleTargets[i].vessel == missileLauncher.targetVessel.Vessel)
                                                    {
                                                        targetGEOPos = possibleTargets[i].geoPos;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                        ml.targetGPSCoords = targetGEOPos;
                                        ml.lockedCamera = missileLauncher.lockedCamera;
                                        ml.TargetAcquired = (targetGEOPos != Vector3.zero);
                                        break;
                                    }
                                case TargetingModes.Heat:
                                    {
                                        ml.targetGPSCoords = targetGEOPos;
                                        ml.heatTarget = missileLauncher.heatTarget;
                                        ml.TargetAcquired = ml.heatTarget.exists;
                                        break;
                                    }
                                case TargetingModes.Radar:
                                    {
                                        ml.targetGPSCoords = targetGEOPos;
                                        ml.radarTarget = missileLauncher.radarTarget;
                                        ml.vrd = FiredByWM.vesselRadarData;
                                        ml.TargetAcquired = ml.radarTarget.exists;
                                        break;
                                    }
                                case TargetingModes.AntiRad:
                                    {
                                        ml.targetGPSCoords = targetGEOPos;
                                        break;
                                    }
                                case TargetingModes.Inertial:
                                    {
                                        if (FiredByWM.vesselRadarData && missileLauncher.targetVessel != null)
                                        {
                                            TargetSignatureData INSTarget = TargetSignatureData.noTarget;
                                            if (ml.GetWeaponClass() == WeaponClasses.SLW)
                                            {
                                                if (FiredByWM._sonarsEnabled)
                                                    INSTarget = FiredByWM.vesselRadarData.detectedRadarTarget(missileLauncher.targetVessel.Vessel, FiredByWM); //detected by radar scan?
                                            }
                                            else
                                            {
                                                if (FiredByWM._radarsEnabled)
                                                    INSTarget = FiredByWM.vesselRadarData.detectedRadarTarget(missileLauncher.targetVessel.Vessel, FiredByWM); //detected by radar scan?
                                                if (!INSTarget.exists && FiredByWM._irstsEnabled)
                                                    INSTarget = FiredByWM.vesselRadarData.activeIRTarget(missileLauncher.targetVessel.Vessel, FiredByWM, true); //how about IRST?
                                            }
                                            if (INSTarget.exists)
                                            {
                                                VectorUtils.WorldPositionToGeoCoords(MissileGuidance.GetAirToAirFireSolution(ml, missileLauncher.targetVessel.Vessel, out INStimetogo), missileLauncher.targetVessel.Vessel.mainBody);
                                                TimeOfLastINS = Time.time;
                                                targetINScoords = INSTarget.geoPos;
                                            }
                                        }
                                        ml.targetGPSCoords = targetGEOPos;
                                        if (TimeOfLastINS > 0)
                                        {
                                            ml.TargetINSCoords = targetINScoords;
                                            ml.TimeOfLastINS = TimeOfLastINS;
                                            ml.INStimetogo = INStimetogo;
                                        }
                                        ml.TargetAcquired = true;
                                        break;
                                    }
                            }
                            ml.targetVessel = missileLauncher.targetVessel;
                            //Debug.Log("[BDArmory.MultiMissileLauncher]: Data transfer complete.");
                        }
                    }
                    else
                    {
                        FiredByWM.SendTargetDataToMissile(ml, missileLauncher.targetVessel != null ? missileLauncher.targetVessel.Vessel : null, false);
                    }
                    ml.GpsUpdateMax = FiredByWM.GpsUpdateMax;
                }
                if (missileRegistry || removeFromQueue)
                {
                    BDATargetManager.FiredMissiles.Add(ml); //so multi-missile salvoes only count as a single missile fired by the WM for maxMissilesPerTarget

                    // Account for the datalink for the missileLauncher target
                    if (removeFromQueue)
                    {
                        if (missileLauncher.radarTarget.exists && missileLauncher.radarTarget.lockedByRadar && missileLauncher.radarTarget.lockedByRadar.vessel != missileLauncher.SourceVessel)
                        {
                            MissileFire datalinkwpm = missileLauncher.radarTarget.lockedByRadar.vessel.ActiveController().WM;
                            if (datalinkwpm)
                                datalinkwpm.UpdateQueuedLaunches(missileLauncher.targetVessel, missileLauncher, false, false);
                        }
                    }

                    if (FiredByWM)
                    {
                        // Remove the missileLauncher target from queue
                        if (removeFromQueue)
                        {
                            FiredByWM.UpdateQueuedLaunches(missileLauncher.targetVessel, missileLauncher, false);
                            removeFromQueue = false;
                        }
                        // Update missiles away with the real target
                        FiredByWM.UpdateMissilesAway(ml.targetVessel, ml);
                    }

                    // Update datalink missiles away with the real target
                    if (ml.radarTarget.exists && ml.radarTarget.lockedByRadar && ml.radarTarget.lockedByRadar.vessel != ml.SourceVessel)
                    {
                        MissileFire datalinkwpm = ml.radarTarget.lockedByRadar.vessel.ActiveController().WM;
                        if (datalinkwpm)
                            datalinkwpm.UpdateMissilesAway(ml.targetVessel, ml, false);
                    }

                    if (BDArmorySettings.DEBUG_MISSILES)
                        Debug.Log($"[BDArmory.MultiMissileLauncher]: Missile {ml.shortName} with target {(ml.targetVessel != null ? ml.targetVessel.Vessel.GetName() : "null vessel")} added to FiredMissiles.");
                }
                ml.FiredByWM = FiredByWM;
                ml.launched = true;
                if (ml.TargetPosition == Vector3.zero) ml.TargetPosition = missileLauncher.MissileReferenceTransform.position + (missileLauncher.MissileReferenceTransform.forward * 5000); //set initial target position so if no target update, missileBase will count a miss if it nears this point or is flying post-thrust
                ml.MissileLaunch();
                lastInSalvo = ml;
                if (FiredByWM != null) FiredByWM.heatTarget = TargetSignatureData.noTarget;
            }
            salvoLaunching = false; // Don't wait for the post-launch stuff to flag this.

            if (removeFromQueue)
            {
                if (missileLauncher.radarTarget.exists && missileLauncher.radarTarget.lockedByRadar && missileLauncher.radarTarget.lockedByRadar.vessel != missileLauncher.SourceVessel)
                {
                    MissileFire datalinkwpm = missileLauncher.radarTarget.lockedByRadar.vessel.ActiveController().WM;
                    if (datalinkwpm)
                        datalinkwpm.UpdateQueuedLaunches(missileLauncher.targetVessel, missileLauncher, false, false);
                }
            }

            if (FiredByWM != null)
            {
                if (removeFromQueue)
                {
                    Debug.LogWarning($"[BDArmory.MultiMissileLauncher]: {part.name} attempted to fire, all missiles failed to launch! Check your vessel design!");
                    FiredByWM.UpdateQueuedLaunches(missileLauncher.targetVessel, missileLauncher, false);
                    removeFromQueue = false;
                }

                using (List<TargetInfo>.Enumerator Tgt = targetsAssigned.GetEnumerator())
                    while (Tgt.MoveNext())
                    {
                        if (Tgt.Current == null) continue;
                        if (!firedTargets.Contains(Tgt.Current))
                            Tgt.Current.Disengage(FiredByWM);
                    }
            }
            if (deployState != null) yield return new WaitForSecondsFixed(deployTime); //wait for missile to clear bay
            yield return SetDeployed(false);
            if (missileLauncher == null)
            {
                missileSalvo = null;
                yield break;
            }
            if (tubesFired >= launchTransforms.Length) //add a timer for reloading a partially emptied MML if it hasn't been used for a while?
            {
                if (!isLaunchedClusterMissile && (BDArmorySettings.INFINITE_ORDINANCE || missileSpawner.ammoCount >= 0))
                    if (!(missileLauncher.reloadRoutine != null))
                    {
                        missileLauncher.reloadRoutine = StartCoroutine(missileLauncher.MissileReload());
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MultiMissileLauncher]: all submunitions fired. Reloading");
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
                        if (missileLauncher == null)
                        {
                            missileSalvo = null;
                            yield break;
                        }
                        missileLauncher.launched = false;
                        missileLauncher.heatTimer = -1;
                    }
                    else
                    {
                        missileLauncher.heatTimer = -1;
                        missileLauncher.launched = false;
                    }
                }
            }
            missileSalvo = null;
        }
        MissileLauncher lastInSalvo = null;
        /// <summary>
        /// Get the most recent MissileLauncher launched from a salvo.
        /// yield return WaitForSalvo() prior to calling this to get the final ML of the salvo.
        /// </summary>
        /// <returns></returns>
        public MissileLauncher GetLastInSalvo()
        {
            return lastInSalvo;
        }
        bool salvoLaunching = false; // Flag that a salvo is launching or preparing to launch.
        public IEnumerator WaitForSalvo()
        {
            var wait = new WaitForFixedUpdate();
            while (salvoLaunching) yield return wait;
        }

        void AssignInertialTarget(MissileLauncher ml, Vessel targetV)
        {
            TargetSignatureData tgtData = TargetSignatureData.noTarget;
            if (FiredByWM.vesselRadarData)
            {
                if (FiredByWM._radarsEnabled)
                {
                    tgtData = FiredByWM.vesselRadarData.detectedRadarTarget(targetV, FiredByWM);
                    ml.vrd = FiredByWM.vesselRadarData;
                }
                if (!tgtData.exists && FiredByWM._irstsEnabled)
                {
                    tgtData = FiredByWM.vesselRadarData.activeIRTarget(targetV, FiredByWM, true);
                }
            }
            if (tgtData.exists)
            {
                ml.targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(MissileGuidance.GetAirToAirFireSolution(ml, tgtData.position, tgtData.velocity), targetV.mainBody);
                ml.TargetINSCoords = VectorUtils.WorldPositionToGeoCoords(tgtData.position, vessel.mainBody);
                ml.TargetAcquired = true;
            }
            else
            {
                ml.targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(ml.MissileReferenceTransform.position + ml.MissileReferenceTransform.forward * 10000, vessel.mainBody);
                ml.TargetINSCoords = ml.targetGPSCoords;
                ml.TargetAcquired = (ml.GetWeaponClass() != WeaponClasses.Bomb);
            }
        }

        void AssignRadarTarget(MissileLauncher ml, Vessel targetV)
        {
            ml.vrd = FiredByWM.vesselRadarData;
            if (FiredByWM.vesselRadarData) FiredByWM.vesselRadarData.TryLockTarget(targetV);
            bool foundTarget = false;
            if (FiredByWM.vesselRadarData && FiredByWM.vesselRadarData.locked) //if we have existing radar locks, use thsoe
            {
                List<TargetSignatureData> possibleTargets = FiredByWM.vesselRadarData.GetLockedTargets();
                TargetSignatureData lockedTarget = TargetSignatureData.noTarget;
                for (int i = 0; i < possibleTargets.Count; i++)
                {
                    if (possibleTargets[i].vessel == targetV)
                    {
                        lockedTarget = possibleTargets[i]; //send correct targetlock if firing multiple SARH missiles
                        foundTarget = true;
                        if (BDArmorySettings.DEBUG_MISSILES)
                            Debug.Log($"[BDArmory.MultiMissileLauncher]: Found locked Radar target {targetV.GetName()}");
                        break;
                    }
                }
                ml.radarTarget = lockedTarget;
            }
            if (!foundTarget)
            {
                if (missileLauncher.MissileReferenceTransform.position.CloserToThan(targetV.CoM, ml.activeRadarRange))
                {
                    TargetSignatureData[] scannedTargets = new TargetSignatureData[(int)FiredByWM.multiMissileTgtNum];
                    int numLocked = RadarUtils.RadarUpdateMissileLock(new Ray(ml.transform.position, ml.GetForwardTransform()), ml.maxOffBoresight, ref scannedTargets, RadarUtils.ACTIVE_MISSILE_PING_PERSIST_TIME, ml, false);
                    TargetSignatureData lockedTarget = TargetSignatureData.noTarget;

                    for (int i = 0; i < scannedTargets.Length; i++)
                    {
                        if (i == numLocked) break;

                        if (!scannedTargets[i].exists) continue;

                        if (scannedTargets[i].vessel == targetV)
                        {
                            lockedTarget = scannedTargets[i];
                            if (BDArmorySettings.DEBUG_MISSILES)
                                Debug.Log($"[BDArmory.MultiMissileLauncher]: Found Radar target {targetV.GetName()}");
                            break;
                        }
                    }
                    ml.radarTarget = lockedTarget;
                    if (BDArmorySettings.DEBUG_MISSILES)
                    {
                        if (!ml.radarTarget.exists)
                            Debug.Log($"[BDArmory.MultiMissileLauncher]: unable to lock Radar target {targetV.GetName()}, skipping");
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
            ml.TargetAcquired = ml.radarTarget.exists;
            //Debug.Log($"[BDArmory.MultiMissileLauncher]: {targetV.GetName()}; assigned radar target {(ml.radarTarget.exists ? ml.radarTarget.vessel.GetName() : "null")}");
        }

        public bool SetupMissileDummyPool(string modelpath)
        {
            var key = modelpath;
            if (!mslDummyPool.ContainsKey(key) || mslDummyPool[key] == null)
            {
                var Template = GameDatabase.Instance.GetModel(modelpath);
                if (Template == null)
                {
                    if (HighLogic.LoadedSceneIsEditor && !part.ConnectedToShip()) return false; // Don't spam logs when the part isn't attached to the ship yet.
                    Debug.LogError("[BDArmory.MultiMissileLauncher]: model '" + modelpath + "' not found. Expect exceptions if trying to use this missile.");
                    return false;
                }
                Template.SetActive(false);
                Template.AddComponent<MissileDummy>();
                mslDummyPool[key] = ObjectPool.CreateObjectPool(Template, 10, true, true);
            }
            return true;
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
