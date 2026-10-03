using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ModuleWheels;

using BDArmory.Competition;
using BDArmory.Extensions;
using BDArmory.GameModes.Waypoints;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;

namespace BDArmory.Control
{
    /// <summary>
    /// A base class for implementing AI.
    /// Note: You do not have to use it, it is just for convenience, all the game cares about is that you implement the IBDAIControl interface.
    /// </summary>
    public abstract class BDGenericAIBase : BDAPartModule, IBDAIControl, IBDWMModule
    {
        #region declarations
        public virtual AIType aiType => AIType.GenericAI;
        public bool pilotEnabled => pilotOn;

        // separate private field for pilot On, because properties cannot be KSPFields
        [KSPField(isPersistant = true)]
        public bool pilotOn;
        protected Vessel activeVessel;

        public MissileFire WeaponManager
        {
            get
            {
                if (field == null || !field.IsPrimaryWM || field.vessel != vessel)
                    field = vessel && vessel.loaded ? vessel.ActiveController().WM : null;
                return field;
            }
        }

        /// <summary>
        /// The default is BDAirspeedControl. If you want to use something else, just override ActivatePilot  (and, potentially, DeactivatePilot), and make it use something else.
        /// </summary>
        protected BDAirspeedControl speedController;
        public float originalMaxSpeed = -1;
        protected bool hasAxisGroupsModule = false;
        protected AxisGroupsModule axisGroupsModule;

        protected Transform vesselTransform => vessel.ReferenceTransform;

        protected StringBuilder debugString = new StringBuilder();

        protected Vessel targetVessel;

        protected virtual Vector3d assignedPositionGeo { get; set; }

        public Vector3d assignedPositionWorld
        {
            get
            {
                return VectorUtils.GetWorldSurfacePostion(assignedPositionGeo, vessel.mainBody);
            }
            protected set
            {
                assignedPositionGeo = VectorUtils.WorldPositionToGeoCoords(value, vessel.mainBody);
            }
        }

        //wing commander
        public ModuleWingCommander commandLeader
        {
            get
            {
                if (field == null || field.vessel == null || !field.vessel.isActiveAndEnabled) return null; // Vessel's don't immediately become null on dying if they're the active vessel.
                return field;
            }
            protected set { field = value; }
        }

        protected PilotCommands command;
        PilotCommands previousCommand;
        public string currentStatus { get; protected set; } = "Free";
        public int commandFollowIndex { get; protected set; } = -1;
        public float commandWingAttackRange { get; protected set; } = 10000f;

        public PilotCommands currentCommand => command;
        public virtual Vector3d commandGPS => assignedPositionGeo;

        #endregion declarations

        public abstract bool CanEngage();

        public abstract bool IsValidFixedWeaponTarget(Vessel target);

        /// <summary>
        /// This will be called every update and should run the autopilot logic.
        ///
        /// For simple use cases:
        ///		1. Engage your target (get in position to engage, shooting is done by guard mode)
        ///		2. If no target, check command, and follow it
        ///		Do this by setting s.pitch, s.yaw and s.roll.
        ///
        /// For advanced use cases you probably know what you're doing :P
        /// </summary>
        /// <param name="s">current flight control state</param>
        protected abstract void AutoPilot(FlightCtrlState s);

        // A small wrapper to make sure the autopilot does not do anything when it shouldn't
        private void autoPilot(FlightCtrlState s)
        {
            debugString.Length = 0;
            if (!vessel || !vessel.transform || vessel.packed || !vessel.mainBody)
                return;
            //vessel lost command parts from damage?
            if (!vessel.isCommandable) //isCommandable is only false when there is *no* command parts on the vessel (cockpits/probecores/etc)
            {
                DeactivatePilot();
                debugString.AppendLine($"Vessel: No Command parts!");
                if (vessel.Autopilot.Enabled) Debug.Log("[BDArmory.BDGenericAIBase]: " + vessel.vesselName + " is not commandable, disabling autopilot.");
                s.NeutralizeStick();
                vessel.Autopilot.Disable();
                return;
            }
            // nobody is controlling any more possibly due to G forces?
            if (!vessel.IsControllable) //false when probes out of EC/cockpits don't have pilots
            {
                debugString.AppendLine($"Vessel: No Control!");
                if (vessel.Autopilot.Enabled) Debug.Log("[BDArmory.BDGenericAIBase]: " + vessel.vesselName + " is not controllable, disabling autopilot.");
                s.NeutralizeStick();
                vessel.Autopilot.Disable();
                return;
            }

            // generally other AI and guard mode expects this target to be engaged
            GetGuardTarget(); // get the guard target from weapon manager
            GetNonGuardTarget(); // if guard mode is off, get the UI target
            GetGuardNonTarget(); // pick a target if guard mode is on, but no target is selected,
                                 // though really targeting should be managed by the weaponManager, what if we pick an airplane while having only abrams cannons? :P
                                 // (this is another reason why target selection is hardcoded into the base class, so changing this later is less of a mess :) )

            AutoPilot(s);
        }

        /// <summary>
        /// Set the flight control state and also the corresponding axis groups.
        /// </summary>
        /// <param name="s">The flight control state</param>
        /// <param name="pitch">pitch</param>
        /// <param name="yaw">yaw</param>
        /// <param name="roll">roll</param>
        protected virtual void SetFlightControlState(FlightCtrlState s, float pitch, float yaw, float roll)
        {
            s.pitch = pitch;
            s.yaw = yaw;
            s.roll = roll;
            if (hasAxisGroupsModule)
            {
                axisGroupsModule.UpdateAxisGroup(KSPAxisGroup.Pitch, pitch);
                axisGroupsModule.UpdateAxisGroup(KSPAxisGroup.Yaw, yaw);
                axisGroupsModule.UpdateAxisGroup(KSPAxisGroup.Roll, roll);
            }
        }

        #region Pilot on/off

        /// <summary>
        /// Activate the AI.
        /// </summary>
        /// <returns>true if successfully activated, false otherwise.</returns>
        public virtual bool ActivatePilot()
        {
            pilotOn = true;
            vessel.ActiveController().UpdateAIModules(); // Update the active AI module.
            if (!pilotOn) return false; // If it's not us, abort.
            if (activeVessel)
                activeVessel.OnFlyByWire -= autoPilot;
            activeVessel = vessel;
            activeVessel.OnFlyByWire += autoPilot;

            if (!speedController)
            {
                speedController = gameObject.AddComponent<BDAirspeedControl>();
                speedController.vessel = vessel;
            }

            speedController.Activate();

            GameEvents.onVesselDestroy.Remove(RemoveAutopilot);
            GameEvents.onVesselDestroy.Add(RemoveAutopilot);

            if (command == PilotCommands.Free) assignedPositionWorld = vessel.ReferenceTransform.position;
            try // Sometimes the FSM breaks trying to set the gear action group
            {
                // Make sure the FSM is started for deployable wheels. (This should hopefully fix the FSM errors.)
                foreach (var part in VesselModuleRegistry.GetModules<ModuleWheelDeployment>(vessel).Where(part => part != null && part.fsm != null && !part.fsm.Started))
                {
                    if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDAGenericAIBase]: Starting FSM with state {(string.IsNullOrEmpty(part.fsm.currentStateName) ? "Retracted" : part.fsm.currentStateName)} on {part.name} of {part.vessel.vesselName}");
                    part.fsm.StartFSM(part.fsm.CurrentState ?? new KFSMState("Retracted"));
                }
                // I need to make sure gear is deployed on startup so it'll get properly retracted.
                vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, true);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BDArmory.BDGenericAIBase]: Failed to set Gear action group on {vessel.vesselName}: {e.Message}");
            }
            RefreshPartWindow();
            return true;
        }

        public virtual void DeactivatePilot()
        {
            pilotOn = false;
            if (activeVessel)
                activeVessel.OnFlyByWire -= autoPilot;
            RefreshPartWindow();

            if (speedController)
            {
                speedController.Deactivate();
            }
        }

        protected void RemoveAutopilot(Vessel v)
        {
            if (v == vessel)
            {
                v.OnFlyByWire -= autoPilot;
            }
        }

        protected void RefreshPartWindow()
        {
            Events[nameof(TogglePilot)].guiName = pilotEnabled ? StringUtils.Localize("#LOC_BDArmory_DeactivatePilot") : StringUtils.Localize("#LOC_BDArmory_ActivatePilot");//"Deactivate Pilot""Activate Pilot"
        }

        [KSPEvent(guiActive = true, guiName = "#LOC_BDArmory_TogglePilot", active = true)]//Toggle Pilot
        public void TogglePilot()
        {
            if (pilotEnabled)
            {
                DeactivatePilot();
            }
            else
            {
                vessel.ActiveController().SetActiveAI(this);
            }
        }

        [KSPAction("Activate Pilot")]
        public void AGActivatePilot(KSPActionParam param) => vessel.ActiveController().SetActiveAI(this);

        [KSPAction("Deactivate Pilot")]
        public void AGDeactivatePilot(KSPActionParam param) => DeactivatePilot();

        [KSPAction("Toggle Pilot")]
        public void AGTogglePilot(KSPActionParam param) => TogglePilot();

        public virtual string Name { get; } = "AI Control";
        public bool Enabled => pilotEnabled;

        public bool TakingOff = true;

        public void Toggle() => TogglePilot();

        #endregion Pilot on/off

        #region events

        protected virtual void Start()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                part.OnJustAboutToBeDestroyed += DeactivatePilot;
                GameEvents.onVesselWasModified.Add(onVesselWasModified);
                GameEvents.onPartDie.Add(OnPartDie);

                activeVessel = vessel;
                axisGroupsModule = vessel.FindVesselModuleImplementingBDA<AxisGroupsModule>(); // Look for an axis group module so we can set the axis groups when setting the flight control state.
                if (axisGroupsModule != null) hasAxisGroupsModule = true;

                if (pilotEnabled)
                {
                    ActivatePilot();
                }
            }

            RefreshPartWindow();
        }

        void OnPartDie() { OnPartDie(part); }
        protected virtual void OnPartDie(Part p)
        {
            if (part == p)
            {
                Destroy(this); // Force this module to be removed from the gameObject as something is holding onto part references and causing a memory leak.
            }
        }

        protected virtual void OnDestroy()
        {
            part.OnJustAboutToBeDestroyed -= DeactivatePilot;
            GameEvents.onVesselWasModified.Remove(onVesselWasModified);
            GameEvents.onVesselDestroy.Remove(RemoveAutopilot);
            GameEvents.onPartDie.Remove(OnPartDie);
        }

        protected virtual void OnGUI()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (!pilotEnabled || !vessel || !vessel.isActiveVessel) return;

            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI)
            {
                GUI.Label(new Rect(200, Screen.height - 350, 600, 350), $"{vessel.vesselName} UUID: {vessel.id}\n{debugString.ToString()}");
            }
        }

        protected virtual void onVesselWasModified(Vessel v)
        {
            if (v != activeVessel)
                return;

            activeVessel = vessel;
        }

        #endregion events

        #region utilities

        protected void GetGuardTarget()
        {
            var weaponManager = WeaponManager;
            if (weaponManager == null) return;
            if (weaponManager.guardMode && weaponManager.currentTarget != null)
            {
                targetVessel = weaponManager.currentTarget.Vessel;
            }
            else
            {
                targetVessel = null;
            }
        }

        /// <summary>
        /// If guard mode is set but no target is selected, pick something
        /// </summary>
        protected virtual void GetGuardNonTarget()
        {
            var weaponManager = WeaponManager;
            if (weaponManager && weaponManager.guardMode && !targetVessel)
            {
                // select target based on competition style
                TargetInfo potentialTarget = BDArmorySettings.DEFAULT_FFA_TARGETING ? BDATargetManager.GetClosestTargetWithBiasAndHysteresis(weaponManager) : BDATargetManager.GetLeastEngagedTarget(weaponManager);
                if (potentialTarget && potentialTarget.Vessel)
                {
                    targetVessel = potentialTarget.Vessel;
                }
            }
        }

        /// <summary>
        /// If guard mode off, and UI target is of the opposing team, set it as target
        /// </summary>
        protected void GetNonGuardTarget()
        {
            var weaponManager = WeaponManager;
            if (weaponManager != null && !weaponManager.guardMode)
            {
                if (vessel.targetObject != null)
                {
                    var nonGuardTargetVessel = vessel.targetObject.GetVessel();
                    if (nonGuardTargetVessel != null)
                    {
                        var targetWeaponManager = nonGuardTargetVessel.ActiveController().WM;
                        if (targetWeaponManager != null && weaponManager.Team.IsEnemy(targetWeaponManager.Team))
                            targetVessel = (Vessel)vessel.targetObject;
                    }
                }
            }
        }

        /// <summary>
        /// Write some text to the debug field (the one on lower left when debug labels are on), followed by a newline.
        /// </summary>
        /// <param name="text">text to write</param>
        protected void DebugLine(string text)
        {
            debugString.AppendLine(text);
        }

        protected virtual void SetStatus(string text)
        {
            currentStatus = text;
            // DebugLine(text);
        }

        #endregion utilities

        #region WingCommander

        public virtual void ReleaseCommand(bool resetAssignedPosition = true, bool storeCommand = false)
        {
            if (!vessel || command == PilotCommands.Free) return;
            if (BDArmorySettings.DEBUG_AI) Debug.Log("[BDArmory.BDGenericAIBase]:" + vessel.vesselName + " was released from command.");
            previousCommand = command;
            command = PilotCommands.Free;
            if (commandLeader != null) commandLeader.RemoveWingman(this);

            if (!storeCommand) // Clear the previous command.
            {
                commandLeader = null;
                commandFollowIndex = -1;
                previousCommand = PilotCommands.Free;
            }
            if (resetAssignedPosition) // Clear the assigned position.
            {
                assignedPositionWorld = vesselTransform.position;
            }
        }

        public virtual void CommandFollow(ModuleWingCommander leader, int followerIndex)
        {
            if (!pilotEnabled) return;
            if (leader is null || leader == vessel || followerIndex < 0) return;

            if (BDArmorySettings.DEBUG_AI) Debug.Log("[BDArmory.BDGenericAIBase]:" + vessel.vesselName + " was commanded to follow.");
            previousCommand = command;
            command = PilotCommands.Follow;
            commandLeader = leader;
            commandFollowIndex = followerIndex;
            leader.AddWingman(this);
        }

        public virtual void CommandAG(KSPActionGroup ag)
        {
            if (!pilotEnabled) return;
            vessel.ActionGroups.ToggleGroup(ag);
        }

        public virtual void CommandFlyTo(Vector3 gpsCoords)
        {
            if (!pilotEnabled) return;

            if (BDArmorySettings.DEBUG_AI && (command != PilotCommands.FlyTo || (gpsCoords - assignedPositionGeo).sqrMagnitude > 0.1)) Debug.Log($"[BDArmory.BDGenericAIBase]: {vessel.vesselName} was commanded to go to {gpsCoords}.");
            assignedPositionGeo = gpsCoords;
            previousCommand = command;
            command = PilotCommands.FlyTo;
        }

        public virtual void CommandAttack(Vector3 gpsCoords)
        {
            if (!pilotEnabled) return;

            if (BDArmorySettings.DEBUG_AI && (command != PilotCommands.Attack || (gpsCoords - assignedPositionGeo).sqrMagnitude > 0.1)) Debug.Log($"[BDArmory.BDGenericAIBase]: {vessel.vesselName} was commanded to attack {gpsCoords}.");
            assignedPositionGeo = gpsCoords;
            previousCommand = command;
            command = PilotCommands.Attack;
            if (WeaponManager is var wm && wm != null && !wm.guardMode) wm.ToggleGuardMode();
        }

        // Call with followerIndex=-1 for the leader.
        public virtual void CommandWingAttack(ModuleWingCommander leader, int followerIndex, Vector3 gpsCoords, float breakRange)
        {
            if (!pilotEnabled) return;

            if (BDArmorySettings.DEBUG_AI && (command != PilotCommands.WingAttack || (gpsCoords - assignedPositionGeo).sqrMagnitude > 0.1)) Debug.Log($"[BDArmory.BDGenericAIBase]: {vessel.vesselName} was commanded to wing attack {gpsCoords}.");
            assignedPositionGeo = gpsCoords;
            previousCommand = command;
            command = PilotCommands.WingAttack;
            commandLeader = leader;
            commandFollowIndex = followerIndex;
            commandWingAttackRange = breakRange;
            if (commandFollowIndex > -1)
            {
                leader.AddWingman(this);
            }
            if (WeaponManager is var wm && wm != null && !wm.guardMode) wm.ToggleGuardMode();
        }

        public virtual void CommandTakeOff()
        {
            ActivatePilot();
        }

        public virtual void CommandFollowWaypoints()
        {
            if (!pilotEnabled) return; // Do nothing if we haven't taken off (or activated with airspawn) yet.

            if (BDArmorySettings.DEBUG_AI) Debug.Log("[BDArmory.BDGenericAIBase]:" + vessel.vesselName + " was commanded to follow waypoints.");
            previousCommand = command;
            command = PilotCommands.Waypoints;
        }

        /// <summary>
        /// Resume a previous command.
        /// ReleaseCommand should be called with resetAssignedPosition=false if the previous command is to be preserved.
        /// </summary>
        /// <returns>true if the previous command is resumed, false otherwise.</returns>
        public virtual bool ResumeCommand()
        {
            switch (previousCommand)
            {
                case PilotCommands.Free:
                    return false;
                case PilotCommands.Attack:
                    CommandAttack(assignedPositionGeo);
                    break;
                case PilotCommands.WingAttack:
                    {
                        // Check that the WingAttack command is still valid before resuming it.
                        if (commandLeader != null && WeaponManager is var wm && wm != null && (
                            commandFollowIndex == -1 || // We're the leader.
                            commandFollowIndex >= 0 && commandLeader.WeaponManager is var cwm && cwm != null && cwm.Team == wm.Team // The leader is still alive and on our team.
                        ))
                        {
                            var leaderAI = commandLeader.vessel.ActiveController().AI;
                            CommandWingAttack(commandLeader, commandFollowIndex, assignedPositionGeo, leaderAI != null ? leaderAI.commandWingAttackRange : wm.guardRange);
                        }
                        else // Otherwise, revert to Free.
                        {
                            commandLeader = null;
                            commandFollowIndex = -1;
                            previousCommand = PilotCommands.Free;
                            return false;
                        }
                    }
                    break;
                case PilotCommands.FlyTo:
                    CommandFlyTo(assignedPositionGeo);
                    break;
                case PilotCommands.Follow:
                    {
                        // Check that the follow command is still valid before resuming it.
                        if (commandLeader != null && commandFollowIndex >= 0 && WeaponManager is var wm && wm != null && commandLeader.WeaponManager is var cwm && cwm != null && cwm.Team == wm.Team)
                        {
                            CommandFollow(commandLeader, commandFollowIndex);
                        }
                        else // Otherwise, revert to Free.
                        {
                            commandLeader = null;
                            commandFollowIndex = -1;
                            previousCommand = PilotCommands.Free;
                            return false;
                        }
                    }
                    break;
                case PilotCommands.Waypoints:
                    CommandFollowWaypoints();
                    break;
            }
            return true;
        }
        #endregion WingCommander

        #region Waypoints
        protected List<Vector3> waypoints = null;
        protected int waypointCourseIndex = 0;
        protected int activeWaypointIndex = -1;
        protected int activeWaypointLap = 1;
        protected int waypointLapLimit = 1;
        protected Vector3 waypointPosition = default;
        //protected float waypointRadius = 500f;
        public float waypointRange = 999f;

        public bool IsRunningWaypoints => command == PilotCommands.Waypoints &&
            activeWaypointLap <= waypointLapLimit &&
            activeWaypointIndex >= 0 &&
            waypoints != null &&
            waypoints.Count > 0;
        public int CurrentWaypointIndex => this.activeWaypointIndex;

        public void ClearWaypoints()
        {
            if (BDArmorySettings.DEBUG_AI) Debug.Log("[BDArmory.BDGenericAIBase]: Cleared waypoints");
            this.waypoints = null;
            this.activeWaypointIndex = -1;
        }

        public void SetWaypoints(List<Vector3> waypoints)
        {
            if (waypoints == null || waypoints.Count == 0)
            {
                this.activeWaypointIndex = -1;
                this.waypoints = null;
                return;
            }
            if (BDArmorySettings.DEBUG_AI) Debug.Log(string.Format("[BDArmory.BDGenericAIBase]: Set {0} waypoints", waypoints.Count));
            this.waypoints = waypoints;
            this.waypointCourseIndex = BDArmorySettings.WAYPOINT_COURSE_INDEX;
            this.activeWaypointIndex = 0;
            this.activeWaypointLap = 1;
            this.waypointLapLimit = BDArmorySettings.WAYPOINT_LOOP_INDEX;
            var waypoint = waypoints[activeWaypointIndex];
            var terrainAltitude = FlightGlobals.currentMainBody.TerrainAltitude(waypoint.x, waypoint.y);
            waypointPosition = FlightGlobals.currentMainBody.GetWorldSurfacePosition(waypoint.x, waypoint.y, waypoint.z + terrainAltitude);
            CommandFollowWaypoints();
        }

        protected virtual void UpdateWaypoint()
        {
            if (activeWaypointIndex < 0 || waypoints == null || waypoints.Count == 0)
            {
                if (command == PilotCommands.Waypoints) ReleaseCommand();
                return;
            }
            var waypoint = waypoints[activeWaypointIndex];
            var terrainAltitude = FlightGlobals.currentMainBody.TerrainAltitude(waypoint.x, waypoint.y);
            waypointPosition = FlightGlobals.currentMainBody.GetWorldSurfacePosition(waypoint.x, waypoint.y, waypoint.z + terrainAltitude);
            waypointRange = (float)(vesselTransform.position - waypointPosition).magnitude;
            var timeToCPA = AIUtils.TimeToCPA(vessel.transform.position - waypointPosition, vessel.Velocity(), vessel.acceleration, Time.fixedDeltaTime);
            if (waypointRange < (BDArmorySettings.WAYPOINTS_SCALE > 0 ? BDArmorySettings.WAYPOINTS_SCALE : (WaypointCourses.CourseLocations[waypointCourseIndex].waypoints[activeWaypointIndex].scale)) && timeToCPA < Time.fixedDeltaTime) // Within waypointRadius and reaching a minimum within the next frame. Looking forwards like this avoids a frame where the fly-to direction is backwards allowing smoother waypoint traversal.
            {
                // moving away, proceed to next point
                var deviation = AIUtils.PredictPosition(vessel.transform.position - waypointPosition, vessel.Velocity(), vessel.acceleration, timeToCPA).magnitude;
                if (BDArmorySettings.DEBUG_AI) Debug.Log(string.Format("[BDArmory.BDGenericAIBase]: Reached waypoint {0} with range {1}; active index{2} of {3}", activeWaypointIndex, deviation, activeWaypointIndex * (activeWaypointLap - 1), waypoints.Count * waypointLapLimit));
                BDACompetitionMode.Instance.Scores.RegisterWaypointReached(vessel.vesselName, waypointCourseIndex, activeWaypointIndex, activeWaypointLap, waypointLapLimit, deviation);

                var weaponManager = WeaponManager;
                if (BDArmorySettings.WAYPOINT_GUARD_INDEX >= 0 && !weaponManager.guardMode && activeWaypointIndex + (waypoints.Count * (activeWaypointLap - 1)) >= Mathf.Min(BDArmorySettings.WAYPOINT_GUARD_INDEX, waypoints.Count * waypointLapLimit)) //allow guard activating, i.e. halfway through lap2), guarantee guard activation after last guate
                {
                    // activate guard mode   
                    weaponManager.guardMode = true;
                }

                ++activeWaypointIndex;
                if (activeWaypointIndex >= waypoints.Count)
                {
                    activeWaypointIndex = 0;
                    activeWaypointLap++;

                    if (activeWaypointLap > waypointLapLimit)
                    {
                        if (BDArmorySettings.DEBUG_AI) Debug.Log("[BDArmory.BDGenericAIBase]: Waypoints complete");
                        waypoints = null;
                        ReleaseCommand();
                        if (BDArmorySettings.WAYPOINT_GUARD_INDEX >= 0 && !weaponManager.guardMode) weaponManager.guardMode = true;
                        return;
                    }
                }
                UpdateWaypoint(); // Call ourselves again for the new waypoint to follow.
                //Modify AI maxSpeed if the gate we just pased has a speed limit
                float mSpeed = WaypointCourses.CourseLocations[waypointCourseIndex].waypoints[activeWaypointIndex].maxSpeed;
                var ai = vessel.ActiveController().AI;
                if (ai != null) switch (ai.aiType)
                    {
                        case AIType.PilotAI:
                            var pilotAI = ai as BDModulePilotAI;
                            pilotAI.maxSpeed = mSpeed > 0 ? mSpeed : originalMaxSpeed;
                            pilotAI.OnMaxSpeedChanged();
                            break;
                        case AIType.SurfaceAI:
                            var surfaceAI = ai as BDModuleSurfaceAI;
                            surfaceAI.MaxSpeed = mSpeed > 0 ? mSpeed : originalMaxSpeed;
                            break;
                        case AIType.VTOLAI:
                            var vtolAI = ai as BDModuleVTOLAI;
                            vtolAI.MaxSpeed = mSpeed > 0 ? mSpeed : originalMaxSpeed;
                            break;
                        case AIType.OrbitalAI:
                            // var orbitalAI = ai as BDModuleOrbitalAI;
                            // orbitalAI.ManeuverSpeed = mSpeed > 0 ? mSpeed : originalMaxSpeed; //Don' think WPs would work, period, with an orbital reference frame?
                            break;
                    }
            }
        }

        Coroutine maintainingFuelLevelsCoroutine;
        Coroutine maintainingWaypointFuelLevelsCoroutine;
        /// <summary>
        /// Prevent fuel resource drain until the next waypoint.
        /// </summary>
        public void MaintainFuelLevelsUntilWaypoint()
        {
            if (maintainingWaypointFuelLevelsCoroutine != null) StopCoroutine(maintainingWaypointFuelLevelsCoroutine);
            maintainingWaypointFuelLevelsCoroutine = StartCoroutine(MaintainFuelLevelsUntilWaypointCoroutine());
        }
        /// <summary>
        /// Prevent fuel resource drain until the next waypoint (coroutine).
        /// Note: this should probably use the non-waypoint version below and just start/stop it based on the waypoint index.
        /// </summary>
        IEnumerator MaintainFuelLevelsUntilWaypointCoroutine()
        {
            if (vessel == null) yield break;
            /*
            var vesselName = vessel.vesselName;
            var wait = new WaitForFixedUpdate();
            var fuelResourceParts = new Dictionary<string, HashSet<PartResource>>();
            var currentWaypointIndex = CurrentWaypointIndex;
            ResourceUtils.DeepFind(vessel.rootPart, ResourceUtils.FuelResources, fuelResourceParts, true);
            var fuelResources = fuelResourceParts.ToDictionary(t => t.Key, t => t.Value.ToDictionary(p => p, p => p.amount));
            while (vessel != null && IsRunningWaypoints && CurrentWaypointIndex == currentWaypointIndex)
            {
                foreach (var fuelResource in fuelResources.Values)
                {
                    foreach (var partResource in fuelResource.Keys)
                    { partResource.amount = fuelResource[partResource]; }
                }
                yield return wait;
            }
            */
            MaintainFuelLevels(true);
            var wait = new WaitForFixedUpdate();
            var currentWaypointIndex = CurrentWaypointIndex;
            while (vessel != null && IsRunningWaypoints && CurrentWaypointIndex == currentWaypointIndex)
            {
                yield return wait;
            }
            MaintainFuelLevels(false);
        }
        #endregion
        /// <summary>
        /// Prevent fuel drain (control function).
        /// Note: CheatOptions.InfinitePropellant doesn't work for FS helicopter engines, so we need to maintain fuel manually.
        /// </summary>
        /// <param name="active">Activate or deactive fuel preservation.</param>
        public void MaintainFuelLevels(bool active)
        {
            if (maintainingFuelLevelsCoroutine != null) StopCoroutine(maintainingFuelLevelsCoroutine);
            if (active) maintainingFuelLevelsCoroutine = StartCoroutine(MaintainFuelLevelsCoroutine());
        }
        /// <summary>
        /// Prevent fuel drain (coroutine).
        /// </summary>
        /// <returns></returns>
        IEnumerator MaintainFuelLevelsCoroutine()
        {
            if (vessel == null) yield break;
            var wait = new WaitForFixedUpdate();
            var fuelResourceParts = new Dictionary<string, HashSet<PartResource>>();
            ResourceUtils.DeepFind(vessel.rootPart, ResourceUtils.FuelResources, fuelResourceParts, true);
            var fuelResources = fuelResourceParts.ToDictionary(t => t.Key, t => t.Value.ToDictionary(p => p, p => p.amount));
            while (vessel != null)
            {
                foreach (var fuelResource in fuelResources.Values)
                {
                    foreach (var partResource in fuelResource.Keys)
                    { partResource.amount = fuelResource[partResource]; }
                }
                yield return wait;
            }
        }

        Coroutine disableBattleDamageCoroutine = null;
        public void DisableBattleDamage(bool active)
        {
            if (!active && disableBattleDamageCoroutine != null)
            {
                StopCoroutine(disableBattleDamageCoroutine);
                disableBattleDamageCoroutine = null;
                BDArmorySettings.BATTLEDAMAGE = true;
            }
            if (active && BDArmorySettings.BATTLEDAMAGE)
            {
                BDArmorySettings.BATTLEDAMAGE = false;
                disableBattleDamageCoroutine = StartCoroutine(new WaitWhile(() => true));
            }
        }
    }
}
