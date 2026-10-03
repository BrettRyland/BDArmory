using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.WeaponMounts;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static BDArmory.Radar.ModuleRadar;

namespace BDArmory.Radar
{
    public class ModuleIRST : ModuleSensorBase
    {
        #region KSPFields (Part Configuration)

        #region General Configuration

        [KSPField]
        private string IRSTName = null;

        [KSPField]
        public int turretID = 0;

        [KSPField]
        public string irstTransformName = string.Empty;
        /*public Vector3 irstForward
        {
            get { return sensorTransform.up; }
        }*/

        #endregion General Configuration

        #region Capabilities

        [KSPField]
        public bool irstRanging = false;            //irst can get ranging info for target distance

        [KSPField]
        public FloatCurve DetectionCurve = new FloatCurve();		//FloatCurve setting default ranging capabilities of the IRST

        [KSPField]
        public FloatCurve TempSensitivityCurve = new FloatCurve();		//FloatCurve setting default IR spectrum capabilities of the IRST

        [KSPField]
        public FloatCurve atmAttenuationCurve = new FloatCurve();        //FloatCurve range increase/decrease based on atm density/temp, thinner/cooler air yields longer range returns


        [KSPField]
        public float GroundClutterFactor = 0.16f; //Factor defining how effective the irst is at detecting heatsigs against ambient ground temperature (0=ineffective, 1=fully effective)
                                                  //default to 0.16, IRSTs have about a 6th of the detection range for ground targets vs air targets.

        public override bool CanLock
        {
            get
            {
                return false;
            }
        }

        #endregion Capabilities

        #region Persisted State in flight

        [KSPField(isPersistant = true)]
        public string linkedVesselID;

        [Obsolete]
        [KSPField(isPersistant = true)]
        public bool irstEnabled;

        #endregion Persisted State in flight

        #endregion KSPFields (Part Configuration)

        #region KSP Events & Actions

        [KSPAction("Toggle IRST")]
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

        [KSPEvent(active = true, guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_ToggleIRST")]//Toggle IRST - FIXME - Localize
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

        #endregion KSP Events & Actions

        #region Part members

        public float irstMinDistanceDetect
        {
            get { return DetectionCurve.minTime; }
        }

        //[KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Detection Range")]
        public float irstMaxDistanceDetect
        {
            get { return DetectionCurve.maxTime; }
        }

        //GUI
        private bool drawGUI;

        public bool boresightScan;

        //locking
        public bool slaveTurrets;
        public ModuleTurret lockingTurret;
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
            Events[nameof(Toggle)].guiName = sensorEnabled ? StringUtils.Localize("#autoLOC_bda_1000036") : StringUtils.Localize("#autoLOC_bda_1000037");		// fixme - fix localizations
        }
        void Start()
        {
            resourceID = PartResourceLibrary.Instance.GetDefinition(resourceName).id;
        }

        protected override void AddSensorToVRD()
        {
            if (vesselRadarData == null) return;
            vesselRadarData.AddIRST(this);
        }

        protected override void RemoveSensorFromVRD()
        {
            if (vesselRadarData == null) return;
            MissileFire weaponManager = vesselRadarData.weaponManager;
            vesselRadarData.RemoveIRST(this);
            if (weaponManager != null)
            {
                if (weaponManager.irsts.Count > 1)
                {
                    using (List<ModuleIRST>.Enumerator irst = weaponManager.irsts.GetEnumerator())
                        while (irst.MoveNext())
                        {
                            if (irst.Current == null) continue;
                            weaponManager._irstsEnabled = false;
                            if (irst.Current != this && irst.Current.sensorEnabled)
                            {
                                weaponManager._irstsEnabled = true;
                                break;
                            }
                        }
                }
                else weaponManager._irstsEnabled = false;
            }
        }

        public override void EnableSensor()
        {
            base.EnableSensor();

            StartCoroutine(PostAnimSetup());


        }

        IEnumerator PostAnimSetup()
        {
            WaitForFixedUpdate wait = new WaitForFixedUpdate();
            while (!sensorEnabled) yield return wait;

            EnsureVesselRadarData(true);

            UpdateToggleGuiName();
            //vesselRadarData.AddIRST(this);
            var weaponManager = WeaponManager;
            if (weaponManager != null)
            {
                weaponManager._irstsEnabled = true;
            }
        }

        public override void DisableSensor()
        {
            base.DisableSensor();

            UpdateToggleGuiName();

            var weaponManager = WeaponManager;
            using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedvessels.MoveNext())
                {
                    BDATargetManager.ClearRadarReport(loadedvessels.Current, weaponManager); //reset radar contact status
                }
        }

        void OnDestroy()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (vesselRadarData)
                {
                    vesselRadarData.RemoveIRST(this);
                    vesselRadarData.RemoveDataFromIRST(this);
                }
            }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            if (!string.IsNullOrEmpty(IRSTName))
            {
                sensorName = IRSTName;
            }

#pragma warning disable 0612 // Disable obsolete warning for this valid use.
            if (irstEnabled)
            {
                sensorEnabled = true;
                irstEnabled = false;
            }
#pragma warning restore 0612

            if (HighLogic.LoadedSceneIsFlight)
            {
                FlightSetup(irstTransformName);

                // fill TempSensitivityCurve with default values if not set by part config:
                if (TempSensitivityCurve.minTime == float.MaxValue)
                    TempSensitivityCurve.Add(0f, 1f);

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
        }

        protected override void StartupRoutineActions()
        {
            UpdateToggleGuiName();
        }

        void Update()
        {
            drawGUI = (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && !vessel.packed && sensorEnabled &&
                       vessel.isActiveVessel && BDArmorySetup.GAME_UI_ENABLED && !MapView.MapIsEnabled);
        }

        protected override void EnabledUpdate()
        {
            if (boresightScan)
            {
                BoresightScan();
            }
            else if (canScan)
            {
                Scan();
            }
        }

        protected override void PerformScan(float angleDelta)
        {
            RadarUtils.IRSTUpdateScan(WeaponManager, currentAngle, sensorElOffset, angleDelta, sensorElFOV, this);
        }

        void BoresightScan()
        {
            //currentAngle = Mathf.Lerp(currentAngle, 0, 0.08f);
            RadarUtils.IRSTUpdateScan(WeaponManager, currentAngle, sensorElOffset, boresightFOV, -1f, this);
        }

        public override void ReceiveContactData(TargetSignatureData contactData, bool locked)
        {
            if (vesselRadarData)
            {
                vesselRadarData.AddIRSTContact(this, contactData, contactData.signalStrength);
            }
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
            return;
        }

        protected override void UnlinkFromVRD(VesselRadarData vrd)
        {
            return;
        }

        // RMB info in editor
        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();
            output.Append(Environment.NewLine);
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000008", omnidirectional ? StringUtils.Localize("#autoLOC_bda_1000019") : StringUtils.Localize("#autoLOC_bda_1000020")));

            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000021", resourceDrain)); //Ec/sec

            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000022", directionalFieldOfView)); //Field of View

            output.Append(Environment.NewLine);
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000024")); //Capabilities
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000025", canScan)); //-Scanning

            output.Append(Environment.NewLine);
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000030")); //Performance

            if (canScan)
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000031", DetectionCurve.Evaluate(irstMaxDistanceDetect) - 273, irstMaxDistanceDetect)); //Detection x.xx deg C @ n km
            else
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000032"));

            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000034"));
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000035", GroundClutterFactor));


            return output.ToString();
        }
    }
}
