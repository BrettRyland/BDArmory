using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KSP.UI.Screens;

using BDArmory.Competition;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;

namespace BDArmory.VesselSpawning
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class VesselMover : VesselSpawnerBase
    {
        public static VesselMover Instance;
        public static ApplicationLauncherButton button;
        public static bool buttonSetup;

        enum State { None, Moving, Lowering, Spawning };
        State state
        {
            get { return _state; }
            set { _state = value; ResetWindowHeight(); }
        }
        State _state;
        internal WaitForFixedUpdate wait = new();
        HashSet<Vessel> movingVessels = [];
        HashSet<Vessel> loweringVessels = [];
        readonly List<float> jumpToAltitudes = [10, 100, 1000, 10000, 50000];
        RaycastHit[] hits = new RaycastHit[10];

        #region Monobehaviour routines
        protected override void Awake()
        {
            base.Awake();
            if (Instance != null) Destroy(Instance);
            Instance = this;
        }

        void Start()
        {
            ready = false;
            StartCoroutine(WaitForBdaSettings());
            ConfigureStyles();
            SetupMoveIndicator();
            GameEvents.onVesselChange.Add(OnVesselChanged);

            if (BDArmorySettings.VM_TOOLBAR_BUTTON) AddToolbarButton();
        }

        void SetupMoveIndicator()
        {
            moveIndicator = new GameObject().AddComponent<LineRenderer>();
            moveIndicator.material = new Material(Shader.Find("KSP/Emissive/Diffuse"));
            moveIndicator.material.SetColor("_EmissiveColor", Color.green);
            moveIndicator.startWidth = 0.15f;
            moveIndicator.endWidth = 0.15f;
            moveIndicator.enabled = false;
            moveIndicator.positionCount = circleRes + 3;
        }

        private IEnumerator WaitForBdaSettings()
        {
            yield return new WaitUntil(() => BDArmorySettings.ready);

            BDArmorySetup.WindowRectVesselMover.height = 0;
            if (guiCheckIndex < 0) guiCheckIndex = GUIUtils.RegisterGUIRect(new Rect());
            if (_vesselGUICheckIndex < 0) _vesselGUICheckIndex = GUIUtils.RegisterGUIRect(new Rect());
            if (_crewGUICheckIndex < 0) _crewGUICheckIndex = GUIUtils.RegisterGUIRect(new Rect());
            ready = true;
            BDArmorySetup.Instance.hasVesselMover = true;
            SetVisible(BDArmorySetup.showVesselMoverGUI);
        }

        void OnDestroy()
        {
            GameEvents.onVesselChange.Remove(OnVesselChanged);
        }

        void Update()
        {
            if (state != State.None && FlightGlobals.ActiveVessel == null) state = State.None;
            if (state == State.Moving && IsMoving(FlightGlobals.ActiveVessel))
                HandleInput(); // Input needs to be handled in Update.
        }

        void LateUpdate()
        {
            if (state == State.Moving && !MapView.MapIsEnabled && BDArmorySetup.GAME_UI_ENABLED)
            {
                moveIndicator.enabled = true;
                DrawMovingIndicator();
            }
            else moveIndicator.enabled = false;
        }
        #endregion

        #region Input
        Vector3 positionAdjustment = Vector3.zero; // X, Y, Z
        Vector3 rotationAdjustment = Vector3.zero; // Roll, Yaw, Pitch
        bool translating = false;
        bool rotating = false;
        bool jump = false;
        bool jumpDirection = false; // false = down, true = up
        bool reset = false;
        bool autoLevelPlane = false;
        bool autoLevelRocket = false;
        void HandleInput()
        {
            positionAdjustment = Vector3.zero;
            rotationAdjustment = Vector3.zero;
            translating = false;
            rotating = false;
            autoLevelPlane = false;
            autoLevelRocket = false;

            if (!MapView.MapIsEnabled) // No altitude changes while in map mode.
            {
                if (GameSettings.THROTTLE_CUTOFF.GetKeyDown()) // Reset altitude to base.
                { reset = true; }
                else if (Input.GetKeyDown(KeyCode.Tab)) // Jump to next reference altitude.
                {
                    jump = true;
                    jumpDirection = GameSettings.THROTTLE_UP.GetKey();
                }
                else if (GameSettings.THROTTLE_UP.GetKey()) // Increase altitude.
                {
                    positionAdjustment.z = 1f;
                    translating = true;
                }
                else if (GameSettings.THROTTLE_DOWN.GetKey()) // Decrease altitude.
                {
                    positionAdjustment.z = -1f;
                    translating = true;
                }
            }

            if (GameSettings.PITCH_DOWN.GetKey()) // Translate forward.
            {
                positionAdjustment.y = 1f;
                translating = true;
            }
            else if (GameSettings.PITCH_UP.GetKey()) // Translate backward.
            {
                positionAdjustment.y = -1f;
                translating = true;
            }

            if (GameSettings.YAW_RIGHT.GetKey()) // Translate right.
            {
                positionAdjustment.x = 1f;
                translating = true;
            }
            else if (GameSettings.YAW_LEFT.GetKey()) // Translate left.
            {
                positionAdjustment.x = -1f;
                translating = true;
            }

            if (GameSettings.TRANSLATE_FWD.GetKey()) // Auto-level plane
            { autoLevelPlane = true; rotating = true; }
            else if (GameSettings.TRANSLATE_BACK.GetKey()) // Auto-level rocket
            { autoLevelRocket = true; rotating = true; }
            else
            {
                if (GameSettings.ROLL_LEFT.GetKey()) // Roll left.
                {
                    rotationAdjustment.x = -1f;
                    rotating = true;
                }
                else if (GameSettings.ROLL_RIGHT.GetKey()) // Roll right.
                {
                    rotationAdjustment.x = 1f;
                    rotating = true;
                }

                if (GameSettings.TRANSLATE_DOWN.GetKey()) // Pitch down.
                {
                    rotationAdjustment.z = 1f;
                    rotating = true;
                }
                else if (GameSettings.TRANSLATE_UP.GetKey()) // Pitch up.
                {
                    rotationAdjustment.z = -1f;
                    rotating = true;
                }

                if (GameSettings.TRANSLATE_RIGHT.GetKey()) // Yaw right.
                {
                    rotationAdjustment.y = 1f;
                    rotating = true;
                }
                else if (GameSettings.TRANSLATE_LEFT.GetKey()) // Yaw left.
                {
                    rotationAdjustment.y = -1f;
                    rotating = true;
                }
            }
        }
        #endregion

        #region Moving
        Vector3d geoCoords;
        public bool IsValid(Vessel vessel) => vessel != null && vessel.loaded && !vessel.packed;
        public bool IsMoving(Vessel vessel) => IsValid(vessel) && movingVessels.Contains(vessel);
        public bool IsLowering(Vessel vessel) => IsValid(vessel) && loweringVessels.Contains(vessel);
        IEnumerator MoveVessel(Vessel vessel)
        {
            if (!IsValid(vessel)) { state = State.None; yield break; }
            if (BDArmorySettings.DEBUG_SPAWNING) Debug.Log($"[BDArmory.VesselMover]: Moving {vessel.vesselName}");
            movingVessels.Add(vessel);
            state = State.Moving;

            var hadPatchedConicsSolver = vessel.PatchedConicsAttached;
            if (hadPatchedConicsSolver)
            {
                if (BDArmorySettings.DEBUG_SPAWNING) Debug.Log($"[BDArmory.VesselMover]: Detaching patched conic solver");
                try
                {
                    vessel.DetachPatchedConicsSolver();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[BDArmory.VesselMover]: Failed to remove the Patched Conic Solver: {e.Message}");
                }
            }

            // Disable various action groups. We'll enable some of them again later if specified.
            vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, false); // Disable RCS
            if (BDArmorySettings.VESSEL_MOVER_ENABLE_SAS) vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false); // Disable SAS
            if (BDArmorySettings.VESSEL_MOVER_ENABLE_BRAKES) vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, false); // Disable Brakes
            foreach (LaunchClamp clamp in vessel.FindPartModulesImplementing<LaunchClamp>()) clamp.Release(); // Release clamps
            KillRotation(vessel);
            if (BDArmorySettings.VESSEL_MOVER_ENABLE_BRAKES) vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);

            var initialUp = (vessel.transform.position - FlightGlobals.currentMainBody.transform.position).normalized;
            var up = initialUp;
            Vector3 forward = default, right = default;
            float startingAltitude = 2f * vessel.GetRadius();
            var lowerBound = GetLowerBound(vessel);
            var safeAlt = SafeAltitude(vessel, lowerBound);
            var position = vessel.transform.position;
            var rotation = vessel.transform.rotation;
            var referenceTransform = vessel.ReferenceTransform;
            if (LandedOrSplashed(vessel) || startingAltitude > safeAlt) // Careful with initial separation from ground.
            {
                var count = 0;
                while (IsMoving(vessel) && ++count <= 10)
                {
                    vessel.Landed = false;
                    vessel.Splashed = false;
                    vessel.IgnoreGForces(240);
                    vessel.IgnoreSpeed(240);
                    position += count * (startingAltitude - safeAlt) / 55f * up;
                    vessel.SetPosition(position);
                    vessel.SetVelocity(Vector3.zero);
                    vessel.SetRotation(rotation);
                    yield return wait;
                    KrakensbaneCorrection(ref position);
                }
            }

            KillRotation(vessel);
            float moveSpeed = 0;
            float rotateSpeed = 0;
            var coordinateFrameAdjustAngle = Mathf.Cos(Mathf.Deg2Rad * 0.1f);
            float preMapViewAltitude = 0;
            while (IsMoving(vessel))
            {
                if (vessel.isActiveVessel)
                {
                    if (translating || autoLevelPlane || autoLevelRocket)
                    {
                        up = (vessel.transform.position - FlightGlobals.currentMainBody.transform.position).normalized;
                        if (Vector3.Dot(initialUp, up) < coordinateFrameAdjustAngle) // Up changed by > coordinateFrameAdjustAngle: rotate the vessel and reset initialUp.
                        {
                            rotation = Quaternion.FromToRotation(initialUp, up) * rotation;
                            rotating = true;
                            initialUp = up;
                        }
                        if (MapView.MapIsEnabled)
                        {
                            forward = -Math.Sign(vessel.latitude) * (vessel.mainBody.BDGetWorldSurfacePosition(vessel.latitude - Math.Sign(vessel.latitude), vessel.longitude, vessel.altitude) - vessel.GetWorldPos3D()).ProjectOnPlanePreNormalized(up).normalized;
                        }
                        else
                        {
                            forward = (vessel.transform.position - FlightCamera.fetch.mainCamera.transform.position).ProjectOnPlanePreNormalized(up).normalized;
                        }
                        right = Vector3.Cross(up, forward);
                    }

                    // Perform rotations first to update lower bound.
                    if (rotating)
                    {
                        var radarAltitude = RadarAltitude(vessel) - lowerBound;
                        rotateSpeed = Mathf.Clamp(Mathf.MoveTowards(rotateSpeed, 180f, Mathf.Min(10f + 10f * rotateSpeed, 180f) * Time.fixedDeltaTime), 0f, 180f);
                    }
                    else { rotateSpeed = 0f; }
                    if (autoLevelPlane)
                    {
                        Quaternion targetRot = Quaternion.LookRotation(-up, forward);
                        var angleDelta = Quaternion.Angle(rotation, targetRot);
                        if (angleDelta > 0) // Note Quaternion.Angle is considered 0 below around 0.03, so this only runs a few times when finally approaching the target angle.
                        {
                            if (angleDelta < rotateSpeed * 4f * Time.fixedDeltaTime) rotation = Quaternion.Slerp(rotation, targetRot, 0.5f); // Slerp the last part to avoid overshooting, which shouldn't happen, but does!
                            else rotation = Quaternion.RotateTowards(rotation, targetRot, rotateSpeed * 2f * Time.fixedDeltaTime);
                        }
                    }
                    else if (autoLevelRocket)
                    {
                        Quaternion targetRot = Quaternion.LookRotation(forward, up);
                        rotation = Quaternion.RotateTowards(rotation, targetRot, rotateSpeed * 2f * Time.fixedDeltaTime);
                    }
                    else if (rotating)
                    {
                        if (rotationAdjustment.x != 0) rotation = Quaternion.AngleAxis(-rotateSpeed * Time.fixedDeltaTime * rotationAdjustment.x, referenceTransform.up) * rotation; // Roll
                        if (rotationAdjustment.z != 0) rotation = Quaternion.AngleAxis(rotateSpeed * Time.fixedDeltaTime * rotationAdjustment.z, referenceTransform.right) * rotation; // Pitch
                        if (rotationAdjustment.y != 0) rotation = Quaternion.AngleAxis(-rotateSpeed * Time.fixedDeltaTime * rotationAdjustment.y, referenceTransform.forward) * rotation; // Yaw
                    }
                    if (rotating)
                    {
                        vessel.IgnoreGForces(240);
                        vessel.IgnoreSpeed(240);
                        var previousLowerBound = lowerBound;
                        vessel.SetRotation(rotation);
                        lowerBound = GetLowerBound(vessel);
                        position += (lowerBound - previousLowerBound) * up;
                        vessel.SetPosition(position);
                        vessel.SetVelocity(Vector3.zero);
                    }

                    // Translations/Altitude changes
                    if (MapView.MapIsEnabled && preMapViewAltitude == 0) // Map View was enabled, raise the craft to at least 10km.
                    {
                        preMapViewAltitude = RadarAltitude(vessel); // When map view gets enabled, store the altitude the craft was at.
                        var altitude = Mathf.Max(Mathf.Max((float)vessel.mainBody.Radius * 0.05f, 1e4f), preMapViewAltitude); // Then raise the vessel to at least 10km to avoid terrain loading.
                        var safeAltitude = SafeAltitude(vessel, lowerBound);
                        position += (altitude - safeAltitude) * up;
                        if (BDArmorySettings.DEBUG_SPAWNING) Debug.Log($"[BDArmory.VesselMover]: Switching to map altitude {altitude + lowerBound}m");
                    }
                    else if (!MapView.MapIsEnabled && preMapViewAltitude != 0) // Map View was disabled, lower the craft back to it's original altitude (min base+1km). Leave horizontal movement for later frames.
                    {
                        preMapViewAltitude = Mathf.Max(preMapViewAltitude, 2f * vessel.GetRadius() + 1000f);
                        var safeAltitude = SafeAltitude(vessel, lowerBound);
                        position += (preMapViewAltitude - safeAltitude) * up;
                        if (BDArmorySettings.DEBUG_SPAWNING) Debug.Log($"[BDArmory.VesselMover]: Reverting to pre-map altitude {preMapViewAltitude + lowerBound}m (safeAlt: {safeAltitude}, lower bound: {lowerBound}m)");
                        preMapViewAltitude = 0;
                    }
                    else if (reset)
                    {
                        var baseAltitude = 2f * vessel.GetRadius();
                        var safeAltitude = SafeAltitude(vessel, lowerBound);
                        if (!BDArmorySettings.VESSEL_MOVER_BELOW_WATER) safeAltitude = Mathf.Min((float)vessel.altitude, safeAltitude);
                        if (BDArmorySettings.DEBUG_SPAWNING) Debug.Log($"[BDArmory.VesselMover]: Resetting to base altitude {baseAltitude + lowerBound}m (safeAlt: {safeAltitude}, lower bound: {lowerBound}m)");
                        position += (baseAltitude - safeAltitude) * up;
                        reset = false;
                    }
                    else if (jump)
                    {
                        var baseAltitude = 2f * vessel.GetRadius();
                        var safeAltitude = SafeAltitude(vessel, lowerBound);
                        var jumpToAltitude = baseAltitude;
                        if (jumpDirection) // Jump up
                        {
                            jumpToAltitude = jumpToAltitudes.Where(a => a > 1.05f * safeAltitude && a > 2f * baseAltitude).Select(a => Mathf.Max(a, baseAltitude)).FirstOrDefault();
                            if (jumpToAltitude < baseAltitude) jumpToAltitude = baseAltitude;
                        }
                        else // Jump down
                        {
                            jumpToAltitude = safeAltitude < 1.1f * baseAltitude ? jumpToAltitudes.Last() : jumpToAltitudes.Where(a => a < 0.95f * safeAltitude).LastOrDefault();
                            if (jumpToAltitude < 2f * baseAltitude) jumpToAltitude = baseAltitude;
                        }
                        if (BDArmorySettings.DEBUG_SPAWNING) Debug.Log($"[BDArmory.VesselMover]: Jumping to altitude {jumpToAltitude}m (safeAlt: {safeAltitude}, baseAlt: {baseAltitude})");
                        up = (vessel.transform.position - FlightGlobals.currentMainBody.transform.position).normalized;
                        position += (jumpToAltitude - safeAltitude) * up;
                        jump = false;
                    }
                    else if (translating)
                    {
                        var radarAltitude = RadarAltitude(vessel);
                        var distance = Mathf.Abs(radarAltitude - lowerBound);
                        float maxMoveSpeed = MapView.MapIsEnabled ? 1e5f : (distance < 1e4f ? 10f + distance : 100f * BDAMath.Sqrt(distance));
                        moveSpeed = Mathf.Clamp(Mathf.MoveTowards(moveSpeed, maxMoveSpeed, (4.79f * moveSpeed + 0.05f * maxMoveSpeed) * Time.fixedDeltaTime), 0, maxMoveSpeed); // Accelerated acceleration for ~2s.

                        var moveDistanceHorizontal = 10f * moveSpeed * Time.fixedDeltaTime;
                        var moveDistanceVertical = moveSpeed * Time.fixedDeltaTime;
                        var offset = positionAdjustment.x * moveDistanceHorizontal * right + positionAdjustment.y * moveDistanceHorizontal * forward;
                        offset += (radarAltitude - RadarAltitude(position + offset)) * up;
                        var safeAltitude = radarAltitude < 1000 ? SafeAltitude(vessel, lowerBound, offset) : radarAltitude; // Don't bother when over 1000m.
                        offset += Mathf.Max(positionAdjustment.z * moveDistanceVertical, -safeAltitude) * up;
                        position += offset;
                        // Debug.Log($"DEBUG position: {position:G6}, altitude: {radarAltitude}, safeAltCorrection: {safeAltitude}, distance: {distance}, moveSpeed: {moveSpeed}, maxSpeed: {maxMoveSpeed}, moveDistanceHorizontal: {moveDistanceHorizontal}, moveDistanceVertical: {moveDistanceVertical}");
                    }
                    else { moveSpeed = 0; }
                }

                vessel.IgnoreGForces(240);
                vessel.IgnoreSpeed(240);
                vessel.SetPosition(position);
                vessel.SetVelocity(Vector3.zero);
                vessel.acceleration = Vector3d.zero;
                vessel.SetRotation(rotation); // Reset the rotation to prevent any angular momentum from messing with the orientation.
                yield return wait;
                KrakensbaneCorrection(ref position);
            }

            if (hadPatchedConicsSolver && !vessel.PatchedConicsAttached)
            {
                if (BDArmorySettings.DEBUG_SPAWNING) Debug.Log($"[BDArmory.VesselMover]: Re-attaching patched conic solver");
                try
                {
                    vessel.AttachPatchedConicsSolver();
                    if (vessel.altitude > 1e5) vessel.SetVelocity(UnityEngine.Random.rotation * Vector3.one * 0.01f); // Add noise to the velocity if above 100km to avoid NaNs in the Patched Cubic Solver due to a degenerate orbit.
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[BDArmory.VesselMover]: Failed to re-attach the Patched Conic Solver: {e.Message}");
                }
            }
        }

        void KillRotation(Vessel vessel)
        {
            if (vessel.angularVelocity == default) return;
            foreach (var part in vessel.Parts)
            {
                var rb = part.Rigidbody;
                if (rb == null) continue;
                rb.angularVelocity = default;
            }
        }

        void KrakensbaneCorrection(ref Vector3 position)
        {
            if (!BDKrakensbane.IsActive) return;
            position -= BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
        }

        public IEnumerator PlaceVessel(Vessel vessel, bool skipMovingCheck = false)
        {
            if (IsLowering(vessel)) yield break; // We're already doing this.
            if (!skipMovingCheck && !IsMoving(vessel)) { state = State.None; yield break; } // The vessel isn't moving, abort.
            if (BDArmorySettings.DEBUG_SPAWNING) Debug.Log($"[BDArmory.VesselMover]: Placing {vessel.vesselName}");
            movingVessels.Remove(vessel);
            loweringVessels.Add(vessel);

            KillRotation(vessel);
            if (BDArmorySettings.VESSEL_MOVER_ENABLE_BRAKES) vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
            if (!FlightGlobals.currentMainBody.hasSolidSurface) { DropVessel(vessel); yield break; } // No surface to lower to!
            if (BDArmorySettings.VESSEL_MOVER_LOWER_FAST)
            {
                var up = (vessel.transform.position - FlightGlobals.currentMainBody.transform.position).normalized;
                var baseAltitude = 2f * vessel.GetRadius();
                var safeAltitude = SafeAltitude(vessel) * 0.95f; // Only go 95% of the way in a single jump in case terrain is still loading in.
                if (baseAltitude < safeAltitude)
                    vessel.Translate((baseAltitude - safeAltitude) * up);
                vessel.SetVelocity(Vector3.zero);
            }
            yield return LowerVessel(vessel);
        }

        IEnumerator LowerVessel(Vessel vessel)
        {
            if (!FlightGlobals.currentMainBody.hasSolidSurface) { DropVessel(vessel); yield break; } // No surface to lower to!
            if (BDArmorySettings.DEBUG_SPAWNING) Debug.Log($"[BDArmory.VesselMover]: Lowering {vessel.vesselName}");
            state = State.Lowering;
            var lowerBound = GetLowerBound(vessel);
            var up = (vessel.transform.position - FlightGlobals.currentMainBody.transform.position).normalized;
            bool finalLowering = false;
            var previousAltitude = vessel.altitude;
            while (IsLowering(vessel) && !LandedOrSplashed(vessel) && (!finalLowering || vessel.altitude - previousAltitude < -0.1 * BDArmorySettings.VESSEL_MOVER_MIN_LOWER_SPEED * Time.fixedDeltaTime))
            {
                var distance = SafeAltitude(vessel, lowerBound);
                var speed = distance > 1e4 ? 100f * BDAMath.Sqrt(distance) : distance > BDArmorySettings.VESSEL_MOVER_MIN_LOWER_SPEED ? Mathf.Clamp(1f + 30f / distance, 1f, 4f) * distance : BDArmorySettings.VESSEL_MOVER_MIN_LOWER_SPEED;
                if (speed > BDArmorySettings.VESSEL_MOVER_MIN_LOWER_SPEED)
                {
                    vessel.SetVelocity(Vector3.zero);
                    vessel.Translate(-speed * up * Time.fixedDeltaTime);
                }
                else
                {
                    if (!finalLowering && vessel.verticalSpeed < -1e-2) finalLowering = true;
                    vessel.SetVelocity(-speed * up);
                }
                // Debug.Log($"DEBUG landed/splashed: {LandedOrSplashed(vessel)}, altitude: {vessel.altitude}m ({distance}m), v-speed: {vessel.verticalSpeed}m/s, speed: {speed}");
                previousAltitude = vessel.altitude;
                yield return wait;
            }
            if (!IsLowering(vessel)) yield break; // Vessel destroyed or state switched, e.g., moving again.

            // Turn on brakes and SAS (apparently helps to avoid the turning bug).
            if (BDArmorySettings.VESSEL_MOVER_ENABLE_SAS)
            { vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true); }
            if (BDArmorySettings.VESSEL_MOVER_ENABLE_BRAKES)
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                // Ease the craft to a resting position.
                messageState = Messages.EasingCraft;
                var startTime = Time.time;
                var stationaryStartTime = startTime;
                vessel.IgnoreGForces(0);
                vessel.IgnoreSpeed(0);
                while (IsLowering(vessel) && Time.time - startTime < 10f && Time.time - stationaryStartTime <= 0.1f) // Damp movement for up to 10s.
                {
                    // if ((float)vessel.verticalSpeed < -0.1f * BDArmorySettings.VESSEL_MOVER_MIN_LOWER_SPEED)
                    if ((vessel.altitude >= 0 && Math.Abs(vessel.altitude - previousAltitude) > 0.1 * BDArmorySettings.VESSEL_MOVER_MIN_LOWER_SPEED * Time.fixedDeltaTime)
                        || (vessel.altitude < 0 && vessel.altitude - previousAltitude < -0.1 * BDArmorySettings.VESSEL_MOVER_MIN_LOWER_SPEED * Time.fixedDeltaTime))
                    {
                        vessel.SetVelocity(vessel.GetSrfVelocity() * (0.45f + 0.5f * BDArmorySettings.VESSEL_MOVER_MIN_LOWER_SPEED));
                        stationaryStartTime = Time.time;
                        yield return wait; // Setting the velocity prevents a proper velocity calculation on the next frame, so wait an extra frame for it to take effect.
                    }
                    // Debug.Log($"DEBUG landed/splashed: {LandedOrSplashed(vessel)}, altitude: {vessel.altitude}m, v-speed: {vessel.verticalSpeed}m/s");
                    previousAltitude = vessel.altitude;
                    yield return wait;
                }
            }
            if (IsLowering(vessel))
            {
                loweringVessels.Remove(vessel);
                state = State.None;
                messageState = Messages.None;
            }
        }

        void DropVessel(Vessel vessel)
        {
            if (!IsMoving(vessel) && !IsLowering(vessel)) return; // Not in a valid state for dropping.
            if (BDArmorySettings.DEBUG_SPAWNING) Debug.Log($"[BDArmory.VesselMover]: Dropping {vessel.vesselName}");
            movingVessels.Remove(vessel);
            loweringVessels.Remove(vessel);
            state = State.None;
            messageState = Messages.None;
        }

        public void PickUpAndDrop(Vessel vessel, float altitude) { StartCoroutine(PickUpAndDropCoroutine(vessel, altitude)); }
        public IEnumerator PickUpAndDropCoroutine(Vessel vessel, float altitude)
        {
            StartCoroutine(MoveVessel(vessel)); // Pick it up.
            yield return new WaitForSecondsFixed(0.25f); // Wait a quarter-sec (the initial pick-up should only take 0.2s).
            DropVessel(vessel); // Drop it.
            vessel.SetPosition(vessel.transform.position + (altitude - (float)vessel.radarAltitude) * (vessel.transform.position - FlightGlobals.currentMainBody.transform.position).normalized);
            vessel.IgnoreGForces(1);
            vessel.IgnoreSpeed(1);
        }

        /// <summary>
        /// Get the vertical distance (non-negative) from the vessel transform position to the lowest point.
        /// </summary>
        /// <param name="vessel"></param>
        /// <returns></returns>
        float GetLowerBound(Vessel vessel)
        {
            var up = (vessel.transform.position - FlightGlobals.currentMainBody.transform.position).normalized;
            var radius = vessel.GetRadius();
            var maxDim = 2f * radius;
            var hitCount = Physics.BoxCastNonAlloc(vessel.transform.position - (maxDim + 0.1f) * up, new Vector3(radius, 0.1f, radius), up, hits, Quaternion.FromToRotation(Vector3.up, up), maxDim, (int)(LayerMasks.Parts | LayerMasks.EVA | LayerMasks.Wheels));
            if (hitCount == hits.Length)
            {
                hits = Physics.BoxCastAll(vessel.transform.position - (maxDim + 0.1f) * up, new Vector3(radius, 0.1f, radius), up, Quaternion.FromToRotation(Vector3.up, up), maxDim, (int)(LayerMasks.Parts | LayerMasks.EVA | LayerMasks.Wheels));
                hitCount = hits.Length;
            }
            var distances = hits.Take(hitCount).Where(hit => hit.collider != null && hit.collider.gameObject != null).Where(hit => { var part = hit.collider.gameObject.GetComponentInParent<Part>(); return part != null && part.vessel == vessel; }).Select(hit => hit.distance).ToArray();
            if (distances.Length == 0)
            {
                Debug.LogWarning($"[BDArmory.VesselMover]: Failed to detect craft for lower bound!");
                return 0;
            }
            return maxDim - distances.Min();
        }

        bool LandedOrSplashed(Vessel vessel) => BDArmorySettings.VESSEL_MOVER_BELOW_WATER ? vessel.Landed : vessel.LandedOrSplashed;
        float RadarAltitude(Vessel vessel) => (float)(vessel.altitude - vessel.mainBody.TerrainAltitude(vessel.latitude, vessel.longitude, BDArmorySettings.VESSEL_MOVER_BELOW_WATER));
        float RadarAltitude(Vector3 position) => (float)(FlightGlobals.currentMainBody.GetAltitude(position) - BodyUtils.GetTerrainAltitudeAtPos(position, BDArmorySettings.VESSEL_MOVER_BELOW_WATER));
        float SafeAltitude(Vessel vessel, float lowerBound = -1f, Vector3 offset = default) // Get the safe altitude range we can adjust by.
        {
            var altitude = RadarAltitude(vessel);
            if (BDArmorySettings.VESSEL_MOVER_DONT_WORRY_ABOUT_COLLISIONS && state == State.Moving) return altitude;
            var position = vessel.transform.position + offset;
            var up = (position - FlightGlobals.currentMainBody.transform.position).normalized;
            var radius = vessel.GetRadius();
            if (lowerBound < 0) lowerBound = GetLowerBound(vessel);

            // Detect collisions from moving in the direction of the offset. 100m is generally sufficient.
            var hitCount = Physics.BoxCastNonAlloc(position + 100.1f * up, new Vector3(radius, 0.1f, radius), -up, hits, Quaternion.FromToRotation(Vector3.up, up), altitude + 100f, (int)(LayerMasks.Scenery | LayerMasks.Parts | LayerMasks.EVA | LayerMasks.Wheels));
            if (hitCount == hits.Length)
            {
                hits = Physics.BoxCastAll(position + 100.1f * up, new Vector3(radius, 0.1f, radius), -up, Quaternion.FromToRotation(Vector3.up, up), altitude + 100f, (int)(LayerMasks.Scenery | LayerMasks.Parts | LayerMasks.EVA | LayerMasks.Wheels));
                hitCount = hits.Length;
            }
            if (hitCount > 0)
            {
                var distances = hits.Take(hitCount).Where(hit => hit.collider != null && hit.collider.gameObject != null).Where(hit => { var part = hit.collider.gameObject.GetComponentInParent<Part>(); return part == null || part.vessel != vessel; }).Select(hit => hit.distance).ToArray();
                if (distances.Length > 0) altitude = Mathf.Min(altitude, distances.Min() - 100f);
            }
            return altitude - lowerBound - 0.1f;
        }
        #endregion

        #region Spawning
        Vessel spawnedVessel;
        HashSet<string> KerbalNames = [];
        int crewCapacity = -1;
        string vesselNameToSpawn = "";
        CustomCraftBrowserDialog craftBrowser;
        bool abortCraftSelection = false;
        bool resizingSelectionWindow = false;
        IEnumerator SpawnVessel()
        {
            state = State.Spawning;

            // Open craft selection
            string craftFile = "";
            abortCraftSelection = false;
            messageState = Messages.OpeningCraftBrowser;
            if (BDArmorySettings.VESSEL_MOVER_CLASSIC_CRAFT_CHOOSER)
            {
                var craftBrowser = CraftBrowserDialog.Spawn(EditorFacility.SPH, HighLogic.SaveFolder, (path, loadType) => { craftFile = path; }, () => { abortCraftSelection = true; }, false);
                while (!abortCraftSelection && string.IsNullOrEmpty(craftFile)) yield return wait;
                if (craftBrowser != null) craftBrowser.Dismiss();
                craftBrowser = null;
            }
            else
            {
                ShowVesselSelection((path) => { craftFile = path; }, () => { abortCraftSelection = true; });
                while (!abortCraftSelection && string.IsNullOrEmpty(craftFile)) yield return wait;
            }
            messageState = Messages.None;
            if (abortCraftSelection || string.IsNullOrEmpty(craftFile)) { state = State.None; yield break; }
            if (BDArmorySettings.DEBUG_SPAWNING) Debug.Log($"[BDArmory.VesselMover]: {craftFile} selected for spawning.");

            // Choose crew
            crewCapacity = GetCrewCapacity(craftFile, out vesselNameToSpawn);
            if (BDArmorySettings.VESSEL_MOVER_CHOOSE_CREW)
            { yield return ChooseCrew(); }
            messageState = Messages.None;

            // Select location
            yield return GetSpawnPoint();
            messageState = Messages.None;
            if (geoCoords == Vector3d.zero) { state = State.None; yield break; }

            // Store the camera view
            var camera = FlightCamera.fetch;
            var cameraOffset = FlightGlobals.ActiveVessel != null ? camera.transform.position - FlightGlobals.ActiveVessel.transform.position : Vector3.zero;

            // Spawn the craft
            yield return SpawnVessel(craftFile, geoCoords.x, geoCoords.y, geoCoords.z + 1000f, kerbalNames: KerbalNames); // Spawn 1km higher than requested and then move it down.
            messageState = Messages.None;
            if (spawnFailureReason != SpawnFailureReason.None) { state = State.None; yield break; }
            if (BDArmorySettings.DEBUG_SPAWNING) Debug.Log($"[BDArmory.VesselMover]: Spawned {spawnedVessel.vesselName} at {geoCoords:G6}");

            // Wait for the vessel to be usable.
            // Note: Smart parts that are pre-enabled can trigger events that break craft while they spawn (particularly altitude and speed based ones). Those should be set active on AG10 instead.
            while (spawnedVessel != null && (!spawnedVessel.loaded || spawnedVessel.packed)) yield return wait;

            // Reposition the vessel to where it should be.
            if (spawnedVessel != null)
            {
                var up = (spawnedVessel.transform.position - FlightGlobals.currentMainBody.transform.position).normalized;
                if (FlightGlobals.currentMainBody.hasSolidSurface)
                {
                    var safeAltitude = SafeAltitude(spawnedVessel);
                    if (!BDArmorySettings.VESSEL_MOVER_BELOW_WATER) safeAltitude = Mathf.Min((float)spawnedVessel.altitude, safeAltitude);
                    spawnedVessel.Translate((2f * spawnedVessel.GetRadius() - safeAltitude) * up);
                }
                else // No surface to lower to!
                {
                    spawnedVessel.Translate(-1000f * up);
                }
                spawnedVessel.SetVelocity(Vector3.zero);
            }

            // Switch to it when possible
            yield return LoadedVesselSwitcher.Instance.SwitchToVesselWhenPossible(spawnedVessel);
            if (!IsValid(spawnedVessel))
            {
                Debug.LogWarning($"[BDArmory.VesselMover]: The spawned vessel disappeared before we could switch to it!");
                state = State.None;
                yield break;
            }

            // Restore the camera view
            camera.SetCamCoordsFromPosition(spawnedVessel.transform.position + cameraOffset);

            // Switch to moving mode
            if (BDArmorySettings.VESSEL_MOVER_PLACE_AFTER_SPAWN)
            {
                yield return PlaceVessel(spawnedVessel, true);
            }
            else
            {
                yield return MoveVessel(spawnedVessel);
            }
            spawnedVessel = null; // Clear the reference to the spawned vessel.
        }

        void RecoverVessel()
        {
            var vessel = FlightGlobals.ActiveVessel;
            var nearestOtherVessel = FlightGlobals.VesselsLoaded.Where(v => v != vessel).OrderBy(v => (v.transform.position - vessel.transform.position).sqrMagnitude).FirstOrDefault();
            if (nearestOtherVessel != null)
            {
                if (BDArmorySettings.DEBUG_SPAWNING) Debug.Log($"[BDArmory.VesselMover]: Switching to nearest vessel {nearestOtherVessel.vesselName}");
                LoadedVesselSwitcher.Instance.ForceSwitchVessel(nearestOtherVessel);
            }
            SpawnUtils.RemoveVessel(vessel);
        }

        int GetCrewCapacity(string craftFile, out string vesselName)
        {
            CraftProfileInfo.PrepareCraftMetaFileLoad();
            var craftMeta = $"{Path.GetFileNameWithoutExtension(craftFile)}.loadmeta";
            var meta = new CraftProfileInfo();
            if (File.Exists(craftMeta)) // If the loadMeta file exists, use it, otherwise generate one.
            {
                meta.LoadFromMetaFile(craftMeta);
            }
            else
            {
                var craftNode = ConfigNode.Load(craftFile);
                meta.LoadDetailsFromCraftFile(craftNode, craftFile);
                meta.SaveToMetaFile(craftMeta);
            }
            int crewCapacity = 0;
            vesselName = meta.shipName;
            foreach (var partName in meta.partNames)
            {
                if (SpawnUtils.PartCrewCounts.ContainsKey(partName))
                    crewCapacity += SpawnUtils.PartCrewCounts[partName];
            }
            return crewCapacity;
        }

        IEnumerator ChooseCrew()
        {
            messageState = Messages.ChoosingCrew;
            KerbalNames.Clear();
            ShowCrewSelection(new Vector2(Screen.width / 2, Screen.height / 2));
            while (showCrewSelection)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    HideCrewSelection();
                    break;
                }
                yield return null;
            }
        }

        IEnumerator GetSpawnPoint()
        {
            messageState = Messages.ChoosingSpawnPoint;
            GameObject indicatorObject = SetupSpawnPointIndicator();

            Vector3 mouseAim, point;
            Ray ray;
            var currentMainBody = FlightGlobals.currentMainBody;
            while (BDArmorySetup.showVesselMoverGUI)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    geoCoords = Vector3d.zero;
                    break;
                }

                mouseAim = new Vector3(Input.mousePosition.x / Screen.width, Input.mousePosition.y / Screen.height, 0);
                ray = FlightCamera.fetch.mainCamera.ViewportPointToRay(mouseAim);
                bool altitudeCorrection;
                if (Physics.Raycast(ray, out RaycastHit hit, (ray.origin - currentMainBody.transform.position).magnitude, (int)(LayerMasks.Scenery | LayerMasks.Parts | LayerMasks.Wheels | LayerMasks.EVA)))
                {
                    point = hit.point;
                    altitudeCorrection = false;
                }
                else if (SphereRayIntersect(ray, currentMainBody.transform.position, (float)currentMainBody.Radius, out float distance))
                {
                    point = ray.GetPoint(distance);
                    altitudeCorrection = true;
                }
                else
                {
                    yield return null;
                    continue;
                }

                indicatorObject.transform.position = point;
                indicatorObject.transform.rotation = Quaternion.LookRotation(point - currentMainBody.transform.position);

                if (Input.GetMouseButtonDown(0))
                {
                    currentMainBody.GetLatLonAlt(point, out geoCoords.x, out geoCoords.y, out geoCoords.z);
                    if (altitudeCorrection) geoCoords.z = currentMainBody.TerrainAltitude(geoCoords.x, geoCoords.y, BDArmorySettings.VESSEL_MOVER_BELOW_WATER);
                    break;
                }
                yield return null;
            }
            Destroy(indicatorObject);
        }

        GameObject SetupSpawnPointIndicator()
        {
            // Use the same indicator as the original VesselMover for familiarity.
            GameObject indicatorObject = new();
            LineRenderer lr = indicatorObject.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("KSP/Particles/Alpha Blended"));
            lr.material.SetColor("_TintColor", Color.green);
            lr.material.mainTexture = Texture2D.whiteTexture;
            lr.useWorldSpace = false;

            Vector3[] positions = [Vector3.zero, 10 * Vector3.forward];
            lr.SetPositions(positions);
            lr.positionCount = positions.Length;
            lr.startWidth = 0.1f;
            lr.endWidth = 1f;

            return indicatorObject;
        }

        bool SphereRayIntersect(Ray ray, Vector3 sphereCenter, float sphereRadius, out float distance)
        {
            distance = 0;
            Vector3 n = ray.direction;
            Vector3 R = ray.origin - sphereCenter;
            float r = sphereRadius;
            float d = Vector3.Dot(n, R); // d is non-positive if the ray originates outside the sphere and intersects it
            if (d > 0) return false;
            var G = d * d - R.sqrMagnitude + r * r;
            if (G < 0) return false;
            distance = -d - BDAMath.Sqrt(G);
            return true;
        }

        IEnumerator SpawnVessel(string craftUrl, double latitude, double longitude, double altitude, float initialHeading = 90f, float initialPitch = 0f, HashSet<string> kerbalNames = null)
        {
            messageState = Messages.LoadingCraft;
            spawnFailureReason = SpawnFailureReason.None; // Reset the spawn failure reason.
            var spawnPoint = FlightGlobals.currentMainBody.BDGetWorldSurfacePosition(latitude, longitude, altitude);
            var radialUnitVector = (spawnPoint - FlightGlobals.currentMainBody.transform.position).normalized;
            var north = VectorUtils.GetNorthVector(spawnPoint, FlightGlobals.currentMainBody);
            var direction = (Quaternion.AngleAxis(initialHeading, radialUnitVector) * north).ProjectOnPlanePreNormalized(radialUnitVector).normalized;
            var crew = new List<ProtoCrewMember>();
            if (kerbalNames != null)
            {
                foreach (var kerbalName in kerbalNames) crew.Add(HighLogic.CurrentGame.CrewRoster[kerbalName]);
                VesselSpawner.ReservedCrew = crew.Select(crew => crew.name).ToHashSet(); // Reserve the crew so they don't get swapped out.
                foreach (var c in crew) c.rosterStatus = ProtoCrewMember.RosterStatus.Available; // Set all the requested crew as available.
            }
            VesselSpawnConfig vesselSpawnConfig = new(
                craftUrl,
                spawnPoint,
                direction,
                (float)altitude,
                initialPitch,
                airborne: false,
                inOrbit: false,
                deconflictVesselName: BDACompetitionMode.Instance.competitionIsActive || BDACompetitionMode.Instance.competitionStarting, // Deconflict name only if spawning into an active competition.
                crew: crew
            );
            ResetInternals(); // Reset spawner internals.

            // Spawn vessel.
            yield return SpawnSingleVessel(vesselSpawnConfig);
            VesselSpawner.ReservedCrew.Clear(); // Clear the reserved crew again.
            if (spawnFailureReason != SpawnFailureReason.None) { state = State.None; yield break; }
            if (!spawnedVessels.TryGetValue(latestSpawnedVesselName, out Vessel vessel) || vessel == null)
            {
                spawnFailureReason = SpawnFailureReason.VesselFailedToSpawn;
                state = State.None;
                yield break;
            }
            if (vesselSpawnConfig.editorFacility == EditorFacility.VAB) vessel.SetRotation(Quaternion.AngleAxis(90, radialUnitVector) * vessel.transform.rotation); // Rotate rockets to the same orientation as the launch pad.
            spawnedVessel = vessel;
        }

        public override IEnumerator Spawn(SpawnConfig spawnConfig) { yield break; } // Compliance with SpawnStrategy kludge.
        #endregion

        #region GUI
        static int guiCheckIndex = -1;
        bool helpShowing = false;
        bool ready = false;
        float windowWidth = 300;
        enum Messages { None, Custom, OpeningCraftBrowser, ChoosingCrew, ChoosingSpawnPoint, LoadingCraft, EasingCraft }
        Messages messageState { get { return _messageState; } set { _messageState = value; if (value != Messages.None) messageDisplayTime = Time.time + 5; } }
        Messages _messageState = Messages.None;
        string customMessage = "";
        float messageDisplayTime = 0;
        float previousVesselMoverWindowHeight = 0;

        private void OnGUI()
        {
            if (!(ready && BDArmorySetup.GAME_UI_ENABLED && HighLogic.LoadedSceneIsFlight))
                return;

            if (resizingSelectionWindow && Event.current.type == EventType.MouseUp) { resizingSelectionWindow = false; }
            if (BDArmorySetup.showVesselMoverGUI)
            {
                BDArmorySetup.SetGUIOpacity();
                var guiMatrix = GUI.matrix; // Store and restore the GUI.matrix so we can apply a different scaling for the WM window.
                if (BDArmorySettings.UI_SCALE_ACTUAL != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, BDArmorySetup.WindowRectVesselMover.position);
                BDArmorySetup.WindowRectVesselMover = GUILayout.Window(
                    GUIUtility.GetControlID(FocusType.Passive),
                    BDArmorySetup.WindowRectVesselMover,
                    WindowVesselMover,
                    StringUtils.Localize("#LOC_BDArmory_VesselMover_Title"), // "BDA Vessel Mover"
                    BDArmorySetup.BDGuiSkin.window,
                    GUILayout.Width(windowWidth)
                );
                GUI.matrix = guiMatrix;
                previousVesselMoverWindowHeight = BDArmorySetup.WindowRectVesselMover.height;
                if (showVesselSelection)
                {
                    if (BDArmorySettings.UI_SCALE_ACTUAL != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, BDArmorySetup.WindowRectVesselMoverVesselSelection.position);
                    BDArmorySetup.WindowRectVesselMoverVesselSelection = GUILayout.Window(
                        GUIUtility.GetControlID(FocusType.Passive),
                        BDArmorySetup.WindowRectVesselMoverVesselSelection,
                        VesselSelectionWindow,
                        StringUtils.Localize("#LOC_BDArmory_VesselMover_VesselSelection"),
                        BDArmorySetup.BDGuiSkin.window
                    );
                    GUI.matrix = guiMatrix;
                }
                else if (showCrewSelection)
                {
                    if (BDArmorySettings.UI_SCALE_ACTUAL != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, crewSelectionWindowRect.position);
                    crewSelectionWindowRect = GUILayout.Window(
                        GUIUtility.GetControlID(FocusType.Passive),
                        crewSelectionWindowRect,
                        CrewSelectionWindow,
                        StringUtils.Localize("#LOC_BDArmory_VesselMover_CrewSelection"),
                        BDArmorySetup.BDGuiSkin.window
                    );
                    GUI.matrix = guiMatrix;
                }
                BDArmorySetup.SetGUIOpacity(false);
                GUIUtils.UpdateGUIRect(BDArmorySetup.WindowRectVesselMover, guiCheckIndex);
            }
            else
            {
                if (showCrewSelection)
                {
                    KerbalNames.Clear();
                    HideCrewSelection();
                }
                if (showVesselSelection)
                {
                    HideVesselSelection();
                }
            }

            if (Time.time > messageDisplayTime) messageState = Messages.None;
            switch (messageState)
            {
                case Messages.Custom:
                    DrawShadowedMessage(customMessage);
                    break;
                case Messages.OpeningCraftBrowser:
                    DrawShadowedMessage("Opening Craft Browser...");
                    break;
                case Messages.ChoosingCrew:
                    DrawShadowedMessage("Opening Crew Selection...");
                    break;
                case Messages.ChoosingSpawnPoint:
                    DrawShadowedMessage("Click somewhere to spawn!");
                    break;
                case Messages.LoadingCraft:
                    DrawShadowedMessage("Loading Craft...");
                    break;
                case Messages.EasingCraft:
                    DrawShadowedMessage("Easing Craft for up to 10s...");
                    break;
            }
        }

        GUIStyle messageStyle;
        GUIStyle messageShadowStyle;
        void ConfigureStyles()
        {
            messageStyle = new GUIStyle(HighLogic.Skin.label)
            {
                fontSize = 22,
                alignment = TextAnchor.UpperCenter
            };

            messageShadowStyle = new GUIStyle(messageStyle);
            messageShadowStyle.normal.textColor = new Color(0, 0, 0, 0.75f);
        }

        void DrawShadowedMessage(string message)
        {
            Rect labelRect = new Rect(0, (Screen.height * 0.25f) + (Mathf.Sin(2 * Time.time) * 5), Screen.width, 200);
            Rect shadowRect = new Rect(labelRect);
            shadowRect.position += new Vector2(2, 2);
            GUI.Label(shadowRect, message, messageShadowStyle);
            GUI.Label(labelRect, message, messageStyle);
        }

        void OnVesselChanged(Vessel vessel)
        {
            if (!IsValid(vessel))
            { state = State.None; }
            if (movingVessels.Contains(vessel))
            { state = State.Moving; }
            else if (loweringVessels.Contains(vessel))
            { state = State.Lowering; }
            else
            { state = State.None; }
            // Clean the moving and lowering vessel hashsets.
            movingVessels = movingVessels.Where(v => IsValid(v)).ToHashSet();
            loweringVessels = loweringVessels.Where(v => IsValid(v)).ToHashSet();
        }

        void WindowVesselMover(int id)
        {
            if (GUI.Button(new Rect(BDArmorySetup.WindowRectVesselMover.width - 24, 0, 24, 24), " X", BDArmorySetup.CloseButtonStyle)) SetVisible(false);
            GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            switch (state)
            {
                case State.None:
                    {
                        if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_VesselMover_MoveVessel"), BDArmorySetup.ButtonStyle, GUILayout.Height(40))) StartCoroutine(MoveVessel(FlightGlobals.ActiveVessel));
                        if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_VesselMover_SpawnVessel"), BDArmorySetup.ButtonStyle, GUILayout.Height(40))) StartCoroutine(SpawnVessel());
                        if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_VesselMover_RecoverVessel"), BDArmorySetup.ButtonStyle, GUILayout.Height(20))) RecoverVessel();
                        GUILayout.BeginHorizontal();
                        BDArmorySettings.VESSEL_MOVER_CHOOSE_CREW = GUILayout.Toggle(BDArmorySettings.VESSEL_MOVER_CHOOSE_CREW, StringUtils.Localize("#LOC_BDArmory_VesselMover_ChooseCrew"));
                        BDArmorySettings.VESSEL_MOVER_CLASSIC_CRAFT_CHOOSER = GUILayout.Toggle(BDArmorySettings.VESSEL_MOVER_CLASSIC_CRAFT_CHOOSER, StringUtils.Localize("#LOC_BDArmory_VesselMover_ClassicChooser"));
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                        BDArmorySettings.VESSEL_MOVER_CLOSE_ON_COMPETITION_START = GUILayout.Toggle(BDArmorySettings.VESSEL_MOVER_CLOSE_ON_COMPETITION_START, StringUtils.Localize("#LOC_BDArmory_VesselMover_CloseOnCompetitionStart"));
                        GUILayout.EndHorizontal();
                        break;
                    }
                case State.Moving:
                    {
                        if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_VesselMover_PlaceVessel"), BDArmorySetup.ButtonStyle, GUILayout.Height(40))) StartCoroutine(PlaceVessel(FlightGlobals.ActiveVessel));
                        if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_VesselMover_DropVessel"), BDArmorySetup.ButtonStyle, GUILayout.Height(40))) DropVessel(FlightGlobals.ActiveVessel);
                        GUILayout.BeginHorizontal();
                        GUILayout.BeginVertical();
                        BDArmorySettings.VESSEL_MOVER_ENABLE_BRAKES = GUILayout.Toggle(BDArmorySettings.VESSEL_MOVER_ENABLE_BRAKES, StringUtils.Localize("#LOC_BDArmory_VesselMover_EnableBrakes"));
                        BDArmorySettings.VESSEL_MOVER_LOWER_FAST = GUILayout.Toggle(BDArmorySettings.VESSEL_MOVER_LOWER_FAST, StringUtils.Localize("#LOC_BDArmory_VesselMover_LowerFast"));
                        BDArmorySettings.VESSEL_MOVER_DONT_WORRY_ABOUT_COLLISIONS = GUILayout.Toggle(BDArmorySettings.VESSEL_MOVER_DONT_WORRY_ABOUT_COLLISIONS, StringUtils.Localize("#LOC_BDArmory_VesselMover_DontWorryAboutCollisions"));
                        GUILayout.EndVertical();
                        GUILayout.BeginVertical();
                        BDArmorySettings.VESSEL_MOVER_ENABLE_SAS = GUILayout.Toggle(BDArmorySettings.VESSEL_MOVER_ENABLE_SAS, StringUtils.Localize("#LOC_BDArmory_VesselMover_EnableSAS"));
                        BDArmorySettings.VESSEL_MOVER_BELOW_WATER = GUILayout.Toggle(BDArmorySettings.VESSEL_MOVER_BELOW_WATER, StringUtils.Localize("#LOC_BDArmory_VesselMover_BelowWater"));
                        GUILayout.EndVertical();
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_VesselMover_MinLowerSpeed")}: {BDArmorySettings.VESSEL_MOVER_MIN_LOWER_SPEED}", GUILayout.Width(130));
                        BDArmorySettings.VESSEL_MOVER_MIN_LOWER_SPEED = BDAMath.RoundToUnit(GUILayout.HorizontalSlider(BDArmorySettings.VESSEL_MOVER_MIN_LOWER_SPEED, 0.1f, 1f), 0.1f);
                        GUILayout.EndHorizontal();
                        if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_Generic_Help"), helpShowing ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle, GUILayout.Height(20)))
                        {
                            helpShowing = !helpShowing;
                            if (!helpShowing) ResetWindowHeight();
                        }
                        if (helpShowing)
                        {
                            GUILayout.BeginVertical();
                            GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_VesselMover_Help_Movement")}: {GameSettings.PITCH_DOWN.primary}, {GameSettings.PITCH_UP.primary}, {GameSettings.YAW_LEFT.primary}, {GameSettings.YAW_RIGHT.primary}");
                            GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_VesselMover_Help_Roll")}: {GameSettings.ROLL_LEFT.primary}, {GameSettings.ROLL_RIGHT.primary}");
                            GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_VesselMover_Help_Pitch")}: {GameSettings.TRANSLATE_DOWN.primary}, {GameSettings.TRANSLATE_UP.primary}");
                            GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_VesselMover_Help_Yaw")}: {GameSettings.TRANSLATE_LEFT.primary}, {GameSettings.TRANSLATE_RIGHT.primary}");
                            GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_VesselMover_Help_AutoRotateRocket")}: {GameSettings.TRANSLATE_BACK.primary}");
                            GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_VesselMover_Help_AutoRotatePlane")}: {GameSettings.TRANSLATE_FWD.primary}");
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_VesselMover_Help_CycleAltitudes"));
                            GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_VesselMover_Help_ResetAltitude")}: {GameSettings.THROTTLE_CUTOFF.primary}");
                            GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_VesselMover_Help_AdjustAltitude")}: {GameSettings.THROTTLE_UP.primary}, {GameSettings.THROTTLE_DOWN.primary}");
                            GUILayout.EndVertical();
                        }
                        break;
                    }
                case State.Lowering:
                    {
                        if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_VesselMover_MoveVessel"), BDArmorySetup.ButtonStyle, GUILayout.Height(40))) { DropVessel(FlightGlobals.ActiveVessel); StartCoroutine(MoveVessel(FlightGlobals.ActiveVessel)); }
                        if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_VesselMover_DropVessel"), BDArmorySetup.ButtonStyle, GUILayout.Height(40))) DropVessel(FlightGlobals.ActiveVessel);
                        GUILayout.BeginHorizontal();
                        GUILayout.BeginVertical();
                        BDArmorySettings.VESSEL_MOVER_ENABLE_BRAKES = GUILayout.Toggle(BDArmorySettings.VESSEL_MOVER_ENABLE_BRAKES, StringUtils.Localize("#LOC_BDArmory_VesselMover_EnableBrakes"));
                        BDArmorySettings.VESSEL_MOVER_LOWER_FAST = GUILayout.Toggle(BDArmorySettings.VESSEL_MOVER_LOWER_FAST, StringUtils.Localize("#LOC_BDArmory_VesselMover_LowerFast"));
                        GUILayout.EndVertical();
                        GUILayout.BeginVertical();
                        BDArmorySettings.VESSEL_MOVER_ENABLE_SAS = GUILayout.Toggle(BDArmorySettings.VESSEL_MOVER_ENABLE_SAS, StringUtils.Localize("#LOC_BDArmory_VesselMover_EnableSAS"));
                        BDArmorySettings.VESSEL_MOVER_BELOW_WATER = GUILayout.Toggle(BDArmorySettings.VESSEL_MOVER_BELOW_WATER, StringUtils.Localize("#LOC_BDArmory_VesselMover_BelowWater"));
                        GUILayout.EndVertical();
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_VesselMover_MinLowerSpeed")}: {BDArmorySettings.VESSEL_MOVER_MIN_LOWER_SPEED}", GUILayout.Width(130));
                        BDArmorySettings.VESSEL_MOVER_MIN_LOWER_SPEED = BDAMath.RoundToUnit(GUILayout.HorizontalSlider(BDArmorySettings.VESSEL_MOVER_MIN_LOWER_SPEED, 0.1f, 1f), 0.1f);
                        GUILayout.EndHorizontal();
                        break;
                    }
                case State.Spawning:
                    {
                        GUILayout.Label($"Spawning craft...", BDArmorySetup.SelectedButtonStyle, GUILayout.Height(40));
                        GUILayout.BeginHorizontal();
                        BDArmorySettings.VESSEL_MOVER_CHOOSE_CREW = GUILayout.Toggle(BDArmorySettings.VESSEL_MOVER_CHOOSE_CREW, StringUtils.Localize("#LOC_BDArmory_VesselMover_ChooseCrew"));
                        BDArmorySettings.VESSEL_MOVER_PLACE_AFTER_SPAWN = GUILayout.Toggle(BDArmorySettings.VESSEL_MOVER_PLACE_AFTER_SPAWN, StringUtils.Localize("#LOC_BDArmory_VesselMover_PlaceAfterSpawn"));
                        GUILayout.EndHorizontal();
                        break;
                    }
            }
            GUILayout.EndVertical();
            GUIUtils.DragWindow();
            GUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectVesselMover, previousVesselMoverWindowHeight);
            GUIUtils.UpdateGUIRect(BDArmorySetup.WindowRectVesselMover, guiCheckIndex);
            GUIUtils.UseMouseEventInRect(BDArmorySetup.WindowRectVesselMover);
        }

        /// <summary>
        /// Reset the height of the window so that it shrinks.
        /// </summary>
        void ResetWindowHeight()
        {
            BDArmorySetup.WindowRectVesselMover.height = 0;
            GUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectVesselMover, previousVesselMoverWindowHeight);
        }

        public void SetVisible(bool visible)
        {
            if (!visible)
            {
                if (state == State.Spawning)
                {
                    abortCraftSelection = true;
                    state = State.None;
                }
                craftBrowser = null; // Make sure the craft browser is cleaned up.
            }
            BDArmorySetup.showVesselMoverGUI = visible;
            GUIUtils.SetGUIRectVisible(guiCheckIndex, visible);
            if (button != null)
            {
                if (visible) button.SetTrue(false);
                else button.SetFalse(false);
            }
        }
        void ShowVMGUI() => SetVisible(true);
        void HideVMGUI() => SetVisible(false);

        #region Vessel Selection
        internal static int _vesselGUICheckIndex = -1;
        bool showVesselSelection = false;
        Vector2 vesselSelectionScrollPos = default;
        float selectionTimer = 0;
        string selectedVesselURL = "";
        string selectionFilter = "";
        bool focusFilterField = false; // Focus the filter text field.
        bool folderSelectionMode = false; // Show SPH/VAB and folders instead of craft files.

        public void ShowVesselSelection(Action<string> selectedCallback = null, Action cancelledCallback = null)
        {
            if (craftBrowser == null)
            {
                craftBrowser = new CustomCraftBrowserDialog();
                craftBrowser.UpdateList();
            }
            craftBrowser.selectFileCallback = selectedCallback;
            craftBrowser.cancelledCallback = cancelledCallback;
            selectedVesselURL = "";
            showVesselSelection = true;
            focusFilterField = true;
            selectionTimer = Time.realtimeSinceStartup;
            craftBrowser.CheckCurrent();
            GUIUtils.SetGUIRectVisible(_vesselGUICheckIndex, true);
        }

        public void HideVesselSelection()
        {
            showVesselSelection = false;
            GUIUtils.SetGUIRectVisible(_vesselGUICheckIndex, false);
        }

        List<string> FilteredCraft
        {
            get
            {
                if (_filteredCraft.Item1 != selectionFilter || _filteredCraft.Item2 < craftBrowser.craftListUpdateTimestamp)
                {
                    // Something changed, update the filtered list.
                    _filteredCraft.Item1 = selectionFilter;
                    _filteredCraft.Item2 = craftBrowser.craftListUpdateTimestamp;
                    _filteredCraft.Item3 = [.. craftBrowser.craftList.Where(kvp => kvp.Key != null && kvp.Value != null && kvp.Value.shipName.ToLower().Contains(selectionFilter.ToLower())).Select(kvp => kvp.Key)];
                }
                return _filteredCraft.Item3;
            }
        }
        (string, float, List<string>) _filteredCraft = ("", 0, []);

        public void VesselSelectionWindow(int windowID)
        {
            GUILayout.BeginVertical();
            selectionFilter = GUIUtils.TextField(selectionFilter, " Filter", "VMFilterField");
            if (focusFilterField)
            {
                GUI.FocusControl("VMFilterField");
                focusFilterField = false;
            }
            vesselSelectionScrollPos = GUILayout.BeginScrollView(vesselSelectionScrollPos, GUI.skin.box, GUILayout.Width(BDArmorySetup.WindowRectVesselMoverVesselSelection.width - 15), GUILayout.MaxHeight(BDArmorySetup.WindowRectVesselMoverVesselSelection.height - 60));
            if (folderSelectionMode)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("SPH", CustomCraftBrowserDialog.ButtonStyle, GUILayout.Height(80))) craftBrowser.ChangeFolder(EditorFacility.SPH);
                if (GUILayout.Button("VAB", CustomCraftBrowserDialog.ButtonStyle, GUILayout.Height(80))) craftBrowser.ChangeFolder(EditorFacility.VAB);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label(craftBrowser.DisplayFolder, CustomCraftBrowserDialog.LabelStyle, GUILayout.Height(50), GUILayout.ExpandWidth(true));
                if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_Generic_Select"), CustomCraftBrowserDialog.ButtonStyle, GUILayout.Height(50), GUILayout.MaxWidth(BDArmorySetup.WindowRectVesselMoverVesselSelection.width / 3))) folderSelectionMode = false;
                GUILayout.EndHorizontal();
                foreach (var folder in craftBrowser.subfolders)
                {
                    if (GUILayout.Button($"{folder}", CustomCraftBrowserDialog.ButtonStyle, GUILayout.MaxHeight(60)))
                    {
                        craftBrowser.ChangeFolder(craftBrowser.Facility, folder);
                        break; // The iteration can't continue since subfolders has changed.
                    }
                }
            }
            else
            {
                foreach (var vesselURL in FilteredCraft)
                {
                    if (!craftBrowser.craftList.TryGetValue(vesselURL, out var vesselInfo)) continue; // This shouldn't happen.
                    GUILayout.BeginHorizontal(); // Vessel buttons
                    if (GUILayout.Button($"{vesselInfo.shipName}", selectedVesselURL == vesselURL ? CustomCraftBrowserDialog.SelectedButtonStyle : CustomCraftBrowserDialog.ButtonStyle, GUILayout.MaxHeight(64), GUILayout.MaxWidth(BDArmorySetup.WindowRectVesselMoverVesselSelection.width - 230)))
                    {
                        if (Time.realtimeSinceStartup - selectionTimer < 0.5f)
                        {
                            craftBrowser.selectFileCallback?.Invoke(vesselURL);
                            HideVesselSelection();
                        }
                        else if (selectedVesselURL == vesselURL) { selectedVesselURL = ""; }
                        else { selectedVesselURL = vesselURL; }
                        selectionTimer = Time.realtimeSinceStartup;
                    }
                    GUILayout.Label(VesselInfoEntry(vesselURL, vesselInfo, true), CustomCraftBrowserDialog.InfoStyle, GUILayout.Width(166));
                    GUILayout.Label(craftBrowser.craftThumbnails.GetValueOrDefault(vesselURL), CustomCraftBrowserDialog.InfoStyle, GUILayout.Height(64), GUILayout.Width(64));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_Generic_Select"), selectedVesselURL != "" ? BDArmorySetup.ButtonStyle : BDArmorySetup.SelectedButtonStyle) && selectedVesselURL != "")
            {
                if (craftBrowser.selectFileCallback != null) craftBrowser.selectFileCallback(selectedVesselURL);
                HideVesselSelection();
            }
            if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_Generic_Cancel"), BDArmorySetup.ButtonStyle))
            {
                if (craftBrowser.cancelledCallback != null) craftBrowser.cancelledCallback();
                HideVesselSelection();
            }
            if (GUILayout.Button(folderSelectionMode ? StringUtils.Localize("#LOC_BDArmory_CraftBrowser_Craft") : StringUtils.Localize("#LOC_BDArmory_CraftBrowser_Folder"), folderSelectionMode ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle, GUILayout.Width(BDArmorySetup.WindowRectVesselMoverVesselSelection.width / 6)))
            {
                folderSelectionMode = !folderSelectionMode;
            }
            if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_CraftBrowser_Refresh"), BDArmorySetup.ButtonStyle, GUILayout.Width(BDArmorySetup.WindowRectVesselMoverVesselSelection.width / 6)))
            {
                craftBrowser.UpdateList();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            #region Resizing
            var resizeRect = new Rect(BDArmorySetup.WindowRectVesselMoverVesselSelection.width - 16, BDArmorySetup.WindowRectVesselMoverVesselSelection.height - 16, 16, 16);
            GUI.DrawTexture(resizeRect, GUIUtils.resizeTexture, ScaleMode.StretchToFill, true);
            if (Event.current.type == EventType.MouseDown && resizeRect.Contains(Event.current.mousePosition))
            {
                resizingSelectionWindow = true;
            }
            else GUIUtils.DragWindow();
            if (resizingSelectionWindow && Event.current.type == EventType.Repaint)
            { BDArmorySetup.WindowRectVesselMoverVesselSelection.size += Mouse.delta / BDArmorySettings.UI_SCALE_ACTUAL; }
            #endregion
            GUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectVesselMoverVesselSelection);
            GUIUtils.UpdateGUIRect(BDArmorySetup.WindowRectVesselMoverVesselSelection, _vesselGUICheckIndex);
            GUIUtils.UseMouseEventInRect(BDArmorySetup.WindowRectVesselMoverVesselSelection);
        }

        readonly StringBuilder vesselInfoEntry = new();
        public string VesselInfoEntry(string vesselURL, CraftProfileInfo vesselInfo, bool withCrewCount)
        {
            vesselInfoEntry.Clear();
            vesselInfoEntry.AppendLine(
                $"{StringUtils.Localize("#LOC_BDArmory_CraftBrowser_Parts")}: {(
                    vesselInfo.partCount < 101 ? vesselInfo.partCount :
                    vesselInfo.partCount < 201 ? $"<color=yellow>{vesselInfo.partCount}</color>" :
                    vesselInfo.partCount < 301 ? $"<color=red>{vesselInfo.partCount}</color>" :
                    $"<b><color=red>{vesselInfo.partCount}</color></b>"
                )},  {StringUtils.Localize("#LOC_BDArmory_CraftBrowser_Mass")}: {(
                    vesselInfo.totalMass < 1000f ? $"{vesselInfo.totalMass:G3}t" :
                    $"{vesselInfo.totalMass / 1000f:G3}kt"
                )}"
            );
            if (withCrewCount)
                vesselInfoEntry.AppendLine($"Crew count: {(craftBrowser.crewCounts.ContainsKey(vesselURL) ? craftBrowser.crewCounts[vesselURL].ToString() : "unknown")}");
            vesselInfoEntry.Append(
                vesselInfo.UnavailableShipParts.Count > 0 ? $"<b><color=red>{StringUtils.Localize("#LOC_BDArmory_CraftBrowser_InvalidParts")}</color></b>" :
                $"{StringUtils.Localize("#LOC_BDArmory_CraftBrowser_Version")}: {(vesselInfo.compatibility == VersionCompareResult.COMPATIBLE ? $"{vesselInfo.version}" :
                $"<color=red>{vesselInfo.version}</color>")}{(vesselInfo.UnavailableShipPartModules.Count > 0 ? $"  <color=red>{StringUtils.Localize("#LOC_BDArmory_CraftBrowser_UnknownModules")}</color>" : "")}"
            );
            return vesselInfoEntry.ToString();
        }

        #endregion

        #region Crew Selection
        internal static int _crewGUICheckIndex = -1;
        bool showCrewSelection = false;
        Rect crewSelectionWindowRect = new Rect(0, 0, 300, 400);
        Vector2 crewSelectionScrollPos = default;
        float crewSelectionTimer = 0;
        HashSet<string> ActiveCrewMembers = new HashSet<string>();
        bool newCustomKerbal = false;
        string newKerbalName = "";
        bool focusKerbalNameField = false; // Focus the kerbal name field.
        bool notThisFrame = true; // Delay for dynamically added text field.
        ProtoCrewMember.Gender newKerbalGender = ProtoCrewMember.Gender.Male;
        bool removeKerbals = false;

        /// <summary>
        /// Show the crew selection window.
        /// </summary>
        /// <param name="position">Position of the mouse click.</param>
        /// <param name="vesselSpawnConfig">The VesselSpawnConfig clicked on.</param>
        public void ShowCrewSelection(Vector2 position)
        {
            crewSelectionWindowRect.position = position + new Vector2(50, -crewSelectionWindowRect.height / 2); // Centred and slightly offset to allow clicking the same spot.
            showCrewSelection = true;
            // Find any crew on active vessels.
            ActiveCrewMembers.Clear();
            foreach (var vessel in FlightGlobals.Vessels)
            {
                if (vessel == null || !vessel.loaded) continue;
                foreach (var part in vessel.Parts)
                {
                    if (part == null) continue;
                    foreach (var crew in part.protoModuleCrew)
                    {
                        if (crew == null) continue;
                        ActiveCrewMembers.Add(crew.name);
                    }
                }
            }
            GUIUtils.SetGUIRectVisible(_crewGUICheckIndex, true);
            foreach (var crew in HighLogic.CurrentGame.CrewRoster.Kerbals(ProtoCrewMember.KerbalType.Crew)) // Set any non-assigned crew as available.
            {
                if (crew.rosterStatus != ProtoCrewMember.RosterStatus.Assigned)
                    crew.rosterStatus = ProtoCrewMember.RosterStatus.Available;
            }
            crewSelectionTimer = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// Hide the crew selection window.
        /// </summary>
        public void HideCrewSelection()
        {
            showCrewSelection = false;
            newCustomKerbal = false;
            removeKerbals = false;
            GUIUtils.SetGUIRectVisible(_crewGUICheckIndex, false);
        }

        /// <summary>
        /// Crew selection window.
        /// </summary>
        /// <param name="windowID"></param>
        public void CrewSelectionWindow(int windowID)
        {
            KerbalRoster kerbalRoster = HighLogic.CurrentGame.CrewRoster;
            GUILayout.BeginVertical();
            if (BDArmorySettings.VESSEL_SPAWN_FILL_SEATS == 0) GUILayout.Label($"Select up to {crewCapacity} kerbals to populate {vesselNameToSpawn}.");
            crewSelectionScrollPos = GUILayout.BeginScrollView(crewSelectionScrollPos, GUI.skin.box, GUILayout.Width(crewSelectionWindowRect.width - 15), GUILayout.MaxHeight(crewSelectionWindowRect.height - 60));
            using (var kerbals = kerbalRoster.Kerbals(ProtoCrewMember.KerbalType.Crew).GetEnumerator())
                while (kerbals.MoveNext())
                {
                    ProtoCrewMember crewMember = kerbals.Current;
                    if (crewMember == null || ActiveCrewMembers.Contains(crewMember.name)) continue;
                    if (GUILayout.Button($"{crewMember.name}, {crewMember.gender}, {crewMember.trait}", KerbalNames.Contains(crewMember.name) ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
                    {
                        if (Time.realtimeSinceStartup - crewSelectionTimer < 0.5f)
                        {
                            KerbalNames.Add(crewMember.name);
                            HideCrewSelection();
                        }
                        else if (KerbalNames.Contains(crewMember.name)) KerbalNames.Remove(crewMember.name);
                        else KerbalNames.Add(crewMember.name);
                        crewSelectionTimer = Time.realtimeSinceStartup;
                    }
                }
            GUILayout.EndScrollView();
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_Generic_Select"), BDArmorySetup.ButtonStyle))
            { HideCrewSelection(); }
            if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_VesselMover_Any"), BDArmorySetup.ButtonStyle, GUILayout.Width(crewSelectionWindowRect.width / 6)))
            { KerbalNames.Clear(); HideCrewSelection(); }
            if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_Generic_New"), newCustomKerbal ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle, GUILayout.Width(crewSelectionWindowRect.width / 6)))
            {
                // Create a new Kerbal!
                newCustomKerbal = !newCustomKerbal;
                newKerbalName = "";
                focusKerbalNameField = newCustomKerbal;
                notThisFrame = true;
            }
            if (GUILayout.Button(" X", removeKerbals ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.CloseButtonStyle, GUILayout.Width(27)))
            {
                // Remove selected Kerbals!
                removeKerbals = !removeKerbals;
            }
            GUILayout.EndHorizontal();
            if (newCustomKerbal)
            {
                newKerbalName = GUIUtils.TextField(newKerbalName, " Enter a new Kerbal name...", "kerbalNameField");
                if (!notThisFrame && focusKerbalNameField)
                {
                    GUI.FocusControl("kerbalNameField");
                    focusKerbalNameField = false;
                }
                if (notThisFrame) notThisFrame = false;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_Generic_OK"), BDArmorySetup.ButtonStyle))
                {
                    newKerbalName = newKerbalName.Trim();
                    if (!string.IsNullOrEmpty(newKerbalName))
                    {
                        if (HighLogic.CurrentGame.CrewRoster.Exists(newKerbalName))
                        {
                            customMessage = $"Failed to add {newKerbalName}. They already exist!";
                            messageState = Messages.Custom;
                            Debug.LogWarning($"[BDArmory.VesselMover]: {customMessage}");
                        }
                        else
                        {
                            var crewMember = HighLogic.CurrentGame.CrewRoster.GetNewKerbal(ProtoCrewMember.KerbalType.Crew);
                            if (crewMember.ChangeName(newKerbalName))
                            {
                                crewMember.gender = newKerbalGender;
                                KerbalRoster.SetExperienceTrait(crewMember, KerbalRoster.pilotTrait); // Make the kerbal a pilot (so they can use SAS properly).
                                KerbalRoster.SetExperienceLevel(crewMember, KerbalRoster.GetExperienceMaxLevel()); // Make them experienced.
                                crewMember.isBadass = true; // Make them bad-ass (likes nearby explosions).
                                crewMember.courage = 0.5f;
                            }
                            else
                            {
                                customMessage = $"Failed to set name of {crewMember.name} to {newKerbalName}";
                                messageState = Messages.Custom;
                                Debug.LogWarning($"[BDArmory.VesselMover]: {customMessage}");
                                HighLogic.CurrentGame.CrewRoster.Remove(crewMember.name);
                            }
                        }
                    }
                    newCustomKerbal = false;
                }
                if (GUILayout.Button(newKerbalGender.ToStringCached(), BDArmorySetup.ButtonStyle, GUILayout.Width(crewSelectionWindowRect.width / 4)))
                {
                    var genders = Enum.GetValues(typeof(ProtoCrewMember.Gender)).Cast<ProtoCrewMember.Gender>();
                    bool found = false, set = false;
                    foreach (var gender in genders)
                    {
                        if (found) { newKerbalGender = gender; set = true; break; }
                        if (newKerbalGender == gender) found = true;
                    }
                    if (!set) newKerbalGender = genders.First();
                }
                GUILayout.EndHorizontal();
            }
            if (removeKerbals)
            {
                if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_VesselMover_ReallyRemoveKerbals"), BDArmorySetup.CloseButtonStyle))
                {
                    var cantRemove = KerbalRoster.GenerateInitialCrewRoster(HighLogic.CurrentGame.Mode).Crew.Select(crew => crew.name).ToHashSet();
                    KerbalNames = KerbalNames.Where(kerbal => !cantRemove.Contains(kerbal)).ToHashSet();
                    customMessage = $"Removing {string.Join(", ", KerbalNames)}";
                    messageState = Messages.Custom;
                    foreach (var kerbalName in KerbalNames) HighLogic.CurrentGame.CrewRoster.Remove(kerbalName);
                    KerbalNames.Clear();
                    removeKerbals = false;
                }
            }
            GUILayout.EndVertical();
            GUIUtils.DragWindow();
            GUIUtils.RepositionWindow(ref crewSelectionWindowRect);
            GUIUtils.UpdateGUIRect(crewSelectionWindowRect, _crewGUICheckIndex);
            GUIUtils.UseMouseEventInRect(crewSelectionWindowRect);
        }
        #endregion

        const int circleRes = 24;
        private LineRenderer moveIndicator;
        Vector3[] moveIndicatorPositions = new Vector3[circleRes + 3];
        private void DrawMovingIndicator()
        {
            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || !vessel.loaded || vessel.packed) return;

            var angle = 360f / circleRes;
            var radius = 2f + vessel.GetRadius();
            var centre = vessel.CoM;
            VectorUtils.GetWorldCoordinateFrame(vessel.mainBody, centre, out Vector3 up, out Vector3 north, out Vector3 right);

            moveIndicatorPositions[0] = centre + radius * north;
            for (int i = 1; i < circleRes; i++)
            {
                moveIndicatorPositions[i] = centre + Quaternion.AngleAxis(i * angle, up) * north * radius;
            }
            moveIndicatorPositions[circleRes] = centre + radius * north;
            moveIndicatorPositions[circleRes + 1] = centre;
            moveIndicatorPositions[circleRes + 2] = centre + RadarAltitude(vessel) * -up;

            moveIndicator.SetPositions(moveIndicatorPositions);
        }
        #endregion

        #region Toolbar button
        public void AddToolbarButton()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            StartCoroutine(ToolbarButtonRoutine());
        }
        public void RemoveToolbarButton()
        {
            if (button == null) return;
            if (!HighLogic.LoadedSceneIsFlight) return;
            ApplicationLauncher.Instance.RemoveModApplication(button);
            button = null;
            buttonSetup = false;
        }

        IEnumerator ToolbarButtonRoutine()
        {
            if (buttonSetup) // Just update the callbacks for the current instance.
            {
                button.onTrue = ShowVMGUI;
                button.onFalse = HideVMGUI;
                yield break;
            }
            yield return new WaitUntil(() => ApplicationLauncher.Ready && BDArmorySetup.toolbarButtonAdded); // Wait until after the main BDA toolbar button.
            Texture buttonTexture = GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "icon_vm", false);
            button = ApplicationLauncher.Instance.AddModApplication(ShowVMGUI, HideVMGUI, Dummy, Dummy, Dummy, Dummy, ApplicationLauncher.AppScenes.FLIGHT, buttonTexture);
            buttonSetup = true;
            if (BDArmorySetup.showVesselMoverGUI) button.SetTrue(false);
        }
        void Dummy() { }
        #endregion
    }

    internal class CustomCraftBrowserDialog
    {
        // Keep some of these as static so that they're remembered between instances of showing the dialog.
        static EditorFacility facility = EditorFacility.None;
        static string profile = HighLogic.SaveFolder;
        static string baseFolder;
        static string displayFolder;
        static string currentFolder;

        // Public getters
        public string CurrentFolder => currentFolder;
        public string BaseFolder => baseFolder;
        public string DisplayFolder => displayFolder;
        public string GameName => profile;
        public EditorFacility Facility => facility;

        string _currentFolder; // For checking if the current folder has changed between instances and thus the craftList needs refreshing.
        public float craftListUpdateTimestamp = 0; // Timestamp for when the craftList was last updated.
        public Dictionary<string, CraftProfileInfo> craftList = [];
        public Dictionary<string, int> crewCounts = [];
        public Dictionary<string, Texture2D> craftThumbnails = [];
        public List<string> subfolders = [];
        public Action<string> selectFileCallback = null;
        public Action cancelledCallback = null;
        public static Dictionary<string, string> shipNames = []; // craftURLs to ship names.
        public static GUIStyle ButtonStyle = new(BDArmorySetup.ButtonStyle);
        public static GUIStyle SelectedButtonStyle = new(BDArmorySetup.SelectedButtonStyle);
        public static GUIStyle InfoStyle = new(BDArmorySetup.BDGuiSkin.label);
        public static GUIStyle LabelStyle = new(BDArmorySetup.BDGuiSkin.label);
        public void UpdateList()
        {
            if (CheckCurrent()) return; // If CheckCurrent changes the folder, then UpdateList gets called internally.
            if (string.IsNullOrEmpty(currentFolder) || !Directory.Exists(currentFolder)) ChangeFolder(facility); // Default to the current base folder if something is wrong.
            craftList = Directory.GetFiles(currentFolder, "*.craft").ToDictionary(craft => craft, craft => new CraftProfileInfo());
            if (craftList.ContainsKey(Path.Combine(currentFolder, "Auto-Saved Ship.craft"))) craftList.Remove(Path.Combine(currentFolder, "Auto-Saved Ship.craft")); // Ignore the Auto-Saved Ship.
            subfolders = Directory.GetDirectories(currentFolder).Select(Path.GetFileName).ToList();
            if (currentFolder != baseFolder) subfolders.Insert(0, "..");
            CraftProfileInfo.PrepareCraftMetaFileLoad();
            var thumbURLSubDir = $"/{Path.Combine(facility.ToString(), currentFolder.Substring(baseFolder.Length).Trim('/'))}";
            foreach (var craft in craftList.Keys.ToList())
            {
                var craftMeta = Path.Combine(currentFolder, $"{Path.GetFileNameWithoutExtension(craft)}.loadmeta");
                if (File.Exists(craftMeta) && File.GetLastWriteTime(craftMeta) > File.GetLastWriteTime(craft)) // If the loadMeta file exists and has a timestamp that's later than the craft file (because WTF KSP‽), use it, otherwise generate one.
                {
                    craftList[craft].LoadFromMetaFile(craftMeta);
                }
                else
                {
                    var craftNode = ConfigNode.Load(craft);
                    craftList[craft].LoadDetailsFromCraftFile(craftNode, craft);
                    craftList[craft].SaveToMetaFile(craftMeta);
                }
                var thumbURL = $"/thumbs/{GetPlayerCraftThumbnailName(profile, thumbURLSubDir, Path.GetFileNameWithoutExtension(craft))}";
                craftThumbnails[craft] = ShipConstruction.GetThumbnail(thumbURL);
            }
            var failedToParse = craftList.Where(kvp => kvp.Value is null || kvp.Value.partNames is null).ToList();
            if (failedToParse.Count > 0) Debug.LogError($"[BDArmory.VesselMover]: Failed to properly parse some loadmeta files:\n{string.Join("\n  ", failedToParse)}");
            crewCounts = craftList.ToDictionary(kvp => kvp.Key, kvp => (kvp.Value is null || kvp.Value.partNames is null) ? 0 : kvp.Value.partNames.Where(p => SpawnUtils.PartCrewCounts.ContainsKey(p)).Sum(p => SpawnUtils.PartCrewCounts[p]));
            ButtonStyle.stretchHeight = true;
            ButtonStyle.fontSize = 24;
            SelectedButtonStyle.stretchHeight = true;
            SelectedButtonStyle.fontSize = 24;
            InfoStyle.fontSize = 14;
            InfoStyle.richText = true;
            LabelStyle.fontSize = 24;
            LabelStyle.alignment = TextAnchor.MiddleCenter;
            LabelStyle.normal.textColor = Color.green;
            shipNames.Where(kvp => File.Exists(kvp.Key)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value); // Remove any no longer valid ship names.
            foreach (var craft in craftList.Keys)
                shipNames[craft] = craftList[craft].shipName;
            craftListUpdateTimestamp = Time.realtimeSinceStartup;
        }

        public void ChangeFolder(EditorFacility facility, string subfolder = null, bool relative = true)
        {
            if (facility == EditorFacility.None) // Very first time used, default to the VAB if the current vessel was launched from there or fall back to the SPH.
            {
                facility = FlightDriver.LaunchSiteName == "LaunchPad" ? EditorFacility.VAB : EditorFacility.SPH;
            }
            if (facility != CustomCraftBrowserDialog.facility)
            {
                subfolder = null; // Revert to the base folder when changing facilities.
                CustomCraftBrowserDialog.facility = facility;
            }
            baseFolder = Path.GetFullPath(GetShipsPathFor(profile, facility).TrimEnd(['/']));
            if (!Directory.Exists(baseFolder))
            {
                var message = $"The base folder for the {facility} doesn't exist! Your KSP install is broken!";
                Debug.LogError($"[BDArmory.VesselMover]: {message}");
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                return;
            }
            if (!relative || string.IsNullOrEmpty(currentFolder)) currentFolder = baseFolder;
            if (string.IsNullOrEmpty(subfolder))
            {
                currentFolder = baseFolder;
            }
            else
            {
                var newFolder = Path.GetFullPath(Path.Combine(currentFolder, subfolder.TrimStart(['/'])));
                if (Directory.Exists(newFolder)) currentFolder = newFolder;
                else currentFolder = baseFolder;
            }
            _currentFolder = currentFolder;
            displayFolder = currentFolder.Substring(baseFolder.Length - facility.ToString().Length);
            UpdateList();
        }

        public bool CheckCurrent()
        {
            if (profile != HighLogic.SaveFolder) // The user changed saves after having opened the craft browser. Reset to the default.
            {
                profile = HighLogic.SaveFolder;
                baseFolder = null;
                displayFolder = null;
                currentFolder = null;
                ChangeFolder(EditorFacility.None);
                return true;
            }
            if (_currentFolder != currentFolder)
            {
                ChangeFolder(facility, currentFolder.Substring(baseFolder.Length), false); // Another instance changed the current folder, so switch to match it.
                return true;
            }
            return false;
        }

        public static string GetPlayerCraftThumbnailName(string profile, string thumbURLSubDir, string shipName)
        {
            if ((Versioning.version_major == 1 && Versioning.version_minor > 11) || Versioning.version_major > 1) // Introduced in 1.12
                return GetPlayerCraftThumbnailName_1_12(profile, thumbURLSubDir, shipName);
            return $"{profile}{thumbURLSubDir.Replace('/', '_')}_{shipName}";
        }
        public static string GetPlayerCraftThumbnailName_1_12(string profile, string thumbURLSubDir, string shipName) => ShipConstruction.GetPlayerCraftThumbnailName(profile, thumbURLSubDir, shipName);
        public static string GetShipsPathFor(string profile, EditorFacility facility)
        {
            if ((Versioning.version_major == 1 && Versioning.version_minor > 11) || Versioning.version_major > 1) // Introduced in 1.12
                return GetShipsPathFor_1_12(profile, facility);
            return Path.Combine(KSPUtil.ApplicationRootPath, "saves", profile, "Ships", ShipConstruction.GetShipsSubfolderFor(facility));
        }
        public static string GetShipsPathFor_1_12(string profile, EditorFacility facility) => ShipConstruction.GetShipsPathFor(profile, facility);
    }

    internal class CraftBrowserMissingThumbnailGenerator : MonoBehaviour
    {
        static CraftBrowserMissingThumbnailGenerator instance;
        public static CraftBrowserMissingThumbnailGenerator Instance
        {
            get
            {
                if (instance == null)
                {
                    GameObject gameObject = new() { name = "CraftBrowserMissingThumbnailGenerator" };
                    instance = gameObject.AddComponent<CraftBrowserMissingThumbnailGenerator>();
                }
                return instance;
            }
        }
        public static bool recurse = false;

        public void GenerateMissingThumbnails(EditorFacility facility) => StartCoroutine(GenerateMissingThumbnailsWorker(facility));
        IEnumerator GenerateMissingThumbnailsWorker(EditorFacility facility)
        {
            var craftBrowser = new CustomCraftBrowserDialog();
            craftBrowser.ChangeFolder(facility);
            var thumbURLRoot = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "thumbs"));

            List<string> folders = [craftBrowser.BaseFolder];
            if (recurse) folders.AddRange(Directory.GetDirectories(craftBrowser.BaseFolder, "*", SearchOption.AllDirectories).Select(p => p.Substring(craftBrowser.BaseFolder.Length).Trim('/')));
            foreach (var folder in folders)
            {
                craftBrowser.ChangeFolder(facility, folder, false);
                var thumbURLSubDir = $"/{Path.Combine(facility.ToString(), craftBrowser.CurrentFolder.Substring(craftBrowser.BaseFolder.Length).Trim('/'))}";
                bool hasShownMessage = false;
                foreach (var craft in craftBrowser.craftList.Keys)
                {
                    var craftInfo = craftBrowser.craftList[craft];
                    if (craftInfo.UnavailableShipParts.Count > 0)
                    {
                        Debug.Log($"[BDArmory.VesselMover]: Craft {craftInfo.shipName} has missing parts, unable to generate thumbnail.");
                        continue;
                    }
                    var thumbURL = Path.Combine(thumbURLRoot, $"{ShipConstruction.GetPlayerCraftThumbnailName(craftBrowser.GameName, thumbURLSubDir, Path.GetFileNameWithoutExtension(craft))}.png"); // Actual URL.
                    if (!File.Exists(thumbURL))
                    {
                        if (!hasShownMessage)
                        {
                            ScreenMessages.PostScreenMessage($"{StringUtils.Localize("#LOC_BDArmory_CraftBrowser_GenerateMissingThumbnailsGeneratingForCraftIn")} {craftBrowser.DisplayFolder}", 5);
                            Debug.Log($"[BDArmory.VesselMover]: Generating thumbnails for craft files in {craftBrowser.CurrentFolder}");
                            hasShownMessage = true;
                        }
                        // Load the ship and take a thumbnail of it.
                        Debug.Log($"[BDArmory.VesselMover]: Generating thumbnail for {craftInfo.shipName} at KSP{thumbURL.Substring(KSPUtil.ApplicationRootPath.Length)}");
                        ScreenMessages.PostScreenMessage($"{StringUtils.Localize("#LOC_BDArmory_CraftBrowser_GenerateMissingThumbnailsGeneratingFor")} {craftInfo.shipName}.", 3);
                        yield return null;
                        try
                        {
                            EditorLogic.LoadShipFromFile(craft);
                            ShipConstruction.CaptureThumbnail(EditorLogic.fetch.ship, $"/thumbs", ShipConstruction.GetPlayerCraftThumbnailName(craftBrowser.GameName, thumbURLSubDir, Path.GetFileNameWithoutExtension(craft)));
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[BDArmory.VesselMover]: Error capturing thumbnail of {craftInfo.shipName} {e.Message}\n{e.StackTrace}");
                            ScreenMessages.PostScreenMessage($"{StringUtils.Localize("#LOC_BDArmory_CraftBrowser_GenerateMissingThumbnailsFailure")} {craftInfo.shipName}", 5);
                        }
                    }
                }
            }
            ScreenMessages.PostScreenMessage(StringUtils.Localize("#LOC_BDArmory_CraftBrowser_GenerateMissingThumbnailsFinished"), 5);
            EditorLogic.LoadShipFromFile(null);
        }
    }
}