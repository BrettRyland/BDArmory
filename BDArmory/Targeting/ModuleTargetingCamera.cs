using System.Collections;
using UnityEngine;
using Debug = UnityEngine.Debug;

using BDArmory.Control;
using BDArmory.CounterMeasure;
using BDArmory.Extensions;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Weapons;
using BDArmory.Weapons.Missiles;
using System.Text;
using System;

namespace BDArmory.Targeting
{
    public class ModuleTargetingCamera : BDAPartModule
    {
        [KSPField]
        public string cameraTransformName;
        public Transform cameraParentTransform;

        [KSPField]
        public string eyeHolderTransformName;
        Transform eyeHolderTransform;

        [KSPField]
        public float maxRayDistance = 15500;

        [KSPField]
        public float gimbalLimit = 120;
        public bool gimbalLimitReached;

        [KSPField]
        public float traverseRate = 90;

        [KSPField]
        public bool rollCameraModel = false;

        [KSPField(isPersistant = true)]
        public bool cameraEnabled;

        [KSPField]
        public bool colorCamera = false;

        float fov
        {
            get
            {
                return zoomFovs[currentFovIndex];
            }
        }

        [KSPField]
        public string zoomFOVs = "40,15,3,1";
        float[] zoomFovs;
        float[] zoomTimes;

        [KSPField(isPersistant = true)]
        public int currentFovIndex;

        [KSPField(isPersistant = true)]
        public bool slaveTurrets;

        [KSPField(isPersistant = true)]
        public bool CoMLock;

        public bool radarLock;
        public Vessel lockedVessel;

        Part lockedPart;

        public bool guidingOrdnance = false;

        [KSPField(isPersistant = true)]
        public bool groundStabilized;

        /// <summary>
        /// Point on surface that camera is focused and stabilized on.
        /// </summary>
        public Vector3 groundTargetPosition = Vector3.zero;

        [KSPField(isPersistant = true)]
        public double savedLat;

        [KSPField(isPersistant = true)]
        public double savedLong;

        [KSPField(isPersistant = true)]
        public double savedAlt;

        public Vector3 bodyRelativeGTP
        {
            get
            {
                return new Vector3d(savedLat, savedLong, savedAlt);
            }

            set
            {
                savedLat = value.x;
                savedLong = value.y;
                savedAlt = value.z;
            }
        }

        bool resetting;

        public bool surfaceDetected;

        /// <summary>
        /// Point where camera is focused, regardless of whether surface is detected or not.
        /// </summary>
        public Vector3 targetPointPosition;

        [KSPField(isPersistant = true)]
        public bool nvMode;

        //GUI
        public static int camIndex = 0;
        public static ModuleTargetingCamera activeCam;
        public static bool camRectInitialized;
        public static bool windowIsOpen;
        private static float camImageSize = 360;
        private static float adjCamImageSize = 360;
        internal static bool ResizingWindow;
        internal static bool SlewingMouseCam;

        internal static bool SlewingButtonCam;
        float finalSlewSpeed;
        Vector2 slewInput = Vector2.zero;
        public static bool IsSlewing => SlewingMouseCam;

        private static float gap = 2;
        private static float buttonHeight = 18;
        private static float controlsStartY = 22;
        private static float windowWidth = adjCamImageSize + (3 * buttonHeight) + 16 + 2 * gap;
        private static float windowHeight = adjCamImageSize + 23;

        Texture2D riTex;

        Texture2D rollIndicatorTexture
        {
            get
            {
                if (!riTex)
                { riTex = GameDatabase.Instance.GetTexture("BDArmory/Textures/rollIndicator", false); }
                return riTex;
            }
        }

        Texture2D rrTex;

        Texture2D rollReferenceTexture
        {
            get
            {
                if (!rrTex)
                { rrTex = GameDatabase.Instance.GetTexture("BDArmory/Textures/rollReference", false); }
                return rrTex;
            }
        }

        public MissileFire WeaponManager
        {
            get
            {
                if (field == null || !field.IsPrimaryWM || field.vessel != vessel)
                    field = vessel && vessel.loaded ? vessel.ActiveController().WM : null;
                return field;
            }
        }

        [KSPEvent(guiName = "#LOC_BDArmory_Enable", guiActive = true, guiActiveEditor = false)]//Enable
        public void EnableButton()
        {
            EnableCamera();
        }

        [KSPAction("Enable")]
        public void AGEnable(KSPActionParam param)
        {
            EnableCamera();
        }

        public void ToggleCamera()
        {
            if (cameraEnabled)
            {
                DisableCamera();
            }
            else
            {
                EnableCamera();
            }
        }

        public void EnableCamera()
        {
            if (!TargetingCamera.Instance)
            {
                Debug.Log("[BDArmory.ModuleTargetingCamera]: Tried to enable targeting camera, but camera instance is null.");
                return;
            }
            if (vessel.isActiveVessel)
            {
                activeCam = this;
                windowIsOpen = true;
                TargetingCamera.Instance.EnableCamera(cameraParentTransform);
                TargetingCamera.Instance.color = colorCamera;
                TargetingCamera.Instance.nvMode = nvMode;
                TargetingCamera.Instance.SetFOV(fov);
                ResizeTargetWindow();
            }

            cameraEnabled = true;

            var weaponManager = WeaponManager;
            if (weaponManager)
            {
                weaponManager.mainTGP = this;
            }

            BDATargetManager.RegisterLaserPoint(this);
        }

        public void DisableCamera()
        {
            cameraEnabled = false;
            groundStabilized = false;

            if (slaveTurrets)
            {
                UnslaveTurrets();
            }
            //StopResetting();

            if (vessel.isActiveVessel)
            {
                if (!TargetingCamera.Instance)
                {
                    Debug.Log("[BDArmory.ModuleTargetingCamera]: Tried to disable targeting camera, but camera instance is null.");
                    return;
                }

                TargetingCamera.Instance.DisableCamera();
                if (activeCam == this)
                {
                    activeCam = FindNextActiveCamera();
                    if (!activeCam)
                    {
                        windowIsOpen = false;
                    }
                }
                else
                {
                    windowIsOpen = false;
                }
            }
            BDATargetManager.ActiveLasers.Remove(this);

            var weaponManager = WeaponManager;
            if (weaponManager && weaponManager.mainTGP == this)
            {
                weaponManager.mainTGP = FindNextActiveCamera();
            }
        }

        ModuleTargetingCamera FindNextActiveCamera()
        {
            using (var mtc = VesselModuleRegistry.GetModules<ModuleTargetingCamera>(vessel).GetEnumerator())
                while (mtc.MoveNext())
                {
                    if (mtc.Current && mtc.Current.cameraEnabled)
                    {
                        mtc.Current.EnableCamera();
                        return mtc.Current;
                    }
                }

            return null;
        }

        public override void OnAwake()
        {
            base.OnAwake();

            if (HighLogic.LoadedSceneIsFlight)
            {
                if (!TargetingCamera.Instance)
                {
                    new GameObject("TargetingCameraObject").AddComponent<TargetingCamera>();
                }
            }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (!HighLogic.LoadedSceneIsFlight) return;
            part.force_activate();

            //GUI setup
            if (!camRectInitialized)
            {
                BDArmorySetup.WindowRectTargetingCam = new Rect(BDArmorySetup.WindowRectTargetingCam.x, BDArmorySetup.WindowRectTargetingCam.y, windowWidth, windowHeight);
                camRectInitialized = true;
            }

            cameraParentTransform = part.FindModelTransform(cameraTransformName);

            eyeHolderTransform = part.FindModelTransform(eyeHolderTransformName);

            ParseFovs();
            UpdateSlewRate();

            GameEvents.onVesselCreate.Add(Disconnect);

            if (cameraEnabled)
            {
                Debug.Log("[BDArmory.ModuleTargetingCamera]: saved gtp: " + bodyRelativeGTP);
                DelayedEnable();
            }
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.BetterLateThanNever, CameraTracking);
        }

        void Disconnect(Vessel v)
        {
            var weaponManager = WeaponManager;
            if (weaponManager && vessel)
            {
                if (weaponManager.vessel != vessel)
                {
                    if (slaveTurrets)
                    {
                        weaponManager.slavingTurrets = false;
                        weaponManager.slavedPosition = Vector3.zero;
                        weaponManager.slavedTarget = TargetSignatureData.noTarget;
                    }
                }
            }
        }

        public void DelayedEnable()
        {
            StartCoroutine(DelayedEnableRoutine());
        }

        bool delayedEnabling;

        IEnumerator DelayedEnableRoutine()
        {
            if (delayedEnabling) yield break;
            delayedEnabling = true;

            Vector3d savedGTP = bodyRelativeGTP;
            Debug.Log("[BDArmory.ModuleTargetingCamera]: saved gtp: " + BodyUtils.FormattedGeoPos(savedGTP, true));
            Debug.Log("[BDArmory.ModuleTargetingCamera]: groundStabilized: " + groundStabilized);

            while (TargetingCamera.Instance == null)
            {
                yield return null;
            }
            while (!FlightGlobals.ready)
            {
                yield return null;
            }
            while (FlightCamera.fetch == null)
            {
                yield return null;
            }
            while (FlightCamera.fetch.mainCamera == null)
            {
                yield return null;
            }
            while (vessel.packed)
            {
                yield return null;
            }
            while (vessel.mainBody == null)
            {
                yield return null;
            }
            while (BDArmorySetup.Instance == null)
            {
                yield return null;
            }

            EnableCamera();
            if (groundStabilized)
            {
                Debug.Log("[BDArmory.ModuleTargetingCamera]: Camera delayed enabled");
                groundTargetPosition = VectorUtils.GetWorldSurfacePostion(savedGTP, vessel.mainBody);// vessel.mainBody.GetWorldSurfacePosition(bodyRelativeGTP.x, bodyRelativeGTP.y, bodyRelativeGTP.z);
                Vector3 lookVector = groundTargetPosition - cameraParentTransform.position;
                PointCameraModel(lookVector);
                GroundStabilize();
            }
            delayedEnabling = false;

            Debug.Log("[BDArmory.ModuleTargetingCamera]: post load saved gtp: " + bodyRelativeGTP);
        }

        void PointCameraModel(Vector3 lookVector)
        {
            Vector3 worldUp = VectorUtils.GetUpDirection(cameraParentTransform.position);
            if (rollCameraModel)
            {
                cameraParentTransform.rotation = Quaternion.LookRotation(lookVector, worldUp);
            }
            else
            {
                Vector3 camUp = cameraParentTransform.up;
                if (eyeHolderTransform) camUp = Vector3.Cross(cameraParentTransform.forward, eyeHolderTransform.right);
                cameraParentTransform.rotation = Quaternion.LookRotation(lookVector, camUp);
            }
        }

        void UpdateCameraDirection()
        {
            Vector3 worldUp = VectorUtils.GetUpDirection(cameraParentTransform.position);
            if (vessel.isActiveVessel && activeCam == this && TargetingCamera.cameraTransform)
            {
                TargetingCamera.cameraTransform.rotation = Quaternion.LookRotation(cameraParentTransform.forward, worldUp);
            }
        }

        void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (cameraEnabled && TargetingCamera.ReadyForUse && vessel.IsControllable)
            {
                if (delayedEnabling) return;

                if (!TargetingCamera.Instance || FlightGlobals.currentMainBody == null)
                {
                    return;
                }

                if (activeCam == this)
                {
                    if (zoomFovs != null)
                    {
                        TargetingCamera.Instance.SetFOV(fov);
                    }

                    UpdateCameraDirection();
                }

                CameraTracking();
            }
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (cameraEnabled && !vessel.packed && !vessel.IsControllable)
                {
                    DisableCamera();
                }
            }
        }

        void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (cameraEnabled && !vessel.packed)
                {
                    if (!vessel.IsControllable)
                    {
                        DisableCamera();
                    }
                    if (delayedEnabling) return;
                }
            }
        }

        void CameraTracking()
        {
            if (cameraEnabled && TargetingCamera.ReadyForUse && vessel.IsControllable)
            {
                if (delayedEnabling) return;
                if (!TargetingCamera.Instance || FlightGlobals.currentMainBody == null) return;

                if (radarLock)
                {
                    UpdateRadarLock();
                }

                if (groundStabilized && !slewingToPosition)
                {
                    if (lockedVessel != null)
                    {
                        if (lockedPart != null)
                            groundTargetPosition = lockedPart.transform.position;
                        else
                            groundTargetPosition = lockedVessel.CoM;

                        groundTargetPosition = lockedVessel.CoM;
                    }
                    else
                    {
                        groundTargetPosition = VectorUtils.GetWorldSurfacePostion(bodyRelativeGTP, vessel.mainBody);//vessel.mainBody.GetWorldSurfacePosition(bodyRelativeGTP.x, bodyRelativeGTP.y, bodyRelativeGTP.z);
                        guidingOrdnance = false;
                        lockedPart = null;
                    }
                    Vector3 lookVector = groundTargetPosition - cameraParentTransform.position;
                    Vector3 currLookDirection = cameraParentTransform.forward;
                    float trackError = VectorUtils.Angle(currLookDirection, lookVector);
                    if (trackError > traverseRate * Time.fixedDeltaTime)
                    {
                        lookVector = Vector3.Slerp(currLookDirection, lookVector, traverseRate * Time.fixedDeltaTime / trackError);
                    }

                    //cameraParentTransform.rotation = Quaternion.LookRotation(lookVector);
                    PointCameraModel(lookVector);
                }

                Vector3 lookDirection = cameraParentTransform.forward;
                Vector3 cameraForward = cameraParentTransform.parent.forward;
                float gimbalAngle = VectorUtils.Angle(lookDirection, cameraForward);
                if (gimbalAngle > gimbalLimit)
                {
                    //lookDirection = Vector3.RotateTowards(cameraForward, lookDirection, gimbalLimit * Mathf.Deg2Rad, 0);
                    lookDirection = Vector3.Slerp(cameraForward, lookDirection, gimbalLimit / gimbalAngle);
                    gimbalLimitReached = true;
                    lockedVessel = null;
                    lockedPart = null;
                    guidingOrdnance = false;
                }
                else
                {
                    gimbalLimitReached = false;
                }

                if (!groundStabilized || gimbalLimitReached)
                {
                    PointCameraModel(lookDirection);
                }

                if (eyeHolderTransform)
                {
                    Vector3 projectedForward = cameraParentTransform.forward.ProjectOnPlanePreNormalized(eyeHolderTransform.parent.up);
                    if (projectedForward != Vector3.zero)
                    {
                        eyeHolderTransform.rotation = Quaternion.LookRotation(projectedForward, eyeHolderTransform.parent.up);
                    }
                }

                UpdateControls();
                UpdateSlaveData();
                GetHitPoint();
            }
        }

        void UpdateKeyInputs()
        {
            if (!vessel.isActiveVessel || activeCam != this)
            {
                return;
            }

            if (BDInputUtils.GetKey(BDInputSettingsFields.TGP_SLEW_LEFT))
            {
                slewInput.x = -1;
            }
            else if (BDInputUtils.GetKey(BDInputSettingsFields.TGP_SLEW_RIGHT))
            {
                slewInput.x = 1;
            }

            if (BDInputUtils.GetKey(BDInputSettingsFields.TGP_SLEW_UP))
            {
                slewInput.y = 1;
            }
            else if (BDInputUtils.GetKey(BDInputSettingsFields.TGP_SLEW_DOWN))
            {
                slewInput.y = -1;
            }

            if (BDInputUtils.GetKeyDown(BDInputSettingsFields.TGP_IN))
            {
                ZoomIn();
            }
            else if (BDInputUtils.GetKeyDown(BDInputSettingsFields.TGP_OUT))
            {
                ZoomOut();
            }

            if (BDInputUtils.GetKeyDown(BDInputSettingsFields.TGP_LOCK))
            {
                if (groundStabilized)
                {
                    ClearTarget();
                }
                else
                {
                    GroundStabilize();
                }
            }

            if (BDInputUtils.GetKeyDown(BDInputSettingsFields.TGP_NV))
            {
                ToggleNV();
            }

            if (groundStabilized && BDInputUtils.GetKeyDown(BDInputSettingsFields.TGP_SEND_GPS))
            {
                SendGPS();
            }

            if (BDInputUtils.GetKeyDown(BDInputSettingsFields.TGP_COM))
            {
                CoMLock = !CoMLock;
                if (!CoMLock)
                {
                    lockedVessel = null;
                    lockedPart = null;
                    guidingOrdnance = false;
                }
            }

            if (BDInputUtils.GetKeyDown(BDInputSettingsFields.TGP_RADAR))
            {
                radarLock = !radarLock;
            }

            if (BDInputUtils.GetKeyDown(BDInputSettingsFields.TGP_TURRETS))
            {
                if (slaveTurrets)
                {
                    UnslaveTurrets();
                }
                else
                {
                    SlaveTurrets();
                }
            }

            if (BDInputUtils.GetKeyDown(BDInputSettingsFields.TGP_TO_GPS))
            {
                PointToGPSTarget();
            }

            if (BDInputUtils.GetKeyDown(BDInputSettingsFields.TGP_RESET))
            {
                ResetCameraButton();
            }
        }

        void ToggleNV()
        {
            nvMode = !nvMode;
            TargetingCamera.Instance.nvMode = nvMode;
        }

        void SetActiveCam(bool nextCam)
        {
            camIndex += nextCam ? -1 : 1;
            int totalCams = VesselModuleRegistry.GetModules<ModuleTargetingCamera>(vessel).Count;
            if (camIndex < 0 && totalCams > 0) camIndex = totalCams - 1;
            if (camIndex >= totalCams && totalCams > 0) camIndex -= totalCams * Mathf.FloorToInt((camIndex / totalCams));
            activeCam = VesselModuleRegistry.GetModules<ModuleTargetingCamera>(vessel)[camIndex];
        }

        void UpdateControls()
        {
            UpdateKeyInputs();
            UpdateSlewRate();
            if (slewInput != Vector2.zero)
            {
                SlewCamera(slewInput);
            }
            slewInput = Vector2.zero;
        }

        void UpdateSlewRate()
        {
            if (SlewingButtonCam)
            {
                finalSlewSpeed = Mathf.Clamp(finalSlewSpeed + (0.5f * (fov / traverseRate)), 0, 80 * fov / traverseRate);
                SlewingButtonCam = false;
            }
            else
            {
                finalSlewSpeed = 15 * fov / 60;
            }
        }

        void UpdateRadarLock()
        {
            // If CoMLock disable radar lock
            if (CoMLock && lockedVessel)
            {
                radarLock = false;
                return;
            }

            var weaponManager = WeaponManager;
            if (weaponManager && weaponManager.vesselRadarData && weaponManager.vesselRadarData.locked)
            {
                RadarDisplayData tgt = weaponManager.vesselRadarData.lockedTargetData;
                TargetSignatureData tgtData = tgt.targetData;
                ModuleRadar detectedRadar = tgtData.lockedByRadar;
                Vector3 radarTargetPos = tgtData.predictedPositionWithChaffFactor(detectedRadar.radarChaffClutterFactor, detectedRadar._radarChaffNotchVFac, detectedRadar._radarChaffNotchRFac);
                Vector3 targetDirection = radarTargetPos - cameraParentTransform.position;

                //Quaternion lookRotation = Quaternion.LookRotation(radarTargetPos-cameraParentTransform.position, VectorUtils.GetUpDirection(cameraParentTransform.position));
                if (VectorUtils.Angle(radarTargetPos - cameraParentTransform.position, cameraParentTransform.forward) < 0.5f)
                {
                    //cameraParentTransform.rotation = lookRotation;
                    if (tgt.vessel)
                    {
                        targetDirection = ((tgt.vessel.CoM) - cameraParentTransform.transform.position);
                    }
                    PointCameraModel(targetDirection);
                    GroundStabilize();
                }
                else
                {
                    if (groundStabilized)
                    {
                        ClearTarget();
                    }
                    //lookRotation = Quaternion.RotateTowards(cameraParentTransform.rotation, lookRotation, 120*Time.fixedDeltaTime);
                    Vector3 rotateTwdDirection = Vector3.RotateTowards(cameraParentTransform.forward, targetDirection, traverseRate * Time.fixedDeltaTime * Mathf.Deg2Rad, 0);
                    PointCameraModel(rotateTwdDirection);
                }
            }
            else
            {
                //radarLock = false;
            }
        }

        void OnGUI()
        {
            if (Event.current.type == EventType.MouseUp)
            {
                if (ResizingWindow) ResizingWindow = false;
                if (SlewingMouseCam) SlewingMouseCam = false;
            }

            if (HighLogic.LoadedSceneIsFlight && !MapView.MapIsEnabled && BDArmorySetup.GAME_UI_ENABLED && !delayedEnabling)
            {
                if (cameraEnabled && vessel.isActiveVessel && FlightGlobals.ready)
                {
                    //window
                    if (activeCam == this && TargetingCamera.ReadyForUse)
                    {
                        if (BDArmorySettings.UI_SCALE_ACTUAL != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, BDArmorySetup.WindowRectTargetingCam.position);
                        BDArmorySetup.WindowRectTargetingCam = GUI.Window(125452, BDArmorySetup.WindowRectTargetingCam, WindowTargetCam, "Target Camera", GUI.skin.window);
                        GUIUtils.UseMouseEventInRect(BDArmorySetup.WindowRectTargetingCam);
                    }

                    //locked target icon
                    if (groundStabilized)
                    {
                        GUIUtils.DrawTextureOnWorldPos(groundTargetPosition, BDArmorySetup.Instance.greenPointCircleTexture, new Vector3(20, 20), 0);
                    }
                    else
                    {
                        GUIUtils.DrawTextureOnWorldPos(targetPointPosition, BDArmorySetup.Instance.greenCircleTexture, new Vector3(18, 18), 0);
                    }
                }

                if (BDArmorySettings.DEBUG_RADAR)
                {
                    GUI.Label(new Rect(600, 1000, 200, 30), $"Slew rate: {finalSlewSpeed:G3}");
                    GUI.Label(new Rect(600, 975, 200, 30), $"Locked part: {(lockedPart != null ? lockedPart.partInfo.title : "null")}");
                    GUI.Label(new Rect(600, 950, 200, 30), $"ComLock: {(CoMLock ? lockedVessel != null ? lockedVessel.GetName() : "null" : "false")}");
                }

                if (BDArmorySettings.DEBUG_LINES && cameraEnabled && cameraParentTransform is not null)
                {
                    if (groundStabilized)
                    {
                        GUIUtils.DrawLineBetweenWorldPositions(cameraParentTransform.position, groundTargetPosition, 2, Color.red);
                    }
                    else
                    {
                        GUIUtils.DrawLineBetweenWorldPositions(cameraParentTransform.position, targetPointPosition, 2, Color.white);
                    }
                }
            }
        }

        void WindowTargetCam(int windowID)
        {
            float windowScale = BDArmorySettings.TARGET_WINDOW_SCALE;
            adjCamImageSize = camImageSize * windowScale;
            if (!TargetingCamera.Instance)
            {
                return;
            }

            windowIsOpen = true;
            var guiMatrix = GUI.matrix;

            if (GUI.Button(new Rect(BDArmorySetup.WindowRectTargetingCam.width - 18, 2, 16, 16), " X", BDArmorySetup.CloseButtonStyleDark))
            {
                DisableCamera();
                return;
            }

            Rect imageRect = new Rect(2, 20, adjCamImageSize, adjCamImageSize);
            GUI.DrawTexture(imageRect, TargetingCamera.Instance.targetCamRenderTexture, ScaleMode.StretchToFill, false);
            GUI.DrawTexture(imageRect, TargetingCamera.Instance.ReticleTexture, ScaleMode.StretchToFill, true);

            // slew buttons
            DrawSlewButtons();

            // zoom buttons
            DrawZoomButtons();

            // Cam select buttons
            DrawCamSelectButtons();

            // Right side control buttons
            DrawSideControlButtons(imageRect);

            // Check for mousedown / mousescroll in target Cam and handle slew and zoom.
            if (Event.current.type == EventType.MouseDown && imageRect.Contains(Event.current.mousePosition))
            {
                if (!SlewingMouseCam) SlewingMouseCam = true;
            }
            if (Event.current.type == EventType.Repaint && SlewingMouseCam)
            {
                if (Mouse.delta.x != 0 || Mouse.delta.y != 0)
                {
                    SlewRoutine(Mouse.delta);
                }
            }

            if (Event.current.type == EventType.ScrollWheel && imageRect.Contains(Event.current.mousePosition))
            {
                ZoomRoutine(Input.mouseScrollDelta);
            }

            float indicatorSize = Mathf.Clamp(64 * (adjCamImageSize / camImageSize), 48, 128);
            float indicatorBorder = imageRect.width * 0.056f;
            Vector3 vesForward = vessel.ReferenceTransform.up;
            Vector3 upDirection = (transform.position - FlightGlobals.currentMainBody.transform.position).normalized;

            //horizon indicator
            float horizY = imageRect.y + imageRect.height - indicatorSize - indicatorBorder;
            Vector3 hForward = vesForward.ProjectOnPlanePreNormalized(upDirection);
            // Unfortunately, because `vesForward` and `upDirection` are not perpendicular to
            // each other the more efficient `GetAngleOnPlane` cannot be used!
            float hAngle = -VectorUtils.SignedAngle(hForward, vesForward, upDirection);
            horizY -= (hAngle / 90) * (indicatorSize / 2);
            Rect horizonRect = new Rect(indicatorBorder + imageRect.x, horizY, indicatorSize, indicatorSize);
            GUI.DrawTexture(horizonRect, BDArmorySetup.Instance.horizonIndicatorTexture, ScaleMode.StretchToFill, true);

            //roll indicator
            Rect rollRect = new Rect(indicatorBorder + imageRect.x, imageRect.y + imageRect.height - indicatorSize - indicatorBorder, indicatorSize, indicatorSize);
            GUI.DrawTexture(rollRect, rollReferenceTexture, ScaleMode.StretchToFill, true);
            Vector3 localUp = vessel.ReferenceTransform.InverseTransformDirection(upDirection);
            localUp = localUp.ProjectOnPlanePreNormalized(Vector3.up).normalized;
            float rollAngle = -VectorUtils.GetAngleOnPlane(localUp, -Vector3.forward, Vector3.right);
            GUIUtility.RotateAroundPivot(rollAngle, guiMatrix * rollRect.center);
            GUI.DrawTexture(rollRect, rollIndicatorTexture, ScaleMode.StretchToFill, true);
            GUI.matrix = guiMatrix;

            //target direction indicator
            float angleToTarget = VectorUtils.GetAngleOnPlane((targetPointPosition - transform.position), hForward, Vector3.Cross(upDirection, hForward));
            GUIUtility.RotateAroundPivot(angleToTarget, guiMatrix * rollRect.center);
            GUI.DrawTexture(rollRect, BDArmorySetup.Instance.targetDirectionTexture, ScaleMode.StretchToFill, true);
            GUI.matrix = guiMatrix;

            //resizing
            Rect resizeRect =
                new Rect(BDArmorySetup.WindowRectTargetingCam.width - 18, BDArmorySetup.WindowRectTargetingCam.height - 18, 16, 16);
            GUI.DrawTexture(resizeRect, GUIUtils.resizeTexture, ScaleMode.StretchToFill, true);
            if (Event.current.type == EventType.MouseDown && resizeRect.Contains(Event.current.mousePosition))
            {
                ResizingWindow = true;
            }
            else if (!SlewingMouseCam) GUIUtils.DragWindow();

            if (Event.current.type == EventType.Repaint && ResizingWindow)
            {
                if (Mouse.delta.x != 0 || Mouse.delta.y != 0)
                {
                    float diff = (Mathf.Abs(Mouse.delta.x) > Mathf.Abs(Mouse.delta.y) ? Mouse.delta.x : Mouse.delta.y) / BDArmorySettings.UI_SCALE_ACTUAL;
                    BDArmorySettings.TARGET_WINDOW_SCALE = Mathf.Clamp(BDArmorySettings.TARGET_WINDOW_SCALE + diff / camImageSize, BDArmorySettings.TARGET_WINDOW_SCALE_MIN, BDArmorySettings.TARGET_WINDOW_SCALE_MAX);
                    ResizeTargetWindow();
                }
            }
            GUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectTargetingCam);
        }

        private void DrawSlewButtons()
        {
            //slew buttons
            float slewStartX = adjCamImageSize * 0.06f;
            float slewStartY = 20 + (adjCamImageSize * 0.06f);

            Rect slewLeftRect = new Rect(slewStartX, slewStartY + ((buttonHeight + gap) / 2), buttonHeight, buttonHeight);
            Rect slewUpRect = new Rect(slewStartX + buttonHeight + gap, slewStartY, buttonHeight, buttonHeight);
            Rect slewDownRect = new Rect(slewStartX + buttonHeight + gap, slewStartY + buttonHeight + gap, buttonHeight, buttonHeight);
            Rect slewRightRect = new Rect(slewStartX + (2 * buttonHeight) + (gap * 2), slewStartY + ((buttonHeight + gap) / 2), buttonHeight, buttonHeight);
            if (GUI.RepeatButton(slewUpRect, "^", GUI.skin.button))
            {
                SlewCamera(Vector3.up); // OnGUI occurs after LateUpdate, so we can't set slewInput and expect it to work for the current frame.
                // slewInput.y = 1;
            }

            if (GUI.RepeatButton(slewDownRect, "v", GUI.skin.button))
            {
                SlewCamera(Vector3.down);
                // slewInput.y = -1;
            }

            if (GUI.RepeatButton(slewLeftRect, "<", GUI.skin.button))
            {
                SlewCamera(Vector3.left);
                // slewInput.x = -1;
            }

            if (GUI.RepeatButton(slewRightRect, ">", GUI.skin.button))
            {
                SlewCamera(Vector3.right);
                // slewInput.x = 1;
            }
        }

        private void DrawZoomButtons()
        {
            float zoomGap = 9f * gap;
            if (zoomTimes[currentFovIndex] >= 100f)
                zoomGap += 10f * gap;
            else if (zoomTimes[currentFovIndex] >= 10f)
                zoomGap += 5f * gap;

            float zoomStartX = adjCamImageSize * 0.94f - (buttonHeight * 3) - zoomGap;
            float zoomStartY = 20 + (adjCamImageSize * 0.06f);
            Rect zoomOutRect = new Rect(zoomStartX, zoomStartY, buttonHeight, buttonHeight);
            Rect zoomInfoRect = new Rect(zoomStartX + buttonHeight + gap, zoomStartY, buttonHeight + zoomGap, buttonHeight);
            Rect zoomInRect = new Rect(zoomStartX + buttonHeight * 2f + (zoomGap + 2f * gap), zoomStartY, buttonHeight, buttonHeight);

            GUI.enabled = currentFovIndex > 0;
            if (GUI.Button(zoomOutRect, "-", GUI.skin.button))
            {
                ZoomOut();
            }

            GUIStyle zoomBox = GUI.skin.box;
            zoomBox.alignment = TextAnchor.UpperCenter;
            zoomBox.padding.top = 0;
            GUI.enabled = true;
            GUI.Label(zoomInfoRect, $"{zoomTimes[currentFovIndex]:F1} X", zoomBox);

            GUI.enabled = currentFovIndex < zoomFovs.Length - 1;
            if (GUI.Button(zoomInRect, "+", GUI.skin.button))
            {
                ZoomIn();
            }
            GUI.enabled = true;
        }

        private void DrawCamSelectButtons()
        {
            float camGap = 9f * gap;

            float camStartX = adjCamImageSize * 0.94f - (buttonHeight * 3) - camGap;
            float camStartY = 40 + (adjCamImageSize * 0.06f);
            Rect camOutRect = new Rect(camStartX, camStartY, buttonHeight, buttonHeight);
            Rect camInfoRect = new Rect(camStartX + buttonHeight + gap, camStartY, buttonHeight + camGap, buttonHeight);
            Rect camInRect = new Rect(camStartX + buttonHeight * 2f + (camGap + 2f * gap), camStartY, buttonHeight, buttonHeight);

            if (GUI.Button(camOutRect, "<", GUI.skin.button))
            {
                SetActiveCam(true);
            }

            GUIStyle camBox = GUI.skin.box;
            camBox.alignment = TextAnchor.UpperCenter;
            camBox.padding.top = 0;
            GUI.enabled = true;
            GUI.Label(camInfoRect, $"# {camIndex}", camBox);

            if (GUI.Button(camInRect, ">", GUI.skin.button))
            {
                SetActiveCam(false);
            }
            GUI.enabled = true;
        }

        private void DrawSideControlButtons(Rect imageRect)
        {
            GUIStyle dataStyle = new()
            {
                alignment = TextAnchor.MiddleCenter,
                normal = new GUIStyleState { textColor = Color.white }
            };
            GUIStyle buttonStyle = new(BDArmorySetup.ButtonStyle)
            {
                fontSize = 11
            };

            int line = 0;
            float lineHeight = buttonHeight + gap;
            float buttonWidth = 3 * buttonHeight + 4 * gap;
            //groundStablize button
            float startX = imageRect.width + 3 * gap;
            Rect stabilizeRect = new Rect(startX, controlsStartY, buttonWidth, buttonHeight + lineHeight);
            if (!groundStabilized)
            {
                if (GUI.Button(stabilizeRect, "Lock\nTarget", buttonStyle))
                {
                    GroundStabilize();
                }
                ++line; //stabilizerect is two lines tall, so account for that for later incrementing of linecount
            }
            else
            {
                if (GUI.Button(new Rect(startX, controlsStartY, buttonWidth, buttonHeight),
                    "Unlock", buttonStyle))
                {
                    ClearTarget();
                }

                var weaponManager = WeaponManager;
                if (weaponManager)
                {
                    Rect sendGPSRect = new Rect(startX, controlsStartY + ++line * lineHeight, buttonWidth, buttonHeight);
                    if (GUI.Button(sendGPSRect, "Send GPS", buttonStyle))
                    {
                        SendGPS();
                    }
                }

                if (!gimbalLimitReached)
                {
                    //open square
                    float oSqrSize = (24f / 512f) * adjCamImageSize;
                    Rect oSqrRect = new Rect(imageRect.x + (adjCamImageSize / 2) - (oSqrSize / 2),
                        imageRect.y + (adjCamImageSize / 2) - (oSqrSize / 2), oSqrSize, oSqrSize);
                    GUI.DrawTexture(oSqrRect, BDArmorySetup.Instance.openWhiteSquareTexture, ScaleMode.StretchToFill, true);
                }

                //geo data
                dataStyle.fontSize = (int)Mathf.Clamp(12 * BDArmorySettings.TARGET_WINDOW_SCALE, 8, 12);
                Rect geoRect = new Rect(imageRect.x, (adjCamImageSize * 0.94f), adjCamImageSize, 14);
                string geoLabel = BodyUtils.FormattedGeoPos(bodyRelativeGTP, false);
                GUI.Label(geoRect, geoLabel, dataStyle);

                //target data
                dataStyle.fontSize = (int)Mathf.Clamp(16 * BDArmorySettings.TARGET_WINDOW_SCALE, 9, 16);
                //float dataStartX = stabilStartX + stabilizeRect.width + 8;
                Rect targetRangeRect = new Rect(imageRect.x, (adjCamImageSize * 0.94f) - (int)Mathf.Clamp(18 * BDArmorySettings.TARGET_WINDOW_SCALE, 9, 18), adjCamImageSize, (int)Mathf.Clamp(18 * BDArmorySettings.TARGET_WINDOW_SCALE, 10, 18));
                float targetRange = Vector3.Distance(groundTargetPosition, transform.position);
                string rangeString = "Range: " + targetRange.ToString("0.0") + "m";
                GUI.Label(targetRangeRect, rangeString, dataStyle);

                //laser ranging indicator
                dataStyle.fontSize = (int)Mathf.Clamp(18 * BDArmorySettings.TARGET_WINDOW_SCALE, 9, 18);
                string lrLabel = surfaceDetected ? "LR" : "NO LR";
                Rect lrRect = new Rect(imageRect.x, imageRect.y + (adjCamImageSize * 0.65f), adjCamImageSize, 20);
                GUI.Label(lrRect, lrLabel, dataStyle);

                //azimuth and elevation indicator //UNFINISHED
                /*
                Vector2 azielPos = TargetAzimuthElevationScreenPos(imageRect, groundTargetPosition, 4);
                Rect azielRect = new Rect(azielPos.x, azielPos.y, 4, 4);
                GUI.DrawTexture(azielRect, BDArmorySetup.Instance.whiteSquareTexture, ScaleMode.StretchToFill, true);
                */

                //DLZ
                if (weaponManager && weaponManager.selectedWeapon != null)
                {
                    if (weaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.Missile)
                    {
                        MissileBase currMissile = weaponManager.CurrentMissile;
                        if (currMissile &&
                            (currMissile.TargetingMode == MissileBase.TargetingModes.Gps ||
                            currMissile.TargetingMode == MissileBase.TargetingModes.Laser))
                        {
                            MissileLaunchParams dlz =
                                MissileLaunchParams.GetDynamicLaunchParams(currMissile, Vector3.zero, groundTargetPosition);
                            float dlzWidth = 12 * (imageRect.width / 360);
                            float lineWidth = 2;
                            Rect dlzRect = new Rect(imageRect.x + imageRect.width - (3 * dlzWidth) - lineWidth,
                                imageRect.y + (imageRect.height / 4), dlzWidth, imageRect.height / 2);
                            float scaleDistance =
                                Mathf.Max(Mathf.Max(8000f, currMissile.maxStaticLaunchRange * 2), targetRange);
                            float rangeToPixels = (1f / scaleDistance) * dlzRect.height;

                            GUI.BeginGroup(dlzRect);

                            float dlzX = 0;

                            GUIUtils.DrawRectangle(new Rect(0, 0, dlzWidth, dlzRect.height), Color.black);

                            Rect maxRangeVertLineRect = new Rect(dlzRect.width - lineWidth,
                                Mathf.Clamp(dlzRect.height - (dlz.maxLaunchRange * rangeToPixels), 0, dlzRect.height),
                                lineWidth, Mathf.Clamp(dlz.maxLaunchRange * rangeToPixels, 0, dlzRect.height));
                            GUIUtils.DrawRectangle(maxRangeVertLineRect, Color.white);

                            Rect maxRangeTickRect = new Rect(dlzX, maxRangeVertLineRect.y, dlzWidth, lineWidth);
                            GUIUtils.DrawRectangle(maxRangeTickRect, Color.white);

                            Rect minRangeTickRect = new Rect(dlzX,
                                Mathf.Clamp(dlzRect.height - (dlz.minLaunchRange * rangeToPixels), 0, dlzRect.height), dlzWidth,
                                lineWidth);
                            GUIUtils.DrawRectangle(minRangeTickRect, Color.white);

                            Rect rTrTickRect = new Rect(dlzX,
                                Mathf.Clamp(dlzRect.height - (dlz.rangeTr * rangeToPixels), 0, dlzRect.height), dlzWidth,
                                lineWidth);
                            GUIUtils.DrawRectangle(rTrTickRect, Color.white);

                            Rect noEscapeLineRect =
                                new Rect(dlzX, rTrTickRect.y, lineWidth, minRangeTickRect.y - rTrTickRect.y);
                            GUIUtils.DrawRectangle(noEscapeLineRect, Color.white);

                            GUI.EndGroup();

                            float targetDistIconSize = 6;
                            float targetDistY = dlzRect.y + dlzRect.height - (targetRange * rangeToPixels);
                            Rect targetDistanceRect = new Rect(dlzRect.x - (targetDistIconSize / 2), targetDistY,
                                (targetDistIconSize / 2) + dlzRect.width, targetDistIconSize);
                            GUIUtils.DrawRectangle(targetDistanceRect, Color.white);
                        }
                    }
                }
            }

            //gimbal limit
            dataStyle.fontSize = (int)Mathf.Clamp(24 * BDArmorySettings.TARGET_WINDOW_SCALE, 12, 24);
            if (gimbalLimitReached)
            {
                Rect gLimRect = new Rect(imageRect.x, imageRect.y + (adjCamImageSize * 0.15f), adjCamImageSize, 28);
                GUI.Label(gLimRect, "GIMBAL LIMIT", dataStyle);
            }

            //reset button
            Rect resetRect = new Rect(startX, controlsStartY + ++line * lineHeight, buttonWidth, buttonHeight);
            if (GUI.Button(resetRect, "Reset", buttonStyle))
            {
                ResetCameraButton();
            }

            //CoM lock
            Rect comLockRect = new(startX, controlsStartY + ++line * lineHeight, buttonWidth, buttonHeight);
            GUIStyle comStyle = new(CoMLock ? BDArmorySetup.SelectedButtonStyle : buttonStyle)
            {
                fontSize = 10,
                wordWrap = false
            };
            if (GUI.Button(comLockRect, "CoM Track", comStyle))
            {
                CoMLock = !CoMLock;
            }

            //radar slave
            Rect radarSlaveRect = new Rect(startX, controlsStartY + ++line * lineHeight, buttonWidth, buttonHeight);
            GUIStyle radarSlaveStyle = radarLock ? BDArmorySetup.SelectedButtonStyle : buttonStyle;
            if (GUI.Button(radarSlaveRect, "Radar", radarSlaveStyle))
            {
                radarLock = !radarLock;
            }

            //slave turrets button
            Rect slaveRect = new Rect(startX, controlsStartY + ++line * lineHeight, buttonWidth, buttonHeight);
            if (!slaveTurrets)
            {
                if (GUI.Button(slaveRect, "Turrets", buttonStyle))
                {
                    SlaveTurrets();
                }
            }
            else
            {
                if (GUI.Button(slaveRect, "Turrets", BDArmorySetup.SelectedButtonStyle))
                {
                    UnslaveTurrets();
                }
            }

            //point to gps button
            Rect toGpsRect = new Rect(startX, controlsStartY + ++line * lineHeight, buttonWidth, buttonHeight);
            if (GUI.Button(toGpsRect, "To GPS", buttonStyle))
            {
                PointToGPSTarget();
            }

            //nv button
            float nvStartX = startX;
            Rect nvRect = new Rect(nvStartX, controlsStartY + ++line * lineHeight, buttonWidth, buttonHeight);
            string nvLabel = nvMode ? "NV Off" : "NV On";
            GUIStyle nvStyle = nvMode ? BDArmorySetup.SelectedButtonStyle : buttonStyle;
            if (GUI.Button(nvRect, nvLabel, nvStyle))
            {
                ToggleNV();
            }

            if (BDArmorySettings.DEBUG_RADAR) // Debug what the various cameras show in the targeting window.
            {
                ++line;
                if (GUI.Button(new Rect(nvStartX, controlsStartY + ++line * lineHeight, buttonWidth, buttonHeight), $"Near", TargetingCamera.Instance.CamEnabled[0] ? BDArmorySetup.SelectedButtonStyle : buttonStyle))
                    TargetingCamera.Instance.CamEnabled[0] = !TargetingCamera.Instance.CamEnabled[0];
                if (GUI.Button(new Rect(nvStartX, controlsStartY + ++line * lineHeight, buttonWidth, buttonHeight), $"Far", TargetingCamera.Instance.CamEnabled[1] ? BDArmorySetup.SelectedButtonStyle : buttonStyle))
                    TargetingCamera.Instance.CamEnabled[1] = !TargetingCamera.Instance.CamEnabled[1];
                if (GUI.Button(new Rect(nvStartX, controlsStartY + ++line * lineHeight, buttonWidth, buttonHeight), $"Sky", TargetingCamera.Instance.CamEnabled[2] ? BDArmorySetup.SelectedButtonStyle : buttonStyle))
                    TargetingCamera.Instance.CamEnabled[2] = !TargetingCamera.Instance.CamEnabled[2];
                if (GUI.Button(new Rect(nvStartX, controlsStartY + ++line * lineHeight, buttonWidth, buttonHeight), $"Galaxy", TargetingCamera.Instance.CamEnabled[3] ? BDArmorySetup.SelectedButtonStyle : buttonStyle))
                    TargetingCamera.Instance.CamEnabled[3] = !TargetingCamera.Instance.CamEnabled[3];
            }
        }

        void ResetCameraButton()
        {
            if (!resetting)
            {
                resetCamera = StartCoroutine(ResetCamera());
            }
        }

        void SendGPS()
        {
            var weaponManager = WeaponManager;
            if (groundStabilized && weaponManager)
            {
                BDATargetManager.GPSTargetList(weaponManager.Team).Add(new GPSTargetInfo(bodyRelativeGTP, "Target"));
                BDATargetManager.Instance.SaveGPSTargets();
            }
        }

        void SlaveTurrets()
        {
            var weaponManager = WeaponManager;
            using (var mtc = VesselModuleRegistry.GetModules<ModuleTargetingCamera>(vessel).GetEnumerator())
                while (mtc.MoveNext())
                {
                    mtc.Current.slaveTurrets = false;
                }

            if (weaponManager && weaponManager.vesselRadarData)
            {
                weaponManager.vesselRadarData.slaveTurrets = false;
            }

            slaveTurrets = true;
        }

        void UnslaveTurrets()
        {
            var weaponManager = WeaponManager;
            using (var mtc = VesselModuleRegistry.GetModules<ModuleTargetingCamera>(vessel).GetEnumerator())
                while (mtc.MoveNext())
                {
                    mtc.Current.slaveTurrets = false;
                }

            if (weaponManager && weaponManager.vesselRadarData)
            {
                weaponManager.vesselRadarData.slaveTurrets = false;
            }

            if (weaponManager)
            {
                weaponManager.slavingTurrets = false;
                weaponManager.slavedPosition = Vector3.zero;
                weaponManager.slavedTarget = TargetSignatureData.noTarget; //reset and null these so hitting the slave target button on a weapon later doesn't lock it to a legacy position/target
            }
        }

        void UpdateSlaveData()
        {
            if (!slaveTurrets) return;
            var weaponManager = WeaponManager;
            if (!weaponManager) return;
            if (weaponManager.slavingTurrets) return; //turrets already slaved to active radar lock
            weaponManager.slavedPosition = groundStabilized ? groundTargetPosition : targetPointPosition;
            weaponManager.slavedVelocity = Vector3.zero;
            weaponManager.slavedAcceleration = Vector3.zero;
        }

        internal static void ResizeTargetWindow()
        {
            windowWidth = camImageSize * BDArmorySettings.TARGET_WINDOW_SCALE + (3 * buttonHeight) + 16 + 2 * gap;
            windowHeight = camImageSize * BDArmorySettings.TARGET_WINDOW_SCALE + 23;
            BDArmorySetup.WindowRectTargetingCam = new Rect(BDArmorySetup.WindowRectTargetingCam.x, BDArmorySetup.WindowRectTargetingCam.y, windowWidth, windowHeight);
        }

        void SlewCamera(Vector3 direction)
        {
            SlewingButtonCam = true;
            StartCoroutine(SlewCamRoutine(direction));
        }

        IEnumerator SlewMouseCamRoutine(Vector3 direction)
        {
            radarLock = false;
            lockedVessel = null;
            lockedPart = null;
            if (!BDArmorySettings.TARGET_WINDOW_INVERT_MOUSE_X) direction.x = -direction.x; // Invert the x-axis by default (original defaults).
            if (BDArmorySettings.TARGET_WINDOW_INVERT_MOUSE_Y) direction.y = -direction.y;
            Vector3 rotationAxis = Matrix4x4.TRS(Vector3.zero, Quaternion.LookRotation(cameraParentTransform.forward, vessel.upAxis), Vector3.one)
                .MultiplyVector(Quaternion.AngleAxis(90, Vector3.forward) * direction);
            float velocity = Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.y)) + 0.1f * direction.sqrMagnitude;
            float angle = velocity / (1 + currentFovIndex) * Time.deltaTime;
            if (angle / (1f + currentFovIndex) < .01f / (1f + currentFovIndex)) angle = .01f / ((1f + currentFovIndex) / 2f);
            Vector3 lookVector = Quaternion.AngleAxis(angle, rotationAxis) * cameraParentTransform.forward;

            PointCameraModel(lookVector);
            yield return new WaitForEndOfFrame();

            if (groundStabilized)
            {
                GroundStabilize();
                lookVector = groundTargetPosition - cameraParentTransform.position;
            }
            PointCameraModel(lookVector);
        }

        IEnumerator SlewCamRoutine(Vector3 direction)
        {
            StopResetting();
            StopPointToPosRoutine();
            lockedVessel = null;
            radarLock = false;
            lockedPart = null;
            float slewRate = finalSlewSpeed;
            Vector3 rotationAxis = Matrix4x4.TRS(Vector3.zero, Quaternion.LookRotation(cameraParentTransform.forward, vessel.upAxis), Vector3.one).MultiplyVector(Quaternion.AngleAxis(90, Vector3.forward) * direction);
            Vector3 lookVector = Quaternion.AngleAxis(slewRate * Time.deltaTime, rotationAxis) * cameraParentTransform.forward;
            PointCameraModel(lookVector);
            yield return new WaitForEndOfFrame();

            if (groundStabilized)
            {
                GroundStabilize();
                lookVector = groundTargetPosition - cameraParentTransform.position;
            }

            PointCameraModel(lookVector);
        }

        void PointToGPSTarget()
        {
            var weaponManager = WeaponManager;
            if (weaponManager && weaponManager.designatedGPSCoords != Vector3d.zero)
            {
                StartCoroutine(PointToPositionRoutine(VectorUtils.GetWorldSurfacePostion(weaponManager.designatedGPSCoords, vessel.mainBody), -1));
            }
        }

        private void SlewRoutine(Vector2 direction)
        {
            if (SlewingMouseCam)
            {
                StartCoroutine(SlewMouseCamRoutine(direction));
            }
        }

        void ZoomRoutine(Vector2 zoomAmt)
        {
            if (zoomAmt.y > 0) ZoomIn();
            else ZoomOut();
            Mouse.delta = new Vector2(0, 0);
        }

        void ZoomIn()
        {
            StopResetting();
            if (currentFovIndex < zoomFovs.Length - 1)
            {
                currentFovIndex++;
            }

            //fov = zoomFovs[currentFovIndex];
        }

        void ZoomOut()
        {
            StopResetting();
            if (currentFovIndex > 0)
            {
                currentFovIndex--;
            }

            //fov = zoomFovs[currentFovIndex];
        }

        public void GroundStabilize()
        {
            if (vessel.packed) return;
            StopResetting();

            RaycastHit rayHit;
            Ray ray = new Ray(cameraParentTransform.position, cameraParentTransform.forward);
            bool raycasted = Physics.Raycast(ray, out rayHit, maxRayDistance, (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.EVA | LayerMasks.Unknown19 | LayerMasks.Unknown23 | LayerMasks.Wheels));

            // If we didn't hit anything and there isn't an ocean
            if (!raycasted && !vessel.mainBody.ocean) return;
            Vector3d newGTP;

            // If we didn't hit anything and there's an ocean, or we hit something underwater
            if (!raycasted || (vessel.mainBody.ocean && FlightGlobals.getAltitudeAtPos(rayHit.point) < 0))
            {
                Vector3 upDir = VectorUtils.GetUpDirection(cameraParentTransform.position);
                double altitude = vessel.altitude; //MissileGuidance.GetRadarAltitude(vessel);
                double radius = vessel.mainBody.Radius;

                Vector3d planetCenter = vessel.GetWorldPos3D() - ((vessel.altitude + vessel.mainBody.Radius) * vessel.upAxis);
                if (VectorUtils.SphereRayIntersect(ray, planetCenter, radius, out double enter))
                {
                    if (enter > 0)
                    {
                        groundStabilized = true;
                        groundTargetPosition = ray.GetPoint((float)enter);
                        newGTP = VectorUtils.WorldPositionToGeoCoords(groundTargetPosition, vessel.mainBody);
                        if (newGTP != Vector3d.zero)
                        {
                            bodyRelativeGTP = newGTP;
                        }
                    }
                }

                return;
            }

            Part p;
            if ((p = rayHit.collider.GetComponentInParent<Part>()) && p.vessel == vessel) return;

            KerbalEVA hitEVA = rayHit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
            if (hitEVA)
            {
                p = hitEVA.part;
            }

            bool pCheck = false;

            if (p && p.vessel)
            {
                TargetInfo pInfo;
                if (p.vessel != lockedVessel && (pInfo = p.vessel.gameObject.GetComponent<TargetInfo>()) != null && pInfo.isMissile && pInfo.MissileBaseModule.FiredByWM == WeaponManager)
                {
                    return;
                }
                pCheck = true;
            }

            groundStabilized = true;
            groundTargetPosition = rayHit.point;

            if (CoMLock)
            {
                if (pCheck)
                {
                    if (lockedPart != null && lockedPart.vessel == p.vessel)
                    {
                        groundTargetPosition = lockedPart.transform.position; // + (p.vessel.Velocity() * Time.fixedDeltaTime);
                    }
                    else
                    {
                        groundTargetPosition = p.vessel.CoM; // + (p.vessel.Velocity() * Time.fixedDeltaTime);
                    }
                    //StartCoroutine(StabilizeNextFrame());
                    lockedVessel = p.vessel;
                    //StartCoroutine(PointToPositionRoutine(p.vessel.CoM, p.vessel, false));
                }
                else
                {
                    lockedVessel = null;
                }
            }
            newGTP = VectorUtils.WorldPositionToGeoCoords(groundTargetPosition, vessel.mainBody);
            if (newGTP != Vector3d.zero)
            {
                bodyRelativeGTP = newGTP;
            }
        }

        IEnumerator StabilizeNextFrame()
        {
            yield return new WaitForFixedUpdate();
            yield return new WaitForEndOfFrame();
            if (!gimbalLimitReached && surfaceDetected)
            {
                GroundStabilize();
            }
        }

        void GetHitPoint()
        {
            if (vessel.packed) return;
            if (delayedEnabling) return;

            RaycastHit rayHit;
            Ray ray = new Ray(cameraParentTransform.position, cameraParentTransform.forward);
            if (!Physics.Raycast(ray, out rayHit, maxRayDistance, (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.EVA | LayerMasks.Unknown19 | LayerMasks.Unknown23 | LayerMasks.Wheels)))
            {
                targetPointPosition = cameraParentTransform.position + (maxRayDistance * cameraParentTransform.forward);
                surfaceDetected = false;
                // If we hit nothing but air, then only stay locked if we're at the gimbal limit
                // or we're in the process of slewing to the target
                groundStabilized &= (gimbalLimitReached || slewingToPosition);
                return;
            }

            Part p = rayHit.collider.GetComponentInParent<Part>();
            TargetInfo pInfo;
            if (p && (p.vessel == vessel || (p.vessel != lockedVessel && p.vessel && (pInfo = p.vessel.gameObject.GetComponent<TargetInfo>()) != null && pInfo.isMissile && pInfo.MissileBaseModule.FiredByWM == WeaponManager)))
            {
                targetPointPosition = cameraParentTransform.position + (maxRayDistance * cameraParentTransform.forward);
                surfaceDetected = false;
                return;
            }

            targetPointPosition = rayHit.point;
            if (groundStabilized && !gimbalLimitReached)
            {
                groundTargetPosition = rayHit.point;

                if (CoMLock)
                {
                    KerbalEVA hitEVA = rayHit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                    if (hitEVA)
                    {
                        p = hitEVA.part;
                    }
                    if (p && p.vessel)
                    {
                        if (lockedPart && lockedPart.vessel == p.vessel) groundTargetPosition = lockedPart.transform.position;
                        else groundTargetPosition = p.vessel.CoM;
                        lockedVessel = p.vessel;
                    }
                    else
                    {
                        lockedVessel = null;
                    }
                }
                Vector3d newGTP = VectorUtils.WorldPositionToGeoCoords(groundTargetPosition, vessel.mainBody);
                if (newGTP != Vector3d.zero)
                {
                    bodyRelativeGTP = newGTP;
                }

                if (CMDropper.smokePool != null && CMSmoke.RaycastSmoke(ray))
                {
                    surfaceDetected = false;
                    return;
                }
            }

            surfaceDetected = true;
        }

        void ClearTarget()
        {
            groundStabilized = false;
            lockedVessel = null;
            lockedPart = null;
            guidingOrdnance = false;
        }

        Coroutine resetCamera;
        IEnumerator ResetCamera()
        {
            resetting = true;
            radarLock = false;
            StopPointToPosRoutine();

            if (groundStabilized)
            {
                ClearTarget();
            }

            currentFovIndex = 0;
            //fov = zoomFovs[currentFovIndex];

            while (VectorUtils.Angle(cameraParentTransform.forward, cameraParentTransform.parent.forward) > 0.1f)
            {
                Vector3 newForward = Vector3.RotateTowards(cameraParentTransform.forward, cameraParentTransform.parent.forward, (2 / 3) * traverseRate * Mathf.Deg2Rad * Time.deltaTime, 0);
                //cameraParentTransform.rotation = Quaternion.LookRotation(newForward, VectorUtils.GetUpDirection(transform.position));
                PointCameraModel(newForward);
                gimbalLimitReached = false;
                yield return null;
            }
            resetting = false;
        }

        void StopPointToPosRoutine()
        {
            if (slewingToPosition)
            {
                StartCoroutine(StopPTPRRoutine());
            }
        }

        IEnumerator StopPTPRRoutine()
        {
            stopPTPR = true;
            yield return null;
            yield return new WaitForEndOfFrame();
            stopPTPR = false;
        }

        bool stopPTPR;
        bool slewingToPosition;

        public IEnumerator PointToPositionRoutine(Vector3 position, float attemptDuration, Vessel tgtVessel = null, bool clearTgt = true, Part tgtPart = null)
        {
            yield return StopPTPRRoutine();
            stopPTPR = false;
            slewingToPosition = true;
            radarLock = false;
            StopResetting();
            if (clearTgt) ClearTarget();
            if (cameraParentTransform == null)
            {
                slewingToPosition = false;
                yield break;
            }
            var wait = new WaitForFixedUpdate();
            Vector3 cameraPos;
            Vector3 cameraForward;
            CoMLock = tgtVessel;
            //float tolerance = tgtVessel ? tgtVessel.GetRadius() : 10f;
            float rangeTolerance;
            float crossrangeTolerance;
            Vector3 geoPos = Vector3.zero;
            float endTime = attemptDuration > 0 ? Time.time + attemptDuration : float.MaxValue;
            if (tgtVessel)
            {
                TargetInfo ti = tgtVessel.gameObject ? tgtVessel.gameObject.GetComponent<TargetInfo>() : null;
                if (ti)
                {
                    // Because TargetInfo already has all this data, we can just directly access it
                    rangeTolerance = ti.bounds[ti.boundsSorted[0]];
                    crossrangeTolerance = ti.bounds[ti.boundsSorted[2]];
                }
                else
                {
                    // Fallback method using vessel.vesselSize (which isn't great...)
                    Vector3 tempSize = vessel.vesselSize;

                    // We expect vessel forward/back to be large
                    rangeTolerance = tempSize.z;
                    // And vessel up/down to be small
                    crossrangeTolerance = tempSize.y;

                    // If z > y
                    if (rangeTolerance > crossrangeTolerance)
                    {
                        // If z > x
                        if (rangeTolerance > tempSize.x)
                        {
                            // z is the largest, and we take the smallest of the other two
                            crossrangeTolerance = Mathf.Min(crossrangeTolerance, tempSize.x);
                        }
                        else
                        {
                            // x is the largest and y is the smallest
                            rangeTolerance = tempSize.x;
                        }
                    }
                    else
                    {
                        // If z > x
                        if (rangeTolerance > tempSize.x)
                        {
                            // y is the largest
                            rangeTolerance = crossrangeTolerance;
                            // and x is the smallest
                            crossrangeTolerance = tempSize.x;
                        }
                        else
                        {
                            // Otherwise, z is the smallest
                            crossrangeTolerance = rangeTolerance;
                            // And the largest is between the other two
                            rangeTolerance = Mathf.Max(tempSize.x, tempSize.y);
                        }
                    }

                    // Gotta multiply by 0.5 -> get length / 2
                    rangeTolerance *= 0.5f;
                    // Gotta multiply by 0.5^2 -> get (height / 2)^2
                    crossrangeTolerance *= 0.25f * crossrangeTolerance;
                }
            }
            else
            {
                rangeTolerance = 10f;
                crossrangeTolerance = 100f;
                geoPos = VectorUtils.WorldPositionToGeoCoords(position, vessel.mainBody);
            }

            while (!stopPTPR && Time.time < endTime)
            {
                GetHitPoint();
                cameraForward = cameraParentTransform.transform.forward;
                (float rangeDist, Vector3 normalDist) = (targetPointPosition - position).DotProjectOnPlanePreNormalized(cameraForward);

                // If...
                if (surfaceDetected && // We've hit something
                    Mathf.Abs(rangeDist) < rangeTolerance && // And we're within tolerance in range
                    normalDist.sqrMagnitude < crossrangeTolerance) // And we're within crossrange tolerance
                {
                    break; // Break out of the loop
                }

                (float distance, Vector3 relativeDir) = (position - (cameraPos = cameraParentTransform.transform.position)).MagNorm();

                // If no tgtVessel or the tgtVessel is too far, break if angle < 0.1°
                if ((!tgtVessel || distance > maxRayDistance) && VectorUtils.AnglePreNormalized(cameraForward, relativeDir) < 0.1f) break;

                if (tgtVessel != null)
                {
                    position = tgtPart != null ? tgtPart.transform.position : tgtVessel.CoM + tgtVessel.Velocity() * Time.fixedDeltaTime;
                    lockedPart = tgtPart;
                    //if ((tgtVessel.CoM - cameraPos).sqrMagnitude < maxRayDistance * maxRayDistance)
                    //{
                    //    lockedVessel = tgtVessel;
                    //}
                    //else
                    //{
                    //    lockedVessel = null;
                    //}
                }
                else
                {
                    // Clear any potential vessel targets if we're not looking for a vessel
                    lockedVessel = null;
                    lockedPart = null;
                    guidingOrdnance = false;
                }
                //Vector3 newForward = Vector3.RotateTowards(cameraForward, position - cameraPos, traverseRate * Mathf.Deg2Rad * Time.fixedDeltaTime, 0);
                Vector3 newForward = position - cameraPos;
                float angle = VectorUtils.Angle(cameraForward, newForward);
                if (angle > traverseRate * Time.fixedDeltaTime)
                {
                    newForward = Vector3.Slerp(cameraForward, newForward, traverseRate * Time.fixedDeltaTime / angle);
                }
                //cameraParentTransform.rotation = Quaternion.LookRotation(newForward, VectorUtils.GetUpDirection(transform.position));
                PointCameraModel(newForward);
                yield return wait;
                if (cameraParentTransform == null)
                {
                    slewingToPosition = false;
                    yield break;
                }
                if (gimbalLimitReached)
                {
                    ClearTarget();
                    resetCamera = StartCoroutine(ResetCamera());
                    slewingToPosition = false;
                    yield break;
                }
                //position -= BDKrakensbane.FrameVelocityV3f * Time.fixedDeltaTime;
                position = tgtVessel ? tgtVessel.CoM : (VectorUtils.GetWorldSurfacePostion(geoPos, vessel.mainBody));
            }
            if (surfaceDetected && !stopPTPR)
            {
                //cameraParentTransform.transform.rotation = Quaternion.LookRotation(position - cameraParentTransform.position, VectorUtils.GetUpDirection(transform.position));
                //PointCameraModel(position - cameraParentTransform.position);
                GroundStabilize();
            }
            slewingToPosition = false;
        }

        void StopResetting()
        {
            if (resetting)
            {
                StopCoroutine(resetCamera);
                resetting = false;
            }
        }

        void ParseFovs()
        {
            zoomFovs = OtherUtils.ParseToFloatArray(zoomFOVs);
            zoomTimes = new float[zoomFovs.Length];
            zoomTimes[0] = 1f;
            // Just doing it based on relative size of objects
            float tanoneXzoom = Mathf.Tan(Mathf.Deg2Rad * zoomFovs[0]);
            for (int i = 1; i < zoomFovs.Length; i++)
                zoomTimes[i] = tanoneXzoom / Mathf.Tan(Mathf.Deg2Rad * zoomFovs[i]);
        }

        void OnDestroy()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                windowIsOpen = false;
                var weaponManager = WeaponManager;
                if (weaponManager)
                {
                    if (slaveTurrets)
                    {
                        weaponManager.slavingTurrets = false; //this should already be false...
                        weaponManager.slavedPosition = Vector3.zero;
                        weaponManager.slavedTarget = TargetSignatureData.noTarget;
                    }
                }
            }
            GameEvents.onVesselCreate.Remove(Disconnect);
            SlewingMouseCam = false;
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.BetterLateThanNever, CameraTracking);
        }

        Vector2 TargetAzimuthElevationScreenPos(Rect screenRect, Vector3 targetPosition, float textureSize)
        {
            Vector3 localPos = vessel.ReferenceTransform.InverseTransformPoint(targetPosition);
            Vector3 aziRef = Vector3.up;
            Vector3 aziPos = localPos.ProjectOnPlanePreNormalized(Vector3.forward);
            float elevation = VectorUtils.SignedAngle(aziPos, localPos, Vector3.forward);
            float normElevation = elevation / 70;

            float azimuth = VectorUtils.SignedAngle(aziRef, aziPos, Vector3.right);
            float normAzimuth = Mathf.Clamp(azimuth / 120, -1, 1);

            float x = screenRect.x + (screenRect.width / 2) + (normAzimuth * (screenRect.width / 2)) - (textureSize / 2);
            float y = screenRect.y + (screenRect.height / 4) + (normElevation * (screenRect.height / 4)) - (textureSize / 2);

            x = Mathf.Clamp(x, textureSize / 2, screenRect.width - (textureSize / 2));
            y = Mathf.Clamp(y, textureSize / 2, (screenRect.height) - (textureSize / 2));

            return new Vector2(x, y);
        }
        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();

            output.Append(Environment.NewLine);
            output.AppendLine($"Targeting Camera:");
            output.AppendLine($"- Slew rate: {traverseRate}Deg./s");
            output.AppendLine($"- Max traverse: {gimbalLimit} degrees");
            output.AppendLine($"- Max range: {maxRayDistance} m");
            return output.ToString();
        }
    }
}
