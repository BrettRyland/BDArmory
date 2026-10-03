using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

using BDArmory.Extensions;
using BDArmory.Targeting;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Weapons;
using BDArmory.Guidances;
using BDArmory.Weapons.Missiles;

namespace BDArmory.Control
{
    public class BDModuleOrbitalAI : BDGenericAIBase, IBDAIControl
    {
        // Code contained within this file is adapted from Hatbat, Spartwo and MiffedStarfish's Kerbal Combat Systems Mod https://github.com/Halbann/StockCombatAI/tree/dev/Source/KerbalCombatSystems.
        // Code is distributed under CC-BY-SA 4.0: https://creativecommons.org/licenses/by-sa/4.0/

        public override AIType aiType => AIType.OrbitalAI;
        #region Declarations

        // Orbiter AI variables.
        public float updateInterval;
        public float emergencyUpdateInterval = 0.5f;
        public float combatUpdateInterval = 2.5f;

        private BDOrbitalControl fc;
        private bool PIDActive;
        private int ECID;

        public IBDWeapon currentWeapon;

        private float trackedDeltaV;
        private Vector3 attitudeCommand;
        private PilotCommands lastUpdateCommand = PilotCommands.Free;
        private float maneuverTime;
        private float minManeuverTime;
        private bool maneuverStateChanged = false;
        enum OrbitCorrectionReason { None, FallingInsideAtmosphere, ApoapsisLow, PeriapsisLow, Escaping };
        private OrbitCorrectionReason ongoingOrbitCorrectionDueTo = OrbitCorrectionReason.None;
        private float missileTryLaunchTime = 0f;
        private bool wasDescendingUnsafe = false;
        private bool hasPropulsion;
        private bool hasRCS;
        private bool hasWeapons;
        private bool hasEC;
        private float maxAcceleration;
        private float maxThrust;

        private float reverseForwardThrustRatio = 0f;
        private List<ModuleEngines> forwardEngines = new List<ModuleEngines>();
        private List<ModuleEngines> reverseEngines = new List<ModuleEngines>();
        private List<ModuleEngines> rcsEngines = new List<ModuleEngines>();
        private bool currentForwardThrust;
        private bool engineListsRequireUpdating = true;
        private Dictionary<uint, Tuple<bool, float, float>> engineIndependentThrottleState = new Dictionary<uint, Tuple<bool, float, float>>();

        private Vector3 maxAngularAcceleration;
        private float maxAngularAccelerationMag;
        private Vector3 availableTorque;
        private double minSafeAltitude;
        private CelestialBody safeAltBody = null;
        public Vector3 interceptRanges = Vector3.one;
        private Vector3 lastFiringSolution;
        const float interceptMargin = 0.25f;

        // Evading
        bool evadingGunfire = false;
        float evasiveTimer;
        Vector3 threatRelativePosition;
        Vector3 evasionNonLinearityDirection;
        string evasionString = " & Evading Gunfire";

        //collision detection (for other vessels).
        const int vesselCollisionAvoidanceTickerFreq = 10; // Number of fixedDeltaTime steps between vessel-vessel collision checks.
        int collisionDetectionTicker = 0;
        Vector3 collisionAvoidDirection;
        public Vessel currentlyAvoidedVessel;

        public enum PIDModeTypes
        {
            Inactive, // Stock autopilot always used
            Firing, // PID used for firing weapons
            Everything // PID used for firing weapons and maneuvers
        }

        public enum RollModeTypes
        {
            Port_Starboard, // Roll to port or starboard, whichever is closer
            Dorsal_Ventral, // Roll to dorsal or ventral, whichever is closer
            Port, // Always roll port to target
            Starboard, // Always roll starboard to target
            Dorsal, // Always roll dorsal to target
            Ventral, // Always roll ventral to target
        }

        // User parameters changed via UI.
        #region PID
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_OrbitalPIDActive"),//PID active mode
            UI_ChooseOption(options = new string[3] { "Inactive", "Firing", "Everything" })]
        public string pidMode = "Firing";
        public readonly string[] pidModes = new string[3] { "Inactive", "Firing", "Everything" };

        public PIDModeTypes PIDMode
            => (PIDModeTypes)Enum.Parse(typeof(PIDModeTypes), pidMode);

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerPower"),//Steer Factor
            UI_FloatRange(minValue = 0.2f, maxValue = 20f, stepIncrement = .1f, scene = UI_Scene.All)]
        public float steerMult = 14;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerKi"), //Steer Ki
            UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float steerKiAdjust = 0.1f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerDamping"),//Steer Damping
            UI_FloatRange(minValue = 0.1f, maxValue = 10f, stepIncrement = .1f, scene = UI_Scene.All)]
        public float steerDamping = 5;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerMaxError"),//Steer Max Error
            UI_FloatRange(minValue = 25f, maxValue = 180f, stepIncrement = 5f, scene = UI_Scene.All)]
        public float steerMaxError = 45f;
        #endregion

        #region Combat
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_BroadsideAttack"),//Attack vector
            UI_Toggle(enabledText = "#LOC_BDArmory_AI_BroadsideAttack_enabledText", disabledText = "#LOC_BDArmory_AI_BroadsideAttack_disabledText")]//Broadside--Bow
        public bool BroadsideAttack = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_RollMode"),// Preferred roll direction of ship towards target
            UI_ChooseOption(options = new string[6] { "Port_Starboard", "Dorsal_Ventral", "Port", "Starboard", "Dorsal", "Ventral" })]
        public string rollTowards = "Port_Starboard";
        public readonly string[] rollTowardsModes = new string[6] { "Port_Starboard", "Dorsal_Ventral", "Port", "Starboard", "Dorsal", "Ventral" };

        public RollModeTypes rollMode
            => (RollModeTypes)Enum.Parse(typeof(RollModeTypes), rollTowards);

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MinEngagementRange"),//Min engagement range
            UI_FloatSemiLogRange(minValue = 10f, maxValue = 10000f, sigFig = 1, withZero = true)]
        public float MinEngagementRange = 100;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ForceFiringRange"),//Force firing range
            UI_FloatSemiLogRange(minValue = 10f, maxValue = 10000f, sigFig = 1, withZero = true)]
        public float ForceFiringRange = 100f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_AllowRamming", advancedTweakable = true), //Toggle Allow Ramming
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All),]
        public bool allowRamming = true; // Allow switching to ramming mode.
        #endregion

        #region Control
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_FiringRCS"),//Use RCS to kill relative velocity when firing
            UI_Toggle(enabledText = "#LOC_BDArmory_AI_FiringRCS_enabledText", disabledText = "#LOC_BDArmory_AI_FiringRCS_disabledText", scene = UI_Scene.All),]//Manage Velocity--Maneuvers Only
        public bool FiringRCS = true;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ManeuverRCS"),//Use RCS for all maneuvering
            UI_Toggle(enabledText = "#LOC_BDArmory_AI_ManeuverRCS_enabledText", disabledText = "#LOC_BDArmory_AI_ManeuverRCS_disabledText", scene = UI_Scene.All),]//Always--Combat Only
        public bool ManeuverRCS = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ReverseEngines"),//Use reverse engines
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All),]
        public bool ReverseThrust = true;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_EngineRCSRotation"),//Use engines as RCS for rotation
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All),]
        public bool EngineRCSRotation = true;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_EngineRCSTranslation"),//Use engines as RCS for translation
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All),]
        public bool EngineRCSTranslation = true;
        #endregion

        #region Speeds
        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "#LOC_BDArmory_AI_ManeuverSpeed",
            guiUnits = " m/s"),
            UI_FloatSemiLogRange(
                minValue = 10f,
                maxValue = 10000f,
                stepIncrement = 10f,
                scene = UI_Scene.All
            )]
        public float ManeuverSpeed = 100f;

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "#LOC_BDArmory_AI_FiringSpeedMin",
            guiUnits = " m/s"),
            UI_FloatSemiLogRange(
                minValue = 2f,
                maxValue = 1000f,
                scene = UI_Scene.All,
                withZero = true
            )]
        public float minFiringSpeed = 0f;

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "#LOC_BDArmory_AI_FiringSpeedLimit",
            guiUnits = " m/s"),
            UI_FloatSemiLogRange(
                minValue = 1f,
                maxValue = 10000f,
                reducedPrecisionAtMin = true,
                scene = UI_Scene.All
            )]
        public float firingSpeed = 50f;

        [KSPField(isPersistant = true,
            guiActive = true,
            guiActiveEditor = true,
            guiName = "#LOC_BDArmory_AI_AngularSpeedLimit",
            guiUnits = " m/s"),
            UI_FloatSemiLogRange(
                minValue = 1f,
                maxValue = 1000f,
                scene = UI_Scene.All
            )]
        public float firingAngularVelocityLimit = 10f;
        #endregion

        #region Evade
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MinEvasionTime", advancedTweakable = true, // Min Evasion Time
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = .05f, scene = UI_Scene.All)]
        public float minEvasionTime = 0.2f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_EvasionThreshold", advancedTweakable = true, //Evasion Distance Threshold
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float evasionThreshold = 25f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_EvasionTimeThreshold", advancedTweakable = true, // Evasion Time Threshold
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 5f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float evasionTimeThreshold = 0.1f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_EvasionErraticness", advancedTweakable = true, // Evasion Erraticness
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float evasionErraticness = 0.1f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_EvasionMinRangeThreshold", advancedTweakable = true, // Evasion Min Range Threshold
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatSemiLogRange(minValue = 10f, maxValue = 10000f, sigFig = 1, withZero = true)]
        public float evasionMinRangeThreshold = 10f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_EvasionRCS", advancedTweakable = true,//Use RCS for gun evasion
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All),]//Enabled--Disabled
        public bool evasionRCS = true;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_EvasionEngines", advancedTweakable = true,//Use engines for gun evasion
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All),]//Enabled--Disabled
        public bool evasionEngines = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_EvasionIgnoreMyTargetTargetingMe", advancedTweakable = true,//Ignore my target targeting me
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All),]
        public bool evasionIgnoreMyTargetTargetingMe = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_CollisionAvoidanceThreshold", advancedTweakable = true, //Vessel collision avoidance threshold
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float collisionAvoidanceThreshold = 50f; // 20m + target's average radius.

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_CollisionAvoidanceLookAheadPeriod", advancedTweakable = true, //Vessel collision avoidance look ahead period
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 30f, stepIncrement = 0.5f, scene = UI_Scene.All)]
        public float vesselCollisionAvoidanceLookAheadPeriod = 10f; // Look 10s ahead for potential collisions.
        #endregion


        // Debugging
        internal float distToCPA;
        internal float timeToCPA;
        internal string timeToCPAString;
        internal float stoppingDist;
        internal Vector3 debugTargetPosition;
        internal Vector3 debugTargetDirection;
        internal Vector3 debugRollTarget;

        // Dynamic measurements
        float dynAngAccel = 1f; // Start at reasonable value.
        float lastAngVel = 1f; // Start at reasonable value.
        float dynDecayRate = 1f; // Decay rate for dynamic measurements. Set to a half-life of 60s in ActivatePilot.

        /// <summary>
        /// //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        /// </summary>

        Vector3 upDir;

        #endregion

        #region Status Mode
        public enum StatusMode { Idle, AvoidingCollision, Evading, CorrectingOrbit, Withdrawing, Ramming, Firing, Maneuvering, Stranded, Commanded, Custom }
        public StatusMode currentStatusMode = StatusMode.Idle;
        StatusMode lastStatusMode = StatusMode.Idle;
        protected override void SetStatus(string status)
        {
            if (evadingGunfire && (evasionRCS || evasionEngines))
                status += evasionString;

            base.SetStatus(status);
            if (status.StartsWith("Idle")) currentStatusMode = StatusMode.Idle;
            else if (status.StartsWith("Avoiding Collision")) currentStatusMode = StatusMode.AvoidingCollision;
            else if (status.StartsWith("Correcting Orbit")) currentStatusMode = StatusMode.CorrectingOrbit;
            else if (status.StartsWith("Evading")) currentStatusMode = StatusMode.Evading;
            else if (status.StartsWith("Withdrawing")) currentStatusMode = StatusMode.Withdrawing;
            else if (status.StartsWith("Ramming")) currentStatusMode = StatusMode.Ramming;
            else if (status.StartsWith("Firing")) currentStatusMode = StatusMode.Firing;
            else if (status.StartsWith("Maneuvering")) currentStatusMode = StatusMode.Maneuvering;
            else if (status.StartsWith("Stranded")) currentStatusMode = StatusMode.Stranded;
            else if (status.StartsWith("Commanded")) currentStatusMode = StatusMode.Commanded;
            else currentStatusMode = StatusMode.Custom;
        }
        #endregion

        #region RMB info in editor

        // <color={XKCDColors.HexFormat.Lime}>Yes</color>
        public override string GetInfo()
        {
            // known bug - the game caches the RMB info, changing the variable after checking the info
            // does not update the info. :( No idea how to force an update.
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<b>Available settings</b>:");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Min Engagement Range</color> - AI will try to move away from oponents if closer than this range");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- RCS Active</color> - Use RCS during any maneuvers, or only in combat");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Maneuver Speed</color> - Max speed relative to target during intercept maneuvers");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Strafing Speed</color> - Max speed relative to target during gun firing");
            if (GameSettings.ADVANCED_TWEAKABLES)
            {
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Min Evasion Time</color> - Minimum seconds AI will evade for");
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Evasion Distance Threshold</color> - How close incoming gunfire needs to come to trigger evasion");
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Evasion Time Threshold</color> - How many seconds the AI needs to be under fire to begin evading");
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Evasion Min Range Threshold</color> - Attacker needs to be beyond this range to trigger evasion");
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Don't Evade My Target</color> - Whether gunfire from the current target is ignored for evasion");
            }
            return sb.ToString();
        }

        #endregion RMB info in editor

        #region UI Initialisers and Callbacks
        protected void SetSliderPairClamps(string fieldNameMin, string fieldNameMax)
        {
            // Enforce min <= max for pairs of sliders
            UI_FloatRange field = (UI_FloatRange)(HighLogic.LoadedSceneIsFlight ? Fields[fieldNameMin].uiControlFlight : Fields[fieldNameMin].uiControlEditor);
            field.onFieldChanged = OnMinUpdated;
            field = (UI_FloatRange)(HighLogic.LoadedSceneIsFlight ? Fields[fieldNameMax].uiControlFlight : Fields[fieldNameMax].uiControlEditor);
            field.onFieldChanged = OnMaxUpdated;
        }

        public void OnMinUpdated(BaseField field = null, object obj = null)
        {
            if (firingSpeed < minFiringSpeed) { firingSpeed = minFiringSpeed; } // Enforce min < max for firing speeds.
            if (ManeuverSpeed < firingSpeed) { ManeuverSpeed = firingSpeed; } // Enforce firing < maneuver for firing/maneuver speeds.
            if (ForceFiringRange < MinEngagementRange) { ForceFiringRange = MinEngagementRange; } // Enforce MinEngagementRange < ForceFiringRange
        }

        public void OnMaxUpdated(BaseField field = null, object obj = null)
        {
            if (minFiringSpeed > firingSpeed) { minFiringSpeed = firingSpeed; }  // Enforce min < max for firing speeds.
            if (firingSpeed > ManeuverSpeed) { firingSpeed = ManeuverSpeed; }  // Enforce firing < maneuver for firing/maneuver speeds.
            if (MinEngagementRange > ForceFiringRange) { MinEngagementRange = ForceFiringRange; } // Enforce MinEngagementRange < ForceFiringRange
        }
        #endregion

        #region events

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (!(HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor)) return;
            SetChooseOptions();
            SetSliderPairClamps("minFiringSpeed", "firingSpeed");
            SetSliderPairClamps("firingSpeed", "ManeuverSpeed");
            SetSliderPairClamps("MinEngagementRange", "ForceFiringRange");
            if (HighLogic.LoadedSceneIsFlight)
            {
                GameEvents.onVesselPartCountChanged.Add(CalculateAvailableTorque);
                GameEvents.onVesselPartCountChanged.Add(CheckEngineLists);
            }
            CalculateAvailableTorque(vessel);
            ECID = PartResourceLibrary.Instance.GetDefinition("ElectricCharge").id; // This should always be found.
        }

        protected override void OnDestroy()
        {
            GameEvents.onVesselPartCountChanged.Remove(CalculateAvailableTorque);
            GameEvents.onVesselPartCountChanged.Remove(CheckEngineLists);
            base.OnDestroy();
        }

        public override bool ActivatePilot()
        {
            if (!base.ActivatePilot()) return false;
            TakingOff = false;
            dynDecayRate = Mathf.Exp(Mathf.Log(0.5f) * Time.fixedDeltaTime / 60f); // Decay rate for a half-life of 60s.
            //originalMaxSpeed = ManeuverSpeed;
            if (!fc)
            {
                fc = gameObject.AddComponent<BDOrbitalControl>();
                fc.vessel = vessel;

                fc.alignmentToleranceforBurn = 7.5f;
                fc.throttleLerpRate = 3;
            }
            fc.Activate();
            foreach (var engine in VesselModuleRegistry.GetModuleEngines(vessel)) // Save indepedent throttle settings
            {
                if (engine.throttleLocked || !engine.allowShutdown || !engine.allowRestart) continue; // Ignore engines that can't be throttled, shutdown, or restart
                if (VesselSpawning.SpawnUtils.IsModularMissilePart(engine.part)) continue; // Ignore modular missile engines.
                if (engineIndependentThrottleState.ContainsKey(engine.part.persistentId)) continue; // don't re-add engines
                engineIndependentThrottleState.Add(engine.part.persistentId, new(engine.independentThrottle, engine.independentThrottlePercentage, engine.thrustPercentage));
            }
            UpdateEngineLists(true); // Update engine list, turn off reverse engines if active
            return true;
        }

        public override void DeactivatePilot()
        {
            base.DeactivatePilot();

            if (fc)
            {
                fc.Deactivate();
                fc = null;
            }
            foreach (var engine in VesselModuleRegistry.GetModuleEngines(vessel)) // Restore indepedent throttle settings
            {
                if (engine.throttleLocked || !engine.allowShutdown || !engine.allowRestart) continue; // Ignore engines that can't be throttled, shutdown, or restart
                if (VesselSpawning.SpawnUtils.IsModularMissilePart(engine.part)) continue; // Ignore modular missile engines.
                if (engineIndependentThrottleState.ContainsKey(engine.part.persistentId))
                {
                    engine.independentThrottle = engineIndependentThrottleState[engine.part.persistentId].Item1;
                    engine.independentThrottlePercentage = engineIndependentThrottleState[engine.part.persistentId].Item2;
                    engine.thrustPercentage = engineIndependentThrottleState[engine.part.persistentId].Item3;
                }
            }
            engineIndependentThrottleState.Clear();
            evadingGunfire = false;
            SetStatus("");
        }

        public void SetChooseOptions()
        {
            UI_ChooseOption pidmode = (UI_ChooseOption)(HighLogic.LoadedSceneIsFlight ? Fields[nameof(pidMode)].uiControlFlight : Fields[nameof(pidMode)].uiControlEditor);
            pidmode.onFieldChanged = ChooseOptionsUpdated;
            UI_ChooseOption rolltowards = (UI_ChooseOption)(HighLogic.LoadedSceneIsFlight ? Fields[nameof(rollTowards)].uiControlFlight : Fields[nameof(rollTowards)].uiControlEditor);
            rolltowards.onFieldChanged = ChooseOptionsUpdated;
        }

        public void ChooseOptionsUpdated(BaseField field, object obj)
        {
            this.UpdateChooseOptionPAW(field, obj);
            if (BDArmoryAIGUI.Instance != null && BDArmoryAIGUI.Instance.ActiveAI == this)
            {
                BDArmoryAIGUI.Instance.SetChooseOptionSliders();
            }
        }

        protected override void OnGUI()
        {
            base.OnGUI();

            if (!HighLogic.LoadedSceneIsFlight) return;
            if (!pilotEnabled || !vessel || !vessel.isActiveVessel) return;

            if (!BDArmorySettings.DEBUG_LINES) return;
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, PIDActive ? debugTargetPosition : fc.attitude * 1000, 5, Color.red); // The point we're asked to turn to
            if (fc.thrustDirection != fc.attitude) GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, fc.thrustDirection * 100, 5, Color.yellow); // Thrust direction
            if (PIDActive) GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, debugTargetDirection, 5, Color.green); // The direction PID control will actually turn to
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, fc.RCSVector * 100, 5, Color.cyan); // RCS command
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, fc.RCSVectorLerped * 100, 5, Color.magenta); // RCS lerped command
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + debugRollTarget, 2, Color.blue); // Roll target
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + vesselTransform.up * 1000, 3, Color.white);
            if (currentStatusMode == StatusMode.AvoidingCollision) GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, currentlyAvoidedVessel.transform.position, 8, Color.grey); // Collision avoidance
        }

        #endregion events

        #region Actual AI Pilot
        protected override void AutoPilot(FlightCtrlState s)
        {
            // Update vars
            InitialFrameUpdates();
            UpdateStatus(); // Combat decisions, evasion, maneuverStateChanged = true and set new statusMode, etc.

            maneuverTime += Time.fixedDeltaTime;
            if (maneuverStateChanged || maneuverTime > minManeuverTime)
            {
                maneuverTime = 0;
                evasionNonLinearityDirection = UnityEngine.Random.onUnitSphere;
                fc.lerpAttitude = true;
                minManeuverTime = combatUpdateInterval;
                switch (currentStatusMode)
                {
                    case StatusMode.AvoidingCollision:
                        minManeuverTime = emergencyUpdateInterval;
                        break;
                    case StatusMode.Evading:
                        minManeuverTime = emergencyUpdateInterval;
                        break;
                    case StatusMode.CorrectingOrbit:
                        break;
                    case StatusMode.Withdrawing:
                        {
                            var weaponManager = WeaponManager;
                            // Determine the direction.
                            Vector3 averagePos = Vector3.zero;
                            using (List<TargetInfo>.Enumerator target = BDATargetManager.TargetList(weaponManager.Team).GetEnumerator())
                                while (target.MoveNext())
                                {
                                    if (target.Current == null) continue;
                                    if (target.Current && target.Current.Vessel && weaponManager.CanSeeTarget(target.Current, true, true) >= MissileFire.TargetVisibility.RecentlyVisible)
                                    {
                                        averagePos += FromTo(vessel, target.Current.Vessel).normalized;
                                    }
                                }

                            Vector3 direction = -averagePos.normalized;
                            Vector3 orbitNormal = vessel.orbit.Normal(Planetarium.GetUniversalTime());
                            bool facingNorth = Vector3.Dot(direction, orbitNormal) > 0;
                            trackedDeltaV = 200;
                            attitudeCommand = (orbitNormal * (facingNorth ? 1 : -1)).normalized;
                        }
                        break;
                    case StatusMode.Ramming:
                        break;
                    case StatusMode.Commanded:
                        {
                            lastUpdateCommand = currentCommand;
                            if (maneuverStateChanged)
                            {
                                if (currentCommand == PilotCommands.Follow)
                                    attitudeCommand = commandLeader.transform.up;
                                else
                                    attitudeCommand = (assignedPositionWorld - vessel.transform.position).normalized;
                            }
                            minManeuverTime = 30f;
                            trackedDeltaV = 200;
                        }
                        break;
                    case StatusMode.Firing:
                        fc.lerpAttitude = false;
                        break;
                    case StatusMode.Maneuvering:
                        break;
                    case StatusMode.Stranded:
                        break;
                    default: // Idle
                        break;
                }
            }
            Maneuver(); // Set attitude, alignment tolerance, throttle, update RCS if needed
            if (PIDActive)
                AttitudeControl(s);
            AddDebugMessages();
        }

        void InitialFrameUpdates()
        {
            upDir = vessel.up;
            UpdateBody();
            UpdateEngineLists();
            CalculateAngularAcceleration();
            maxAcceleration = GetMaxAcceleration();
            fc.alignmentToleranceforBurn = 7.5f;
            if (fc.throttle > 0)
                lastFiringSolution = Vector3.zero; // Forget prior firing solution if we recently used engines
            fc.throttle = 0;
            fc.lerpThrottle = true;
            fc.useReverseThrust = false;
            fc.thrustDirection = Vector3.zero;
            vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, ManeuverRCS);
            maneuverStateChanged = false;
        }

        void Maneuver()
        {
            Vector3 rcsVector = Vector3.zero;
            var weaponManager = WeaponManager;

            switch (currentStatusMode)
            {
                case StatusMode.AvoidingCollision:
                    {
                        SetStatus("Avoiding Collision");
                        vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);

                        Vector3 safeDirection = -collisionAvoidDirection;

                        fc.attitude = safeDirection;
                        fc.alignmentToleranceforBurn = 70;
                        fc.throttle = 1;
                        fc.lerpThrottle = false;
                        rcsVector = safeDirection;
                    }
                    break;
                case StatusMode.Evading:
                    {
                        SetStatus("Evading Missile");
                        vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);

                        Vector3 incomingVector = FromTo(vessel, weaponManager.incomingMissileVessel);
                        Vector3 dodgeVector = vessel.ReferenceTransform.up.ProjectOnPlanePreNormalized(incomingVector.normalized);

                        fc.attitude = dodgeVector;
                        fc.alignmentToleranceforBurn = 70;
                        fc.throttle = 1;
                        fc.lerpThrottle = false;
                        rcsVector = dodgeVector;
                    }
                    break;
                case StatusMode.CorrectingOrbit:
                    {
                        Orbit o = vessel.orbit;
                        double UT = Planetarium.GetUniversalTime();
                        fc.alignmentToleranceforBurn = 15f;
                        var descending = o.timeToPe > 0 && o.timeToPe < o.timeToAp;
                        if (o.altitude > minSafeAltitude && (
                            (ongoingOrbitCorrectionDueTo == OrbitCorrectionReason.None && EscapingOrbit()) ||
                            (ongoingOrbitCorrectionDueTo == OrbitCorrectionReason.Escaping && (EscapingOrbit() || (o.ApA > 0.1f * safeAltBody.sphereOfInfluence)))))
                        {
                            // Vessel is on an escape orbit and has passed the periapsis by over 60s, burn retrograde
                            SetStatus("Correcting Orbit (On escape trajectory)");
                            ongoingOrbitCorrectionDueTo = OrbitCorrectionReason.Escaping;

                            fc.attitude = -o.GetPrograde(UT);
                            fc.throttle = 1;
                        }
                        else if (descending && o.PeA < minSafeAltitude && (
                            (ongoingOrbitCorrectionDueTo == OrbitCorrectionReason.None && o.ApA >= minSafeAltitude && o.altitude >= minSafeAltitude) ||
                            (ongoingOrbitCorrectionDueTo == OrbitCorrectionReason.PeriapsisLow && o.altitude > minSafeAltitude * 1.1f)))
                        {
                            // We are outside the atmosphere but our periapsis is inside the atmosphere.
                            // Execute a burn to circularize our orbit at the current altitude.
                            SetStatus("Correcting Orbit (Circularizing)");
                            ongoingOrbitCorrectionDueTo = OrbitCorrectionReason.PeriapsisLow;

                            Vector3d fvel = Math.Sqrt(o.referenceBody.gravParameter / o.GetRadiusAtUT(UT)) * o.Horizontal(UT);
                            Vector3d deltaV = fvel - vessel.GetObtVelocity();
                            fc.attitude = deltaV.normalized;
                            fc.throttle = Mathf.Lerp(0, 1, (float)(deltaV.sqrMagnitude / 100));
                        }
                        else
                        {
                            if (o.ApA < minSafeAltitude * 1.1f)
                            {
                                // Entirety of orbit is inside atmosphere, perform gravity turn burn until apoapsis is outside atmosphere by a 10% margin.
                                SetStatus("Correcting Orbit (Apoapsis too low)");
                                ongoingOrbitCorrectionDueTo = OrbitCorrectionReason.ApoapsisLow;

                                double gravTurnAlt = 0.1;
                                float turn;

                                if (o.altitude < gravTurnAlt * minSafeAltitude || descending || wasDescendingUnsafe) // At low alts or when descending, burn straight up
                                {
                                    turn = 1f;
                                    fc.alignmentToleranceforBurn = 45f; // Use a wide tolerance as aero forces could make it difficult to align otherwise.
                                    wasDescendingUnsafe = descending || o.timeToAp < 10; // Hysteresis for upwards vs gravity turn burns.
                                }
                                else // At higher alts, gravity turn towards horizontal orbit vector
                                {
                                    turn = Mathf.Clamp((float)((1.1 * minSafeAltitude - o.ApA) / (minSafeAltitude * (1.1 - gravTurnAlt))), 0.1f, 1f);
                                    turn = Mathf.Clamp(Mathf.Log10(turn) + 1f, 0.33f, 1f);
                                    fc.alignmentToleranceforBurn = Mathf.Clamp(15f * turn, 5f, 15f);
                                    wasDescendingUnsafe = false;
                                }

                                fc.attitude = Vector3.Lerp(o.Horizontal(UT), upDir, turn);
                                fc.throttle = 1;
                            }
                            else if (descending && o.altitude < minSafeAltitude * 1.1f)
                            {
                                // Our apoapsis is outside the atmosphere but we are inside the atmosphere and descending.
                                // Burn up until we are ascending and our apoapsis is outside the atmosphere by a 10% margin.
                                SetStatus("Correcting Orbit (Falling inside atmo)");
                                ongoingOrbitCorrectionDueTo = OrbitCorrectionReason.FallingInsideAtmosphere;

                                fc.attitude = o.Radial(UT);
                                fc.alignmentToleranceforBurn = 45f; // Use a wide tolerance as aero forces could make it difficult to align otherwise.
                                fc.throttle = 1;
                            }
                            else
                            {
                                SetStatus("Correcting Orbit (Drifting)");
                                ongoingOrbitCorrectionDueTo = OrbitCorrectionReason.None;
                            }
                        }
                    }
                    break;
                case StatusMode.Commanded:
                    {
                        // We have been given a command from the WingCommander to fly/follow/attack in a general direction
                        // Burn for 200 m/s then coast remainder of 30s period
                        switch (currentCommand)
                        {
                            case PilotCommands.Follow:
                                SetStatus("Commanded to Follow Leader");
                                break;
                            case PilotCommands.Attack:
                            case PilotCommands.WingAttack:
                                SetStatus("Commanded to Attack");
                                break;
                            default: // Fly To
                                SetStatus("Commanded to Position");
                                break;
                        }
                        trackedDeltaV -= Vector3.Project(vessel.acceleration, attitudeCommand).magnitude * TimeWarp.fixedDeltaTime;
                        fc.attitude = attitudeCommand;
                        fc.throttle = (trackedDeltaV > 10) ? 1 : 0;
                    }
                    break;
                case StatusMode.Withdrawing:
                    {
                        SetStatus("Withdrawing");

                        // Withdraw sequence. Locks behaviour while burning 200 m/s of delta-v either north or south.
                        trackedDeltaV -= Vector3.Project(vessel.acceleration, attitudeCommand).magnitude * TimeWarp.fixedDeltaTime;
                        fc.attitude = attitudeCommand;
                        fc.throttle = (trackedDeltaV > 10) ? 1 : 0;
                        fc.alignmentToleranceforBurn = 70;
                    }
                    break;
                case StatusMode.Ramming:
                    {
                        SetStatus("Ramming Speed!");

                        vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);

                        // Target information
                        Vector3 targetPosition = targetVessel.CoM;
                        Vector3 targetVector = targetPosition - vessel.CoM;
                        Vector3 relVel = vessel.GetObtVelocity() - targetVessel.GetObtVelocity();
                        Vector3 relVelNrm = relVel.normalized;
                        Vector3 interceptVector;
                        float relVelmag = relVel.magnitude;

                        float timeToImpact = BDAMath.SolveTime(targetVector.magnitude, maxAcceleration, Vector3.Dot(relVel, targetVector.normalized));
                        Vector3 lead = -timeToImpact * relVelmag * relVelNrm;
                        interceptVector = (targetPosition + lead) - vessel.CoM;

                        fc.attitude = interceptVector;
                        fc.thrustDirection = interceptVector;
                        fc.throttle = 1f;
                        fc.alignmentToleranceforBurn = 25f;

                        rcsVector = -relVel.ProjectOnPlanePreNormalized(vesselTransform.up);
                    }
                    break;
                case StatusMode.Firing:
                    {
                        // Aim at appropriate point to fire guns/launch missiles
                        vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);

                        fc.lerpAttitude = false;
                        Vector3 firingSolution = BroadsideAttack ? BroadsideAttitude(vessel, targetVessel) : FromTo(vessel, targetVessel).normalized;
                        Vector3 relVel = RelVel(vessel, targetVessel);
                        if (FiringRCS)
                        {
                            float targetSpeed = FiringTargetSpeed();
                            float margin = Mathf.Max(Mathf.Abs(firingSpeed - minFiringSpeed) * 0.1f, 2f);
                            float minSpeed = Mathf.Clamp(targetSpeed - margin, minFiringSpeed, firingSpeed);
                            float maxSpeed = Mathf.Clamp(targetSpeed + margin, minFiringSpeed, firingSpeed);
                            if (minFiringSpeed == 0 || relVel.sqrMagnitude > maxSpeed * maxSpeed)
                            {
                                rcsVector = -relVel.ProjectOnPlane(FromTo(vessel, targetVessel));
                            }
                            else if (relVel.sqrMagnitude < minSpeed * minSpeed)
                            {
                                rcsVector = relVel.ProjectOnPlane(FromTo(vessel, targetVessel));
                            }
                        }

                        if (weaponManager.currentGun && GunReady(weaponManager.currentGun))
                        {
                            SetStatus("Firing Guns");
                            firingSolution = GunFiringSolution(weaponManager.currentGun);
                        }
                        else if (weaponManager.CurrentMissile && !weaponManager.GetLaunchAuthorization(targetVessel, weaponManager, weaponManager.CurrentMissile))
                        {
                            SetStatus("Firing Missiles");
                            firingSolution = MissileGuidance.GetAirToAirFireSolution(weaponManager.CurrentMissile, targetVessel);
                            firingSolution = (firingSolution - vessel.transform.position).normalized;
                        }
                        else
                            SetStatus("Firing");

                        lastFiringSolution = firingSolution;
                        fc.attitude = firingSolution;
                        fc.throttle = 0;
                    }
                    break;
                case StatusMode.Maneuvering:
                    {
                        Vector3 toTarget = FromTo(vessel, targetVessel).normalized;

                        TimeSpan t = TimeSpan.FromSeconds(Mathf.Min(timeToCPA, 86400f)); // Clamp at one day 24*60*60s
                        timeToCPAString = string.Format((t.Hours > 0 ? "{0:D2}h:" : "") + (t.Minutes > 0 ? "{1:D2}m:" : "") + "{2:D2}s", t.Hours, t.Minutes, t.Seconds);

                        float minRange = interceptRanges.x;
                        float maxRange = interceptRanges.y;
                        float interceptRange = interceptRanges.z;

                        vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);

                        float currentRange = VesselDistance(vessel, targetVessel);
                        Vector3 relVel = RelVel(vessel, targetVessel);

                        float speedTarget = KillVelocityTargetSpeed();
                        bool killVelOngoing = currentStatus.Contains("Kill Velocity");
                        bool interceptOngoing = currentStatus.Contains("Intercept Target") && !OnIntercept(0.05f) && !ApproachingIntercept();

                        if (currentRange < minRange && AwayCheck(minRange)) // Too close, maneuever away
                        {
                            SetStatus("Maneuvering (Away)");
                            fc.throttle = 1;
                            fc.alignmentToleranceforBurn = 135;
                            fc.thrustDirection = -toTarget;
                            if (UseForwardThrust(fc.thrustDirection))
                                fc.attitude = -toTarget;
                            else
                                fc.attitude = toTarget;
                            fc.throttle = Vector3.Dot(RelVel(vessel, targetVessel), fc.thrustDirection) < ManeuverSpeed ? 1 : 0;
                        }
                        else if (hasPropulsion && (relVel.sqrMagnitude > speedTarget * speedTarget) && (ApproachingIntercept(currentStatus.Contains("Kill Velocity") ? 1.5f : 0f) || killVelOngoing)) // Approaching intercept point, kill velocity
                            KillVelocity(true);
                        else if (hasPropulsion && interceptOngoing || (currentRange > maxRange && CanInterceptShip(targetVessel) && !OnIntercept(currentStatus.Contains("Intercept Target") ? 0.05f : interceptMargin))) // Too far away, intercept target
                            InterceptTarget();
                        else if (currentRange > interceptRange && OnIntercept(interceptMargin))
                        {
                            fc.throttle = 0;
                            fc.thrustDirection = -toTarget;
                            if (ApproachingIntercept(3f))
                            {
                                Vector3 killVel = (relVel + targetVessel.perturbation).normalized;
                                fc.thrustDirection = killVel;
                                if (UseForwardThrust(fc.thrustDirection))
                                    fc.attitude = killVel;
                                else
                                    fc.attitude = -killVel;
                            }
                            else
                                fc.attitude = toTarget;
                            SetStatus($"Maneuvering (On Intercept), {timeToCPAString}");
                        }
                        else // Within weapons range, adjust velocity and attitude for targeting
                        {
                            bool killAngOngoing = currentStatus.Contains("Kill Angular Velocity") && (AngularVelocity(vessel, targetVessel, 5f) < firingAngularVelocityLimit / 2);
                            bool increaseVelOngoing = currentStatus.Contains("Increasing Velocity") && relVel.sqrMagnitude < FiringTargetSpeed() * FiringTargetSpeed();
                            if (hasPropulsion && (relVel.sqrMagnitude > firingSpeed * firingSpeed || killVelOngoing))
                            {
                                KillVelocity();
                            }
                            else if (hasPropulsion && targetVessel != null && (AngularVelocity(vessel, targetVessel, 5f) > firingAngularVelocityLimit || killAngOngoing))
                            {
                                SetStatus("Maneuvering (Kill Angular Velocity)");
                                fc.attitude = -RelVel(vessel, targetVessel).ProjectOnPlane(vessel.PredictPosition(timeToCPA)).normalized;
                                fc.throttle = 1;
                                fc.alignmentToleranceforBurn = 45f;
                            }
                            else if (hasPropulsion && targetVessel != null && (relVel.sqrMagnitude < minFiringSpeed * minFiringSpeed || increaseVelOngoing))
                            {
                                SetStatus("Maneuvering (Increasing Velocity)");
                                Vector3 relPos = targetVessel.CoM - vessel.CoM;
                                float r = Mathf.Clamp01(currentRange / interceptRanges.y);
                                Vector3 lateralOffset = relVel.ProjectOnPlane(relPos).normalized * Mathf.Max(interceptRanges.z, currentRange);
                                fc.thrustDirection = Vector3.Lerp(relVel - targetVessel.perturbation, relPos + lateralOffset, r).normalized;
                                if (UseForwardThrust(fc.thrustDirection))
                                    fc.attitude = fc.thrustDirection;
                                else
                                    fc.attitude = -fc.thrustDirection;
                                fc.throttle = 1;
                                fc.alignmentToleranceforBurn = 45f;
                            }
                            else // Drifting
                            {
                                fc.throttle = 0;
                                fc.attitude = toTarget;

                                if (RecentFiringSolution(out Vector3 recentSolution)) // If we had a valid firing solution recently, use it to continue pointing toward target
                                    fc.attitude = recentSolution;
                                else if (BroadsideAttack)
                                    fc.attitude = BroadsideAttitude(vessel, targetVessel);

                                if (currentRange < minRange)
                                    SetStatus("Maneuvering (Drift Away)");
                                else
                                    SetStatus("Maneuvering (Drift)");
                            }
                        }
                    }
                    break;
                case StatusMode.Stranded:
                    {
                        SetStatus("Stranded");

                        fc.attitude = GunFiringSolution(weaponManager.previousGun);
                        fc.throttle = 0;
                    }
                    break;
                default: // Idle
                    {
                        if (hasWeapons)
                            SetStatus("Idle");
                        else
                            SetStatus("Idle (Unarmed)");

                        fc.attitude = Vector3.zero;
                        fc.throttle = 0;
                    }
                    break;
            }
            GunEngineEvasion();
            AlignEnginesWithThrust();
            UpdateRCSVector(rcsVector);
            UpdateBurnAlignmentTolerance();
        }

        void KillVelocity(bool onIntercept = false)
        {
            // Slow down to KillVelocityTargetSpeed(). If on an intercept to within gun range, try to slow down as late as possible
            // Otherwise, keep burning until at target speed

            Vector3 relPos = targetVessel.CoM - vessel.CoM;
            Vector3 relVel = targetVessel.GetObtVelocity() - vessel.GetObtVelocity();
            float targetSpeed = KillVelocityTargetSpeed();
            bool maintainThrottle = (relVel.sqrMagnitude > targetSpeed * targetSpeed);
            fc.thrustDirection = (relVel + targetVessel.perturbation).normalized;
            bool useReverseThrust = !UseForwardThrust(fc.thrustDirection) && (Vector3.Dot(relPos, relVel) < 0f);
            if (onIntercept)
            {
                Vector3 relAccel = targetVessel.perturbation - vessel.perturbation;
                Vector3 toIntercept = Intercept(relPos, relVel);
                float distanceToIntercept = toIntercept.magnitude;
                float timeToIntercept = vessel.TimeToCPA(toIntercept, targetVessel.GetObtVelocity(), targetVessel.perturbation, (float)vessel.orbit.period / 4f); // Avoid checking too far ahead (in case of multiple intercepts)
                float cpaDistSqr = AIUtils.PredictPosition(relPos, relVel, relAccel, timeToIntercept).sqrMagnitude;
                float interceptRangeMargin = Mathf.Min(interceptRanges.z * (1f + interceptMargin + (useReverseThrust ? 0.25f : 0f)), interceptRanges.y); // Extra margin when reverse thrusting since still facing target
                var weaponManager = WeaponManager;
                if (cpaDistSqr < weaponManager.gunRange * weaponManager.gunRange) // Gun range intercept, balance between throttle actions and intercept accuracy
                    maintainThrottle = relPos.sqrMagnitude < (interceptRangeMargin * interceptRangeMargin) || // Within intercept range margin
                        (maintainThrottle && Vector3.Dot(relPos, relVel) > 0f) || // Moving away from target faster than target speed, timeToIntecept == 0 does not always work here because it's possible we are on an orbit with two intersection points
                        ApproachingIntercept(); // Stopping distance > distance to target
                //else missile range intercept, exact positioning matters less

                SetStatus($"Maneuvering (Kill Velocity), {timeToCPAString}, {distanceToIntercept:N0}m");
            }
            else
            {
                SetStatus($"Maneuvering (Kill Velocity)");
            }

            if (useReverseThrust) // Use reverse thrust if possible and we are closing to target
                fc.attitude = -fc.thrustDirection;
            else
                fc.attitude = fc.thrustDirection;
            fc.throttle = maintainThrottle ? 1f : 0f;
            fc.alignmentToleranceforBurn = 45f;
        }

        void InterceptTarget()
        {
            SetStatus($"Maneuvering (Intercept Target), {timeToCPAString}, {distToCPA:N0}m");
            Vector3 relPos = targetVessel.CoM - vessel.CoM;
            Vector3 relVel = targetVessel.GetObtVelocity() - vessel.GetObtVelocity();

            // Burn the difference between the target and current velocities.
            Vector3 toIntercept = Intercept(relPos, relVel);
            Vector3 burn = toIntercept.normalized * ManeuverSpeed + relVel;
            fc.thrustDirection = burn.normalized;

            // Use reverse thrust if it is necessary to face target during intercept
            bool useReverseThrust = ReverseThrust && (Vector3.Dot(relPos, fc.thrustDirection) < 0f);
            if (useReverseThrust)
                fc.attitude = -fc.thrustDirection;
            else
                fc.attitude = fc.thrustDirection;
            fc.throttle = 1f;
        }

        void AddDebugMessages()
        {
            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI)
            {
                debugString.AppendLine($"Current Status: {currentStatus}");
                debugString.AppendLine($"Propulsion:{hasPropulsion}; RCS:{hasRCS}; EC:{hasEC}; Weapons:{hasWeapons}");
                debugString.AppendLine($"Max Acceleration:{maxAcceleration:G3}; Intercept Velocity:{KillVelocityTargetSpeed():G3}");
                if (targetVessel)
                {
                    debugString.AppendLine($"Target Vessel: {targetVessel.GetDisplayName()}");
                    debugString.AppendLine($"Can Intercept: {CanInterceptShip(targetVessel)}, On Intercept: {OnIntercept(currentStatus.Contains("Intercept Target") ? 0.05f : 0.25f)}");
                    debugString.AppendLine($"Target Range: {VesselDistance(vessel, targetVessel):G3}");
                    debugString.AppendLine($"Min/Max/Intercept Range: {interceptRanges.x}/{interceptRanges.y}/{interceptRanges.z}");
                    debugString.AppendLine($"Time to CPA: {timeToCPA:G3}");
                    debugString.AppendLine($"Distance to CPA: {distToCPA:G3}");
                    debugString.AppendLine($"Stopping Distance: {stoppingDist:G3}");
                    debugString.AppendLine($"Apoapsis: {vessel.orbit.ApA / 1000:G2}km / {vessel.orbit.timeToAp:G2}s");
                    debugString.AppendLine($"Periapsis: {vessel.orbit.PeA / 1000:G2}km / {vessel.orbit.timeToPe:G2}s");
                    debugString.AppendLine($"Missile Launch Fail Timer: {missileTryLaunchTime:G2}s");
                }
                debugString.AppendLine($"Evasive {evasiveTimer}s");
                var weaponManager = WeaponManager;
                if (weaponManager) debugString.AppendLine($"Threat Sqr Distance: {weaponManager.incomingThreatDistanceSqr}");
            }
        }

        void UpdateStatus()
        {
            // Update propulsion and weapon status
            hasRCS = VesselModuleRegistry.GetModules<ModuleRCS>(vessel).Any(e => e.rcsEnabled && !e.flameout) || (rcsEngines.Count > 0 && rcsEngines.Any(e => e != null && e.EngineIgnited && e.isOperational && !e.flameout));
            hasPropulsion = VesselModuleRegistry.GetModules<ModuleRCS>(vessel).Any(e => e.rcsEnabled && !e.flameout && e.useThrottle) ||
                forwardEngines.Any(e => e != null && e.EngineIgnited && e.isOperational && !e.flameout) ||
                reverseEngines.Any(e => e != null && e.EngineIgnited && e.isOperational && !e.flameout);
            vessel.GetConnectedResourceTotals(ECID, out double EcCurrent, out double ecMax);
            hasEC = EcCurrent > 0 || CheatOptions.InfiniteElectricity;
            var weaponManager = WeaponManager;
            hasWeapons = (weaponManager != null) && weaponManager.HasWeaponsAndAmmo();

            // Check on command status
            UpdateCommand();

            // Update intercept ranges and time to CPA
            interceptRanges = InterceptionRanges(); //.x = minRange, .y = maxRange, .z = interceptRange
            float targetSqrDist = 0f;
            float forceFiringRangeSqr = Mathf.Min(ForceFiringRange, interceptRanges.y);
            forceFiringRangeSqr *= forceFiringRangeSqr;
            if (targetVessel != null)
            {
                timeToCPA = vessel.TimeToCPA(targetVessel);
                targetSqrDist = FromTo(vessel, targetVessel).sqrMagnitude;
            }

            // Prioritize safe orbits over combat outside of weapon range
            bool fixOrbitNow = hasPropulsion && (CheckOrbitDangerous() || ongoingOrbitCorrectionDueTo != OrbitCorrectionReason.None) && currentStatusMode != StatusMode.Ramming;
            bool fixOrbitLater = false;
            if (hasPropulsion && !fixOrbitNow && CheckOrbitUnsafe())
            {
                fixOrbitLater = true;
                if (weaponManager && targetVessel != null && currentStatusMode != StatusMode.Ramming)
                    fixOrbitNow = ((vessel.CoM - targetVessel.CoM).sqrMagnitude > interceptRanges.y * interceptRanges.y) && (timeToCPA > 10f);
            }

            // Update status mode
            if (currentStatusMode != StatusMode.Ramming && FlyAvoidOthers())
                currentStatusMode = StatusMode.AvoidingCollision;
            else if (weaponManager && weaponManager.missileIsIncoming && weaponManager.incomingMissileVessel && weaponManager.incomingMissileTime <= weaponManager.evadeThreshold) // Needs to start evading an incoming missile.
                currentStatusMode = StatusMode.Evading;
            else if (fixOrbitNow)
                currentStatusMode = StatusMode.CorrectingOrbit;
            else if (currentCommand == PilotCommands.FlyTo || currentCommand == PilotCommands.Follow || currentCommand == PilotCommands.Attack || currentCommand == PilotCommands.WingAttack)
            {
                currentStatusMode = StatusMode.Commanded;
                if (currentCommand != lastUpdateCommand)
                    maneuverStateChanged = true;
            }
            else if (weaponManager)
            {
                if (hasPropulsion && !hasWeapons && (allowRamming && !BDArmorySettings.DISABLE_RAMMING) && targetVessel != null)
                    currentStatusMode = StatusMode.Ramming;
                else if (hasPropulsion && !hasWeapons && CheckWithdraw())
                    currentStatusMode = StatusMode.Withdrawing;
                else if (targetVessel != null && weaponManager.currentGun && GunReady(weaponManager.currentGun))
                    currentStatusMode = StatusMode.Firing; // Guns
                else if (targetVessel != null && weaponManager.CurrentMissile && !weaponManager.GetLaunchAuthorization(targetVessel, weaponManager, weaponManager.CurrentMissile))
                    currentStatusMode = StatusMode.Firing; // Missiles
                else if (targetVessel != null && weaponManager.CurrentMissile && weaponManager.guardFiringMissile && currentStatusMode == StatusMode.Firing)
                    currentStatusMode = StatusMode.Firing; // Post-launch authorization missile firing underway, don't change status from Firing
                else if (targetVessel != null && hasWeapons)
                {
                    if (hasPropulsion)
                        if (targetSqrDist < MinEngagementRange * MinEngagementRange || targetSqrDist > forceFiringRangeSqr)
                            currentStatusMode = StatusMode.Maneuvering; // Maneuver if outside MinEngagementRange - ForceFiringRange
                        else
                            currentStatusMode = StatusMode.Firing; // Else fire with zero throttle (thrust evasion can override this)
                    else
                        currentStatusMode = StatusMode.Stranded;
                }
                else if (fixOrbitLater)
                    currentStatusMode = StatusMode.CorrectingOrbit;
                else
                    currentStatusMode = StatusMode.Idle;
            }
            else if (fixOrbitLater)
                currentStatusMode = StatusMode.CorrectingOrbit;
            else
                currentStatusMode = StatusMode.Idle;

            // Flag changed status if necessary
            if (lastStatusMode != currentStatusMode || maneuverStateChanged)
            {
                maneuverStateChanged = true;
                lastStatusMode = currentStatusMode;
                if (BDArmorySettings.DEBUG_AI)
                    Debug.Log("[BDArmory.BDModuleOrbitalAI]: Status of " + vessel.vesselName + " changed from " + lastStatusMode + " to " + currentStatus);
            }

            // Switch on PID Mode
            switch (PIDMode)
            {
                case PIDModeTypes.Inactive:
                    PIDActive = false;
                    break;
                case PIDModeTypes.Firing:
                    PIDActive = currentStatusMode == StatusMode.Firing;
                    break;
                case PIDModeTypes.Everything:
                    PIDActive = true;
                    break;
            }

            // Temporarily inhibit maneuvers if not evading a missile and waiting for a launched missile to fly to a safe distance
            if (currentStatusMode != StatusMode.Evading && weaponManager && weaponManager.PreviousMissile)
            {
                if ((vessel.CoM - weaponManager.PreviousMissile.vessel.transform.position).sqrMagnitude < vessel.vesselSize.sqrMagnitude)
                {
                    fc.Stability(true);
                    PIDActive = false;
                }
                else
                    fc.Stability(false);
            }
            else
                fc.Stability(false);

            // Disable PID when ramming to prevent vessels from going haywire - FIXME - Figure out why this is happening and fix
            if (PIDActive && currentStatusMode == StatusMode.Ramming)
                PIDActive = false;

            // Set PID Mode
            fc.PIDActive = PIDActive;
            if (PIDActive)
                vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
            // Check for incoming gunfire
            EvasionStatus();

            // Set target as UI target
            if (vessel.isActiveVessel && targetVessel && !targetVessel.IsMissile() && (vessel.targetObject == null || vessel.targetObject.GetVessel() != targetVessel))
            {
                FlightGlobals.fetch.SetVesselTarget(targetVessel, true);
            }
        }

        void UpdateCommand()
        {
            if (command == PilotCommands.Follow && commandLeader is null)
            {
                ReleaseCommand();
                return;
            }
            else if (command == PilotCommands.Attack || command == PilotCommands.WingAttack)
            {
                if (targetVessel != null)
                {
                    ReleaseCommand(false);
                    return;
                }
                else
                {
                    var weaponManager = WeaponManager;
                    if (weaponManager.underAttack || weaponManager.underFire)
                    {
                        ReleaseCommand(false);
                        return;
                    }
                }
            }
        }

        void EvasionStatus()
        {
            evadingGunfire = false;
            var weaponManager = WeaponManager;

            // Return if evading missile or ramming
            if (weaponManager == null || currentStatusMode == StatusMode.Evading || currentStatusMode == StatusMode.Ramming)
            {
                evasiveTimer = 0;
                return;
            }

            // Check if we should be evading gunfire, missile evasion is handled separately
            float threatRating = evasionThreshold + 1f; // Don't evade by default
            if (weaponManager.underFire)
            {
                if (weaponManager.incomingMissTime >= evasionTimeThreshold && weaponManager.incomingThreatDistanceSqr >= evasionMinRangeThreshold * evasionMinRangeThreshold) // If we haven't been under fire long enough or they're too close, ignore gunfire
                    threatRating = weaponManager.incomingMissDistance;
            }
            // If we're currently evading or a threat is significant
            if ((evasiveTimer < minEvasionTime && evasiveTimer != 0) || threatRating < evasionThreshold)
            {
                if (evasiveTimer < minEvasionTime)
                {
                    threatRelativePosition = vessel.GetObtVelocity().normalized + vesselTransform.right;
                    if (weaponManager)
                    {
                        if (weaponManager.underFire)
                            threatRelativePosition = weaponManager.incomingThreatPosition - vesselTransform.position;
                    }
                }
                evadingGunfire = true;
                evasionNonLinearityDirection = (evasionNonLinearityDirection + evasionErraticness * UnityEngine.Random.onUnitSphere).normalized;
                evasiveTimer += Time.fixedDeltaTime;

                if (evasiveTimer >= minEvasionTime)
                    evasiveTimer = 0;
            }
        }
        #endregion Actual AI Pilot

        #region Utility Functions

        private bool CheckWithdraw()
        {
            var nearest = BDATargetManager.GetClosestTarget(WeaponManager);
            if (nearest == null) return false;

            return RelVel(vessel, nearest.Vessel).sqrMagnitude < 200 * 200;
        }

        private bool CheckOrbitDangerous()
        {
            Orbit o = vessel.orbit;
            bool descending = o.timeToPe > 0 && o.timeToPe < o.timeToAp;
            bool fallingInsideAtmo = descending && o.altitude < minSafeAltitude; // Descending inside atmo, OrbitCorrectionReason.FallingInsideAtmosphere
            bool dangerousPeriapsis = descending && o.PeA < 0.8f * minSafeAltitude; // Descending & periapsis suggests we are close to falling inside atmosphere, OrbitCorrectionReason.PeriapsisLow
            bool entirelyInsideAtmo = o.ApA < minSafeAltitude && o.ApA >= 0; // Entirety of orbit is inside atmosphere, OrbitCorrectionReason.ApoapsisLow
            return (fallingInsideAtmo || dangerousPeriapsis || entirelyInsideAtmo);
        }

        private bool CheckOrbitUnsafe()
        {
            Orbit o = vessel.orbit;
            bool descending = o.timeToPe > 0 && o.timeToPe < o.timeToAp;
            bool escaping = EscapingOrbit(); // Vessel is on an escape orbit and has passed the periapsis by over 60s, OrbitCorrectionReason.Escaping
            bool periapsisLow = descending && o.PeA < minSafeAltitude && o.ApA >= minSafeAltitude && o.altitude >= minSafeAltitude; // We are outside the atmosphere but our periapsis is inside the atmosphere, OrbitCorrectionReason.PeriapsisLow
            return (escaping || periapsisLow); // Match conditions in PilotLogic
        }

        private void UpdateBody()
        {
            if (vessel.orbit.referenceBody != safeAltBody) // Body has been updated, update min safe alt
            {
                minSafeAltitude = vessel.orbit.referenceBody.MinSafeAltitude();
                safeAltBody = vessel.orbit.referenceBody;
            }
        }

        private bool EscapingOrbit()
        {
            return (vessel.orbit.ApA < 0 && vessel.orbit.timeToPe < -60);
        }

        private bool CanInterceptShip(Vessel target)
        {
            bool canIntercept = false;

            // Is it worth us chasing a withdrawing ship?
            BDModuleOrbitalAI targetAI = target.ActiveController().OrbitalAI;

            if (targetAI && targetAI.pilotEnabled)
            {
                Vector3 toTarget = target.CoM - vessel.CoM;
                bool escaping = targetAI.currentStatusMode == StatusMode.Withdrawing;
                var weaponManager = WeaponManager;

                canIntercept = !escaping || // It is not trying to escape.
                    toTarget.sqrMagnitude < weaponManager.gunRange * weaponManager.gunRange || // It is already in range.
                    maxAcceleration * maxAcceleration > targetAI.vessel.acceleration_immediate.sqrMagnitude || //  We are faster (currently).
                    Vector3.Dot(target.GetObtVelocity() - vessel.GetObtVelocity(), toTarget) < 0; // It is getting closer.
            }
            return canIntercept;
        }
        public float BurnTime(float deltaV, float totalConsumption, bool useReverseThrust)
        {
            float thrust = maxThrust * ((useReverseThrust && currentForwardThrust) ? reverseForwardThrustRatio : 1f);
            if (totalConsumption == 0f)
                return ((float)vessel.totalMass * deltaV / thrust);
            else
            {
                float isp = thrust / totalConsumption;
                return ((float)vessel.totalMass * (1.0f - 1.0f / Mathf.Exp(deltaV / isp)) / totalConsumption);
            }
        }
        public float StoppingDistance(float speed, bool useReverseThrust)
        {
            float consumptionRate = GetConsumptionRate(useReverseThrust);
            float time = BurnTime(speed, consumptionRate, useReverseThrust);
            float f = ((useReverseThrust && currentForwardThrust) ? reverseForwardThrustRatio : 1f);
            float jerk = (float)(f * maxThrust / (vessel.totalMass - consumptionRate)) - f * maxAcceleration;
            return speed * time + 0.5f * -(f * maxAcceleration) * time * time + 1 / 6 * -jerk * time * time * time;
        }

        private bool ApproachingIntercept(float margin = 0.0f)
        {
            Vector3 relPos = targetVessel.CoM - vessel.CoM;
            Vector3 relVel = targetVessel.GetObtVelocity() - vessel.GetObtVelocity();
            if (Vector3.Dot(relVel, relPos.normalized) > -10f)
                return false;
            Vector3 toIntercept = Intercept(relPos, relVel);
            bool useReverseThrust = (!UseForwardThrust(-toIntercept) && (Vector3.Dot(relPos, relVel) < 0f)) || (ReverseThrust && forwardEngines.Count == 0);
            margin += useReverseThrust ? 1.5f : 0f; // Stop earlier when reverse thrusting to keep facing target
            float angleToRotate = VectorUtils.Angle((useReverseThrust ? -1 : 1) * vessel.ReferenceTransform.up, relVel) * Mathf.Deg2Rad * 0.75f;
            float timeToRotate = BDAMath.SolveTime(angleToRotate, maxAngularAccelerationMag) / 0.75f;
            float relSpeed = relVel.magnitude;
            float interceptStoppingDistance = StoppingDistance(relSpeed, useReverseThrust) + relSpeed * (margin + timeToRotate * 3f);
            float distanceToIntercept = toIntercept.magnitude;
            distToCPA = distanceToIntercept;
            stoppingDist = interceptStoppingDistance;

            return distanceToIntercept < interceptStoppingDistance;
        }

        private bool OnIntercept(float tolerance)
        {
            if (targetVessel is null)
                return false;
            Vector3 relPos = targetVessel.CoM - vessel.CoM;
            Vector3 relVel = targetVessel.GetObtVelocity() - vessel.GetObtVelocity();
            if (Vector3.Dot(relPos, relVel) >= 0f)
                return false;
            Vector3 cpa = vessel.orbit.getPositionAtUT(Planetarium.GetUniversalTime() + timeToCPA) - targetVessel.orbit.getPositionAtUT(Planetarium.GetUniversalTime() + timeToCPA);
            float interceptRange = interceptRanges.z;
            float interceptRangeTolSqr = (interceptRange * (tolerance + 1f)) * (interceptRange * (tolerance + 1f));

            bool speedWithinLimits = Mathf.Abs(relVel.magnitude - ManeuverSpeed) < ManeuverSpeed * tolerance;
            if (!speedWithinLimits)
            {
                BDModuleOrbitalAI targetAI = targetVessel.ActiveController().OrbitalAI;
                if (targetAI && targetAI.pilotEnabled)
                    speedWithinLimits = targetAI.ManeuverSpeed > ManeuverSpeed && targetAI.targetVessel == vessel && RelVel(targetVessel, vessel).sqrMagnitude > ManeuverSpeed * ManeuverSpeed; // Target is targeting us, set to maneuver faster, and is maneuvering faster
            }
            return cpa.sqrMagnitude < interceptRangeTolSqr && speedWithinLimits;
        }

        private Vector3 Intercept(Vector3 relPos, Vector3 relVel)
        {
            Vector3 lateralVel = -relVel.ProjectOnPlane(relPos);
            Vector3 lateralOffset = lateralVel.normalized * interceptRanges.z;
            return relPos + lateralOffset;
        }

        private Vector3 InterceptionRanges()
        {
            Vector3 interceptRanges = Vector3.zero;
            float minRange = MinEngagementRange;
            float maxRange = Mathf.Max(minRange * 1.2f, ForceFiringRange);
            bool usingProjectile = true;
            var weaponManager = WeaponManager;
            if (weaponManager != null)
            {
                bool checkAllWeapons = false;
                if (weaponManager.selectedWeapon != null)
                {
                    currentWeapon = weaponManager.selectedWeapon;
                    EngageableWeapon engageableWeapon = currentWeapon as EngageableWeapon;
                    minRange = Mathf.Max(engageableWeapon.GetEngagementRangeMin(), minRange);
                    maxRange = engageableWeapon.GetEngagementRangeMax();
                    usingProjectile = currentWeapon.GetWeaponClass() switch
                    {
                        WeaponClasses.Missile or WeaponClasses.Bomb or WeaponClasses.SLW => false,
                        _ => true
                    };
                    if (usingProjectile)
                    {
                        missileTryLaunchTime = 0f;
                        if (weaponManager.CheckAmmo(currentWeapon as ModuleWeapon))
                            maxRange = Mathf.Min(maxRange, weaponManager.gunRange);
                        else
                            checkAllWeapons = true;
                    }
                    else
                    {
                        MissileBase ml = currentWeapon as MissileBase;
                        missileTryLaunchTime = weaponManager.missilesAway.Any() ? 0f : missileTryLaunchTime;
                        maxRange = weaponManager.MaxMissileRange(ml, weaponManager.UnguidedMissile(ml, maxRange));
                        maxRange = Mathf.Max(maxRange * (1 - 0.15f * Mathf.Floor(missileTryLaunchTime / 20f)),
                                    Mathf.Min(weaponManager.gunRange, minRange * 1.2f));
                        // If trying to fire a missile and within range, gradually decrease max range by 15% every 20s outside of range and unable to fire
                        if (targetVessel != null && (targetVessel.CoM - vessel.CoM).sqrMagnitude < maxRange * maxRange)
                            missileTryLaunchTime += Time.fixedDeltaTime;
                    }
                }
                else
                    checkAllWeapons = true;

                if (checkAllWeapons)
                {
                    missileTryLaunchTime = 0f;

                    foreach (var weapon in VesselModuleRegistry.GetModules<IBDWeapon>(vessel))
                    {
                        if (weapon == null) continue;
                        if (!((EngageableWeapon)weapon).engageAir) continue;
                        float maxEngageRange = ((EngageableWeapon)weapon).GetEngagementRangeMax();
                        if (weapon.GetWeaponClass() == WeaponClasses.Missile)
                        {
                            MissileBase ml = weapon as MissileBase;
                            maxRange = Mathf.Max(weaponManager.MaxMissileRange(ml, weaponManager.UnguidedMissile(ml, maxRange)), maxRange);
                            usingProjectile = false;
                        }
                        else if (weaponManager.CheckAmmo(weapon as ModuleWeapon))
                            maxRange = Mathf.Max(Mathf.Min(maxEngageRange, weaponManager.gunRange), maxRange);
                    }
                }
                if (targetVessel != null)
                    minRange = Mathf.Max(minRange, targetVessel.GetRadius());
            }
            float interceptRange = minRange + (maxRange - minRange) * (usingProjectile ? 0.25f : 0.75f);
            interceptRanges.x = minRange;
            interceptRanges.y = maxRange;
            interceptRanges.z = interceptRange;
            return interceptRanges;
        }

        private bool GunReady(ModuleWeapon gun)
        {
            if (gun == null) return false;

            // Check gun/laser can fire soon, we are within guard and weapon engagement ranges, and we are under the firing speed
            float targetSqrDist = FromTo(vessel, targetVessel).sqrMagnitude;
            var weaponManager = WeaponManager;
            return GunFiringSpeedCheck() && gun.CanFireSoon() &&
                (targetSqrDist <= gun.GetEngagementRangeMax() * gun.GetEngagementRangeMax()) &&
                (targetSqrDist <= weaponManager.gunRange * weaponManager.gunRange);
        }

        private bool GunFiringSpeedCheck()
        {
            // See if we are under firing speed for firing, or if killing velocity, under kill velocity speed target
            float relVelSqrMag = RelVel(vessel, targetVessel).sqrMagnitude;
            if (currentStatus.Contains("Kill Velocity"))
            {
                float speedTarget = KillVelocityTargetSpeed();
                return relVelSqrMag < speedTarget * speedTarget;
            }
            else
                return minFiringSpeed * minFiringSpeed < relVelSqrMag && relVelSqrMag < firingSpeed * firingSpeed;
        }
        private Vector3 GunFiringSolution(ModuleWeapon weapon)
        {
            // For fixed weapons, returns attitude that puts fixed weapon on target, even if not aligned with vesselTransform.up
            // For turreted weapons, returns attitude toward target or broadside attitude depending on BroadsideAttack setting

            Vector3 firingSolution;
            if (weapon != null && !weapon.turret)
            {
                Vector3 leadOffset = weapon.GetLeadOffset();
                Vector3 target = targetVessel.CoM;
                target -= leadOffset;  // Lead offset from aiming assuming the gun is forward aligned and centred.
                                       // Note: depending on the airframe, there is an island of stability around -2°—30° in pitch and ±10° in yaw where the vessel can stably aim with offset weapons.
                Vector3 weaponPosition = weapon.offsetWeaponPosition + vessel.ReferenceTransform.position;
                Vector3 weaponDirection = vessel.ReferenceTransform.TransformDirection(weapon.offsetWeaponDirection);

                target = Quaternion.FromToRotation(weaponDirection, vesselTransform.up) * (target - vesselTransform.position) + vesselTransform.position; // correctly account for angular offset guns/schrage Musik
                var weaponOffset = vessel.ReferenceTransform.position - weaponPosition;

                debugString.AppendLine($"WeaponOffset ({targetVessel.vesselName}): {weaponOffset.x}x m; {weaponOffset.y}y m; {weaponOffset.z}z m");
                target += weaponOffset; //account for weapons with translational offset from longitudinal axis
                firingSolution = (target - vessel.CoM).normalized;
            }
            else // Null weapon or firing turrets, just point in general direction
            {
                firingSolution = BroadsideAttack ? BroadsideAttitude(vessel, targetVessel) : FromTo(vessel, targetVessel).normalized;
            }
            return firingSolution;
        }

        private bool RecentFiringSolution(out Vector3 recentFiringSolution)
        {
            // Return true if a valid recent firing solution exists, if the solution exists, also return that value
            bool validRecentSolution = false;
            var weaponManager = WeaponManager;

            if (lastFiringSolution == Vector3.zero) // Throttle was used recently, no valid firing solution exists, even if previousGun != null
            {
                recentFiringSolution = Vector3.zero;
            }
            else if (weaponManager && weaponManager.previousGun != null) // If we had a gun recently selected, use it to continue pointing toward target
            {
                recentFiringSolution = GunFiringSolution(weaponManager.previousGun);
                validRecentSolution = true;
            }
            else // (lastFiringSolution != Vector3.zero)
            {
                recentFiringSolution = lastFiringSolution;
                validRecentSolution = true;
            }

            return validRecentSolution;
        }

        private float KillVelocityTargetSpeed()
        {
            float speedTarget = maxAcceleration * 0.15f;

            if (targetVessel != null)
            {
                float speedTargetHigh = Mathf.Abs(firingSpeed - minFiringSpeed) * 0.75f + minFiringSpeed;

                // If below speedTargetHigh and enemy is still decelerating, set the speed target as speedTargetHigh
                if (speedTarget < speedTargetHigh && targetVessel.acceleration_immediate.sqrMagnitude > 0.1f &&
                    Vector3.Dot(targetVessel.acceleration_immediate, FromTo(vessel, targetVessel)) > 0 &&
                    RelVel(targetVessel, vessel).sqrMagnitude < speedTargetHigh * speedTargetHigh)
                    speedTarget = speedTargetHigh;

                // If our target is targeting us and is maneuvering at a speed slower than our fire speed, set speed target as slighly above their maneuver speed
                BDModuleOrbitalAI targetAI = targetVessel.ActiveController().OrbitalAI;
                if (targetAI && targetAI.pilotEnabled && targetAI.targetVessel == vessel && targetAI.ManeuverSpeed < firingSpeed)
                    speedTarget = Mathf.Max(1.05f * targetAI.ManeuverSpeed, speedTarget);
            }

            return Mathf.Clamp(speedTarget, FiringTargetSpeed(), firingSpeed);
        }

        private float FiringTargetSpeed()
        {
            return Mathf.Abs(firingSpeed - minFiringSpeed) * 0.2f + minFiringSpeed;
        }

        private bool AwayCheck(float minRange)
        {
            // Check if we need to manually burn away from an enemy that's too close or
            // if it would be better to drift away.

            Vector3 toTarget = FromTo(vessel, targetVessel);
            Vector3 toEscape = -toTarget.normalized;
            Vector3 relVel = targetVessel.GetObtVelocity() - vessel.GetObtVelocity();
            if (relVel.sqrMagnitude < minFiringSpeed * minFiringSpeed) return true;
            bool useForwardThrust = UseForwardThrust(toEscape);
            Vector3 thrustDir = useForwardThrust ? vessel.ReferenceTransform.up : -vessel.ReferenceTransform.up;
            float rotDistance = VectorUtils.Angle(thrustDir, toEscape) * Mathf.Deg2Rad;
            float timeToRotate = BDAMath.SolveTime(rotDistance / 2, maxAngularAccelerationMag) * 2;
            float timeToDisplace = BDAMath.SolveTime(minRange - toTarget.magnitude, (!useForwardThrust && currentForwardThrust ? reverseForwardThrustRatio : 1f) * maxAcceleration, Vector3.Dot(-relVel, toEscape));
            float timeToEscape = timeToRotate * 2 + timeToDisplace;

            Vector3 drift = AIUtils.PredictPosition(toTarget, relVel, Vector3.zero, timeToEscape);
            bool manualEscape = drift.sqrMagnitude < minRange * minRange;

            return manualEscape;
        }

        bool PredictCollisionWithVessel(Vessel v, float maxTime, out Vector3 badDirection)
        {
            var weaponManager = WeaponManager;
            if (vessel == null || v == null ||
                (weaponManager != null && v == weaponManager.incomingMissileVessel) ||
                (v.rootPart != null && v.rootPart.FindModuleImplementing<MissileBase>() != null) ||  //evasive will handle avoiding missiles
                Vector3.Dot(v.GetObtVelocity() - vessel.GetObtVelocity(), v.CoM - vessel.CoM) >= 0f) // Don't bother if vessels are not approaching each other
            {
                badDirection = Vector3.zero;
                return false;
            }

            // Adjust some values for asteroids.
            var targetRadius = v.GetRadius();
            var threshold = collisionAvoidanceThreshold + targetRadius; // Add the target's average radius to the threshold.
            if (v.vesselType == VesselType.SpaceObject) // Give asteroids some extra room.
            {
                maxTime += targetRadius * targetRadius / (v.GetObtVelocity() - vessel.GetObtVelocity()).sqrMagnitude;
            }

            // Use the nearest time to closest point of approach to check separation instead of iteratively sampling. Should give faster, more accurate results.
            float timeToCPA = vessel.TimeToCPA(v, maxTime); // This uses the same kinematics as AIUtils.PredictPosition.
            if (timeToCPA > 0 && timeToCPA < maxTime)
            {
                Vector3 tPos = AIUtils.PredictPosition(v, timeToCPA);
                Vector3 myPos = AIUtils.PredictPosition(vessel, timeToCPA);
                if (Vector3.SqrMagnitude(tPos - myPos) < threshold * threshold) // Within collisionAvoidanceThreshold of each other. Danger Will Robinson!
                {
                    badDirection = tPos - vesselTransform.position;
                    return true;
                }
            }

            badDirection = Vector3.zero;
            return false;
        }

        bool FlyAvoidOthers() // Check for collisions with other vessels and try to avoid them.
        {
            if (collisionAvoidanceThreshold == 0) return false;
            if (currentlyAvoidedVessel != null) // Avoidance has been triggered.
            {
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Avoiding Collision");

                // Monitor collision avoidance, adjusting or stopping as necessary.
                if (currentlyAvoidedVessel != null && PredictCollisionWithVessel(currentlyAvoidedVessel, vesselCollisionAvoidanceLookAheadPeriod * 1.2f, out collisionAvoidDirection)) // *1.2f for hysteresis.
                    return true;
                else // Stop avoiding, but immediately check again for new collisions.
                {
                    currentlyAvoidedVessel = null;
                    collisionDetectionTicker = vesselCollisionAvoidanceTickerFreq + 1;
                    return FlyAvoidOthers();
                }
            }
            else if (collisionDetectionTicker > vesselCollisionAvoidanceTickerFreq) // Only check every vesselCollisionAvoidanceTickerFreq frames.
            {
                collisionDetectionTicker = 0;

                // Check for collisions with other vessels.
                bool vesselCollision = false;
                VesselType collisionVesselType = VesselType.Unknown; // Start as not debris.
                float collisionTargetLargestSize = -1f;
                collisionAvoidDirection = vessel.srf_vel_direction;
                // First pass, only consider valid vessels.
                using (var vs = BDATargetManager.LoadedVessels.GetEnumerator())
                    while (vs.MoveNext())
                    {
                        if (vs.Current == null) continue;
                        if (vs.Current.vesselType == VesselType.Debris) continue; // Ignore debris on the first pass.
                        if (vs.Current == vessel || vs.Current.Landed) continue;
                        if (!PredictCollisionWithVessel(vs.Current, vesselCollisionAvoidanceLookAheadPeriod, out Vector3 collisionAvoidDir)) continue;
                        if (!VesselModuleRegistry.IgnoredVesselTypes.Contains(vs.Current.vesselType))
                        {
                            var ibdaiControl = vs.Current.ActiveController().AI;
                            if (ibdaiControl != null && ibdaiControl.currentCommand == PilotCommands.Follow && ibdaiControl.commandLeader != null && ibdaiControl.commandLeader.vessel == vessel) continue;
                        }
                        var collisionTargetSize = vs.Current.vesselSize.sqrMagnitude; // We're only interested in sorting by size, which is much faster than sorting by mass.
                        if (collisionVesselType == vs.Current.vesselType && collisionTargetSize < collisionTargetLargestSize) continue; // Avoid the largest object.
                        vesselCollision = true;
                        currentlyAvoidedVessel = vs.Current;
                        collisionAvoidDirection = collisionAvoidDir;
                        collisionVesselType = vs.Current.vesselType;
                        collisionTargetLargestSize = collisionTargetSize;
                    }
                // Second pass, only consider debris.
                if (!vesselCollision)
                {
                    using var vs = BDATargetManager.LoadedVessels.GetEnumerator();
                    while (vs.MoveNext())
                    {
                        if (vs.Current == null) continue;
                        if (vs.Current.vesselType != VesselType.Debris) continue; // Only consider debris on the second pass.
                        if (vs.Current == vessel || vs.Current.Landed) continue;
                        if (!PredictCollisionWithVessel(vs.Current, vesselCollisionAvoidanceLookAheadPeriod, out Vector3 collisionAvoidDir)) continue;
                        var collisionTargetSize = vs.Current.vesselSize.sqrMagnitude;
                        if (collisionTargetSize < collisionTargetLargestSize) continue; // Avoid the largest debris object.
                        vesselCollision = true;
                        currentlyAvoidedVessel = vs.Current;
                        collisionAvoidDirection = collisionAvoidDir;
                        collisionVesselType = vs.Current.vesselType;
                        collisionTargetLargestSize = collisionTargetSize;
                    }
                }
                if (vesselCollision)
                    return true;
                else
                { currentlyAvoidedVessel = null; }
            }
            else
            { ++collisionDetectionTicker; }
            return false;
        }

        Vector3 broadsideAttitudeLerp;
        private Vector3 BroadsideAttitude(Vessel self, Vessel target)
        {
            // Return lerped attitude for broadside attack. Lerp attitude to reduce oscillation.
            Vector3 toTarget = FromTo(self, target).normalized;
            Vector3 up = self.vesselTransform.up;
            Vector3 broadsideAttitude = up.ProjectOnPlanePreNormalized(toTarget);
            float error = VectorUtils.Angle(up, broadsideAttitude);
            float angleLerp = Mathf.InverseLerp(0, 10, error);
            float lerpRate = Mathf.Lerp(1, 10, angleLerp);
            broadsideAttitudeLerp = Vector3.Lerp(broadsideAttitudeLerp, broadsideAttitude, lerpRate * Time.deltaTime); //Lerp has reduced oscillation compared to Slerp
            return broadsideAttitudeLerp;
        }

        private void GunEngineEvasion()
        {
            if (!((evadingGunfire && evasionEngines) && (currentStatusMode == StatusMode.Maneuvering || currentStatusMode == StatusMode.Firing))) return;

            var weaponManager = WeaponManager;
            Vector3 relVelProjected = (weaponManager.incomingThreatVessel ? RelVel(vessel, weaponManager.incomingThreatVessel) : vessel.GetObtVelocity()).ProjectOnPlane(threatRelativePosition);
            Vector3 evasionDir = evasionErraticness * Vector3.Project(evasionNonLinearityDirection, relVelProjected).normalized;
            Vector3 thrustDir = (fc.thrustDirection == Vector3.zero ? fc.attitude : fc.thrustDirection);
            evasionDir = evasionErraticness == 0 ? thrustDir : Vector3.Lerp(evasionDir, thrustDir + evasionDir, fc.throttle).normalized;
            if (fc.thrustDirection == fc.attitude || fc.thrustDirection == Vector3.zero)
            {
                fc.thrustDirection = evasionDir;
                fc.attitude = evasionDir;
            }
            else
            {
                fc.thrustDirection = evasionDir;
                fc.attitude = -evasionDir;
            }
            fc.alignmentToleranceforBurn = 70;
            fc.throttle = Mathf.Clamp01(fc.throttle + Mathf.Clamp01(1f - threatRelativePosition.sqrMagnitude / 1e8f));
            fc.lerpThrottle = false;
        }

        private void UpdateRCSVector(Vector3 inputVec = default(Vector3))
        {
            if (currentStatusMode == StatusMode.AvoidingCollision)
            {
                fc.rcsLerpRate = 100f / Time.fixedDeltaTime; // instant changes, don't Lerp
                fc.rcsRotate = false;
            }
            else if (currentStatusMode == StatusMode.Ramming)
            {
                fc.RCSPower = 20f;
                fc.rcsLerpRate = 5f;
                fc.rcsRotate = false;
            }
            else if (evadingGunfire && evasionRCS) // Quickly move RCS vector
            {
                var weaponManager = WeaponManager;
                Vector3 relVelProjected = (weaponManager.incomingThreatVessel ? RelVel(vessel, weaponManager.incomingThreatVessel) : vessel.GetObtVelocity()).ProjectOnPlane(threatRelativePosition);
                inputVec = Vector3.Cross(Vector3.Project(evasionNonLinearityDirection, relVelProjected), threatRelativePosition).normalized;
                fc.rcsLerpRate = 100f / Time.fixedDeltaTime; // instant changes, don't Lerp
                fc.rcsRotate = false;
            }
            else // Slowly lerp RCS vector
            {
                fc.rcsLerpRate = 5f;
                fc.rcsRotate = false;
            }

            fc.RCSVector = inputVec;

            // Update engine RCS
            fc.engineRCSTranslation = EngineRCSTranslation;
            fc.engineRCSRotation = EngineRCSRotation;
        }

        private void UpdateBurnAlignmentTolerance()
        {
            if (!hasEC && !hasRCS)
                fc.alignmentToleranceforBurn = 180f;
        }

        public bool HasPropulsion => hasPropulsion;
        #endregion

        #region Utils
        public static Vector3 FromTo(Vessel v1, Vessel v2)
        {
            return v2.transform.position - v1.transform.position;
        }

        public static Vector3 RelVel(Vessel v1, Vessel v2)
        {
            return v1.GetObtVelocity() - v2.GetObtVelocity();
        }

        public static Vector3 AngularAcceleration(Vector3 torque, Vector3 MoI)
        {
            return new Vector3(MoI.x.Equals(0) ? float.MaxValue : torque.x / MoI.x,
                MoI.y.Equals(0) ? float.MaxValue : torque.y / MoI.y,
                MoI.z.Equals(0) ? float.MaxValue : torque.z / MoI.z);
        }

        public static float AngularVelocity(Vessel v, Vessel t, float window)
        {
            Vector3 tv1 = FromTo(v, t);
            Vector3 tv2 = tv1 + window * RelVel(v, t);
            return VectorUtils.Angle(tv1, tv2) / window;
        }

        public static float VesselDistance(Vessel v1, Vessel v2)
        {
            return (v1.transform.position - v2.transform.position).magnitude;
        }

        public static Vector3 Displacement(Vector3 velocity, Vector3 acceleration, float time)
        {
            return velocity * time + 0.5f * acceleration * time * time;
        }

        private void CalculateAngularAcceleration()
        {
            maxAngularAcceleration = AngularAcceleration(availableTorque, vessel.MOI);
            maxAngularAccelerationMag = maxAngularAcceleration.magnitude;

            float angVel = vessel.angularVelocity.magnitude;
            float angAccel = Mathf.Abs(angVel - lastAngVel) / Time.fixedDeltaTime / 3f;
            dynAngAccel *= dynDecayRate; // Decay the highest observed angular acceleration (we want a fairly recent value in case the craft's dynamics have changed).
            dynAngAccel = Mathf.Max(dynAngAccel, angAccel);
            maxAngularAccelerationMag = Mathf.Clamp(((1f - 0.1f) * maxAngularAccelerationMag + 0.1f * dynAngAccel), maxAngularAccelerationMag, dynAngAccel);
            lastAngVel = angVel;
        }

        private void CalculateAvailableTorque(Vessel v)
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (v != vessel) return;

            availableTorque = Vector3.zero;
            var reactionWheels = VesselModuleRegistry.GetModules<ModuleReactionWheel>(v);
            foreach (var wheel in reactionWheels)
            {
                wheel.GetPotentialTorque(out Vector3 pos, out pos);
                availableTorque += pos;
            }
        }

        private void CheckEngineLists(Vessel v)
        {
            if (v != vessel) return;
            if (
                rcsEngines.Any(e => e == null || e.vessel != vessel) ||
                reverseEngines.Any(e => e == null || e.vessel != vessel) ||
                forwardEngines.Any(e => e == null || e.vessel != vessel)
            ) engineListsRequireUpdating = true;
        }

        private float GetMaxAcceleration()
        {
            maxThrust = GetMaxThrust();
            return maxThrust / vessel.GetTotalMass();
        }

        private float GetMaxThrust()
        {
            float thrust = VesselModuleRegistry.GetModuleEngines(vessel).Where(e => e != null && e.EngineIgnited && e.isOperational && !rcsEngines.Contains(e)).Sum(e => e.MaxThrustOutputVac(true));
            thrust += VesselModuleRegistry.GetModules<ModuleRCS>(vessel).Where(rcs => rcs != null && rcs.useThrottle).Sum(rcs => rcs.thrusterPower);
            return thrust;
        }
        private void UpdateEngineLists(bool forceUpdate = false)
        {
            // Update lists of engines that can provide forward, reverse and rcs thrust
            if (!(engineListsRequireUpdating || forceUpdate)) return;
            engineListsRequireUpdating = false;
            forwardEngines.Clear();
            reverseEngines.Clear();
            rcsEngines.Clear();
            float forwardThrust = 0f;
            float reverseThrust = 0f;
            foreach (var engine in VesselModuleRegistry.GetModuleEngines(vessel))
            {
                if (engine.throttleLocked || !engine.allowShutdown || !engine.allowRestart) continue; // Ignore engines that can't be throttled, shutdown, or restart
                if (VesselSpawning.SpawnUtils.IsModularMissilePart(engine.part)) continue; // Ignore modular missile engines.
                if (Vector3.Dot(-engine.thrustTransforms[0].forward, vesselTransform.up) > 0.1f && engine.MaxThrustOutputVac(true) > 0)
                {
                    forwardEngines.Add(engine);
                    forwardThrust += engine.MaxThrustOutputVac(true);
                }
                else if (Vector3.Dot(-engine.thrustTransforms[0].forward, -vesselTransform.up) > 0.1f && engine.MaxThrustOutputVac(true) > 0)
                {
                    reverseEngines.Add(engine);
                    reverseThrust += engine.MaxThrustOutputVac(true);
                    var gimbal = engine.part.FindModuleImplementing<ModuleGimbal>();
                    if (gimbal != null) // Disable gimbal on reverse/RCS engines since they don't work for directions other than forward
                    {
                        gimbal.gimbalRange = 0;
                        gimbal.gimbalLock = true;
                    }
                }
                else if (EngineRCSRotation || EngineRCSTranslation)
                {
                    if (Vector3.Dot(-engine.thrustTransforms[0].forward, vesselTransform.right) > 0.5f ||
                        Vector3.Dot(-engine.thrustTransforms[0].forward, vesselTransform.right) < -0.5f ||
                        Vector3.Dot(-engine.thrustTransforms[0].forward, vesselTransform.forward) > 0.5f ||
                        Vector3.Dot(-engine.thrustTransforms[0].forward, vesselTransform.forward) < -0.5f)
                        rcsEngines.Add(engine); //grab engines pointing sideways. Not grabbing fore/aft engines since while those would impart some torque,
                                                //ship design is generally going to be long and narrow, so torque imparted would be minimal while adding noticable forward/reverse vel
                    if (engine.MaxThrustOutputVac(true) > 0) //just in case someone has mounted jets to the side of their space cruiser for some reason
                    {
                        engine.Activate();
                        if (!engine.independentThrottle) engine.independentThrottle = true; //using independent throttle so these can fire while main engines are off but not shudown
                        engine.thrustPercentage = 0; //activate and set to 0 thrust so they're ready when needed
                        if (engine.independentThrottlePercentage == 0) engine.independentThrottlePercentage = 100;
                    }
                    var gimbal = engine.part.FindModuleImplementing<ModuleGimbal>();
                    if (gimbal != null) // Disable gimbal on reverse/RCS engines since they don't work for directions other than forward
                    {
                        gimbal.gimbalRange = 0;
                        gimbal.gimbalLock = true;
                    }
                    fc.rcsEngines = rcsEngines;
                }
            }
            if (ReverseThrust && reverseEngines.Count == 0) // Disable reverse thrust if no reverse engines available
            {
                ReverseThrust = false;
                reverseForwardThrustRatio = 1f;
                fc.thrustDirection = vesselTransform.up;
                currentForwardThrust = false;
                AlignEnginesWithThrust(true);
            }
            else
                reverseForwardThrustRatio = (forwardThrust == 0) ? 1 : reverseThrust / forwardThrust;
            // Debug.Log($"DEBUG {vessel.vesselName} has forward engines: {string.Join(", ", forwardEngines.Select(e => e.part.partInfo.name))}");
            // Debug.Log($"DEBUG {vessel.vesselName} has reverse engines: {string.Join(", ", reverseEngines.Select(e => e.part.partInfo.name))}");
            // Debug.Log($"DEBUG {vessel.vesselName} has rcs engines: {string.Join(", ", rcsEngines.Select(e => e.part.partInfo.name))}");
        }

        private void AlignEnginesWithThrust(bool forceUpdate = false)
        {
            // Activate or shutdown forward or reverse engines based on fc.thrustDirection
            fc.thrustDirection = (fc.thrustDirection == Vector3.zero) ? fc.attitude : fc.thrustDirection; // Set thrust direction to attitude if not already set

            if (!ReverseThrust && !forceUpdate) return; // Don't continue if reverse thrust is disabled
            bool forwardThrust = UseForwardThrust(fc.thrustDirection); // See if it makes sense to use forward thrust for this thrustDirection

            if (forwardEngines.Count == 0 && reverseEngines.Count > 0 && fc.throttle > 0) // Edge case of no forward engines, but we have reverse engines
            {
                forwardThrust = false;
                fc.attitude = -fc.thrustDirection;
            }

            fc.useReverseThrust = !forwardThrust; // Tell fc what thrust mode to use

            if (forwardThrust == currentForwardThrust) return; // Don't bother toggling engines if desired direction matches current
            if (forwardThrust) // Activate forward engines, shutdown reverse engines
            {
                foreach (var engine in forwardEngines.Where(e => e != null))
                    engine.Activate();
                foreach (var engine in reverseEngines.Where(e => e != null))
                    engine.Shutdown();
                currentForwardThrust = true;
            }
            else // Activate reverse engines, shutdown forward engines
            {
                foreach (var engine in reverseEngines.Where(e => e != null))
                    engine.Activate();
                foreach (var engine in forwardEngines.Where(e => e != null))
                    engine.Shutdown();
                currentForwardThrust = false;
            }
        }

        private bool UseForwardThrust(Vector3 thrustDir)
        {
            if (!ReverseThrust) return true;
            else
                return Vector3.Dot(thrustDir, vesselTransform.up) > -0.3f; // Use forward thrust for directions within ~110 deg of vesselTransform.up
        }

        private float GetConsumptionRate(bool useReverseThrust)
        {
            if (BDArmorySettings.INFINITE_FUEL || CheatOptions.InfinitePropellant) return 0f;
            float consumptionRate = 0.0f;
            List<ModuleEngines> engines = !ReverseThrust ? VesselModuleRegistry.GetModuleEngines(vessel).FindAll(e => (e.EngineIgnited && e.isOperational)) : // No reverse thrust capability, use active engines
                (useReverseThrust ? reverseEngines : forwardEngines); // Reverse thrust capability, use appropriate thrusters to evaluate ISP
            foreach (var engine in engines)
                consumptionRate += Mathf.Lerp(engine.minFuelFlow, engine.maxFuelFlow, 0.01f * engine.thrustPercentage) * engine.flowMultiplier;
            return consumptionRate;
        }

        //Controller Integral
        Vector3 directionIntegral;
        float pitchIntegral;
        float yawIntegral;
        float rollIntegral;
        Vector3 prevTargetDir;
        void AttitudeControl(FlightCtrlState s)
        {
            Vector3 targetDirection = fc.attitude;
            Vector3 currentRoll = -vesselTransform.forward;
            Vector3 rollTarget = currentRoll;
            debugRollTarget = Vector3.zero;
            if (targetVessel != null) // If we have a target, adjust roll orientation relative to target based on rollMode setting
            {
                // Determine toTarget direction for roll command
                Vector3 toTarget;
                if (currentStatusMode == StatusMode.Firing)
                    toTarget = targetDirection;
                else if (RecentFiringSolution(out Vector3 recentSolution)) // If we valid firing solution recently, use it to continue pointing toward target
                    toTarget = recentSolution;
                else
                    toTarget = FromTo(vessel, targetVessel);
                toTarget = toTarget.normalized;

                // Determine roll target
                if (Vector3.Dot(vesselTransform.up, toTarget) < 0.999) // Only roll if we are not aligned with target/firing solution
                {
                    switch (rollMode)
                    {
                        case RollModeTypes.Port_Starboard:
                            {
                                if (Vector3.Dot(toTarget, vesselTransform.right) > 0f)
                                    rollTarget = Vector3.Cross(vesselTransform.up, toTarget).ProjectOnPlanePreNormalized(vesselTransform.up);
                                else
                                    rollTarget = Vector3.Cross(-vesselTransform.up, toTarget).ProjectOnPlanePreNormalized(vesselTransform.up);
                            }
                            break;
                        case RollModeTypes.Dorsal_Ventral:
                            {
                                if (Vector3.Dot(toTarget, vesselTransform.forward) < 0f)
                                    rollTarget = toTarget.ProjectOnPlanePreNormalized(vesselTransform.up);
                                else
                                    rollTarget = -toTarget.ProjectOnPlanePreNormalized(vesselTransform.up);
                            }
                            break;
                        case RollModeTypes.Port:
                            rollTarget = Vector3.Cross(-vesselTransform.up, toTarget).ProjectOnPlanePreNormalized(vesselTransform.up);
                            break;
                        case RollModeTypes.Starboard:
                            rollTarget = Vector3.Cross(vesselTransform.up, toTarget).ProjectOnPlanePreNormalized(vesselTransform.up);
                            break;
                        case RollModeTypes.Dorsal:
                            rollTarget = toTarget.ProjectOnPlanePreNormalized(vesselTransform.up);
                            break;
                        case RollModeTypes.Ventral:
                            rollTarget = -toTarget.ProjectOnPlanePreNormalized(vesselTransform.up);
                            break;
                    }
                    debugRollTarget = rollTarget * 100f;
                }
            }

            Vector3 localTargetDirection = vesselTransform.InverseTransformDirection(targetDirection).normalized;
            float rotationPerFrame = (currentStatusMode == StatusMode.Firing && Vector3.Dot(vesselTransform.up, targetDirection) > 0.94) ? 25f : steerMaxError; // Reduce rotation rate if firing and within ~20 deg of target
            localTargetDirection = Vector3.RotateTowards(Vector3.up, localTargetDirection, rotationPerFrame * Mathf.Deg2Rad, 0);

            float pitchError = VectorUtils.GetAngleOnPlane(localTargetDirection, Vector3.up, Vector3.back);
            float yawError = VectorUtils.GetAngleOnPlane(localTargetDirection, Vector3.up, Vector3.right);
            float rollError = Mathf.Clamp(VectorUtils.GetAngleOnPlane(rollTarget, currentRoll, vesselTransform.right), -steerMaxError, steerMaxError);
            
            Vector3 localAngVel = vessel.angularVelocity;
            Vector3 targetAngVel = Vector3.Cross(prevTargetDir, targetDirection) / Time.fixedDeltaTime;
            Vector3 localTargetAngVel = vesselTransform.InverseTransformVector(targetAngVel);
            localAngVel -= localTargetAngVel;
            prevTargetDir = targetDirection;

            #region PID calculations
            float pitchProportional = 0.005f * steerMult * pitchError;
            float yawProportional = 0.005f * steerMult * yawError;
            float rollProportional = 0.005f * steerMult * rollError;

            float pitchDamping = steerDamping * -localAngVel.x;
            float yawDamping = steerDamping * -localAngVel.z;
            float rollDamping = steerDamping * -localAngVel.y;

            // For the integral, we track the vector of the pitch and yaw in the 2D plane of the vessel's forward pointing vector so that the pitch and yaw components translate between the axes when the vessel rolls.
            directionIntegral = (directionIntegral + (pitchError * -vesselTransform.forward + yawError * vesselTransform.right) * Time.deltaTime).ProjectOnPlanePreNormalized(vesselTransform.up);
            if (directionIntegral.sqrMagnitude > 1f) directionIntegral = directionIntegral.normalized;
            pitchIntegral = steerKiAdjust * Vector3.Dot(directionIntegral, -vesselTransform.forward);
            yawIntegral = steerKiAdjust * Vector3.Dot(directionIntegral, vesselTransform.right);
            rollIntegral = steerKiAdjust * Mathf.Clamp(rollIntegral + rollError * Time.deltaTime, -1f, 1f);

            var steerPitch = pitchProportional + pitchIntegral - pitchDamping;
            var steerYaw = yawProportional + yawIntegral - yawDamping;
            var steerRoll = rollProportional + rollIntegral - rollDamping;

            float maxSteer = 1;

            if (BDArmorySettings.DEBUG_LINES)
            {
                debugTargetPosition = vessel.transform.position + targetDirection * 1000; // The asked for target position's direction
                debugTargetDirection = vessel.transform.position + vesselTransform.TransformDirection(localTargetDirection) * 200; // The actual direction to match the "up" direction of the craft with for pitch (used for PID calculations).
            }

            SetFlightControlState(s,
            Mathf.Clamp(steerPitch, -maxSteer, maxSteer), // pitch
            Mathf.Clamp(steerYaw, -maxSteer, maxSteer), // yaw
            Mathf.Clamp(steerRoll, -maxSteer, maxSteer)); // roll

            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI)
            {
                debugString.AppendLine(string.Format("rollError: {0,7:F4}, pitchError: {1,7:F4}, yawError: {2,7:F4}", rollError, pitchError, yawError));
                debugString.AppendLine(string.Format("Pitch: P: {0,7:F4}, I: {1,7:F4}, D: {2,7:F4}", pitchProportional, pitchIntegral, pitchDamping));
                debugString.AppendLine(string.Format("Yaw: P: {0,7:F4}, I: {1,7:F4}, D: {2,7:F4}", yawProportional, yawIntegral, yawDamping));
                debugString.AppendLine(string.Format("Roll: P: {0,7:F4}, I: {1,7:F4}, D: {2,7:F4}", rollProportional, rollIntegral, rollDamping));
            }
            #endregion
        }
        #endregion

        #region Autopilot helper functions

        public override bool CanEngage()
        {
            return !vessel.LandedOrSplashed && vessel.InOrbit();
        }

        public override bool IsValidFixedWeaponTarget(Vessel target)
        {
            if (!vessel) return false;

            return true;
        }

        #endregion Autopilot helper functions

        #region WingCommander

        Vector3 GetFormationPosition()
        {
            Vector3 formationPosition = commandLeader.GetFormationPosition(commandFollowIndex); // (right, ahead, above)
            return commandLeader.vessel.CoM + Quaternion.LookRotation(commandLeader.vessel.up, upDir) * new Vector3(formationPosition.x, formationPosition.z, formationPosition.y);
        }

        #endregion WingCommander
    }
}