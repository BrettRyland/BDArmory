using KSP.UI.Screens;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using System;
using UniLinq;
using UnityEngine;

using BDArmory.Bullets;
using BDArmory.Competition;
using BDArmory.Control;
using BDArmory.Damage;
using BDArmory.Extensions;
using BDArmory.FX;
using BDArmory.GameModes;
using BDArmory.ModIntegration;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Weapons.Missiles;
using BDArmory.WeaponMounts;
using static BDArmory.Bullets.PooledBullet;

namespace BDArmory.Weapons
{
    public class ModuleWeapon : EngageableWeapon, IBDWeapon
    {
        #region Declarations

        public static ObjectPool bulletPool;

        public static Dictionary<string, ObjectPool> rocketPool = new Dictionary<string, ObjectPool>(); //for ammo switching
        public static ObjectPool shellPool;

        Coroutine startupRoutine;
        Coroutine shutdownRoutine;
        Coroutine standbyRoutine;
        Coroutine reloadRoutine;
        Coroutine chargeRoutine;

        bool finalFire;

        public int rippleIndex = 0;
        public string OriginalShortName { get; private set; }

        // WeaponTypes.Cannon is deprecated.  identical behavior is achieved with WeaponType.Ballistic and bulletInfo.explosive = true.
        public enum WeaponTypes
        {
            Ballistic,
            Rocket, //Cannon's deprecated, lets use this for rocketlaunchers
            Laser
        }

        public enum WeaponStates
        {
            Enabled,
            Disabled,
            PoweringUp,
            PoweringDown,
            Locked,
            Standby, // Not currently firing, but can still track the current target.
            EnabledForSecondaryFiring // Enabled, but only for secondary firing.
        }

        //public enum BulletDragTypes
        //{
        //    None,
        //    AnalyticEstimate,
        //    NumericalIntegration
        //}

        //public enum FuzeTypes
        //{
        //    None,       //So very tempted to have none be 'no fuze', and HE rounds with fuzetype = None act just like standard slug rounds
        //    Timed,      //detonates after set flighttime. Main use case probably AA, assume secondary contact fuze
        //    Proximity,  //detonates when in proximity to target. No need for secondary contact fuze
        //    Flak,       //detonates when in proximity or after set flighttime. Again, shouldn't need secondary contact fuze
        //    Delay,      //detonates 0.02s after any impact. easily defeated by whipple shields
        //    Penetrating,//detonates 0.02s after penetrating a minimum thickness of armor. will ignore lightly armored/soft hits
        //    Impact      //standard contact + graze fuze, detonates on hit
        //                //Laser     //laser-guided smart rounds?
        //}

        //public enum FillerTypes
        //{
        //    None,       //No HE filler, non-explosive slug.
        //    Standard,   //standard HE filler for a standard exposive shell
        //    Shaped      //shaped charge filler, for HEAT rounds and similar
        //}

        public enum APSTypes
        {
            Ballistic,
            Missile,
            Omni,
            None
        }
        public WeaponStates weaponState = WeaponStates.Disabled;

        //animations
        private float fireAnimSpeed = 1;
        //is set when setting up animation so it plays a full animation for each shot (animation speed depends on rate of fire)

        public float bulletBallisticCoefficient;

        public WeaponTypes eWeaponType;

        public BulletFuzeTypes eFuzeType;

        public PooledBulletTypes eHEType;

        public APSTypes eAPSType;

        public float heat;
        public bool isOverheated;

        private bool isRippleFiring = false;//used to tell when weapon has started firing for initial ripple delay

        private bool wasFiring;
        //used for knowing when to stop looped audio clip (when you're not shooting, but you were)

        AudioClip reloadCompleteAudioClip;
        AudioClip fireSound;
        AudioClip overheatSound;
        AudioClip chargeSound;
        AudioSource audioSource;
        AudioSource audioSource2;
        AudioLowPassFilter lowpassFilter;

        private BDStagingAreaGauge gauge;
        private int AmmoID;
        private int ECID;
        //AI
        public bool aiControlled = false;
        public bool autoFire;
        public float autoFireLength = 0;
        public float autoFireTimer = 0;
        public float autofireShotCount = 0;
        bool aimAndFireIfPossible = false;
        bool aimOnly = false;

        //used by AI to lead moving targets
        private float targetDistance = 8000f;
        private float origTargetDistance = 8000f;
        public float targetRadius = 35f; // Radius of target 2° @ 1km.
        public float targetAdjustedMaxCosAngle
        {
            get
            {
                var fireTransform = (eWeaponType == WeaponTypes.Rocket && rocketPod) ? (rockets[0] != null ? rockets[0].parent : null) : fireTransforms != null ? fireTransforms[0] : null;
                if (fireTransform == null) return 1f;
                var theta = FiringTolerance * targetRadius / (finalAimTarget - fireTransform.position).magnitude + Mathf.Deg2Rad * maxDeviation / 2f; // Approximation to arctan(α*r/d) + θ/2. (arctan(x) = x-x^3/3 + O(x^5))
                return finalAimTarget.IsZero() ? 1f : Mathf.Max(1f - 0.5f * theta * theta, 0); // Approximation to cos(theta). (cos(x) = 1-x^2/2!+O(x^4))
            }
        }
        public Vector3 atprTargetPosition;
        public Vector3 targetPosition;
        public Vector3 targetVelocity;  // local frame velocity
        readonly SmoothingV3 targetVelocitySmoothing = new(); // Smoothing for the target's velocity, required for long-range aiming.
        public Vector3 targetAcceleration; // local frame
        readonly SmoothingV3 targetAccelerationSmoothing = new(); // Smoothing for the target's acceleration, required for long-range aiming.
        private Vector3 smoothedPartVelocity; // Also apply smoothing to the part's velocity, required for long-range aiming.
        readonly SmoothingV3 partVelocitySmoothing = new(); // Smoothing for the part's velocity, required for long-range aiming.
        private Vector3 smoothedPartAcceleration; // Also apply smoothing to the part's acceleration, required for long-range aiming.
        readonly SmoothingV3 partAccelerationSmoothing = new(); // Smoothing for the part's acceleration, required for long-range aiming.
        readonly SmoothingV3 smoothedRelativeFinalTarget = new(0.5f); // Smoothing for the finalTarget aim-point: half-life of 1 frame. This seems good. More than 5 frames (0.1s) seems too slow.
        public bool targetIsLandedOrSplashed = false; // Used in the targeting simulations to know whether to separate gravity from other acceleration.
        private float lastTimeToCPA = -1, deltaTimeToCPA = 0;
        float bulletTimeToCPA; // Time until the bullet is expected to reach the closest point to the target. Used for timing-based bullet detonation.
        public Vector3 finalAimTarget;
        Vector3 staleFinalAimTarget, staleTargetVelocity, staleTargetAcceleration, stalePartVelocity;
        public Vessel visualTargetVessel
        {
            get
            {
                if (_visualTargetVessel != null && !_visualTargetVessel.gameObject.activeInHierarchy) _visualTargetVessel = null;
                return _visualTargetVessel;
            }
            set { _visualTargetVessel = value; }
        }
        Vessel _visualTargetVessel;
        public Vessel lastVisualTargetVessel
        {
            get
            {
                if (_lastVisualTargetVessel != null && !_lastVisualTargetVessel.gameObject.activeInHierarchy) _lastVisualTargetVessel = null;
                return _lastVisualTargetVessel;
            }
            set { _lastVisualTargetVessel = value; }
        }
        Vessel _lastVisualTargetVessel;
        public Part visualTargetPart
        {
            get
            {
                if (_visualTargetPart != null && !_visualTargetPart.gameObject.activeInHierarchy) _visualTargetPart = null;
                return _visualTargetPart;
            }
            set { _visualTargetPart = value; }
        }
        Part _visualTargetPart;
        public PooledBullet tgtShell = null;
        public PooledRocket tgtRocket = null;
        Vector3 closestTarget = Vector3.zero;
        Vector3 tgtVelocity = Vector3.zero;

        private int targetID = 0;
        bool targetAcquired;

        public bool targetCOM = true;
        public bool targetCockpits = false;
        public bool targetEngines = false;
        public bool targetWeapons = false;
        public bool targetMass = false;
        public bool targetRandom = false;

        RaycastHit[] laserHits = new RaycastHit[100];
        Collider[] heatRayColliders = new Collider[100];
        const int layerMask1 = (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.EVA | LayerMasks.Unknown19 | LayerMasks.Unknown23 | LayerMasks.Wheels); // Why 19 and 23?
        const int layerMask2 = (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.Unknown19 | LayerMasks.Wheels); // Why 19 and why not the other layer mask?
        enum TargetAcquisitionType { None, Visual, Slaved, Radar, AutoProxy, GPS };
        TargetAcquisitionType targetAcquisitionType = TargetAcquisitionType.None;
        TargetAcquisitionType lastTargetAcquisitionType = TargetAcquisitionType.None;
        float staleGoodTargetTime = 0;

        public Vector3? FiringSolutionVector => finalAimTarget.IsZero() ? (Vector3?)null : (finalAimTarget - fireTransforms[0].position).normalized;

        // For aiming corrections for offset/non-centerline fixed weapon positions
        public Vector3 offsetWeaponPosition = Vector3.zero;
        public Vector3 offsetWeaponDirection = Vector3.zero;

        public bool recentlyFiring //used by guard to know if it should evade this
        {
            get { return timeSinceFired < 1; }
        }

        //used to reduce volume of audio if multiple guns are being fired (needs to be improved/changed)
        //private int numberOfGuns = 0;

        //AI will fire gun if target is within this Cos(angle) of barrel
        public float maxAutoFireCosAngle = 0.9993908f; //corresponds to ~2 degrees

        //aimer textures
        Vector3 pointingAtPosition;
        float pointingDistance = 500f;
        Vector3 bulletPrediction;
        Vector3 fixedLeadOffset = Vector3.zero;

        float predictedFlightTime = 1; //for rockets
        Vector3 trajectoryOffset = Vector3.zero;

        //gapless particles
        List<BDAGaplessParticleEmitter> gaplessEmitters = new List<BDAGaplessParticleEmitter>();

        //muzzleflash emitters
        List<List<KSPParticleEmitter>> muzzleFlashList;

        //module references
        [KSPField] public int turretID = 0;
        public ModuleTurret turret;
        MissileFire mf;

        public MissileFire weaponManager
        {
            get
            {
                if (mf) return mf;
                mf = VesselModuleRegistry.GetMissileFire(vessel, true);
                return mf;
            }
        }

        public bool pointingAtSelf; //true if weapon is pointing at own vessel
        bool userFiring;
        Vector3 laserPoint;
        public bool slaved;
        public bool GPSTarget;
        public bool radarTarget;

        public Transform turretBaseTransform
        {
            get
            {
                if (turret)
                {
                    return turret.yawTransform.parent;
                }
                else
                {
                    return fireTransforms[0];
                }
            }
        }

        public float maxPitch
        {
            get { return turret ? turret.maxPitch : 0; }
        }

        public float minPitch
        {
            get { return turret ? turret.minPitch : 0; }
        }

        public float yawRange
        {
            get { return turret ? turret.yawRange : 0; }
        }

        //weapon interface
        public WeaponClasses GetWeaponClass()
        {
            if (eWeaponType == WeaponTypes.Ballistic)
            {
                return WeaponClasses.Gun;
            }
            else if (eWeaponType == WeaponTypes.Rocket)
            {
                return WeaponClasses.Rocket;
            }
            else
            {
                return WeaponClasses.DefenseLaser;
            }
        }
        public ModuleWeapon GetWeaponModule()
        {
            return this;
        }
        public Part GetPart()
        {
            return part;
        }

        public double ammoCount;
        public double ammoMaxCount;
        public string ammoLeft; //#191

        public string GetSubLabel() //think BDArmorySetup only calls this for the first instance of a particular ShortName, so this probably won't result in a group of n guns having n GetSublabelCalls per frame
        {
            //using (List<Part>.Enumerator craftPart = vessel.parts.GetEnumerator())
            //{
            ammoLeft = $"Ammo Left: {ammoCount:0}";
            int lastAmmoID = AmmoID;
            using (var weapon = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                while (weapon.MoveNext())
                {
                    if (weapon.Current == null) continue;
                    if (weapon.Current.GetShortName() != GetShortName()) continue;
                    if (weapon.Current.AmmoID != AmmoID && weapon.Current.AmmoID != lastAmmoID)
                    {
                        GetAmmoCount(weapon.Current.AmmoID, out double ammoCurrent, out double ammoMax);
                        ammoLeft += $"; {ammoCurrent:0}";
                        lastAmmoID = weapon.Current.AmmoID;
                    }
                }
            //}
            return ammoLeft;
        }
        public string GetMissileType()
        {
            return string.Empty;
        }

        public string GetPartName()
        {
            return WeaponName;
        }

        public float GetEngageRange()
        {
            return engageRangeMax;
        }

        public bool resourceSteal = false;
        public float strengthMutator = 1;
        public bool instagib = false;

        Vector3 debugTargetPosition;
        Vector3 debugLastTargetPosition;
        Vector3 debugRelVelAdj;
        Vector3 debugAccAdj;
        Vector3 debugGravAdj;

        #endregion Declarations

        #region KSPFields

        [KSPField(isPersistant = true, guiActive = true, guiName = "#LOC_BDArmory_WeaponName", guiActiveEditor = true), UI_Label(affectSymCounterparts = UI_Scene.All, scene = UI_Scene.All)]//Weapon Name 
        public string WeaponDisplayName;

        public string WeaponName;

        [KSPField]
        public string fireTransformName = "fireTransform";
        public Transform[] fireTransforms;

        [KSPField]
        public string muzzleTransformName = "muzzleTransform";

        [KSPField]
        public string shellEjectTransformName = "shellEject";
        public Transform[] shellEjectTransforms;

        [KSPField]
        public float shellEjectDelay = 0;

        [KSPField]
        public float shellEjectLifeTime = 2;

        [KSPField]
        public Vector3 shellEjectVelocity = new(0, 0, 7);

        [KSPField]
        public float shellEjectDeviation = 0.5f;

        [KSPField]
        public bool hasDeployAnim = false;

        [KSPField]
        public string deployAnimName = "deployAnim";
        AnimationState deployState;

        [KSPField]
        public bool hasReloadAnim = false;

        [KSPField]
        public string reloadAnimName = "reloadAnim";
        AnimationState reloadState;

        [KSPField]
        public bool hasChargeAnimation = false;

        [KSPField]
        public string chargeAnimName = "chargeAnim";
        AnimationState chargeState;

        [KSPField]
        public bool hasChargeHoldAnimation = false;

        [KSPField]
        public string chargeHoldAnimName = "chargeHoldAnim";
        AnimationState chargeHoldState;

        [KSPField]
        public bool hasFireAnimation = false;

        [KSPField]
        public string fireAnimName = "fireAnim";

        AnimationState[] fireState = new AnimationState[0];
        //private List<AnimationState> fireState;

        [KSPField]
        public bool spinDownAnimation = false;
        private bool spinningDown;

        //weapon specifications
        [KSPField(advancedTweakable = true, isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_FiringPriority"),
            UI_FloatRange(minValue = 0, maxValue = 10, stepIncrement = 1, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        public float priority = 0; //per-weapon priority selection override

        [KSPField(isPersistant = true)]
        public bool BurstOverride = false;

        [KSPField(advancedTweakable = true, isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_FiringBurstCount"),//Burst Firing Count
            UI_FloatRange(minValue = 1f, maxValue = 100f, stepIncrement = 1, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        public float fireBurstLength = 1;

        [KSPField(isPersistant = true)]
        public bool FireAngleOverride = false;

        [KSPField(advancedTweakable = true, isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_FiringAngle"),
            UI_FloatRange(minValue = 0f, maxValue = 4, stepIncrement = 0.05f, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        public float FiringTolerance = 1.0f; //per-weapon override of maxcosfireangle

        [KSPField]
        public float maxTargetingRange = 2000; //max range for raycasting and sighting

        [KSPField]
        public float SpoolUpTime = -1; //barrel spin-up period for gas-driven rotary cannon and similar
        float spooltime = 0;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Rate of Fire"),
            UI_FloatRange(minValue = 100f, maxValue = 1500, stepIncrement = 25f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float roundsPerMinute = 650; //RoF slider

        public float baseRPM = 650;

        [KSPField]
        public bool isChaingun = false; //does the gun have adjustable RoF

        [KSPField]
        public float maxDeviation = 1; //inaccuracy two standard deviations in degrees (two because backwards compatibility :)
        public float baseDeviation = 1;

        [KSPField]
        public float maxEffectiveDistance = 2500; //used by AI to select appropriate weapon

        [KSPField]
        public float minSafeDistance = 0; //used by AI to select appropriate weapon

        [KSPField]
        public float bulletMass = 0.3880f; //mass in KG - used for damage and recoil and drag

        [KSPField]
        public float caliber = 30; //caliber in mm, used for penetration calcs

        [KSPField]
        public float bulletDmgMult = 1; //Used for heat damage modifier for non-explosive bullets

        [KSPField]
        public float bulletVelocity = 1030; //velocity in meters/second

        [KSPField]
        public float baseBulletVelocity = -1; //vel of primary ammo type for mixed belts

        public int ProjectileCount = 1;

        public bool SabotRound = false;

        [KSPField]
        public bool BeltFed = true; //draws from an ammo bin; default behavior

        [KSPField]
        public int RoundsPerMag = 1; //For weapons fed from clips/mags. left at one as sanity check, incase this not set if !BeltFed
        public int RoundsRemaining = 0;
        public bool isReloading;

        [KSPField]
        public bool crewserved = false; //does the weapon need a gunner?
        public bool hasGunner = true; //if so, are they present?
        private KerbalSeat gunnerSeat;
        private bool gunnerSeatLookedFor = false;

        [KSPField]
        public float ReloadTime = 10;
        public float ReloadTimer = 0;
        public float ReloadAnimTime = 10;
        public float AnimTimer = 0;

        [KSPField]
        public bool BurstFire = false; // set to true for weapons that fire multiple times per triggerpull

        [KSPField]
        public float ChargeTime = -1;
        bool isCharging = false;
        [KSPField]
        public bool ChargeEachShot = true;
        [KSPField]
        public bool postFireChargeAnim = false;
        bool hasCharged = false;
        [KSPField]
        public float chargeHoldLength = 1;
        [KSPField]
        public string bulletDragTypeName = "AnalyticEstimate";
        public BulletDragTypes bulletDragType;

        //drag area of the bullet in m^2; equal to Cd * A with A being the frontal area of the bullet; as a first approximation, take Cd to be 0.3
        //bullet mass / bullet drag area.  Used in analytic estimate to speed up code
        [KSPField]
        public float bulletDragArea = 1.209675e-5f;

        private BulletInfo bulletInfo;

        [KSPField]
        public string bulletType = "def";

        public string currentType = "def";

        [KSPField]
        public string ammoName = "50CalAmmo"; //resource usage

        [KSPField]
        public float requestResourceAmount = 1; //amount of resource/ammo to deplete per shot

        [KSPField]
        public string secondaryAmmoName = "ElectricCharge"; //resource usage

        [KSPField]
        public float ECPerShot = 0; //EC to use per shot for weapons like railguns - DEPRECATED

        [KSPField]
        public float secondaryAmmoPerShot = 0; //EC to use per shot for weapons like railguns

        private bool electricResource = false;

        [KSPField]
        public float shellScale = 0.66f; //scale of shell to eject

        [KSPField]
        public bool hasRecoil = true;

        [KSPField]
        public float recoilReduction = 1; //for reducing recoil on large guns with built in compensation

        //[KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_FireLimits"),//Fire Limits
        // UI_Toggle(disabledText = "#LOC_BDArmory_FireLimits_disabledText", enabledText = "#LOC_BDArmory_FireLimits_enabledText")]//None--In range
        [KSPField]
        public bool onlyFireInRange = true;
        // UNUSED, supposedly once prevented firing when gun's turret is trying to exceed gimbal limits

        [KSPField]
        public bool bulletDrop = true; //projectiles are affected by gravity

        [KSPField]
        public string weaponType = "ballistic";
        //ballistic, cannon or laser

        //laser info
        [KSPField]
        public float laserDamage = 10000; //base damage/second of lasers
        [KSPField]
        public float laserMaxDamage = -1; //maximum damage/second of lasers if laser growth enabled
        public float baseLaserdamage;
        [KSPField]
        public float LaserGrowTime = -1; //time laser to be fired to go from base to max damage
        [KSPField] public bool DynamicBeamColor = false; //beam color changes longer laser fired, for growlasers
        bool dynamicFX = false;
        [KSPField] public float beamScrollRate = 0.5f; //Beam texture scroll rate, for plasma beams, etc
        private float Offset = 0;
        [KSPField] public float beamScalar = 0.01f; //x scaling for beam texture. lower is more stretched
        [KSPField] public bool pulseLaser = false; //pulse vs beam
        public bool pulseInConfig = false; //record if pulse laser in config for resetting lasers post mutator
        [KSPField] public bool HEpulses = false; //do the pulses have blast damage
        [KSPField] public bool HeatRay = false; //conic AoE
        [KSPField] public bool electroLaser = false; //Drains EC from target/induces EMP effects
        float beamDuration = 0.1f; // duration of pulselaser beamFX
        float beamScoreTime = 0.2f; //frequency of score accumulation for beam lasers, currently 5x/sec
        float BeamTracker = 0; // timer for scoring shots fired for beams
        float ScoreAccumulator = 0; //timer for scoring shots hit for beams
        bool grow = true;

        LineRenderer[] laserRenderers;
        LineRenderer trajectoryRenderer;
        List<Vector3> trajectoryPoints;

        public string rocketModelPath;
        public float rocketMass = 1;
        public float thrust = 1;
        public float thrustTime = 1;
        public float blastRadius = 1;
        public bool choker = false;
        public bool descendingOrder = true;
        public float thrustDeviation = 0.10f;
        [KSPField] public bool rocketPod = true; //is the RL a rocketpod, or a gyrojet gun?
        [KSPField] public bool externalAmmo = true; // weapon is supplied by external ammo boxes isntead of internal supply (e.g. guns vs rocket pods)
        Transform[] rockets;
        double rocketsMax;
        private RocketInfo rocketInfo;

        public float tntMass = 0;


        //public bool ImpulseInConfig = false; //record if impulse weapon in config for resetting weapons post mutator
        //public bool GraviticInConfig = false; //record if gravitic weapon in config for resetting weapons post mutator
        //public List<string> attributeList;

        public bool explosive = false;
        public bool beehive = false;
        public bool incendiary = false;
        public bool impulseWeapon = false;
        public bool graviticWeapon = false;

        [KSPField]
        public float Impulse = 0;

        [KSPField]
        public float massAdjustment = 0; //tons


        //deprectated
        //[KSPField] public float cannonShellRadius = 30; //max radius of explosion forces/damage
        //[KSPField] public float cannonShellPower = 8; //explosion's impulse force
        //[KSPField] public float cannonShellHeat = -1; //if non-negative, heat damage

        //projectile graphics
        [KSPField]
        public string projectileColor = "255, 130, 0, 255"; //final color of projectile; left public for lasers
        Color projectileColorC;
        string[] endColorS;
        [KSPField]
        public bool fadeColor = false;

        [KSPField]
        public string startColor = "255, 160, 0, 200";
        //if fade color is true, projectile starts at this color
        string[] startColorS;
        Color startColorC;

        [KSPField]
        public float tracerStartWidth = 0.25f; //set from bulletdefs, left for lasers

        [KSPField]
        public float tracerEndWidth = 0.2f;

        [KSPField]
        public float tracerMaxStartWidth = 0.5f; //set from bulletdefs, left for lasers

        [KSPField]
        public float tracerMaxEndWidth = 0.5f;

        float tracerBaseSWidth = 0.25f; // for laser FX
        float tracerBaseEWidth = 0.2f; // for laser FX
        [KSPField]
        public float tracerLength = 0;
        //if set to zero, tracer will be the length of the distance covered by the projectile in one physics timestep

        [KSPField]
        public float tracerDeltaFactor = 2.65f;

        [KSPField]
        public float nonTracerWidth = 0.01f;

        [KSPField]
        public int tracerInterval = 0;

        [KSPField]
        public float tracerLuminance = 1.75f;

        [KSPField]
        public float bulletLuminance = 0.75f;

        [KSPField]
        public bool tracerOverrideWidth = false;

        int tracerIntervalCounter;

        [KSPField]
        public string bulletTexturePath = "BDArmory/Textures/bullet";

        [KSPField]
        public string smokeTexturePath = ""; //"BDArmory/Textures/tracerSmoke";

        [KSPField]
        public string laserTexturePath = "BDArmory/Textures/laser";

        public List<string> laserTexList;

        [KSPField]
        public bool oneShotWorldParticles = false;

        //heat
        [KSPField]
        public float maxHeat = 3600;

        [KSPField]
        public float heatPerShot = 75;

        [KSPField]
        public float heatLoss = 250;

        //canon explosion effects
        public static string defaultExplModelPath = "BDArmory/Models/explosion/explosion";
        [KSPField]
        public string explModelPath = defaultExplModelPath;

        public static string defaultExplSoundPath = "BDArmory/Sounds/explode1";
        [KSPField]
        public string explSoundPath = defaultExplSoundPath;

        //Used for scaling laser damage down based on distance.
        [KSPField]
        public float tanAngle = 0.0001f;
        //Angle of divergeance/2. Theoretical minimum value calculated using θ = (1.22 L/RL)/2,
        //where L is laser's wavelength and RL is the radius of the mirror (=gun).

        //audioclip paths
        [KSPField]
        public string fireSoundPath = "BDArmory/Parts/50CalTurret/sounds/shot";

        [KSPField]
        public string overheatSoundPath = "BDArmory/Parts/50CalTurret/sounds/turretOverheat";

        [KSPField]
        public string chargeSoundPath = "BDArmory/Parts/ABL/sounds/charge";

        [KSPField]
        public string rocketSoundPath = "BDArmory/Sounds/rocketLoop";

        //audio
        [KSPField]
        public bool oneShotSound = true;
        //play audioclip on every shot, instead of playing looping audio while firing

        [KSPField]
        public float soundRepeatTime = 1;
        //looped audio will loop back to this time (used for not playing the opening bit, eg the ramp up in pitch of gatling guns)

        [KSPField]
        public string reloadAudioPath = string.Empty;
        AudioClip reloadAudioClip;

        [KSPField]
        public string reloadCompletePath = string.Empty;

        [KSPField]
        public bool showReloadMeter = false; //used for cannons or guns with extremely low rate of fire

        //Air Detonating Rounds
        //public bool airDetonation = false;
        public bool proximityDetonation = false;
        //public bool airDetonationTiming = true;

        [KSPField(isPersistant = true, guiActive = true, guiName = "#LOC_BDArmory_DefaultDetonationRange", guiActiveEditor = false)]//Fuzed Detonation Range 
        public float defaultDetonationRange = 3500; // maxairDetrange works for altitude fuzing, use this for VT fuzing

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ProximityFuzeRadius"), UI_FloatRange(minValue = 0f, maxValue = 300f, stepIncrement = 1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Proximity Fuze Radius
        public float detonationRange = -1f; // give ability to set proximity range

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_DetonateAtMinimumDistance"), UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled")]
        public bool detonateAtMinimumDistance = true;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_Ammo_Type"),//Ammunition Types
        UI_FloatRange(minValue = 1, maxValue = 999, stepIncrement = 1, scene = UI_Scene.All)]
        public float AmmoTypeNum = 1;

        [KSPField(isPersistant = true)]
        public bool advancedAmmoOption = false;

        [KSPEvent(advancedTweakable = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_simple", active = true)]//Disable Engage Options
        public void ToggleAmmoConfig()
        {
            advancedAmmoOption = !advancedAmmoOption;

            if (advancedAmmoOption == true)
            {
                Events["ToggleAmmoConfig"].guiName = StringUtils.Localize("#LOC_BDArmory_advanced");//"Advanced Ammo Config"
                Events["ConfigAmmo"].guiActive = true;
                Events["ConfigAmmo"].guiActiveEditor = true;
                Fields["AmmoTypeNum"].guiActive = false;
                Fields["AmmoTypeNum"].guiActiveEditor = false;
            }
            else
            {
                Events["ToggleAmmoConfig"].guiName = StringUtils.Localize("#LOC_BDArmory_simple");//"Simple Ammo Config
                Events["ConfigAmmo"].guiActive = false;
                Events["ConfigAmmo"].guiActiveEditor = false;
                Fields["AmmoTypeNum"].guiActive = true;
                Fields["AmmoTypeNum"].guiActiveEditor = true;
                useCustomBelt = false;
            }
            GUIUtils.RefreshAssociatedWindows(part);
        }
        [KSPField(advancedTweakable = true, isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_useBelt")]//Using Custom Loadout
        public bool useCustomBelt = false;

        [KSPEvent(advancedTweakable = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_Ammo_Setup")]//Configure Ammo Loadout
        public void ConfigAmmo()
        {
            BDAmmoSelector.Instance.Open(this, new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
        }

        [KSPField(isPersistant = true)]
        public string SelectedAmmoType;

        public List<string> ammoList;

        [KSPField(isPersistant = true)]
        public string ammoBelt = "def";

        public List<string> customAmmoBelt;

        int AmmoIntervalCounter = 0;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_Ammo_LoadedAmmo")]//Status
        public string guiAmmoTypeString = StringUtils.Localize("#LOC_BDArmory_Ammo_Slug");

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_DeployableWeapon"), // In custom/modded "cargo bay"
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
        public string deployWepGroup = "0";

        [KSPField(isPersistant = true)]
        public bool canHotSwap = false; //for select weapons that it makes sense to be able to swap ammo types while in-flight, like the Abrams turret

        //auto proximity tracking
        [KSPField]
        public float autoProxyTrackRange = 0;
        public bool atprAcquired;
        int aptrTicker;

        public float timeFired; // Note: this is technically off by Time.fixedDeltaTime (since it's meant to be within the range [Time.time <—> Time.time + Time.fixedDeltaTime]), but so is Time.time in timeSinceFired, so we can skip adding the constant.
        public float timeSinceFired => Time.time - timeFired;
        public float initialFireDelay = 0; //used to ripple fire multiple weapons of this type
        float InitialFireDelay => weaponManager && weaponManager.barrageStagger > 0 ? initialFireDelay * weaponManager.barrageStagger : initialFireDelay;


        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_Barrage")]//Barrage
        public bool useRippleFire = true;

        public bool canRippleFire = true;

        [KSPEvent(guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_ToggleBarrage")]//Toggle Barrage
        public void ToggleRipple()
        {
            List<Part>.Enumerator craftPart = EditorLogic.fetch.ship.parts.GetEnumerator();
            while (craftPart.MoveNext())
            {
                if (craftPart.Current == null) continue;
                if (craftPart.Current.name != part.name) continue;
                List<ModuleWeapon>.Enumerator weapon = craftPart.Current.FindModulesImplementing<ModuleWeapon>().GetEnumerator();
                while (weapon.MoveNext())
                {
                    if (weapon.Current == null) continue;
                    weapon.Current.useRippleFire = !weapon.Current.useRippleFire;
                }
                weapon.Dispose();
            }
            craftPart.Dispose();
        }

        [KSPField(isPersistant = true)]
        public bool isAPS = false;

        [KSPField(isPersistant = true)]
        public bool dualModeAPS = false;

        [KSPField]
        public string APSType = "missile"; //missile/ballistic/omni

        private float delayTime = -1;

        IEnumerator IncrementRippleIndex(float delay)
        {
            if (isRippleFiring) delay = 0;
            if (delay > 0)
            {
                yield return new WaitForSecondsFixed(delay);
            }
            if (weaponManager == null || weaponManager.vessel != vessel) yield break;
            weaponManager.incrementRippleIndex(WeaponName);

            //Debug.Log("[BDArmory.ModuleWeapon]: incrementing ripple index to: " + weaponManager.gunRippleIndex);
        }

        int barrelIndex = 0;
        int animIndex = 0;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_CustomFireKey"), UI_Label(scene = UI_Scene.All)]
        public string customFireKey = "";
        BDInputInfo CustomFireKey;
        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_SetCustomFireKey")] // Set Custom Fire Key
        void SetCustomFireKey()
        {
            if (!bindingKey)
                StartCoroutine(BindCustomFireKey());
        }
        bool bindingKey = false;
        IEnumerator BindCustomFireKey()
        {
            Events["SetCustomFireKey"].guiName = StringUtils.Localize("#LOC_BDArmory_InputSettings_recordedInput");
            bindingKey = true;
            int id = 0;
            BDKeyBinder.BindKey(id);
            while (bindingKey)
            {
                if (BDKeyBinder.IsRecordingID(id))
                {
                    string recordedInput;
                    if (BDKeyBinder.current.AcquireInputString(out recordedInput))
                    {
                        if (recordedInput == "escape") // Clear the binding
                            SetCustomFireKey("");
                        else if (recordedInput != "mouse 0") // Left clicking cancels
                            SetCustomFireKey(recordedInput);
                        bindingKey = false;
                        break;
                    }
                }
                else
                {
                    bindingKey = false;
                    break;
                }
                yield return null;
            }
            Events["SetCustomFireKey"].guiName = StringUtils.Localize("#LOC_BDArmory_SetCustomFireKey");
        }
        public void SetCustomFireKey(string key, bool applySym = true)
        {
            CustomFireKey = new BDInputInfo(key, "Custom Fire Key");
            customFireKey = CustomFireKey.inputString;
            if (!applySym) return;
            using (List<Part>.Enumerator sym = part.symmetryCounterparts.GetEnumerator())
                while (sym.MoveNext())
                {
                    if (sym.Current == null) continue;
                    sym.Current.FindModuleImplementing<ModuleWeapon>().SetCustomFireKey(key, false);
                }
        }
        #endregion KSPFields

        #region KSPActions

        [KSPAction("Toggle Weapon")]
        public void AGToggle(KSPActionParam param)
        {
            Toggle();
        }

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_Status")]//Status
        public string guiStatusString =
            "Disabled";

        //PartWindow buttons
        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_Toggle")]//Toggle
        public void Toggle()
        {
            if (weaponState == WeaponStates.Disabled || weaponState == WeaponStates.PoweringDown)
            {
                EnableWeapon();
            }
            else
            {
                DisableWeapon();
            }
        }

        bool agHoldFiring;

        [KSPAction("Fire (Toggle)")]
        public void AGFireToggle(KSPActionParam param)
        {
            agHoldFiring = (param.type == KSPActionType.Activate);
        }

        [KSPAction("Fire (Hold)")]
        public void AGFireHold(KSPActionParam param)
        {
            StartCoroutine(FireHoldRoutine(param.group));
        }

        IEnumerator FireHoldRoutine(KSPActionGroup group)
        {
            KeyBinding key = OtherUtils.AGEnumToKeybinding(group);
            if (key == null)
            {
                yield break;
            }

            while (key.GetKey())
            {
                agHoldFiring = true;
                yield return null;
            }

            agHoldFiring = false;
            yield break;
        }

        [KSPEvent(guiActive = true, guiName = "#LOC_BDArmory_Jettison", active = true, guiActiveEditor = false)]//Jettison
        public void Jettison() // make rocketpods jettisonable
        {
            if ((turret || eWeaponType != WeaponTypes.Rocket) || (eWeaponType == WeaponTypes.Rocket && (!rocketPod || (rocketPod && externalAmmo))))
            {
                return;
            }
            part.decouple(0);
            if (BDArmorySetup.Instance.ActiveWeaponManager != null)
                BDArmorySetup.Instance.ActiveWeaponManager.UpdateList();
        }
        [KSPAction("Jettison")] // Give them an action group too.
        public void AGJettison(KSPActionParam param)
        {
            Jettison();
        }
        #endregion KSPActions

        #region KSP Events

        public override void OnAwake()
        {
            base.OnAwake();

            part.stagingIconAlwaysShown = true;
            part.stackIconGrouping = StackIconGrouping.SAME_TYPE;
        }

        public void Start()
        {
            part.stagingIconAlwaysShown = true;
            part.stackIconGrouping = StackIconGrouping.SAME_TYPE;

            Events["HideUI"].active = false;
            Events["ShowUI"].active = true;
            ParseWeaponType(weaponType);

            // extension for feature_engagementenvelope
            if (dualModeAPS) isAPS = true;
            if (isAPS)
            {
                engageMissile = false; //missiles targeted separately from base WM targeting logic, having this is unnecessary and can cause problems with radar slaving
                if (!dualModeAPS)
                {
                    HideEngageOptions();
                    Events["ShowUI"].active = false;
                    Events["HideUI"].active = false;
                    Events["Toggle"].active = false;
                    Fields["priority"].guiActive = false;
                    Fields["priority"].guiActiveEditor = false;
                }
                ParseAPSType(APSType);
            }
            InitializeEngagementRange(minSafeDistance, maxEffectiveDistance);
            if (string.IsNullOrEmpty(GetShortName()))
            {
                shortName = part.partInfo.title;
            }
            OriginalShortName = shortName;
            WeaponDisplayName = shortName;
            WeaponName = part.partInfo.name; //have weaponname be the .cfg part name, since not all weapons have a shortName in the .cfg
            using (var emitter = part.FindModelComponents<KSPParticleEmitter>().AsEnumerable().GetEnumerator())
                while (emitter.MoveNext())
                {
                    if (emitter.Current == null) continue;
                    emitter.Current.emit = false;
                    EffectBehaviour.AddParticleEmitter(emitter.Current);
                }

            if (eWeaponType != WeaponTypes.Laser || (eWeaponType == WeaponTypes.Laser && pulseLaser))
            {
                try
                {
                    baseRPM = float.Parse(ConfigNodeUtils.FindPartModuleConfigNodeValue(part.partInfo.partConfig, "ModuleWeapon", "roundsPerMinute", "fireTransformName", fireTransformName)); //if multiple moduleWeapons, make sure this grabs the right one unsing fireTransformname as an ID
                }
                catch
                {
                    baseRPM = 3000;
                    Debug.LogError($"[BDArmory.ModuleWeapon] {shortName} missing roundsPerMinute field in .cfg! Fix your .cfg!");
                }
            }
            else baseRPM = 3000;

            if (roundsPerMinute >= 1500 || (eWeaponType == WeaponTypes.Laser && !pulseLaser))
            {
                Events["ToggleRipple"].guiActiveEditor = false;
                Fields["useRippleFire"].guiActiveEditor = false;
                useRippleFire = false;
                canRippleFire = false;
                if (HighLogic.LoadedSceneIsFlight)
                {
                    using (List<Part>.Enumerator craftPart = vessel.parts.GetEnumerator()) //set other weapons in the group to ripple = false if the group contains a weapon with RPM > 1500, should fix the brownings+GAU WG, GAU no longer overheats exploit
                    {
                        using (var weapon = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                            while (weapon.MoveNext())
                            {
                                if (weapon.Current == null) continue;
                                if (weapon.Current.isAPS) continue;
                                if (weapon.Current.GetShortName() != GetShortName()) continue;
                                if (weapon.Current.roundsPerMinute >= 1500 || (weapon.Current.eWeaponType == WeaponTypes.Laser && !weapon.Current.pulseLaser)) continue;
                                weapon.Current.canRippleFire = false;
                                weapon.Current.useRippleFire = false;
                            }
                    }
                }
            }

            if (!(isChaingun || eWeaponType == WeaponTypes.Rocket))//disable rocket RoF slider for non rockets 
            {
                Fields["roundsPerMinute"].guiActiveEditor = false;
            }
            else
            {
                try
                {
                    UI_FloatRange RPMEditor = (UI_FloatRange)Fields["roundsPerMinute"].uiControlEditor; // FIXME this is throwing an invalid cast for rocket pods sometimes
                    if (isChaingun)
                    {
                        RPMEditor.maxValue = baseRPM;
                        RPMEditor.minValue = baseRPM / 2;
                        RPMEditor.onFieldChanged = AccAdjust;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BDArmory.DEBUG]: {e.Message}\n{e.StackTrace}");
                }
            }

            ammoList = BDAcTools.ParseNames(bulletType);
            if (ammoList.Count > 1)
            {
                if (!canHotSwap)
                {
                    Fields["AmmoTypeNum"].guiActive = false;
                }
                UI_FloatRange ATrangeEditor = (UI_FloatRange)Fields["AmmoTypeNum"].uiControlEditor;
                ATrangeEditor.maxValue = (float)ammoList.Count;
                ATrangeEditor.onFieldChanged = SetupAmmo;
                UI_FloatRange ATrangeFlight = (UI_FloatRange)Fields["AmmoTypeNum"].uiControlFlight;
                ATrangeFlight.maxValue = (float)ammoList.Count;
                ATrangeFlight.onFieldChanged = SetupAmmo;
            }
            else //disable ammo selector
            {
                Fields["AmmoTypeNum"].guiActive = false;
                Fields["AmmoTypeNum"].guiActiveEditor = false;
                Events["ToggleAmmoConfig"].guiActiveEditor = false;
            }
            UI_FloatRange FAOEditor = (UI_FloatRange)Fields["FiringTolerance"].uiControlEditor;
            FAOEditor.onFieldChanged = FAOCos;
            UI_FloatRange FAOFlight = (UI_FloatRange)Fields["FiringTolerance"].uiControlFlight;
            FAOFlight.onFieldChanged = FAOCos;
            Fields["FiringTolerance"].guiActive = FireAngleOverride;
            Fields["FiringTolerance"].guiActiveEditor = FireAngleOverride;
            Fields["fireBurstLength"].guiActive = BurstOverride;
            Fields["fireBurstLength"].guiActiveEditor = BurstOverride;
            if (BurstFire)
            {
                BeltFed = false;
            }
            if (eWeaponType == WeaponTypes.Ballistic)
            {
                rocketPod = false;
            }
            if (eWeaponType == WeaponTypes.Rocket)
            {
                try
                {
                    externalAmmo = bool.Parse(ConfigNodeUtils.FindPartModuleConfigNodeValue(part.partInfo.partConfig, "ModuleWeapon", "externalAmmo"));
                }
                catch
                {
                    externalAmmo = false;
                    Debug.LogError($"[BDArmory.ModuleWeapon] {shortName} missing externalAmmo field in .cfg! Fix your .cfg!");
                }
                if (rocketPod && externalAmmo)
                {
                    BeltFed = false;
                    PartResource rocketResource = GetRocketResource();
                    if (rocketResource != null)
                    {
                        part.resourcePriorityOffset = +2; //make rocketpods draw from internal ammo first, if any, before using external supply
                    }
                }
                if (!rocketPod)
                {
                    externalAmmo = true;
                }
                Events["ToggleAmmoConfig"].guiActiveEditor = false;
            }
            if (eWeaponType == WeaponTypes.Laser)
            {
                if (!pulseLaser)
                {
                    roundsPerMinute = 3000; //50 rounds/sec or 1 'round'/FixedUpdate
                }
                else
                {
                    pulseInConfig = true;
                }
                if (HEpulses)
                {
                    pulseLaser = true;
                    HeatRay = false;
                }
                if (HeatRay)
                {
                    HEpulses = false;
                    electroLaser = false;
                }
                rocketPod = false;
                //disable fuze GUI elements
                Fields["defaultDetonationRange"].guiActive = false;
                Fields["defaultDetonationRange"].guiActiveEditor = false;
                Fields["detonationRange"].guiActive = false;
                Fields["detonationRange"].guiActiveEditor = false;
                Fields["guiAmmoTypeString"].guiActiveEditor = false; //ammoswap
                Fields["guiAmmoTypeString"].guiActive = false;
                Events["ToggleAmmoConfig"].guiActiveEditor = false;
                tracerBaseSWidth = tracerStartWidth;
                tracerBaseEWidth = tracerEndWidth;
                laserTexList = BDAcTools.ParseNames(laserTexturePath);
                if (laserMaxDamage < 0) laserMaxDamage = laserDamage;
                if (laserTexList.Count > 1) dynamicFX = true;
            }
            muzzleFlashList = new List<List<KSPParticleEmitter>>();
            List<string> emitterList = BDAcTools.ParseNames(muzzleTransformName);
            for (int i = 0; i < emitterList.Count; i++)
            {
                List<KSPParticleEmitter> muzzleFlashEmitters = new List<KSPParticleEmitter>();
                using (var mtf = part.FindModelTransforms(emitterList[i]).AsEnumerable().GetEnumerator())
                    while (mtf.MoveNext())
                    {
                        if (mtf.Current == null) continue;
                        KSPParticleEmitter kpe = mtf.Current.GetComponent<KSPParticleEmitter>();
                        if (kpe == null)
                        {
                            Debug.LogError("[BDArmory.ModuleWeapon] MuzzleFX transform missing KSPParticleEmitter component. Please fix your model");
                            continue;
                        }
                        EffectBehaviour.AddParticleEmitter(kpe);
                        muzzleFlashEmitters.Add(kpe);
                        kpe.emit = false;
                    }
                muzzleFlashList.Add(muzzleFlashEmitters);
            }
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (bulletPool == null)
                {
                    SetupBulletPool(); // Always set up the bullet pool in case the ammo type has bullet submunitions (it's not that big anyway).
                }
                if (eWeaponType == WeaponTypes.Ballistic)
                {
                    if (shellPool == null)
                    {
                        SetupShellPool();
                    }
                    if (useCustomBelt)
                    {
                        if (!string.IsNullOrEmpty(ammoBelt) && ammoBelt != "def")
                        {
                            var validAmmoTypes = BDAcTools.ParseNames(bulletType);
                            if (validAmmoTypes.Count == 0)
                            {
                                Debug.LogError($"[BDArmory.ModuleWeapon]: Weapon {WeaponName} has no valid ammo types! Reverting to 'def'.");
                                validAmmoTypes = new List<string> { "def" };
                            }
                            customAmmoBelt = BDAcTools.ParseNames(ammoBelt);
                            for (int i = 0; i < customAmmoBelt.Count; ++i)
                            {
                                if (!validAmmoTypes.Contains(customAmmoBelt[i]))
                                {
                                    Debug.LogWarning($"[BDArmory.ModuleWeapon] Invalid ammo type {customAmmoBelt[i]} at position {i} in ammo belt of {WeaponName} on {vessel.vesselName}! reverting to valid ammo type {validAmmoTypes[0]}");
                                    customAmmoBelt[i] = validAmmoTypes[0];
                                }
                            }
                            baseBulletVelocity = BulletInfo.bullets[customAmmoBelt[0].ToString()].bulletVelocity;
                        }
                        else //belt is empty/"def" reset useAmmoBelt
                        {
                            useCustomBelt = false;
                        }
                    }
                }
                if (eWeaponType == WeaponTypes.Rocket)
                {
                    if (rocketPod)// only call these for rocket pods
                    {
                        MakeRocketArray();
                        UpdateRocketScales();
                    }
                    else
                    {
                        if (shellPool == null)
                        {
                            SetupShellPool();
                        }
                    }
                }

                //setup transforms
                fireTransforms = part.FindModelTransforms(fireTransformName);
                if (fireTransforms.Length == 0) Debug.LogError("[BDArmory.ModuleWeapon] Weapon missing fireTransform [" + fireTransformName + "]! Please fix your model");
                shellEjectTransforms = part.FindModelTransforms(shellEjectTransformName);
                if (shellEjectTransforms.Length > 0 && shellPool == null) SetupShellPool();

                //setup emitters
                using (var pe = part.FindModelComponents<KSPParticleEmitter>().AsEnumerable().GetEnumerator())
                    while (pe.MoveNext())
                    {
                        if (pe.Current == null) continue;
                        pe.Current.maxSize *= part.rescaleFactor;
                        pe.Current.minSize *= part.rescaleFactor;
                        pe.Current.shape3D *= part.rescaleFactor;
                        pe.Current.shape2D *= part.rescaleFactor;
                        pe.Current.shape1D *= part.rescaleFactor;

                        if (pe.Current.useWorldSpace && !oneShotWorldParticles)
                        {
                            BDAGaplessParticleEmitter gpe = pe.Current.gameObject.AddComponent<BDAGaplessParticleEmitter>();
                            gpe.part = part;
                            gaplessEmitters.Add(gpe);
                        }
                        else
                        {
                            EffectBehaviour.AddParticleEmitter(pe.Current);
                        }
                    }

                //setup projectile colors
                projectileColorC = GUIUtils.ParseColor255(projectileColor);
                endColorS = projectileColor.Split(","[0]);

                startColorC = GUIUtils.ParseColor255(startColor);
                startColorS = startColor.Split(","[0]);

                //init and zero points
                targetPosition = Vector3.zero;
                pointingAtPosition = Vector3.zero;
                bulletPrediction = Vector3.zero;

                //setup audio
                SetupAudio();
                if (eWeaponType == WeaponTypes.Laser || ChargeTime > 0)
                {
                    chargeSound = SoundUtils.GetAudioClip(chargeSoundPath);
                }
                // Setup gauges
                gauge = (BDStagingAreaGauge)part.AddModule("BDStagingAreaGauge");
                gauge.AmmoName = ammoName;

                var AmmoDef = PartResourceLibrary.Instance.GetDefinition(ammoName);
                if (AmmoDef != null)
                    AmmoID = AmmoDef.id;
                else
                    Debug.LogError($"[BDArmory.ModuleWeapon]: Resource definition for {ammoName} not found!");
                var SecAmmoDef = PartResourceLibrary.Instance.GetDefinition(secondaryAmmoName);
                if (SecAmmoDef != null)
                {
                    ECID = SecAmmoDef.id;
                    electricResource = PartResourceLibrary.Instance.GetDefinition(ECID).density == 0;
                }
                else
                    Debug.LogError($"[BDArmory.ModuleWeapon]: Resource definition for {secondaryAmmoName} not found!");

                if (ECPerShot != 0)
                {
                    Debug.LogWarning($"[BDArmory.ModuleWeapon]: Weapon part {part.name} is using deprecated 'ecPerShot' attribute. Please update the config to use 'secondaryAmmoPerShot' instead.");
                    secondaryAmmoPerShot = ECPerShot;
                }                //laser setup
                if (eWeaponType == WeaponTypes.Laser)
                {
                    SetupLaserSpecifics();
                    if (maxTargetingRange < maxEffectiveDistance)
                    {
                        maxEffectiveDistance = maxTargetingRange;
                    }
                    baseLaserdamage = laserDamage;
                }
                if (crewserved)
                {
                    CheckCrewed();
                }

                if (ammoList.Count > 1)
                {
                    UI_FloatRange ATrangeFlight = (UI_FloatRange)Fields["AmmoTypeNum"].uiControlFlight;
                    ATrangeFlight.maxValue = (float)ammoList.Count;
                    if (!canHotSwap)
                    {
                        Fields["AmmoTypeNum"].guiActive = false;
                    }
                }
                baseDeviation = maxDeviation; //store original MD value
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                fireTransforms = part.FindModelTransforms(fireTransformName);
                if (fireTransforms.Length == 0) Debug.LogError("[BDArmory.ModuleWeapon] Weapon missing fireTransform [" + fireTransformName + "]! Please fix your model");
                WeaponNameWindow.OnActionGroupEditorOpened.Add(OnActionGroupEditorOpened);
                WeaponNameWindow.OnActionGroupEditorClosed.Add(OnActionGroupEditorClosed);
                if (useCustomBelt)
                {
                    if (!string.IsNullOrEmpty(ammoBelt) && ammoBelt != "def")
                    {
                        customAmmoBelt = BDAcTools.ParseNames(ammoBelt);
                        baseBulletVelocity = BulletInfo.bullets[customAmmoBelt[0].ToString()].bulletVelocity;
                    }
                    else
                    {
                        useCustomBelt = false;
                    }
                }
            }
            //turret setup
            List<ModuleTurret>.Enumerator turr = part.FindModulesImplementing<ModuleTurret>().GetEnumerator();
            while (turr.MoveNext())
            {
                if (turr.Current == null) continue;
                if (turr.Current.turretID != turretID) continue;
                turret = turr.Current;
                turret.SetReferenceTransform(fireTransforms[0]);
                break;
            }
            turr.Dispose();

            if (!turret)
            {
                Fields["onlyFireInRange"].guiActive = false;
                Fields["onlyFireInRange"].guiActiveEditor = false;
            }
            if (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)
            {
                if ((turret || eWeaponType != WeaponTypes.Rocket) || (eWeaponType == WeaponTypes.Rocket && (!rocketPod || (rocketPod && externalAmmo))))
                {
                    Events["Jettison"].guiActive = false;
                    Actions["AGJettison"].active = false;
                }
            }
            //setup animations
            if (hasDeployAnim)
            {
                deployState = GUIUtils.SetUpSingleAnimation(deployAnimName, part);
                Events["ToggleDeploy"].guiActiveEditor = true;
                if (deployState != null)
                {
                    deployState.normalizedTime = 0;
                    deployState.speed = 0;
                    deployState.enabled = true;
                    ReloadAnimTime = (ReloadTime - deployState.length);
                }
                else
                {
                    Debug.LogWarning($"[BDArmory.ModuleWeapon]: {OriginalShortName} is missing deploy anim");
                    hasDeployAnim = false;
                }
            }
            if (hasReloadAnim)
            {
                reloadState = GUIUtils.SetUpSingleAnimation(reloadAnimName, part);
                if (reloadState != null)
                {
                    reloadState.normalizedTime = 1;
                    reloadState.speed = 0;
                    reloadState.enabled = true;
                }
                else
                {
                    Debug.LogWarning($"[BDArmory.ModuleWeapon]: {OriginalShortName} is missing reload anim");
                    hasReloadAnim = false;
                }
            }
            if (hasChargeAnimation)
            {
                chargeState = GUIUtils.SetUpSingleAnimation(chargeAnimName, part);
                if (chargeState != null)
                {
                    chargeState.normalizedTime = 0;
                    chargeState.speed = 0;
                    chargeState.enabled = true;
                }
                else
                {
                    Debug.LogWarning($"[BDArmory.ModuleWeapon]: {OriginalShortName} is missing charge anim");
                    hasChargeAnimation = false;
                }
                if (hasChargeHoldAnimation)
                {
                    chargeHoldState = GUIUtils.SetUpSingleAnimation(chargeHoldAnimName, part);
                    if (chargeHoldState != null)
                    {
                        chargeHoldState.normalizedTime = 0;
                        chargeHoldState.speed = 0;
                        chargeHoldState.enabled = true;
                    }
                }
            }
            if (hasFireAnimation)
            {
                List<string> animList = BDAcTools.ParseNames(fireAnimName);
                //animList = animList.OrderBy(w => w).ToList();
                fireState = new AnimationState[animList.Count];
                //for (int i = 0; i < fireTransforms.Length; i++)
                for (int i = 0; i < animList.Count; i++)
                {
                    try
                    {
                        fireState[i] = GUIUtils.SetUpSingleAnimation(animList[i].ToString(), part);
                        //Debug.Log("[BDArmory.ModuleWeapon] Added fire anim " + i);
                        fireState[i].normalizedTime = 0;
                    }
                    catch
                    {
                        Debug.LogWarning($"[BDArmory.ModuleWeapon]: {OriginalShortName} is missing fire anim " + i);
                    }
                }
            }
            /*
			if (graviticWeapon)
			{
				GraviticInConfig = true;
			}
			if (impulseWeapon)
			{
				ImpulseInConfig = true;
			}*/
            if (eWeaponType != WeaponTypes.Laser)
            {
                SetupAmmo(null, null);

                if (eWeaponType == WeaponTypes.Rocket)
                {
                    if (rocketInfo == null)
                    {
                        //if (BDArmorySettings.DEBUG_WEAPONS)
                        Debug.LogWarning("[BDArmory.ModuleWeapon]: Failed To load rocket : " + currentType);
                    }
                    else
                    {
                        if (BDArmorySettings.DEBUG_WEAPONS)
                            Debug.Log("[BDArmory.ModuleWeapon]: AmmoType Loaded : " + currentType);
                        if (beehive)
                        {
                            string[] subMunitionData = bulletInfo.subMunitionType.Split(new char[] { ';' });
                            string projType = subMunitionData[0];
                            string[] subrocketData = rocketInfo.subMunitionType.Split(new char[] { ';' });
                            string rocketType = subMunitionData[0];
                            if (!BulletInfo.bulletNames.Contains(projType) || !RocketInfo.rocketNames.Contains(rocketType))
                            {
                                beehive = false;
                                Debug.LogWarning("[BDArmory.ModuleWeapon]: Invalid submunition on : " + currentType);
                            }
                            else
                            {
                                if (RocketInfo.rocketNames.Contains(rocketType))
                                {
                                    RocketInfo sRocket = RocketInfo.rockets[rocketType];
                                    SetupRocketPool(sRocket.name, sRocket.rocketModelPath); //Will need to move this if rockets ever get ammobelt functionality
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (bulletInfo == null)
                    {
                        //if (BDArmorySettings.DEBUG_WEAPONS)
                        Debug.LogWarning("[BDArmory.ModuleWeapon]: Failed To load bullet : " + currentType);
                        ParseBulletDragType(); // Have to parse the bullet drag type if there's no bulletInfo for
                        // compatibility with older mods where bulletDragTypeName was part of ModuleWeapon's specs.
                    }
                    else
                    {
                        if (BDArmorySettings.DEBUG_WEAPONS)
                            Debug.Log("[BDArmory.ModuleWeapon]: BulletType Loaded : " + currentType);
                        if (beehive)
                        {
                            string[] subMunitionData = bulletInfo.subMunitionType.Split(new char[] { ';' });
                            string projType = subMunitionData[0];
                            if (!BulletInfo.bulletNames.Contains(projType))
                            {
                                beehive = false;
                                Debug.LogWarning("[BDArmory.ModuleWeapon]: Invalid submunition on : " + currentType);
                            }
                        }
                    }
                }
            }

            BDArmorySetup.OnVolumeChange += UpdateVolume;
            if (HighLogic.LoadedSceneIsFlight)
            { TimingManager.FixedUpdateAdd(TimingManager.TimingStage.FashionablyLate, AimAndFire); }
            CustomFireKey = new BDInputInfo(customFireKey, "Custom Fire");

            if (HighLogic.LoadedSceneIsFlight)
            {
                if (isAPS)
                {
                    // Wait a frame before enabling the weapon so that all symmetric weapons have started.
                    StartCoroutine(EnableWeaponNextFrame());
                }
            }

            if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 59)
            {
                if (WeaponName == "bahaTurret")
                {
                    maxEffectiveDistance = 1000;
                    InitializeEngagementRange(minSafeDistance, 1000);
                    engageRangeMax = 1000;
                }
            }
            if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 60)
            {
                if (WeaponName == "bahaChemLaser")
                {
                    if (turret != null)
                    {
                        turret.minPitch = -0.1f;
                        turret.maxPitch = 0.1f;
                        turret.yawRange = 0.2f;
                    }
                }
            }
            if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 65)
            {
                if (HighLogic.LoadedSceneIsFlight)
                {
                    using (var engines = VesselModuleRegistry.GetModuleEngines(vessel).GetEnumerator())
                        while (engines.MoveNext())
                        {
                            if (engines.Current == null) continue;
                            MultiModeEngine mme = engines.Current.part.FindModuleImplementing<MultiModeEngine>();

                            if (mme && engines.Current.engineID == "Dry") continue;
                            float engineThrust = engines.Current.maxThrust * (mme != null ? 2 : 1); //AB velCurves tend to be around 2x at ~300m/s, will add extra thrust after initial jousts, but AB engines also capable of faster accel/energy recovery
                            S6R5dynamicRecoil += Mathf.Max(0f, engineThrust * (engines.Current.thrustPercentage / 100f));
                            Debug.Log("[BDArmory.ModuleWeapon]: S6R5 DynamicRecoil set to : " + Mathf.CeilToInt(S6R5dynamicRecoil * 2));
                        }
                }
            }
            PAWRefresh(); // Refresh weapon PAW to enable/disable any extra options
        }

        IEnumerator EnableWeaponNextFrame()
        {
            yield return new WaitForFixedUpdate();
            EnableWeapon();
        }

        private float S6R5dynamicRecoil;

        void OnDestroy()
        {
            if (muzzleFlashList != null)
                foreach (var pelist in muzzleFlashList)
                    foreach (var pe in pelist)
                        if (pe) EffectBehaviour.RemoveParticleEmitter(pe);
            foreach (var pe in part.FindModelComponents<KSPParticleEmitter>())
                if (pe) EffectBehaviour.RemoveParticleEmitter(pe);
            if (laserRenderers != null)
            {
                for (int i = 0; i < laserRenderers.Length; i++)
                {
                    laserRenderers[i].enabled = false;
                }
            }
            BDArmorySetup.OnVolumeChange -= UpdateVolume;
            WeaponNameWindow.OnActionGroupEditorOpened.Remove(OnActionGroupEditorOpened);
            WeaponNameWindow.OnActionGroupEditorClosed.Remove(OnActionGroupEditorClosed);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.FashionablyLate, AimAndFire);
        }
        public void PAWRefresh()
        {
            if (eFuzeType == BulletFuzeTypes.Proximity || eFuzeType == BulletFuzeTypes.Flak || eFuzeType == BulletFuzeTypes.Timed || beehive)
            {
                Fields["defaultDetonationRange"].guiActive = true;
                Fields["defaultDetonationRange"].guiActiveEditor = true;
                Fields["detonationRange"].guiActive = true;
                Fields["detonationRange"].guiActiveEditor = true;
                // detonationRange = -1;
            }
            else
            {
                Fields["defaultDetonationRange"].guiActive = false;
                Fields["defaultDetonationRange"].guiActiveEditor = false;
                Fields["detonationRange"].guiActive = false;
                Fields["detonationRange"].guiActiveEditor = false;
            }
            if (eWeaponType == WeaponTypes.Rocket && proximityDetonation)
            {
                Fields["detonateAtMinimumDistance"].guiActive = true;
                Fields["detonateAtMinimumDistance"].guiActiveEditor = true;
            }
            else
            {
                Fields["detonateAtMinimumDistance"].guiActive = false;
                Fields["detonateAtMinimumDistance"].guiActiveEditor = false;
            }
            GUIUtils.RefreshAssociatedWindows(part);
        }

        [KSPEvent(advancedTweakable = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_FireAngleOverride_Enable", active = true)]//Disable fire angle override
        public void ToggleOverrideAngle()
        {
            FireAngleOverride = !FireAngleOverride;
            if (!FireAngleOverride)
            {
                Events["ToggleOverrideAngle"].guiName = StringUtils.Localize("#LOC_BDArmory_FireAngleOverride_Enable");// Enable Firing Angle Override
            }
            else
            {
                Events["ToggleOverrideAngle"].guiName = StringUtils.Localize("#LOC_BDArmory_FireAngleOverride_Disable");// Disable Firing Angle Override
            }

            Fields["FiringTolerance"].guiActive = FireAngleOverride;
            Fields["FiringTolerance"].guiActiveEditor = FireAngleOverride;

            GUIUtils.RefreshAssociatedWindows(part);
        }
        [KSPEvent(advancedTweakable = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_BurstLengthOverride_Enable", active = true)]//Burst length override
        public void ToggleBurstLengthOverride()
        {
            BurstOverride = !BurstOverride;
            if (!BurstOverride)
            {
                Events["ToggleBurstLengthOverride"].guiName = StringUtils.Localize("#LOC_BDArmory_BurstLengthOverride_Enable");// Enable Firing Angle Override
            }
            else
            {
                Events["ToggleBurstLengthOverride"].guiName = StringUtils.Localize("#LOC_BDArmory_BurstLengthOverride_Disable");// Disable Firing Angle Override
            }

            Fields["fireBurstLength"].guiActive = BurstOverride;
            Fields["fireBurstLength"].guiActiveEditor = BurstOverride;

            GUIUtils.RefreshAssociatedWindows(part);
        }

        public bool toggleDeployState = true;
        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_ToggleAnimation", active = true)]//Disable Engage Options
        public void ToggleDeploy()
        {
            toggleDeployState = !toggleDeployState;

            if (toggleDeployState == false)
            {
                Events["ToggleDeploy"].guiName = StringUtils.Localize("#autoLOC_6001080");//"Deploy"
            }
            else
            {
                Events["ToggleDeploy"].guiName = StringUtils.Localize("#autoLOC_6001339");//""Retract"
            }
            if (deployState != null)
            {
                deployState.normalizedTime = HighLogic.LoadedSceneIsFlight ? 0 : toggleDeployState ? 1 : 0;
                using (List<Part>.Enumerator pSym = part.symmetryCounterparts.GetEnumerator())
                    while (pSym.MoveNext())
                    {
                        if (pSym.Current == null) continue;
                        if (pSym.Current != part && pSym.Current.vessel == vessel)
                        {
                            var wep = pSym.Current.FindModuleImplementing<ModuleWeapon>();
                            if (wep == null) continue;
                            wep.deployState.normalizedTime = toggleDeployState ? 1 : 0;
                        }
                    }
            }
        }

        void FAOCos(BaseField field, object obj)
        {
            maxAutoFireCosAngle = Mathf.Cos((FiringTolerance * Mathf.Deg2Rad));
        }
        void AccAdjust(BaseField field, object obj)
        {
            maxDeviation = baseDeviation + ((baseDeviation / (baseRPM / roundsPerMinute)) - baseDeviation);
            maxDeviation *= Mathf.Clamp(bulletInfo.projectileCount / 5, 1, 5); //modify deviation if shot vs slug
        }
        public string WeaponStatusdebug()
        {
            string status = "Weapon Type: ";
            /*
			if (eWeaponType == WeaponTypes.Ballistic)
				status += "Ballistic; BulletType: " + currentType;
			if (eWeaponType == WeaponTypes.Rocket)
				status += "Rocket; RocketType: " + currentType + "; " + rocketModelPath;
			if (eWeaponType == WeaponTypes.Laser)
				status += "Laser";
			status += "; RoF: " + roundsPerMinute + "; deviation: " + maxDeviation + "; instagib = " + instagib;
			*/
            status += "-Lead Offset: " + GetLeadOffset() + "; FinalAimTgt: " + finalAimTarget + "; tgt: " + visualTargetVessel.GetName() + "; tgt Pos: " + targetPosition + "; pointingAtSelf: " + pointingAtSelf + "; tgt CosAngle " + targetCosAngle + "; wpn CosAngle " + targetAdjustedMaxCosAngle + "; Wpn Autofire " + autoFire;

            return status;
        }

        bool fireConditionCheck => ((((userFiring || agHoldFiring) && !isAPS) || autoFire) && (!turret || turret.TargetInRange(finalAimTarget, 10, float.MaxValue))) || (BurstFire && RoundsRemaining > 0 && RoundsRemaining < RoundsPerMag);
        //if user pulling the trigger || AI controlled and on target if turreted || finish a burstfire weapon's burst

        void Update()
        {
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && !vessel.packed && vessel.IsControllable)
            {
                if (lowpassFilter)
                {
                    if (InternalCamera.Instance && InternalCamera.Instance.isActive)
                    {
                        lowpassFilter.enabled = true;
                    }
                    else
                    {
                        lowpassFilter.enabled = false;
                    }
                }

                var secondaryFireKeyActive = false;
                if ((vessel.isActiveVessel || BDArmorySettings.REMOTE_SHOOTING) && !MapView.MapIsEnabled && !aiControlled)
                {
                    secondaryFireKeyActive = BDInputUtils.GetKey(CustomFireKey);
                    if (secondaryFireKeyActive) EnableWeapon(secondaryFiring: true);
                    else if (weaponState == WeaponStates.EnabledForSecondaryFiring) StandbyWeapon();
                }

                if ((weaponState == WeaponStates.Enabled || weaponState == WeaponStates.EnabledForSecondaryFiring) && (TimeWarp.WarpMode != TimeWarp.Modes.HIGH || TimeWarp.CurrentRate == 1))
                {
                    userFiring = (((weaponState == WeaponStates.Enabled && BDInputUtils.GetKey(BDInputSettingsFields.WEAP_FIRE_KEY) && !GUIUtils.CheckMouseIsOnGui()) //don't fire if mouse on WM GUI; Issue #348
                            || secondaryFireKeyActive)
                        && (vessel.isActiveVessel || BDArmorySettings.REMOTE_SHOOTING) && !MapView.MapIsEnabled && !aiControlled);
                    if (!fireConditionCheck)
                    {
                        if (spinDownAnimation) spinningDown = true; //this doesn't need to be called every fixed frame and can remain here
                        if (!oneShotSound && wasFiring)             //technically the laser reset stuff could also have remained here
                        {
                            audioSource.Stop();
                            wasFiring = false;
                            audioSource2.PlayOneShot(overheatSound);
                        }
                    }
                }
                else
                {
                    if (!oneShotSound)
                    {
                        audioSource.Stop();
                    }
                    autoFire = false;
                }

                if (spinningDown && spinDownAnimation)
                {
                    if (hasFireAnimation)
                    {
                        for (int i = 0; i < fireState.Length; i++)
                        {
                            if (fireState[i].normalizedTime > 1) fireState[i].normalizedTime = 0;
                            fireState[i].speed = fireAnimSpeed;
                            fireAnimSpeed = Mathf.Lerp(fireAnimSpeed, 0, 0.04f);
                        }
                    }
                }
                // Draw gauges
                if (vessel.isActiveVessel)
                {
                    gauge.UpdateAmmoMeter((float)((ammoCount > requestResourceAmount ? ammoCount : 0) / ammoMaxCount));

                    if (showReloadMeter)
                    {
                        {
                            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                                gauge.UpdateReloadMeter(timeSinceFired * BDArmorySettings.FIRE_RATE_OVERRIDE / 60);
                            else
                                gauge.UpdateReloadMeter(timeSinceFired * roundsPerMinute / 60);
                        }
                    }
                    if (isReloading)
                    {
                        gauge.UpdateReloadMeter(ReloadTimer);
                    }
                    gauge.UpdateHeatMeter(heat / maxHeat);
                }
            }
        }

        void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight && !vessel.packed)
            {
                if (!vessel.IsControllable)
                {
                    if (!(weaponState == WeaponStates.PoweringDown || weaponState == WeaponStates.Disabled))
                    {
                        if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log($"[BDArmory.ModuleWeapon]: Vessel {vessel.vesselName} is uncontrollable, disabling weapon " + part.name);
                        DisableWeapon();
                    }
                    return;
                }

                UpdateHeat();
                if (weaponState == WeaponStates.Standby && (TimeWarp.WarpMode != TimeWarp.Modes.HIGH || TimeWarp.CurrentRate == 1)) { aimOnly = true; }
                if ((weaponState == WeaponStates.Enabled || weaponState == WeaponStates.EnabledForSecondaryFiring) && (TimeWarp.WarpMode != TimeWarp.Modes.HIGH || TimeWarp.CurrentRate == 1))
                {
                    aimAndFireIfPossible = true; // Aim and fire in a later timing phase of FixedUpdate. This synchronises firing with the physics instead of waiting until the scene is rendered. It also occurs before Krakensbane adjustments have been made (in the Late timing phase).
                }
                else if (eWeaponType == WeaponTypes.Laser)
                {
                    for (int i = 0; i < laserRenderers.Length; i++)
                    {
                        laserRenderers[i].enabled = false;
                    }
                    //audioSource.Stop();
                }
                GetAmmoCount(AmmoID, out ammoCount, out ammoMaxCount);
                if (!BeltFed)
                {
                    ReloadWeapon();
                }
                if (crewserved)
                {
                    CheckCrewed();
                }
            }
        }

        private void UpdateMenus(bool visible)
        {
            Events["HideUI"].active = visible;
            Events["ShowUI"].active = !visible;
        }

        private void OnActionGroupEditorOpened()
        {
            Events["HideUI"].active = false;
            Events["ShowUI"].active = false;
        }

        private void OnActionGroupEditorClosed()
        {
            Events["HideUI"].active = false;
            Events["ShowUI"].active = true;
        }

        [KSPEvent(guiActiveEditor = true, guiName = "#LOC_BDArmory_HideWeaponGroupUI", active = false)]//Hide Weapon Group UI
        public void HideUI()
        {
            WeaponGroupWindow.HideGUI();
            UpdateMenus(false);
        }

        [KSPEvent(guiActiveEditor = true, guiName = "#LOC_BDArmory_SetWeaponGroupUI", active = false)]//Set Weapon Group UI
        public void ShowUI()
        {
            WeaponGroupWindow.ShowGUI(this);
            UpdateMenus(true);
        }

        void OnGUI()
        {
            if (trajectoryRenderer != null && (!BDArmorySettings.DEBUG_LINES || !(weaponState == WeaponStates.Enabled || weaponState == WeaponStates.EnabledForSecondaryFiring || weaponState == WeaponStates.Standby))) { trajectoryRenderer.enabled = false; }
            if (HighLogic.LoadedSceneIsFlight && (weaponState == WeaponStates.Enabled || weaponState == WeaponStates.EnabledForSecondaryFiring) && vessel && !vessel.packed && vessel.isActiveVessel &&
                BDArmorySettings.DRAW_AIMERS && (MouseAimFlight.IsMouseAimActive || !aiControlled) && !MapView.MapIsEnabled && !pointingAtSelf && !isAPS)
            {
                float size = 30;

                Vector3 reticlePosition;
                if (BDArmorySettings.AIM_ASSIST)
                {
                    if (targetAcquired && (GPSTarget || slaved || MouseAimFlight.IsMouseAimActive || yawRange < 1 || maxPitch - minPitch < 1)
                        && (BDArmorySettings.AIM_ASSIST_MODE || !turret))
                    {
                        if (BDArmorySettings.AIM_ASSIST_MODE) // Target
                            reticlePosition = pointingAtPosition + fixedLeadOffset / targetDistance * pointingDistance;
                        else // Aimer
                            reticlePosition = transform.position + (finalAimTarget - transform.position).normalized * pointingDistance;

                        if (!slaved && !GPSTarget)
                        {
                            GUIUtils.DrawLineBetweenWorldPositions(pointingAtPosition, reticlePosition, 2, new Color(0, 1, 0, 0.6f));
                        }

                        GUIUtils.DrawTextureOnWorldPos(pointingAtPosition, BDArmorySetup.Instance.greenDotTexture, new Vector2(6, 6), 0);

                        if (atprAcquired)
                        {
                            GUIUtils.DrawTextureOnWorldPos(atprTargetPosition, BDArmorySetup.Instance.openGreenSquare, new Vector2(20, 20), 0);
                        }
                    }
                    else
                    {
                        reticlePosition = bulletPrediction;
                    }
                }
                else
                {
                    reticlePosition = pointingAtPosition;
                }

                Texture2D texture;
                if (Vector3.Angle(pointingAtPosition - transform.position, finalAimTarget - transform.position) < 1f)
                {
                    texture = BDArmorySetup.Instance.greenSpikedPointCircleTexture;
                }
                else
                {
                    texture = BDArmorySetup.Instance.greenPointCircleTexture;
                }
                GUIUtils.DrawTextureOnWorldPos(reticlePosition, texture, new Vector2(size, size), 0);

                if (BDArmorySettings.DEBUG_LINES)
                {
                    if (targetAcquired)
                    {
                        GUIUtils.DrawLineBetweenWorldPositions(fireTransforms[0].position, targetPosition, 2, Color.blue);
                    }
                }
            }

            if (HighLogic.LoadedSceneIsEditor && BDArmorySetup.showWeaponAlignment && !(isAPS && !dualModeAPS))
            {
                DrawAlignmentIndicator();
            }

            if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DEBUG_WEAPONS && (weaponState == WeaponStates.Enabled || weaponState == WeaponStates.EnabledForSecondaryFiring) && vessel && !vessel.packed && !MapView.MapIsEnabled)
            {
                GUIUtils.MarkPosition(debugTargetPosition, transform, Color.grey); //lets not have two MarkPositions use the same color...
                GUIUtils.DrawLineBetweenWorldPositions(debugTargetPosition, debugTargetPosition + debugRelVelAdj, 2, Color.green);
                GUIUtils.DrawLineBetweenWorldPositions(debugTargetPosition + debugRelVelAdj, debugTargetPosition + debugRelVelAdj + debugAccAdj, 2, Color.magenta);
                GUIUtils.DrawLineBetweenWorldPositions(debugTargetPosition + debugRelVelAdj + debugAccAdj, debugTargetPosition + debugRelVelAdj + debugAccAdj + debugGravAdj, 2, Color.yellow);
                GUIUtils.MarkPosition(finalAimTarget, transform, Color.cyan, size: 4);
            }
        }

        #endregion KSP Events
        //some code organization
        //Ballistics
        #region Guns 
        private void Fire()
        {
            if (BDArmorySetup.GameIsPaused)
            {
                if (audioSource.isPlaying)
                {
                    audioSource.Stop();
                }
                return;
            }

            float timeGap = GetTimeGap();
            if (timeSinceFired > timeGap
                && !isOverheated
                && !isReloading
                && !pointingAtSelf
                && (aiControlled || !GUIUtils.CheckMouseIsOnGui())
                && WMgrAuthorized())
            {
                bool effectsShot = false;
                CheckLoadedAmmo();
                //Transform[] fireTransforms = part.FindModelTransforms("fireTransform");
                for (float iTime = Mathf.Min(timeSinceFired - timeGap, TimeWarp.fixedDeltaTime); iTime > 1e-4f; iTime -= timeGap) // Use 1e-4f instead of 0 to avoid jitter.
                {
                    for (int i = 0; i < fireTransforms.Length; i++)
                    {
                        if ((!useRippleFire || fireState.Length == 1) || (useRippleFire && i == barrelIndex))
                        {
                            if (CanFire(requestResourceAmount))
                            {
                                Transform fireTransform = fireTransforms[i];
                                spinningDown = false;

                                //recoil
                                if (hasRecoil)
                                {
                                    if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 65)
                                        part.rb.AddForceAtPosition(-fireTransform.forward * ((S6R5dynamicRecoil * 2) / (roundsPerMinute / 60)),
                                        fireTransform.position, ForceMode.Impulse);
                                    else
                                        //doesn't take propellant gas mass into account; GAU-8 should be 44kN, yields 29.9; Vulc should be 14.2, yields ~10.4; GAU-22 16.5, yields 11.9
                                        //Adding a mult of 1.4 brings the GAU8 to 41.8, Vulc to 14.5, GAU-22 to 16.6; not exact, but a reasonably close approximation that looks to scale consistantly across ammos
                                        part.rb.AddForceAtPosition((-fireTransform.forward * (bulletVelocity * (bulletMass * ProjectileCount) / 1000) * 1.4f * BDArmorySettings.RECOIL_FACTOR * recoilReduction),
                                            fireTransform.position, ForceMode.Impulse);
                                }

                                if (!effectsShot)
                                {
                                    WeaponFX();
                                    effectsShot = true;
                                }

                                SourceInfo sourceInfo = new SourceInfo(vessel, weaponManager.Team.Name, part, fireTransform.position);
                                GraphicsInfo graphicsInfo;
                                tracerIntervalCounter++;
                                if (tracerIntervalCounter > tracerInterval)
                                {
                                    tracerIntervalCounter = 0;
                                    graphicsInfo = new GraphicsInfo(bulletTexturePath, projectileColorC, startColorC,
                                    tracerStartWidth, tracerEndWidth, tracerLength, tracerLuminance, tracerDeltaFactor,
                                    smokeTexturePath, explModelPath, explSoundPath);
                                }
                                else
                                {
                                    graphicsInfo = new GraphicsInfo(bulletTexturePath, projectileColorC, startColorC,
                                        nonTracerWidth, nonTracerWidth, tracerLength, bulletLuminance, tracerDeltaFactor,
                                        smokeTexturePath, explModelPath, explSoundPath);

                                    if (!string.IsNullOrEmpty(smokeTexturePath))
                                    {
                                        graphicsInfo.projectileColor = Color.grey;
                                        graphicsInfo.startColor = Color.grey;
                                        graphicsInfo.tracerLength = 0;
                                        graphicsInfo.tracerLuminance = -1;
                                        graphicsInfo.projectileColor.a *= 0.5f;
                                    }

                                    graphicsInfo.startColor.a *= 0.5f;
                                    graphicsInfo.projectileColor.a *= 0.5f;
                                }
                                NukeInfo nukeInfo = new NukeInfo(); // Will inherit parent part's models on enable
                                Vector3[] firedVelocities = new Vector3[bulletInfo.projectileCount];

                                for (int s = 0; s < ProjectileCount; s++)
                                    firedVelocities[s] = VectorUtils.GaussianDirectionDeviation(fireTransform.forward, (maxDeviation / 2)) * bulletVelocity;

                                Vessel targetV = null;
                                float guidance = 0f;

                                if (visualTargetVessel != null && targetAcquisitionType == TargetAcquisitionType.Slaved)
                                {
                                    targetV = visualTargetVessel;
                                    guidance = bulletInfo.guidanceDPS;
                                }

                                float dmgMultiplier = strengthMutator;
                                if (instagib)
                                {
                                    dmgMultiplier = -1;
                                }

                                timeFired = Time.time - iTime;
                                if (isRippleFiring && weaponManager.barrageStagger > 0) // Add variability to fired time to cause variability in reload time.
                                {
                                    var reloadVariability = UnityEngine.Random.Range(-weaponManager.barrageStagger, weaponManager.barrageStagger);
                                    timeFired += reloadVariability;
                                }

                                FireBullet(bulletInfo, bulletInfo.projectileCount, sourceInfo, graphicsInfo, nukeInfo, firedVelocities, true,
                                    (isAPS && delayTime > -1) ? delayTime - Time.time : Mathf.Max(maxTargetingRange, defaultDetonationRange) / bulletVelocity * 1.1f,
                                    iTime, detonationRange, bulletTimeToCPA, false, isAPS, isAPS ? tgtRocket : null, isAPS ? tgtShell : null, resourceSteal,
                                    instagib ? -1 : dmgMultiplier, true, 0f, targetV, guidance, true);

                                //heat
                                heat += heatPerShot;

                                RoundsRemaining++;
                                if (BurstOverride)
                                {
                                    autofireShotCount++;
                                }

                                // APS
                                if (isAPS && (tgtShell != null || tgtRocket != null))
                                {
                                    StartCoroutine(KillIncomingProjectile(tgtShell, tgtRocket));
                                }
                            }
                            else
                            {
                                spinningDown = true;
                                if (!oneShotSound && wasFiring)
                                {
                                    audioSource.Stop();
                                    wasFiring = false;
                                    audioSource2.PlayOneShot(overheatSound);
                                }
                            }
                        }
                    }
                }

                if (fireState.Length > 1)
                {
                    barrelIndex++;
                    animIndex++;
                    //Debug.Log("[BDArmory.ModuleWeapon]: barrelIndex for " + GetShortName() + " is " + barrelIndex + "; total barrels " + fireTransforms.Length);
                    if ((!BurstFire || (BurstFire && (RoundsRemaining >= RoundsPerMag))) && barrelIndex + 1 > fireTransforms.Length) //only advance ripple index if weapon isn't burstfire, has finished burst, or has fired with all barrels
                    {
                        StartCoroutine(IncrementRippleIndex(InitialFireDelay * TimeWarp.CurrentRate));
                        isRippleFiring = true;
                        if (barrelIndex >= fireTransforms.Length)
                        {
                            barrelIndex = 0;
                            //Debug.Log("[BDArmory.ModuleWeapon]: barrelIndex for " + GetShortName() + " reset");
                        }
                    }
                    if (animIndex >= fireState.Length) animIndex = 0;
                }
                else
                {
                    if (!BurstFire || (BurstFire && (RoundsRemaining >= RoundsPerMag)))
                    {
                        StartCoroutine(IncrementRippleIndex(InitialFireDelay * TimeWarp.CurrentRate)); //this is why ripplefire is slower, delay to stagger guns should only be being called once
                        isRippleFiring = true;
                        //need to know what next weapon in ripple sequence is, and have firedelay be set to whatever it's RPM is, not this weapon's or a generic average
                    }
                }
            }
            else
            {
                spinningDown = true;
            }
        }

        public bool CanFireSoon()
        {
            float timeGap = GetTimeGap();

            if (timeGap <= weaponManager.targetScanInterval)
                return true;
            else
                return timeSinceFired >= timeGap - weaponManager.targetScanInterval;
        }
        #endregion Guns
        //lasers
        #region LaserFire
        private bool FireLaser()
        {
            float chargeAmount;
            if (pulseLaser)
            {
                chargeAmount = requestResourceAmount;
            }
            else
            {
                chargeAmount = requestResourceAmount * TimeWarp.fixedDeltaTime;
            }

            float timeGap = GetTimeGap();
            beamDuration = Math.Min(timeGap * 0.8f, 0.1f);

            if (timeSinceFired > timeGap
                && !isOverheated
                // && !isReloading
                && !pointingAtSelf
                && (aiControlled || !GUIUtils.CheckMouseIsOnGui())
                && WMgrAuthorized())
            {
                if (CanFire(chargeAmount))
                {
                    var aName = vessel.GetName();
                    if (pulseLaser)
                    {
                        for (float iTime = Mathf.Min(timeSinceFired - timeGap, TimeWarp.fixedDeltaTime); iTime > 1e-4f; iTime -= timeGap)
                        {
                            timeFired = Time.time - iTime;
                            BDACompetitionMode.Instance.Scores.RegisterShot(aName);
                            LaserBeam(aName);
                        }
                        heat += heatPerShot;

                        if (fireState.Length > 1)
                        {
                            barrelIndex++;
                            animIndex++;
                            //Debug.Log("[BDArmory.ModuleWeapon]: barrelIndex for " + GetShortName() + " is " + barrelIndex + "; total barrels " + fireTransforms.Length);
                            if ((!BurstFire || (BurstFire && (RoundsRemaining >= RoundsPerMag))) && barrelIndex + 1 > fireTransforms.Length) //only advance ripple index if weapon isn't brustfire, has finished burst, or has fired with all barrels
                            {
                                StartCoroutine(IncrementRippleIndex(InitialFireDelay * TimeWarp.CurrentRate));
                                isRippleFiring = true;
                                if (barrelIndex >= fireTransforms.Length)
                                {
                                    barrelIndex = 0;
                                    //Debug.Log("[BDArmory.ModuleWeapon]: barrelIndex for " + GetShortName() + " reset");
                                }
                            }
                            if (animIndex >= fireState.Length) animIndex = 0;
                        }
                        else
                        {
                            if (!BurstFire || (BurstFire && (RoundsRemaining >= RoundsPerMag)))
                            {
                                StartCoroutine(IncrementRippleIndex(InitialFireDelay * TimeWarp.CurrentRate));
                                isRippleFiring = true;
                            }
                        }
                    }
                    else
                    {
                        LaserBeam(aName);
                        heat += heatPerShot * TimeWarp.CurrentRate;
                        BeamTracker += 0.02f;
                        if (BeamTracker > beamScoreTime)
                        {
                            BDACompetitionMode.Instance.Scores.RegisterShot(aName);
                        }
                        timeFired = Time.time;
                    }
                    if (!BeltFed)
                    {
                        RoundsRemaining++;
                    }
                    if (BurstOverride)
                    {
                        autofireShotCount++;
                    }
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
        private void LaserBeam(string vesselname)
        {
            if (BDArmorySetup.GameIsPaused)
            {
                if (audioSource.isPlaying)
                {
                    audioSource.Stop();
                }
                return;
            }
            WeaponFX();
            for (int i = 0; i < fireTransforms.Length; i++)
            {
                if (!useRippleFire || !pulseLaser || fireState.Length == 1 || (useRippleFire && i == barrelIndex))
                {
                    float damage = laserDamage;
                    float initialDamage = damage * 0.425f;
                    Transform tf = fireTransforms[i];
                    LineRenderer lr = laserRenderers[i];
                    Vector3 rayDirection = tf.forward;

                    Vector3 targetDirection = Vector3.zero; //autoTrack enhancer
                    Vector3 targetDirectionLR = tf.forward;
                    if (pulseLaser)
                    {
                        rayDirection = VectorUtils.GaussianDirectionDeviation(tf.forward, maxDeviation / 2);
                        targetDirectionLR = rayDirection.normalized;
                    }
                    /*else if (((((visualTargetVessel != null && visualTargetVessel.loaded) || slaved) || (isAPS && (tgtShell != null || tgtRocket != null))) && (turret && (turret.yawRange > 0 && turret.maxPitch > 0))) // causes laser to snap to target CoM if close enough. changed to only apply to turrets
						&& Vector3.Angle(rayDirection, targetDirection) < (isAPS ? 1f : 0.25f)) //if turret and within .25 deg (or 1 deg if APS), snap to target
					{
						//targetDirection = targetPosition + (relativeVelocity * Time.fixedDeltaTime) * 2 - tf.position;
						targetDirection = targetPosition - tf.position; //something in here is throwing off the laser aim, causing the beam to be fired wildly off-target. Disabling it for now. FIXME - debug this later
						rayDirection = targetDirection;
						targetDirectionLR = targetDirection.normalized;
					}*/
                    Ray ray = new Ray(tf.position, rayDirection);
                    lr.useWorldSpace = false;
                    lr.SetPosition(0, Vector3.zero);

                    var raycastDistance = isAPS ? (tgtShell != null ? (tgtShell.currentPosition - tf.position).magnitude : tgtRocket != null ? (tgtRocket.currentPosition - tf.position).magnitude : maxTargetingRange) : maxTargetingRange; // Only raycast to the incoming projectile if APS.
                    var hitCount = Physics.RaycastNonAlloc(ray, laserHits, raycastDistance, layerMask1);
                    if (hitCount == laserHits.Length) // If there's a whole bunch of stuff in the way (unlikely), then we need to increase the size of our hits buffer.
                    {
                        laserHits = Physics.RaycastAll(ray, raycastDistance, layerMask1);
                        hitCount = laserHits.Length;
                    }
                    //Debug.Log($"[LASER DEBUG] hitCount: {hitCount}");
                    if (hitCount > 0)
                    {
                        var orderedHits = laserHits.Take(hitCount).OrderBy(x => x.distance);
                        using (var hitsEnu = orderedHits.GetEnumerator())
                        {
                            Vector3 hitPartVelocity = Vector3.zero;
                            while (hitsEnu.MoveNext())
                            {
                                var hitPart = hitsEnu.Current.collider.gameObject.GetComponentInParent<Part>();
                                if (hitPart != null) // Don't ignore terrain hits.
                                {
                                    hitPartVelocity = hitPart.vessel.Velocity() - BDKrakensbane.FrameVelocityV3f;
                                }
                                break;
                            }
                            var hit = hitsEnu.Current;
                            lr.useWorldSpace = true;
                            laserPoint = hit.point + TimeWarp.fixedDeltaTime * hitPartVelocity;

                            lr.SetPosition(0, tf.position + (part.rb.velocity * Time.fixedDeltaTime));
                            lr.SetPosition(1, laserPoint);

                            KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                            Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();
                            if (p && p.vessel && p.vessel != vessel)
                            {
                                float distance = hit.distance;
                                if (instagib)
                                {
                                    p.AddInstagibDamage();
                                    ExplosionFx.CreateExplosion(hit.point, 1, "BDArmory/Models/explosion/explosion", explSoundPath, ExplosionSourceType.Bullet, 0, null, vessel.vesselName, null, Hitpart: p);
                                }
                                else
                                {
                                    if (electroLaser || HeatRay)
                                    {
                                        if (electroLaser)
                                        {
                                            if (!VesselModuleRegistry.ignoredVesselTypes.Contains(p.vessel.vesselType))
                                            {
                                                var emp = p.vessel.rootPart.FindModuleImplementing<ModuleDrainEC>();
                                                if (emp == null)
                                                {
                                                    emp = (ModuleDrainEC)p.vessel.rootPart.AddModule("ModuleDrainEC");
                                                    //Debug.Log($"[BDArmory.ModuleWeapon]: EMP Module added to {p.vessel.GetName()}: {p.vessel.rootPart.partInfo.title}");
                                                }
                                                float EMPDamage = 0;
                                                if (!pulseLaser)
                                                {
                                                    //EMPDamage = secondaryAmmoPerShot / 500;
                                                    EMPDamage = laserDamage * TimeWarp.fixedDeltaTime; //have materials come into play? wooden part not going to conduct electricity as well as aluminium?
                                                    emp.incomingDamage += EMPDamage;
                                                }
                                                else
                                                {
                                                    //EMPDamage = secondaryAmmoPerShot / 10;
                                                    EMPDamage = laserDamage;
                                                    emp.incomingDamage += EMPDamage;
                                                }
                                                emp.softEMP = true;
                                                damage = EMPDamage;
                                                if (BDArmorySettings.DEBUG_WEAPONS) 
                                                    Debug.Log($"[BDArmory.ModuleWeapon]: EMP Buildup Applied to {p.vessel.GetName()}: {laserDamage}");
                                            }
                                        }
                                        else
                                        {
                                            var dist = Mathf.Sin(maxDeviation) * (tf.position - laserPoint).magnitude;
                                            var hitCount2 = Physics.OverlapSphereNonAlloc(hit.point, dist, heatRayColliders, layerMask2);
                                            if (hitCount2 == heatRayColliders.Length)
                                            {
                                                heatRayColliders = Physics.OverlapSphere(hit.point, dist, layerMask2);
                                                hitCount2 = heatRayColliders.Length;
                                            }
                                            using (var hitsEnu2 = heatRayColliders.Take(hitCount2).GetEnumerator())
                                            {
                                                while (hitsEnu2.MoveNext())
                                                {
                                                    KerbalEVA kerb = hitsEnu2.Current.gameObject.GetComponentUpwards<KerbalEVA>();
                                                    Part hitP = kerb ? kerb.part : hitsEnu2.Current.GetComponentInParent<Part>();
                                                    if (hitP == null) continue;
                                                    if (ProjectileUtils.IsIgnoredPart(hitP)) continue;
                                                    if (hitP && hitP != p && hitP.vessel && hitP.vessel != vessel)
                                                    {
                                                        //p.AddDamage(damage);
                                                        if (Physics.Raycast(new Ray(tf.position, hitP.CenterOfDisplacement), out RaycastHit h, (tf.position - laserPoint).magnitude, (int)LayerMasks.Parts))
                                                        {
                                                            var hitPart = h.collider.gameObject.GetComponentInParent<Part>();
                                                            var hitEVA = h.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                                                            if (hitEVA != null) hitPart = hitEVA.part;
                                                            if (hitPart != null && hitPart == hitP)
                                                            {
                                                                p.skinTemperature += (damage * (pulseLaser ? 1 : TimeWarp.fixedDeltaTime)); //add modifier to adjust damage by armor diffusivity value

                                                                if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log($"[BDArmory.ModuleWeapon]: Heatray Applying {damage} heat to {p.name}");
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        HitpointTracker armor = p.GetComponent<HitpointTracker>();
                                        if (laserDamage > 0)
                                        {
                                            var angularSpread = tanAngle * distance; //Scales down the damage based on the increased surface area of the area being hit by the laser. Think flashlight on a wall.
                                            initialDamage = laserDamage / (1 + Mathf.PI * angularSpread * angularSpread) * 0.425f;

                                            if (armor != null)// technically, lasers shouldn't do damage until armor gone, but that would require localized armor tracking instead of the monolithic model currently used                                              
                                            {
                                                damage = (initialDamage * (pulseLaser ? 1 : TimeWarp.fixedDeltaTime)) * Mathf.Clamp((1 - (BDAMath.Sqrt(armor.Diffusivity * (armor.Density / 1000)) * armor.Armor) / initialDamage), 0.005f, 1); //old calc lacked a clamp, could potentially become negative damage
                                            }  //clamps laser damage to not go negative, allow some small amount of bleedthrough - ~30 Be/Steel will negate ABL, ~62 Ti, 42 DU
                                            else
                                            {
                                                damage = initialDamage;
                                                if (!pulseLaser)
                                                {
                                                    damage = initialDamage * TimeWarp.fixedDeltaTime;
                                                }
                                            }
                                            p.ReduceArmor(damage); //really should be tied into diffuisvity, density, and SafeUseTemp - lasers would need to melt/ablate material away; needs to be in cm^3. Review later
                                            p.AddDamage(damage);
                                            if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log($"[BDArmory.ModuleWeapon]: Damage Applied to {p.name} on {p.vessel.GetName()}: {damage}");
                                            if (pulseLaser) BattleDamageHandler.CheckDamageFX(p, caliber, 1 + (damage / initialDamage), HEpulses, false, part.vessel.GetName(), hit, false, false); //beams will proc BD once every scoreAccumulatorTick
                                        }
                                        if (HEpulses)
                                        {
                                            ExplosionFx.CreateExplosion(hit.point,
                                                           (laserDamage / 10000),
                                                           explModelPath, explSoundPath, ExplosionSourceType.Bullet, 1, null, vessel.vesselName, null, Hitpart: p);
                                        }
                                        if (Impulse != 0)
                                        {
                                            if (!pulseLaser)
                                            {
                                                Impulse *= TimeWarp.fixedDeltaTime;
                                            }
                                            if (p.rb != null && p.rb.mass > 0)
                                            {
                                                //if (Impulse > 0)
                                                //{
                                                p.rb.AddForceAtPosition((p.transform.position - tf.position).normalized * (float)Impulse, laserPoint, ForceMode.Impulse);
                                                //}
                                                //else
                                                //{
                                                //    p.rb.AddForceAtPosition((tf.position - p.transform.position).normalized * (float)Impulse, p.transform.position, ForceMode.Impulse);
                                                //}
                                                if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log($"[BDArmory.ModuleWeapon]: Impulse of {Impulse} Applied to {p.vessel.GetName()}");
                                                //if (laserDamage == 0) 
                                                damage += Impulse / 100;
                                            }
                                        }
                                        if (graviticWeapon)
                                        {
                                            if (p.rb != null && p.rb.mass > 0)
                                            {
                                                float duration = BDArmorySettings.WEAPON_FX_DURATION;
                                                if (!pulseLaser)
                                                {
                                                    duration = BDArmorySettings.WEAPON_FX_DURATION * TimeWarp.fixedDeltaTime;
                                                }
                                                var ME = p.FindModuleImplementing<ModuleMassAdjust>();
                                                if (ME == null)
                                                {
                                                    ME = (ModuleMassAdjust)p.AddModule("ModuleMassAdjust");
                                                }
                                                ME.massMod += (massAdjustment * TimeWarp.fixedDeltaTime);
                                                ME.duration += duration;
                                                if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log($"[BDArmory.ModuleWeapon]: Gravitic Buildup Applied to {p.vessel.GetName()}: {massAdjustment}t added");
                                                //if (laserDamage == 0) 
                                                damage += massAdjustment * 100;
                                            }
                                        }
                                    }
                                }
                                var aName = vesselname;
                                var tName = p.vessel.GetName();

                                if (BDACompetitionMode.Instance.Scores.RegisterBulletDamage(aName, tName, Mathf.Abs(damage)))
                                {
                                    if (pulseLaser || (!pulseLaser && ScoreAccumulator > beamScoreTime)) // Score hits with pulse lasers or when the score accumulator is sufficient.
                                    {
                                        ScoreAccumulator = 0;
                                        BDACompetitionMode.Instance.Scores.RegisterBulletHit(aName, tName, WeaponName, distance);
                                        if (!pulseLaser && laserDamage > 0) BattleDamageHandler.CheckDamageFX(p, caliber, 1 + (damage / initialDamage), HEpulses, false, part.vessel.GetName(), hit, false, false);
                                        //pulse lasers check battle damage earlier in the code
                                        if (ProjectileUtils.isReportingWeapon(part) && BDACompetitionMode.Instance.competitionIsActive)
                                        {
                                            string message = $"{tName} hit by {aName}'s {OriginalShortName} at {distance:F3}m!";
                                            BDACompetitionMode.Instance.competitionStatus.Add(message);
                                        }
                                    }
                                    else
                                    {
                                        ScoreAccumulator += TimeWarp.fixedDeltaTime;
                                    }
                                }

                                if (timeSinceFired > 6 / 120 && BDArmorySettings.BULLET_HITS)
                                {
                                    BulletHitFX.CreateBulletHit(p, hit.point, hit, hit.normal, false, 10, 0, weaponManager.Team.Name);
                                }
                            }
                            else
                            {
                                if (electroLaser || HeatRay) continue;
                                var angularSpread = tanAngle * hit.distance; //Scales down the damage based on the increased surface area of the area being hit by the laser. Think flashlight on a wall.
                                initialDamage = laserDamage / (1 + Mathf.PI * angularSpread * angularSpread) * 0.425f;
                                if (!BDArmorySettings.PAINTBALL_MODE) ProjectileUtils.CheckBuildingHit(hit, initialDamage, pulseLaser);
                                if (HEpulses)
                                {
                                    ExplosionFx.CreateExplosion(hit.point,
                                                   (laserDamage / 10000),
                                                   explModelPath, explSoundPath, ExplosionSourceType.Bullet, 1, null, vessel.vesselName, null);
                                }
                            }
                        }
                    }
                    else
                    {
                        if (isAPS && !pulseLaser)
                            laserPoint = lr.transform.InverseTransformPoint(
                                tgtShell != null ? (tgtShell.currentPosition - TimeWarp.fixedDeltaTime * BDKrakensbane.FrameVelocityV3f) : // Bullets have already moved, so we need to correct for the frame velocity.
                                tgtRocket != null ? (tgtRocket.currentPosition + TimeWarp.fixedDeltaTime * (tgtRocket.currentVelocity - BDKrakensbane.FrameVelocityV3f)) : // Rockets have had frame velocity corrections applied, but not physics.
                                (targetDirectionLR * maxTargetingRange) + tf.position
                            );
                        else
                            laserPoint = lr.transform.InverseTransformPoint((targetDirectionLR * maxTargetingRange) + tf.position);
                        lr.SetPosition(1, laserPoint);
                        if (HEpulses)
                        {
                            ExplosionFx.CreateExplosion(tf.position + rayDirection * raycastDistance,
                                           (laserDamage / 10000),
                                           explModelPath, explSoundPath, ExplosionSourceType.Bullet, 1, null, vessel.vesselName, null);
                        }
                    }
                }
                if (BDArmorySettings.DISCO_MODE)
                {
                    projectileColorC = Color.HSVToRGB(Mathf.Lerp(tracerEndWidth, grow ? 1 : 0, 0.35f), 1, 1);
                    tracerStartWidth = Mathf.Lerp(tracerStartWidth, grow ? 1 : 0.05f, 0.35f); //add new tracerGrowWidth field?
                    tracerEndWidth = Mathf.Lerp(tracerEndWidth, grow ? 1 : 0.05f, 0.35f); //add new tracerGrowWidth field?
                    if (grow && tracerStartWidth > 0.95) grow = false;
                    if (!grow && tracerStartWidth < 0.06f) grow = true;
                    UpdateLaserSpecifics(true, dynamicFX, true, false);
                }
            }
        }
        public void SetupLaserSpecifics()
        {
            //chargeSound = SoundUtils.GetAudioClip(chargeSoundPath);
            if (HighLogic.LoadedSceneIsFlight)
            {
                audioSource.clip = fireSound;
            }
            if (laserRenderers == null)
            {
                laserRenderers = new LineRenderer[fireTransforms.Length];
            }
            for (int i = 0; i < fireTransforms.Length; i++)
            {
                Transform tf = fireTransforms[i];
                laserRenderers[i] = tf.gameObject.AddOrGetComponent<LineRenderer>();
                Color laserColor = GUIUtils.ParseColor255(projectileColor);
                laserColor.a = laserColor.a / 2;
                laserRenderers[i].material = new Material(Shader.Find("KSP/Particles/Alpha Blended"));
                laserRenderers[i].material.SetColor("_TintColor", laserColor);
                laserRenderers[i].material.mainTexture = GameDatabase.Instance.GetTexture(laserTexList[0], false);
                laserRenderers[i].material.SetTextureScale("_MainTex", new Vector2(0.01f, 1));
                laserRenderers[i].textureMode = LineTextureMode.Tile;
                laserRenderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; //= false;
                laserRenderers[i].receiveShadows = false;
                laserRenderers[i].startWidth = tracerStartWidth;
                laserRenderers[i].endWidth = tracerEndWidth;
                laserRenderers[i].positionCount = 2;
                laserRenderers[i].SetPosition(0, Vector3.zero);
                laserRenderers[i].SetPosition(1, Vector3.zero);
                laserRenderers[i].useWorldSpace = false;
                laserRenderers[i].enabled = false;
            }
        }
        public void UpdateLaserSpecifics(bool newColor, bool newTex, bool newWidth, bool newOffset)
        {
            if (laserRenderers == null)
            {
                return;
            }
            for (int i = 0; i < fireTransforms.Length; i++)
            {
                if (newColor)
                {
                    laserRenderers[i].material.SetColor("_TintColor", projectileColorC); //change beam to new color
                }
                if (newTex)
                {
                    laserRenderers[i].material.mainTexture = GameDatabase.Instance.GetTexture(laserTexList[UnityEngine.Random.Range(0, laserTexList.Count - 1)], false); //add support for multiple tex patchs, randomly cycle through
                    laserRenderers[i].material.SetTextureScale("_MainTex", new Vector2(beamScalar, 1));
                }
                if (newWidth)
                {
                    laserRenderers[i].startWidth = tracerStartWidth;
                    laserRenderers[i].endWidth = tracerEndWidth;
                }
                if (newOffset)
                {
                    Offset += beamScrollRate;
                    laserRenderers[i].material.SetTextureOffset("_MainTex", new Vector2(Offset, 0));
                }
            }
        }
        #endregion
        //Rockets
        #region RocketFire
        // this is the extent of RocketLauncher code that differs from ModuleWeapon
        public void FireRocket() //#11, #673
        {
            int rocketsLeft;

            float timeGap = GetTimeGap();
            if (timeSinceFired > timeGap
                && !isReloading
                && !pointingAtSelf
                && (aiControlled || !GUIUtils.CheckMouseIsOnGui())
                && WMgrAuthorized())
            {// fixes rocket ripple code for proper rippling
                bool effectsShot = false;
                for (float iTime = Mathf.Min(timeSinceFired - timeGap, TimeWarp.fixedDeltaTime); iTime > 1e-4f; iTime -= timeGap)
                {
                    if (BDArmorySettings.INFINITE_AMMO)
                    {
                        rocketsLeft = 1;
                    }
                    else
                    {
                        if (!externalAmmo)
                        {
                            PartResource rocketResource = GetRocketResource();
                            rocketsLeft = (int)rocketResource.amount;
                        }
                        else
                        {
                            vessel.GetConnectedResourceTotals(AmmoID, out double ammoCurrent, out double ammoMax);
                            rocketsLeft = rocketPod ? Mathf.Clamp((int)(RoundsPerMag - RoundsRemaining), 0, Mathf.Clamp((int)ammoCurrent, 0, RoundsPerMag)) : (int)ammoCurrent;
                        }
                    }
                    if (rocketsLeft >= 1)
                    {
                        if (rocketPod)
                        {
                            for (int s = 0; s < ProjectileCount; s++)
                            {
                                Transform currentRocketTfm = rockets[rocketsLeft - 1];
                                GameObject rocketObj = rocketPool[SelectedAmmoType].GetPooledObject();
                                rocketObj.transform.position = currentRocketTfm.position;
                                //rocketObj.transform.rotation = currentRocketTfm.rotation;
                                rocketObj.transform.rotation = currentRocketTfm.parent.rotation;
                                rocketObj.transform.localScale = part.rescaleFactor * Vector3.one;
                                PooledRocket rocket = rocketObj.GetComponent<PooledRocket>();
                                rocket.explModelPath = explModelPath;
                                rocket.explSoundPath = explSoundPath;
                                rocket.spawnTransform = currentRocketTfm;
                                rocket.caliber = rocketInfo.caliber;
                                rocket.apMod = rocketInfo.apMod;
                                rocket.rocketMass = rocketMass;
                                rocket.blastRadius = blastRadius;
                                rocket.thrust = thrust;
                                rocket.thrustTime = thrustTime;
                                rocket.lifeTime = rocketInfo.lifeTime;
                                rocket.flak = proximityDetonation;
                                rocket.detonateAtMinimumDistance = detonateAtMinimumDistance;
                                rocket.detonationRange = detonationRange;
                                // rocket.maxAirDetonationRange = maxAirDetonationRange;
                                rocket.timeToDetonation = predictedFlightTime;
                                rocket.tntMass = rocketInfo.tntMass;
                                rocket.shaped = rocketInfo.shaped;
                                rocket.concussion = rocketInfo.impulse;
                                rocket.gravitic = rocketInfo.gravitic; ;
                                rocket.EMP = electroLaser; //borrowing this as a EMP weapon bool, since a rocket isn't going to be a laser
                                rocket.nuclear = rocketInfo.nuclear;
                                rocket.beehive = beehive;
                                if (beehive)
                                {
                                    rocket.subMunitionType = rocketInfo.subMunitionType;
                                }
                                rocket.choker = choker;
                                rocket.impulse = Impulse;
                                rocket.massMod = massAdjustment;
                                rocket.incendiary = incendiary;
                                rocket.randomThrustDeviation = thrustDeviation;
                                rocket.bulletDmgMult = bulletDmgMult;
                                rocket.sourceVessel = vessel;
                                rocket.sourceWeapon = part;
                                rocketObj.transform.SetParent(currentRocketTfm.parent);
                                rocket.rocketName = GetShortName() + " rocket";
                                rocket.team = weaponManager.Team.Name;
                                rocket.parentRB = part.rb;
                                rocket.rocket = RocketInfo.rockets[currentType];
                                rocket.rocketSoundPath = rocketSoundPath;
                                rocket.thief = resourceSteal; //currently will only steal on direct hit
                                rocket.dmgMult = strengthMutator;
                                if (instagib) rocket.dmgMult = -1;
                                if (isAPS)
                                {
                                    rocket.isAPSprojectile = true;
                                    rocket.tgtShell = tgtShell;
                                    rocket.tgtRocket = tgtRocket;
                                    if (delayTime > 0) rocket.lifeTime = delayTime;
                                }
                                rocket.isSubProjectile = false;
                                rocketObj.SetActive(true);
                            }
                            if (!BDArmorySettings.INFINITE_AMMO)
                            {
                                if (externalAmmo)
                                {
                                    part.RequestResource(ammoName.GetHashCode(), (double)requestResourceAmount, BDArmorySettings.WEAPONS_RESPECT_CROSSFEED ? ResourceFlowMode.STACK_PRIORITY_SEARCH : ResourceFlowMode.ALL_VESSEL);
                                }
                                else
                                {
                                    GetRocketResource().amount--;
                                }
                            }
                            if (!BeltFed)
                            {
                                RoundsRemaining++;
                            }
                            if (BurstOverride)
                            {
                                autofireShotCount++;
                            }
                            UpdateRocketScales();
                        }
                        else
                        {
                            if (!isOverheated)
                            {
                                for (int i = 0; i < fireTransforms.Length; i++)
                                {
                                    if ((!useRippleFire || fireState.Length == 1) || (useRippleFire && i == barrelIndex))
                                    {
                                        for (int s = 0; s < ProjectileCount; s++)
                                        {
                                            Transform currentRocketTfm = fireTransforms[i];
                                            GameObject rocketObj = rocketPool[SelectedAmmoType].GetPooledObject();
                                            rocketObj.transform.position = currentRocketTfm.position;
                                            //rocketObj.transform.rotation = currentRocketTfm.rotation;
                                            rocketObj.transform.rotation = currentRocketTfm.parent.rotation;
                                            rocketObj.transform.localScale = part.rescaleFactor * Vector3.one;
                                            PooledRocket rocket = rocketObj.GetComponent<PooledRocket>();
                                            rocket.explModelPath = explModelPath;
                                            rocket.explSoundPath = explSoundPath;
                                            rocket.spawnTransform = currentRocketTfm;
                                            rocket.caliber = rocketInfo.caliber;
                                            rocket.apMod = rocketInfo.apMod;
                                            rocket.rocketMass = rocketMass;
                                            rocket.blastRadius = blastRadius;
                                            rocket.thrust = thrust;
                                            rocket.thrustTime = thrustTime;
                                            rocket.lifeTime = rocketInfo.lifeTime;
                                            rocket.flak = proximityDetonation;
                                            rocket.detonateAtMinimumDistance = detonateAtMinimumDistance;
                                            rocket.detonationRange = detonationRange;
                                            // rocket.maxAirDetonationRange = maxAirDetonationRange;
                                            rocket.timeToDetonation = predictedFlightTime;
                                            rocket.tntMass = rocketInfo.tntMass;
                                            rocket.shaped = rocketInfo.shaped;
                                            rocket.concussion = impulseWeapon;
                                            rocket.gravitic = graviticWeapon;
                                            rocket.EMP = electroLaser;
                                            rocket.nuclear = rocketInfo.nuclear;
                                            rocket.beehive = beehive;
                                            if (beehive)
                                            {
                                                rocket.subMunitionType = rocketInfo.subMunitionType;
                                            }
                                            rocket.choker = choker;
                                            rocket.impulse = Impulse;
                                            rocket.massMod = massAdjustment;
                                            rocket.incendiary = incendiary;
                                            rocket.randomThrustDeviation = thrustDeviation;
                                            rocket.bulletDmgMult = bulletDmgMult;
                                            rocket.sourceVessel = vessel;
                                            rocket.sourceWeapon = part;
                                            rocketObj.transform.SetParent(currentRocketTfm);
                                            rocket.parentRB = part.rb;
                                            rocket.rocket = RocketInfo.rockets[currentType];
                                            rocket.rocketName = GetShortName() + " rocket";
                                            rocket.team = weaponManager.Team.Name;
                                            rocket.rocketSoundPath = rocketSoundPath;
                                            rocket.thief = resourceSteal;
                                            rocket.dmgMult = strengthMutator;
                                            if (instagib) rocket.dmgMult = -1;
                                            if (isAPS)
                                            {
                                                rocket.isAPSprojectile = true;
                                                rocket.tgtShell = tgtShell;
                                                rocket.tgtRocket = tgtRocket;
                                                if (delayTime > 0) rocket.lifeTime = delayTime;
                                            }
                                            rocket.isSubProjectile = false;
                                            rocketObj.SetActive(true);
                                        }
                                        if (!BDArmorySettings.INFINITE_AMMO)
                                        {
                                            part.RequestResource(ammoName.GetHashCode(), (double)requestResourceAmount, BDArmorySettings.WEAPONS_RESPECT_CROSSFEED ? ResourceFlowMode.STACK_PRIORITY_SEARCH : ResourceFlowMode.ALL_VESSEL);
                                        }
                                        heat += heatPerShot;
                                        if (!BeltFed)
                                        {
                                            RoundsRemaining++;
                                        }
                                        if (BurstOverride)
                                        {
                                            autofireShotCount++;
                                        }
                                    }
                                }
                            }
                        }
                        if (!effectsShot)
                        {
                            WeaponFX();
                            effectsShot = true;
                        }
                        timeFired = Time.time - iTime;
                    }
                }
                if (fireState.Length > 1)
                {
                    barrelIndex++;
                    animIndex++;
                    //Debug.Log("[BDArmory.ModuleWeapon]: barrelIndex for " + GetShortName() + " is " + barrelIndex + "; total barrels " + fireTransforms.Length);
                    if ((!BurstFire || (BurstFire && (RoundsRemaining >= RoundsPerMag))) && barrelIndex + 1 > fireTransforms.Length) //only advance ripple index if weapon isn't brustfire, has finished burst, or has fired with all barrels
                    {
                        StartCoroutine(IncrementRippleIndex(InitialFireDelay * TimeWarp.CurrentRate));
                        isRippleFiring = true;
                        if (barrelIndex >= fireTransforms.Length)
                        {
                            barrelIndex = 0;
                            //Debug.Log("[BDArmory.ModuleWeapon]: barrelIndex for " + GetShortName() + " reset");
                        }
                    }
                    if (animIndex >= fireState.Length) animIndex = 0;
                }
                else
                {
                    if (!BurstFire || (BurstFire && (RoundsRemaining >= RoundsPerMag)))
                    {
                        StartCoroutine(IncrementRippleIndex(InitialFireDelay * TimeWarp.CurrentRate));
                        isRippleFiring = true;
                    }
                }
                if (isAPS && (tgtShell != null || tgtRocket != null))
                {
                    StartCoroutine(KillIncomingProjectile(tgtShell, tgtRocket));
                }
            }
        }

        void MakeRocketArray()
        {
            Transform rocketsTransform = part.FindModelTransform("rockets");// important to keep this seperate from the fireTransformName transform
            int numOfRockets = rocketsTransform.childCount;     // due to rockets.Rocket_n being inconsistantly aligned 
            rockets = new Transform[numOfRockets];              // (and subsequently messing up the aim() vestors) 
            if (rocketPod)                                    // and this overwriting the previous fireTransFormName -> fireTransForms
            {
                RoundsPerMag = numOfRockets;
            }
            for (int i = 0; i < numOfRockets; i++)
            {
                string rocketName = rocketsTransform.GetChild(i).name;
                int rocketIndex = int.Parse(rocketName.Substring(7)) - 1;
                rockets[rocketIndex] = rocketsTransform.GetChild(i);
            }
            if (!descendingOrder) Array.Reverse(rockets);
        }

        void UpdateRocketScales()
        {
            double rocketQty = 0;

            if (!externalAmmo)
            {
                PartResource rocketResource = GetRocketResource();
                if (rocketResource != null)
                {
                    rocketQty = rocketResource.amount;
                    rocketsMax = rocketResource.maxAmount;
                }
                else
                {
                    rocketQty = 0;
                    rocketsMax = 0;
                }
            }
            else
            {
                rocketQty = (RoundsPerMag - RoundsRemaining);
                rocketsMax = Mathf.Min(RoundsPerMag, (float)ammoCount);
            }
            var rocketsLeft = Math.Floor(rocketQty);

            for (int i = 0; i < rocketsMax; i++)
            {
                if (i < rocketsLeft) rockets[i].localScale = Vector3.one;
                else rockets[i].localScale = Vector3.zero;
            }
        }

        public PartResource GetRocketResource()
        {
            using (IEnumerator<PartResource> res = part.Resources.GetEnumerator())
                while (res.MoveNext())
                {
                    if (res.Current == null) continue;
                    if (res.Current.resourceName == ammoName) return res.Current;
                }
            return null;
        }
        #endregion RocketFire
        //Shared FX and resource consumption code
        #region WeaponUtilities

        /// <summary>
        /// Get the time gap between shots for the weapon.
        /// </summary>
        /// <returns></returns>
        float GetTimeGap()
        {
            if (eWeaponType == WeaponTypes.Laser && !pulseLaser) return 0;
            float timeGap = 60 / roundsPerMinute * TimeWarp.CurrentRate; // RPM * barrels
            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                timeGap = 60 / BDArmorySettings.FIRE_RATE_OVERRIDE * TimeWarp.CurrentRate;
            switch (eWeaponType)
            {
                // FIXME These are functionally the same as before. Are these actually correct, particularly the case for rockets?
                case WeaponTypes.Ballistic:
                    // FIXME This should also be being called on guns with multiple fireanims(and thus multiple independant barrels); is causing twinlinked weapons to gain 2x firespeed in barrageMode
                    if (!(useRippleFire && fireState.Length > 1))
                        timeGap *= fireTransforms.Length; // RPM compensating for barrel count
                    break;
                case WeaponTypes.Rocket:
                    if (!rocketPod)
                        timeGap *= fireTransforms.Length;
                    if (useRippleFire && fireState.Length > 1)
                        timeGap /= fireTransforms.Length;
                    break;
                case WeaponTypes.Laser:
                    if (!(useRippleFire && fireState.Length > 1))
                        timeGap *= fireTransforms.Length;
                    break;
            }
            return timeGap;
        }

        void GetAmmoCount(int resourceID, out double ammoC, out double ammoM)
        {
            if (externalAmmo)
                part.GetConnectedResourceTotals(resourceID,
                    BDArmorySettings.WEAPONS_RESPECT_CROSSFEED ? ResourceFlowMode.STACK_PRIORITY_SEARCH : ResourceFlowMode.ALL_VESSEL,
                    out ammoC, out ammoM);
            else part.GetConnectedResourceTotals(resourceID, ResourceFlowMode.NO_FLOW, out ammoC, out ammoM);
        }

        bool CanFire(float AmmoPerShot)
        {
            if (!hasGunner)
            {
                ScreenMessages.PostScreenMessage(StringUtils.Localize("#autoLOC_211097"), 5.0f, ScreenMessageStyle.UPPER_CENTER); // #autoLOC_211097 = No crew on part!
                return false;
            }
            if (BDArmorySettings.INFINITE_AMMO) return true;
            if (secondaryAmmoPerShot != 0)
            {
                if (!(electricResource && CheatOptions.InfiniteElectricity)) //else skip to main ammo as EC is accounted for
                {
                    vessel.GetConnectedResourceTotals(ECID, out double EcCurrent, out double ecMax);
                    if (EcCurrent > secondaryAmmoPerShot * (electricResource ? 0.95f : 1))
                    {
                        part.RequestResource(ECID, secondaryAmmoPerShot, (electricResource || !BDArmorySettings.WEAPONS_RESPECT_CROSSFEED) ? ResourceFlowMode.ALL_VESSEL : ResourceFlowMode.STACK_PRIORITY_SEARCH);
                        if (requestResourceAmount == 0) return true; //weapon only uses secondaryAmmoName for some reason?
                    }
                    else
                    {
                        if (part.vessel.isActiveVessel) ScreenMessages.PostScreenMessage($"{part.partInfo.title} {StringUtils.Localize("#autoLOC_244332")} {PartResourceLibrary.Instance.GetDefinition(secondaryAmmoName).displayName}", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                        return false;
                    }
                    //else return true; //this is causing weapons thath have ECPerShot + standard ammo (railguns, etc) to not consume ammo, only EC
                }
            }

            GetAmmoCount(AmmoID, out double ammoCurrent, out double ammoMax);

            ammoCount = ammoCurrent;
            if (ammoCount >= AmmoPerShot * 0.995f) //catch floating point errors from fractional ammo spread across multiple boxes
            // TODO?? Change code to some sort of list of ammoboxes to only draw from box with current highest resouce amount to do proper integer reductions?
            {
                if (part.RequestResource(ammoName.GetHashCode(), (double)AmmoPerShot, externalAmmo ? (BDArmorySettings.WEAPONS_RESPECT_CROSSFEED ? ResourceFlowMode.STACK_PRIORITY_SEARCH : ResourceFlowMode.ALL_VESSEL) : ResourceFlowMode.NO_FLOW) > 0) //for guns with internal ammo supplies and no external, only draw from the weapon part
                {
                    return true;
                }
            }
            StartCoroutine(IncrementRippleIndex(useRippleFire ? InitialFireDelay * TimeWarp.CurrentRate : 0)); //if out of ammo (howitzers, say, or other weapon with internal ammo, move on to next weapon; maybe it still has ammo
            isRippleFiring = true;
            return false;
        }

        void PlayFireAnim()
        {
            if (hasCharged)
            {
                if (hasChargeHoldAnimation)
                    chargeHoldState.enabled = false;
                else if (hasChargeAnimation) chargeState.enabled = false;
            }
            //Debug.Log("[BDArmory.ModuleWeapon]: fireState length = " + fireState.Length);
            for (int i = 0; i < fireState.Length; i++)
            {
                // try { }
                // catch
                // {
                //     Debug.Log("[BDArmory.ModuleWeapon]: error with fireanim number " + barrelIndex);
                // }
                if ((!useRippleFire && fireTransforms.Length > 1) || (i == animIndex)) //play fireanims sequentially, unless a multibarrel weapon in salvomode, then play all fireanims simultaneously
                {
                    float unclampedSpeed = (roundsPerMinute * fireState[i].length) / 60f;
                    if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                        unclampedSpeed = (BDArmorySettings.FIRE_RATE_OVERRIDE * fireState[i].length) / 60f;

                    float lowFramerateFix = 1;
                    if (roundsPerMinute > 500f)
                    {
                        lowFramerateFix = (0.02f / Time.deltaTime);
                    }
                    fireAnimSpeed = Mathf.Clamp(unclampedSpeed, 1f * lowFramerateFix, 20f * lowFramerateFix);
                    fireState[i].enabled = true;
                    if (unclampedSpeed == fireAnimSpeed || fireState[i].normalizedTime > 1)
                    {
                        fireState[i].normalizedTime = 0;
                    }
                    fireState[i].speed = fireAnimSpeed;
                    fireState[i].normalizedTime = Mathf.Repeat(fireState[i].normalizedTime, 1);
                    //if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log("[BDArmory.ModuleWeapon]: playing Fire Anim, i = " + i + "; fire anim " + fireState[i].name + "normalizedTime: " + fireState[i].normalizedTime);
                }
            }
        }

        void WeaponFX()
        {
            //sound
            if (ChargeTime > 0 && !hasCharged)
            {
                audioSource.Stop();
            }
            if (oneShotSound)
            {
                audioSource.Stop();
                audioSource.PlayOneShot(fireSound);
            }
            else
            {
                wasFiring = true;
                if (!audioSource.isPlaying)
                {
                    if (audioSource2.isPlaying) audioSource2.Stop(); // Stop any continuing cool-down sounds.
                    audioSource.clip = fireSound;
                    audioSource.loop = (soundRepeatTime == 0);
                    audioSource.time = 0;
                    audioSource.Play();
                }
                else
                {
                    if (audioSource.time >= fireSound.length)
                    {
                        audioSource.time = soundRepeatTime;
                    }
                }
            }
            //animation
            if (hasFireAnimation)
            {
                PlayFireAnim();
            }

            for (int i = 0; i < muzzleFlashList.Count; i++)
            {
                if ((!useRippleFire || fireState.Length == 1) || useRippleFire && i == barrelIndex)
                    //muzzle flash
                    using (List<KSPParticleEmitter>.Enumerator pEmitter = muzzleFlashList[i].GetEnumerator())
                        while (pEmitter.MoveNext())
                        {
                            if (pEmitter.Current == null) continue;
                            if (pEmitter.Current.useWorldSpace && !oneShotWorldParticles) continue;
                            if (pEmitter.Current.maxEnergy < 0.5f)
                            {
                                float twoFrameTime = Mathf.Clamp(Time.deltaTime * 2f, 0.02f, 0.499f);
                                pEmitter.Current.maxEnergy = twoFrameTime;
                                pEmitter.Current.minEnergy = twoFrameTime / 3f;
                            }
                            pEmitter.Current.Emit();
                        }
                using (List<BDAGaplessParticleEmitter>.Enumerator gpe = gaplessEmitters.GetEnumerator())
                    while (gpe.MoveNext())
                    {
                        if (gpe.Current == null) continue;
                        gpe.Current.EmitParticles();
                    }
            }
            //shell ejection
            if (BDArmorySettings.EJECT_SHELLS)
            {
                for (int i = 0; i < shellEjectTransforms.Length; ++i)
                {
                    if ((!useRippleFire || fireState.Length == 1) || (useRippleFire && i == barrelIndex))
                        StartCoroutine(EjectShell(shellEjectDelay, i));
                }
            }
        }

        IEnumerator EjectShell(float delay, int ejectTransformIndex)
        {
            if (delay > 0) yield return new WaitForSecondsFixed(delay);
            if (part == null || part.rb == null) yield break;

            GameObject ejectedShell = shellPool.GetPooledObject();
            ejectedShell.transform.position = shellEjectTransforms[ejectTransformIndex].position;
            ejectedShell.transform.rotation = shellEjectTransforms[ejectTransformIndex].rotation;
            ejectedShell.transform.localScale = Vector3.one * shellScale;
            ShellCasing shellComponent = ejectedShell.GetComponent<ShellCasing>();
            shellComponent.initialV = part.rb.velocity;
            shellComponent.configV = shellEjectVelocity;
            shellComponent.configD = shellEjectDeviation;
            shellComponent.lifeTime = shellEjectLifeTime;
            ejectedShell.SetActive(true);

        }

        private void CheckLoadedAmmo()
        {
            if (!useCustomBelt) return;
            if (customAmmoBelt.Count < 1) return;
            if (AmmoIntervalCounter == 0 || (AmmoIntervalCounter > 1 && customAmmoBelt[AmmoIntervalCounter].ToString() != customAmmoBelt[AmmoIntervalCounter - 1].ToString()))
            {
                SetupAmmo(null, null);
            }
            AmmoIntervalCounter++;
            if (AmmoIntervalCounter == customAmmoBelt.Count)
            {
                AmmoIntervalCounter = 0;
            }
        }
        #endregion WeaponUtilities
        //misc. like check weaponmgr
        #region WeaponSetup
        bool WMgrAuthorized()
        {
            MissileFire manager = BDArmorySetup.Instance.ActiveWeaponManager;
            if (manager != null && manager.vessel == vessel)
            {
                if (manager.hasSingleFired) return false;
                else return true;
            }
            else
            {
                return true;
            }
        }

        void CheckWeaponSafety()
        {
            pointingAtSelf = false;

            // While I'm not saying vessels larger than 500m are impossible, let's be practical here
            const float maxCheckRange = 500f;
            pointingDistance = Mathf.Min(targetAcquired ? targetDistance : maxTargetingRange, maxCheckRange);

            for (int i = 0; i < fireTransforms.Length; i++)
            {
                Ray ray = new Ray(fireTransforms[i].position, fireTransforms[i].forward);
                RaycastHit hit;

                if (Physics.Raycast(ray, out hit, pointingDistance, layerMask1))
                {
                    KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                    Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();
                    if (p && p.vessel && p.vessel == vessel)
                    {
                        pointingAtSelf = true;
                        break;
                    }
                }

                pointingAtPosition = fireTransforms[i].position + (ray.direction * pointingDistance);
            }
        }

        HashSet<WeaponStates> enabledStates = new HashSet<WeaponStates> { WeaponStates.Enabled, WeaponStates.PoweringUp, WeaponStates.Locked };
        public void EnableWeapon(bool secondaryFiring = false)
        {
            if (enabledStates.Contains(weaponState) || (secondaryFiring && weaponState == WeaponStates.EnabledForSecondaryFiring))
                return;

            StopShutdownStartupRoutines();
            UpdateOffsetWeapon(); // Re-calculate offset/non-centerline weapon corrections on weapon selection
            startupRoutine = StartCoroutine(StartupRoutine(secondaryFiring: secondaryFiring));
        }

        HashSet<WeaponStates> disabledStates = new HashSet<WeaponStates> { WeaponStates.Disabled, WeaponStates.PoweringDown };
        public void DisableWeapon()
        {
            if (dualModeAPS) isAPS = true;
            if (isAPS)
            {
                if (ammoCount > 0 || BDArmorySettings.INFINITE_AMMO)
                {
                    //EnableWeapon();
                    aiControlled = true;
                    return;
                }
            }
            if (disabledStates.Contains(weaponState))
                return;

            StopShutdownStartupRoutines();

            if (part.isActiveAndEnabled) shutdownRoutine = StartCoroutine(ShutdownRoutine());
        }

        HashSet<WeaponStates> standbyStates = new HashSet<WeaponStates> { WeaponStates.Standby, WeaponStates.PoweringUp, WeaponStates.Locked };
        public void StandbyWeapon()
        {
            if (dualModeAPS) isAPS = true;
            if (isAPS)
            {
                if (ammoCount > 0 || BDArmorySettings.INFINITE_AMMO)
                {
                    EnableWeapon();
                    aiControlled = true;
                    return;
                }
            }
            if (standbyStates.Contains(weaponState))
                return;
            if (disabledStates.Contains(weaponState))
            {
                StopShutdownStartupRoutines();
                standbyRoutine = StartCoroutine(StandbyRoutine());
            }
            else
            {
                weaponState = WeaponStates.Standby;
                UpdateGUIWeaponState();
                BDArmorySetup.Instance.UpdateCursorState();
            }
        }

        public void ParseWeaponType(string type)
        {
            type = type.ToLower();

            switch (type)
            {
                case "ballistic":
                    eWeaponType = WeaponTypes.Ballistic;
                    break;
                case "rocket":
                    eWeaponType = WeaponTypes.Rocket;
                    break;
                case "laser":
                    eWeaponType = WeaponTypes.Laser;
                    break;
                case "cannon":
                    // Note:  this type is deprecated.  behavior is duplicated with Ballistic and bulletInfo.explosive = true
                    // Type remains for backward compatability for now.
                    eWeaponType = WeaponTypes.Ballistic;
                    break;
            }
        }
        #endregion WeaponSetup

        #region Audio

        void UpdateVolume()
        {
            if (audioSource)
            {
                audioSource.volume = BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
            }
            if (audioSource2)
            {
                audioSource2.volume = BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
            }
            if (lowpassFilter)
            {
                lowpassFilter.cutoffFrequency = BDArmorySettings.IVA_LOWPASS_FREQ;
            }
        }

        void SetupAudio()
        {
            fireSound = SoundUtils.GetAudioClip(fireSoundPath);
            overheatSound = SoundUtils.GetAudioClip(overheatSoundPath);
            if (!audioSource)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.bypassListenerEffects = true;
                audioSource.minDistance = .3f;
                audioSource.maxDistance = 1000;
                audioSource.priority = 10;
                audioSource.dopplerLevel = 0;
                audioSource.spatialBlend = 1;
            }

            if (!audioSource2)
            {
                audioSource2 = gameObject.AddComponent<AudioSource>();
                audioSource2.bypassListenerEffects = true;
                audioSource2.minDistance = .3f;
                audioSource2.maxDistance = 1000;
                audioSource2.dopplerLevel = 0;
                audioSource2.priority = 10;
                audioSource2.spatialBlend = 1;
            }

            if (reloadAudioPath != string.Empty)
            {
                reloadAudioClip = SoundUtils.GetAudioClip(reloadAudioPath);
            }
            if (reloadCompletePath != string.Empty)
            {
                reloadCompleteAudioClip = SoundUtils.GetAudioClip(reloadCompletePath);
            }

            if (!lowpassFilter && gameObject.GetComponents<AudioLowPassFilter>().Length == 0)
            {
                lowpassFilter = gameObject.AddComponent<AudioLowPassFilter>();
                lowpassFilter.cutoffFrequency = BDArmorySettings.IVA_LOWPASS_FREQ;
                lowpassFilter.lowpassResonanceQ = 1f;
            }

            UpdateVolume();
        }

        #endregion Audio

        #region Targeting
        void Aim()
        {
            //AI control
            if (aiControlled && !slaved && !GPSTarget)
            {
                if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                {
                    if (!targetAcquired && (!weaponManager || Time.time - staleGoodTargetTime > Mathf.Max(60f / BDArmorySettings.FIRE_RATE_OVERRIDE, weaponManager.targetScanInterval)))
                    {
                        autoFire = false;
                        return;
                    }
                }
                else
                {
                    if (!targetAcquired && (!weaponManager || Time.time - staleGoodTargetTime > Mathf.Max(60f / roundsPerMinute, weaponManager.targetScanInterval)))
                    {
                        autoFire = false;
                        return;
                    }
                }
            }

            Vector3 finalTarget = targetPosition;
            bool manualAiming = false;
            if (aiControlled && !slaved && weaponManager != null && (!targetAcquired || (weaponManager.staleTarget && weaponManager.detectedTargetTimeout > 0)))
            {
                if (weaponManager.staleTarget && staleGoodTargetTime > 0 && staleGoodTargetTime <= weaponManager.detectedTargetTimeout) //cap staletarget prediction to point when target forgotten
                {
                    if (BDKrakensbane.IsActive)
                    {
                        staleFinalAimTarget -= BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
                        if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DEBUG_WEAPONS)
                        {
                            debugLastTargetPosition -= BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
                        }
                    }
                    // Continue aiming towards where the target is expected to be while reloading based on the last measured pos, vel, acc.
                    var timeSinceGood = Time.time - staleGoodTargetTime;
                    finalAimTarget = AIUtils.PredictPosition(staleFinalAimTarget, staleTargetVelocity - BDKrakensbane.FrameVelocityV3f, staleTargetAcceleration, timeSinceGood / (1f + timeSinceGood / 30f)); // Smoothly limit prediction to 30s to prevent wild aiming.
                    switch (eWeaponType)
                    {
                        case WeaponTypes.Ballistic:
                        case WeaponTypes.Rocket: // Perform same correction for rockets as for bullets (it's probably fine).
                            finalAimTarget += lastTimeToCPA * (stalePartVelocity - BDKrakensbane.FrameVelocityV3f - smoothedPartVelocity); // Account for our own velocity changes.
                            break;
                            // Lasers have no timeToCPA correction.
                    }

                    fixedLeadOffset = targetPosition - finalAimTarget; //for aiming fixed guns to moving target
                                                                       // if (FlightGlobals.ActiveVessel == vessel) Debug.Log($"DEBUG t: {Time.time}, tgt acq: {targetAcquired}, stale: {weaponManager.staleTarget}, Stale aimer: aim at {finalAimTarget:G3} ({finalAimTarget.magnitude:G3}m), last: {staleFinalAimTarget:G3}, Δt: {timeSinceGood:F2}s ({timeSinceGood / (1f + timeSinceGood / 30f):F2}s)");

                    if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DEBUG_WEAPONS)
                    {
                        debugTargetPosition = AIUtils.PredictPosition(debugLastTargetPosition, targetVelocity, targetAcceleration, Time.time - staleGoodTargetTime);
                    }
                }
                if (!targetAcquired)
                    if (turret) turret.ReturnTurret();
            }
            else
            {
                Transform fireTransform = fireTransforms[0];
                if (eWeaponType == WeaponTypes.Rocket && rocketPod)
                {
                    fireTransform = rockets[0].parent; // support for legacy RLs
                }
                if (!slaved && !GPSTarget && !aiControlled && !isAPS && (vessel.isActiveVessel || BDArmorySettings.REMOTE_SHOOTING))
                {
                    manualAiming = true;
                    bool foundTarget = targetAcquired;
                    if (!targetAcquired)
                    { // Override the smoothing (which isn't run without a target otherwise).
                        smoothedPartVelocity = part.rb.velocity;
                        smoothedPartAcceleration = vessel.acceleration_immediate;
                    }
                    if (yawRange > 0 || maxPitch - minPitch > 0)
                    {
                        //MouseControl
                        var camera = FlightCamera.fetch;
                        Ray ray;
                        if (!MouseAimFlight.IsMouseAimActive)
                        {
                            Vector3 mouseAim = new(Input.mousePosition.x / Screen.width, Input.mousePosition.y / Screen.height, 0);
                            ray = camera.mainCamera.ViewportPointToRay(mouseAim);
                        }
                        else
                        {
                            Vector3 mouseAimFlightTarget = MouseAimFlight.GetMouseAimTarget;
                            ray = new Ray(camera.transform.position, mouseAimFlightTarget);
                        }

                        float maxAimRange = targetAcquired ? (targetPosition - ray.origin).magnitude : maxTargetingRange;
                        if (Physics.Raycast(ray, out RaycastHit hit, maxTargetingRange, layerMask1))
                        {
                            KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                            Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();

                            if (p != null && p.vessel != null && p.vessel == vessel) //aim through self vessel if occluding mouseray
                            {
                                targetPosition = ray.origin + ray.direction * maxAimRange;
                            }
                            else
                            {
                                targetPosition = hit.point;
                            }
                            if (p != null && p.rb != null && p.vessel != null)
                            {
                                foundTarget = true;
                                targetVelocity = p.rb.velocity;
                                targetAcceleration = p.vessel.acceleration_immediate;
                                targetIsLandedOrSplashed = p.vessel.LandedOrSplashed;
                            }
                        }
                        else
                        {
                            if (visualTargetVessel != null && visualTargetVessel.loaded)
                            {
                                foundTarget = true;
                                if (!targetCOM && visualTargetPart != null)
                                {
                                    targetPosition = ray.origin + ray.direction * Vector3.Distance(visualTargetPart.transform.position, ray.origin);
                                    targetVelocity = visualTargetPart.rb.velocity;
                                    targetAcceleration = visualTargetPart.vessel.acceleration;
                                    targetIsLandedOrSplashed = visualTargetPart.vessel.LandedOrSplashed;
                                }
                                else
                                {
                                    targetPosition = ray.origin + ray.direction * Vector3.Distance(visualTargetVessel.transform.position, ray.origin);
                                    targetVelocity = visualTargetVessel.rb_velocity;
                                    targetAcceleration = visualTargetVessel.acceleration;
                                    targetIsLandedOrSplashed = visualTargetVessel.LandedOrSplashed;
                                }
                            }
                            else
                            {
                                targetPosition = ray.origin + ray.direction * maxAimRange;
                            }
                        }
                    }
                    else if (!targetAcquired && (weaponManager == null || !weaponManager.staleTarget))
                    {
                        float maxAimRange = targetAcquired ? (targetPosition - fireTransform.position).magnitude : maxTargetingRange;
                        targetPosition = fireTransform.position + fireTransform.forward * maxAimRange; // For fixed weapons, aim straight ahead (needed for targetDistance below for the trajectory sim) if no current target.
                    }
                    if (!foundTarget)
                    {
                        targetVelocity = -BDKrakensbane.FrameVelocityV3f; // Stationary targets' rigid bodies are being moved opposite to the Krakensbane frame velocity.
                        targetAcceleration = Vector3.zero;
                        targetIsLandedOrSplashed = true;
                    }
                    finalTarget = targetPosition; // In case aim assist and AI control is off.
                }
                if (BDArmorySettings.BULLET_WATER_DRAG)
                {
                    if ((FlightGlobals.getAltitudeAtPos(targetPosition) < 0) && (FlightGlobals.getAltitudeAtPos(targetPosition) + targetRadius > 0)) //vessel not completely submerged
                    {
                        if (caliber < 75)
                        {
                            targetPosition += VectorUtils.GetUpDirection(targetPosition) * Mathf.Abs(FlightGlobals.getAltitudeAtPos(targetPosition)); //set targetposition to surface directly above target
                        }
                    }
                }
                //aim assist
                Vector3 originalTarget = targetPosition;
                var supported = targetIsLandedOrSplashed || targetAcceleration.sqrMagnitude == 0; // Assume non-accelerating targets are "supported".
                if (!manualAiming)
                {
                    // Correct for the FI, which hasn't run yet, but does before visuals are next shown. This should synchronise the target's position and velocity with the bullet at the start of the next frame.
                    targetPosition = AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, Time.fixedDeltaTime);
                    targetVelocity += Time.fixedDeltaTime * targetAcceleration;

                    // Correct for unity integration system
                    // Unity uses semi-implicit euler method during fixed updates. This means the velocity is updated first, and then position.
                    // This creates consistent errors that the following velocity offset compensates for.
                    targetVelocity += 0.5f * Time.fixedDeltaTime * (supported ? targetAcceleration : targetAcceleration - (Vector3)FlightGlobals.getGeeForceAtPosition(targetPosition));
                    // There is no equivalent correction for the weapon part due to our specific placement of the bullet with the given velocity.
                }
                targetDistance = Vector3.Distance(targetPosition, fireTransform.parent.position);
                origTargetDistance = targetDistance;

                RunTrajectorySimulation(); // Run the trajectory sim after picking a target for this frame, otherwise a bunch of stuff is reset between frames. This is required for the rocket aiming.

                if (BDArmorySettings.AIM_ASSIST || aiControlled)
                {
                    switch (eWeaponType)
                    {
                        case WeaponTypes.Ballistic: //Gun targeting
                            {
                                /* There are 3 main situations that the aiming code needs to satisfy:
									- Static: where the two vessels are supported on or near the surface of Kerbin.
										In this situation, there is no effect from velocity, acceleration or Krakensbane, just the variation in gravity over the path of the bullet.
										VM gives perfectly stationary vessels, and the kinematic smoothing should give sufficiently static landed/splashed vessels.
										The numerical integrator for the target is irrelevant here due to the target being static.
										This situation is useful for getting the initial setup of the bullets and their trajectories (e.g., bullet drop, iTime) correct. Bullets should closely follow the debug lines.
									- Translational: where variation in gravity is negligible, e.g., at the limit of Kerbin's SoI.
										In this situation, the solution should be analytically solvable for constantly accelerating vessels once changes in the Krakensbane velocity offloading are accounted for.
										This situation is useful for getting the velocity corrections to finalTarget correct (e.g., part.rb.velocity)'b'.
										The solution from numerical simulation should agree closely with the analytic solution here — can be used to partially validate the numerical accuracy of KSP/Unity (only partially since the simplicity of the Hamiltonian may mean that the integrators appear more accurate here than they would normally be).
										This situation also covers most short-range conditions during in atmosphere dogfights, which generally are sufficiently covered by the analytic solution.
										FIXME There still seems to be a small offset for the non-active vessels (e.g., firing from the active vessel, then switching to the targetted vessel shortly before the bullets arrive shows an offset of ~1m.)
									- Orbital (>100km): where varying gravity, vessel acceleration and Krakensbane must all be accounted for.
										In this situation, the numerical integrators for bullets and the target need to closely approximate their actual trajectories.
										This situation is useful for making sure that the way finalTarget is calculated works in this geometry (i.e., is combining bulletDrop and part.rb.velocity to get the firing direction sufficient or are they just the lowest order terms when in orbit?) and dealing with KSP's orbital drift compensation (separate gravitational vs local acceleration).
										For <100km orbits, corrections due to Krakensbane may need adjusting.
								*/
                                Vector3 bulletInitialPosition, relativePosition, bulletEffectiveVelocity, relativeVelocity, bulletAcceleration, relativeAcceleration, targetPredictedPosition, bulletDropOffset, bulletInitialVelocityDelta;
                                float timeToCPA;
                                Vector3 firingDirection, lastFiringDirection;

                                var timeGap = GetTimeGap();
                                var iTime = timeSinceFired - timeGap >= TimeWarp.fixedDeltaTime ?
                                    TimeWarp.fixedDeltaTime :
                                    TimeWarp.fixedDeltaTime - (TimeWarp.fixedDeltaTime + timeGap - timeSinceFired) % TimeWarp.fixedDeltaTime; // This is the iTime correction for the frame that the gun will actually fire on.
                                if (iTime < 1e-4f) iTime = TimeWarp.fixedDeltaTime; // Avoid jitter by aliasing iTime < 1e-4 to TimeWarp.fixedDeltaTime for the frame after.
                                var firePosition = AIUtils.PredictPosition(fireTransforms[0].position, smoothedPartVelocity, smoothedPartAcceleration, Time.fixedDeltaTime); // Position of the end of the barrel at the start of the next frame.

                                firingDirection = smoothedRelativeFinalTarget.At(Time.fixedDeltaTime).normalized; // Estimate of the current firing direction for this frame based on the previous frames.
                                bulletAcceleration = bulletDrop ? (Vector3)FlightGlobals.getGeeForceAtPosition(firePosition) : Vector3.zero; // Acceleration at the start point.
                                bulletInitialPosition = AIUtils.PredictPosition(firePosition, baseBulletVelocity * firingDirection, bulletAcceleration, iTime); // Bullets are initially placed up to 1 frame ahead (iTime).
                                if (!BDKrakensbane.IsActive) bulletInitialPosition += TimeWarp.fixedDeltaTime * part.rb.velocity; // If Krakensbane isn't active, bullets get an additional shift by this amount.
                                bulletInitialVelocityDelta = iTime * bulletAcceleration;

                                // Check whether we should use the analytic solution or the numeric one. These initial values don't affect the numeric solution.
                                if (lastTimeToCPA >= 0)
                                {
                                    timeToCPA = lastTimeToCPA + deltaTimeToCPA; // Use the previous timeToCPA adjusted for the previous delta as a decent initial estimate.
                                }
                                else
                                {
                                    relativePosition = targetPosition - bulletInitialPosition;
                                    relativeVelocity = targetVelocity - (smoothedPartVelocity + baseBulletVelocity * firingDirection);
                                    timeToCPA = BDAMath.Sqrt(relativePosition.sqrMagnitude / relativeVelocity.sqrMagnitude); // Rough initial estimate.
                                }
                                targetPredictedPosition = AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, timeToCPA);
                                bulletAcceleration = bulletDrop ? (Vector3)FlightGlobals.getGeeForceAtPosition((bulletInitialPosition + targetPredictedPosition) / 2f) : Vector3.zero; // Average acceleration over the bullet's path. Drag is ignored.
                                var offTarget = Vector3.Dot(firingDirection, fireTransforms[0].forward) < 0.985f; // More than 10° off-target. This should cover most cases, even when not using the analytic solution.
                                bool useAnalyticAiming = timeToCPA * bulletAcceleration.magnitude < 100f; // TODO This condition could be improved to better cover all situations where the analytic solution isn't sufficiently accurate.
                                if (offTarget || useAnalyticAiming) // The gun is significantly off-target or we want the optimum analytic solution => perform a loop based on an "optimal" firing direction.
                                {
                                    // For artillery (if it ever gets implemented), TimeToCPA needs to use the furthest time, not the closest (AIUtils.CPAType).
                                    int count = 0;
                                    // This loop is correct for situation 1.
                                    // It also appears to be correct for situation 2, but accuracy is different depending on which vessel has focus.
                                    // - From the target's perspective, the shots are quite accurate.
                                    // - From the shooter's perspective, the shots are often wide, but not consistently.
                                    do
                                    {
                                        // Note: Bullets are initially placed up to 1 frame ahead (iTime) to compensate for where they would move to during this physics frame.
                                        //       Also, we have already adjusted the target's position and velocity for where it ought to be next frame.
                                        //       Thus, the following calculations are based on the state at the start of the next frame.
                                        //       It is also using the firing direction from the initial estimate, so it is effectively always performing 2 iterations (1 initial and 1 here).
                                        lastFiringDirection = firingDirection;
                                        bulletEffectiveVelocity = smoothedPartVelocity + baseBulletVelocity * firingDirection + bulletInitialVelocityDelta;
                                        bulletInitialPosition = firePosition + iTime * baseBulletVelocity * firingDirection;
                                        if (!BDKrakensbane.IsActive) bulletInitialPosition += TimeWarp.fixedDeltaTime * part.rb.velocity; // If Krakensbane isn't active, bullets get an additional shift by this amount.
                                        bulletAcceleration = bulletDrop ? (Vector3)FlightGlobals.getGeeForceAtPosition((bulletInitialPosition + targetPredictedPosition) / 2f) : Vector3.zero; // Drag is ignored.
                                        relativePosition = targetPosition - bulletInitialPosition;
                                        relativeVelocity = targetVelocity - bulletEffectiveVelocity;
                                        relativeAcceleration = targetAcceleration - bulletAcceleration;
                                        timeToCPA = AIUtils.TimeToCPA(relativePosition, relativeVelocity, relativeAcceleration, maxTargetingRange / bulletEffectiveVelocity.magnitude); // time to CPA from the next frame (where the bullet starts).
                                        targetPredictedPosition = AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, timeToCPA);
                                        bulletDropOffset = -0.5f * (timeToCPA + iTime) * (timeToCPA + iTime) * bulletAcceleration; // The bullet starts on the next frame so it uses timeToCPA+iTime, other uses use timeToCPA+Time.fixedDeltaTime.
                                        finalTarget = targetPredictedPosition + bulletDropOffset - (timeToCPA + Time.fixedDeltaTime) * smoothedPartVelocity;
                                        firingDirection = (finalTarget - fireTransforms[0].position).normalized;
                                    } while (++count < 10 && Vector3.Dot(lastFiringDirection, firingDirection) < 0.9998f); // ~1° margin of error is sufficient to prevent premature firing (usually)
                                }
                                else // Reasonably on-target and the analytic solution isn't accurate enough.
                                {
                                    // Note: we can't base this on the firing direction from the analytic solution as the single step is not enough to converge sufficiently accurately from the analytic solution to the correct solution.
                                    // Instead, we must rely on the convergence over time based on our estimate from the previous frame (which is very quick unless near the limits of the weapon).
                                    // This is correct for situations 1 and 2.
                                    // It suffers the same accuracy noise as the analytic solution.
                                    // However, there seems to be some inconsistencies between the analytic and numeric solutions when the CPA distance is non-zero (t<0 or t>max) or when the solver switches between roots of the cubic. Fortunately, these situations are generally only for extreme situations.
                                    // Also, the numeric solution is giving strangely discrete values initially.
                                    // For situation 3, the solution is not quite right, but is fairly good for high accelerations when within 5-15km.

                                    bulletEffectiveVelocity = smoothedPartVelocity + baseBulletVelocity * firingDirection;

                                    var (simBulletCPA, simTargetCPA, simTimeToCPA) = BallisticTrajectoryClosestApproachSimulation(
                                        bulletInitialPosition,
                                        bulletEffectiveVelocity + bulletInitialVelocityDelta,
                                        bulletDrop,
                                        targetPosition,
                                        targetVelocity,
                                        targetAcceleration,
                                        supported,
                                        BDArmorySettings.BALLISTIC_TRAJECTORY_SIMULATION_MULTIPLIER * Time.fixedDeltaTime,
                                        maxTargetingRange / bulletEffectiveVelocity.magnitude,
                                        AIUtils.CPAType.Earliest
                                    );
                                    timeToCPA = simTimeToCPA;
                                    bulletDropOffset = AIUtils.PredictPosition(bulletInitialPosition, bulletEffectiveVelocity, Vector3.zero, timeToCPA) - simBulletCPA; // Bullet drop is the acceleration component.
                                    finalTarget = simTargetCPA + bulletDropOffset - (timeToCPA + Time.fixedDeltaTime) * smoothedPartVelocity;
                                }
                                if (lastTimeToCPA >= 0)
                                {
                                    deltaTimeToCPA = timeToCPA - lastTimeToCPA;
                                    smoothedRelativeFinalTarget.Update(finalTarget - fireTransforms[0].position);
                                }
                                else
                                {
                                    deltaTimeToCPA = 0;
                                    smoothedRelativeFinalTarget.Reset(finalTarget - fireTransforms[0].position);
                                }
                                lastTimeToCPA = timeToCPA;
                                bulletTimeToCPA = timeToCPA;
                                targetDistance = Vector3.Distance(finalTarget, firePosition);

                                if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DEBUG_WEAPONS)
                                {
                                    // Debug.Log($"DEBUG {count} iterations for convergence in aiming loop");
                                    debugTargetPosition = targetPosition;
                                    debugLastTargetPosition = debugTargetPosition;
                                    debugRelVelAdj = (targetVelocity - smoothedPartVelocity) * timeToCPA;
                                    debugAccAdj = 0.5f * targetAcceleration * timeToCPA * timeToCPA;
                                    debugGravAdj = bulletDropOffset;
                                    // var missDistance = AIUtils.PredictPosition(relativePosition, bulletRelativeVelocity, bulletRelativeAcceleration, timeToCPA);
                                    // if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log("DEBUG δt: " + timeToCPA + ", miss: " + missDistance + ", bullet drop: " + bulletDropOffset + ", final: " + finalTarget + ", target: " + targetPosition + ", " + targetVelocity + ", " + targetAcceleration + ", distance: " + targetDistance);
                                }
                            }
                            break;
                        case WeaponTypes.Rocket: //Rocket targeting
                            {
                                finalTarget = AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, predictedFlightTime) + trajectoryOffset;
                                targetDistance = Mathf.Clamp(Vector3.Distance(targetPosition, fireTransform.parent.position), 0, maxTargetingRange);
                            }
                            break;
                    }
                }
                //airdetonation
                if (eFuzeType == BulletFuzeTypes.Timed || eFuzeType == BulletFuzeTypes.Flak)
                {
                    if (targetAcquired)
                    {
                        defaultDetonationRange = targetDistance;// adds variable time fuze if/when proximity fuzes fail
                    }
                    else
                    {
                        defaultDetonationRange = maxEffectiveDistance; //airburst at max range
                    }
                }
                fixedLeadOffset = originalTarget - finalTarget; //for aiming fixed guns to moving target
                finalAimTarget = finalTarget;
                staleFinalAimTarget = finalAimTarget;
                staleTargetVelocity = targetVelocity + BDKrakensbane.FrameVelocityV3f;
                staleTargetAcceleration = targetAcceleration;
                stalePartVelocity = smoothedPartVelocity + BDKrakensbane.FrameVelocityV3f;
                staleGoodTargetTime = Time.time;
            }

            //final turret aiming
            if (slaved && !targetAcquired) return;
            if (turret)
            {
                bool origSmooth = turret.smoothRotation;
                if (aiControlled || slaved)
                {
                    turret.smoothRotation = false;
                }
                turret.AimToTarget(finalAimTarget); //no aimbot turrets when target out of sight
                turret.smoothRotation = origSmooth;
            }
        }

        /// <summary>
        /// Run a trajectory simulation in the current frame.
        /// 
        /// Note: Since this is running in the current frame, for moving targets the trajectory appears to be off, but it's not.
        /// By the time the projectile arrives at the target, the target has moved to that point in the trajectory.
        /// </summary>
        /// For bullets, this isn't used for aiming, only for visuals. But for rockets it is.
        public void RunTrajectorySimulation()
        {
            if ((eWeaponType == WeaponTypes.Rocket && ((BDArmorySettings.AIM_ASSIST && BDArmorySettings.DRAW_AIMERS && vessel.isActiveVessel) || aiControlled)) ||
            (BDArmorySettings.AIM_ASSIST && BDArmorySettings.DRAW_AIMERS &&
            (BDArmorySettings.DEBUG_LINES || (vessel && vessel.isActiveVessel && !aiControlled && !MapView.MapIsEnabled && !pointingAtSelf && eWeaponType != WeaponTypes.Rocket))))
            {
                Transform fireTransform = fireTransforms[0];

                if (eWeaponType == WeaponTypes.Rocket && rocketPod)
                {
                    fireTransform = rockets[0].parent; // support for legacy RLs
                }

                if ((eWeaponType == WeaponTypes.Laser || (eWeaponType == WeaponTypes.Ballistic && !bulletDrop)) && BDArmorySettings.AIM_ASSIST && BDArmorySettings.DRAW_AIMERS)
                {
                    Ray ray = new Ray(fireTransform.position, fireTransform.forward);
                    RaycastHit rayHit;
                    if (Physics.Raycast(ray, out rayHit, maxTargetingRange, layerMask1))
                    {
                        bulletPrediction = rayHit.point;
                    }
                    else
                    {
                        bulletPrediction = ray.GetPoint(maxTargetingRange);
                    }
                    pointingAtPosition = ray.GetPoint(maxTargetingRange);
                }
                else if (eWeaponType == WeaponTypes.Ballistic && BDArmorySettings.AIM_ASSIST && BDArmorySettings.DRAW_AIMERS)
                {
                    var timeGap = GetTimeGap();
                    var iTime = timeSinceFired - timeGap >= TimeWarp.fixedDeltaTime ?
                        TimeWarp.fixedDeltaTime :
                        TimeWarp.fixedDeltaTime - (TimeWarp.fixedDeltaTime + timeGap - timeSinceFired) % TimeWarp.fixedDeltaTime; // This is the iTime correction for the frame that the gun will actually fire on.
                    if (iTime < 1e-4f) iTime = TimeWarp.fixedDeltaTime; // Avoid jitter by aliasing iTime < 1e-4 to TimeWarp.fixedDeltaTime for the frame after.
                    var firePosition = AIUtils.PredictPosition(fireTransform.position, part.rb.velocity, vessel.acceleration_immediate, Time.fixedDeltaTime); // Position of the end of the barrel at the start of the next frame.
                    var bulletAcceleration = bulletDrop ? (Vector3)FlightGlobals.getGeeForceAtPosition(firePosition) : Vector3.zero; // Acceleration at the start point.
                    var simCurrPos = AIUtils.PredictPosition(firePosition, baseBulletVelocity * fireTransform.forward, bulletAcceleration, iTime); // Bullets are initially placed up to 1 frame ahead (iTime).
                    if (!BDKrakensbane.IsActive) simCurrPos += TimeWarp.fixedDeltaTime * part.rb.velocity; // If Krakensbane isn't active, bullets get an additional shift by this amount.

                    if (Physics.Raycast(new Ray(firePosition, simCurrPos - firePosition), out RaycastHit hit, (simCurrPos - firePosition).magnitude, layerMask1)) // Check between the barrel and the point the bullet appears.
                    {
                        bulletPrediction = hit.point;
                    }
                    else
                    {
                        Vector3 simVelocity = part.rb.velocity + BDKrakensbane.FrameVelocityV3f + baseBulletVelocity * fireTransform.forward + iTime * bulletAcceleration;
                        var simDeltaTime = Mathf.Clamp(Mathf.Min(maxTargetingRange, Mathf.Max(targetDistance, origTargetDistance)) / simVelocity.magnitude / 2f, Time.fixedDeltaTime, Time.fixedDeltaTime * BDArmorySettings.BALLISTIC_TRAJECTORY_SIMULATION_MULTIPLIER); // With leap-frog, we can use a higher time-step and still get better accuracy than with Euler variants (what was used before). Always take at least 2 steps though.
                        BallisticTrajectorySimulation(ref simCurrPos, simVelocity, maxTargetingRange / baseBulletVelocity / Vector3.Dot((targetPosition - simCurrPos).normalized, fireTransform.forward), simDeltaTime, 0, FlightGlobals.getAltitudeAtPos(targetPosition) < 0);
                        bulletPrediction = simCurrPos;
                    }
                }
                else if (eWeaponType == WeaponTypes.Rocket)
                {
                    float simTime = 0;
                    float maxTime = rocketInfo.lifeTime;
                    Vector3 startPos = fireTransform.position;
                    Vector3 simVelocity = smoothedPartVelocity; // Use the velocity in the local velocity frame.
                    Vector3 simCurrPos = startPos;
                    Vector3 rocketDirection = fireTransform.forward;
                    Quaternion simRotation = fireTransform.rotation;
                    Vector3 simInvInitialDirection = Quaternion.Inverse(simRotation) * rocketDirection;
                    Vector3 closestPointOfApproach = simCurrPos;
                    float timeToCPA;
                    Vector3 targetPredictedPosition;
                    bool hitDetected = false;
                    float atmosMultiplier = Mathf.Clamp01(2.5f * (float)FlightGlobals.getAtmDensity(vessel.staticPressurekPa, vessel.externalTemperature, vessel.mainBody));
                    bool slaved = turret && weaponManager && (weaponManager.slavingTurrets || weaponManager.guardMode);
                    bool inOrbit = vessel.InOrbit(); // When in orbit, ignore raycasts

                    if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DRAW_AIMERS)
                    {
                        trajectoryPoints ??= [];
                        trajectoryPoints.Clear();
                        trajectoryPoints.Add(simCurrPos);
                    }

                    // Initial frame (no forces or collision/proximity detection)
                    float simDeltaTime = Time.fixedDeltaTime;
                    simCurrPos += simDeltaTime * simVelocity;
                    simTime += simDeltaTime;

                    while (true)
                    {
                        // Update acceleration for this frame. Note: rockets don't get gravity applied when in non-rotating reference frames.
                        Vector3 simAcceleration = FlightGlobals.RefFrameIsRotating ? FlightGlobals.getGeeForceAtPosition(simCurrPos) : Vector3.zero;
                        if (simTime <= thrustTime) simAcceleration += thrust / rocketMass * rocketDirection;
                        Vector3 simVelocityActual = simVelocity + BDKrakensbane.FrameVelocityV3f; // We want the actual velocity for aero-stabilisation.
                        if (BDArmorySettings.BULLET_WATER_DRAG)
                        {
                            if (FlightGlobals.getAltitudeAtPos(simCurrPos) < 0)
                                simAcceleration += -(0.5f * 1 * simVelocityActual.sqrMagnitude * 0.5f * (Mathf.PI * caliber * caliber * 0.25f / 1000000)) / rocketMass * rocketDirection;//this is going to throw off aiming code, but you aren't going to hit anything with rockets underwater anyway
                        }

                        if (atmosMultiplier > 0)
                        {
                            // Rotation (aero stabilize).
                            var atmosFactor = atmosMultiplier * 0.5f * 0.012f * simVelocityActual.sqrMagnitude * simDeltaTime;
                            simRotation = Quaternion.RotateTowards(simRotation, Quaternion.LookRotation(simVelocityActual, fireTransform.up), atmosFactor); // Using Vector3.RotateTowards isn't accurate enough.
                            rocketDirection = simRotation * simInvInitialDirection;
                        }

                        // No longer thrusting, finish up with a ballistic sim.
                        if (simTime > thrustTime)
                        {
                            targetPredictedPosition = AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, simTime);
                            var timeRemaining = maxTime - simTime;
                            timeToCPA = AIUtils.TimeToCPA(targetPredictedPosition - simCurrPos, targetVelocity - simVelocity, targetAcceleration - simAcceleration, timeRemaining); // For aiming, we want the closest approach to refine our aim.
                            closestPointOfApproach = AIUtils.PredictPosition(simCurrPos, simVelocity, simAcceleration, timeToCPA);
                            if (!hitDetected) bulletPrediction = closestPointOfApproach;
                            if (BDArmorySettings.AIM_ASSIST && BDArmorySettings.DRAW_AIMERS && !hitDetected)
                            {
                                if (FlightGlobals.RefFrameIsRotating)
                                {
                                    simDeltaTime = Mathf.Clamp(Mathf.Min((closestPointOfApproach - simCurrPos).magnitude / simVelocity.magnitude, timeRemaining) / 8f, Time.fixedDeltaTime, Time.fixedDeltaTime * BDArmorySettings.BALLISTIC_TRAJECTORY_SIMULATION_MULTIPLIER); // Use 8 steps for better visuals.
                                    BallisticTrajectorySimulation(ref simCurrPos, simVelocity, timeRemaining, simDeltaTime, simTime, FlightGlobals.getAltitudeAtPos(targetPosition) < 0, resetTrajectoryPoints: false); // For visuals, we want the trajectory sim with collision detection. Note: this is done after to avoid messing with simCurrPos.
                                    bulletPrediction = simCurrPos;
                                }
                                else
                                {
                                    // simAcceleration is zero, so, it's just a straight line to the CPA.
                                    if (Physics.Raycast(new Ray(simCurrPos, simVelocity), out RaycastHit hit, simVelocity.magnitude * timeRemaining, layerMask1) && hit.collider != null && hit.collider.gameObject != null && hit.collider.gameObject.GetComponentInParent<Part>() != part) // Any hit other than the part firing the rocket.
                                    { bulletPrediction = hit.point; }
                                }
                            }
                            simTime += timeToCPA;
                            break;
                        }

                        // Symplectic Euler velocity update. (Unity's integrator is closer to Symplectic Euler than LeapFrog.)
                        simVelocity += simDeltaTime * simAcceleration;

                        // Check for collisions within the next update. (Note: this is only relevant against static objects and isn't used for aiming.)
                        if (!inOrbit && !hitDetected && !aiControlled && !slaved)
                        {
                            if (Physics.Raycast(simCurrPos, simVelocity, out RaycastHit hit, simDeltaTime * simVelocity.magnitude, layerMask1) && hit.collider != null && hit.collider.gameObject != null && hit.collider.gameObject.GetComponentInParent<Part>() != part) // Any hit other than the part firing the rocket.
                            {
                                bulletPrediction = hit.point;
                                hitDetected = true;
                                Part hitPart;
                                KerbalEVA hitEVA;
                                try
                                {
                                    hitPart = hit.collider.gameObject.GetComponentInParent<Part>();
                                    hitEVA = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                                    if (hitEVA != null)
                                    {
                                        hitPart = hitEVA.part;
                                    }
                                    if (hitPart == null) autoFire = false;
                                }
                                catch (NullReferenceException e)
                                {
                                    Debug.Log("[BDArmory.ModuleWeapon]:NullReferenceException for Ballistic Hit: " + e.Message);
                                }
                            }
                            // else if (FlightGlobals.getAltitudeAtPos(simCurrPos) < 0) // Note: this prevents aiming below sea-level. 
                            // {
                            //    bulletPrediction = simCurrPos;
                            //   break;
                            // }
                        }

                        // Check for closest approach within the next update.
                        targetPredictedPosition = AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, simTime);
                        var targetPredictedVelocity = targetVelocity + simTime * targetAcceleration;
                        timeToCPA = AIUtils.TimeToCPA(targetPredictedPosition - simCurrPos, targetPredictedVelocity - simVelocity, targetAcceleration - simAcceleration, simDeltaTime);
                        if (timeToCPA < 0) // No longer approaching.
                        {
                            if (!hitDetected) bulletPrediction = closestPointOfApproach;
                            break; // No longer approaching.
                        }
                        else if (timeToCPA < simDeltaTime) // CPA within the next frame.
                        {
                            closestPointOfApproach = AIUtils.PredictPosition(simCurrPos, simVelocity, simAcceleration, timeToCPA);
                            if (!hitDetected) bulletPrediction = closestPointOfApproach;
                            simTime += timeToCPA;
                            break;
                        }
                        else // CPA beyond the next frame.
                        {
                            closestPointOfApproach = simCurrPos;
                        }

                        // Symplectic Euler position update. (Unity's integrator is closer to Symplectic Euler than LeapFrog.)
                        simCurrPos += simDeltaTime * simVelocity;

                        // Update the current sim time.
                        simTime += simDeltaTime;

                        if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DRAW_AIMERS && !hitDetected)
                            trajectoryPoints.Add(simCurrPos);

                        // Book-keeping and max time checks.
                        if (simTime > maxTime)
                        {
                            if (!hitDetected) bulletPrediction = simCurrPos;
                            break;
                        }
                    }

                    // Visuals
                    if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DRAW_AIMERS)
                    {
                        trajectoryPoints.Add(bulletPrediction);
                        // Debug.Log($"DEBUG traj d {simTime}s Δ {trajectoryPoints.Count - 1}: {(trajectoryPoints[trajectoryPoints.Count - 1] - trajectoryPoints[trajectoryPoints.Count - 2]).magnitude}");
                        trajectoryRenderer = gameObject.GetComponent<LineRenderer>();
                        if (trajectoryRenderer == null)
                        {
                            trajectoryRenderer = gameObject.AddComponent<LineRenderer>();
                            trajectoryRenderer.startWidth = .1f;
                            trajectoryRenderer.endWidth = .1f;
                        }
                        trajectoryRenderer.enabled = true;
                        trajectoryRenderer.positionCount = trajectoryPoints.Count;
                        int i = 0;
                        var offset = BDKrakensbane.IsActive ? Vector3.zero : AIUtils.PredictPosition(Vector3.zero, vessel.Velocity(), vessel.acceleration, Time.fixedDeltaTime);
                        using var point = trajectoryPoints.GetEnumerator();
                        while (point.MoveNext())
                        {
                            trajectoryRenderer.SetPosition(i, point.Current + offset);
                            ++i;
                        }
                    }

                    predictedFlightTime = simTime;
                    Vector3 pointingPos = startPos + targetDistance * fireTransform.forward;
                    if (!FlightGlobals.RefFrameIsRotating) // Compensate for gravity in non-rotating reference frames.
                        pointingPos -= 0.5f * predictedFlightTime * predictedFlightTime * (Vector3)FlightGlobals.getGeeForceAtPosition(pointingPos);
                    trajectoryOffset = pointingPos - closestPointOfApproach;
                }
            }
        }

        public enum SimulationStage { Normal, Refining, Final };
        /// <summary>
        /// Use the leapfrog numerical integrator for a ballistic trajectory simulation under the influence of just gravity.
        /// The leapfrog integrator is a second-order symplectic method.
        /// 
        /// Note: Use this to see the trajectory with collision detection, but use BallisticTrajectoryClosestApproachSimulation instead for targeting purposes.
        /// <param name="position"></param>
        /// <param name="velocity"></param>
        /// <param name="maxTime"></param>
        /// <param name="timeStep"></param>
        /// <param name="startTime"></param>
        /// <param name="ignoreWater"></param>
        /// <param name="stage"></param>
        /// <param name="resetTrajectoryPoints"></param>
        /// <returns></returns>
        public float BallisticTrajectorySimulation(ref Vector3 position, Vector3 velocity, float maxTime, float timeStep, float startTime = 0, bool ignoreWater = false, SimulationStage stage = SimulationStage.Normal, bool resetTrajectoryPoints = true)
        {
            float elapsedTime = 0f;
            var startPosition = position;
            if (FlightGlobals.getAltitudeAtPos(position) < 0) ignoreWater = true;
            bool inOrbit = vessel.InOrbit();
            var gravity = (Vector3)FlightGlobals.getGeeForceAtPosition(position);
            velocity += 0.5f * timeStep * gravity; // Boot-strap velocity calculation.
            Ray ray = new();
            RaycastHit hit = new();
            if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DRAW_AIMERS && stage == SimulationStage.Normal)
            {
                trajectoryPoints ??= [];
                if (resetTrajectoryPoints)
                    trajectoryPoints.Clear();
                if (trajectoryPoints.Count == 0)
                    trajectoryPoints.Add(fireTransforms[0].position);
                trajectoryPoints.Add(position);
            }
            while (elapsedTime < maxTime)
            {
                ray.origin = position;
                ray.direction = velocity;
                var deltaPosition = timeStep * velocity;
                var altitude = FlightGlobals.getAltitudeAtPos(position + deltaPosition);
                if (!inOrbit // Raycast detection isn't meaningful in orbit as it assumes stationary targets. Use CPA instead.
                    && Physics.Raycast(ray, out hit, deltaPosition.magnitude, layerMask1) && hit.collider != null && hit.collider.gameObject != null && hit.collider.gameObject.GetComponentInParent<Part>() != part
                    || (!ignoreWater && altitude < 0) // Underwater
                    || (stage == SimulationStage.Normal && elapsedTime + timeStep > maxTime) // Out of time
                )
                {
                    switch (stage)
                    {
                        case SimulationStage.Normal:
                            {
                                if (elapsedTime + timeStep > maxTime) // Final time amount.
                                {
                                    velocity -= 0.5f * timeStep * gravity; // Correction to final velocity.
                                    var finalTime = BallisticTrajectorySimulation(ref position, velocity, maxTime - elapsedTime, (maxTime - elapsedTime) / 4f, startTime + elapsedTime, ignoreWater, SimulationStage.Final, false);
                                    elapsedTime += finalTime;
                                }
                                else
                                    goto case SimulationStage.Refining;
                                break;
                            }
                        case SimulationStage.Refining: // Perform a more accurate final step for the collision.
                            {
                                velocity -= 0.5f * timeStep * gravity; // Correction to final velocity.
                                var finalTime = BallisticTrajectorySimulation(ref position, velocity, timeStep, timeStep / 4f, startTime + elapsedTime, ignoreWater, timeStep > 5f * Time.fixedDeltaTime ? SimulationStage.Refining : SimulationStage.Final, false);
                                elapsedTime += finalTime;
                                break;
                            }
                        case SimulationStage.Final:
                            {
                                if (!ignoreWater && altitude < 0) // Underwater
                                {
                                    var currentAltitude = FlightGlobals.getAltitudeAtPos(position);
                                    timeStep *= currentAltitude / (currentAltitude - altitude);
                                    elapsedTime += timeStep;
                                    position += timeStep * velocity;
                                    // Debug.Log("DEBUG breaking trajectory sim due to water at " + position.ToString("F6") + " at altitude " + FlightGlobals.getAltitudeAtPos(position));
                                }
                                else if (!inOrbit) // Collision
                                {
                                    elapsedTime += (hit.point - position).magnitude / velocity.magnitude;
                                    position = hit.point;
                                    if (hit.collider != null && hit.collider.gameObject != null)
                                    {
                                        Part hitPart;
                                        KerbalEVA hitEVA;
                                        try
                                        {
                                            hitPart = hit.collider.gameObject.GetComponentInParent<Part>();
                                            hitEVA = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                                            if (hitEVA != null)
                                            {
                                                hitPart = hitEVA.part;
                                            }
                                            if (hitPart == null) autoFire = false;
                                        }
                                        catch (NullReferenceException e)
                                        {
                                            Debug.Log("[BDArmory.ModuleWeapon]:NullReferenceException for Ballistic Hit: " + e.Message);
                                        }
                                    }
                                }
                                break;
                            }
                    }
                    break;
                }
                { // CPA check
                    var timeToCPA = AIUtils.TimeToCPA(
                        AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, elapsedTime) - position,
                        targetVelocity + elapsedTime * targetAcceleration - velocity,
                        targetAcceleration - gravity,
                        timeStep);
                    if (timeToCPA < timeStep)
                    {
                        position = AIUtils.PredictPosition(position, velocity, gravity, timeToCPA);
                        elapsedTime += timeToCPA;
                        break;
                    }
                }
                position += deltaPosition;
                gravity = (Vector3)FlightGlobals.getGeeForceAtPosition(position);
                velocity += timeStep * gravity;
                elapsedTime += timeStep;
                if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DRAW_AIMERS)
                {
                    trajectoryPoints.Add(position);
                }
            }
            if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DRAW_AIMERS && resetTrajectoryPoints)
            {
                trajectoryPoints.Add(position);
                trajectoryRenderer = gameObject.GetComponent<LineRenderer>();
                if (trajectoryRenderer == null)
                {
                    trajectoryRenderer = gameObject.AddComponent<LineRenderer>();
                    trajectoryRenderer.startWidth = .1f;
                    trajectoryRenderer.endWidth = .1f;
                }
                trajectoryRenderer.enabled = true;
                trajectoryRenderer.positionCount = trajectoryPoints.Count;
                int i = 0;
                var offset = BDKrakensbane.IsActive ? Vector3.zero : AIUtils.PredictPosition(Vector3.zero, vessel.Velocity(), vessel.acceleration, Time.fixedDeltaTime);
                using var point = trajectoryPoints.GetEnumerator();
                while (point.MoveNext())
                {
                    trajectoryRenderer.SetPosition(i, point.Current + offset);
                    ++i;
                }
            }
            return elapsedTime;
        }

        /// <summary>
        /// Solve the closest time to CPA via simulation for ballistic projectiles over long distances to account for varying gravity.
        /// 
        /// Both the bullet and target positions are integrated with leap-frog.
        /// This is consistent with how bullets are moved in PooledBullet.cs and, since it is second-order, is more accurate for larger timesteps than semi-implicit Euler (which is what Unity appears to be using).
        /// </summary>
        /// <param name="position">The bullet's position.</param>
        /// <param name="velocity">The bullet's velocity.</param>
        /// <param name="bulletDrop">Whether the bullet is affected by gravity or not.</param>
        /// <param name="targetPosition">The target's position.</param>
        /// <param name="targetVelocity">The target's velocity.</param>
        /// <param name="targetAcceleration">The target's acceleration (combined gravitational and local forces).</param>
        /// <param name="targetIsSupported">Whether the target is supported (in which case gravitational forces are ignored).</param>
        /// <param name="timeStep">The timestep to use initially.</param>
        /// <param name="maxTime">The max time to run for.</param>
        /// <param name="cpaType">The type of closest approach (earliest, latest, closest).</param>
        /// <param name="elapsedTime">Tracker for the elapsed time.</param>
        /// <param name="stage">Tracker for the simulation stage.</param>
        /// <returns>The position of the bullet at the CPA, position of the target at the CPA and the time to the CPA.</returns>
        public (Vector3, Vector3, float) BallisticTrajectoryClosestApproachSimulation(Vector3 position, Vector3 velocity, bool bulletDrop, Vector3 targetPosition, Vector3 targetVelocity, Vector3 targetAcceleration, bool targetIsSupported, float timeStep, float maxTime, AIUtils.CPAType cpaType = AIUtils.CPAType.Earliest, float elapsedTime = 0, SimulationStage stage = SimulationStage.Normal)
        {
            Vector3 initialPosition = position, initialTargetPosition = targetPosition;
            Vector3 lastPosition, lastTargetPosition;

            Vector3 gravity = bulletDrop ? FlightGlobals.getGeeForceAtPosition(position) : Vector3.zero;
            Vector3 targetGravity = targetIsSupported ? Vector3.zero : FlightGlobals.getGeeForceAtPosition(targetPosition); // Supported targets (landed, splashed, VM) aren't affected by gravity due to contact forces.
            targetAcceleration -= targetGravity; // Separate the target's acceleration into a gravity component (varying with position) and a constant component (local thrust).
            velocity += 0.5f * timeStep * gravity; // Leap-frog boot-strapping for the bullet.
            targetVelocity += 0.5f * timeStep * (targetAcceleration + targetGravity); // Leap-frog boot-strapping for the target.

            bool closing = (stage != SimulationStage.Normal) || Vector3.Dot(targetPosition - position, targetVelocity - velocity) < 0f; // For the Normal stage, we need to wait until we've started closing or are certain we never will.
            var simStartTime = Time.realtimeSinceStartup;
            while (elapsedTime < maxTime && Time.realtimeSinceStartup - simStartTime < 0.1f) // Allow 0.1s of real-time for the simulation. This ought to be plenty.
            {
                lastPosition = position;
                lastTargetPosition = targetPosition;

                position += timeStep * velocity; // Leap-frog for the bullet's position.
                targetPosition += timeStep * targetVelocity; // Leap-frog for the target's position.

                // Check whether we've passed through the CPA. This has to behave similarly to AIUtils.TimeToCPA.
                // TODO It should support the different CPATypes, but that can be left for later as we only need Earliest for now.
                if (!closing)
                {
                    closing = Vector3.Dot(targetPosition - position, targetVelocity - velocity) < 0; // Check if we've started closing.
                    if (!closing && Vector3.Dot(targetVelocity - velocity, targetGravity + targetAcceleration - gravity) > 0) // Check if they're accelerating away from each other => never going to meet (without performing a full orbit...).
                    {
                        return (initialPosition, initialTargetPosition, 0); // timeToCPA is negative
                    }
                }
                if (closing && Vector3.Dot(targetPosition - position, targetVelocity - velocity) >= 0f) // Step went beyond the CPA.
                {
                    velocity -= 0.5f * timeStep * gravity; // Undo the last leap-frog step for the bullet's velocity.
                    targetVelocity -= 0.5f * timeStep * (targetAcceleration + targetGravity); // Undo the last leap-frog step for the target's velocity.
                    switch (stage)
                    {
                        case SimulationStage.Normal:
                        case SimulationStage.Refining: // Perform a more accurate final step for the collision.
                            return BallisticTrajectoryClosestApproachSimulation(
                                lastPosition,
                                velocity,
                                bulletDrop,
                                lastTargetPosition,
                                targetVelocity,
                                targetAcceleration + targetGravity,
                                targetIsSupported,
                                timeStep / 4f,
                                maxTime,
                                cpaType,
                                elapsedTime,
                                timeStep > 5f * Time.fixedDeltaTime ? SimulationStage.Refining : SimulationStage.Final
                            );
                        case SimulationStage.Final:
                            // Perform the last step analytically
                            var timeToCPA = AIUtils.TimeToCPA(lastPosition - lastTargetPosition, velocity - targetVelocity, gravity - (targetAcceleration + targetGravity), timeStep);
                            position = AIUtils.PredictPosition(lastPosition, velocity, gravity, timeToCPA);
                            targetPosition = AIUtils.PredictPosition(lastTargetPosition, targetVelocity, targetAcceleration + targetGravity, timeToCPA);
                            elapsedTime += timeToCPA;
                            return (position, targetPosition, elapsedTime);
                    }
                }
                gravity = bulletDrop ? FlightGlobals.getGeeForceAtPosition(position) : Vector3.zero;
                if (!targetIsSupported) targetGravity = FlightGlobals.getGeeForceAtPosition(targetPosition);
                velocity += timeStep * gravity;
                targetVelocity += timeStep * (targetAcceleration + targetGravity);
                elapsedTime += timeStep;
            }
            if (elapsedTime < maxTime) Debug.LogWarning("[BDArmory.ModuleWeapon]: Ballistic trajectory closest approach simulation timed out.");
            return (position, targetPosition, elapsedTime); // Was heading to a CPA, but didn't reach it in time.
        }

        //more organization, grouping like with like
        public Vector3 GetLeadOffset()
        {
            return fixedLeadOffset;
        }

        void UpdateOffsetWeapon()
        {
            if (fireTransforms == null) return;
            Vector3 weaponPosition = fireTransforms[0].position;
            Vector3 weaponDirection = fireTransforms[0].forward;
            if (part.symmetryCounterparts.Count > 0)
            {
                foreach (var part in part.symmetryCounterparts)
                {
                    weaponPosition += part.transform.position;
                    if (part.GetComponent<ModuleWeapon>().fireTransforms != null)
                        weaponDirection += part.GetComponent<ModuleWeapon>().fireTransforms[0].forward;
                }
                weaponPosition /= 1 + part.symmetryCounterparts.Count;
                weaponDirection /= 1 + part.symmetryCounterparts.Count;
            }

            offsetWeaponPosition = weaponPosition - vessel.ReferenceTransform.position;
            offsetWeaponDirection = vessel.ReferenceTransform.InverseTransformDirection(weaponDirection);
        }

        public float targetCosAngle;
        public bool safeToFire;
        void CheckAIAutofire()
        {
            //autofiring with AI
            if (targetAcquired && aiControlled)
            {
                Transform fireTransform = fireTransforms[0];
                if (eWeaponType == WeaponTypes.Rocket && rocketPod)
                {
                    fireTransform = rockets[0].parent; // support for legacy RLs
                }

                Vector3 targetRelPos = finalAimTarget - fireTransform.position;
                Vector3 aimDirection = fireTransform.forward;
                targetCosAngle = Vector3.Dot(aimDirection, targetRelPos.normalized);
                // var maxAutoFireCosAngle2 = targetAdjustedMaxCosAngle;
                safeToFire = CheckForFriendlies(fireTransform); //TODO - test why APS returning safeToFire = false
                if (BDArmorySettings.BULLET_WATER_DRAG && eWeaponType == WeaponTypes.Ballistic && FlightGlobals.getAltitudeAtPos(fireTransforms[0].position) < 0)
                    safeToFire = false; //don't fire guns underwater 

                if (safeToFire)
                {
                    if (eWeaponType == WeaponTypes.Ballistic || eWeaponType == WeaponTypes.Laser)
                        autoFire = (targetCosAngle >= targetAdjustedMaxCosAngle);
                    else // Rockets
                        autoFire = (targetCosAngle >= targetAdjustedMaxCosAngle) && ((finalAimTarget - fireTransform.position).sqrMagnitude > (blastRadius * blastRadius) * 2);

                    if (autoFire && Vector3.Angle(targetPosition - fireTransform.position, aimDirection) < 5) //check LoS for direct-fire weapons
                    {
                        if (RadarUtils.TerrainCheck(eWeaponType == WeaponTypes.Laser ? targetPosition : fireTransform.position + (fireTransform.forward * 1500), fireTransform.position)) //kerbin curvature is going to start returning raycast terrain hits at about 1.8km for tanks
                        {
                            autoFire = false;
                        }
                    }
                }
                else
                {
                    autoFire = false;
                }
                if (autoFire && weaponManager.staleTarget && (lastVisualTargetVessel != null && lastVisualTargetVessel.LandedOrSplashed && vessel.LandedOrSplashed)) autoFire = false; //ground Vee engaging another ground Vee which has ducked out of sight, don't fire
                                                                                                                                                                                       // won't catch cloaked tanks, but oh well.

                // if (eWeaponType != WeaponTypes.Rocket) //guns/lasers
                // {
                //     // Vector3 targetDiffVec = finalAimTarget - lastFinalAimTarget;
                //     // Vector3 projectedTargetPos = targetDiffVec;
                //     //projectedTargetPos /= TimeWarp.fixedDeltaTime;
                //     //projectedTargetPos *= TimeWarp.fixedDeltaTime;
                //     // projectedTargetPos *= 2; //project where the target will be in 2 timesteps
                //     // projectedTargetPos += finalAimTarget;

                //     // targetDiffVec.Normalize();
                //     // Vector3 lastTargetRelPos = (lastFinalAimTarget) - fireTransform.position;

                //     safeToFire = BDATargetManager.CheckSafeToFireGuns(weaponManager, aimDirection, 1000, 0.999962f); //~0.5 degree of unsafe angle, was 0.999848f (1deg)
                //     if (safeToFire && targetCosAngle >= maxAutoFireCosAngle2) //check if directly on target
                //     {
                //         autoFire = true;
                //     }
                //     else
                //     {
                //         autoFire = false;
                //     }
                // }
                // else // rockets
                // {
                //     safeToFire = BDATargetManager.CheckSafeToFireGuns(weaponManager, aimDirection, 1000, 0.999848f);
                //     if (safeToFire)
                //     {
                //         if ((Vector3.Distance(finalAimTarget, fireTransform.position) > blastRadius) && (targetCosAngle >= maxAutoFireCosAngle2))
                //         {
                //             autoFire = true; //rockets already calculate where target will be
                //         }
                //         else
                //         {
                //             autoFire = false;
                //         }
                //     }
                // }
            }
            else
            {
                autoFire = false;
            }

            //disable autofire after burst length
            if (autoFire && (!BurstOverride && Time.time - autoFireTimer > autoFireLength) || (BurstOverride && autofireShotCount >= fireBurstLength))
            {
                autoFire = false;
                //visualTargetVessel = null;
                //visualTargetPart = null;
                //tgtShell = null;
                //tgtRocket = null;
                if (SpoolUpTime > 0)
                {
                    roundsPerMinute = baseRPM / 10;
                    spooltime = 0;
                }
                if (eWeaponType == WeaponTypes.Laser && LaserGrowTime > 0)
                {
                    projectileColorC = GUIUtils.ParseColor255(projectileColor);
                    startColorS = startColor.Split(","[0]);
                    laserDamage = baseLaserdamage;
                    tracerStartWidth = tracerBaseSWidth;
                    tracerEndWidth = tracerBaseEWidth;
                    Offset = 0;
                }
            }
            if (isAPS)
            {
                float threatDirectionFactor = (fireTransforms[0].position - targetPosition).DotNormalized(targetVelocity - part.rb.velocity);
                if (threatDirectionFactor < 0.9f) autoFire = false;   //within 28 degrees in front, else ignore, target likely not on intercept vector
            }
        }

        /// <summary>
        /// Check for friendlies being likely to be hit by firing.
        /// </summary>
        /// <returns>true if no friendlies are likely to be hit, false otherwise.</returns>
        bool CheckForFriendlies(Transform fireTransform)
        {
            if (weaponManager == null || weaponManager.vessel == null) return false;
            var firingDirection = fireTransform.forward;

            if (eWeaponType == WeaponTypes.Laser)
            {
                using (var friendly = FlightGlobals.Vessels.GetEnumerator())
                    while (friendly.MoveNext())
                    {
                        if (VesselModuleRegistry.ignoredVesselTypes.Contains(friendly.Current.vesselType)) continue;
                        if (friendly.Current == null || friendly.Current == weaponManager.vessel) continue;
                        var wms = VesselModuleRegistry.GetModule<MissileFire>(friendly.Current);
                        if (wms == null || wms.Team != weaponManager.Team) continue;
                        var friendlyRelativePosition = friendly.Current.CoM - fireTransform.position;
                        var theta = friendly.Current.GetRadius() / friendlyRelativePosition.magnitude; // Approx to arctan(θ) =  θ - θ^3/3 + O(θ^5)
                        var cosTheta = Mathf.Clamp(1f - 0.5f * theta * theta, -1f, 1f); // Approximation to cos(theta) for the friendly vessel's radius at that distance. (cos(x) = 1-x^2/2!+O(x^4))
                        if (Vector3.Dot(firingDirection, friendlyRelativePosition.normalized) > cosTheta) return false; // A friendly is in the way.
                    }
                return true;
            }

            // Projectile. Use bullet velocity or estimate of the rocket velocity post-thrust.
            var projectileEffectiveVelocity = part.rb.velocity + (eWeaponType == WeaponTypes.Rocket ? (BDKrakensbane.FrameVelocityV3f + thrust * thrustTime / rocketMass * firingDirection) : (baseBulletVelocity * firingDirection));
            var gravity = (Vector3)FlightGlobals.getGeeForceAtPosition(fireTransform.position); // Use the local gravity value as long distance doesn't really matter here.
            var projectileAcceleration = bulletDrop || eWeaponType == WeaponTypes.Rocket ? gravity : Vector3.zero; // Drag is ignored.

            using (var friendly = FlightGlobals.Vessels.GetEnumerator())
                while (friendly.MoveNext())
                {
                    if (VesselModuleRegistry.ignoredVesselTypes.Contains(friendly.Current.vesselType)) continue;
                    if (friendly.Current == null || friendly.Current == weaponManager.vessel) continue;
                    var wms = VesselModuleRegistry.GetModule<MissileFire>(friendly.Current);
                    if (wms == null || wms.Team != weaponManager.Team) continue;
                    var friendlyPosition = friendly.Current.CoM;
                    var friendlyVelocity = friendly.Current.Velocity();
                    var friendlyAcceleration = friendly.Current.acceleration;
                    var projectileRelativePosition = friendlyPosition - fireTransform.position;
                    var projectileRelativeVelocity = friendlyVelocity - projectileEffectiveVelocity;
                    var projectileRelativeAcceleration = friendlyAcceleration - projectileAcceleration;
                    var timeToCPA = AIUtils.TimeToCPA(projectileRelativePosition, projectileRelativeVelocity, projectileRelativeAcceleration, maxTargetingRange / projectileEffectiveVelocity.magnitude);
                    if (timeToCPA == 0) continue; // They're behind us.
                    var missDistanceSqr = AIUtils.PredictPosition(projectileRelativePosition, projectileRelativeVelocity, projectileRelativeAcceleration, timeToCPA).sqrMagnitude;
                    var tolerance = friendly.Current.GetRadius() + projectileRelativePosition.magnitude * Mathf.Deg2Rad * maxDeviation; // Use a firing tolerance of 1 and twice the projectile deviation for friendlies.
                    if (missDistanceSqr < tolerance * tolerance) return false; // A friendly is in the way.
                }
            return true;
        }

        void CheckFinalFire()
        {
            finalFire = false;
            //if user pulling the trigger || AI controlled and on target if turreted || finish a burstfire weapon's burst
            if (fireConditionCheck)
            {
                if ((pointingAtSelf || isOverheated || isReloading) || (aiControlled && (engageRangeMax < targetDistance || engageRangeMin > targetDistance)))// is weapon within set max range?
                {
                    if (useRippleFire) //old method wouldn't catch non-ripple guns (i.e. Vulcan) trying to fire at targets beyond fire range
                    {
                        //StartCoroutine(IncrementRippleIndex(0));
                        StartCoroutine(IncrementRippleIndex(InitialFireDelay * TimeWarp.CurrentRate)); //FIXME - possibly not getting called in all circumstances? Investigate later, future SI
                                                                                                       //Debug.Log($"[BDarmory.moduleWeapon] Weapon on rippleindex {weaponManager.GetRippleIndex(WeaponName)} cant't fire, skipping to next weapon after a {initialFireDelay * TimeWarp.CurrentRate} sec delay");
                        isRippleFiring = true;
                    }
                    if (eWeaponType == WeaponTypes.Laser)
                    {
                        if ((!pulseLaser && !BurstFire) || (!pulseLaser && BurstFire && (RoundsRemaining >= RoundsPerMag)) || (pulseLaser && timeSinceFired > beamDuration))
                        {
                            for (int i = 0; i < laserRenderers.Length; i++)
                            {
                                laserRenderers[i].enabled = false;
                            }
                        }
                    }
                }
                else
                {
                    if (SpoolUpTime > 0)
                    {
                        if (spooltime < 1)
                        {
                            spooltime += TimeWarp.deltaTime / SpoolUpTime;
                            spooltime = Mathf.Clamp01(spooltime);
                            roundsPerMinute = Mathf.Lerp((baseRPM / 10), baseRPM, spooltime);
                        }
                    }
                    if (!useRippleFire || isRippleFiring || weaponManager.GetRippleIndex(WeaponName) == rippleIndex) // Don't fire rippling weapons when they're on the wrong part of the cycle (initially; afterwards, let their timers decide). Spool up and grow lasers though.
                    {
                        finalFire = true;
                    }
                    if (BurstFire && RoundsRemaining > 0 && RoundsRemaining < RoundsPerMag)
                    {
                        finalFire = true;
                    }
                    if (eWeaponType == WeaponTypes.Laser)
                    {
                        if (LaserGrowTime > 0)
                        {
                            laserDamage = Mathf.Lerp(laserDamage, laserMaxDamage, 0.02f / LaserGrowTime);
                            tracerStartWidth = Mathf.Lerp(tracerStartWidth, tracerMaxStartWidth, 0.02f / LaserGrowTime);
                            tracerEndWidth = Mathf.Lerp(tracerEndWidth, tracerMaxEndWidth, 0.02f / LaserGrowTime);
                            if (DynamicBeamColor)
                            {
                                startColorS[0] = Mathf.Lerp(float.Parse(startColorS[0]), float.Parse(endColorS[0]), 0.02f / LaserGrowTime).ToString();
                                startColorS[1] = Mathf.Lerp(float.Parse(startColorS[1]), float.Parse(endColorS[1]), 0.02f / LaserGrowTime).ToString();
                                startColorS[2] = Mathf.Lerp(float.Parse(startColorS[2]), float.Parse(endColorS[2]), 0.02f / LaserGrowTime).ToString();
                                startColorS[3] = Mathf.Lerp(float.Parse(startColorS[3]), float.Parse(endColorS[3]), 0.02f / LaserGrowTime).ToString();
                            }
                            for (int i = 0; i < 4; i++)
                            {
                                projectileColorC[i] = float.Parse(startColorS[i]) / 255;
                            }
                        }
                        UpdateLaserSpecifics(DynamicBeamColor, dynamicFX, LaserGrowTime > 0, beamScrollRate != 0);
                    }
                }
            }
            else
            {
                if (weaponManager != null && weaponManager.GetRippleIndex(WeaponName) == rippleIndex)
                {
                    StartCoroutine(IncrementRippleIndex(0));
                    isRippleFiring = false;
                }
                if (eWeaponType == WeaponTypes.Laser)
                {
                    if (LaserGrowTime > 0)
                    {
                        projectileColorC = GUIUtils.ParseColor255(projectileColor);
                        startColorS = startColor.Split(","[0]);
                        laserDamage = baseLaserdamage;
                        tracerStartWidth = tracerBaseSWidth;
                        tracerEndWidth = tracerBaseEWidth;
                        Offset = 0;
                    }
                    if ((!pulseLaser && !BurstFire) || (!pulseLaser && BurstFire && (RoundsRemaining >= RoundsPerMag)) || (pulseLaser && timeSinceFired > beamDuration))
                    {
                        for (int i = 0; i < laserRenderers.Length; i++)
                        {
                            laserRenderers[i].enabled = false;
                        }
                    }
                    //if (!pulseLaser || !oneShotSound)
                    //{
                    //    audioSource.Stop();
                    //}
                }
                if (SpoolUpTime > 0)
                {
                    if (spooltime > 0)
                    {
                        spooltime -= TimeWarp.deltaTime / SpoolUpTime;
                        spooltime = Mathf.Clamp01(spooltime);
                        roundsPerMinute = Mathf.Lerp(baseRPM, (baseRPM / 10), spooltime);
                    }
                }
                if (hasCharged)
                {
                    if (hasChargeHoldAnimation)
                    {
                        chargeHoldState.enabled = true; //play chargedHold anim while weapon is charged but not firing - spooled gatling gun spin anims, etc
                        if (chargeHoldState.normalizedTime > 1)
                            chargeHoldState.normalizedTime = 0;
                        chargeHoldState.speed = fireAnimSpeed;
                    }
                    else if (hasChargeAnimation)
                    {
                        chargeState.enabled = true;
                        chargeState.speed = 0;
                        chargeState.normalizedTime = 1; //else use final frame of the chargeAnim if no hold anim, so weapon doesn't immediately revert to default state moment firing stops
                    }

                    if (ChargeTime > 0 && timeSinceFired > chargeHoldLength && !isReloading)
                    {
                        hasCharged = false;
                        if (hasChargeAnimation) chargeRoutine = StartCoroutine(ChargeRoutine(postFireChargeAnim));
                    }
                }
            }
        }

        void AimAndFire()
        {
            // This runs in the FashionablyLate timing phase of FixedUpdate before Krakensbane corrections have been applied.
            if (!(aimAndFireIfPossible || aimOnly)) return;
            if (this == null || vessel == null || !vessel.loaded || weaponManager == null || !gameObject.activeInHierarchy || FlightGlobals.currentMainBody == null) return;

            if (isAPS || (weaponManager.guardMode && dualModeAPS)) //prioritize APS as APS if AI using dualmode units for engaging standard targets
            {
                if (isAPS) TrackIncomingProjectile();
                if ((weaponManager.guardMode && dualModeAPS) && !TrackIncomingProjectile()) UpdateTargetVessel();
            }
            else
            {
                UpdateTargetVessel();
            }
            if (targetAcquired)
            {
                bool reset = lastTargetAcquisitionType != targetAcquisitionType || (targetAcquisitionType == TargetAcquisitionType.Visual && lastVisualTargetVessel != visualTargetVessel);
                SmoothTargetKinematics(targetPosition, targetVelocity, targetAcceleration, targetIsLandedOrSplashed, reset);
            }

            Aim();
            if (aimAndFireIfPossible)
            {
                CheckWeaponSafety();
                CheckAIAutofire();
                CheckFinalFire();
                // if (BDArmorySettings.DEBUG_LABELS) Debug.Log("DEBUG " + vessel.vesselName + " targeting visualTargetVessel: " + visualTargetVessel + ", finalFire: " + finalFire + ", pointingAtSelf: " + pointingAtSelf + ", targetDistance: " + targetDistance);

                if (finalFire)
                {
                    if (ChargeTime > 0 && !hasCharged)
                    {
                        if (!isCharging)
                        {
                            if (chargeRoutine != null)
                            {
                                StopCoroutine(chargeRoutine);
                                chargeRoutine = null;
                            }
                            chargeRoutine = StartCoroutine(ChargeRoutine());
                        }
                        else
                        {
                            aimAndFireIfPossible = false;
                            aimOnly = false;
                        }
                    }
                    else
                    {
                        switch (eWeaponType)
                        {
                            case WeaponTypes.Laser:
                                if (FireLaser())
                                {
                                    for (int i = 0; i < laserRenderers.Length; i++)
                                    {
                                        laserRenderers[i].enabled = true;
                                    }
                                    if (isAPS && (tgtShell != null || tgtRocket != null))
                                    {
                                        StartCoroutine(KillIncomingProjectile(tgtShell, tgtRocket));
                                    }
                                }
                                else
                                {
                                    if ((!pulseLaser && !BurstFire) || (!pulseLaser && BurstFire && (RoundsRemaining >= RoundsPerMag)) || (pulseLaser && timeSinceFired > beamDuration))
                                    {
                                        for (int i = 0; i < laserRenderers.Length; i++)
                                        {
                                            laserRenderers[i].enabled = false;
                                        }
                                    }
                                    //if (!pulseLaser || !oneShotSound)
                                    //{
                                    //    audioSource.Stop();
                                    //}
                                }
                                break;
                            case WeaponTypes.Ballistic:
                                Fire();
                                break;
                            case WeaponTypes.Rocket:
                                FireRocket();
                                break;
                        }
                    }
                }
            }

            aimAndFireIfPossible = false;
            aimOnly = false;
        }

        void DrawAlignmentIndicator()
        {
            if (fireTransforms == null || fireTransforms[0] == null) return;

            Part rootPart = EditorLogic.RootPart;
            if (rootPart == null) return;

            Transform refTransform = rootPart.GetReferenceTransform();
            if (!refTransform) return;

            Vector3 fwdPos = fireTransforms[0].position + (5 * fireTransforms[0].forward);
            GUIUtils.DrawLineBetweenWorldPositions(fireTransforms[0].position, fwdPos, 4, Color.green);

            Vector3 referenceDirection = refTransform.up;
            Vector3 refUp = -refTransform.forward;
            Vector3 refRight = refTransform.right;

            Vector3 refFwdPos = fireTransforms[0].position + (5 * referenceDirection);
            GUIUtils.DrawLineBetweenWorldPositions(fireTransforms[0].position, refFwdPos, 2, Color.white);

            GUIUtils.DrawLineBetweenWorldPositions(fwdPos, refFwdPos, 2, XKCDColors.Orange);

            string blocker = "";
            if (Physics.Raycast(new Ray(fireTransforms[0].position, fireTransforms[0].forward), out RaycastHit hit, 1000f, (int)LayerMasks.Parts))
            {
                var hitPart = hit.collider.gameObject.GetComponentInParent<Part>();
                var hitEVA = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                if (hitEVA != null) hitPart = hitEVA.part;
                if (hitPart != null)
                {
                    blocker = hitPart.partInfo.title;
                    GUIUtils.DrawTextureOnWorldPos(hit.point, BDArmorySetup.Instance.redDotTexture, new Vector2(16, 16), 0);
                }
            }

            Vector2 guiPos;
            if (GUIUtils.WorldToGUIPos(fwdPos, out guiPos))
            {
                Rect angleRect = new Rect(guiPos.x, guiPos.y, 100, 200);

                Vector3 pitchVector = (5 * fireTransforms[0].forward.ProjectOnPlanePreNormalized(refRight));
                Vector3 yawVector = (5 * fireTransforms[0].forward.ProjectOnPlanePreNormalized(refUp));

                GUIUtils.DrawLineBetweenWorldPositions(fireTransforms[0].position + pitchVector, fwdPos, 3,
                    Color.white);
                GUIUtils.DrawLineBetweenWorldPositions(fireTransforms[0].position + yawVector, fwdPos, 3, Color.white);

                float pitch = Vector3.Angle(pitchVector, referenceDirection);
                float yaw = Vector3.Angle(yawVector, referenceDirection);

                string convergeDistance;

                Vector3 projAxis = Vector3.Project(refTransform.position - fireTransforms[0].transform.position,
                    refRight);
                float xDist = projAxis.magnitude;
                float convergeAngle = 90 - Vector3.Angle(yawVector, refTransform.up);
                if (Vector3.Dot(fireTransforms[0].forward, projAxis) > 0)
                {
                    convergeDistance = $"Converge: {Mathf.Round((xDist * Mathf.Tan(convergeAngle * Mathf.Deg2Rad))).ToString()} m";
                }
                else
                {
                    convergeDistance = "Diverging";
                }

                string xAngle = $"X: {Vector3.Angle(fireTransforms[0].forward, pitchVector):0.00}";
                string yAngle = $"Y: {Vector3.Angle(fireTransforms[0].forward, yawVector):0.00}";

                string label = $"{xAngle}\n{yAngle}\n{convergeDistance}";
                if (!string.IsNullOrEmpty(blocker))
                {
                    angleRect.width += 6 * blocker.Length;
                    label += $"\nBlocked: {blocker}";
                }
                GUI.Label(angleRect, label);
            }
        }

        #endregion Targeting

        #region Updates
        void CheckCrewed()
        {
            if (!gunnerSeatLookedFor) // Only find the module once.
            {
                var kerbalSeats = part.Modules.OfType<KerbalSeat>();
                if (kerbalSeats.Count() > 0)
                    gunnerSeat = kerbalSeats.First();
                else
                    gunnerSeat = null;
                gunnerSeatLookedFor = true;
            }
            if ((gunnerSeat == null || gunnerSeat.Occupant == null) && part.protoModuleCrew.Count <= 0) //account for both lawn chairs and internal cabins
            {
                hasGunner = false;
            }
            else
            {
                hasGunner = true;
            }
        }
        void UpdateHeat()
        {
            if (heat > maxHeat && !isOverheated)
            {
                isOverheated = true;
                autoFire = false;
                hasCharged = false;
                if (hasChargeAnimation) chargeRoutine = StartCoroutine(ChargeRoutine(postFireChargeAnim));
                if (!oneShotSound) audioSource.Stop();
                wasFiring = false;
                if (spinDownAnimation) spinningDown = true;
                audioSource2.PlayOneShot(overheatSound);
                weaponManager.ResetGuardInterval();
            }
            heat = Mathf.Clamp(heat - heatLoss * TimeWarp.fixedDeltaTime, 0, Mathf.Infinity);
            if (heat < maxHeat / 3 && isOverheated) //reset on cooldown
            {
                isOverheated = false;
                autofireShotCount = 0;
                //Debug.Log("[BDArmory.ModuleWeapon]: AutoFire length: " + autofireShotCount);
            }
        }
        void ReloadWeapon()
        {
            if (isReloading)
            {
                ReloadTimer = Mathf.Min(ReloadTimer + TimeWarp.fixedDeltaTime / ReloadTime, 1);
                if (hasDeployAnim)
                {
                    AnimTimer = Mathf.Min(AnimTimer + TimeWarp.fixedDeltaTime / (ReloadTime - deployState.length), 1);
                }
            }
            if ((RoundsRemaining >= RoundsPerMag && !isReloading) && (ammoCount > 0 || BDArmorySettings.INFINITE_AMMO))
            {
                isReloading = true;
                autoFire = false;
                if (eWeaponType == WeaponTypes.Laser)
                {
                    for (int i = 0; i < laserRenderers.Length; i++)
                    {
                        laserRenderers[i].enabled = false;
                    }
                }
                wasFiring = false;
                weaponManager.ResetGuardInterval();
                showReloadMeter = true;
                if (hasReloadAnim)
                {
                    if (reloadRoutine != null)
                    {
                        StopCoroutine(reloadRoutine);
                        reloadRoutine = null;
                    }
                    reloadRoutine = StartCoroutine(ReloadRoutine());
                }
                else
                {
                    if (hasDeployAnim)
                    {
                        StopShutdownStartupRoutines();
                        shutdownRoutine = StartCoroutine(ShutdownRoutine(true));
                    }
                    if (!oneShotSound) audioSource.Stop();
                    if (!string.IsNullOrEmpty(reloadAudioPath))
                    {
                        audioSource2.Stop();
                        audioSource2.PlayOneShot(reloadAudioClip);
                    }
                }
            }
            if (!hasReloadAnim && hasDeployAnim && (AnimTimer >= 1 && isReloading))
            {
                if (eWeaponType == WeaponTypes.Rocket && rocketPod)
                {
                    RoundsRemaining = 0;
                    UpdateRocketScales();
                }
                if (weaponState == WeaponStates.Disabled || weaponState == WeaponStates.PoweringDown)
                {
                }
                else
                {
                    StopShutdownStartupRoutines(); //if weapon un-selected while reloading, don't activate weapon
                    startupRoutine = StartCoroutine(StartupRoutine(true));
                }
            }
            if (ReloadTimer >= 1 && isReloading)
            {
                RoundsRemaining = 0;
                autofireShotCount = 0;
                gauge.UpdateReloadMeter(1);
                showReloadMeter = false;
                isReloading = false;
                ReloadTimer = 0;
                AnimTimer = 0;
                if (eWeaponType == WeaponTypes.Rocket && rocketPod)
                {
                    UpdateRocketScales();
                }
                if (!string.IsNullOrEmpty(reloadCompletePath))
                {
                    audioSource2.Stop();
                    audioSource2.PlayOneShot(reloadCompleteAudioClip);
                }
            }
        }
        void UpdateTargetVessel()
        {
            targetAcquired = false;
            slaved = false;
            GPSTarget = false;
            radarTarget = false;
            bool atprWasAcquired = atprAcquired;
            atprAcquired = false;
            lastTargetAcquisitionType = targetAcquisitionType;

            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
            {
                if (Time.time - staleGoodTargetTime > Mathf.Max(BDArmorySettings.FIRE_RATE_OVERRIDE / 60f, weaponManager.targetScanInterval))
                {
                    targetAcquisitionType = TargetAcquisitionType.None;
                }
            }
            else
            {
                if (Time.time - staleGoodTargetTime > Mathf.Max(roundsPerMinute / 60f, weaponManager.targetScanInterval))
                {
                    targetAcquisitionType = TargetAcquisitionType.None;
                }
            }
            lastVisualTargetVessel = visualTargetVessel;

            if (weaponManager)
            {
                bool visRange = false;
                if (visualTargetVessel)
                {
                    visRange = (visualTargetVessel.transform.position - transform.position).sqrMagnitude < weaponManager.guardRange * weaponManager.guardRange;
                }
                //moving radar aiming/turret slaving sections ahead of legacy, else they'll never proc outside of manual control or very short visual range settings
                if (weaponManager.vesselRadarData && weaponManager.vesselRadarData.locked) //would only apply if fixed gun, else VRD would slave the weapon and the above block applies
                {
                    if (!(weaponManager.slavingTurrets && turret)) //no turrets/radar/laser locks, those are handled later
                    {
                        TargetSignatureData targetData = weaponManager.vesselRadarData.lockedTargetData.targetData; //no support for radar tracking, only locks?
                        if (targetData.exists)
                        {
                            targetVelocity = targetData.velocity - BDKrakensbane.FrameVelocityV3f;
                            targetPosition = targetData.predictedPositionWithChaffFactor(targetData.lockedByRadar.radarChaffClutterFactor);
                            targetRadius = 35f;
                            targetAcceleration = targetData.acceleration;
                            targetIsLandedOrSplashed = false;
                            if (targetData.vessel)
                            {
                                targetRadius = targetData.vessel.GetRadius();
                                targetIsLandedOrSplashed = targetData.vessel.LandedOrSplashed;
                            }
                            targetAcquired = true;
                            targetAcquisitionType = TargetAcquisitionType.Radar;
                            radarTarget = true;
                            return;
                        }
                    }
                }
                //else, we have turrets slaved to a targetpainter, use that
                if (weaponManager.slavingTurrets && turret)
                {
                    slaved = true;
                    targetRadius = weaponManager.slavedTarget.vessel != null ? weaponManager.slavedTarget.vessel.GetRadius() : 35f;
                    targetPosition = weaponManager.slavedPosition;
                    //currently overriding multi-turret multi-targeting if enabled as all turrets slaved to WM's guardTarget/current active radarLock
                    targetVelocity = weaponManager.slavedTarget.vessel != null ? weaponManager.slavedTarget.vessel.rb_velocity : (weaponManager.slavedVelocity - BDKrakensbane.FrameVelocityV3f);
                    if (weaponManager.slavedTarget.vessel != null)
                    {
                        targetAcceleration = weaponManager.slavedTarget.vessel.acceleration;
                        targetIsLandedOrSplashed = weaponManager.slavedTarget.vessel.LandedOrSplashed;
                    }
                    else
                    {
                        targetAcceleration = weaponManager.slavedAcceleration;
                        targetIsLandedOrSplashed = false;
                    }
                    targetAcquired = true;
                    targetAcquisitionType = TargetAcquisitionType.Slaved;
                    return;
                }

                // within visual range and no radar aiming/need precision visual targeting of specific subsystems
                if (aiControlled && visualTargetVessel && visRange)
                {
                    //targetRadius = visualTargetVessel.GetRadius();

                    if (visualTargetPart == null || visualTargetPart.vessel != visualTargetVessel)
                    {
                        TargetInfo currentTarget = visualTargetVessel.gameObject.GetComponent<TargetInfo>();
                        if (currentTarget == null)
                        {
                            if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log($"[BDArmory.ModuleWeapon]: Targeted vessel {(visualTargetVessel != null ? visualTargetVessel.vesselName : "'unknown'")} has no TargetInfo.");
                            return;
                        }
                        //targetRadius = visualTargetVessel.GetRadius(fireTransforms[0].forward, currentTarget.bounds);
                        List<Part> targetparts = new List<Part>();
                        if (targetCOM)
                        {
                            targetPosition = visualTargetVessel.CoM;
                            visualTargetPart = null; //make sure this gets reset
                            targetRadius = visualTargetVessel.GetRadius(fireTransforms[0].forward, currentTarget.bounds);
                        }
                        else
                        {
                            if (targetCockpits)
                            {
                                for (int i = 0; i < currentTarget.targetCommandList.Count; i++)
                                {
                                    if (!targetparts.Contains(currentTarget.targetCommandList[i]))
                                    {
                                        targetparts.Add(currentTarget.targetCommandList[i]);
                                    }
                                }
                            }
                            if (targetEngines)
                            {
                                for (int i = 0; i < currentTarget.targetEngineList.Count; i++)
                                {
                                    if (!targetparts.Contains(currentTarget.targetEngineList[i]))
                                    {
                                        targetparts.Add(currentTarget.targetEngineList[i]);
                                    }
                                }
                            }
                            if (targetWeapons)
                            {
                                for (int i = 0; i < currentTarget.targetWeaponList.Count; i++)
                                {
                                    if (!targetparts.Contains(currentTarget.targetWeaponList[i]))
                                    {
                                        targetparts.Add(currentTarget.targetWeaponList[i]);
                                    }
                                }
                            }
                            if (targetMass)
                            {
                                for (int i = 0; i < currentTarget.targetMassList.Count; i++)
                                {
                                    if (!targetparts.Contains(currentTarget.targetMassList[i]))
                                    {
                                        targetparts.Add(currentTarget.targetMassList[i]);
                                    }
                                }
                            }
                            if (targetRandom && currentTarget.Vessel != null)
                            {
                                for (int i = 0; i < Mathf.Min(currentTarget.Vessel.Parts.Count, weaponManager.multiTargetNum); i++)
                                {
                                    int r = (int)UnityEngine.Random.Range(0, Mathf.Min(currentTarget.Vessel.Parts.Count, weaponManager.multiTargetNum));
                                    if (!targetparts.Contains(currentTarget.Vessel.Parts[r]))
                                    {
                                        targetparts.Add(currentTarget.Vessel.Parts[r]);
                                    }
                                }
                            }
                            if (!targetCOM && !targetCockpits && !targetEngines && !targetWeapons && !targetMass)
                            {
                                for (int i = 0; i < currentTarget.targetMassList.Count; i++)
                                {
                                    if (!targetparts.Contains(currentTarget.targetMassList[i]))
                                    {
                                        targetparts.Add(currentTarget.targetMassList[i]);
                                    }
                                }
                            }
                            targetparts = targetparts.OrderBy(w => w.mass).ToList(); //weight target part priority by part mass, also serves as a default 'target heaviest part' in case other options not selected
                            targetparts.Reverse(); //Order by mass is lightest to heaviest. We want H>L
                                                   //targetparts.Shuffle(); //alternitively, increase the random range from maxtargetnum to targetparts.count, otherwise edge cases where lots of one thing (targeting command/mass) will be pulled before lighter things (weapons, maybe engines) if both selected
                            if (turret)
                            {
                                targetID = (int)UnityEngine.Random.Range(0, Mathf.Min(targetparts.Count, weaponManager.multiTargetNum));
                            }
                            else //make fixed guns all get the same target part
                            {
                                targetID = 0;
                            }
                            if (targetparts.Count == 0)
                            {
                                if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log($"[BDArmory.ModuleWeapon]: Targeted vessel {visualTargetVessel.vesselName} has no targetable parts.");
                                targetPosition = visualTargetVessel.CoM;
                                targetRadius = visualTargetVessel.GetRadius(fireTransforms[0].forward, currentTarget.bounds);
                            }
                            else
                            {
                                visualTargetPart = targetparts[targetID];
                                targetPosition = visualTargetPart.transform.position;
                                targetRadius = 3; //allow for more focused targeting of weighted subsystems
                            }
                        }
                    }
                    else
                    {
                        if (targetCOM)
                        {
                            targetPosition = visualTargetVessel.CoM;
                            visualTargetPart = null; //make sure these get reset
                            targetRadius = visualTargetVessel.GetRadius(average: true);
                        }
                        else
                        {
                            targetPosition = visualTargetPart.transform.position;
                            targetRadius = 5;
                        }
                    }
                    targetVelocity = visualTargetVessel.rb_velocity;
                    targetAcceleration = visualTargetVessel.acceleration;
                    targetIsLandedOrSplashed = visualTargetVessel.LandedOrSplashed;
                    targetAcquired = true;
                    targetAcquisitionType = TargetAcquisitionType.Visual;
                    return;
                }

                // GPS TARGETING HERE
                if (BDArmorySetup.Instance.showingWindowGPS && weaponManager.designatedGPSCoords != Vector3d.zero && !aiControlled)
                {
                    GPSTarget = true;
                    targetVelocity = Vector3d.zero;
                    targetPosition = weaponManager.designatedGPSInfo.worldPos;
                    targetRadius = 35f;
                    targetAcceleration = Vector3d.zero;
                    targetIsLandedOrSplashed = true;
                    targetAcquired = true;
                    targetAcquisitionType = TargetAcquisitionType.GPS;
                    return;
                }

                //auto proxy tracking
                if (vessel.isActiveVessel && (autoProxyTrackRange > 0 || MouseAimFlight.IsMouseAimActive)) // Allow better auto-proxy tracking when using MouseAimFlight.
                {
                    if (++aptrTicker < 20)
                    {
                        if (atprWasAcquired)
                        {
                            targetAcquired = true;
                            atprAcquired = true;
                        }
                    }
                    else
                    {
                        aptrTicker = 0;
                        Vessel tgt = null;
                        float closestSqrDist = autoProxyTrackRange * autoProxyTrackRange;
                        if (MouseAimFlight.IsMouseAimActive) closestSqrDist = Mathf.Max(closestSqrDist, maxEffectiveDistance * maxEffectiveDistance);
                        using (var v = BDATargetManager.LoadedVessels.GetEnumerator())
                            while (v.MoveNext())
                            {
                                if (v.Current == null || !v.Current.loaded || VesselModuleRegistry.ignoredVesselTypes.Contains(v.Current.vesselType)) continue;
                                if (!v.Current.IsControllable) continue;
                                if (v.Current == vessel) continue;
                                Vector3 targetVector = v.Current.CoM - part.transform.position;
                                var turretInRange = turret && turret.TargetInRange(v.Current.CoM, 20, maxEffectiveDistance);
                                if (!(turretInRange || Vector3.Dot(targetVector, fireTransforms[0].forward) > 0)) continue;
                                float sqrDist = (v.Current.CoM - part.transform.position).sqrMagnitude;
                                if (sqrDist > closestSqrDist) continue;
                                if (!(turretInRange || Vector3.Angle(targetVector, fireTransforms[0].forward) < 20)) continue;
                                tgt = v.Current;
                                closestSqrDist = sqrDist;
                            }

                        if (tgt != null)
                        {
                            targetAcquired = true;
                            atprAcquired = true;
                            targetRadius = tgt.GetRadius(average: true);
                            targetPosition = tgt.CoM;
                            targetVelocity = tgt.rb_velocity;
                            targetAcceleration = tgt.acceleration;
                            targetIsLandedOrSplashed = tgt.LandedOrSplashed;
                            atprTargetPosition = targetPosition;
                        }
                    }
                    if (targetAcquired)
                    {
                        targetPosition = atprTargetPosition;
                        targetAcquisitionType = TargetAcquisitionType.AutoProxy;
                        return;
                    }
                }
            }

            if (!targetAcquired)
            {
                targetVelocity = Vector3.zero;
                targetAcceleration = Vector3.zero;
                targetIsLandedOrSplashed = false;
            }
        }


        bool TrackIncomingProjectile()
        {
            targetAcquired = false;
            atprAcquired = false;
            slaved = false;
            radarTarget = false;
            GPSTarget = false;
            lastTargetAcquisitionType = targetAcquisitionType;
            closestTarget = Vector3.zero;
            if (Time.time - staleGoodTargetTime > Mathf.Max(roundsPerMinute / 60f, weaponManager.targetScanInterval))
            {
                targetAcquisitionType = TargetAcquisitionType.None;
            }
            if (weaponManager && weaponState == WeaponStates.Enabled)
            {
                if (tgtShell != null || tgtRocket != null || visualTargetPart != null)
                {
                    visualTargetVessel = null;
                    if (tgtShell != null)
                    {
                        targetVelocity = tgtShell.currentVelocity - BDKrakensbane.FrameVelocityV3f; // Local frame velocity.
                        targetPosition = tgtShell.previousPosition; // Bullets have been moved already, but aiming logic is based on pre-move positions.
                        targetRadius = 0.25f;
                    }
                    if (tgtRocket != null)
                    {
                        targetVelocity = tgtRocket.currentVelocity - BDKrakensbane.FrameVelocityV3f;
                        targetPosition = tgtRocket.currentPosition;
                        targetRadius = 0.25f;
                    }
                    if (visualTargetPart != null)
                    {
                        targetVelocity = visualTargetPart.vessel.rb_velocity;
                        targetPosition = visualTargetPart.transform.position;
                        visualTargetVessel = visualTargetPart.vessel;
                        TargetInfo currentTarget = (visualTargetVessel != null ? visualTargetVessel.gameObject.GetComponent<TargetInfo>() : null);
                        targetRadius = currentTarget != null ? visualTargetVessel.GetRadius(fireTransforms[0].forward, currentTarget.bounds) : 0;
                    }

                    if (visualTargetPart != null && visualTargetPart.vessel != null)
                    {
                        targetAcceleration = (Vector3)visualTargetPart.vessel.acceleration;
                        targetIsLandedOrSplashed = visualTargetPart.vessel.LandedOrSplashed;
                    }
                    else
                    {
                        targetAcceleration = Vector3.zero;
                        targetIsLandedOrSplashed = false;
                    }
                    targetAcquired = true;
                    targetAcquisitionType = TargetAcquisitionType.Visual;
                    if (weaponManager.slavingTurrets && turret) slaved = false;
                    if (BDArmorySettings.DEBUG_WEAPONS)
                    {
                        Debug.Log("[BDArmory.ModuleWeapon] tgtVelocity: " + tgtVelocity + "; tgtPosition: " + targetPosition + "; tgtAccel: " + targetAcceleration);
                        Debug.Log($"[BDArmory.ModuleWeapon - {(vessel != null ? vessel.GetName() : "null")}] Lead Offset: {fixedLeadOffset}, FinalAimTgt: {finalAimTarget}, tgt CosAngle {targetCosAngle}, wpn CosAngle {targetAdjustedMaxCosAngle}, Wpn Autofire: {autoFire}");
                    }
                    return true;
                }
                else
                {
                    if (turret && visualTargetVessel == null) turret.ReturnTurret(); //reset turret if no target
                                                                                     //visualTargetPart = null;
                                                                                     //tgtShell = null;
                                                                                     //tgtRocket = null;
                }
            }
            return false;
        }

        IEnumerator KillIncomingProjectile(PooledBullet shell, PooledRocket rocket)
        {
            //So, uh, this is fine for simgle shot APS; what about conventional CIWS type AMS using rotary cannon for dakka vs accuracy?
            //should include a check for non-explosive rounds merely getting knocked off course instead of exploded.
            //should this be shell size dependant? I.e. sure, an APS can knock a sabot offcourse with a 60mm interceptor; what about that same 60mm shot vs a 155mm arty shell? or a 208mm naval gun?
            //really only an issue in case of AP APS (e.g. flechette APS for anti-missile work) vs AP shell; HE APS rounds should be able to destroy incoming proj
            if (shell != null || rocket != null)
            {
                delayTime = -1;
                if (baseDeviation > 0.05 && (eWeaponType == WeaponTypes.Ballistic || (eWeaponType == WeaponTypes.Laser && pulseLaser))) //if using rotary cannon/CIWS for APS
                {
                    if (UnityEngine.Random.Range(0, (targetDistance - (Mathf.Cos(baseDeviation) * targetDistance))) > 1)
                    {
                        yield break; //simulate inaccuracy, decreasing as incoming projectile gets closer
                    }
                }
                delayTime = eWeaponType == WeaponTypes.Ballistic ? (targetDistance / (bulletVelocity + (targetVelocity - part.rb.velocity).magnitude)) : (eWeaponType == WeaponTypes.Rocket ? (targetDistance / ((targetDistance / predictedFlightTime) + (targetVelocity - part.rb.velocity).magnitude)) : -1);
                if (delayTime < 0)
                {
                    delayTime = rocket != null ? 0.5f : (shell.bulletMass * (1 - Mathf.Clamp(shell.tntMass / shell.bulletMass, 0f, 0.95f) / 2)); //for shells, laser delay time is based on shell mass/HEratio. The heavier the shell, the more mass to burn through. Don't expect to stop sabots via laser APS
                    var angularSpread = tanAngle * targetDistance;
                    delayTime /= ((laserDamage / (1 + Mathf.PI * angularSpread * angularSpread) * 0.425f) / 100);
                    if (delayTime < TimeWarp.fixedDeltaTime) delayTime = 0;
                }
                yield return new WaitForSeconds(delayTime);
                if (shell != null)
                {
                    if (shell.tntMass > 0)
                    {
                        shell.hasDetonated = true;
                        ExplosionFx.CreateExplosion(shell.transform.position, shell.tntMass, shell.explModelPath, shell.explSoundPath, ExplosionSourceType.Bullet, shell.caliber, null, shell.sourceVesselName, null, null, default, -1, false, shell.bulletMass, -1, 1, sourceVelocity: shell.currentVelocity);
                        shell.KillBullet();
                        tgtShell = null;
                        if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log($"[BDArmory.ModuleWeapon] {part.partInfo.name} on {vessel.vesselName} Detonated Incoming Projectile!");
                    }
                    else
                    {
                        if (eWeaponType == WeaponTypes.Laser)
                        {
                            shell.KillBullet();
                            tgtShell = null;
                            if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log($"[BDArmory.ModuleWeapon] {part.partInfo.name} on {vessel.vesselName} Vaporized Incoming Projectile!");
                        }
                        else
                        {
                            if (tntMass <= 0) //e.g. APS flechettes vs sabot
                            {
                                shell.bulletMass -= bulletMass;
                                shell.currentVelocity = VectorUtils.GaussianDirectionDeviation(shell.currentVelocity, ((shell.bulletMass * shell.currentVelocity.magnitude) / (bulletMass * bulletVelocity)));
                                //shell.caliber = //have some modification of caliber to sim knocking round off-prograde?
                                //Thing is, something like a sabot liable to have lever action work upon it, spin it so it now hits on it's side instead of point first, but a heavy arty shell you have both substantially greater mass to diflect, and lesser increase in caliber from perpendicular hit - sabot from point on to side on is like a ~10x increase, a 208mm shell is like 1.2x 
                                //there's also the issue of gross modification of caliber in this manner if the shell receives multiple impacts from APS interceptors before it hits; would either need to be caliber = x, which isn't appropraite for heavy shells that would not be easily knocked off course, or caliber +=, which isn't viable for sabots
                                //easiest way would just have the APS interceptor destroy the incoming round, regardless; and just accept the occasional edge cases like a flechetteammo APS being able to destroy AP naval shells instead of tickling them and not much else
                            }
                            else
                            {
                                shell.KillBullet();
                                tgtShell = null;
                                if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log($"[BDArmory.ModuleWeapon] {part.partInfo.name} on {vessel.vesselName} Exploded Incoming Projectile!");
                            }
                        }
                    }
                }
                else
                {
                    if (rocket.tntMass > 0)
                    {
                        rocket.hasDetonated = true;
                        ExplosionFx.CreateExplosion(rocket.transform.position, rocket.tntMass, rocket.explModelPath, rocket.explSoundPath, ExplosionSourceType.Rocket, rocket.caliber, null, rocket.sourceVesselName, null, null, default, -1, false, rocket.rocketMass * 1000, -1, 1, sourceVelocity: rocket.currentVelocity);
                    }
                    rocket.gameObject.SetActive(false);
                    tgtRocket = null;
                }
            }
            else
            {
                //Debug.Log("[BDArmory.ModuleWeapon] KillIncomingProjectile called on null object!");
            }
        }

        /// <summary>
        /// Apply Brown's double exponential smoothing to the target velocity and acceleration values to smooth out noise.
        /// The smoothing factor depends on the distance to the target.
        /// The smoothed velocity components are corrected for the Krakensbane velocity frame and may suffer loss of precision at extreme speeds.
        /// </summary>
        /// <param name="position"></param>
        /// <param name="velocity"></param>
        /// <param name="acceleration"></param>
        /// <param name="reset"></param>
        void SmoothTargetKinematics(Vector3 position, Vector3 velocity, Vector3 acceleration, bool landedOrSplashed, bool reset = false)
        {
            // Floating objects need vertical smoothing.
            float altitude = (float)FlightGlobals.currentMainBody.GetAltitude(position);
            if (altitude < 12 && altitude > -10)
                acceleration = acceleration.ProjectOnPlanePreNormalized(VectorUtils.GetUpDirection(position));

            var distance = Vector3.Distance(position, part.transform.position);
            var alpha = Mathf.Max(1f - BDAMath.Sqrt(distance) / (landedOrSplashed ? 256f : 512f), 0.1f); // Landed targets have various "corrections" that cause significant noise in their acceleration values.
            var beta = alpha * alpha;
            if (!reset)
            {
                // To smooth velocities, we need to use a consistent reference frame.
                targetVelocitySmoothing.Update(velocity + BDKrakensbane.FrameVelocityV3f, alpha);
                targetVelocity = targetVelocitySmoothing.Value - BDKrakensbane.FrameVelocityV3f;
                targetAccelerationSmoothing.Update(acceleration, beta);
                targetAcceleration = targetAccelerationSmoothing.Value;
                partVelocitySmoothing.Update(part.rb.velocity + BDKrakensbane.FrameVelocityV3f, alpha);
                smoothedPartVelocity = partVelocitySmoothing.Value - BDKrakensbane.FrameVelocityV3f;
                partAccelerationSmoothing.Update(part.vessel.acceleration_immediate);
                smoothedPartAcceleration = partAccelerationSmoothing.Value;
            }
            else
            {
                targetVelocitySmoothing.Reset(velocity + BDKrakensbane.FrameVelocityV3f);
                targetVelocity = velocity;
                targetAccelerationSmoothing.Reset(acceleration);
                targetAcceleration = acceleration;
                partVelocitySmoothing.Reset(part.rb.velocity + BDKrakensbane.FrameVelocityV3f);
                smoothedPartVelocity = part.rb.velocity;
                partAccelerationSmoothing.Reset(part.vessel.acceleration_immediate);
                smoothedPartAcceleration = partAccelerationSmoothing.Value;
                lastTimeToCPA = -1;
            }
        }

        void UpdateGUIWeaponState()
        {
            guiStatusString = weaponState.ToString();
        }

        IEnumerator StartupRoutine(bool calledByReload = false, bool secondaryFiring = false)
        {
            if (hasReloadAnim && isReloading) //wait for reload to finish before shutting down
            {
                yield return new WaitWhileFixed(() => reloadState.normalizedTime < 1);
            }
            if (!calledByReload)
            {
                weaponState = WeaponStates.PoweringUp;
                UpdateGUIWeaponState();
            }
            if (hasDeployAnim && deployState)
            {
                deployState.enabled = true;
                deployState.speed = 1;
                yield return new WaitWhileFixed(() => deployState.normalizedTime < 1); //wait for animation here
                deployState.normalizedTime = 1;
                deployState.speed = 0;
                deployState.enabled = false;
            }
            if (!calledByReload)
            {
                if (!secondaryFiring)
                    weaponState = WeaponStates.Enabled;
                else
                    weaponState = WeaponStates.EnabledForSecondaryFiring;
            }
            UpdateGUIWeaponState();
            BDArmorySetup.Instance.UpdateCursorState();
            if (isAPS && (ammoCount > 0 || BDArmorySettings.INFINITE_AMMO))
            {
                aiControlled = true;
                targetPosition = fireTransforms[0].forward * engageRangeMax; //Ensure targetPosition is not null or 0 by the time code reaches Aim(), in case of no incoming projectile, since no target vessel to be continuously tracked.
            }
        }
        IEnumerator ShutdownRoutine(bool calledByReload = false)
        {
            if (hasReloadAnim && isReloading) //wait for relaod to finish before shutting down
            {
                yield return new WaitWhileFixed(() => reloadState.normalizedTime < 1);
            }
            if (!calledByReload) //allow isreloading to co-opt the startup/shutdown anim without disabling weapon in the process
            {
                weaponState = WeaponStates.PoweringDown;
                UpdateGUIWeaponState();
            }
            else
            {
                guiStatusString = "Reloading";
            }
            BDArmorySetup.Instance.UpdateCursorState();
            if (turret)
            {
                yield return new WaitForSecondsFixed(0.2f);
                yield return new WaitWhileFixed(() => !turret.ReturnTurret()); //wait till turret has returned
            }
            if (hasCharged)
            {
                if (hasChargeAnimation)
                    yield return chargeRoutine = StartCoroutine(ChargeRoutine(postFireChargeAnim));
            }
            if (hasDeployAnim)
            {
                deployState.enabled = true;
                deployState.speed = -1;
                yield return new WaitWhileFixed(() => deployState.normalizedTime > 0);
                deployState.normalizedTime = 0;
                deployState.speed = 0;
                deployState.enabled = false;
            }
            if (!calledByReload)
            {
                weaponState = WeaponStates.Disabled;
                UpdateGUIWeaponState();
            }
        }
        IEnumerator ReloadRoutine()
        {
            guiStatusString = "Reloading";
            hasCharged = false;
            float timeGap = 60 / roundsPerMinute * TimeWarp.CurrentRate;
            float netReloadTime = ReloadTime - timeGap - (postFireChargeAnim && ChargeTime > 0 ? ChargeTime : 0);
            if (hasFireAnimation) yield return new WaitForSecondsFixed(Mathf.Min(timeGap, fireState[0].length / fireAnimSpeed)); //wait for fire anim to finish.
            for (int i = 0; i < fireState.Length; i++)
            {
                fireState[i].normalizedTime = 1;
                fireState[i].speed = 0;
                fireState[i].enabled = false;
                //if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log("[BDArmory.ModuleWeapon]: packing Fire Anim, i = " + i + "; fire anim " + fireState[i].name + "normalizedTime: " + fireState[i].normalizedTime);
            }
            if (hasChargeAnimation && postFireChargeAnim)
            {
                chargeRoutine = StartCoroutine(ChargeRoutine(true));
                yield return new WaitWhileFixed(() => chargeState.normalizedTime > 0); //wait for animation here
            }
            if (!oneShotSound) audioSource.Stop();
            if (!string.IsNullOrEmpty(reloadAudioPath))
            {
                audioSource2.Stop();
                audioSource2.PlayOneShot(reloadAudioClip);
            }
            reloadState.normalizedTime = 0;
            reloadState.enabled = true;
            reloadState.speed = (reloadState.length / netReloadTime);//ensure reload anim is not longer than reload time
            yield return new WaitWhileFixed(() => reloadState.normalizedTime < 1); //wait for animation here
            reloadState.normalizedTime = 1;
            reloadState.speed = 0;
            reloadState.enabled = false;

            UpdateGUIWeaponState();
        }
        IEnumerator ChargeRoutine(bool discharge = false)
        {
            isCharging = true;
            guiStatusString = "Charging";
            if (discharge)
            {
                if (hasChargeHoldAnimation) chargeHoldState.enabled = false;
                if (hasChargeAnimation) chargeState.enabled = false;
                hasCharged = false;
            }
            if (!string.IsNullOrEmpty(chargeSoundPath) && !discharge)
            {
                audioSource.Stop();
                audioSource.PlayOneShot(chargeSound);
            }
            if (hasChargeAnimation)
            {
                chargeState.normalizedTime = discharge ? 1 : 0;
                chargeState.enabled = true;
                chargeState.speed = (chargeState.length / ChargeTime) * (discharge ? -1 : 1);//ensure reload anim is not longer than reload time
                yield return new WaitWhileFixed(() => discharge ? chargeState.normalizedTime > 0 : chargeState.normalizedTime < 1); //wait for animation here
                chargeState.normalizedTime = discharge ? 0 : 1;
                chargeState.speed = 0;
                chargeState.enabled = false;
            }
            else
            {
                yield return new WaitForSecondsFixed(ChargeTime);
            }
            UpdateGUIWeaponState();
            isCharging = false;
            if (!discharge)
            {
                if (!ChargeEachShot) hasCharged = true;
                switch (eWeaponType)
                {
                    case WeaponTypes.Laser:
                        if (FireLaser())
                        {
                            for (int i = 0; i < laserRenderers.Length; i++)
                            {
                                laserRenderers[i].enabled = true;
                            }
                        }
                        else
                        {
                            if ((!pulseLaser && !BurstFire) || (!pulseLaser && BurstFire && (RoundsRemaining >= RoundsPerMag)) || (pulseLaser && timeSinceFired > beamDuration))
                            {
                                for (int i = 0; i < laserRenderers.Length; i++)
                                {
                                    laserRenderers[i].enabled = false;
                                }
                            }
                        }
                        break;
                    case WeaponTypes.Ballistic:
                        Fire();
                        break;
                    case WeaponTypes.Rocket:
                        FireRocket();
                        break;
                }
            }
        }
        IEnumerator StandbyRoutine()
        {
            yield return StartupRoutine(true);
            weaponState = WeaponStates.Standby;
            UpdateGUIWeaponState();
            BDArmorySetup.Instance.UpdateCursorState();
        }
        void StopShutdownStartupRoutines()
        {
            if (shutdownRoutine != null)
            {
                StopCoroutine(shutdownRoutine);
                shutdownRoutine = null;
            }

            if (startupRoutine != null)
            {
                StopCoroutine(startupRoutine);
                startupRoutine = null;
            }

            if (standbyRoutine != null)
            {
                StopCoroutine(standbyRoutine);
                standbyRoutine = null;
            }
        }

        #endregion Updates

        #region Bullets

        void ParseBulletDragType()
        {
            bulletDragTypeName = bulletDragTypeName.ToLower();

            switch (bulletDragTypeName)
            {
                case "none":
                    bulletDragType = BulletDragTypes.None;
                    break;

                case "numericalintegration":
                    bulletDragType = BulletDragTypes.NumericalIntegration;
                    break;

                case "analyticestimate":
                    bulletDragType = BulletDragTypes.AnalyticEstimate;
                    break;
                default:
                    bulletDragType = BulletDragTypes.AnalyticEstimate;
                    break;
            }
        }

        void ParseAPSType(string type)
        {
            type = type.ToLower();
            switch (type)
            {
                case "ballistic":
                    eAPSType = APSTypes.Ballistic;
                    break;
                case "missile":
                    eAPSType = APSTypes.Missile;
                    break;
                case "omni":
                    eAPSType = APSTypes.Omni;
                    break;
                default:
                    eAPSType = APSTypes.None;
                    break;
            }
        }

        public void SetupBulletPool()
        {
            if (bulletPool != null) return;
            GameObject templateBullet = new GameObject("Bullet");
            templateBullet.AddComponent<PooledBullet>();
            templateBullet.SetActive(false);
            bulletPool = ObjectPool.CreateObjectPool(templateBullet, 100, true, true);
        }

        void SetupShellPool()
        {
            GameObject templateShell = GameDatabase.Instance.GetModel("BDArmory/Models/shell/model");
            templateShell.SetActive(false);
            templateShell.AddComponent<ShellCasing>();
            shellPool = ObjectPool.CreateObjectPool(templateShell, 50, true, true);
        }

        public void SetupRocketPool(string name, string modelpath)
        {
            var key = name;
            if (!rocketPool.ContainsKey(key) || rocketPool[key] == null)
            {
                var RocketTemplate = GameDatabase.Instance.GetModel(modelpath);
                if (RocketTemplate == null)
                {
                    Debug.LogError("[BDArmory.ModuleWeapon]: model '" + modelpath + "' not found. Expect exceptions if trying to use this rocket.");
                    return;
                }
                RocketTemplate.SetActive(false);
                RocketTemplate.AddComponent<PooledRocket>();
                rocketPool[key] = ObjectPool.CreateObjectPool(RocketTemplate, 10, true, true);
            }
        }

        public void SetupAmmo(BaseField field, object obj)
        {
            if (useCustomBelt && customAmmoBelt.Count > 0)
            {
                currentType = customAmmoBelt[AmmoIntervalCounter].ToString();
            }
            else
            {
                ammoList = BDAcTools.ParseNames(bulletType);
                currentType = ammoList[(int)AmmoTypeNum - 1].ToString();
            }
            ParseAmmoStats();
        }
        public void ParseAmmoStats()
        {
            if (eWeaponType == WeaponTypes.Ballistic)
            {
                bulletInfo = BulletInfo.bullets[currentType];
                guiAmmoTypeString = ""; //reset name
                maxDeviation = baseDeviation; //reset modified deviation
                caliber = bulletInfo.caliber;
                bulletVelocity = bulletInfo.bulletVelocity;
                bulletMass = bulletInfo.bulletMass;
                ProjectileCount = bulletInfo.projectileCount;
                bulletDragTypeName = bulletInfo.bulletDragTypeName;
                projectileColorC = GUIUtils.ParseColor255(bulletInfo.projectileColor);
                startColorC = GUIUtils.ParseColor255(bulletInfo.startColor);
                fadeColor = bulletInfo.fadeColor;
                //ParseBulletDragType();
                bulletDragType = bulletInfo.bulletDragType;
                //ParseBulletFuzeType(bulletInfo.fuzeType, bulletInfo.tntMass, bulletInfo.beehive);
                eFuzeType = bulletInfo.eFuzeType;
                //ParseBulletHEType(bulletInfo.explosive);
                eHEType = bulletInfo.eHEType;
                tntMass = bulletInfo.tntMass;
                beehive = bulletInfo.beehive;
                Impulse = bulletInfo.impulse;
                massAdjustment = bulletInfo.massMod;
                if (!tracerOverrideWidth)
                {
                    tracerStartWidth = caliber / 300;
                    tracerEndWidth = caliber / 750;
                    nonTracerWidth = caliber / 500;
                }
                SabotRound = bulletInfo.sabot;
                SelectedAmmoType = bulletInfo.name; //store selected ammo name as string for retrieval by web orc filter/later GUI implementation
                if (!useCustomBelt)
                {
                    baseBulletVelocity = bulletVelocity;
                    if (bulletInfo.projectileCount > 1)
                    {
                        guiAmmoTypeString = StringUtils.Localize("#LOC_BDArmory_Ammo_Shot") + " ";
                        //maxDeviation *= Mathf.Clamp(bulletInfo.subProjectileCount/5, 2, 5); //modify deviation if shot vs slug
                        AccAdjust(null, null);
                    }
                    if (bulletInfo.apBulletMod >= 1.1 || SabotRound)
                    {
                        guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_AP") + " ";
                    }
                    else if (bulletInfo.apBulletMod < 1.1 && bulletInfo.apBulletMod > 0.8f)
                    {
                        guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_SAP") + " ";
                    }
                    if (bulletInfo.nuclear)
                    {
                        guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Nuclear") + " ";
                    }
                    if (bulletInfo.tntMass > 0 && !bulletInfo.nuclear)
                    {
                        if (eFuzeType == BulletFuzeTypes.Timed || eFuzeType == BulletFuzeTypes.Proximity || eFuzeType == BulletFuzeTypes.Flak)
                        {
                            guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Flak") + " ";
                        }
                        else if (eHEType == PooledBulletTypes.Shaped)
                        {
                            if (bulletInfo.projectileCount > 1)
                            {
                                guiAmmoTypeString = StringUtils.Localize("#LOC_BDArmory_Ammo_Shot") + " " +
                                                    StringUtils.Localize("#LOC_BDArmory_Ammo_Shaped") + " ";
                            }
                            else
                            {
                                guiAmmoTypeString = StringUtils.Localize("#LOC_BDArmory_Ammo_Shaped") + " ";
                            }
                        }
                        guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Explosive") + " ";
                    }
                    if (bulletInfo.incendiary)
                    {
                        guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Incendiary") + " ";
                    }
                    if (bulletInfo.EMP && !bulletInfo.nuclear)
                    {
                        guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_EMP") + " ";
                    }
                    if (bulletInfo.beehive)
                    {
                        guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Beehive") + " ";
                    }
                    if (bulletInfo.tntMass <= 0 && bulletInfo.apBulletMod <= 0.8)
                    {
                        guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Slug");
                    }
                }
                else
                {
                    guiAmmoTypeString = StringUtils.Localize("#LOC_BDArmory_Ammo_Multiple");
                    if (baseBulletVelocity < 0)
                    {
                        baseBulletVelocity = BulletInfo.bullets[customAmmoBelt[0].ToString()].bulletVelocity;
                    }
                }
            }
            if (eWeaponType == WeaponTypes.Rocket)
            {
                rocketInfo = RocketInfo.rockets[currentType];
                guiAmmoTypeString = ""; //reset name
                rocketMass = rocketInfo.rocketMass;
                caliber = rocketInfo.caliber;
                thrust = rocketInfo.thrust;
                thrustTime = rocketInfo.thrustTime;
                ProjectileCount = rocketInfo.projectileCount;
                rocketModelPath = rocketInfo.rocketModelPath;
                SelectedAmmoType = rocketInfo.name; //store selected ammo name as string for retrieval by web orc filter/later GUI implementation
                beehive = rocketInfo.beehive;
                tntMass = rocketInfo.tntMass;
                Impulse = rocketInfo.force;
                massAdjustment = rocketInfo.massMod;
                if (rocketInfo.projectileCount > 1)
                {
                    guiAmmoTypeString = StringUtils.Localize("#LOC_BDArmory_Ammo_Shot") + " "; // maybe add an int value to these for future Missilefire SmartPick expansion? For now, choose loadouts carefuly!
                }
                if (rocketInfo.nuclear)
                {
                    guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Nuclear") + " ";
                }
                if (rocketInfo.explosive && !rocketInfo.nuclear)
                {
                    if (rocketInfo.flak)
                    {
                        guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Flak") + " ";
                        eFuzeType = BulletFuzeTypes.Flak; //fix rockets not getting detonation range slider 
                    }
                    else if (rocketInfo.shaped)
                    {
                        guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Shaped") + " ";
                    }
                    if (rocketInfo.EMP || rocketInfo.choker || rocketInfo.impulse)
                    {
                        if (rocketInfo.EMP)
                        {
                            guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_EMP") + " ";
                        }
                        if (rocketInfo.choker)
                        {
                            guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Choker") + " ";
                        }
                        if (rocketInfo.impulse)
                        {
                            guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Impulse") + " ";
                        }
                    }
                    else
                    {
                        guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_HE") + " ";
                    }
                    if (rocketInfo.incendiary)
                    {
                        guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Incendiary") + " ";
                    }
                    if (rocketInfo.gravitic)
                    {
                        guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Gravitic") + " ";
                    }
                }
                else
                {
                    guiAmmoTypeString += StringUtils.Localize("#LOC_BDArmory_Ammo_Kinetic");
                }
                if (rocketInfo.flak)
                {
                    proximityDetonation = true;
                }
                else
                {
                    proximityDetonation = false;
                }
                graviticWeapon = rocketInfo.gravitic;
                impulseWeapon = rocketInfo.impulse;
                electroLaser = rocketInfo.EMP; //borrowing electrolaser bool, should really rename it empWeapon
                choker = rocketInfo.choker;
                incendiary = rocketInfo.incendiary;
                SetupRocketPool(currentType, rocketModelPath);
            }
            PAWRefresh();
            SetInitialDetonationDistance();
        }
        protected void SetInitialDetonationDistance()
        {
            if (detonationRange == -1)
            {
                if (eWeaponType == WeaponTypes.Ballistic && bulletInfo.tntMass > 0f && (eFuzeType == BulletFuzeTypes.Proximity || eFuzeType == BulletFuzeTypes.Flak))
                {
                    blastRadius = BlastPhysicsUtils.CalculateBlastRange(bulletInfo.tntMass); //reporting as two so blastradius can be handed over to PooledRocket for detonation/safety stuff
                    detonationRange = beehive ? 100 : blastRadius * 0.666f;
                }
                else if (eWeaponType == WeaponTypes.Rocket && rocketInfo.tntMass > 0f) //don't fire rockets at point blank
                {
                    blastRadius = BlastPhysicsUtils.CalculateBlastRange(rocketInfo.tntMass);
                    detonationRange = beehive ? 100 : blastRadius * 0.666f;
                }
            }
            if (BDArmorySettings.DEBUG_WEAPONS)
            {
                Debug.Log("[BDArmory.ModuleWeapon]: DetonationDistance = : " + detonationRange);
            }
        }

        #endregion Bullets

        #region RMB Info

        public override string GetInfo()
        {
            ammoList = BDAcTools.ParseNames(bulletType);
            StringBuilder output = new StringBuilder();
            output.Append(Environment.NewLine);
            output.AppendLine($"Weapon Type: {weaponType}");

            if (weaponType == "laser")
            {
                if (electroLaser)
                {
                    if (pulseLaser)
                    {
                        output.AppendLine($"Electrolaser EMP damage: {laserDamage}/shot");
                    }
                    else
                    {
                        output.AppendLine($"Electrolaser EMP damage: {laserDamage}/s");
                    }
                    output.AppendLine($"{(electricResource ? "Power" : secondaryAmmoName)} required: {secondaryAmmoPerShot * (pulseLaser ? roundsPerMinute / 60 : 50)}/s");
                }
                else
                {
                    output.AppendLine($"Laser damage: {laserDamage}");
                    if (LaserGrowTime > 0)
                    {
                        output.AppendLine($"-Laser takes: {LaserGrowTime} seconds to reach max power");
                        output.AppendLine($"-Maximum output: {laserMaxDamage} damage");
                    }
                    if (secondaryAmmoPerShot > 0 || ECPerShot > 0)
                    {
                        if (secondaryAmmoPerShot == 0) secondaryAmmoPerShot = ECPerShot;
                        if (pulseLaser)
                        {
                            output.AppendLine($"{secondaryAmmoName} required per shot: {secondaryAmmoPerShot}");
                        }
                        else
                        {
                            output.AppendLine($"{secondaryAmmoName}: {secondaryAmmoPerShot}/s");
                        }
                    }
                    if (requestResourceAmount > 0)
                    {
                        if (pulseLaser)
                        {
                            output.AppendLine($"{ammoName} required per shot: {requestResourceAmount}");
                        }
                        else
                        {
                            output.AppendLine($"{ammoName}: {requestResourceAmount}/s");
                        }
                    }
                }
                if (pulseLaser)
                {
                    output.AppendLine($"Rounds Per Minute: {roundsPerMinute * (fireTransforms?.Length ?? 1)}");
                    if (SpoolUpTime > 0) output.AppendLine($"Weapon requires {SpoolUpTime} seconds to come to max RPM");
                    output.AppendLine($"Shot Deviation: {Mathf.Tan(Mathf.Deg2Rad * maxDeviation) * 1000 * (1.285f / 2) * 2:F2} mrad, 80% hit");
                    if (HEpulses)
                    {
                        output.AppendLine($"Blast:");
                        output.AppendLine($"- tnt mass:  {Math.Round((laserDamage / 10000), 2)} kg");
                        output.AppendLine($"- radius:  {Math.Round(BlastPhysicsUtils.CalculateBlastRange(laserDamage / 10000), 2)} m");
                    }
                }

            }
            else
            {
                output.AppendLine($"Rounds Per Minute: {roundsPerMinute * (fireTransforms?.Length ?? 1)}");
                if (SpoolUpTime > 0) output.AppendLine($"Weapon requires {SpoolUpTime} second" + (SpoolUpTime > 1 ? "s" : "") + " to come to max RPM");
                output.AppendLine();
                output.AppendLine($"Ammunition: {ammoName}");
                if (secondaryAmmoPerShot > 0 || ECPerShot > 0)
                {
                    if (secondaryAmmoPerShot == 0) secondaryAmmoPerShot = ECPerShot;
                    output.AppendLine($"{secondaryAmmoName} required per shot: {secondaryAmmoPerShot}");
                }
                output.AppendLine($"Max Range: {maxEffectiveDistance} m");
                if (minSafeDistance > 0)
                {
                    output.AppendLine($"Min Range: {minSafeDistance} m");
                }
                if (weaponType == "ballistic")
                {
                    output.AppendLine($"Shot Deviation: {Mathf.Tan(Mathf.Deg2Rad * maxDeviation) * 1000 * (1.285f / 2) * 2:F2} mrad, 80% hit");
                    for (int i = 0; i < ammoList.Count; i++)
                    {
                        BulletInfo binfo = BulletInfo.bullets[ammoList[i].ToString()];
                        if (binfo == null)
                        {
                            Debug.LogError("[BDArmory.ModuleWeapon]: The requested bullet type (" + ammoList[i].ToString() + ") does not exist.");
                            output.AppendLine($"Bullet type: {ammoList[i]} - MISSING");
                            output.AppendLine("");
                            continue;
                        }
                        // HE and fuze types parsed here previously despite not needing to be parsed?
                        output.AppendLine("");
                        output.AppendLine($"Bullet type: {(string.IsNullOrEmpty(binfo.DisplayName) ? binfo.name : binfo.DisplayName)}");
                        output.AppendLine($"Bullet mass: {Math.Round(binfo.bulletMass, 2)} kg");
                        output.AppendLine($"Muzzle velocity: {Math.Round(binfo.bulletVelocity, 2)} m/s");
                        //output.AppendLine($"Explosive: {binfo.explosive}");
                        if (binfo.projectileCount > 1)
                        {
                            output.AppendLine($"Cannister Round");
                            output.AppendLine($" - Submunition count: {binfo.projectileCount}");
                        }
                        //bool sabotTemp = PooledBullet.isSabot(binfo.bulletMass, binfo.caliber);

                        output.AppendLine($"Estimated Penetration: {ProjectileUtils.CalculatePenetration(binfo.caliber, binfo.bulletVelocity, binfo.bulletMass, binfo.apBulletMod, muParam1: binfo.sabot ? 0.9470311374f : 0.656060636f, muParam2: binfo.sabot ? 1.555757746f : 1.20190930f, muParam3: binfo.sabot ? 2.753715499f : 1.77791929f, sabot: binfo.sabot):F2} mm");
                        if ((binfo.tntMass > 0) && !binfo.nuclear)
                        {
                            output.AppendLine($"Blast:");
                            output.AppendLine($"- tnt mass:  {Math.Round(binfo.tntMass, 3)} kg");
                            output.AppendLine($"- radius:  {Math.Round(BlastPhysicsUtils.CalculateBlastRange(binfo.tntMass), 2)} m");
                            output.AppendLine($"- fuze type: {binfo.eFuzeType switch
                                {
                                    BulletFuzeTypes.None => "None",
                                    BulletFuzeTypes.Impact => "Impact",
                                    BulletFuzeTypes.Timed => "Timed",
                                    BulletFuzeTypes.Proximity => "Proximity",
                                    BulletFuzeTypes.Flak => "Flak",
                                    BulletFuzeTypes.Delay => "Delayed",
                                    BulletFuzeTypes.Penetrating => "Penetrating",
                                    _ => "Unknown"
                                }}");
                            if (binfo.eFuzeType == BulletFuzeTypes.Timed || binfo.eFuzeType == BulletFuzeTypes.Proximity || binfo.eFuzeType == BulletFuzeTypes.Flak)
                            {
                                output.AppendLine($"Air detonation: True");
                                output.AppendLine($"- auto timing: {(binfo.eFuzeType != BulletFuzeTypes.Proximity)}");
                                output.AppendLine($"- max range: {maxTargetingRange} m");
                            }
                            else
                            {
                                output.AppendLine($"Air detonation: False");
                            }

                            if (binfo.eHEType == PooledBulletTypes.Shaped)
                                output.AppendLine($"Shaped Charge Penetration: {ProjectileUtils.CalculatePenetration(binfo.caliber > 0 ? binfo.caliber * 0.05f : 6f, 5000f, binfo.tntMass * 0.0555f, binfo.apBulletMod):F2} mm");
                        }
                        if (binfo.nuclear)
                        {
                            output.AppendLine($"Nuclear Shell:");
                            output.AppendLine($"- yield:  {Math.Round(binfo.tntMass, 3)} kT");
                            if (binfo.EMP)
                            {
                                output.AppendLine($"- generates EMP");
                            }
                        }
                        if (binfo.EMP && !binfo.nuclear)
                        {
                            output.AppendLine($"BlueScreen:");
                            output.AppendLine($"- EMP buildup per hit:{binfo.caliber * Mathf.Clamp(bulletMass - tntMass, 0.1f, 100)}");
                        }
                        if (binfo.impulse != 0)
                        {
                            output.AppendLine($"Concussive:");
                            output.AppendLine($"- Impulse to target:{Impulse}");
                        }
                        if (binfo.massMod != 0)
                        {
                            output.AppendLine($"Gravitic:");
                            output.AppendLine($"- weight added per hit:{massAdjustment * 1000} kg");
                        }
                        if (binfo.incendiary)
                        {
                            output.AppendLine($"Incendiary");
                        }
                        if (binfo.beehive)
                        {
                            output.AppendLine($"Beehive Shell:");
                            string[] subMunitionData = binfo.subMunitionType.Split(new char[] { ';' });
                            string projType = subMunitionData[0];
                            if (subMunitionData.Length < 2 || !int.TryParse(subMunitionData[1], out int count)) count = 1;
                            BulletInfo sinfo = BulletInfo.bullets[projType];
                            output.AppendLine($"- deploys {count}x {(string.IsNullOrEmpty(sinfo.DisplayName) ? sinfo.name : sinfo.DisplayName)}");
                        }
                    }
                }
                if (weaponType == "rocket")
                {
                    for (int i = 0; i < ammoList.Count; i++)
                    {
                        RocketInfo rinfo = RocketInfo.rockets[ammoList[i].ToString()];
                        if (rinfo == null)
                        {
                            Debug.LogError("[BDArmory.ModuleWeapon]: The requested rocket type (" + ammoList[i].ToString() + ") does not exist.");
                            output.AppendLine($"Rocket type: {ammoList[i]} - MISSING");
                            output.AppendLine("");
                            continue;
                        }
                        output.AppendLine($"Rocket type: {(string.IsNullOrEmpty(rinfo.DisplayName) ? rinfo.name : rinfo.DisplayName)}");
                        output.AppendLine($"Rocket mass: {Math.Round(rinfo.rocketMass * 1000, 2)} kg");
                        //output.AppendLine($"Thrust: {thrust}kn"); mass and thrust don't really tell us the important bit, so lets replace that with accel
                        output.AppendLine($"Acceleration: {rinfo.thrust / rinfo.rocketMass}m/s2");
                        if (rinfo.explosive && !rinfo.nuclear)
                        {
                            output.AppendLine($"Blast:");
                            output.AppendLine($"- tnt mass:  {Math.Round((rinfo.tntMass), 3)} kg");
                            output.AppendLine($"- radius:  {Math.Round(BlastPhysicsUtils.CalculateBlastRange(rinfo.tntMass), 2)} m");
                            output.AppendLine($"Proximity Fuzed: {rinfo.flak}");
                            if (rinfo.shaped)
                                output.AppendLine($"Estimated Penetration: {ProjectileUtils.CalculatePenetration(rinfo.caliber > 0 ? rinfo.caliber * 0.05f : 6f, 5000f, rinfo.tntMass * 0.0555f, rinfo.apMod):F2} mm");
                        }
                        if (rinfo.nuclear)
                        {
                            output.AppendLine($"Nuclear Rocket:");
                            output.AppendLine($"- yield:  {Math.Round(rinfo.tntMass, 3)} kT");
                            if (rinfo.EMP)
                            {
                                output.AppendLine($"- generates EMP");
                            }
                        }
                        output.AppendLine("");
                        if (rinfo.projectileCount > 1)
                        {
                            output.AppendLine($"Cluster Rocket");
                            output.AppendLine($" - Submunition count: {rinfo.projectileCount}");
                        }
                        if (impulseWeapon || graviticWeapon || choker || electroLaser || incendiary)
                        {
                            output.AppendLine($"Special Weapon:");
                            if (impulseWeapon)
                            {
                                output.AppendLine($"Concussion warhead:");
                                output.AppendLine($"- Impulse to target:{Impulse}");
                            }
                            if (graviticWeapon)
                            {
                                output.AppendLine($"Gravitic warhead:");
                                output.AppendLine($"- Mass added per part hit:{massAdjustment * 1000} kg");
                            }
                            if (electroLaser && !rinfo.nuclear)
                            {
                                output.AppendLine($"EMP warhead:");
                                output.AppendLine($"- can temporarily shut down targets");
                            }
                            if (choker)
                            {
                                output.AppendLine($"Atmospheric Deprivation Warhead:");
                                output.AppendLine($"- Will temporarily knock out air intakes");
                            }
                            if (incendiary)
                            {
                                output.AppendLine($"Incendiary:");
                                output.AppendLine($"- Covers targets in inferno gel");
                            }
                            if (rinfo.beehive)
                            {
                                output.AppendLine($"Cluster Rocket:");
                                string[] subMunitionData = rinfo.subMunitionType.Split(new char[] { ';' });
                                string projType = subMunitionData[0];
                                if (subMunitionData.Length < 2 || !int.TryParse(subMunitionData[1], out int count)) count = 1;
                                if (BulletInfo.bulletNames.Contains(projType))
                                {
                                    BulletInfo sinfo = BulletInfo.bullets[projType];
                                    output.AppendLine($"- deploys {count}x {(string.IsNullOrEmpty(sinfo.DisplayName) ? sinfo.name : sinfo.DisplayName)}");
                                }
                                else if (RocketInfo.rocketNames.Contains(projType))
                                {
                                    RocketInfo sinfo = RocketInfo.rockets[projType];
                                    output.AppendLine($"- deploys {count}x {(string.IsNullOrEmpty(sinfo.DisplayName) ? sinfo.name : sinfo.DisplayName)}");
                                }
                            }
                        }


                    }
                    if (externalAmmo)
                    {
                        output.AppendLine($"Uses External Ammo");
                    }

                }
            }
            output.AppendLine("");
            if (BurstFire)
            {
                output.AppendLine($"Burst Fire Weapon");
                output.AppendLine($" - Rounds Per Burst: {RoundsPerMag}");
            }
            if (!BeltFed && !BurstFire)
            {
                output.AppendLine($" Reloadable");
                output.AppendLine($" - Shots before Reload: {RoundsPerMag}");
                output.AppendLine($" - Reload Time: {ReloadTime}");
            }
            if (crewserved)
            {
                output.AppendLine($"Crew-served Weapon - Requires onboard Kerbal");
            }
            if (isAPS)
            {
                output.AppendLine($"Autonomous Point Defense Weapon");
                output.AppendLine($" - Interception type: {APSType}");
                if (dualModeAPS) output.AppendLine($" - Dual purpose; can be used offensively");
            }
            return output.ToString();
        }

        #endregion RMB Info
    }

    #region UI //borrowing code from ModularMissile GUI

    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class WeaponGroupWindow : MonoBehaviour
    {
        internal static EventVoid OnActionGroupEditorOpened = new EventVoid("OnActionGroupEditorOpened");
        internal static EventVoid OnActionGroupEditorClosed = new EventVoid("OnActionGroupEditorClosed");

        private static GUIStyle unchanged;
        private static GUIStyle changed;
        private static GUIStyle greyed;
        private static GUIStyle overfull;

        private static WeaponGroupWindow instance;

        private bool ActionGroupMode;

        private Rect guiWindowRect = new Rect(0, 0, 0, 0);

        private ModuleWeapon WPNmodule;

        [KSPField] public int offsetGUIPos = -1;

        private Vector2 scrollPos;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiActive = false, guiName = "#LOC_BDArmory_ShowGroupEditor"), UI_Toggle(enabledText = "#LOC_BDArmory_ShowGroupEditor_enabledText", disabledText = "#LOC_BDArmory_ShowGroupEditor_disabledText")][NonSerialized] public bool showRFGUI;//Show Group Editor--close Group GUI--open Group GUI

        private bool styleSetup;

        private string txtName = string.Empty;

        public static void HideGUI()
        {
            // Doing it this way prevents OnGUI events from below the window from being triggered by the window disappearing.
            if (instance != null) instance.StartCoroutine(instance.HideGUIAtEndOfFrame());
            else GUIUtils.PreventClickThrough(default, "BD_MN_GUILock", true);
        }
        bool waitingForEndOfFrame = false;
        IEnumerator HideGUIAtEndOfFrame()
        {
            if (waitingForEndOfFrame) yield break;
            waitingForEndOfFrame = true;
            yield return new WaitForEndOfFrame();
            waitingForEndOfFrame = false;
            if (instance != null && instance.WPNmodule != null)
            {
                instance.WPNmodule.WeaponDisplayName = instance.WPNmodule.shortName;
                instance.WPNmodule = null;
                instance.applyWeaponGroupTo = null;
                instance.UpdateGUIState();
            }
            GUIUtils.PreventClickThrough(default, "BD_MN_GUILock", true);
        }

        public static void ShowGUI(ModuleWeapon WPNmodule)
        {
            if (instance != null)
            {
                instance.WPNmodule = WPNmodule;
                instance.UpdateGUIState();
            }
            instance.applyWeaponGroupTo = new string[] { "this weapon", "symmetric weapons", $"all {WPNmodule.part.partInfo.title}s", $"all {WPNmodule.GetWeaponClass()}s", "all Guns/Rockets/Lasers" };
            instance._applyWeaponGroupTo = instance.applyWeaponGroupTo[instance._applyWeaponGroupToIndex];
        }

        private void UpdateGUIState()
        {
            enabled = WPNmodule != null;
        }

        private IEnumerator<YieldInstruction> CheckActionGroupEditor()
        {
            while (EditorLogic.fetch == null)
            {
                yield return null;
            }
            EditorLogic editor = EditorLogic.fetch;
            while (EditorLogic.fetch != null)
            {
                if (editor.editorScreen == EditorScreen.Actions)
                {
                    if (!ActionGroupMode)
                    {
                        HideGUI();
                        OnActionGroupEditorOpened.Fire();
                    }
                    EditorActionGroups age = EditorActionGroups.Instance;
                    if (WPNmodule && !age.GetSelectedParts().Contains(WPNmodule.part))
                    {
                        HideGUI();
                    }
                    ActionGroupMode = true;
                }
                else
                {
                    if (ActionGroupMode)
                    {
                        HideGUI();
                        OnActionGroupEditorClosed.Fire();
                    }
                    ActionGroupMode = false;
                }
                yield return null;
            }
        }

        private void Awake()
        {
            enabled = false;
            instance = this;
        }

        private void OnDestroy()
        {
            instance = null;
            GUIUtils.PreventClickThrough(guiWindowRect, "BD_MN_GUILock", true);
        }

        public void OnGUI()
        {
            if (!styleSetup)
            {
                styleSetup = true;
                Styles.InitStyles();
            }

            EditorLogic editor = EditorLogic.fetch;
            if (!HighLogic.LoadedSceneIsEditor || !editor)
            {
                return;
            }

            int posMult = 0;
            if (offsetGUIPos != -1)
            {
                posMult = offsetGUIPos;
            }
            if (ActionGroupMode)
            {
                if (guiWindowRect.width == 0)
                {
                    guiWindowRect = new Rect(430 * posMult, 365, 438, 50);
                }
                new Rect(guiWindowRect.xMin + 440, Screen.height - Input.mousePosition.y - 5, 300, 20);
            }
            else
            {
                if (guiWindowRect.width == 0)
                {
                    //guiWindowRect = new Rect(Screen.width - 8 - 430 * (posMult + 1), 365, 438, (Screen.height - 365));
                    guiWindowRect = new Rect(Screen.width - 8 - 430 * (posMult + 1), 365, 438, 50);
                }
                new Rect(guiWindowRect.xMin - (230 - 8), Screen.height - Input.mousePosition.y - 5, 220, 20);
            }
            if (BDArmorySettings._UI_SCALE != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings._UI_SCALE * Vector2.one, guiWindowRect.position);
            guiWindowRect = GUILayout.Window(GUIUtility.GetControlID(FocusType.Passive), guiWindowRect, GUIWindow, StringUtils.Localize("#LOC_BDArmory_WeaponGroup"), Styles.styleEditorPanel);
        }

        string[] applyWeaponGroupTo;
        string _applyWeaponGroupTo;
        int _applyWeaponGroupToIndex = 0;
        public void GUIWindow(int windowID)
        {
            GUIUtils.PreventClickThrough(guiWindowRect, "BD_MN_GUILock");
            InitializeStyles();

            GUILayout.BeginVertical();
            GUILayout.Space(20);

            GUILayout.BeginHorizontal();

            GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_WeaponGroup")} ");

            txtName = GUILayout.TextField(txtName);

            if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_saveClose")))
            {
                string newName = string.IsNullOrEmpty(txtName.Trim()) ? WPNmodule.OriginalShortName : txtName.Trim();

                switch (_applyWeaponGroupToIndex)
                {
                    case 0:
                        WPNmodule.WeaponDisplayName = newName;
                        WPNmodule.shortName = newName;
                        break;
                    case 1: // symmetric parts
                        WPNmodule.WeaponDisplayName = newName;
                        WPNmodule.shortName = newName;
                        foreach (Part p in WPNmodule.part.symmetryCounterparts)
                        {
                            var wpn = p.GetComponent<ModuleWeapon>();
                            if (wpn == null) continue;
                            wpn.WeaponDisplayName = newName;
                            wpn.shortName = newName;
                        }
                        break;
                    case 2: // all weapons of the same type
                        foreach (Part p in EditorLogic.fetch.ship.parts)
                        {
                            if (p.name == WPNmodule.part.name)
                            {
                                var wpn = p.GetComponent<ModuleWeapon>();
                                if (wpn == null) continue;
                                wpn.WeaponDisplayName = newName;
                                wpn.shortName = newName;
                            }
                        }
                        break;
                    case 3: // all weapons of the same class
                        var wpnClass = WPNmodule.GetWeaponClass();
                        foreach (Part p in EditorLogic.fetch.ship.parts)
                        {
                            var wpn = p.GetComponent<ModuleWeapon>();
                            if (wpn == null) continue;
                            if (wpn.isAPS && !wpn.dualModeAPS) continue;
                            if (wpn.GetWeaponClass() != wpnClass) continue;
                            wpn.WeaponDisplayName = newName;
                            wpn.shortName = newName;
                        }
                        break;
                    case 4: // all guns/rockets/lasers
                        var gunsRocketsLasers = new HashSet<WeaponClasses> { WeaponClasses.Gun, WeaponClasses.Rocket, WeaponClasses.DefenseLaser };
                        foreach (Part p in EditorLogic.fetch.ship.parts)
                        {
                            var wpn = p.GetComponent<ModuleWeapon>();
                            if (wpn == null) continue;
                            if (!gunsRocketsLasers.Contains(wpn.GetWeaponClass())) continue;
                            wpn.WeaponDisplayName = newName;
                            wpn.shortName = newName;
                        }
                        break;
                }
                instance.WPNmodule.HideUI();
            }

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_applyTo")} {_applyWeaponGroupTo}");
            if (_applyWeaponGroupToIndex != (_applyWeaponGroupToIndex = Mathf.RoundToInt(GUILayout.HorizontalSlider(_applyWeaponGroupToIndex, 0, 4, GUILayout.Width(150))))) _applyWeaponGroupTo = applyWeaponGroupTo[_applyWeaponGroupToIndex];
            GUILayout.EndHorizontal();

            scrollPos = GUILayout.BeginScrollView(scrollPos);

            GUILayout.EndScrollView();

            GUILayout.EndVertical();

            GUI.DragWindow();
            GUIUtils.RepositionWindow(ref guiWindowRect);
        }

        private static void InitializeStyles()
        {
            if (unchanged == null)
            {
                if (GUI.skin == null)
                {
                    unchanged = new GUIStyle();
                    changed = new GUIStyle();
                    greyed = new GUIStyle();
                    overfull = new GUIStyle();
                }
                else
                {
                    unchanged = new GUIStyle(GUI.skin.textField);
                    changed = new GUIStyle(GUI.skin.textField);
                    greyed = new GUIStyle(GUI.skin.textField);
                    overfull = new GUIStyle(GUI.skin.label);
                }

                unchanged.normal.textColor = Color.white;
                unchanged.active.textColor = Color.white;
                unchanged.focused.textColor = Color.white;
                unchanged.hover.textColor = Color.white;

                changed.normal.textColor = Color.yellow;
                changed.active.textColor = Color.yellow;
                changed.focused.textColor = Color.yellow;
                changed.hover.textColor = Color.yellow;

                greyed.normal.textColor = Color.gray;

                overfull.normal.textColor = Color.red;
            }
        }
    }

    #endregion UI //borrowing code from ModularMissile GUI
}
