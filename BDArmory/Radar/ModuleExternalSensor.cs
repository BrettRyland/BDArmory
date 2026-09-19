using BDArmory.Competition;
using BDArmory.Control;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Weapons.Missiles;
using System;
using System.Collections;
using System.Text;
using UnityEngine;

namespace BDArmory.Radar
{
    public class ModuleExternalSensor : ModuleRadarSensorBase
    {
        #region KSPFields (Part Configuration)

        [KSPField]
        public float datalinkRange = 5000f;

        [KSPField]
        public bool detonateOnDisable = true; //reason for not wanting sensor to auto-cleanup when battery dry?

        [KSPField]
        public bool requireDirectConnection = false; //if datalinkrange > 0, this is intrinsically true, if we're assuming a sat link...

        [KSPField]
        public float deployDelay = -1f;

        [KSPField]
        public bool deployAltitudeTrigger = false; //irrelevant? would be true if deployAlt > 1 unless for whatever reason we need sonars that only activate after sinking x hundred meters

        [KSPField]
        public float deployAltitude = -1f;

        [KSPField]
        public bool deployWhenLanded = false; //is this *landedOrSplashed*, or specifically for ground touchdown?

        #endregion KSPFields (Part Configuration)

        public override bool CanLock
        {
            get 
            { 
                return false;
            } 
        }

        public ModuleExternalSensor BaseModule { get; private set; }

        #region Persisted State in flight

        // Within range?
        protected bool[] linksActive;

        private bool setLinks = false;

        #endregion Persisted State in flight

        #region Part members

        //vessel
        public MissileLauncher Missile { get; private set; }

        public BDTeam Team
        {
            get
            {
                return Missile ? Missile.Team : null;
            }
        }

        public override MissileFire WeaponManager
        {
            get
            {
                if (!field) GetWPMR();
                return field;
            }
            protected set;
        }

        public void GetWPMR()
        {
            // If somehow the missile is gone the sensor *should* be dead...
            if (!Missile)
            {
                WeaponManager = null;
                return;
            }
            // Return FiredByWM
            if (Missile.FiredByWM)
            {
                WeaponManager = Missile.FiredByWM;
                return;
            }
            //if (!requireDirectConnection)
            //{
                // If dead, return the first linkedToVessels
                if (linkedToVessels == null)
                {
                    WeaponManager = null;
                    return;
                }
                for (int i = 0; i < linkedToVessels.Count; i++)
                {
                    if (linkedToVessels[i] != null)
                    {
                        WeaponManager = linkedToVessels[i].weaponManager;
                        return;
                    }
                }
            //}
            WeaponManager = null;
            return;
        }

        #endregion Part members

        float deployTime = 0f;

        public void ArmSensor()
        {
            deployTime = Time.time + deployDelay;
            StartCoroutine(SensorActivationCoroutine());
        }

        IEnumerator SensorActivationCoroutine()
        {
            WaitForFixedUpdate wait = new WaitForFixedUpdate();
            while (true)
            {
                if (Time.time > deployTime && (!deployAltitudeTrigger || vessel.altitude < deployAltitude) && (!deployWhenLanded || (vessel.altitude < 1 || vessel.LandedOrSplashed)))
                {
                    EnableSensor();
                    yield break;
                }
                yield return wait;
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

            linkedToVessels = BDATargetManager.RegisterExternalSensor(this);
            linksActive = new bool[linkedToVessels.Count];
        }

        public override void DisableSensor()
        {
            base.DisableSensor();

            //var weaponManager = WeaponManager;
            //using (var loadedvessels = BDATargetManager.LoadedVessels.GetEnumerator())
            //    while (loadedvessels.MoveNext())
            //    {
            //        BDATargetManager.ClearRadarReport(loadedvessels.Current, weaponManager); //reset radar contact status
            //    }

            if (detonateOnDisable)
            {
                Missile.Detonate();
            }
        }

        void OnDestroy()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (sensorEnabled)
                {
                    DisableSensor();
                }

                referenceTransform = null;
            }
        }

        public override void OnAwake()
        {
            base.OnAwake();
            Fields[nameof(retractOnDisable)].isPersistant = false;
            Fields[nameof(retractOnDisable)].guiActive = false;
            Fields[nameof(retractOnDisable)].guiActiveEditor = false;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            if (HighLogic.LoadedSceneIsFlight)
            {
                FlightSetup(radarTransformName);

                Missile = part.FindModuleImplementing<MissileLauncher>();
                BaseModule = part.partInfo.partPrefab.FindModuleImplementing<ModuleExternalSensor>();
                retractOnDisable = BaseModule.retractOnDisable; // Safeguard in case the `OnAwake` `isPersistant = false` setting doesn't work...

                StartCoroutine(StartUpRoutine());
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
            if (sensorEnabled) EnableSensor();
        }

        protected override void EnabledUpdate()
        {
            if (canScan)
            {
                CheckLinks();
                Scan();
            }
        }

        protected override void PerformScan(float angleDelta)
        {
            RadarUtils.ExternalSensorScan(WeaponManager, currentAngle, sensorElOffset, angleDelta, sensorElFOV, this);
        }

        bool isConnected = false;

        public void CheckLinks()
        {
            // If < 0 we don't care about EITHER range or LoS
            if (datalinkRange < 0f)
            {
                isConnected = true;
                if (!setLinks) //these all start false, need to set them to true
                {
                    for (int i = 0; i < linkedToVessels.Count; i++)
                    {
                        linksActive[i] = true;
                    }
                    setLinks = true;
                }
                return;
            }
            isConnected = false;
            Vector3 adjustedPos = vessel.LandedOrSplashed ? (vessel.CoM + vessel.up * ((vessel.mainBody.ocean && vessel.altitude < 0f) ? (5f - vessel.altitude) : 5f)) : vessel.CoM;
            for (int i = 0; i < linkedToVessels.Count; i++)
            {
                Vessel currVessel = linkedToVessels[i].vessel;
                Vector3 currPos = currVessel.CoM;
                // If range == 0, we don't care about range, only LoS
                if (datalinkRange > 0f && (currPos - vessel.CoM).sqrMagnitude > datalinkRange * datalinkRange)
                {
                    linksActive[i] = false;
                    continue;
                }
                // LoS must not be blocked
                if (!RadarUtils.TerrainCheck(adjustedPos, currPos, vessel.mainBody))
                {
                    linksActive[i] = true;
                    isConnected = true;
                    if (!requireDirectConnection) break;
                }
                else
                {
                    linksActive[i] = false;
                }    
            }
        }

        public override void ReceiveContactData(TargetSignatureData contactData, bool _locked)
        {
            if (!isConnected) return;
            for (int i = 0; i < linkedToVessels.Count; i++)
            {
                if (requireDirectConnection && datalinkRange >= 0f && !linksActive[i]) continue;
                VesselRadarData currVRD = linkedToVessels[i];
                if (currVRD == null) continue;
                if (currVRD.canReceiveRadarData && currVRD.vessel != contactData.vessel)
                {
                    currVRD.AddRadarContact(this, contactData, _locked, true);
                }
            }
        }

        public void CheckLinkArraySize()
        {
            // Check array size...
            if (linksActive.Length >= linkedToVessels.Count) return;

            // Resize array...
            linksActive = new bool[linkedToVessels.Count];
            // Populate array if datalinkRange < 0f
            // Not technically necessary since we skip this check in the code if
            // datalinkRange < 0, but this will probably save a headache in case
            // that gets changed for some reason...
            if (datalinkRange < 0f)
            {
                for (int i = 0; i < linksActive.Length; i++)
                {
                    linksActive[i] = true;
                }
            }
        }

        protected override void LinkToVRD(VesselRadarData vrd)
        {
            if (vrd == null) return;
            BDATargetManager.LinkExternalSensorGroup(vrd, BaseModule);
        }

        protected override void UnlinkFromVRD(VesselRadarData vrd)
        {
            if (vrd == null) return;
            vrd.RemoveDataFromRadar(this);
        }

        protected override void AddSensorToVRD()
        {
            linkedToVessels = BDATargetManager.RegisterExternalSensor(this);
            linksActive = new bool[linkedToVessels.Count];
            vesselRadarData.AddSensor(this); //otherwise the sensor only registers when vrd.RefreshAvailableLinks() is called
            vesselRadarData.queueLinks = true;
        }

        protected override void RemoveSensorFromVRD()
        {
            linkedToVessels = null;
            BDATargetManager.RemoveExternalSensor(this);
        }

        // RMB info in editor
        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();
            output.Append(Environment.NewLine);
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000008", (omnidirectional ? StringUtils.Localize("#autoLOC_bda_1000019") : StringUtils.Localize("#autoLOC_bda_1000020"))));

            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000021", resourceDrain));

            // For some reason just doing this in OnStart(), even outside of the Flight scene check wasn't working...
            SetRadarLimits();

            if (!omnidirectional)
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000022", sensorAzLimits[0], sensorAzLimits[1]));
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000041", sensorElLimits[0], sensorElLimits[1]));
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000023", getRWRType(rwrThreatType)));

            output.Append(Environment.NewLine);
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000024"));
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000025", canScan));

            output.Append(Environment.NewLine);
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000030"));

            if (canScan)
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000031", radarDetectionCurve.Evaluate(radarMaxDistanceDetect), radarMaxDistanceDetect));
            else
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000032"));

            // Cannot lock...
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000034"));

            if (sonarType == 1)
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000039"));
            if (sonarType == 2)
                output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000040"));
            output.AppendLine(StringUtils.Localize("#autoLOC_bda_1000035", radarGroundClutterFactor));

            return output.ToString();
        }
    }

}
