using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using KSP.Localization;

using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.WeaponMounts;

namespace BDArmory.Radar
{
    public class ModuleRadar : PartModule
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
        public bool omnidirectional = true;			//false=boresight only

        [KSPField]
        public float directionalFieldOfView = 90;	//relevant for omnidirectional only

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
        public float radarGroundClutterFactor = 0.25f; //Factor defining how effective the radar is for look-down, compensating for ground clutter (0=ineffective, 1=fully effective)
                                                       //default to 0.25, so all cross sections of landed/splashed/submerged vessels are reduced to 1/4th, as these vessel usually a quite large
        [KSPField]
        public float radarChaffClutterFactor = 1.0f;     //Factor defining how effective the radar is at compensating for enemy chaff (0 = ineffective, 1 = no decrease in signal position/strength)
                                                         //default to 1, since that's legacy behavior. Relevant for guiding SARH ordinance.
        [KSPField]
        public int sonarType = 0; //0 = Radar; 1 == Active Sonar; 2 == Passive Sonar

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_DynamicRadar", advancedTweakable = true),//Disable Radar vs ARMs
            UI_Toggle(enabledText = "#LOC_BDArmory_true", disabledText = "#LOC_BDArmory_false", scene = UI_Scene.All),]//Starboard (CW)--Port (CCW)
        public bool DynamicRadar = true;

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
        public bool lockingPitch = true;
        public bool lockingYaw = true;

        //vessel
        private MissileFire wpmr;

        public MissileFire weaponManager
        {
            get
            {
                if (wpmr != null && wpmr.vessel == vessel) return wpmr;
                wpmr = VesselModuleRegistry.GetMissileFire(vessel, true);
                return wpmr;
            }
            set { wpmr = value; }
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
            Events["Toggle"].guiName = radarEnabled ? StringUtils.Localize("#autoLOC_bda_1000000") : StringUtils.Localize("#autoLOC_bda_1000001");		// #autoLOC_bda_1000000 = Disable Radar		// #autoLOC_bda_1000001 = Enable Radar
        }
        void Start()
        {
            resourceID = PartResourceLibrary.Instance.GetDefinition(resourceName).id;
        }

        public void EnsureVesselRadarData()
        {
            if (vessel == null) return;
            //myVesselID = vessel.id.ToString();

            if (vesselRadarData != null && vesselRadarData.vessel == vessel) return;
            vesselRadarData = vessel.gameObject.GetComponent<VesselRadarData>();

            if (vesselRadarData == null)
            {
                vesselRadarData = vessel.gameObject.AddComponent<VesselRadarData>();
                vesselRadarData.weaponManager = weaponManager;
            }
        }

        public void EnableRadar()
        {
            EnsureVesselRadarData();
            radarEnabled = true;

            var mf = VesselModuleRegistry.GetMissileFire(vessel, true);
            if (mf != null && vesselRadarData != null) vesselRadarData.weaponManager = mf;
            UpdateToggleGuiName();
            vesselRadarData.AddRadar(this);
            if (mf != null)
            {
                if (mf.guardMode) vesselRadarData.LinkAllRadars();
                if (sonarMode == SonarModes.None)
                    mf._radarsEnabled = true;
                else if (sonarMode == SonarModes.Active)
                    mf._sonarsEnabled = true;
            }
        }

        public void DisableRadar()
        {
            if (locked)
            {
                UnlockAllTargets();
            }

            radarEnabled = false;
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
            using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedvessels.MoveNext())
                {
                    BDATargetManager.ClearRadarReport(loadedvessels.Current, weaponManager); //reset radar contact status
                }
            var mf = VesselModuleRegistry.GetMissileFire(vessel, true);
            if (mf != null)
            {
                if (mf.radars.Count > 1)
                {
                    bool detectorsEnabled = false;
                    using (List<ModuleRadar>.Enumerator rd = mf.radars.GetEnumerator())
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
                        mf._radarsEnabled = detectorsEnabled;
                    else if (sonarMode == SonarModes.Active)
                        mf._sonarsEnabled = detectorsEnabled;
                }
                else
                {
                    if (sonarMode == SonarModes.None)
                        mf._radarsEnabled = false;
                    else if (sonarMode == SonarModes.Active)
                        mf._sonarsEnabled = false;
                }
            }
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

                signalPersistTime = omnidirectional
                    ? 360 / (scanRotationSpeed + 5)
                    : directionalFieldOfView / (scanRotationSpeed + 5);

                rwrType = (RadarWarningReceiver.RWRThreatTypes)rwrThreatType;
                sonarMode = (SonarModes)sonarType;
                if (rwrType == RadarWarningReceiver.RWRThreatTypes.Sonar)
                    signalPersistTimeForRwr = RadarUtils.ACTIVE_MISSILE_PING_PERISTS_TIME;
                else
                {
                    signalPersistTimeForRwr = signalPersistTime / 2;
                }

                if (rotationTransformName != string.Empty)
                {
                    rotationTransform = part.FindModelTransform(rotationTransformName);
                }
                radarTransform = radarTransformName != string.Empty ? part.FindModelTransform(radarTransformName) : part.transform;

                attemptedLocks = new TargetSignatureData[maxLocks];
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
                    lockingTurret.Fields["minPitch"].guiActiveEditor = false;
                    lockingTurret.Fields["maxPitch"].guiActiveEditor = false;
                    lockingTurret.Fields["yawRange"].guiActiveEditor = false;
                }
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

        void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && startupComplete)
            {
                if (!vessel.IsControllable && radarEnabled)
                {
                    DisableRadar();
                }

                if (radarEnabled)
                {
                    DrainElectricity(); //physics behaviour, thus moved here from update

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
                if (!vessel.packed && radarEnabled)
                {
                    if (omnidirectional)
                    {
                        referenceTransform.position = part.transform.position;
                        referenceTransform.rotation =
                            Quaternion.LookRotation(VectorUtils.GetNorthVector(radarTransform.position, vessel.mainBody),
                                VectorUtils.GetUpDirection(transform.position));
                    }
                    else
                    {
                        referenceTransform.position = part.transform.position;
                        referenceTransform.rotation = Quaternion.LookRotation(radarTransform.up,
                            VectorUtils.GetUpDirection(referenceTransform.position));
                    }
                    //UpdateInputs();
                }
            }
        }

        void UpdateSlaveData()
        {
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
                            Quaternion.AngleAxis(canTrackWhileScan ? currentAngle : lockScanAngle, referenceTransform.up) *
                            referenceTransform.forward;
                    }
                    else
                    {
                        direction = Quaternion.AngleAxis(currentAngle, referenceTransform.up) * referenceTransform.forward;
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
            RadarUtils.RadarUpdateScanLock(weaponManager, currentAngle, referenceTransform, angleDelta, referenceTransform.position, this, false, ref attemptedLocks);

            if (omnidirectional)
            {
                currentAngle = Mathf.Repeat(currentAngle + angleDelta, 360);
            }
            else
            {
                currentAngle += radialScanDirection * angleDelta;

                if (locked)
                {
                    float targetAngle = VectorUtils.SignedAngle(referenceTransform.forward, (lockedTarget.position - referenceTransform.position).ProjectOnPlanePreNormalized(referenceTransform.up), referenceTransform.right);
                    leftLimit = Mathf.Clamp(targetAngle - (multiLockFOV / 2), -directionalFieldOfView / 2,
                        directionalFieldOfView / 2);
                    rightLimit = Mathf.Clamp(targetAngle + (multiLockFOV / 2), -directionalFieldOfView / 2,
                        directionalFieldOfView / 2);

                    if (radialScanDirection < 0 && currentAngle < leftLimit)
                    {
                        currentAngle = leftLimit;
                        radialScanDirection = 1;
                    }
                    else if (radialScanDirection > 0 && currentAngle > rightLimit)
                    {
                        currentAngle = rightLimit;
                        radialScanDirection = -1;
                    }
                }
                else
                {
                    if (Mathf.Abs(currentAngle) > directionalFieldOfView / 2)
                    {
                        currentAngle = Mathf.Sign(currentAngle) * directionalFieldOfView / 2;
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

            if (currentLocks == maxLocks)
            {
                if (BDArmorySettings.DEBUG_RADAR)
                    Debug.Log("[BDArmory.ModuleRadar]: - Failed, this radar already has the maximum allowed targets locked.");
                return false;
            }

            Vector3 targetPlanarDirection = (position - referenceTransform.position).ProjectOnPlanePreNormalized(referenceTransform.up);
            float angle = Vector3.Angle(targetPlanarDirection, referenceTransform.forward);
            if (referenceTransform.InverseTransformPoint(position).x < 0)
            {
                angle = -angle;
            }
            TargetSignatureData.ResetTSDArray(ref attemptedLocks);
            RadarUtils.RadarUpdateScanLock(weaponManager, angle, referenceTransform, lockAttemptFOV, referenceTransform.position, this, true, ref attemptedLocks, signalPersistTime);

            for (int i = 0; i < attemptedLocks.Length; i++)
            {
                if (attemptedLocks[i].exists && (attemptedLocks[i].predictedPosition - position).sqrMagnitude < 40 * 40) //(lockSuccesses[i] && attemptedLocks[i].exists && (attemptedLocks[i].predictedPosition - position).sqrMagnitude < 40 * 40)
                {
                    // If locked onto a vessel that was not our target, return false
                    if ((attemptedLocks[i].vessel != null) && (targetVessel != null) && (attemptedLocks[i].vessel != targetVessel))
                        return false;

                    if (!locked && !omnidirectional)
                    {
                        float targetAngle = VectorUtils.SignedAngle(referenceTransform.forward, (attemptedLocks[i].position - referenceTransform.position).ProjectOnPlanePreNormalized(referenceTransform.up), referenceTransform.right);
                        currentAngle = targetAngle;
                    }
                    lockedTargets.Add(attemptedLocks[i]);
                    currLocks = lockedTargets.Count;

                    if (BDArmorySettings.DEBUG_RADAR)
                        Debug.Log("[BDArmory.ModuleRadar]: - Acquired lock on target (" + (attemptedLocks[i].vessel != null ? attemptedLocks[i].vessel.name : null) + ")");

                    vesselRadarData.AddRadarContact(this, lockedTarget, true);
                    vesselRadarData.UpdateLockedTargets();
                    attemptedLocks[i] = TargetSignatureData.noTarget;
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
            RadarUtils.RadarUpdateScanBoresight(new Ray(transform.position, transform.up), boresightFOV, ref attemptedLocks, Time.fixedDeltaTime, this);

            for (int i = 0; i < attemptedLocks.Length; i++)
            {
                if (!attemptedLocks[i].exists || !(attemptedLocks[i].age < 0.1f)) continue;
                TryLockTarget(attemptedLocks[i].predictedPosition);
                boresightScan = false;
                return;
            }
        }

        void UpdateLock(int index)
        {
            TargetSignatureData lockedTarget = lockedTargets[index];

            Vector3 targetPlanarDirection = (lockedTarget.predictedPosition - referenceTransform.position).ProjectOnPlanePreNormalized(referenceTransform.up);
            float lookAngle = Vector3.Angle(targetPlanarDirection, referenceTransform.forward);
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

            if (Vector3.Angle(lockedTarget.position - referenceTransform.position, this.lockedTarget.position - referenceTransform.position) > multiLockFOV / 2)
            {
                UnlockTargetAt(index, true);
                return;
            }

            if (!RadarUtils.RadarUpdateLockTrack(
                new Ray(referenceTransform.position, lockedTarget.predictedPosition - referenceTransform.position),
                lockedTarget.predictedPosition, lockRotationAngle * 2, this, lockedSignalPersist, true, index, lockedTarget.vessel))
            {
                UnlockTargetAt(index, true);
                return;
            }

            //if still failed or out of FOV, unlock.
            if (!lockedTarget.exists || (!omnidirectional && Vector3.Angle(lockedTarget.position - referenceTransform.position, transform.up) > directionalFieldOfView / 2))
            {
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
                vesselRadarData.RemoveVesselFromTargets(rVess);
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

        public void RefreshLockArray()
        {
            if (wpmr != null)
            {
                attemptedLocks = new TargetSignatureData[wpmr.MaxradarLocks];
                TargetSignatureData.ResetTSDArray(ref attemptedLocks);
                //lockSuccesses = new bool[wpmr.MaxradarLocks];
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
                        if (v.Current == null || !v.Current.loaded || v.Current == vessel || VesselModuleRegistry.ignoredVesselTypes.Contains(v.Current.vesselType)) continue;
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
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000022", directionalFieldOfView));
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
                DisableRadar();
            }
        }
    }

}
