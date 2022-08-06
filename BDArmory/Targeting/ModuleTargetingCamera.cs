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

namespace BDArmory.Targeting
{
    public class ModuleTargetingCamera : PartModule
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
        public bool rollCameraModel = false;

        [KSPField(isPersistant = true)]
        public bool cameraEnabled;

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

        [KSPField(isPersistant = true)]
        public int currentFovIndex;

        [KSPField(isPersistant = true)]
        public bool slaveTurrets;

        [KSPField(isPersistant = true)]
        public bool CoMLock;

        public bool radarLock;


        [KSPField(isPersistant = true)]
        public bool groundStabilized;

        /// <summary>
        /// Point on surface that camera is focused and stabilized on.
        /// </summary>
        public Vector3 groundTargetPosition;

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
        public static ModuleTargetingCamera activeCam;
        public static bool camRectInitialized;
        public static bool windowIsOpen;
        private static float camImageSize = 360;
        private static float adjCamImageSize = 360;
        internal static bool ResizingWindow;
        internal static bool SlewingMouseCam;
        internal static bool ZoomKeysSet;
        internal static bool isZooming;
        internal static bool wasZooming;

        internal static bool SlewingButtonCam;
        float finalSlewSpeed;
        Vector2 slewInput = Vector2.zero;

        private static float gap = 2;
        private static float buttonHeight = 18;
        private static float controlsStartY = 22;
        private static float windowWidth = adjCamImageSize + (3 * buttonHeight) + 16 + 2 * gap;
        private static float windowHeight = adjCamImageSize + 23;
        private AxisBinding_Single ZoomKeyP;
        private AxisBinding_Single ZoomKeyS;
        private AxisBinding_Single NoZoomKeyP;
        private AxisBinding_Single NoZoomKeyS;

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

        private MissileFire wpmr;

        public MissileFire weaponManager
        {
            get
            {
                if (wpmr == null || wpmr.vessel != vessel)
                { wpmr = VesselModuleRegistry.GetMissileFire(vessel, true); }
                return wpmr;
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
                TargetingCamera.Instance.nvMode = nvMode;
                TargetingCamera.Instance.SetFOV(fov);
                ResizeTargetWindow();
            }

            cameraEnabled = true;

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
                    (new GameObject("TargetingCameraObject")).AddComponent<TargetingCamera>();
                }
            }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            ZoomKeyP = GameSettings.AXIS_MOUSEWHEEL.primary;
            ZoomKeyS = GameSettings.AXIS_MOUSEWHEEL.secondary;
            NoZoomKeyP = new AxisBinding_Single();
            NoZoomKeyS = new AxisBinding_Single();

            if (HighLogic.LoadedSceneIsFlight)
            {
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
            }
        }

        void Disconnect(Vessel v)
        {
            if (weaponManager && vessel)
            {
                if (weaponManager.vessel != vessel)
                {
                    if (slaveTurrets)
                    {
                        weaponManager.slavingTurrets = false;
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
                if (vessel.isActiveVessel && activeCam == this && TargetingCamera.cameraTransform)
                {
                    TargetingCamera.cameraTransform.rotation = Quaternion.LookRotation(cameraParentTransform.forward, worldUp);
                }
            }
        }

        void Update()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
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
                    }

                    if (radarLock)
                    {
                        UpdateRadarLock();
                    }

                    if (groundStabilized)
                    {
                        groundTargetPosition = VectorUtils.GetWorldSurfacePostion(bodyRelativeGTP, vessel.mainBody);//vessel.mainBody.GetWorldSurfacePosition(bodyRelativeGTP.x, bodyRelativeGTP.y, bodyRelativeGTP.z);
                        Vector3 lookVector = groundTargetPosition - cameraParentTransform.position;
                        //cameraParentTransform.rotation = Quaternion.LookRotation(lookVector);
                        PointCameraModel(lookVector);
                    }

                    Vector3 lookDirection = cameraParentTransform.forward;
                    if (Vector3.Angle(lookDirection, cameraParentTransform.parent.forward) > gimbalLimit)
                    {
                        lookDirection = Vector3.RotateTowards(cameraParentTransform.transform.parent.forward, lookDirection, gimbalLimit * Mathf.Deg2Rad, 0);
                        gimbalLimitReached = true;
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
                        Vector3 projectedForward = Vector3.ProjectOnPlane(cameraParentTransform.forward, eyeHolderTransform.parent.up);
                        if (projectedForward != Vector3.zero)
                        {
                            eyeHolderTransform.rotation = Quaternion.LookRotation(projectedForward, eyeHolderTransform.parent.up);
                        }
                    }

                    UpdateControls();
                    UpdateSlaveData();
                }
            }
        }

        public override void OnFixedUpdate()
        {
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
                if (delayedEnabling) return;

                if (cameraEnabled)
                {
                    GetHitPoint();
                }
            }
        }

        void UpdateKeyInputs()
        {
            if (!vessel.isActiveVessel)
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
                finalSlewSpeed = Mathf.Clamp(finalSlewSpeed + (0.5f * (fov / 60)), 0, 80 * fov / 60);
                SlewingButtonCam = false;
            }
            else
            {
                finalSlewSpeed = 15 * fov / 60;
            }
        }

        void UpdateRadarLock()
        {
            if (weaponManager && weaponManager.vesselRadarData && weaponManager.vesselRadarData.locked)
            {
                RadarDisplayData tgt = weaponManager.vesselRadarData.lockedTargetData;
                Vector3 radarTargetPos = tgt.targetData.predictedPosition;
                Vector3 targetDirection = radarTargetPos - cameraParentTransform.position;

                //Quaternion lookRotation = Quaternion.LookRotation(radarTargetPos-cameraParentTransform.position, VectorUtils.GetUpDirection(cameraParentTransform.position));
                if (Vector3.Angle(radarTargetPos - cameraParentTransform.position, cameraParentTransform.forward) < 0.5f)
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
                    Vector3 rotateTwdDirection = Vector3.RotateTowards(cameraParentTransform.forward, targetDirection, 1200 * Time.fixedDeltaTime * Mathf.Deg2Rad, 0);
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

            if (!wasZooming && isZooming)
            {
                wasZooming = true;
                SetZoomKeys();
            }

            if (!isZooming && wasZooming)
            {
                wasZooming = false;
                ResetZoomKeys();
            }

            if (HighLogic.LoadedSceneIsFlight && !MapView.MapIsEnabled && BDArmorySetup.GAME_UI_ENABLED && !delayedEnabling)
            {
                if (cameraEnabled && vessel.isActiveVessel && FlightGlobals.ready)
                {
                    //window
                    if (activeCam == this && TargetingCamera.ReadyForUse)
                    {
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
                    GUI.Label(new Rect(600, 1000, 100, 100), "Slew rate: " + finalSlewSpeed);
                }

                if (BDArmorySettings.DEBUG_LINES)
                {
                    if (groundStabilized)
                    {
                        GUIUtils.DrawLineBetweenWorldPositions(cameraParentTransform.position, groundTargetPosition, 2, Color.red);
                    }
                    else
                    {
                        GUIUtils.DrawLineBetweenWorldPositions(cameraParentTransform.position, targetPointPosition, 2, Color.red);
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

            GUI.DragWindow(new Rect(0, 0, BDArmorySetup.WindowRectTargetingCam.width - 18, 30));
            if (GUI.Button(new Rect(BDArmorySetup.WindowRectTargetingCam.width - 18, 2, 16, 16), "X", GUI.skin.button))
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

            // Right side control buttons
            DrawSideControlButtons(imageRect);

            // Check for mousedown / mousescroll in target Cam and handle slew and zoom.
            if (Event.current.type == EventType.MouseDown && imageRect.Contains(Event.current.mousePosition))
            {
                if (!SlewingMouseCam) SlewingMouseCam = true;
            }
            if (Event.current.type == EventType.Repaint && SlewingMouseCam)
            {
                if (Mouse.delta.x != 0 && Mouse.delta.y != 0)
                {
                    SlewRoutine(Mouse.delta);
                }
            }

            if (Event.current.type == EventType.Repaint && imageRect.Contains(Event.current.mousePosition))
            {
                if (!wasZooming) isZooming = true;
            }

            if (Event.current.type == EventType.ScrollWheel && imageRect.Contains(Event.current.mousePosition))
            {
                ZoomRoutine(Input.mouseScrollDelta);
            }
            if (Event.current.type == EventType.Repaint && !imageRect.Contains(Event.current.mousePosition))
            {
                if (wasZooming) isZooming = false;
            }

            float indicatorSize = Mathf.Clamp(64 * (adjCamImageSize / camImageSize), 48, 128);
            float indicatorBorder = imageRect.width * 0.056f;
            Vector3 vesForward = vessel.ReferenceTransform.up;
            Vector3 upDirection = (transform.position - FlightGlobals.currentMainBody.transform.position).normalized;

            //horizon indicator
            float horizY = imageRect.y + imageRect.height - indicatorSize - indicatorBorder;
            Vector3 hForward = Vector3.ProjectOnPlane(vesForward, upDirection);
            float hAngle = -BDAMath.SignedAngle(hForward, vesForward, upDirection);
            horizY -= (hAngle / 90) * (indicatorSize / 2);
            Rect horizonRect = new Rect(indicatorBorder + imageRect.x, horizY, indicatorSize, indicatorSize);
            GUI.DrawTexture(horizonRect, BDArmorySetup.Instance.horizonIndicatorTexture, ScaleMode.StretchToFill, true);

            //roll indicator
            Rect rollRect = new Rect(indicatorBorder + imageRect.x, imageRect.y + imageRect.height - indicatorSize - indicatorBorder, indicatorSize, indicatorSize);
            GUI.DrawTexture(rollRect, rollReferenceTexture, ScaleMode.StretchToFill, true);
            Vector3 localUp = vessel.ReferenceTransform.InverseTransformDirection(upDirection);
            localUp = Vector3.ProjectOnPlane(localUp, Vector3.up).normalized;
            float rollAngle = -BDAMath.SignedAngle(-Vector3.forward, localUp, Vector3.right);
            GUIUtility.RotateAroundPivot(rollAngle, rollRect.center);
            GUI.DrawTexture(rollRect, rollIndicatorTexture, ScaleMode.StretchToFill, true);
            GUI.matrix = Matrix4x4.identity;

            //target direction indicator
            float angleToTarget = BDAMath.SignedAngle(hForward, Vector3.ProjectOnPlane(targetPointPosition - transform.position, upDirection), Vector3.Cross(upDirection, hForward));
            GUIUtility.RotateAroundPivot(angleToTarget, rollRect.center);
            GUI.DrawTexture(rollRect, BDArmorySetup.Instance.targetDirectionTexture, ScaleMode.StretchToFill, true);
            GUI.matrix = Matrix4x4.identity;

            //resizing
            Rect resizeRect =
                new Rect(BDArmorySetup.WindowRectTargetingCam.width - 18, BDArmorySetup.WindowRectTargetingCam.height - 18, 16, 16);
            GUI.DrawTexture(resizeRect, GUIUtils.resizeTexture, ScaleMode.StretchToFill, true);
            if (Event.current.type == EventType.MouseDown && resizeRect.Contains(Event.current.mousePosition))
            {
                ResizingWindow = true;
            }

            if (Event.current.type == EventType.Repaint && ResizingWindow)
            {
                if (Mouse.delta.x != 0 || Mouse.delta.y != 0)
                {
                    float diff = Mouse.delta.x + Mouse.delta.y;
                    UpdateTargetScale(diff);
                    ResizeTargetWindow();
                }
            }
            //ResetZoomKeys();
            GUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectTargetingCam);
        }

        internal static void UpdateTargetScale(float diff)
        {
            float scaleDiff = ((diff / (BDArmorySetup.WindowRectTargetingCam.width + BDArmorySetup.WindowRectTargetingCam.height)) * 100 * .01f);
            BDArmorySettings.TARGET_WINDOW_SCALE += Mathf.Abs(scaleDiff) > .01f ? scaleDiff : scaleDiff > 0 ? .01f : -.01f;
            BDArmorySettings.TARGET_WINDOW_SCALE = Mathf.Clamp(BDArmorySettings.TARGET_WINDOW_SCALE,
                BDArmorySettings.TARGET_WINDOW_SCALE_MIN,
                BDArmorySettings.TARGET_WINDOW_SCALE_MAX);
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
                //SlewCamera(Vector3.up);
                slewInput.y = 1;
            }

            if (GUI.RepeatButton(slewDownRect, "v", GUI.skin.button))
            {
                //SlewCamera(Vector3.down);
                slewInput.y = -1;
            }

            if (GUI.RepeatButton(slewLeftRect, "<", GUI.skin.button))
            {
                //SlewCamera(Vector3.left);
                slewInput.x = -1;
            }

            if (GUI.RepeatButton(slewRightRect, ">", GUI.skin.button))
            {
                //SlewCamera(Vector3.right);
                slewInput.x = 1;
            }
        }

        private void DrawZoomButtons()
        {
            float zoomStartX = adjCamImageSize * 0.94f - (buttonHeight * 3) - (4 * gap);
            float zoomStartY = 20 + (adjCamImageSize * 0.06f);
            Rect zoomOutRect = new Rect(zoomStartX, zoomStartY, buttonHeight, buttonHeight);
            Rect zoomInfoRect = new Rect(zoomStartX + buttonHeight + gap, zoomStartY, buttonHeight + 4 * gap, buttonHeight);
            Rect zoomInRect = new Rect(zoomStartX + buttonHeight * 2 + 5 * gap, zoomStartY, buttonHeight, buttonHeight);

            GUI.enabled = currentFovIndex > 0;
            if (GUI.Button(zoomOutRect, "-", GUI.skin.button))
            {
                ZoomOut();
            }

            GUIStyle zoomBox = GUI.skin.box;
            zoomBox.alignment = TextAnchor.UpperCenter;
            zoomBox.padding.top = 0;
            GUI.enabled = true;
            GUI.Label(zoomInfoRect, (currentFovIndex + 1).ToString() + "X", zoomBox);

            GUI.enabled = currentFovIndex < zoomFovs.Length - 1;
            if (GUI.Button(zoomInRect, "+", GUI.skin.button))
            {
                ZoomIn();
            }
            GUI.enabled = true;
        }

        private void DrawSideControlButtons(Rect imageRect)
        {
            GUIStyle dataStyle = new GUIStyle();
            dataStyle.alignment = TextAnchor.MiddleCenter;
            dataStyle.normal.textColor = Color.white;
            GUIStyle buttonStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
            buttonStyle.fontSize = 11;

            float line = buttonHeight + gap;
            float buttonWidth = 3 * buttonHeight + 4 * gap;
            //groundStablize button
            float startX = imageRect.width + 3 * gap;
            Rect stabilizeRect = new Rect(startX, controlsStartY, buttonWidth, buttonHeight + line);
            if (!groundStabilized)
            {
                if (GUI.Button(stabilizeRect, "Lock\nTarget", buttonStyle))
                {
                    GroundStabilize();
                }
            }
            else
            {
                if (GUI.Button(new Rect(startX, controlsStartY, buttonWidth, buttonHeight),
                    "Unlock", buttonStyle))
                {
                    ClearTarget();
                }

                if (weaponManager)
                {
                    Rect sendGPSRect = new Rect(startX, controlsStartY + line, buttonWidth, buttonHeight);
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
                        if (currMissile.TargetingMode == MissileBase.TargetingModes.Gps ||
                            currMissile.TargetingMode == MissileBase.TargetingModes.Laser)
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
            Rect resetRect = new Rect(startX, controlsStartY + (2 * line), buttonWidth, buttonHeight);
            if (GUI.Button(resetRect, "Reset", buttonStyle))
            {
                ResetCameraButton();
            }

            //CoM lock
            Rect comLockRect = new Rect(startX, controlsStartY + 3 * line, buttonWidth, buttonHeight);
            GUIStyle comStyle = new GUIStyle(CoMLock ? BDArmorySetup.BDGuiSkin.box : buttonStyle);
            comStyle.fontSize = 10;
            comStyle.wordWrap = false;
            if (GUI.Button(comLockRect, "CoM Track", comStyle))
            {
                CoMLock = !CoMLock;
            }

            //radar slave
            Rect radarSlaveRect = new Rect(startX, controlsStartY + 4 * line, buttonWidth, buttonHeight);
            GUIStyle radarSlaveStyle = radarLock ? BDArmorySetup.BDGuiSkin.box : buttonStyle;
            if (GUI.Button(radarSlaveRect, "Radar", radarSlaveStyle))
            {
                radarLock = !radarLock;
            }

            //slave turrets button
            Rect slaveRect = new Rect(startX, controlsStartY + 5 * line, buttonWidth, buttonHeight);
            if (!slaveTurrets)
            {
                if (GUI.Button(slaveRect, "Turrets", buttonStyle))
                {
                    SlaveTurrets();
                }
            }
            else
            {
                if (GUI.Button(slaveRect, "Turrets", BDArmorySetup.BDGuiSkin.box))
                {
                    UnslaveTurrets();
                }
            }

            //point to gps button
            Rect toGpsRect = new Rect(startX, controlsStartY + 6 * line, buttonWidth, buttonHeight);
            if (GUI.Button(toGpsRect, "To GPS", buttonStyle))
            {
                PointToGPSTarget();
            }

            //nv button
            float nvStartX = startX;
            Rect nvRect = new Rect(nvStartX, controlsStartY + 7 * line, buttonWidth, buttonHeight);
            string nvLabel = nvMode ? "NV Off" : "NV On";
            GUIStyle nvStyle = nvMode ? BDArmorySetup.BDGuiSkin.box : buttonStyle;
            if (GUI.Button(nvRect, nvLabel, nvStyle))
            {
                ToggleNV();
            }
        }

        void ResetCameraButton()
        {
            if (!resetting)
            {
                StartCoroutine("ResetCamera");
            }
        }

        void SendGPS()
        {
            if (groundStabilized && weaponManager)
            {
                BDATargetManager.GPSTargetList(weaponManager.Team).Add(new GPSTargetInfo(bodyRelativeGTP, "Target"));
                BDATargetManager.Instance.SaveGPSTargets();
            }
        }

        void SlaveTurrets()
        {
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
            }
        }

        void UpdateSlaveData()
        {
            if (!slaveTurrets) return;
            if (!weaponManager) return;
            weaponManager.slavingTurrets = true;
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
            if (!BDArmorySettings.TARGET_WINDOW_INVERT_MOUSE_X) direction.x = -direction.x; // Invert the x-axis by default (original defaults).
            if (BDArmorySettings.TARGET_WINDOW_INVERT_MOUSE_Y) direction.y = -direction.y;
            float velocity = Mathf.Abs(direction.x) > Mathf.Abs(direction.y) ? Mathf.Abs(direction.x) : Mathf.Abs(direction.y);
            Vector3 rotationAxis = Matrix4x4.TRS(Vector3.zero, Quaternion.LookRotation(cameraParentTransform.forward, vessel.upAxis), Vector3.one)
                .MultiplyVector(Quaternion.AngleAxis(90, Vector3.forward) * direction);
            float angle = velocity / (1 + currentFovIndex) * Time.deltaTime;
            if (angle / (1f + currentFovIndex) < .05f / (1f + currentFovIndex)) angle = .05f / ((1f + currentFovIndex) / 2f);
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

            radarLock = false;
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
            if (weaponManager && weaponManager.designatedGPSCoords != Vector3d.zero)
            {
                StartCoroutine(PointToPositionRoutine(VectorUtils.GetWorldSurfacePostion(weaponManager.designatedGPSCoords, vessel.mainBody)));
            }
        }

        private void ResetZoomKeys()
        {
            ZoomKeysSet = false;
            GameSettings.AXIS_MOUSEWHEEL.primary = ZoomKeyP;
            GameSettings.AXIS_MOUSEWHEEL.secondary = ZoomKeyS;
        }

        private void SetZoomKeys()
        {
            ZoomKeysSet = true;
            GameSettings.AXIS_MOUSEWHEEL.primary = NoZoomKeyP;
            GameSettings.AXIS_MOUSEWHEEL.secondary = NoZoomKeyS;
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

        GameObject debugSphere;

        void CreateDebugSphere()
        {
            debugSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            debugSphere.GetComponent<Collider>().enabled = false;
        }

        void MoveDebugSphere()
        {
            if (!debugSphere)
            {
                CreateDebugSphere();
            }
            debugSphere.transform.position = groundTargetPosition;
        }

        public void GroundStabilize()
        {
            if (vessel.packed) return;
            StopResetting();

            RaycastHit rayHit;
            Ray ray = new Ray(cameraParentTransform.position + (50 * cameraParentTransform.forward), cameraParentTransform.forward);
            bool raycasted = Physics.Raycast(ray, out rayHit, maxRayDistance - 50, (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.EVA | LayerMasks.Unknown19 | LayerMasks.Unknown23 | LayerMasks.Wheels));
            if (raycasted)
            {
                if (FlightGlobals.getAltitudeAtPos(rayHit.point) < 0)
                {
                    raycasted = false;
                }
                else
                {
                    groundStabilized = true;
                    groundTargetPosition = rayHit.point;

                    if (CoMLock)
                    {
                        KerbalEVA hitEVA = rayHit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                        Part p = hitEVA ? hitEVA.part : rayHit.collider.GetComponentInParent<Part>();
                        if (p && p.vessel && p.vessel.CoM != Vector3.zero)
                        {
                            groundTargetPosition = p.vessel.CoM + (p.vessel.Velocity() * Time.fixedDeltaTime);
                            StartCoroutine(StabilizeNextFrame());
                        }
                    }
                    Vector3d newGTP = VectorUtils.WorldPositionToGeoCoords(groundTargetPosition, vessel.mainBody);
                    if (newGTP != Vector3d.zero)
                    {
                        bodyRelativeGTP = newGTP;
                    }
                }
            }

            if (!raycasted)
            {
                Vector3 upDir = VectorUtils.GetUpDirection(cameraParentTransform.position);
                double altitude = vessel.altitude; //MissileGuidance.GetRadarAltitude(vessel);
                double radius = vessel.mainBody.Radius;

                Vector3d planetCenter = vessel.GetWorldPos3D() - ((vessel.altitude + vessel.mainBody.Radius) * vessel.upAxis);
                double enter;
                if (VectorUtils.SphereRayIntersect(ray, planetCenter, radius, out enter))
                {
                    if (enter > 0)
                    {
                        groundStabilized = true;
                        groundTargetPosition = ray.GetPoint((float)enter);
                        Vector3d newGTP = VectorUtils.WorldPositionToGeoCoords(groundTargetPosition, vessel.mainBody);
                        if (newGTP != Vector3d.zero)
                        {
                            bodyRelativeGTP = newGTP;
                        }
                    }
                }
            }

            if (BDArmorySettings.DEBUG_RADAR)
            {
                MoveDebugSphere();
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
            Ray ray = new Ray(cameraParentTransform.position + (50 * cameraParentTransform.forward), cameraParentTransform.forward);
            if (Physics.Raycast(ray, out rayHit, maxRayDistance - 50, (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.EVA | LayerMasks.Unknown19 | LayerMasks.Unknown23 | LayerMasks.Wheels)))
            {
                targetPointPosition = rayHit.point;

                if (!surfaceDetected && groundStabilized && !gimbalLimitReached)
                {
                    groundStabilized = true;
                    groundTargetPosition = rayHit.point;

                    if (CoMLock)
                    {
                        KerbalEVA hitEVA = rayHit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                        Part p = hitEVA ? hitEVA.part : rayHit.collider.GetComponentInParent<Part>();
                        if (p && p.vessel)
                        {
                            groundTargetPosition = p.vessel.CoM;
                        }
                    }
                    Vector3d newGTP = VectorUtils.WorldPositionToGeoCoords(groundTargetPosition, vessel.mainBody);
                    if (newGTP != Vector3d.zero)
                    {
                        bodyRelativeGTP = newGTP;
                    }
                }

                surfaceDetected = true;

                if (groundStabilized && !gimbalLimitReached && CMDropper.smokePool != null)
                {
                    if (CMSmoke.RaycastSmoke(ray))
                    {
                        surfaceDetected = false;
                    }
                }
            }
            else
            {
                targetPointPosition = cameraParentTransform.position + (maxRayDistance * cameraParentTransform.forward);
                surfaceDetected = false;
            }
        }

        void ClearTarget()
        {
            groundStabilized = false;
        }

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

            while (Vector3.Angle(cameraParentTransform.forward, cameraParentTransform.parent.forward) > 0.1f)
            {
                Vector3 newForward = Vector3.RotateTowards(cameraParentTransform.forward, cameraParentTransform.parent.forward, 60 * Mathf.Deg2Rad * Time.deltaTime, 0);
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

        public IEnumerator PointToPositionRoutine(Vector3 position)
        {
            yield return StopPTPRRoutine();
            stopPTPR = false;
            slewingToPosition = true;
            radarLock = false;
            StopResetting();
            ClearTarget();
            if (cameraParentTransform == null) yield break;
            while (!stopPTPR && Vector3.Angle(cameraParentTransform.transform.forward, position - (cameraParentTransform.transform.position)) > 0.1f)
            {
                Vector3 newForward = Vector3.RotateTowards(cameraParentTransform.transform.forward, position - cameraParentTransform.transform.position, 90 * Mathf.Deg2Rad * Time.fixedDeltaTime, 0);
                //cameraParentTransform.rotation = Quaternion.LookRotation(newForward, VectorUtils.GetUpDirection(transform.position));
                PointCameraModel(newForward);
                yield return new WaitForFixedUpdate();
                if (gimbalLimitReached)
                {
                    ClearTarget();
                    StartCoroutine("ResetCamera");
                    slewingToPosition = false;
                    yield break;
                }
            }
            if (surfaceDetected && !stopPTPR)
            {
                //cameraParentTransform.transform.rotation = Quaternion.LookRotation(position - cameraParentTransform.position, VectorUtils.GetUpDirection(transform.position));
                PointCameraModel(position - cameraParentTransform.position);
                GroundStabilize();
            }
            slewingToPosition = false;
            yield break;
        }

        void StopResetting()
        {
            if (resetting)
            {
                StopCoroutine("ResetCamera");
                resetting = false;
            }
        }

        void ParseFovs()
        {
            zoomFovs = OtherUtils.ParseToFloatArray(zoomFOVs);
        }

        void OnDestroy()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                windowIsOpen = false;
                if (wpmr)
                {
                    if (slaveTurrets)
                    {
                        weaponManager.slavingTurrets = false;
                    }
                }
            }
            GameEvents.onVesselCreate.Remove(Disconnect);
        }

        Vector2 TargetAzimuthElevationScreenPos(Rect screenRect, Vector3 targetPosition, float textureSize)
        {
            Vector3 localPos = vessel.ReferenceTransform.InverseTransformPoint(targetPosition);
            Vector3 aziRef = Vector3.up;
            Vector3 aziPos = Vector3.ProjectOnPlane(localPos, Vector3.forward);
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
    }
}
