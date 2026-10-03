using BDArmory.Utils;
using static BDArmory.Radar.ModuleRadar;

namespace BDArmory.Radar
{
    public abstract class ModuleRadarSensorBase : ModuleSensorBase
    {
        [KSPField]
        public string radarTransformName = string.Empty;

        [KSPField]
        public FloatCurve radarDetectionCurve = new FloatCurve();		//FloatCurve defining at what range which RCS size can be detected

        [KSPField]
        public FloatCurve radarVelocityGate = new FloatCurve();		//FloatCurve defining the reduction in received RCS due to a doppler gate

        [KSPField]
        public FloatCurve radarRangeGate = new FloatCurve();		//FloatCurve defining the reduction in received RCS due to a range gate

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

        public ModuleRadar.SonarModes sonarMode = ModuleRadar.SonarModes.None;

        public float signalPersistTimeForRwr;

        public float radarMinDistanceDetect
        {
            get { return radarDetectionCurve.minTime; }
        }

        //[KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Detection Range")]
        public float radarMaxDistanceDetect
        {
            get { return radarDetectionCurve.maxTime; }
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

        protected void SetNotchChaffFac()
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

        protected override void FlightSetup(string sensorTransformName)
        {
            base.FlightSetup(sensorTransformName);
            RadarUtils.SetupResources();

            SetNotchChaffFac();

            rwrType = (RadarWarningReceiver.RWRThreatTypes)rwrThreatType;
            sonarMode = (SonarModes)sonarType;
            if (rwrType == RadarWarningReceiver.RWRThreatTypes.Sonar)
            {
                signalPersistTimeForRwr = RadarUtils.ACTIVE_MISSILE_PING_PERSIST_TIME;
            }
            else
            {
                // A scan ping must persist until the beam sweeps back around (#796-4):
                // signalPersistTime is one full sweep cycle for omnidirectional radars and one
                // FOV sweep for directional ones - a directional scan needs an out-and-back pass
                // (2x) to revisit a target. Halving it left RWR consumers (e.g. antirad target
                // acquisition) blind for about half of every sweep, so antirad missiles only
                // acquired against a radar that happened to be locking the player at fire time.
                signalPersistTimeForRwr = omnidirectional ? signalPersistTime : 2f * signalPersistTime;
            }
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
    }
}
