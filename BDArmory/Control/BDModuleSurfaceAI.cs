using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Weapons;
using BDArmory.Weapons.Missiles;
using BDArmory.GameModes;
using BDArmory.Guidances;

namespace BDArmory.Control
{
    public class BDModuleSurfaceAI : BDGenericAIBase, IBDAIControl
    {
        public override AIType aiType => AIType.SurfaceAI;
        #region Declarations
        Vessel extendingTarget = null;
        public bool orderedToExtend = false;
        Vessel bypassTarget = null;
        Vector3 bypassTargetPos;

        Vector3 targetDirection; // Note: this isn't normalized
        float targetVelocity; // the velocity the ship should target, not the velocity of its target
        enum AimingMode { Off = 0, Yaw = 1, Pitch = 2, Direct = Yaw | Pitch }
        AimingMode aimingMode = AimingMode.Off;

        //Building collision detection stuff
        float terrainAlertDetectionRadius;
        float terrainAlertThreatRange = 100; //assuming most tanks/ground Vees can manage a 100m turning circle. may need increase for hovercraft
        RaycastHit[] terrainAvoidanceHits = new RaycastHit[10];
        int collisionTicker = 100;
        int collisionDetectionTicker = 0;
        int reverseTicker = 0;
        Vector3 dodgeVector = Vector3.zero;
        float vehicleWidth;
        Vector3 alertNormalAvg = Vector3.zero;
        float alertNormalAvgF = 0.95f;

        float weaveAdjustment = 0;
        float weaveDirection = 1;
        const float weaveLimit = 2.3f; // Scale factor for the limit of the WeaveFactor (original was 6.5 factor and 15 limit).

        Vector3 upDir;
        Vector3 terrainNormal;

        AIUtils.TraversabilityMatrix pathingMatrix;
        List<Vector3> pathingWaypoints = new List<Vector3>();
        bool leftPath = false;

        bool doExtend = false;
        bool doReverse = false;
        bool wasReversing = false;
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

        private BDLandSpeedControl motorControl;

        //settings
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_VehicleType"),//Vehicle type
            UI_ChooseOption(options = new string[5] { "Stationary", "Land", "Water", "Amphibious", "Submarine" })]
        public string SurfaceTypeName = "Land";

        bool isHovercraft = false;

        public AIUtils.VehicleMovementType SurfaceType
            => (AIUtils.VehicleMovementType)Enum.Parse(typeof(AIUtils.VehicleMovementType), SurfaceTypeName);

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MaxSlopeAngle"),//Max slope angle
            UI_FloatRange(minValue = 1f, maxValue = 30f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float MaxSlopeAngle = 10f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_CombatAltitude"), //Combat Alt.
            UI_FloatRange(minValue = -200, maxValue = -15, stepIncrement = 5, scene = UI_Scene.All)]
        public float CombatAltitude = -75;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_CruiseSpeed"),//Cruise speed
            UI_FloatRange(minValue = 5f, maxValue = 60f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float CruiseSpeed = 20;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MaxSpeed"),//Max speed
            UI_FloatRange(minValue = 5f, maxValue = 80f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float MaxSpeed = 30;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MaxDrift"),//Max drift
            UI_FloatRange(minValue = 1f, maxValue = 180f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float MaxDrift = 10;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_TargetPitch"),//Moving pitch
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = .1f, scene = UI_Scene.All)]
        public float TargetPitch = 0;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_BankAngle"),//Bank angle
            UI_FloatRange(minValue = -45f, maxValue = 45f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float BankAngle = 0;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_WeaveFactor"),//Weave Factor
            UI_FloatRange(minValue = 0f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float WeaveFactor = 6.5f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerPower"),//Steer Factor
            UI_FloatRange(minValue = 0.2f, maxValue = 20f, stepIncrement = .1f, scene = UI_Scene.All)]
        public float steerMult = 6;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerDamping"),//Steer Damping
            UI_FloatRange(minValue = 0.1f, maxValue = 10f, stepIncrement = .1f, scene = UI_Scene.All)]
        public float steerDamping = 3;

        //[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Steering"),
        //	UI_Toggle(enabledText = "Powered", disabledText = "Passive")]
        public bool PoweredSteering = true;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_BroadsideAttack"),//Attack vector
            UI_Toggle(enabledText = "#LOC_BDArmory_AI_BroadsideAttack_enabledText", disabledText = "#LOC_BDArmory_AI_BroadsideAttack_disabledText")]//Broadside--Bow
        public bool BroadsideAttack = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MinEngagementRange"),//Min engagement range
            UI_FloatRange(minValue = 0f, maxValue = 6000f, stepIncrement = 100f, scene = UI_Scene.All)]
        public float MinEngagementRange = 500;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MaxEngagementRange"),//Max engagement range
            UI_FloatRange(minValue = 500f, maxValue = 8000f, stepIncrement = 100f, scene = UI_Scene.All)]
        public float MaxEngagementRange = 4000;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MaintainEngagementRange"),//Maintain min Range
            UI_Toggle(enabledText = "#LOC_BDArmory_true", disabledText = "#LOC_BDArmory_false")]//true; false
        public bool maintainMinRange = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ManeuverRCS"),//RCS active
            UI_Toggle(enabledText = "#LOC_BDArmory_AI_ManeuverRCS_enabledText", disabledText = "#LOC_BDArmory_AI_ManeuverRCS_disabledText", scene = UI_Scene.All),]//Maneuvers--Combat
        public bool ManeuverRCS = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MinObstacleMass", advancedTweakable = true),//Min obstacle mass
            UI_FloatSemiLogRange(minValue = 0.1f, maxValue = 100f, sigFig = 2, withZero = true, scene = UI_Scene.All)]
        public float AvoidMass = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_PreferredBroadsideDirection", advancedTweakable = true),//Preferred broadside direction
            UI_ChooseOption(options = new string[3] { "Port", "Either", "Starboard" }, scene = UI_Scene.All),]
        public string OrbitDirectionName = "Either";
        public readonly string[] orbitDirections = new string[3] { "Port", "Either", "Starboard" };

        [KSPField(isPersistant = true)]
        int sideSlipDirection = 0;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_GoesUp", advancedTweakable = true),//Goes up to 
            UI_Toggle(enabledText = "#LOC_BDArmory_AI_GoesUp_enabledText", disabledText = "#LOC_BDArmory_AI_GoesUp_disabledText", scene = UI_Scene.All),]//eleven--ten
        bool upToEleven = false;
        public bool UpToEleven { get { return upToEleven; } set { if (upToEleven != (upToEleven = value)) this.UpdateToggle(Fields[nameof(upToEleven)]); } }

        const float AttackAngleAtMaxRange = 30f;

        Dictionary<string, float> altMaxValues = new Dictionary<string, float>
        {
            { nameof(MaxSlopeAngle), 90f },
            { nameof(CruiseSpeed), 300f },
            { nameof(MaxSpeed), 400f },
            { nameof(steerMult), 200f },
            { nameof(steerDamping), 100f },
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
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Vehicle type</color> - can this vessel operate on land/sea/both");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Max slope angle</color> - what is the steepest slope this vessel can negotiate");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Cruise speed</color> - the default speed at which it is safe to maneuver");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Max speed</color> - the maximum combat speed");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Max drift</color> - maximum allowed angle between facing and velocity vector");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Moving pitch</color> - the pitch level to maintain when moving at cruise speed");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Bank angle</color> - the limit on roll when turning, positive rolls into turns");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Steer Factor</color> - higher will make the AI apply more control input for the same desired rotation");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Steer Damping</color> - higher will make the AI apply more control input when it wants to stop rotation");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Attack vector</color> - does the vessel attack from the front or the sides");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Min engagement range</color> - AI will try to move away from oponents if closer than this range");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Max engagement range</color> - AI will prioritize getting closer over attacking when beyond this range");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- RCS active</color> - Use RCS during any maneuvers, or only in combat ");
            if (GameSettings.ADVANCED_TWEAKABLES)
            {
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Min obstacle mass</color> - Obstacles of a lower mass than this will be ignored instead of avoided");
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
            TakingOff = false;
            originalMaxSpeed = MaxSpeed;
            pathingMatrix = new AIUtils.TraversabilityMatrix();

            if (!motorControl)
            {
                motorControl = gameObject.AddComponent<BDLandSpeedControl>();
                motorControl.vessel = vessel;
            }
            speedController.Deactivate();
            motorControl.Activate();
            if (BroadsideAttack && sideSlipDirection == 0)
            {
                SetBroadsideDirection(OrbitDirectionName);
            }

            leftPath = true;
            extendingTarget = null;
            bypassTarget = null;
            collisionDetectionTicker = 6;
            terrainAlertDetectionRadius = vessel.GetRadius() * 2;
            if (VesselModuleRegistry.GetModules<ModuleSpaceFriction>(vessel).Count > 0) isHovercraft = true;
            vehicleWidth = vessel.vesselSize.x / 2;
            alertNormalAvgF = Mathf.Exp(Mathf.Log(0.5f) * Time.fixedDeltaTime * 5f); // Decay rate for a half-life of 1/5s.
            return true;
        }

        public override void DeactivatePilot()
        {
            base.DeactivatePilot();

            if (motorControl)
                motorControl.Deactivate();
        }

        void SetChooseOptions()
        {
            UI_ChooseOption surface = (UI_ChooseOption)(HighLogic.LoadedSceneIsFlight ? Fields[nameof(SurfaceTypeName)].uiControlFlight : Fields[nameof(SurfaceTypeName)].uiControlEditor);
            surface.onFieldChanged = ChooseOptionsUpdated;
            ChooseOptionsUpdated(null, null);
        }

        public void ChooseOptionsUpdated(BaseField field, object obj)
        {
            // Hide/display the AI fields
            var fieldEnabled = SurfaceType != AIUtils.VehicleMovementType.Stationary;
            foreach (var fieldName in new List<string>{
                    nameof(MaxSlopeAngle),
                    nameof(CruiseSpeed),
                    nameof(MaxSpeed),
                    nameof(MaxDrift),
                    nameof(TargetPitch),
                    nameof(BankAngle),
                    // nameof(steerMult),
                    // nameof(steerDamping),
                    nameof(BroadsideAttack),
                    // nameof(MinEngagementRange),
                    // nameof(MaxEngagementRange),
                    // nameof(ManeuverRCS),
                    nameof(AvoidMass),
                    nameof(OrbitDirectionName)
                })
            {
                Fields[fieldName].guiActive = fieldEnabled;
                Fields[fieldName].guiActiveEditor = fieldEnabled;
            }
            Fields[nameof(CombatAltitude)].guiActive = SurfaceType == AIUtils.VehicleMovementType.Submarine;
            Fields[nameof(CombatAltitude)].guiActiveEditor = SurfaceType == AIUtils.VehicleMovementType.Submarine;
            Fields[nameof(maintainMinRange)].guiActive = SurfaceType == AIUtils.VehicleMovementType.Land;
            Fields[nameof(maintainMinRange)].guiActiveEditor = SurfaceType == AIUtils.VehicleMovementType.Land;
            this.DefaultChooseOptionHandler(field, obj);
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
                StartCoroutine(SetVar(s.Current, (float)typeof(BDModuleSurfaceAI).GetField(s.Current).GetValue(this))); // change the value back to what it is now after fixed update, because changing the max value will clamp it down
            }
            foreach (var fieldName in altSemiLogValues.Keys.ToList())
            {
                var field = (UI_FloatSemiLogRange)(HighLogic.LoadedSceneIsFlight ? Fields[fieldName].uiControlFlight : Fields[fieldName].uiControlEditor);
                var temp = (field.minValue, field.maxValue, field.sigFig);
                var altValues = altSemiLogValues[fieldName];
                if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModuleSurfaceAI]: Swapping semiLog limits of {fieldName} from {temp} to {altValues}");
                field.UpdateLimits(altValues.Item1, altValues.Item2, altValues.Item3);
                altSemiLogValues[fieldName] = temp;
            }
            this.UpdateToggle(Fields[nameof(upToEleven)]);
        }

        IEnumerator SetVar(string name, float value)
        {
            yield return new WaitForFixedUpdate();
            typeof(BDModuleSurfaceAI).GetField(name).SetValue(this, value);
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
            foreach (var hit in debugHits) GUIUtils.DrawLineBetweenWorldPositions(hit.Item1, hit.Item1 + 5 * hit.Item2, 5 - 5 / debugHitFadeTime * (Time.time - hit.Item3), Color.magenta); // Collision Avoidance (width fades before they're removed)
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + targetDirection * 10f, 2, Color.blue);
            GUIUtils.DrawLineBetweenWorldPositions(vessel.CoM + vehicleWidth * vesselTransform.right, vessel.CoM + vehicleWidth * vesselTransform.right + (wasReversing ? -vesselTransform.up : vesselTransform.up) * (vehicleWidth + terrainAlertDetectionRadius), 2, Color.red);
            GUIUtils.DrawLineBetweenWorldPositions(vessel.CoM - vehicleWidth * vesselTransform.right, vessel.CoM - vehicleWidth * vesselTransform.right + (wasReversing ? -vesselTransform.up : vesselTransform.up) * (vehicleWidth + terrainAlertDetectionRadius), 2, Color.red);
            //GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position + (0.05f * vesselTransform.right), vesselTransform.position + (0.05f * vesselTransform.right), 2, Color.green);
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + vessel.srf_vel_direction.ProjectOnPlanePreNormalized(upDir) * 10f, 2, Color.green);
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + vesselTransform.up * 10f, 5, Color.red);
            if (SurfaceType != AIUtils.VehicleMovementType.Stationary)
            {
                pathingMatrix.DrawDebug(vessel.CoM, pathingWaypoints);
                GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + (Vector3)(dodgeVector != null ? dodgeVector : vessel.srf_vel_direction * 25), 2, Color.white);
            }
        }

        #endregion events

        #region Status
        public enum StatusMode { Free, OnAlert, Engaging, Evading, Extending, Moving, Repositioning, Braking, Reversing, CollisionAvoidance, RammingSpeed, Panic, Custom }
        public StatusMode currentStatusMode = StatusMode.Free;
        protected override void SetStatus(string status)
        {
            base.SetStatus(status);
            if (status.StartsWith("Free")) currentStatusMode = StatusMode.Free;
            else if (status.StartsWith("On Alert")) currentStatusMode = StatusMode.OnAlert;
            else if (status.StartsWith("Engaging")) currentStatusMode = StatusMode.Engaging;
            else if (status.StartsWith("Evading")) currentStatusMode = StatusMode.Evading;
            else if (status.StartsWith("Moving")) currentStatusMode = StatusMode.Moving;
            else if (status.StartsWith("Repositioning")) currentStatusMode = StatusMode.Repositioning;
            else if (status.StartsWith("Braking")) currentStatusMode = StatusMode.Braking;
            else if (status.StartsWith("Reversing")) currentStatusMode = StatusMode.Reversing;
            else if (status.StartsWith("Extending")) currentStatusMode = StatusMode.Extending;
            else if (status.StartsWith("Avoiding Collision")) currentStatusMode = StatusMode.CollisionAvoidance;
            else if (status.StartsWith("Ramming")) currentStatusMode = StatusMode.RammingSpeed;
            else if (status.StartsWith("Airtime!") || status.StartsWith("Stranded") || status.StartsWith("Floating") || status.StartsWith("Sunk") || status.StartsWith("Disabled")) currentStatusMode = StatusMode.Panic;
            else currentStatusMode = StatusMode.Custom;
        }
        #endregion

        #region Actual AI Pilot

        protected override void AutoPilot(FlightCtrlState s)
        {
            if (!vessel.Autopilot.Enabled)
                vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);

            targetVelocity = 0;
            targetDirection = vesselTransform.up;
            aimingMode = AimingMode.Off;
            upDir = vessel.up;
            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine("");
            if (IsRunningWaypoints) UpdateWaypoint(); // Update the waypoint state.
            if (SurfaceType == AIUtils.VehicleMovementType.Stationary)
            {
                if (!vessel.Splashed && Physics.Raycast(new Ray(vessel.CoM, -upDir), out RaycastHit hit, (float)vessel.radarAltitude + vessel.GetRadius(), (int)LayerMasks.Scenery))
                    terrainNormal = hit.normal;
                else
                    terrainNormal = upDir;
            }

            // check if we should be panicking
            if (SurfaceType == AIUtils.VehicleMovementType.Stationary || !PanicModes()) // Stationary vehicles don't panic (so, free-fall stationary turrets are a possibility).
            {
                // pilot logic figures out what we're supposed to be doing, and sets the base state
                PilotLogic();
                // situational awareness modifies the base as best as it can (evasive mainly)
                Tactical();
            }

            AttitudeControl(s); // move according to our targets
            AdjustThrottle(targetVelocity); // set throttle according to our targets and movement
        }
        readonly List<(Vector3, Vector3, float)> debugHits = [];
        float debugHitFadeTime = 0.5f;
        void PilotLogic()
        {
            wasReversing = doReverse;
            doReverse = false;
            if (BDArmorySettings.DEBUG_LINES) debugHits.RemoveAll(hit => Time.time - hit.Item3 > debugHitFadeTime); // Clear out those older than the fade time.
            if (SurfaceType != AIUtils.VehicleMovementType.Stationary)
            {
                float alertDistance = terrainAlertThreatRange;
                bool vesselCollision = false;
                bool reversingTurn = false;
                int validHitCount = 0;
                Vector3 vesselDir = vessel.srfSpeed > 1 ? vessel.srf_vel_direction : wasReversing ? -vesselTransform.up : vesselTransform.up;
                string collidingWith = "";

                // check for collisions, but not every frame unless we're currently avoiding a collision
                if (collisionDetectionTicker == 0 || currentStatusMode == StatusMode.CollisionAvoidance)
                {
                    collisionDetectionTicker = 20; // Every 0.4s when not actively avoiding collisions

                    dodgeVector = Vector3.zero;

                    { // Vessel-vessel collisions
                        float predictMult = Mathf.Clamp(10 / MaxDrift, 1, 10);
                        using var vs = BDATargetManager.LoadedVessels.GetEnumerator();
                        while (vs.MoveNext())
                        {
                            if (vs.Current == null || vs.Current == vessel || vs.Current.GetTotalMass() < AvoidMass) continue; //expand for SrfAi ramming implementation?
                            if (!VesselModuleRegistry.IgnoredVesselTypes.Contains(vs.Current.vesselType))
                            {
                                var ibdaiControl = vs.Current.ActiveController().AI;
                                if (!vs.Current.LandedOrSplashed || (ibdaiControl != null && ibdaiControl.commandLeader != null && ibdaiControl.commandLeader.vessel == vessel))
                                    continue;
                            }
                            dodgeVector = PredictCollisionWithVessel(vs.Current, 5f * predictMult, 0.5f);
                            if (dodgeVector != Vector3.zero) // Dodge the first potential collision (this isn't necessarily the closest, but multi-vessel collisions are unlikely).


                            {
                                vesselCollision = true;
                                collidingWith = vs.Current.GetName();
                                break;
                            }
                        }
                    }

                    { // Terrain/building collisions  FIXME We're only checking buildings, should we drop that and check terrain too?
                        Ray ray = new(vessel.CoM, vesselDir);
                        terrainAlertThreatRange = Mathf.Clamp((float)vessel.srfSpeed * 10f, 2f * terrainAlertDetectionRadius, Mathf.Max(200f, 10f * terrainAlertDetectionRadius)); // Have threat range scale with speed, but within limits.

                        // Check in the direction we're moving up to the threat range
                        int hitCount = Physics.SphereCastNonAlloc(ray, terrainAlertDetectionRadius, terrainAvoidanceHits, terrainAlertThreatRange, (int)LayerMasks.Scenery);
                        if (hitCount == terrainAvoidanceHits.Length)
                        {
                            terrainAvoidanceHits = Physics.SphereCastAll(ray, terrainAlertDetectionRadius, terrainAlertThreatRange, (int)LayerMasks.Scenery);
                            hitCount = terrainAvoidanceHits.Length;
                        }
                        if (hitCount > 0) // Found something. 
                        {
                            Vector3 alertNormal = Vector3.zero;
                            float maxSlopeDot = Mathf.Cos(Mathf.Deg2Rad * MaxSlopeAngle);
                            bool doProximityCheck = false;
                            using var hits = terrainAvoidanceHits.Take(hitCount).GetEnumerator();
                            while (hits.MoveNext())
                            {
                                if (hits.Current.collider.gameObject.GetComponentUpwards<DestructibleBuilding>() != null) // Hit a building.
                                {
                                    if (Vector3.Dot(hits.Current.normal, vesselDir) > 0) continue; // Ignore back-facing hits.
                                    if (Mathf.Abs(Vector3.Dot(hits.Current.normal, vessel.up)) > maxSlopeDot) continue; // Ignore slopes < MaxSlopeAngle.
                                    // Note: for spherecasts, colliders within the starting sphere have distance=0, point=Vector3.zero and normal=-ray.direction ... FFS Unity!
                                    if (hits.Current.distance > 0)
                                    {
                                        alertDistance = Mathf.Min(alertDistance, hits.Current.distance);
                                        var normal = hits.Current.normal;
                                        float collisionAngle = VectorUtils.Angle(vesselDir, -normal);
                                        if (hits.Current.distance < (100 - (100 * Mathf.Cos(collisionAngle - 90))) + (terrainAlertDetectionRadius / 2))
                                            normal = Vector3.Reflect(vesselDir, hits.Current.normal); // assuming a 100m turning circle, crashing Vee can wait to start turn depending on approach angle
                                        alertNormal += normal / (1 + hits.Current.distance * hits.Current.distance * (collisionAngle < 15 ? 1 : (collisionAngle / 90) * 18)); //weight normals in front of us more heavily than normals to the the vessel's side
                                        //should probably adjust to angle to width of craft at terrainAlertDetectionRadius. ATAN(TAN(vehicleWidth/terrainAlertDetectionRadius * 2)*2)? Since past that, collisions from the spherecast aren't in the way. Test later.
                                        ++validHitCount;
                                        if (BDArmorySettings.DEBUG_LINES) debugHits.Add((hits.Current.point, hits.Current.normal, Time.time));
                                    }
                                    else // The hit could be anywhere within the sphere centered on CoM, we need to do a short-range proximity check.
                                    {
                                        doProximityCheck = true;
                                    }
                                }
                            }
                            if (doProximityCheck)
                            {
                                for (int i = 0; i < 2; ++i)
                                {
                                    ray.origin = i switch // Just setting the origin avoids re-normalising the direction.
                                    {
                                        0 => vessel.CoM + vehicleWidth * vesselTransform.right,
                                        1 => vessel.CoM - vehicleWidth * vesselTransform.right,
                                        _ => vessel.CoM // Dummy to suppress switch complaining about not handling all integer cases
                                    };
                                    if (Physics.Raycast(ray, out RaycastHit hit, terrainAlertDetectionRadius + vehicleWidth, (int)LayerMasks.Scenery) // Hit something.
                                        && hit.collider.gameObject.GetComponentUpwards<DestructibleBuilding>() != null // Hit a building.
                                        && Vector3.Dot(hit.normal, ray.direction) < 0 // Ignore back-facing hits.
                                        && Mathf.Abs(Vector3.Dot(hit.normal, vessel.up)) < maxSlopeDot // Ignore slopes < MaxSlopeAngle.
                                    )
                                    {
                                        alertDistance = Mathf.Min(alertDistance, hit.distance);
                                        alertNormal += hit.normal / (1 + hit.distance * hit.distance);

                                        ++validHitCount;
                                        if (BDArmorySettings.DEBUG_LINES) debugHits.Add((hit.point, hit.normal, Time.time));
                                    }
                                }
                            }
                            if (wasReversing) // If reversing, also look directly ahead (but not as far) to keep tracking what we're reversing from.
                            {
                                if (Physics.Raycast(new Ray(vessel.CoM, vesselTransform.up), out RaycastHit hit, 5f * terrainAlertDetectionRadius, (int)LayerMasks.Scenery) // Hit something within 5 detection radii.
                                    && hit.collider.gameObject.GetComponentUpwards<DestructibleBuilding>() != null // Hit a building.
                                    && Vector3.Dot(hit.normal, vesselTransform.up) < 0 // Ignore back-facing hits.
                                    && Mathf.Abs(Vector3.Dot(hit.normal, vessel.up)) < maxSlopeDot // Ignore slopes < MaxSlopeAngle.
                                )
                                {
                                    alertDistance = Mathf.Min(alertDistance, hit.distance);
                                    alertNormal += hit.normal / (1 + hit.distance * hit.distance);
                                    ++validHitCount;
                                    reversingTurn = true;
                                    if (BDArmorySettings.DEBUG_LINES) debugHits.Add((hit.point, hit.normal, Time.time));
                                }
                            }
                            alertNormalAvg = Vector3.Slerp(alertNormal.normalized, alertNormalAvg, alertNormalAvgF); // Smooth out the alert normal direction.
                        }
                        else alertNormalAvg = Vector3.zero;
                        if (validHitCount > 0)
                        {
                            // Smooth out the dodge vector with our current heading to avoid over-correcting for things far away.
                            alertNormalAvg = Vector3.Slerp(alertNormalAvg.normalized, vesselDir, Mathf.Clamp(alertDistance / terrainAlertThreatRange, 0, 0.5f));
                            dodgeVector = vesselCollision ? (dodgeVector + alertNormalAvg).normalized : alertNormalAvg;
                            // Note, if heading straight at a wall, the yawError in AttitudeControl will handle pulling hard to the left or right.
                            if (!wasReversing && collisionTicker < 100 && (vessel.srfSpeed < 1 || alertDistance < vessel.srfSpeed) && Vector3.Dot(dodgeVector, vesselTransform.up) < -0.707f) // Close to hitting wall forwards (45°) => trigger reverse early
                            {
                                collisionTicker = -1;
                            }
                            else if (wasReversing && alertDistance < vessel.srfSpeed && Vector3.Dot(dodgeVector, vesselTransform.up) > 0.866f) // Close to hitting wall in reverse (30°) => abort reverse and delay reverse checks for 1s
                                collisionTicker = 150;
                            else if (wasReversing || vessel.srfSpeed < 1 || alertDistance < 1 * vessel.srfSpeed) // Reversing, stuck or about to crash in <1s
                                --collisionTicker;
                            else
                                collisionTicker = Math.Max(100, collisionTicker);
                        }
                        else if (collisionTicker < 0 || collisionTicker > 100) // was reversing or had been stuck, but no longer any valid hits => wait for reverse timer to expire or ticker to return to normal range.
                        {
                            --collisionTicker;
                        }
                        else
                            collisionTicker = Math.Max(100, collisionTicker);
                    }
                    /* collisionTicker thresholds (50 ticks == 1s):
                    *      > 100  recovery from being stuck in reverse, no early reversing checks
                    *    0 — 100  normal
                    * -250 — 0    reversing
                    *      < -250 reversing and maybe stuck?
                    */
                    if (collisionTicker < 0)
                    {
                        doReverse = true;
                        // Reversing typically has the dodgeVector pointing backwards relative to the vessel. We want to reverse in an arc peeling away from the normal by up to 90°.
                        if (reversingTurn && Vector3.Dot(dodgeVector, vesselDir) > 0)
                            dodgeVector = Vector3.RotateTowards(dodgeVector, -Mathf.Sign(Vector3.Dot(dodgeVector, vesselTransform.right)) * vesselTransform.right, Mathf.Deg2Rad * Mathf.Clamp(alertDistance * 2, 0, 90), 0); // Aim for a 45m arc.

                        if (collisionTicker < -250 && vessel.srfSpeed < 1) // Reversing for 5s and we seem to be stuck.
                        {
                            collisionTicker = 200;
                            doReverse = false;
                            reverseTicker = 0;
                        }
                        else if (vessel.srfSpeed > 1 && ++reverseTicker > (validHitCount == 0 ? 150 : 300)) // Have reversed above 1m/s for cumulative 3s with no hits or 6s.
                        {
                            collisionTicker = 150;
                            doReverse = false;
                            reverseTicker = 0;
                        }
                    }
                    else
                    {
                        reverseTicker = 0;
                    }
                }
                else
                {
                    --collisionDetectionTicker;
                }
                // avoid collisions if any are found
                if (vesselCollision || validHitCount > 0 || collisionTicker < 0 || collisionTicker > 100)
                {
                    // Lower speed when needing to turn sharply: 25% @ 180°, 75% @ 90°.
                    targetVelocity = (doReverse && (Vector3.Dot(vessel.vesselTransform.up, vessel.srf_vel_direction.ProjectOnPlanePreNormalized(upDir)) > 0)) ? -MaxSpeed  //we're still moving forward and need to be going backward
                        : Mathf.Clamp01(0.75f + 0.5f * Vector3.Dot(doReverse ? -vesselTransform.up : vesselTransform.up, dodgeVector)) * (doReverse ? -MaxSpeed : MaxSpeed);
                    targetDirection = (vesselCollision || validHitCount > 0) ? dodgeVector : vesselDir;
                    if (vesselCollision) SetStatus($"Avoiding Collision with {collidingWith}");
                    else SetStatus($"Avoiding Collision ({alertDistance:0}m)");
                    leftPath = true;
                    DebugLine($"Collision: {alertDistance:0.0}m / {terrainAlertThreatRange:0.0}m ({terrainAlertDetectionRadius:0.0}m, {validHitCount} hits), Reverse {doReverse} ({collisionTicker}), vel: {targetVelocity:0.0}m/s");
                    return;
                }
            }
            else { collisionDetectionTicker = 0; }

            // if bypass target is no longer relevant, remove it
            if (bypassTarget != null && ((bypassTarget != targetVessel && bypassTarget != (commandLeader != null ? commandLeader.vessel : null))
            || (VectorUtils.GetWorldSurfacePostion(bypassTargetPos, vessel.mainBody) - bypassTarget.CoM).sqrMagnitude > 500000))
            {
                bypassTarget = null;
            }

            if (bypassTarget == null)
            {
                // check for enemy targets and engage
                // not checking for guard mode, because if guard mode is off now you can select a target manually and if it is of opposing team, the AI will try to engage while you can man the turrets
                var weaponManager = WeaponManager;
                if (weaponManager != null && targetVessel != null && !BDArmorySettings.PEACE_MODE)
                {
                    leftPath = true;
                    if (collisionDetectionTicker == 5)
                        checkBypass(targetVessel);

                    Vector3 vecToTarget = targetVessel.CoM - vessel.CoM;
                    float distance = vecToTarget.magnitude;
                    // lead the target a bit, where 1km/s is a ballpark estimate of the average bullet velocity
                    float shotSpeed = 1000f;
                    if (weaponManager.selectedWeapon is ModuleWeapon wep)
                        shotSpeed = wep.bulletVelocity;
                    var timeToCPA = targetVessel.TimeToCPA(vessel.CoM, vessel.Velocity() + vesselTransform.up * shotSpeed, FlightGlobals.getGeeForceAtPosition(vessel.CoM), MaxEngagementRange / shotSpeed);
                    vecToTarget = targetVessel.PredictPosition(timeToCPA) - vessel.CoM;

                    if (SurfaceType == AIUtils.VehicleMovementType.Stationary)
                    {
                        if (distance >= MinEngagementRange && distance <= MaxEngagementRange)
                        {
                            targetDirection = vecToTarget;
                            aimingMode = AimingMode.Direct;
                        }
                        else
                        {
                            SetStatus("On Alert");
                            return;
                        }
                    }
                    else if (BroadsideAttack)
                    {
                        Vector3 sideVector = Vector3.Cross(vecToTarget, upDir); //find a vector perpendicular to direction to target
                        if (collisionDetectionTicker == 10
                                && !pathingMatrix.TraversableStraightLine(
                                        VectorUtils.WorldPositionToGeoCoords(vessel.CoM, vessel.mainBody),
                                        VectorUtils.WorldPositionToGeoCoords(vessel.PredictPosition(10), vessel.mainBody),
                                        vessel.mainBody, SurfaceType, MaxSlopeAngle, AvoidMass))
                            sideSlipDirection = -Math.Sign(Vector3.Dot(vesselTransform.up, sideVector)); // switch sides if we're running ashore
                        sideVector *= sideSlipDirection;

                        float sidestep = distance >= MaxEngagementRange ? Mathf.Clamp01((MaxEngagementRange - distance) / (CruiseSpeed * Mathf.Clamp(90 / MaxDrift, 0, 10)) + 1) * AttackAngleAtMaxRange / 90 : // direct to target to attackAngle degrees if over maxrange
                            (distance <= MinEngagementRange ? 1.5f - distance / (MinEngagementRange * 2) : // 90 to 135 degrees if closer than minrange
                            (MaxEngagementRange - distance) / (MaxEngagementRange - MinEngagementRange) * (1 - AttackAngleAtMaxRange / 90) + AttackAngleAtMaxRange / 90); // attackAngle to 90 degrees from maxrange to minrange
                        targetDirection = Vector3.LerpUnclamped(vecToTarget.normalized, sideVector.normalized, sidestep); // interpolate between the side vector and target direction vector based on sidestep
                        targetVelocity = MaxSpeed;
                        if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine($"Broadside attack angle {sidestep}");
                    }
                    else // just point at target and go
                    {
                        if (!maintainMinRange && (((targetVessel.horizontalSrfSpeed < 10) || Vector3.Dot(targetVessel.vesselTransform.up, vessel.vesselTransform.up) < 0 || orderedToExtend) //if target is stationary or we're facing in opposite directions
                            && (distance < MinEngagementRange || (distance < (MinEngagementRange * 3 + MaxEngagementRange) / 4 //and too close together
                            && extendingTarget != null && targetVessel != null && extendingTarget == targetVessel))))
                        {
                            extendingTarget = targetVessel;
                            // not sure if this part is very smart, potential for improvement
                            targetDirection = SurfaceType == AIUtils.VehicleMovementType.Water ? -vecToTarget + vessel.srf_vel_direction : -vecToTarget; //extend
                            targetVelocity = MaxSpeed;
                            if (distance > Mathf.Max(MaxEngagementRange / 2, 2000)) orderedToExtend = false;
                            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine($"Extending: ({distance:F2}/{Mathf.Max(MaxEngagementRange / 2, 2000)})");
                            SetStatus($"Extending {distance:0}m/{Mathf.Max(MaxEngagementRange / 2, 2000):0}m");
                            return;
                        }
                        else
                        {
                            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine($"velAngle: {VectorUtils.Angle(vessel.srf_vel_direction.ProjectOnPlanePreNormalized(vessel.up), vesselTransform.up):G3}");
                            extendingTarget = null;
                            targetDirection = vecToTarget.ProjectOnPlanePreNormalized(upDir);
                            if (weaponManager.selectedWeapon != null)
                            {
                                switch (weaponManager.selectedWeapon.GetWeaponClass())
                                {
                                    case WeaponClasses.Gun:
                                    case WeaponClasses.Rocket:
                                    case WeaponClasses.DefenseLaser:
                                        var gun = (ModuleWeapon)weaponManager.selectedWeapon;
                                        orderedToExtend = false;
                                        if (gun != null && (gun.yawRange == 0 || gun.maxPitch == gun.minPitch) && gun.FiringSolutionVector != null)
                                        {
                                            if (gun.yawRange == 0) aimingMode |= AimingMode.Yaw;
                                            if (gun.maxPitch == gun.minPitch) aimingMode |= AimingMode.Pitch; // FIXME: currently pitch isn't used directly in attitude control for aiming.
                                            if (VectorUtils.Angle((Vector3)gun.FiringSolutionVector, vessel.transform.up) < 20)
                                                targetDirection = (Vector3)gun.FiringSolutionVector;
                                        }
                                        break;
                                    case WeaponClasses.Bomb: //depthcharging subs from a ship
                                        {
                                            if (SurfaceType == AIUtils.VehicleMovementType.Water || SurfaceType == AIUtils.VehicleMovementType.Amphibious)
                                            {
                                                MissileBase bomb = weaponManager.CurrentMissile;

                                                targetDirection = (AIUtils.PredictPosition(targetVessel, weaponManager.bombAirTime) - vessel.CoM).ProjectOnPlanePreNormalized(upDir);
                                                aimingMode = AimingMode.Yaw;
                                            }
                                        }
                                        break;
                                    case WeaponClasses.SLW: //torpedo boats
                                        {
                                            if (SurfaceType == AIUtils.VehicleMovementType.Water || SurfaceType == AIUtils.VehicleMovementType.Amphibious)
                                            {
                                                MissileBase torpedo = weaponManager.CurrentMissile;
                                                if (torpedo != null)
                                                {
                                                    if (distance < torpedo.engageRangeMax + (float)(vessel.srf_velocity - targetVessel.srf_velocity).magnitude)
                                                    {
                                                        aimingMode = AimingMode.Direct;
                                                        targetDirection = (MissileGuidance.GetAirToAirFireSolution(torpedo, targetVessel) - vessel.CoM).ProjectOnPlanePreNormalized(upDir);
                                                    }
                                                    if (weaponManager.firedMissiles >= weaponManager.maxMissilesOnTarget)
                                                    {
                                                        targetVelocity = MaxSpeed; //torps away, get out of there
                                                        orderedToExtend = true;
                                                    }
                                                }
                                            }
                                        }
                                        break;
                                }
                            }
                            if (distance >= MaxEngagementRange || distance <= MinEngagementRange * 1.25f)
                            {
                                if (distance >= MaxEngagementRange)
                                    targetVelocity = MaxSpeed;//out of engagement range, engines ahead full
                                else if (distance <= MinEngagementRange * 1.25f) //coming within minEngagement range
                                {
                                    if (maintainMinRange) //for some reason ignored if both vessel and targetvessel using Mk2roverCans?
                                    {
                                        //Add LoS provisions if target is behind hill/building?
                                        if (weaponManager.staleTarget.ContainsKey(targetVessel))
                                        {
                                            if (!weaponManager.staleTarget[targetVessel])
                                            {
                                                if (distance <= MinEngagementRange) //rolled to a stop inside minRange/target has encroached
                                                {
                                                    //if (Vector3.Dot(vessel.vesselTransform.up, vessel.srf_vel_direction.ProjectOnPlanePreNormalized(upDir)) > 0) //we're still moving forward
                                                    //brakes = true;
                                                    //else brakes = false;//come to a stop and reversing, stop braking
                                                    if (Vector3.Dot(targetDirection, vesselTransform.up) < 0)
                                                    {
                                                        targetVelocity = MaxSpeed;
                                                        targetDirection = -targetDirection;
                                                        extendingTarget = targetVessel;
                                                        SetStatus($"Extending {distance:0}m/{MinEngagementRange:0}m");
                                                    }
                                                    else
                                                    {
                                                        doReverse = true;
                                                        targetVelocity = -MaxSpeed;
                                                        SetStatus($"Reversing");
                                                    }
                                                    return;
                                                }
                                                else if (vessel.srfSpeed < 0.1f * MaxSpeed && weaponManager && !weaponManager.recentlyFiring)
                                                {
                                                    if (distance < 1.125f * MinEngagementRange)
                                                    {
                                                        targetVelocity = -0.1f * MaxSpeed;
                                                        doReverse = true;
                                                    }
                                                    else
                                                    {
                                                        targetVelocity = 0.1f * MaxSpeed;
                                                    }
                                                    SetStatus($"Adjusting alignment");
                                                }
                                                else if (targetVessel.srfSpeed < 0.1f * MaxSpeed)
                                                {
                                                    targetVelocity = 0;
                                                    SetStatus($"Braking");
                                                }
                                                return;
                                            }
                                        }
                                        else Debug.LogError($"[BDArmory.SurfaceAI] weaponmanager.staleTarget does not contain {targetVessel.name}");
                                    }
                                    else
                                    {
                                        targetVelocity = MaxSpeed;
                                        if (weaponManager.selectedWeapon != null && (weaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.Bomb
                                            || weaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.SLW))
                                            orderedToExtend = true;
                                    }
                                }
                            }
                            else //within engagement envelope
                            {
                                targetVelocity = !maintainMinRange ? MaxSpeed : CruiseSpeed / 10 + (MaxSpeed - CruiseSpeed / 10) * (distance - MinEngagementRange) / (MaxEngagementRange - MinEngagementRange); //slow down if inside engagement range to extend shooting opportunities
                            }
                            targetVelocity = Mathf.Clamp(targetVelocity, PoweredSteering ? CruiseSpeed / 5 : (doReverse ? -MaxSpeed : 0), MaxSpeed); // maintain a bit of speed if using powered steering
                        }
                    }
                    SetStatus($"Engaging target");
                    return;
                }

                // follow
                if (command == PilotCommands.Follow && SurfaceType != AIUtils.VehicleMovementType.Stationary)
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
            }

            if (SurfaceType != AIUtils.VehicleMovementType.Stationary)
            {
                // goto
                if (command == PilotCommands.Waypoints)
                {
                    Pathfind(VectorUtils.WorldPositionToGeoCoords(waypointPosition, vessel.mainBody));
                }
                else if (leftPath && bypassTarget == null)
                {
                    Pathfind(finalPositionGeo);
                    leftPath = false;
                }

                const float targetRadius = 250f;
                targetDirection = (assignedPositionWorld - vesselTransform.position).ProjectOnPlanePreNormalized(upDir);

                if (command == PilotCommands.Waypoints || targetDirection.sqrMagnitude > targetRadius * targetRadius)
                {
                    if (bypassTarget != null)
                        targetVelocity = MaxSpeed;
                    else if (pathingWaypoints.Count > 1)
                        targetVelocity = (command == PilotCommands.Attack || command == PilotCommands.Waypoints) ? MaxSpeed : CruiseSpeed;
                    else
                    {
                        targetVelocity = command == PilotCommands.Waypoints ? MaxSpeed : Mathf.Clamp((targetDirection.magnitude - targetRadius / 2) / 5f,
                        0, command == PilotCommands.Attack ? MaxSpeed : CruiseSpeed);
                    }
                    //if targetDirection > VesselTurnRate reduce speed until vessel is slow enough to make turn ?
                    if (Vector3.Dot(targetDirection, vesselTransform.up) < 0 && !PoweredSteering) targetVelocity = 0;
                    SetStatus(bypassTarget ? "Repositioning" : "Moving");
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
            }

            SetStatus($"Not doing anything in particular");
            targetDirection = vesselTransform.up;
        }

        void Tactical()
        {
            var weaponManager = WeaponManager;
            // enable RCS if we're in combat
            vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, weaponManager != null && (
                targetVessel && !BDArmorySettings.PEACE_MODE && (
                    weaponManager.selectedWeapon != null || (vessel.CoM - targetVessel.CoM).sqrMagnitude < MaxEngagementRange * MaxEngagementRange
                ) || weaponManager.underFire || weaponManager.missileIsIncoming));

            // if weaponManager thinks we're under fire, do the evasive dance
            if (SurfaceType != AIUtils.VehicleMovementType.Stationary && weaponManager != null && (weaponManager.underFire || weaponManager.missileIsIncoming))
            {
                if (!maintainMinRange) targetVelocity = doReverse ? -MaxSpeed : MaxSpeed;
                if (weaponManager.underFire || weaponManager.incomingMissileDistance < 2500)
                {
                    if (Mathf.Abs(weaveAdjustment) + Time.deltaTime * WeaveFactor > weaveLimit * WeaveFactor) weaveDirection *= -1;
                    weaveAdjustment += WeaveFactor * weaveDirection * Time.deltaTime;
                }
                else
                {
                    weaveAdjustment = 0;
                }
            }
            else
            {
                weaveAdjustment = 0;
            }
            if ((BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) && weaponManager) DebugLine($"underFire {weaponManager.underFire}, weaveAdjustment {weaveAdjustment:G3}");
        }

        bool PanicModes()
        {
            var weaponManager = WeaponManager;
            if (!vessel.LandedOrSplashed && (!isHovercraft || isHovercraft && vessel.radarAltitude > MaxSlopeAngle * 3)) //FIXME - unlink hoverAlt from maxSlope, else low hover alt may prevent navigating steeper terrain
            {
                targetVelocity = 0;
                targetDirection = vessel.srf_velocity.ProjectOnPlanePreNormalized(upDir);
                SetStatus("Airtime!");
                return true;
            }
            else if (vessel.Landed
                && !vessel.Splashed // I'm looking at you, Kerbal Konstructs. (When launching directly into water, KK seems to set both vessel.Landed and vessel.Splashed to true.)
                && (SurfaceType & AIUtils.VehicleMovementType.Land) == 0)
            {
                targetVelocity = 0;
                SetStatus("Stranded");
                return true;
            }
            else if (
                (SurfaceType & AIUtils.VehicleMovementType.Land) != 0 && vessel.Landed //land Vee on land
                && weaponManager != null && weaponManager.guardMode && targetVessel != null //and under AI control 
                && (
                    currentStatusMode == StatusMode.RammingSpeed || !weaponManager.HasWeaponsAndAmmo() //and have been told to ram or doesn't have weapons
                    || !WeaponCanEngage(weaponManager.currentGun) //or have no guns, or only fixed guns/turrets unable to traverse to target, or out of range
                )
                && (Mathf.Abs(targetVelocity) > 0 && vessel.horizontalSrfSpeed < 1 && vessel.angularVelocity.sqrMagnitude < 4) //and engaging but immobilized and can't rotate to bring guns to bear. TODO: angularVel threshold value? Or is 2 ?deg/s? sufficient cutoff?
            )
            {
                //not setting targetVel to 0, since a craft at rest will take a few moments to accel to > 1m/s
                SetStatus("Disabled");
                return true;
            }
            else if (vessel.Splashed && !vessel.Landed && (SurfaceType & AIUtils.VehicleMovementType.Water) == 0)
            {
                targetVelocity = 0;
                SetStatus("Floating");
                return true;
            }
            else if (vessel.IsUnderwater() && SurfaceType != AIUtils.VehicleMovementType.Submarine // Only surface vessels.
                && !((SurfaceType & AIUtils.VehicleMovementType.Land) != 0 && vessel.Landed) // Unless they're driving on the bottom. FIXME Maybe add a hasPropulsion check to these? Note: this prevents panicking, but the pathing logic should try to get out of the water.
                && !(SurfaceType == AIUtils.VehicleMovementType.Water && !(vessel.Landed || vessel.verticalSpeed < 1)) // Or boats actively regaining the surface. FIXME Should this be allowed?
            )
            {
                targetVelocity = 0;
                SetStatus("Sunk");
                return true;
            }
            return false;
        }

        bool WeaponCanEngage(ModuleWeapon weapon)
        {
            if (!weapon) return false;
            if (weapon.turret)
            {
                return weapon.turret.TargetInRange(targetVessel.CoM - weapon.GetLeadOffset(), weapon.engageRangeMax);
            }
            else
            {
                Transform weaponTransform = weapon.fireTransforms[0];
                Vector3 vectorToTarget = (targetVessel.CoM - weapon.GetLeadOffset()) - weaponTransform.position;
                bool withinView = Vector3.Dot(weaponTransform.forward, vectorToTarget) >= weapon.targetAdjustedMaxCosAngle; // Fixed weapon within firing angle
                bool withinDistance = vectorToTarget.sqrMagnitude < weapon.engageRangeMax * weapon.engageRangeMax;
                return withinView && withinDistance;
            }
        }


        void AdjustThrottle(float targetSpeed)
        {
            targetSpeed = Mathf.Clamp(targetSpeed, doReverse ? -MaxSpeed : 0, MaxSpeed);
            float velocitySignedSrfSpeed = VectorUtils.Angle(vessel.srf_vel_direction.ProjectOnPlanePreNormalized(upDir), vesselTransform.up) < 110 ? (float)vessel.srfSpeed : -(float)vessel.srfSpeed;

            if (float.IsNaN(targetSpeed)) //because yeah, I might have left division by zero in there somewhere
            {
                targetSpeed = CruiseSpeed;
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine("Target velocity NaN, set to CruiseSpeed.");
            }
            else
            {
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine($"Target velocity: {targetSpeed}; signed Velocity: {velocitySignedSrfSpeed}; brakeVel: {velocitySignedSrfSpeed * 0.9f}; use brakes: {(targetSpeed < velocitySignedSrfSpeed * 0.9f)}");
            }
            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine($"engine thrust: {speedController.debugThrust}, motor zero: {motorControl.zeroPoint}");

            speedController.targetSpeed = motorControl.targetSpeed = targetSpeed;
            motorControl.signedSrfSpeed = velocitySignedSrfSpeed;
            //speedController.useBrakes = motorControl.preventNegativeZeroPoint = speedController.debugThrust > 0;
            speedController.useBrakes = targetSpeed * velocitySignedSrfSpeed < 0 || Mathf.Abs(targetSpeed) < Mathf.Abs(velocitySignedSrfSpeed) * 0.9f;
            motorControl.useBrakes = speedController.useBrakes;
        }

        Vector3 directionIntegral;
        float pitchIntegral = 0;

        void AttitudeControl(FlightCtrlState s)
        {
            const float terrainOffset = 5;

            Vector3 yawTarget = targetDirection.ProjectOnPlanePreNormalized(vesselTransform.forward);
            // Invert the yawTarget only if we're deliberately reversing and the target direction is behind us.
            // This puts the yawTarget ahead of us when we're deliberately reversing no matter the targetDirection.
            if (doReverse && Vector3.Dot(yawTarget, vesselTransform.up) < 0)
                yawTarget = -yawTarget;

            // limit "aoa" if we're moving
            float driftMult = 1;
            if (SurfaceType != AIUtils.VehicleMovementType.Stationary && vessel.horizontalSrfSpeed * 10 > CruiseSpeed)
            {
                Vector3d tempSrfVel = doReverse ? -vessel.srf_velocity : vessel.srf_velocity;
                if (Vector3.Dot(tempSrfVel, yawTarget) < 0) tempSrfVel = -tempSrfVel; // Avoid wrenching sideways when switching from forward to reverse and vice-versa.
                driftMult = Mathf.Max(VectorUtils.Angle(tempSrfVel, yawTarget) / MaxDrift, 1);
                yawTarget = Vector3.RotateTowards(tempSrfVel, yawTarget, MaxDrift * Mathf.Deg2Rad, 0);
            }
            float yawError = VectorUtils.GetAngleOnPlane(yawTarget, vesselTransform.up, vesselTransform.right);

            // Reverse the angle if we're going backwards to steer correctly.
            bool invertCtrlPoint = SurfaceType != AIUtils.VehicleMovementType.Stationary && vessel.srfSpeed > 0.1f && Vector3.Dot(vessel.srf_vel_direction.ProjectOnPlanePreNormalized(vessel.up), vesselTransform.up) < 0;
            if (invertCtrlPoint) yawError = -yawError;

            // If we don't have a fixed-yaw weapon, add in some weaving.
            if ((aimingMode & AimingMode.Yaw) == 0) yawError += weaveAdjustment;

            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI)
            {
                DebugLine($"yaw target: {yawTarget}, yaw error: {yawError:G3}, invertCtrlPoint: {invertCtrlPoint}");
                DebugLine($"drift multiplier: {driftMult}");
            }

            float pitchError;
            if (SurfaceType != AIUtils.VehicleMovementType.Stationary)
            {
                if (SurfaceType == AIUtils.VehicleMovementType.Submarine)
                {
                    float targetAlt = CombatAltitude;
                    var weaponManager = WeaponManager;
                    if (weaponManager != null && weaponManager.currentTarget != null && weaponManager.selectedWeapon != null)
                    {
                        switch (weaponManager.selectedWeapon.GetWeaponClass())
                        {
                            case WeaponClasses.Missile:
                                {
                                    targetAlt = -10; //come to periscope depth for missile launch
                                    break;
                                }
                            case WeaponClasses.Gun:
                                {
                                    if (BDArmorySettings.BULLET_WATER_DRAG)
                                    {
                                        if (weaponManager.currentTarget.isSplashed || ((weaponManager.currentTarget.isFlying || weaponManager.currentTarget.Vessel.situation == Vessel.Situations.LANDED) && weaponManager.currentGun.turret))
                                        {
                                            if (vessel.CoM.FurtherFromThan(weaponManager.currentTarget.Vessel.CoM, weaponManager.selectedWeapon.GetEngageRange()))
                                                targetAlt = -10; //come to periscope depth in preparation for surface attack when in range
                                            else
                                                targetAlt = 1;//in range, surface to engage with deck guns
                                        }
                                    }
                                    else
                                    {
                                        if (weaponManager.currentTarget.Vessel.situation == Vessel.Situations.LANDED && weaponManager.currentGun.turret) //surface for shooting land targets with turrets
                                        {
                                            if (vessel.CoM.FurtherFromThan(weaponManager.currentTarget.Vessel.CoM, weaponManager.selectedWeapon.GetEngageRange()))
                                                targetAlt = -10; //come to periscope depth in preparation for surface attack when in range
                                            else
                                                targetAlt = 1;//in range, surface to engage with deck guns
                                        }
                                        if (!weaponManager.currentGun.turret && weaponManager.currentTarget.isSplashed)
                                        {
                                            if (!doExtend)
                                            {
                                                if (weaponManager.currentTarget.Vessel.altitude < CombatAltitude / 4 && vessel.CoM.FurtherFromThan(weaponManager.currentTarget.Vessel.CoM, 200)) //200m
                                                {
                                                    targetAlt = (float)weaponManager.currentTarget.Vessel.altitude; //engaging enemy sub or ship, but break off when too close to target or surface
                                                }
                                                else
                                                    doExtend = true;
                                            }
                                            else
                                            {
                                                if (vessel.altitude < (CombatAltitude * .66f) || vessel.CoM.FurtherFromThan(weaponManager.currentTarget.Vessel.CoM, 1000)) doExtend = false;
                                            }
                                        }
                                        //else remain at combat depth and engage with turrets.
                                    }
                                    break;
                                }
                            case WeaponClasses.Rocket:
                            case WeaponClasses.DefenseLaser:
                                {
                                    if (weaponManager.currentTarget.Vessel.situation == Vessel.Situations.LANDED || weaponManager.currentTarget.isFlying && weaponManager.currentGun.turret)
                                    {
                                        if (vessel.CoM.FurtherFromThan(weaponManager.currentTarget.Vessel.CoM, weaponManager.selectedWeapon.GetEngageRange()))
                                            targetAlt = -10; //come to periscope depth in preparation for surface attack when in range
                                        else
                                            targetAlt = 1; //surface to engage with turrets
                                    }
                                    if (weaponManager.currentTarget.isSplashed)
                                    {
                                        if (!doExtend)
                                        {
                                            if (weaponManager.currentTarget.Vessel.altitude < CombatAltitude / 4 && vessel.CoM.FurtherFromThan(weaponManager.currentTarget.Vessel.CoM, 200))
                                            {
                                                targetAlt = (float)weaponManager.currentTarget.Vessel.altitude; //engaging enemy sub or ship, but break off when too close
                                            }
                                            else
                                                doExtend = true;
                                        }
                                        else
                                        {
                                            if (vessel.altitude < (CombatAltitude * .66f) || vessel.CoM.FurtherFromThan(weaponManager.currentTarget.Vessel.CoM, 1000)) doExtend = false;
                                        }
                                    }
                                    break;
                                }
                            default: //SLW
                                break;
                        }
                        //if (weaponManager.missileIsIncoming && !weaponManager.incomingMissileVessel.LandedOrSplashed && targetAlt > -10) targetAlt = -10; //this might make subs too hard to kill?
                    }
                    //look into some sort of crash dive routine if under fire from enemies dropping depthcharges/air-dropped torps?
                    float pitchAngle;
                    if ((float)vessel.altitude > targetAlt) pitchAngle = -MaxSlopeAngle * (1 - ((float)vessel.altitude / targetAlt)); //may result in not reaching target depth, depending on how neutrally buoyant the sub is. Clamp to maxSlopeAngle if Dist(vessel.altitude, targetAlt) > combatAlt * 0.25 or similar?
                    else pitchAngle = MaxSlopeAngle * (1 - (targetAlt / (float)vessel.altitude));
                    float pitch = 90 - VectorUtils.Angle(vesselTransform.up, upDir);

                    pitchError = pitchAngle - pitch;
                    if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine($"Target Alt: {targetAlt.ToString("F3")}: PitchAngle: {pitchAngle.ToString("F3")}, Pitch: {pitch.ToString("F3")}, PitchError: {pitchError.ToString("F3")}");

                    directionIntegral = (directionIntegral + (pitchError * -vesselTransform.forward + yawError * vesselTransform.right) * Time.deltaTime).ProjectOnPlanePreNormalized(vesselTransform.up);
                    if (directionIntegral.sqrMagnitude > 1f) directionIntegral = directionIntegral.normalized;
                    pitchIntegral = 0.4f * Vector3.Dot(directionIntegral, -vesselTransform.forward);
                }
                else
                {
                    pitchError = Vector3.SignedAngle(upDir.ProjectOnPlanePreNormalized(vesselTransform.right), -vesselTransform.forward, vesselTransform.right);
                    if (pitchError > 0) pitchError = Mathf.Max(pitchError - MaxSlopeAngle, 0);
                    else pitchError = Mathf.Min(pitchError + MaxSlopeAngle, 0);
                    if ((BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) && pitchError != 0) DebugLine($"pitch error: {pitchError}");
                }
            }
            else
            {
                // Stationary vessels should align with the terrain to avoid constantly running reaction wheels.
                pitchError = VectorUtils.GetAngleOnPlane(terrainNormal, -vesselTransform.forward, -vesselTransform.up);
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine($"pitch error: {pitchError}");
            }
            float rollError;
            if (SurfaceType != AIUtils.VehicleMovementType.Stationary)
            {
                Vector3 baseLateral = vessel.transform.right * terrainOffset;
                float baseRoll = Mathf.Atan2(
                    AIUtils.GetTerrainAltitude(vessel.CoM + baseLateral, vessel.mainBody, false)
                    - AIUtils.GetTerrainAltitude(vessel.CoM - baseLateral, vessel.mainBody, false),
                    terrainOffset * 2) * Mathf.Rad2Deg;
                float drift = VectorUtils.GetAngleOnPlane(vessel.GetSrfVelocity(), vesselTransform.up, vesselTransform.right);
                float bank = -VectorUtils.GetAngleOnPlane(upDir, -vesselTransform.forward, vesselTransform.right);
                float targetRoll = baseRoll + BankAngle * Mathf.Clamp01(drift / MaxDrift) * Mathf.Clamp01((float)vessel.srfSpeed / CruiseSpeed);
                rollError = targetRoll - bank;
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine($"terrain sideways slope: {baseRoll}, target roll: {targetRoll}");
            }
            else
            {
                // Stationary vessels should align with the terrain to avoid constantly running reaction wheels.
                rollError = VectorUtils.GetAngleOnPlane(terrainNormal, -vesselTransform.forward, vesselTransform.right);
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine($"roll error: {rollError}");
            }

            Vector3 localAngVel = vessel.angularVelocity;
            SetFlightControlState(s,
                Mathf.Clamp((((aimingMode & AimingMode.Pitch) > 0 ? 0.02f : 0.015f) * steerMult * pitchError) + pitchIntegral - (steerDamping * -localAngVel.x), -2, 2), // pitch
                Mathf.Clamp(((((aimingMode & AimingMode.Yaw) > 0 ? 0.007f : 0.005f) * steerMult * yawError) - (steerDamping * 0.2f * -localAngVel.z)) * driftMult, -2, 2), // yaw
                steerMult * 0.006f * rollError - 0.4f * steerDamping * -localAngVel.y, // roll
                -Mathf.Clamp((((aimingMode & AimingMode.Yaw) > 0 ? 0.005f : 0.003f) * steerMult * yawError) - (steerDamping * 0.1f * -localAngVel.z), -2, 2) // wheel steer
            );

            if (ManeuverRCS && (Mathf.Abs(s.roll) >= 1 || Mathf.Abs(s.pitch) >= 1 || Mathf.Abs(s.yaw) >= 1))
                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);
        }

        protected void SetFlightControlState(FlightCtrlState s, float pitch, float yaw, float roll, float wheelSteer)
        {
            base.SetFlightControlState(s, pitch, yaw, roll);
            s.wheelSteer = wheelSteer;
            if (hasAxisGroupsModule)
            {
                axisGroupsModule.UpdateAxisGroup(KSPAxisGroup.WheelSteer, wheelSteer);
            }

        }
        #endregion Actual AI Pilot

        #region Autopilot helper functions

        public override bool CanEngage()
        {
            if (SurfaceType == AIUtils.VehicleMovementType.Stationary) // Stationary can shoot at whatever it can see without moving.
            {
                return true;
            }
            else if (vessel.Splashed && ((SurfaceType & AIUtils.VehicleMovementType.Water) == 0 || (SurfaceType & AIUtils.VehicleMovementType.Submarine) == 0))
            {
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine(vessel.vesselName + " cannot engage: land vehicle in water");
            }
            else if (vessel.Landed && (SurfaceType & AIUtils.VehicleMovementType.Land) == 0)
            {
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine(vessel.vesselName + " cannot engage: water vehicle on land");
            }
            else if (!vessel.LandedOrSplashed && !isHovercraft)
            {
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine(vessel.vesselName + " cannot engage: vessel not on surface");
            }
            // the motorControl part fails sometimes, and guard mode then decides not to select a weapon
            // figure out what is wrong with motor control before uncommenting :D
            // else if (speedController.debugThrust + (motorControl?.MaxAccel ?? 0) <= 0)
            // {
            //     if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) DebugLine(vessel.vesselName + " cannot engage: no engine power");
            // }
            else
                return true;
            return false;
        }

        public override bool IsValidFixedWeaponTarget(Vessel target)
            => !BroadsideAttack &&
            (((target != null && target.Splashed) && (SurfaceType & AIUtils.VehicleMovementType.Water) != 0) //boat targeting boat
            || ((target != null && target.Landed) && (SurfaceType & AIUtils.VehicleMovementType.Land) != 0) //vee targeting vee
            || (((target != null && !target.LandedOrSplashed) && (SurfaceType & AIUtils.VehicleMovementType.Amphibious) != 0) && isHovercraft)) //repulsorcraft targeting repulsorcraft
            ; //valid if can traverse the same medium and using bow fire

        /// <returns>null if no collision, dodge vector if one detected</returns>
        Vector3 PredictCollisionWithVessel(Vessel v, float maxTime, float interval)
        {
            //evasive will handle avoiding missiles
            var weaponManager = WeaponManager;
            if ((weaponManager != null && v == weaponManager.incomingMissileVessel)
                || v.rootPart.FindModuleImplementing<MissileBase>() != null)
                return Vector3.zero;

            float time = Mathf.Min(0.5f, maxTime);
            while (time < maxTime)
            {
                Vector3 tPos = v.PredictPosition(time);
                Vector3 myPos = vessel.PredictPosition(time);
                float radii = v.GetRadius() + vessel.GetRadius();
                if ((tPos - myPos).sqrMagnitude < 2 * radii * radii)
                {
                    return Vector3.Dot(tPos - myPos, vesselTransform.right) > 0 ? -vesselTransform.right : vesselTransform.right;
                }

                time = Mathf.MoveTowards(time, maxTime, interval);
            }

            return Vector3.zero;
        }

        void checkBypass(Vessel target)
        {
            if (!pathingMatrix.TraversableStraightLine(
                    VectorUtils.WorldPositionToGeoCoords(vessel.CoM, vessel.mainBody),
                    VectorUtils.WorldPositionToGeoCoords(target.CoM, vessel.mainBody),
                    vessel.mainBody, SurfaceType, MaxSlopeAngle, AvoidMass))
            {
                bypassTarget = target;
                bypassTargetPos = VectorUtils.WorldPositionToGeoCoords(target.CoM, vessel.mainBody);
                pathingWaypoints = pathingMatrix.Pathfind(
                    VectorUtils.WorldPositionToGeoCoords(vessel.CoM, vessel.mainBody),
                    VectorUtils.WorldPositionToGeoCoords(target.CoM, vessel.mainBody),
                    vessel.mainBody, SurfaceType, MaxSlopeAngle, AvoidMass);
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
                                    destination, vessel.mainBody, SurfaceType, MaxSlopeAngle, AvoidMass);
            intermediatePositionGeo = pathingWaypoints[0];
            //any sort of spline stuff would need to modify this value.
            //Spline calc would likely also need to determine if the first couple pathingWaypoints are multiple points beween the craft and 
            //the next WP Gate (sloping/uneven terrain), or a straight shot between WP Gates. if (pathingWaypoints.count > 1)?
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
            return commandLeader.vessel.CoM + Quaternion.LookRotation(commandLeader.vessel.up, upDir) * commandLeader.GetFormationPosition(commandFollowIndex);
        }

        #endregion WingCommander
    }
}