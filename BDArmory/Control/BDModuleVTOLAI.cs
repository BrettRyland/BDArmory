using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

using BDArmory.Extensions;
using BDArmory.Guidances;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Weapons;
using BDArmory.Weapons.Missiles;


namespace BDArmory.Control
{
    public class BDModuleVTOLAI : BDGenericAIBase, IBDAIControl
    {
        public override AIType aiType => AIType.VTOLAI;
        #region Declarations
        Vessel extendingTarget = null;
        public bool orderedToExtend = false;
        Vessel bypassTarget = null;
        Vector3 bypassTargetPos;

        Vector3 targetDirection;
        float targetVelocity; // the forward/reverse velocity the craft should target, not the velocity of its target
        float targetLatVelocity; // the left/right velocity the craft should target, not the velocity of its target
        float targetAltitude; // the altitude the craft should hold, not the altitude of its target
        double targetPitchAngularRate;
        double targetYawAngularRate;

        Vector3d projectedForward;
        Vector3d projectedLeft;
        double yawOffset;
        double pitchOffset;

        enum AimingMode { Off = 0, Yaw = 1, Pitch = 2, Direct = Yaw | Pitch }
        AimingMode aimingMode = AimingMode.Off;

        int collisionDetectionTicker = 0;
        Vector3? dodgeVector;
        float weaveAdjustment = 0;
        float weaveDirection = 1;
        const float weaveLimit = 2.3f;

        Vector3d upDir;

        AIUtils.TraversabilityMatrix pathingMatrix;
        List<Vector3> pathingWaypoints = new List<Vector3>();
        bool leftPath = false;

        bool doReverse = false;

        protected override Vector3d assignedPositionGeo
        {
            get { return intermediatePositionGeo; }
            set
            {
                finalPositionGeo = value;
                leftPath = true;
            }
        }

        Vector3d finalPositionGeo;
        Vector3d intermediatePositionGeo;
        public override Vector3d commandGPS => finalPositionGeo;

        private BDVTOLSpeedControl altitudeControl; // Throttle is used to control altitude in most quadcopter control systems (position error feeds pitch/roll control), this works decently well for helicopters in BDA

        // Terrain avoidance and below minimum altitude globals.
        bool belowMinAltitude; // True when below minAltitude or avoiding terrain.
        bool avoidingTerrain = false; // True when avoiding terrain.
        bool initialTakeOff = true; // False after the initial take-off
        Vector3 terrainAlertNormal; // Approximate surface normal at the terrain intercept.

        // values currently hard-coded since VTOL AI is adapted from surface AI, but should be removed/changed as AI behavior is improved
        public string SurfaceTypeName = "Amphibious"; // hard code this for the moment until we have something better
        public bool PoweredSteering = true;
        public float MaxDrift = 180;

        public AIUtils.VehicleMovementType SurfaceType
            => (AIUtils.VehicleMovementType)Enum.Parse(typeof(AIUtils.VehicleMovementType), SurfaceTypeName);

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerPower"),//Steer Factor
            UI_FloatRange(minValue = 0.2f, maxValue = 20f, stepIncrement = .1f, scene = UI_Scene.All)]
        public float steerMult = 6;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerKi"), //Steer Ki
            UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float steerKiAdjust = 0.4f;

        // We can't really get angular acceleration from KSP (at least not without a ton of noise)
        /*[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerDamping"),//Steer Damping
            UI_FloatRange(minValue = 0.1f, maxValue = 10f, stepIncrement = .1f, scene = UI_Scene.All)]
        public float steerDamping = 3;*/

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_DragCompMult"),// Drag Compensation
            UI_FloatRange(minValue = 0.0f, maxValue = 20f, stepIncrement = .1f, scene = UI_Scene.All)]
        public float DragCompensationMult = 6;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerVelErrMult"),// Proportional control for velocity
            UI_FloatRange(minValue = 0.0f, maxValue = 20f, stepIncrement = .1f, scene = UI_Scene.All)]
        public float steerVelErrMult = 6;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_TiltAccelMult"),//Tilt Accel Mult
            UI_FloatRange(minValue = 0.0f, maxValue = 10f, stepIncrement = .1f, scene = UI_Scene.All)]
        public float tiltAccelMult = 3;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerAngVelMult"),//Steer Ang. Vel Mult.
            UI_FloatRange(minValue = 0.05f, maxValue = 5f, stepIncrement = .05f, scene = UI_Scene.All)]
        public float steerAngVelMult = 1;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerMaxAngVel"),//Steer Max. Ang. Vel.
            UI_FloatRange(minValue = 5f, maxValue = 180f, stepIncrement = 5f, scene = UI_Scene.All)]
        public float steerMaxAngVel = 60f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerRollFactor"),//Steer Roll Factor
            UI_FloatRange(minValue = 0.0f, maxValue = 20f, stepIncrement = .1f, scene = UI_Scene.All)]
        public float steerRollFactor = 3;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_DefaultAltitude"), //Default Alt.
            UI_FloatRange(minValue = 25f, maxValue = 5000f, stepIncrement = 25f, scene = UI_Scene.All)]
        public float defaultAltitude = 300;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_CombatAltitude"), //Combat Alt.
            UI_FloatRange(minValue = 25f, maxValue = 5000f, stepIncrement = 50f, scene = UI_Scene.All)]
        public float CombatAltitude = 150;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_BombingAltitude"), //bombing Altitude
            UI_FloatRange(minValue = 100f, maxValue = 2000, stepIncrement = 10f, scene = UI_Scene.All)]
        public float bombingAltitude = 300;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MinAltitude"), //Min Altitude
            UI_FloatRange(minValue = 10f, maxValue = 1000, stepIncrement = 10f, scene = UI_Scene.All)]
        public float minAltitude = 100f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MaxSpeed"),//Max speed
            UI_FloatRange(minValue = 5f, maxValue = 200f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float MaxSpeed = 80;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_CombatSpeed"),//Combat speed
            UI_FloatRange(minValue = 5f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float CombatSpeed = 40;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ControlSpeed"),//Control speed
            UI_FloatRange(minValue = 5f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float ControlSpeed = 30;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_HoverPitchAngle"),//Hover pitch angle
            UI_FloatRange(minValue = -45f, maxValue = 45f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float HoverPitchAngle = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MaxTiltAngle"),//Max tilt angle
            UI_FloatRange(minValue = 1f, maxValue = 90f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float MaxTiltAngle = 30f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MaxReverseSideslipAngle"),// Max Bank angle
            UI_FloatRange(minValue = 0f, maxValue = 90f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float MaxReverseSideslipAngle = 30;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_BrakingAccel"),// Proportional control for velocity
            UI_FloatRange(minValue = 0.2f, maxValue = 20f, stepIncrement = .1f, scene = UI_Scene.All)]
        public float BrakingAccel = 3f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_BrakingLinearVel"),// Swap to linear control
            UI_FloatRange(minValue = 5f, maxValue = 100f, stepIncrement = 5f, scene = UI_Scene.All)]
        public float BrakingLinearVel = 60f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_WeaveFactor"),//Weave Factor
            UI_FloatRange(minValue = 0f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float WeaveFactor = 6.5f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MinEngagementRange"),//Min engagement range
            UI_FloatRange(minValue = 0f, maxValue = 6000f, stepIncrement = 100f, scene = UI_Scene.All)]
        public float MinEngagementRange = 500;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MaxEngagementRange"),//Max engagement range
            UI_FloatRange(minValue = 500f, maxValue = 8000f, stepIncrement = 100f, scene = UI_Scene.All)]
        public float MaxEngagementRange = 4000;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MaintainEngagementRange"),//Maintain min Range
            UI_Toggle(enabledText = "#LOC_BDArmory_true", disabledText = "#LOC_BDArmory_false")]//true; false
        public bool maintainMinRange = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_BroadsideAttack"),//Attack vector
            UI_Toggle(enabledText = "#LOC_BDArmory_AI_BroadsideAttack_enabledText", disabledText = "#LOC_BDArmory_AI_BroadsideAttack_disabledText")]//Broadside--Bow
        public bool BroadsideAttack = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ManeuverRCS"),//RCS active
            UI_Toggle(enabledText = "#LOC_BDArmory_AI_ManeuverRCS_enabledText", disabledText = "#LOC_BDArmory_AI_ManeuverRCS_disabledText", scene = UI_Scene.All),]//Maneuvers--Combat
        public bool ManeuverRCS = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MinObstacleMass"),//Min obstacle mass
            UI_FloatSemiLogRange(minValue = 0.1f, maxValue = 100f, sigFig = 2, withZero = true, scene = UI_Scene.All)]
        public float AvoidMass = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_PreferredBroadsideDirection", advancedTweakable = true),//Preferred broadside direction
            UI_ChooseOption(options = new string[3] { "Port", "Either", "Starboard" }, scene = UI_Scene.All),]
        public string OrbitDirectionName = "Either";
        public readonly string[] orbitDirections = new string[3] { "Port", "Either", "Starboard" };

        [KSPField(isPersistant = true)]
        int sideSlipDirection = 0;

        #region EvadeExtend
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

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_EvasionMinRangeThreshold", advancedTweakable = true, // Evasion Min Range Threshold
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatSemiLogRange(minValue = 10f, maxValue = 10000f, sigFig = 1, withZero = true)]
        public float evasionMinRangeThreshold = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_EvasionIgnoreMyTargetTargetingMe", advancedTweakable = true,//Ignore my target targeting me
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All),]
        public bool evasionIgnoreMyTargetTargetingMe = false;
        #endregion

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_GoesUp", advancedTweakable = true),//Goes up to 
            UI_Toggle(enabledText = "#LOC_BDArmory_AI_GoesUp_enabledText", disabledText = "#LOC_BDArmory_AI_GoesUp_disabledText", scene = UI_Scene.All),]//eleven--ten
        bool upToEleven = false;
        public bool UpToEleven { get { return upToEleven; } set { if (upToEleven != value) { upToEleven = value; TurnItUpToEleven(); } } }

        const float AttackAngleAtMaxRange = 30f;

        Dictionary<string, float> altMaxValues = new Dictionary<string, float>
        {
            { nameof(defaultAltitude), 100000f },
            { nameof(CombatAltitude), 100000f },
            { nameof(minAltitude), 100000f },
            { nameof(steerMult), 200f },
            { nameof(steerKiAdjust), 20f },
            //{ nameof(steerDamping), 100f },
            { nameof(DragCompensationMult), 200f },
            { nameof(steerVelErrMult), 200f },
            { nameof(tiltAccelMult), 200f },
            { nameof(steerAngVelMult), 50f },
            { nameof(steerMaxAngVel), 360f },
            { nameof(steerRollFactor), 200f },
            { nameof(MaxTiltAngle), 90f },
            { nameof(CombatSpeed), 300f },
            { nameof(MaxSpeed), 400f },
            { nameof(ControlSpeed), 200f },
            { nameof(BrakingLinearVel), 150f },
            { nameof(minEvasionTime), 10f },
            { nameof(evasionThreshold), 300f },
            { nameof(evasionTimeThreshold), 30f },
            { nameof(MinEngagementRange), 20000f },
            { nameof(MaxEngagementRange), 30000f },
        };
        Dictionary<string, (float, float, float)> altSemiLogValues = new Dictionary<string, (float, float, float)> {
            { nameof(AvoidMass), (10f, 1000000f, 2f) },
        };

        #endregion Declarations

        #region RMB info in editor

        // <color={XKCDColors.HexFormat.Lime}>Yes</color>
        public override string GetInfo()
        {
            // known bug - the game caches the RMB info, changing the variable after checking the info
            // does not update the info. :( No idea how to force an update.
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<b>Available settings</b>:");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Steer Factor</color> - higher will make the AI apply more control input for the same desired rotation");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Steer Ki</color> - higher will make the AI apply control trim faster");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Steer Damping</color> - higher will make the AI apply more control input when it wants to stop rotation");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Steer Vel. Mult.</color> - higher will make the AI fly at a higher angle for a given target airspeed");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Vel. Err Mult</color> - higher will make the AI angle more aggressively to accelerate");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Steer Accel Damping</color> - higher will make the AI more aggressively counteract acceleration");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Default Alt.</color> - AI will fly at this altitude outside of combat");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Combat Altitude</color> - AI will fly at this altitude during combat");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Min Altitude</color> - below this altitude AI will prioritize gaining altitude over combat");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Max Speed</color> - the maximum combat speed");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Combat Speed</color> - the default speed at which it is safe to maneuver");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Hover pitch angle</color> - pitch angle at which the AI will hover");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Max pitch angle</color> - the limit on pitch when moving");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Bank angle</color> - the limit on roll when turning, positive rolls into turns");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Braking accel</color> - the acceleration which the AI will try come to stop at");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Braking Linear Vel</color> - the speed at which the AI will decrease the deceleration for fine control at");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Attack vector</color> - does the vessel attack from the front or the sides");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Min engagement range</color> - AI will try to move away from oponents if closer than this range");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Max engagement range</color> - AI will prioritize getting closer over attacking when beyond this range");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- RCS active</color> - Use RCS during any maneuvers, or only in combat ");
            if (GameSettings.ADVANCED_TWEAKABLES)
            {
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Goes up to</color> - Increases variable limits, no direct effect on behaviour");
            }

            return sb.ToString();
        }

        #endregion RMB info in editor

        #region events

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (!(HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor)) return;

            SetChooseOptions();
            SetOnUpToElevenChanged();
        }

        public override bool ActivatePilot()
        {
            if (!base.ActivatePilot()) return false;
            //originalMaxSpeed = MaxSpeed;
            pathingMatrix = new AIUtils.TraversabilityMatrix();

            if (!altitudeControl)
            {
                altitudeControl = gameObject.AddComponent<BDVTOLSpeedControl>();
                altitudeControl.vessel = vessel;
            }
            altitudeControl.Activate();

            if (initialTakeOff && !vessel.LandedOrSplashed) // In case we activate pilot after taking off manually.
            {
                initialTakeOff = false;
                TakingOff = false;
            }

            if (BroadsideAttack && sideSlipDirection == 0)
            {
                SetBroadsideDirection(OrbitDirectionName);
            }

            leftPath = true;
            extendingTarget = null;
            bypassTarget = null;
            collisionDetectionTicker = 6;
            return true;
        }

        public override void DeactivatePilot()
        {
            base.DeactivatePilot();

            if (altitudeControl)
                altitudeControl.Deactivate();
        }

        public void SetChooseOptions()
        {
            UI_ChooseOption broadside = (UI_ChooseOption)(HighLogic.LoadedSceneIsFlight ? Fields[nameof(OrbitDirectionName)].uiControlFlight : Fields[nameof(OrbitDirectionName)].uiControlEditor);
            broadside.onFieldChanged = ChooseOptionsUpdated;
            // UI_ChooseOption surface = (UI_ChooseOption)(HighLogic.LoadedSceneIsFlight ? Fields[nameof(SurfaceTypeName)].uiControlFlight : Fields[nameof(SurfaceTypeName)].uiControlEditor);
            // surface.onFieldChanged = ChooseOptionsUpdated;
        }

        public void ChooseOptionsUpdated(BaseField field, object obj)
        {
            this.UpdateChooseOptionPAW(field, obj);
            if (BDArmoryAIGUI.Instance != null && BDArmoryAIGUI.Instance.ActiveAI == this)
            {
                BDArmoryAIGUI.Instance.SetChooseOptionSliders();
            }
        }

        public void SetBroadsideDirection(string direction)
        {
            if (!orbitDirections.Contains(direction)) return;
            OrbitDirectionName = direction;
            sideSlipDirection = orbitDirections.IndexOf(OrbitDirectionName) - 1;
            if (sideSlipDirection == 0)
                sideSlipDirection = UnityEngine.Random.value > 0.5f ? 1 : -1;
        }

        void SetOnUpToElevenChanged()
        {
            var field = (UI_Toggle)(HighLogic.LoadedSceneIsFlight ? Fields[nameof(upToEleven)].uiControlFlight : Fields[nameof(upToEleven)].uiControlEditor);
            field.onFieldChanged = TurnItUpToEleven; // Only triggered on UI interaction.
            if (upToEleven) TurnItUpToEleven(); // The initially loaded values are not the alternate ones.
        }

        void TurnItUpToEleven(BaseField _field = null, object _obj = null)
        {
            using var s = altMaxValues.Keys.ToList().GetEnumerator();
            while (s.MoveNext())
            {
                UI_FloatRange euic = (UI_FloatRange)(HighLogic.LoadedSceneIsFlight ? Fields[s.Current].uiControlFlight : Fields[s.Current].uiControlEditor);
                (altMaxValues[s.Current], euic.maxValue) = (euic.maxValue, altMaxValues[s.Current]);
                StartCoroutine(SetVar(s.Current, (float)typeof(BDModuleVTOLAI).GetField(s.Current).GetValue(this))); // change the value back to what it is now after fixed update, because changing the max value will clamp it down
            }
            foreach (var fieldName in altSemiLogValues.Keys.ToList())
            {
                var field = (UI_FloatSemiLogRange)(HighLogic.LoadedSceneIsFlight ? Fields[fieldName].uiControlFlight : Fields[fieldName].uiControlEditor);
                var temp = (field.minValue, field.maxValue, field.sigFig);
                var altValues = altSemiLogValues[fieldName];
                if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModuleVTOLAI]: Swapping semiLog limits of {fieldName} from {temp} to {altValues}");
                field.UpdateLimits(altValues.Item1, altValues.Item2, altValues.Item3);
                altSemiLogValues[fieldName] = temp;
            }
            BDArmoryAIGUI.Instance.RefreshInputFields();
        }

        IEnumerator SetVar(string name, float value)
        {
            yield return new WaitForFixedUpdate();
            typeof(BDModuleVTOLAI).GetField(name).SetValue(this, value);
        }

        protected override void OnGUI()
        {
            base.OnGUI();

            if (!HighLogic.LoadedSceneIsFlight) return;
            if (!pilotEnabled || !vessel || !vessel.isActiveVessel) return;

            if (!BDArmorySettings.DEBUG_LINES) return;
            if (command == PilotCommands.Follow)
            {
                GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, assignedPositionWorld, 2, Color.red);
            }

            //GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + targetDirection * 10f, 2, Color.blue);
            //GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position + (0.05f * vesselTransform.right), vesselTransform.position + (0.05f * vesselTransform.right), 2, Color.green);

            // Vel vectors
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + Vector3.Project(vessel.Velocity(), projectedForward).normalized * 10f, 2, Color.cyan); //forward/rev
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + Vector3.Project(vessel.Velocity(), projectedLeft).normalized * 10f, 3, Color.yellow); //lateral
            /*GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + 20f * projectedForward, 3, Color.green);
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + 20f * projectedLeft, 3, Color.green);
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + 20f * upDir, 3, Color.green);*/

            // Replaces rollTarget
            GUIUtils.DrawLineBetweenWorldPositions(vessel.CoM, vessel.CoM + alignVector * 10f, 5, Color.blue);

            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + targetDirection * 10f, 5, Color.red);
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + vesselTransform.up * 1000, 3, Color.white);
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + -vesselTransform.forward * 100, 3, Color.yellow);
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + vessel.Velocity().normalized * 100, 3, Color.magenta);

            pathingMatrix.DrawDebug(vessel.CoM, pathingWaypoints);
        }

        #endregion events

        #region Actual AI Pilot

        protected override void AutoPilot(FlightCtrlState s)
        {
            if (!vessel.Autopilot.Enabled)
                vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);

            targetVelocity = 0;
            targetLatVelocity = 0;
            targetPitchAngularRate = 0;
            targetYawAngularRate = 0;
            targetDirection = vesselTransform.up;
            targetAltitude = defaultAltitude;
            aimingMode = AimingMode.Off;
            upDir = vessel.upAxis;
            projectedForward = ((Vector3d)vesselTransform.up).ProjectOnPlanePreNormalized(upDir).normalized;
            projectedLeft = Vector3d.Cross(projectedForward, upDir); // For some reason, this gives left instead of right, Unity?
            pitchOffset = 0f;
            yawOffset = 0f;
            //if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.VTOLAI] forward: {(Vector3)projectedForward}, left: {(Vector3)projectedLeft}, up: {(Vector3)upDir}");
            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine("");
            if (IsRunningWaypoints) UpdateWaypoint(); // Update the waypoint state.
            if (initialTakeOff)
            {
                Takeoff();
            }
            // pilot logic figures out what we're supposed to be doing, and sets the base state
            PilotLogic(); // TODO: pitch based on targetVelocity, roll always 0
            // situational awareness modifies the base as best as it can (evasive mainly)
            Tactical();
            CheckLandingGear();
            //CommandAttitude(); // Determine pitch/roll/yaw for movement
            AttitudeControl(s); // move according to commanded movement
            AdjustThrottle(targetVelocity); // set throttle according to our targets and movement
        }

        void PilotLogic() // Surface AI-based with byass target disabled
        {
            // check for belowMinAlt
            belowMinAltitude = (float)vessel.radarAltitude < minAltitude;

            // VTOL ram capability? have it match alt with target flier and engines ahead full?
            // ramming ground targets would be iffy, though. 

            // check for collisions, but not every frame
            if (collisionDetectionTicker == 0)
            {
                collisionDetectionTicker = 20;
                float predictMult = Mathf.Clamp(10 / MaxDrift, 1, 10);

                dodgeVector = null;

                using (var vs = BDATargetManager.LoadedVessels.GetEnumerator())
                    while (vs.MoveNext())
                    {
                        if (vs.Current == null || vs.Current == vessel || vs.Current.totalMass < AvoidMass) continue;
                        if (!VesselModuleRegistry.IgnoredVesselTypes.Contains(vs.Current.vesselType))
                        {
                            var ibdaiControl = vs.Current.ActiveController().AI;
                            if (!vs.Current.LandedOrSplashed || (ibdaiControl != null && ibdaiControl.commandLeader != null && ibdaiControl.commandLeader.vessel == vessel))
                                continue;
                        }
                        dodgeVector = PredictCollisionWithVessel(vs.Current, 5f * predictMult, 0.5f);
                        if (dodgeVector != null) break;
                    }
            }
            else
                collisionDetectionTicker--;

            // avoid collisions if any are found
            if (dodgeVector != null)
            {
                targetVelocity = PoweredSteering ? MaxSpeed : CombatSpeed;
                targetDirection = (Vector3)dodgeVector;
                SetStatus($"Avoiding Collision");
                leftPath = true;
                return;
            }

            // check for enemy targets and engage
            // not checking for guard mode, because if guard mode is off now you can select a target manually and if it is of opposing team, the AI will try to engage while you can man the turrets
            var weaponManager = WeaponManager;
            if (weaponManager && targetVessel != null && !BDArmorySettings.PEACE_MODE)
            {
                leftPath = true;

                Vector3d vecToTarget = targetVessel.CoMD - vessel.CoMD;
                (double distanceD, Vector3d normVecToTarget) = (vecToTarget).MagNorm();
                float distance = (float)distanceD;
                /*// lead the target a bit, where 1km/s is a ballpark estimate of the average bullet velocity
                float shotSpeed = 1000f;
                if ((weaponManager != null ? weaponManager.selectedWeapon : null) is ModuleWeapon wep)
                    shotSpeed = wep.bulletVelocity;
                vecToTarget = targetVessel.PredictPositionV3D(distanceD / shotSpeed) - vessel.CoMD;*/

                if (BroadsideAttack)
                {
                    Vector3d sideVector = Vector3d.Cross(vecToTarget, upDir); //find a vector perpendicular to direction to target
                    if (collisionDetectionTicker == 10
                            && !pathingMatrix.TraversableStraightLine(
                                    VectorUtils.WorldPositionToGeoCoords(vessel.CoM, vessel.mainBody),
                                    VectorUtils.WorldPositionToGeoCoords(vessel.PredictPosition(10), vessel.mainBody),
                                    vessel.mainBody, SurfaceType, MaxTiltAngle, AvoidMass))
                        sideSlipDirection = -Math.Sign(Vector3.Dot(vesselTransform.up, sideVector)); // switch sides if we're running ashore
                    sideVector *= sideSlipDirection;

                    float sidestep = distance >= MaxEngagementRange ? Mathf.Clamp01((MaxEngagementRange - distance) / (CombatSpeed * Mathf.Clamp(90 / MaxDrift, 0, 10)) + 1) * AttackAngleAtMaxRange / 90 : // direct to target to attackAngle degrees if over maxrange
                        (distance <= MinEngagementRange ? 1.5f - distance / (MinEngagementRange * 2) : // 90 to 135 degrees if closer than minrange
                        (MaxEngagementRange - distance) / (MaxEngagementRange - MinEngagementRange) * (1 - AttackAngleAtMaxRange / 90) + AttackAngleAtMaxRange / 90); // attackAngle to 90 degrees from maxrange to minrange
                    targetDirection = Vector3.LerpUnclamped(vecToTarget.normalized, sideVector.normalized, sidestep); // interpolate between the side vector and target direction vector based on sidestep
                    targetVelocity = MaxSpeed;
                    targetAltitude = CombatAltitude;
                    if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine($"Broadside attack angle {sidestep}");
                }
                else // just point at target and go
                {
                    targetAltitude = CombatAltitude;
                    if ((!maintainMinRange || orderedToExtend) && (((targetVessel.horizontalSrfSpeed < 10) || Vector3.Dot(targetVessel.srf_vel_direction.ProjectOnPlanePreNormalized(upDir), targetDirection) < 0 || orderedToExtend) //if target is stationary or we're facing in opposite directions
                        && (distance < MinEngagementRange || (distance < (MinEngagementRange * 3 + MaxEngagementRange) / 4 //and too close together
                        && extendingTarget != null && targetVessel != null && extendingTarget == targetVessel))))
                    {
                        extendingTarget = targetVessel;
                        // not sure if this part is very smart, potential for improvement
                        if (distance > Mathf.Max(MaxEngagementRange / 2, 2000)) orderedToExtend = false;
                        if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine($"Extending: ({distance:F2}/{Mathf.Max(MaxEngagementRange / 2, 2000)})");
                        targetDirection = -vecToTarget + vessel.srf_vel_direction; //extend perpendicular to target to maintain some forward vel
                        targetVelocity = MaxSpeed;
                        targetAltitude = CombatAltitude;
                        SetStatus($"Extending");
                        return;
                    }
                    else
                    {
                        extendingTarget = null;
                        targetDirection = vecToTarget.ProjectOnPlanePreNormalizedV3F(upDir);
                        if (Vector3.Dot(targetDirection, vesselTransform.up) < 0)
                            targetVelocity = PoweredSteering ? MaxSpeed : 0; // if facing away from target
                        else if (distance >= MaxEngagementRange || distance <= MinEngagementRange * 1.25f)
                        {
                            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine($"Adjusting Range and speed");
                            if (distance >= MaxEngagementRange)
                            {
                                targetVelocity = MaxSpeed;//out of engagement range, engines ahead full
                                SetStatus($"Closing Fast");
                                return;
                            }
                            //coming within minEngagement range
                            if (maintainMinRange) //for some reason ignored if both vessel and targetvessel using Mk2roverCans?
                            {
                                if (distance <= MinEngagementRange) //rolled to a stop inside minRange/target has encroached
                                {
                                    double forwardVelOffset = Vector3d.Dot(targetVessel.Velocity(), targetDirection);
                                    // If we're able to reverse, reverse
                                    if (forwardVelOffset > -ControlSpeed)
                                    {
                                        doReverse = true;
                                        targetVelocity = -ControlSpeed;
                                        SetStatus($"Reversing");
                                    }
                                    else
                                    {
                                        extendingTarget = targetVessel;
                                        orderedToExtend = true;
                                        // not sure if this part is very smart, potential for improvement
                                        if (distance > Mathf.Max(MaxEngagementRange / 2, 2000)) orderedToExtend = false;
                                        if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine($"Extending: ({distance:F2}/{Mathf.Max(MaxEngagementRange / 2, 2000)})");
                                        targetDirection = -vecToTarget;
                                        targetVelocity = MaxSpeed;
                                        targetAltitude = CombatAltitude;
                                        SetStatus($"Extending");
                                    }
                                    return;
                                }
                                if (targetVessel.srfSpeed < 10)
                                {
                                    targetVelocity = 0f;
                                    SetStatus($"Braking");
                                }

                                targetVelocity = Math.Min((float)targetVessel.srfSpeed, CombatSpeed);
                            }
                            else
                            {
                                targetVelocity = MaxSpeed;
                                if (weaponManager != null && weaponManager.selectedWeapon != null && (
                                    weaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.Bomb ||
                                    weaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.SLW
                                ))
                                {
                                    extendingTarget = targetVessel;
                                    orderedToExtend = true;
                                }
                            }
                        }

                        SetStatus($"Closing");
                        targetVelocity = !maintainMinRange ? MaxSpeed : CombatSpeed / 10 + (MaxSpeed - CombatSpeed / 10) * (distance - MinEngagementRange) / (MaxEngagementRange - MinEngagementRange); //slow down if inside engagement range to extend shooting opportunities
                        
                        if (weaponManager != null && weaponManager.selectedWeapon != null)
                        {
                            switch (weaponManager.selectedWeapon.GetWeaponClass())
                            {
                                case WeaponClasses.Missile:
                                    MissileBase missile = weaponManager.CurrentMissile;
                                    orderedToExtend = false;
                                    if (missile != null)
                                    {
                                        if (missile.TargetingMode == MissileBase.TargetingModes.Heat && !weaponManager.heatTarget.exists)
                                        {
                                            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine($"Acq. Heat Lock");
                                            aimingMode = AimingMode.Direct;
                                            targetDirection = ((missile.uncagedLock && (weaponManager.hasHMD || weaponManager._radarsEnabled || weaponManager._irstsEnabled)) ? MissileGuidance.GetAirToAirFireSolution(missile, targetVessel) : targetVessel.CoM) - vessel.CoM;
                                        }
                                        else
                                        {
                                            bool withinRange = Vector3d.SqrMagnitude(targetVessel.CoMD - vessel.CoMD) < (missile.engageRangeMax * missile.engageRangeMax);
                                            if (!weaponManager.GetLaunchAuthorization(targetVessel, weaponManager, missile, weaponManager.unguidedWeapon) && withinRange)
                                            {
                                                aimingMode = AimingMode.Direct;
                                                targetDirection = MissileGuidance.GetAirToAirFireSolution(missile, targetVessel) - vessel.CoM;
                                                SetStatus($"Prep. for Msl.");
                                            }

                                            if (maintainMinRange && withinRange)
                                            {
                                                // We're presumably able to engage at this range, so unless we've got a launch auth issue,
                                                // we should try to keep our distance if maintainMinRange, note that aimingMode will override
                                                // this so there's no concerns about conflicts there
                                                targetVelocity = 0f;
                                            }
                                        }
                                        if (aimingMode != AimingMode.Off)
                                        {
                                            Vector3d missileForward = missile.GetForwardTransform();
                                            Vector3d forwardPitch = ((Vector3d)vesselTransform.up).ProjectOnPlanePreNormalized(projectedLeft);
                                            Vector3d upPitch = Vector3d.Cross(projectedLeft, forwardPitch);
                                            pitchOffset = VectorUtils.GetAngleOnPlaneD(missileForward, forwardPitch, upPitch);
                                            yawOffset = VectorUtils.GetAngleOnPlaneD(missileForward, projectedForward, projectedLeft);
                                        }
                                    }
                                    break;
                                case WeaponClasses.Bomb:
                                    {
                                        MissileBase bomb = weaponManager.CurrentMissile;
                                        targetAltitude = bombingAltitude;

                                        SetStatus($"Bomb Run");
                                        targetDirection = (AIUtils.PredictPosition(targetVessel, weaponManager.bombAirTime) - vessel.CoM).ProjectOnPlanePreNormalized(upDir);
                                        aimingMode = AimingMode.Yaw;
                                    }
                                    break;
                                case WeaponClasses.SLW:
                                    {
                                        MissileBase torpedo = weaponManager.CurrentMissile;
                                        if (torpedo != null)
                                        {
                                            targetAltitude = CombatAltitude < 100 ? CombatAltitude : 100; //100 vs 200 since helis are going to be going much slower, don't want torps bellyflopping; if we ever do sonobuoys or similar this should probably really be something like 15m, not 100
                                            if (distance < torpedo.engageRangeMax + (float)(vessel.srf_velocity - targetVessel.srf_velocity).magnitude)
                                            {
                                                if (weaponManager.firedMissiles < weaponManager.maxMissilesOnTarget)
                                                    targetVelocity = CombatSpeed; //slow to drop speed

                                                SetStatus($"Torpedo Run");

                                                aimingMode = AimingMode.Yaw;
                                                targetDirection = (MissileGuidance.GetAirToAirFireSolution(torpedo, targetVessel) - vessel.CoM).ProjectOnPlanePreNormalized(upDir);
                                            }
                                            if (weaponManager.firedMissiles >= weaponManager.maxMissilesOnTarget)
                                            {
                                                targetAltitude = bombingAltitude;
                                                targetVelocity = MaxSpeed; //torps away, get out of there
                                                orderedToExtend = true;
                                            }
                                        }
                                    }
                                    break;
                                case WeaponClasses.Gun:
                                case WeaponClasses.Rocket:
                                case WeaponClasses.DefenseLaser:
                                    var gun = (ModuleWeapon)weaponManager.selectedWeapon;
                                    orderedToExtend = false;
                                    if (gun != null)
                                    {
                                        if ((!gun.turret || (gun.yawRange == 0 || gun.maxPitch == gun.minPitch)) && gun.FiringSolutionVector != null)
                                        {
                                            Vector3d gunForward = Vector3d.zero;
                                            Vector3d targetVel = targetVessel.Velocity();
                                            Vector3d targetAngularVel = Vector3d.Cross(vecToTarget, targetVel - vessel.Velocity()) / vecToTarget.sqrMagnitude;
                                            if (gun.yawRange == 0)
                                            {
                                                gunForward = gun.fireTransforms[0].forward;
                                                yawOffset = -VectorUtils.GetAngleOnPlaneD(gunForward, vesselTransform.up, vesselTransform.right);
                                                aimingMode |= AimingMode.Yaw;
                                                targetYawAngularRate = Vector3d.Dot(targetAngularVel, vesselTransform.forward);
                                            }
                                            bool pitchAim = false;
                                            double forwardVelOffset = Vector3d.Dot(targetVel, targetDirection);
                                            if (gun.maxPitch == gun.minPitch)
                                            {
                                                if ((aimingMode & AimingMode.Yaw) == 0)
                                                {
                                                    gunForward = gun.fireTransforms[0].forward;
                                                }
                                                double meanRange = 0.5d * ((maintainMinRange ? Math.Max(gun.engageRangeMin, MinEngagementRange) : gun.engageRangeMin) + gun.engageRangeMax);
                                                if ((forwardVel - forwardVelOffset < -CombatSpeed / 10 && distance > meanRange) || (forwardVel - forwardVelOffset < CombatSpeed / 10 && distance > gun.engageRangeMax))
                                                {
                                                    SetStatus($"Closing");
                                                    // We're already ordering the helicopter to approach the target, so we just need to avoid setting the aimingMode
                                                    return;
                                                }
                                                //Vector3d forwardPitch = ((Vector3d)vesselTransform.up).ProjectOnPlanePreNormalized(projectedLeft);
                                                //Vector3d upPitch = Vector3d.Cross(projectedLeft, forwardPitch);
                                                pitchOffset = VectorUtils.GetAngleOnPlaneD(gunForward, vesselTransform.up, -vesselTransform.forward);
                                                aimingMode |= AimingMode.Pitch;
                                                targetPitchAngularRate = -Vector3d.Dot(targetAngularVel, vesselTransform.right);
                                            }
                                            Vector3d gunSolution = (Vector3)gun.FiringSolutionVector;
                                            Vector3d projectedSolution = gunSolution.ProjectOnPlanePreNormalized(projectedLeft);
                                            if (!pitchAim || VectorUtils.Angle(projectedForward, projectedSolution) < MaxTiltAngle)
                                            {
                                                targetDirection = gunSolution;
                                                if (maintainMinRange && distance < gun.engageRangeMax)
                                                {
                                                    // We should try to keep our range if we're able to comfortably engage at this range
                                                    targetVelocity = (float)forwardVelOffset;
                                                }
                                            }
                                            else if (pitchAim && Vector3d.Dot(projectedSolution, upDir) < 0f && maintainMinRange)
                                            {
                                                // If the issue is the angle is too low, back away
                                                aimingMode &= ~AimingMode.Pitch;
                                                yawOffset = 0f;
                                                pitchOffset = 0f;
                                                targetYawAngularRate = 0f;
                                                targetPitchAngularRate = 0f;
                                                // If we're able to reverse, reverse
                                                if (forwardVelOffset > -ControlSpeed)
                                                {
                                                    doReverse = true;
                                                    targetVelocity = -ControlSpeed;
                                                    SetStatus($"Reversing");
                                                }
                                                else
                                                {
                                                    extendingTarget = targetVessel;
                                                    orderedToExtend = true;
                                                    // not sure if this part is very smart, potential for improvement
                                                    if (distance > Mathf.Max(MaxEngagementRange / 2, 2000)) orderedToExtend = false;
                                                    if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine($"Extending: ({distance:F2}/{Mathf.Max(MaxEngagementRange / 2, 2000)})");
                                                    targetDirection = -vecToTarget;
                                                    targetVelocity = MaxSpeed;
                                                    targetAltitude = CombatAltitude;
                                                    SetStatus($"Extending");
                                                }
                                                return;
                                            }
                                            SetStatus($"Engaging target");
                                            // TODO: this will get stuck in a loop for fixed guns, we need a pseudo-extend here, but also if other weapons are suitable...
                                        }
                                        else
                                        {
                                            if (maintainMinRange && distance < gun.engageRangeMax)
                                            {
                                                // We should try to keep our range if we're able to comfortably engage at this range
                                                targetVelocity = 0f;
                                            }
                                        }
                                    }
                                    break;
                            }
                            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine($"Target combat alt: {targetAltitude}");
                            if ((aimingMode & AimingMode.Yaw) > 0)
                            {
                                targetLatVelocity = 0f;
                                //targetLatVelocity = Vector3.Dot(vessel.Velocity(), projectedLeft) * 4; //Zero out sideslip if aiming so torps/bombs don't go sideways
                                //Investigate using sidestrafe capability to adjust aim if target moving perpendicularly?
                            }
                        }
                        targetVelocity = Mathf.Clamp(targetVelocity, PoweredSteering ? CombatSpeed / 5 : (doReverse ? -MaxSpeed : 0), MaxSpeed); // maintain a bit of speed if using powered steering
                    }
                }
                return;
            }

            // follow
            if (command == PilotCommands.Follow)
            {
                if (commandLeader == null)
                {
                    ReleaseCommand();
                    SetStatus($"Lost the command leader");
                    targetDirection = vesselTransform.up;
                    return;
                }
                leftPath = true;
                if (collisionDetectionTicker == 5)
                    checkBypass(commandLeader.vessel);

                Vector3 targetPosition = GetFormationPosition();
                Vector3 targetDistance = targetPosition - vesselTransform.position;
                if (Vector3.Dot(targetDistance, vesselTransform.up) < 0
                    && targetDistance.ProjectOnPlanePreNormalized(upDir).sqrMagnitude < 250f * 250f
                    && VectorUtils.Angle(vesselTransform.up, commandLeader.vessel.srf_velocity) < 0.8f)
                {
                    targetDirection = Vector3.RotateTowards(commandLeader.vessel.srf_vel_direction.ProjectOnPlanePreNormalized(upDir), targetDistance, 0.2f, 0);
                }
                else
                {
                    targetDirection = targetDistance.ProjectOnPlanePreNormalized(upDir);
                }
                targetVelocity = (float)(commandLeader.vessel.horizontalSrfSpeed + (vesselTransform.position - targetPosition).magnitude / 15);
                if (Vector3.Dot(targetDirection, vesselTransform.up) < 0 && !PoweredSteering) targetVelocity = 0;
                SetStatus($"Following");
                return;
            }


            // goto
            if (command == PilotCommands.Waypoints)
            {
                Pathfind(VectorUtils.WorldPositionToGeoCoords(waypointPosition, vessel.mainBody));
            }
            else if (leftPath)
            {
                Pathfind(finalPositionGeo); //is surface navigation pathing nodes really necessary for an aircraft?
                leftPath = false;
            }

            const float targetRadius = 250f;
            targetDirection = (assignedPositionWorld - vessel.CoM).ProjectOnPlanePreNormalized(upDir);

            //float forwardVelSqr = forwardVel * forwardVel;
            // Braking distance is proportional to the square of the speed
            //if (targetDirection.sqrMagnitude > Mathf.Max(BrakingMult * (forwardVelSqr * forwardVelSqr / 10000f), targetRadius * targ etRadius))

            //const float linearVel = 20f; // When do we go from sqrt(dist) to directly proportional to dist?
            //const float linearVelConst = linearVel * linearVel / 2f; // d = 1/2 a t² -> d = (1/2 v²) / a

            float targetDirDist = Vector3.Dot(projectedForward, targetDirection);
            if (Math.Abs(targetDirDist) * BrakingAccel * 2f > BrakingLinearVel * BrakingLinearVel)
            {
                // Constant deceleration
                targetVelocity = Mathf.Clamp(BDAMath.SignedSqrt(targetDirDist) * BrakingAccel, -MaxSpeed, MaxSpeed);
            }
            else
            {
                // We go to linear at d = (1/2 linearVel²) / a, thus v = linearVel / ((1/2 linearVel²) / a) * d -> v = 2a / linearVel * d
                targetVelocity = Mathf.Clamp((2f * BrakingAccel / BrakingLinearVel) * targetDirDist, -MaxSpeed, MaxSpeed);
            }

            if (targetDirection.sqrMagnitude > (targetRadius * targetRadius))
            {
                //targetVelocity = MaxSpeed;

                if (targetDirDist < 0 && !PoweredSteering) targetVelocity = 0;
                SetStatus("Moving");
                if (IsRunningWaypoints)
                {
                    if (BDArmorySettings.WAYPOINT_LOOP_INDEX > 1)
                        SetStatus($"Lap {activeWaypointLap}, Waypoint {activeWaypointIndex} ({waypointRange:F0}m)");
                    else
                        SetStatus($"Waypoint {activeWaypointIndex} ({waypointRange:F0}m)");
                }
                return;
            }

            cycleWaypoint();

            targetLatVelocity = Mathf.Clamp((2f * BrakingAccel / BrakingLinearVel) * Vector3.Dot(projectedLeft, targetDirection), -0.2f * MaxSpeed, 0.2f * MaxSpeed);

            SetStatus($"Not doing anything in particular");
            targetDirection = vesselTransform.up;
        }

        // Evading
        bool evading = false;
        bool wasEvading = false;
        public bool IsEvading => evading;

        float evasiveTimer;
        float threatRating;

        bool CheckThreats()
        {
            // Calculate threat rating from any threats
            float minimumEvasionTime = minEvasionTime;
            threatRating = evasionThreshold + 1f; // Don't evade by default
            wasEvading = evading;
            evading = false;
            var weaponManager = WeaponManager;
            if (weaponManager != null)
            {
                bool evadeMissile = weaponManager.incomingMissileTime <= weaponManager.evadeThreshold;

                if (evadeMissile)
                {
                    threatRating = -1f; // Allow entering evasion code if we're under missile fire
                    minimumEvasionTime = 0f; //  Trying to evade missile threats when they don't exist will result in NREs
                }
                else if (weaponManager.underFire && weaponManager.incomingMissTime >= evasionTimeThreshold && weaponManager.incomingThreatDistanceSqr >= evasionMinRangeThreshold * evasionMinRangeThreshold) // If we haven't been under fire long enough or they're too close, ignore gunfire
                {
                    threatRating = weaponManager.incomingMissDistance;
                }
            }

            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Threat Rating: {threatRating:G3}");

            Vector3 vesselTransformPosition = vesselTransform.position;

            // If we're currently evading or a threat is significant.
            if ((evasiveTimer < minimumEvasionTime && evasiveTimer != 0) || threatRating < evasionThreshold)
            {
                evasiveTimer += Time.fixedDeltaTime;

                if (evasiveTimer >= minimumEvasionTime)
                {
                    evasiveTimer = 0;
                }
                return true;
            }
            return false;
        }

        void Tactical()
        {
            var weaponManager = WeaponManager;

            bool evade = CheckThreats();

            // enable RCS if we're in combat
            vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, weaponManager && targetVessel && !BDArmorySettings.PEACE_MODE
                && (weaponManager.selectedWeapon != null || (vessel.CoM - targetVessel.CoM).sqrMagnitude < MaxEngagementRange * MaxEngagementRange)
                || evade);

            // if weaponManager thinks we're under fire, do the evasive dance
            if (evade)
            {
                Vessel missileThreat = null;
                if (weaponManager.missileIsIncoming && weaponManager.incomingMissileTime < weaponManager.cmThreshold && (missileThreat = weaponManager.incomingMissileVessel))
                {
                    MissileBase incomingMB = VesselModuleRegistry.GetMissileBase(missileThreat);
                    Vector3d threatDir;
                    if (!BDArmorySettings.RADAR_NOTCHING || incomingMB.TargetingMode != MissileBase.TargetingModes.Radar || !incomingMB.vrd || !incomingMB.radarTarget.lockedByRadar)
                    {
                        threatDir = missileThreat.CoMD - vessel.CoMD;
                    }
                    else
                    {
                        threatDir = incomingMB.radarTarget.lockedByRadar.vessel.CoMD - vessel.CoMD;
                    }
                    double dotForwards = Vector3d.Dot(threatDir, projectedForward);
                    double dotLeft = Vector3d.Dot(threatDir, projectedLeft);

                    // We gotta dodge the missile first!
                    aimingMode = AimingMode.Off;
                    pitchOffset = 0f;
                    yawOffset = 0f;
                    targetPitchAngularRate = 0f;
                    targetYawAngularRate = 0f;

                    // If the missile threat dir is more closely aligned with forwards
                    if (Math.Abs(dotForwards) > Math.Abs(dotLeft))
                    {
                        // We're pointing towards the target
                        targetDirection.x = (float)(dotForwards * projectedForward.x + dotLeft * projectedLeft.x);
                        targetDirection.y = (float)(dotForwards * projectedForward.y + dotLeft * projectedLeft.y);
                        targetDirection.z = (float)(dotForwards * projectedForward.z + dotLeft * projectedLeft.z);
                        // Coming to a stop
                        targetVelocity = 0f;
                        // And trying to book it sideways (away from the missile)
                        targetLatVelocity = dotLeft >= 0f ? -ControlSpeed : ControlSpeed;
                    }
                    else
                    {
                        // Otherwise, we're going 90° to the missile
                        // We're always going to point forwards if possible
                        if (dotLeft >= 0)
                        {
                            dotForwards = -dotForwards;
                        }
                        else
                        {
                            dotLeft = -dotLeft;
                        }
                        targetDirection.x = (float)(dotLeft * projectedForward.x + dotForwards * projectedLeft.x);
                        targetDirection.y = (float)(dotLeft * projectedForward.y + dotForwards * projectedLeft.y);
                        targetDirection.z = (float)(dotLeft * projectedForward.z + dotForwards * projectedLeft.z);
                        // Notch it
                        targetLatVelocity = 0f;
                        // And book it
                        targetVelocity = MaxSpeed;
                    }

                    weaveAdjustment = 0f;
                    SetStatus("Evading Msl.");
                    return;
                }

                // If we aren't evading a missile, just do the standard weave
                if (Math.Abs(weaveAdjustment) + Time.fixedDeltaTime * WeaveFactor > weaveLimit * WeaveFactor)
                {
                    weaveAdjustment = weaveDirection * (weaveLimit - Time.fixedDeltaTime) * WeaveFactor;
                    weaveDirection *= -1;
                }
                weaveAdjustment += WeaveFactor * weaveDirection * Time.fixedDeltaTime;
                targetLatVelocity = weaveAdjustment * MaxReverseSideslipAngle;
            }
            else
            {
                weaveAdjustment = 0;
            }
            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine($"underFire {weaponManager.underFire}, aimingMode {aimingMode}, weaveAdjustment {weaveAdjustment}");
        }

        void AdjustThrottle(float targetSpeed)
        {
            altitudeControl.targetAltitude = targetAltitude;

            targetVelocity = Mathf.Clamp(targetVelocity, doReverse ? -MaxSpeed : 0, MaxSpeed);
            targetSpeed = Mathf.Clamp(targetSpeed, doReverse ? -MaxSpeed : 0, MaxSpeed);
            float velocitySignedSrfSpeed = VectorUtils.Angle(vessel.srf_vel_direction.ProjectOnPlanePreNormalized(upDir), vesselTransform.up) < 110 ? (float)vessel.srfSpeed : -(float)vessel.srfSpeed;

            if (float.IsNaN(targetSpeed)) //because yeah, I might have left division by zero in there somewhere
            {
                targetSpeed = CombatSpeed;
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine("Target velocity NaN, set to CruiseSpeed.");
            }
            else
            {
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine($"Target velocity: {targetSpeed}; signed Velocity: {velocitySignedSrfSpeed}; brakeVel: {targetSpeed * velocitySignedSrfSpeed}; use brakes: {(targetSpeed * velocitySignedSrfSpeed < -5)}");
            }

            altitudeControl.targetSpeed = targetSpeed;
        }

        //Controller Integral
        //Vector3 directionIntegral; // Because everything is now rate-based this isn't as applicable
        float pitchIntegral;
        float yawIntegral;
        float rollIntegral;
        double forwardVel;
        double latVel;
        double MaxTiltSin = 1f;
        double MaxTiltTan = 0f;
        float prevMaxTiltAngle = 0f;
        double HoverPitchCos = 1f;
        double HoverPitchSin = 0f;
        float prevHoverPitchAngle = 0f;

        Vector3d alignVector = Vector3d.zero;

        void AttitudeControl(FlightCtrlState s)
        {
            Vector3d velocity = vessel.Velocity();
            forwardVel = Vector3d.Dot(velocity, projectedForward);
            latVel = Vector3d.Dot(velocity, projectedLeft);

            double planarSpd = Math.Sqrt(forwardVel * forwardVel + latVel * latVel);
            Vector3d planarVelFwd;
            planarVelFwd.x = forwardVel * projectedForward.x + latVel * projectedLeft.x;
            planarVelFwd.y = forwardVel * projectedForward.y + latVel * projectedLeft.y;
            planarVelFwd.z = forwardVel * projectedForward.z + latVel * projectedLeft.z;
            Vector3d planarVelLat;
            planarVelLat.x = -latVel * projectedForward.x + forwardVel * projectedLeft.x;
            planarVelLat.y = -latVel * projectedForward.y + forwardVel * projectedLeft.y;
            planarVelLat.z = -latVel * projectedForward.z + forwardVel * projectedLeft.z;

            double commandedSideslip = VectorUtils.GetAngleOnPlaneD(forwardVel > 0 ? targetDirection : -targetDirection, planarVelFwd, planarVelLat);
            // We're basically directly commanding acceleration here, hence the forward * planarSpd, which gives us
            // our v^2 component, the division by 1000 is just a scaling factor to make the practical range of
            // `steerRollFactor` fall within the limits we have
            targetLatVelocity += steerRollFactor * (float)((forwardVel * planarSpd / 1000d) * (forwardVel > 0 ? commandedSideslip : -commandedSideslip) * BDAMath.Deg2Rad);

            if (Math.Abs(forwardVel) > ControlSpeed)
            {
                // We assume the sideslip limit is quadratic, torque vs. AoA we assume is approx. linear
                // and torque is a function of v^2. Since both the tail rotor and this torque scale with
                // atmDensity, this doesn't require atmDensity scaling (we assume all in-atmosphere VTOLs
                // are helicopters...)
                double velDiff = (ControlSpeed - (forwardVel > 0d ? MaxSpeed : CombatSpeed));
                double currSideslipLimit = (forwardVel - (forwardVel > 0 ? MaxSpeed : CombatSpeed)) / velDiff;
                currSideslipLimit = (forwardVel > 0 ? 85d : MaxReverseSideslipAngle) * currSideslipLimit * currSideslipLimit + (forwardVel > 0 ? 5d : 0d);

                commandedSideslip = Math.Abs(commandedSideslip);
                if (commandedSideslip > currSideslipLimit)
                {
                    // Invert the equation
                    double temp = Math.Sqrt((commandedSideslip - (forwardVel > 0 ? 5d : 0d)) / (forwardVel > 0 ? 85d : MaxReverseSideslipAngle));
                    float velLimit = MaxSpeed + (float)(velDiff * temp);
                    targetVelocity = Mathf.Clamp(targetVelocity, -velLimit, velLimit);
                    // We're past the sideslip limit, slow down first
                    aimingMode &= ~AimingMode.Pitch;
                    pitchOffset = 0f;
                    yawOffset = 0f;
                    targetYawAngularRate = 0f;
                    targetPitchAngularRate = 0f;

                    // Limit the commanded sideslip angle
                    targetDirection = VectorUtils.RotateTowards(forwardVel > 0 ? planarVelFwd : -planarVelFwd, targetDirection, currSideslipLimit * BDAMath.Deg2Rad);
                }
            }

            double velError = targetVelocity - forwardVel;
            double latError = targetLatVelocity - latVel;

            Vector3d accelerationCommand;
            // steerVelMult acts essentially like C_D * dragArea here
            // steerVelErrMult on the other hand is proportional control
            double atmScale = (vessel.atmDensity / 1.225);
            double forwardAccelCommand = atmScale * DragCompensationMult * forwardVel * (Math.Abs(forwardVel) / 10000d) + 10f * steerVelErrMult * velError;
            double latAccelCommand = atmScale * DragCompensationMult * latVel * (Math.Abs(latVel) / 10000d) + 10f * steerVelErrMult * latError;
            accelerationCommand.x = forwardAccelCommand * projectedForward.x + latAccelCommand * projectedLeft.x;
            accelerationCommand.y = forwardAccelCommand * projectedForward.y + latAccelCommand * projectedLeft.y;
            accelerationCommand.z = forwardAccelCommand * projectedForward.z + latAccelCommand * projectedLeft.z;

            if (MaxTiltAngle != prevMaxTiltAngle)
            {
                prevMaxTiltAngle = MaxTiltAngle;
                MaxTiltTan = Math.Tan(MaxTiltAngle * BDAMath.Deg2Rad);
                MaxTiltSin = Math.Sin(MaxTiltAngle * BDAMath.Deg2Rad);
            }

            (double accelMag, accelerationCommand) = accelerationCommand.MagNorm();
            // We command the VTOL to tilt in the direction of accelerationCommand
            // this probably ought to scale with vessel mass vs. mass at tuning somehow...
            double tiltTan = BDAMath.Clamp(tiltAccelMult * accelMag / 1200d, 0, MaxTiltTan);
            double tiltCos = Math.Sqrt(1d + tiltTan * tiltTan);
            tiltCos = 1d / tiltCos;
            double tiltSin = tiltTan * tiltCos;
            
            //Vector3d alignVector;
            alignVector.x = accelerationCommand.x * tiltSin + upDir.x * tiltCos;
            alignVector.y = accelerationCommand.y * tiltSin + upDir.y * tiltCos;
            alignVector.z = accelerationCommand.z * tiltSin + upDir.z * tiltCos;

            if (prevHoverPitchAngle != HoverPitchAngle)
            {
                prevHoverPitchAngle = HoverPitchAngle;
                HoverPitchCos = Math.Cos(((double)HoverPitchAngle) * BDAMath.Deg2Rad);
                HoverPitchSin = Math.Sin(((double)HoverPitchAngle) * BDAMath.Deg2Rad);
            }

            Vector3d up = -vesselTransform.forward;
            Vector3d adjustedUp;
            Vector3d adjustedFwd;
            if (HoverPitchAngle != 0)
            {
                Vector3d forward = vesselTransform.up;
                adjustedUp.x = HoverPitchCos * up.x + HoverPitchSin * forward.x;
                adjustedUp.y = HoverPitchCos * up.y + HoverPitchSin * forward.y;
                adjustedUp.z = HoverPitchCos * up.z + HoverPitchSin * forward.z;

                adjustedFwd.x = -HoverPitchSin * up.x + HoverPitchCos * forward.x;
                adjustedFwd.y = -HoverPitchSin * up.y + HoverPitchCos * forward.y;
                adjustedFwd.z = -HoverPitchSin * up.z + HoverPitchCos * forward.z;
            }
            else
            {
                adjustedFwd = vesselTransform.up;
                adjustedUp = up;
            }
            Vector3d right = vesselTransform.right;

            // Direct yaw control
            double yawError = VectorUtils.GetAngleOnPlaneD(targetDirection, adjustedFwd, right) + ((aimingMode & AimingMode.Yaw) != 0 ? 0 : weaveAdjustment) - -yawOffset;

            double pitchError;
            double rollError;
            // Highest priority is min alt and upset attitude (exceeding max tilt angle) recovery
            if (belowMinAltitude || initialTakeOff || Math.Abs(Vector3d.Dot(adjustedUp, upDir)) < MaxTiltSin)
            {
                pitchError = -VectorUtils.GetAngleOnPlaneD(upDir, adjustedUp, adjustedFwd);
                rollError = VectorUtils.GetAngleOnPlaneD(upDir, up, right);
            }
            else if ((aimingMode & AimingMode.Pitch) > 0)
            {
                targetDirection.Normalize();
                double dotProd = Vector3d.Dot(targetDirection, upDir);
                if (Math.Abs(dotProd) > MaxTiltSin)
                {
                    if (dotProd > 0)
                    {
                        targetDirection = VectorUtils.RotateTowardsStartNormUnlim(upDir, targetDirection, (90f - MaxTiltAngle) * BDAMath.Deg2Rad);
                    }
                    else
                    {
                        targetDirection = VectorUtils.RotateTowardsStartNormUnlim(-upDir, targetDirection, (90f - MaxTiltAngle) * BDAMath.Deg2Rad);
                    }
                }
                // Direct pitch control
                pitchError = VectorUtils.GetAngleOnPlaneD(targetDirection, adjustedFwd, adjustedUp);
                // Roll control unchanged from conventional
                rollError = VectorUtils.GetAngleOnPlaneD(alignVector, up, right);
            }
            /*else if (avoidingTerrain)
            {
                // Terrain avoidance takes precedence over min alt behavior
                pitchError = VectorUtils.GetAngleOnPlaneD(targetDirection, adjustedFwd, adjustedUp); // Not really implemented, but in the spirit of what was here we assume targetDirection is parallel to the ground
                // Similarly, we assume upDir is adjusted such that now it is aligned with the ground normal
                rollError = VectorUtils.GetAngleOnPlaneD(upDir, up, right);
            }*/
            else
            {
                pitchError = -VectorUtils.GetAngleOnPlaneD(alignVector, adjustedUp, adjustedFwd); // align up with adjustedUp
                rollError = VectorUtils.GetAngleOnPlaneD(alignVector, up, right);
            }

            pitchError -= pitchOffset;

            double adjPitchError = pitchError;
            if (Math.Abs(adjPitchError) < 1d)
            {
                adjPitchError = BDAMath.SignedSqrt(adjPitchError);
            }
            double adjYawError = yawError;
            if (Math.Abs(adjYawError) < 1d)
            {
                adjYawError = BDAMath.SignedSqrt(adjYawError);
            }

            Vector3d localAngVel = vessel.angularVelocityD;
            #region PID calculations
            // FIXME Why are there various constants in here that mess with the scaling of the PID in the various axes? Ratios between the axes are 1:0.33:0.1
            double pitchAngVelErr = BDAMath.Clamp(steerAngVelMult * adjPitchError, -steerMaxAngVel, steerMaxAngVel) * BDAMath.Deg2Rad + localAngVel.x + targetPitchAngularRate;
            double yawAngVelErr = BDAMath.Clamp(steerAngVelMult * adjYawError, -steerMaxAngVel, steerMaxAngVel) * BDAMath.Deg2Rad + localAngVel.z + targetYawAngularRate;
            double rollAngVelErr = BDAMath.Clamp(steerAngVelMult * rollError, -steerMaxAngVel, steerMaxAngVel) * BDAMath.Deg2Rad + localAngVel.y;

            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI)
            {
                DebugLine($"""
                    yaw error: {yawError:+0.00;-0.00;0.00}; angVel: {localAngVel.z * BDAMath.Rad2Deg:+0.00;-0.00;0.00}, angVelErr: {yawAngVelErr * BDAMath.Rad2Deg:+0.00;-0.00;0.00}; targetYawAngularRate: {targetYawAngularRate * BDAMath.Rad2Deg:+0.00;-0.00;0.00}
                    target vel: {targetVelocity:+0.00;-0.00;0.00}, forward vel: {forwardVel:+0.00;-0.00;0.00}, vel error: {velError:+0.00;-0.00;0.00}; pitch error: {pitchError:+0.00;-0.00;0.00}; angVel: {localAngVel.x * BDAMath.Rad2Deg:+0.00;-0.00;0.00}, angVelErr: {pitchAngVelErr * BDAMath.Rad2Deg:+0.00;-0.00;0.00}; targetPitchAngularRate: {targetPitchAngularRate * BDAMath.Rad2Deg:+0.00;-0.00;0.00}
                    target lat vel: {targetLatVelocity:+0.00;-0.00;0.00}; lateral vel: {latVel:+0.00;-0.00;0.00}, lat vel error: {latError:+0.00;-0.00;0.00}; roll error: {rollError:+0.00;-0.00;0.00}; angVel: {localAngVel.y * BDAMath.Rad2Deg:+0.00;-0.00;0.00}, angVelErr: {rollAngVelErr * BDAMath.Rad2Deg:+0.00;-0.00;0.00}
                    """);
            }

            double velAngleScaling;
            if (forwardVel > ControlSpeed)
            {
                velAngleScaling = forwardVel - ControlSpeed;
                velAngleScaling *= velAngleScaling / (double)(MaxSpeed * MaxSpeed);
                double AoASideslipSin = Math.Abs(Vector3d.Dot(velocity, adjustedUp) * Vector3d.Dot(velocity, right) / velocity.sqrMagnitude); // Sin(AoA) * Sin(Sideslip)
                velAngleScaling = Math.Min(atmScale * velAngleScaling * AoASideslipSin, 2d);
            }
            else
            {
                velAngleScaling = 0f;
            }

            double pitchProportional = steerMult * (0.15d * pitchAngVelErr);
            double yawProportional = steerMult * (0.05d * (1d + velAngleScaling) * yawAngVelErr);
            double rollProportional = steerMult * (0.15d * (1d + velAngleScaling) * rollAngVelErr);

            /*float pitchDamping = steerDamping * -localAngVel.x;
            float yawDamping = 0.33f * steerDamping * -localAngVel.z;
            float rollDamping = 0.66f * steerDamping * -localAngVel.y;*/

            // For the integral, we track the vector of the pitch and yaw in the 2D plane of the vessel's forward pointing vector so that the pitch and yaw components translate between the axes when the vessel rolls.
            /*directionIntegral = (directionIntegral + ((float)pitchAngVelErr * vesselTransform.right + (float)yawAngVelErr * vesselTransform.forward) * Time.deltaTime).ProjectOnPlanePreNormalized(vesselTransform.up);
            if (directionIntegral.sqrMagnitude > 1f) directionIntegral = directionIntegral.normalized;*/
            float integralLimit = 1f / steerKiAdjust;
            pitchIntegral = Mathf.Clamp(pitchIntegral + (float)pitchAngVelErr * Time.deltaTime, -integralLimit, integralLimit);//steerKiAdjust * Vector3.Dot(directionIntegral, vesselTransform.right); //Vector3.Dot(directionIntegral, -vesselTransform.forward);
            yawIntegral = Mathf.Clamp(yawIntegral + (float)yawAngVelErr * Time.deltaTime, -3f * integralLimit, 3f * integralLimit); //Vector3.Dot(directionIntegral, vesselTransform.forward); //Mathf.Clamp(yawIntegral + yawError * Time.deltaTime, -1f, 1f);
            rollIntegral = 0.33f * steerKiAdjust * Mathf.Clamp(rollIntegral + (float)rollAngVelErr * Time.deltaTime, -1f, 1f);

            SetFlightControlState(s,
                s.pitch = (float)(pitchProportional + steerKiAdjust * pitchIntegral),
                s.yaw = (float)(yawProportional + 0.33f * steerKiAdjust * yawIntegral),
                s.roll = (float)(rollProportional + rollIntegral)
            );
            #endregion

            if (ManeuverRCS && (Mathf.Abs(s.roll) >= 1 || Mathf.Abs(s.pitch) >= 1 || Mathf.Abs(s.yaw) >= 1))
                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);
        }

        #endregion Actual AI Pilot

        #region Autopilot helper functions

        public override bool CanEngage()
        {
            return !vessel.LandedOrSplashed;
        }

        GameObject vobj;

        Transform velocityTransform
        {
            get
            {
                if (!vobj)
                {
                    vobj = new GameObject("velObject");
                    vobj.transform.position = vessel.ReferenceTransform.position;
                    vobj.transform.parent = vessel.ReferenceTransform;
                }

                return vobj.transform;
            }
        }

        void CheckLandingGear()
        {
            if (!vessel.LandedOrSplashed)
            {
                if (vessel.radarAltitude > Mathf.Min(50f, minAltitude / 2f))
                    vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, false);
                else
                    vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, true);
            }
        }

        void Takeoff()
        {
            belowMinAltitude = (float)vessel.radarAltitude < minAltitude;
            if (vessel.Landed && (float)vessel.radarAltitude > 1)
            {
                vessel.Landed = false; // KSP sometimes isn't updating this correctly after spawning.
                vessel.Splashed = vessel.altitude < 0; // Radar altitude could be > 1, while the craft is still underwater due to the way radarAlt works...
            }
            if (!belowMinAltitude)
            {
                initialTakeOff = false;
                TakingOff = false;
            }
        }

        public override bool IsValidFixedWeaponTarget(Vessel target)
        {
            if (!vessel) return false;

            return true;
        }

        Vector3? PredictCollisionWithVessel(Vessel v, float maxTime, float interval)
        {
            var weaponManager = WeaponManager;
            //evasive will handle avoiding missiles
            if (weaponManager && v == weaponManager.incomingMissileVessel
                || v.rootPart.FindModuleImplementing<MissileBase>() != null)
                return null;

            float time = Mathf.Min(0.5f, maxTime);
            while (time < maxTime)
            {
                Vector3 tPos = v.PredictPosition(time);
                Vector3 myPos = vessel.PredictPosition(time);
                if (Vector3.SqrMagnitude(tPos - myPos) < 2500f)
                {
                    return Vector3.Dot(tPos - myPos, vesselTransform.right) > 0 ? -vesselTransform.right : vesselTransform.right;
                }

                time = Mathf.MoveTowards(time, maxTime, interval);
            }

            return null;
        }

        void checkBypass(Vessel target)
        {
            if (!pathingMatrix.TraversableStraightLine(
                    VectorUtils.WorldPositionToGeoCoords(vessel.CoM, vessel.mainBody),
                    VectorUtils.WorldPositionToGeoCoords(target.CoM, vessel.mainBody),
                    vessel.mainBody, SurfaceType, MaxTiltAngle, AvoidMass))
            {
                bypassTarget = target;
                bypassTargetPos = VectorUtils.WorldPositionToGeoCoords(target.CoM, vessel.mainBody);
                pathingWaypoints = pathingMatrix.Pathfind(
                    VectorUtils.WorldPositionToGeoCoords(vessel.CoM, vessel.mainBody),
                    VectorUtils.WorldPositionToGeoCoords(target.CoM, vessel.mainBody),
                    vessel.mainBody, SurfaceType, MaxTiltAngle, AvoidMass);
                if (VectorUtils.GeoDistance(pathingWaypoints[pathingWaypoints.Count - 1], bypassTargetPos, vessel.mainBody) < 200)
                    pathingWaypoints.RemoveAt(pathingWaypoints.Count - 1);
                if (pathingWaypoints.Count > 0)
                    intermediatePositionGeo = pathingWaypoints[0];
                else
                    bypassTarget = null;
            }
        }

        private void Pathfind(Vector3 destination)
        {
            pathingWaypoints = pathingMatrix.Pathfind(
                                    VectorUtils.WorldPositionToGeoCoords(vessel.CoM, vessel.mainBody),
                                    destination, vessel.mainBody, SurfaceType, MaxTiltAngle, AvoidMass);
            intermediatePositionGeo = pathingWaypoints[0];
        }

        void cycleWaypoint()
        {
            if (pathingWaypoints.Count > 1)
            {
                pathingWaypoints.RemoveAt(0);
                intermediatePositionGeo = pathingWaypoints[0];
            }
            else if (bypassTarget != null)
            {
                pathingWaypoints.Clear();
                bypassTarget = null;
                leftPath = true;
            }
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
