using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KSP.Localization;

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
    public abstract class BDGenericAIBase : PartModule, IBDAIControl, IBDWMModule
    {
        #region declarations

        public bool pilotEnabled => pilotOn;

        // separate private field for pilot On, because properties cannot be KSPFields
        [KSPField(isPersistant = true)]
        public bool pilotOn;
        protected Vessel activeVessel;

        public MissileFire weaponManager { get; protected set; }

        /// <summary>
        /// The default is BDAirspeedControl. If you want to use something else, just override ActivatePilot  (and, potentially, DeactivatePilot), and make it use something else.
        /// </summary>
        protected BDAirspeedControl speedController;

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
        public ModuleWingCommander commandLeader { get; protected set; }

        protected PilotCommands command;
        PilotCommands previousCommand;
        public string currentStatus { get; protected set; } = "Free";
        protected int commandFollowIndex;

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
            if (!weaponManager || !vessel || !vessel.transform || vessel.packed || !vessel.mainBody)
                return;
            // nobody is controlling any more possibly due to G forces?
            if (!vessel.isCommandable)
            {
                if (vessel.Autopilot.Enabled) Debug.Log("[BDArmory.BDGenericAIBase]: " + vessel.vesselName + " is not commandable, disabling autopilot.");
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

        #region Pilot on/off

        public virtual void ActivatePilot()
        {
            pilotOn = true;
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

            assignedPositionWorld = vessel.ReferenceTransform.position;
            // I need to make sure gear is deployed on startup so it'll get properly retracted.
            vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, true);
            RefreshPartWindow();
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
            Events["TogglePilot"].guiName = pilotEnabled ? Localizer.Format("#LOC_BDArmory_DeactivatePilot") : Localizer.Format("#LOC_BDArmory_ActivatePilot");//"Deactivate Pilot""Activate Pilot"
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
                ActivatePilot();
            }
        }

        [KSPAction("Activate Pilot")]
        public void AGActivatePilot(KSPActionParam param) => ActivatePilot();

        [KSPAction("Deactivate Pilot")]
        public void AGDeactivatePilot(KSPActionParam param) => DeactivatePilot();

        [KSPAction("Toggle Pilot")]
        public void AGTogglePilot(KSPActionParam param) => TogglePilot();

        public virtual string Name { get; } = "AI Control";
        public bool Enabled => pilotEnabled;

        public void Toggle() => TogglePilot();

        #endregion Pilot on/off

        #region events

        protected virtual void Start()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                part.OnJustAboutToBeDestroyed += DeactivatePilot;
                vessel.OnJustAboutToBeDestroyed += DeactivatePilot;
                GameEvents.onVesselWasModified.Add(onVesselWasModified);
                MissileFire.OnChangeTeam += OnToggleTeam;
                GameEvents.onPartDie.Add(OnPartDie);

                activeVessel = vessel;
                UpdateWeaponManager();

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
            if (vessel != null) vessel.OnJustAboutToBeDestroyed -= DeactivatePilot;
            GameEvents.onVesselWasModified.Remove(onVesselWasModified);
            GameEvents.onVesselDestroy.Remove(RemoveAutopilot);
            MissileFire.OnChangeTeam -= OnToggleTeam;
            GameEvents.onPartDie.Remove(OnPartDie);
        }

        protected virtual void OnGUI()
        {
            if (!pilotEnabled || !vessel.isActiveVessel) return;
            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI)
            {
                GUI.Label(new Rect(200, Screen.height - 300, 600, 300), $"{vessel.name}\n{debugString.ToString()}");
            }
        }

        protected virtual void OnToggleTeam(MissileFire mf, BDTeam team)
        {
            if (mf.vessel == vessel || (commandLeader && commandLeader.vessel == mf.vessel))
            {
                ReleaseCommand();
            }
        }

        protected virtual void onVesselWasModified(Vessel v)
        {
            if (v != activeVessel)
                return;

            if (vessel != activeVessel)
            {
                if (activeVessel)
                    activeVessel.OnJustAboutToBeDestroyed -= DeactivatePilot;
                if (vessel)
                    vessel.OnJustAboutToBeDestroyed += DeactivatePilot;
                if (weaponManager != null && weaponManager.vessel == activeVessel)
                {
                    if (this.Equals(weaponManager.AI))
                        weaponManager.AI = null;
                    UpdateWeaponManager();
                }
            }

            activeVessel = vessel;
        }

        #endregion events

        #region utilities

        protected void UpdateWeaponManager()
        {
            VesselModuleRegistry.OnVesselModified(vessel);
            weaponManager = VesselModuleRegistry.GetModule<MissileFire>(vessel);
            if (weaponManager != null)
                weaponManager.AI = this;
        }

        protected void GetGuardTarget()
        {
            if (weaponManager == null || weaponManager.vessel != vessel)
                UpdateWeaponManager();
            if (weaponManager != null && weaponManager.vessel == vessel)
            {
                if (weaponManager.guardMode && weaponManager.currentTarget != null)
                {
                    targetVessel = weaponManager.currentTarget.Vessel;
                }
                else
                {
                    targetVessel = null;
                }
                weaponManager.AI = this;
                return;
            }
        }

        /// <summary>
        /// If guard mode is set but no target is selected, pick something
        /// </summary>
        protected virtual void GetGuardNonTarget()
        {
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
            if (weaponManager != null && !weaponManager.guardMode)
            {
                if (vessel.targetObject != null)
                {
                    var nonGuardTargetVessel = vessel.targetObject.GetVessel();
                    if (nonGuardTargetVessel != null)
                    {
                        var targetWeaponManager = VesselModuleRegistry.GetModule<MissileFire>(nonGuardTargetVessel);
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

        protected void SetStatus(string text)
        {
            currentStatus = text;
            // DebugLine(text);
        }

        #endregion utilities

        #region WingCommander

        public virtual void ReleaseCommand(bool resetAssignedPosition = true, bool storeCommand = true)
        {
            if (!vessel || command == PilotCommands.Free) return;
            if (BDArmorySettings.DEBUG_AI) Debug.Log("[BDArmory.BDGenericAIBase]:" + vessel.vesselName + " was released from command.");
            previousCommand = command;
            command = PilotCommands.Free;

            if (!storeCommand) // Clear the previous command.
            {
                if (previousCommand == PilotCommands.Follow) commandLeader = null;
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
        }

        public virtual void CommandAG(KSPActionGroup ag)
        {
            if (!pilotEnabled) return;
            vessel.ActionGroups.ToggleGroup(ag);
        }

        public virtual void CommandFlyTo(Vector3 gpsCoords)
        {
            if (!pilotEnabled) return;

            if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDGenericAIBase]: {vessel.vesselName} was commanded to go to {gpsCoords}.");
            assignedPositionGeo = gpsCoords;
            previousCommand = command;
            command = PilotCommands.FlyTo;
        }

        public virtual void CommandAttack(Vector3 gpsCoords)
        {
            if (!pilotEnabled) return;

            if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDGenericAIBase]: {vessel.vesselName} was commanded to attack {gpsCoords}.");
            assignedPositionGeo = gpsCoords;
            previousCommand = command;
            command = PilotCommands.Attack;
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
                case PilotCommands.FlyTo:
                    CommandFlyTo(assignedPositionGeo);
                    break;
                case PilotCommands.Follow:
                    CommandFollow(commandLeader, commandFollowIndex);
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
        protected int activeWaypointIndex = -1;
        protected int activeWaypointLap = 1;
        protected int waypointLapLimit = 1;
        protected Vector3 waypointPosition = default;
        //protected float waypointRadius = 500f;
        protected float waypointRange = 999f;

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
            var timeToCPA = AIUtils.ClosestTimeToCPA(vessel.transform.position - waypointPosition, vessel.Velocity(), vessel.acceleration, Time.fixedDeltaTime);
            if (waypointRange < WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].waypoints[activeWaypointIndex].scale && timeToCPA < Time.fixedDeltaTime) // Within waypointRadius and reaching a minimum within the next frame. Looking forwards like this avoids a frame where the fly-to direction is backwards allowing smoother waypoint traversal.
            {
                // moving away, proceed to next point
                var deviation = AIUtils.PredictPosition(vessel.transform.position - waypointPosition, vessel.Velocity(), vessel.acceleration, timeToCPA).magnitude;
                if (BDArmorySettings.DEBUG_AI) Debug.Log(string.Format("[BDArmory.BDGenericAIBase]: Reached waypoint {0} with range {1}", activeWaypointIndex, deviation));
                BDACompetitionMode.Instance.Scores.RegisterWaypointReached(vessel.vesselName, activeWaypointIndex, activeWaypointLap, deviation);

                if (BDArmorySettings.WAYPOINT_GUARD_INDEX >= 0 && activeWaypointIndex >= BDArmorySettings.WAYPOINT_GUARD_INDEX && !weaponManager.guardMode)
                {
                    // activate guard mode
                    weaponManager.guardMode = true;
                }

                ++activeWaypointIndex;
                if (activeWaypointIndex >= waypoints.Count && activeWaypointLap > waypointLapLimit)
                {
                    if (BDArmorySettings.DEBUG_AI) Debug.Log("[BDArmory.BDGenericAIBase]: Waypoints complete");
                    waypoints = null;
                    ReleaseCommand();
                    return;
                }
                else if (activeWaypointIndex >= waypoints.Count && activeWaypointLap <= waypointLapLimit)
                {
                    activeWaypointIndex = 0;
                    activeWaypointLap++;
                }
                UpdateWaypoint(); // Call ourselves again for the new waypoint to follow.
            }
        }

        Coroutine maintainingFuelLevelsCoroutine;
        /// <summary>
        /// Prevent fuel resource drain until the next waypoint.
        /// </summary>
        public void MaintainFuelLevelsUntilWaypoint()
        {
            if (maintainingFuelLevelsCoroutine != null) StopCoroutine(maintainingFuelLevelsCoroutine);
            maintainingFuelLevelsCoroutine = StartCoroutine(MaintainFuelLevelsUntilWaypointCoroutine());
        }
        /// <summary>
        /// Prevent fuel resource drain until the next waypoint (coroutine).
        /// Note: this should probably use the non-waypoint version below and just start/stop it based on the waypoint index.
        /// </summary>
        IEnumerator MaintainFuelLevelsUntilWaypointCoroutine()
        {
            if (vessel == null) yield break;
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
        }
        #endregion

        /// <summary>
        /// Prevent fuel drain (control function).
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
    }
}
