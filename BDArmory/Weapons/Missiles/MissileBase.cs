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
using BDArmory.WeaponMounts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

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

        public MissileType GetMissileType()
        {
            return _missileType;
        }

        public float GetEngageFOV()
        {
            return missileFireAngle;
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

        public enum MissileType { None = -1, Missile, Bomb, Torpedo, DepthCharge, ASWMissile, Sonobuoy };

        protected MissileType _missileType = MissileType.Missile;

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
            UI_FloatRange(minValue = 0f, maxValue = 180f, stepIncrement = 5f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float maxOffBoresight = 180;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_DetonationDistanceOverride"),
            UI_FloatSemiLogRange(minValue = 1f, maxValue = 1000f, sigFig = 2, withZero = true, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Detonation distance override
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
        public FloatCurve lockedSensorVelocityBias = new FloatCurve();             // weighting of targets and flares from angular velocity angle of prior target and new target aligned (0) to opposite (180)

        [KSPField]
        public FloatCurve lockedSensorVelocityMagnitudeBias = new FloatCurve();             // weighting of targets and flares from angular velocity magnitude of prior target and new target, normalized by the prior target angular velocity magnitude

        [KSPField]
        public float lockedSensorMinAngularVelocity = 5; // minimum target/flare angular velocity detectable, in deg/s, used primarily for dealing with divide-by-zero issues in MagnitudeBias

        [KSPField]
        public float heatThreshold = 150;

        [KSPField]
        public float frontAspectHeatModifier = 1f;                   // Modifies heat value returned to missiles outside of ~50 deg exhaust cone from non-prop engines. Only takes affect when ASPECTED_IR_SEEKERS = true in settings.cfg

        [KSPField]
        public float chaffEffectivity = 1f;                            // Modifies how the missile targeting is affected by chaff, 1 is fully affected (normal behavior), lower values mean less affected (0 is ignores chaff), higher values means more affected

        //[KSPField]
        //public float chaffNotchEffectivity = 1f;                       // Modifies how the missile targeting is affected by chaff while notching, 1 is fully affected (normal behavior), lower values mean less affected (0 is ignores chaff), higher values means more affected

        protected float chaffNotchVFac = 1f;
        protected float chaffNotchRFac = 1f;

        [KSPField]
        public float flareEffectivity = 1f;                            // Modifies how the missile targeting is affected by flares, 1 is fully affected (normal behavior), lower values mean less affected (0 is ignores flares), higher values means more affected

        [KSPField]
        public bool allAspect = false;                                 // DEPRECATED, replaced by uncagedIRLock. uncagedIRLock is automatically set to this value upon loading (to maintain compatability with old BDA mods)

        [KSPField]
        public bool uncagedLock = true;                             //if true it simulates a modern IR missile with "uncaged lock" ability. Even if the target is not within boresight fov, it can be radar locked and the target information transfered to the missile. It will then try to lock on with the heat seeker. If false, it is an older missile which requires a direct "in boresight" lock.

        [KSPField]
        public bool targetCoM = false;                             //if true, IR missile targets the center of a craft, false IR missile targets hottest part

        [KSPField]
        public bool isTimed = false;

        [KSPField]
        public bool radarLOAL = false;                              //if true, radar missile will acquire and lock onto a target after launch, using the missile's onboard radar

        [KSPField]
        public bool canRelock = true;                               //if true, if a FCS radar guiding a SARH missile loses lock, the missile will be switched to the active radar lock instead of going inactive from target loss.

        [KSPField]
        public bool hasIFF = true;

        [KSPField]
        public float missileCMRange = -1f;

        [KSPField]
        public float missileCMInterval = -1f;

        protected List<CMDropper> missileCM;

        protected float missileCMTime = -1f;

        protected bool CMenabled = false;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_DropTime"),//Drop Time
            UI_FloatRange(minValue = 0f, maxValue = 5f, stepIncrement = 0.1f, scene = UI_Scene.Editor)]
        public float dropTime = 0.5f;

        [KSPField(isPersistant = true, advancedTweakable = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_FiringAngle"),//Firing Angle
  UI_FloatRange(minValue = 1f, maxValue = 90, stepIncrement = 1f, scene = UI_Scene.Editor)]
        public float missileFireAngle = -1;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_InCargoBay"),//In Cargo Bay: 
            UI_Toggle(disabledText = "#LOC_BDArmory_false", enabledText = "#LOC_BDArmory_true", affectSymCounterparts = UI_Scene.All)]//False--True
        public bool inCargoBay = false;

        // This previously was in both MissileLauncher and BDModular Guidance, transferred it here
        [KSPField(advancedTweakable = true, isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_FiringPriority"),
            UI_FloatRange(minValue = 0, maxValue = 10, stepIncrement = 1, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        public float priority = 0; //per-weapon priority selection override

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

        [KSPField]
        public bool activeRadarCanNotch = true;

        [KSPField]
        public float activeRadarGlintMult = -1f;

        [KSPField]
        public FloatCurve activeRadarGlintCurve = new FloatCurve();

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

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_CruisePopup"),
            UI_Toggle(disabledText = "#LOC_BDArmory_false", enabledText = "#LOC_BDArmory_true", scene = UI_Scene.All)]
        public bool CruisePopup = false; // Cruise Guidance Popup Attack

        [KSPField]
        public float CruisePopupAngle = 45f; // Cruise Guidance Popup Attack Angle

        [KSPField]
        public float CruisePopupAltitude = 250; // Cruise Guidance Popup Attack Altitude

        [KSPField]
        public float CruisePopupRange = 2000; // Cruise Guidance Popup Attack Range

        [KSPField]
        public float WeaveVerticalG = 2f; // Weave Guidance Vertical g

        [KSPField]
        public float WeaveHorizontalG = 2f; // Weave Guidance Horizontal g

        [KSPField]
        public float WeaveFrequency = 0.05f; // Weave Guidance Frequency (Hz)

        [KSPField]
        public float WeaveTerminalAngle = 25f; // Weave Guidance Terminal Angle (deg, down)

        [KSPField]
        public float WeaveFactor = 1f; // Weave Guidance Factor (higher means more weave, lower means less)

        [KSPField]
        public bool WeaveUseAGMDescentRatio = false; // Weave Guidance will use agmDescentRatio as a floor

        [KSPField]
        public Vector3 WeaveRandomRange = new Vector3(0.5f, 0.5f, 0.01f); // Weave Guidance horz/vert g and freq randomization range (only affects if horz/vert != 0)

        protected float WeaveOffset = -1f;

        protected Vector3 WeaveStart = Vector3.zero;

        protected float WeaveAlt = -1f;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftMaxAltitude"),//Loft Max Altitude
            UI_FloatRange(minValue = 5000f, maxValue = 30000f, stepIncrement = 100f, scene = UI_Scene.Flight, affectSymCounterparts = UI_Scene.All)]
        public float LoftMaxAltitude = 16000;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftRangeOverride"),//Loft Altitude Difference
            UI_FloatRange(minValue = 0f, maxValue = 40000f, stepIncrement = 500f, scene = UI_Scene.Flight, affectSymCounterparts = UI_Scene.All)]
        public float LoftRangeOverride = 15000;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftAltitudeAdvMax"),//Loft Maximum Altitude Advantage
            UI_FloatRange(minValue = 500f, maxValue = 10000f, stepIncrement = 100f, scene = UI_Scene.Flight, affectSymCounterparts = UI_Scene.All)]
        public float LoftAltitudeAdvMax = 3000;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftMinAltitude"),//Loft Maximum Altitude Advantage
            UI_FloatRange(minValue = 0f, maxValue = 10000f, stepIncrement = 100f, scene = UI_Scene.Flight, affectSymCounterparts = UI_Scene.All)]
        public float LoftMinAltitude = 2000;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftAngle"),//Loft Angle
            UI_FloatRange(minValue = 0f, maxValue = 90f, stepIncrement = 0.5f, scene = UI_Scene.Flight, affectSymCounterparts = UI_Scene.All)]
        public float LoftAngle = 45;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftTermAngle"),//Loft Termination Angle
            UI_FloatRange(minValue = 0f, maxValue = 90f, stepIncrement = 0.5f, scene = UI_Scene.Flight, affectSymCounterparts = UI_Scene.All)]
        public float LoftTermAngle = 20;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftRangeFac"),//Loft Range Factor
         UI_FloatRange(minValue = 0.1f, maxValue = 5.0f, stepIncrement = 0.01f, scene = UI_Scene.Flight)]
        public float LoftRangeFac = 0.5f;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftVelComp"),//Loft Velocity Compensation (Horizontal)
         UI_FloatRange(minValue = -2.0f, maxValue = 2.0f, stepIncrement = 0.01f, scene = UI_Scene.Flight)]
        public float LoftVelComp = -0.5f;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftVertVelComp"),//Loft Velocity Compensation (Vertical)
         UI_FloatRange(minValue = -2.0f, maxValue = 2.0f, stepIncrement = 0.01f, scene = UI_Scene.Flight)]
        public float LoftVertVelComp = -0.5f;

        //[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_LoftAltComp"), UI_FloatRange(minValue = -2000f, maxValue = 2000f, stepIncrement = 10f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Loft Altitude Compensation
        //public float LoftAltComp = 0;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_terminalHomingRange"),
            UI_FloatRange(minValue = 0f, maxValue = 40000f, stepIncrement = 500f, scene = UI_Scene.Flight, affectSymCounterparts = UI_Scene.All)]//Terminal Homing Range
        public float terminalHomingRange = 3000;

        [KSPField(advancedTweakable = false, isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_TurretID"),//Custom Turret ID
            UI_FloatRange(minValue = 0f, maxValue = 20f, stepIncrement = 1, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        public float customTurretID = 0;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_TurretLoft"),
         UI_Toggle(scene = UI_Scene.All)]
        public bool customTurretLoft = false; // Turret fires at a lofted trajectory

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_TurretLoftFac"),
            UI_FloatRange(minValue = 0, maxValue = 1, stepIncrement = 0.05f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.Editor)]
        public float customTurretLoftFac = 0.5f; // Factor for optimumVelocity for lofting, lower means a more lofted trajectory

        [KSPField]
        public bool terminalHoming = false;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_KappaAngle"), // Kappa Shaping Angle
            UI_FloatRange(minValue = 0f, maxValue = 90f, stepIncrement = 0.5f, scene = UI_Scene.Flight, affectSymCounterparts = UI_Scene.All)]
        public float kappaAngle = 45; // Kappa Guidance Vertical Shaping Angle

        [KSPField]
        public float missileRadarCrossSection = RadarUtils.RCS_MISSILES;            // radar cross section of this missile for detection purposes

        public enum MissileStates { Idle, Drop, Boost, Cruise, PostThrust }

        public enum DetonationDistanceStates { NotSafe, Cruising, CheckingProximity, Detonate }

        public enum TargetingModes { None, Radar, Heat, Laser, Gps, AntiRad, Inertial }

        public MissileStates MissileState { get; set; } = MissileStates.Idle;

        public DetonationDistanceStates DetonationDistanceState { get; set; } = DetonationDistanceStates.NotSafe;

        public enum GuidanceModes { None, AAMLead, AAMPure, AGM, AGMBallistic, Cruise, Weave, STS, Bomb, Orbital, BeamRiding, SLW, PN, APN, AAMLoft, Kappa, CLOS, CLOSThreePoint, CLOSLead }

        public GuidanceModes GuidanceMode;

        public enum WarheadTypes { Kinetic, Standard, ContinuousRod, Custom, CustomStandard, CustomContinuous, EMP, Nuke, Legacy, Launcher }

        public WarheadTypes warheadType = WarheadTypes.Kinetic;
        public bool HasFired { get; set; } = false;
        public MissileFire FiredByWM { get; set; } // The WM that fired this missile.

        public bool launched = false;

        public BDTeam Team { get; set; } = null;

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

        public enum LoftStates { PreLaunch, Boost, Midcourse, Terminal }

        public LoftStates loftState = LoftStates.PreLaunch;

        public bool TargetAcquired { get; set; }

        public bool ActiveRadar { get; set; }

        // Boolean, used to determine whether or not to update the missile's TargetInfo, should be set
        // to true whenever a change is to be made to the missile's RCS, like ActiveRadar or radarLOALSearching
        public bool updateRadarCS = false;

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

        protected float _lockFailTimer = -1;

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

        public List<ModuleCustomTurret> customTurret = new List<ModuleCustomTurret>();

        //laser stuff
        public ModuleTargetingCamera lockedCamera;
        protected Vector3 lastLaserPoint;
        protected Vector3 laserStartPosition;
        protected Vector3 startDirection;

        //GPS stuff
        public Vector3d targetGPSCoords;
        public float lastPingTime = -1f;
        //heat stuff
        public TargetSignatureData heatTarget;
        private TargetSignatureData predictedHeatTarget;

        //radar stuff
        public VesselRadarData vrd;
        public TargetSignatureData radarTarget;
        protected TargetSignatureData[] scannedTargets;
        private LineRenderer LR;

        //INS stuff
        public Vector3d TargetINSCoords { get; set; } = Vector3d.zero;
        public float TimeOfLastINS = 0;
        public float INStimetogo = 0;

        private int snapshotTicker;
        private int locksCount = 0;

        [KSPField] public float radarTimeout = -1;
        [KSPField] public float seekerTimeout = 5;
        [KSPField] public float terminalSeekerTimeout = -1;
        protected float nextRWRPing = 0;
        public int antiradTargets { get; protected set; }

        [KSPField]
        public bool antiradTargetPrediction = true;

        public bool radarLOALSearching { get; protected set; } = false;
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
                    laserStartPosition -= BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
                }
            }
        }

        public void ParseMissileType()
        {
            missileType = missileType.ToLower();
            _missileType = missileType switch
            {
                "bomb" => MissileType.Bomb,
                "torpedo" => MissileType.Torpedo,
                "depthcharge" => MissileType.DepthCharge,
                "aswmissile" => MissileType.ASWMissile,
                "sonobuoy" => MissileType.Sonobuoy,
                _ => MissileType.Missile
            };
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
            ParseMissileType();
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
                    if (craftPart.Current.weaponChannel > weaponChannel) continue;
                    if (craftPart.Current.engageRangeMax != engageRangeMax) continue;
                    if (craftPart.Current.missileFireAngle != missileFireAngle) continue;
                    missilecount += craftPart.Current.AmmoCount;
                }
            if (hasAmmo) missilecount += (int)reloadableRail.magazineAmmo;
        }

        public string GetSubLabel()
        {
            return Sublabel = $"Guidance: {Enum.GetName(typeof(TargetingModes), TargetingMode)}; Max Range: {Mathf.Round(engageRangeMax / 100) / 10} km; Boresight: {(missileFireAngle > 0 ? missileFireAngle : "360")}°; Remaining: {missilecount}";
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

            List<BDWarheadBase> tntList = part.FindModulesImplementing<BDWarheadBase>();
            foreach (BDWarheadBase tnt in tntList)
            {
                if (tnt)
                    tnt.Armed = false;
            }

            var emp = p.FindModuleImplementing<ModuleEMP>();
            if (emp != null) emp.Armed = false;
        }

        protected void SetupExplosive(Part p)
        {
            if (p == null) return;

            List<BDWarheadBase> tntList = part.FindModulesImplementing<BDWarheadBase>();
            foreach (BDWarheadBase tnt in tntList)
            {
                tnt.Armed = true;
                tnt.detonateAtMinimumDistance = DetonateAtMinimumDistance;
            }

            var emp = p.FindModuleImplementing<ModuleEMP>();
            if (emp != null) emp.Armed = true;
        }

        public abstract void Detonate();

        public abstract Vector3 GetForwardTransform();

        public abstract float GetKinematicTime(float minSpeed, out float kinematicRange);

        public abstract float GetKinematicSpeed();

        protected void AddTargetInfoToVessel()
        {
            TargetInfo info = vessel.gameObject.GetComponent<TargetInfo>();
            if (!info)
            {
                info = vessel.gameObject.AddComponent<TargetInfo>();
            }
            SetTargetInfo(info);
        }

        public void SetTargetInfo(TargetInfo info)
        {
            if (Team == null && FiredByWM)
            {
                Team = FiredByWM.Team;
            }
            info.Team = Team;
            info.isMissile = true;
            info.MissileBaseModule = this;
            updateRadarCS = true;
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
            Fields[nameof(gpsTargetName)].guiActive = true;
            var weaponManager = vessel.ActiveController().WM;
            if (weaponManager)
            {
                gpsTargetName = weaponManager.designatedGPSInfo.name;
                assignedGPSCoords = weaponManager.designatedGPSCoords;
            }
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
                    UpdateLaserTarget(true); //available cam for new GPS coords?
                    //Debug.Log($"[MissileBase] GPS vrd: {vrd != null}; vrd lock: {vrd && vrd.locked}");
                    if (vrd && vrd.locked)//no cam; available radar lock? 
                    {
                        List<TargetSignatureData> possibleTargets = vrd.GetLockedTargets();
                        for (int i = 0; i < possibleTargets.Count; i++)
                        {
                            if (possibleTargets[i].vessel == targetVessel.Vessel)
                                t = possibleTargets[i];
                        }
                        if (t.exists) TargetPosition = t.position;
                        //Debug.Log($"[MissileBase] GPS targetPosition is{TargetPosition.x:F2}, {TargetPosition.x:F2}, {TargetPosition.z:F2}");
                    }
                    if (TargetPosition != Vector3.zero)
                    {
                        float distanceToTargetSqr = (vessel.transform.position - gpsTargetCoords_).sqrMagnitude;
                        float jamDistance = RadarUtils.GetVesselECMJammingDistance(targetVessel.Vessel); //does the target have a jammer, and is the missile within the jammed AoE
                        if (jamDistance * jamDistance < distanceToTargetSqr) //outside/no area of interference, can receive GPS signal
                        {
                            //if (FiredByWM != null && FiredByWM.CanSeeTarget(targetVessel, false))

                            if (gpsUpdates == 0) // Constant updates
                            {
                                gpsTargetCoords_ = VectorUtils.WorldPositionToGeoCoords(TargetPosition, targetVessel.Vessel.mainBody);
                                targetGPSCoords = gpsTargetCoords_;
                            }
                            else // Update every gpsUpdates seconds
                            {
                                float updateCount = TimeIndex / gpsUpdates;
                                if (updateCount > gpsUpdateCounter)
                                {
                                    gpsUpdateCounter++;
                                    gpsTargetCoords_ = VectorUtils.WorldPositionToGeoCoords(TargetPosition, targetVessel.Vessel.mainBody);
                                    targetGPSCoords = gpsTargetCoords_;
                                }
                            }
                        }
                    }
                    //else 
                    // In theory if the jammer knew the GPS receiver channel/encryption, could transmit false coords using a more powerful signal to override out the originals...
                    //currently just cuts off updates and ordnance heads to last valid coords. Instead have jammer Strength come into play and have it be a jStrength * 4prDist^2 check that slows update 
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

            if (_lockFailTimer > seekerTimeout)
            {
                targetVessel = null;
                TargetAcquired = false;
                predictedHeatTarget.exists = false;
                predictedHeatTarget.signalStrength = 0; //have this instead set to originalHeatTarget missile had on initial lock?
                return;
            }

            if (heatTarget.exists && _lockFailTimer < 0)
            {
                _lockFailTimer = 0;
                predictedHeatTarget = heatTarget;
            }
            if (_lockFailTimer >= 0)
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
                float offBoresightAngle = VectorUtils.Angle(GetForwardTransform(), lookRay.direction);
                if (offBoresightAngle > maxOffBoresight)
                    lookRay = new Ray(lookRay.origin, Vector3.RotateTowards(lookRay.direction, GetForwardTransform(), (offBoresightAngle - maxOffBoresight) * Mathf.Deg2Rad, 0));

                DrawDebugLine(lookRay.origin, lookRay.origin + lookRay.direction * 10000, predictedHeatTarget.exists ? Color.magenta : heatTarget.exists ? Color.yellow : Color.blue);
                //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[MissileBase] offboresightAngle {offBoresightAngle > maxOffBoresight}; lockFailtimer: {lockFailTimer}; heatTarget? {heatTarget.exists}; predictedheattaret? {predictedHeatTarget.exists}; heatTarget vessel {(heatTarget.exists && heatTarget.vessel != null ? heatTarget.vessel.vesselName : "null")}");
                // Update heat target
                if (activeRadarRange < 0)
                {
                    heatTarget = BDATargetManager.GetAcousticTarget(SourceVessel, vessel, lookRay, predictedHeatTarget, lockedSensorFOV * 0.5f, heatThreshold, targetCoM, lockedSensorFOVBias, lockedSensorVelocityBias, lockedSensorVelocityMagnitudeBias, lockedSensorMinAngularVelocity,
                        FiredByWM, targetVessel, IFF: hasIFF);
                }
                else
                {
                    heatTarget = BDATargetManager.GetHeatTarget(SourceVessel, vessel, lookRay, predictedHeatTarget, lockedSensorFOV * 0.5f, heatThreshold, frontAspectHeatModifier, uncagedLock, targetCoM, lockedSensorFOVBias, lockedSensorVelocityBias, lockedSensorVelocityMagnitudeBias, lockedSensorMinAngularVelocity, FiredByWM, targetVessel, IFF: hasIFF);
                }

                // heatTarget.vessel == null should account for flares and decoys, but out of an abundance of caution
                // I've added a .isDecoy flag to TargetSignatureData, might be useful for other purposes too
                if (heatTarget.exists && (heatTarget.isDecoy || CheckTargetEngagementEnvelope(heatTarget.targetInfo)))
                {
                    TargetAcquired = true;
                    TargetPosition = heatTarget.position;
                    TargetVelocity = heatTarget.velocity;
                    TargetAcceleration = heatTarget.acceleration;
                    if (!heatTarget.isDecoy)
                    {
                        targetVessel = heatTarget.targetInfo;
                    }
                    _lockFailTimer = 0;

                    // Update target information
                    // if (heatTarget.vessel != predictedHeatTarget.vessel) Debug.LogError($"[IR DEBUG] Switching targets from {predictedHeatTarget.vessel.vesselName} to {heatTarget.vessel.vesselName}");
                    predictedHeatTarget = heatTarget;
                }
                else
                {
                    if (predictedHeatTarget.exists)
                    {
                        TargetPosition = predictedHeatTarget.position;
                        TargetVelocity = predictedHeatTarget.velocity;
                        TargetAcceleration = Vector3.zero;
                    }
                    _lockFailTimer += Time.fixedDeltaTime;
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
                    TargetPosition = lockedCamera.groundTargetPosition;
                    lastLaserPoint = lockedCamera.bodyRelativeGTP;
                    targetingPod = lockedCamera;
                    lockedCamera.guidingOrdnance = true;
                    timeOfLastLaserUpdate = -1f;
                }
            }
        }


        float timeOfLastLaserUpdate = -1f;
        protected void UpdateLaserTarget(bool gpsRequest = false)
        {
            if (TargetAcquired)
            {
                bool isCLOS = GuidanceMode == GuidanceModes.CLOS || GuidanceMode == GuidanceModes.CLOSThreePoint || GuidanceMode == GuidanceModes.CLOSLead;
                if (lockedCamera && !lockedCamera.gimbalLimitReached && lockedCamera.groundStabilized && lockedCamera.surfaceDetected) //active laser target
                {
                    TargetPosition = lockedCamera.groundTargetPosition;
                    TargetVelocity = (isCLOS || timeOfLastLaserUpdate <= 0f) ? Vector3.zero : (TargetPosition - VectorUtils.GetWorldSurfacePostion(lastLaserPoint, vessel.mainBody)) / (Time.time - timeOfLastLaserUpdate);
                    timeOfLastLaserUpdate = Time.time;
                    TargetAcceleration = Vector3.zero;
                    lastLaserPoint = lockedCamera.bodyRelativeGTP;
                    if (!gpsRequest)
                        _lockFailTimer = 0f;

                    if (GuidanceMode == GuidanceModes.BeamRiding && TimeIndex > 0.25f && Vector3.Dot(GetForwardTransform(), vessel.CoM - lockedCamera.transform.position) < 0)
                    {
                        TargetAcquired = false;
                        lockedCamera.guidingOrdnance = false;
                        lockedCamera = null;
                    }

                    return;
                }

                //lost active laser target, home on last known position
                Vector3 prevLaserPoint = VectorUtils.GetWorldSurfacePostion(lastLaserPoint, vessel.mainBody);
                Ray smokeRay = new Ray(vessel.CoM, (isCLOS && lockedCamera) ? lockedCamera.transform.position : (prevLaserPoint - vessel.CoM));
                if ((!isCLOS || lockedCamera) && CMSmoke.RaycastSmoke(smokeRay))
                {
                    float angle = VectorUtils.FullRangePerlinNoise(0.75f * Time.time, 10) * BDArmorySettings.SMOKE_DEFLECTION_FACTOR;
                    TargetPosition = isCLOS ?
                        VectorUtils.RotatePointAround(lockedCamera.targetPointPosition, smokeRay.origin, vessel.up, angle) :
                        VectorUtils.RotatePointAround(prevLaserPoint, vessel.CoM, vessel.up, angle);
                    lastLaserPoint = VectorUtils.WorldPositionToGeoCoords(TargetPosition, vessel.mainBody);
                    if (!gpsRequest)
                        _lockFailTimer = 0f;
                }
                else
                {
                    TargetPosition = prevLaserPoint;
                    if (!gpsRequest)
                    {
                        _lockFailTimer += Time.fixedDeltaTime;
                        if (_lockFailTimer > seekerTimeout)
                        {
                            TargetAcquired = false;
                            // Reset _lockFailTimer for search
                            _lockFailTimer = 0f;
                        }
                    }
                }

                TargetVelocity = Vector3.zero;
                TargetAcceleration = Vector3.zero;
            }
            else
            {
                ModuleTargetingCamera foundCam = null;
                bool parentOnly = GuidanceMode == GuidanceModes.BeamRiding || GuidanceMode == GuidanceModes.CLOS || GuidanceMode == GuidanceModes.CLOSThreePoint || GuidanceMode == GuidanceModes.CLOSLead;
                foundCam = BDATargetManager.GetLaserTarget(this, parentOnly, Team);
                float threshold = Mathf.Max(targetVessel ? targetVessel.Vessel.GetRadius() : 20, 20);
                if (foundCam != null && foundCam.cameraEnabled && foundCam.groundStabilized && BDATargetManager.CanSeePosition(foundCam.groundTargetPosition, vessel.transform.position, MissileReferenceTransform.position, threshold))
                {
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase]: {shortName} with UUID: {vessel.id}: Laser guided missileBase actively found laser point. Enabling guidance.");
                    lockedCamera = foundCam;
                    TargetAcquired = true;
                    if (!gpsRequest)
                        _lockFailTimer = 0;
                    SetLaserTargeting();
                    return;
                }

                if (!gpsRequest)
                {
                    _lockFailTimer += Time.fixedDeltaTime;
                    if (_lockFailTimer > seekerTimeout)
                    {
                        guidanceActive = false;
                    }
                }
            }
        }

        protected void UpdateRadarTarget()
        {
            TargetAcquired = false;

            if (radarTarget.exists)
            {
                Vector3 vectorToTarget = radarTarget.predictedPosition - transform.position;
                float angleToTarget = VectorUtils.Angle(vectorToTarget, GetForwardTransform());
                // locked-on before launch, passive radar guidance or waiting till in active radar range:
                if (!ActiveRadar && (vectorToTarget.sqrMagnitude > (activeRadarRange * activeRadarRange) || angleToTarget > maxOffBoresight * 0.75f))
                {
                    if (vrd && vrd.locked)
                    {
                        TargetSignatureData t = TargetSignatureData.noTarget;
                        if (canRelock && hasLostLock)
                        {
                            if (vrd.locked)
                            {
                                //t = vrd.lockedTargetData.targetData; //SARH is passive, and guided towards whatever is currently painted by FCS radar
                                //Debug.Log($"[MML RADAR DEBUG] missile switched target to {t.vessel.GetName()}");
                                List<TargetSignatureData> possibleTargets = vrd.GetLockedTargets();
                                for (int i = 0; i < possibleTargets.Count; i++)
                                {
                                    if (!possibleTargets[i].exists) continue;

                                    //re-check engagement envelope, only lock appropriate targets
                                    if (!CheckTargetEngagementEnvelope(possibleTargets[i].targetInfo)) continue;

                                    if (hasIFF && Team.IsFriendly(possibleTargets[i].Team)) continue;

                                    t = possibleTargets[i];

                                    break;
                                }
                            }
                        }
                        else
                        {
                            (t, bool tempLocked) = vrd.detectedRadarTargetLock(radarTarget.vessel, FiredByWM);
                            if (!tempLocked)
                            {
                                // If not locked, unset t
                                t = TargetSignatureData.noTarget;
                            }
                            /*List<TargetSignatureData> possibleTargets = vrd.GetLockedTargets();
                            for (int i = 0; i < possibleTargets.Count; i++)
                            {
                                if (possibleTargets[i].vessel == radarTarget.vessel) //this means SARH will remain locked to whatever was the initial target, regardless of current radar lock
                                {
                                    t = possibleTargets[i];
                                }
                            }*/
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
                            ModuleRadar lockingRadar = t.lockedByRadar;
                            TargetPosition = radarTarget.predictedPositionWithChaffFactor(!ActiveRadar ? lockingRadar.radarChaffClutterFactor * chaffEffectivity : chaffEffectivity,
                                                                                          !ActiveRadar ? lockingRadar._radarChaffNotchVFac * chaffNotchVFac : chaffNotchVFac,
                                                                                          !ActiveRadar ? lockingRadar._radarChaffNotchRFac * chaffNotchRFac : chaffNotchRFac);
                            TargetVelocity = radarTarget.velocity;
                            TargetAcceleration = radarTarget.acceleration;
                            targetVessel = t.targetInfo; //reset targetvessel in case of canRelock getting a new target
                            _lockFailTimer = 0;
                            startDirection = vectorToTarget; //have radar missile head in direction of last known radar contact if radarTarget goes off scope
                            return;
                        }
                        else
                        {
                            if (_lockFailTimer > 0f)
                            {
                                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase]: {shortName} with UUID: {vessel.id}: Semi-Active Radar guidance failed. Parent radar lost target.");
                                radarTarget = TargetSignatureData.noTarget;
                                targetVessel = null;
                                return;
                            }
                            else
                            {
                                if (_lockFailTimer == 0)
                                {
                                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase]: {shortName} with UUID: {vessel.id}: Semi-Active Radar guidance failed - waiting for data");
                                    hasLostLock = true;
                                }
                                _lockFailTimer += Time.fixedDeltaTime;
                                radarTarget.timeAcquired = Time.time;
                                radarTarget.position = radarTarget.predictedPosition;
                                //if (weaponClass == WeaponClasses.SLW)
                                //    TargetPosition = radarTarget.predictedPosition;
                                //else
                                TargetPosition = radarTarget.predictedPositionWithChaffFactor(chaffEffectivity, chaffNotchVFac, chaffNotchRFac);
                                TargetVelocity = radarTarget.velocity;
                                TargetAcceleration = Vector3.zero;
                                TargetAcquired = true;
                            }
                        }
                    }
                    else
                    {
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase]: {shortName} with UUID: {vessel.id}: Semi-Active Radar guidance failed. Out of range and no data feed.");
                        radarTarget = TargetSignatureData.noTarget;
                        targetVessel = null;
                        return;
                    }
                }
                else //onboard radar is on, or off but in range
                {
                    // active radar with target locked:
                    vrd = null;
                    /*if (angleToTarget > maxOffBoresight && TimeIndex > 3) //Give non-SARH VLS-launched missiles a 3sec grace period after launch to tip over towards target first before checking boresight
                    {
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase]: {shortName}: Active Radar guidance failed. Target is out of active seeker gimbal limits.");
                        radarTarget = TargetSignatureData.noTarget;
                        targetVessel = null;
                        return;
                    }
                    else*/
                    if (angleToTarget < maxOffBoresight || TimeIndex < 3) //Give non-SARH VLS-launched missiles a 3sec grace period after launch to tip over towards target first before checking boresight
                    {
                        if (scannedTargets == null) scannedTargets = new TargetSignatureData[BDATargetManager.LoadedVessels.Count];
                        //TargetSignatureData.ResetTSDArray(ref scannedTargets);
                        Ray ray = new Ray(transform.position, vectorToTarget);
                        bool pingRWR = Time.time >= nextRWRPing;
                        if (pingRWR) nextRWRPing = Time.time + RadarUtils.ACTIVE_MISSILE_PING_PERSIST_TIME;
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
                        int numLocked = RadarUtils.RadarUpdateMissileLock(ray, lockedSensorFOV, ref scannedTargets, RadarUtils.ACTIVE_MISSILE_PING_PERSIST_TIME, this, pingRWR);

                        if (radarLOAL && radarLOALSearching && !radarSnapshot)
                        {
                            //only scan on snapshot interval
                            TargetAcquired = true;
                        }
                        else
                        {
                            float sqrThresh = 250000f;// radarLOALSearching ? 250000f : 1600; // 500 * 500 : 40 * 40;
                            float closestDist = float.MaxValue;
                            TargetSignatureData selectedTarget = TargetSignatureData.noTarget;

                            if (weaponClass == WeaponClasses.SLW && vessel.Splashed || weaponClass != WeaponClasses.SLW && !vessel.IsUnderwater())
                            {
                                for (int i = 0; i < scannedTargets.Length; i++)
                                {
                                    // Once we've reached the last target we've locked, break
                                    if (i == numLocked) break;

                                    TargetSignatureData currTarget = scannedTargets[i];

                                    // Shouldn't happen, but if for some reason target doesn't exist -> continue
                                    if (!currTarget.exists)
                                    {
                                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.missileBase][Active Radar]: {shortName} with UUID: {vessel.id}: Target: null at index {i} doesn't exist!");
                                        continue;
                                    }

                                    float sqrDist = (currTarget.predictedPosition - radarTarget.predictedPosition).sqrMagnitude;
                                    if (sqrDist > sqrThresh || sqrDist > closestDist)
                                    {
                                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.missileBase][Radar LOAL]: {shortName} with UUID: {vessel.id}: Target: {currTarget.Name()} with UUID: {currTarget.ID()} at index {i} too far from target lock! SqrDist/MinSqrDist: {sqrDist}/{closestDist}");
                                        continue;
                                    }

                                    //re-check engagement envelope, only lock appropriate targets
                                    if (!CheckTargetEngagementEnvelope(currTarget.targetInfo))
                                    {
                                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase][Radar LOAL]: {shortName} with UUID: {vessel.id}: Target: {currTarget.Name()} with UUID: {currTarget.ID()} at index {i} rejected due to target envelope!");
                                        continue;
                                    }

                                    if (hasIFF && Team.IsFriendly(currTarget.Team))
                                    {
                                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase][Radar LOAL]: {shortName} with UUID: {vessel.id}: Target: {currTarget.Name()} with UUID: {currTarget.ID()} at index {i} rejected due to IFF! Team: {Team}, Target Team: {(currTarget.targetInfo.Team != null ? currTarget.targetInfo.Team.Name : "null")}");
                                        continue;
                                    }

                                    closestDist = sqrDist;
                                    selectedTarget = currTarget;

                                    //if (!scannedTargets[i].exists)
                                    //    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase][Radar Active]: Target: {i} doesn't exist!.");
                                    //if (scannedTargets[i].exists && (scannedTargets[i].predictedPosition - radarTarget.predictedPosition).sqrMagnitude >= sqrThresh)
                                    //    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase][Radar Active]: Target: {i} too far from target loc!.");
                                }
                            }
                            if (selectedTarget.exists)
                            {
                                radarTarget = selectedTarget;
                                TargetAcquired = true;
                                radarLOALSearching = false;
                                //if (weaponClass == WeaponClasses.SLW)
                                //    TargetPosition = radarTarget.predictedPosition + (radarTarget.velocity * Time.fixedDeltaTime);
                                //else
                                TargetPosition = radarTarget.predictedPositionWithChaffFactor(chaffEffectivity, chaffNotchVFac, chaffNotchRFac);

                                TargetVelocity = radarTarget.velocity;
                                TargetAcceleration = radarTarget.acceleration;
                                _lockFailTimer = 0;
                                if (!ActiveRadar)
                                {
                                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase]: {shortName} with UUID: {vessel.id}: Pitbull! Radar missileBase has gone active on target: {radarTarget.Name()} with UUID: {radarTarget.ID()}. Radar sig strength: {radarTarget.signalStrength:0.0}");
                                    //lets have this debug message fire only once, please
                                    updateRadarCS = true;
                                }
                                ActiveRadar = true;

                                if (weaponClass == WeaponClasses.SLW && vessel.Splashed) //depth charges, torpedos
                                {
                                    RadarWarningReceiver.PingRWR(ray, lockedSensorFOV, RadarWarningReceiver.RWRThreatTypes.Torpedo, RadarUtils.LAUNCH_PING_PERSIST_TIME, vessel);
                                }
                                else if (weaponClass != WeaponClasses.SLW && !vessel.IsUnderwater())
                                {
                                    RadarWarningReceiver.PingRWR(ray, lockedSensorFOV, RadarWarningReceiver.RWRThreatTypes.MissileLaunch, RadarUtils.LAUNCH_PING_PERSIST_TIME, vessel);
                                }
                                return;
                            }

                            if (!ActiveRadar && Time.time - TimeFired > 1)
                            {
                                if (locksCount > 2)
                                {
                                    guidanceActive = false;
                                    checkMiss = true;
                                    if (BDArmorySettings.DEBUG_MISSILES)
                                    {
                                        Debug.Log($"[BDArmory.MissileBase]: {shortName} with UUID: {vessel.id}: Active Radar guidance failed. Radar missileBase reached max re-lock attempts.");
                                    }
                                }
                                locksCount++;
                            }
                        }
                    }

                    if (radarLOAL)
                    {
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase]: {shortName} with UUID: {vessel.id}: Active Radar guidance target {(angleToTarget > maxOffBoresight ? "is outside of seeker gimbal limits" : "not found")}! Starting radarLOAL search!");

                        // Lost track of target, but we can re-acquire set radarLOALSearching = true and try to re-acquire using existing target information
                        if (!radarLOALSearching)
                        {
                            radarLOALSearching = true;
                            updateRadarCS = true;
                            startDirection = vectorToTarget;
                        }
                        TargetAcquired = true;

                        TargetPosition = radarTarget.predictedPositionWithChaffFactor(chaffEffectivity, chaffNotchVFac, chaffNotchRFac);
                        TargetVelocity = radarTarget.velocity;
                        TargetAcceleration = Vector3.zero;
                        ActiveRadar = false;
                        _lockFailTimer = 0;
                        radarTarget = TargetSignatureData.noTarget;
                    }
                    else
                    {
                        // Lost track of target and unable to re-acquire
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase]: {shortName} with UUID: {vessel.id}: Active Radar guidance failed. Target {(angleToTarget > maxOffBoresight ? "is outside of seeker gimbal limits" : "not found")}!");

                        radarTarget = TargetSignatureData.noTarget;
                        targetVessel = null;
                        radarLOALSearching = false;
                        radarLOAL = false;
                        TargetAcquired = false;
                        ActiveRadar = false;
                        updateRadarCS = true;
                        startDirection = GetForwardTransform();
                    }
                }
            }
            else if (radarLOAL && radarLOALSearching) //add a check for missing radar, so LOAL missiles that have been dumbfired can still activate?
            {
                // not locked on before launch, trying lock-on after launch:

                if (scannedTargets == null) scannedTargets = new TargetSignatureData[BDATargetManager.LoadedVessels.Count];
                //TargetSignatureData.ResetTSDArray(ref scannedTargets);
                Vector3 forward = GetForwardTransform();
                Ray ray = new Ray(transform.position, forward);
                bool pingRWR = Time.time >= nextRWRPing;
                if (pingRWR) nextRWRPing = Time.time + RadarUtils.ACTIVE_MISSILE_PING_PERSIST_TIME;
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
                int numLocked = RadarUtils.RadarUpdateMissileLock(ray, maxOffBoresight, ref scannedTargets, RadarUtils.ACTIVE_MISSILE_PING_PERSIST_TIME, this, pingRWR);

                //float sqrThresh = targetVessel != null ? 1000000 : 90000f; // 1000 * 1000 : 300 * 300; Expand threshold if no target to search for, grab first available target

                float smallestAngle = maxOffBoresight;
                float smallestDist = float.PositiveInfinity;
                float currDist = 0;
                float currAngle = 0;
                TargetSignatureData lockedTarget = TargetSignatureData.noTarget;
                bool useSoughtTarget = radarTarget.exists || targetVessel != null;
                Vector3 soughtTarget;
                if (useSoughtTarget)
                {
                    soughtTarget = radarTarget.exists ? radarTarget.predictedPosition : targetVessel.Vessel.CoM;
                }
                else
                {
                    soughtTarget = vessel.CoM;
                }

                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase][Radar LOAL]: {shortName} with UUID: {vessel.id}: Active radar found: {numLocked} targets; radarTarget?{radarTarget.exists}; tgtVessel? {targetVessel != null}");
                for (int i = 0; i < scannedTargets.Length; i++)
                {
                    // Once we've reached the last target we've locked, break
                    if (i == numLocked) break;

                    TargetSignatureData currTarget = scannedTargets[i];

                    if (!currTarget.exists)
                    {
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.missileBase][Radar LOAL]: {shortName} with UUID: {vessel.id}: Target: null at index {i} doesn't exist!");
                        continue;
                    }

                    float tempDist = -1f;
                    if (useSoughtTarget && (tempDist = (currTarget.predictedPosition - soughtTarget).sqrMagnitude) > 1000000f)
                    {
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.missileBase][Radar LOAL]: {shortName} with UUID: {vessel.id}: Target: {currTarget.Name()} with UUID: {currTarget.ID()} at index {i} too far from target lock!.");
                        continue;
                    }

                    //re-check engagement envelope, only lock appropriate targets
                    if (!CheckTargetEngagementEnvelope(currTarget.targetInfo))
                    {
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase][Radar LOAL]: {shortName} with UUID: {vessel.id}: Target: {currTarget.Name()} with UUID: {currTarget.ID()} at index {i} rejected due to target envelope!");
                        continue;
                    }

                    //Don't lock friendlies
                    if (hasIFF && Team.IsFriendly(currTarget.targetInfo.Team))
                    {
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase][Radar LOAL]: {shortName} with UUID: {vessel.id}: Target: {currTarget.Name()} with UUID: {currTarget.ID()} at index {i} rejected due to IFF! Team: {Team}, Target Team: {(currTarget.targetInfo.Team != null ? currTarget.targetInfo.Team.Name : "null")}");
                        continue;
                    }

                    if (!useSoughtTarget)
                    {
                        (tempDist, Vector3 currDir) = (currTarget.predictedPosition - soughtTarget).MagNorm();
                        currAngle = Mathf.Rad2Deg * Mathf.Acos(Vector3.Dot(currDir, forward));
                        if (currAngle > (smallestAngle + 5f))
                        {
                            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase][Radar LOAL]: {shortName} with UUID: {vessel.id}: Target: {currTarget.Name()} with UUID: {currTarget.ID()} rejected due to angle! currAngle: {currAngle}°, smallestAngle: {smallestAngle}°");
                            continue; // Look for the smallest angle, give 5 degrees of wiggle room.
                        }
                        // Look for closest target to the missile
                        currDist = tempDist;
                    }
                    else
                    {
                        // Look for closest target to the previous target location
                        currDist = tempDist;
                    }

                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase][Radar LOAL]: {shortName} with UUID: {vessel.id}: Target: {currTarget.Name()} has {(targetVessel == null ? "currDist" : "currSqrDist")}: {currDist}.");

                    if (currDist < smallestDist)
                    {
                        if (!useSoughtTarget && currAngle < smallestAngle)
                        {
                            smallestAngle = currAngle;
                        }
                        smallestDist = currDist;
                        lockedTarget = scannedTargets[i];
                        ActiveRadar = true;
                        updateRadarCS = true;
                        //if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase][Radar LOAL]: Target: {scannedTargets[i].vessel.vesselName} selected.");
                    }
                }

                if (lockedTarget.exists)
                {
                    radarTarget = lockedTarget;
                    targetVessel = radarTarget.targetInfo;
                    TargetAcquired = true;
                    radarLOALSearching = false;
                    //if (weaponClass == WeaponClasses.SLW)
                    //    TargetPosition = radarTarget.predictedPosition + (radarTarget.velocity * Time.fixedDeltaTime);
                    //else
                    TargetPosition = radarTarget.predictedPositionWithChaffFactor(chaffEffectivity, chaffNotchVFac, chaffNotchRFac);
                    TargetVelocity = radarTarget.velocity;
                    TargetAcceleration = radarTarget.acceleration;
                    startDirection = TargetPosition - transform.position;
                    if (!ActiveRadar && Time.time - TimeFired > 1)
                    {
                        if (weaponClass == WeaponClasses.SLW && vessel.Splashed) //depth charges, torpedos
                            RadarWarningReceiver.PingRWR(new Ray(transform.position, radarTarget.predictedPosition - transform.position), lockedSensorFOV, RadarWarningReceiver.RWRThreatTypes.Torpedo, RadarUtils.LAUNCH_PING_PERSIST_TIME, vessel);
                        else if (weaponClass != WeaponClasses.SLW && !vessel.IsUnderwater())
                            RadarWarningReceiver.PingRWR(new Ray(transform.position, radarTarget.predictedPosition - transform.position), lockedSensorFOV, RadarWarningReceiver.RWRThreatTypes.MissileLaunch, RadarUtils.LAUNCH_PING_PERSIST_TIME, vessel);

                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase]: {shortName} with UUID: {vessel.id}: Pitbull! Radar missileBase has gone active on Target: {radarTarget.Name()} with UUID: {radarTarget.ID()}. Radar sig strength: {radarTarget.signalStrength:0.0}");
                    }
                    return;
                }
                else
                {
                    radarTarget = TargetSignatureData.noTarget;
                    TargetAcquired = true;
                    TargetPosition = transform.position + (startDirection * 5000);
                    TargetVelocity = Vector3.zero; // Set the target velocity to 0.
                    TargetAcceleration = Vector3.zero;
                    if (!radarLOALSearching)
                    {
                        radarLOALSearching = true;
                        updateRadarCS = true;
                    }
                    _lockFailTimer += Time.fixedDeltaTime;
                    if (_lockFailTimer > seekerTimeout)
                    {
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase]: {shortName} with UUID: {vessel.id}: Active Radar guidance failed. LOAL could not lock a target.");
                        radarLOAL = false;
                        targetVessel = null;
                        radarLOALSearching = false;
                        TargetAcquired = false;
                        ActiveRadar = false;
                        updateRadarCS = true;
                    }
                    return;
                }
            }

            if (!radarTarget.exists)
            {
                if (_lockFailTimer < seekerTimeout)
                {
                    if (radarLOAL)
                    {
                        if (!radarLOALSearching)
                        {
                            radarLOALSearching = true;
                            updateRadarCS = true;
                            TargetAcquired = true;
                        }
                    }
                    else
                    {
                        _lockFailTimer += Time.fixedDeltaTime;
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase]: {shortName} with UUID: {vessel.id}: No assigned radar target. Awaiting timeout({seekerTimeout - _lockFailTimer}).... ");
                    }
                }
                else
                {
                    targetVessel = null;
                    TargetAcquired = false;
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase]: {shortName} with UUID: {vessel.id}: No radar target. Active Radar guidance timed out. ");
                }
            }
        }

        protected bool CheckTargetEngagementEnvelope(TargetInfo ti)
        {
            if (ti == null) return false;
            return (ti.isMissile && engageMissile) ||
                    (!ti.isMissile && ti.isFlying && engageAir) ||
                    (ti.isLandedOrSurfaceSplashed && engageGround) ||
                    (ti.isSplashed && engageSLW);
        }

        protected void ReceiveRadarPing(Vessel v, Vector3 source, RadarWarningReceiver.RWRThreatTypes type, float persistTime, Vessel vSource)
        {
            if (TargetingMode == TargetingModes.AntiRad && TargetAcquired && v == vessel)
            {
                // These CanDetectRWRThreat function calls can probably be replaced with the actual code,
                // but I think this is more readable and maintainable for anyone not familiar with bitmasks
                if (!RadarWarningReceiver.CanDetectRWRThreat(antiradTargets, type)) return;  //Type check, so a different RWRType ping doesn't decoy the ARM. multiple radar sources on the same frequency within boresight will canse missile to pingpong between them, if sufficiently close to each other.
                //if (targetVessel != null) //filter on a per-vessel basis? Technically speaking, as a passive sensor, ARH would have no way of distinguishing a specific vessel to focus on, and ping filtering would need to be based on distance from previous ping(s)
                //{
                //    if ((VectorUtils.WorldPositionToGeoCoords(source, vessel.mainBody) - VectorUtils.WorldPositionToGeoCoords(targetVessel.Vessel.CoM, vessel.mainBody)).sqrMagnitude > Mathf.Max(400, 0.013f * (!vessel.InVacuum() ? (float)targetVessel.Vessel.srf_velocity.sqrMagnitude : (float)targetVessel.Vessel.obt_velocity.sqrMagnitude)) return;
                //}

                // Given that we have a position filter, this ping time based filter is not needed and will worsen the performance of ARMs against vessels with multiple radars
                //if (Time.time - lastPingTime < persistTime) return; //if multiple radar sources in boresite, filter the ones we don't want. How does the misisle know this interval?
                // presumably, the launching craft has held the intended target in boresight fo a few seconds before launch, and the missile can log the charateristics of the intended radar source pre-launch.
                //persistTime is a bit shorter than actual radar sweep interval, so there's some margin built in as ping times shift slightly due to maneuvering changing relative location on radar scope.
                //Technically, ARH should probably be able to log signal *strength* as well as type and frequency to help filter out extraneous radar sources to prevent getting decoyed by a different source...

                // Ping was close to the previous target position and is within the boresight of the missile.
                //this needs to look at previous ping, and filter ping that's closest to previous position. Easy, if they all happened at the same time. If they're coming in one at a time...
                //var staticLaunchThresholdSqr = maxStaticLaunchRange * maxStaticLaunchRange / 16f; //this is a huge threshold. 7.5km for the stock HARM. shouldn't it instead be something akin to MissileFire.GPSDistanceCheck? Only have it grab pings that are within ~20m of previous ping?
                float pingDistanceThreshold = (BodyUtils.GetRadarAltitudeAtPos(source) < 20 ? 50 : 350) * Mathf.Max(1, persistTime); //ground stuff likely not going to move > 50m/s, air stuff... min 350m should be sufficient? Anything with slow ping time will get extended threshold, and faster stuff will be pinging more than 1/s
                pingDistanceThreshold *= pingDistanceThreshold;
                //if ((source - VectorUtils.GetWorldSurfacePostion(targetGPSCoords, vessel.mainBody)).sqrMagnitude < staticLaunchThresholdSqr && VectorUtils.Angle(source - transform.position, GetForwardTransform()) < maxOffBoresight)

                Vector3 prevPos = VectorUtils.GetWorldSurfacePostion(targetGPSCoords, vessel.mainBody);

                //should this be predicted ping pos instead of last ping pos? targetGPSCoords + (lastPingCoords - targetGPSCoords) * (lastPingCoords - targetGPSCoords).distance?
                if ((source - prevPos).sqrMagnitude < pingDistanceThreshold && VectorUtils.Angle(source - transform.position, GetForwardTransform()) < maxOffBoresight)
                {
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase]: {shortName} with UUID: {vessel.id}: Radar ping! Adjusting target position by {(source - VectorUtils.GetWorldSurfacePostion(targetGPSCoords, vessel.mainBody)).magnitude} to {TargetPosition}, ping type {type} from vessel {v.vesselName}");
                    TargetAcquired = true;
                    TargetPosition = source;
                    // Because the reference frame is constantly rotating, this is not gonna be hugely accurate over time...
                    // but then again, ARMs vs moving targets isn't really supposed to be that good of a matchup to begin with
                    if (antiradTargetPrediction && lastPingTime > 0)
                    {
                        TargetVelocity = (TargetPosition - prevPos) / (Time.time - lastPingTime);
                    }
                    else
                    {
                        TargetVelocity = Vector3.zero;
                    }
                    targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(TargetPosition, vessel.mainBody);
                    TargetAcceleration = Vector3.zero;
                    _lockFailTimer = 0;
                    lastPingTime = Time.time;
                }
            }
        }

        protected void UpdateAntiRadiationTarget()
        {
            if (FlightGlobals.ready && TargetAcquired)
            {
                if (_lockFailTimer < 0)
                {
                    _lockFailTimer = 0;
                }
                _lockFailTimer += Time.fixedDeltaTime;
                if (_lockFailTimer > seekerTimeout)
                {
                    TargetAcquired = false;
                }
            }
            if (targetGPSCoords != Vector3d.zero)
            {
                TargetPosition = antiradTargetPrediction ? VectorUtils.GetWorldSurfacePostion(targetGPSCoords, vessel.mainBody) + TargetVelocity * (Time.time - lastPingTime) : VectorUtils.GetWorldSurfacePostion(targetGPSCoords, vessel.mainBody);
                if (BDArmorySettings.DEBUG_LINES)
                    DrawDebugLine(vessel.CoM, TargetPosition, Color.blue);
            }
            else
            {
                if (BDArmorySettings.DEBUG_LINES)
                    DrawDebugLine(vessel.CoM, vessel.CoM + MissileReferenceTransform.forward * 10000, Color.grey);
            }
        }
        private bool setInertialTarget = false;

        public Vector3d UpdateInertialTarget()
        {
            Vector3 TargetCoords_;
            Vector3 TargetLead;
            ModuleRadar detectedByRadar = null;
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

            if (_lockFailTimer > seekerTimeout)
            {
                targetVessel = null;
                TargetAcquired = false;
                guidanceActive = false;
                return TargetCoords_;
            }
            if (targetVessel && _lockFailTimer < 0)
            {
                _lockFailTimer = 0;
            }
            if (targetVessel && HasFired)
            {
                if (gpsUpdates >= 0f)
                {
                    TargetSignatureData INStarget = TargetSignatureData.noTarget;
                    bool radarLocked = false;
                    if (FiredByWM != null && FiredByWM.vesselRadarData)
                    {
                        if (FiredByWM._radarsEnabled || weaponClass == WeaponClasses.SLW && FiredByWM._sonarsEnabled)
                        {
                            (INStarget, radarLocked) = FiredByWM.vesselRadarData.detectedRadarTargetGetRadar(targetVessel.Vessel, FiredByWM); //is the target tracked by radar or ISRT?
                        }
                        if (INStarget.exists)
                        {
                            detectedByRadar = INStarget.lockedByRadar;
                        }
                        else
                        {
                            if (FiredByWM._irstsEnabled) INStarget = FiredByWM.vesselRadarData.activeIRTarget(targetVessel.Vessel, FiredByWM, true);
                        }
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
                                TargetINSCoords = INStarget.geoPos; //VectorUtils.WorldPositionToGeoCoords(INStarget.position, targetVessel.Vessel.mainBody);
                                TimeOfLastINS = TimeIndex;
                                TargetLead = MissileGuidance.GetAirToAirFireSolution(this, targetVessel.Vessel, out INStimetogo);
                                if (detectedByRadar) TargetLead += (INStarget.predictedPositionWithChaffFactor(detectedByRadar.radarChaffClutterFactor, detectedByRadar._radarChaffNotchVFac, detectedByRadar._radarChaffNotchRFac) - INStarget.position);
                                TargetCoords_ = VectorUtils.WorldPositionToGeoCoords(TargetLead, targetVessel.Vessel.mainBody);
                                targetGPSCoords = TargetCoords_;
                            }
                            else //clamp updates to radar/IRST track speed
                            {
                                float updateCount = TimeIndex / GpsUpdateMax;
                                if (updateCount > gpsUpdateCounter)
                                {
                                    gpsUpdateCounter++;
                                    TargetINSCoords = INStarget.geoPos; //VectorUtils.WorldPositionToGeoCoords(targetVessel.Vessel.CoM, targetVessel.Vessel.mainBody);
                                    TimeOfLastINS = TimeIndex;
                                    TargetLead = MissileGuidance.GetAirToAirFireSolution(this, targetVessel.Vessel, out INStimetogo);
                                    if (detectedByRadar) TargetLead += (INStarget.predictedPositionWithChaffFactor(detectedByRadar.radarChaffClutterFactor, detectedByRadar._radarChaffNotchVFac, detectedByRadar._radarChaffNotchRFac) - INStarget.position);
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
                        float angleToTarget = VectorUtils.Angle(targetVessel.Vessel.CoM - transform.position, GetForwardTransform());

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
                    _lockFailTimer = 0;
                }
                else if (gpsUpdates >= 0)
                {
                    _lockFailTimer += Time.fixedDeltaTime;
                }
            }
            else
            {
                _lockFailTimer += Time.fixedDeltaTime;
            }
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
                                    if ((toSource.sqrMagnitude) >= (sourceRadius + 10f) * (sourceRadius + 10f))
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

        protected void CheckCountermeasureDistance()
        {
            if (missileCMRange > 0)
            {
                if (CMenabled)
                {
                    if (missileCMInterval > 0 && (Time.time - missileCMTime) > missileCMInterval)
                    {
                        missileCMTime = Time.time;

                        DropCountermeasures();
                    }
                }
                else if ((TargetPosition - vessel.CoM).sqrMagnitude < missileCMRange * missileCMRange)
                {
                    InitializeCountermeasures();
                }
            }
        }

        protected abstract void InitializeCountermeasures();

        protected abstract void DropCountermeasures();

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
                GUI.Label(new Rect(200, Screen.height - 300, 800, 300), $"{this.shortName} UUID: {vessel.id}\n{debugString}");
            }
        }
        float tntMass = -1;
        public float GetTntMass()
        {
            if (tntMass > 0) return tntMass;
            if (warheadType == WarheadTypes.EMP)
            {
                var EMP = part.FindModuleImplementing<ModuleEMP>();
                if (EMP != null)
                {
                    tntMass = EMP.proximity;
                    return tntMass;
                }
            }
            else if (warheadType == WarheadTypes.Nuke)
            {
                var nuke = part.FindModuleImplementing<BDModuleNuke>();
                if (nuke != null)
                {
                    tntMass = nuke.yield * 1000; /// 000; //Technically should be a x1 million mult, but this keeps things managable for maxTNTOnTarget values
                    return tntMass;
                }
            }
            else if (warheadType == WarheadTypes.Kinetic)
            {
                tntMass = part.mass;
                return tntMass;
            }
            else
            {
                var MMG = part.FindModuleImplementing<BDModularGuidance>();
                if (MMG != null)
                {
                    tntMass = MMG.warheadYield;
                    return tntMass;
                }
                var HE = part.FindModuleImplementing<BDExplosivePart>();
                var MML = part.FindModuleImplementing<MultiMissileLauncher>();
                if (HE != null)
                {
                    List<BDExplosivePart> tntList = part.FindModulesImplementing<BDExplosivePart>();
                    foreach (BDExplosivePart tnt in tntList)
                    {
                        tntMass = Mathf.Max(tntMass, tnt.tntMass);
                    }
                    return tntMass;
                }
                else if (MML != null)
                {
                    tntMass = MML.tntMass;
                    return tntMass;
                }
                else tntMass = 1;
            }            
            return tntMass;
        }

        public void CheckDetonationState(bool separateWarheads = false, bool preventProxyArming = false, bool allowMines = false)
        {
            //Guard clauses
            //if (!TargetAcquired) return;
            Vector3 missileVel = vessel.Velocity();
            var targetDistancePerFrame = Time.fixedDeltaTime * (TargetVelocity - BDKrakensbane.FrameVelocityV3f) + 0.5f * Time.fixedDeltaTime * Time.fixedDeltaTime * TargetAcceleration;
            var missileDistancePerFrame = Time.fixedDeltaTime * (missileVel - BDKrakensbane.FrameVelocityV3f) + 0.5f * Time.fixedDeltaTime * Time.fixedDeltaTime * vessel.acceleration_immediate;

            var futureTargetPosition = (TargetPosition + targetDistancePerFrame);
            var futureMissilePosition = (vessel.CoM + missileDistancePerFrame);

            float relativeSpeed = (float)(TargetVelocity - missileVel).magnitude * Time.fixedDeltaTime; // relativeSpeed is actually a distance!

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
                        if (!TargetAcquired && !allowMines) return;

                        // If impact fuze, or arming delay has prevented the proxy fuze from arming itself
                        if (DetonationDistance == 0 || preventProxyArming)
                        {
                            if (weaponClass == WeaponClasses.Bomb) return;

                            if (TimeIndex > 1f)
                            {
                                Ray rayFuturePosition = new Ray(vessel.CoM, missileDistancePerFrame);
                                //float dist = Time.fixedDeltaTime * (float)vessel.Velocity().magnitude;
                                var hitCount = Physics.RaycastNonAlloc(rayFuturePosition, proximityHits, relativeSpeed, layerMask);
                                if (hitCount == proximityHits.Length) // If there's a whole bunch of stuff in the way (unlikely), then we need to increase the size of our hits buffer.
                                {
                                    proximityHits = Physics.RaycastAll(rayFuturePosition, relativeSpeed, layerMask);
                                    hitCount = proximityHits.Length;
                                }
                                if (hitCount > 0)
                                {
                                    int closestHitIndex = -1;
                                    float closestDist = float.MaxValue;
                                    for (int i = 0; i < hitCount; i++)
                                    {
                                        RaycastHit hit = proximityHits[i];

                                        if (closestDist < hit.distance) continue;

                                        Part hitPart = hit.collider.gameObject.GetComponentInParent<Part>();
                                        if (hitPart == null) continue;
                                        if (ProjectileUtils.IsIgnoredPart(hitPart)) continue; // Ignore ignored parts.

                                        if (hitPart.vessel != SourceVessel && hitPart.vessel != vessel)
                                        {
                                            closestHitIndex = i;
                                            closestDist = hit.distance;
                                        }
                                    }

                                    if (closestHitIndex >= 0)
                                    {
                                        RaycastHit closestHit = proximityHits[closestHitIndex];
                                        //We found a hit to other vessel, set transform.position to hit point (moves immediately, but doesn't update .CoM fields, etc)
                                        vessel.SetPosition(closestHit.point - 0.1f * rayFuturePosition.direction);
                                        DetonationDistanceState = DetonationDistanceStates.Detonate;
                                        if (BDArmorySettings.DEBUG_MISSILES)
                                        {
                                            Part hitPart = closestHit.collider.gameObject.GetComponentInParent<Part>();
                                            Debug.Log($"[BDArmory.MissileBase] Contact Detonation! part: {hitPart} on vessel: {(hitPart.vessel ? hitPart.vessel.vesselName : "null")}");
                                        }
                                        Detonate();
                                        return;
                                    }
                                }
                                else if (TargetAcquired && targetVessel != null && targetVessel.Vessel != null)
                                {
                                    // For very high speed intercepts when missiles may phase through small vessels/missiles within a frame
                                    // Use the real position and velocity for this
                                    Vector3d relVel = targetVessel.Vessel.Velocity() - missileVel;
                                    Vector3 relPos = targetVessel.Vessel.CoM - vessel.CoM;
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
                                            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase] Contact Detonation! Target vessel {targetVessel.Vessel.vesselName} hit by raycast!");
                                            vessel.SetPosition(hit.point - 0.1f * ray.direction); // Slightly back so that shaped charge explosives hit properly.
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

                                                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileBase] Contact Detonation! Target vessel {targetVessel.Vessel.vesselName} hit by min sep! Dist at detonation: {AIUtils.PredictPosition(relPos, relVel, relAccel, cpaTime).magnitude}");

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
                            float blastR;
                            Vector3 targetPoint = (warheadType == WarheadTypes.ContinuousRod ? vessel.CoM + (Mathf.Sign(Vector3.Dot(TargetPosition - vessel.CoM, vessel.up)) * ((blastR = GetBlastRadius()) > 0f ? Mathf.Min(blastR, DetonationDistance) / 3f : 5f)) * vessel.up : vessel.CoM);
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
                DetonationDistance = GetInitialDetonationDistance(GetBlastRadius());
            }
            if (BDArmorySettings.DEBUG_MISSILES)
            {
                Debug.Log($"[BDArmory.MissileBase]: DetonationDistance = : {DetonationDistance}");
            }
        }

        public float GetInitialDetonationDistance(float bRadius)
        {
            return GuidanceMode switch
            {
                GuidanceModes.AAMLead or GuidanceModes.AAMPure or GuidanceModes.PN or GuidanceModes.APN or GuidanceModes.AAMLoft or GuidanceModes.Kappa or GuidanceModes.CLOSThreePoint or GuidanceModes.CLOSLead => bRadius * 0.25f,
                _ => 0f
            };
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
            if (Events[nameof(CruiseAltitudeRange)].guiName == "Change to Low Altitude Range")
            {
                Events[nameof(CruiseAltitudeRange)].guiName = "Change to High Altitude Range";

                UI_FloatRange cruiseAltitudeField = (UI_FloatRange)Fields[nameof(CruiseAltitude)].uiControlEditor;
                cruiseAltitudeField.maxValue = 500f;
                cruiseAltitudeField.minValue = 5f;
                cruiseAltitudeField.stepIncrement = 5f;
            }
            else
            {
                Events[nameof(CruiseAltitudeRange)].guiName = "Change to Low Altitude Range";
                UI_FloatRange cruiseAltitudField = (UI_FloatRange)Fields[nameof(CruiseAltitude)].uiControlEditor;
                cruiseAltitudField.maxValue = 25000f;
                cruiseAltitudField.minValue = 500;
                cruiseAltitudField.stepIncrement = 500f;
            }
        }

        public virtual bool HasTurrets()
        {
            return customTurret.Count > 0;
        }

        public virtual void SetSlavedGuard(bool slavedGuard)
        {
            for (int i = 0; i < customTurret.Count; i++)
            {
                if (customTurret[i] == null) continue;
                if (customTurret[i].vessel != vessel) continue;
                customTurret[i].SetSlavedGuard(slavedGuard, this);
            }
        }

        public virtual bool AimTurrets(Vector3 targetPos)
        {
            for (int i = 0; i < customTurret.Count; i++)
            {
                if (customTurret[i] == null) continue;
                if (customTurret[i].vessel != vessel) continue;
                customTurret[i].slavedTargetPosition = targetPos;
            }
            return customTurret.Count > 0;
        }

        public virtual Vector3 TurretAimPosition()
        {
            for (int i = 0; i < customTurret.Count; i++)
            {
                if (customTurret[i] == null) continue;
                if (customTurret[i].vessel != vessel) continue;
                return customTurret[i].slavedTargetPosition;
            }

            return Vector3.zero;
        }
    }
}
