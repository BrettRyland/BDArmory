using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        public float guidanceDelay = -1;

        [KSPField]
        public float pronavGain = 3f;

        [KSPField]
        public float gLimit = -1;

        [KSPField]
        public float gMargin = -1;

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
        public float dragArea = -1f; // Optional parameter to specify separate drag reference area, otherwise defaults to liftArea

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
        public float cruiseRangeTrigger = -1;

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
        public string terminalGuidanceType = "";

        [KSPField]
        public bool dumbTerminalGuidance = true;

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

        public bool useSimpleDragTemp = false;

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
        bool boostersDecoupled = false;

        [KSPField]
        public float boosterDecoupleSpeed = 5;

        [KSPField]
        public float boosterMass = 0; // The booster mass (dry mass if using fuel, wet otherwise)

        //Fuel Weight variables
        [KSPField]
        public float boosterFuelMass = 0; // The mass of the booster fuel (separate from the booster mass)

        [KSPField]
        public float cruiseFuelMass = 0; // The mass of the cruise fuel

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

        // Loft Options
        [KSPField]
        public string terminalHomingType = "pronav";

        [KSPField]
        public float LoftTermRange = -1;

        public GPSTargetInfo designatedGPSInfo;

        float[] rcsFiredTimes;
        KSPParticleEmitter[] rcsTransforms;

        private bool OldInfAmmo = false;
        private bool StartSetupComplete = false;

        //Fuel Burn Variables
        public float GetModuleMass(float baseMass, ModifierStagingSituation situation) => -burnedFuelMass - (boostersDecoupled ? boosterMass : 0);
        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.CONSTANTLY;

        private float burnRate = 0;
        private float burnedFuelMass = 0;

        private int cruiseTerminationFrames = 0;

        public bool SetupComplete => StartSetupComplete;
        public float initMaxAoA = 0;
        public SmoothingF smoothedAoA;
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

                if (boosterMass + boosterFuelMass + cruiseFuelMass > initialMass * 0.95f)
                {
                    Debug.LogWarning($"[BDArmory.MissileLauncher]: Error in configuration of {part.name}, boosterMass: {boosterMass} + boosterFuelMass: {boosterFuelMass} + cruiseFuelMass: {cruiseFuelMass} can't be greater than 95% of the missile mass {initialMass}, clamping to 80% of the missile mass.");
                    if (boosterFuelMass > 0 || boostTime > 0)
                    {
                        if (cruiseFuelMass > 0 || cruiseTime > 0)
                        {
                            var totalBoosterMass = Mathf.Clamp(boosterMass + boosterFuelMass, 0, initialMass * 0.4f); // Scale total booster mass + fuel to 40% of missile.
                            boosterMass = boosterMass / (boosterMass + boosterFuelMass) * totalBoosterMass;
                            boosterFuelMass = totalBoosterMass - boosterMass;
                            cruiseFuelMass = Mathf.Clamp(cruiseFuelMass, 0, initialMass * 0.4f);
                        }
                        else
                        {
                            var totalBoosterMass = Mathf.Clamp(boosterMass + boosterFuelMass, 0, initialMass * 0.8f); // Scale total booster mass + fuel to 80% of missile.
                            boosterMass = boosterMass / (boosterMass + boosterFuelMass) * totalBoosterMass;
                            boosterFuelMass = totalBoosterMass - boosterMass;
                        }
                    }
                    else
                    {
                        boosterMass = 0; // Fuel-less boosters aren't sensible when requiring fuel.
                        cruiseFuelMass = Mathf.Clamp(cruiseFuelMass, 0, initialMass * 0.8f);
                    }
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

            if (dragArea < 0)
            {
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher]: OnStart missile {shortName}: setting default dragArea to liftArea {liftArea}:");
                dragArea = liftArea;
            }

            loftState = LoftStates.Boost;
            TimeToImpact = float.PositiveInfinity;
            initMaxAoA = maxAoA;
            terminalHomingActive = false;

            if (LoftTermRange > 0)
            {
                Debug.LogWarning($"[BDArmory.MissileLauncher]: Error in configuration of {part.name}, LoftTermRange is deprecated, please use terminalHomingRange instead.");
                terminalHomingRange = LoftTermRange;
                LoftTermRange = -1;
            }

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
                                        var existingBE = be.Current.GetComponent<BDAGaplessParticleEmitter>();
                                        if (existingBE)
                                        {
                                            existingBE.emit = false;
                                            be.Current.emit = false;
                                            continue;
                                        }
                                        BDAGaplessParticleEmitter ge = be.Current.gameObject.AddComponent<BDAGaplessParticleEmitter>();
                                        ge.part = part;
                                        ge.emit = false;
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

                using (var pEmitter = part.FindModelComponents<KSPParticleEmitter>().AsEnumerable().GetEnumerator())
                    while (pEmitter.MoveNext())
                    {
                        if (pEmitter.Current == null) continue;
                        var existingGE = pEmitter.Current.GetComponent<BDAGaplessParticleEmitter>();
                        if (existingGE || boostEmitters.Contains(pEmitter.Current))
                        {
                            if (existingGE) existingGE.emit = false;
                                continue;
                        }

                        if (pEmitter.Current.useWorldSpace)
                        {
                            BDAGaplessParticleEmitter gaplessEmitter = pEmitter.Current.gameObject.AddComponent<BDAGaplessParticleEmitter>();
                            gaplessEmitter.part = part;
                            gaplessEmitter.emit = false;
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
                var missileSpawner = part.FindModuleImplementing<ModuleMissileRearm>();
                if (missileSpawner != null)
                {
                    reloadableRail = missileSpawner;
                    hasAmmo = true;
                }
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
                    clusterbomb *= (int)((MultiMissileLauncher)partModules.Current).salvoSize;
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
            smoothedAoA = new SmoothingF(Mathf.Exp(Mathf.Log(0.5f) * Time.fixedDeltaTime * 10f)); // Half-life of 0.1s.
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
                if (torpedo) dropTime = 0;
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
                terminalGuidanceShouldActivate = false;
            }

            if (GuidanceMode != GuidanceModes.AAMLoft && GuidanceMode != GuidanceModes.Kappa)
            {
                Fields["LoftMaxAltitude"].guiActive = false;
                Fields["LoftMaxAltitude"].guiActiveEditor = false;
                Fields["LoftRangeOverride"].guiActive = false;
                Fields["LoftRangeOverride"].guiActiveEditor = false;
                Fields["LoftAngle"].guiActive = false;
                Fields["LoftAngle"].guiActiveEditor = false;
                Fields["LoftTermAngle"].guiActive = false;
                Fields["LoftTermAngle"].guiActiveEditor = false;
                Fields["LoftRangeFac"].guiActive = false;
                Fields["LoftRangeFac"].guiActiveEditor = false;
                Fields["LoftVertVelComp"].guiActive = false;
                Fields["LoftVertVelComp"].guiActiveEditor = false;
            }
            else
            {
                Fields["LoftMaxAltitude"].guiActive = true;
                Fields["LoftMaxAltitude"].guiActiveEditor = true;
                Fields["LoftRangeOverride"].guiActive = true;
                Fields["LoftRangeOverride"].guiActiveEditor = true;
                

                if (!GameSettings.ADVANCED_TWEAKABLES)
                {
                    Fields["LoftAngle"].guiActive = false;
                    Fields["LoftAngle"].guiActiveEditor = false;
                    Fields["LoftTermAngle"].guiActive = false;
                    Fields["LoftTermAngle"].guiActiveEditor = false;
                    Fields["LoftRangeFac"].guiActive = false;
                    Fields["LoftRangeFac"].guiActiveEditor = false;
                    Fields["LoftVertVelComp"].guiActive = false;
                    Fields["LoftVertVelComp"].guiActiveEditor = false;
                }
                else
                {
                    Fields["LoftAngle"].guiActive = true;
                    Fields["LoftAngle"].guiActiveEditor = true;
                    Fields["LoftTermAngle"].guiActive = true;
                    Fields["LoftTermAngle"].guiActiveEditor = true;
                    Fields["LoftRangeFac"].guiActive = true;
                    Fields["LoftRangeFac"].guiActiveEditor = true;
                    Fields["LoftVertVelComp"].guiActive = true;
                    Fields["LoftVertVelComp"].guiActiveEditor = true;
                }
            }

            if (GuidanceMode != GuidanceModes.AAMLoft)
            {
                Fields["LoftMinAltitude"].guiActive = false;
                Fields["LoftMinAltitude"].guiActiveEditor = false;
                Fields["LoftVelComp"].guiActive = false;
                Fields["LoftVelComp"].guiActiveEditor = false;
                Fields["LoftAltitudeAdvMax"].guiActive = false;
                Fields["LoftAltitudeAdvMax"].guiActiveEditor = false;
                //Fields["LoftAltComp"].guiActive = false;
                //Fields["LoftAltComp"].guiActiveEditor = false;
                //Fields["terminalHomingRange"].guiActive = false;
                //Fields["terminalHomingRange"].guiActiveEditor = false;
            }
            else
            {
                Fields["LoftMinAltitude"].guiActive = true;
                Fields["LoftMinAltitude"].guiActiveEditor = true;
                Fields["LoftAltitudeAdvMax"].guiActive = true;
                Fields["LoftAltitudeAdvMax"].guiActiveEditor = true;
                //Fields["terminalHomingRange"].guiActive = true;
                //Fields["terminalHomingRange"].guiActiveEditor = true;

                if (!GameSettings.ADVANCED_TWEAKABLES)
                {
                    Fields["LoftVelComp"].guiActive = false;
                    Fields["LoftVelComp"].guiActiveEditor = false;
                    //Fields["LoftAltComp"].guiActive = false;
                    //Fields["LoftAltComp"].guiActiveEditor = false;
                }
                else
                {
                    Fields["LoftVelComp"].guiActive = true;
                    Fields["LoftVelComp"].guiActiveEditor = true;
                    //Fields["LoftAltComp"].guiActive = true;
                    //Fields["LoftAltComp"].guiActiveEditor = true;
                }
            }
            if (!terminalHoming && GuidanceMode != GuidanceModes.AAMLoft) //(GuidanceMode != GuidanceModes.AAMHybrid && GuidanceMode != GuidanceModes.AAMLoft)
            {
                Fields["terminalHomingRange"].guiActive = false;
                Fields["terminalHomingRange"].guiActiveEditor = false;
            }
            else
            {
                Fields["terminalHomingRange"].guiActive = true;
                Fields["terminalHomingRange"].guiActiveEditor = true;
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
            //Debug.Log("{TorpDebug] torpedo crash tolerance: " + part.crashTolerance);
            DetachExhaustPrefabs();
            KillRCS();
            if (upRCS) EffectBehaviour.RemoveParticleEmitter(upRCS);
            if (downRCS) EffectBehaviour.RemoveParticleEmitter(downRCS);
            if (leftRCS) EffectBehaviour.RemoveParticleEmitter(leftRCS);
            if (rightRCS) EffectBehaviour.RemoveParticleEmitter(rightRCS);
            if (forwardRCS) EffectBehaviour.RemoveParticleEmitter(forwardRCS);
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
                    else if (part.FindModuleImplementing<MultiMissileLauncher>() != null)
                    {
                        blastRadius = BlastPhysicsUtils.CalculateBlastRange(part.FindModuleImplementing<MultiMissileLauncher>().tntMass);
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
            if (SourceVessel == null)
            {
                SourceVessel = vessel;
            }
            if (multiLauncher)
            {
                if (multiLauncher.isMultiLauncher)
                {
                    //multiLauncher.rippleRPM = wpm.rippleRPM;               
                    //if (wpm.rippleRPM > 0) multiLauncher.rippleRPM = wpm.rippleRPM;
                    multiLauncher.Team = Team;
                    if (reloadableRail && reloadableRail.ammoCount >= 1 || BDArmorySettings.INFINITE_ORDINANCE) multiLauncher.fireMissile();
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher]: firing Multilauncher! {vessel.vesselName}; {multiLauncher.subMunitionName}");
                }
                else //isClusterMissile
                {
                    if (reloadableRail && (reloadableRail.maxAmmo > 1 && (reloadableRail.ammoCount >= 1 || BDArmorySettings.INFINITE_ORDINANCE))) //clustermissile with reload module
                    {
                        if (reloadableMissile == null) reloadableMissile = StartCoroutine(FireReloadableMissile());
                        launched = true;
                    }
                    else //standard non-reloadable missile
                    {
                        multiLauncher.missileSpawner.MissileName = multiLauncher.subMunitionName;
                        multiLauncher.missileSpawner.UpdateMissileValues();
                        DetonationDistance = multiLauncher.clusterMissileTriggerDist;
                        blastRadius = multiLauncher.clusterMissileTriggerDist;
                        multiLauncher.isLaunchedClusterMissile = true;
                        TimeFired = Time.time;
                        part.decouple(0);
                        part.Unpack();
                        TargetPosition = vessel.ReferenceTransform.position + vessel.ReferenceTransform.up * 5000; //set initial target position so if no target update, missileBase will count a miss if it nears this point or is flying post-thrust
                        MissileLaunch();
                        BDATargetManager.FiredMissiles.Add(this);
                        if (wpm != null)
                        {
                            wpm.heatTarget = TargetSignatureData.noTarget;
                            GpsUpdateMax = wpm.GpsUpdateMax;
                        }
                        launched = true;
                    }
                }
            }
            else
            {
                if (reloadableRail && (reloadableRail.ammoCount >= 1 || BDArmorySettings.INFINITE_ORDINANCE))
                {
                    if (reloadableMissile == null) reloadableMissile = StartCoroutine(FireReloadableMissile());
                    launched = true;
                }
                else
                {
                    TimeFired = Time.time;
                    part.decouple(0);
                    part.Unpack();
                    TargetPosition = transform.position + transform.forward * 5000; //set initial target position so if no target update, missileBase will count a miss if it nears this point or is flying post-thrust
                    MissileLaunch();
                    BDATargetManager.FiredMissiles.Add(this);
                    if (wpm != null)
                    {
                        wpm.heatTarget = TargetSignatureData.noTarget;
                        GpsUpdateMax = wpm.GpsUpdateMax;
                    }
                    launched = true;
                }
            }
        }
        IEnumerator FireReloadableMissile()
        {
            part.partTransform.localScale = Vector3.zero;
            part.ShieldedFromAirstream = true;
            part.crashTolerance = 100;
            if (!reloadableRail.SpawnMissile(MissileReferenceTransform))
            {
                if (BDArmorySettings.DEBUG_MISSILES) Debug.LogWarning($"[BDArmory.MissileLauncher]: Failed to spawn a missile in {reloadableRail} on {vessel.vesselName}");
                yield break;
            }
            MissileLauncher ml = reloadableRail.SpawnedMissile.FindModuleImplementing<MissileLauncher>();
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher]: Spawning missile {reloadableRail.SpawnedMissile.name}; type: {ml.homingType}/{ml.targetingType}");
            yield return new WaitUntilFixed(() => ml == null || ml.SetupComplete); // Wait until missile fully initialized.
            if (ml is null || ml.gameObject is null || !ml.gameObject.activeInHierarchy)
            {
                if (ml is not null) Destroy(ml); // The gameObject is gone, make sure the module goes too.
                Debug.LogWarning($"[BDArmory.MissileLauncher]: Error while spawning missile with {part.name}, MissileLauncher was null!");
                yield break;
            }

            ml.launched = true;
            var wpm = VesselModuleRegistry.GetMissileFire(SourceVessel, true);
            BDATargetManager.FiredMissiles.Add(ml);
            ml.SourceVessel = SourceVessel;
            ml.GuidanceMode = GuidanceMode;
            //wpm.SendTargetDataToMissile(ml);
            ml.TimeFired = Time.time;
            ml.DetonationDistance = DetonationDistance;
            ml.DetonateAtMinimumDistance = DetonateAtMinimumDistance;
            ml.dropTime = dropTime;
            ml.detonationTime = detonationTime;
            ml.engageAir = engageAir;
            ml.engageGround = engageGround;
            ml.engageMissile = engageMissile;
            ml.engageSLW = engageSLW;
            ml.gLimit = gLimit;
            ml.gMargin = gMargin;

            if (GuidanceMode == GuidanceModes.AGMBallistic)
            {
                ml.BallisticOverShootFactor = BallisticOverShootFactor; //are some of these null, and causing this to quit? 
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
                ml.terminalHomingRange = terminalHomingRange;
                ml.homingModeTerminal = homingModeTerminal;
                ml.pronavGain = pronavGain;
                ml.loftState = LoftStates.Boost;
                ml.TimeToImpact = float.PositiveInfinity;
                ml.initMaxAoA = maxAoA;
            }
            /*            if (GuidanceMode == GuidanceModes.AAMHybrid)
                            ml.pronavGain = pronavGain;*/
            if (GuidanceMode == GuidanceModes.APN || GuidanceMode == GuidanceModes.PN)
                ml.pronavGain = pronavGain;

            if (GuidanceMode == GuidanceModes.Kappa)
            {
                ml.kappaAngle = kappaAngle;
                ml.LoftAngle = LoftAngle;
                ml.loftState = LoftStates.Boost;
                ml.LoftTermAngle = LoftTermAngle;
                ml.LoftMaxAltitude = LoftMaxAltitude;
                ml.LoftRangeFac = LoftRangeFac;
                ml.LoftVertVelComp = LoftVertVelComp;
                ml.LoftRangeOverride = LoftRangeOverride;
            }

            ml.terminalHoming = terminalHoming;
            if (terminalHoming)
            {
                if (homingModeTerminal == GuidanceModes.AGMBallistic)
                {
                    ml.BallisticOverShootFactor = BallisticOverShootFactor; //are some of these null, and causeing this to quit? 
                    ml.BallisticAngle = BallisticAngle;
                }
                if (homingModeTerminal == GuidanceModes.Cruise)
                {
                    ml.CruiseAltitude = CruiseAltitude;
                    ml.CruiseSpeed = CruiseSpeed;
                    ml.CruisePredictionTime = CruisePredictionTime;
                }
                if (homingModeTerminal == GuidanceModes.AAMLoft)
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
                    ml.pronavGain = pronavGain;
                    ml.loftState = LoftStates.Boost;
                    ml.TimeToImpact = float.PositiveInfinity;
                    ml.initMaxAoA = maxAoA;
                }
                if (homingModeTerminal == GuidanceModes.APN || homingModeTerminal == GuidanceModes.PN)
                    ml.pronavGain = pronavGain;

                if (homingModeTerminal == GuidanceModes.Kappa)
                {
                    ml.kappaAngle = kappaAngle;
                    ml.LoftAngle = LoftAngle;
                    ml.loftState = LoftStates.Boost;
                    ml.LoftTermAngle = LoftTermAngle;
                    ml.LoftMaxAltitude = LoftMaxAltitude;
                    ml.LoftRangeFac = LoftRangeFac;
                    ml.LoftVertVelComp = LoftVertVelComp;
                    ml.LoftRangeOverride = LoftRangeOverride;
                }

                ml.terminalHomingRange = terminalHomingRange;
                ml.homingModeTerminal = homingModeTerminal;
                ml.terminalHomingActive = false;
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
                ml.GpsUpdateMax = wpm.GpsUpdateMax;
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
            // if (gameObject is null || !gameObject.activeInHierarchy) { Debug.LogError($"[BDArmory.MissileLauncher]: Trying to fire non-existent missile {missileName} {(reloadableRail != null ? " (reloadable)" : "")} on {SourceVesselName} at {TargetVesselName}!"); return; }
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
                if (BDArmorySettings.DEBUG_MISSILES) shortName = $"{SourceVessel.GetName()}'s {GetShortName()}";
                vessel.vesselName = GetShortName();
                vessel.vesselType = VesselType.Probe;
                //setting ref transform for navball
                GameObject refObject = new GameObject();
                refObject.transform.rotation = Quaternion.LookRotation(-transform.up, transform.forward);
                refObject.transform.parent = transform;
                part.SetReferenceTransform(refObject.transform);
                vessel.SetReferenceTransform(part);
                vesselReferenceTransform = refObject.transform;
                DetonationDistanceState = DetonationDistanceStates.NotSafe;
                MissileState = MissileStates.Drop;
                part.crashTolerance = torpedo ? waterImpactTolerance : 9999; //to combat stresses of launch, missiles generate a lot of G Force
                part.explosionPotential = 0; // Minimise the default part explosion FX that sometimes gets offset from the main explosion.
                vacuumClearanceState = (GuidanceMode == GuidanceModes.Orbital && vacuumSteerable && vessel.InVacuum() && missileTurret == null) ? 
                    VacuumClearanceStates.Clearing : VacuumClearanceStates.Cleared; // Set up clearance check if missile is vacuumSteerable, and is in space, and was not launched from a turret

                StartCoroutine(MissileRoutine());
                var tnt = part.FindModuleImplementing<BDExplosivePart>();
                if (tnt)
                {
                    tnt.Team = Team;
                    tnt.sourcevessel = SourceVessel;
                }
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileLauncher]: Missile Launched!");
                if (BDArmorySettings.CAMERA_SWITCH_INCLUDE_MISSILES && SourceVessel.isActiveVessel) LoadedVesselSwitcher.Instance.ForceSwitchVessel(vessel);
            }
            catch (Exception e)
            {
                Debug.LogError("[BDArmory.MissileLauncher]: DEBUG " + e.Message + "\n" + e.StackTrace);
                try { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG null part?: " + (part == null)); } catch (Exception e2) { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG part: " + e2.Message); }
                try { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG null part.rb?: " + (part.rb == null)); } catch (Exception e2) { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG part.rb: " + e2.Message); }
                try { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG null BDATargetManager.FiredMissiles?: " + (BDATargetManager.FiredMissiles == null)); } catch (Exception e2) { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG BDATargetManager.FiredMissiles: " + e2.Message); }
                try { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG null vessel?: " + (vessel == null)); } catch (Exception e2) { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG vessel: " + e2.Message); }
                try { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG null targetVessel?: " + (targetVessel == null)); } catch (Exception e2) { Debug.LogWarning("[BDArmory.MissileLauncher]: DEBUG targetVessel: " + e2.Message); }
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
            if (!multiLauncher) part.crashTolerance = 5;
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
                    if (useSimpleDrag || useSimpleDragTemp)
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

                    UpdateThrustForces();
                    UpdateGuidance();

                    //RaycastCollisions();

                    //Timed detonation
                    if (isTimed && TimeIndex > detonationTime)
                    {
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher] missile timed out; self-destructing!");
                        Detonate();
                    }
                    //debugString.AppendLine($"crashTol: {part.crashTolerance}; collider: {part.collider.enabled}; usingSimpleDrag: {(useSimpleDrag && useSimpleDragTemp)}; drag: {part.angularDrag.ToString("0.00")}");
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
            if (weaponClass == WeaponClasses.Bomb) return;
            float sqrDist = (float)((TargetPosition + (TargetVelocity * Time.fixedDeltaTime)) - (vessel.CoM + (vessel.Velocity() * Time.fixedDeltaTime))).sqrMagnitude;
            bool targetBehindMissile = !TargetAcquired || (!(MissileState != MissileStates.PostThrust && hasRCS) && Vector3.Dot(TargetPosition - transform.position, transform.forward) < 0f); // Target is not acquired or we are behind it and not an RCS missile
            if (sqrDist < 160000 || MissileState == MissileStates.PostThrust || (targetBehindMissile && sqrDist > 1000000)) //missile has come within 400m, is post thrust, or > 1km behind target
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
                Vector3 tgtVel = TargetVelocity == Vector3.zero && targetVessel != null ? targetVessel.Vessel.Velocity() : TargetVelocity;
                bool noProgress = MissileState == MissileStates.PostThrust && (Vector3.Dot(vessel.Velocity() - tgtVel, TargetPosition - vessel.transform.position) < 0 ||
                    (!vessel.InVacuum() && vessel.srfSpeed < GetKinematicSpeed()) && weaponClass == WeaponClasses.Missile);
                bool pastGracePeriod = TimeIndex > ((vessel.LandedOrSplashed ? 0f : dropTime) + Mathf.Clamp(maxTurnRateDPS / 15, 1, 8)); //180f / maxTurnRateDPS);
                if ((pastGracePeriod && targetBehindMissile) || noProgress) // Check that we're not moving away from the target after a grace period
                {
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher]: Missile has missed({(noProgress ? "no progress" : "past target")})!");

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
                                debugGuidanceTarget = $"{heatTarget.vessel.GetName()} {heatTarget.signalStrength}";
                            else if (heatTarget.signalStrength > 0)
                                debugGuidanceTarget = $"Flare {heatTarget.signalStrength}";
                        }
                        break;
                    case TargetingModes.Radar:
                        UpdateRadarTarget();
                        if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
                        {
                            if (radarTarget.vessel)
                                debugGuidanceTarget = $"{radarTarget.vessel.GetName()} {radarTarget.signalStrength}";
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
                    case TargetingModes.Inertial:
                        UpdateInertialTarget();
                        if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
                        {
                            debugGuidanceTarget = $"TgtPos: {TargetPosition}; Drift: {(TargetPosition - VectorUtils.GetWorldSurfacePostion(targetGPSCoords, vessel.mainBody)).ToString()}";
                        }
                        break;
                    default:
                        if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
                        {
                            TargetPosition = transform.position + (startDirection * 500);
                            debugGuidanceTarget = TargetPosition.ToString();
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

                if (guidanceActive && TimeIndex - dropTime > guidanceDelay)
                {
                    WarnTarget();

                    //if (targetVessel && targetVessel.loaded)
                    //{
                    //   Vector3 targetCoMPos = targetVessel.CoM;
                    //    TargetPosition = targetCoMPos + targetVessel.Velocity() * Time.fixedDeltaTime;
                    //}

                    // Increase turn rate gradually after launch, unless vacuum steerable in space
                    float turnRateDPS = maxTurnRateDPS;
                    if (!((vacuumSteerable && vessel.InVacuum()) || boostTime == 0f))
                        turnRateDPS = Mathf.Clamp(((TimeIndex - dropTime) / boostTime) * maxTurnRateDPS * 25f, 0, maxTurnRateDPS);
                    if (!hasRCS)
                    {
                        turnRateDPS *= controlAuthority;
                    }

                    //decrease turn rate after thrust cuts out
                    if (TimeIndex > dropTime + boostTime + cruiseDelay + cruiseTime)
                    {
                        var clampedTurnRate = Mathf.Clamp(maxTurnRateDPS - ((TimeIndex - dropTime - boostTime - cruiseDelay - cruiseTime) * 0.45f),
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

                    if (terminalHoming && !terminalHomingActive)
                    {
                        if (Vector3.SqrMagnitude(TargetPosition - vessel.transform.position) < terminalHomingRange * terminalHomingRange)
                        {
                            GuidanceMode = homingModeTerminal;
                            terminalHomingActive = true;
                            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileGuidance]: Terminal");
                        }
                    }
                    switch (GuidanceMode)
                    {
                        case GuidanceModes.AAMLead:
                        case GuidanceModes.APN:
                        case GuidanceModes.PN:
                        case GuidanceModes.AAMLoft:
                        case GuidanceModes.AAMPure:
                        case GuidanceModes.Kappa:
                            //GuidanceModes.AAMHybrid:
                            AAMGuidance();
                            break;
                        case GuidanceModes.AGM:
                            AGMGuidance();
                            break;
                        case GuidanceModes.AGMBallistic:
                            AGMBallisticGuidance();
                            break;
                        case GuidanceModes.BeamRiding:
                            BeamRideGuidance();
                            break;
                        case GuidanceModes.Orbital: //nee GuidanceModes.RCS
                            OrbitalGuidance(turnRateDPS);
                            break;
                        case GuidanceModes.Cruise:
                            CruiseGuidance();
                            break;
                        case GuidanceModes.SLW:
                            SLWGuidance();
                            break;
                        case GuidanceModes.None:
                            DoAero(TargetPosition);
                            CheckMiss();
                            break;
                    }
                }
                else
                {
                    CheckMiss();
                    TargetMf = null;
                    if (aero)
                    {
                        aeroTorque = MissileGuidance.DoAeroForces(this, TargetPosition, liftArea, dragArea, .25f, aeroTorque, maxTorque, maxAoA, MissileGuidance.DefaultLiftCurve, MissileGuidance.DefaultDragCurve);
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

            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
            {
                if (guidanceActive) debugString.AppendLine("Missile target=" + debugGuidanceTarget);
                else debugString.AppendLine("Guidance inactive");

                if (!(BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)) return;
                var distance = (TargetPosition - transform.position).magnitude;
                debugString.AppendLine($"Target distance: {(distance > 1000 ? $" {distance / 1000:F1} km" : $" {distance:F0} m")}, closing speed: {Vector3.Dot(vessel.Velocity() - TargetVelocity, GetForwardTransform()):F1} m/s");
            }
        }

        // feature_engagementenvelope: terminal guidance mode for cruise missiles
        private void UpdateTerminalGuidance()
        {
            Vector3 tempTargetPos = TargetPosition;

            if (TargetingMode == TargetingModes.Inertial)
            {
                float deltaT = TimeIndex - TimeOfLastINS;
                tempTargetPos = VectorUtils.GetWorldSurfacePostion(TargetINSCoords, vessel.mainBody);
                if (deltaT > GpsUpdateMax)
                {
                    deltaT /= INStimetogo;

                    tempTargetPos = new Vector3((1f - deltaT) * tempTargetPos.x + deltaT * TargetPosition.x, (1f - deltaT) * tempTargetPos.y + deltaT * TargetPosition.y, (1f - deltaT) * tempTargetPos.z + deltaT * TargetPosition.z);
                }
            }

            // check if guidance mode should be changed for terminal phase
            float distanceSqr = (tempTargetPos - vessel.CoM).sqrMagnitude;

            if (terminalGuidanceShouldActivate && !terminalGuidanceActive && (TargetingModeTerminal != TargetingModes.None) && (distanceSqr < terminalGuidanceDistance * terminalGuidanceDistance))
            {
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher][Terminal Guidance]: missile {GetPartName()} updating targeting mode: {terminalGuidanceType}");

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
                            terminalGuidanceActive = true;

                            // Adjust heat score based on distance missile will travel in the next update
                            if (heatTarget.signalStrength > 0)
                            {
                                float currentFactor = (1400 * 1400) / Mathf.Clamp((heatTarget.position - transform.position).sqrMagnitude, 90000, 36000000);
                                Vector3 currVel = vessel.Velocity();
                                heatTarget.position = heatTarget.position + heatTarget.velocity * Time.fixedDeltaTime;
                                heatTarget.velocity = heatTarget.velocity + heatTarget.acceleration * Time.fixedDeltaTime;
                                float futureFactor = (1400 * 1400) / Mathf.Clamp((heatTarget.position - (transform.position + (currVel * Time.fixedDeltaTime))).sqrMagnitude, 90000, 36000000);
                                heatTarget.signalStrength *= futureFactor / currentFactor;
                            }
                        }
                        else
                        {
                            if (!dumbTerminalGuidance)
                            {
                                TargetAcquired = true;
                                TargetVelocity = Vector3.zero;
                                TargetAcceleration = Vector3.zero;
                                //continue towards primary guidance targetPosition until heat lock acquired
                            }
                            if (BDArmorySettings.DEBUG_MISSILES)
                            {
                                Debug.Log("[BDArmory.MissileLauncher][Terminal Guidance]: Missile heatseeker could not acquire a target lock, reverting to default guidance.");
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
                        //TargetSignatureData lockedTarget = TargetSignatureData.noTarget;

                        float currDist = float.PositiveInfinity;
                        float prevDist = float.PositiveInfinity;
                        int lockIndex = -1;

                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher][Terminal Guidance]: Active radar found: {scannedTargets.Length} targets.");

                        for (int i = 0; i < scannedTargets.Length; i++)
                        {
                            if (scannedTargets[i].exists)
                            {
                                currDist = (scannedTargets[i].predictedPosition - tempTargetPos).sqrMagnitude;

                                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher][Terminal Guidance]: Target: {scannedTargets[i].vessel.name} has currDist: {currDist}.");

                                //re-check engagement envelope, only lock appropriate targets
                                if (currDist < sqrThresh && currDist < prevDist && CheckTargetEngagementEnvelope(scannedTargets[i].targetInfo))
                                {
                                    prevDist = currDist;

                                    lockIndex = i;
                                    ActiveRadar = true;
                                }
                            }
                        }

                        if (lockIndex >= 0)
                        {
                            radarTarget = scannedTargets[lockIndex];
                            TargetAcquired = true;
                            TargetPosition = radarTarget.predictedPositionWithChaffFactor(chaffEffectivity);
                            TargetVelocity = radarTarget.velocity;
                            TargetAcceleration = radarTarget.acceleration;
                            targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(TargetPosition, vessel.mainBody);

                            if (weaponClass == WeaponClasses.SLW)
                                RadarWarningReceiver.PingRWR(new Ray(transform.position, radarTarget.predictedPosition - transform.position), 45, RadarWarningReceiver.RWRThreatTypes.Torpedo, 2f);
                            else
                                RadarWarningReceiver.PingRWR(new Ray(transform.position, radarTarget.predictedPosition - transform.position), 45, RadarWarningReceiver.RWRThreatTypes.MissileLaunch, 2f);

                            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher][Terminal Guidance]: Pitbull! Radar missileBase has gone active.  Radar sig strength: {radarTarget.signalStrength:0.0} - target: {radarTarget.vessel.name}");
                            terminalGuidanceActive = true;
                        }
                        else
                        {
                            TargetAcquired = true;
                            TargetPosition = VectorUtils.GetWorldSurfacePostion(UpdateGPSTarget(), vessel.mainBody); //putting back the GPS target if no radar target found
                            TargetVelocity = Vector3.zero;
                            TargetAcceleration = Vector3.zero;
                            targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(TargetPosition, vessel.mainBody); //tgtPos/tgtGPS should relly be not set here, so the last valid postion/coords are used, in case of non-GPS primary guidance
                            if (dumbTerminalGuidance)
                                terminalGuidanceActive = true;
                            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileLauncher][Terminal Guidance]: Missile radar could not acquire a target lock - Defaulting to GPS Target");
                        }
                        break;

                    case TargetingModes.Laser:
                        // not very useful, currently unsupported!
                        break;

                    case TargetingModes.Gps:
                        // from gps to gps -> no actions need to be done!
                        break;
                    case TargetingModes.Inertial:
                        // Not sure *why* you'd use this for TerminalGuideance, but ok...
                        TargetAcquired = true;
                        if (targetVessel != null) TargetPosition = VectorUtils.GetWorldSurfacePostion(MissileGuidance.GetAirToAirFireSolution(this, targetVessel.Vessel.CoM, TargetVelocity), vessel.mainBody);
                        TargetVelocity = Vector3.zero;
                        TargetAcceleration = Vector3.zero;
                        terminalGuidanceActive = true;
                        break;

                    case TargetingModes.AntiRad:
                        TargetAcquired = true;
                        targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(TargetPosition, vessel.mainBody); // Set the GPS coordinates from the current target position.
                        SetAntiRadTargeting(); //should then already work automatically via OnReceiveRadarPing
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileLauncher][Terminal Guidance]: Antiradiation mode set! Waiting for radar signals...");
                        terminalGuidanceActive = true;
                        break;
                }
                if (dumbTerminalGuidance || terminalGuidanceActive)
                {
                    TargetingMode = TargetingModeTerminal;
                    terminalGuidanceActive = true;
                    terminalGuidanceShouldActivate = false;
                }
            }
        }

        void UpdateThrustForces()
        {
            if (MissileState == MissileStates.PostThrust) return;
            if (weaponClass == WeaponClasses.SLW && FlightGlobals.getAltitudeAtPos(part.transform.position) > 0) return; //#710, no torp thrust out of water
            if (currentThrust * Throttle > 0)
            {
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
                {
                    debugString.AppendLine($"Missile thrust= {currentThrust * Throttle:F3} kN");
                    debugString.AppendLine($"Missile mass= {part.mass * 1000f:F1} kg");
                }
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

            if (deployStates != null) StartCoroutine(DeployAnimRoutine());
            yield return new WaitForSecondsFixed(dropTime);
            yield return StartCoroutine(BoostRoutine());

            if (animStates != null) StartCoroutine(FlightAnimRoutine());
            yield return new WaitForSecondsFixed(cruiseDelay);
            if (cruiseRangeTrigger > 0)
                yield return new WaitUntilFixed(checkCruiseRangeTrigger);
                
            yield return StartCoroutine(CruiseRoutine());
        }

        bool checkCruiseRangeTrigger()
        {
            float sqrRange = (TargetPosition - part.rb.position).sqrMagnitude;

            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher]: Check cruise range trigger range: {BDAMath.Sqrt(sqrRange)}");

            if (sqrRange < cruiseRangeTrigger * cruiseRangeTrigger)
            {
                if (cruiseTerminationFrames < 5)
                {
                    cruiseTerminationFrames++;
                    return false;
                }

                cruiseTerminationFrames = 0;
                return true;
            }

            cruiseTerminationFrames = 0;
            return false;
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
        IEnumerator updateCrashTolerance()
        {
            yield return new WaitForSecondsFixed(0.5f); //wait half sec after boost motor fires, then set crashTolerance to 1. Torps have already waited until splashdown before this is called.
            part.crashTolerance = 1;

            var missileCOL = part.collider;
            if (missileCOL) missileCOL.enabled = true;
            if (useSimpleDragTemp)
            {
                part.dragModel = Part.DragModel.DEFAULT;
                useSimpleDragTemp = false;
            }
        }
        IEnumerator BoostRoutine()
        {
            if (weaponClass == WeaponClasses.SLW && FlightGlobals.getAltitudeAtPos(part.transform.position) > 0)
            {
                yield return new WaitUntilFixed(() => vessel == null || vessel.LandedOrSplashed);//don't start torpedo thrust until underwater
                if (vessel == null || vessel.Landed) Detonate(); //dropping torpedoes over land is just going to turn them into heavy, expensive bombs...
            }
            if (useFuel)
            {
                burnRate = boostTime > 0 ? boosterFuelMass / boostTime * Time.fixedDeltaTime : 0;
                burnedFuelMass = 0f;
            }
            StartBoost();
            StartCoroutine(updateCrashTolerance());
            var wait = new WaitForFixedUpdate();
            float boostStartTime = Time.time;
            while (Time.time - boostStartTime < boostTime || (useFuel && burnedFuelMass < boosterFuelMass))
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

                //thrust
                if (useFuel && burnRate > 0)
                {
                    //if (boosterFuelMass - burnedFuelMass < burnRate * Throttle)
                    //{
                    //    Throttle = (boosterFuelMass - burnedFuelMass) / burnRate;
                    //    burnedFuelMass = boosterFuelMass;
                    //}
                    //else
                    //{
                        burnedFuelMass = Mathf.Min(burnedFuelMass + Throttle * burnRate, boosterFuelMass); // Impulse conservation code was showing issues
                    //}
                }

                audioSource.volume = Throttle;

                //particleFx
                using (var emitter = boostEmitters.GetEnumerator())
                    while (emitter.MoveNext())
                    {
                        if (emitter.Current == null) continue;
                        if (!hasRCS)
                        {
                            emitter.Current.sizeGrow = Mathf.Lerp(emitter.Current.sizeGrow, 0, 20 * Time.deltaTime);
                        }
                        if (Throttle == 0)
                            emitter.Current.emit = false;
                        else
                            emitter.Current.emit = true;
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
            audioSource.volume = Throttle;

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
                if (hasRCS && rcsTransforms != null) boostEmitters.RemoveAll(pe => rcsTransforms.Contains(pe));
                if (hasRCS && forwardRCS && !boostEmitters.Contains(forwardRCS)) boostEmitters.Add(forwardRCS);
                boostGaplessEmitters = gaplessEmitters;
            }

            using (var emitter = boostEmitters.GetEnumerator())
                while (emitter.MoveNext())
                {
                    if (emitter.Current == null) continue;
                    emitter.Current.emit = true;
                }

            if (!(thrust > 0)) return;
            sfAudioSource.PlayOneShot(SoundUtils.GetAudioClip("BDArmory/Sounds/launch"));
            RadarWarningReceiver.WarnMissileLaunch(vessel.CoM, transform.forward, TargetingMode == TargetingModes.Radar);
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
                boostersDecoupled = true;
                using (var booster = boosters.GetEnumerator())
                    while (booster.MoveNext())
                    {
                        if (booster.Current == null) continue;
                        booster.Current.AddComponent<DecoupledBooster>().DecoupleBooster(part.rb.velocity, boosterDecoupleSpeed);
                    }
            }

            if (cruiseDelay > 0 || cruiseRangeTrigger > 0)
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
            while (Time.time - cruiseStartTime < cruiseTime || (useFuel && burnedFuelMass < massToBurn))
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

                //Thrust
                if (useFuel && burnRate > 0)
                {
                    //if (massToBurn - burnedFuelMass < burnRate * Throttle)
                    //{
                    //    Throttle = (massToBurn - burnedFuelMass) / burnRate;
                    //    burnedFuelMass = massToBurn;
                    //}
                    //else
                    //{
                        burnedFuelMass = Mathf.Min(burnedFuelMass + Throttle * burnRate, massToBurn); // Other code was causing issues
                    //}
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

            currentThrust = 0f;

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
            Vector3 aamTarget = TargetPosition;
            float currgLimit = -1;

            if (TargetAcquired)
            {
                if (warheadType == WarheadTypes.ContinuousRod) //Have CR missiles target slightly above target to ensure craft caught in planar blast AOE
                {
                    TargetPosition += VectorUtils.GetUpDirection(TargetPosition) * (blastRadius > 0f ? Mathf.Min(blastRadius / 3f, DetonationDistance / 3f) : 5f);
                }
                DrawDebugLine(transform.position + (part.rb.velocity * Time.fixedDeltaTime), TargetPosition);

                float timeToImpact;
                switch (GuidanceMode)
                {
                    case GuidanceModes.APN:
                        {
                            aamTarget = MissileGuidance.GetAPNTarget(TargetPosition, TargetVelocity, TargetAcceleration, vessel, pronavGain, out timeToImpact, out currgLimit);
                            TimeToImpact = timeToImpact;
                            break;
                        }

                    case GuidanceModes.PN: // Pro-Nav
                        {
                            aamTarget = MissileGuidance.GetPNTarget(TargetPosition, TargetVelocity, vessel, pronavGain, out timeToImpact, out currgLimit);
                            TimeToImpact = timeToImpact;
                            break;
                        }
                    case GuidanceModes.AAMLoft:
                        {
                            float targetAlt = FlightGlobals.getAltitudeAtPos(TargetPosition);

                            if (TimeToImpact == float.PositiveInfinity)
                            {
                                // If the missile is not in a vaccuum, is above LoftMinAltitude and has an angle to target below the climb angle (or 90 - climb angle if climb angle > 45) (in this case, since it's angle from the vertical the check is if it's > 90f - LoftAngle) and is either is at a lower altitude than targetAlt + LoftAltitudeAdvMax or further than LoftRangeOverride, then loft.
                                if (!vessel.InVacuum() && (vessel.altitude >= LoftMinAltitude) && Vector3.Angle(TargetPosition - vessel.CoM, vessel.upAxis) > Mathf.Min(LoftAngle, 90f - LoftAngle) && ((vessel.altitude - targetAlt <= LoftAltitudeAdvMax) || (TargetPosition - vessel.CoM).sqrMagnitude > (LoftRangeOverride * LoftRangeOverride))) loftState = LoftStates.Boost;
                                else loftState = LoftStates.Terminal;
                            }

                            //aamTarget = MissileGuidance.GetAirToAirLoftTarget(TargetPosition, TargetVelocity, TargetAcceleration, vessel, targetAlt, LoftMaxAltitude, LoftRangeFac, LoftAltComp, LoftVelComp, LoftAngle, LoftTermAngle, terminalHomingRange, ref loftState, out float currTimeToImpact, out float rangeToTarget, optimumAirspeed);
                            aamTarget = MissileGuidance.GetAirToAirLoftTarget(TargetPosition, TargetVelocity, TargetAcceleration, vessel, targetAlt, LoftMaxAltitude, LoftRangeFac, LoftVertVelComp, LoftVelComp, LoftAngle, LoftTermAngle, terminalHomingRange, ref loftState, out float currTimeToImpact, out currgLimit, out float rangeToTarget, homingModeTerminal, pronavGain, optimumAirspeed);

                            float fac = (1 - (rangeToTarget - terminalHomingRange - 100f) / Mathf.Clamp(terminalHomingRange * 4f, 5000f, 25000f));

                            if (loftState > LoftStates.Boost)
                                maxAoA = Mathf.Clamp(initMaxAoA * fac, 4f, initMaxAoA);

                            TimeToImpact = currTimeToImpact;

                            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher]: AAM Loft TTGO: [{TimeToImpact:G3}]. Currently State: {loftState}. Fly to: [{aamTarget}]. Target Position: [{TargetPosition}]. Max AoA: [{maxAoA:G3}]");
                            break;
                        }
                    case GuidanceModes.AAMPure:
                        {
                            TimeToImpact = Vector3.Distance(TargetPosition, transform.position) / Mathf.Max((float)vessel.srfSpeed, optimumAirspeed);
                            aamTarget = TargetPosition;
                            break;
                        }
                    /* Case GuidanceModes.AAMHybrid:
{
                            aamTarget = MissileGuidance.GetAirToAirHybridTarget(TargetPosition, TargetVelocity, TargetAcceleration, vessel, terminalHomingRange, out timeToImpact, homingModeTerminal, pronavGain, optimumAirspeed);
                            TimeToImpact = timeToImpact;
                            break;
                        }
                    */
                    case GuidanceModes.AAMLead:
                        {
                            aamTarget = MissileGuidance.GetAirToAirTarget(TargetPosition, TargetVelocity, TargetAcceleration, vessel, out timeToImpact, optimumAirspeed);
                            TimeToImpact = timeToImpact;
                            break;
                        }

                    case GuidanceModes.Kappa:
                        {
                            aamTarget = MissileGuidance.GetKappaTarget(TargetPosition, TargetVelocity, this, MissileState == MissileStates.PostThrust ? 0f : currentThrust * Throttle, kappaAngle, LoftRangeFac, LoftVertVelComp, FlightGlobals.getAltitudeAtPos(TargetPosition), terminalHomingRange, LoftAngle, LoftTermAngle, LoftRangeOverride, LoftMaxAltitude, out timeToImpact, out currgLimit, ref loftState);
                            TimeToImpact = timeToImpact;
                            break;
                        }
                }

                if (Vector3.Angle(aamTarget - transform.position, transform.forward) > maxOffBoresight * 0.75f)
                {
                    aamTarget = TargetPosition;
                }

                //proxy detonation
                var distThreshold = 0.5f * GetBlastRadius();
                if (proxyDetonate && !DetonateAtMinimumDistance && ((TargetPosition + (TargetVelocity * Time.fixedDeltaTime)) - (transform.position)).sqrMagnitude < distThreshold * distThreshold)
                {
                    //part.Destroy(); //^look into how this interacts with MissileBase.DetonationState
                    // - if the missile is still within the notSafe status, the missile will delete itself, else, the checkProximity state of DetonationState would trigger before the missile reaches the 1/2 blastradius.
                    // would only trigger if someone set the detonation distance override to something smallerthan 1/2 blst radius, for some reason
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher] ProxiDetonate triggered");
                    Detonate();
                }
            }
            else
            {
                aamTarget = transform.position + (2000 * vessel.Velocity().normalized);
            }

            if (TimeIndex > dropTime + 0.25f)
            {
                DoAero(aamTarget, currgLimit);
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
                SLWTarget = TargetPosition; //head to last known contact and then begin circling
            }

            if (FlightGlobals.getAltitudeAtPos(SLWTarget) > 0) SLWTarget -= ((MissileGuidance.GetRaycastRadarAltitude(SLWTarget) + 2) * vessel.up);// see about implementing a 'set target running depth'?
            //allow inverse contRod-style target offset for srf targets for 'under-the-keel' proximity detonation? or at least not having the torps have a target alt of 0 (and thus be vulnerable to surface PD?)
            if (TimeIndex > dropTime + 0.25f)
            {
                DoAero(SLWTarget);
            }

            CheckMiss();

        }

        void DoAero(Vector3 targetPosition, float currgLimit = -1)
        {
            if (currgLimit < 0 || currgLimit > gLimit)
            {
                currgLimit = gLimit;
            }

            float currAoALimit = maxAoA;

            if (currgLimit > 0)
            {
                currAoALimit = MissileGuidance.getGLimit(this, MissileState == MissileStates.PostThrust ? 0f : currentThrust * Throttle, currgLimit, gMargin);
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher]: maxAoA: {maxAoA}, currAoALimit: {currAoALimit}, currgLimit: {currgLimit}");
            }

            aeroTorque = MissileGuidance.DoAeroForces(this, targetPosition, liftArea, dragArea, controlAuthority * steerMult, aeroTorque, finalMaxTorque, currAoALimit, MissileGuidance.DefaultLiftCurve, MissileGuidance.DefaultDragCurve);
        }

        void AGMBallisticGuidance()
        {
            DoAero(CalculateAGMBallisticGuidance(this, TargetPosition));
        }

        void OrbitalGuidance(float turnRateDPS)
        {
            Vector3 orbitalTarget;
            if (TargetAcquired)
            {
                if (!hasRCS) // Use thrust to kill relative velocity
                {
                    float guidance_thrust = currentThrust;
                    if (currentThrust == 0 && cruiseDelay > 0 && (TimeIndex > dropTime + boostTime) && (TimeIndex < dropTime + boostTime + cruiseDelay)) // If in the cruiseDelay, fake thrust to avoid discontinuities in the guidance
                        guidance_thrust = Mathf.Lerp(thrust, cruiseThrust, (TimeIndex - (dropTime + boostTime)) / cruiseDelay);
                    Vector3 targetVector = TargetPosition - vessel.CoM;
                    Vector3 acceleration = guidance_thrust / part.mass * GetForwardTransform();
                    Vector3 relVel = TargetVelocity - vessel.Velocity();
                    float timeToImpact = AIUtils.TimeToCPA(targetVector, relVel, TargetAcceleration - acceleration, 30);
                    orbitalTarget = AIUtils.PredictPosition(targetVector, relVel, TargetAcceleration - 0.5f * acceleration, timeToImpact);
                }
                else // Use RCS (in DoRCS()) to kill relative velocity
                    orbitalTarget = TargetPosition;

                // Clamp target position to max off boresight
                float angleToTarget = Vector3.Angle(TargetPosition - transform.position, orbitalTarget - transform.position);
                if (angleToTarget > maxOffBoresight)
                {
                    orbitalTarget = transform.position + Vector3.RotateTowards(TargetPosition - transform.position, orbitalTarget - part.transform.position, maxOffBoresight * Mathf.Deg2Rad, 0f);
                }
            }
            else
                orbitalTarget = transform.position + (2000 * vessel.Velocity().normalized);

            // In vacuum, with RCS, point towards target shortly after launch to minimize wasted delta-V
            // During this maneuver, check that we have cleared any obstacles before throttling up
            orbitalTarget = VacuumClearanceManeuver(orbitalTarget, part.transform.position, hasRCS, vacuumSteerable);
            if (Throttle == 0)
                turnRateDPS *= 15f;

            part.transform.rotation = Quaternion.RotateTowards(part.transform.rotation, Quaternion.LookRotation(orbitalTarget - part.transform.position, TargetVelocity), turnRateDPS * Time.fixedDeltaTime);
            if (TimeIndex > dropTime + 0.25f)
                CheckMiss();

            DrawDebugLine(transform.position + (part.rb.velocity * Time.fixedDeltaTime), orbitalTarget);
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
                    multiLauncher.Team = Team;
                    multiLauncher.fireMissile(true);
                }
            }
            else
            {
                if (warheadType == WarheadTypes.Standard || warheadType == WarheadTypes.ContinuousRod)
                {
                    var tnt = part.FindModuleImplementing<BDExplosivePart>();
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

                    ExplosionFx.CreateExplosion(position, blastPower, explModelPath, explSoundPath, ExplosionSourceType.Missile, 0, part, SourceVessel.vesselName, Team.Name, GetShortName(), default(Vector3), -1, warheadType == WarheadTypes.EMP, part.mass * 1000);
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

        public override float GetKinematicTime()
        {
            // Get time at which the missile is traveling at the GetKinematicSpeed() speed
            if (!launched) return -1f;

            float missileKinematicTime = boostTime + cruiseTime + cruiseDelay + dropTime - TimeIndex;
            if (!vessel.InVacuum())
            {
                float speed = currentThrust > 0 ? optimumAirspeed : (float)vessel.srfSpeed;
                float minSpeed = GetKinematicSpeed();
                if (speed > minSpeed)
                {
                    float airDensity = (float)vessel.atmDensity;
                    float dragTerm;
                    float t;
                    if (useSimpleDrag)
                    {
                        dragTerm = (deployed ? deployedDrag : simpleDrag) * (0.008f * part.mass) * 0.5f * airDensity;
                        t = part.mass / (minSpeed * dragTerm) - part.mass / (speed * dragTerm);
                    }
                    else
                    {
                        float AoA = smoothedAoA.Value;
                        FloatCurve dragCurve = MissileGuidance.DefaultDragCurve;
                        float dragCd = dragCurve.Evaluate(AoA);
                        float dragMultiplier = BDArmorySettings.GLOBAL_DRAG_MULTIPLIER;
                        dragTerm = 0.5f * airDensity * dragArea * dragMultiplier * dragCd;
                        float dragTermMinSpeed = 0.5f * airDensity * dragArea * dragMultiplier * dragCurve.Evaluate(Mathf.Min(29f, maxAoA)); // Max AoA or 29 deg (at kink in drag curve)
                        t = part.mass / (minSpeed * dragTermMinSpeed) - part.mass / (speed * dragTerm);
                    }
                    missileKinematicTime += t; // Add time for missile to slow down to min speed
                }
            }

            return missileKinematicTime;
        }

        public override float GetKinematicSpeed()
        {
            if (vessel.InVacuum() || weaponClass != WeaponClasses.Missile) return 0f;

            // Get speed at which the missile is only capable of pulling a 2G turn at maxAoA
            float Gs = 2f;

            FloatCurve liftCurve = MissileGuidance.DefaultLiftCurve;
            float bodyGravity = (float)PhysicsGlobals.GravitationalAcceleration * (float)vessel.orbit.referenceBody.GeeASL;
            float liftMultiplier = BDArmorySettings.GLOBAL_LIFT_MULTIPLIER;
            float kinematicSpeed = BDAMath.Sqrt((Gs * part.mass * bodyGravity) / (0.5f * (float)vessel.atmDensity * liftArea * liftMultiplier * liftCurve.Evaluate(maxAoA)));

            return Mathf.Min(kinematicSpeed, 0.5f * (float)vessel.speedOfSound);
        }

        protected override void PartDie(Part p)
        {
            if (p != part) return;
            HasDied = true;
            Detonate();
            BDATargetManager.FiredMissiles.Remove(this);
            GameEvents.onPartDie.Remove(PartDie);
            Destroy(this); // If this is the active vessel, then KSP doesn't destroy it until we switch away, but we want to get rid of the MissileBase straight away.
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
            rcsFiredTimes = [0, 0, 0, 0];
            rcsTransforms = [upRCS, leftRCS, rightRCS, downRCS];
        }

        void DoRCS()
        {
            try
            {
                if (vacuumClearanceState == VacuumClearanceStates.Clearing || (TimeIndex < dropTime + Mathf.Min(0.5f, BDAMath.SolveTime(10f, currentThrust / part.mass)))) return; // Don't use RCS immediately after launch or when clearing a vessel to avoid running into VLS/SourceVessel
                Vector3 relV;
                if (vacuumClearanceState == VacuumClearanceStates.Turning && SourceVessel) // Clear away from launching vessel
                {
                    Vector3 relP = (part.transform.position - SourceVessel.CoM).normalized;
                    relV = relP + (vessel.Velocity() - SourceVessel.Velocity()).normalized.ProjectOnPlanePreNormalized(relP);
                    relV = 100f * relV.ProjectOnPlane(TargetPosition - part.transform.position);
                }
                else // Kill relative velocity to target
                    relV = TargetVelocity - vessel.Velocity();

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
                    if (BDArmorySettings.DEBUG_LINES && HasFired)
                    {
                        float burnTimeleft = 10 - Mathf.Min(((TimeIndex / (boostTime + cruiseTime)) * 10), 10);

                        GUIUtils.DrawLineBetweenWorldPositions(MissileReferenceTransform.position + MissileReferenceTransform.forward * burnTimeleft,
            MissileReferenceTransform.position + MissileReferenceTransform.forward * 10, 2, Color.red);
                        GUIUtils.DrawLineBetweenWorldPositions(MissileReferenceTransform.position,
        MissileReferenceTransform.position + MissileReferenceTransform.forward * burnTimeleft, 2, Color.green);
                    }
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
                /*case "aamhybrid":
                    GuidanceMode = GuidanceModes.AAMHybrid;
                    break;*/
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
                    GuidanceMode = GuidanceModes.Orbital;
                    break;

                case "orbital":
                    GuidanceMode = GuidanceModes.Orbital;
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

                case "kappa":
                    GuidanceMode = GuidanceModes.Kappa;
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

                case "inertial":
                    TargetingMode = TargetingModes.Inertial;
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

                case "inertial":
                    TargetingMode = TargetingModes.Inertial;
                    break;

                default:
                    TargetingModeTerminal = TargetingModes.None;
                    break;
            }

            terminalHomingType = terminalHomingType.ToLower();
            switch (terminalHomingType)
            {
                case "aam":
                    homingModeTerminal = GuidanceModes.AAMLead;
                    break;

                case "aamlead":
                    homingModeTerminal = GuidanceModes.AAMLead;
                    break;

                case "aampure":
                    homingModeTerminal = GuidanceModes.AAMPure;
                    break;
                case "aamloft":
                    homingModeTerminal = GuidanceModes.AAMLoft;
                    break;
                case "agm":
                    homingModeTerminal = GuidanceModes.AGM;
                    break;

                case "agmballistic":
                    homingModeTerminal = GuidanceModes.AGMBallistic;
                    break;

                case "cruise":
                    homingModeTerminal = GuidanceModes.Cruise;
                    break;

                case "sts":
                    homingModeTerminal = GuidanceModes.STS;
                    break;

                case "rcs":
                    homingModeTerminal = GuidanceModes.Orbital;
                    break;

                case "orbital":
                    homingModeTerminal = GuidanceModes.Orbital;
                    break;

                case "beamriding":
                    homingModeTerminal = GuidanceModes.BeamRiding;
                    break;

                case "slw":
                    homingModeTerminal = GuidanceModes.SLW;
                    break;

                case "pronav":
                    homingModeTerminal = GuidanceModes.PN;
                    break;

                case "augpronav":
                    homingModeTerminal = GuidanceModes.APN;
                    break;

                case "kappa":
                    homingModeTerminal = GuidanceModes.Kappa;
                    break;


                default:
                    homingModeTerminal = GuidanceModes.None;
                    break;
            }

            if (!terminalHoming && GuidanceMode == GuidanceModes.AAMLoft)
            {
                if (homingModeTerminal == GuidanceModes.None)
                {
                    homingModeTerminal = GuidanceModes.PN;
                    Debug.Log($"[BDArmory.MissileLauncher]: Error in configuration of {part.name}, homingType is AAMLoft but no terminal guidance mode was specified, defaulting to pro-nav.");
                }
                else if (!(homingModeTerminal == GuidanceModes.AAMLead || homingModeTerminal == GuidanceModes.AAMPure || homingModeTerminal == GuidanceModes.PN || homingModeTerminal == GuidanceModes.APN))
                {
                    terminalHoming = true;
                    Debug.LogWarning($"[BDArmory.MissileLauncher]: Error in configuration of {part.name}, homingType is AAMLoft but an unsupported terminalHomingType: {terminalHomingType} was used without setting terminalHoming = true. ");
                }
            }

            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileLauncher]: parsing guidance and homing complete on {GetPartName()}");
        }

        private string GetBrevityCode()
        {
            //torpedo: determine subtype
            if (missileType.ToLower() == "torpedo")
            {
                if (TargetingMode == TargetingModes.Radar && activeRadarRange > 0)
                    return "Active Sonar";

                if (TargetingMode == TargetingModes.Laser || TargetingMode == TargetingModes.Gps)
                    return "Optical/wireguided";

                if (TargetingMode == TargetingModes.Heat)
                {
                    if (activeRadarRange <= 0) return "Passive Sonar";
                    else return "Heat guided";
                }

                if (TargetingMode == TargetingModes.None)
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
            if (TargetingMode == TargetingModes.Inertial)
            {
                return TargetingModeTerminal != TargetingModes.None ? "Inertial/Terminal" : "Inertial";
            }
            if (TargetingMode == TargetingModes.None)
            {
                return TargetingModeTerminal != TargetingModes.None ? "Unguided/Terminal" : "Unguided";
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
            if (terminalHoming)
            {
                output.AppendLine($"Terminal Guidance Mode: {terminalHomingType.ToLower()} @ distance: {terminalHomingRange} m");
            }
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
                output.AppendLine($"Max Offboresight: {maxOffBoresight}");
                output.AppendLine($"Locked FOV: {lockedSensorFOV}");
            }

            if (TargetingMode == TargetingModes.Heat)
            {
                output.AppendLine($"Uncaged Lock: {uncagedLock}");
                output.AppendLine($"Min Heat threshold: {heatThreshold}");
                output.AppendLine($"Max Offboresight: {maxOffBoresight}");
                output.AppendLine($"Locked FOV: {lockedSensorFOV}");
            }

            if (TargetingMode == TargetingModes.Gps || TargetingMode == TargetingModes.None || TargetingMode == TargetingModes.Inertial)
            {
                output.AppendLine($"Terminal Maneuvering: {terminalGuidanceShouldActivate}");
                if (terminalGuidanceType != "")
                {
                    output.AppendLine($"Terminal Targeting: {terminalGuidanceType} @ distance: {terminalGuidanceDistance} m");

                    if (TargetingModeTerminal == TargetingModes.Radar)
                    {
                        output.AppendLine($"Active Radar Range: {activeRadarRange} m");
                        if (activeRadarLockTrackCurve.maxTime > 0)
                            output.AppendLine($"- Lock/Track: {activeRadarLockTrackCurve.Evaluate(activeRadarLockTrackCurve.maxTime)} m^2 @ {activeRadarLockTrackCurve.maxTime} km");
                        else
                            output.AppendLine($"- Lock/Track: {RadarUtils.MISSILE_DEFAULT_LOCKABLE_RCS} m^2 @ {activeRadarRange / 1000} km");
                        output.AppendLine($"- LOAL: {radarLOAL}");
                        if (radarLOAL) output.AppendLine($"  - Radar Search Time: {radarTimeout}");
                        output.AppendLine($"Max Offboresight: {maxOffBoresight}");
                        output.AppendLine($"Locked FOV: {lockedSensorFOV}");
                    }

                    if (TargetingModeTerminal == TargetingModes.Heat)
                    {
                        output.AppendLine($"Uncaged Lock: {uncagedLock}");
                        output.AppendLine($"Min Heat threshold: {heatThreshold}");
                        output.AppendLine($"Max Offboresight: {maxOffBoresight}");
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
                    if (((BDExplosivePart)partModules.Current).warheadType == "shapedcharge")
                        output.AppendLine($"- Penetration: {ProjectileUtils.CalculatePenetration(((BDExplosivePart)partModules.Current).caliber > 0 ? ((BDExplosivePart)partModules.Current).caliber * 0.05f : 6f * 0.05f, 5000f, ((BDExplosivePart)partModules.Current).tntMass * 0.0555f, ((BDExplosivePart)partModules.Current).apMod):F2} mm");
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