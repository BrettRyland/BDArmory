using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.WeaponMounts;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BDArmory.Radar
{
    public class ModuleRadar : ModuleRadarSensorBase
    {
        #region KSPFields (Part Configuration)

        #region General Configuration

        [KSPField]
        private string radarName = null;

        [KSPField]
        public int turretID = 0;

        #endregion General Configuration

        #region Radar Capabilities

        [KSPField]
        protected bool canLock = true;				//radar has locking/tracking capabilities

        public override bool CanLock
        {
            get
            {
                return canLock;
            }
        }

        [KSPField]
        public bool canReceiveRadarData = false;    //can radar data be received from friendly sources?

        [KSPField] // DEPRECATED
        public bool canRecieveRadarData = false;    // Original mis-spelling of "receive" for compatibility.

        [KSPField]
        public FloatCurve radarLockTrackCurve = new FloatCurve();		//FloatCurve defining at what range which RCS size can be locked/tracked

        [KSPField]
        public float radarMinTrackSCR = 1f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_DynamicRadar", advancedTweakable = true),//Disable Radar vs ARMs
            UI_Toggle(enabledText = "#LOC_BDArmory_true", disabledText = "#LOC_BDArmory_false", scene = UI_Scene.All),]//Starboard (CW)--Port (CCW)
        public bool DynamicRadar = false;

        public enum SonarModes
        {
            None = 0,
            Active = 1,
            passive = 2
        }

        #endregion Radar Capabilities

        #region Persisted State in flight

        [Obsolete]
        [KSPField(isPersistant = true)]
        public bool radarEnabled;

        [KSPField(isPersistant = true)]
        public string linkedVesselID;

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
            if (sensorEnabled)
            {
                DisableSensor();
            }
            else
            {
                EnableSensor();
            }
        }

        [KSPEvent(active = true, guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_ToggleRadar")]//Toggle Radar
        public void Toggle()
        {
            if (sensorEnabled)
            {
                DisableSensor();
            }
            else
            {
                EnableSensor();
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

        [KSPEvent(active = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_ToggleRadarDeploy")]//Toggle Radar Deploy
        public void ToggleDeploy()
        {
            if (isDeployed())
            {
                if (sensorEnabled)
                {
                    DisableSensor();
                }
                Deploy(false);
            }
            else
            {
                Deploy(true);
            }
        }

        #endregion KSP Events & Actions

        #region Part members

        public float radarMinDistanceLockTrack
        {
            get { return radarLockTrackCurve.minTime; }
        }

        //[KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Locking Range")]
        public float radarMaxDistanceLockTrack
        {
            get { return radarLockTrackCurve.maxTime; }
        }

        //GUI
        private bool drawGUI;

        //scanning
        private float lockScanDirection = 1;

        public bool boresightScan;

        //locking
        public bool slaveTurrets;
        public ModuleTurret lockingTurret;

        // lockingPitch and lockingYaw are toggles for whether or not the radar should be able to control the lockingTurret's pitch/yaw
        // this is associated with MissileTurret and ModuleWeapon turrets, if the radar is mounted on a turret.
        public bool lockingPitch = true;
        public bool lockingYaw = true;

        //vessel
        public override MissileFire WeaponManager
        {
            get
            {
                if (field == null || !field.IsPrimaryWM || field.vessel != vessel)
                    field = vessel && vessel.loaded ? vessel.ActiveController().WM : null;
                return field;
            }
            protected set;
        }

        #endregion Part members

        void UpdateToggleGuiName()
        {
            Events[nameof(Toggle)].guiName = sensorEnabled ? StringUtils.Localize("#autoLOC_bda_1000000") : StringUtils.Localize("#autoLOC_bda_1000001");		// #autoLOC_bda_1000000 = Disable Radar		// #autoLOC_bda_1000001 = Enable Radar
        }

        protected override void AddSensorToVRD()
        {
            if (vesselRadarData == null) return;
            vesselRadarData.AddRadar(this);
        }

        protected override void RemoveSensorFromVRD()
        {
            if (vesselRadarData == null) return;
            MissileFire weaponManager = vesselRadarData.weaponManager;
            vesselRadarData.RemoveRadar(this);
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
                            if (rd.Current != this && rd.Current.sensorEnabled)
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
        public override void EnableSensor()
        {
            base.EnableSensor();
            StartCoroutine(PostAnimSetup()); //wait until radar is actually deployed to activate

        }
        IEnumerator PostAnimSetup()
        {
            WaitForFixedUpdate wait = new WaitForFixedUpdate();
            while (!sensorEnabled) yield return wait;

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


        public override void DisableSensor()
        {
            if (locked)
            {
                UnlockAllTargets();
            }
            UpdateToggleGuiName();

            var weaponManager = WeaponManager;
            using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedvessels.MoveNext())
                {
                    BDATargetManager.ClearRadarReport(loadedvessels.Current, weaponManager); //reset radar contact status
                }

            // Needs to be called LAST because it removes the sensor from VRD
            base.DisableSensor();
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

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            if (!string.IsNullOrEmpty(radarName))
            {
                sensorName = radarName;
            }

#pragma warning disable 0612 // Disable obsolete warning for this valid use.
            if (radarEnabled)
            {
                sensorEnabled = true;
                radarEnabled = false;
            }
#pragma warning restore 0612

            if (HighLogic.LoadedSceneIsFlight)
            {
                FlightSetup(radarTransformName);

                // linkedToVessels only needs to be created for ModuleRadars as they're independent sensors
                linkedToVessels = new List<VesselRadarData>();

                attemptedLocks = new TargetSignatureData[Math.Max(maxLocks, 6)];
                //lockSuccesses = new bool[maxLocks];
                TargetSignatureData.ResetTSDArray(ref attemptedLocks);
                lockedTargets = new List<TargetSignatureData>();

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

            if (hasDeployAnimation)
            {
                Events[nameof(ToggleDeploy)].guiActive = true;
                Events[nameof(ToggleDeploy)].guiActiveEditor = true;
            }

            // check for not updated legacy part:
            if ((canScan && (radarMinDistanceDetect == float.MaxValue)) || (canLock && (radarMinDistanceLockTrack == float.MaxValue)))
            {
                Debug.Log($"[BDArmory.ModuleRadar]: WARNING: {part.name} has legacy definition, missing new radarDetectionCurve and radarLockTrackCurve definitions! Please update for the part to be usable!");
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

        protected override void StartupRoutineActions()
        {
            if (!vesselRadarData.hasLoadedExternalVRDs)
            {
                RecoverLinkedVessels();
                vesselRadarData.hasLoadedExternalVRDs = true;
            }

            UpdateToggleGuiName();
        }

        void Update()
        {
            drawGUI = (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && !vessel.packed && sensorEnabled &&
                       vessel.isActiveVessel && BDArmorySetup.GAME_UI_ENABLED && !MapView.MapIsEnabled);
        }

        protected override void EnabledUpdate()
        {
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

        protected override void EnabledModelUpdate()
        {
            base.EnabledModelUpdate();

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

        protected override void DisabledModelUpdate()
        {
            base.DisabledModelUpdate();

            if (lockingTurret)
            {
                lockingTurret.ReturnTurret();
            }
        }

        protected override void PerformScan(float angleDelta)
        {
            RadarUtils.RadarUpdateScanLock(WeaponManager, currentAngle, sensorElOffset, angleDelta, sensorElFOV, this, false, ref attemptedLocks);
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
                    Debug.Log("[BDArmory.ModuleRadar]: Trying to radar lock target with (" + sensorName + ")");
                else
                    Debug.Log("[BDArmory.ModuleRadar]: Trying to radar lock target " + targetVessel.vesselName + " with (" + sensorName + ")");
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
                Debug.Log("[BDArmory.ModuleRadar]: Radar Targets were cleared (" + sensorName + ").");
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
            if (WeaponManager != null)
            {
                attemptedLocks = new TargetSignatureData[WeaponManager.MaxRadarLocks];
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

        public override void ReceiveContactData(TargetSignatureData contactData, bool _locked)
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

        public void RecoverLinkedVessels()
        {
            string[] vesselIDs = linkedVesselID.Split(new char[] { ',' });
            for (int i = 0; i < vesselIDs.Length; i++)
            {
                if (String.IsNullOrEmpty(vesselIDs[i])) continue;
                if (BDArmorySettings.DEBUG_RADAR)
                    Debug.Log($"[BDArmory.ModuleRadar.RecoverLinkedVessels] Parsing ID: {vesselIDs[i]}");

                try
                {
                    Guid vesselID = Guid.Parse(vesselIDs[i]);
                }
                catch
                {
                    Debug.Log($"[BDArmory.ModuleRadar.RecoverLinkedVessels] Parse error!");
                    continue;
                }

                StartCoroutine(RecoverLinkedVesselRoutine(Guid.Parse(vesselIDs[i])));
            }
        }

        protected IEnumerator RecoverLinkedVesselRoutine(Guid vesselID)
        {
            while (true)
            {
                using (var v = BDATargetManager.LoadedVessels.GetEnumerator())
                    while (v.MoveNext())
                    {
                        if (v.Current == null || !v.Current.loaded || v.Current == vessel || VesselModuleRegistry.IgnoredVesselTypes.Contains(v.Current.vesselType)) continue;
                        if (v.Current.id != vesselID) continue;
                        VesselRadarData vrd = v.Current.gameObject.GetComponent<VesselRadarData>();
                        if (!vrd) continue;
                        StartCoroutine(RelinkVRDWhenReadyRoutine(vrd));
                        yield break;
                    }

                yield return new WaitForSecondsFixed(0.5f);
            }
        }

        protected IEnumerator RelinkVRDWhenReadyRoutine(VesselRadarData vrd)
        {
            yield return new WaitWhile(() => !vrd.radarsReady || (vrd.vessel is not null && (vrd.vessel.packed || !vrd.vessel.loaded)));
            yield return new WaitForFixedUpdate();
            if (vrd.vessel is null) yield break;
            LinkToVRD(vrd);
            if (BDArmorySettings.DEBUG_RADAR) Debug.Log("[BDArmory.ModuleRadar]: Radar data link recovered: Local - " + vessel.vesselName + ", External - " + vrd.vessel.vesselName);
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

        protected override void LinkToVRD(VesselRadarData vrd)
        {
            if (vrd == null) return;
            vesselRadarData.LinkVRD(vrd);
        }

        protected override void UnlinkFromVRD(VesselRadarData vrd)
        {
            if (vrd == null) return;
            vrd.UnlinkDisabledRadar(this);
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
                    output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000022", sensorAzLimits[0], sensorAzLimits[1]));
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000041", sensorElLimits[0], sensorElLimits[1]));
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
    }

}
