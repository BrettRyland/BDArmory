using KSP.Localization;
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
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Weapons.Missiles;
using BDArmory.WeaponMounts;

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
            Rocket, //Cannon's depreciated, lets use this for rocketlaunchers
            Laser
        }

        public enum WeaponStates
        {
            Enabled,
            Disabled,
            PoweringUp,
            PoweringDown,
            Locked,
            Standby // Not currently firing, but can still track the current target.
        }

        public enum BulletDragTypes
        {
            None,
            AnalyticEstimate,
            NumericalIntegration
        }

        public enum FuzeTypes
        {
            None,       //So very tempted to have none be 'no fuze', and HE rounds with fuzetype = None act just like standard slug rounds
            Timed,      //detonates after set flighttime. Main use case probably AA, assume secondary contact fuze
            Proximity,  //detonates when in proximity to target. No need for secondary contact fuze
            Flak,       //detonates when in proximity or after set flighttime. Again, shouldn't need secondary contact fuze
            Delay,      //detonates 0.02s after any impact. easily defeated by whipple shields
            Penetrating,//detonates 0.02s after penetrating a minimum thickness of armor. will ignore lightly armored/soft hits
            Impact      //standard contact + graze fuze, detonates on hit
            //Laser     //laser-guided smart rounds?
        }
        public enum APSTypes
        {
            Ballistic,
            Missile,
            Omni
        }
        public WeaponStates weaponState = WeaponStates.Disabled;

        //animations
        private float fireAnimSpeed = 1;
        //is set when setting up animation so it plays a full animation for each shot (animation speed depends on rate of fire)

        public float bulletBallisticCoefficient;

        public WeaponTypes eWeaponType;

        public FuzeTypes eFuzeType;

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
                var fireTransform = (eWeaponType == WeaponTypes.Rocket && rocketPod) ? rockets[0].parent : fireTransforms[0];
                var theta = FiringTolerance * targetRadius / (finalAimTarget - fireTransform.position).magnitude + Mathf.Deg2Rad * maxDeviation / 2f; // Approximation to arctan(α*r/d) + θ/2. (arctan(x) = x-x^3/3 + O(x^5))
                return finalAimTarget.IsZero() ? 1f : 1f - 0.5f * theta * theta; // Approximation to cos(theta). (cos(x) = 1-x^2/2!+O(x^4))
            }
        }
        public Vector3 targetPosition;
        private Vector3 targetVelocity;  // local frame velocity
        private Vector3 targetAcceleration; // local frame
        private Vector3 targetVelocityPrevious; // for acceleration calculation
        // private Vector3 targetAccelerationPrevious;
        private Vector3 targetVelocityS1;
        private Vector3 targetVelocityS2;
        private Vector3 targetAccelerationS1;
        private Vector3 targetAccelerationS2;
        private bool targetAccelerationWasReset = true;
        // private Vector3 relativeVelocity;
        public Vector3 finalAimTarget;
        Vector3 lastFinalAimTarget;
        public Vessel visualTargetVessel;
        public Vessel lastVisualTargetVessel;
        public Part visualTargetPart;
        PooledBullet tgtShell = null;
        PooledRocket tgtRocket = null;
        Vector3 closestTarget = Vector3.zero;
        Vector3 tgtVelocity = Vector3.zero;
        TargetInfo MissileTgt = null;

        private int targetID = 0;
        bool targetAcquired;

        public bool targetCOM = true;
        public bool targetCockpits = false;
        public bool targetEngines = false;
        public bool targetWeapons = false;
        public bool targetMass = false;

        int layerMask1 = (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.EVA | LayerMasks.Unknown19 | LayerMasks.Unknown23); // Why 19 and 23?
        int layerMask2 = (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.Unknown19); // Why 19 and why not the other layer mask?

        enum TargetAcquisitionType { None, Visual, Slaved, Radar, AutoProxy };
        TargetAcquisitionType targetAcquisitionType = TargetAcquisitionType.None;
        TargetAcquisitionType lastTargetAcquisitionType = TargetAcquisitionType.None;
        float lastGoodTargetTime = 0;

        public Vector3? FiringSolutionVector => finalAimTarget.IsZero() ? (Vector3?)null : (finalAimTarget - fireTransforms[0].position).normalized;

        public bool recentlyFiring //used by guard to know if it should evaid this
        {
            get { return Time.time - timeFired < 1; }
        }

        //used to reduce volume of audio if multiple guns are being fired (needs to be improved/changed)
        //private int numberOfGuns = 0;

        //AI will fire gun if target is within this Cos(angle) of barrel
        public float maxAutoFireCosAngle = 0.9993908f; //corresponds to ~2 degrees

        //aimer textures
        Vector3 pointingAtPosition;
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
            ammoLeft = "Ammo Left: " + ammoCount.ToString("0");
            int lastAmmoID = this.AmmoID;
            using (var weapon = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                while (weapon.MoveNext())
                {
                    if (weapon.Current == null) continue;
                    if (weapon.Current.GetShortName() != this.GetShortName()) continue;
                    if (weapon.Current.AmmoID != this.AmmoID && weapon.Current.AmmoID != lastAmmoID)
                    {
                        vessel.GetConnectedResourceTotals(weapon.Current.AmmoID, out double ammoCurrent, out double ammoMax);
                        ammoLeft += "; " + ammoCurrent.ToString("0");
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

        public bool resourceSteal = false;
        public float strengthMutator = 1;
        public bool instagib = false;

#if DEBUG
        Vector3 debugTargetPosition;
        Vector3 debugLastTargetPosition;
        Vector3 debugRelVelAdj;
        Vector3 debugAccAdj;
        Vector3 debugGravAdj;
#endif

        #endregion Declarations

        #region KSPFields

        [KSPField(isPersistant = true, guiActive = true, guiName = "#LOC_BDArmory_WeaponName", guiActiveEditor = true), UI_Label(affectSymCounterparts = UI_Scene.All, scene = UI_Scene.All)]//Weapon Name 
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
            UI_FloatRange(minValue = 1f, maxValue = 100f, stepIncrement = 1, scene = UI_Scene.All)]
        public float fireBurstLength = 1;

        [KSPField(isPersistant = true)]
        public bool FireAngleOverride = false;

        [KSPField(advancedTweakable = true, isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_FiringAngle"),
            UI_FloatRange(minValue = 0f, maxValue = 4, stepIncrement = 0.05f, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        public float FiringTolerance = 1.0f; //per-weapon override of maxcosfireangle

        [KSPField]
        public float maxTargetingRange = 2000; //max range for raycasting and sighting

        [KSPField]
        public float SpoolUpTime = -1; //barrel spin-up period for electric-driven rotary cannon and similar
        float spooltime = 0;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Rate of Fire"),
            UI_FloatRange(minValue = 100f, maxValue = 1500, stepIncrement = 25f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float roundsPerMinute = 650; //rocket RoF slider

        public float baseRPM;

        [KSPField]
        public bool isChaingun = false; //does the gun have adjustable RoF

        [KSPField]
        public float maxDeviation = 1; //inaccuracy two standard deviations in degrees (two because backwards compatibility :)
        public float baseDeviation = 1;

        [KSPField]
        public float maxEffectiveDistance = 2500; //used by AI to select appropriate weapon

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

        [KSPField]
        public float ECPerShot = 0; //EC to use per shot for weapons like railguns

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
        [KSPField] public bool externalAmmo = false; //used for rocketlaunchers that are Gyrojet guns drawing from ammoboxes instead of internals 
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
        public bool tracerOverrideWidth = false;

        int tracerIntervalCounter;

        [KSPField]
        public string bulletTexturePath = "BDArmory/Textures/bullet";

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
        public string chargeSoundPath = "BDArmory/Parts/laserTest/sounds/charge";

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

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ProximityFuzeRadius"), UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Proximity Fuze Radius
        public float detonationRange = -1f; // give ability to set proximity range

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_MaxDetonationRange"),//Max Detonation Range
        UI_FloatRange(minValue = 500, maxValue = 8000f, stepIncrement = 5f, scene = UI_Scene.All)]
        public float maxAirDetonationRange = 3500; // could probably get rid of this entirely, max engagement range more or less already does this

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
                Events["ToggleAmmoConfig"].guiName = Localizer.Format("#LOC_BDArmory_advanced");//"Advanced Ammo Config"
                Events["ConfigAmmo"].guiActive = true;
                Events["ConfigAmmo"].guiActiveEditor = true;
                Fields["AmmoTypeNum"].guiActive = false;
                Fields["AmmoTypeNum"].guiActiveEditor = false;
            }
            else
            {
                Events["ToggleAmmoConfig"].guiName = Localizer.Format("#LOC_BDArmory_simple");//"Simple Ammo Config
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
        public string SelectedAmmoType; //presumably Aubranium can use this to filter allowed/banned ammotypes

        public List<string> ammoList;

        [KSPField(isPersistant = true)]
        public string ammoBelt = "def";

        public List<string> customAmmoBelt;

        int AmmoIntervalCounter = 0;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_Ammo_LoadedAmmo")]//Status
        public string guiAmmoTypeString = Localizer.Format("#LOC_BDArmory_Ammo_Slug");

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_DeployableWeapon"), // In custom/modded "cargo bay"
            UI_ChooseOption(
            options = new String[] {
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
            display = new String[] {
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
        bool atprAcquired;
        int aptrTicker;

        float timeFired;
        public float initialFireDelay = 0; //used to ripple fire multiple weapons of this type

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

        [KSPField]
        public string APSType = "missile"; //missile/ballistic/omni

        IEnumerator IncrementRippleIndex(float delay)
        {
            if (isRippleFiring) delay = 0;
            if (delay > 0)
            {
                yield return new WaitForSeconds(delay);
            }
            if (weaponManager == null) yield break;
            weaponManager.gunRippleIndex = weaponManager.gunRippleIndex + 1;

            //Debug.Log("[BDArmory.ModuleWeapon]: incrementing ripple index to: " + weaponManager.gunRippleIndex);
        }

        int barrelIndex = 0;

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
        #endregion KSPActions

        #region KSP Events

        public override void OnAwake()
        {
            base.OnAwake();

            part.stagingIconAlwaysShown = true;
            this.part.stackIconGrouping = StackIconGrouping.SAME_TYPE;
        }

        public void Start()
        {
            part.stagingIconAlwaysShown = true;
            this.part.stackIconGrouping = StackIconGrouping.SAME_TYPE;

            Events["HideUI"].active = false;
            Events["ShowUI"].active = true;
            ParseWeaponType(weaponType);

            // extension for feature_engagementenvelope
            if (isAPS)
            {
                HideEngageOptions();
                Events["ShowUI"].active = false;
                Events["HideUI"].active = false;
                Events["Toggle"].active = false;
                ParseAPSType(APSType);
            }
            InitializeEngagementRange(0, maxEffectiveDistance);
            if (string.IsNullOrEmpty(GetShortName()))
            {
                shortName = part.partInfo.title;
            }
            OriginalShortName = shortName;
            WeaponName = shortName;
            using (var emitter = part.FindModelComponents<KSPParticleEmitter>().AsEnumerable().GetEnumerator())
                while (emitter.MoveNext())
                {
                    if (emitter.Current == null) continue;
                    emitter.Current.emit = false;
                    EffectBehaviour.AddParticleEmitter(emitter.Current);
                }
            baseRPM = roundsPerMinute;

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
                                if (weapon.Current.GetShortName() != this.GetShortName()) continue;
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
                UI_FloatRange RPMEditor = (UI_FloatRange)Fields["roundsPerMinute"].uiControlEditor;
                if (isChaingun)
                {
                    RPMEditor.maxValue = baseRPM;
                    RPMEditor.minValue = baseRPM / 2;
                    RPMEditor.onFieldChanged = AccAdjust;
                }
            }

            int typecount = 0;
            ammoList = BDAcTools.ParseNames(bulletType);
            for (int i = 0; i < ammoList.Count; i++)
            {
                typecount++;
            }
            if (ammoList.Count > 1)
            {
                if (!canHotSwap)
                {
                    Fields["AmmoTypeNum"].guiActive = false;
                }
                UI_FloatRange ATrangeEditor = (UI_FloatRange)Fields["AmmoTypeNum"].uiControlEditor;
                ATrangeEditor.maxValue = (float)typecount;
                ATrangeEditor.onFieldChanged = SetupAmmo;
                UI_FloatRange ATrangeFlight = (UI_FloatRange)Fields["AmmoTypeNum"].uiControlFlight;
                ATrangeFlight.maxValue = (float)typecount;
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
            vessel.Velocity();
            if (BurstFire)
            {
                BeltFed = false;
            }
            if (eWeaponType == WeaponTypes.Ballistic)
            {
                UI_FloatRange detRange = (UI_FloatRange)Fields["maxAirDetonationRange"].uiControlEditor;
                detRange.maxValue = maxEffectiveDistance; //altitude fuzing clamped to max range

                rocketPod = false;
            }
            if (eWeaponType == WeaponTypes.Rocket)
            {
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
                Fields["maxAirDetonationRange"].guiActive = false;
                Fields["maxAirDetonationRange"].guiActiveEditor = false;
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
                        EffectBehaviour.AddParticleEmitter(kpe);
                        muzzleFlashEmitters.Add(kpe);
                        kpe.emit = false;
                    }
                muzzleFlashList.Add(muzzleFlashEmitters);
            }
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (eWeaponType == WeaponTypes.Ballistic)
                {
                    if (bulletPool == null)
                    {
                        SetupBulletPool();
                    }
                    if (shellPool == null)
                    {
                        SetupShellPool();
                    }
                    if (useCustomBelt)
                    {
                        if (!string.IsNullOrEmpty(ammoBelt) && ammoBelt != "def")
                        {
                            customAmmoBelt = BDAcTools.ParseNames(ammoBelt);
                            List<string> testAmmo = BDAcTools.ParseNames(bulletType);
                            for (int i = 0; i < customAmmoBelt.Count; i++)
                            {
                                bool validAmmo = false;
                                for (int t = 0; t < testAmmo.Count; t++)
                                {
                                    if (customAmmoBelt[i].Contains(testAmmo[t]))
                                    {
                                        validAmmo = true;
                                        break;
                                    }
                                    if (!validAmmo) customAmmoBelt[i] = testAmmo[0];
                                }
                            }
                            baseBulletVelocity = BulletInfo.bullets[customAmmoBelt[0].ToString()].bulletVelocity;
                        }
                        else
                        {
                            customAmmoBelt = BDAcTools.ParseNames(bulletType);
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
                shellEjectTransforms = part.FindModelTransforms(shellEjectTransformName);

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
                    chargeSound = GameDatabase.Instance.GetAudioClip(chargeSoundPath);
                }
                // Setup gauges
                gauge = (BDStagingAreaGauge)part.AddModule("BDStagingAreaGauge");
                gauge.AmmoName = ammoName;
                gauge.AudioSource = audioSource;
                gauge.ReloadAudioClip = reloadAudioClip;
                gauge.ReloadCompleteAudioClip = reloadCompleteAudioClip;

                AmmoID = PartResourceLibrary.Instance.GetDefinition(ammoName).id;

                //laser setup
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
                    ATrangeFlight.maxValue = (float)typecount;
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
                WeaponNameWindow.OnActionGroupEditorOpened.Add(OnActionGroupEditorOpened);
                WeaponNameWindow.OnActionGroupEditorClosed.Add(OnActionGroupEditorClosed);
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
                }
            }
            //setup animations
            if (hasDeployAnim)
            {
                deployState = GUIUtils.SetUpSingleAnimation(deployAnimName, part);
                deployState.normalizedTime = 0;
                deployState.speed = 0;
                deployState.enabled = true;
                ReloadAnimTime = (ReloadTime - deployState.length);
            }
            if (hasReloadAnim)
            {
                reloadState = GUIUtils.SetUpSingleAnimation(reloadAnimName, part);
                reloadState.normalizedTime = 0;
                reloadState.speed = 0;
                reloadState.enabled = true;
            }
            if (hasChargeAnimation)
            {
                chargeState = GUIUtils.SetUpSingleAnimation(chargeAnimName, part);
                chargeState.normalizedTime = 0;
                chargeState.speed = 0;
                chargeState.enabled = true;
            }
            if (hasFireAnimation)
            {
                List<string> animList = BDAcTools.ParseNames(fireAnimName);
                fireState = new AnimationState[animList.Count]; //this should become animList.Count, for cases where there's a multibarrel weapon with a single fireanim
                //for (int i = 0; i < fireTransforms.Length; i++)
                for (int i = 0; i < animList.Count; i++)
                {
                    try
                    {
                        fireState[i] = GUIUtils.SetUpSingleAnimation(animList[i].ToString(), part);
                        //Debug.Log("[BDArmory.ModuleWeapon] Added fire anim " + i);
                        fireState[i].enabled = false;
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
                        Debug.Log("[BDArmory.ModuleWeapon]: Failed To load rocket : " + currentType);
                    }
                    else
                    {
                        if (BDArmorySettings.DEBUG_WEAPONS)
                            Debug.Log("[BDArmory.ModuleWeapon]: AmmoType Loaded : " + currentType);
                        if (beehive)
                        {
                            if (!BulletInfo.bulletNames.Contains(rocketInfo.subMunitionType) || !RocketInfo.rocketNames.Contains(rocketInfo.subMunitionType))
                            {
                                beehive = false;
                                Debug.Log("[BDArmory.ModuleWeapon]: Invalid submunition on : " + currentType);
                            }
                            else
                            {
                                if (RocketInfo.rocketNames.Contains(rocketInfo.subMunitionType))
                                {
                                    RocketInfo sRocket = RocketInfo.rockets[rocketInfo.subMunitionType];
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
                        Debug.Log("[BDArmory.ModuleWeapon]: Failed To load bullet : " + currentType);
                    }
                    else
                    {
                        if (BDArmorySettings.DEBUG_WEAPONS)
                            Debug.Log("[BDArmory.ModuleWeapon]: BulletType Loaded : " + currentType);
                        if (beehive)
                        {
                            if (!BulletInfo.bulletNames.Contains(bulletInfo.subMunitionType))
                            {
                                beehive = false;
                                Debug.Log("[BDArmory.ModuleWeapon]: Invalid submunition on : " + currentType);
                            }
                        }
                    }
                }
            }

            BDArmorySetup.OnVolumeChange += UpdateVolume;
            if (HighLogic.LoadedSceneIsFlight)
            { TimingManager.FixedUpdateAdd(TimingManager.TimingStage.FashionablyLate, AimAndFire); }
        }

        void OnDestroy()
        {
            if (muzzleFlashList != null)
                foreach (var pelist in muzzleFlashList)
                    foreach (var pe in pelist)
                        if (pe) EffectBehaviour.RemoveParticleEmitter(pe);
            foreach (var pe in part.FindModelComponents<KSPParticleEmitter>())
                if (pe) EffectBehaviour.RemoveParticleEmitter(pe);
            BDArmorySetup.OnVolumeChange -= UpdateVolume;
            WeaponNameWindow.OnActionGroupEditorOpened.Remove(OnActionGroupEditorOpened);
            WeaponNameWindow.OnActionGroupEditorClosed.Remove(OnActionGroupEditorClosed);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.FashionablyLate, AimAndFire);
        }
        public void PAWRefresh()
        {
            if (eFuzeType == FuzeTypes.Proximity || eFuzeType == FuzeTypes.Flak || eFuzeType == FuzeTypes.Timed || beehive)
            {
                Fields["maxAirDetonationRange"].guiActive = true;
                Fields["maxAirDetonationRange"].guiActiveEditor = true;
                Fields["defaultDetonationRange"].guiActive = true;
                Fields["defaultDetonationRange"].guiActiveEditor = true;
                Fields["detonationRange"].guiActive = true;
                Fields["detonationRange"].guiActiveEditor = true;
            }
            else
            {
                Fields["maxAirDetonationRange"].guiActive = false;
                Fields["maxAirDetonationRange"].guiActiveEditor = false;
                Fields["defaultDetonationRange"].guiActive = false;
                Fields["defaultDetonationRange"].guiActiveEditor = false;
                Fields["detonationRange"].guiActive = false;
                Fields["detonationRange"].guiActiveEditor = false;
            }
            GUIUtils.RefreshAssociatedWindows(part);
        }

        [KSPEvent(advancedTweakable = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_FireAngleOverride_Enable", active = true)]//Disable fire angle override
        public void ToggleOverrideAngle()
        {
            FireAngleOverride = !FireAngleOverride;

            if (FireAngleOverride == false)
            {
                Events["ToggleOverrideAngle"].guiName = Localizer.Format("#LOC_BDArmory_FireAngleOverride_Enable");// Enable Firing Angle Override
            }
            else
            {
                Events["ToggleOverrideAngle"].guiName = Localizer.Format("#LOC_BDArmory_FireAngleOverride_Disable");// Disable Firing Angle Override
            }

            Fields["FiringTolerance"].guiActive = FireAngleOverride;
            Fields["FiringTolerance"].guiActiveEditor = FireAngleOverride;

            GUIUtils.RefreshAssociatedWindows(part);
        }
        [KSPEvent(advancedTweakable = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_BurstLengthOverride_Enable", active = true)]//Burst length override
        public void ToggleBurstLengthOverride()
        {
            BurstOverride = !BurstOverride;

            if (BurstOverride == false)
            {
                Events["ToggleBurstLengthOverride"].guiName = Localizer.Format("#LOC_BDArmory_BurstLengthOverride_Enable");// Enable Burst Fire length Override
            }
            else
            {
                Events["ToggleBurstLengthOverride"].guiName = Localizer.Format("#LOC_BDArmory_BurstLengthOverride_Disable");// Disable Burst Fire length Override
            }

            Fields["fireBurstLength"].guiActive = BurstOverride;
            Fields["fireBurstLength"].guiActiveEditor = BurstOverride;

            GUIUtils.RefreshAssociatedWindows(part);
        }
        void FAOCos(BaseField field, object obj)
        {
            maxAutoFireCosAngle = Mathf.Cos((FiringTolerance * Mathf.Deg2Rad));
        }
        void AccAdjust(BaseField field, object obj)
        {
            maxDeviation = baseDeviation + ((baseDeviation / (baseRPM / roundsPerMinute)) - baseDeviation);
            maxDeviation *= Mathf.Clamp(bulletInfo.subProjectileCount / 5, 1, 5); //modify deviation if shot vs slug
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

                if (weaponState == WeaponStates.Enabled && (TimeWarp.WarpMode != TimeWarp.Modes.HIGH || TimeWarp.CurrentRate == 1))
                {
                    userFiring = (BDInputUtils.GetKey(BDInputSettingsFields.WEAP_FIRE_KEY) && (vessel.isActiveVessel || BDArmorySettings.REMOTE_SHOOTING) && !MapView.MapIsEnabled && !aiControlled && !GUIUtils.CheckMouseIsOnGui()); //don't fire if mouse on WM GUI; Issue #348
                    if (!(((userFiring || agHoldFiring) && !isAPS) || (autoFire && //if user pulling the trigger || AI controlled and on target if turreted || finish a burstfire weapon's burst
                        (!turret || turret.TargetInRange(finalAimTarget, 10, float.MaxValue))) || (BurstFire && RoundsRemaining > 0 && RoundsRemaining < RoundsPerMag)))
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

                if (spinningDown && spinDownAnimation && hasFireAnimation)
                {
                    for (int i = 0; i < fireState.Length; i++)
                    {
                        if (fireState[i].normalizedTime > 1) fireState[i].normalizedTime = 0;
                        fireState[i].speed = fireAnimSpeed;
                        fireAnimSpeed = Mathf.Lerp(fireAnimSpeed, 0, 0.04f);
                    }
                }

                // Draw gauges
                if (vessel.isActiveVessel)
                {
                    gauge.UpdateAmmoMeter((float)(ammoCount / ammoMaxCount));

                    if (showReloadMeter)
                    {
                        {
                            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                                gauge.UpdateReloadMeter((Time.time - timeFired) * BDArmorySettings.FIRE_RATE_OVERRIDE / 60);
                            else
                                gauge.UpdateReloadMeter((Time.time - timeFired) * roundsPerMinute / 60);
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
                        if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log("[BDArmory.ModuleWeapon]: Vessel is uncontrollable, disabling weapon " + part.name);
                        DisableWeapon();
                    }
                    return;
                }

                UpdateHeat();
                if (weaponState == WeaponStates.Standby && (TimeWarp.WarpMode != TimeWarp.Modes.HIGH || TimeWarp.CurrentRate == 1)) { aimOnly = true; }
                if (weaponState == WeaponStates.Enabled && (TimeWarp.WarpMode != TimeWarp.Modes.HIGH || TimeWarp.CurrentRate == 1))
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
                vessel.GetConnectedResourceTotals(AmmoID, out double ammoCurrent, out double ammoMax); //ammo count was originally updating only for active vessel, while reload can be called by any loaded vessel, and needs current ammo count
                ammoCount = ammoCurrent;
                ammoMaxCount = ammoMax;
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
            if (trajectoryRenderer != null && (!BDArmorySettings.DEBUG_LINES || !(weaponState == WeaponStates.Enabled || weaponState == WeaponStates.Standby))) { trajectoryRenderer.enabled = false; }
            if (HighLogic.LoadedSceneIsFlight && weaponState == WeaponStates.Enabled && vessel && !vessel.packed && vessel.isActiveVessel &&
                BDArmorySettings.DRAW_AIMERS && !aiControlled && !MapView.MapIsEnabled && !pointingAtSelf && !isAPS)
            {
                float size = 30;

                Vector3 reticlePosition;
                if (BDArmorySettings.AIM_ASSIST)
                {
                    if (targetAcquired && (slaved || yawRange < 1 || maxPitch - minPitch < 1))
                    {
                        reticlePosition = pointingAtPosition + fixedLeadOffset;

                        if (!slaved)
                        {
                            GUIUtils.DrawLineBetweenWorldPositions(pointingAtPosition, reticlePosition, 2,
                                new Color(0, 1, 0, 0.6f));
                        }

                        GUIUtils.DrawTextureOnWorldPos(pointingAtPosition, BDArmorySetup.Instance.greenDotTexture,
                            new Vector2(6, 6), 0);

                        if (atprAcquired)
                        {
                            GUIUtils.DrawTextureOnWorldPos(targetPosition, BDArmorySetup.Instance.openGreenSquare,
                                new Vector2(20, 20), 0);
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
                        GUIUtils.DrawLineBetweenWorldPositions(fireTransforms[0].position, targetPosition, 2,
                            Color.blue);
                    }
                }
            }

            if (HighLogic.LoadedSceneIsEditor && BDArmorySetup.showWeaponAlignment && !isAPS)
            {
                DrawAlignmentIndicator();
            }

#if DEBUG
            if (BDArmorySettings.DEBUG_LINES && weaponState == WeaponStates.Enabled && vessel && !vessel.packed && !MapView.MapIsEnabled)
            {
                GUIUtils.MarkPosition(debugTargetPosition, transform, Color.cyan);
                GUIUtils.DrawLineBetweenWorldPositions(debugTargetPosition, debugTargetPosition + debugRelVelAdj, 2, Color.green);
                GUIUtils.DrawLineBetweenWorldPositions(debugTargetPosition + debugRelVelAdj, debugTargetPosition + debugRelVelAdj + debugAccAdj, 2, Color.magenta);
                GUIUtils.DrawLineBetweenWorldPositions(debugTargetPosition + debugRelVelAdj + debugAccAdj, debugTargetPosition + debugRelVelAdj + debugAccAdj + debugGravAdj, 2, Color.yellow);
                GUIUtils.MarkPosition(finalAimTarget, transform, Color.cyan, size: 4);
            }
#endif
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
            float timeGap = ((60 / roundsPerMinute) * fireTransforms.Length) * TimeWarp.CurrentRate; //this way weapon delivers stated RPM, not RPM * barrel num
            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                timeGap = ((60 / BDArmorySettings.FIRE_RATE_OVERRIDE) * fireTransforms.Length) * TimeWarp.CurrentRate;

            if (useRippleFire)
            {
                timeGap /= fireTransforms.Length; //to maintain RPM if only firing one barrel at a time
            }
            if (Time.time - timeFired > timeGap
                && !isOverheated
                && !isReloading
                && !pointingAtSelf
                && (aiControlled || !GUIUtils.CheckMouseIsOnGui())
                && WMgrAuthorized())
            {
                bool effectsShot = false;
                CheckLoadedAmmo();
                //Transform[] fireTransforms = part.FindModelTransforms("fireTransform");
                for (float iTime = Mathf.Min(Time.time - timeFired - timeGap, TimeWarp.fixedDeltaTime); iTime >= 0; iTime -= timeGap)
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
                                    part.rb.AddForceAtPosition((-fireTransform.forward) * (bulletVelocity * (bulletMass * ProjectileCount) / 1000 * BDArmorySettings.RECOIL_FACTOR * recoilReduction),
                                        fireTransform.position, ForceMode.Impulse);
                                }

                                if (!effectsShot)
                                {
                                    WeaponFX();
                                    effectsShot = true;
                                }

                                // Debug.Log("DEBUG FIRE!");
                                //firing bullet
                                for (int s = 0; s < ProjectileCount; s++)
                                {
                                    GameObject firedBullet = bulletPool.GetPooledObject();
                                    PooledBullet pBullet = firedBullet.GetComponent<PooledBullet>();


                                    pBullet.transform.position = fireTransform.position;

                                    pBullet.caliber = bulletInfo.caliber;
                                    pBullet.bulletVelocity = bulletInfo.bulletVelocity;
                                    pBullet.bulletMass = bulletInfo.bulletMass;
                                    pBullet.explosive = bulletInfo.explosive;
                                    pBullet.incendiary = bulletInfo.incendiary;
                                    pBullet.apBulletMod = bulletInfo.apBulletMod;
                                    pBullet.bulletDmgMult = bulletDmgMult;

                                    //A = π x (Ø / 2)^2
                                    bulletDragArea = Mathf.PI * 0.25f * caliber * caliber;

                                    //Bc = m/Cd * A
                                    bulletBallisticCoefficient = bulletMass / ((bulletDragArea / 1000000f) * 0.295f); // mm^2 to m^2

                                    //Bc = m/d^2 * i where i = 0.484
                                    //bulletBallisticCoefficient = bulletMass / Mathf.Pow(caliber / 1000, 2f) * 0.484f;

                                    pBullet.ballisticCoefficient = bulletBallisticCoefficient;

                                    pBullet.timeElapsedSinceCurrentSpeedWasAdjusted = iTime;
                                    // measure bullet lifetime in time rather than in distance, because distances get very relative in orbit
                                    pBullet.timeToLiveUntil = Mathf.Max(maxTargetingRange, maxEffectiveDistance) / bulletVelocity * 1.1f + Time.time;

                                    timeFired = Time.time - iTime;

                                    Vector3 firedVelocity = VectorUtils.GaussianDirectionDeviation(fireTransform.forward, (maxDeviation / 2)) * bulletVelocity;
                                    pBullet.currentVelocity = (part.rb.velocity + Krakensbane.GetFrameVelocityV3f()) + firedVelocity; // use the real velocity, w/o offloading
                                    // pBullet.transform.position += (part.rb.velocity + Krakensbane.GetFrameVelocityV3f()) * Time.fixedDeltaTime; // Initially, put the bullet at the fireTransform position at the start of the next physics frame

                                    pBullet.sourceWeapon = this.part;
                                    pBullet.sourceVessel = vessel;
                                    pBullet.team = weaponManager.Team.Name;
                                    pBullet.bulletTexturePath = bulletTexturePath;
                                    pBullet.projectileColor = projectileColorC;
                                    pBullet.startColor = startColorC;
                                    pBullet.fadeColor = fadeColor;
                                    tracerIntervalCounter++;
                                    if (tracerIntervalCounter > tracerInterval)
                                    {
                                        tracerIntervalCounter = 0;
                                        pBullet.tracerStartWidth = tracerStartWidth;
                                        pBullet.tracerEndWidth = tracerEndWidth;
                                        pBullet.tracerLength = tracerLength;
                                    }
                                    else
                                    {
                                        pBullet.tracerStartWidth = nonTracerWidth;
                                        pBullet.tracerEndWidth = nonTracerWidth;
                                        pBullet.startColor.a *= 0.5f;
                                        pBullet.projectileColor.a *= 0.5f;
                                        pBullet.tracerLength = tracerLength * 0.4f;
                                    }
                                    pBullet.tracerDeltaFactor = tracerDeltaFactor;
                                    pBullet.tracerLuminance = tracerLuminance;
                                    pBullet.bulletDrop = bulletDrop;

                                    if (bulletInfo.tntMass > 0 || bulletInfo.beehive)
                                    {
                                        pBullet.explModelPath = explModelPath;
                                        pBullet.explSoundPath = explSoundPath;
                                        pBullet.tntMass = bulletInfo.tntMass;
                                        pBullet.detonationRange = detonationRange;
                                        pBullet.maxAirDetonationRange = maxAirDetonationRange;
                                        pBullet.defaultDetonationRange = defaultDetonationRange;
                                        switch (eFuzeType)
                                        {
                                            case FuzeTypes.None:
                                                pBullet.fuzeType = PooledBullet.BulletFuzeTypes.None;
                                                break;
                                            case FuzeTypes.Impact:
                                                pBullet.fuzeType = PooledBullet.BulletFuzeTypes.Impact;
                                                break;
                                            case FuzeTypes.Delay:
                                                pBullet.fuzeType = PooledBullet.BulletFuzeTypes.Delay;
                                                break;
                                            case FuzeTypes.Penetrating:
                                                pBullet.fuzeType = PooledBullet.BulletFuzeTypes.Penetrating;
                                                break;
                                            case FuzeTypes.Timed:
                                                pBullet.fuzeType = PooledBullet.BulletFuzeTypes.Timed;
                                                break;
                                            case FuzeTypes.Proximity:
                                                pBullet.fuzeType = PooledBullet.BulletFuzeTypes.Proximity;
                                                break;
                                            case FuzeTypes.Flak:
                                                pBullet.fuzeType = PooledBullet.BulletFuzeTypes.Flak;
                                                break;
                                        }
                                    }
                                    else
                                    {
                                        pBullet.fuzeType = PooledBullet.BulletFuzeTypes.None;
                                        pBullet.sabot = SabotRound;
                                    }
                                    pBullet.EMP = bulletInfo.EMP;
                                    pBullet.nuclear = bulletInfo.nuclear;
                                    pBullet.beehive = beehive;
                                    if (bulletInfo.beehive)
                                    {
                                        pBullet.subMunitionType = BulletInfo.bullets[bulletInfo.subMunitionType];
                                    }
                                    //pBullet.homing = BulletInfo.homing;
                                    pBullet.impulse = Impulse;
                                    pBullet.massMod = massAdjustment;
                                    switch (bulletDragType)
                                    {
                                        case BulletDragTypes.None:
                                            pBullet.dragType = PooledBullet.BulletDragTypes.None;
                                            break;

                                        case BulletDragTypes.AnalyticEstimate:
                                            pBullet.dragType = PooledBullet.BulletDragTypes.AnalyticEstimate;
                                            break;

                                        case BulletDragTypes.NumericalIntegration:
                                            pBullet.dragType = PooledBullet.BulletDragTypes.NumericalIntegration;
                                            break;
                                    }

                                    pBullet.bullet = BulletInfo.bullets[currentType];
                                    pBullet.stealResources = resourceSteal;
                                    pBullet.dmgMult = strengthMutator;
                                    if (instagib)
                                    {
                                        pBullet.dmgMult = -1;
                                    }
                                    if (isAPS)
                                    {
                                        pBullet.isAPSprojectile = true;
                                        pBullet.tgtShell = tgtShell;
                                        pBullet.tgtRocket = tgtRocket;
                                    }
                                    pBullet.gameObject.SetActive(true);

                                    if (!pBullet.CheckBulletCollisions(iTime)) // Check that the bullet won't immediately hit anything.
                                    {
                                        pBullet.MoveBullet(iTime); // Move the bullet forward by the amount of time within the physics frame determined by it's firing rate.
                                    }
                                    else
                                    {
                                        // Debug.Log("DEBUG immediately hit after " + pBullet.DistanceTraveled + "m and time " + iTime);
                                    }
                                }
                                //heat

                                heat += heatPerShot;
                                //EC
                                RoundsRemaining++;
                                if (BurstOverride)
                                {
                                    autofireShotCount++;
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

                if (useRippleFire)
                {
                    if (fireState.Length > 1) //need to add clause for singlebarrel guns
                    {
                        barrelIndex++;
                        //Debug.Log("[BDArmory.ModuleWeapon]: barrelIndex for " + this.GetShortName() + " is " + barrelIndex + "; total barrels " + fireTransforms.Length);
                        if ((!BurstFire || (BurstFire && (RoundsRemaining >= RoundsPerMag))) && barrelIndex + 1 > fireTransforms.Length) //only advance ripple index if weapon isn't brustfire, has finished burst, or has fired with all barrels
                        {
                            StartCoroutine(IncrementRippleIndex(initialFireDelay * TimeWarp.CurrentRate));
                            isRippleFiring = true;
                            if (barrelIndex + 1 > fireTransforms.Length)
                            {
                                barrelIndex = 0;
                                //Debug.Log("[BDArmory.ModuleWeapon]: barrelIndex for " + this.GetShortName() + " reset");
                            }
                        }
                    }
                    else
                    {
                        if (!BurstFire || (BurstFire && (RoundsRemaining >= RoundsPerMag)))
                        {
                            StartCoroutine(IncrementRippleIndex(initialFireDelay * TimeWarp.CurrentRate)); //this is why ripplefire is slower, delay to stagger guns should only be being called once
                            isRippleFiring = true;
                        }
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
            float timeGap = (60 / roundsPerMinute) * TimeWarp.CurrentRate;
            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                timeGap = (60 / BDArmorySettings.FIRE_RATE_OVERRIDE) * TimeWarp.CurrentRate;

            if (timeGap <= weaponManager.targetScanInterval)
                return true;
            else
                return (Time.time - timeFired >= timeGap - weaponManager.targetScanInterval);
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
            float timeGap = ((60 / roundsPerMinute) * fireTransforms.Length) * TimeWarp.CurrentRate; //this way weapon delivers stated RPM, not RPM * barrel num
            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41 && !isAPS)
                timeGap = ((60 / BDArmorySettings.FIRE_RATE_OVERRIDE) * fireTransforms.Length) * TimeWarp.CurrentRate;

            if (useRippleFire)
            {
                timeGap /= fireTransforms.Length; //to maintain RPM if only firing one barrel at a time
            }
            beamDuration = Math.Min(timeGap * 0.8f, 0.1f);
            if ((!pulseLaser || ((Time.time - timeFired > timeGap) && pulseLaser))
                && !pointingAtSelf && !GUIUtils.CheckMouseIsOnGui() && WMgrAuthorized() && !isOverheated) // && !isReloading)
            {
                if (CanFire(chargeAmount))
                {
                    var aName = vessel.GetName();
                    if (pulseLaser)
                    {
                        for (float iTime = Mathf.Min(Time.time - timeFired - timeGap, TimeWarp.fixedDeltaTime); iTime >= 0; iTime -= timeGap)
                        {
                            timeFired = Time.time - iTime;
                            BDACompetitionMode.Instance.Scores.RegisterShot(aName);
                            LaserBeam(aName);
                        }
                        heat += heatPerShot;
                        if (useRippleFire)
                        {
                            if (fireState.Length > 1) //need to add clause for singlebarrel guns
                            {
                                barrelIndex++;
                                //Debug.Log("[BDArmory.ModuleWeapon]: barrelIndex for " + this.GetShortName() + " is " + barrelIndex + "; total barrels " + fireTransforms.Length);
                                if ((!BurstFire || (BurstFire && (RoundsRemaining >= RoundsPerMag))) && barrelIndex + 1 > fireTransforms.Length) //only advance ripple index if weapon isn't brustfire, has finished burst, or has fired with all barrels
                                {
                                    StartCoroutine(IncrementRippleIndex(initialFireDelay * TimeWarp.CurrentRate));
                                    isRippleFiring = true;
                                    if (barrelIndex + 1 > fireTransforms.Length)
                                    {
                                        barrelIndex = 0;
                                        //Debug.Log("[BDArmory.ModuleWeapon]: barrelIndex for " + this.GetShortName() + " reset");
                                    }
                                }
                            }
                            else
                            {
                                if (!BurstFire || (BurstFire && (RoundsRemaining >= RoundsPerMag)))
                                {
                                    StartCoroutine(IncrementRippleIndex(initialFireDelay * TimeWarp.CurrentRate));
                                    isRippleFiring = true;
                                }
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
                        for (float iTime = TimeWarp.fixedDeltaTime; iTime >= 0; iTime -= timeGap)
                            timeFired = Time.time - iTime;
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
                if ((!useRippleFire || !pulseLaser || fireState.Length == 1) || (useRippleFire && i == barrelIndex))
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
                    else if (((((visualTargetVessel != null && visualTargetVessel.loaded) || slaved) || (isAPS && (tgtShell != null || tgtRocket != null))) && (turret && (turret.yawRange > 0 && turret.maxPitch > 0))) // causes laser to snap to target CoM if close enough. changed to only apply to turrets
                        && Vector3.Angle(rayDirection, targetDirection) < (isAPS ? 1f : 0.25f)) //if turret and within .25 deg (or 1 deg if APS), snap to target
                    {
                        //targetDirection = targetPosition + (relativeVelocity * Time.fixedDeltaTime) * 2 - tf.position;
                        targetDirection = targetPosition - tf.position;
                        rayDirection = targetDirection;
                        targetDirectionLR = targetDirection.normalized;
                    }
                    Ray ray = new Ray(tf.position, rayDirection);
                    lr.useWorldSpace = false;
                    lr.SetPosition(0, Vector3.zero);
                    var hits = Physics.RaycastAll(ray, maxTargetingRange, layerMask1);
                    if (hits.Length > 0) // Find the first valid hit.
                    {
                        var orderedHits = hits.OrderBy(x => x.distance);
                        using (var hitsEnu = orderedHits.GetEnumerator())
                        {
                            while (hitsEnu.MoveNext())
                            {
                                var hitPart = hitsEnu.Current.collider.gameObject.GetComponentInParent<Part>();
                                if (hitPart == null) continue;
                                if (ProjectileUtils.IsIgnoredPart(hitPart)) continue; // Ignore ignored parts.
                                break;
                            }
                            var hit = hitsEnu.Current;
                            lr.useWorldSpace = true;
                            laserPoint = hit.point + (targetVelocity * Time.fixedDeltaTime);

                            lr.SetPosition(0, tf.position + (part.rb.velocity * Time.fixedDeltaTime));
                            lr.SetPosition(1, laserPoint);

                            KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                            Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();

                            if (p && p.vessel && p.vessel != vessel)
                            {
                                float distance = hit.distance;
                                //Scales down the damage based on the increased surface area of the area being hit by the laser. Think flashlight on a wall.
                                if (electroLaser)
                                {
                                    var mdEC = p.vessel.rootPart.FindModuleImplementing<ModuleDrainEC>();
                                    if (mdEC == null)
                                    {
                                        p.vessel.rootPart.AddModule("ModuleDrainEC");
                                    }
                                    var emp = p.vessel.rootPart.FindModuleImplementing<ModuleDrainEC>();
                                    if (!pulseLaser)
                                    {
                                        emp.incomingDamage += (ECPerShot / 1000);
                                    }
                                    else
                                    {
                                        emp.incomingDamage += (ECPerShot / 20);
                                    }
                                    emp.softEMP = true;
                                }
                                else if (impulseWeapon)
                                {
                                    if (!pulseLaser)
                                    {
                                        damage = Impulse * TimeWarp.fixedDeltaTime;
                                    }
                                    else
                                    {
                                        damage = Impulse;
                                    }
                                    if (p.rb != null && p.rb.mass > 0)
                                    {
                                        if (Impulse > 0)
                                        {
                                            p.rb.AddForceAtPosition((p.transform.position - tf.position).normalized * (float)damage, p.transform.position, ForceMode.Acceleration);
                                        }
                                        else
                                        {
                                            p.rb.AddForceAtPosition((tf.position - p.transform.position).normalized * (float)damage, p.transform.position, ForceMode.Acceleration);
                                        }
                                    }
                                }
                                else
                                {
                                    HitpointTracker armor = p.GetComponent<HitpointTracker>();
                                    initialDamage = (laserDamage / (1 + Mathf.PI * Mathf.Pow(tanAngle * distance, 2)) * 0.425f);

                                    if (armor != null)// technically, lasers shouldn't do damage until armor gone, but that would require localized armor tracking instead of the monolithic model currently used                                              
                                    {
                                        damage = (initialDamage * (pulseLaser ? 1 : TimeWarp.fixedDeltaTime)) * Mathf.Clamp((1 - (Mathf.Sqrt(armor.Diffusivity * (armor.Density / 1000)) * armor.ArmorThickness) / initialDamage), 0.005f, 1); //old calc lacked a clamp, could potentially become negative damage
                                    }  //clamps laser damage to not go negative, allow some small amount of bleedthrough - ~30 Be/Steel will negate ABL, ~62 Ti, 42 DU
                                    else
                                    {
                                        damage = initialDamage;
                                        if (!pulseLaser)
                                        {
                                            damage = initialDamage * TimeWarp.fixedDeltaTime;
                                        }
                                    }
                                    p.ReduceArmor(damage / 10000); //really should be tied into diffuisvity, density, and SafeUseTemp - lasers would need to melt/ablate material away; needs to be in cm^3. Review later
                                    p.AddDamage(damage);
                                    if (pulseLaser) BattleDamageHandler.CheckDamageFX(p, caliber, 1 + (damage / initialDamage), HEpulses, false, part.vessel.GetName(), hit, false, false); //beams will proc BD once every scoreAccumulatorTick

                                }
                                if (HEpulses)
                                {
                                    ExplosionFx.CreateExplosion(hit.point,
                                                   (laserDamage / 30000),
                                                   explModelPath, explSoundPath, ExplosionSourceType.Bullet, 1, null, vessel.vesselName, null);
                                }
                                if (HeatRay)
                                {
                                    using (var hitsEnu2 = Physics.OverlapSphere(hit.point, (Mathf.Sin(maxDeviation) * (tf.position - laserPoint).magnitude), layerMask2).AsEnumerable().GetEnumerator())
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
                                                p.AddSkinThermalFlux(damage);
                                            }
                                        }
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
                                    }
                                }
                                if (instagib)
                                {
                                    p.AddInstagibDamage();
                                    ExplosionFx.CreateExplosion(hit.point,
                                                   (1), "BDArmory/Models/explosion/explosion", explSoundPath, ExplosionSourceType.Bullet, 0, null, vessel.vesselName, null);
                                }
                                var aName = vesselname;
                                var tName = p.vessel.GetName();
                                if (BDACompetitionMode.Instance.Scores.RegisterBulletDamage(aName, tName, damage))
                                {
                                    if (pulseLaser || (!pulseLaser && ScoreAccumulator > beamScoreTime)) // Score hits with pulse lasers or when the score accumulator is sufficient.
                                    {
                                        ScoreAccumulator = 0;
                                        BDACompetitionMode.Instance.Scores.RegisterBulletHit(aName, tName, WeaponName, distance);
                                        if (!pulseLaser) BattleDamageHandler.CheckDamageFX(p, caliber, 1 + (damage / initialDamage), HEpulses, false, part.vessel.GetName(), hit, false, false);
                                        //pulse lasers check battle damage earlier in the code
                                    }
                                    else
                                    {
                                        ScoreAccumulator += TimeWarp.fixedDeltaTime;
                                    }
                                }

                                if (Time.time - timeFired > 6 / 120 && BDArmorySettings.BULLET_HITS)
                                {
                                    BulletHitFX.CreateBulletHit(p, hit.point, hit, hit.normal, false, 10, 0, weaponManager.Team.Name);
                                }
                            }
                        }
                    }
                    else
                    {
                        laserPoint = lr.transform.InverseTransformPoint((targetDirectionLR * maxTargetingRange) + tf.position);
                        lr.SetPosition(1, laserPoint);
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
            //chargeSound = GameDatabase.Instance.GetAudioClip(chargeSoundPath);
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

            float timeGap = (60 / roundsPerMinute) * TimeWarp.CurrentRate; //this way weapon delivers stated RPM, not RPM * barrel num
            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                timeGap = (60 / BDArmorySettings.FIRE_RATE_OVERRIDE) * TimeWarp.CurrentRate;
            if (!rocketPod)
                timeGap *= fireTransforms.Length;

            if (useRippleFire)
            {
                timeGap /= fireTransforms.Length; //to maintain RPM if only firing one barrel at a time
            }
            if (Time.time - timeFired > timeGap && !isReloading || !pointingAtSelf && (aiControlled || !GUIUtils.CheckMouseIsOnGui()) && WMgrAuthorized())
            {// fixes rocket ripple code for proper rippling
                bool effectsShot = false;
                for (float iTime = Mathf.Min(Time.time - timeFired - timeGap, TimeWarp.fixedDeltaTime); iTime >= 0; iTime -= timeGap)
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
                            rocketsLeft = Mathf.Clamp((int)(RoundsPerMag - RoundsRemaining), 0, Mathf.Clamp((int)ammoCurrent, 0, RoundsPerMag));
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
                                rocket.rocketMass = rocketMass;
                                rocket.blastRadius = blastRadius;
                                rocket.thrust = thrust;
                                rocket.thrustTime = thrustTime;
                                rocket.flak = proximityDetonation;
                                rocket.detonationRange = detonationRange;
                                rocket.maxAirDetonationRange = maxAirDetonationRange;
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
                                rocket.sourceWeapon = this.part;
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
                                }
                                rocketObj.SetActive(true);
                            }
                            if (!BDArmorySettings.INFINITE_AMMO)
                            {
                                if (externalAmmo)
                                {
                                    part.RequestResource(ammoName.GetHashCode(), (double)requestResourceAmount, ResourceFlowMode.STAGE_PRIORITY_FLOW_BALANCE);
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
                                            rocket.rocketMass = rocketMass;
                                            rocket.blastRadius = blastRadius;
                                            rocket.thrust = thrust;
                                            rocket.thrustTime = thrustTime;
                                            rocket.flak = proximityDetonation;
                                            rocket.detonationRange = detonationRange;
                                            rocket.maxAirDetonationRange = maxAirDetonationRange;
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
                                            rocket.sourceWeapon = this.part;
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
                                            }
                                            rocketObj.SetActive(true);
                                        }
                                        if (!BDArmorySettings.INFINITE_AMMO)
                                        {
                                            part.RequestResource(ammoName.GetHashCode(), (double)requestResourceAmount, ResourceFlowMode.STAGE_PRIORITY_FLOW_BALANCE);
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
                if (useRippleFire)
                {
                    if (fireState.Length > 1) //need to add clause for singlebarrel guns
                    {
                        barrelIndex++;
                        //Debug.Log("[BDArmory.ModuleWeapon]: barrelIndex for " + this.GetShortName() + " is " + barrelIndex + "; total barrels " + fireTransforms.Length);
                        if ((!BurstFire || (BurstFire && (RoundsRemaining >= RoundsPerMag))) && barrelIndex + 1 > fireTransforms.Length) //only advance ripple index if weapon isn't brustfire, has finished burst, or has fired with all barrels
                        {
                            StartCoroutine(IncrementRippleIndex(initialFireDelay * TimeWarp.CurrentRate));
                            isRippleFiring = true;
                            if (barrelIndex + 1 > fireTransforms.Length)
                            {
                                barrelIndex = 0;
                                //Debug.Log("[BDArmory.ModuleWeapon]: barrelIndex for " + this.GetShortName() + " reset");
                            }
                        }
                    }
                    else
                    {
                        if (!BurstFire || (BurstFire && (RoundsRemaining >= RoundsPerMag)))
                        {
                            StartCoroutine(IncrementRippleIndex(initialFireDelay * TimeWarp.CurrentRate));
                            isRippleFiring = true;
                        }
                    }
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

        bool CanFire(float AmmoPerShot)
        {
            if (!hasGunner)
            {
                ScreenMessages.PostScreenMessage("Weapon Requires Gunner", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }
            if (BDArmorySettings.INFINITE_AMMO) return true;
            if (ECPerShot != 0)
            {
                double chargeAvailable = part.RequestResource("ElectricCharge", ECPerShot, ResourceFlowMode.ALL_VESSEL);
                if (chargeAvailable < ECPerShot * 0.95f && !CheatOptions.InfiniteElectricity)
                {
                    ScreenMessages.PostScreenMessage("Weapon Requires EC", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                    return false;
                }
                //else return true; //this is causing weapons thath have ECPerShot + standard ammo (railguns, etc) to not consume ammo, only EC
            }
            if (part.RequestResource(ammoName.GetHashCode(), (double)AmmoPerShot) > 0)
            {
                return true;
            }
            StartCoroutine(IncrementRippleIndex(useRippleFire ? initialFireDelay * TimeWarp.CurrentRate : 0)); //if out of ammo (howitzers, say, or other weapon with internal ammo, move on to next weapon; maybe it still has ammo
            isRippleFiring = true;
            return false;
        }

        void PlayFireAnim()
        {
            //Debug.Log("[BDArmory.ModuleWeapon]: fireState length = " + fireState.Length);
            for (int i = 0; i < fireState.Length; i++)
            {
                try
                {
                    //Debug.Log("[BDArmory.ModuleWeapon]: playing Fire Anim, i = " + i + "; fire anim " + fireState[i].name);
                }
                catch
                {
                    Debug.Log("[BDArmory.ModuleWeapon]: error with fireanim number " + barrelIndex);
                }
                if ((!useRippleFire) || (useRippleFire && i == barrelIndex))
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
                }
            }
        }

        void WeaponFX()
        {
            //sound
            if (ChargeTime > 0)
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
                    audioSource.clip = fireSound;
                    audioSource.loop = false;
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
                for (int i = 0; i < shellEjectTransforms.Length; i++)
                {
                    if ((!useRippleFire || fireState.Length == 1) || (useRippleFire && i == barrelIndex))
                    {
                        GameObject ejectedShell = shellPool.GetPooledObject();
                        ejectedShell.transform.position = shellEjectTransforms[i].position;
                        ejectedShell.transform.rotation = shellEjectTransforms[i].rotation;
                        ejectedShell.transform.localScale = Vector3.one * shellScale;
                        ShellCasing shellComponent = ejectedShell.GetComponent<ShellCasing>();
                        shellComponent.initialV = part.rb.velocity;
                        ejectedShell.SetActive(true);
                    }
                }
            }
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
            float checkRange = Mathf.Min(targetAcquired ? targetDistance : maxTargetingRange, maxCheckRange);

            for (int i = 0; i < fireTransforms.Length; i++)
            {
                Ray ray = new Ray(fireTransforms[i].position, fireTransforms[i].forward);
                RaycastHit hit;

                if (Physics.Raycast(ray, out hit, maxTargetingRange, layerMask1))
                {
                    KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                    Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();
                    if (p && p.vessel && p.vessel == vessel)
                    {
                        pointingAtSelf = true;
                        break;
                    }
                }

                pointingAtPosition = fireTransforms[i].position + (ray.direction * targetDistance);
            }
        }

        HashSet<WeaponStates> enabledStates = new HashSet<WeaponStates> { WeaponStates.Enabled, WeaponStates.PoweringUp, WeaponStates.Locked };
        public void EnableWeapon()
        {
            if (enabledStates.Contains(weaponState))
                return;

            StopShutdownStartupRoutines();

            startupRoutine = StartCoroutine(StartupRoutine());
        }

        HashSet<WeaponStates> disabledStates = new HashSet<WeaponStates> { WeaponStates.Disabled, WeaponStates.PoweringDown };
        public void DisableWeapon()
        {
            if (disabledStates.Contains(weaponState))
                return;

            StopShutdownStartupRoutines();

            if (part.isActiveAndEnabled) shutdownRoutine = StartCoroutine(ShutdownRoutine());
        }

        HashSet<WeaponStates> standbyStates = new HashSet<WeaponStates> { WeaponStates.Standby, WeaponStates.PoweringUp, WeaponStates.Locked };
        public void StandbyWeapon()
        {
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
            fireSound = GameDatabase.Instance.GetAudioClip(fireSoundPath);
            overheatSound = GameDatabase.Instance.GetAudioClip(overheatSoundPath);
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
                reloadAudioClip = (AudioClip)GameDatabase.Instance.GetAudioClip(reloadAudioPath);
            }
            if (reloadCompletePath != string.Empty)
            {
                reloadCompleteAudioClip = (AudioClip)GameDatabase.Instance.GetAudioClip(reloadCompletePath);
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
            if (aiControlled && !slaved)
            {
                if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                {
                    if (!targetAcquired && (!weaponManager || Time.time - lastGoodTargetTime > Mathf.Max(60f / BDArmorySettings.FIRE_RATE_OVERRIDE, weaponManager.targetScanInterval)))
                    {
                        autoFire = false;
                        return;
                    }
                }
                else
                {
                    if (!targetAcquired && (!weaponManager || Time.time - lastGoodTargetTime > Mathf.Max(60f / roundsPerMinute, weaponManager.targetScanInterval)))
                    {
                        autoFire = false;
                        return;
                    }
                }
            }

            Vector3 finalTarget = targetPosition;
            if (aiControlled && !slaved && !targetAcquired && weaponManager)
            {
                if (!FloatingOrigin.Offset.IsZero() || !Krakensbane.GetFrameVelocity().IsZero())
                {
                    lastFinalAimTarget -= FloatingOrigin.OffsetNonKrakensbane;
#if DEBUG
                    debugLastTargetPosition -= FloatingOrigin.OffsetNonKrakensbane;
#endif
                }
                // Continue aiming towards where the target is expected to be while reloading based on the last measured pos, vel, acc.
                finalAimTarget = AIUtils.PredictPosition(lastFinalAimTarget, targetVelocity, targetAcceleration, Time.time - lastGoodTargetTime); // FIXME Check this predicted position when in orbit.
#if DEBUG
                debugTargetPosition = AIUtils.PredictPosition(debugLastTargetPosition, targetVelocity, targetAcceleration, Time.time - lastGoodTargetTime);
#endif
            }
            else
            {
                Transform fireTransform = fireTransforms[0];
                if (eWeaponType == WeaponTypes.Rocket && rocketPod)
                {
                    fireTransform = rockets[0].parent; // support for legacy RLs
                }
                if (!slaved && !aiControlled && (yawRange > 0 || maxPitch - minPitch > 0) && !isAPS)
                {
                    //MouseControl
                    Vector3 mouseAim = new Vector3(Input.mousePosition.x / Screen.width, Input.mousePosition.y / Screen.height, 0);
                    Ray ray = FlightCamera.fetch.mainCamera.ViewportPointToRay(mouseAim);
                    RaycastHit hit;

                    if (Physics.Raycast(ray, out hit, maxTargetingRange, layerMask1))
                    {
                        KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                        Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();

                        if (p != null && p.vessel != null && p.vessel == vessel) //aim through self vessel if occluding mouseray
                        {
                            targetPosition = ray.origin + ray.direction * maxTargetingRange;
                        }
                        else
                        {
                            targetPosition = hit.point;
                        }
                        targetAccelerationWasReset = true;
                    }
                    else
                    {
                        targetPosition = ray.origin + ray.direction * (maxTargetingRange + (FlightCamera.fetch.Distance * 0.75f));

                        if (visualTargetVessel != null && visualTargetVessel.loaded)
                        {
                            if (!targetCOM && visualTargetPart != null)
                            {
                                targetPosition = ray.origin + ray.direction * Vector3.Distance(visualTargetPart.transform.position, ray.origin);
                            }
                            else
                            {
                                targetPosition = ray.origin + ray.direction * Vector3.Distance(visualTargetVessel.transform.position, ray.origin);
                            }
                        }
                        else
                        {
                            targetPosition = ray.origin + ray.direction * maxTargetingRange;
                        }
                    }
                }
                if (BDArmorySettings.BULLET_WATER_DRAG)
                {
                    if ((FlightGlobals.getAltitudeAtPos(targetPosition) < 0) && (FlightGlobals.getAltitudeAtPos(targetPosition) + targetRadius > 0)) //vessel not completely submerged
                    {
                        if (caliber < 75)
                        {
                            targetPosition += (VectorUtils.GetUpDirection(targetPosition) * Mathf.Abs(FlightGlobals.getAltitudeAtPos(targetPosition))); //set targetposition to surface directly above target
                        }
                    }
                }
                //aim assist
                Vector3 originalTarget = targetPosition;
                targetDistance = Vector3.Distance(targetPosition, fireTransform.parent.position);
                origTargetDistance = targetDistance;

                if ((BDArmorySettings.AIM_ASSIST || aiControlled) && eWeaponType == WeaponTypes.Ballistic)//Gun targeting
                {
                    var gravity = (Vector3)FlightGlobals.getGeeForceAtPosition((fireTransforms[0].position + targetPosition) / 2f);
                    var bulletAcceleration = bulletDrop ? gravity : Vector3.zero; // Drag is ignored.
                    var bulletRelativePosition = targetPosition - fireTransforms[0].position;
                    var firingDirection = fireTransforms[0].forward;
                    Vector3 bulletEffectiveVelocity, bulletRelativeVelocity, bulletRelativeAcceleration, targetPredictedPosition, bulletDropOffset, lastFiringDirection;
                    float timeToCPA;
                    var count = 0;
                    do
                    {
                        lastFiringDirection = firingDirection;
                        bulletEffectiveVelocity = part.rb.velocity + baseBulletVelocity * firingDirection;
                        bulletRelativeVelocity = targetVelocity - bulletEffectiveVelocity;
                        bulletRelativeAcceleration = targetAcceleration - bulletAcceleration;
                        timeToCPA = AIUtils.ClosestTimeToCPA(bulletRelativePosition, bulletRelativeVelocity, bulletRelativeAcceleration, maxTargetingRange / bulletEffectiveVelocity.magnitude);
                        targetPredictedPosition = AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, timeToCPA);
                        bulletDropOffset = -0.5f * bulletAcceleration * timeToCPA * timeToCPA;
                        finalTarget = targetPredictedPosition + bulletDropOffset - part.rb.velocity * timeToCPA;
                        firingDirection = (finalTarget - fireTransforms[0].position).normalized;
                    } while (++count < 10 && Vector3.Angle(lastFiringDirection, firingDirection) > 1f); // 1° margin of error is sufficient to prevent premature firing
                    targetDistance = Vector3.Distance(finalTarget, fireTransforms[0].position);
                    if (bulletDrop && targetDistance > 10000)// The above calculation becomes inaccurate for distances over approximately 10km due to surface curvature (varying gravity direction), so we try to narrow it down with a simulation.
                    {
                        var simulatedCPA = BallisticTrajectoryClosestApproachSimulation(fireTransforms[0].position, bulletEffectiveVelocity, targetPosition, targetVelocity, targetAcceleration, BDArmorySettings.BALLISTIC_TRAJECTORY_SIMULATION_MULTIPLIER * Time.fixedDeltaTime);
                        var correction = simulatedCPA - AIUtils.PredictPosition(fireTransforms[0].position, bulletEffectiveVelocity, bulletAcceleration, timeToCPA);
                        finalTarget -= correction;
                        targetDistance = Vector3.Distance(finalTarget, fireTransforms[0].position);
                    }
#if DEBUG
                    // Debug.Log($"DEBUG {count} iterations for convergence in aiming loop");
                    debugTargetPosition = targetPosition;
                    debugLastTargetPosition = debugTargetPosition;
                    debugRelVelAdj = (targetVelocity - part.rb.velocity) * timeToCPA;
                    debugAccAdj = 0.5f * targetAcceleration * timeToCPA * timeToCPA;
                    debugGravAdj = bulletDropOffset;
                    // var missDistance = AIUtils.PredictPosition(bulletRelativePosition, bulletRelativeVelocity, bulletRelativeAcceleration, timeToCPA);
                    // if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log("DEBUG δt: " + timeToCPA + ", miss: " + missDistance + ", bullet drop: " + bulletDropOffset + ", final: " + finalTarget + ", target: " + targetPosition + ", " + targetVelocity + ", " + targetAcceleration + ", distance: " + targetDistance);
#endif
                }
                if ((BDArmorySettings.AIM_ASSIST || aiControlled) && eWeaponType == WeaponTypes.Rocket) //Rocket targeting
                {
                    finalTarget = AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, predictedFlightTime) + trajectoryOffset;
                    targetDistance = Mathf.Clamp(Vector3.Distance(targetPosition, fireTransform.parent.position), 0, maxTargetingRange);
                }
                //airdetonation
                if (eFuzeType == FuzeTypes.Timed || eFuzeType == FuzeTypes.Flak)
                {
                    if (targetAcquired)
                    {
                        defaultDetonationRange = targetDistance;// adds variable time fuze if/when proximity fuzes fail
                    }
                    else
                    {
                        defaultDetonationRange = maxAirDetonationRange; //airburst at max range
                    }
                }
                fixedLeadOffset = originalTarget - finalTarget; //for aiming fixed guns to moving target
                finalAimTarget = finalTarget;
                lastFinalAimTarget = finalAimTarget;
                lastGoodTargetTime = Time.time;
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
                turret.AimToTarget(finalAimTarget);
                turret.smoothRotation = origSmooth;
            }
        }
        //moving RTS to get all the targeting code together for convenience once rockets get added
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
                    Vector3 simVelocity = part.rb.velocity + (baseBulletVelocity * fireTransform.forward);
                    Vector3 simCurrPos = fireTransform.position;
                    var simDeltaTime = Mathf.Clamp(Mathf.Min(maxTargetingRange, Mathf.Max(targetDistance, origTargetDistance)) / simVelocity.magnitude / 2f, Time.fixedDeltaTime, Time.fixedDeltaTime * BDArmorySettings.BALLISTIC_TRAJECTORY_SIMULATION_MULTIPLIER); // With leap-frog, we can use a higher time-step and still get better accuracy than forward Euler (what was used before). Always take at least 2 steps though.
                    var timeOfFlight = BallisticTrajectorySimulation(ref simCurrPos, simVelocity, Mathf.Min(maxTargetingRange, targetDistance), maxTargetingRange / bulletVelocity, simDeltaTime, FlightGlobals.getAltitudeAtPos(targetPosition) < 0);
                    bulletPrediction = simCurrPos;
                    Vector3 pointingPos = fireTransform.position + (fireTransform.forward * targetDistance);
                    trajectoryOffset = pointingPos - bulletPrediction;
                }
                else if (eWeaponType == WeaponTypes.Rocket)
                {
                    float simTime = 0;
                    Vector3 pointingDirection = fireTransform.forward;
                    Vector3 simVelocity = part.rb.velocity + Krakensbane.GetFrameVelocityV3f();
                    Vector3 simCurrPos = fireTransform.position;
                    Vector3 simPrevPos = simCurrPos;
                    Vector3 simStartPos = simCurrPos;
                    float simDeltaTime = Time.fixedDeltaTime;
                    float atmosMultiplier = Mathf.Clamp01(2.5f * (float)FlightGlobals.getAtmDensity(vessel.staticPressurekPa, vessel.externalTemperature, vessel.mainBody));
                    bool slaved = turret && weaponManager && (weaponManager.slavingTurrets || weaponManager.guardMode);

                    if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DRAW_AIMERS)
                    {
                        if (trajectoryPoints == null) trajectoryPoints = new List<Vector3>();
                        trajectoryPoints.Clear();
                        trajectoryPoints.Add(simCurrPos);
                    }

                    // Bootstrap leap-frog
                    var gravity = FlightGlobals.getGeeForceAtPosition(simCurrPos);
                    if (FlightGlobals.RefFrameIsRotating)
                    { simVelocity += 0.5f * simDeltaTime * gravity; }
                    simVelocity += 0.5f * thrust / rocketMass * simDeltaTime * pointingDirection;

                    while (true)
                    {
                        RaycastHit hit;

                        // No longer thrusting, finish up with a ballistic sim.
                        if (simTime > thrustTime)
                        {
                            // Correct the velocity for the current time.
                            if (FlightGlobals.RefFrameIsRotating)
                            { simVelocity -= 0.5f * simDeltaTime * gravity; }
                            simVelocity -= 0.5f * thrust / rocketMass * simDeltaTime * pointingDirection; // Note: we're ignoring the underwater slow-down here.

                            var currentTargetDistance = Mathf.Min(maxTargetingRange, (simCurrPos - targetPosition).magnitude);
                            simDeltaTime = Mathf.Clamp(currentTargetDistance / simVelocity.magnitude / 2f, Time.fixedDeltaTime, Time.fixedDeltaTime * BDArmorySettings.BALLISTIC_TRAJECTORY_SIMULATION_MULTIPLIER);
                            var timeToCPA = AIUtils.ClosestTimeToCPA(targetPosition - simCurrPos, targetVelocity - simVelocity, targetAcceleration - gravity, maxTargetingRange / bulletVelocity - simTime); // For aiming, we want the closest approach to refine our aim.
                            bulletPrediction = AIUtils.PredictPosition(simCurrPos, simVelocity, gravity, timeToCPA);
                            simTime += timeToCPA;
                            if (BDArmorySettings.AIM_ASSIST && BDArmorySettings.DRAW_AIMERS) BallisticTrajectorySimulation(ref simCurrPos, simVelocity, currentTargetDistance, maxTargetingRange / bulletVelocity - simTime, simDeltaTime, FlightGlobals.getAltitudeAtPos(targetPosition) < 0, SimulationStage.Normal, false); // For visuals, we want the trajectory sim with collision detection. Note: this is done after to avoid messing with simCurrPos.
                            break;
                        }

                        // Update the current sim time.
                        simTime += simDeltaTime;

                        // Position update (current time).
                        simCurrPos += simVelocity * simDeltaTime;
                        if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DRAW_AIMERS)
                            trajectoryPoints.Add(simCurrPos);

                        // Check for collisions.
                        if (!aiControlled && !slaved)
                        {
                            if (Physics.Raycast(simPrevPos, simCurrPos - simPrevPos, out hit, Vector3.Distance(simPrevPos, simCurrPos), layerMask1))
                            {
                                /*
                                Vessel hitVessel = null;
                                try
                                {
                                    if (hit.collider.gameObject != FlightGlobals.currentMainBody.gameObject) // Ignore terrain hits. FIXME The collider could still be a building (SpaceCenterBuilding?), but chances of this is low.
                                    {
                                        KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                                        var part = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();
                                        if (part)
                                        {
                                            hitVessel = part.vessel;
                                        }
                                    }
                                }
                                catch (NullReferenceException e)
                                {
                                    Debug.LogError("[BDArmory.ModuleWeapon]: NullReferenceException while simulating trajectory: " + e.Message);
                                }

                                if (hitVessel == null || hitVessel != vessel)
                                {
                                    bulletPrediction = hit.point; //this is why rocket aimers appear a few meters infront of muzzle
                                    break;
                                }
                                */
                                try
                                {
                                    if (hit.collider.gameObject == FlightGlobals.currentMainBody.gameObject)
                                    {
                                        bulletPrediction = hit.point;
                                        break;
                                    }
                                    else
                                    {
                                        KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                                        DestructibleBuilding building = hit.collider.gameObject.GetComponentUpwards<DestructibleBuilding>();
                                        var part = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();
                                        if (part || building != null)
                                        {
                                            bulletPrediction = hit.point;
                                            break;
                                        }
                                    }
                                }
                                catch (NullReferenceException e)
                                {
                                    Debug.LogError("[BDArmory.ModuleWeapon]: NullReferenceException while simulating trajectory: " + e.Message);
                                }
                            }
                            //else if (FlightGlobals.getAltitudeAtPos(simCurrPos) < 0) // Note: this prevents aiming below sea-level. 
                            //{
                            //    bulletPrediction = simCurrPos;
                            //   break;
                            //}
                        }

                        // Book-keeping and max distance checks.
                        simPrevPos = simCurrPos;
                        if ((simStartPos - simCurrPos).sqrMagnitude > targetDistance * targetDistance)
                        {
                            bulletPrediction = simStartPos + (simCurrPos - simStartPos).normalized * targetDistance;
                            break;
                        }
                        if ((simStartPos - simCurrPos).sqrMagnitude > maxTargetingRange * maxTargetingRange)
                        {
                            bulletPrediction = simStartPos + ((simCurrPos - simStartPos).normalized * maxTargetingRange);
                            break;
                        }

                        // Rotation (aero stabilize).
                        pointingDirection = Vector3.RotateTowards(pointingDirection, simVelocity + Krakensbane.GetFrameVelocityV3f(), atmosMultiplier * (0.5f * simTime) * 50 * simDeltaTime * Mathf.Deg2Rad, 0);

                        // Velocity update (half of current time and half of the next... that's why it's called leapfrog).
                        if (simTime < thrustTime)
                        { simVelocity += thrust / rocketMass * simDeltaTime * pointingDirection; }
                        if (FlightGlobals.RefFrameIsRotating)
                        {
                            gravity = FlightGlobals.getGeeForceAtPosition(simCurrPos);
                            simVelocity += gravity * simDeltaTime;
                        }
                        if (BDArmorySettings.BULLET_WATER_DRAG)
                        {
                            if (FlightGlobals.getAltitudeAtPos(simCurrPos) < 0)
                            {
                                simVelocity += (-(0.5f * 1 * (simVelocity.magnitude * simVelocity.magnitude) * 0.5f * ((Mathf.PI * caliber * caliber * 0.25f) / 1000000)) * simDeltaTime) * pointingDirection;//this is going to throw off aiming code, but you aren't going to hit anything with rockets underwater anyway
                            }
                        }
                    }

                    // Visuals
                    if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DRAW_AIMERS)
                    {
                        trajectoryPoints.Add(bulletPrediction);
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
                        var offset = Krakensbane.GetFrameVelocity().IsZero() ? AIUtils.PredictPosition(Vector3.zero, vessel.Velocity(), vessel.acceleration, Time.fixedDeltaTime) : Vector3.zero;
                        using (var point = trajectoryPoints.GetEnumerator())
                            while (point.MoveNext())
                            {
                                trajectoryRenderer.SetPosition(i, point.Current + offset);
                                ++i;
                            }
                    }

                    Vector3 pointingPos = fireTransform.position + (fireTransform.forward * targetDistance);
                    trajectoryOffset = pointingPos - bulletPrediction;
                    predictedFlightTime = simTime;
                }
            }
        }

        public enum SimulationStage { Normal, Refining, Final };
        /// <summary>
        /// Use the leapfrog numerical integrator for a ballistic trajectory simulation under the influence of just gravity.
        /// The leapfrog integrator is a second-order symplectic method.
        /// 
        /// Note: Use this to see the trajectory with collision detection, but use BallisticTrajectoryClosestApproachSimulation instead for targeting purposes.
        /// </summary>
        /// <param name="position"></param>
        /// <param name="velocity"></param>
        /// <param name="maxTime"></param>
        /// <param name="timeStep"></param>
        public float BallisticTrajectorySimulation(ref Vector3 position, Vector3 velocity, float maxDistance, float maxTime, float timeStep, bool ignoreWater = false, SimulationStage stage = SimulationStage.Normal, bool resetTrajectoryPoints = true)
        {
            float elapsedTime = 0f;
            var startPosition = position;
            var maxDistanceSqr = maxDistance * maxDistance;
            if (FlightGlobals.getAltitudeAtPos(position) < 0) ignoreWater = true;
            var gravity = FlightGlobals.getGeeForceAtPosition(position);
            velocity += 0.5 * timeStep * gravity; // Boot-strap velocity calculation.
            Ray ray = new Ray();
            RaycastHit hit;
            if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DRAW_AIMERS)
            {
                if (trajectoryPoints == null) trajectoryPoints = new List<Vector3>();
                if (resetTrajectoryPoints) trajectoryPoints.Clear();
            }
            while (elapsedTime < maxTime)
            {
                ray.origin = position;
                ray.direction = velocity;
                var altitude = FlightGlobals.getAltitudeAtPos(position + velocity * timeStep);
                if ((Physics.Raycast(ray, out hit, timeStep * velocity.magnitude, layerMask1) && (hit.collider != null && hit.collider.gameObject != null && hit.collider.gameObject.GetComponentInParent<Part>() != part)) // Ignore the part firing the projectile.
                    || (!ignoreWater && altitude < 0))
                {
                    switch (stage)
                    {
                        case SimulationStage.Normal:
                            if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DRAW_AIMERS && trajectoryPoints.Count == 0)
                                trajectoryPoints.Add(position);
                            goto case SimulationStage.Refining;
                        case SimulationStage.Refining: // Perform a more accurate final step for the collision.
                            velocity -= 0.5f * timeStep * gravity; // Correction to final velocity.
                            var finalTime = BallisticTrajectorySimulation(ref position, velocity, velocity.magnitude * timeStep, timeStep, timeStep / 4f, ignoreWater, timeStep > 5f * Time.fixedDeltaTime ? SimulationStage.Refining : SimulationStage.Final, false);
                            elapsedTime += finalTime;
                            break;
                        case SimulationStage.Final:
                            if (!ignoreWater && altitude < 0)
                            {
                                var currentAltitude = FlightGlobals.getAltitudeAtPos(position);
                                timeStep *= currentAltitude / (currentAltitude - altitude);
                                elapsedTime += timeStep;
                                position += timeStep * velocity;
                                // Debug.Log("DEBUG breaking trajectory sim due to water at " + position.ToString("F6") + " at altitude " + FlightGlobals.getAltitudeAtPos(position));
                            }
                            else
                            {
                                elapsedTime += (hit.point - position).magnitude / velocity.magnitude;
                                position = hit.point;
                                // Debug.Log("DEBUG breaking trajectory sim due to hit at " + position.ToString("F6") + " at altitude " + FlightGlobals.getAltitudeAtPos(position));
                            }
                            break;
                    }
                    break;
                }
                if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DRAW_AIMERS && stage != SimulationStage.Final)
                    trajectoryPoints.Add(position);
                position += timeStep * velocity;
                gravity = FlightGlobals.getGeeForceAtPosition(position);
                velocity += timeStep * gravity;
                elapsedTime += timeStep;
                if ((startPosition - position).sqrMagnitude > maxDistanceSqr)
                {
                    // Debug.Log("DEBUG breaking trajectory sim due to max distance: " + maxDistance.ToString("F6") + " at altitude " + FlightGlobals.getAltitudeAtPos(position));
                    break;
                }
            }
            if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DRAW_AIMERS && stage == SimulationStage.Normal)
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
                var offset = Krakensbane.GetFrameVelocity().IsZero() ? AIUtils.PredictPosition(Vector3.zero, vessel.Velocity(), vessel.acceleration, Time.fixedDeltaTime) : Vector3.zero;
                using (var point = trajectoryPoints.GetEnumerator())
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
        /// </summary>
        /// <param name="position"></param>
        /// <param name="velocity"></param>
        /// <param name="targetPosition"></param>
        /// <param name="targetVelocity"></param>
        /// <param name="targetAcceleration"></param>
        /// <param name="stage"></param>
        /// <returns>The CPA to the target.</returns>
        public Vector3 BallisticTrajectoryClosestApproachSimulation(Vector3 position, Vector3 velocity, Vector3 targetPosition, Vector3 targetVelocity, Vector3 targetAcceleration, float timeStep, float elapsedTime = 0, SimulationStage stage = SimulationStage.Normal)
        {
            var predictedTargetPosition = AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, elapsedTime);
            var lastPosition = position;
            var lastPredictedTargetPosition = predictedTargetPosition;
            var gravity = FlightGlobals.getGeeForceAtPosition(position);
            velocity += 0.5f * timeStep * gravity;
            var simStartTime = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - simStartTime < 0.1f) // Allow 0.1s of real-time for the simulation. This ought to be plenty. FIXME Find a better way to detect when this loop will never exit.
            {
                lastPosition = position;
                lastPredictedTargetPosition = predictedTargetPosition;

                position += timeStep * velocity;
                predictedTargetPosition = AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, elapsedTime + timeStep);
                if (Vector3.Dot(predictedTargetPosition - position, velocity) < 0f)
                {
                    switch (stage)
                    {
                        case SimulationStage.Normal:
                        case SimulationStage.Refining: // Perform a more accurate final step for the collision.
                            return BallisticTrajectoryClosestApproachSimulation(lastPosition, velocity - 0.5f * timeStep * gravity, targetPosition, targetVelocity, targetAcceleration, timeStep / 4f, elapsedTime, timeStep > 5f * Time.fixedDeltaTime ? SimulationStage.Refining : SimulationStage.Final);
                        case SimulationStage.Final:
                            var timeToCPA = AIUtils.ClosestTimeToCPA(lastPosition - lastPredictedTargetPosition, velocity - (predictedTargetPosition - lastPredictedTargetPosition) / timeStep, Vector3.zero, timeStep);
                            position = lastPosition + timeToCPA * velocity;
                            predictedTargetPosition = AIUtils.PredictPosition(targetPosition, targetVelocity, targetAcceleration, elapsedTime + timeToCPA);
                            return position;
                    }
                }
                gravity = FlightGlobals.getGeeForceAtPosition(position);
                velocity += timeStep * gravity;
                elapsedTime += timeStep;
            }
            Debug.LogWarning("[BDArmory.ModuleWeapon]: Ballistic trajectory closest approach simulation timed out.");
            return position;
        }

        //more organization, grouping like with like
        public Vector3 GetLeadOffset()
        {
            return fixedLeadOffset;
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
                var maxAutoFireCosAngle2 = targetAdjustedMaxCosAngle;

                safeToFire = CheckForFriendlies(fireTransform);
                if (safeToFire)
                {
                    if (eWeaponType == WeaponTypes.Ballistic || eWeaponType == WeaponTypes.Laser)
                    { autoFire = (targetCosAngle >= targetAdjustedMaxCosAngle); }
                    else // Rockets
                    { autoFire = (targetCosAngle >= targetAdjustedMaxCosAngle) && ((finalAimTarget - fireTransform.position).sqrMagnitude > blastRadius * blastRadius); }
                }
                else
                {
                    autoFire = false;
                }

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
            if (BurstOverride)
            {
                if (autoFire && autofireShotCount >= fireBurstLength)
                {
                    autoFire = false;
                    visualTargetVessel = null;
                    visualTargetPart = null;
                    tgtShell = null;
                    tgtRocket = null;
                    autofireShotCount = 0;
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
            }
            else
            {
                if (autoFire && Time.time - autoFireTimer > autoFireLength)
                {
                    autoFire = false;
                    visualTargetVessel = null;
                    visualTargetPart = null;
                    tgtShell = null;
                    tgtRocket = null;
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
            var projectileEffectiveVelocity = part.rb.velocity + (eWeaponType == WeaponTypes.Rocket ? (Krakensbane.GetFrameVelocityV3f() + thrust * thrustTime / rocketMass * firingDirection) : (baseBulletVelocity * firingDirection));
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
                    var timeToCPA = AIUtils.ClosestTimeToCPA(projectileRelativePosition, projectileRelativeVelocity, projectileRelativeAcceleration, maxTargetingRange / projectileEffectiveVelocity.magnitude);
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
            if (((userFiring || agHoldFiring) && !isAPS) || (autoFire && (!turret || turret.TargetInRange(finalAimTarget, 10, float.MaxValue))) || (BurstFire && RoundsRemaining > 0 && RoundsRemaining < RoundsPerMag))
            {
                if ((pointingAtSelf || isOverheated || isReloading) || (aiControlled && engageRangeMax < targetDistance))// is weapon within set max range?
                {
                    if (useRippleFire) //old method wouldn't catch non-ripple guns (i.e. Vulcan) trying to fire at targets beyond fire range
                    {
                        //StartCoroutine(IncrementRippleIndex(0));
                        StartCoroutine(IncrementRippleIndex(initialFireDelay * TimeWarp.CurrentRate));
                        isRippleFiring = true;
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
                    if (!useRippleFire || weaponManager.gunRippleIndex == rippleIndex) // Don't fire rippling weapons when they're on the wrong part of the cycle. Spool up and grow lasers though.
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
                                startColorS[0] = Mathf.Lerp(Single.Parse(startColorS[0]), Single.Parse(endColorS[0]), 0.02f / LaserGrowTime).ToString();
                                startColorS[1] = Mathf.Lerp(Single.Parse(startColorS[1]), Single.Parse(endColorS[1]), 0.02f / LaserGrowTime).ToString();
                                startColorS[2] = Mathf.Lerp(Single.Parse(startColorS[2]), Single.Parse(endColorS[2]), 0.02f / LaserGrowTime).ToString();
                                startColorS[3] = Mathf.Lerp(Single.Parse(startColorS[3]), Single.Parse(endColorS[3]), 0.02f / LaserGrowTime).ToString();
                            }
                            for (int i = 0; i < 4; i++)
                            {
                                projectileColorC[i] = Single.Parse(startColorS[i]) / 255;
                            }
                        }
                        UpdateLaserSpecifics(DynamicBeamColor, dynamicFX, LaserGrowTime > 0, beamScrollRate != 0);
                    }
                }
            }
            else
            {
                if (weaponManager != null && weaponManager.gunRippleIndex == rippleIndex)
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
                    if ((!pulseLaser && !BurstFire) || (!pulseLaser && BurstFire && (RoundsRemaining >= RoundsPerMag)) || (pulseLaser && Time.time - timeFired > beamDuration))
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
            }
        }

        void AimAndFire()
        {
            // This runs in the FashionablyLate timing phase of FixedUpdate before Krakensbane corrections have been applied.
            if (!(aimAndFireIfPossible || aimOnly)) return;
            if (this == null || weaponManager == null || FlightGlobals.currentMainBody == null) return;

            if (isAPS)
            {
                TrackIncomingprojectile();
            }
            else
            {
                UpdateTargetVessel();
            }
            if (targetAcquired)
            {
                // updateAcceleration(targetVelocity, targetPosition, lastTargetAcquisitionType != targetAcquisitionType || (targetAcquisitionType == TargetAcquisitionType.Visual && lastVisualTargetVessel != visualTargetVessel));
                smoothTargetKinematics(targetPosition, targetVelocity, targetAcceleration, lastTargetAcquisitionType != targetAcquisitionType || (targetAcquisitionType == TargetAcquisitionType.Visual && lastVisualTargetVessel != visualTargetVessel));
            }

            RunTrajectorySimulation();
            Aim();
            if (aimAndFireIfPossible)
            {
                CheckWeaponSafety();
                CheckAIAutofire();
                CheckFinalFire();
                // if (BDArmorySettings.DEBUG_LABELS) Debug.Log("DEBUG " + vessel.vesselName + " targeting visualTargetVessel: " + visualTargetVessel + ", finalFire: " + finalFire + ", pointingAtSelf: " + pointingAtSelf + ", targetDistance: " + targetDistance);

                if (finalFire)
                {
                    if (ChargeTime > 0)
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
                                    if ((!pulseLaser && !BurstFire) || (!pulseLaser && BurstFire && (RoundsRemaining >= RoundsPerMag)) || (pulseLaser && Time.time - timeFired > beamDuration))
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
                                if (isAPS && (tgtShell != null || tgtRocket != null))
                                {
                                    StartCoroutine(KillIncomingProjectile(tgtShell, tgtRocket));
                                }
                                break;
                            case WeaponTypes.Rocket:
                                FireRocket();
                                if (isAPS && (tgtShell != null || tgtRocket != null))
                                {
                                    StartCoroutine(KillIncomingProjectile(tgtShell, tgtRocket));
                                }
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

            Vector2 guiPos;
            if (GUIUtils.WorldToGUIPos(fwdPos, out guiPos))
            {
                Rect angleRect = new Rect(guiPos.x, guiPos.y, 100, 200);

                Vector3 pitchVector = (5 * Vector3.ProjectOnPlane(fireTransforms[0].forward, refRight));
                Vector3 yawVector = (5 * Vector3.ProjectOnPlane(fireTransforms[0].forward, refUp));

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
                    convergeDistance = "Converge: " +
                                       Mathf.Round((xDist * Mathf.Tan(convergeAngle * Mathf.Deg2Rad))).ToString() + "m";
                }
                else
                {
                    convergeDistance = "Diverging";
                }

                string xAngle = "X: " + Vector3.Angle(fireTransforms[0].forward, pitchVector).ToString("0.00");
                string yAngle = "Y: " + Vector3.Angle(fireTransforms[0].forward, yawVector).ToString("0.00");

                GUI.Label(angleRect, xAngle + "\n" + yAngle + "\n" + convergeDistance);
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
                if (!oneShotSound) audioSource.Stop();
                wasFiring = false;
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
                ReloadTimer = Mathf.Clamp((ReloadTimer + 1 * TimeWarp.fixedDeltaTime / ReloadTime), 0, 1);
                if (hasDeployAnim)
                {
                    AnimTimer = Mathf.Clamp((AnimTimer + 1 * TimeWarp.fixedDeltaTime / (ReloadAnimTime)), 0, 1);
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
                if (!oneShotSound) audioSource.Stop();
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
                    StopShutdownStartupRoutines();
                    shutdownRoutine = StartCoroutine(ShutdownRoutine(true));
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
            }
        }
        void UpdateTargetVessel()
        {
            targetAcquired = false;
            slaved = false;
            bool atprWasAcquired = atprAcquired;
            atprAcquired = false;
            lastTargetAcquisitionType = targetAcquisitionType;

            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
            {
                if (Time.time - lastGoodTargetTime > Mathf.Max(BDArmorySettings.FIRE_RATE_OVERRIDE / 60f, weaponManager.targetScanInterval))
                {
                    targetAcquisitionType = TargetAcquisitionType.None;
                }
            }
            else
            {
                if (Time.time - lastGoodTargetTime > Mathf.Max(roundsPerMinute / 60f, weaponManager.targetScanInterval))
                {
                    targetAcquisitionType = TargetAcquisitionType.None;
                }
            }
            lastVisualTargetVessel = visualTargetVessel;

            if (weaponManager)
            {
                //legacy or visual range guard targeting
                if (aiControlled && weaponManager && visualTargetVessel &&
                    (visualTargetVessel.transform.position - transform.position).sqrMagnitude < weaponManager.guardRange * weaponManager.guardRange)
                {
                    targetRadius = visualTargetVessel.GetRadius();

                    if (visualTargetPart == null || visualTargetPart.vessel != visualTargetVessel)
                    {
                        TargetInfo currentTarget = visualTargetVessel.gameObject.GetComponent<TargetInfo>();
                        if (currentTarget == null)
                        {
                            if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log("[BDArmory.ModuleWeapon]: Targeted vessel " + (visualTargetVessel != null ? visualTargetVessel.vesselName : "'unknown'") + " has no TargetInfo.");
                            return;
                        }
                        List<Part> targetparts = new List<Part>();
                        if (targetCOM)
                        {
                            targetPosition = visualTargetVessel.CoM;
                            visualTargetPart = null; //make sure this gets reset
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
                                if (BDArmorySettings.DEBUG_WEAPONS) Debug.Log("[BDArmory.ModuleWeapon]: Targeted vessel " + visualTargetVessel.vesselName + " has no targetable parts.");
                                targetPosition = visualTargetVessel.CoM;
                            }
                            else
                            {
                                visualTargetPart = targetparts[targetID];
                                targetPosition = visualTargetPart.transform.position;
                            }
                        }
                    }
                    else
                    {
                        if (targetCOM)
                        {
                            targetPosition = visualTargetVessel.CoM;
                            visualTargetPart = null; //make sure these get reset
                        }
                        else
                        {
                            targetPosition = visualTargetPart.transform.position;
                        }
                    }
                    targetVelocity = visualTargetVessel.rb_velocity;
                    targetAcceleration = visualTargetVessel.acceleration;
                    targetAcquired = true;
                    targetAcquisitionType = TargetAcquisitionType.Visual;
                    return;
                }

                if (weaponManager.slavingTurrets && turret)
                {
                    slaved = true;
                    targetRadius = weaponManager.slavedTarget.vessel != null ? weaponManager.slavedTarget.vessel.GetRadius() : 35f;
                    targetPosition = weaponManager.slavedPosition;
                    targetVelocity = weaponManager.slavedTarget.vessel != null ? weaponManager.slavedTarget.vessel.rb_velocity : (weaponManager.slavedVelocity - Krakensbane.GetFrameVelocityV3f());
                    //targetAcceleration = weaponManager.slavedTarget.vessel != null ? weaponManager.slavedTarget.vessel.acceleration : weaponManager.slavedAcceleration;
                    //CS0172 Type of conditional expression cannot be determined because 'Vector3' and 'Vector3' implicitly convert to one another
                    if (weaponManager.slavedTarget.vessel != null) targetAcceleration = weaponManager.slavedTarget.vessel.acceleration;
                    else
                        targetAcceleration = weaponManager.slavedAcceleration;
                    targetAcquired = true;
                    targetAcquisitionType = TargetAcquisitionType.Slaved;
                    return;
                }

                if (weaponManager.vesselRadarData && weaponManager.vesselRadarData.locked)
                {
                    TargetSignatureData targetData = weaponManager.vesselRadarData.lockedTargetData.targetData;
                    targetVelocity = targetData.velocity - Krakensbane.GetFrameVelocityV3f();
                    targetPosition = targetData.predictedPosition;
                    targetRadius = 35f;
                    targetAcceleration = targetData.acceleration;
                    if (targetData.vessel)
                    {
                        targetVelocity = targetData.vessel != null ? targetData.vessel.rb_velocity : targetVelocity;
                        targetPosition = targetData.vessel.CoM;
                        targetAcceleration = targetData.vessel.acceleration;
                        targetRadius = targetData.vessel.GetRadius();
                    }
                    targetAcquired = true;
                    targetAcquisitionType = TargetAcquisitionType.Radar;
                    return;
                }

                //auto proxy tracking
                if (vessel.isActiveVessel && autoProxyTrackRange > 0)
                {
                    if (aptrTicker < 20)
                    {
                        aptrTicker++;

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
                        using (var v = BDATargetManager.LoadedVessels.GetEnumerator())
                            while (v.MoveNext())
                            {
                                if (v.Current == null || !v.Current.loaded || VesselModuleRegistry.ignoredVesselTypes.Contains(v.Current.vesselType)) continue;
                                if (!v.Current.IsControllable) continue;
                                if (v.Current == vessel) continue;
                                Vector3 targetVector = v.Current.transform.position - part.transform.position;
                                if (Vector3.Dot(targetVector, fireTransforms[0].forward) < 0) continue;
                                float sqrDist = (v.Current.transform.position - part.transform.position).sqrMagnitude;
                                if (sqrDist > closestSqrDist) continue;
                                if (Vector3.Angle(targetVector, fireTransforms[0].forward) > 20) continue;
                                tgt = v.Current;
                                closestSqrDist = sqrDist;
                            }

                        if (tgt == null) return;
                        targetAcquired = true;
                        atprAcquired = true;
                        targetRadius = tgt.GetRadius();
                        targetPosition = tgt.CoM;
                        targetVelocity = tgt.rb_velocity;
                        targetAcceleration = tgt.acceleration;
                    }
                    targetAcquisitionType = TargetAcquisitionType.AutoProxy;
                }
            }
        }

        void TrackIncomingprojectile() //this is holding onto initial target for some reason, not properly nulling target somewher it should be nulled
        {
            targetAcquired = false;
            slaved = false;
            atprAcquired = false;
            lastTargetAcquisitionType = targetAcquisitionType;

            if (Time.time - lastGoodTargetTime > Mathf.Max(roundsPerMinute / 60f, weaponManager.targetScanInterval))
            {
                targetAcquisitionType = TargetAcquisitionType.None;
            }

            if (weaponManager && aiControlled)
            {
                //if (tgtShell == null && tgtRocket == null && MissileTgt == null)
                {
                    if (eAPSType == APSTypes.Ballistic || eAPSType == APSTypes.Omni)
                    {
                        if (BDATargetManager.FiredBullets.Count > 0)
                        {
                            using (List<PooledBullet>.Enumerator target = BDATargetManager.FiredBullets.GetEnumerator())
                                while (target.MoveNext())
                                {
                                    if (target.Current == null) continue;

                                    if ((target.Current.currPosition - fireTransforms[0].position).sqrMagnitude < (maxTargetingRange * 2) * (maxTargetingRange * 2))
                                    {
                                        if (RadarUtils.TerrainCheck(target.Current.transform.position, transform.position))
                                        {
                                            continue;
                                        }
                                        else
                                        {
                                            if (target.Current.team != weaponManager.team)
                                            {
                                                if ((closestTarget == Vector3.zero || (target.Current.transform.position - fireTransforms[0].position).sqrMagnitude < (closestTarget - fireTransforms[0].position).sqrMagnitude))
                                                {
                                                    closestTarget = target.Current.currPosition;
                                                    //tgtVelocity = target.Current.currentVelocity;
                                                    tgtShell = target.Current;
                                                    visualTargetPart = null; //make sure this gets reset
                                                    tgtRocket = null;
                                                }
                                            }
                                            else continue;
                                        }
                                    }
                                }
                        }
                        else tgtShell = null;
                    }
                    if (eAPSType == APSTypes.Missile || eAPSType == APSTypes.Omni)
                    {
                        if (BDATargetManager.FiredRockets.Count > 0)
                        {
                            using (List<PooledRocket>.Enumerator target = BDATargetManager.FiredRockets.GetEnumerator())
                                while (target.MoveNext())
                                {
                                    if (target.Current == null) continue;

                                    if ((target.Current.transform.position - fireTransforms[0].position).sqrMagnitude < (maxTargetingRange * 2) * (maxTargetingRange * 2))
                                    {
                                        if (RadarUtils.TerrainCheck(target.Current.transform.position, transform.position))
                                        {
                                            continue;
                                        }
                                        else
                                        {
                                            if (target.Current.team != weaponManager.team)
                                            {
                                                if ((closestTarget == Vector3.zero || (target.Current.transform.position - fireTransforms[0].position).sqrMagnitude < (closestTarget - fireTransforms[0].position).sqrMagnitude))
                                                {
                                                    closestTarget = target.Current.transform.position;
                                                    //tgtVelocity = target.Current.currentVelocity;
                                                    tgtRocket = target.Current;
                                                    tgtShell = null;
                                                    visualTargetPart = null; //make sure this gets reset
                                                }
                                            }
                                            else continue;
                                        }
                                    }
                                }
                        }
                        else tgtRocket = null;
                        if (BDATargetManager.FiredMissiles.Count > 0)
                        {
                            MissileTgt = BDATargetManager.GetClosestMissileTarget(weaponManager);
                            if ((MissileTgt.transform.position - fireTransforms[0].position).sqrMagnitude < (maxTargetingRange * 2) * (maxTargetingRange * 2))
                            {
                                if ((closestTarget == Vector3.zero || (MissileTgt.transform.position - fireTransforms[0].position).sqrMagnitude < (closestTarget - fireTransforms[0].position).sqrMagnitude))
                                {
                                    closestTarget = MissileTgt.transform.position;
                                    //tgtVelocity = MissileTgt.velocity;
                                    visualTargetPart = MissileTgt.Vessel.Parts.FirstOrDefault();
                                    tgtShell = null;
                                    tgtRocket = null;
                                }
                            }
                        }
                        else visualTargetPart = null;
                    }
                }
                if (tgtShell != null || tgtRocket != null || MissileTgt != null)
                {
                    if (tgtShell != null)
                    {
                        targetVelocity = tgtShell.currentVelocity;
                        targetPosition = tgtShell.currPosition;
                    }
                    else if (tgtRocket != null)
                    {
                        targetVelocity = tgtRocket.currentVelocity;
                        targetPosition = tgtRocket.currPosition;
                    }
                    else
                    {
                        targetVelocity = visualTargetPart.vessel.rb_velocity;
                        targetPosition = visualTargetPart.transform.position;
                    }
                    targetVelocity -= Krakensbane.GetFrameVelocityV3f();
                    targetRadius = 1;

                    targetAcceleration = MissileTgt != null && MissileTgt.Vessel != null ? (Vector3)MissileTgt.Vessel.acceleration : Vector3.zero;
                    targetAcquired = true;
                    targetAcquisitionType = TargetAcquisitionType.Radar;

                    Debug.Log("[APS DEBUG] tgtVelocity: " + tgtVelocity + "; tgtPosition: " + targetPosition + "; tgtAccel: " + targetAcceleration);
                    Debug.Log("[APS DEBUG] Lead Offset: " + fixedLeadOffset + ", FinalAimTgt: " + finalAimTarget + ", tgt CosAngle " + targetCosAngle + ", wpn CosAngle " + targetAdjustedMaxCosAngle + ", Wpn Autofire: " + autoFire);
                    return;
                }
                //Need to get bullet trajectory - is it on a collision course? Moght be able to plug into the TimeToCPA to see if it's going to hit, and if not, ignore it
            }
        }

        IEnumerator KillIncomingProjectile(PooledBullet shell, PooledRocket rocket)
        {
            //So, uh, this is fine for simgle shot APS; what about conventional CIWS type AMS using rotary cannon for dakka vs accuracy?
            //should include a check for non-explosive rounds merely getting knocked off course instead of exploded.
            //should this be shell size dependant? I.e. sure, an APS can knock a sabot offcourse with a 60mm interceptor; what about that same 60mm shot vs a 155mm arty shell? or a 208mm naval gun?
            //really only an issue in case of AP APS (e.g. flechette APS for anti-missile work) vs AP shell; HE APS rounds should be able to destroy inncoming proj
            float delayTime = eWeaponType == WeaponTypes.Ballistic ? (targetDistance / bulletVelocity) : (eWeaponType == WeaponTypes.Rocket ? predictedFlightTime : -1);
            if (delayTime < 0)
            {
                delayTime = rocket != null ? 0.5f : (shell.bulletMass * (1 - Mathf.Clamp(shell.tntMass / shell.bulletMass, 0f, 0.95f) / 2)); //for shells, laser delay time is based on shell mass/HEratio. The heavier the shell, the mroe mass to burn through. Don't expect to stop sabots via laser APS
                delayTime /= ((laserDamage / (1 + Mathf.PI * Mathf.Pow(tanAngle * targetDistance, 2)) * 0.425f) / 100);
                if (delayTime < TimeWarp.fixedDeltaTime) delayTime = 0;
            }
            yield return new WaitForSeconds(delayTime);
            if (shell != null)
            {
                if (shell.tntMass > 0)
                {
                    shell.hasDetonated = true;
                    ExplosionFx.CreateExplosion(shell.transform.position, shell.tntMass, shell.explModelPath, shell.explSoundPath, ExplosionSourceType.Bullet, shell.caliber, null, shell.sourceVesselName, null, default, -1, false, shell.bulletMass, -1, 1);
                    shell.KillBullet();
                    Debug.Log("[BDArmory.ModuleWeapon] Detonated Incoming Projectile!");
                }
                else
                {
                    if (eWeaponType == WeaponTypes.Laser)
                    {
                        shell.KillBullet();
                        Debug.Log("[BDArmory.ModuleWeapon] Vaporized Incoming Projectile!");
                    }
                    else
                    {
                        if (tntMass <= 0) //e.g. APS flechettes vs sabot
                        {
                            shell.bulletMass -= bulletMass;
                            shell.currentVelocity = VectorUtils.GaussianDirectionDeviation(shell.currentVelocity, ((shell.bulletMass * shell.currentVelocity.magnitude) / (bulletMass * bulletVelocity)));
                            //shell.caliber = //have some modification of caliber to sim knocking round off-prograde?
                            //Thing is, something like a sabot liable to have lever action work upon it, spin it so it now hits on it's side instead of point first, but a heavy arty shell you have both substantially greater mass to diflect, and lesser increase in caliber from perpendicular hit - sabot from point on to side on is like a ~10x increase, a 208mm shell is like 1.2x 
                            //there's also the issue of gross modification of caliber in this manner if the shell recieves multiple impacts from APS interceptors before it hits; would either need to be caliber = x, which isn't appropraite for heavy shells that would not be easily knocked off course, or caliber +=, which isn't viable for sabots
                            //easiest way would just have the APS interceptor destroy the incoming round, regardless; and just accept the occasional edge cases like a flechetteammo APS being able to destroy AP naval shells instead of tickling them and not much else
                        }
                        else
                        {
                            shell.KillBullet();
                            Debug.Log("[BDArmory.ModuleWeapon] Exploded Incoming Projectile!");
                        }
                    }
                }
                shell = null;
                tgtShell = null;
            }
            else if (rocket != null)
            {
                if (rocket.tntMass > 0)
                {
                    rocket.hasDetonated = true;
                    ExplosionFx.CreateExplosion(rocket.transform.position, rocket.tntMass, rocket.explModelPath, rocket.explSoundPath, ExplosionSourceType.Rocket, rocket.caliber, null, rocket.sourceVesselName, null, default, -1, false, rocket.rocketMass * 1000, -1, 1);
                }
                rocket.gameObject.SetActive(false);
                rocket = null;
                tgtRocket = null;
            }
            else
            {
                Debug.Log("[BDArmory.ModuleWeapon] KillIncomingProjectile called on null object!");
            }
        }

        /// <summary>
        /// Update target acceleration based on previous velocity.
        /// Position is used to clamp acceleration for splashed targets, as ksp produces excessive bobbing.
        /// </summary>
        void updateAcceleration(Vector3 target_rb_velocity, Vector3 position, bool reset = false)
        {
            var distance = Vector3.Distance(targetPosition, part.transform.position);
            var accelerationSmoothingFactor = 512f / (Mathf.Max(distance, 512f)); // Dynamically adjust the smoothing from none at 512m to 127/128 at 65.536km
            var velocitySmoothingFactor = 2048f / (Mathf.Max(distance, 2048f)); // Dynamically adjust the smoothing from none at 2048m to 31/32 at 65.536km
            if (!reset)
            {
                if (!FloatingOrigin.Offset.IsZero() || !Krakensbane.GetFrameVelocity().IsZero())
                {
                    targetVelocityPrevious += (Vector3)Krakensbane.GetLastCorrection();
                }
                if (!targetAccelerationWasReset)
                {
                    targetVelocity = (1f - velocitySmoothingFactor) * targetVelocityPrevious + velocitySmoothingFactor * target_rb_velocity; // Smooth out the velocity to account for imprecise velocity from KSP.
                    targetAcceleration = (1f - accelerationSmoothingFactor) * targetAcceleration + accelerationSmoothingFactor * (target_rb_velocity - targetVelocityPrevious) / (Time.time - lastGoodTargetTime); // Smooth out the acceleration calculation to account for oscillating control surfaces in the target.
                }
                else
                {
                    targetAcceleration = (target_rb_velocity - targetVelocityPrevious) / (Time.time - lastGoodTargetTime);
                    targetAccelerationWasReset = false;
                }
            }
            else
            {
                targetAcceleration = Vector3.zero;
                targetAccelerationWasReset = true;
            }
            float altitude = (float)FlightGlobals.currentMainBody.GetAltitude(position);
            if (altitude < 12 && altitude > -10)
                targetAcceleration = Vector3.ProjectOnPlane(targetAcceleration, VectorUtils.GetUpDirection(position));
            // Debug.Log("DEBUG " + Time.time.ToString("0.00") + " targetAcceleration: " + targetAcceleration.ToString("G3") + ", V_diff: " + (targetVelocity - targetVelocityPrevious).ToString("G3") + ", smoothing: (" + velocitySmoothingFactor.ToString("G3") + ", " + accelerationSmoothingFactor.ToString("G3") + "), Δt: " + (Time.time - lastGoodTargetTime));
            targetVelocityPrevious = target_rb_velocity;
        }

        /// <summary>
        /// Apply Brown's double exponential smoothing to the target velocity and acceleration values to smooth out noise.
        /// The smoothing factor depends on the distance to the target.
        /// </summary>
        /// <param name="position"></param>
        /// <param name="velocity"></param>
        /// <param name="acceleration"></param>
        /// <param name="reset"></param>
        void smoothTargetKinematics(Vector3 position, Vector3 velocity, Vector3 acceleration, bool reset = false)
        {
            // Floating objects need vertical smoothing.
            float altitude = (float)FlightGlobals.currentMainBody.GetAltitude(position);
            if (altitude < 12 && altitude > -10)
                acceleration = Vector3.ProjectOnPlane(acceleration, VectorUtils.GetUpDirection(position));

            var distance = Vector3.Distance(position, part.transform.position);
            var alpha = Mathf.Max(1f - Mathf.Sqrt(distance) / 512f, 0.1f);
            var beta = alpha * alpha;
            if (!reset)
            {
                targetVelocityS1 = alpha * velocity + (1f - alpha) * targetVelocityS1;
                targetVelocityS2 = alpha * targetVelocityS1 + (1f - alpha) * targetVelocityS2;
                targetVelocity = 2f * targetVelocityS1 - targetVelocityS2;
                targetAccelerationS1 = beta * acceleration + (1f - beta) * targetAccelerationS1;
                targetAccelerationS2 = beta * targetAccelerationS1 + (1f - beta) * targetAccelerationS2;
                targetAcceleration = 2f * targetAccelerationS1 - targetAccelerationS2;
            }
            else
            {
                targetVelocityS1 = velocity;
                targetVelocityS2 = velocity;
                targetVelocity = velocity;
                targetAccelerationS1 = acceleration;
                targetAccelerationS2 = acceleration;
                targetAcceleration = acceleration;
            }
        }

        void UpdateGUIWeaponState()
        {
            guiStatusString = weaponState.ToString();
        }

        IEnumerator StartupRoutine(bool calledByReload = false)
        {
            if (hasReloadAnim && isReloading) //wait for reload to finish before shutting down
            {
                while (reloadState.normalizedTime < 1)
                {
                    yield return null;
                }
            }
            if (!calledByReload)
            {
                UpdateGUIWeaponState();
            }
            if (hasDeployAnim && deployState)
            {
                deployState.enabled = true;
                deployState.speed = 1;
                while (deployState.normalizedTime < 1) //wait for animation here
                {
                    yield return null;
                }
                deployState.normalizedTime = 1;
                deployState.speed = 0;
                deployState.enabled = false;
            }
            if (!calledByReload)
            {
                weaponState = WeaponStates.Enabled;
            }
            UpdateGUIWeaponState();
            BDArmorySetup.Instance.UpdateCursorState();
        }
        IEnumerator ShutdownRoutine(bool calledByReload = false)
        {
            if (hasReloadAnim && isReloading) //wait for relaod to finish before shutting down
            {
                while (reloadState.normalizedTime < 1)
                {
                    yield return null;
                }
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
                yield return new WaitForSeconds(0.2f);

                while (!turret.ReturnTurret()) //wait till turret has returned
                {
                    yield return new WaitForFixedUpdate();
                }
            }
            if (hasDeployAnim)
            {
                deployState.enabled = true;
                deployState.speed = -1;
                while (deployState.normalizedTime > 0)
                {
                    yield return null;
                }
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

            reloadState.normalizedTime = 0;
            reloadState.enabled = true;
            reloadState.speed = (reloadState.length / ReloadTime);//ensure relaod anim is not longer than reload time
            while (reloadState.normalizedTime < 1) //wait for animation here
            {
                yield return null;
            }
            reloadState.normalizedTime = 1;
            reloadState.speed = 0;
            reloadState.enabled = false;

            UpdateGUIWeaponState();
        }
        IEnumerator ChargeRoutine()
        {
            isCharging = true;
            guiStatusString = "Charging";
            if (!String.IsNullOrEmpty(chargeSoundPath))
            {
                audioSource.PlayOneShot(chargeSound);
            }
            if (hasChargeAnimation)
            {
                chargeState.normalizedTime = 0;
                chargeState.enabled = true;
                chargeState.speed = (chargeState.length / ChargeTime);//ensure relaod anim is not longer than reload time
                while (chargeState.normalizedTime < 1) //wait for animation here
                {
                    yield return null;
                }
                chargeState.normalizedTime = 1;
                chargeState.speed = 0;
                chargeState.enabled = false;
            }
            else
            {
                yield return new WaitForSeconds(ChargeTime);
            }
            UpdateGUIWeaponState();
            isCharging = false;
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
                        if ((!pulseLaser && !BurstFire) || (!pulseLaser && BurstFire && (RoundsRemaining >= RoundsPerMag)) || (pulseLaser && Time.time - timeFired > beamDuration))
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
            }
        }

        void ParseBulletFuzeType(string type)
        {
            type = type.ToLower();
            switch (type)
            {
                //Anti-Air fuzes
                case "timed":
                    eFuzeType = FuzeTypes.Timed;
                    break;
                case "proximity":
                    eFuzeType = FuzeTypes.Proximity;
                    break;
                case "flak":
                    eFuzeType = FuzeTypes.Flak;
                    break;
                //Anti-Armor fuzes
                case "delay":
                    eFuzeType = FuzeTypes.Delay;
                    break;
                case "penetrating":
                    eFuzeType = FuzeTypes.Penetrating;
                    break;
                case "impact":
                    eFuzeType = FuzeTypes.Impact;
                    break;
                case "none":
                    eFuzeType = FuzeTypes.Impact;
                    break;
                default:
                    eFuzeType = FuzeTypes.None;
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
                    eAPSType = APSTypes.Ballistic;
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
                ProjectileCount = bulletInfo.subProjectileCount;
                bulletDragTypeName = bulletInfo.bulletDragTypeName;
                projectileColorC = GUIUtils.ParseColor255(bulletInfo.projectileColor);
                startColorC = GUIUtils.ParseColor255(bulletInfo.startColor);
                fadeColor = bulletInfo.fadeColor;
                ParseBulletDragType();
                ParseBulletFuzeType(bulletInfo.fuzeType);
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
                if (((((bulletMass * 1000) / ((caliber * caliber * Mathf.PI / 400) * 19) + 1) * 10) > caliber * 4))
                {
                    SabotRound = true;
                }
                else
                {
                    SabotRound = false;
                }
                SelectedAmmoType = bulletInfo.name; //store selected ammo name as string for retrieval by web orc filter/later GUI implementation
                if (!useCustomBelt)
                {
                    baseBulletVelocity = bulletVelocity;
                    if (bulletInfo.subProjectileCount > 1)
                    {
                        guiAmmoTypeString = Localizer.Format("#LOC_BDArmory_Ammo_Shot") + " ";
                        //maxDeviation *= Mathf.Clamp(bulletInfo.subProjectileCount/5, 2, 5); //modify deviation if shot vs slug
                        AccAdjust(null, null);
                    }
                    if (bulletInfo.apBulletMod >= 1.1 || SabotRound)
                    {
                        guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_AP") + " ";
                    }
                    else if (bulletInfo.apBulletMod < 1.1 && bulletInfo.apBulletMod > 0.8f)
                    {
                        guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_SAP") + " ";
                    }
                    if (bulletInfo.nuclear)
                    {
                        guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Nuclear") + " ";
                    }
                    if (bulletInfo.explosive && !bulletInfo.nuclear)
                    {
                        if (eFuzeType == FuzeTypes.Timed || eFuzeType == FuzeTypes.Proximity || eFuzeType == FuzeTypes.Flak)
                        {
                            guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Flak") + " ";
                        }
                        guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Explosive") + " ";
                    }
                    if (bulletInfo.incendiary)
                    {
                        guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Incendiary") + " ";
                    }
                    if (bulletInfo.EMP && !bulletInfo.nuclear)
                    {
                        guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_EMP") + " ";
                    }
                    if (bulletInfo.beehive)
                    {
                        guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Beehive") + " ";
                    }
                    if (!bulletInfo.explosive && bulletInfo.apBulletMod <= 0.8)
                    {
                        guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Slug");
                    }
                }
                else
                {
                    guiAmmoTypeString = Localizer.Format("#LOC_BDArmory_Ammo_Multiple");
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
                ProjectileCount = rocketInfo.subProjectileCount;
                rocketModelPath = rocketInfo.rocketModelPath;
                SelectedAmmoType = rocketInfo.name; //store selected ammo name as string for retrieval by web orc filter/later GUI implementation
                beehive = rocketInfo.beehive;
                tntMass = rocketInfo.tntMass;
                Impulse = rocketInfo.force;
                massAdjustment = rocketInfo.massMod;
                if (rocketInfo.subProjectileCount > 1)
                {
                    guiAmmoTypeString = Localizer.Format("#LOC_BDArmory_Ammo_Shot") + " "; // maybe add an int value to these for future Missilefire SmartPick expansion? For now, choose loadouts carefuly!
                }
                if (rocketInfo.nuclear)
                {
                    guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Nuclear") + " ";
                }
                if (rocketInfo.explosive && !rocketInfo.nuclear)
                {
                    if (rocketInfo.flak)
                    {
                        guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Flak") + " ";
                        eFuzeType = FuzeTypes.Flak; //fix rockets not getting detonation range slider 
                    }
                    else if (rocketInfo.shaped)
                    {
                        guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Shaped") + " ";
                    }
                    if (rocketInfo.EMP || rocketInfo.choker || rocketInfo.impulse)
                    {
                        if (rocketInfo.EMP)
                        {
                            guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_EMP") + " ";
                        }
                        if (rocketInfo.choker)
                        {
                            guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Choker") + " ";
                        }
                        if (rocketInfo.impulse)
                        {
                            guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Impulse") + " ";
                        }
                    }
                    else
                    {
                        guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_HE") + " ";
                    }
                    if (rocketInfo.incendiary)
                    {
                        guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Incendiary") + " ";
                    }
                    if (rocketInfo.gravitic)
                    {
                        guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Gravitic") + " ";
                    }
                }
                else
                {
                    guiAmmoTypeString += Localizer.Format("#LOC_BDArmory_Ammo_Kinetic");
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
                SelectedAmmoType = rocketInfo.name; //store selected ammo name as string for retrieval by web orc filter/later GUI implementation                
                SetupRocketPool(currentType, rocketModelPath);
            }
            PAWRefresh();
            SetInitialDetonationDistance();
        }
        protected void SetInitialDetonationDistance()
        {
            if (this.detonationRange == -1)
            {
                if (eWeaponType == WeaponTypes.Ballistic && (bulletInfo.tntMass != 0 && (eFuzeType == FuzeTypes.Proximity || eFuzeType == FuzeTypes.Flak)))
                {
                    blastRadius = BlastPhysicsUtils.CalculateBlastRange(bulletInfo.tntMass); //reproting as two so blastradius can be handed over to PooledRocket for detonation/safety stuff
                    detonationRange = blastRadius * 0.666f;
                }
                else if (eWeaponType == WeaponTypes.Rocket && rocketInfo.tntMass != 0) //don't fire rockets ar point blank
                {
                    blastRadius = BlastPhysicsUtils.CalculateBlastRange(rocketInfo.tntMass);
                    detonationRange = blastRadius * 0.666f;
                }
                else
                {
                    blastRadius = 0;
                    detonationRange = 0f;
                    proximityDetonation = false;
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
                        output.AppendLine($"Electrolaser EMP damage: {Math.Round((ECPerShot / 20), 2)}/s");
                    }
                    else
                    {
                        output.AppendLine($"Electrolaser EMP damage: {Math.Round((ECPerShot / 1000), 2)}/s");
                    }
                    output.AppendLine($"Power Required: {ECPerShot}/s");
                }
                else
                {
                    output.AppendLine($"Laser damage: {laserDamage}");
                    if (LaserGrowTime > 0)
                    {
                        output.AppendLine($"-Laser takes: {LaserGrowTime} seconds to reach max power");
                        output.AppendLine($"-Maximum output: {laserMaxDamage} damage");
                    }
                    if (ECPerShot > 0)
                    {
                        if (pulseLaser)
                        {
                            output.AppendLine($"Electric Charge required per shot: {ECPerShot}");
                        }
                        else
                        {
                            output.AppendLine($"Electric Charge: {ECPerShot}/s");
                        }
                    }
                    else if (requestResourceAmount > 0)
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
                    output.AppendLine($"Rounds Per Minute: {baseRPM * (fireTransforms?.Length ?? 1)}");
                    if (SpoolUpTime > 0) output.AppendLine($"Weapon requires {SpoolUpTime} seconds to come to max RPM");
                    if (HEpulses)
                    {
                        output.AppendLine($"Blast:");
                        output.AppendLine($"- tnt mass:  {Math.Round((laserDamage / 30000), 2)} kg");
                        output.AppendLine($"- radius:  {Math.Round(BlastPhysicsUtils.CalculateBlastRange(laserDamage / 30000), 2)} m");
                    }
                }

            }
            else
            {
                output.AppendLine($"Rounds Per Minute: {roundsPerMinute * (fireTransforms?.Length ?? 1)}");
                if (SpoolUpTime > 0) output.AppendLine($"Weapon requires {SpoolUpTime} second" + (SpoolUpTime > 1 ? "s" : "") + " to come to max RPM");
                output.AppendLine();
                output.AppendLine($"Ammunition: {ammoName}");
                if (ECPerShot > 0)
                {
                    output.AppendLine($"Electric Charge required per shot: {ammoName}");
                }
                output.AppendLine($"Max Range: {maxEffectiveDistance} m");
                if (weaponType == "ballistic")
                {
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
                        ParseBulletFuzeType(binfo.fuzeType);
                        output.AppendLine($"Bullet type: {(string.IsNullOrEmpty(binfo.DisplayName) ? binfo.name : binfo.DisplayName)}");
                        output.AppendLine($"Bullet mass: {Math.Round(binfo.bulletMass, 2)} kg");
                        output.AppendLine($"Muzzle velocity: {Math.Round(binfo.bulletVelocity, 2)} m/s");
                        //output.AppendLine($"Explosive: {binfo.explosive}");
                        if (binfo.subProjectileCount > 1)
                        {
                            output.AppendLine($"Cannister Round");
                            output.AppendLine($" - Submunition count: {binfo.subProjectileCount}");
                        }
                        if (binfo.explosive && !binfo.nuclear)
                        {
                            output.AppendLine($"Blast:");
                            output.AppendLine($"- tnt mass:  {Math.Round(binfo.tntMass, 3)} kg");
                            output.AppendLine($"- radius:  {Math.Round(BlastPhysicsUtils.CalculateBlastRange(binfo.tntMass), 2)} m");
                            if (binfo.fuzeType.ToLower() == "timed" || binfo.fuzeType.ToLower() == "proximity" || binfo.fuzeType.ToLower() == "flak")
                            {
                                output.AppendLine($"Air detonation: True");
                                output.AppendLine($"- auto timing: {(binfo.fuzeType.ToLower() != "proximity")}");
                                output.AppendLine($"- max range: {maxAirDetonationRange} m");
                            }
                            else
                            {
                                output.AppendLine($"Air detonation: False");
                            }
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
                            BulletInfo sinfo = BulletInfo.bullets[binfo.subMunitionType.ToString()];
                            output.AppendLine($"- deploys {sinfo.subProjectileCount}x {(string.IsNullOrEmpty(sinfo.DisplayName) ? sinfo.name : sinfo.DisplayName)}");
                        }
                        output.AppendLine("");
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
                        output.AppendLine($"Bullet type: {(string.IsNullOrEmpty(rinfo.DisplayName) ? rinfo.name : rinfo.DisplayName)}");
                        output.AppendLine($"Rocket mass: {Math.Round(rinfo.rocketMass * 1000, 2)} kg");
                        //output.AppendLine($"Thrust: {thrust}kn"); mass and thrust don't really tell us the important bit, so lets replace that with accel
                        output.AppendLine($"Acceleration: {rinfo.thrust / rinfo.rocketMass}m/s2");
                        if (rinfo.explosive && !rinfo.nuclear)
                        {
                            output.AppendLine($"Blast:");
                            output.AppendLine($"- tnt mass:  {Math.Round((rinfo.tntMass), 3)} kg");
                            output.AppendLine($"- radius:  {Math.Round(BlastPhysicsUtils.CalculateBlastRange(rinfo.tntMass), 2)} m");
                            output.AppendLine($"Proximity Fuzed: {rinfo.flak}");
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
                        if (rinfo.subProjectileCount > 1)
                        {
                            output.AppendLine($"Cluster Rocket");
                            output.AppendLine($" - Submunition count: {rinfo.subProjectileCount}");
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
                                if (BulletInfo.bulletNames.Contains(rinfo.subMunitionType))
                                {
                                    BulletInfo sinfo = BulletInfo.bullets[rinfo.subMunitionType.ToString()];
                                    output.AppendLine($"- deploys {sinfo.subProjectileCount}x {(string.IsNullOrEmpty(sinfo.DisplayName) ? sinfo.name : sinfo.DisplayName)}");
                                }
                                else if (RocketInfo.rocketNames.Contains(rinfo.subMunitionType))
                                {
                                    RocketInfo sinfo = RocketInfo.rockets[rinfo.subMunitionType.ToString()];
                                    output.AppendLine($"- deploys {sinfo.subProjectileCount}x {(string.IsNullOrEmpty(sinfo.DisplayName) ? sinfo.name : sinfo.DisplayName)}");
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
        private static Vector3 mousePos = Vector3.zero;

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
            if (instance != null && instance.WPNmodule != null)
            {
                instance.WPNmodule.WeaponName = instance.WPNmodule.shortName;
                instance.WPNmodule = null;
                instance.UpdateGUIState();
            }
            EditorLogic editor = EditorLogic.fetch;
            if (editor != null)
                editor.Unlock("BD_MN_GUILock");
        }

        public static void ShowGUI(ModuleWeapon WPNmodule)
        {
            if (instance != null)
            {
                instance.WPNmodule = WPNmodule;
                instance.UpdateGUIState();
            }
        }

        private void UpdateGUIState()
        {
            enabled = WPNmodule != null;
            EditorLogic editor = EditorLogic.fetch;
            if (!enabled && editor != null)
                editor.Unlock("BD_MN_GUILock");
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
            bool cursorInGUI = false; // nicked the locking code from Ferram
            mousePos = Input.mousePosition; //Mouse location; based on Kerbal Engineer Redux code
            mousePos.y = Screen.height - mousePos.y;

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
                new Rect(guiWindowRect.xMin + 440, mousePos.y - 5, 300, 20);
            }
            else
            {
                if (guiWindowRect.width == 0)
                {
                    //guiWindowRect = new Rect(Screen.width - 8 - 430 * (posMult + 1), 365, 438, (Screen.height - 365));
                    guiWindowRect = new Rect(Screen.width - 8 - 430 * (posMult + 1), 365, 438, 50);
                }
                new Rect(guiWindowRect.xMin - (230 - 8), mousePos.y - 5, 220, 20);
            }
            cursorInGUI = guiWindowRect.Contains(mousePos);
            if (cursorInGUI)
            {
                editor.Lock(false, false, false, "BD_MN_GUILock");
                //if (EditorTooltip.Instance != null)
                //    EditorTooltip.Instance.HideToolTip();
            }
            else
            {
                editor.Unlock("BD_MN_GUILock");
            }
            guiWindowRect = GUILayout.Window(GetInstanceID(), guiWindowRect, GUIWindow, "Weapon Group GUI", Styles.styleEditorPanel);
        }

        public void GUIWindow(int windowID)
        {
            InitializeStyles();

            GUILayout.BeginVertical();
            GUILayout.Space(20);

            GUILayout.BeginHorizontal();

            GUILayout.Label("Add to Weapon Group: ");

            txtName = GUILayout.TextField(txtName);

            if (GUILayout.Button("Save & Close"))
            {
                string newName = string.IsNullOrEmpty(txtName.Trim()) ? WPNmodule.OriginalShortName : txtName.Trim();

                WPNmodule.WeaponName = newName;
                WPNmodule.shortName = newName;
                instance.WPNmodule.HideUI();
            }

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
