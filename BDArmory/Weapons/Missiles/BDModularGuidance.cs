using KSP.UI.Screens;
using System.Collections.Generic;
using System;
using UniLinq;
using UnityEngine;

using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Guidances;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.VesselSpawning;

namespace BDArmory.Weapons.Missiles
{
    public class BDModularGuidance : MissileBase
    {
        private bool _missileIgnited;
        private int _nextStage = 1;

        private PartModule _targetDecoupler;

        private readonly Vessel _targetVessel = new Vessel();

        private Transform _velocityTransform;

        public Vessel LegacyTargetVessel;

        private MissileFire weaponManager = null;
        private bool mfChecked = false;

        private readonly List<Part> _vesselParts = new List<Part>();

        #region KSP FIELDS

        [KSPField]
        public string ForwardTransform = "ForwardNegative";

        [KSPField]
        public string UpTransform = "RightPositive";

        [KSPField(isPersistant = true, guiActive = true, guiName = "#LOC_BDArmory_WeaponName", guiActiveEditor = true), UI_Label(affectSymCounterparts = UI_Scene.All, scene = UI_Scene.All)]//Weapon Name 
        public string WeaponName;

        [KSPField(advancedTweakable = true, isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_FiringPriority"),
    UI_FloatRange(minValue = 0, maxValue = 10, stepIncrement = 1, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        public float priority = 0; //per-weapon priority selection override

        [KSPField(isPersistant = false, guiActive = true, guiName = "#LOC_BDArmory_GuidanceType", guiActiveEditor = true)]//Guidance Type 
        public string GuidanceLabel = "AGM/STS";

        [KSPField(isPersistant = true, guiActive = true, guiName = "#LOC_BDArmory_TargetingMode", guiActiveEditor = true), UI_Label(affectSymCounterparts = UI_Scene.All, scene = UI_Scene.All)]//Targeting Mode 
        private string _targetingLabel = TargetingModes.Radar.ToString();

        [KSPField(isPersistant = true)]
        public int GuidanceIndex = 2;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ActiveRadarRange"), UI_FloatRange(minValue = 0, maxValue = 50000f, stepIncrement = 1000f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Active Radar Range
        public float ActiveRadarRange = 6000;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ChaffFactor"), UI_FloatRange(minValue = 0, maxValue = 2, stepIncrement = 0.1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Active Radar Range
        public float ChaffEffectivity = 1;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_SteerLimiter"), UI_FloatRange(minValue = .1f, maxValue = 1f, stepIncrement = .05f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Steer Limiter
        public float MaxSteer = 1;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_StagesNumber"), UI_FloatRange(minValue = 1f, maxValue = 9f, stepIncrement = 1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Stages Number
        public float StagesNumber = 1;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_StageToTriggerOnProximity"), UI_FloatRange(minValue = 0f, maxValue = 6f, stepIncrement = 1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Stage to Trigger On Proximity
        public float StageToTriggerOnProximity = 0;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_SteerDamping"), UI_FloatRange(minValue = 0f, maxValue = 20f, stepIncrement = .05f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Steer Damping
        public float SteerDamping = 5;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_SteerFactor"), UI_FloatRange(minValue = 0.1f, maxValue = 20f, stepIncrement = .1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Steer Factor
        public float SteerMult = 10;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_RollCorrection"), UI_Toggle(controlEnabled = true, enabledText = "#LOC_BDArmory_RollCorrection_enabledText", disabledText = "#LOC_BDArmory_RollCorrection_disabledText", scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]//Roll Correction--Roll enabled--Roll disabled
        public bool RollCorrection = false;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_TimeBetweenStages"),//Time Between Stages
         UI_FloatRange(minValue = 0f, maxValue = 5f, stepIncrement = 0.5f, scene = UI_Scene.Editor)]
        public float timeBetweenStages = 1f;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_MinSpeedGuidance"),//Min Speed before guidance
         UI_FloatRange(minValue = 0f, maxValue = 1000f, stepIncrement = 50f, scene = UI_Scene.Editor)]
        public float MinSpeedGuidance = 200f;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_ClearanceRadius", advancedTweakable = true),//Clearance radius
         UI_FloatRange(minValue = 0f, maxValue = 5f, stepIncrement = 0.05f, scene = UI_Scene.Editor)]
        public float clearanceRadius = 0.14f;

        public override float ClearanceRadius => clearanceRadius;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_ClearanceLength", advancedTweakable = true),//Clearance length
         UI_FloatRange(minValue = 0f, maxValue = 5f, stepIncrement = 0.05f, scene = UI_Scene.Editor)]
        public float clearanceLength = 0.14f;

        public override float ClearanceLength => clearanceLength;

        private Vector3 initialMissileRollPlane;
        private Vector3 initialMissileForward;


        private bool _minSpeedAchieved = false;
        private double lastRollAngle;
        private double angularVelocity;

        public float warheadYield = 0;
        #endregion KSP FIELDS

        public TransformAxisVectors ForwardTransformAxis { get; set; }
        public TransformAxisVectors UpTransformAxis { get; set; }

        public float Mass => (float)vessel.totalMass;

        public enum TransformAxisVectors
        {
            UpPositive,
            UpNegative,
            ForwardPositive,
            ForwardNegative,
            RightPositive,
            RightNegative
        }

        private void RefreshGuidanceMode()
        {
            switch (GuidanceIndex)
            {
                case 1:
                    GuidanceMode = GuidanceModes.AAMPure;
                    GuidanceLabel = "AAM";
                    break;

                case 2:
                    GuidanceMode = GuidanceModes.AGM;
                    GuidanceLabel = "AGM/STS";
                    break;

                case 3:
                    GuidanceMode = GuidanceModes.Cruise;
                    GuidanceLabel = "Cruise";
                    break;

                case 4:
                    GuidanceMode = GuidanceModes.AGMBallistic;
                    GuidanceLabel = "Ballistic";
                    break;

                case 5:
                    GuidanceMode = GuidanceModes.PN;
                    GuidanceLabel = "Proportional Navigation";
                    break;

                case 6:
                    GuidanceMode = GuidanceModes.APN;
                    GuidanceLabel = "Augmented Pro-Nav";
                    break;
            }

            if (Fields["CruiseAltitude"] != null)
            {
                CruiseAltitudeRange();
                Fields["CruiseAltitude"].guiActive = GuidanceMode == GuidanceModes.Cruise;
                Fields["CruiseAltitude"].guiActiveEditor = GuidanceMode == GuidanceModes.Cruise;
                Fields["CruiseSpeed"].guiActive = GuidanceMode == GuidanceModes.Cruise;
                Fields["CruiseSpeed"].guiActiveEditor = GuidanceMode == GuidanceModes.Cruise;
                Events["CruiseAltitudeRange"].guiActive = GuidanceMode == GuidanceModes.Cruise;
                Events["CruiseAltitudeRange"].guiActiveEditor = GuidanceMode == GuidanceModes.Cruise;
                Fields["CruisePredictionTime"].guiActiveEditor = GuidanceMode == GuidanceModes.Cruise;
            }

            if (Fields["BallisticOverShootFactor"] != null)
            {
                Fields["BallisticOverShootFactor"].guiActive = GuidanceMode == GuidanceModes.AGMBallistic;
                Fields["BallisticOverShootFactor"].guiActiveEditor = GuidanceMode == GuidanceModes.AGMBallistic;
                Fields["BallisticAngle"].guiActive = GuidanceMode == GuidanceModes.AGMBallistic;
                Fields["BallisticAngle"].guiActiveEditor = GuidanceMode == GuidanceModes.AGMBallistic;
            }
            if (Fields["SoftAscent"] != null)
            {
                Fields["SoftAscent"].guiActive = GuidanceMode == GuidanceModes.AGMBallistic;
                Fields["SoftAscent"].guiActiveEditor = GuidanceMode == GuidanceModes.AGMBallistic;
            }

            if (GuidanceMode != GuidanceModes.AAMLoft)
            {
                Fields["LoftMaxAltitude"].guiActive = false;
                Fields["LoftMaxAltitude"].guiActiveEditor = false;
                Fields["LoftRangeOverride"].guiActive = false;
                Fields["LoftRangeOverride"].guiActiveEditor = false;
                Fields["LoftAltitudeAdvMax"].guiActive = false;
                Fields["LoftAltitudeAdvMax"].guiActiveEditor = false;
                Fields["LoftMinAltitude"].guiActive = false;
                Fields["LoftMinAltitude"].guiActiveEditor = false;
                Fields["LoftAngle"].guiActive = false;
                Fields["LoftAngle"].guiActiveEditor = false;
                Fields["LoftTermAngle"].guiActive = false;
                Fields["LoftTermAngle"].guiActiveEditor = false;
                Fields["LoftRangeFac"].guiActive = false;
                Fields["LoftRangeFac"].guiActiveEditor = false;
                Fields["LoftVelComp"].guiActive = false;
                Fields["LoftVelComp"].guiActiveEditor = false;
                Fields["LoftVertVelComp"].guiActive = false;
                Fields["LoftVertVelComp"].guiActiveEditor = false;
                //Fields["LoftAltComp"].guiActive = false;
                //Fields["LoftAltComp"].guiActiveEditor = false;
                //Fields["terminalHomingRange"].guiActive = false;
                //Fields["terminalHomingRange"].guiActiveEditor = false;
            }
            else
            {
                Fields["LoftMaxAltitude"].guiActive = true;
                Fields["LoftMaxAltitude"].guiActiveEditor = true;
                Fields["LoftRangeOverride"].guiActive = true;
                Fields["LoftRangeOverride"].guiActiveEditor = true;
                Fields["LoftAltitudeAdvMax"].guiActive = true;
                Fields["LoftAltitudeAdvMax"].guiActiveEditor = true;
                Fields["LoftMinAltitude"].guiActive = true;
                Fields["LoftMinAltitude"].guiActiveEditor = true;
                //Fields["terminalHomingRange"].guiActive = true;
                //Fields["terminalHomingRange"].guiActiveEditor = true;

                if (!GameSettings.ADVANCED_TWEAKABLES)
                {
                    Fields["LoftAngle"].guiActive = false;
                    Fields["LoftAngle"].guiActiveEditor = false;
                    Fields["LoftTermAngle"].guiActive = false;
                    Fields["LoftTermAngle"].guiActiveEditor = false;
                    Fields["LoftRangeFac"].guiActive = false;
                    Fields["LoftRangeFac"].guiActiveEditor = false;
                    Fields["LoftVelComp"].guiActive = false;
                    Fields["LoftVelComp"].guiActiveEditor = false;
                    Fields["LoftVertVelComp"].guiActive = false;
                    Fields["LoftVertVelComp"].guiActiveEditor = false;
                    //Fields["LoftAltComp"].guiActive = false;
                    //Fields["LoftAltComp"].guiActiveEditor = false;
                }
                else
                {
                    Fields["LoftAngle"].guiActive = true;
                    Fields["LoftAngle"].guiActiveEditor = true;
                    Fields["LoftTermAngle"].guiActive = true;
                    Fields["LoftTermAngle"].guiActiveEditor = true;
                    Fields["LoftRangeFac"].guiActive = true;
                    Fields["LoftRangeFac"].guiActiveEditor = true;
                    Fields["LoftVelComp"].guiActive = true;
                    Fields["LoftVelComp"].guiActiveEditor = true;
                    Fields["LoftVertVelComp"].guiActive = true;
                    Fields["LoftVertVelComp"].guiActiveEditor = true;
                    //Fields["LoftAltComp"].guiActive = true;
                    //Fields["LoftAltComp"].guiActiveEditor = true;
                }
            }

            if (GuidanceMode != GuidanceModes.AAMHybrid && GuidanceMode != GuidanceModes.AAMLoft)
            {
                Fields["terminalHomingRange"].guiActive = false;
                Fields["terminalHomingRange"].guiActiveEditor = false;
            }
            else
            {
                Fields["terminalHomingRange"].guiActive = true;
                Fields["terminalHomingRange"].guiActiveEditor = true;
            }

            GUIUtils.RefreshAssociatedWindows(part);
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();

            if (!HighLogic.LoadedSceneIsFlight) return;

            if (HasFired && !HasExploded)
            {
                UpdateGuidance();
                CheckDetonationState(true);
                CheckDetonationDistance();
                CheckDelayedFired();
                CheckNextStage();

                if (isTimed && TimeIndex > detonationTime)
                {
                    AutoDestruction();
                }
            }

            if (HasExploded && StageToTriggerOnProximity == 0)
            {
                AutoDestruction();
            }
        }

        void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;

            if (!HasFired)
                CheckDetonationState(true);
        }

        private void CheckNextStage()
        {
            if (ShouldExecuteNextStage())
            {
                if (!nextStageCountdownStart)
                {
                    this.nextStageCountdownStart = true;
                    this.stageCutOfftime = Time.time;
                }
                else
                {
                    if ((Time.time - stageCutOfftime) >= timeBetweenStages)
                    {
                        ExecuteNextStage();
                        nextStageCountdownStart = false;
                    }
                }
            }
        }

        public bool nextStageCountdownStart { get; set; } = false;

        public float stageCutOfftime { get; set; } = 0f;

        private void CheckDelayedFired()
        {
            if (_missileIgnited) return;
            if (TimeIndex > dropTime)
            {
                MissileIgnition();
            }
        }

        private void DisableRecursiveFlow(List<Part> children)
        {
            List<Part>.Enumerator child = children.GetEnumerator();
            while (child.MoveNext())
            {
                if (child.Current == null) continue;

                DisablingExplosives(child.Current);

                IEnumerator<PartResource> resource = child.Current.Resources.GetEnumerator();
                while (resource.MoveNext())
                {
                    if (resource.Current == null) continue;
                    if (resource.Current.flowState)
                    {
                        resource.Current.flowState = false;
                    }
                }
                resource.Dispose();

                if (child.Current.children.Count > 0)
                {
                    DisableRecursiveFlow(child.Current.children);
                }
                if (!_vesselParts.Contains(child.Current)) _vesselParts.Add(child.Current);
            }
            child.Dispose();
        }

        private void EnableResourceFlow(List<Part> children)
        {
            List<Part>.Enumerator child = children.GetEnumerator();
            while (child.MoveNext())
            {
                if (child.Current == null) continue;

                SetupExplosive(child.Current);

                IEnumerator<PartResource> resource = child.Current.Resources.GetEnumerator();
                while (resource.MoveNext())
                {
                    if (resource.Current == null) continue;
                    if (!resource.Current.flowState)
                    {
                        resource.Current.flowState = true;
                    }
                }
                resource.Dispose();
                if (child.Current.children.Count > 0)
                {
                    EnableResourceFlow(child.Current.children);
                }
            }
            child.Dispose();
        }

        private void DisableResourcesFlow()
        {
            if (_targetDecoupler != null)
            {
                if (_targetDecoupler.part.children.Count == 0) return;
                _vesselParts.Clear();
                DisableRecursiveFlow(_targetDecoupler.part.children);
            }
        }

        private void MissileIgnition()
        {
            EnableResourceFlow(_vesselParts);
            GameObject velocityObject = new GameObject("velObject");
            velocityObject.transform.position = vessel.transform.position;
            velocityObject.transform.parent = vessel.transform;
            _velocityTransform = velocityObject.transform;

            MissileState = MissileStates.Boost;

            ExecuteNextStage();

            MissileState = MissileStates.Cruise;

            _missileIgnited = true;
            RadarWarningReceiver.WarnMissileLaunch(MissileReferenceTransform.position, GetForwardTransform(), TargetingMode == TargetingModes.Radar);
        }

        private bool ShouldExecuteNextStage()
        {
            if (!_missileIgnited) return false;
            if (TimeIndex < 1) return false;

            // Replaced Linq expression...
            using (List<Part>.Enumerator parts = vessel.parts.GetEnumerator())
                while (parts.MoveNext())
                {
                    if (parts.Current == null || !IsEngine(parts.Current)) continue;
                    if (EngineIgnitedAndHasFuel(parts.Current))
                    {
                        return false;
                    }
                }

            //If the next stage is greater than the number defined of stages the missile is done
            if (_nextStage > StagesNumber)
            {
                MissileState = MissileStates.PostThrust;
                return false;
            }

            return true;
        }

        public bool IsEngine(Part p)
        {
            using (List<PartModule>.Enumerator m = p.Modules.GetEnumerator())
                while (m.MoveNext())
                {
                    if (m.Current == null) continue;
                    if (m.Current is ModuleEngines) return true;
                }
            return false;
        }

        public static bool EngineIgnitedAndHasFuel(Part p)
        {
            using (List<PartModule>.Enumerator m = p.Modules.GetEnumerator())
                while (m.MoveNext())
                {
                    PartModule pm = m.Current;
                    ModuleEngines eng = pm as ModuleEngines;
                    if (eng != null)
                    {
                        return (eng.EngineIgnited && (!eng.getFlameoutState || eng.flameoutBar == 0 || eng.status == "Nominal"));
                    }
                }
            return false;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            SetupsFields();

            if (string.IsNullOrEmpty(GetShortName()))
            {
                shortName = "Unnamed";
            }

            part.force_activate();
            RefreshGuidanceMode();

            UpdateTargetingMode((TargetingModes)Enum.Parse(typeof(TargetingModes), _targetingLabel));

            _targetDecoupler = FindFirstDecoupler(part.parent, null);

            DisableResourcesFlow();

            weaponClass = WeaponClasses.Missile;
            WeaponName = GetShortName();
            if (HighLogic.LoadedSceneIsFlight) missileName = shortName;
            activeRadarRange = ActiveRadarRange;
            chaffEffectivity = ChaffEffectivity;
            //TODO: BDModularGuidance should be configurable?
            heatThreshold = 50;
            lockedSensorFOV = 5;
            radarLOAL = true;

            // fill lockedSensorFOVBias with default values if not set by part config:
            if ((TargetingMode == TargetingModes.Heat || TargetingModeTerminal == TargetingModes.Heat) && heatThreshold > 0 && lockedSensorFOVBias.minTime == float.MaxValue)
            {
                float a = lockedSensorFOV / 2f;
                float b = -1f * ((1f - 1f / 1.2f));
                float[] x = new float[6] { 0f * a, 0.2f * a, 0.4f * a, 0.6f * a, 0.8f * a, 1f * a };
                if (BDArmorySettings.DEBUG_MISSILES)
                    Debug.Log($"[BDArmory.BDModularGuidance]: OnStart missile {shortName}: setting default lockedSensorFOVBias curve to:");
                for (int i = 0; i < 6; i++)
                {
                    lockedSensorFOVBias.Add(x[i], b / (a * a) * x[i] * x[i] + 1f, -1f / 3f * x[i] / (a * a), -1f / 3f * x[i] / (a * a));
                    if (BDArmorySettings.DEBUG_MISSILES)
                        Debug.Log("key = " + x[i] + " " + (b / (a * a) * x[i] * x[i] + 1f) + " " + (-1f / 3f * x[i] / (a * a)) + " " + (-1f / 3f * x[i] / (a * a)));
                }
            }

            // fill lockedSensorVelocityBias with default values if not set by part config:
            if ((TargetingMode == TargetingModes.Heat || TargetingModeTerminal == TargetingModes.Heat) && heatThreshold > 0 && lockedSensorVelocityBias.minTime == float.MaxValue)
            {
                lockedSensorVelocityBias.Add(0f, 1f);
                lockedSensorVelocityBias.Add(180f, 1f);
                if (BDArmorySettings.DEBUG_MISSILES)
                {
                    Debug.Log($"[BDArmory.BDModularGuidance]: OnStart missile {shortName}: setting default lockedSensorVelocityBias curve to:");
                    Debug.Log("key = 0 1");
                    Debug.Log("key = 180 1");
                }
            }

            // fill activeRadarLockTrackCurve with default values if not set by part config:
            if ((TargetingMode == TargetingModes.Radar || TargetingModeTerminal == TargetingModes.Radar) && activeRadarRange > 0 && activeRadarLockTrackCurve.minTime == float.MaxValue)
            {
                activeRadarLockTrackCurve.Add(0f, 0f);
                activeRadarLockTrackCurve.Add(activeRadarRange, RadarUtils.MISSILE_DEFAULT_LOCKABLE_RCS);           // TODO: tune & balance constants!
                if (BDArmorySettings.DEBUG_MISSILES)
                    Debug.Log($"[BDArmory.BDModularGuidance]: OnStart missile {shortName}: setting default locktrackcurve with maxrange/minrcs: {activeRadarLockTrackCurve.maxTime} / {RadarUtils.MISSILE_DEFAULT_LOCKABLE_RCS}");
            }

            var explosiveParts = VesselModuleRegistry.GetModules<BDExplosivePart>(vessel);
            if (explosiveParts != null)
            {
                foreach (var explosivePart in explosiveParts)
                {
                    if (warheadYield < explosivePart.blastRadius) warheadYield = explosivePart.blastRadius;
                }
            }
        }

        private void SetupsFields()
        {
            Events["HideUI"].active = false;
            Events["ShowUI"].active = true;

            if (isTimed)
            {
                Fields["detonationTime"].guiActive = true;
                Fields["detonationTime"].guiActiveEditor = true;
            }
            else
            {
                Fields["detonationTime"].guiActive = false;
                Fields["detonationTime"].guiActiveEditor = false;
            }

            if (HighLogic.LoadedSceneIsEditor)
            {
                WeaponNameWindow.OnActionGroupEditorOpened.Add(OnActionGroupEditorOpened);
                WeaponNameWindow.OnActionGroupEditorClosed.Add(OnActionGroupEditorClosed);
                Fields["CruiseAltitude"].guiActiveEditor = true;
                Fields["CruiseSpeed"].guiActiveEditor = false;
                Events["SwitchTargetingMode"].guiActiveEditor = true;
                Events["SwitchGuidanceMode"].guiActiveEditor = true;
            }
            else
            {
                Fields["CruiseAltitude"].guiActiveEditor = false;
                Fields["CruiseSpeed"].guiActiveEditor = false;
                Events["SwitchTargetingMode"].guiActiveEditor = false;
                Events["SwitchGuidanceMode"].guiActiveEditor = false;
                SetMissileTransform();
            }

            UI_FloatRange staticMin = (UI_FloatRange)Fields["minStaticLaunchRange"].uiControlEditor;
            UI_FloatRange staticMax = (UI_FloatRange)Fields["maxStaticLaunchRange"].uiControlEditor;
            UI_FloatRange radarMax = (UI_FloatRange)Fields["ActiveRadarRange"].uiControlEditor;

            staticMin.onFieldChanged += OnStaticRangeUpdated;
            staticMax.onFieldChanged += OnStaticRangeUpdated;
            staticMax.maxValue = BDArmorySettings.MAX_ENGAGEMENT_RANGE;
            staticMax.stepIncrement = BDArmorySettings.MAX_ENGAGEMENT_RANGE / 100;
            radarMax.maxValue = BDArmorySettings.MAX_ENGAGEMENT_RANGE;
            radarMax.stepIncrement = BDArmorySettings.MAX_ENGAGEMENT_RANGE / 100;

            UI_FloatRange stageOnProximity = (UI_FloatRange)Fields["StageToTriggerOnProximity"].uiControlEditor;
            stageOnProximity.onFieldChanged = OnStageOnProximity;

            OnStageOnProximity(Fields["StageToTriggerOnProximity"], null);
            InitializeEngagementRange(minStaticLaunchRange, maxStaticLaunchRange);
        }

        private void OnStageOnProximity(BaseField baseField, object o)
        {
            UI_FloatRange detonationDistance = (UI_FloatRange)Fields["DetonationDistance"].uiControlEditor;

            if (StageToTriggerOnProximity != 0)
            {
                detonationDistance = (UI_FloatRange)Fields["DetonationDistance"].uiControlEditor;

                detonationDistance.maxValue = 8000;

                detonationDistance.stepIncrement = 50;
            }
            else
            {
                detonationDistance.maxValue = 100;

                detonationDistance.stepIncrement = 1;
            }
        }

        private void OnStaticRangeUpdated(BaseField baseField, object o)
        {
            InitializeEngagementRange(minStaticLaunchRange, maxStaticLaunchRange);
        }

        private void UpdateTargetingMode(TargetingModes newTargetingMode)
        {
            if (newTargetingMode == TargetingModes.Radar)
            {
                Fields["ActiveRadarRange"].guiActive = true;
                Fields["ActiveRadarRange"].guiActiveEditor = true;
            }
            else
            {
                Fields["ActiveRadarRange"].guiActive = false;
                Fields["ActiveRadarRange"].guiActiveEditor = false;
            }
            TargetingMode = newTargetingMode;
            _targetingLabel = newTargetingMode.ToString();

            GUIUtils.RefreshAssociatedWindows(part);
        }

        private void OnDestroy()
        {
            WeaponNameWindow.OnActionGroupEditorOpened.Remove(OnActionGroupEditorOpened);
            WeaponNameWindow.OnActionGroupEditorClosed.Remove(OnActionGroupEditorClosed);
            GameEvents.onPartDie.Remove(PartDie);
            if (_velocityTransform != null) { Destroy(_velocityTransform.gameObject); }
        }

        private void SetMissileTransform()
        {
            MissileReferenceTransform = part.transform;
            ForwardTransformAxis = (TransformAxisVectors)Enum.Parse(typeof(TransformAxisVectors), ForwardTransform);
            UpTransformAxis = (TransformAxisVectors)Enum.Parse(typeof(TransformAxisVectors), UpTransform);
        }

        void UpdateGuidance()
        {
            if (guidanceActive)
            {
                switch (TargetingMode)
                {
                    case TargetingModes.None:
                        if (_targetVessel != null)
                        {
                            TargetPosition = _targetVessel.CurrentCoM;
                            TargetVelocity = _targetVessel.Velocity();
                            TargetAcceleration = _targetVessel.acceleration;
                        }
                        break;

                    case TargetingModes.Radar:
                        UpdateRadarTarget();
                        break;

                    case TargetingModes.Heat:
                        UpdateHeatTarget();
                        break;

                    case TargetingModes.Laser:
                        UpdateLaserTarget();
                        break;

                    case TargetingModes.Gps:
                        UpdateGPSTarget();
                        break;

                    case TargetingModes.AntiRad:
                        UpdateAntiRadiationTarget();
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private Vector3 AAMGuidance()
        {
            Vector3 aamTarget;
            if (TargetAcquired)
            {
                float timeToImpact;
                if (GuidanceIndex == 6) // Augmented Pro-Nav
                    aamTarget = MissileGuidance.GetAPNTarget(TargetPosition, TargetVelocity, TargetAcceleration, vessel, 3f, out timeToImpact);
                else if (GuidanceIndex == 5) // Pro-Nav
                    aamTarget = MissileGuidance.GetPNTarget(TargetPosition, TargetVelocity, vessel, 3f, out timeToImpact);
                else // AAM Lead
                    aamTarget = MissileGuidance.GetAirToAirTargetModular(TargetPosition, TargetVelocity, TargetAcceleration, vessel, out timeToImpact);
                TimeToImpact = timeToImpact;

                if (Vector3.Angle(aamTarget - vessel.CoM, vessel.transform.forward) > maxOffBoresight * 0.75f)
                {
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.LogFormat("[BDArmory.BDModularGuidance]: Missile with Name={0} has exceeded the max off boresight, checking missed target ", vessel.vesselName);
                    aamTarget = TargetPosition;
                }
                DrawDebugLine(vessel.CoM, aamTarget);
            }
            else
            {
                aamTarget = vessel.CoM + (20 * vessel.srfSpeed * vessel.Velocity().normalized);
            }

            return aamTarget;
        }

        private Vector3 AGMGuidance()
        {
            if (TargetingMode != TargetingModes.Gps)
            {
                if (TargetAcquired)
                {
                    //lose lock if seeker reaches gimbal limit
                    float targetViewAngle = Vector3.Angle(vessel.transform.forward, TargetPosition - vessel.CoM);

                    if (targetViewAngle > maxOffBoresight)
                    {
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.BDModularGuidance]: AGM Missile guidance failed - target out of view");
                        guidanceActive = false;
                    }
                }
                else
                {
                    if (TargetingMode == TargetingModes.Laser)
                    {
                        //keep going straight until found laser point
                        TargetPosition = laserStartPosition + (20000 * startDirection);
                    }
                }
            }
            Vector3 agmTarget = MissileGuidance.GetAirToGroundTarget(TargetPosition, TargetVelocity, vessel, 1.85f);
            return agmTarget;
        }

        private Vector3 CruiseGuidance()
        {
            if (this._guidance == null)
            {
                this._guidance = new CruiseGuidance(this);
            }

            return this._guidance.GetDirection(this, TargetPosition, TargetVelocity);
        }

        private void CheckMiss(Vector3 targetPosition)
        {
            if (HasMissed) return;
            if (MissileState != MissileStates.PostThrust) return;
            // if I'm to close to my vessel avoid explosion
            if ((vessel.CoM - SourceVessel.CoM).magnitude < 4 * DetonationDistance) return;
            // if I'm getting closer to my target avoid explosion
            if ((vessel.CoM - targetPosition).sqrMagnitude >
                (vessel.CoM + (vessel.Velocity() * Time.fixedDeltaTime) - (targetPosition + (TargetVelocity * Time.fixedDeltaTime))).sqrMagnitude) return;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.BDModularGuidance]: Missile CheckMiss showed miss for {vessel.vesselName} ({SourceVessel}) with target at {targetPosition - vessel.CoM:G3}");

            var pilotAI = VesselModuleRegistry.GetModule<BDModulePilotAI>(vessel); // Get the pilot AI if the  missile has one.
            if (pilotAI != null)
            {
                ResetMissile();
                pilotAI.ActivatePilot();
                return;
            }

            HasMissed = true;
            guidanceActive = false;
            TargetMf = null;
            isTimed = true;
            detonationTime = TimeIndex + 1.5f;
            if (BDArmorySettings.CAMERA_SWITCH_INCLUDE_MISSILES && vessel.isActiveVessel) LoadedVesselSwitcher.Instance.TriggerSwitchVessel();
        }

        private void ResetMissile()
        {
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.BDModularGuidance]: Resetting missile {vessel.vesselName}");
            heatTarget = TargetSignatureData.noTarget;
            vrd = null;
            radarTarget = TargetSignatureData.noTarget;
            HasFired = false;
            StagesNumber = 1;
            _nextStage = 1;
            TargetAcquired = false;
            TargetMf = null;
            TimeFired = -1;
            _missileIgnited = false;
            lockFailTimer = -1;
            guidanceActive = false;
            HasMissed = false;
            HasExploded = false;
            DetonationDistanceState = DetonationDistanceStates.Cruising;
            BDATargetManager.FiredMissiles.Remove(this);
            MissileState = MissileStates.Idle;
            if (mfChecked && weaponManager != null)
            {
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.BDModularGuidance]: disabling target lock for {vessel.vesselName}");
                weaponManager.guardFiringMissile = false; // Disable target lock.
                mfChecked = false;
            }
        }

        private void CheckMiss()
        {
            if (HasMissed) return;

            if (MissileState == MissileStates.PostThrust && (vessel.LandedOrSplashed || vessel.Velocity().magnitude < 10f))
            {
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.BDModularGuidance]: Missile CheckMiss showed miss for {vessel.vesselName}");

                var pilotAI = VesselModuleRegistry.GetModule<BDModulePilotAI>(vessel); // Get the pilot AI if the  missile has one.
                if (pilotAI != null)
                {
                    ResetMissile();
                    pilotAI.ActivatePilot();
                    return;
                }

                HasMissed = true;
                guidanceActive = false;
                TargetMf = null;
                isTimed = true;
                detonationTime = TimeIndex + 1.5f;
                if (BDArmorySettings.CAMERA_SWITCH_INCLUDE_MISSILES && vessel.isActiveVessel) LoadedVesselSwitcher.Instance.TriggerSwitchVessel();
            }
        }


        public void GuidanceSteer(FlightCtrlState s)
        {
            FloatingOriginCorrection();
            debugString.Length = 0;
            if (guidanceActive && MissileReferenceTransform != null && _velocityTransform != null)
            {
                if (!mfChecked)
                {
                    weaponManager = VesselModuleRegistry.GetModule<MissileFire>(vessel);
                    mfChecked = true;
                }
                if (mfChecked && weaponManager != null && !weaponManager.guardFiringMissile)
                {
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.BDModularGuidance]: enabling target lock for {vessel.vesselName}");
                    weaponManager.guardFiringMissile = true; // Enable target lock.
                }

                if (vessel.Velocity().magnitude < MinSpeedGuidance)
                {
                    if (!_minSpeedAchieved)
                    {
                        s.mainThrottle = 1;
                        return;
                    }
                }
                else
                {
                    _minSpeedAchieved = true;
                }

                Vector3 newTargetPosition = new Vector3();
                switch (GuidanceIndex)
                {
                    case 1:
                        newTargetPosition = AAMGuidance();
                        break;

                    case 2:
                        newTargetPosition = AGMGuidance();
                        break;

                    case 3:
                        newTargetPosition = CruiseGuidance();
                        break;

                    case 4:
                        newTargetPosition = BallisticGuidance();
                        break;
                    case 5:
                        newTargetPosition = AAMGuidance();
                        break;
                    case 6:
                        newTargetPosition = AAMGuidance();
                        break;
                }
                CheckMiss(newTargetPosition);

                //Updating aero surfaces
                if (TimeIndex > dropTime + 0.5f)
                {
                    _velocityTransform.rotation = Quaternion.LookRotation(vessel.Velocity(), -vessel.transform.forward);
                    Vector3 targetDirection = _velocityTransform.InverseTransformPoint(newTargetPosition).normalized;
                    targetDirection = Vector3.RotateTowards(Vector3.forward, targetDirection, 15 * Mathf.Deg2Rad, 0);

                    Vector3 localAngVel = vessel.angularVelocity;
                    float steerYaw = SteerMult * targetDirection.x - SteerDamping * -localAngVel.z;
                    float steerPitch = SteerMult * targetDirection.y - SteerDamping * -localAngVel.x;

                    s.yaw = Mathf.Clamp(steerYaw, -MaxSteer, MaxSteer);
                    s.pitch = Mathf.Clamp(steerPitch, -MaxSteer, MaxSteer);

                    if (RollCorrection)
                    {
                        SetRoll();
                        s.roll = Roll;
                    }
                }
                s.mainThrottle = Throttle;

                CheckMiss();
            }
        }

        private void SetRoll()
        {
            var vesselTransform = vessel.transform.position;

            Vector3 gravityVector = FlightGlobals.getGeeForceAtPosition(vesselTransform).normalized;
            Vector3 rollVessel = -vessel.transform.right.normalized;

            var currentAngle = Vector3.SignedAngle(rollVessel, gravityVector, Vector3.Cross(rollVessel, gravityVector)) - 90f;

            this.angularVelocity = currentAngle - this.lastRollAngle;
            //this.angularAcceleration = angularVelocity - this.lasAngularVelocity;

            var futureAngle = currentAngle + angularVelocity / Time.fixedDeltaTime * 1f;

            if (futureAngle > 0.5f || currentAngle > 0.5f)
            {
                this.Roll = Mathf.Clamp(Roll - 0.001f, -1f, 0f);
            }
            else if (futureAngle < -0.5f || currentAngle < -0.5f)
            {
                this.Roll = Mathf.Clamp(Roll + 0.001f, 0, 1f);
            }

            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
            {
                debugString.AppendLine($"Roll angle: {currentAngle}");
                debugString.AppendLine($"future Roll angle: {futureAngle}");
                debugString.AppendLine($"Roll value: {this.Roll}");
            }
            lastRollAngle = currentAngle;
            //lasAngularVelocity = angularVelocity;
        }

        public float Roll { get; set; }

        private Vector3 BallisticGuidance()
        {
            return CalculateAGMBallisticGuidance(this, TargetPosition);
        }

        private void UpdateMenus(bool visible)
        {
            Events["HideUI"].active = visible;
            Events["ShowUI"].active = !visible;
        }

        private void OnActionGroupEditorOpened()
        {
            Events["HideUI"].active = false;
            Events["ShowUI"].active = false;
        }

        private void OnActionGroupEditorClosed()
        {
            Events["HideUI"].active = false;
            Events["ShowUI"].active = true;
        }

        /// <summary>
        ///     Recursive method to find the top decoupler that should be used to jettison the missile.
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="last"></param>
        /// <returns></returns>
        public static PartModule FindFirstDecoupler(Part parent, PartModule last)
        {
            if (parent == null || !parent) return last;

            PartModule newModuleDecouple = parent.FindModuleImplementing<ModuleDecouple>();
            if (newModuleDecouple == null)
            {
                newModuleDecouple = parent.FindModuleImplementing<ModuleAnchoredDecoupler>();
            }
            if (newModuleDecouple != null && newModuleDecouple)
            {
                return FindFirstDecoupler(parent.parent, newModuleDecouple);
            }
            return FindFirstDecoupler(parent.parent, last);
        }

        /// <summary>
        ///     This method will execute the next ActionGroup. Due to StageManager is designed to work with an active vessel
        ///     And a missile is not an active vessel. I had to use a different way handle stages. And action groups works perfect!
        /// </summary>
        public void ExecuteNextStage()
        {
            if (BDArmorySettings.DEBUG_MISSILES) Debug.LogFormat("[BDArmory.BDModularGuidance]: Executing next stage {0} for {1}", _nextStage, vessel.vesselName);
            vessel.ActionGroups.ToggleGroup(
                (KSPActionGroup)Enum.Parse(typeof(KSPActionGroup), "Custom0" + (int)_nextStage));

            _nextStage++;

            vessel.OnFlyByWire += GuidanceSteer;

            //todo: find a way to fly by wire vessel decoupled
        }

        protected override void OnGUI()
        {
            base.OnGUI();
            if (HighLogic.LoadedSceneIsFlight)
            {
                drawLabels();
            }
        }

        #region KSP ACTIONS

        [KSPAction("Fire Missile")]
        public void AgFire(KSPActionParam param)
        {
            FireMissile();
        }

        /// <summary>
        ///     Reset the missile if it has a pilot AI.
        /// </summary>
        [KSPAction("Reset Missile")]
        public void AGReset(KSPActionParam param)
        {
            var pilotAI = VesselModuleRegistry.GetModule<BDModulePilotAI>(vessel); // Get the pilot AI if the  missile has one.
            if (pilotAI != null)
            {
                ResetMissile();
                pilotAI.ActivatePilot();
            }
        }

        #endregion KSP ACTIONS

        #region KSP EVENTS

        [KSPEvent(guiActive = true, guiName = "#LOC_BDArmory_FireMissile", active = true)]//Fire Missile
        public void GuiFire()
        {
            FireMissile();
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_FireMissile", active = true)]//Fire Missile
        public override void FireMissile()
        {
            if (BDArmorySetup.Instance.ActiveWeaponManager != null &&
                BDArmorySetup.Instance.ActiveWeaponManager.vessel == vessel)
            {
                BDArmorySetup.Instance.ActiveWeaponManager.SendTargetDataToMissile(this);
            }

            if (!HasFired)
            {
                GameEvents.onPartDie.Add(PartDie);
                BDATargetManager.FiredMissiles.Add(this);

                var wpm = VesselModuleRegistry.GetMissileFire(vessel, true);
                if (wpm != null) Team = wpm.Team;

                SourceVessel = vessel;
                SetTargeting();
                Jettison();
                AddTargetInfoToVessel();
                IncreaseTolerance();

                this.initialMissileRollPlane = -this.vessel.transform.up;
                this.initialMissileForward = this.vessel.transform.forward;
                vessel.vesselName = GetShortName();
                vessel.vesselType = VesselType.Plane;

                if (!vessel.ActionGroups[KSPActionGroup.SAS])
                {
                    vessel.ActionGroups.ToggleGroup(KSPActionGroup.SAS);
                }

                TimeFired = Time.time;
                guidanceActive = true;
                MissileState = MissileStates.Drop;

                GUIUtils.RefreshAssociatedWindows(part);

                HasFired = true;
                DetonationDistanceState = DetonationDistanceStates.NotSafe;
                if (vessel.atmDensity < 0.05)
                {
                    vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);
                }
                if (BDArmorySettings.CAMERA_SWITCH_INCLUDE_MISSILES && SourceVessel.isActiveVessel) LoadedVesselSwitcher.Instance.ForceSwitchVessel(vessel);
            }
            if (BDArmorySetup.Instance.ActiveWeaponManager != null)
            {
                BDArmorySetup.Instance.ActiveWeaponManager.UpdateList();
            }
        }

        private void IncreaseTolerance()
        {
            foreach (var vesselPart in this.vessel.parts)
            {
                vesselPart.crashTolerance = 99;
                vesselPart.breakingForce = 99;
                vesselPart.breakingTorque = 99;
            }
        }

        private void SetTargeting()
        {
            startDirection = GetForwardTransform();
            SetLaserTargeting();
            SetAntiRadTargeting();
        }

        void OnDisable()
        {
            if (TargetingMode == TargetingModes.AntiRad)
            {
                RadarWarningReceiver.OnRadarPing -= ReceiveRadarPing;
            }
        }

        public Vector3 StartDirection { get; set; }

        [KSPEvent(guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_GuidanceMode", active = true)]//Guidance Mode
        public void SwitchGuidanceMode()
        {
            GuidanceIndex++;
            if (GuidanceIndex > 6)
            {
                GuidanceIndex = 1;
            }

            RefreshGuidanceMode();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetingMode", active = true)]//Targeting Mode
        public void SwitchTargetingMode()
        {
            string[] targetingModes = Enum.GetNames(typeof(TargetingModes));

            int currentIndex = targetingModes.IndexOf(TargetingMode.ToString());

            if (currentIndex < targetingModes.Length - 1)
            {
                UpdateTargetingMode((TargetingModes)Enum.Parse(typeof(TargetingModes), targetingModes[currentIndex + 1]));
            }
            else
            {
                UpdateTargetingMode((TargetingModes)Enum.Parse(typeof(TargetingModes), targetingModes[0]));
            }
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, active = true, guiName = "#LOC_BDArmory_Jettison")]//Jettison
        public override void Jettison()
        {
            if (_targetDecoupler == null || !_targetDecoupler || !(_targetDecoupler is IStageSeparator)) return;

            ModuleDecouple decouple = _targetDecoupler as ModuleDecouple;
            if (decouple != null)
            {
                decouple.ejectionForce *= 5;
                decouple.Decouple();
            }
            else
            {
                ((ModuleAnchoredDecoupler)_targetDecoupler).ejectionForce *= 5;
                ((ModuleAnchoredDecoupler)_targetDecoupler).Decouple();
            }

            if (BDArmorySetup.Instance.ActiveWeaponManager != null)
                BDArmorySetup.Instance.ActiveWeaponManager.UpdateList();
        }

        public override float GetBlastRadius()
        {
            if (VesselModuleRegistry.GetModuleCount<BDExplosivePart>(vessel) > 0)
            {
                return VesselModuleRegistry.GetModules<BDExplosivePart>(vessel).Max(x => x.blastRadius);
            }
            else
            {
                return 5;
            }
        }

        protected override void PartDie(Part p)
        {
            if (p != part) return;
            AutoDestruction();
            BDATargetManager.FiredMissiles.Remove(this);
            GameEvents.onPartDie.Remove(PartDie);
        }

        private void AutoDestruction()
        {
            var parts = this.vessel.Parts.ToArray();
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                if (parts[i] != null)
                    parts[i].explode();
            }

            parts = null;
        }

        public override void Detonate()
        {
            if (HasExploded || !HasFired) return;
            if (SourceVessel == null) SourceVessel = vessel;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.BDModularGuidance]: Detonating missile {vessel.vesselName} ({SourceVessel})");

            if (StageToTriggerOnProximity != 0)
            {
                vessel.ActionGroups.ToggleGroup((KSPActionGroup)Enum.Parse(typeof(KSPActionGroup), "Custom0" + (int)StageToTriggerOnProximity));
                HasExploded = true;
            }
            else
            {
                var explosiveParts = VesselModuleRegistry.GetModules<BDExplosivePart>(vessel);
                if (explosiveParts != null)
                {
                    foreach (var explosivePart in explosiveParts)
                    { if (!explosivePart.manualOverride) explosivePart.DetonateIfPossible(); }
                    if (explosiveParts.Any(explosivePart => explosivePart.hasDetonated))
                    {
                        HasExploded = true;
                        AutoDestruction();
                    }
                }
            }
        }

        public override Vector3 GetForwardTransform()
        {
            return GetTransform(ForwardTransformAxis);
        }

        public Vector3 GetTransform(TransformAxisVectors transformAxis)
        {
            switch (transformAxis)
            {
                case TransformAxisVectors.UpPositive:
                    return MissileReferenceTransform.up;

                case TransformAxisVectors.UpNegative:
                    return -MissileReferenceTransform.up;

                case TransformAxisVectors.ForwardPositive:
                    return MissileReferenceTransform.forward;

                case TransformAxisVectors.ForwardNegative:
                    return -MissileReferenceTransform.forward;

                case TransformAxisVectors.RightNegative:
                    return -MissileReferenceTransform.right;

                case TransformAxisVectors.RightPositive:
                    return MissileReferenceTransform.right;

                default:
                    return MissileReferenceTransform.forward;
            }
        }

        [KSPEvent(guiActiveEditor = true, guiName = "#LOC_BDArmory_HideUI", active = false)]//Hide Weapon Name UI
        public void HideUI()
        {
            WeaponNameWindow.HideGUI();
            UpdateMenus(false);
        }

        [KSPEvent(guiActiveEditor = true, guiName = "#LOC_BDArmory_ShowUI", active = false)]//Set Weapon Name UI
        public void ShowUI()
        {
            WeaponNameWindow.ShowGUI(this);
            UpdateMenus(true);
        }

        void OnCollisionEnter(Collision col)
        {
            base.CollisionEnter(col);
        }

        #endregion KSP EVENTS
    }

    #region UI

    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class WeaponNameWindow : MonoBehaviour
    {
        internal static EventVoid OnActionGroupEditorOpened = new EventVoid("OnActionGroupEditorOpened");
        internal static EventVoid OnActionGroupEditorClosed = new EventVoid("OnActionGroupEditorClosed");

        private static GUIStyle unchanged;
        private static GUIStyle changed;
        private static GUIStyle greyed;
        private static GUIStyle overfull;

        private static WeaponNameWindow instance;
        private static Vector3 mousePos = Vector3.zero;

        private bool ActionGroupMode;

        private Rect guiWindowRect = new Rect(0, 0, 0, 0);

        private BDModularGuidance missile_module;

        [KSPField] public int offsetGUIPos = -1;

        private Vector2 scrollPos;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiActive = false, guiName = "#LOC_BDArmory_RollCorrection_showRFGUI"), UI_Toggle(enabledText = "#LOC_BDArmory_showRFGUI_enabledText", disabledText = "#LOC_BDArmory_showRFGUI_disabledText")][NonSerialized] public bool showRFGUI;//Show Weapon Name Editor--Weapon Name GUI--GUI

        private bool styleSetup;

        private string txtName = string.Empty;

        public static void HideGUI()
        {
            if (instance != null && instance.missile_module != null)
            {
                instance.missile_module.WeaponName = instance.missile_module.shortName;
                instance.missile_module = null;
                instance.UpdateGUIState();
            }
            EditorLogic editor = EditorLogic.fetch;
            if (editor != null)
                editor.Unlock("BD_MN_GUILock");
        }

        public static void ShowGUI(BDModularGuidance missile_module)
        {
            if (instance != null)
            {
                instance.missile_module = missile_module;
                instance.UpdateGUIState();
            }
        }

        private void UpdateGUIState()
        {
            enabled = missile_module != null;
            EditorLogic editor = EditorLogic.fetch;
            if (!enabled && editor != null)
                editor.Unlock("BD_MN_GUILock");
        }

        private IEnumerator<YieldInstruction> CheckActionGroupEditor()
        {
            while (EditorLogic.fetch == null)
            {
                yield return null;
            }
            EditorLogic editor = EditorLogic.fetch;
            while (EditorLogic.fetch != null)
            {
                if (editor.editorScreen == EditorScreen.Actions)
                {
                    if (!ActionGroupMode)
                    {
                        HideGUI();
                        OnActionGroupEditorOpened.Fire();
                    }
                    EditorActionGroups age = EditorActionGroups.Instance;
                    if (missile_module && !age.GetSelectedParts().Contains(missile_module.part))
                    {
                        HideGUI();
                    }
                    ActionGroupMode = true;
                }
                else
                {
                    if (ActionGroupMode)
                    {
                        HideGUI();
                        OnActionGroupEditorClosed.Fire();
                    }
                    ActionGroupMode = false;
                }
                yield return null;
            }
        }

        private void Awake()
        {
            enabled = false;
            instance = this;
        }

        void Start()
        {
            StartCoroutine(CheckActionGroupEditor());
        }

        private void OnDestroy()
        {
            instance = null;
        }

        public void OnGUI()
        {
            if (!styleSetup)
            {
                styleSetup = true;
                Styles.InitStyles();
            }

            EditorLogic editor = EditorLogic.fetch;
            if (!HighLogic.LoadedSceneIsEditor || !editor)
            {
                return;
            }
            bool cursorInGUI = false; // nicked the locking code from Ferram
            mousePos = Input.mousePosition; //Mouse location; based on Kerbal Engineer Redux code
            mousePos.y = Screen.height - mousePos.y;

            int posMult = 0;
            if (offsetGUIPos != -1)
            {
                posMult = offsetGUIPos;
            }
            if (ActionGroupMode)
            {
                if (guiWindowRect.width == 0)
                {
                    guiWindowRect = new Rect(430 * posMult, 365, 438, 50);
                }
                new Rect(guiWindowRect.xMin + 440, mousePos.y - 5, 300, 20);
            }
            else
            {
                if (guiWindowRect.width == 0)
                {
                    //guiWindowRect = new Rect(Screen.width - 8 - 430 * (posMult + 1), 365, 438, (Screen.height - 365));
                    guiWindowRect = new Rect(Screen.width - 8 - 430 * (posMult + 1), 365, 438, 50);
                }
                new Rect(guiWindowRect.xMin - (230 - 8), mousePos.y - 5, 220, 20);
            }
            cursorInGUI = guiWindowRect.Contains(mousePos);
            if (cursorInGUI)
            {
                editor.Lock(false, false, false, "BD_MN_GUILock");
                //if (EditorTooltip.Instance != null)
                //    EditorTooltip.Instance.HideToolTip();
            }
            else
            {
                editor.Unlock("BD_MN_GUILock");
            }
            guiWindowRect = GUILayout.Window(GUIUtility.GetControlID(FocusType.Passive), guiWindowRect, GUIWindow, "Weapon Name GUI", Styles.styleEditorPanel);
        }

        public void GUIWindow(int windowID)
        {
            InitializeStyles();

            GUILayout.BeginVertical();
            GUILayout.Space(20);

            GUILayout.BeginHorizontal();

            GUILayout.Label("Weapon Name: ");

            txtName = GUILayout.TextField(txtName);

            if (GUILayout.Button("Save & Close"))
            {
                missile_module.WeaponName = txtName;
                missile_module.shortName = txtName;
                instance.missile_module.HideUI();
            }

            GUILayout.EndHorizontal();

            scrollPos = GUILayout.BeginScrollView(scrollPos);

            GUILayout.EndScrollView();

            GUILayout.EndVertical();

            GUI.DragWindow();
            GUIUtils.RepositionWindow(ref guiWindowRect);
        }

        private static void InitializeStyles()
        {
            if (unchanged == null)
            {
                if (GUI.skin == null)
                {
                    unchanged = new GUIStyle();
                    changed = new GUIStyle();
                    greyed = new GUIStyle();
                    overfull = new GUIStyle();
                }
                else
                {
                    unchanged = new GUIStyle(GUI.skin.textField);
                    changed = new GUIStyle(GUI.skin.textField);
                    greyed = new GUIStyle(GUI.skin.textField);
                    overfull = new GUIStyle(GUI.skin.label);
                }

                unchanged.normal.textColor = Color.white;
                unchanged.active.textColor = Color.white;
                unchanged.focused.textColor = Color.white;
                unchanged.hover.textColor = Color.white;

                changed.normal.textColor = Color.yellow;
                changed.active.textColor = Color.yellow;
                changed.focused.textColor = Color.yellow;
                changed.hover.textColor = Color.yellow;

                greyed.normal.textColor = Color.gray;

                overfull.normal.textColor = Color.red;
            }
        }
    }

    internal class Styles
    {
        // Base styles
        public static GUIStyle styleEditorTooltip;
        public static GUIStyle styleEditorPanel;

        /// <summary>
        ///     This one sets up the styles we use
        /// </summary>
        internal static void InitStyles()
        {
            styleEditorTooltip = new GUIStyle();
            styleEditorTooltip.name = "Tooltip";
            styleEditorTooltip.fontSize = 12;
            styleEditorTooltip.normal.textColor = new Color32(207, 207, 207, 255);
            styleEditorTooltip.stretchHeight = true;
            styleEditorTooltip.wordWrap = true;
            styleEditorTooltip.normal.background = CreateColorPixel(new Color32(7, 54, 66, 200));
            styleEditorTooltip.border = new RectOffset(3, 3, 3, 3);
            styleEditorTooltip.padding = new RectOffset(4, 4, 6, 4);
            styleEditorTooltip.alignment = TextAnchor.MiddleLeft;

            styleEditorPanel = new GUIStyle();
            styleEditorPanel.normal.background = CreateColorPixel(new Color32(7, 54, 66, 200));
            styleEditorPanel.border = new RectOffset(27, 27, 27, 27);
            styleEditorPanel.padding = new RectOffset(10, 10, 10, 10);
            styleEditorPanel.normal.textColor = new Color32(147, 161, 161, 255);
            styleEditorPanel.fontSize = 12;
        }

        /// <summary>
        ///     Creates a 1x1 texture
        /// </summary>
        /// <param name="Background">Color of the texture</param>
        /// <returns></returns>
        internal static Texture2D CreateColorPixel(Color32 Background)
        {
            Texture2D retTex = new Texture2D(1, 1);
            retTex.SetPixel(0, 0, Background);
            retTex.Apply();
            return retTex;
        }
    }

    #endregion UI
}
