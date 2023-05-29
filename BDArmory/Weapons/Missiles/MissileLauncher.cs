using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UniLinq;
using UnityEngine;

using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.FX;
using BDArmory.Guidances;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.WeaponMounts;

namespace BDArmory.Weapons.Missiles
{
    public class MissileLauncher : MissileBase, IPartMassModifier
    {
        public Coroutine reloadRoutine;
        Coroutine reloadableMissile;
        #region Variable Declarations

        [KSPField]
        public string homingType = "AAM";

        [KSPField]
        public float pronavGain = 3f;

        [KSPField]
        public string targetingType = "none";

        [KSPField]
        public string antiradTargetTypes = "0,5";
        public float[] antiradTargets;

        public MissileTurret missileTurret = null;
        public BDRotaryRail rotaryRail = null;
        public BDDeployableRail deployableRail = null;
        public MultiMissileLauncher multiLauncher = null;
        private BDStagingAreaGauge gauge;
        private float reloadTimer = 0;
        public float heatTimer = -1;
        private Vector3 origScale = Vector3.one;

        [KSPField]
        public string exhaustPrefabPath;

        [KSPField]
        public string boostExhaustPrefabPath;

        [KSPField]
        public string boostExhaustTransformName;

        #region Aero

        [KSPField]
        public bool aero = false;

        [KSPField]
        public float liftArea = 0.015f;

        [KSPField]
        public float steerMult = 0.5f;

        [KSPField]
        public float torqueRampUp = 30f;
        Vector3 aeroTorque = Vector3.zero;
        float controlAuthority;
        float finalMaxTorque;

        [KSPField]
        public float aeroSteerDamping = 0;

        #endregion Aero

        [KSPField]
        public float maxTorque = 90;

        [KSPField]
        public float thrust = 30;

        [KSPField]
        public float cruiseThrust = 3;

        [KSPField]
        public float boostTime = 2.2f;

        [KSPField]
        public float cruiseTime = 45;

        [KSPField]
        public float cruiseDelay = 0;

        [KSPField]
        public float maxAoA = 35;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_Direction"),//Direction: 
            UI_Toggle(disabledText = "#LOC_BDArmory_Direction_disabledText", enabledText = "#LOC_BDArmory_Direction_enabledText")]//Lateral--Forward
        public bool decoupleForward = false;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_DecoupleSpeed"),//Decouple Speed
                  UI_FloatRange(minValue = 0f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.Editor)]
        public float decoupleSpeed = 0;

        [KSPField]
        public float clearanceRadius = 0.14f;

        public override float ClearanceRadius => clearanceRadius;

        [KSPField]
        public float clearanceLength = 0.14f;

        public override float ClearanceLength => clearanceLength;

        [KSPField]
        public float optimumAirspeed = 220;

        [KSPField]
        public float blastRadius = -1;

        [KSPField]
        public float blastPower = 25;

        [KSPField]
        public float blastHeat = -1;

        [KSPField]
        public float maxTurnRateDPS = 20;

        [KSPField]
        public bool proxyDetonate = true;

        [KSPField]
        public string audioClipPath = string.Empty;

        AudioClip thrustAudio;

        [KSPField]
        public string boostClipPath = string.Empty;

        AudioClip boostAudio;

        [KSPField]
        public bool isSeismicCharge = false;

        [KSPField]
        public float rndAngVel = 0;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_MaxAltitude"),//Max Altitude
         UI_FloatRange(minValue = 0f, maxValue = 5000f, stepIncrement = 10f, scene = UI_Scene.All)]
        public float maxAltitude = 0f;

        [KSPField]
        public string rotationTransformName = string.Empty;
        Transform rotationTransform;

        [KSPField]
        public bool terminalManeuvering = false;

        [KSPField]
        public string terminalGuidanceType = "";

        [KSPField]
        public float terminalGuidanceDistance = 0.0f;

        private bool terminalGuidanceActive;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TerminalGuidance"), UI_Toggle(disabledText = "#LOC_BDArmory_false", enabledText = "#LOC_BDArmory_true")]//Terminal Guidance: false true
        public bool terminalGuidanceShouldActivate = true;

        [KSPField]
        public string explModelPath = "BDArmory/Models/explosion/explosion";

        public string explSoundPath = "BDArmory/Sounds/explode1";

        //weapon specifications
        [KSPField(advancedTweakable = true, isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_FiringPriority"),
            UI_FloatRange(minValue = 0, maxValue = 10, stepIncrement = 1, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        public float priority = 0; //per-weapon priority selection override

        [KSPField]
        public bool spoolEngine = false;

        [KSPField]
        public bool hasRCS = false;

        [KSPField]
        public float rcsThrust = 1;
        float rcsRVelThreshold = 0.13f;
        KSPParticleEmitter upRCS;
        KSPParticleEmitter downRCS;
        KSPParticleEmitter leftRCS;
        KSPParticleEmitter rightRCS;
        KSPParticleEmitter forwardRCS;
        float rcsAudioMinInterval = 0.2f;

        private AudioSource audioSource;
        public AudioSource sfAudioSource;
        List<KSPParticleEmitter> pEmitters;
        List<BDAGaplessParticleEmitter> gaplessEmitters;

        //float cmTimer;

        //deploy animation
        [KSPField]
        public string deployAnimationName = "";

        [KSPField]
        public float deployedDrag = 0.02f;

        [KSPField]
        public float deployTime = 0.2f;

        [KSPField]
        public string flightAnimationName = "";

        [KSPField]
        public bool OneShotAnim = true;

        [KSPField]
        public bool useSimpleDrag = false;

        [KSPField]
        public float simpleDrag = 0.02f;

        [KSPField]
        public float simpleStableTorque = 5;

        [KSPField]
        public Vector3 simpleCoD = new Vector3(0, 0, -1);

        [KSPField]
        public float agmDescentRatio = 1.45f;

        float currentThrust;

        public bool deployed;
        //public float deployedTime;

        AnimationState[] deployStates;

        AnimationState[] animStates;

        bool hasPlayedFlyby;

        float debugTurnRate;

        List<GameObject> boosters;

        List<GameObject> fairings;

        [KSPField]
        public bool decoupleBoosters = false;

        [KSPField]
        public float boosterDecoupleSpeed = 5;

        [KSPField]
        public float boosterMass = 0;

        //Fuel Weight variables
        [KSPField]
        public float boosterFuelMass = 0;

        [KSPField]
        public float cruiseFuelMass = 0;

        [KSPField]
        public bool useFuel = false;

        Transform vesselReferenceTransform;

        [KSPField]
        public string boostTransformName = string.Empty;
        List<KSPParticleEmitter> boostEmitters;
        List<BDAGaplessParticleEmitter> boostGaplessEmitters;

        [KSPField]
        public string fairingTransformName = string.Empty;

        [KSPField]
        public bool torpedo = false;

        [KSPField]
        public float waterImpactTolerance = 25;

        //ballistic options
        [KSPField]
        public bool indirect = false; //unused

        [KSPField]
        public bool vacuumSteerable = true;

        public GPSTargetInfo designatedGPSInfo;

        float[] rcsFiredTimes;
        KSPParticleEmitter[] rcsTransforms;

        private bool OldInfAmmo = false;
        private bool StartSetupComplete = false;

        //Fuel Burn Variables
        public float GetModuleMass(float baseMass, ModifierStagingSituation situation) => -burnedFuelMass;
        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.CONSTANTLY;

        private float burnRate = 0;
        private float burnedFuelMass = 0;

        public bool SetupComplete => StartSetupComplete;
        public int loftState = 0;
        public float initMaxAoA = 0;
        #endregion Variable Declarations

        [KSPAction("Fire Missile")]
        public void AGFire(KSPActionParam param)
        {
            if (BDArmorySetup.Instance.ActiveWeaponManager != null && BDArmorySetup.Instance.ActiveWeaponManager.vessel == vessel) BDArmorySetup.Instance.ActiveWeaponManager.SendTargetDataToMissile(this);
            if (missileTurret)
            {
                missileTurret.FireMissile(this);
            }
            else if (rotaryRail)
            {
                rotaryRail.FireMissile(this);
            }
            else if (deployableRail)
            {
                deployableRail.FireMissile(this);
            }
            else
            {
                FireMissile();
            }
            if (BDArmorySetup.Instance.ActiveWeaponManager != null) BDArmorySetup.Instance.ActiveWeaponManager.UpdateList();
        }

        [KSPEvent(guiActive = true, guiName = "#LOC_BDArmory_FireMissile", active = true)]//Fire Missile
        public void GuiFire()
        {
            if (BDArmorySetup.Instance.ActiveWeaponManager != null && BDArmorySetup.Instance.ActiveWeaponManager.vessel == vessel) BDArmorySetup.Instance.ActiveWeaponManager.SendTargetDataToMissile(this);
            if (missileTurret)
            {
                missileTurret.FireMissile(this);
            }
            else if (rotaryRail)
            {
                rotaryRail.FireMissile(this);
            }
            else if (deployableRail)
            {
                deployableRail.FireMissile(this);
            }
            else
            {
                FireMissile();
            }
            if (BDArmorySetup.Instance.ActiveWeaponManager != null) BDArmorySetup.Instance.ActiveWeaponManager.UpdateList();
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, active = true, guiName = "#LOC_BDArmory_Jettison")]//Jettison
        public override void Jettison()
        {
            if (missileTurret) return;
            if (multiLauncher && !multiLauncher.permitJettison) return;
            part.decouple(0);
            if (BDArmorySetup.Instance.ActiveWeaponManager != null) BDArmorySetup.Instance.ActiveWeaponManager.UpdateList();
        }

        [KSPAction("Jettison")]
        public void AGJettsion(KSPActionParam param)
        {
            Jettison();
        }

        void ParseWeaponClass()
        {
            missileType = missileType.ToLower();
            if (missileType == "bomb")
            {
                weaponClass = WeaponClasses.Bomb;
            }
            else if (missileType == "torpedo" || missileType == "depthcharge")
            {
                weaponClass = WeaponClasses.SLW;
            }
            else
            {
                weaponClass = WeaponClasses.Missile;
            }
        }
        public override void OnStart(StartState state)
        {
            //base.OnStart(state);

            if (useFuel)
            {
                float initialMass = part.mass;
                if (boosterFuelMass < 0 || boostTime <= 0)
                {
                    if (boosterFuelMass < 0) Debug.LogWarning($"[BDArmory.MissileLauncher]: Error in configuration of {part.name}, boosterFuelMass: {boosterFuelMass} can't be less than 0, reverting to default value.");
                    boosterFuelMass = 0;
                }

                if (cruiseFuelMass < 0 || cruiseTime <= 0)
                {
                    if (cruiseFuelMass < 0) Debug.LogWarning($"[BDArmory.MissileLauncher]: Error in configuration of {part.name}, cruiseFuelMass: {cruiseFuelMass} can't be less than 0, reverting to default value.");
                    cruiseFuelMass = 0;
                }

                if (boosterFuelMass + cruiseFuelMass > initialMass * 0.95f)
                {
                    Debug.LogWarning($"[BDArmory.MissileLauncher]: Error in configuration of {part.name}, boosterFuelMass: {boosterFuelMass} + cruiseFuelMass: {cruiseFuelMass} can't be greater than 95% of the missile mass {initialMass}, clamping to 80% of the missile mass.");
                    if(boosterFuelMass > 0 || boostTime > 0)
                    {
                        if(cruiseFuelMass > 0 || cruiseTime > 0)
                        {
                            boosterFuelMass = Mathf.Clamp(boosterFuelMass, 0, initialMass * 0.4f);
                            cruiseFuelMass = Mathf.Clamp(cruiseFuelMass, 0, initialMass * 0.4f);
                        }
                        else boosterFuelMass = Mathf.Clamp(boosterFuelMass, 0, initialMass * 0.8f);
                    }
                    else cruiseFuelMass = Mathf.Clamp(cruiseFuelMass, 0, initialMass * 0.8f);
                }
                else
                {
                    if (boostTime > 0 && boosterFuelMass <= 0) boosterFuelMass = initialMass * 0.1f;
                    if (cruiseTime > 0 && cruiseFuelMass <= 0) cruiseFuelMass = initialMass * 0.1f;
                }
            }

            if (shortName == string.Empty)
            {
                shortName = part.partInfo.title;
            }
            gaplessEmitters = new List<BDAGaplessParticleEmitter>();
            pEmitters = new List<KSPParticleEmitter>();
            boostEmitters = new List<KSPParticleEmitter>();
            boostGaplessEmitters = new List<BDAGaplessParticleEmitter>();

            Fields["maxOffBoresight"].guiActive = false;
            Fields["maxOffBoresight"].guiActiveEditor = false;
            Fields["maxStaticLaunchRange"].guiActive = false;
            Fields["maxStaticLaunchRange"].guiActiveEditor = false;
            Fields["minStaticLaunchRange"].guiActive = false;
            Fields["minStaticLaunchRange"].guiActiveEditor = false;

            loftState = 0;
            TimeToImpact = float.PositiveInfinity;
            initMaxAoA = maxAoA;

            ParseAntiRadTargetTypes();
            // extension for feature_engagementenvelope

            using (var pEemitter = part.FindModelComponents<KSPParticleEmitter>().GetEnumerator())
                while (pEemitter.MoveNext())
                {
                    if (pEemitter.Current == null) continue;
                    EffectBehaviour.AddParticleEmitter(pEemitter.Current);
                    pEemitter.Current.emit = false;
                }

            if (HighLogic.LoadedSceneIsFlight)
            {
                missileName = part.name;
                if (warheadType == WarheadTypes.Standard || warheadType == WarheadTypes.ContinuousRod)
                {
                    var tnt = part.FindModuleImplementing<BDExplosivePart>();
                    if (tnt is null)
                    {
                        tnt = (BDExplosivePart)part.AddModule("BDExplosivePart");
                        tnt.tntMass = BlastPhysicsUtils.CalculateExplosiveMass(blastRadius);
                    }

                    //New Explosive module
                    DisablingExplosives(part);
                    if (tnt.explModelPath == ModuleWeapon.defaultExplModelPath) tnt.explModelPath = explModelPath; // If the BDExplosivePart is using the default explosion part and sound,
                    if (tnt.explSoundPath == ModuleWeapon.defaultExplSoundPath) tnt.explSoundPath = explSoundPath; // override them with those of the MissileLauncher (if specified).
                }

                MissileReferenceTransform = part.FindModelTransform("missileTransform");
                if (!MissileReferenceTransform)
                {
                    MissileReferenceTransform = part.partTransform;
                }

                origScale = part.partTransform.localScale;
                gauge = (BDStagingAreaGauge)part.AddModule("BDStagingAreaGauge");
                part.force_activate();

                if (!string.IsNullOrEmpty(exhaustPrefabPath))
                {
                    using (var t = part.FindModelTransforms("exhaustTransform").AsEnumerable().GetEnumerator())
                        while (t.MoveNext())
                        {
                            if (t.Current == null) continue;
                            AttachExhaustPrefab(exhaustPrefabPath, this, t.Current);
                        }
                }

                if (!string.IsNullOrEmpty(boostExhaustPrefabPath) && !string.IsNullOrEmpty(boostExhaustTransformName))
                {
                    using (var t = part.FindModelTransforms(boostExhaustTransformName).AsEnumerable().GetEnumerator())
                        while (t.MoveNext())
                        {
                            if (t.Current == null) continue;
                            AttachExhaustPrefab(boostExhaustPrefabPath, this, t.Current);
                        }
                }

                boosters = new List<GameObject>();
                if (!string.IsNullOrEmpty(boostTransformName))
                {
                    using (var t = part.FindModelTransforms(boostTransformName).AsEnumerable().GetEnumerator())
                        while (t.MoveNext())
                        {
                            if (t.Current == null) continue;
                            boosters.Add(t.Current.gameObject);
                            using (var be = t.Current.GetComponentsInChildren<KSPParticleEmitter>().AsEnumerable().GetEnumerator())
                                while (be.MoveNext())
                                {
                                    if (be.Current == null) continue;
                                    if (be.Current.useWorldSpace)
                                    {
                                        if (be.Current.GetComponent<BDAGaplessParticleEmitter>()) continue;
                                        BDAGaplessParticleEmitter ge = be.Current.gameObject.AddComponent<BDAGaplessParticleEmitter>();
                                        ge.part = part;
                                        boostGaplessEmitters.Add(ge);
                                    }
                                    else
                                    {
                                        if (!boostEmitters.Contains(be.Current))
                                        {
                                            boostEmitters.Add(be.Current);
                                        }
                                        EffectBehaviour.AddParticleEmitter(be.Current);
                                    }
                                }
                        }
                }

                fairings = new List<GameObject>();
                if (!string.IsNullOrEmpty(fairingTransformName))
                {
                    using (var t = part.FindModelTransforms(fairingTransformName).AsEnumerable().GetEnumerator())
                        while (t.MoveNext())
                        {
                            if (t.Current == null) continue;
                            fairings.Add(t.Current.gameObject);
                        }
                }

                using (var pEmitter = part.partTransform.Find("model").GetComponentsInChildren<KSPParticleEmitter>().AsEnumerable().GetEnumerator())
                    while (pEmitter.MoveNext())
                    {
                        if (pEmitter.Current == null) continue;
                        if (pEmitter.Current.GetComponent<BDAGaplessParticleEmitter>() || boostEmitters.Contains(pEmitter.Current))
                        {
                            continue;
                        }

                        if (pEmitter.Current.useWorldSpace)
                        {
                            BDAGaplessParticleEmitter gaplessEmitter = pEmitter.Current.gameObject.AddComponent<BDAGaplessParticleEmitter>();
                            gaplessEmitter.part = part;
                            gaplessEmitters.Add(gaplessEmitter);
                        }
                        else
                        {
                            if (pEmitter.Current.transform.name != boostTransformName)
                            {
                                pEmitters.Add(pEmitter.Current);
                            }
                            else
                            {
                                boostEmitters.Add(pEmitter.Current);
                            }
                            EffectBehaviour.AddParticleEmitter(pEmitter.Current);
                        }
                    }

                using (IEnumerator<Light> light = gameObject.GetComponentsInChildren<Light>().AsEnumerable().GetEnumerator())
                    while (light.MoveNext())
                    {
                        if (light.Current == null) continue;
                        light.Current.intensity = 0;
                    }

                //cmTimer = Time.time;

                using (var pe = pEmitters.GetEnumerator())
                    while (pe.MoveNext())
                    {
                        if (pe.Current == null) continue;
                        if (hasRCS)
                        {
                            if (pe.Current.gameObject.name == "rcsUp") upRCS = pe.Current;
                            else if (pe.Current.gameObject.name == "rcsDown") downRCS = pe.Current;
                            else if (pe.Current.gameObject.name == "rcsLeft") leftRCS = pe.Current;
                            else if (pe.Current.gameObject.name == "rcsRight") rightRCS = pe.Current;
                            else if (pe.Current.gameObject.name == "rcsForward") forwardRCS = pe.Current;
                        }

                        if (!pe.Current.gameObject.name.Contains("rcs") && !pe.Current.useWorldSpace)
                        {
                            pe.Current.sizeGrow = 99999;
                        }
                    }

                if (rotationTransformName != string.Empty)
                {
                    rotationTransform = part.FindModelTransform(rotationTransformName);
                }

                if (hasRCS)
                {
                    SetupRCS();
                    KillRCS();
                }
                SetupAudio();

            }

            SetFields();

            if (deployAnimationName != "")
            {
                deployStates = GUIUtils.SetUpAnimation(deployAnimationName, part);
            }
            else
            {
                deployedDrag = simpleDrag;
            }
            if (flightAnimationName != "")
            {
                animStates = GUIUtils.SetUpAnimation(flightAnimationName, part);
            }

            IEnumerator<PartModule> partModules = part.Modules.GetEnumerator();
            while (partModules.MoveNext())
            {
                if (partModules.Current == null) continue;
                if (partModules.Current.moduleName == "BDExplosivePart")
                {
                    ((BDExplosivePart)partModules.Current).ParseWarheadType();
                    if (((BDExplosivePart)partModules.Current).warheadReportingName == "Continuous Rod")
                    {
                        warheadType = WarheadTypes.ContinuousRod;
                    }
                    else warheadType = WarheadTypes.Standard;
                }
                if (partModules.Current.moduleName == "ClusterBomb")
                {
                    clusterbomb = ((ClusterBomb)partModules.Current).submunitions.Count;
                }
                if (partModules.Current.moduleName == "MultiMissileLauncher" && weaponClass == WeaponClasses.Bomb)
                {
                    clusterbomb *= ((MultiMissileLauncher)partModules.Current).salvoSize;
                }
                if (partModules.Current.moduleName == "ModuleEMP")
                {
                    warheadType = WarheadTypes.EMP;
                    StandOffDistance = ((ModuleEMP)partModules.Current).proximity;
                }
                if (partModules.Current.moduleName == "BDModuleNuke")
                {
                    warheadType = WarheadTypes.Nuke;
                    StandOffDistance = BDAMath.Sqrt(((BDModuleNuke)partModules.Current).yield) * 500;
                }
                else continue;
                break;
            }
            partModules.Dispose();
            StartSetupComplete = true;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileLauncher] Start() setup complete");
        }

        public void SetFields()
        {
            ParseWeaponClass();
            ParseModes();
            InitializeEngagementRange(minStaticLaunchRange, maxStaticLaunchRange);
            SetInitialDetonationDistance();
            uncagedLock = (allAspect) ? allAspect : uncagedLock;
            guidanceFailureRatePerFrame = (guidanceFailureRate >= 1) ? 1f : 1f - Mathf.Exp(Mathf.Log(1f - guidanceFailureRate) * Time.fixedDeltaTime); // Convert from per-second failure rate to per-frame failure rate

            if (isTimed)
            {
                Fields["detonationTime"].guiActive = true;
                Fields["detonationTime"].guiActiveEditor = true;
            }
            else
            {
                Fields["detonationTime"].guiActive = false;
                Fields["detonationTime"].guiActiveEditor = false;
            }
            if (GuidanceMode != GuidanceModes.Cruise)
            {
                CruiseAltitudeRange();
                Fields["CruiseAltitude"].guiActive = false;
                Fields["CruiseAltitude"].guiActiveEditor = false;
                Fields["CruiseSpeed"].guiActive = false;
                Fields["CruiseSpeed"].guiActiveEditor = false;
                Events["CruiseAltitudeRange"].guiActive = false;
                Events["CruiseAltitudeRange"].guiActiveEditor = false;
                Fields["CruisePredictionTime"].guiActiveEditor = false;
            }
            else
            {
                CruiseAltitudeRange();
                Fields["CruiseAltitude"].guiActive = true;
                Fields["CruiseAltitude"].guiActiveEditor = true;
                Fields["CruiseSpeed"].guiActive = true;
                Fields["CruiseSpeed"].guiActiveEditor = true;
                Events["CruiseAltitudeRange"].guiActive = true;
                Events["CruiseAltitudeRange"].guiActiveEditor = true;
                Fields["CruisePredictionTime"].guiActiveEditor = true;
            }

            if (GuidanceMode != GuidanceModes.AGM)
            {
                Fields["maxAltitude"].guiActive = false;
                Fields["maxAltitude"].guiActiveEditor = false;
            }
            else
            {
                Fields["maxAltitude"].guiActive = true;
                Fields["maxAltitude"].guiActiveEditor = true;
            }
            if (GuidanceMode != GuidanceModes.AGMBallistic)
            {
                Fields["BallisticOverShootFactor"].guiActive = false;
                Fields["BallisticOverShootFactor"].guiActiveEditor = false;
                Fields["BallisticAngle"].guiActive = false;
                Fields["BallisticAngle"].guiActiveEditor = false;
            }
            else
            {
                Fields["BallisticOverShootFactor"].guiActive = true;
                Fields["BallisticOverShootFactor"].guiActiveEditor = true;
                Fields["BallisticAngle"].guiActive = true;
                Fields["BallisticAngle"].guiActiveEditor = true;
            }

            if (part.partInfo.title.Contains("Bomb") || weaponClass == WeaponClasses.SLW)
            {
                Fields["dropTime"].guiActive = false;
                Fields["dropTime"].guiActiveEditor = false;
                if (torpedo) dropTime = 999;
            }
            else
            {
                Fields["dropTime"].guiActive = true;
                Fields["dropTime"].guiActiveEditor = true;
            }

            if (TargetingModeTerminal != TargetingModes.None)
            {
                Fields["terminalGuidanceShouldActivate"].guiName += terminalGuidanceType;
            }
            else
            {
                Fields["terminalGuidanceShouldActivate"].guiActive = false;
                Fields["terminalGuidanceShouldActivate"].guiActiveEditor = false;
            }

            if (GuidanceMode != GuidanceModes.AAMLoft)
            {
                Fields["LoftMaxAltitude"].guiActive = false;
                Fields["LoftMaxAltitude"].guiActiveEditor = false;
                Fields["LoftRangeOverride"].guiActive = false;
                Fields["LoftRangeOverride"].guiActiveEditor = false;
                Fields["LoftAltitudeAdvMax"].guiActive = false;
                Fields["LoftAltitudeAdvMax"].guiActiveEditor = false;
                Fields["LoftMinAltitude"].guiActive = false;
                Fields["LoftMinAltitude"].guiActiveEditor = false;
                Fields["LoftAngle"].guiActive = false;
                Fields["LoftAngle"].guiActiveEditor = false;
                Fields["LoftTermAngle"].guiActive = false;
                Fields["LoftTermAngle"].guiActiveEditor = false;
                Fields["LoftRangeFac"].guiActive = false;
                Fields["LoftRangeFac"].guiActiveEditor = false;
                Fields["LoftVelComp"].guiActive = false;
                Fields["LoftVelComp"].guiActiveEditor = false;
                Fields["LoftVertVelComp"].guiActive = false;
                Fields["LoftVertVelComp"].guiActiveEditor = false;
                //Fields["LoftAltComp"].guiActive = false;
                //Fields["LoftAltComp"].guiActiveEditor = false;
                Fields["LoftTermRange"].guiActive = false;
                Fields["LoftTermRange"].guiActiveEditor = false;
            }
            else
            {
                Fields["LoftMaxAltitude"].guiActive = true;
                Fields["LoftMaxAltitude"].guiActiveEditor = true;
                Fields["LoftRangeOverride"].guiActive = true;
                Fields["LoftRangeOverride"].guiActiveEditor = true;
                Fields["LoftAltitudeAdvMax"].guiActive = true;
                Fields["LoftAltitudeAdvMax"].guiActiveEditor = true;
                Fields["LoftMinAltitude"].guiActive = true;
                Fields["LoftMinAltitude"].guiActiveEditor = true;
                Fields["LoftTermRange"].guiActive = true;
                Fields["LoftTermRange"].guiActiveEditor = true;

                if (!GameSettings.ADVANCED_TWEAKABLES)
                {
                    Fields["LoftAngle"].guiActive = false;
                    Fields["LoftAngle"].guiActiveEditor = false;
                    Fields["LoftTermAngle"].guiActive = false;
                    Fields["LoftTermAngle"].guiActiveEditor = false;
                    Fields["LoftRangeFac"].guiActive = false;
                    Fields["LoftRangeFac"].guiActiveEditor = false;
                    Fields["LoftVelComp"].guiActive = false;
                    Fields["LoftVelComp"].guiActiveEditor = false;
                    Fields["LoftVertVelComp"].guiActive = false;
                    Fields["LoftVertVelComp"].guiActiveEditor = false;
                    //Fields["LoftAltComp"].guiActive = false;
                    //Fields["LoftAltComp"].guiActiveEditor = false;
                }
                else
                {
                    Fields["LoftAngle"].guiActive = true;
                    Fields["LoftAngle"].guiActiveEditor = true;
                    Fields["LoftTermAngle"].guiActive = true;
                    Fields["LoftTermAngle"].guiActiveEditor = true;
                    Fields["LoftRangeFac"].guiActive = true;
                    Fields["LoftRangeFac"].guiActiveEditor = true;
                    Fields["LoftVelComp"].guiActive = true;
                    Fields["LoftVelComp"].guiActiveEditor = true;
                    Fields["LoftVertVelComp"].guiActive = true;
                    Fields["LoftVertVelComp"].guiActiveEditor = true;
                    //Fields["LoftAltComp"].guiActive = true;
                    //Fields["LoftAltComp"].guiActiveEditor = true;
                }
            }

            // fill lockedSensorFOVBias with default values if not set by part config:
            if ((TargetingMode == TargetingModes.Heat || TargetingModeTerminal == TargetingModes.Heat) && heatThreshold > 0 && lockedSensorFOVBias.minTime == float.MaxValue)
            {
                float a = lockedSensorFOV / 2f;
                float b = -1f * ((1f - 1f / 1.2f));
                float[] x = new float[6] { 0f * a, 0.2f * a, 0.4f * a, 0.6f * a, 0.8f * a, 1f * a };
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher]: OnStart missile {shortName}: setting default lockedSensorFOVBias curve to:");
                for (int i = 0; i < 6; i++)
                {
                    lockedSensorFOVBias.Add(x[i], b / (a * a) * x[i] * x[i] + 1f, -1f / 3f * x[i] / (a * a), -1f / 3f * x[i] / (a * a));
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("key = " + x[i] + " " + (b / (a * a) * x[i] * x[i] + 1f) + " " + (-1f / 3f * x[i] / (a * a)) + " " + (-1f / 3f * x[i] / (a * a)));
                }
            }

            // fill lockedSensorVelocityBias with default values if not set by part config:
            if ((TargetingMode == TargetingModes.Heat || TargetingModeTerminal == TargetingModes.Heat) && heatThreshold > 0 && lockedSensorVelocityBias.minTime == float.MaxValue)
            {
                lockedSensorVelocityBias.Add(0f, 1f);
                lockedSensorVelocityBias.Add(180f, 1f);
                if (BDArmorySettings.DEBUG_MISSILES)
                {
                    Debug.Log($"[BDArmory.MissileLauncher]: OnStart missile {shortName}: setting default lockedSensorVelocityBias curve to:");
                    Debug.Log("key = 0 1");
                    Debug.Log("key = 180 1");
                }
            }

            // fill activeRadarLockTrackCurve with default values if not set by part config:
            if ((TargetingMode == TargetingModes.Radar || TargetingModeTerminal == TargetingModes.Radar) && activeRadarRange > 0 && activeRadarLockTrackCurve.minTime == float.MaxValue)
            {
                activeRadarLockTrackCurve.Add(0f, 0f);
                activeRadarLockTrackCurve.Add(activeRadarRange, RadarUtils.MISSILE_DEFAULT_LOCKABLE_RCS);           // TODO: tune & balance constants!
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher]: OnStart missile {shortName}: setting default locktrackcurve with maxrange/minrcs: {activeRadarLockTrackCurve.maxTime}/{RadarUtils.MISSILE_DEFAULT_LOCKABLE_RCS}");
            }
            GUIUtils.RefreshAssociatedWindows(part);
        }

        /// <summary>
        /// This method will convert the blastPower to a tnt mass equivalent
        /// </summary>
        private void FromBlastPowerToTNTMass()
        {
            blastPower = BlastPhysicsUtils.CalculateExplosiveMass(blastRadius);
        }

        void OnCollisionEnter(Collision col)
        {
            base.CollisionEnter(col);
        }

        void SetupAudio()
        {
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.minDistance = 1;
                audioSource.maxDistance = 1000;
                audioSource.loop = true;
                audioSource.pitch = 1f;
                audioSource.priority = 255;
                audioSource.spatialBlend = 1;
            }

            if (audioClipPath != string.Empty)
            {
                audioSource.clip = SoundUtils.GetAudioClip(audioClipPath);
            }

            if (sfAudioSource == null)
            {
                sfAudioSource = gameObject.AddComponent<AudioSource>();
                sfAudioSource.minDistance = 1;
                sfAudioSource.maxDistance = 2000;
                sfAudioSource.dopplerLevel = 0;
                sfAudioSource.priority = 230;
                sfAudioSource.spatialBlend = 1;
            }

            if (audioClipPath != string.Empty)
            {
                thrustAudio = SoundUtils.GetAudioClip(audioClipPath);
            }

            if (boostClipPath != string.Empty)
            {
                boostAudio = SoundUtils.GetAudioClip(boostClipPath);
            }

            UpdateVolume();
            BDArmorySetup.OnVolumeChange -= UpdateVolume; // Remove it if it's already there. (Doesn't matter if it isn't.)
            BDArmorySetup.OnVolumeChange += UpdateVolume;
        }

        void UpdateVolume()
        {
            if (audioSource)
            {
                audioSource.volume = BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
            }
            if (sfAudioSource)
            {
                sfAudioSource.volume = BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
            }
        }

        void OnDestroy()
        {
            DetachExhaustPrefabs();
            KillRCS();
            if (upRCS) EffectBehaviour.RemoveParticleEmitter(upRCS);
            if (downRCS) EffectBehaviour.RemoveParticleEmitter(downRCS);
            if (leftRCS) EffectBehaviour.RemoveParticleEmitter(leftRCS);
            if (rightRCS) EffectBehaviour.RemoveParticleEmitter(rightRCS);
            if (pEmitters != null)
                foreach (var pe in pEmitters)
                    if (pe) EffectBehaviour.RemoveParticleEmitter(pe);
            if (gaplessEmitters is not null) // Make sure the gapless emitters get destroyed (they should anyway, but KSP holds onto part references, which may prevent this from happening automatically).
                foreach (var gpe in gaplessEmitters)
                    if (gpe is not null) Destroy(gpe);
            if (boostEmitters != null)
                foreach (var pe in boostEmitters)
                    if (pe) EffectBehaviour.RemoveParticleEmitter(pe);
            BDArmorySetup.OnVolumeChange -= UpdateVolume;
            GameEvents.onPartDie.Remove(PartDie);
            if (vesselReferenceTransform != null && vesselReferenceTransform.gameObject != null)
            {
                Destroy(vesselReferenceTransform.gameObject);
            }
        }

        public override float GetBlastRadius()
        {
            if (blastRadius > 0) { return blastRadius; }
            else
            {
                if (warheadType == WarheadTypes.EMP)
                {
                    if (part.FindModuleImplementing<ModuleEMP>() != null)
                    {
                        blastRadius = part.FindModuleImplementing<ModuleEMP>().proximity;
                        return blastRadius;
                    }
                    else
                    {
                        blastRadius = 150;
                        return 150;
                    }
                }
                else if (warheadType == WarheadTypes.Nuke)
                {
                    if (part.FindModuleImplementing<BDModuleNuke>() != null)
                    {
                        blastRadius = BDAMath.Sqrt(part.FindModuleImplementing<BDModuleNuke>().yield) * 500;
                        return blastRadius;
                    }
                    else
                    {
                        blastRadius = 150;
                        return 150;
                    }
                }
                else
                {
                    if (part.FindModuleImplementing<BDExplosivePart>() != null)
                    {
                        blastRadius = part.FindModuleImplementing<BDExplosivePart>().GetBlastRadius();
                        return blastRadius;
                    }
                    else
                    {
                        blastRadius = 150;
                        return blastRadius;
                    }
                }
            }
        }

        public override void FireMissile()
        {
            if (HasFired || launched) return;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher]: Missile launch initiated! {vessel.vesselName}");

            var wpm = VesselModuleRegistry.GetMissileFire(SourceVessel != null ? SourceVessel : vessel, true);
            if (wpm != null) Team = wpm.Team;
            if (SourceVessel == null) SourceVessel = vessel;

            if (multiLauncher && multiLauncher.isMultiLauncher)
            {
                //multiLauncher.rippleRPM = wpm.rippleRPM;               
                //if (wpm.rippleRPM > 0) multiLauncher.rippleRPM = wpm.rippleRPM;
                multiLauncher.Team = Team;
                if (reloadableRail && reloadableRail.ammoCount >= 1 || BDArmorySettings.INFINITE_ORDINANCE) multiLauncher.fireMissile();
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher]: firing Multilauncher! {vessel.vesselName}; {multiLauncher.subMunitionName}");
            }
            else
            {
                if (reloadableRail && (multiLauncher && !multiLauncher.isClusterMissile) || ((multiLauncher && multiLauncher.isClusterMissile) && reloadableRail.maxAmmo > 1))
                {
                    if (reloadableMissile == null) reloadableMissile = StartCoroutine(FireReloadableMissile());
                    launched = true;
                }
                else
                {
                    TimeFired = Time.time;
                    part.decouple(0);
                    part.Unpack();
                    vessel.vesselName = GetShortName();
                    TargetPosition = (multiLauncher ? vessel.ReferenceTransform.position + vessel.ReferenceTransform.up * 5000 : transform.position + transform.forward * 5000); //set initial target position so if no target update, missileBase will count a miss if it nears this point or is flying post-thrust
                    MissileLaunch();
                    BDATargetManager.FiredMissiles.Add(this);
                    if (wpm != null) wpm.heatTarget = TargetSignatureData.noTarget;
                    launched = true;
                }
            }
        }
        IEnumerator FireReloadableMissile()
        {
            part.partTransform.localScale = Vector3.zero;
            part.ShieldedFromAirstream = true;
            part.crashTolerance = 100;
            reloadableRail.SpawnMissile(MissileReferenceTransform);
            MissileLauncher ml = reloadableRail.SpawnedMissile.FindModuleImplementing<MissileLauncher>();
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher]: Spawning missile {reloadableRail.SpawnedMissile.name}; type: {ml.homingType}/{ml.targetingType}");
            yield return new WaitUntilFixed(() => ml.SetupComplete); // Wait until missile fully initialized.

            ml.launched = true;
            GetMissileCount();
            var wpm = VesselModuleRegistry.GetMissileFire(SourceVessel, true);
            BDATargetManager.FiredMissiles.Add(ml);
            ml.SourceVessel = SourceVessel;
            ml.GuidanceMode = GuidanceMode;
            //wpm.SendTargetDataToMissile(ml);
            ml.TimeFired = Time.time;
            ml.vessel.vesselName = GetShortName();
            ml.DetonationDistance = DetonationDistance;
            ml.DetonateAtMinimumDistance = DetonateAtMinimumDistance;
            ml.dropTime = dropTime;
            ml.detonationTime = detonationTime;
            ml.engageAir = engageAir;
            ml.engageGround = engageGround;
            ml.engageMissile = engageMissile;
            ml.engageSLW = engageSLW;
            if (GuidanceMode == GuidanceModes.AGMBallistic)
            {
                ml.BallisticOverShootFactor = BallisticOverShootFactor; //are some of these null, and causeing this to quit? 
                ml.BallisticAngle = BallisticAngle;
            }
            if (GuidanceMode == GuidanceModes.Cruise)
            {
                ml.CruiseAltitude = CruiseAltitude;
                ml.CruiseSpeed = CruiseSpeed;
                ml.CruisePredictionTime = CruisePredictionTime;
            }
            if (GuidanceMode == GuidanceModes.AAMLoft)
            {
                ml.LoftMaxAltitude = LoftMaxAltitude;
                ml.LoftRangeOverride = LoftRangeOverride;
                ml.LoftAltitudeAdvMax = LoftAltitudeAdvMax;
                ml.LoftMinAltitude = LoftMinAltitude;
                ml.LoftAngle = LoftAngle;
                ml.LoftTermAngle = LoftTermAngle;
                ml.LoftRangeFac = LoftRangeFac;
                ml.LoftVelComp = LoftVelComp;
                ml.LoftVertVelComp = LoftVertVelComp;
                //ml.LoftAltComp = LoftAltComp;
                ml.LoftTermRange = LoftTermRange;
                ml.loftState = 0;
                ml.TimeToImpact = float.PositiveInfinity;
                ml.initMaxAoA = maxAoA;
            }
            ml.decoupleForward = decoupleForward;
            ml.decoupleSpeed = decoupleSpeed;
            if (GuidanceMode == GuidanceModes.AGM)
                ml.maxAltitude = maxAltitude;
            ml.terminalGuidanceShouldActivate = terminalGuidanceShouldActivate;
            ml.guidanceActive = true;
            if (wpm != null)
            {
                ml.Team = wpm.Team;
                wpm.SendTargetDataToMissile(ml);
                wpm.heatTarget = TargetSignatureData.noTarget;
            }
            ml.TargetPosition = transform.position + (multiLauncher ? vessel.ReferenceTransform.up * 5000 : transform.forward * 5000); //set initial target position so if no target update, missileBase will count a miss if it nears this point or is flying post-thrust
            ml.MissileLaunch();
            GetMissileCount();
            if (reloadableRail.ammoCount > 0 || BDArmorySettings.INFINITE_ORDINANCE)
            {
                if (!(reloadRoutine != null))
                {
                    reloadRoutine = StartCoroutine(MissileReload());
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileLauncher] reloading standard missile");
                }
            }
            reloadableMissile = null;
        }
        public void MissileLaunch()
        {
            HasFired = true;
            try // FIXME Remove this once the fix is sufficiently tested.
            {
                GameEvents.onPartDie.Add(PartDie);

                if (GetComponentInChildren<KSPParticleEmitter>())
                {
                    BDArmorySetup.numberOfParticleEmitters++;
                }

                if (sfAudioSource == null) SetupAudio();
                sfAudioSource.PlayOneShot(SoundUtils.GetAudioClip("BDArmory/Sounds/deployClick"));
                //SourceVessel = vessel;

                //TARGETING
                startDirection = transform.forward;

                if (maxAltitude == 0) // && GuidanceMode != GuidanceModes.Lofted)
                {
                    if (targetVessel != null) maxAltitude = (float)Math.Max(vessel.radarAltitude, targetVessel.Vessel.radarAltitude) + 1000;
                    else maxAltitude = (float)vessel.radarAltitude + 2500;
                }
                SetLaserTargeting();
                SetAntiRadTargeting();

                part.force_activate();

                vessel.situation = Vessel.Situations.FLYING;
                part.rb.isKinematic = false;
                part.bodyLiftMultiplier = 0;
                part.dragModel = Part.DragModel.NONE;

                //add target info to vessel
                AddTargetInfoToVessel();
                StartCoroutine(DecoupleRoutine());

                vessel.vesselType = VesselType.Probe;
                if (BDArmorySettings.DEBUG_MISSILES) vessel.vesselName = $"{SourceVessel}'s {GetShortName()}";
                //setting ref transform for navball
                GameObject refObject = new GameObject();
                refObject.transform.rotation = Quaternion.LookRotation(-transform.up, transform.forward);
                refObject.transform.parent = transform;
                part.SetReferenceTransform(refObject.transform);
                vessel.SetReferenceTransform(part);
                vesselReferenceTransform = refObject.transform;
                DetonationDistanceState = DetonationDistanceStates.NotSafe;
                MissileState = MissileStates.Drop;
                part.crashTolerance = 9999; //to combat stresses of launch, missle generate a lot of G Force
                part.explosionPotential = 0; // Minimise the default part explosion FX that sometimes gets offset from the main explosion.

                StartCoroutine(MissileRoutine());
                if (multiLauncher && multiLauncher.isClusterMissile)
                {
                    reloadableRail.MissileName = multiLauncher.subMunitionName;
                    reloadableRail.UpdateMissileValues();
                }
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileLauncher]: Missile Launched!");
                if (BDArmorySettings.CAMERA_SWITCH_INCLUDE_MISSILES && SourceVessel.isActiveVessel) LoadedVesselSwitcher.Instance.ForceSwitchVessel(vessel);
            }
            catch (Exception e)
            {
                Debug.LogError("[BDArmory.MissileLauncher]: DEBUG " + e.Message);
                try { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG null part?: " + (part == null)); } catch (Exception e2) { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG part: " + e2.Message); }
                try { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG null part.rb?: " + (part.rb == null)); } catch (Exception e2) { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG part.rb: " + e2.Message); }
                try { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG null BDATargetManager.FiredMissiles?: " + (BDATargetManager.FiredMissiles == null)); } catch (Exception e2) { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG BDATargetManager.FiredMissiles: " + e2.Message); }
                try { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG null vessel?: " + (vessel == null)); } catch (Exception e2) { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG vessel: " + e2.Message); }
                try { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG null sfAudioSource?: " + (sfAudioSource == null)); } catch (Exception e2) { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG sfAudioSource: " + e2.Message); }
                throw; // Re-throw the exception so behaviour is unchanged so we see it.
            }
        }

        public IEnumerator MissileReload()
        {
            yield return new WaitForSecondsFixed(reloadableRail.reloadTime);
            launched = false;
            part.partTransform.localScale = origScale;
            reloadTimer = 0;
            gauge.UpdateReloadMeter(1);
            part.crashTolerance = 5;
            if (!inCargoBay) part.ShieldedFromAirstream = false;
            if (deployableRail) deployableRail.UpdateChildrenPos();
            if (rotaryRail) rotaryRail.UpdateMissilePositions();
            if (multiLauncher) multiLauncher.PopulateMissileDummies();
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher] reload complete on {part.name}");
            reloadRoutine = null;
        }

        IEnumerator DecoupleRoutine()
        {
            yield return new WaitForFixedUpdate();

            if (rndAngVel > 0)
            {
                part.rb.angularVelocity += UnityEngine.Random.insideUnitSphere.normalized * rndAngVel;
            }

            if (decoupleForward)
            {
                part.rb.velocity += decoupleSpeed * part.transform.forward;
                if (multiLauncher && multiLauncher.isMultiLauncher && multiLauncher.salvoSize > 1) //add some scatter to missile salvoes
                {
                    part.rb.velocity += (UnityEngine.Random.Range(-1f, 1f) * (decoupleSpeed / 4)) * part.transform.up;
                    part.rb.velocity += (UnityEngine.Random.Range(-1f, 1f) * (decoupleSpeed / 4)) * part.transform.right;
                }
            }
            else
            {
                part.rb.velocity += decoupleSpeed * -part.transform.up;
            }
        }

        /// <summary>
        /// Fires the missileBase on target vessel.  Used by AI currently.
        /// </summary>
        /// <param name="v">V.</param>
        public void FireMissileOnTarget(Vessel v)
        {
            if (!HasFired)
            {
                targetVessel = v.gameObject.GetComponent<TargetInfo>();
                FireMissile();
            }
        }

        void OnDisable()
        {
            if (TargetingMode == TargetingModes.AntiRad)
            {
                RadarWarningReceiver.OnRadarPing -= ReceiveRadarPing;
            }
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();

            if (!HighLogic.LoadedSceneIsFlight) return;

            FloatingOriginCorrection();

            try // FIXME Remove this once the fix is sufficiently tested.
            {
                debugString.Length = 0;

                if (HasFired && !HasExploded && part != null)
                {
                    CheckDetonationState();
                    CheckDetonationDistance();
                    part.rb.isKinematic = false;
                    AntiSpin();
                    //simpleDrag
                    if (useSimpleDrag)
                    {
                        SimpleDrag();
                    }

                    //flybyaudio
                    float mCamDistanceSqr = (FlightCamera.fetch.mainCamera.transform.position - transform.position).sqrMagnitude;
                    float mCamRelVSqr = (float)(FlightGlobals.ActiveVessel.Velocity() - vessel.Velocity()).sqrMagnitude;
                    if (!hasPlayedFlyby
                       && FlightGlobals.ActiveVessel != vessel
                       && FlightGlobals.ActiveVessel != SourceVessel
                       && mCamDistanceSqr < 400 * 400 && mCamRelVSqr > 300 * 300
                       && mCamRelVSqr < 800 * 800
                       && Vector3.Angle(vessel.Velocity(), FlightGlobals.ActiveVessel.transform.position - transform.position) < 60)
                    {
                        if (sfAudioSource == null) SetupAudio();
                        sfAudioSource.PlayOneShot(SoundUtils.GetAudioClip("BDArmory/Sounds/missileFlyby"));
                        hasPlayedFlyby = true;
                    }
                    if (vessel.isActiveVessel)
                    {
                        audioSource.dopplerLevel = 0;
                    }
                    else
                    {
                        audioSource.dopplerLevel = 1f;
                    }
                    if (TimeIndex > 0.5f)
                    {
                        if (torpedo)
                        {
                            if (vessel.altitude > 0)
                            {
                                part.crashTolerance = waterImpactTolerance; // + (float)vessel.horizontalSrfSpeed; ?
                            }
                            else
                            {
                                part.crashTolerance = 1;
                            }
                        }
                        else
                        {
                            part.crashTolerance = 1;
                        }
                    }

                    UpdateThrustForces();
                    UpdateGuidance();

                    //RaycastCollisions();

                    //Timed detonation
                    if (isTimed && TimeIndex > detonationTime)
                    {
                        Detonate();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[BDArmory.MissileLauncher]: DEBUG " + e.Message + "\n" + e.StackTrace);
                // throw; // Re-throw the exception so behaviour is unchanged so we see it.
                /* FIXME this is being caused by attempting to get the wm.Team in RadarUpdateMissileLock. A similar exception occurred in BDATeamIcons, line 239
                    [ERR 12:05:24.391] Module MissileLauncher threw during OnFixedUpdate: System.NullReferenceException: Object reference not set to an instance of an object
                        at BDArmory.Radar.RadarUtils.RadarUpdateMissileLock (UnityEngine.Ray ray, System.Single fov, BDArmory.Targeting.TargetSignatureData[]& dataArray, System.Single dataPersistTime, BDArmory.Weapons.Missiles.MissileBase missile) [0x00076] in /storage/github/BDArmory/BDArmory/Radar/RadarUtils.cs:972 
                        at BDArmory.Weapons.Missiles.MissileBase.UpdateRadarTarget () [0x003d9] in /storage/github/BDArmory/BDArmory/Weapons/Missiles/MissileBase.cs:747 
                        at BDArmory.Weapons.Missiles.MissileLauncher.UpdateGuidance () [0x000ba] in /storage/github/BDArmory/BDArmory/Weapons/Missiles/MissileLauncher.cs:1134 
                        at BDArmory.Weapons.Missiles.MissileLauncher.OnFixedUpdate () [0x00593] in /storage/github/BDArmory/BDArmory/Weapons/Missiles/MissileLauncher.cs:1046 
                        at Part.ModulesOnFixedUpdate () [0x000bd] in <4deecb19beb547f19b1ff89b4c59bd84>:0 
                        UnityEngine.DebugLogHandler:LogFormat(LogType, Object, String, Object[])
                        ModuleManager.UnityLogHandle.InterceptLogHandler:LogFormat(LogType, Object, String, Object[])
                        UnityEngine.Debug:LogError(Object)
                        Part:ModulesOnFixedUpdate()
                        Part:FixedUpdate()
                */
            }
            if (reloadableRail)
            {
                if (launched && reloadRoutine != null)
                {
                    reloadTimer = Mathf.Clamp((reloadTimer + 1 * TimeWarp.fixedDeltaTime / reloadableRail.reloadTime), 0, 1);
                    if (vessel.isActiveVessel) gauge.UpdateReloadMeter(reloadTimer);
                }
                if (heatTimer > 0)
                {
                    heatTimer -= TimeWarp.fixedDeltaTime;
                    if (vessel.isActiveVessel)
                    {
                        gauge.UpdateHeatMeter(heatTimer / multiLauncher.launcherCooldown);
                    }
                }
                if (OldInfAmmo != BDArmorySettings.INFINITE_ORDINANCE)
                {
                    if (reloadableRail.ammoCount < 1 && BDArmorySettings.INFINITE_ORDINANCE)
                    {
                        if (!(reloadRoutine != null))
                        {
                            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileLauncher] Infinite Ammo enabled, reloading");
                            reloadRoutine = StartCoroutine(MissileReload());
                        }
                    }
                    OldInfAmmo = BDArmorySettings.INFINITE_ORDINANCE;
                }
            }
        }

        private void CheckMiss()
        {
            float sqrDist = (float)((TargetPosition + (TargetVelocity * Time.fixedDeltaTime)) - (vessel.CoM + (vessel.Velocity() * Time.fixedDeltaTime))).sqrMagnitude;
            if (sqrDist < 160000 || MissileState == MissileStates.PostThrust)
            {
                checkMiss = true;
            }
            if (maxAltitude != 0f)
            {
                if (vessel.altitude >= maxAltitude) checkMiss = true;
            }

            //kill guidance if missileBase has missed
            if (!HasMissed && checkMiss)
            {
                bool noProgress = MissileState == MissileStates.PostThrust && (Vector3.Dot(vessel.Velocity() - TargetVelocity, TargetPosition - vessel.transform.position) < 0);
                bool pastGracePeriod = TimeIndex > ((vessel.LandedOrSplashed ? 0f : dropTime) + 180f / maxTurnRateDPS);
                bool targetBehindMissile = Vector3.Dot(TargetPosition - transform.position, transform.forward) < 0f;
                if ((pastGracePeriod && targetBehindMissile) || noProgress) // Check that we're not moving away from the target after a grace period
                {
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileLauncher]: Missile has missed!");

                    if (vessel.altitude >= maxAltitude && maxAltitude != 0f)
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileLauncher]: CheckMiss trigged by MaxAltitude");

                    HasMissed = true;
                    guidanceActive = false;

                    TargetMf = null;

                    MissileLauncher launcher = this as MissileLauncher;
                    if (launcher != null)
                    {
                        if (launcher.hasRCS) launcher.KillRCS();
                    }

                    var distThreshold = 0.5f * GetBlastRadius();
                    if (sqrDist < distThreshold * distThreshold) part.Destroy();
                    if (FuseFailed) part.Destroy();

                    isTimed = true;
                    detonationTime = TimeIndex + 1.5f;
                    if (BDArmorySettings.CAMERA_SWITCH_INCLUDE_MISSILES && vessel.isActiveVessel) LoadedVesselSwitcher.Instance.TriggerSwitchVessel();
                    return;
                }
            }
        }

        string debugGuidanceTarget;
        void UpdateGuidance()
        {
            if (guidanceActive && guidanceFailureRatePerFrame > 0f)
                if (UnityEngine.Random.Range(0f, 1f) < guidanceFailureRatePerFrame)
                {
                    guidanceActive = false;
                    BDATargetManager.FiredMissiles.Remove(this);
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileLauncher]: Missile Guidance Failed!");
                }

            if (guidanceActive)
            {
                switch (TargetingMode)
                {
                    case TargetingModes.Heat:
                        UpdateHeatTarget();
                        if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
                        {
                            if (heatTarget.vessel)
                                debugGuidanceTarget = $"{heatTarget.vessel.GetDisplayName()} {heatTarget.signalStrength}";
                            else if (heatTarget.signalStrength > 0)
                                debugGuidanceTarget = $"Flare {heatTarget.signalStrength}";
                        }
                        break;
                    case TargetingModes.Radar:
                        UpdateRadarTarget();
                        if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
                        {
                            if (radarTarget.vessel)
                                debugGuidanceTarget = $"{radarTarget.vessel.GetDisplayName()} {radarTarget.signalStrength}";
                            else if (radarTarget.signalStrength > 0)
                                debugGuidanceTarget = $"Chaff {radarTarget.signalStrength}";
                        }
                        break;
                    case TargetingModes.Laser:
                        UpdateLaserTarget();
                        if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
                        {
                            debugGuidanceTarget = TargetPosition.ToString();
                        }
                        break;
                    case TargetingModes.Gps:
                        UpdateGPSTarget();
                        if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
                        {
                            debugGuidanceTarget = UpdateGPSTarget().ToString();
                        }
                        break;
                    case TargetingModes.AntiRad:
                        UpdateAntiRadiationTarget();
                        if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
                        {
                            debugGuidanceTarget = TargetPosition.ToString();
                        }
                        break;
                    default:
                        if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
                        {
                            debugGuidanceTarget = "none";
                        }
                        break;
                }

                UpdateTerminalGuidance();
            }

            if (MissileState != MissileStates.Idle && MissileState != MissileStates.Drop) //guidance
            {
                //guidance and attitude stabilisation scales to atmospheric density. //use part.atmDensity
                float atmosMultiplier = Mathf.Clamp01(2.5f * (float)FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(transform.position), FlightGlobals.getExternalTemperature(), FlightGlobals.currentMainBody));

                if (vessel.srfSpeed < optimumAirspeed)
                {
                    float optimumSpeedFactor = (float)vessel.srfSpeed / (2 * optimumAirspeed);
                    controlAuthority = Mathf.Clamp01(atmosMultiplier * (-Mathf.Abs(2 * optimumSpeedFactor - 1) + 1));
                }
                else
                {
                    controlAuthority = Mathf.Clamp01(atmosMultiplier);
                }

                if (vacuumSteerable)
                {
                    controlAuthority = 1;
                }

                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES) debugString.AppendLine($"controlAuthority: {controlAuthority}");

                if (guidanceActive)// && timeIndex - dropTime > 0.5f)
                {
                    WarnTarget();

                    //if (targetVessel && targetVessel.loaded)
                    //{
                    //   Vector3 targetCoMPos = targetVessel.CoM;
                    //    TargetPosition = targetCoMPos + targetVessel.Velocity() * Time.fixedDeltaTime;
                    //}

                    //increaseTurnRate after launch
                    float turnRateDPS = Mathf.Clamp(((TimeIndex - dropTime) / boostTime) * maxTurnRateDPS * 25f, 0, maxTurnRateDPS);
                    if (!hasRCS)
                    {
                        turnRateDPS *= controlAuthority;
                    }

                    //decrease turn rate after thrust cuts out
                    if (TimeIndex > dropTime + boostTime + cruiseTime)
                    {
                        var clampedTurnRate = Mathf.Clamp(maxTurnRateDPS - ((TimeIndex - dropTime - boostTime - cruiseTime) * 0.45f),
                            1, maxTurnRateDPS);
                        turnRateDPS = clampedTurnRate;

                        if (!vacuumSteerable)
                        {
                            turnRateDPS *= atmosMultiplier;
                        }

                        if (hasRCS)
                        {
                            turnRateDPS = 0;
                        }
                    }

                    if (hasRCS)
                    {
                        if (turnRateDPS > 0)
                        {
                            DoRCS();
                        }
                        else
                        {
                            KillRCS();
                        }
                    }
                    debugTurnRate = turnRateDPS;

                    finalMaxTorque = Mathf.Clamp((TimeIndex - dropTime) * torqueRampUp, 0, maxTorque); //ramp up torque

                    if ((GuidanceMode == GuidanceModes.AAMLead) || (GuidanceMode == GuidanceModes.APN) || (GuidanceMode == GuidanceModes.PN) || (GuidanceMode == GuidanceModes.AAMLoft) || (GuidanceMode == GuidanceModes.AAMPure))
                    {
                        AAMGuidance();
                    }
                    else if (GuidanceMode == GuidanceModes.AGM)
                    {
                        AGMGuidance();
                    }
                    else if (GuidanceMode == GuidanceModes.AGMBallistic)
                    {
                        AGMBallisticGuidance();
                    }
                    else if (GuidanceMode == GuidanceModes.BeamRiding)
                    {
                        BeamRideGuidance();
                    }
                    else if (GuidanceMode == GuidanceModes.RCS)
                    {
                        part.transform.rotation = Quaternion.RotateTowards(part.transform.rotation, Quaternion.LookRotation(TargetPosition - part.transform.position, part.transform.up), turnRateDPS * Time.fixedDeltaTime);
                    }
                    else if (GuidanceMode == GuidanceModes.Cruise)
                    {
                        CruiseGuidance();
                    }
                    else if (GuidanceMode == GuidanceModes.SLW)
                    {
                        SLWGuidance();
                    }

                }
                else
                {
                    CheckMiss();
                    TargetMf = null;
                    if (aero)
                    {
                        aeroTorque = MissileGuidance.DoAeroForces(this, transform.position + (20 * vessel.Velocity()), liftArea, .25f, aeroTorque, maxTorque, maxAoA);
                    }
                }

                if (aero && aeroSteerDamping > 0)
                {
                    part.rb.AddRelativeTorque(-aeroSteerDamping * part.transform.InverseTransformVector(part.rb.angularVelocity));
                }

                if (hasRCS && !guidanceActive)
                {
                    KillRCS();
                }
            }

            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES) debugString.AppendLine("Missile target=" + debugGuidanceTarget);
        }

        // feature_engagementenvelope: terminal guidance mode for cruise missiles
        private void UpdateTerminalGuidance()
        {
            // check if guidance mode should be changed for terminal phase
            float distanceSqr = (TargetPosition - transform.position).sqrMagnitude;

            if (terminalGuidanceShouldActivate && !terminalGuidanceActive && (TargetingModeTerminal != TargetingModes.None) && (distanceSqr < terminalGuidanceDistance * terminalGuidanceDistance))
            {
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher][Terminal Guidance]: missile {GetPartName()} updating targeting mode: {terminalGuidanceType}");

                TargetingMode = TargetingModeTerminal;
                terminalGuidanceActive = true;
                TargetAcquired = false;

                switch (TargetingModeTerminal)
                {
                    case TargetingModes.Heat:
                        // gets ground heat targets and after locking one, disallows the lock to break to another target
                        heatTarget = BDATargetManager.GetHeatTarget(SourceVessel, vessel, new Ray(transform.position + (50 * GetForwardTransform()), GetForwardTransform()), heatTarget, lockedSensorFOV / 2, heatThreshold, frontAspectHeatModifier, uncagedLock, lockedSensorFOVBias, lockedSensorVelocityBias, SourceVessel ? VesselModuleRegistry.GetModule<MissileFire>(SourceVessel) : null, targetVessel);
                        if (heatTarget.exists)
                        {
                            if (BDArmorySettings.DEBUG_MISSILES)
                            {
                                Debug.Log($"[BDArmory.MissileLauncher][Terminal Guidance]: Heat target acquired! Position: {heatTarget.position}, heatscore: {heatTarget.signalStrength}");
                            }
                            TargetAcquired = true;
                            TargetPosition = heatTarget.position + (2 * heatTarget.velocity * Time.fixedDeltaTime); // Not sure why this is 2*
                            TargetVelocity = heatTarget.velocity;
                            TargetAcceleration = heatTarget.acceleration;
                            lockFailTimer = -1; // ensures proper entry into UpdateHeatTarget()

                            // Disable terminal guidance and switch to regular heat guidance for next update
                            terminalGuidanceShouldActivate = false;
                            TargetingMode = TargetingModes.Heat;

                            // Adjust heat score based on distance missile will travel in the next update
                            if (heatTarget.signalStrength > 0)
                            {
                                float currentFactor = (1400 * 1400) / Mathf.Clamp((heatTarget.position - transform.position).sqrMagnitude, 90000, 36000000);
                                Vector3 currVel = (float)vessel.srfSpeed * vessel.Velocity().normalized;
                                heatTarget.position = heatTarget.position + heatTarget.velocity * Time.fixedDeltaTime;
                                heatTarget.velocity = heatTarget.velocity + heatTarget.acceleration * Time.fixedDeltaTime;
                                float futureFactor = (1400 * 1400) / Mathf.Clamp((heatTarget.position - (transform.position + (currVel * Time.fixedDeltaTime))).sqrMagnitude, 90000, 36000000);
                                heatTarget.signalStrength *= futureFactor / currentFactor;
                            }
                        }
                        else
                        {
                            if (BDArmorySettings.DEBUG_MISSILES)
                            {
                                Debug.Log("[BDArmory.MissileLauncher][Terminal Guidance]: Missile heatseeker could not acquire a target lock.");
                            }
                        }
                        break;

                    case TargetingModes.Radar:

                        // pretend we have an active radar seeker for ground targets:
                        TargetSignatureData[] scannedTargets = new TargetSignatureData[5];
                        TargetSignatureData.ResetTSDArray(ref scannedTargets);
                        Ray ray = new Ray(transform.position, GetForwardTransform());

                        //RadarUtils.UpdateRadarLock(ray, maxOffBoresight, activeRadarMinThresh, ref scannedTargets, 0.4f, true, RadarWarningReceiver.RWRThreatTypes.MissileLock, true);
                        RadarUtils.RadarUpdateMissileLock(ray, maxOffBoresight, ref scannedTargets, 0.4f, this);
                        float sqrThresh = terminalGuidanceDistance * terminalGuidanceDistance * 2.25f; // (terminalGuidanceDistance * 1.5f)^2

                        //float smallestAngle = maxOffBoresight;
                        TargetSignatureData lockedTarget = TargetSignatureData.noTarget;

                        for (int i = 0; i < scannedTargets.Length; i++)
                        {
                            if (scannedTargets[i].exists && (scannedTargets[i].predictedPosition - TargetPosition).sqrMagnitude < sqrThresh)
                            {
                                //re-check engagement envelope, only lock appropriate targets
                                if (CheckTargetEngagementEnvelope(scannedTargets[i].targetInfo))
                                {
                                    lockedTarget = scannedTargets[i];
                                    ActiveRadar = true;
                                }
                            }
                        }

                        if (lockedTarget.exists)
                        {
                            radarTarget = lockedTarget;
                            TargetAcquired = true;
                            if (weaponClass == WeaponClasses.SLW)
                            {
                                TargetPosition = radarTarget.predictedPosition;
                            }
                            else
                                TargetPosition = radarTarget.predictedPositionWithChaffFactor(chaffEffectivity);
                            TargetVelocity = radarTarget.velocity;
                            TargetAcceleration = radarTarget.acceleration;
                            targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(TargetPosition, vessel.mainBody);

                            if (weaponClass == WeaponClasses.SLW)
                                RadarWarningReceiver.PingRWR(new Ray(transform.position, radarTarget.predictedPosition - transform.position), 45, RadarWarningReceiver.RWRThreatTypes.Torpedo, 2f);
                            else
                                RadarWarningReceiver.PingRWR(new Ray(transform.position, radarTarget.predictedPosition - transform.position), 45, RadarWarningReceiver.RWRThreatTypes.MissileLaunch, 2f);

                            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher][Terminal Guidance]: Pitbull! Radar missileBase has gone active.  Radar sig strength: {radarTarget.signalStrength:0.0} - target: {radarTarget.vessel.name}");
                        }
                        else
                        {
                            TargetAcquired = true;
                            TargetPosition = VectorUtils.GetWorldSurfacePostion(UpdateGPSTarget(), vessel.mainBody); //putting back the GPS target if no radar target found
                            TargetVelocity = Vector3.zero;
                            TargetAcceleration = Vector3.zero;
                            targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(TargetPosition, vessel.mainBody);
                            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileLauncher][Terminal Guidance]: Missile radar could not acquire a target lock - Defaulting to GPS Target");
                        }
                        break;

                    case TargetingModes.Laser:
                        // not very useful, currently unsupported!
                        break;

                    case TargetingModes.Gps:
                        // from gps to gps -> no actions need to be done!
                        break;

                    case TargetingModes.AntiRad:
                        TargetAcquired = true;
                        targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(TargetPosition, vessel.mainBody); // Set the GPS coordinates from the current target position.
                        SetAntiRadTargeting(); //should then already work automatically via OnReceiveRadarPing
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileLauncher][Terminal Guidance]: Antiradiation mode set! Waiting for radar signals...");
                        break;
                }
            }
        }

        void UpdateThrustForces()
        {
            if (MissileState == MissileStates.PostThrust) return;
            if (weaponClass == WeaponClasses.SLW && FlightGlobals.getAltitudeAtPos(part.transform.position) > 0) return; //#710, no torp thrust out of water
            if (currentThrust * Throttle > 0)
            {
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES) debugString.AppendLine($"Missile thrust= {currentThrust * Throttle}");
                part.rb.AddRelativeForce(currentThrust * Throttle * Vector3.forward);
            }
        }

        IEnumerator MissileRoutine()
        {
            MissileState = MissileStates.Drop;
            if (engineFailureRate > 0f)
                if (UnityEngine.Random.Range(0f, 1f) < engineFailureRate)
                {
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileLauncher]: Missile Engine Failed on Launch!");
                    yield return new WaitForSecondsFixed(2f); // Pilot reaction time
                    BDATargetManager.FiredMissiles.Remove(this);
                    yield break;
                }

            StartCoroutine(DeployAnimRoutine());
            yield return new WaitForSecondsFixed(dropTime);
            yield return StartCoroutine(BoostRoutine());

            StartCoroutine(FlightAnimRoutine());
            yield return new WaitForSecondsFixed(cruiseDelay);
            yield return StartCoroutine(CruiseRoutine());
        }

        IEnumerator DeployAnimRoutine()
        {
            yield return new WaitForSecondsFixed(deployTime);
            if (deployStates == null)
            {
                if (BDArmorySettings.DEBUG_MISSILES) Debug.LogWarning("[BDArmory.MissileLauncher]: deployStates was null, aborting AnimRoutine.");
                yield break;
            }

            if (!string.IsNullOrEmpty(deployAnimationName))
            {
                deployed = true;
                using (var anim = deployStates.AsEnumerable().GetEnumerator())
                    while (anim.MoveNext())
                    {
                        if (anim.Current == null) continue;
                        anim.Current.enabled = true;
                        anim.Current.speed = 1;
                    }
            }
        }
        IEnumerator FlightAnimRoutine()
        {
            if (animStates == null)
            {
                if (BDArmorySettings.DEBUG_MISSILES) Debug.LogWarning("[BDArmory.MissileLauncher]: animStates was null, aborting AnimRoutine.");
                yield break;
            }

            if (!string.IsNullOrEmpty(flightAnimationName))
            {
                using (var anim = animStates.AsEnumerable().GetEnumerator())
                    while (anim.MoveNext())
                    {
                        if (anim.Current == null) continue;
                        anim.Current.enabled = true;
                        if (!OneShotAnim)
                        {
                            anim.Current.wrapMode = WrapMode.Loop;
                        }
                        anim.Current.speed = 1;
                    }
            }
        }
        IEnumerator BoostRoutine()
        {
            if (weaponClass == WeaponClasses.SLW && FlightGlobals.getAltitudeAtPos(part.transform.position) > 0)
            {
                yield return new WaitUntilFixed(() => vessel == null || vessel.LandedOrSplashed);//don't start torpedo thrust until underwater
                if (vessel == null || vessel.Landed) Detonate(); //dropping torpedoes over land is just going to turn them into heavy, expensive bombs...
                dropTime = TimeIndex;
            }
            if (useFuel) burnRate = boostTime > 0 ? boosterFuelMass / boostTime * Time.fixedDeltaTime : 0;
            StartBoost();
            var wait = new WaitForFixedUpdate();
            float boostStartTime = Time.time;
            while (Time.time - boostStartTime < boostTime)
            {
                //light, sound & particle fx
                //sound
                if (!BDArmorySetup.GameIsPaused)
                {
                    if (!audioSource.isPlaying)
                    {
                        audioSource.Play();
                    }
                }
                else if (audioSource.isPlaying)
                {
                    audioSource.Stop();
                }

                //particleFx
                using (var emitter = boostEmitters.GetEnumerator())
                    while (emitter.MoveNext())
                    {
                        if (emitter.Current == null) continue;
                        if (!hasRCS)
                        {
                            emitter.Current.sizeGrow = Mathf.Lerp(emitter.Current.sizeGrow, 0, 20 * Time.deltaTime);
                        }
                    }

                using (var gpe = boostGaplessEmitters.GetEnumerator())
                    while (gpe.MoveNext())
                    {
                        if (gpe.Current == null) continue;
                        if ((!vessel.InVacuum() && Throttle > 0) && weaponClass != WeaponClasses.SLW || (weaponClass == WeaponClasses.SLW && FlightGlobals.getAltitudeAtPos(part.transform.position) < 0)) //#710
                        {
                            gpe.Current.emit = true;
                            gpe.Current.pEmitter.worldVelocity = 2 * ParticleTurbulence.flareTurbulence;
                        }
                        else
                        {
                            gpe.Current.emit = false;
                        }
                    }

                //thrust
                if (useFuel && burnRate > 0 && burnedFuelMass < boosterFuelMass)
                {
                    burnedFuelMass = Mathf.Min(burnedFuelMass + burnRate, boosterFuelMass);
                }

                if (spoolEngine)
                {
                    currentThrust = Mathf.MoveTowards(currentThrust, thrust, thrust / 10);
                }

                yield return wait;
            }
            EndBoost();
        }

        void StartBoost()
        {
            MissileState = MissileStates.Boost;

            if (audioSource == null || sfAudioSource == null) SetupAudio();
            if (boostAudio)
            {
                audioSource.clip = boostAudio;
            }
            else if (thrustAudio)
            {
                audioSource.clip = thrustAudio;
            }

            using (var light = gameObject.GetComponentsInChildren<Light>().AsEnumerable().GetEnumerator())
                while (light.MoveNext())
                {
                    if (light.Current == null) continue;
                    light.Current.intensity = 1.5f;
                }

            if (!spoolEngine)
            {
                currentThrust = thrust;
            }

            if (string.IsNullOrEmpty(boostTransformName))
            {
                boostEmitters = pEmitters;
                boostGaplessEmitters = gaplessEmitters;
            }

            using (var emitter = boostEmitters.GetEnumerator())
                while (emitter.MoveNext())
                {
                    if (emitter.Current == null) continue;
                    emitter.Current.emit = true;
                }

            if (hasRCS)
            {
                forwardRCS.emit = true;
            }

            if (!(thrust > 0)) return;
            sfAudioSource.PlayOneShot(SoundUtils.GetAudioClip("BDArmory/Sounds/launch"));
            RadarWarningReceiver.WarnMissileLaunch(transform.position, transform.forward, TargetingMode == TargetingModes.Radar);
        }

        void EndBoost()
        {
            using (var emitter = boostEmitters.GetEnumerator())
                while (emitter.MoveNext())
                {
                    if (emitter.Current == null) continue;
                    emitter.Current.emit = false;
                }

            using (var gEmitter = boostGaplessEmitters.GetEnumerator())
                while (gEmitter.MoveNext())
                {
                    if (gEmitter.Current == null) continue;
                    gEmitter.Current.emit = false;
                }

            if (useFuel) burnedFuelMass = boosterFuelMass;

            if (decoupleBoosters)
            {
                part.mass -= boosterMass;
                using (var booster = boosters.GetEnumerator())
                    while (booster.MoveNext())
                    {
                        if (booster.Current == null) continue;
                        booster.Current.AddComponent<DecoupledBooster>().DecoupleBooster(part.rb.velocity, boosterDecoupleSpeed);
                    }
            }

            if (cruiseDelay > 0)
            {
                currentThrust = 0;
            }
        }

        IEnumerator CruiseRoutine()
        {
            float massToBurn = 0;
            if (useFuel)
            {
                burnRate = cruiseTime > 0 ? cruiseFuelMass / cruiseTime * Time.fixedDeltaTime : 0;
                massToBurn = boosterFuelMass + cruiseFuelMass;
            }
            StartCruise();
            var wait = new WaitForFixedUpdate();
            float cruiseStartTime = Time.time;
            while (Time.time - cruiseStartTime < cruiseTime)
            {
                if (!BDArmorySetup.GameIsPaused)
                {
                    if (!audioSource.isPlaying || audioSource.clip != thrustAudio)
                    {
                        audioSource.clip = thrustAudio;
                        audioSource.Play();
                    }
                }
                else if (audioSource.isPlaying)
                {
                    audioSource.Stop();
                }
                audioSource.volume = Throttle;

                //particleFx
                using (var emitter = pEmitters.GetEnumerator())
                    while (emitter.MoveNext())
                    {
                        if (emitter.Current == null) continue;
                        if (!hasRCS)
                        {
                            emitter.Current.sizeGrow = Mathf.Lerp(emitter.Current.sizeGrow, 0, 20 * Time.deltaTime);
                        }

                        emitter.Current.maxSize = Mathf.Clamp01(Throttle / Mathf.Clamp((float)vessel.atmDensity, 0.2f, 1f));
                        if (weaponClass != WeaponClasses.SLW || (weaponClass == WeaponClasses.SLW && FlightGlobals.getAltitudeAtPos(part.transform.position) < 0)) //#710
                        {
                            emitter.Current.emit = true;
                        }
                        else
                        {
                            emitter.Current.emit = false; // #710, shut down thrust FX for torps out of water
                        }
                    }

                using (var gpe = gaplessEmitters.GetEnumerator())
                    while (gpe.MoveNext())
                    {
                        if (gpe.Current == null) continue;
                        if (weaponClass != WeaponClasses.SLW || (weaponClass == WeaponClasses.SLW && FlightGlobals.getAltitudeAtPos(part.transform.position) < 0)) //#710
                        {
                            gpe.Current.pEmitter.maxSize = Mathf.Clamp01(Throttle / Mathf.Clamp((float)vessel.atmDensity, 0.2f, 1f));
                            gpe.Current.emit = true;
                            gpe.Current.pEmitter.worldVelocity = 2 * ParticleTurbulence.flareTurbulence;
                        }
                        else
                        {
                            gpe.Current.emit = false;
                        }
                    }
                //Thrust
                if (useFuel && burnRate > 0 && burnedFuelMass < massToBurn)
                {
                    burnedFuelMass = Mathf.Min(burnedFuelMass + burnRate, massToBurn);
                }

                if (spoolEngine)
                {
                    currentThrust = Mathf.MoveTowards(currentThrust, cruiseThrust, cruiseThrust / 10);
                }
                yield return wait;
            }
            EndCruise();
        }

        void StartCruise()
        {
            MissileState = MissileStates.Cruise;

            if (audioSource == null) SetupAudio();
            if (thrustAudio)
            {
                audioSource.clip = thrustAudio;
            }

            currentThrust = spoolEngine ? 0 : cruiseThrust;

            using (var pEmitter = pEmitters.GetEnumerator())
                while (pEmitter.MoveNext())
                {
                    if (pEmitter.Current == null) continue;
                    EffectBehaviour.AddParticleEmitter(pEmitter.Current);
                    pEmitter.Current.emit = true;
                }

            using (var gEmitter = gaplessEmitters.GetEnumerator())
                while (gEmitter.MoveNext())
                {
                    if (gEmitter.Current == null) continue;
                    EffectBehaviour.AddParticleEmitter(gEmitter.Current.pEmitter);
                    gEmitter.Current.emit = true;
                }

            if (!hasRCS) return;
            forwardRCS.emit = false;
            audioSource.Stop();
        }

        void EndCruise()
        {
            MissileState = MissileStates.PostThrust;

            if (useFuel) burnedFuelMass = cruiseFuelMass + boosterFuelMass;

            using (IEnumerator<Light> light = gameObject.GetComponentsInChildren<Light>().AsEnumerable().GetEnumerator())
                while (light.MoveNext())
                {
                    if (light.Current == null) continue;
                    light.Current.intensity = 0;
                }

            StartCoroutine(FadeOutAudio());
            StartCoroutine(FadeOutEmitters());
        }

        IEnumerator FadeOutAudio()
        {
            if (thrustAudio && audioSource.isPlaying)
            {
                while (audioSource.volume > 0 || audioSource.pitch > 0)
                {
                    audioSource.volume = Mathf.Lerp(audioSource.volume, 0, 5 * Time.deltaTime);
                    audioSource.pitch = Mathf.Lerp(audioSource.pitch, 0, 5 * Time.deltaTime);
                    yield return null;
                }
            }
        }

        IEnumerator FadeOutEmitters()
        {
            float fadeoutStartTime = Time.time;
            while (Time.time - fadeoutStartTime < 5)
            {
                using (var pe = pEmitters.GetEnumerator())
                    while (pe.MoveNext())
                    {
                        if (pe.Current == null) continue;
                        pe.Current.maxEmission = Mathf.FloorToInt(pe.Current.maxEmission * 0.8f);
                        pe.Current.minEmission = Mathf.FloorToInt(pe.Current.minEmission * 0.8f);
                    }

                using (var gpe = gaplessEmitters.GetEnumerator())
                    while (gpe.MoveNext())
                    {
                        if (gpe.Current == null) continue;
                        gpe.Current.pEmitter.maxSize = Mathf.MoveTowards(gpe.Current.pEmitter.maxSize, 0, 0.005f);
                        gpe.Current.pEmitter.minSize = Mathf.MoveTowards(gpe.Current.pEmitter.minSize, 0, 0.008f);
                        gpe.Current.pEmitter.worldVelocity = ParticleTurbulence.Turbulence;
                    }
                yield return new WaitForFixedUpdate();
            }

            using (var pe2 = pEmitters.GetEnumerator())
                while (pe2.MoveNext())
                {
                    if (pe2.Current == null) continue;
                    pe2.Current.emit = false;
                }

            using (var gpe2 = gaplessEmitters.GetEnumerator())
                while (gpe2.MoveNext())
                {
                    if (gpe2.Current == null) continue;
                    gpe2.Current.emit = false;
                }
        }

        [KSPField]
        public float beamCorrectionFactor;

        [KSPField]
        public float beamCorrectionDamping;

        Ray previousBeam;

        void BeamRideGuidance()
        {
            if (!targetingPod)
            {
                guidanceActive = false;
                return;
            }

            if (RadarUtils.TerrainCheck(targetingPod.cameraParentTransform.position, transform.position))
            {
                guidanceActive = false;
                return;
            }
            Ray laserBeam = new Ray(targetingPod.cameraParentTransform.position + (targetingPod.vessel.Velocity() * Time.fixedDeltaTime), targetingPod.targetPointPosition - targetingPod.cameraParentTransform.position);
            Vector3 target = MissileGuidance.GetBeamRideTarget(laserBeam, part.transform.position, vessel.Velocity(), beamCorrectionFactor, beamCorrectionDamping, (TimeIndex > 0.25f ? previousBeam : laserBeam));
            previousBeam = laserBeam;
            DrawDebugLine(part.transform.position, target);
            DoAero(target);
        }

        void CruiseGuidance()
        {
            if (this._guidance == null)
            {
                this._guidance = new CruiseGuidance(this);
            }

            Vector3 cruiseTarget = Vector3.zero;

            cruiseTarget = this._guidance.GetDirection(this, TargetPosition, TargetVelocity);

            Vector3 upDirection = VectorUtils.GetUpDirection(transform.position);

            //axial rotation
            if (rotationTransform)
            {
                Quaternion originalRotation = transform.rotation;
                Quaternion originalRTrotation = rotationTransform.rotation;
                transform.rotation = Quaternion.LookRotation(transform.forward, upDirection);
                rotationTransform.rotation = originalRTrotation;
                Vector3 lookUpDirection = (cruiseTarget - transform.position).ProjectOnPlanePreNormalized(transform.forward) * 100;
                lookUpDirection = transform.InverseTransformPoint(lookUpDirection + transform.position);

                lookUpDirection = new Vector3(lookUpDirection.x, 0, 0);
                lookUpDirection += 10 * Vector3.up;

                rotationTransform.localRotation = Quaternion.Lerp(rotationTransform.localRotation, Quaternion.LookRotation(Vector3.forward, lookUpDirection), 0.04f);
                Quaternion finalRotation = rotationTransform.rotation;
                transform.rotation = originalRotation;
                rotationTransform.rotation = finalRotation;

                vesselReferenceTransform.rotation = Quaternion.LookRotation(-rotationTransform.up, rotationTransform.forward);
            }
            DoAero(cruiseTarget);
            CheckMiss();
        }

        void AAMGuidance()
        {
            Vector3 aamTarget;
            if (TargetAcquired)
            {
                if (warheadType == WarheadTypes.ContinuousRod) //Have CR missiles target slightly above target to ensure craft caught in planar blast AOE
                {
                    TargetPosition += VectorUtils.GetUpDirection(TargetPosition) * (blastRadius > 0 ? (DetonationDistance / 3) : 5);
                    //TargetPosition += VectorUtils.GetUpDirection(TargetPosition) * (blastRadius < 10? (blastRadius / 2) : 10);
                }
                DrawDebugLine(transform.position + (part.rb.velocity * Time.fixedDeltaTime), TargetPosition);

                float timeToImpact;
                if (GuidanceMode == GuidanceModes.APN) // Augmented Pro-Nav
                    aamTarget = MissileGuidance.GetAPNTarget(TargetPosition, TargetVelocity, TargetAcceleration, vessel, pronavGain, out timeToImpact);
                else if (GuidanceMode == GuidanceModes.PN) // Pro-Nav
                    aamTarget = MissileGuidance.GetPNTarget(TargetPosition, TargetVelocity, vessel, pronavGain, out timeToImpact);
                else if (GuidanceMode == GuidanceModes.AAMLoft)
                {
                    float targetAlt = FlightGlobals.getAltitudeAtPos(TargetPosition);

                    if (TimeToImpact == float.PositiveInfinity)
                    {
                        if (!vessel.InVacuum() && (vessel.altitude >= LoftMinAltitude) && ((vessel.altitude - targetAlt <= LoftAltitudeAdvMax) || (TargetPosition - vessel.transform.position).sqrMagnitude > (LoftRangeOverride * LoftRangeOverride))) loftState = 0;
                        else loftState = 3;
                    }

                    //aamTarget = MissileGuidance.GetAirToAirLoftTarget(TargetPosition, TargetVelocity, TargetAcceleration, vessel, targetAlt, LoftMaxAltitude, LoftRangeFac, LoftAltComp, LoftVelComp, LoftAngle, LoftTermAngle, LoftTermRange, ref loftState, out float currTimeToImpact, out float rangeToTarget, optimumAirspeed);
                    aamTarget = MissileGuidance.GetAirToAirLoftTarget(TargetPosition, TargetVelocity, TargetAcceleration, vessel, targetAlt, LoftMaxAltitude, LoftRangeFac, LoftVertVelComp, LoftVelComp, LoftAngle, LoftTermAngle, LoftTermRange, ref loftState, out float currTimeToImpact, out float rangeToTarget, LoftUseAPN, pronavGain, optimumAirspeed);

                    float fac = (1 - (rangeToTarget - LoftTermRange) / Mathf.Clamp(LoftTermRange * 4f, 5000f, 25000f));

                    maxAoA = Mathf.Clamp(initMaxAoA * fac, 4f, initMaxAoA);

                    TimeToImpact = currTimeToImpact;

                    /*if (prevTimeToImpact - TimeToImpact < -0.1)
                    {
                        loftTerminal = true;
                    }*/

                    //if (prevTimeToImpact != float.PositiveInfinity && (TimeToImpact - prevTimeToImpact) > 0) loftTimeToGoGain += (TimeToImpact - prevTimeToImpact);

                    /*if (loftTimeToGoGain > LoftThreshold && !loftTerminal)
                    {
                        maxAoA *= 4;
                        loftTerminal = true;
                    }*/

                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher]: AAM Loft TTGO: [{TimeToImpact:G3}]. Currently State: {loftState}. Fly to: [{aamTarget}]. Target Position: [{TargetPosition}]. Max AoA: [{maxAoA:G3}]");
                }
                else if (GuidanceMode == GuidanceModes.AAMPure)
                {
                    TimeToImpact = Vector3.Distance(TargetPosition, transform.position) / Mathf.Max((float)vessel.srfSpeed, optimumAirspeed);
                    aamTarget = TargetPosition;
                }
                else// AAM Lead
                    aamTarget = MissileGuidance.GetAirToAirTarget(TargetPosition, TargetVelocity, TargetAcceleration, vessel, out timeToImpact, optimumAirspeed);


                if (Vector3.Angle(aamTarget - transform.position, transform.forward) > maxOffBoresight * 0.75f)
                {
                    aamTarget = TargetPosition;
                }

                //proxy detonation
                var distThreshold = 0.5f * GetBlastRadius();
                if (proxyDetonate && !DetonateAtMinimumDistance && ((TargetPosition + (TargetVelocity * Time.fixedDeltaTime)) - (transform.position)).sqrMagnitude < distThreshold * distThreshold)
                {
                    //part.Destroy(); //^look into how this interacts with MissileBase.DetonationState
                    // - if the missile is still within the notSafe status, the missile will delete itself, else, the checkProximity state of DetpnationState would trigger before the missile reaches the 1/2 blastradius.
                    // would only trigger if someone set the detonation distance override to something smallerthan 1/2 blst radius, for some reason
                    Detonate();
                }
            }
            else
            {
                aamTarget = transform.position + (20 * vessel.Velocity().normalized);
            }

            if (TimeIndex > dropTime + 0.25f)
            {
                DoAero(aamTarget);
                CheckMiss();
            }

        }

        void AGMGuidance()
        {
            if (TargetingMode != TargetingModes.Gps)
            {
                if (TargetAcquired)
                {
                    //lose lock if seeker reaches gimbal limit
                    float targetViewAngle = Vector3.Angle(transform.forward, TargetPosition - transform.position);

                    if (targetViewAngle > maxOffBoresight)
                    {
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileLauncher]: AGM Missile guidance failed - target out of view");
                        guidanceActive = false;
                    }
                    CheckMiss();
                }
                else
                {
                    if (TargetingMode == TargetingModes.Laser)
                    {
                        //keep going straight until found laser point
                        TargetPosition = laserStartPosition + (20000 * startDirection);
                    }
                }
            }

            Vector3 agmTarget = MissileGuidance.GetAirToGroundTarget(TargetPosition, TargetVelocity, vessel, agmDescentRatio);
            DoAero(agmTarget);
        }

        void SLWGuidance()
        {
            Vector3 SLWTarget;
            if (TargetAcquired)
            {
                DrawDebugLine(transform.position + (part.rb.velocity * Time.fixedDeltaTime), TargetPosition);
                float timeToImpact;
                SLWTarget = MissileGuidance.GetAirToAirTarget(TargetPosition, TargetVelocity, TargetAcceleration, vessel, out timeToImpact, optimumAirspeed);
                TimeToImpact = timeToImpact;
                if (Vector3.Angle(SLWTarget - transform.position, transform.forward) > maxOffBoresight * 0.75f)
                {
                    SLWTarget = TargetPosition;
                }

                //proxy detonation
                var distThreshold = 0.5f * GetBlastRadius();
                if (proxyDetonate && !DetonateAtMinimumDistance && ((TargetPosition + (TargetVelocity * Time.fixedDeltaTime)) - (transform.position)).sqrMagnitude < distThreshold * distThreshold)
                {
                    Detonate(); //ends up the same as part.Destroy, except it doesn't trip the hasDied flag for clustermissiles
                }
            }
            else
            {
                SLWTarget = transform.position + (20 * vessel.Velocity().normalized);
            }

            if (TimeIndex > dropTime + 0.25f)
            {
                DoAero(SLWTarget);
            }

            if (SLWTarget.y > 0f) SLWTarget.y = getSWLWOffset;

            CheckMiss();

        }

        void DoAero(Vector3 targetPosition)
        {
            aeroTorque = MissileGuidance.DoAeroForces(this, targetPosition, liftArea, controlAuthority * steerMult, aeroTorque, finalMaxTorque, maxAoA);
        }

        void AGMBallisticGuidance()
        {
            DoAero(CalculateAGMBallisticGuidance(this, TargetPosition));
        }

        public override void Detonate()
        {
            if (HasExploded || FuseFailed || !HasFired) return;

            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileLauncher]: Detonate Triggered");

            BDArmorySetup.numberOfParticleEmitters--;
            HasExploded = true;
            /*
            if (targetVessel != null)
            {
                using (var wpm = VesselModuleRegistry.GetModules<MissileFire>(targetVessel).GetEnumerator())
                    while (wpm.MoveNext())
                    {
                        if (wpm.Current == null) continue;
                        wpm.Current.missileIsIncoming = false; //handled by attacked vessel
                    }
            }
            */
            if (SourceVessel == null) SourceVessel = vessel;
            if (multiLauncher && multiLauncher.isClusterMissile)
            {
                if (!HasDied)
                {
                    if (fairings.Count > 0)
                    {
                        using (var fairing = fairings.GetEnumerator())
                            while (fairing.MoveNext())
                            {
                                if (fairing.Current == null) continue;
                                fairing.Current.AddComponent<DecoupledBooster>().DecoupleBooster(part.rb.velocity, boosterDecoupleSpeed);
                            }
                    }
                    reloadableRail.MissileName = multiLauncher.subMunitionName;
                    multiLauncher.Team = Team;
                    multiLauncher.fireMissile(true);
                }
            }
            else
            {
                if (warheadType == WarheadTypes.Standard || warheadType == WarheadTypes.ContinuousRod)
                {
                    var tnt = part.FindModuleImplementing<BDExplosivePart>();
                    tnt.sourcevessel = SourceVessel;
                    tnt.DetonateIfPossible();
                    FuseFailed = tnt.fuseFailed;
                    guidanceActive = false;
                    if (FuseFailed)
                        HasExploded = false;
                }
                else if (warheadType == WarheadTypes.Nuke)
                {
                    var U235 = part.FindModuleImplementing<BDModuleNuke>();
                    U235.Detonate();
                }
                else // EMP/really ond legacy missiles using BlastPower
                {
                    Vector3 position = transform.position;//+rigidbody.velocity*Time.fixedDeltaTime;

                    ExplosionFx.CreateExplosion(position, blastPower, explModelPath, explSoundPath, ExplosionSourceType.Missile, 0, part, SourceVessel.vesselName, GetShortName(), default(Vector3), -1, false, part.mass * 1000);
                }
                if (part != null && !FuseFailed)
                {
                    DestroyMissile(); //splitting this off to a separate function so the clustermissile MultimissileLaunch can call it when the MML launch ienumerator is done
                }
            }

            using (var e = gaplessEmitters.GetEnumerator())
                while (e.MoveNext())
                {
                    if (e.Current == null) continue;
                    e.Current.gameObject.AddComponent<BDAParticleSelfDestruct>();
                    e.Current.transform.parent = null;
                }
            using (IEnumerator<Light> light = gameObject.GetComponentsInChildren<Light>().AsEnumerable().GetEnumerator())
                while (light.MoveNext())
                {
                    if (light.Current == null) continue;
                    light.Current.intensity = 0;
                }
        }

        public void DestroyMissile()
        {
            part.Destroy();
            part.explode();
        }

        public override Vector3 GetForwardTransform()
        {
            if (multiLauncher && multiLauncher.overrideReferenceTransform)
                return vessel.ReferenceTransform.up;
            else
                return MissileReferenceTransform.forward;
        }

        protected override void PartDie(Part p)
        {
            if (p == part)
            {
                HasDied = true;
                Detonate();
                BDATargetManager.FiredMissiles.Remove(this);
                GameEvents.onPartDie.Remove(PartDie);
            }
        }

        public static bool CheckIfMissile(Part p)
        {
            return p.GetComponent<MissileLauncher>();
        }

        void WarnTarget()
        {
            if (targetVessel == null) return;
            var wpm = VesselModuleRegistry.GetMissileFire(targetVessel.Vessel, true);
            if (wpm != null) wpm.MissileWarning(Vector3.Distance(transform.position, targetVessel.transform.position), this);
        }

        void SetupRCS()
        {
            rcsFiredTimes = new float[] { 0, 0, 0, 0 };
            rcsTransforms = new KSPParticleEmitter[] { upRCS, leftRCS, rightRCS, downRCS };
        }

        void DoRCS()
        {
            try
            {
                Vector3 relV = TargetVelocity - vessel.obt_velocity;

                for (int i = 0; i < 4; i++)
                {
                    //float giveThrust = Mathf.Clamp(-localRelV.z, 0, rcsThrust);
                    float giveThrust = Mathf.Clamp(Vector3.Project(relV, rcsTransforms[i].transform.forward).magnitude * -Mathf.Sign(Vector3.Dot(rcsTransforms[i].transform.forward, relV)), 0, rcsThrust);
                    part.rb.AddForce(-giveThrust * rcsTransforms[i].transform.forward);

                    if (giveThrust > rcsRVelThreshold)
                    {
                        rcsAudioMinInterval = UnityEngine.Random.Range(0.15f, 0.25f);
                        if (Time.time - rcsFiredTimes[i] > rcsAudioMinInterval)
                        {
                            if (sfAudioSource == null) SetupAudio();
                            sfAudioSource.PlayOneShot(SoundUtils.GetAudioClip("BDArmory/Sounds/popThrust"));
                            rcsTransforms[i].emit = true;
                            rcsFiredTimes[i] = Time.time;
                        }
                    }
                    else
                    {
                        rcsTransforms[i].emit = false;
                    }

                    //turn off emit
                    if (Time.time - rcsFiredTimes[i] > rcsAudioMinInterval * 0.75f)
                    {
                        rcsTransforms[i].emit = false;
                    }
                }
            }
            catch (Exception e)
            {

                Debug.LogError("[BDArmory.MissileLauncher]: DEBUG " + e.Message);
                try { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG null part?: " + (part == null)); } catch (Exception e2) { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG part: " + e2.Message); }
                try { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG null part.rb?: " + (part.rb == null)); } catch (Exception e2) { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG part.rb: " + e2.Message); }
                try { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG null vessel?: " + (vessel == null)); } catch (Exception e2) { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG vessel: " + e2.Message); }
                try { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG null sfAudioSource?: " + (sfAudioSource == null)); } catch (Exception e2) { Debug.LogWarning("[BDArmory.MissileLauncher]: sfAudioSource: " + e2.Message); }
                try { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG null rcsTransforms?: " + (rcsTransforms == null)); } catch (Exception e2) { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG rcsTransforms: " + e2.Message); }
                if (rcsTransforms != null)
                {
                    for (int i = 0; i < 4; ++i)
                        try { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG null rcsTransforms[" + i + "]?: " + (rcsTransforms[i] == null)); } catch (Exception e2) { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG rcsTransforms[" + i + "]: " + e2.Message); }
                }
                try { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG null rcsFiredTimes?: " + (rcsFiredTimes == null)); } catch (Exception e2) { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG rcsFiredTimes: " + e2.Message); }
                throw; // Re-throw the exception so behaviour is unchanged so we see it.
            }
        }

        public void KillRCS()
        {
            if (upRCS) upRCS.emit = false;
            if (downRCS) downRCS.emit = false;
            if (leftRCS) leftRCS.emit = false;
            if (rightRCS) rightRCS.emit = false;
        }

        protected override void OnGUI()
        {
            base.OnGUI();
            if (HighLogic.LoadedSceneIsFlight)
            {
                try
                {
                    drawLabels();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[BDArmory.MissileLauncher]: Exception thrown in OnGUI: " + e.Message + "\n" + e.StackTrace);
                }
            }
        }

        void AntiSpin()
        {
            part.rb.angularDrag = 0;
            part.angularDrag = 0;
            Vector3 spin = Vector3.Project(part.rb.angularVelocity, part.rb.transform.forward);// * 8 * Time.fixedDeltaTime;
            part.rb.angularVelocity -= spin;
            //rigidbody.maxAngularVelocity = 7;

            if (guidanceActive)
            {
                part.rb.angularVelocity -= 0.6f * part.rb.angularVelocity;
            }
            else
            {
                part.rb.angularVelocity -= 0.02f * part.rb.angularVelocity;
            }
        }

        void SimpleDrag()
        {
            part.dragModel = Part.DragModel.NONE;
            if (part.rb == null || part.rb.mass == 0) return;
            //float simSpeedSquared = (float)vessel.Velocity.sqrMagnitude;
            float simSpeedSquared = (part.rb.GetPointVelocity(part.transform.TransformPoint(simpleCoD)) + (Vector3)Krakensbane.GetFrameVelocity()).sqrMagnitude;
            Vector3 currPos = transform.position;
            float drag = deployed ? deployedDrag : simpleDrag;
            float dragMagnitude = (0.008f * part.rb.mass) * drag * 0.5f * simSpeedSquared * (float)FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(currPos), FlightGlobals.getExternalTemperature(), FlightGlobals.currentMainBody);
            Vector3 dragForce = dragMagnitude * vessel.Velocity().normalized;
            part.rb.AddForceAtPosition(-dragForce, transform.TransformPoint(simpleCoD));

            Vector3 torqueAxis = -Vector3.Cross(vessel.Velocity(), part.transform.forward).normalized;
            float AoA = Vector3.Angle(part.transform.forward, vessel.Velocity());
            AoA /= 20;
            part.rb.AddTorque(AoA * simpleStableTorque * dragMagnitude * torqueAxis);
        }

        void ParseAntiRadTargetTypes()
        {
            antiradTargets = OtherUtils.ParseToFloatArray(antiradTargetTypes);
        }

        void ParseModes()
        {
            homingType = homingType.ToLower();
            switch (homingType)
            {
                case "aam":
                    GuidanceMode = GuidanceModes.AAMLead;
                    break;

                case "aamlead":
                    GuidanceMode = GuidanceModes.AAMLead;
                    break;

                case "aampure":
                    GuidanceMode = GuidanceModes.AAMPure;
                    break;
                case "aamloft":
                    GuidanceMode = GuidanceModes.AAMLoft;
                    break;
                case "agm":
                    GuidanceMode = GuidanceModes.AGM;
                    break;

                case "agmballistic":
                    GuidanceMode = GuidanceModes.AGMBallistic;
                    break;

                case "cruise":
                    GuidanceMode = GuidanceModes.Cruise;
                    break;

                case "sts":
                    GuidanceMode = GuidanceModes.STS;
                    break;

                case "rcs":
                    GuidanceMode = GuidanceModes.RCS;
                    break;

                case "beamriding":
                    GuidanceMode = GuidanceModes.BeamRiding;
                    break;

                case "slw":
                    GuidanceMode = GuidanceModes.SLW;
                    break;

                case "pronav":
                    GuidanceMode = GuidanceModes.PN;
                    break;

                case "augpronav":
                    GuidanceMode = GuidanceModes.APN;
                    break;

                default:
                    GuidanceMode = GuidanceModes.None;
                    break;
            }

            targetingType = targetingType.ToLower();
            switch (targetingType)
            {
                case "radar":
                    TargetingMode = TargetingModes.Radar;
                    break;

                case "heat":
                    TargetingMode = TargetingModes.Heat;
                    break;

                case "laser":
                    TargetingMode = TargetingModes.Laser;
                    break;

                case "gps":
                    TargetingMode = TargetingModes.Gps;
                    maxOffBoresight = 360;
                    break;

                case "antirad":
                    TargetingMode = TargetingModes.AntiRad;
                    break;

                default:
                    TargetingMode = TargetingModes.None;
                    break;
            }

            terminalGuidanceType = terminalGuidanceType.ToLower();
            switch (terminalGuidanceType)
            {
                case "radar":
                    TargetingModeTerminal = TargetingModes.Radar;
                    break;

                case "heat":
                    TargetingModeTerminal = TargetingModes.Heat;
                    break;

                case "laser":
                    TargetingModeTerminal = TargetingModes.Laser;
                    break;

                case "gps":
                    TargetingModeTerminal = TargetingModes.Gps;
                    maxOffBoresight = 360;
                    break;

                case "antirad":
                    TargetingModeTerminal = TargetingModes.AntiRad;
                    break;

                default:
                    TargetingModeTerminal = TargetingModes.None;
                    break;
            }
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher]: parsing guidance and homing complete on {GetPartName()}");
        }

        private string GetBrevityCode()
        {
            //torpedo: determine subtype
            if (missileType.ToLower() == "torpedo")
            {
                if ((TargetingMode == TargetingModes.Radar) && (activeRadarRange > 0))
                    return "Active Sonar";

                if ((TargetingMode == TargetingModes.Radar) && (activeRadarRange <= 0))
                    return "Passive Sonar";

                if ((TargetingMode == TargetingModes.Laser) || (TargetingMode == TargetingModes.Gps))
                    return "Optical/wireguided";

                if ((TargetingMode == TargetingModes.Heat))
                    return "Heat guided";

                if ((TargetingMode == TargetingModes.None))
                    return "Unguided";
            }

            if (missileType.ToLower() == "bomb")
            {
                if ((TargetingMode == TargetingModes.Laser) || (TargetingMode == TargetingModes.Gps))
                    return "JDAM";

                if ((TargetingMode == TargetingModes.None))
                    return "Unguided";
            }

            //else: missiles:

            if (TargetingMode == TargetingModes.Radar)
            {
                //radar: determine subtype
                if (activeRadarRange <= 0)
                    return "SARH";
                if (activeRadarRange > 0 && activeRadarRange < maxStaticLaunchRange)
                    return "Mixed SARH/F&F";
                if (activeRadarRange >= maxStaticLaunchRange)
                    return "Fire&Forget";
            }

            if (TargetingMode == TargetingModes.AntiRad)
                return "Fire&Forget";

            if (TargetingMode == TargetingModes.Heat)
                return "Fire&Forget";

            if (TargetingMode == TargetingModes.Laser)
                return "SALH";

            if (TargetingMode == TargetingModes.Gps)
            {
                return TargetingModeTerminal != TargetingModes.None ? "GPS/Terminal" : "GPS";
            }

            if (TargetingMode == TargetingModes.None)
            {
                return TargetingModeTerminal != TargetingModes.None ? "Inertial/Terminal" : "Unguided";
            }
            // default:
            return "Unguided";
        }

        // RMB info in editor
        public override string GetInfo()
        {
            ParseModes();

            StringBuilder output = new StringBuilder();
            output.AppendLine($"{missileType.ToUpper()} - {GetBrevityCode()}");
            output.Append(Environment.NewLine);
            output.AppendLine($"Targeting Type: {targetingType.ToLower()}");
            output.AppendLine($"Guidance Mode: {homingType.ToLower()}");
            if (missileRadarCrossSection != RadarUtils.RCS_MISSILES)
            {
                output.AppendLine($"Detectable cross section: {missileRadarCrossSection} m^2");
            }
            output.AppendLine($"Min Range: {minStaticLaunchRange} m");
            output.AppendLine($"Max Range: {maxStaticLaunchRange} m");

            if (useFuel && weaponClass == WeaponClasses.Missile)
            {
                double dV = Math.Round(GetDeltaV(), 1);
                if (dV > 0) output.AppendLine($"Total DeltaV: {dV} m/s");
            }

            if (TargetingMode == TargetingModes.Radar)
            {
                if (activeRadarRange > 0)
                {
                    output.AppendLine($"Active Radar Range: {activeRadarRange} m");
                    if (activeRadarLockTrackCurve.maxTime > 0)
                        output.AppendLine($"- Lock/Track: {activeRadarLockTrackCurve.Evaluate(activeRadarLockTrackCurve.maxTime)} m^2 @ {activeRadarLockTrackCurve.maxTime} km");
                    else
                        output.AppendLine($"- Lock/Track: {RadarUtils.MISSILE_DEFAULT_LOCKABLE_RCS} m^2 @ {activeRadarRange / 1000} km");
                    output.AppendLine($"- LOAL: {radarLOAL}");
                    if (radarLOAL) output.AppendLine($"  - Max Radar Search Time: {radarTimeout}");
                }
                output.AppendLine($"Max Offborsight: {maxOffBoresight}");
                output.AppendLine($"Locked FOV: {lockedSensorFOV}");
            }

            if (TargetingMode == TargetingModes.Heat)
            {
                output.AppendLine($"Uncaged Lock: {uncagedLock}");
                output.AppendLine($"Min Heat threshold: {heatThreshold}");
                output.AppendLine($"Max Offborsight: {maxOffBoresight}");
                output.AppendLine($"Locked FOV: {lockedSensorFOV}");
            }

            if (TargetingMode == TargetingModes.Gps)
            {
                output.AppendLine($"Terminal Maneuvering: {terminalManeuvering}");
                if (terminalGuidanceType != "")
                {
                    output.AppendLine($"Terminal guidance: {terminalGuidanceType} @ distance: {terminalGuidanceDistance} m");

                    if (TargetingModeTerminal == TargetingModes.Radar)
                    {
                        output.AppendLine($"Active Radar Range: {activeRadarRange} m");
                        if (activeRadarLockTrackCurve.maxTime > 0)
                            output.AppendLine($"- Lock/Track: {activeRadarLockTrackCurve.Evaluate(activeRadarLockTrackCurve.maxTime)} m^2 @ {activeRadarLockTrackCurve.maxTime} km");
                        else
                            output.AppendLine($"- Lock/Track: {RadarUtils.MISSILE_DEFAULT_LOCKABLE_RCS} m^2 @ {activeRadarRange / 1000} km");
                        output.AppendLine($"- LOAL: {radarLOAL}");
                        if (radarLOAL) output.AppendLine($"  - Radar Search Time: {radarTimeout}");
                        output.AppendLine($"Max Offborsight: {maxOffBoresight}");
                        output.AppendLine($"Locked FOV: {lockedSensorFOV}");
                    }

                    if (TargetingModeTerminal == TargetingModes.Heat)
                    {
                        output.AppendLine($"Uncaged Lock: {uncagedLock}");
                        output.AppendLine($"Min Heat threshold: {heatThreshold}");
                        output.AppendLine($"Max Offborsight: {maxOffBoresight}");
                        output.AppendLine($"Locked FOV: {lockedSensorFOV}");
                    }
                }
            }

            if (TargetingMode == TargetingModes.None)
            {
                output.AppendLine($"Terminal Maneuvering: {terminalManeuvering}");
                if (terminalGuidanceType != "")
                {
                    output.AppendLine($"Terminal guidance: {terminalGuidanceType} @ distance: {terminalGuidanceDistance} m");

                    if (TargetingModeTerminal == TargetingModes.Radar)
                    {
                        output.AppendLine($"Active Radar Range: {activeRadarRange} m");
                        if (activeRadarLockTrackCurve.maxTime > 0)
                            output.AppendLine($"- Lock/Track: {activeRadarLockTrackCurve.Evaluate(activeRadarLockTrackCurve.maxTime)} m^2 @ {activeRadarLockTrackCurve.maxTime} km");
                        else
                            output.AppendLine($"- Lock/Track: {RadarUtils.MISSILE_DEFAULT_LOCKABLE_RCS} m^2 @ {activeRadarRange / 1000} km");
                        output.AppendLine($"- LOAL: {radarLOAL}");
                        if (radarLOAL) output.AppendLine($"  - Radar Search Time: {radarTimeout}");
                        output.AppendLine($"Max Offborsight: {maxOffBoresight}");
                        output.AppendLine($"Locked FOV: {lockedSensorFOV}");
                    }

                    if (TargetingModeTerminal == TargetingModes.Heat)
                    {
                        output.AppendLine($"Uncaged Lock: {uncagedLock}");
                        output.AppendLine($"Min Heat threshold: {heatThreshold}");
                        output.AppendLine($"Max Offborsight: {maxOffBoresight}");
                        output.AppendLine($"Locked FOV: {lockedSensorFOV}");
                    }
                }
            }

            IEnumerator<PartModule> partModules = part.Modules.GetEnumerator();
            output.AppendLine($"Warhead:");
            while (partModules.MoveNext())
            {
                if (partModules.Current == null) continue;
                if (partModules.Current.moduleName == "MultiMissileLauncher")
                {
                    if (((MultiMissileLauncher)partModules.Current).isClusterMissile)
                    {
                        output.AppendLine($"Cluster Missile:");
                        output.AppendLine($"- SubMunition Count: {((MultiMissileLauncher)partModules.Current).salvoSize} ");
                        float tntMass = ((MultiMissileLauncher)partModules.Current).tntMass;
                        output.AppendLine($"- Blast radius: {Math.Round(BlastPhysicsUtils.CalculateBlastRange(tntMass), 2)} m");
                        output.AppendLine($"- tnt Mass: {tntMass} kg");
                    }
                    if (((MultiMissileLauncher)partModules.Current).isMultiLauncher) continue;
                }
                if (partModules.Current.moduleName == "BDExplosivePart")
                {
                    ((BDExplosivePart)partModules.Current).ParseWarheadType();
                    if (clusterbomb > 1)
                    {
                        output.AppendLine($"Cluster Bomb:");
                        output.AppendLine($"- Sub-Munition Count: {clusterbomb} ");
                    }
                    float tntMass = ((BDExplosivePart)partModules.Current).tntMass;
                    output.AppendLine($"- Blast radius: {Math.Round(BlastPhysicsUtils.CalculateBlastRange(tntMass), 2)} m");
                    output.AppendLine($"- tnt Mass: {tntMass} kg");
                    output.AppendLine($"- {((BDExplosivePart)partModules.Current).warheadReportingName} warhead");
                }
                if (partModules.Current.moduleName == "ModuleEMP")
                {
                    float proximity = ((ModuleEMP)partModules.Current).proximity;
                    output.AppendLine($"- EMP Blast Radius: {proximity} m");
                }
                if (partModules.Current.moduleName == "BDModuleNuke")
                {
                    float yield = ((BDModuleNuke)partModules.Current).yield;
                    float radius = ((BDModuleNuke)partModules.Current).thermalRadius;
                    float EMPRadius = ((BDModuleNuke)partModules.Current).isEMP ? BDAMath.Sqrt(yield) * 500 : -1;
                    output.AppendLine($"- Yield: {yield} kT");
                    output.AppendLine($"- Max radius: {radius} m");
                    if (EMPRadius > 0) output.AppendLine($"- EMP Blast Radius: {EMPRadius} m");
                }
                else continue;
                break;
            }
            partModules.Dispose();

            return output.ToString();
        }

        #region ExhaustPrefabPooling
        static Dictionary<string, ObjectPool> exhaustPrefabPool = new Dictionary<string, ObjectPool>();
        List<GameObject> exhaustPrefabs = new List<GameObject>();

        static void AttachExhaustPrefab(string prefabPath, MissileLauncher missileLauncher, Transform exhaustTransform)
        {
            CreateExhaustPool(prefabPath);
            var exhaustPrefab = exhaustPrefabPool[prefabPath].GetPooledObject();
            exhaustPrefab.SetActive(true);
            using (var emitter = exhaustPrefab.GetComponentsInChildren<KSPParticleEmitter>().AsEnumerable().GetEnumerator())
                while (emitter.MoveNext())
                {
                    if (emitter.Current == null) continue;
                    emitter.Current.emit = false;
                }
            exhaustPrefab.transform.parent = exhaustTransform;
            exhaustPrefab.transform.localPosition = Vector3.zero;
            exhaustPrefab.transform.localRotation = Quaternion.identity;
            missileLauncher.exhaustPrefabs.Add(exhaustPrefab);
            missileLauncher.part.OnJustAboutToDie += missileLauncher.DetachExhaustPrefabs;
            missileLauncher.part.OnJustAboutToBeDestroyed += missileLauncher.DetachExhaustPrefabs;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileLauncher]: Exhaust prefab " + exhaustPrefab.name + " added to " + missileLauncher.shortName + " on " + (missileLauncher.vessel != null ? missileLauncher.vessel.vesselName : "unknown"));
        }

        static void CreateExhaustPool(string prefabPath)
        {
            if (exhaustPrefabPool == null)
            { exhaustPrefabPool = new Dictionary<string, ObjectPool>(); }
            if (!exhaustPrefabPool.ContainsKey(prefabPath) || exhaustPrefabPool[prefabPath] == null || exhaustPrefabPool[prefabPath].poolObject == null)
            {
                var exhaustPrefabTemplate = GameDatabase.Instance.GetModel(prefabPath);
                exhaustPrefabTemplate.SetActive(false);
                exhaustPrefabPool[prefabPath] = ObjectPool.CreateObjectPool(exhaustPrefabTemplate, 1, true, true);
            }
        }

        void DetachExhaustPrefabs()
        {
            if (part != null)
            {
                part.OnJustAboutToDie -= DetachExhaustPrefabs;
                part.OnJustAboutToBeDestroyed -= DetachExhaustPrefabs;
            }
            foreach (var exhaustPrefab in exhaustPrefabs)
            {
                if (exhaustPrefab == null) continue;
                exhaustPrefab.transform.parent = null;
                exhaustPrefab.SetActive(false);
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileLauncher]: Exhaust prefab " + exhaustPrefab.name + " removed from " + shortName + " on " + (vessel != null ? vessel.vesselName : "unknown"));
            }
            exhaustPrefabs.Clear();
        }
        #endregion
        public double GetDeltaV()
        {
            double specificImpulse;
            double deltaV;
            double massFlowRate;

            massFlowRate = (boostTime == 0) ? 0 : boosterFuelMass / boostTime;
            specificImpulse = (massFlowRate == 0) ? 0 : thrust / (massFlowRate * 9.81);
            deltaV = specificImpulse * 9.81 * Math.Log(part.mass / (part.mass - boosterFuelMass));

            double mass = part.mass;
            massFlowRate = (cruiseTime == 0) ? 0 : cruiseFuelMass / cruiseTime;
            if (boosterFuelMass > 0) mass -= boosterFuelMass;
            specificImpulse = (massFlowRate == 0) ? 0 : cruiseThrust / (massFlowRate * 9.81);
            deltaV += (specificImpulse * 9.81 * Math.Log(mass / (mass - cruiseFuelMass)));

            return deltaV;
        }
    }
}
