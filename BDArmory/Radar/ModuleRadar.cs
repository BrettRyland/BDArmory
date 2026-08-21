using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Guidances;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.WeaponMounts;
using BDArmory.Weapons.Missiles;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BDArmory.Radar
{
    public class ModuleRadar : BDAPartModule
    {
        #region KSPFields (Part Configuration)

        #region General Configuration

        [KSPField]
        public string radarName;

        [KSPField]
        public int turretID = 0;

        [KSPField]
        public string rotationTransformName = string.Empty;
        Transform rotationTransform;

        [KSPField]
        public string radarTransformName = string.Empty;
        Transform radarTransform;

        [KSPField]
        public string deployAnimationName = "";

        [KSPField] 
        public float deployAnimationSpeed = 1;

        AnimationState deployState;
        public bool hasDeployAnimation;

        #endregion General Configuration

        #region Radar Capabilities

        [KSPField]
        public int rwrThreatType = 0;               //IMPORTANT, configures which type of radar it will show up as on the RWR
        public RadarWarningReceiver.RWRThreatTypes rwrType = RadarWarningReceiver.RWRThreatTypes.SAM;

        [KSPField]
        public double resourceDrain = 0.825;        //resource (EC/sec) usage of active radar

        [KSPField]
        public string resourceName = "ElectricCharge";

        private int resourceID;

        [KSPField]
        public bool omnidirectional = true;			//false=scan FoV limited to directionalFieldOfView

        // NOTE: The radar is assumed to have full roll stabilization capabilities!
        [KSPField]
        public string directionalFieldOfView = "90";   //relevant for NON-omnidirectional only

        [KSPField]
        public string elevationFOV = "-1";             //FoV of the radar in the vertical axis

        public float radarAzOffset = 0f;
        public float radarAzFOV = 90f;
        public float[] radarAzLimits = [-45f, 45f];
        public float radarElOffset = 0f;
        public float radarElFOV = 90f;
        public float[] radarElLimits = [-45f, 45f];

        public float[] radarMinMaxAzLimits = [-45f, 45f];
        public float[] radarMinMaxElLimits = [-45f, 45f];

        [KSPField]
        public float boresightFOV = 10;				//relevant for boresight only

        [KSPField]
        public float scanRotationSpeed = 120; 		//in degrees per second, relevant for omni and directional

        [KSPField]
        public float lockRotationSpeed = 120;		//in degrees per second, relevant for omni only

        [KSPField]
        public float lockRotationAngle = 4;         //???

        [KSPField]
        public bool showDirectionWhileScan = false; //radar can show direction indicator of contacts (false: can show contacts as blocks only)

        [KSPField]
        public float multiLockFOV = 30;             //??

        [KSPField]
        public float lockAttemptFOV = 2;            //??

        [KSPField]
        public bool canScan = true;                 //radar has detection capabilities

        [KSPField]
        public bool canLock = true;					//radar has locking/tracking capabilities

        [KSPField]
        public int maxLocks = 1;					//how many targets can be locked/tracked simultaneously

        [KSPField]
        public bool canTrackWhileScan = false;      //when tracking/locking, can we still detect/scan?

        [KSPField]
        public bool canReceiveRadarData = false;    //can radar data be received from friendly sources?

        [KSPField] // DEPRECATED
        public bool canRecieveRadarData = false;    // Original mis-spelling of "receive" for compatibility.

        [KSPField]
        public FloatCurve radarDetectionCurve = new FloatCurve();		//FloatCurve defining at what range which RCS size can be detected

        [KSPField]
        public FloatCurve radarLockTrackCurve = new FloatCurve();		//FloatCurve defining at what range which RCS size can be locked/tracked

        [KSPField]
        public FloatCurve radarVelocityGate = new FloatCurve();		//FloatCurve defining the reduction in received RCS due to a doppler gate

        [KSPField]
        public FloatCurve radarRangeGate = new FloatCurve();		//FloatCurve defining the reduction in received RCS due to a range gate

        [KSPField]
        public float radarMinTrackSCR = 1f;

        [KSPField]
        public bool radarCanNotch = true;

        [KSPField]
        public float radarGroundClutterFactor = 0.25f; //Factor defining how effective the radar is for look-down, compensating for ground clutter (0=ineffective, 1=fully effective)
                                                       //default to 0.25, so all cross sections of landed/splashed/submerged vessels are reduced to 1/4th, as these vessel usually a quite large
        [KSPField]
        public float radarChaffClutterFactor = 1.0f;     //Factor defining how effective the radar is at compensating for enemy chaff (0 = ineffective, 1 = no decrease in signal position/strength)
                                                         //default to 1, since that's legacy behavior. Relevant for guiding SARH ordnance. Allows up to two values for modifying chaff and notchMod.

        [KSPField]
        public string radarChaffNotchClutterFactor = "1.0";

        public float _radarChaffNotchVFac;
        public float _radarChaffNotchRFac;

        [KSPField]
        public FloatCurve radarGlintCurve = new FloatCurve();		//FloatCurve defining the reduction in received RCS due to a range gate

        [KSPField]
        public float radarGlintMult = -1f;

        [KSPField]
        public int sonarType = 0; //0 = Radar; 1 == Active Sonar; 2 == Passive Sonar

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_DynamicRadar", advancedTweakable = true),//Disable Radar vs ARMs
            UI_Toggle(enabledText = "#LOC_BDArmory_true", disabledText = "#LOC_BDArmory_false", scene = UI_Scene.All),]//Starboard (CW)--Port (CCW)
        public bool DynamicRadar = false;

        MissileLauncher ml = null;
        public bool isMissileRadar => ml != null;
        [KSPField]
        public float maxDatalinkRange = -1; //-1 for satellite link/infinite range, else dist in m
        public enum SonarModes
        {
            None = 0,
            Active = 1,
            passive = 2
        }
        public SonarModes sonarMode = SonarModes.None;

        #endregion Radar Capabilities

        #region Persisted State in flight

        [KSPField(isPersistant = true)]
        public string linkedVesselID;

        [KSPField(isPersistant = true)]
        public bool radarEnabled;

        [KSPField(isPersistant = true)]
        public int rangeIndex = 99;

        [KSPField(isPersistant = true)]
        public float currentAngle;

        private float ReferenceUpdateTime = -1f;

        // Variables to pre-calculate transform directions
        public Vector3 currPosition;
        public Vector3 currForward;
        public Vector3 currUp;
        public Vector3 currRight;

        private float DisplayUpdateTime = -1f;

        // Rotated forward vector according to azimuth and elevation
        // offsets for display purposes
        public Vector3 currDisplayForward;

        #endregion Persisted State in flight

        #region DEPRECATED! ->see Radar Capabilities section for new detectionCurve + trackingCurve

        [Obsolete]
        [KSPField]
        public float minSignalThreshold = 90;

        [Obsolete]
        [KSPField]
        public float minLockedSignalThreshold = 90;

        #endregion DEPRECATED! ->see Radar Capabilities section for new detectionCurve + trackingCurve

        #endregion KSPFields (Part Configuration)

        #region KSP Events & Actions

        [KSPAction("Toggle Radar")]
        public void AGEnable(KSPActionParam param)
        {
            if (radarEnabled)
            {
                DisableRadar();
            }
            else
            {
                EnableRadar();
            }
        }

        [KSPEvent(active = true, guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_ToggleRadar")]//Toggle Radar
        public void Toggle()
        {
            if (radarEnabled)
            {
                DisableRadar();
            }
            else
            {
                EnableRadar();
            }
        }

        [KSPAction("Target Next")]
        public void TargetNext(KSPActionParam param)
        {
            vesselRadarData.TargetNext();
        }

        [KSPAction("Target Prev")]
        public void TargetPrev(KSPActionParam param)
        {
            vesselRadarData.TargetPrev();
        }

        #endregion KSP Events & Actions

        #region Part members

        //locks
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_CurrentLocks")]//Current Locks
        public int currLocks;

        public bool locked
        {
            get { return currLocks > 0; }
        }

        public int currentLocks
        {
            get { return currLocks; }
        }

        private TargetSignatureData[] attemptedLocks;
        //private bool[] lockSuccesses; // Removed as it was deemed unecessary
        private List<TargetSignatureData> lockedTargets;

        public TargetSignatureData lockedTarget
        {
            get
            {
                if (currLocks == 0) return TargetSignatureData.noTarget;
                else
                {
                    return lockedTargets[lockedTargetIndex];
                }
            }
        }

        private int lockedTargetIndex;

        public int currentLockIndex
        {
            get { return lockedTargetIndex; }
        }

        public float radarMinDistanceDetect
        {
            get { return radarDetectionCurve.minTime; }
        }

        //[KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Detection Range")]
        public float radarMaxDistanceDetect
        {
            get { return radarDetectionCurve.maxTime; }
        }

        public float radarMinDistanceLockTrack
        {
            get { return radarLockTrackCurve.minTime; }
        }

        //[KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Locking Range")]
        public float radarMaxDistanceLockTrack
        {
            get { return radarLockTrackCurve.maxTime; }
        }

        public float radarMaxRangeGate
        {
            get { return radarRangeGate.maxTime; }
        }
        public float radarMinRangeGate
        {
            get { return radarRangeGate.minTime; }
        }

        public float radarMaxVelocityGate
        {
            get { return radarVelocityGate.maxTime; }
        }

        public float radarMinVelocityGate
        {
            get { return radarVelocityGate.minTime; }
        }

        //linked vessels
        private List<VesselRadarData> linkedToVessels;
        public int linkedVRDs
        {
            get { return linkedToVessels.Count; }
        }
        public List<ModuleRadar> availableRadarLinks;
        private bool unlinkOnDestroy = true;

        //GUI
        private bool drawGUI;
        public float signalPersistTime;
        public float signalPersistTimeForRwr;

        //scanning
        private float currentAngleLock;
        public Transform referenceTransform;
        private float radialScanDirection = 1;
        private float lockScanDirection = 1;

        public bool boresightScan;

        //locking
        public float lockScanAngle;
        public bool slaveTurrets;
        public ModuleTurret lockingTurret;

        // lockingPitch and lockingYaw are toggles for whether or not the radar should be able to control the lockingTurret's pitch/yaw
        // this is associated with MissileTurret and ModuleWeapon turrets, if the radar is mounted on a turret.
        public bool lockingPitch = true;
        public bool lockingYaw = true;

        //vessel
        private MissileFire wpmr;

        public MissileFire WeaponManager
        {
            get
            {
                if (ml != null && ml.FiredByWM != null)
                    wpmr = ml.FiredByWM;
                else if (wpmr == null || !wpmr.IsPrimaryWM || wpmr.vessel != vessel)
                    wpmr = vessel && vessel.loaded ? vessel.ActiveController().WM : null;
                return wpmr;
            }
        }

        public VesselRadarData vesselRadarData;
        private string myVesselID;

        // part state
        private bool startupComplete;
        public float leftLimit;
        public float rightLimit;
        private int snapshotTicker;

        #endregion Part members

        void UpdateToggleGuiName()
        {
            Events[nameof(Toggle)].guiName = radarEnabled ? StringUtils.Localize("#autoLOC_bda_1000000") : StringUtils.Localize("#autoLOC_bda_1000001");		// #autoLOC_bda_1000000 = Disable Radar		// #autoLOC_bda_1000001 = Enable Radar
        }
        void Start()
        {
            resourceID = PartResourceLibrary.Instance.GetDefinition(resourceName).id;
            ml = part.FindModuleImplementing<MissileLauncher>();
            if (ml != null)
            {
                Events[nameof(Toggle)].guiActive = false;
                Events[nameof(Toggle)].guiActiveEditor = false;
            }
        }

        public void EnsureVesselRadarData(bool addRadar = false)
        {
            if (vessel == null) return;
            //myVesselID = vessel.id.ToString();

            bool swappedVessels = false;
            if (vesselRadarData == null || (swappedVessels = (vesselRadarData.vessel != vessel)) || vesselRadarData.weaponManager != WeaponManager)
            {
                // Technically it would be better if we linked to the previous vessel here, but theoretically speaking,
                // if guard mode is enabled post-decouple on the child craft it should automatically datalink with all
                // available VRDs post swap taking care of this. If we do want to ensure this functions properly even
                // without guard mode being enabled post decouple we would add a `QueueVRDLink(vrd)` function to
                // vesselRadarData, save the previous VRD in this if statement, and then queue the link
                if (swappedVessels)
                {
                    vesselRadarData.RemoveRadar(this);
                }
                vesselRadarData = vessel.gameObject.GetComponent<VesselRadarData>();
                if (vesselRadarData == null)
                    vesselRadarData = vessel.gameObject.AddComponent<VesselRadarData>();
                vesselRadarData.weaponManager = WeaponManager;

                // Something wasn't right with the previous VRD so make sure we add the radar, primarily to take care of the multi-craft case
                addRadar = true;
            }

            if (addRadar && radarEnabled)
            {
                vesselRadarData.AddRadar(this);
            }
        }

        public void EnableRadar()
        {
            if (deployState != null) StartCoroutine(DeployAnimation(true));
            else
            {
                radarEnabled = true;
                RadarSetup();
            }         
        }
        void RadarSetup()
        {
            if (isMissileRadar)
            {
                maxLocks = 0; //don't allow locks on sonobuoys, etc. They make very little sense
                canLock = false;
            }
            EnsureVesselRadarData(true);

            UpdateToggleGuiName();
            //vesselRadarData.AddRadar(this); // Moved this to EnsureVesselRadarData() to account for the multi-craft case
            var wm = WeaponManager;
            if (wm != null)
            {
                if (wm.guardMode) vesselRadarData.queueLinks = true;
                if (sonarMode == SonarModes.None)
                    wm._radarsEnabled = true;
                else if (sonarMode == SonarModes.Active)
                    wm._sonarsEnabled = true;
            }
        }
        public void DisableRadar()
        {
            if (locked)
            {
                UnlockAllTargets();
            }
            if (deployState != null) StartCoroutine(DeployAnimation(false));
            else radarEnabled = false;
            UpdateToggleGuiName();

            if (vesselRadarData)
            {
                vesselRadarData.RemoveRadar(this);
            }

            List<VesselRadarData>.Enumerator vrd = linkedToVessels.GetEnumerator();
            while (vrd.MoveNext())
            {
                if (vrd.Current == null) continue;
                vrd.Current.UnlinkDisabledRadar(this);
            }
            vrd.Dispose();
            var weaponManager = WeaponManager;
            using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedvessels.MoveNext())
                {
                    BDATargetManager.ClearRadarReport(loadedvessels.Current, weaponManager); //reset radar contact status
                }
            if (weaponManager != null)
            {
                if (weaponManager.radars.Count > 1)
                {
                    bool detectorsEnabled = false;
                    using (List<ModuleRadar>.Enumerator rd = weaponManager.radars.GetEnumerator())
                        while (rd.MoveNext())
                        {
                            if (rd.Current == null || rd.Current.sonarMode != sonarMode) continue;
                            //mf._radarsEnabled = false;
                            detectorsEnabled = false;
                            if (rd.Current != this && rd.Current.radarEnabled)
                            {
                                //mf._radarsEnabled = true;
                                detectorsEnabled = true;
                                break;
                            }
                        }

                    if (sonarMode == SonarModes.None)
                        weaponManager._radarsEnabled = detectorsEnabled;
                    else if (sonarMode == SonarModes.Active)
                        weaponManager._sonarsEnabled = detectorsEnabled;
                }
                else
                {
                    if (sonarMode == SonarModes.None)
                        weaponManager._radarsEnabled = false;
                    else if (sonarMode == SonarModes.Active)
                        weaponManager._sonarsEnabled = false;
                }
            }
        }

        IEnumerator DeployAnimation(bool forward)
        {
            var wait = new WaitForFixedUpdate();
            yield return wait;

            if (forward)
            {
                while (deployState.normalizedTime < 1)
                {
                    deployState.speed = deployAnimationSpeed;
                    yield return wait;
                }
                deployState.normalizedTime = 1;
                radarEnabled = true;
                Debug.Log("[parachute debug] radar enabled");
                RadarSetup();                
            }
            else
            {
                deployState.speed = 0;
                radarEnabled = false;

                while (deployState.normalizedTime > 0)
                {
                    deployState.speed = -deployAnimationSpeed;
                    yield return wait;
                }
                deployState.normalizedTime = 0;
            }
            deployState.speed = 0;
        }

        void OnDestroy()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (vesselRadarData)
                {
                    vesselRadarData.RemoveRadar(this);
                    vesselRadarData.RemoveDataFromRadar(this);
                }

                referenceTransform = null;

                if (linkedToVessels != null)
                {
                    List<VesselRadarData>.Enumerator vrd = linkedToVessels.GetEnumerator();
                    while (vrd.MoveNext())
                    {
                        if (vrd.Current == null) continue;
                        if (unlinkOnDestroy)
                        {
                            vrd.Current.UnlinkDisabledRadar(this);
                        }
                        else
                        {
                            vrd.Current.BeginWaitForUnloadedLinkedRadar(this, myVesselID);
                        }
                    }
                    vrd.Dispose();
                }
            }
        }

        void SetNotchChaffFac()
        {
            string[] chaffStrings = radarChaffNotchClutterFactor.Split([',']);
            if (chaffStrings.Length == 0)
            {
                _radarChaffNotchVFac = 1.0f;
                _radarChaffNotchRFac = 1.0f;
                return;
            }

            if (float.TryParse(chaffStrings[0], out float temp))
            {
                _radarChaffNotchVFac = temp;
            }
            else
            {
                _radarChaffNotchVFac = 1.0f;
            }

            if (chaffStrings.Length > 1 && float.TryParse(chaffStrings[1], out temp))
            {
                _radarChaffNotchRFac = temp;
            }
            else
            {
                _radarChaffNotchRFac = _radarChaffNotchVFac;
            }
        }

        void SetRadarLimits()
        {
            ParseRadarLimits(directionalFieldOfView, out radarAzOffset, out radarAzFOV, out radarAzLimits, out radarMinMaxAzLimits);
            // Retain old radar characteristics, if omnidirectional the radar should be able to see targets at +/- 90, otherwise
            // the radar could previously see targets at +/- 90 but not lock them, so we'll just lock it to a square FoV
            ParseRadarLimits(elevationFOV, out radarElOffset, out radarElFOV, out radarElLimits, out radarMinMaxElLimits, true);
            if (BDArmorySettings.DEBUG_RADAR)
            {
                Debug.Log($"[BDArmory.ModuleRadar] radarAzOffset {radarAzOffset}, radarAzFOV: {radarAzFOV}, radarAzLimits: {radarAzLimits[0]},{radarAzLimits[1]}, radarMinMaxAzLimits: {radarMinMaxAzLimits[0]},{radarMinMaxAzLimits[1]}");
                Debug.Log($"[BDArmory.ModuleRadar] radarElOffset {radarElOffset}, radarElFOV: {radarElFOV}, radarElLimits: {radarElLimits[0]},{radarElLimits[1]}, radarMinMaxAzLimits: {radarMinMaxElLimits[0]},{radarMinMaxElLimits[1]}");
            }
        }

        void ParseRadarLimits(in string radarLimitString, out float radarOffset, out float radarFOV, out float[] radarLimits, out float[] radarMinMaxLimits, bool elevationLimits = false)
        {
            // If we're parsing elevation limits
            if (elevationLimits)
            {
                // Then consider if it's omnidirectional or not, by default, omni radars are allowed +/- 90° FoV
                // Otherwise the default is a square radar scan area (based on radarAZLimits)
                radarLimits = omnidirectional ? [-90f, 90f] : [radarAzLimits[0], radarAzLimits[1]];
                //radarMinMaxLimits = omnidirectional ? [90f, 90f] : [radarMinMaxAzLimits[0], radarMinMaxAzLimits[1]];
                // Even if the azimuth is offset, we should start with no offset for elevation
                radarMinMaxLimits = omnidirectional ? [90f, 90f] : [0.5f * radarAzFOV, 0.5f * radarAzFOV];
                // Default omnidirectional FoV is 180°
                radarFOV = omnidirectional ? 180f : radarAzFOV;
            }
            else
            {
                // If we're not parsing elevation limits, then default to a +/- 45° FoV
                radarLimits = [-45f, 45f];
                radarMinMaxLimits = [45f, 45f];
                radarFOV = 90f;
            }
            
            // For both az/el the dfault is 0 offset
            radarOffset = 0f;
            
            string[] limitStrings = radarLimitString.Split([',']);
            if (limitStrings.Length > 0)
            {
                // If we're setting a left/right limit
                if (limitStrings.Length > 1)
                {
                    float tempLim = -45f;
                    // Get first limit
                    if (float.TryParse(limitStrings[0], out float temp))
                        tempLim = temp;
                    // Get second limit
                    if (float.TryParse(limitStrings[1], out temp))
                    {
                        // Test which limit should be which
                        if (tempLim < temp)
                        {
                            radarLimits[0] = tempLim;
                            radarLimits[1] = temp;
                        }
                        else
                        {
                            radarLimits[0] = temp;
                            radarLimits[1] = tempLim;
                        }
                    }

                    radarMinMaxLimits[0] = Mathf.Min(Mathf.Abs(radarLimits[0]), Mathf.Abs(radarLimits[1]));
                    radarMinMaxLimits[1] = Mathf.Max(Mathf.Abs(radarLimits[0]), Mathf.Abs(radarLimits[1]));

                    // Set the offset
                    radarOffset = (radarLimits[1] + radarLimits[0]) * 0.5f;
                    // Set the total width
                    radarFOV = radarLimits[1] - radarLimits[0];
                }
                else
                {
                    // Set total width
                    if (float.TryParse(limitStrings[0], out float temp))
                    {
                        if (temp < 0f)
                            return;
                        radarFOV = temp;
                    }

                    // Set left/right limits
                    radarLimits[1] = 0.5f * radarFOV;
                    radarLimits[0] = -radarLimits[1];

                    radarMinMaxLimits[0] = radarLimits[1];
                    radarMinMaxLimits[1] = radarLimits[1];
                }
            }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            if (HighLogic.LoadedSceneIsFlight)
            {
                myVesselID = vessel.id.ToString();
                RadarUtils.SetupResources();

                if (string.IsNullOrEmpty(radarName))
                {
                    radarName = part.partInfo.title;
                }

                linkedToVessels = new List<VesselRadarData>();

                SetRadarLimits();
                SetNotchChaffFac();

                signalPersistTime = omnidirectional
                    ? 360 / (scanRotationSpeed + 5)
                    : radarAzFOV / (scanRotationSpeed + 5);

                rwrType = (RadarWarningReceiver.RWRThreatTypes)rwrThreatType;
                sonarMode = (SonarModes)sonarType;
                if (rwrType == RadarWarningReceiver.RWRThreatTypes.Sonar)
                    signalPersistTimeForRwr = RadarUtils.ACTIVE_MISSILE_PING_PERSIST_TIME;
                else
                {
                    signalPersistTimeForRwr = signalPersistTime / 2;
                }

                if (rotationTransformName != string.Empty)
                {
                    rotationTransform = part.FindModelTransform(rotationTransformName);
                }
                radarTransform = radarTransformName != string.Empty ? part.FindModelTransform(radarTransformName) : part.transform;

                attemptedLocks = new TargetSignatureData[Math.Max(maxLocks, 6)];
                //lockSuccesses = new bool[maxLocks];
                TargetSignatureData.ResetTSDArray(ref attemptedLocks);
                lockedTargets = new List<TargetSignatureData>();

                referenceTransform = (new GameObject()).transform;
                referenceTransform.parent = radarTransform;
                referenceTransform.localPosition = Vector3.zero;

                List<ModuleTurret>.Enumerator turr = part.FindModulesImplementing<ModuleTurret>().GetEnumerator();
                while (turr.MoveNext())
                {
                    if (turr.Current == null) continue;
                    if (turr.Current.turretID != turretID) continue;
                    lockingTurret = turr.Current;
                    break;
                }
                turr.Dispose();

                //GameEvents.onVesselGoOnRails.Add(OnGoOnRails);    //not needed
                EnsureVesselRadarData();
                StartCoroutine(StartUpRoutine());
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                //Editor only:
                List<ModuleTurret>.Enumerator tur = part.FindModulesImplementing<ModuleTurret>().GetEnumerator();
                while (tur.MoveNext())
                {
                    if (tur.Current == null) continue;
                    if (tur.Current.turretID != turretID) continue;
                    lockingTurret = tur.Current;
                    break;
                }
                tur.Dispose();
                if (lockingTurret)
                {
                    lockingTurret.Fields[nameof(lockingTurret.minPitch)].guiActiveEditor = false;
                    lockingTurret.Fields[nameof(lockingTurret.maxPitch)].guiActiveEditor = false;
                    lockingTurret.Fields[nameof(lockingTurret.yawRange)].guiActiveEditor = false;
                }
            }

            if (deployAnimationName != "")
            {
                deployState = GUIUtils.SetUpSingleAnimation(deployAnimationName, part);
            }
            // check for not updated legacy part:
            if ((canScan && (radarMinDistanceDetect == float.MaxValue)) || (canLock && (radarMinDistanceLockTrack == float.MaxValue)))
            {
                Debug.Log("[BDArmory.ModuleRadar]: WARNING: " + part.name + " has legacy definition, missing new radarDetectionCurve and radarLockTrackCurve definitions! Please update for the part to be usable!");
            }

            if (canRecieveRadarData)
            {
                Debug.LogWarning($"[BDArmory.ModuleRadar]: Radar part {part.name} is using deprecated 'canRecieveRadarData' attribute. Please update the config to use 'canReceiveRadarData' instead.");
                canReceiveRadarData = canRecieveRadarData;
            }
        }

        /*
        void OnGoOnRails(Vessel v)
        {
            if (v != vessel) return;
            unlinkOnDestroy = false;
            //myVesselID = vessel.id.ToString();
        }
        */

        IEnumerator StartUpRoutine()
        {
            if (BDArmorySettings.DEBUG_RADAR)
                Debug.Log("[BDArmory.ModuleRadar]: StartupRoutine: " + radarName + " enabled: " + radarEnabled);
            yield return new WaitWhile(() => !FlightGlobals.ready || vessel.packed || !vessel.loaded);
            yield return new WaitForFixedUpdate();

            // DISABLE RADAR
            /*
            if (radarEnabled)
            {
                EnableRadar();
            }
            */

            if (!vesselRadarData.hasLoadedExternalVRDs)
            {
                RecoverLinkedVessels();
                vesselRadarData.hasLoadedExternalVRDs = true;
            }

            UpdateToggleGuiName();
            startupComplete = true;
        }

        void Update()
        {
            drawGUI = (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && !vessel.packed && radarEnabled &&
                       vessel.isActiveVessel && BDArmorySetup.GAME_UI_ENABLED && !MapView.MapIsEnabled);
        }

        public void UpdateReferenceTransform()
        {
            if (ReferenceUpdateTime >= Time.time)
                return;

            if (omnidirectional)
            {
                referenceTransform.position = part.transform.position;
                currPosition = referenceTransform.position;
                referenceTransform.rotation =
                    Quaternion.LookRotation(VectorUtils.GetNorthVector(currPosition, vessel.mainBody),
                        vessel.up);
            }
            else
            {
                referenceTransform.position = part.transform.position;
                currPosition = referenceTransform.position;
                // THIS IMPLEMENTS FULL ROLL STABILIZATION
                // We assume the radar can *always* roll such that the up direction is the projection of
                // the up vector onto the radarTransform up plane.
                referenceTransform.rotation = Quaternion.LookRotation(radarTransform.up,
                    vessel.up.ProjectOnPlanePreNormalized(radarTransform.up).normalized);
            }
            currForward = referenceTransform.forward;
            currUp = referenceTransform.up;
            currRight = referenceTransform.right;

            currDisplayForward = currForward;

            ReferenceUpdateTime = Time.time;
        }

        public void UpdateDisplayTransform()
        {
            if (DisplayUpdateTime >= Time.time)
                return;
            UpdateReferenceTransform();

            if (radarElOffset != 0 || radarAzOffset != 0)
                currDisplayForward = Quaternion.AngleAxis(radarElOffset, currRight) * Quaternion.AngleAxis(-radarAzOffset, currUp) * currForward;
            DisplayUpdateTime = Time.time;
        }

        void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && startupComplete)
            {
                if (!vessel.IsControllable && !isMissileRadar && radarEnabled)
                {
                    DisableRadar();
                }

                if (radarEnabled)
                {
                    UpdateReferenceTransform();

                    DrainElectricity(); //physics behaviour, thus moved here from update

                    if (BDArmorySettings.DEBUG_RADAR)
                    {
                        Debug.Log($"[BDArmory.ModuleRadar] Vessel: {vessel.vesselName}, {(sonarMode == ModuleRadar.SonarModes.None ? "Radar" : "Sonar")}: {name}, beginning lock checks.");
                    }
                    if (locked)
                    {
                        for (int i = lockedTargets.Count - 1; i >= 0; --i) // We need to iterate backwards as UnlockTargetAt (in UpdateLock) can remove items from the lockedTargets list.
                        {
                            UpdateLock(i);
                        }

                        if (canTrackWhileScan)
                        {
                            Scan();
                        }
                    }
                    else if (boresightScan)
                    {
                        BoresightScan();
                    }
                    else if (canScan)
                    {
                        Scan();
                    }
                }
                //if (!vessel.packed && radarEnabled)
                //{
                //    //UpdateInputs();
                //}
            }
        }

        void UpdateSlaveData()
        {
            var weaponManager = WeaponManager;
            if (slaveTurrets && weaponManager)
            {
                weaponManager.slavingTurrets = true;
                if (locked)
                {
                    weaponManager.slavedPosition = lockedTarget.predictedPosition;
                    weaponManager.slavedVelocity = lockedTarget.velocity;
                    weaponManager.slavedAcceleration = lockedTarget.acceleration;
                    weaponManager.slavedTarget = lockedTarget;
                }
            }
        }

        void LateUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight && (canScan || canLock))
            {
                UpdateModel();
            }
        }

        void UpdateModel()
        {
            //model rotation
            if (radarEnabled)
            {
                if (rotationTransform && canScan)
                {
                    Vector3 direction;
                    if (locked)
                    {
                        direction =
                            Quaternion.AngleAxis(canTrackWhileScan ? currentAngle : lockScanAngle, currUp) *
                            currForward;
                    }
                    else
                    {
                        direction = Quaternion.AngleAxis(currentAngle, currUp) * currForward;
                    }

                    Vector3 localDirection = rotationTransform.parent.InverseTransformDirection(direction).ProjectOnPlanePreNormalized(Vector3.up);
                    if (localDirection != Vector3.zero)
                    {
                        rotationTransform.localRotation = Quaternion.Lerp(rotationTransform.localRotation,
                            Quaternion.LookRotation(localDirection, Vector3.up), 10 * TimeWarp.fixedDeltaTime);
                    }
                }

                //lock turret
                if (lockingTurret && canLock)
                {
                    if (locked)
                    {
                        lockingTurret.AimToTarget(lockedTarget.predictedPosition, lockingPitch, lockingYaw);
                    }
                    else
                    {
                        lockingTurret.ReturnTurret();
                    }
                }
            }
            else
            {
                if (rotationTransform)
                {
                    rotationTransform.localRotation = Quaternion.Lerp(rotationTransform.localRotation,
                        Quaternion.identity, 5 * TimeWarp.fixedDeltaTime);
                }

                if (lockingTurret)
                {
                    lockingTurret.ReturnTurret();
                }
            }
        }

        void Scan()
        {
            float angleDelta = scanRotationSpeed * Time.fixedDeltaTime;
            RadarUtils.RadarUpdateScanLock(WeaponManager, currentAngle, radarElOffset, angleDelta, radarElFOV, this, false, ref attemptedLocks);

            if (omnidirectional)
            {
                currentAngle = Mathf.Repeat(currentAngle + angleDelta, 360f);
            }
            else
            {
                currentAngle += radialScanDirection * angleDelta;

                if (locked)
                {
                    // If we're locked, then get the angle to the target
                    float targetAngle = VectorUtils.GetAngleOnPlane(lockedTarget.position - currPosition, currForward, currRight);

                    // And then set the left/right limits based on multiLockFOV, limited by the radarAzLimits
                    leftLimit = Mathf.Clamp(targetAngle - (multiLockFOV * 0.5f), radarAzLimits[0],
                        radarAzLimits[1]);
                    rightLimit = Mathf.Clamp(targetAngle + (multiLockFOV * 0.5f), radarAzLimits[0],
                        radarAzLimits[1]);

                    if (radialScanDirection < 0 && currentAngle < leftLimit)
                    {
                        // If we're past the left limit, set the angle to the left limit and reverse the direction of the scan
                        currentAngle = leftLimit;
                        radialScanDirection = 1;
                    }
                    else if (radialScanDirection > 0 && currentAngle > rightLimit)
                    {
                        // If we're past the right limit, set the angle to the right limit and reverse the direction of the scan
                        currentAngle = rightLimit;
                        radialScanDirection = -1;
                    }
                }
                else
                {
                    // If we're beyond the radar limits
                    if (Mathf.Abs(currentAngle - radarAzOffset) > radarAzFOV * 0.5f)
                    {
                        // Set current angle to either the left/right limit
                        currentAngle = currentAngle < 0f ? radarAzLimits[0] : radarAzLimits[1];
                        // Reverse the scan direction
                        radialScanDirection = -radialScanDirection;
                    }
                }
            }
        }

        public bool TryLockTarget(Vector3 position, Vessel targetVessel = null)
        {
            //need a way to see what companion radars on the craft have already locked, so multiple radars aren't stacking locks on the same couple target craft? Or is updating attemptedLocks to missileFire.maxradarLocks enough?
            if (!canLock)
            {
                return false;
            }

            if (BDArmorySettings.DEBUG_RADAR)
            {
                if (targetVessel == null)
                    Debug.Log("[BDArmory.ModuleRadar]: Trying to radar lock target with (" + radarName + ")");
                else
                    Debug.Log("[BDArmory.ModuleRadar]: Trying to radar lock target " + targetVessel.vesselName + " with (" + radarName + ")");
            }

            var weaponManager = WeaponManager;
            if (currentLocks == maxLocks)
            {
                if (!weaponManager.guardMode || !ClearUnneededLocks())
                {
                    if (BDArmorySettings.DEBUG_RADAR)
                        Debug.Log("[BDArmory.ModuleRadar]: - Failed, this radar already has the maximum allowed targets locked.");
                    return false;
                }
            }

            // -------------------- IMPORTANT NOTE: --------------------
            // Currently ALL instances of `TryLockTarget()` are gated behind functions
            // which perform the `UpdateReferenceTransform()` check beforehand!
            // ANY NEW USES OF `TryLockTarget()` MUST FOLLOW THIS CONVENTION!
            // If this is, for some reason, impossible, then this check may be
            // uncommented, performance impact is minimal as a check is done first
            // to determine if an update is needed based on fixedTime elapsed since
            // the last update.
            //UpdateReferenceTransform();
            // Ensure cache locality
            Vector3 forwardVector = currForward;
            Vector3 rightVector = currRight;

            //Vector3 targetPlanarDirection = (position - referenceTransform.position).ProjectOnPlanePreNormalized(referenceTransform.up);
            //float angle = VectorUtils.Angle(targetPlanarDirection, referenceTransform.forward);

            //if (referenceTransform.InverseTransformPoint(position).x < 0)
            //{
            //    angle = -angle;
            //}

            // Since now we're concerned with azimuth and elevation, may as well use this function
            //VectorUtils.GetAzimuthElevation(position - currPosition, currForward, currUp, out float azimuthAngle, out float elevationAngle);
            Vector3 relativePosition = position - currPosition;
            // Note this would typically be the wrong way around, however because our radar code uses
            // negative angles for the left and positive angles for the right, may as well take advantage
            // of that fact.
            float azimuthAngle = VectorUtils.GetAngleOnPlane(relativePosition, forwardVector, rightVector);
            float elevationAngle = VectorUtils.GetElevation(relativePosition, currUp);

            TargetSignatureData.ResetTSDArray(ref attemptedLocks);
            // Scan in the target direction
            RadarUtils.RadarUpdateScanLock(weaponManager, azimuthAngle, elevationAngle, lockAttemptFOV, lockAttemptFOV, this, true, ref attemptedLocks, signalPersistTime);

            // Check the locks to see if we've detected the target
            for (int i = 0; i < attemptedLocks.Length; i++)
            {
                if (attemptedLocks[i].exists && (attemptedLocks[i].predictedPosition - position).sqrMagnitude < 40.0f * 40.0f) //(lockSuccesses[i] && attemptedLocks[i].exists && (attemptedLocks[i].predictedPosition - position).sqrMagnitude < 40 * 40)
                {
                    // If locked onto a vessel that was not our target, return false
                    if ((attemptedLocks[i].vessel != null) && (targetVessel != null) && (attemptedLocks[i].vessel != targetVessel))
                        return false;

                    if (!locked && !omnidirectional)
                    {
                        // Note this would typically give the opposite of the desired sign, but because radar convention is reversed this is correct.
                        float targetAngle = VectorUtils.GetAngleOnPlane((attemptedLocks[i].position - currPosition), forwardVector, rightVector);
                        currentAngle = targetAngle;
                    }
                    lockedTargets.Add(attemptedLocks[i]);
                    currLocks = lockedTargets.Count;
                    lockedTargetIndex = currLocks - 1; // Set lockedTargetIndex to the last index

                    if (BDArmorySettings.DEBUG_RADAR)
                        Debug.Log($"[BDArmory.ModuleRadar]: - Acquired lock on target ({attemptedLocks[i].Name()}) with UUID {attemptedLocks[i].ID()}");

                    vesselRadarData.AddRadarContact(this, lockedTarget, true);
                    //vesselRadarData.UpdateLockedTargets();
                    if (linkedToVessels.Count > 0)
                        foreach (VesselRadarData vrd in linkedToVessels)
                        {
                            if (vrd)
                            {
                                vrd.AddRadarContact(this, lockedTarget, true, true);
                                //vrd.UpdateLockedTargets();
                            }
                        }
                    return true;
                }
            }

            if (BDArmorySettings.DEBUG_RADAR)
                Debug.Log("[BDArmory.ModuleRadar]: - Failed to lock on target.");

            return false;
        }

        void BoresightScan()
        {
            if (locked)
            {
                boresightScan = false;
                return;
            }

            currentAngle = Mathf.Lerp(currentAngle, 0, 0.08f);
            RadarUtils.RadarUpdateScanBoresight(new Ray(currPosition, currForward), boresightFOV, ref attemptedLocks, Time.fixedDeltaTime, this);

            for (int i = 0; i < attemptedLocks.Length; i++)
            {
                if (!attemptedLocks[i].exists || !(attemptedLocks[i].age < 0.1f)) continue;
                TryLockTarget(attemptedLocks[i].predictedPosition);
                boresightScan = false;
                return;
            }
        }

        /// <summary>
        /// Checks if targetPosition is within the radar's FoV limits
        /// </summary>
        /// <param name="targetPosition">World target position.</param>
        /// <returns>Boolean value, true if the target is within the radar's FoV limits.</returns>
        public bool CheckFOV(Vector3 targetPosition)
        {
            if (omnidirectional)
            {
                // Check elevation only, determine angle from the vertical axis
                return (Mathf.Abs(VectorUtils.GetElevation(targetPosition - currPosition, currUp) - radarElOffset) < 0.5f * radarElFOV);
            }
            else
            {
                // Target exists and omnidirectional, we must check if we're within radar FoV
                //VectorUtils.GetAzimuthElevation(targetPosition - currPosition, currForward, currUp, out float az, out float el);
                Vector3 relativePosition = targetPosition - currPosition;

                // Radar azimuth is reversed, for whatever reason
                float az = VectorUtils.GetAngleOnPlane(relativePosition, currForward, currRight);
                float el = VectorUtils.GetElevation(relativePosition, currUp);

                // Check if we're outside FoV
                return (Mathf.Abs(az - radarAzOffset) < 0.5f * radarAzFOV && Mathf.Abs(el - radarElOffset) < 0.5f * radarElFOV);
            }
        }

        /// <summary>
        /// Checks if the direction vector is within the radar's FoV limits
        /// </summary>
        /// <param name="dir">Target direction relative to radar (unit vector).</param>
        /// <returns>Boolean value, true if the target is within the radar's FoV limits.</returns>
        public bool CheckFOVDir(Vector3 dir)
        {
            if (omnidirectional)
            {
                // Check elevation only, determine angle from the vertical axis
                return (Mathf.Abs(VectorUtils.GetElevationPreNorm(dir, currUp) - radarElOffset) < 0.5f * radarElFOV);
            }
            else
            {
                // Target exists and omnidirectional, we must check if we're within radar FoV
                // Radar azimuth is reversed, for whatever reason
                float az = VectorUtils.GetAngleOnPlane(dir, currForward, currRight);
                float el = VectorUtils.GetElevationPreNorm(dir, currUp);

                // Check if we're outside FoV
                return (Mathf.Abs(az - radarAzOffset) < 0.5f * radarAzFOV && Mathf.Abs(el - radarElOffset) < 0.5f * radarElFOV);
            }
        }

        void UpdateLock(int index)
        {
            TargetSignatureData lockedTarget = lockedTargets[index];

            Vector3 targetPlanarDirection = (lockedTarget.predictedPosition - currPosition).ProjectOnPlanePreNormalized(currUp);
            float lookAngle = VectorUtils.Angle(targetPlanarDirection, currForward);
            if (referenceTransform.InverseTransformPoint(lockedTarget.predictedPosition).x < 0)
            {
                lookAngle = -lookAngle;
            }

            if (omnidirectional)
            {
                if (lookAngle < 0) lookAngle += 360;
            }

            lockScanAngle = lookAngle + currentAngleLock;
            if (!canTrackWhileScan && index == lockedTargetIndex)
            {
                currentAngle = lockScanAngle;
            }
            float angleDelta = lockRotationSpeed * Time.fixedDeltaTime;
            float lockedSignalPersist = lockRotationAngle / lockRotationSpeed;
            //RadarUtils.ScanInDirection(lockScanAngle, referenceTransform, angleDelta, referenceTransform.position, minLockedSignalThreshold, ref attemptedLocks, lockedSignalPersist);
            bool radarSnapshot = (snapshotTicker > 30);
            if (radarSnapshot)
            {
                snapshotTicker = 0;
            }
            else
            {
                snapshotTicker++;
            }
            //RadarUtils.ScanInDirection (new Ray (referenceTransform.position, lockedTarget.predictedPosition - referenceTransform.position), lockRotationAngle * 2, minLockedSignalThreshold, ref attemptedLocks, lockedSignalPersist, true, rwrType, radarSnapshot);

            Vector3 vectorToTarget = lockedTarget.position - currPosition;
            if (VectorUtils.Angle(vectorToTarget, this.lockedTarget.position - currPosition) > multiLockFOV * 0.5f)
            {
                if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.ModuleRadar] Target: {lockedTarget.Name()} with UUID {lockedTarget.ID()} at index: {index} unlocked due to FoV!");
                UnlockTargetAt(index, true);
                return;
            }

            if (!RadarUtils.RadarUpdateLockTrack(
                new Ray(currPosition, lockedTarget.predictedPosition - currPosition),
                lockedTarget.predictedPosition, lockRotationAngle * 2, this, lockedSignalPersist, true, index, lockedTarget.vessel))
            {
                if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.ModuleRadar] Target: {lockedTarget.Name()} with UUID {lockedTarget.ID()} at index: {index} unlocked due to failed lock checks!");
                UnlockTargetAt(index, true);
                return;
            }

            //if still failed or out of FOV, unlock.
            // MOVED FOV CHECK TO RadarUpdateLockTrack!
            if (!lockedTarget.exists)
            {
                if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.ModuleRadar] Target: null at index: {index} unlocked as it does not exist!");
                //UnlockAllTargets();
                UnlockTargetAt(index, true);
                return;
            }

            //unlock if over-jammed
            // MOVED TO RADARUTILS!

            //cycle scan direction
            if (index == lockedTargetIndex)
            {
                currentAngleLock += lockScanDirection * angleDelta;
                if (Mathf.Abs(currentAngleLock) > lockRotationAngle / 2)
                {
                    currentAngleLock = Mathf.Sign(currentAngleLock) * lockRotationAngle / 2;
                    lockScanDirection = -lockScanDirection;
                }
            }
        }

        public void UnlockAllTargets()
        {
            if (!locked) return;

            lockedTargets.Clear();
            currLocks = 0;
            lockedTargetIndex = 0;

            if (vesselRadarData)
            {
                vesselRadarData.UnlockAllTargetsOfRadar(this);
            }

            if (linkedToVessels.Count > 0)
                foreach (VesselRadarData vrd in linkedToVessels)
                {
                    if (vrd)
                        vrd.UnlockAllTargetsOfRadar(this);
                }

            if (BDArmorySettings.DEBUG_RADAR)
                Debug.Log("[BDArmory.ModuleRadar]: Radar Targets were cleared (" + radarName + ").");
        }

        public void SetActiveLock(TargetSignatureData target)
        {
            for (int i = 0; i < lockedTargets.Count; i++)
            {
                if (target.vessel == lockedTargets[i].vessel)
                {
                    lockedTargetIndex = i;
                    return;
                }
            }
        }

        public void UnlockTargetAt(int index, bool tryRelock = false)
        {
            if (index < 0 || index >= lockedTargets.Count)
            {
                if (BDArmorySettings.DEBUG_RADAR) Debug.Log($"[BDArmory.ModuleRadar]: invalid index {index} for lockedTargets of size {lockedTargets.Count}");
                return;
            }
            Vessel rVess = lockedTargets[index].vessel;

            if (rVess == null)
                tryRelock = false;

            if (tryRelock)
            {
                UnlockTargetAt(index, false);
                if (rVess)
                {
                    StartCoroutine(RetryLockRoutine(rVess));
                }
                return;
            }

            lockedTargets.RemoveAt(index);
            currLocks = lockedTargets.Count;
            if (lockedTargetIndex > index)
            {
                lockedTargetIndex--;
            }

            lockedTargetIndex = Mathf.Clamp(lockedTargetIndex, 0, currLocks - 1);
            lockedTargetIndex = Mathf.Max(lockedTargetIndex, 0);

            if (vesselRadarData)
            {
                //vesselRadarData.UnlockTargetAtPosition(position);
                vesselRadarData.RemoveVesselFromLockedTargets(rVess);
            }
            if (linkedToVessels.Count > 0)
                foreach (VesselRadarData vrd in linkedToVessels)
                {
                    if (vrd)
                        vrd.RemoveVesselFromLockedTargets(rVess);
                }
        }

        IEnumerator RetryLockRoutine(Vessel v)
        {
            yield return new WaitForFixedUpdate();
            if (vesselRadarData != null && vesselRadarData.isActiveAndEnabled)
                vesselRadarData.TryLockTarget(v);
        }

        public void UnlockTargetVessel(Vessel v)
        {
            for (int i = 0; i < lockedTargets.Count; i++)
            {
                if (lockedTargets[i].vessel == v)
                {
                    UnlockTargetAt(i);
                    return;
                }
            }
        }

        public bool ClearUnneededLocks(bool unlockAll = false)
        {
            if (!unlockAll && (currentLocks < maxLocks))
                return true;

            bool cleared = false;
            var weaponManager = WeaponManager;
            for (int i = 0; i < lockedTargets.Count; i++)
            {
                if (weaponManager.GetMissilesAway(lockedTargets[i].targetInfo).numSARH == 0)
                {
                    UnlockTargetAt(i);
                    i--;
                    cleared = true;
                    if (!unlockAll) break;
                }
            }

            return cleared;
        }

        public void RefreshLockArray()
        {
            if (wpmr != null)
            {
                attemptedLocks = new TargetSignatureData[wpmr.MaxRadarLocks];
                TargetSignatureData.ResetTSDArray(ref attemptedLocks);
                //lockSuccesses = new bool[wpmr.MaxRadarLocks];
            }
        }

        void SlaveTurrets()
        {
            using (var mtc = VesselModuleRegistry.GetModules<ModuleTargetingCamera>(vessel).GetEnumerator())
                while (mtc.MoveNext())
                {
                    if (mtc.Current == null) continue;
                    mtc.Current.slaveTurrets = false;
                }

            using (var rad = VesselModuleRegistry.GetModules<ModuleRadar>(vessel).GetEnumerator())
                while (rad.MoveNext())
                {
                    if (rad.Current == null) continue;
                    rad.Current.slaveTurrets = false;
                }

            slaveTurrets = true;
        }

        void UnslaveTurrets()
        {
            using (var mtc = VesselModuleRegistry.GetModules<ModuleTargetingCamera>(vessel).GetEnumerator())
                while (mtc.MoveNext())
                {
                    if (mtc.Current == null) continue;
                    mtc.Current.slaveTurrets = false;
                }

            using (var rad = VesselModuleRegistry.GetModules<ModuleRadar>(vessel).GetEnumerator())
                while (rad.MoveNext())
                {
                    if (rad.Current == null) continue;
                    rad.Current.slaveTurrets = false;
                }

            var weaponManager = WeaponManager;
            if (weaponManager)
            {
                weaponManager.slavingTurrets = false;
            }

            slaveTurrets = false;
        }

        public void UpdateLockedTargetInfo(TargetSignatureData newData)
        {
            int index = -1;
            for (int i = 0; i < lockedTargets.Count; i++)
            {
                if (lockedTargets[i].vessel != newData.vessel) continue;
                index = i;
                break;
            }

            if (index >= 0)
            {
                lockedTargets[index] = newData;
            }
        }

        public void ReceiveContactData(TargetSignatureData contactData, bool _locked)
        {
            if (vesselRadarData)
            {
                vesselRadarData.AddRadarContact(this, contactData, _locked);
            }

            List<VesselRadarData>.Enumerator vrd = linkedToVessels.GetEnumerator();
            while (vrd.MoveNext())
            {
                if (vrd.Current == null) continue;
                if (vrd.Current.canReceiveRadarData && vrd.Current.vessel != contactData.vessel)
                {
                    vrd.Current.AddRadarContact(this, contactData, _locked, true);
                }
            }
            vrd.Dispose();
        }

        public void AddExternalVRD(VesselRadarData vrd)
        {
            if (!linkedToVessels.Contains(vrd))
            {
                linkedToVessels.Add(vrd);
            }
        }

        public void RemoveExternalVRD(VesselRadarData vrd)
        {
            linkedToVessels.Remove(vrd);
        }

        void OnGUI()
        {
            if (drawGUI)
            {
                if (boresightScan)
                {
                    GUIUtils.DrawTextureOnWorldPos(transform.position + (3500 * transform.up),
                        BDArmorySetup.Instance.dottedLargeGreenCircle, new Vector2(156, 156), 0);
                }
            }
        }

        public void RecoverLinkedVessels()
        {
            string[] vesselIDs = linkedVesselID.Split(new char[] { ',' });
            for (int i = 0; i < vesselIDs.Length; i++)
            {
                StartCoroutine(RecoverLinkedVesselRoutine(vesselIDs[i]));
            }
        }

        IEnumerator RecoverLinkedVesselRoutine(string vesselID)
        {
            while (true)
            {
                using (var v = BDATargetManager.LoadedVessels.GetEnumerator())
                    while (v.MoveNext())
                    {
                        if (v.Current == null || !v.Current.loaded || v.Current == vessel || VesselModuleRegistry.IgnoredVesselTypes.Contains(v.Current.vesselType)) continue;
                        if (v.Current.id.ToString() != vesselID) continue;
                        VesselRadarData vrd = v.Current.gameObject.GetComponent<VesselRadarData>();
                        if (!vrd) continue;
                        StartCoroutine(RelinkVRDWhenReadyRoutine(vrd));
                        yield break;
                    }

                yield return new WaitForSecondsFixed(0.5f);
            }
        }

        IEnumerator RelinkVRDWhenReadyRoutine(VesselRadarData vrd)
        {
            yield return new WaitWhile(() => !vrd.radarsReady || (vrd.vessel is not null && (vrd.vessel.packed || !vrd.vessel.loaded)));
            yield return new WaitForFixedUpdate();
            if (vrd.vessel is null) yield break;
            vesselRadarData.LinkVRD(vrd);
            if (BDArmorySettings.DEBUG_RADAR) Debug.Log("[BDArmory.ModuleRadar]: Radar data link recovered: Local - " + vessel.vesselName + ", External - " + vrd.vessel.vesselName);
        }

        public string getRWRType(int i)
        {
            switch (i)
            {
                case 0:
                    return StringUtils.Localize("#autoLOC_bda_1000002");		// #autoLOC_bda_1000002 = SAM

                case 1:
                    return StringUtils.Localize("#autoLOC_bda_1000003");		// #autoLOC_bda_1000003 = FIGHTER

                case 2:
                    return StringUtils.Localize("#autoLOC_bda_1000004");		// #autoLOC_bda_1000004 = AWACS

                case 3:
                case 4:
                    return StringUtils.Localize("#autoLOC_bda_1000005");		// #autoLOC_bda_1000005 = MISSILE

                case 5:
                    return StringUtils.Localize("#autoLOC_bda_1000006");		// #autoLOC_bda_1000006 = DETECTION

                case 6:
                    return StringUtils.Localize("#autoLOC_bda_1000017");		// #autoLOC_bda_1000017 = SONAR
            }
            return StringUtils.Localize("#autoLOC_bda_1000007");		// #autoLOC_bda_1000007 = UNKNOWN
            //{SAM = 0, Fighter = 1, AWACS = 2, MissileLaunch = 3, MissileLock = 4, Detection = 5, Sonar = 6}
        }

        // RMB info in editor
        public override string GetInfo()
        {
            bool isLinkOnly = (canReceiveRadarData && !canScan && !canLock);

            StringBuilder output = new StringBuilder();
            output.Append(Environment.NewLine);
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000008", (isLinkOnly ? StringUtils.Localize("#autoLOC_bda_1000018") : omnidirectional ? StringUtils.Localize("#autoLOC_bda_1000019") : StringUtils.Localize("#autoLOC_bda_1000020"))));

            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000021", resourceDrain));
            if (!isLinkOnly)
            {
                // For some reason just doing this in OnStart(), even outside of the Flight scene check wasn't working...
                SetRadarLimits();

                if (!omnidirectional)
                    output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000022", radarAzLimits[0], radarAzLimits[1]));
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000041", radarElLimits[0], radarElLimits[1]));
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000023", getRWRType(rwrThreatType)));

                output.Append(Environment.NewLine);
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000024"));
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000025", canScan));
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000026", canTrackWhileScan));
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000027", canLock));
                if (canLock)
                {
                    output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000028", maxLocks));
                }
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000029", canReceiveRadarData));

                output.Append(Environment.NewLine);
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000030"));

                if (canScan)
                    output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000031", radarDetectionCurve.Evaluate(radarMaxDistanceDetect), radarMaxDistanceDetect));
                else
                    output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000032"));
                if (canLock)
                    output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000033", radarLockTrackCurve.Evaluate(radarMaxDistanceLockTrack), radarMaxDistanceLockTrack));
                else
                    output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000034"));

                if (sonarType == 1)
                    output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000039"));
                if (sonarType == 2)
                    output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000040"));
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000035", radarGroundClutterFactor));
            }

            return output.ToString();
        }

        void DrainElectricity()
        {
            if (resourceDrain <= 0)
            {
                return;
            }

            double drainAmount = resourceDrain * TimeWarp.fixedDeltaTime;
            double chargeAvailable = part.RequestResource(resourceID, drainAmount, ResourceFlowMode.ALL_VESSEL);
            if (chargeAvailable < drainAmount * 0.95f)
            {
                ScreenMessages.PostScreenMessage($"{part.partInfo.title} {StringUtils.Localize("#autoLOC_244332")} {PartResourceLibrary.Instance.GetDefinition(resourceName).displayName}", 5.0f, ScreenMessageStyle.UPPER_CENTER);		// [part Title] Requires [localized resource name]
                if (ml != null)
                    ml.Detonate(); //sonobuoy's dry, remove it
                DisableRadar();
            }
        }
    }

}
