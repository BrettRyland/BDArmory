using System.Globalization;
using UnityEngine;

namespace BDArmory.Radar
{
    /// <summary>
    /// Configurable radar module (#797): a ModuleRadar whose performance parameters can be
    /// edited directly in the editor PAW instead of writing a custom .cfg file.
    ///
    /// All proxy fields are editor-only and persistent. On load any proxy that is not present
    /// in the config node is initialised from the corresponding base ModuleRadar value (or from
    /// the base detection/lock curves), so a plain cfg works unchanged; on start the proxies are
    /// applied back onto the base fields - before base.OnStart computes signal persist times,
    /// RWR types and radar limits - and the detection/lock curves are rebuilt from the range and
    /// RCS proxies by scaling the original cfg curve (shape preserved), or generated from scratch
    /// if the base part defined no curve.
    ///
    /// The module derives from ModuleRadar, so every part of the game that looks radars up via
    /// FindModuleImplementing&lt;ModuleRadar&gt; / FindPartModulesImplementing&lt;ModuleRadar&gt;
    /// (polymorphic "is T" checks) finds this radar as well.
    /// </summary>
    public class ModuleConfigurableRadar : ModuleRadar
    {
        #region Editor proxy fields
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_Omnidirectional"),
         UI_Toggle(controlEnabled = true, enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public bool cfgOmnidirectional = true;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_AzimuthFOV"),
         UI_FloatRange(minValue = 5f, maxValue = 360f, stepIncrement = 5f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float cfgAzFOV = 90f;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_ElevationFOV"),
         UI_FloatRange(minValue = 0f, maxValue = 180f, stepIncrement = 5f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float cfgElFOV = 0f; // 0 = automatic (base field "-1")

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_ScanSpeed"),
         UI_FloatRange(minValue = 10f, maxValue = 720f, stepIncrement = 5f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float cfgScanSpeed = 120f;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_LockSpeed"),
         UI_FloatRange(minValue = 10f, maxValue = 720f, stepIncrement = 5f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float cfgLockSpeed = 120f;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_LockAngle"),
         UI_FloatRange(minValue = 0f, maxValue = 90f, stepIncrement = 0.5f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float cfgLockAngle = 4f;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_ShowDirection"),
         UI_Toggle(controlEnabled = true, enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public bool cfgShowDirection = true;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_MultiLockFOV"),
         UI_FloatRange(minValue = 5f, maxValue = 180f, stepIncrement = 5f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float cfgMultiLockFOV = 30f;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_LockAttemptFOV"),
         UI_FloatRange(minValue = 0.5f, maxValue = 45f, stepIncrement = 0.5f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float cfgLockAttemptFOV = 2f;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_CanScan"),
         UI_Toggle(controlEnabled = true, enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public bool cfgCanScan = true;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_CanLock"),
         UI_Toggle(controlEnabled = true, enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public bool cfgCanLock = true;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_TWS"),
         UI_Toggle(controlEnabled = true, enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public bool cfgCanTWS = false;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_ReceiveData"),
         UI_Toggle(controlEnabled = true, enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public bool cfgCanReceiveData = false;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_MaxLocks"),
         UI_FloatRange(minValue = 1f, maxValue = 12f, stepIncrement = 1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float cfgMaxLocks = 1f;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_GroundClutter"),
         UI_FloatRange(minValue = 0f, maxValue = 2f, stepIncrement = 0.05f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float cfgGroundClutter = 0.25f;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_ResourceDrain"),
         UI_FloatRange(minValue = 0f, maxValue = 20f, stepIncrement = 0.05f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float cfgResourceDrain = 0.825f;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_RwrType"),
         UI_FloatRange(minValue = 0f, maxValue = 7f, stepIncrement = 1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float cfgRwrThreatType = 0f;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_DetectRange"),
         UI_FloatRange(minValue = 0.5f, maxValue = 500f, stepIncrement = 0.5f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float cfgDetectRange = 30f; // km, scales the cfg detection curve

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_DetectRCS"),
         UI_FloatRange(minValue = 0.1f, maxValue = 100f, stepIncrement = 0.1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float cfgDetectRCS = 25f; // m^2 detectable at max range

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_LockRange"),
         UI_FloatRange(minValue = 0.5f, maxValue = 500f, stepIncrement = 0.5f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float cfgLockRange = 30f; // km, scales the cfg lock curve

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_RadarCfg_LockRCS"),
         UI_FloatRange(minValue = 0.1f, maxValue = 300f, stepIncrement = 0.1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
        public float cfgLockRCS = 35f; // m^2 lockable at max range
        #endregion

        #region Original curve capture
        Keyframe[] _origDetectKeys;
        float _origDetectMax;
        float _origDetectMaxRcs;
        Keyframe[] _origLockKeys;
        float _origLockMax;
        float _origLockMaxRcs;
        #endregion

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            // Capture the original cfg curves before any rebuilding (non-persistent fields are
            // reloaded from the part cfg on every OnLoad, so this is always the pristine shape).
            CaptureCurves();

            // Initialise any proxy absent from the node from the base ModuleRadar values, so a
            // plain cfg (or stock part converted by renaming the module) works unchanged.
            if (!node.HasValue(nameof(cfgOmnidirectional))) cfgOmnidirectional = omnidirectional;
            if (!node.HasValue(nameof(cfgAzFOV))) cfgAzFOV = ParseAzimuthFOV(directionalFieldOfView, 90f);
            if (!node.HasValue(nameof(cfgElFOV))) { float el = ParseFloat(elevationFOV, -1f); cfgElFOV = el < 0f ? 0f : el; }
            if (!node.HasValue(nameof(cfgScanSpeed))) cfgScanSpeed = scanRotationSpeed;
            if (!node.HasValue(nameof(cfgLockSpeed))) cfgLockSpeed = lockRotationSpeed;
            if (!node.HasValue(nameof(cfgLockAngle))) cfgLockAngle = lockRotationAngle;
            if (!node.HasValue(nameof(cfgShowDirection))) cfgShowDirection = showDirectionWhileScan;
            if (!node.HasValue(nameof(cfgMultiLockFOV))) cfgMultiLockFOV = multiLockFOV;
            if (!node.HasValue(nameof(cfgLockAttemptFOV))) cfgLockAttemptFOV = lockAttemptFOV;
            if (!node.HasValue(nameof(cfgCanScan))) cfgCanScan = canScan;
            if (!node.HasValue(nameof(cfgCanLock))) cfgCanLock = canLock;
            if (!node.HasValue(nameof(cfgCanTWS))) cfgCanTWS = canTrackWhileScan;
            if (!node.HasValue(nameof(cfgCanReceiveData))) cfgCanReceiveData = canReceiveRadarData;
            if (!node.HasValue(nameof(cfgMaxLocks))) cfgMaxLocks = maxLocks;
            if (!node.HasValue(nameof(cfgGroundClutter))) cfgGroundClutter = radarGroundClutterFactor;
            if (!node.HasValue(nameof(cfgResourceDrain))) cfgResourceDrain = (float)resourceDrain;
            if (!node.HasValue(nameof(cfgRwrThreatType))) cfgRwrThreatType = rwrThreatType;
            if (!node.HasValue(nameof(cfgDetectRange)) && _origDetectMax > 0f) cfgDetectRange = _origDetectMax;
            if (!node.HasValue(nameof(cfgDetectRCS)) && _origDetectMaxRcs > 0f) cfgDetectRCS = _origDetectMaxRcs;
            if (!node.HasValue(nameof(cfgLockRange)) && _origLockMax > 0f) cfgLockRange = _origLockMax;
            if (!node.HasValue(nameof(cfgLockRCS)) && _origLockMaxRcs > 0f) cfgLockRCS = _origLockMaxRcs;
        }

        public override void OnStart(StartState state)
        {
            ApplyConfig(); // Before base.OnStart: signal persist time, RWR type and radar limits are derived from these values.
            base.OnStart(state);
        }

        /// <summary>
        /// Copy the editor proxies onto the base ModuleRadar fields and rebuild the detection/lock curves.
        /// </summary>
        void ApplyConfig()
        {
            omnidirectional = cfgOmnidirectional;
            directionalFieldOfView = cfgAzFOV.ToString(CultureInfo.InvariantCulture);
            elevationFOV = cfgElFOV <= 0f ? "-1" : cfgElFOV.ToString(CultureInfo.InvariantCulture);
            scanRotationSpeed = cfgScanSpeed;
            lockRotationSpeed = cfgLockSpeed;
            lockRotationAngle = cfgLockAngle;
            showDirectionWhileScan = cfgShowDirection;
            multiLockFOV = cfgMultiLockFOV;
            lockAttemptFOV = cfgLockAttemptFOV;
            canScan = cfgCanScan;
            canLock = cfgCanLock;
            canTrackWhileScan = cfgCanTWS;
            canReceiveRadarData = cfgCanReceiveData;
            canRecieveRadarData = cfgCanReceiveData; // deprecated spelling, kept in sync
            maxLocks = Mathf.RoundToInt(cfgMaxLocks);
            radarGroundClutterFactor = cfgGroundClutter;
            resourceDrain = cfgResourceDrain;
            rwrThreatType = Mathf.RoundToInt(cfgRwrThreatType);

            RebuildCurves();
        }

        void CaptureCurves()
        {
            if (radarDetectionCurve != null && radarDetectionCurve.Curve != null)
            {
                _origDetectKeys = radarDetectionCurve.Curve.keys;
                _origDetectMax = radarDetectionCurve.maxTime;
                _origDetectMaxRcs = _origDetectMax > 0f ? radarDetectionCurve.Evaluate(_origDetectMax) : 0f;
            }
            if (radarLockTrackCurve != null && radarLockTrackCurve.Curve != null)
            {
                _origLockKeys = radarLockTrackCurve.Curve.keys;
                _origLockMax = radarLockTrackCurve.maxTime;
                _origLockMaxRcs = _origLockMax > 0f ? radarLockTrackCurve.Evaluate(_origLockMax) : 0f;
            }
        }

        /// <summary>
        /// Rebuild the detection and lock curves from the range/RCS proxies: the original cfg curve
        /// shape is preserved by scaling its time and value axes; a base part without a curve gets
        /// a standard shape generated instead. Reassigns the field (never mutates the shared prefab
        /// curve instance).
        /// </summary>
        void RebuildCurves()
        {
            if (cfgDetectRange > 0f)
            {
                if (_origDetectKeys != null && _origDetectKeys.Length > 0 && _origDetectMax > 0f)
                {
                    float timeScale = cfgDetectRange / _origDetectMax;
                    float valueScale = _origDetectMaxRcs > 0f ? cfgDetectRCS / _origDetectMaxRcs : 1f;
                    radarDetectionCurve = new FloatCurve(ScaleKeys(_origDetectKeys, timeScale, valueScale));
                }
                else
                {
                    radarDetectionCurve = DefaultDetectCurve(cfgDetectRange, cfgDetectRCS);
                }
            }

            if (cfgLockRange > 0f)
            {
                if (_origLockKeys != null && _origLockKeys.Length > 0 && _origLockMax > 0f)
                {
                    float timeScale = cfgLockRange / _origLockMax;
                    float valueScale = _origLockMaxRcs > 0f ? cfgLockRCS / _origLockMaxRcs : 1f;
                    radarLockTrackCurve = new FloatCurve(ScaleKeys(_origLockKeys, timeScale, valueScale));
                }
                else
                {
                    radarLockTrackCurve = DefaultLockCurve(cfgLockRange, cfgLockRCS);
                }
            }
        }

        static Keyframe[] ScaleKeys(Keyframe[] keys, float timeScale, float valueScale)
        {
            var scaled = new Keyframe[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                var k = keys[i];
                scaled[i] = new Keyframe(
                    k.time * timeScale,
                    k.value * valueScale,
                    k.inTangent * valueScale / timeScale,
                    k.outTangent * valueScale / timeScale);
            }
            return scaled;
        }

        // Standard detection shape: assured detection up to 15% of range, then a rising RCS threshold.
        static FloatCurve DefaultDetectCurve(float maxRangeKm, float rcs)
        {
            return new FloatCurve(new[]
            {
                new Keyframe(0f, 0f),
                new Keyframe(maxRangeKm * 0.15f, 0f),
                new Keyframe(maxRangeKm * 0.33f, rcs * 0.12f),
                new Keyframe(maxRangeKm * 0.67f, rcs * 0.40f),
                new Keyframe(maxRangeKm, rcs),
            });
        }

        // Standard lock shape: NO assured zone (a zero-threshold lock range would neuter chaff/ECM),
        // rising steadily to the RCS threshold at max range.
        static FloatCurve DefaultLockCurve(float maxRangeKm, float rcs)
        {
            return new FloatCurve(new[]
            {
                new Keyframe(0f, 0f),
                new Keyframe(maxRangeKm * 0.25f, rcs * 0.10f),
                new Keyframe(maxRangeKm * 0.60f, rcs * 0.40f),
                new Keyframe(maxRangeKm, rcs),
            });
        }

        static float ParseFloat(string value, float fallback)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            float parsed;
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return parsed;
            return fallback;
        }

        // directionalFieldOfView is either a single symmetric FOV value ("120") or a
        // "left,right" sector pair ("-45,45"); the proxy is symmetric, so convert the pair to a width.
        static float ParseAzimuthFOV(string value, float fallback)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            int comma = value.IndexOf(',');
            if (comma < 0) return ParseFloat(value, fallback);
            var parts = value.Split(',');
            if (parts.Length < 2) return ParseFloat(value, fallback);
            float a, b;
            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out a)) return fallback;
            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out b)) return fallback;
            return Mathf.Abs(b - a);
        }
    }
}
