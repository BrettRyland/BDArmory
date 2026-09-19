using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.Utils;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BDArmory.Radar
{
    public abstract class ModuleSensorBase : BDAPartModule
    {
        #region KSPFields (Part Configuration)

        #region General Configuration

        [KSPField]
        public string sensorName;

        [KSPField]
        public string rotationTransformName = string.Empty;
        protected Transform rotationTransform;

        protected Transform sensorTransform;

        #endregion General Configuration

        #region Radar Capabilities

        [KSPField]
        public int rwrThreatType = 0;               //IMPORTANT, configures which type of radar it will show up as on the RWR
        public RadarWarningReceiver.RWRThreatTypes rwrType = RadarWarningReceiver.RWRThreatTypes.SAM;

        [KSPField]
        public double resourceDrain = 0.825;        //resource (EC/sec) usage of active radar

        [KSPField]
        public string resourceName = "ElectricCharge";

        protected int resourceID;

        [KSPField]
        public bool omnidirectional = true;			//false=scan FoV limited to directionalFieldOfView

        // NOTE: The radar is assumed to have full roll stabilization capabilities!
        [KSPField]
        public string directionalFieldOfView = "90";   //relevant for NON-omnidirectional only

        [KSPField]
        public string elevationFOV = "-1";             //FoV of the radar in the vertical axis

        public float sensorAzOffset = 0f;
        public float sensorAzFOV = 90f;
        public float[] sensorAzLimits = [-45f, 45f];
        public float sensorElOffset = 0f;
        public float sensorElFOV = 90f;
        public float[] sensorElLimits = [-45f, 45f];

        public float[] sensorMinMaxAzLimits = [-45f, 45f];
        public float[] sensorMinMaxElLimits = [-45f, 45f];

        [KSPField]
        public float scanRotationSpeed = 120; 		//in degrees per second, relevant for omni and directional

        [KSPField]
        public bool showDirectionWhileScan = false; //radar can show direction indicator of contacts (false: can show contacts as blocks only)

        [KSPField]
        protected bool canScan = true;                 //radar has detection capabilities

        public bool CanScan
        { 
            get 
            {
                return canScan;
            } 
        }

        public abstract bool CanLock { get; }

        [KSPField]
        public float boresightFOV = 10;				//relevant for boresight only

        [KSPField]
        public float lockRotationSpeed = 120;		//in degrees per second, relevant for omni only

        [KSPField]
        public float lockRotationAngle = 4;         //???

        [KSPField]
        public float multiLockFOV = 30;             //??

        [KSPField]
        public float lockAttemptFOV = 2;            //??

        [KSPField]
        public int maxLocks = 1;					//how many targets can be locked/tracked simultaneously

        [KSPField]
        public bool canTrackWhileScan = false;      //when tracking/locking, can we still detect/scan?

        //animation
        [KSPField] public string deployAnimationName;
        AnimationState deployAnimState;
        public bool hasDeployAnimation;
        [KSPField] public float deployAnimationSpeed = 1;
        [KSPField] public bool deployRotationBlock = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarAutoRetract", advancedTweakable = true),//Retract radar on disable
            UI_Toggle(enabledText = "#LOC_BDArmory_true", disabledText = "#LOC_BDArmory_false", scene = UI_Scene.All),]
        public bool retractOnDisable = false;

        public bool isDeployed()
        {
            return !hasDeployAnimation || deployAnimState.normalizedTime > 0.99;
        }

        Coroutine deployAnimRoutine;

        #endregion Radar Capabilities

        #region Persisted State in flight

        [KSPField(isPersistant = true)]
        public bool sensorEnabled;

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

        #endregion KSPFields (Part Configuration)

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

        protected TargetSignatureData[] attemptedLocks;
        //private bool[] lockSuccesses; // Removed as it was deemed unecessary
        protected List<TargetSignatureData> lockedTargets;

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

        protected int lockedTargetIndex;

        public int currentLockIndex
        {
            get { return lockedTargetIndex; }
        }

        //linked vessels
        protected List<VesselRadarData> linkedToVessels;
        public int linkedVRDs
        {
            get { return linkedToVessels.Count; }
        }
        //public List<ModuleRadar> availableRadarLinks;
        protected bool unlinkOnDestroy = true;

        //GUI
        public float signalPersistTime;

        //scanning
        protected float currentAngleLock;
        public Transform referenceTransform;
        protected float radialScanDirection = 1;

        //locking
        public float lockScanAngle;

        protected string myVesselID;

        // part state
        protected bool startupComplete;
        public float leftLimit;
        public float rightLimit;
        protected int snapshotTicker;

        //vessel
        public abstract MissileFire WeaponManager { get; protected set; }
        public VesselRadarData vesselRadarData;

        #endregion Part members

        public virtual void EnableSensor()
        {
            Deploy(true);
        }

        /// <summary>
        /// Disables sensor, setting sensorEnabled to false, unlinking from linked vessels and removing the sensor from its VRD.
        /// NOTE: Because this removes the sensor from its VRD, this should be run AFTER any manipulations on VRD are complete!
        /// This includes unlocking all targets, removing contacts, etc.
        /// </summary>
        public virtual void DisableSensor()
        {
            sensorEnabled = false;

            List<VesselRadarData>.Enumerator vrd = linkedToVessels.GetEnumerator();
            while (vrd.MoveNext())
            {
                UnlinkFromVRD(vrd.Current);
            }
            vrd.Dispose();

            RemoveSensorFromVRD();

            if (retractOnDisable)
            {
                Deploy(false);
            }
        }

        void Start()
        {
            resourceID = PartResourceLibrary.Instance.GetDefinition(resourceName).id;
            updateModel = (CanScan || CanLock);
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            AnimSetup();
        }

        protected void AnimSetup()
        {
            if (!string.IsNullOrEmpty(deployAnimationName))
            {
                hasDeployAnimation = true;
                deployAnimState = GUIUtils.SetUpSingleAnimation(deployAnimationName, part);
            }
        }

        protected virtual void FlightSetup(string sensorTransformName)
        {
            myVesselID = vessel.id.ToString();

            if (string.IsNullOrEmpty(sensorName))
            {
                sensorName = part.partInfo.title;
            }

            SetRadarLimits();

            signalPersistTime = omnidirectional
                ? 360 / (scanRotationSpeed + 5)
                : sensorAzFOV / (scanRotationSpeed + 5);

            if (rotationTransformName != string.Empty)
            {
                rotationTransform = part.FindModelTransform(rotationTransformName);
            }
            sensorTransform = sensorTransformName != string.Empty ? part.FindModelTransform(sensorTransformName) : part.transform;

            referenceTransform = (new GameObject()).transform;
            referenceTransform.parent = sensorTransform;
            referenceTransform.localPosition = Vector3.zero;
        }

        protected void SetRadarLimits()
        {
            ParseRadarLimits(directionalFieldOfView, out sensorAzOffset, out sensorAzFOV, out sensorAzLimits, out sensorMinMaxAzLimits);
            // Retain old radar characteristics, if omnidirectional the radar should be able to see targets at +/- 90, otherwise
            // the radar could previously see targets at +/- 90 but not lock them, so we'll just lock it to a square FoV
            ParseRadarLimits(elevationFOV, out sensorElOffset, out sensorElFOV, out sensorElLimits, out sensorMinMaxElLimits, true);
            if (BDArmorySettings.DEBUG_RADAR)
            {
                Debug.Log($"[BDArmory.ModuleRadar] radarAzOffset {sensorAzOffset}, radarAzFOV: {sensorAzFOV}, radarAzLimits: {sensorAzLimits[0]},{sensorAzLimits[1]}, radarMinMaxAzLimits: {sensorMinMaxAzLimits[0]},{sensorMinMaxAzLimits[1]}");
                Debug.Log($"[BDArmory.ModuleRadar] radarElOffset {sensorElOffset}, radarElFOV: {sensorElFOV}, radarElLimits: {sensorElLimits[0]},{sensorElLimits[1]}, radarMinMaxAzLimits: {sensorMinMaxElLimits[0]},{sensorMinMaxElLimits[1]}");
            }
        }

        void ParseRadarLimits(in string radarLimitString, out float radarOffset, out float radarFOV, out float[] radarLimits, out float[] radarMinMaxLimits, bool elevationLimits = false)
        {
            // If we're parsing elevation limits
            if (elevationLimits)
            {
                // Then consider if it's omnidirectional or not, by default, omni radars are allowed +/- 90° FoV
                // Otherwise the default is a square radar scan area (based on radarAZLimits)
                radarLimits = omnidirectional ? [-90f, 90f] : [sensorAzLimits[0], sensorAzLimits[1]];
                //radarMinMaxLimits = omnidirectional ? [90f, 90f] : [radarMinMaxAzLimits[0], radarMinMaxAzLimits[1]];
                // Even if the azimuth is offset, we should start with no offset for elevation
                radarMinMaxLimits = omnidirectional ? [90f, 90f] : [0.5f * sensorAzFOV, 0.5f * sensorAzFOV];
                // Default omnidirectional FoV is 180°
                radarFOV = omnidirectional ? 180f : sensorAzFOV;
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

        protected IEnumerator StartUpRoutine()
        {
            if (BDArmorySettings.DEBUG_RADAR)
                Debug.Log($"[BDArmory.ModuleSensor]: StartupRoutine: {sensorName} enabled: {sensorEnabled}");
            yield return new WaitWhile(() => !FlightGlobals.ready || vessel.packed || !vessel.loaded);
            yield return new WaitForFixedUpdate();

            StartupRoutineActions();

            startupComplete = true;
        }

        protected abstract void StartupRoutineActions();

        public void EnsureVesselRadarData(bool addSensor = false)
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
                    RemoveSensorFromVRD();
                }

                vesselRadarData = vessel.gameObject.GetComponent<VesselRadarData>();
                if (vesselRadarData == null)
                    vesselRadarData = vessel.gameObject.AddComponent<VesselRadarData>();

                vesselRadarData.weaponManager = WeaponManager;

                // Something wasn't right with the previous VRD so make sure we add the radar, primarily to take care of the multi-craft case
                addSensor = true;
            }

            if (addSensor && sensorEnabled)
            {
                AddSensorToVRD();
            }
        }

        protected abstract void AddSensorToVRD();

        protected abstract void RemoveSensorFromVRD();

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
                referenceTransform.rotation = Quaternion.LookRotation(sensorTransform.up,
                    vessel.up.ProjectOnPlanePreNormalized(sensorTransform.up).normalized);
            }
            currForward = referenceTransform.forward;
            currUp = referenceTransform.up;
            currRight = referenceTransform.right;

            ReferenceUpdateTime = Time.time;
        }

        public void UpdateDisplayTransform()
        {
            if (DisplayUpdateTime >= Time.time)
                return;
            UpdateReferenceTransform();

            if (sensorElOffset != 0 || sensorAzOffset != 0)
                currDisplayForward = Quaternion.AngleAxis(sensorElOffset, currRight) * Quaternion.AngleAxis(-sensorAzOffset, currUp) * currForward;
            else
                currDisplayForward = currForward;
            DisplayUpdateTime = Time.time;
        }

        protected void Deploy(bool forward)
        {
            if (hasDeployAnimation)
            {
                if (deployAnimRoutine != null)
                {
                    StopCoroutine(deployAnimRoutine);
                }

                deployAnimRoutine = StartCoroutine(DeployAnimation(forward));
            }
            else
                sensorEnabled = true;
        }

        IEnumerator DeployAnimation(bool forward)
        {
            var wait = new WaitForFixedUpdate();
            yield return wait;

            if (forward)
            {
                while (deployAnimState.normalizedTime < 1)
                {
                    deployAnimState.speed = deployAnimationSpeed;
                    yield return wait;
                }

                deployAnimState.normalizedTime = 1;
                sensorEnabled = true;
            }
            else
            {
                deployAnimState.speed = 0;

                // This does force rotationTransform to be a ModuleSensor-level thing but it doesn't have to be set or used...
                if (deployRotationBlock && rotationTransform)
                {
                    // Need to account for both identity and its -1 counterpart...
                    yield return new WaitWhileFixed(() => (rotationTransform.localRotation != Quaternion.identity && rotationTransform.localRotation != new Quaternion(0f, 0f, 0f, -1f)));
                }

                while (deployAnimState.normalizedTime > 0)
                {
                    deployAnimState.speed = -deployAnimationSpeed;
                    yield return wait;
                }

                deployAnimState.normalizedTime = 0;
            }

            deployAnimState.speed = 0;
        }

        protected void Scan()
        {
            float angleDelta = scanRotationSpeed * Time.fixedDeltaTime;
            PerformScan(angleDelta);

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
                    leftLimit = Mathf.Clamp(targetAngle - (multiLockFOV * 0.5f), sensorAzLimits[0],
                        sensorAzLimits[1]);
                    rightLimit = Mathf.Clamp(targetAngle + (multiLockFOV * 0.5f), sensorAzLimits[0],
                        sensorAzLimits[1]);

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
                    if (Mathf.Abs(currentAngle - sensorAzOffset) > sensorAzFOV * 0.5f)
                    {
                        // Set current angle to either the left/right limit
                        currentAngle = currentAngle < 0f ? sensorAzLimits[0] : sensorAzLimits[1];
                        // Reverse the scan direction
                        radialScanDirection = -radialScanDirection;
                    }
                }
            }
        }

        protected abstract void PerformScan(float angleDelta);

        void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && startupComplete)
            {
                if (!vessel.IsControllable && sensorEnabled)
                {
                    DisableSensor();
                }

                if (sensorEnabled && isDeployed())
                {
                    UpdateReferenceTransform();

                    DrainElectricity(); //physics behaviour, thus moved here from update

                    EnabledUpdate();
                }
            }
        }

        protected abstract void EnabledUpdate();

        bool updateModel = false;

        void LateUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight && updateModel)
            {
                UpdateModel();
            }
        }

        void UpdateModel()
        {
            //model rotation
            if (sensorEnabled && isDeployed())
            {
                EnabledModelUpdate();
            }
            else
            {
                DisabledModelUpdate();
            }
        }

        protected virtual void EnabledModelUpdate()
        {
            if (rotationTransform)
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
        }

        protected virtual void DisabledModelUpdate()
        {
            if (rotationTransform)
            {
                rotationTransform.localRotation = Quaternion.Lerp(rotationTransform.localRotation,
                    Quaternion.identity, 5 * TimeWarp.fixedDeltaTime);
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
                return (Mathf.Abs(VectorUtils.GetElevation(targetPosition - currPosition, currUp) - sensorElOffset) < 0.5f * sensorElFOV);
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
                return (Mathf.Abs(az - sensorAzOffset) < 0.5f * sensorAzFOV && Mathf.Abs(el - sensorElOffset) < 0.5f * sensorElFOV);
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
                return (Mathf.Abs(VectorUtils.GetElevationPreNorm(dir, currUp) - sensorElOffset) < 0.5f * sensorElFOV);
            }
            else
            {
                // Target exists and omnidirectional, we must check if we're within radar FoV
                // Radar azimuth is reversed, for whatever reason
                float az = VectorUtils.GetAngleOnPlane(dir, currForward, currRight);
                float el = VectorUtils.GetElevationPreNorm(dir, currUp);

                // Check if we're outside FoV
                return (Mathf.Abs(az - sensorAzOffset) < 0.5f * sensorAzFOV && Mathf.Abs(el - sensorElOffset) < 0.5f * sensorElFOV);
            }
        }

        public abstract void ReceiveContactData(TargetSignatureData contactData, bool _locked);

        protected abstract void LinkToVRD(VesselRadarData vrd);

        protected abstract void UnlinkFromVRD(VesselRadarData vrd);

        protected void DrainElectricity(bool showMessage = true)
        {
            if (resourceDrain <= 0)
            {
                return;
            }

            double drainAmount = resourceDrain * TimeWarp.fixedDeltaTime;
            double chargeAvailable = part.RequestResource(resourceID, drainAmount, ResourceFlowMode.ALL_VESSEL);
            if (chargeAvailable < drainAmount * 0.95f)
            {
                if (showMessage)
                {
                    ScreenMessages.PostScreenMessage($"{part.partInfo.title} {StringUtils.Localize("#autoLOC_244332")} {PartResourceLibrary.Instance.GetDefinition(resourceName).displayName}", 5.0f, ScreenMessageStyle.UPPER_CENTER);		// [part Title] Requires [localized resource name]
                }
                DisableSensor();
            }
        }
    }
}
