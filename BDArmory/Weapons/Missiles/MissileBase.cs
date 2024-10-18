using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

using BDArmory.Competition;
using BDArmory.Control;
using BDArmory.CounterMeasure;
using BDArmory.Extensions;
using BDArmory.FX;
using BDArmory.Guidances;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;

namespace BDArmory.Weapons.Missiles
{
    public abstract class MissileBase : EngageableWeapon, IBDWeapon
    {
        // High Speed missile fix
        /// //////////////////////////////////
        [KSPField(isPersistant = true)]
        public float DetonationOffset = 0.1f;

        [KSPField(isPersistant = true)]
        public bool autoDetCalc = false;
        /// //////////////////////////////////

        protected WeaponClasses weaponClass;

        public WeaponClasses GetWeaponClass()
        {
            return weaponClass;
        }

        ModuleWeapon weap = null;
        public ModuleWeapon GetWeaponModule()
        {
            return weap;
        }

        public string GetMissileType()
        {
            return missileType;
        }

        public string GetPartName()
        {
            return missileName;
        }

        public float GetEngageRange()
        {
            return GetEngagementRangeMax();
        }

        public string missileName { get; set; } = "";

        [KSPField(isPersistant = false, guiActive = true, guiName = "Launched from"), UI_Label(scene = UI_Scene.Flight)]
        public string SourceVesselName;
        [KSPField(isPersistant = false, guiActive = true, guiName = "Launched at"), UI_Label(scene = UI_Scene.Flight)]
        public string TargetVesselName;

        [KSPField]
        public string missileType = "missile";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_MaxStaticLaunchRange"), UI_FloatRange(minValue = 5000f, maxValue = 50000f, stepIncrement = 1000f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Max Static Launch Range
        public float maxStaticLaunchRange = 5000;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_MinStaticLaunchRange"), UI_FloatRange(minValue = 10f, maxValue = 4000f, stepIncrement = 100f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Min Static Launch Range
        public float minStaticLaunchRange = 10;

        public float StandOffDistance = -1;

        [KSPField]
        public float minLaunchSpeed = 0;

        public virtual float ClearanceRadius => 0.14f;

        public virtual float ClearanceLength => 0.14f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_MaxOffBoresight"),//Max Off Boresight
            UI_FloatRange(minValue = 0f, maxValue = 360f, stepIncrement = 5f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float maxOffBoresight = 360;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_DetonationDistanceOverride"), UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Detonation distance override
        public float DetonationDistance = -1;
        public float DetonationDistanceSqr => DetonationDistance > 0 ? DetonationDistance * DetonationDistance : -1; // Account for the -1 special value when checking against Sqr distance.

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_DetonateAtMinimumDistance"), // Detonate At Minumum Distance
            UI_Toggle(disabledText = "#LOC_BDArmory_false", enabledText = "#LOC_BDArmory_true", scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        public bool DetonateAtMinimumDistance = false;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_UseStaticMaxLaunchRange", advancedTweakable = true), // Use Static Max Launch Range
            UI_Toggle(disabledText = "#LOC_BDArmory_dynamic", enabledText = "#LOC_BDArmory_static", scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        public bool UseStaticMaxLaunchRange = false;

        //[KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "SLW Offset"), UI_FloatRange(minValue = -1000f, maxValue = 0f, stepIncrement = 100f, affectSymCounterparts = UI_Scene.All)]
        public float SLWOffset = 0;

        public float getSWLWOffset
        {
            get
            {
                return SLWOffset;
            }
        }

        [KSPField]
        public float engineFailureRate = 0f;                              // How often the missile engine will fail to start (0-1), evaluated once on missile launch

        [KSPField]
        public float guidanceFailureRate = 0f;                              // Probability the missile guidance will fail per second (0-1), evaluated every frame after launch

        public float guidanceFailureRatePerFrame = 0f;                      // guidanceFailureRate (per second) converted to per frame probability

        [KSPField]
        public bool guidanceActive = true;

        [KSPField]
        public float gpsUpdates = -1f;                              // GPS missiles get updates on target position from source vessel every gpsUpdates >= 0 seconds

        public float GpsUpdateMax = -1f;

        [KSPField]
        public float lockedSensorFOV = 2.5f;

        [KSPField]
        public FloatCurve lockedSensorFOVBias = new FloatCurve();             // weighting of targets and flares from center (0) to edge of FOV (lockedSensorFOV)

        [KSPField]
        public FloatCurve lockedSensorVelocityBias = new FloatCurve();             // weighting of targets and flares from velocity angle of prior target and new target aligned (0) to opposite (180)

        [KSPField]
        public float heatThreshold = 150;

        [KSPField]
        public float frontAspectHeatModifier = 1f;                   // Modifies heat value returned to missiles outside of ~50 deg exhaust cone from non-prop engines. Only takes affect when ASPECTED_IR_SEEKERS = true in settings.cfg

        [KSPField]
        public float chaffEffectivity = 1f;                            // Modifies how the missile targeting is affected by chaff, 1 is fully affected (normal behavior), lower values mean less affected (0 is ignores chaff), higher values means more affected

        [KSPField]
        public float flareEffectivity = 1f;                            // Modifies how the missile targeting is affected by flares, 1 is fully affected (normal behavior), lower values mean less affected (0 is ignores flares), higher values means more affected

        [KSPField]
        public bool allAspect = false;                                 // DEPRECATED, replaced by uncagedIRLock. uncagedIRLock is automatically set to this value upon loading (to maintain compatability with old BDA mods)

        [KSPField]
        public bool uncagedLock = false;                             //if true it simulates a modern IR missile with "uncaged lock" ability. Even if the target is not within boresight fov, it can be radar locked and the target information transfered to the missile. It will then try to lock on with the heat seeker. If false, it is an older missile which requires a direct "in boresight" lock.

        [KSPField]
        public bool targetCoM = false;                             //if true, IR missile targets the center of a craft, false IR missile targets hottest part

        [KSPField]
        public bool isTimed = false;

        [KSPField]
        public bool radarLOAL = false;                              //if true, radar missile will acquire and lock onto a target after launch, using the missile's onboard radar

        [KSPField]
        public bool canRelock = true;                               //if true, if a FCS radar guiding a SARH missile loses lock, the missile will be switched to the active radar lock instead of going inactive from target loss.

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_DropTime"),//Drop Time
            UI_FloatRange(minValue = 0f, maxValue = 5f, stepIncrement = 0.1f, scene = UI_Scene.Editor)]
        public float dropTime = 0.5f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_InCargoBay"),//In Cargo Bay: 
            UI_Toggle(disabledText = "#LOC_BDArmory_false", enabledText = "#LOC_BDArmory_true", affectSymCounterparts = UI_Scene.All)]//False--True
        public bool inCargoBay = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_InCustomCargoBay"), // In custom/modded "cargo bay"
            UI_ChooseOption(
            options = new string[] {
                "0",
                "1",
                "2",
                "3",
                "4",
                "5",
                "6",
                "7",
                "8",
                "9",
                "10",
                "11",
                "12",
                "13",
                "14",
                "15",
                "16"
            },
            display = new string[] {
                "Disabled",
                "AG1",
                "AG2",
                "AG3",
                "AG4",
                "AG5",
                "AG6",
                "AG7",
                "AG8",
                "AG9",
                "AG10",
                "Lights",
                "RCS",
                "SAS",
                "Brakes",
                "Abort",
                "Gear"
            }
        )]
        public string customBayGroup = "0";

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_DetonationTime"),//Detonation Time
            UI_FloatRange(minValue = 2f, maxValue = 30f, stepIncrement = 0.5f, scene = UI_Scene.Editor)]
        public float detonationTime = 2;

        [KSPField]
        public float activeRadarRange = 6000;

        [Obsolete("Use activeRadarLockTrackCurve!")]
        [KSPField]
        public float activeRadarMinThresh = 140;

        [KSPField]
        public FloatCurve activeRadarLockTrackCurve = new FloatCurve();             // floatcurve to define min/max range and lockable radar cross section

        [KSPField]
        public FloatCurve activeRadarVelocityGate = new FloatCurve();

        [KSPField]
        public float activeRadarVelocityFilter = 50f;

        [KSPField]
        public FloatCurve activeRadarRangeGate = new FloatCurve();

        [KSPField]
        public float activeRadarRangeFilter = 2000f;

        [KSPField]
        public float activeRadarMinTrackSCR = 1f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_BallisticOvershootFactor"),//Ballistic Overshoot factor
         UI_FloatRange(minValue = 0.5f, maxValue = 1.5f, stepIncrement = 0.01f, scene = UI_Scene.Editor)]
        public float BallisticOverShootFactor = 0.7f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_BallisticAnglePath"),//Ballistic Angle path
         UI_FloatRange(minValue = 5f, maxValue = 60f, stepIncrement = 5f, scene = UI_Scene.Editor)]
        public float BallisticAngle = 45.0f;

        [KSPField]
        public float inertialDrift = 0.05f; //meters/sec

        private Vector3 driftSeed = Vector3.zero;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_CruiseAltitude"), UI_FloatRange(minValue = 5f, maxValue = 500f, stepIncrement = 5f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Cruise Altitude
        public float CruiseAltitude = 500;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_Missile_CruiseSpeed"), UI_FloatRange(minValue = 100f, maxValue = 6000f, stepIncrement = 50f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Cruise speed
        public float CruiseSpeed = 300;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_CruisePredictionTime"), UI_FloatRange(minValue = 1f, maxValue = 15f, stepIncrement = 1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Cruise prediction time
        public float CruisePredictionTime = 5;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftMaxAltitude"), UI_FloatRange(minValue = 5000f, maxValue = 30000f, stepIncrement = 100f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Loft Max Altitude
        public float LoftMaxAltitude = 16000;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftRangeOverride"), UI_FloatRange(minValue = 500f, maxValue = 25000f, stepIncrement = 100f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Loft Altitude Difference
        public float LoftRangeOverride = 15000;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftAltitudeAdvMax"), UI_FloatRange(minValue = 500f, maxValue = 5000f, stepIncrement = 100f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Loft Maximum Altitude Advantage
        public float LoftAltitudeAdvMax = 3000;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftMinAltitude"), UI_FloatRange(minValue = 0f, maxValue = 10000f, stepIncrement = 100f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Loft Maximum Altitude Advantage
        public float LoftMinAltitude = 6000;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftAngle"), UI_FloatRange(minValue = 0f, maxValue = 90f, stepIncrement = 0.5f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Loft Angle
        public float LoftAngle = 45;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftTermAngle"), UI_FloatRange(minValue = 0f, maxValue = 90f, stepIncrement = 0.5f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Loft Termination Angle
        public float LoftTermAngle = 20;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftRangeFac"),//Loft Range Factor
         UI_FloatRange(minValue = 0.1f, maxValue = 5.0f, stepIncrement = 0.01f, scene = UI_Scene.Editor)]
        public float LoftRangeFac = 0.5f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftVelComp"),//Loft Velocity Compensation (Horizontal)
         UI_FloatRange(minValue = -2.0f, maxValue = 2.0f, stepIncrement = 0.01f, scene = UI_Scene.Editor)]
        public float LoftVelComp = -0.5f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftVertVelComp"),//Loft Velocity Compensation (Vertical)
         UI_FloatRange(minValue = -2.0f, maxValue = 2.0f, stepIncrement = 0.01f, scene = UI_Scene.Editor)]
        public float LoftVertVelComp = -0.5f;

        //[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftAltComp"), UI_FloatRange(minValue = -2000f, maxValue = 2000f, stepIncrement = 10f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Loft Altitude Compensation
        //public float LoftAltComp = 0;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_terminalHomingRange"), UI_FloatRange(minValue = 500f, maxValue = 20000f, stepIncrement = 100f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Terminal Homing Range
        public float terminalHomingRange = 3000;

        [KSPField]
        public bool terminalHoming = false;

        [KSPField]
        public float kappaAngle = 45; // Kappa Guidance Vertical Shaping Angle

        [KSPField]
        public float missileRadarCrossSection = RadarUtils.RCS_MISSILES;            // radar cross section of this missile for detection purposes

        public enum MissileStates { Idle, Drop, Boost, Cruise, PostThrust }

        public enum DetonationDistanceStates { NotSafe, Cruising, CheckingProximity, Detonate }

        public enum TargetingModes { None, Radar, Heat, Laser, Gps, AntiRad, Inertial }

        public MissileStates MissileState { get; set; } = MissileStates.Idle;

        public DetonationDistanceStates DetonationDistanceState { get; set; } = DetonationDistanceStates.NotSafe;

        public enum GuidanceModes { None, AAMLead, AAMPure, AGM, AGMBallistic, Cruise, STS, Bomb, Orbital, BeamRiding, SLW, PN, APN, AAMLoft, Kappa }

        public GuidanceModes GuidanceMode;

        public enum WarheadTypes { Kinetic, Standard, ContinuousRod, Custom, CustomStandard, CustomContinuous, EMP, Nuke, Legacy, Launcher }

        public WarheadTypes warheadType = WarheadTypes.Kinetic;
        public bool HasFired { get; set; } = false;

        public bool launched = false;

        public BDTeam Team { get; set; } = BDTeam.Get("Neutral");

        public bool HasMissed { get; set; } = false;

        public Vector3 TargetPosition { get; set; } = Vector3.zero;

        public Vector3 TargetVelocity { get; set; } = Vector3.zero;

        public Vector3 TargetAcceleration { get; set; } = Vector3.zero;

        public float TimeIndex => Time.time - TimeFired;

        public TargetingModes TargetingMode { get; set; }

        public TargetingModes TargetingModeTerminal { get; set; }

        public GuidanceModes homingModeTerminal { get; set; }

        public bool terminalHomingActive = false;

        public float TimeToImpact { get; set; }

        public enum LoftStates { Boost, Midcourse, Terminal }

        public LoftStates loftState = LoftStates.Boost;

        public bool TargetAcquired { get; set; }

        public bool ActiveRadar { get; set; }

        public Vessel SourceVessel
        {
            get { return _sourceVessel; }
            set
            {
                _sourceVessel = value;
                SourceVesselName = SourceVessel != null ? SourceVessel.vesselName : "";
            }
        }
        Vessel _sourceVessel = null;

        public bool HasExploded { get; set; } = false;

        public bool FuseFailed { get; set; } = false;

        public bool HasDied { get; set; } = false;

        public int clusterbomb { get; set; } = 1;

        protected IGuidance _guidance;

        public enum VacuumClearanceStates { Clearing, Turning, Cleared }
        public VacuumClearanceStates vacuumClearanceState = VacuumClearanceStates.Cleared;
        private readonly string[] VacuumClearanceStatesString = new string[3] { "Clearing", "Turning", "Cleared" };

        private double _lastVerticalSpeed;
        private double _lastHorizontalSpeed;
        private int gpsUpdateCounter = 0;

        public double HorizontalAcceleration
        {
            get
            {
                var result = (vessel.horizontalSrfSpeed - _lastHorizontalSpeed);
                _lastHorizontalSpeed = vessel.horizontalSrfSpeed;
                return result;

            }
        }

        public double VerticalAcceleration
        {
            get
            {
                var result = (vessel.horizontalSrfSpeed - _lastHorizontalSpeed);
                _lastVerticalSpeed = vessel.verticalSpeed;
                return result;
            }
        }



        public float Throttle
        {
            get
            {
                return _throttle;
            }

            set
            {
                _throttle = Mathf.Clamp01(value);
            }
        }

        public float TimeFired = -1;

        protected float lockFailTimer = -1;

        public TargetInfo targetVessel
        {
            get
            {
                if (_targetVessel != null && _targetVessel.Vessel == null) _targetVessel = null; // The vessel could die before _targetVessel gets cleared otherwise.
                return _targetVessel;
            }
            set
            {
                _targetVessel = value;
                if (_targetVessel != null && _targetVessel.Vessel != null)
                    TargetVesselName = _targetVessel.Vessel.vesselName;
            }
        }
        TargetInfo _targetVessel;

        public Transform MissileReferenceTransform;

        protected ModuleTargetingCamera targetingPod;

        //laser stuff
        public ModuleTargetingCamera lockedCamera;
        protected Vector3 lastLaserPoint;
        protected Vector3 laserStartPosition;
        protected Vector3 startDirection;

        //GPS stuff
        public Vector3d targetGPSCoords;

        //heat stuff
        public TargetSignatureData heatTarget;
        private TargetSignatureData predictedHeatTarget;

        //radar stuff
        public VesselRadarData vrd;
        public TargetSignatureData radarTarget;
        private TargetSignatureData[] scannedTargets;
        public MissileFire TargetMf = null;
        private LineRenderer LR;

        //INS stuff
        public Vector3d TargetINSCoords { get; set; } = Vector3d.zero;
        public float TimeOfLastINS = 0;
        public float INStimetogo = 0;

        private int snapshotTicker;
        private int locksCount = 0;
        private float _radarFailTimer = 0;

        [KSPField] public float radarTimeout = 5;
        private float lastRWRPing = 0;
        private bool radarLOALSearching = false;
        private bool hasLostLock = false;
        protected bool checkMiss = false;
        public StringBuilder debugString = new StringBuilder();

        private float _throttle = 1f;

        public string Sublabel;
        public int missilecount = 0; //#191
        RaycastHit[] proximityHits = new RaycastHit[100];
        Collider[] proximityHitColliders = new Collider[100];
        int layerMask = (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.Unknown19 | LayerMasks.Wheels);

        /// <summary>
        /// Make corrections for floating origin and Krakensbane adjustments.
        /// This can't simply be in OnFixedUpdate as it needs to be called differently for MissileLauncher (which uses OnFixedUpdate) and BDModularGuidance (which uses FlyByWire which triggers before OnFixedUpdate).
        /// </summary>
        public void FloatingOriginCorrection()
        {
            if (HasFired && !HasExploded)
            {
                if (BDKrakensbane.IsActive)
                {
                    // Debug.Log($"DEBUG {Time.time} Correcting for floating origin shift of {(Vector3)BDKrakensbane.FloatingOriginOffset:G3} ({(Vector3)BDKrakensbane.FloatingOriginOffsetNonKrakensbane:G3}) for {vessel.vesselName} ({SourceVessel})");
                    TargetPosition -= BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
                }
            }
        }

        public ModuleMissileRearm reloadableRail = null;
        public bool hasAmmo = false;
        int AmmoCount // Returns the ammo count if the part contains ModuleMissileRearm, otherwise 1.
        {
            get
            {
                if (!hasAmmo) return 1;
                return (int)reloadableRail.railAmmo;
            }
        }
        public bool isMMG = false;

        public override void OnAwake()
        {
            base.OnAwake();
            var MMG = GetPart().FindModuleImplementing<BDModularGuidance>();
            if (MMG != null)
            {
                hasAmmo = false;
                isMMG = true;
            }
        }

        public void GetMissileCount() // could stick this in GetSublabel, but that gets called every frame by BDArmorySetup?
        {
            missilecount = 0;
            if (part is null) return;
            var missilePartName = GetPartName();
            if (string.IsNullOrEmpty(missilePartName)) return;
            using (var craftPart = VesselModuleRegistry.GetMissileBases(vessel).GetEnumerator())
                while (craftPart.MoveNext())
                {
                    if (craftPart.Current is null) continue;
                    if (craftPart.Current.GetPartName() != missilePartName) continue;
                    if (craftPart.Current.engageRangeMax != engageRangeMax) continue;
                    missilecount += craftPart.Current.AmmoCount;
                }
            if (hasAmmo) missilecount += (int)reloadableRail.magazineAmmo;
        }

        public string GetSubLabel()
        {
            return Sublabel = $"Guidance: {Enum.GetName(typeof(TargetingModes), TargetingMode)}; Max Range: {Mathf.Round(engageRangeMax / 100) / 10} km; Remaining: {missilecount}";
        }

        public Part GetPart()
        {
            return part;
        }

        public abstract void FireMissile();

        public abstract void Jettison();

        public abstract float GetBlastRadius();

        protected abstract void PartDie(Part p);

        protected void DisablingExplosives(Part p)
        {
            if (p == null) return;

            var explosive = p.FindModuleImplementing<BDExplosivePart>();
            if (explosive != null)
            {
                p.FindModuleImplementing<BDExplosivePart>().Armed = false;
            }

            var emp = p.FindModuleImplementing<ModuleEMP>();
            if (emp != null) emp.Armed = false;

            var customWarhead = p.FindModuleImplementing<BDCustomWarhead>();
            if (customWarhead != null) customWarhead.Armed = false;
        }

        protected void SetupExplosive(Part p)
        {
            if (p == null) return;

            var explosive = p.FindModuleImplementing<BDExplosivePart>();
            if (explosive != null)
            {
                explosive.Armed = true;
                explosive.detonateAtMinimumDistance = DetonateAtMinimumDistance;
                //if (GuidanceMode == GuidanceModes.AGM || GuidanceMode == GuidanceModes.AGMBallistic)
                //{
                //    explosive.Shaped = true; //Now configed in the part's BDExplosivePart Module Node
                //}
            }

            var emp = p.FindModuleImplementing<ModuleEMP>();
            if (emp != null) emp.Armed = true;

            var customWarhead = p.FindModuleImplementing<BDCustomWarhead>();
            if (customWarhead != null)
            {
                customWarhead.Armed = true;
                customWarhead.detonateAtMinimumDistance = DetonateAtMinimumDistance;
            }
        }

        public abstract void Detonate();

        public abstract Vector3 GetForwardTransform();

        public abstract float GetKinematicTime();

        public abstract float GetKinematicSpeed();

        protected void AddTargetInfoToVessel()
        {
            TargetInfo info = vessel.gameObject.AddComponent<TargetInfo>();
            info.Team = Team;
            info.isMissile = true;
            info.MissileBaseModule = this;
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_GPSTarget", active = true, name = "GPSTarget")]//GPS Target
        public void assignGPSTarget()
        {
            if (HighLogic.LoadedSceneIsFlight)
                PickGPSTarget();
        }

        [KSPField(isPersistant = true)]
        public bool gpsSet = false;

        [KSPField(isPersistant = true)]
        public Vector3 assignedGPSCoords;

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_GPSTarget")]//GPS Target
        public string gpsTargetName = "Unknown"; // Can't have an empty name as it breaks KSP's flightstate autosave.



        void PickGPSTarget()
        {
            gpsSet = true;
            Fields["gpsTargetName"].guiActive = true;
            gpsTargetName = BDArmorySetup.Instance.ActiveWeaponManager.designatedGPSInfo.name;
            assignedGPSCoords = BDArmorySetup.Instance.ActiveWeaponManager.designatedGPSCoords;
        }

        public Vector3d UpdateGPSTarget()
        {
            Vector3 gpsTargetCoords_;

            if (gpsSet && assignedGPSCoords != null)
            {
                gpsTargetCoords_ = assignedGPSCoords;
            }
            else
            {
                gpsTargetCoords_ = targetGPSCoords;
                if (targetVessel && HasFired && (gpsUpdates >= 0f))
                {
                    TargetSignatureData t = TargetSignatureData.noTarget;
                    TargetPosition = Vector3.zero;
                    UpdateLaserTarget(); //available cam for new GPS coords?
                    if (TargetPosition == Vector3.zero && vrd && vrd.locked)//no cam; available radar lock?
                    {
                        List<TargetSignatureData> possibleTargets = vrd.GetLockedTargets();
                        for (int i = 0; i < possibleTargets.Count; i++)
                        {
                            if (possibleTargets[i].vessel == targetVessel.Vessel)
                                t = possibleTargets[i];
                        }
                        if (t.exists) TargetPosition = t.position;
                    }
                    if (TargetPosition != Vector3.zero)
                    {
                        float distanceToTargetSqr = (vessel.transform.position - gpsTargetCoords_).sqrMagnitude;
                        float jamDistance = RadarUtils.GetVesselECMJammingDistance(targetVessel.Vessel); //does the target have a jammer, and is the missile within the jammed AoE
                        if (jamDistance * jamDistance < distanceToTargetSqr) //outside/no area of interference, can receive GPS signal
                        {
                            //var weaponManager = VesselModuleRegistry.GetMissileFire(SourceVessel);
                            //if (weaponManager != null && weaponManager.CanSeeTarget(targetVessel, false))

                            if (gpsUpdates == 0) // Constant updates
                            {
                                gpsTargetCoords_ = VectorUtils.WorldPositionToGeoCoords(targetVessel.Vessel.CoM, targetVessel.Vessel.mainBody);
                                targetGPSCoords = gpsTargetCoords_;
                            }
                            else // Update every gpsUpdates seconds
                            {
                                float updateCount = TimeIndex / gpsUpdates;
                                if (updateCount > gpsUpdateCounter)
                                {
                                    gpsUpdateCounter++;
                                    gpsTargetCoords_ = VectorUtils.WorldPositionToGeoCoords(targetVessel.Vessel.CoM, targetVessel.Vessel.mainBody);
                                    targetGPSCoords = gpsTargetCoords_;
                                }
                            }
                        }
                    }
                    //else 
                    // In theory if the jammer knew the GPS receiver channel/encryption, could transmit false coords using a more powerful signal to override out the originals...
                    //currently just cuts off updates and ordinance heads to last valid coords. Instead have jammer Strength come into play and have it be a jStrength * 4prDist^2 check that slows update 
                    //frequency/increases the RNG threshold to make the GPS update?
                }
            }

            if (TargetAcquired)
            {
                TargetPosition = VectorUtils.GetWorldSurfacePostion(gpsTargetCoords_, vessel.mainBody);
                TargetVelocity = Vector3.zero;
                TargetAcceleration = Vector3.zero;
            }
            else
            {
                guidanceActive = false;
            }

            return gpsTargetCoords_;
        }

        protected void UpdateHeatTarget()
        {

            if (lockFailTimer > 1)
            {
                targetVessel = null;
                TargetAcquired = false;
                predictedHeatTarget.exists = false;
                predictedHeatTarget.signalStrength = 0; //have this instead set to originalHeatTarget missile had on initial lock?
                return;
            }

            if (heatTarget.exists && lockFailTimer < 0)
            {
                lockFailTimer = 0;
                predictedHeatTarget = heatTarget;
            }
            if (lockFailTimer >= 0)
            {
                // Decide where to point seeker
                Ray lookRay;
                if (predictedHeatTarget.exists) // We have an active target we've been seeking, or a prior target that went stale
                {
                    lookRay = new Ray(transform.position, predictedHeatTarget.position - transform.position);
                }
                else if (heatTarget.exists) // We have a new active target and no prior target
                {
                    lookRay = new Ray(transform.position, heatTarget.position + (heatTarget.velocity * Time.fixedDeltaTime) - transform.position);
                }
                else // No target, look straight ahead
                {
                    lookRay = new Ray(transform.position, MissileReferenceTransform.forward);
                }

                // Prevent seeker from looking past maxOffBoresight
                float offBoresightAngle = Vector3.Angle(GetForwardTransform(), lookRay.direction);
                if (offBoresightAngle > maxOffBoresight)
                    lookRay = new Ray(lookRay.origin, Vector3.RotateTowards(lookRay.direction, GetForwardTransform(), (offBoresightAngle - maxOffBoresight) * Mathf.Deg2Rad, 0));

                DrawDebugLine(lookRay.origin, lookRay.origin + lookRay.direction * 10000, predictedHeatTarget.exists ? Color.magenta : heatTarget.exists ? Color.yellow : Color.blue);
                //Debug.Log($"[MissileBase] offboresightAngle {offBoresightAngle > maxOffBoresight}; lockFailtimer: {lockFailTimer}; heatTarget? {heatTarget.exists}; predictedheattaret? {predictedHeatTarget.exists}");
                // Update heat target
                if (activeRadarRange < 0)
                    heatTarget = BDATargetManager.GetAcousticTarget(SourceVessel, vessel, lookRay, predictedHeatTarget, lockedSensorFOV / 2, heatThreshold, targetCoM, lockedSensorFOVBias, lockedSensorVelocityBias,
                        (SourceVessel == null ? null : SourceVessel.gameObject == null ? null : SourceVessel.gameObject.GetComponent<MissileFire>()), targetVessel);
                else
                    heatTarget = BDATargetManager.GetHeatTarget(SourceVessel, vessel, lookRay, predictedHeatTarget, lockedSensorFOV / 2, heatThreshold, frontAspectHeatModifier, uncagedLock, targetCoM, lockedSensorFOVBias, lockedSensorVelocityBias, (SourceVessel == null ? null : SourceVessel.gameObject == null ? null : SourceVessel.gameObject.GetComponent<MissileFire>()), targetVessel);

                if (heatTarget.exists)
                {
                    TargetAcquired = true;
                    TargetPosition = heatTarget.position;
                    TargetVelocity = heatTarget.velocity;
                    TargetAcceleration = heatTarget.acceleration;
                    //targetVessel = heatTarget.targetInfo;
                    lockFailTimer = 0;

                    // Update target information
                    // if (heatTarget.vessel != predictedHeatTarget.vessel) Debug.LogError($"[IR DEBUG] Switching targets from {predictedHeatTarget.vessel.vesselName} to {heatTarget.vessel.vesselName}");
                    predictedHeatTarget = heatTarget;
                }
                else
                {
                    lockFailTimer += Time.fixedDeltaTime;
                }

                // Update predicted values based on target information
                if (predictedHeatTarget.exists)
                {
                    float currentFactor = (1400 * 1400) / Mathf.Clamp((predictedHeatTarget.position - transform.position).sqrMagnitude, 90000, 36000000);
                    Vector3 currVel = vessel.Velocity();
                    predictedHeatTarget.position = predictedHeatTarget.position + predictedHeatTarget.velocity * Time.fixedDeltaTime;
                    predictedHeatTarget.velocity = predictedHeatTarget.velocity + predictedHeatTarget.acceleration * Time.fixedDeltaTime;
                    float futureFactor = (1400 * 1400) / Mathf.Clamp((predictedHeatTarget.position - (transform.position + (currVel * Time.fixedDeltaTime))).sqrMagnitude, 90000, 36000000);
                    predictedHeatTarget.signalStrength *= futureFactor / currentFactor;
                }

            }
        }

        protected void SetAntiRadTargeting()
        {
            if (TargetingMode == TargetingModes.AntiRad && TargetAcquired)
            {
                RadarWarningReceiver.OnRadarPing += ReceiveRadarPing;
            }
        }

        protected void SetLaserTargeting()
        {
            if (TargetingMode == TargetingModes.Laser)
            {
                laserStartPosition = MissileReferenceTransform.position;
                if (lockedCamera)
                {
                    TargetAcquired = true;
                    TargetPosition = lastLaserPoint = lockedCamera.groundTargetPosition;
                    targetingPod = lockedCamera;
                }
            }
        }

        protected void UpdateLaserTarget()
        {
            if (TargetAcquired)
            {
                if (lockedCamera && lockedCamera.groundStabilized && !lockedCamera.gimbalLimitReached && lockedCamera.surfaceDetected) //active laser target
                {
                    TargetPosition = lockedCamera.groundTargetPosition;
                    TargetVelocity = (TargetPosition - lastLaserPoint) / Time.fixedDeltaTime;
                    TargetAcceleration = Vector3.zero;
                    lastLaserPoint = TargetPosition;

                    if (GuidanceMode == GuidanceModes.BeamRiding && TimeIndex > 0.25f && Vector3.Dot(GetForwardTransform(), part.transform.position - lockedCamera.transform.position) < 0)
                    {
                        TargetAcquired = false;
                        lockedCamera = null;
                    }
                }
                else //lost active laser target, home on last known position
                {
                    if (CMSmoke.RaycastSmoke(new Ray(transform.position, lastLaserPoint - transform.position)))
                    {
                        //Debug.Log("[BDArmory.MissileBase]: Laser missileBase affected by smoke countermeasure");
                        float angle = VectorUtils.FullRangePerlinNoise(0.75f * Time.time, 10) * BDArmorySettings.SMOKE_DEFLECTION_FACTOR;
                        TargetPosition = VectorUtils.RotatePointAround(lastLaserPoint, transform.position, VectorUtils.GetUpDirection(transform.position), angle);
                        TargetVelocity = Vector3.zero;
                        TargetAcceleration = Vector3.zero;
                        lastLaserPoint = TargetPosition;
                    }
                    else
                    {
                        TargetPosition = lastLaserPoint;
                    }
                }
            }
            else
            {
                ModuleTargetingCamera foundCam = null;
                bool parentOnly = (GuidanceMode == GuidanceModes.BeamRiding);
                foundCam = BDATargetManager.GetLaserTarget(this, parentOnly);
                if (foundCam != null && foundCam.cameraEnabled && foundCam.groundStabilized && BDATargetManager.CanSeePosition(foundCam.groundTargetPosition, vessel.transform.position, MissileReferenceTransform.position))
                {
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileBase]: Laser guided missileBase actively found laser point. Enabling guidance.");
                    lockedCamera = foundCam;
                    TargetAcquired = true;
                }
            }
        }

        protected void UpdateRadarTarget()
        {
            TargetAcquired = false;

            if (radarTarget.exists)
            {
                float angleToTarget = Vector3.Angle(radarTarget.predictedPosition - transform.position, GetForwardTransform());
                // locked-on before launch, passive radar guidance or waiting till in active radar range:
                if (!ActiveRadar && ((radarTarget.predictedPosition - transform.position).sqrMagnitude > (activeRadarRange * activeRadarRange) || angleToTarget > maxOffBoresight * 0.75f))
                {
                    if (vrd && vrd.locked)
                    {
                        TargetSignatureData t = TargetSignatureData.noTarget;
                        if (canRelock && hasLostLock)
                        {
                            if (vrd.locked)
                            {
                                t = vrd.lockedTargetData.targetData; //SARH is passive, and guided towards whatever is currently painted by FCS radar
                                //Debug.Log($"[MML RADAR DEBUG] missile switched target to {t.vessel.GetName()}");
                            }
                        }
                        else
                        {
                            List<TargetSignatureData> possibleTargets = vrd.GetLockedTargets();
                            for (int i = 0; i < possibleTargets.Count; i++)
                            {
                                if (possibleTargets[i].vessel == radarTarget.vessel) //this means SARh will remain locked to whatever was the initial target, regardless of current radar lock
                                {
                                    t = possibleTargets[i];
                                }
                            }
                        }
                        if (t.exists)
                        {
                            TargetAcquired = true;
                            hasLostLock = false;
                            radarTarget = t;
                            //if (weaponClass == WeaponClasses.SLW) //Radar/Active Sonar guidance would be vulnerable to chaff/various acoustic CMs that function basically like chaff, so commenting this out
                            //{
                            //    TargetPosition = radarTarget.predictedPosition;
                            //}
                            //else
                            TargetPosition = radarTarget.predictedPositionWithChaffFactor(!ActiveRadar ? vrd.lockedTargetData.detectedByRadar.radarChaffClutterFactor : chaffEffectivity);
                            TargetVelocity = radarTarget.velocity;
                            TargetAcceleration = radarTarget.acceleration;
                            targetVessel = t.targetInfo; //reset targetvessel in case of canRelock getting a new target
                            _radarFailTimer = 0;
                            return;
                        }
                        else
                        {
                            if (_radarFailTimer > radarTimeout)
                            {
                                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileBase]: Semi-Active Radar guidance failed. Parent radar lost target.");
                                radarTarget = TargetSignatureData.noTarget;
                                targetVessel = null;
                                return;
                            }
                            else
                            {
                                if (_radarFailTimer == 0)
                                {
                                    if (BDArmorySettings.DEBUG_MISSILES)
                                        Debug.Log("[BDArmory.MissileBase]: Semi-Active Radar guidance failed - waiting for data");
                                    hasLostLock = true;
                                }
                                _radarFailTimer += Time.fixedDeltaTime;
                                radarTarget.timeAcquired = Time.time;
                                radarTarget.position = radarTarget.predictedPosition;
                                //if (weaponClass == WeaponClasses.SLW)
                                //    TargetPosition = radarTarget.predictedPosition;
                                //else
                                TargetPosition = radarTarget.predictedPositionWithChaffFactor(chaffEffectivity);
                                TargetVelocity = radarTarget.velocity;
                                TargetAcceleration = Vector3.zero;
                                TargetAcquired = true;
                            }
                        }
                    }
                    else
                    {
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileBase]: Semi-Active Radar guidance failed. Out of range and no data feed.");
                        radarTarget = TargetSignatureData.noTarget;
                        targetVessel = null;
                        return;
                    }
                }
                else //onboard radar is on, or off but in range
                {
                    // active radar with target locked:
                    vrd = null;
                    if (angleToTarget > maxOffBoresight)
                    {
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileBase]: Active Radar guidance failed.  Target is out of active seeker gimbal limits.");
                        radarTarget = TargetSignatureData.noTarget;
                        targetVessel = null;
                        return;
                    }
                    else
                    {
                        if (scannedTargets == null) scannedTargets = new TargetSignatureData[BDATargetManager.LoadedVessels.Count];
                        TargetSignatureData.ResetTSDArray(ref scannedTargets);
                        Ray ray = new Ray(transform.position, radarTarget.predictedPosition - transform.position);
                        bool pingRWR = Time.time - lastRWRPing > 0.4f;
                        if (pingRWR) lastRWRPing = Time.time;
                        bool radarSnapshot = (snapshotTicker > 10);
                        if (radarSnapshot)
                        {
                            snapshotTicker = 0;
                        }
                        else
                        {
                            snapshotTicker++;
                        }

                        //RadarUtils.UpdateRadarLock(ray, lockedSensorFOV, activeRadarMinThresh, ref scannedTargets, 0.4f, pingRWR, RadarWarningReceiver.RWRThreatTypes.MissileLock, radarSnapshot);
                        RadarUtils.RadarUpdateMissileLock(ray, lockedSensorFOV, ref scannedTargets, 0.4f, this);

                        float sqrThresh = radarLOALSearching ? 250000f : 1600; // 500 * 500 : 40 * 40;

                        if (radarLOAL && radarLOALSearching && !radarSnapshot)
                        {
                            //only scan on snapshot interval
                            TargetAcquired = true;
                        }
                        else
                        {
                            for (int i = 0; i < scannedTargets.Length; i++)
                            {
                                if (scannedTargets[i].exists && (scannedTargets[i].predictedPosition - radarTarget.predictedPosition).sqrMagnitude < sqrThresh)
                                {
                                    //re-check engagement envelope, only lock appropriate targets
                                    if (CheckTargetEngagementEnvelope(scannedTargets[i].targetInfo))
                                    {
                                        radarTarget = scannedTargets[i];
                                        TargetAcquired = true;
                                        radarLOALSearching = false;
                                        //if (weaponClass == WeaponClasses.SLW)
                                        //    TargetPosition = radarTarget.predictedPosition + (radarTarget.velocity * Time.fixedDeltaTime);
                                        //else
                                        TargetPosition = radarTarget.predictedPositionWithChaffFactor(chaffEffectivity);

                                        TargetVelocity = radarTarget.velocity;
                                        TargetAcceleration = radarTarget.acceleration;
                                        _radarFailTimer = 0;
                                        if (!ActiveRadar && Time.time - TimeFired > 1)
                                        {
                                            if (locksCount == 0)
                                            {
                                                if (weaponClass == WeaponClasses.SLW)
                                                    RadarWarningReceiver.PingRWR(ray, lockedSensorFOV, RadarWarningReceiver.RWRThreatTypes.Torpedo, 2f);
                                                else
                                                    RadarWarningReceiver.PingRWR(ray, lockedSensorFOV, RadarWarningReceiver.RWRThreatTypes.MissileLaunch, 2f);
                                                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase]: Pitbull! Radar missilebase has gone active.  Radar sig strength: {radarTarget.signalStrength:0.0}");
                                            }
                                            else if (locksCount > 2)
                                            {
                                                guidanceActive = false;
                                                checkMiss = true;
                                                if (BDArmorySettings.DEBUG_MISSILES)
                                                {
                                                    Debug.Log("[BDArmory.MissileBase]: Active Radar guidance failed. Radar missileBase reached max re-lock attempts.");
                                                }
                                            }
                                            locksCount++;
                                        }
                                        ActiveRadar = true;
                                        return;
                                    }
                                }
                            }
                        }

                        if (radarLOAL)
                        {
                            // Lost track of target, but we can re-acquire set radarLOALSearching = true and try to re-acquire using existing target information
                            radarLOALSearching = true;
                            TargetAcquired = true;

                            TargetPosition = radarTarget.predictedPositionWithChaffFactor(chaffEffectivity);
                            TargetVelocity = radarTarget.velocity;
                            TargetAcceleration = Vector3.zero;
                            ActiveRadar = false;
                            _radarFailTimer = 0;
                            radarTarget = TargetSignatureData.noTarget;
                        }
                        else
                        {
                            // Lost track of target and unable to re-acquire
                            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileBase]: Active Radar guidance failed.  No target locked.");
                            radarTarget = TargetSignatureData.noTarget;
                            targetVessel = null;
                            radarLOALSearching = false;
                            radarLOAL = false;
                            TargetAcquired = false;
                            ActiveRadar = false;
                        }
                    }
                }
            }
            else if (radarLOAL && radarLOALSearching) //add a check for missing radar, so LOAL missiles that have been dumbfired can still activate?
            {
                // not locked on before launch, trying lock-on after launch:

                if (scannedTargets == null) scannedTargets = new TargetSignatureData[BDATargetManager.LoadedVessels.Count];
                TargetSignatureData.ResetTSDArray(ref scannedTargets);
                Ray ray = new Ray(transform.position, GetForwardTransform());
                bool pingRWR = Time.time - lastRWRPing > 0.4f;
                if (pingRWR) lastRWRPing = Time.time;
                bool radarSnapshot = (snapshotTicker > 5);
                if (radarSnapshot)
                {
                    snapshotTicker = 0;
                }
                else
                {
                    snapshotTicker++;
                }

                //RadarUtils.UpdateRadarLock(ray, lockedSensorFOV * 3, activeRadarMinThresh * 2, ref scannedTargets, 0.4f, pingRWR, RadarWarningReceiver.RWRThreatTypes.MissileLock, radarSnapshot);
                RadarUtils.RadarUpdateMissileLock(ray, maxOffBoresight, ref scannedTargets, 0.4f, this);

                //float sqrThresh = targetVessel != null ? 1000000 : 90000f; // 1000 * 1000 : 300 * 300; Expand threshold if no target to search for, grab first available target

                float smallestAngle = maxOffBoresight;
                float sqrDist = float.PositiveInfinity;
                float currDist = 0;
                float currAngle = 0;
                TargetSignatureData lockedTarget = TargetSignatureData.noTarget;
                Vector3 soughtTarget = radarTarget.exists ? radarTarget.predictedPosition : targetVessel != null ? targetVessel.Vessel.CoM : transform.position + (startDirection);
                for (int i = 0; i < scannedTargets.Length; i++)
                {
                    if (scannedTargets[i].exists && targetVessel == null || (scannedTargets[i].predictedPosition - soughtTarget).sqrMagnitude < 1000000f)
                    {
                        //re-check engagement envelope, only lock appropriate targets
                        if (CheckTargetEngagementEnvelope(scannedTargets[i].targetInfo))
                        {
                            if (scannedTargets[i].targetInfo.Team == Team) continue;//Don't lock friendlies

                            if (targetVessel == null)
                            {
                                currAngle = Mathf.Rad2Deg*Mathf.Acos(Vector3.Dot((scannedTargets[i].predictedPosition - transform.position).normalized, GetForwardTransform()));
                                if (currAngle > (smallestAngle + 5f)) continue; // Look for the smallest angle, give 5 degrees of wiggle room.
                                smallestAngle = currAngle;
                            }

                            // Look for closest target, either to the previous target location if available, or to the missile if not
                            currDist = (targetVessel != null ? scannedTargets[i].predictedPosition : vessel.CoM - soughtTarget).sqrMagnitude;

                            if (currDist < sqrDist)
                            {
                                sqrDist = currDist;
                                lockedTarget = scannedTargets[i];
                                ActiveRadar = true;
                            }
                            //return;
                        }
                    }
                }

                if (lockedTarget.exists)
                {
                    radarTarget = lockedTarget;
                    TargetAcquired = true;
                    radarLOALSearching = false;
                    //if (weaponClass == WeaponClasses.SLW)
                    //    TargetPosition = radarTarget.predictedPosition + (radarTarget.velocity * Time.fixedDeltaTime);
                    //else
                    TargetPosition = radarTarget.predictedPositionWithChaffFactor(chaffEffectivity);
                    TargetVelocity = radarTarget.velocity;
                    TargetAcceleration = radarTarget.acceleration;

                    if (!ActiveRadar && Time.time - TimeFired > 1)
                    {
                        if (weaponClass == WeaponClasses.SLW)
                            RadarWarningReceiver.PingRWR(new Ray(transform.position, radarTarget.predictedPosition - transform.position), lockedSensorFOV, RadarWarningReceiver.RWRThreatTypes.Torpedo, 2f);
                        else
                            RadarWarningReceiver.PingRWR(new Ray(transform.position, radarTarget.predictedPosition - transform.position), lockedSensorFOV, RadarWarningReceiver.RWRThreatTypes.MissileLaunch, 2f);

                        //if (BDArmorySettings.DEBUG_MISSILES) 
                        Debug.Log($"[BDArmory.MissileBase]: Pitbull! Radar missileBase has gone active.  Radar sig strength: {radarTarget.signalStrength:0.0}");
                    }
                    return;
                }
                else
                {
                    radarTarget = TargetSignatureData.noTarget;
                    TargetAcquired = true;
                    TargetPosition = transform.position + (startDirection * 5000);
                    TargetVelocity = vessel.Velocity(); // Set the relative target velocity to 0.
                    TargetAcceleration = Vector3.zero;
                    radarLOALSearching = true;
                    _radarFailTimer += Time.fixedDeltaTime;
                    if (_radarFailTimer > radarTimeout)
                    {
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileBase]: Active Radar guidance failed. LOAL could not lock a target.");
                        radarLOAL = false;
                        targetVessel = null;
                        radarLOALSearching = false;
                        TargetAcquired = false;
                        ActiveRadar = false;
                    }
                    return;
                }
            }

            if (!radarTarget.exists)
            {
                if (_radarFailTimer < radarTimeout)
                {
                    if (vrd && vrd.locked)
                    {
                        TargetSignatureData lockedTarget = vrd.lockedTargetData.targetData;
                        if (targetVessel != null)
                        {
                            List<TargetSignatureData> possibleTargets = vrd.GetLockedTargets();
                            for (int i = 0; i < possibleTargets.Count; i++)
                            {
                                if (possibleTargets[i].vessel == targetVessel.Vessel)
                                {
                                    lockedTarget = possibleTargets[i];
                                    break;
                                }
                            }
                        }
                        radarTarget = lockedTarget;
                    }
                    else if (radarLOAL)
                        radarLOALSearching = true;
                    else
                    {
                        _radarFailTimer += Time.fixedDeltaTime;
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase]: No assigned radar target. Awaiting timeout({radarTimeout - _radarFailTimer}).... ");
                    }
                }
                else
                {
                    targetVessel = null;
                    TargetAcquired = false;
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileBase]: No radar target. Active Radar guidance timed out. ");
                }
            }
        }

        protected bool CheckTargetEngagementEnvelope(TargetInfo ti)
        {
            if (ti == null) return false;
            return (ti.isMissile && engageMissile) ||
                    (!ti.isMissile && ti.isFlying && engageAir) ||
                    ((ti.isLandedOrSurfaceSplashed || ti.isSplashed) && engageGround) ||
                    (ti.isUnderwater && engageSLW);
        }

        protected void ReceiveRadarPing(Vessel v, Vector3 source, RadarWarningReceiver.RWRThreatTypes type, float persistTime)
        {
            if (TargetingMode == TargetingModes.AntiRad && TargetAcquired && v == vessel)
            {
                // Ping was close to the previous target position and is within the boresight of the missile.
                var staticLaunchThresholdSqr = maxStaticLaunchRange * maxStaticLaunchRange / 16f;
                if ((source - VectorUtils.GetWorldSurfacePostion(targetGPSCoords, vessel.mainBody)).sqrMagnitude < staticLaunchThresholdSqr && Vector3.Angle(source - transform.position, GetForwardTransform()) < maxOffBoresight)
                {
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase]: Radar ping! Adjusting target position by {(source - VectorUtils.GetWorldSurfacePostion(targetGPSCoords, vessel.mainBody)).magnitude} to {TargetPosition}");
                    TargetAcquired = true;
                    TargetPosition = source;
                    targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(TargetPosition, vessel.mainBody);
                    TargetVelocity = Vector3.zero;
                    TargetAcceleration = Vector3.zero;
                    lockFailTimer = 0;
                }
            }
        }

        protected void UpdateAntiRadiationTarget()
        {
            if (FlightGlobals.ready && TargetAcquired)
            {
                if (lockFailTimer < 0)
                {
                    lockFailTimer = 0;
                }
                lockFailTimer += Time.fixedDeltaTime;
                if (lockFailTimer > 8)
                {
                    TargetAcquired = false;
                }
            }
            if (targetGPSCoords != Vector3d.zero)
                TargetPosition = VectorUtils.GetWorldSurfacePostion(targetGPSCoords, vessel.mainBody);
        }
        private bool setInertialTarget = false;

        public Vector3d UpdateInertialTarget()
        {
            Vector3 TargetCoords_;
            Vector3 TargetLead;
            bool detectedByRadar = false;
            bool activeDatalink = false;
            if (!setInertialTarget)
            {
                //driftSeed = new Vector3(UnityEngine.Random.Range(-1, 1) * inertialDrift, UnityEngine.Random.Range(-1, 1) * inertialDrift, UnityEngine.Random.Range(-1, 1) * inertialDrift);
                driftSeed = UnityEngine.Random.insideUnitSphere * inertialDrift;
                setInertialTarget = true;
                if (gpsUpdates >= 0)
                {
                    if (gpsUpdates > GpsUpdateMax) GpsUpdateMax = gpsUpdates;
                }
            }
            TargetCoords_ = targetGPSCoords;

            if (lockFailTimer > radarTimeout)
            {
                targetVessel = null;
                TargetAcquired = false;
                guidanceActive = false;
                return TargetCoords_;
            }
            if (targetVessel && lockFailTimer < 0)
            {
                lockFailTimer = 0;
            }
            if (targetVessel && HasFired)
            {
                if (gpsUpdates >= 0f)
                {
                    var weaponManager = VesselModuleRegistry.GetMissileFire(SourceVessel);
                    TargetSignatureData INStarget = TargetSignatureData.noTarget;
                    bool radarLocked = false;
                    if (weaponManager != null && weaponManager.vesselRadarData)
                    {
                        INStarget = weaponManager._radarsEnabled || weaponClass == WeaponClasses.SLW && weaponManager._sonarsEnabled ? weaponManager.vesselRadarData.detectedRadarTarget(targetVessel.Vessel, weaponManager) : TargetSignatureData.noTarget; //is the target tracked by radar or ISRT?
                        if (INStarget.exists)
                        {
                            detectedByRadar = true;
                            List<TargetSignatureData> possibleTargets = weaponManager.vesselRadarData.GetLockedTargets();
                            for (int i = 0; i < possibleTargets.Count; i++)
                            {
                                if (possibleTargets[i].vessel == targetVessel.Vessel)
                                {
                                    radarLocked = true;
                                    break;
                                }
                            }
                        }
                        else
                            if (weaponManager._irstsEnabled) INStarget = weaponManager.vesselRadarData.activeIRTarget(targetVessel.Vessel, weaponManager);
                    }
                    if (INStarget.exists)
                    {
                        activeDatalink = true;
                        float distanceToTargetSqr = (SourceVessel.CoM - targetVessel.Vessel.CoM).sqrMagnitude; //sourceVessel radar tracking garbled?
                        float distanceToJammerSqr = (vessel.CoM - targetVessel.Vessel.CoM).sqrMagnitude; //missile datalink jammed?
                        float jamDistance = RadarUtils.GetVesselECMJammingDistance(targetVessel.Vessel); //is the target jamming?
                        if ((!detectedByRadar || jamDistance * jamDistance < distanceToTargetSqr) && jamDistance * jamDistance < distanceToJammerSqr)
                        {
                            if (gpsUpdates == 0 && (detectedByRadar && radarLocked)) // Constant updates
                            {
                                TargetINSCoords = VectorUtils.WorldPositionToGeoCoords(targetVessel.Vessel.CoM, targetVessel.Vessel.mainBody);
                                TimeOfLastINS = TimeIndex;
                                TargetLead = MissileGuidance.GetAirToAirFireSolution(this, targetVessel.Vessel, out INStimetogo);
                                if (detectedByRadar) TargetLead += (INStarget.predictedPositionWithChaffFactor(chaffEffectivity) - INStarget.position);
                                TargetCoords_ = VectorUtils.WorldPositionToGeoCoords(TargetLead, targetVessel.Vessel.mainBody);
                                targetGPSCoords = TargetCoords_;
                            }
                            else //clamp updates to radar/IRST track speed
                            {
                                float updateCount = TimeIndex / GpsUpdateMax;
                                if (updateCount > gpsUpdateCounter)
                                {
                                    gpsUpdateCounter++;
                                    TargetINSCoords = VectorUtils.WorldPositionToGeoCoords(targetVessel.Vessel.CoM, targetVessel.Vessel.mainBody);
                                    TimeOfLastINS = TimeIndex;
                                    TargetLead = MissileGuidance.GetAirToAirFireSolution(this, targetVessel.Vessel, out INStimetogo);
                                    if (detectedByRadar) TargetLead += (INStarget.predictedPositionWithChaffFactor(chaffEffectivity) - INStarget.position);
                                    TargetCoords_ = VectorUtils.WorldPositionToGeoCoords(TargetLead, targetVessel.Vessel.mainBody);
                                    targetGPSCoords = TargetCoords_;
                                }
                            }
                        }
                    }
                    else activeDatalink = false;
                }
                else
                {
                    TargetAcquired = true;
                    if (targetVessel != null)
                    {
                        float angleToTarget = Vector3.Angle(targetVessel.Vessel.CoM - transform.position, GetForwardTransform());

                        if (angleToTarget > maxOffBoresight * 1.1f)
                        {
                            TargetAcquired = false; //technically non-datalink missiles shouldn't know when they've 'Missed' if they're just fired at a point a target is predicted to be at
                            //but a pilot seeing their missile obviously missing would then fire another, so this frees up the AI to do that if MissilesAway = MaxMissilesPerTarget.
                        }
                    }
                }
            }
            if (TargetAcquired)
            {
                TargetPosition = VectorUtils.GetWorldSurfacePostion(TargetCoords_, vessel.mainBody);
                if (activeDatalink)
                {
                    TargetINSCoords = VectorUtils.WorldPositionToGeoCoords(VectorUtils.GetWorldSurfacePostion(TargetINSCoords, vessel.mainBody) + driftSeed * TimeIndex, vessel.mainBody);
                    lockFailTimer = 0;
                }
                else if (gpsUpdates >= 0)
                    lockFailTimer += Time.fixedDeltaTime;
            }
            else
                lockFailTimer += Time.fixedDeltaTime;
            //Debug.Log($"[INSDebug] lockfailTimer {lockFailTimer.ToString("0.00")}; datalink {activeDatalink}");
            TargetVelocity = Vector3.zero;
            TargetAcceleration = Vector3.zero;
            TargetPosition += driftSeed * TimeIndex;
            return TargetCoords_;
        }

        public Vector3 VacuumClearanceManeuver(Vector3 orbitalTarget, Vector3 CoM, bool hasRCS = true, bool vacuumSteerable = true)
        {
            // In vacuum, gradually turn towards target shortly after launch to minimize wasted delta-V
            // During this maneuver, check that we have cleared any obstacles before throttling up
            if (SourceVessel == null || !vacuumSteerable || !vessel.InVacuum())
            {
                vacuumClearanceState = VacuumClearanceStates.Cleared;
                Throttle = 1f;
                return orbitalTarget;
            }

            if (vacuumSteerable && (vessel.InVacuum()))
            {
                float dotTol;
                float clearedDotTol = hasRCS ? 0.98f : 0.7f;
                Vector3 toSource = CoM - SourceVessel.CoM;
                Ray toTarget = new Ray(CoM, orbitalTarget);
                float sourceRadius = SourceVessel.GetRadius();
                switch (vacuumClearanceState)
                {
                    case VacuumClearanceStates.Clearing: // We are launching, stay on course
                        {
                            dotTol = 0.98f;
                            if (!isMMG)
                            {
                                if (Physics.Raycast(toTarget, out RaycastHit hit, toSource.sqrMagnitude, (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.EVA | LayerMasks.Wheels)))
                                {
                                    Part p = hit.collider.gameObject.GetComponentInParent<Part>();
                                    if (p != null && hit.distance > 10f)
                                        vacuumClearanceState = VacuumClearanceStates.Turning;
                                }
                                else // No obstacles in the way
                                {
                                    vacuumClearanceState = VacuumClearanceStates.Cleared;
                                    Throttle = 1f;
                                    dotTol = clearedDotTol;
                                }
                            }
                            else
                            {
                                if (!VectorUtils.CheckClearOfSphere(toTarget, SourceVessel.CoM, sourceRadius))
                                {
                                    if ((toSource.sqrMagnitude) >= (sourceRadius + 10f)* (sourceRadius + 10f))
                                        vacuumClearanceState = VacuumClearanceStates.Turning;
                                }
                                else // No obstacles in the way
                                {
                                    vacuumClearanceState = VacuumClearanceStates.Cleared;
                                    Throttle = 1f;
                                    dotTol = clearedDotTol;
                                }
                            }
                            if (vacuumClearanceState == VacuumClearanceStates.Clearing) // Adjust throttle if still clearing
                            {
                                orbitalTarget = CoM + 100f * GetForwardTransform();
                                if (isMMG || !hasRCS)
                                {
                                    float t = toSource.sqrMagnitude / ((sourceRadius + 5) * (sourceRadius + 5));
                                    Throttle = Mathf.Lerp(0.5f, 0f, t); // Use throttle kick for MMG or missiles without RCS
                                }
                                else
                                    Throttle = 0f;
                            }
                        }
                        break;
                    case VacuumClearanceStates.Turning: // It is now safe to turn towards target and burn to maneuver away from SourceVessel
                        {
                            dotTol = 0.98f;
                            if (isMMG || !hasRCS) // For MMGs or missiles without RCS use throttle for turning
                            {
                                Throttle = 0.5f;
                                dotTol = 0.5f;
                            }
                            bool cleared = VectorUtils.CheckClearOfSphere(toTarget, SourceVessel.CoM, sourceRadius);
                            cleared = isMMG ? cleared : cleared && !Physics.Raycast(toTarget, out RaycastHit hit, toSource.sqrMagnitude, (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.EVA | LayerMasks.Wheels));
                            if ((Vector3.Dot((orbitalTarget - CoM).normalized, GetForwardTransform()) >= dotTol) && cleared)
                            {
                                vacuumClearanceState = VacuumClearanceStates.Cleared;
                                dotTol = clearedDotTol;
                                Throttle = 1f;
                            }
                        }
                        break;
                    default: // VacuumClearanceStates.Cleared, We are engaging target
                        {
                            dotTol = clearedDotTol;
                            Throttle = 1f;
                        }
                        break;
                }

                // Rotate towards target if necessary
                if (Vector3.Dot((orbitalTarget - CoM).normalized, GetForwardTransform()) < dotTol)
                    Throttle = 0;
            }
            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
                debugString.AppendLine($"Clearance State: {VacuumClearanceStatesString[(int)vacuumClearanceState]}");
            return orbitalTarget;
        }

        public void DrawDebugLine(Vector3 start, Vector3 end, Color color = default(Color))
        {
            if (BDArmorySettings.DEBUG_LINES)
            {
                if (!gameObject.GetComponent<LineRenderer>())
                {
                    LR = gameObject.AddComponent<LineRenderer>();
                    LR.material = new Material(Shader.Find("KSP/Emissive/Diffuse"));
                    LR.material.SetColor("_EmissiveColor", color);
                }
                else
                {
                    LR = gameObject.GetComponent<LineRenderer>();
                }
                LR.enabled = true;
                LR.positionCount = 2;
                LR.SetPosition(0, start);
                LR.SetPosition(1, end);
            }
        }

        protected virtual void OnGUI()
        {
            if (!BDArmorySettings.DEBUG_LINES && LR != null) { LR.enabled = false; }
        }

        protected void CheckDetonationDistance()
        {
            if (DetonationDistanceState == DetonationDistanceStates.Detonate)
            {
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileBase]: Target detected inside sphere - detonating");

                Detonate();
            }
        }

        protected Vector3 CalculateAGMBallisticGuidance(MissileBase missile, Vector3 targetPosition)
        {
            if (this._guidance == null)
            {
                _guidance = new BallisticGuidance();
            }

            return _guidance.GetDirection(this, targetPosition, Vector3.zero);
        }

        protected void drawLabels()
        {
            if (vessel == null || !HasFired || !vessel.isActiveVessel) return;
            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
            {
                GUI.Label(new Rect(200, Screen.height - 300, 600, 300), $"{this.shortName}\n{debugString}");
            }
        }

        public float GetTntMass()
        {
            return VesselModuleRegistry.GetModules<BDExplosivePart>(vessel).Max(x => x.tntMass);
        }

        public void CheckDetonationState(bool separateWarheads = false)
        {
            //Guard clauses
            //if (!TargetAcquired) return;
            var targetDistancePerFrame = Time.fixedDeltaTime * (TargetVelocity - BDKrakensbane.FrameVelocityV3f) + 0.5f * Time.fixedDeltaTime * Time.fixedDeltaTime * TargetAcceleration;
            var missileDistancePerFrame = Time.fixedDeltaTime * (vessel.Velocity() - BDKrakensbane.FrameVelocityV3f) + 0.5f * Time.fixedDeltaTime * Time.fixedDeltaTime * vessel.acceleration_immediate;

            var futureTargetPosition = (TargetPosition + targetDistancePerFrame);
            var futureMissilePosition = (vessel.CoM + missileDistancePerFrame);

            float relativeSpeed = (float)(TargetVelocity - vessel.Velocity()).magnitude * Time.fixedDeltaTime; // relativeSpeed is actually a distance!

            switch (DetonationDistanceState)
            {
                case DetonationDistanceStates.NotSafe:
                    {
                        //Lets check if we are at a safe distance from the source vessel
                        var dist = GetBlastRadius() * 1.25f; //this is from launching vessel, which assuming is also moving forward on a similar vector, could potentially result in missiles not arming for several km for faster planes/slower missiles
                        var hitCount = Physics.OverlapSphereNonAlloc(futureMissilePosition, dist, proximityHitColliders, layerMask);
                        if (hitCount == proximityHitColliders.Length)
                        {
                            proximityHitColliders = Physics.OverlapSphere(futureMissilePosition, dist, layerMask);
                            hitCount = proximityHitColliders.Length;
                        }
                        using (var hitsEnu = proximityHitColliders.Take(hitCount).GetEnumerator())
                        {
                            while (hitsEnu.MoveNext())
                            {
                                if (hitsEnu.Current == null) continue;
                                try
                                {
                                    Part partHit = hitsEnu.Current.GetComponentInParent<Part>();
                                    if (partHit == null) continue;
                                    if (ProjectileUtils.IsIgnoredPart(partHit)) continue; // Ignore ignored parts.

                                    if (partHit.vessel != vessel && partHit.vessel == SourceVessel) // Not ourselves, but the source vessel.
                                    {
                                        //We found a hit to the vessel
                                        return;
                                    }
                                }
                                catch (Exception e)
                                {
                                    // ignored
                                    Debug.LogWarning("[BDArmory.MissileBase]: Exception thrown in CheckDetonatationState: " + e.Message + "\n" + e.StackTrace);
                                }
                            }
                        }

                        //We are safe and we can continue with the cruising phase
                        DetonationDistanceState = DetonationDistanceStates.Cruising;
                        if (!separateWarheads) SetupExplosive(this.part); //moving arming of warhead to here from launch to prevent Laser anti-missile systems zapping a missile immediately after launch and fragging the launching plane as the missile detonates
                        break;
                    }

                case DetonationDistanceStates.Cruising:
                    {
                        if (!TargetAcquired) return;
                        //if (Vector3.Distance(futureMissilePosition, futureTargetPosition) < GetBlastRadius() * 10)
                        // Replaced old proximity check with proximity check based on either detonation distance or distance traveled per frame
                        if ((futureMissilePosition - futureTargetPosition).sqrMagnitude < 100 * (relativeSpeed > DetonationDistance ? relativeSpeed * relativeSpeed : DetonationDistanceSqr))
                        {
                            //We are now close enough to start checking the detonation distance
                            DetonationDistanceState = DetonationDistanceStates.CheckingProximity;
                        }
                        else
                        {
                            BDModularGuidance bdModularGuidance = this as BDModularGuidance;

                            if (bdModularGuidance == null) return;

                            //if (Vector3.Distance(futureMissilePosition, futureTargetPosition) > this.DetonationDistance) return;
                            if ((futureMissilePosition - futureTargetPosition).sqrMagnitude > DetonationDistanceSqr) return;

                            DetonationDistanceState = DetonationDistanceStates.CheckingProximity;
                        }
                        break;
                    }

                case DetonationDistanceStates.CheckingProximity:
                    {
                        if (!TargetAcquired) return;
                        if (DetonationDistance == 0)
                        {
                            if (weaponClass == WeaponClasses.Bomb) return;

                            if (TimeIndex > 1f)
                            {
                                Ray rayFuturePosition = new Ray(vessel.CoM, missileDistancePerFrame);
                                float dist = Time.fixedDeltaTime * (float)vessel.Velocity().magnitude;
                                var hitCount = Physics.RaycastNonAlloc(rayFuturePosition, proximityHits, dist, layerMask);
                                if (hitCount == proximityHits.Length) // If there's a whole bunch of stuff in the way (unlikely), then we need to increase the size of our hits buffer.
                                {
                                    proximityHits = Physics.RaycastAll(rayFuturePosition, dist, layerMask);
                                    hitCount = proximityHits.Length;
                                }
                                if (hitCount > 0)
                                {
                                    Array.Sort<RaycastHit>(proximityHits, 0, hitCount, RaycastHitComparer.raycastHitComparer);

                                    using (var hitsEnu = proximityHits.Take(hitCount).GetEnumerator())
                                    {
                                        while (hitsEnu.MoveNext())
                                        {
                                            RaycastHit hit = hitsEnu.Current;

                                            try
                                            {
                                                var hitPart = hit.collider.gameObject.GetComponentInParent<Part>();
                                                if (hitPart == null) continue;
                                                if (ProjectileUtils.IsIgnoredPart(hitPart)) continue; // Ignore ignored parts.

                                                if (hitPart.vessel != SourceVessel && hitPart.vessel != vessel)
                                                {
                                                    //We found a hit to other vessel, set transform.position to hit point (moves immediately, but doesn't update .CoM fields, etc)
                                                    vessel.SetPosition(hit.point - 0.5f * rayFuturePosition.direction);
                                                    DetonationDistanceState = DetonationDistanceStates.Detonate;
                                                    Detonate();
                                                    return;
                                                }
                                            }
                                            catch (Exception e)
                                            {
                                                // ignored
                                                Debug.LogWarning("[BDArmory.MissileBase]: Exception thrown in CheckDetonationState: " + e.Message + "\n" + e.StackTrace);
                                            }
                                        }
                                    }
                                }
                                else if (TargetAcquired && targetVessel != null && targetVessel.Vessel != null)
                                {
                                    // For very high speed intercepts when missiles may phase through small vessels/missiles within a frame
                                    Vector3 relPos = TargetPosition - vessel.CoM;
                                    Vector3 relVel = TargetVelocity - vessel.Velocity();
                                    bool approaching = Vector3.Dot(relPos, relVel) < 0f;
                                    float targetRad = heatTarget.exists && heatTarget.vessel == null ? 1 : targetVessel.Vessel.GetRadius();
                                    // if target is a flare, return 1, else standard vessel radius
                                    float selfRad = vessel.GetRadius();
                                    float sepRad = 1.7321f * (targetRad + selfRad);                                    

                                    if (approaching && relVel.sqrMagnitude * Time.fixedDeltaTime * Time.fixedDeltaTime > sepRad * sepRad)
                                    {
                                        bool shouldDetonate = false;
                                        Ray ray = new(vessel.CoM, -relVel);
                                        ray.origin += selfRad * ray.direction; // Start at the tip of the missile (assuming it's pointing roughly prograde in the relVel direction and is longest on that axis).
                                        if (Physics.Raycast(ray, out RaycastHit hit, relativeSpeed, (int)(LayerMasks.Parts | LayerMasks.EVA | LayerMasks.Wheels))) // Hit!
                                        {
                                            vessel.SetPosition(hit.point - 0.5f * ray.direction); // Slightly back so that shaped charge explosives hit properly.
                                            shouldDetonate = true;
                                        }
                                        else // Not hitting, just getting close, check for reaching CPA.
                                        {
                                            Vector3 relAccel = TargetAcceleration - vessel.acceleration_immediate;
                                            float cpaTime = AIUtils.TimeToCPA(relPos, relVel, relAccel, Time.fixedDeltaTime);

                                            if (cpaTime > 0f && cpaTime < Time.fixedDeltaTime)
                                            {
                                                // Set relative position to the same as at CPA point, but relative to the target's current position. This avoids having to move the target and wait an additional frame.
                                                vessel.SetPosition(TargetPosition - AIUtils.PredictPosition(relPos, relVel, relAccel, cpaTime));
                                                shouldDetonate = true;
                                            }
                                        }
                                        if (shouldDetonate)
                                        {
                                            Detonate();
                                            if (targetVessel.isMissile && targetVessel.MissileBaseModule)
                                                targetVessel.MissileBaseModule.Detonate(); // The above approach is only about ~50% effective against missiles, so just tell the other missile to detonate just in case
                                            return;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            float optimalDistance = (float)(Math.Max(DetonationDistance, relativeSpeed));
                            Vector3 targetPoint = (warheadType == WarheadTypes.ContinuousRod ? vessel.CoM - VectorUtils.GetUpDirection(TargetPosition) * (GetBlastRadius() > 0f ? Mathf.Min(GetBlastRadius() / 3f, DetonationDistance / 3f) : 5f) : vessel.CoM);
                            var hitCount = Physics.OverlapSphereNonAlloc(targetPoint, optimalDistance, proximityHitColliders, layerMask);
                            if (hitCount == proximityHitColliders.Length)
                            {
                                proximityHitColliders = Physics.OverlapSphere(targetPoint, optimalDistance, layerMask);
                                hitCount = proximityHitColliders.Length;
                            }
                            using (var hitsEnu = proximityHitColliders.Take(hitCount).GetEnumerator())
                            {
                                while (hitsEnu.MoveNext())
                                {
                                    if (hitsEnu.Current == null) continue;

                                    try
                                    {
                                        Part partHit = hitsEnu.Current.GetComponentInParent<Part>();

                                        if (partHit == null) continue;
                                        if (ProjectileUtils.IsIgnoredPart(partHit)) continue; // Ignore ignored parts.
                                        if (partHit.vessel == vessel || partHit.vessel == SourceVessel) continue; // Ignore source vessel
                                        if (partHit.IsMissile() && partHit.GetComponent<MissileBase>().SourceVessel == SourceVessel) continue; // Ignore other missiles fired by same vessel
                                        if (partHit.vessel.vesselType == VesselType.Debris) continue; // Ignore debris

                                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileBase]: Missile proximity sphere hit | Distance overlap = " + optimalDistance + "| Part name = " + partHit.name);

                                        //We found a hit a different vessel than ours
                                        if (DetonateAtMinimumDistance)
                                        {
                                            var distanceSqr = (partHit.transform.position - vessel.CoM).sqrMagnitude;
                                            var predictedDistanceSqr = (AIUtils.PredictPosition(partHit.transform.position, partHit.vessel.Velocity(), partHit.vessel.acceleration, Time.deltaTime) - AIUtils.PredictPosition(vessel, Time.deltaTime)).sqrMagnitude;

                                            //float missileDistFrame = Time.fixedDeltaTime * (float)vessel.srfSpeed; vessel.Velocity() * Time.fixedDeltaTime

                                            if (distanceSqr > predictedDistanceSqr && distanceSqr > relativeSpeed * relativeSpeed) // If we're closing and not going to hit within the next update, then wait.
                                                return;
                                        }
                                        DetonationDistanceState = DetonationDistanceStates.Detonate;
                                        return;
                                    }
                                    catch (Exception e)
                                    {
                                        // ignored
                                        Debug.LogWarning("[BDArmory.MissileBase]: Exception thrown in CheckDetonatationState: " + e.Message + "\n" + e.StackTrace);
                                    }
                                }
                            }
                        }
                        break;
                    }
            }

            if (BDArmorySettings.DEBUG_MISSILES)
            {
                Debug.Log($"[BDArmory.MissileBase]: DetonationDistanceState = : {DetonationDistanceState}");
            }
        }

        protected void SetInitialDetonationDistance()
        {
            if (this.DetonationDistance == -1)
            {
                if (GuidanceMode == GuidanceModes.AAMLead || GuidanceMode == GuidanceModes.AAMPure || GuidanceMode == GuidanceModes.PN || GuidanceMode == GuidanceModes.APN || GuidanceMode == GuidanceModes.AAMLoft || GuidanceMode == GuidanceModes.Kappa) //|| GuidanceMode == GuidanceModes.AAMHybrid)
                {
                    DetonationDistance = GetBlastRadius() * 0.25f;
                }
                else
                {
                    //DetonationDistance = GetBlastRadius() * 0.05f;
                    DetonationDistance = 0f;
                }
            }
            if (BDArmorySettings.DEBUG_MISSILES)
            {
                Debug.Log($"[BDArmory.MissileBase]: DetonationDistance = : {DetonationDistance}");
            }
        }

        protected void CollisionEnter(Collision col)
        {
            if (TimeIndex > 2 && HasFired && col.collider.gameObject.GetComponentInParent<Part>().GetFireFX())
            {
                ContactPoint contact = col.contacts[0];
                Vector3 pos = contact.point;
                BulletHitFX.AttachFlames(pos, col.collider.gameObject.GetComponentInParent<Part>());
            }

            if (HasExploded || !HasFired) return;

            if (DetonationDistanceState != DetonationDistanceStates.CheckingProximity) return;

            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileBase]: Missile Collided - Triggering Detonation");
            Detonate();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_ChangetoLowAltitudeRange", active = true)]//Change to Low Altitude Range
        public void CruiseAltitudeRange()
        {
            if (Events["CruiseAltitudeRange"].guiName == "Change to Low Altitude Range")
            {
                Events["CruiseAltitudeRange"].guiName = "Change to High Altitude Range";

                UI_FloatRange cruiseAltitudeField = (UI_FloatRange)Fields["CruiseAltitude"].uiControlEditor;
                cruiseAltitudeField.maxValue = 500f;
                cruiseAltitudeField.minValue = 5f;
                cruiseAltitudeField.stepIncrement = 5f;
            }
            else
            {
                Events["CruiseAltitudeRange"].guiName = "Change to Low Altitude Range";
                UI_FloatRange cruiseAltitudField = (UI_FloatRange)Fields["CruiseAltitude"].uiControlEditor;
                cruiseAltitudField.maxValue = 25000f;
                cruiseAltitudField.minValue = 500;
                cruiseAltitudField.stepIncrement = 500f;
            }
            this.part.RefreshAssociatedWindows();
        }
    }

    internal class RaycastHitComparer : IComparer<RaycastHit>
    {
        int IComparer<RaycastHit>.Compare(RaycastHit left, RaycastHit right)
        {
            return left.distance.CompareTo(right.distance);
        }
        public static RaycastHitComparer raycastHitComparer = new RaycastHitComparer();
    }
}
