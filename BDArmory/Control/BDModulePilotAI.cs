using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

using BDArmory.Competition;
using BDArmory.Extensions;
using BDArmory.Guidances;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Weapons;
using BDArmory.Weapons.Missiles;

namespace BDArmory.Control
{
    public class BDModulePilotAI : BDGenericAIBase, IBDAIControl
    {
        public enum SteerModes
        { NormalFlight, Aiming }

        SteerModes steerMode = SteerModes.NormalFlight;

        bool extending;
        bool extendParametersSet = false;
        float extendDistance;
        bool extendHorizontally = true; // Measure the extendDistance horizonally (for A2G) or not (for A2A).
        float desiredMinAltitude;
        public string extendingReason = "";
        public Vessel extendTarget = null;

        bool requestedExtend;
        Vector3 requestedExtendTpos;
        float extendRequestMinDistance = 0;

        public bool IsExtending
        {
            get { return extending || requestedExtend; }
        }

        bool evading = false;
        bool wasEvading = false;
        public bool IsEvading => evading;

        public void StopExtending(string reason)
        {
            extending = false;
            extendingReason = "";
            extendTarget = null;
            extendRequestMinDistance = 0;
            if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI]: {vessel.vesselName} stopped extending due to {reason}.");
        }

        /// <summary>
        ///  Request extending away from a target position or vessel.
        ///  If a vessel is specified, it overrides the specified position.
        /// </summary>
        /// <param name="reason">Reason for extending</param>
        /// <param name="target">The target to extend from</param>
        /// <param name="tPosition">The position to extend from if the target is null</param>
        public void RequestExtend(string reason = "requested", Vessel target = null, float minDistance = 0, Vector3 tPosition = default)
        {
            requestedExtend = true;
            extendTarget = target;
            extendRequestMinDistance = minDistance;
            requestedExtendTpos = extendTarget != null ? target.CoM : tPosition;
            extendingReason = reason;
        }

        public override bool CanEngage()
        {
            return !vessel.LandedOrSplashed;
        }

        GameObject vobj;

        Transform velocityTransform
        {
            get
            {
                if (!vobj)
                {
                    vobj = new GameObject("velObject");
                    vobj.transform.position = vessel.ReferenceTransform.position;
                    vobj.transform.parent = vessel.ReferenceTransform;
                }

                return vobj.transform;
            }
        }

        Vector3 upDirection = Vector3.up;

        #region Pilot AI Settings GUI

        #region PID
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_SteerFactor", //Steer Factor
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 20f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float steerMult = 14f;
        //make a combat steer mult and idle steer mult

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_SteerKi", //Steer Ki
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.01f, maxValue = 1f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float steerKiAdjust = 0.4f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_SteerDamping", //Steer Damping
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float steerDamping = 5f;

        #region Dynamic Damping
        // Note: min/max is replaced by off-target/on-target in localisation, but the variable names are kept to avoid reconfiguring existing craft.
        // Dynamic Damping
        [KSPField(guiName = "#LOC_BDArmory_DynamicDamping", groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true), UI_Label(scene = UI_Scene.All)]
        private string DynamicDampingLabel = "";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_DynamicDampingMin", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float DynamicDampingMin = 6f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_DynamicDampingMax", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float DynamicDampingMax = 6.7f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_DynamicDampingFactor", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float dynamicSteerDampingFactor = 5f;

        // Dynamic Pitch
        [KSPField(guiName = "#LOC_BDArmory_DynamicDampingPitch", groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true), UI_Label(scene = UI_Scene.All)]
        private string PitchLabel = "";

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_DynamicDampingPitch", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_Toggle(scene = UI_Scene.All, enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled")]
        public bool dynamicDampingPitch = true;

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_DynamicDampingPitchMin", advancedTweakable = true, //Dynamic steer damping Clamp min
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float DynamicDampingPitchMin = 6f;

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_DynamicDampingPitchMax", advancedTweakable = true, //Dynamic steer damping Clamp max
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float DynamicDampingPitchMax = 6.5f;

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_DynamicDampingPitchFactor", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float dynamicSteerDampingPitchFactor = 8f;

        // Dynamic Yaw
        [KSPField(guiName = "#LOC_BDArmory_DynamicDampingYaw", groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true), UI_Label(scene = UI_Scene.All)]
        private string YawLabel = "";

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_DynamicDampingYaw", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_Toggle(scene = UI_Scene.All, enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled")]
        public bool dynamicDampingYaw = true;

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_DynamicDampingYawMin", advancedTweakable = true, //Dynamic steer damping Clamp min
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float DynamicDampingYawMin = 6f;

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_DynamicDampingYawMax", advancedTweakable = true, //Dynamic steer damping Clamp max
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float DynamicDampingYawMax = 6.5f;

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_DynamicDampingYawFactor", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float dynamicSteerDampingYawFactor = 8f;

        // Dynamic Roll
        [KSPField(guiName = "#LOC_BDArmory_DynamicDampingRoll", groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true), UI_Label(scene = UI_Scene.All)]
        private string RollLabel = "";

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_DynamicDampingRoll", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_Toggle(scene = UI_Scene.All, enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled")]
        public bool dynamicDampingRoll = true;

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_DynamicDampingRollMin", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float DynamicDampingRollMin = 6f;

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_DynamicDampingRollMax", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float DynamicDampingRollMax = 6.5f;

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_DynamicDampingRollFactor", advancedTweakable = true, //Dynamic steer dampening Factor
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float dynamicSteerDampingRollFactor = 8f;

        //Toggle Dynamic Steer Damping
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_DynamicSteerDamping", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_Toggle(scene = UI_Scene.All, disabledText = "#LOC_BDArmory_Disabled", enabledText = "#LOC_BDArmory_Enabled")]
        public bool dynamicSteerDamping = false;

        //Toggle 3-Axis Dynamic Steer Damping
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_3AxisDynamicSteerDamping", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All)]
        public bool CustomDynamicAxisFields = true;
        #endregion

        #region AutoTuning
        //Toggle AutoTuning
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_PIDAutoTune", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All)]
        public bool autoTune = false;
        PIDAutoTuning pidAutoTuning;

        [KSPField(guiName = "#LOC_BDArmory_AutoTuningLoss", groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true), UI_Label(scene = UI_Scene.All)]
        public string autoTuningLossLabel = "";
        [KSPField(guiName = "\tField", groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true), UI_Label(scene = UI_Scene.All)]
        public string autoTuningLossLabel2 = "";

        // //AutoTuning Loss Ratio
        // [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_PIDAutoTuningLossRatio", advancedTweakable = true,
        //     groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
        //     UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        // public float autoTuningLossRatio = 0.5f;

        //AutoTuning Learning Rate
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Learning Rate", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = .01f, maxValue = 1f, stepIncrement = .01f, scene = UI_Scene.All)]
        public float autoTuningLearningRate = 0.3f;

        //AutoTuning Number Of Samples
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_PIDAutoTuningNumSamples", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 1f, maxValue = 10f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float autoTuningNumSamples = 5f;

        //AutoTuning Roll Relevance
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_PIDAutoTuningRollRelevance", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float autoTuningRollRelevance = 0.5f;

        //AutoTuning Fast Response Relevance
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_PIDAutoTuningFastResponseRelevance", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float autoTuningFastResponseRelevance = 0.2f;

        //Toggle Fixed P
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_PIDAutoTuningFixedP", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_PilotAI_PID", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All)]
        public bool autoTuningFixedP = false;
        #endregion
        #endregion

        #region Altitudes
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_DefaultAltitude", //Default Alt.
            groupName = "pilotAI_Altitudes", groupDisplayName = "#LOC_BDArmory_PilotAI_Altitudes", groupStartCollapsed = true),
            UI_FloatRange(minValue = 50f, maxValue = 5000f, stepIncrement = 50f, scene = UI_Scene.All)]
        public float defaultAltitude = 2000;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_MinAltitude", //Min Altitude
            groupName = "pilotAI_Altitudes", groupDisplayName = "#LOC_BDArmory_PilotAI_Altitudes", groupStartCollapsed = true),
            UI_FloatRange(minValue = 10f, maxValue = 1000, stepIncrement = 10f, scene = UI_Scene.All)]
        public float minAltitude = 200f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_MaxAltitude", //Max Altitude
            groupName = "pilotAI_Altitudes", groupDisplayName = "#LOC_BDArmory_PilotAI_Altitudes", groupStartCollapsed = true),
            UI_FloatRange(minValue = 100f, maxValue = 10000, stepIncrement = 100f, scene = UI_Scene.All)]
        public float maxAltitude = 7500f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_MaxAltitude", advancedTweakable = true,
            groupName = "pilotAI_Altitudes", groupDisplayName = "#LOC_BDArmory_PilotAI_Altitudes", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All)]
        public bool maxAltitudeToggle = false;
        #endregion

        #region Speeds
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_MaxSpeed", //Max Speed
            groupName = "pilotAI_Speeds", groupDisplayName = "#LOC_BDArmory_PilotAI_Speeds", groupStartCollapsed = true),
            UI_FloatRange(minValue = 50f, maxValue = 800f, stepIncrement = 5.0f, scene = UI_Scene.All)]
        public float maxSpeed = 350;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TakeOffSpeed", //TakeOff Speed
            groupName = "pilotAI_Speeds", groupDisplayName = "#LOC_BDArmory_PilotAI_Speeds", groupStartCollapsed = true),
            UI_FloatRange(minValue = 10f, maxValue = 200f, stepIncrement = 1.0f, scene = UI_Scene.All)]
        public float takeOffSpeed = 60;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_MinSpeed", //MinCombatSpeed
            groupName = "pilotAI_Speeds", groupDisplayName = "#LOC_BDArmory_PilotAI_Speeds", groupStartCollapsed = true),
            UI_FloatRange(minValue = 10f, maxValue = 200, stepIncrement = 1.0f, scene = UI_Scene.All)]
        public float minSpeed = 60f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_StrafingSpeed", //Strafing Speed
            groupName = "pilotAI_Speeds", groupDisplayName = "#LOC_BDArmory_PilotAI_Speeds", groupStartCollapsed = true),
            UI_FloatRange(minValue = 10f, maxValue = 200, stepIncrement = 1.0f, scene = UI_Scene.All)]
        public float strafingSpeed = 100f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_IdleSpeed", //Idle Speed
            groupName = "pilotAI_Speeds", groupDisplayName = "#LOC_BDArmory_PilotAI_Speeds", groupStartCollapsed = true),
            UI_FloatRange(minValue = 10f, maxValue = 200f, stepIncrement = 1.0f, scene = UI_Scene.All)]
        public float idleSpeed = 200f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ABPriority", advancedTweakable = true, //Afterburner Priority
            groupName = "pilotAI_Speeds", groupDisplayName = "#LOC_BDArmory_PilotAI_Speeds", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1.0f, scene = UI_Scene.All)]
        public float ABPriority = 50f;
        #endregion

        #region Control Limits
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_LowSpeedSteerLimiter", advancedTweakable = true, // Low-Speed Steer Limiter
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_PilotAI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = .1f, maxValue = 1f, stepIncrement = .05f, scene = UI_Scene.All)]
        public float maxSteer = 1;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_LowSpeedLimiterSpeed", advancedTweakable = true, // Low-Speed Limiter Switch Speed 
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_PilotAI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = 10f, maxValue = 500f, stepIncrement = 1.0f, scene = UI_Scene.All)]
        public float lowSpeedSwitch = 100f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_HighSpeedSteerLimiter", advancedTweakable = true, // High-Speed Steer Limiter
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_PilotAI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = .1f, maxValue = 1f, stepIncrement = .05f, scene = UI_Scene.All)]
        public float maxSteerAtMaxSpeed = 1;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_HighSpeedLimiterSpeed", advancedTweakable = true, // High-Speed Limiter Switch Speed 
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_PilotAI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = 10f, maxValue = 500f, stepIncrement = 1.0f, scene = UI_Scene.All)]
        public float cornerSpeed = 200f;

        //[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AttitudeLimiter", advancedTweakable = true, //Attitude Limiter, not currently functional
        //    groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_PilotAI_ControlLimits", groupStartCollapsed = true),
        // UI_FloatRange(minValue = 10f, maxValue = 90f, stepIncrement = 5f, scene = UI_Scene.All)]
        //public float maxAttitude = 90f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_BankLimiter", advancedTweakable = true, //Bank Angle Limiter
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_PilotAI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = 10f, maxValue = 180f, stepIncrement = 5f, scene = UI_Scene.All)]
        public float maxBank = 180f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_WaypointPreRollTime", advancedTweakable = true, //Waypoint Pre-Roll Time
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_PilotAI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 2f, stepIncrement = 0.05f, scene = UI_Scene.All)]
        public float waypointPreRollTime = 0.5f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_WaypointYawAuthorityTime", advancedTweakable = true, //Waypoint Yaw Authority Time
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_PilotAI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float waypointYawAuthorityTime = 5f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_maxAllowedGForce", //Max G
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_PilotAI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = 2f, maxValue = 45f, stepIncrement = 0.25f, scene = UI_Scene.All)]
        public float maxAllowedGForce = 25;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_maxAllowedAoA", //Max AoA
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_PilotAI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 85f, stepIncrement = 2.5f, scene = UI_Scene.All)]
        public float maxAllowedAoA = 35;
        #endregion

        #region EvadeExtend
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_MinEvasionTime", advancedTweakable = true, // Minimum Evasion Time
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = .05f, scene = UI_Scene.All)]
        public float minEvasionTime = 0.2f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_EvasionNonlinearity", advancedTweakable = true, // Evasion/Extension Nonlinearity
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 10f, stepIncrement = .1f, scene = UI_Scene.All)]
        public float evasionNonlinearity = 2f;
        float evasionNonlinearityDirection = 1;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_EvasionThreshold", advancedTweakable = true, //Evade Threshold
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float evasionThreshold = 25f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_EvasionTimeThreshold", advancedTweakable = true, // Time on Target Threshold
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 5f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float evasionTimeThreshold = 0.1f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_EvasionIgnoreMyTargetTargetingMe", advancedTweakable = true,//Ignore my target targeting me
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All),]
        public bool evasionIgnoreMyTargetTargetingMe = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_CollisionAvoidanceThreshold", advancedTweakable = true, //Vessel collision avoidance threshold
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 50f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float collisionAvoidanceThreshold = 20f; // 20m + target's average radius.

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_CollisionAvoidanceLookAheadPeriod", advancedTweakable = true, //Vessel collision avoidance look ahead period
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 3f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float vesselCollisionAvoidanceLookAheadPeriod = 1.5f; // Look 1.5s ahead for potential collisions.

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_CollisionAvoidanceStrength", advancedTweakable = true, //Vessel collision avoidance strength
           groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
           UI_FloatRange(minValue = 0f, maxValue = 4f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float vesselCollisionAvoidanceStrength = 2f; // 2° per frame (100°/s).

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_StandoffDistance", advancedTweakable = true, //Min Approach Distance
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 1000f, stepIncrement = 50f, scene = UI_Scene.All)]

        public float vesselStandoffDistance = 200f; // try to avoid getting closer than 200m

        // [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ExtendMultiplier", advancedTweakable = true, //Extend Distance Multiplier
        //     groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
        //     UI_FloatRange(minValue = 0f, maxValue = 2f, stepIncrement = .1f, scene = UI_Scene.All)]
        // public float extendMult = 1f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ExtendDistanceAirToAir", advancedTweakable = true, //Extend Distance Air-To-Air
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 2000f, stepIncrement = 10f, scene = UI_Scene.All)]
        public float extendDistanceAirToAir = 300f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ExtendAngleAirToAir", advancedTweakable = true, //Extend Angle Air-To-Air
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = -10f, maxValue = 45f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float extendAngleAirToAir = 0f;
        float _extendAngleAirToAir = 0;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ExtendDistanceAirToGroundGuns", advancedTweakable = true, //Extend Distance Air-To-Ground (Guns)
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 5000f, stepIncrement = 50f, scene = UI_Scene.All)]
        public float extendDistanceAirToGroundGuns = 1500f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ExtendDistanceAirToGround", advancedTweakable = true, //Extend Distance Air-To-Ground
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 5000f, stepIncrement = 50f, scene = UI_Scene.All)]
        public float extendDistanceAirToGround = 2500f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ExtendTargetVel", advancedTweakable = true, //Extend Target Velocity Factor
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 2f, stepIncrement = .1f, scene = UI_Scene.All)]
        public float extendTargetVel = 0.8f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ExtendTargetAngle", advancedTweakable = true, //Extend Target Angle
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 180f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float extendTargetAngle = 78f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ExtendTargetDist", advancedTweakable = true, //Extend Target Distance
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 5000f, stepIncrement = 25f, scene = UI_Scene.All)]
        public float extendTargetDist = 300f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ExtendToggle", advancedTweakable = true,//Extend Toggle
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_PilotAI_EvadeExtend", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All),]
        public bool canExtend = true;
        #endregion

        #region Terrain
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, category = "DoubleSlider", guiName = "#LOC_BDArmory_TurnRadiusTwiddleFactorMin", advancedTweakable = true,//Turn radius twiddle factors (category seems to have no effect)
            groupName = "pilotAI_Terrain", groupDisplayName = "#LOC_BDArmory_PilotAI_Terrain", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 5f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float turnRadiusTwiddleFactorMin = 2.0f; // Minimum and maximum twiddle factors for the turn radius. Depends on roll rate and how the vessel behaves under fire.

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, category = "DoubleSlider", guiName = "#LOC_BDArmory_TurnRadiusTwiddleFactorMax", advancedTweakable = true,//Turn radius twiddle factors (category seems to have no effect)
            groupName = "pilotAI_Terrain", groupDisplayName = "#LOC_BDArmory_PilotAI_Terrain", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 5f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float turnRadiusTwiddleFactorMax = 3.0f; // Minimum and maximum twiddle factors for the turn radius. Depends on roll rate and how the vessel behaves under fire.

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_WaypointTerrainAvoidance", advancedTweakable = true,//Waypoint terrain avoidance.
            groupName = "pilotAI_Terrain", groupDisplayName = "#LOC_BDArmory_PilotAI_Terrain", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float waypointTerrainAvoidance = 0.5f;
        float waypointTerrainAvoidanceSmoothingFactor = 0.933f;
        #endregion

        #region Ramming
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AllowRamming", advancedTweakable = true, //Toggle Allow Ramming
            groupName = "pilotAI_Ramming", groupDisplayName = "#LOC_BDArmory_PilotAI_Ramming", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All),]
        public bool allowRamming = true; // Allow switching to ramming mode.

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ControlSurfaceLag", advancedTweakable = true,//Control surface lag (for getting an accurate intercept for ramming).
            groupName = "pilotAI_Ramming", groupDisplayName = "#LOC_BDArmory_PilotAI_Ramming", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 0.2f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float controlSurfaceLag = 0.01f; // Lag time in response of control surfaces.
        #endregion

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_SliderResolution", advancedTweakable = true), // Slider Resolution
            UI_ChooseOption(options = new string[4] { "Low", "Normal", "High", "Insane" }, scene = UI_Scene.All)]
        public string sliderResolution = "Normal";
        string previousSliderResolution = "Normal";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_Orbit", advancedTweakable = true),//Orbit 
            UI_Toggle(enabledText = "#LOC_BDArmory_Orbit_Starboard", disabledText = "#LOC_BDArmory_Orbit_Port", scene = UI_Scene.All),]//Starboard (CW)--Port (CCW)
        public bool ClockwiseOrbit = true;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_UnclampTuning", advancedTweakable = true),//Unclamp tuning 
            UI_Toggle(enabledText = "#LOC_BDArmory_UnclampTuning_enabledText", disabledText = "#LOC_BDArmory_UnclampTuning_disabledText", scene = UI_Scene.All),]//Unclamped--Clamped
        public bool UpToEleven = false;

        Dictionary<string, float> altMaxValues = new Dictionary<string, float>
        {
            { nameof(defaultAltitude), 100000f },
            { nameof(minAltitude), 100000f },
            { nameof(maxAltitude), 150000f },
            { nameof(steerMult), 200f },
            { nameof(steerKiAdjust), 20f },
            { nameof(steerDamping), 100f },
            { nameof(maxSteer), 1f},
            { nameof(maxSpeed), 3000f },
            { nameof(takeOffSpeed), 2000f },
            { nameof(minSpeed), 2000f },
            { nameof(strafingSpeed), 2000f },
            { nameof(idleSpeed), 3000f },
            { nameof(lowSpeedSwitch), 3000f },
            { nameof(cornerSpeed), 3000f },
            { nameof(maxAllowedGForce), 1000f },
            { nameof(maxAllowedAoA), 180f },
            // { nameof(extendMult), 200f },
            { nameof(extendDistanceAirToAir), 20000f },
            { nameof(extendAngleAirToAir), 90f },
            { nameof(extendDistanceAirToGroundGuns), 20000f },
            { nameof(extendDistanceAirToGround), 20000f },
            { nameof(minEvasionTime), 10f },
            { nameof(evasionNonlinearity), 90f },
            { nameof(evasionThreshold), 300f },
            { nameof(evasionTimeThreshold), 30f },
            { nameof(vesselStandoffDistance), 5000f },
            { nameof(turnRadiusTwiddleFactorMin), 10f},
            { nameof(turnRadiusTwiddleFactorMax), 10f},
            { nameof(controlSurfaceLag), 1f},
            { nameof(DynamicDampingMin), 100f },
            { nameof(DynamicDampingMax), 100f },
            { nameof(dynamicSteerDampingFactor), 100f },
            { nameof(DynamicDampingPitchMin), 100f },
            { nameof(DynamicDampingPitchMax), 100f },
            { nameof(dynamicSteerDampingPitchFactor), 100f },
            { nameof(DynamicDampingYawMin), 100f },
            { nameof(DynamicDampingYawMax), 100f },
            { nameof(dynamicSteerDampingYawFactor), 100f },
            { nameof(DynamicDampingRollMin), 100f },
            { nameof(DynamicDampingRollMax), 100f },
            { nameof(dynamicSteerDampingRollFactor), 100f }
        };
        Dictionary<string, float> altMinValues = new Dictionary<string, float> {
            { nameof(extendAngleAirToAir), -90f },
        };

        void TurnItUpToEleven(bool upToEleven)
        {
            using (var s = altMaxValues.Keys.ToList().GetEnumerator())
                while (s.MoveNext())
                {
                    UI_FloatRange euic = (UI_FloatRange)
                        (HighLogic.LoadedSceneIsFlight ? Fields[s.Current].uiControlFlight : Fields[s.Current].uiControlEditor);
                    float tempValue = euic.maxValue;
                    euic.maxValue = altMaxValues[s.Current];
                    altMaxValues[s.Current] = tempValue;
                    // change the value back to what it is now after fixed update, because changing the max value will clamp it down
                    // using reflection here, don't look at me like that, this does not run often
                    StartCoroutine(setVar(s.Current, (float)typeof(BDModulePilotAI).GetField(s.Current).GetValue(this)));
                }
            using (var s = altMinValues.Keys.ToList().GetEnumerator())
                while (s.MoveNext())
                {
                    UI_FloatRange euic = (UI_FloatRange)
                        (HighLogic.LoadedSceneIsFlight ? Fields[s.Current].uiControlFlight : Fields[s.Current].uiControlEditor);
                    float tempValue = euic.minValue;
                    euic.minValue = altMinValues[s.Current];
                    altMinValues[s.Current] = tempValue;
                    // change the value back to what it is now after fixed update, because changing the min value will clamp it down
                    // using reflection here, don't look at me like that, this does not run often
                    StartCoroutine(setVar(s.Current, (float)typeof(BDModulePilotAI).GetField(s.Current).GetValue(this)));
                }
            toEleven = upToEleven;
        }

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_StandbyMode"),//Standby Mode
            UI_Toggle(enabledText = "#LOC_BDArmory_On", disabledText = "#LOC_BDArmory_Off")]//On--Off
        public bool standbyMode = false;

        #region Store/Restore
        private static Dictionary<string, List<System.Tuple<string, object>>> storedSettings; // Stored settings for each vessel.
        [KSPEvent(advancedTweakable = false, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_StoreSettings", active = true)]//Store Settings
        public void StoreSettings()
        {
            var vesselName = HighLogic.LoadedSceneIsFlight ? vessel.GetDisplayName() : EditorLogic.fetch.ship.shipName;
            if (storedSettings == null)
            {
                storedSettings = new Dictionary<string, List<System.Tuple<string, object>>>();
            }
            if (storedSettings.ContainsKey(vesselName))
            {
                if (storedSettings[vesselName] == null)
                {
                    storedSettings[vesselName] = new List<System.Tuple<string, object>>();
                }
                else
                {
                    storedSettings[vesselName].Clear();
                }
            }
            else
            {
                storedSettings.Add(vesselName, new List<System.Tuple<string, object>>());
            }
            var fields = typeof(BDModulePilotAI).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var field in fields)
            {
                storedSettings[vesselName].Add(new System.Tuple<string, object>(field.Name, field.GetValue(this)));
            }
            Events["RestoreSettings"].active = true;
        }
        [KSPEvent(advancedTweakable = false, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_RestoreSettings", active = false)]//Restore Settings
        public void RestoreSettings()
        {
            var vesselName = HighLogic.LoadedSceneIsFlight ? vessel.GetDisplayName() : EditorLogic.fetch.ship.shipName;
            if (storedSettings == null || !storedSettings.ContainsKey(vesselName) || storedSettings[vesselName] == null || storedSettings[vesselName].Count == 0)
            {
                Debug.Log("[BDArmory.BDModulePilotAI]: No stored settings found for vessel " + vesselName + ".");
                return;
            }
            foreach (var setting in storedSettings[vesselName])
            {
                var field = typeof(BDModulePilotAI).GetField(setting.Item1, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(this, setting.Item2);
                }
            }
        }

        // This uses the parts' persistentId to reference the parts. Possibly, it should use some other identifier (what's used as a tag at the end of the "part = ..." and "link = ..." lines?) in case of duplicate persistentIds?
        private static Dictionary<string, Dictionary<uint, List<System.Tuple<string, object>>>> storedControlSurfaceSettings; // Stored control surface settings for each vessel.
        [KSPEvent(advancedTweakable = false, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_StoreControlSurfaceSettings", active = true)]//Store Control Surfaces
        public void StoreControlSurfaceSettings()
        {
            var vesselName = HighLogic.LoadedSceneIsFlight ? vessel.GetDisplayName() : EditorLogic.fetch.ship.shipName;
            if (storedControlSurfaceSettings == null)
            {
                storedControlSurfaceSettings = new Dictionary<string, Dictionary<uint, List<Tuple<string, object>>>>();
            }
            if (storedControlSurfaceSettings.ContainsKey(vesselName))
            {
                if (storedControlSurfaceSettings[vesselName] == null)
                {
                    storedControlSurfaceSettings[vesselName] = new Dictionary<uint, List<Tuple<string, object>>>();
                }
                else
                {
                    storedControlSurfaceSettings[vesselName].Clear();
                }
            }
            else
            {
                storedControlSurfaceSettings.Add(vesselName, new Dictionary<uint, List<Tuple<string, object>>>());
            }
            foreach (var part in HighLogic.LoadedSceneIsFlight ? vessel.Parts : EditorLogic.fetch.ship.Parts)
            {
                var controlSurface = part.GetComponent<ModuleControlSurface>();
                if (controlSurface == null) continue;
                storedControlSurfaceSettings[vesselName][part.persistentId] = new List<Tuple<string, object>>();
                var fields = typeof(ModuleControlSurface).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (var field in fields)
                {
                    storedControlSurfaceSettings[vesselName][part.persistentId].Add(new System.Tuple<string, object>(field.Name, field.GetValue(controlSurface)));
                }
            }
            StoreFARControlSurfaceSettings();
            Events["RestoreControlSurfaceSettings"].active = true;
        }
        private static Dictionary<string, Dictionary<uint, List<System.Tuple<string, object>>>> storedFARControlSurfaceSettings; // Stored control surface settings for each vessel.
        void StoreFARControlSurfaceSettings()
        {
            if (!FerramAerospace.hasFARControllableSurface) return;
            var vesselName = HighLogic.LoadedSceneIsFlight ? vessel.GetDisplayName() : EditorLogic.fetch.ship.shipName;
            if (storedFARControlSurfaceSettings == null)
            {
                storedFARControlSurfaceSettings = new Dictionary<string, Dictionary<uint, List<Tuple<string, object>>>>();
            }
            if (storedFARControlSurfaceSettings.ContainsKey(vesselName))
            {
                if (storedFARControlSurfaceSettings[vesselName] == null)
                {
                    storedFARControlSurfaceSettings[vesselName] = new Dictionary<uint, List<Tuple<string, object>>>();
                }
                else
                {
                    storedFARControlSurfaceSettings[vesselName].Clear();
                }
            }
            else
            {
                storedFARControlSurfaceSettings.Add(vesselName, new Dictionary<uint, List<Tuple<string, object>>>());
            }
            foreach (var part in HighLogic.LoadedSceneIsFlight ? vessel.Parts : EditorLogic.fetch.ship.Parts)
            {
                foreach (var module in part.Modules)
                {
                    if (module.GetType() == FerramAerospace.FARControllableSurfaceModule)
                    {
                        storedFARControlSurfaceSettings[vesselName][part.persistentId] = new List<Tuple<string, object>>();
                        var fields = FerramAerospace.FARControllableSurfaceModule.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                        foreach (var field in fields)
                        {
                            storedFARControlSurfaceSettings[vesselName][part.persistentId].Add(new System.Tuple<string, object>(field.Name, field.GetValue(module)));
                        }
                        break;
                    }
                }
            }
        }

        [KSPEvent(advancedTweakable = false, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_RestoreControlSurfaceSettings", active = false)]//Restore Control Surfaces
        public void RestoreControlSurfaceSettings()
        {
            RestoreFARControlSurfaceSettings();
            var vesselName = HighLogic.LoadedSceneIsFlight ? vessel.GetDisplayName() : EditorLogic.fetch.ship.shipName;
            if (storedControlSurfaceSettings == null || !storedControlSurfaceSettings.ContainsKey(vesselName) || storedControlSurfaceSettings[vesselName] == null || storedControlSurfaceSettings[vesselName].Count == 0)
            {
                return;
            }
            foreach (var part in HighLogic.LoadedSceneIsFlight ? vessel.Parts : EditorLogic.fetch.ship.Parts)
            {
                var controlSurface = part.GetComponent<ModuleControlSurface>();
                if (controlSurface == null || !storedControlSurfaceSettings[vesselName].ContainsKey(part.persistentId)) continue;
                foreach (var setting in storedControlSurfaceSettings[vesselName][part.persistentId])
                {
                    var field = typeof(ModuleControlSurface).GetField(setting.Item1, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (field != null)
                    {
                        field.SetValue(controlSurface, setting.Item2);
                    }
                }
            }
        }
        void RestoreFARControlSurfaceSettings()
        {
            if (!FerramAerospace.hasFARControllableSurface) return;
            var vesselName = HighLogic.LoadedSceneIsFlight ? vessel.GetDisplayName() : EditorLogic.fetch.ship.shipName;
            if (storedFARControlSurfaceSettings == null || !storedFARControlSurfaceSettings.ContainsKey(vesselName) || storedFARControlSurfaceSettings[vesselName] == null || storedFARControlSurfaceSettings[vesselName].Count == 0)
            {
                return;
            }
            foreach (var part in HighLogic.LoadedSceneIsFlight ? vessel.Parts : EditorLogic.fetch.ship.Parts)
            {
                if (!storedFARControlSurfaceSettings[vesselName].ContainsKey(part.persistentId)) continue;
                foreach (var module in part.Modules)
                {
                    if (module.GetType() == FerramAerospace.FARControllableSurfaceModule)
                    {
                        foreach (var setting in storedFARControlSurfaceSettings[vesselName][part.persistentId])
                        {
                            var field = FerramAerospace.FARControllableSurfaceModule.GetField(setting.Item1, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                            if (field != null)
                            {
                                field.SetValue(module, setting.Item2);
                            }
                        }
                        break;
                    }
                }
            }
        }
        #endregion
        #endregion

        #region AI Internal Parameters
        bool toEleven = false;
        bool maxAltitudeEnabled = false;

        //manueuverability and g loading data
        // float maxDynPresGRecorded;
        float dynDynPresGRecorded = 1f; // Start at reasonable non-zero value.
        float dynVelocityMagSqr = 1f; // Start at reasonable non-zero value.
        float dynDecayRate = 1f; // Decay rate for dynamic measurements. Set to a half-life of 60s in Start.
        float dynVelSmoothingCoef = 1f; // Decay rate for smoothing the dynVelocityMagSqr

        float maxAllowedCosAoA;
        float lastAllowedAoA;

        float maxPosG;
        float cosAoAAtMaxPosG;

        float maxNegG;
        float cosAoAAtMaxNegG;

        float[] gLoadMovingAvgArray = new float[32];
        float[] cosAoAMovingAvgArray = new float[32];
        int movingAvgIndex;

        float gLoadMovingAvg;
        float cosAoAMovingAvg;

        float gaoASlopePerDynPres;        //used to limit control input at very high dynamic pressures to avoid structural failure
        float gOffsetPerDynPres;

        float posPitchDynPresLimitIntegrator = 1;
        float negPitchDynPresLimitIntegrator = -1;

        float lastCosAoA;
        float lastPitchInput;

        //Controller Integral
        Vector3 directionIntegral;
        float pitchIntegral;
        float yawIntegral;
        float rollIntegral;

        //instantaneous turn radius and possible acceleration from lift
        //properties can be used so that other AI modules can read this for future maneuverability comparisons between craft
        float turnRadius;
        float bodyGravity = (float)PhysicsGlobals.GravitationalAcceleration;

        public float TurnRadius
        {
            get { return turnRadius; }
            private set { turnRadius = value; }
        }

        float maxLiftAcceleration;

        public float MaxLiftAcceleration
        {
            get { return maxLiftAcceleration; }
            private set { maxLiftAcceleration = value; }
        }

        float turningTimer;
        float evasiveTimer;
        float threatRating;
        // List<Vector3> waypoints = null;
        // int activeWaypointIndex = -1;
        Vector3 lastTargetPosition;

        LineRenderer lr;
        Vector3 flyingToPosition;
        Vector3 rollTarget;
#if DEBUG
        Vector3 DEBUG_vector;
#endif
        Vector3 angVelRollTarget;

        //speed controller
        bool useAB = true;
        bool useBrakes = true;
        bool regainEnergy = false;

        //collision detection (for other vessels).
        const int vesselCollisionAvoidanceTickerFreq = 10; // Number of fixedDeltaTime steps between vessel-vessel collision checks.
        int collisionDetectionTicker = 0;
        Vector3 collisionAvoidDirection;
        public Vessel currentlyAvoidedVessel;

        // Terrain avoidance and below minimum altitude globals.
        int terrainAlertTicker = 0; // A ticker to reduce the frequency of terrain alert checks.
        bool belowMinAltitude; // True when below minAltitude or avoiding terrain.
        bool gainAltInhibited = false; // Inhibit gain altitude to minimum altitude when chasing or evading someone as long as we're pointing upwards.
        bool avoidingTerrain = false; // True when avoiding terrain.
        bool initialTakeOff = true; // False after the initial take-off.
        float terrainAlertDetectionRadius = 30.0f; // Sphere radius that the vessel occupies. Should cover most vessels. FIXME This could be based on the vessel's maximum width/height.
        float terrainAlertThreatRange; // The distance to the terrain to consider (based on turn radius).
        float terrainAlertThreshold; // The current threshold for triggering terrain avoidance based on various factors.
        float terrainAlertDistance; // Distance to the terrain (in the direction of the terrain normal).
        Vector3 terrainAlertNormal; // Approximate surface normal at the terrain intercept.
        Vector3 terrainAlertDirection; // Terrain slope in the direction of the velocity at the terrain intercept.
        Vector3 terrainAlertCorrectionDirection; // The direction to go to avoid the terrain.
        float terrainAlertCoolDown = 0; // Cool down period before allowing other special modes to take effect (currently just "orbitting").
        Vector3 relativeVelocityRightDirection; // Right relative to current velocity and upDirection.
        Vector3 relativeVelocityDownDirection; // Down relative to current velocity and upDirection.
        Vector3 terrainAlertDebugPos, terrainAlertDebugDir, terrainAlertDebugPos2, terrainAlertDebugDir2; // Debug vector3's for drawing lines.
        bool terrainAlertDebugDraw2 = false;

        // Ramming
        public bool ramming = false; // Whether or not we're currently trying to ram someone.

        //Dynamic Steer Damping
        private bool dynamicDamping = false;
        private bool CustomDynamicAxisField = false;
        public float dynSteerDampingValue;
        public float dynSteerDampingPitchValue;
        public float dynSteerDampingYawValue;
        public float dynSteerDampingRollValue;

        //wing command
        bool useRollHint;
        private Vector3d debugFollowPosition;

        double commandSpeed;
        Vector3d commandHeading;

        float finalMaxSteer = 1;

        string lastStatus = "Free";

        #endregion

        #region RMB info in editor

        // <color={XKCDColors.HexFormat.Lime}>Yes</color>
        public override string GetInfo()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<b>Available settings</b>:");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Default Alt.</color> - altitude to fly at when cruising/idle");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Min Altitude</color> - below this altitude AI will prioritize gaining altitude over combat");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Steer Factor</color> - higher will make the AI apply more control input for the same desired rotation");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Steer Ki</color> - higher will make the AI apply control trim faster");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Steer Damping</color> - higher will make the AI apply more control input when it wants to stop rotation");
            if (GameSettings.ADVANCED_TWEAKABLES)
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Steer Limiter</color> - limit AI from applying full control input");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Max Speed</color> - AI will not fly faster than this");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- TakeOff Speed</color> - speed at which to start pitching up when taking off");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- MinCombat Speed</color> - AI will prioritize regaining speed over combat below this");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Idle Speed</color> - Cruising speed when not in combat");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Max G</color> - AI will try not to perform maneuvers at higher G than this");
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Max AoA</color> - AI will try not to exceed this angle of attack");
            if (GameSettings.ADVANCED_TWEAKABLES)
            {
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Extend Multiplier</color> - scale the time spent extending");
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Evasion Multiplier</color> - scale the time spent evading");
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Dynamic Steer Damping (min/max)</color> - Dynamically adjust the steer damping factor based on angle to target");
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Dyn Steer Damping Factor</color> - Strength of dynamic steer damping adjustment");
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Turn Radius Tuning (min/max)</color> - Compensating factor for not being able to perform the perfect turn when oriented correctly/incorrectly");
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Control Surface Lag</color> - Lag time in response of control surfaces");
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Orbit</color> - Which direction to orbit when idling over a location");
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Extend Toggle</color> - Toggle extending multiplier behaviour");
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Dynamic Steer Damping</color> - Toggle dynamic steer damping");
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Allow Ramming</color> - Toggle ramming behaviour when out of guns/ammo");
                sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Unclamp tuning</color> - Increases variable limits, no direct effect on behaviour");
            }
            sb.AppendLine($"<color={XKCDColors.HexFormat.Cyan}>- Standby Mode</color> - AI will not take off until an enemy is detected");

            return sb.ToString();
        }

        #endregion RMB info in editor

        #region UI Initialisers and Callbacks
        protected void SetSliderClamps(string fieldNameMin, string fieldNameMax)
        {
            // Enforce min <= max for pairs of sliders
            UI_FloatRange field = (UI_FloatRange)Fields[fieldNameMin].uiControlEditor;
            field.onFieldChanged = OnMinUpdated;
            field = (UI_FloatRange)Fields[fieldNameMin].uiControlFlight;
            field.onFieldChanged = OnMinUpdated;
            field = (UI_FloatRange)Fields[fieldNameMax].uiControlEditor;
            field.onFieldChanged = OnMaxUpdated;
            field = (UI_FloatRange)Fields[fieldNameMax].uiControlFlight;
            field.onFieldChanged = OnMaxUpdated;
        }

        public void OnMinUpdated(BaseField field, object obj)
        {
            if (turnRadiusTwiddleFactorMax < turnRadiusTwiddleFactorMin) { turnRadiusTwiddleFactorMax = turnRadiusTwiddleFactorMin; } // Enforce min < max for turn radius twiddle factor.
            // if (DynamicDampingMax < DynamicDampingMin) { DynamicDampingMax = DynamicDampingMin; } // Enforce min < max for dynamic steer damping.
            // if (DynamicDampingPitchMax < DynamicDampingPitchMin) { DynamicDampingPitchMax = DynamicDampingPitchMin; }
            // if (DynamicDampingYawMax < DynamicDampingYawMin) { DynamicDampingYawMax = DynamicDampingYawMin; }
            // if (DynamicDampingRollMax < DynamicDampingRollMin) { DynamicDampingRollMax = DynamicDampingRollMin; } // reversed roll dynamic damp behavior
        }

        public void OnMaxUpdated(BaseField field, object obj)
        {
            if (turnRadiusTwiddleFactorMin > turnRadiusTwiddleFactorMax) { turnRadiusTwiddleFactorMin = turnRadiusTwiddleFactorMax; } // Enforce min < max for turn radius twiddle factor.
            // if (DynamicDampingMin > DynamicDampingMax) { DynamicDampingMin = DynamicDampingMax; } // Enforce min < max for dynamic steer damping.
            // if (DynamicDampingPitchMin > DynamicDampingPitchMax) { DynamicDampingPitchMin = DynamicDampingPitchMax; }
            // if (DynamicDampingYawMin > DynamicDampingYawMax) { DynamicDampingYawMin = DynamicDampingYawMax; }
            // if (DynamicDampingRollMin > DynamicDampingRollMax) { DynamicDampingRollMin = DynamicDampingRollMax; } // reversed roll dynamic damp behavior
        }

        void SetAltitudeClamps()
        {
            var minAltField = (UI_FloatRange)Fields["minAltitude"].uiControlEditor;
            minAltField.onFieldChanged = ClampAltitudes;
            minAltField = (UI_FloatRange)Fields["minAltitude"].uiControlFlight;
            minAltField.onFieldChanged = ClampAltitudes;
            var defaultAltField = (UI_FloatRange)Fields["defaultAltitude"].uiControlEditor;
            defaultAltField.onFieldChanged = ClampAltitudes;
            defaultAltField = (UI_FloatRange)Fields["defaultAltitude"].uiControlFlight;
            defaultAltField.onFieldChanged = ClampAltitudes;
            var maxAltField = (UI_FloatRange)Fields["maxAltitude"].uiControlEditor;
            maxAltField.onFieldChanged = ClampAltitudes;
            maxAltField = (UI_FloatRange)Fields["maxAltitude"].uiControlFlight;
            maxAltField.onFieldChanged = ClampAltitudes;
        }

        void ClampAltitudes(BaseField field, object obj)
        {
            ClampAltitudes(field.name);
        }
        public void ClampAltitudes(string fieldName)
        {
            switch (fieldName)
            {
                case "minAltitude":
                    if (defaultAltitude < minAltitude) { defaultAltitude = minAltitude; }
                    if (maxAltitude < minAltitude) { maxAltitude = minAltitude; }
                    break;
                case "defaultAltitude":
                    if (maxAltitude < defaultAltitude) { maxAltitude = defaultAltitude; }
                    if (minAltitude > defaultAltitude) { minAltitude = defaultAltitude; }
                    break;
                case "maxAltitude":
                    if (minAltitude > maxAltitude) { minAltitude = maxAltitude; }
                    if (defaultAltitude > maxAltitude) { defaultAltitude = maxAltitude; }
                    break;
                default:
                    Debug.LogError($"[BDArmory.BDModulePilotAI]: Invalid altitude {fieldName} in ClampAltitudes.");
                    break;
            }
        }

        public void ToggleDynamicDampingFields()
        {
            // Dynamic damping
            var DynamicDampingLabel = Fields["DynamicDampingLabel"];
            var DampingMin = Fields["DynamicDampingMin"];
            var DampingMax = Fields["DynamicDampingMax"];
            var DampingFactor = Fields["dynamicSteerDampingFactor"];

            DynamicDampingLabel.guiActive = dynamicSteerDamping && !CustomDynamicAxisFields;
            DynamicDampingLabel.guiActiveEditor = dynamicSteerDamping && !CustomDynamicAxisFields;
            DampingMin.guiActive = dynamicSteerDamping && !CustomDynamicAxisFields;
            DampingMin.guiActiveEditor = dynamicSteerDamping && !CustomDynamicAxisFields;
            DampingMax.guiActive = dynamicSteerDamping && !CustomDynamicAxisFields;
            DampingMax.guiActiveEditor = dynamicSteerDamping && !CustomDynamicAxisFields;
            DampingFactor.guiActive = dynamicSteerDamping && !CustomDynamicAxisFields;
            DampingFactor.guiActiveEditor = dynamicSteerDamping && !CustomDynamicAxisFields;

            // 3-axis dynamic damping
            var DynamicPitchLabel = Fields["PitchLabel"];
            var DynamicDampingPitch = Fields["dynamicDampingPitch"];
            var DynamicDampingPitchMaxField = Fields["DynamicDampingPitchMax"];
            var DynamicDampingPitchMinField = Fields["DynamicDampingPitchMin"];
            var DynamicDampingPitchFactorField = Fields["dynamicSteerDampingPitchFactor"];

            var DynamicYawLabel = Fields["YawLabel"];
            var DynamicDampingYaw = Fields["dynamicDampingYaw"];
            var DynamicDampingYawMaxField = Fields["DynamicDampingYawMax"];
            var DynamicDampingYawMinField = Fields["DynamicDampingYawMin"];
            var DynamicDampingYawFactorField = Fields["dynamicSteerDampingYawFactor"];

            var DynamicRollLabel = Fields["RollLabel"];
            var DynamicDampingRoll = Fields["dynamicDampingRoll"];
            var DynamicDampingRollMaxField = Fields["DynamicDampingRollMax"];
            var DynamicDampingRollMinField = Fields["DynamicDampingRollMin"];
            var DynamicDampingRollFactorField = Fields["dynamicSteerDampingRollFactor"];

            DynamicPitchLabel.guiActive = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicPitchLabel.guiActiveEditor = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingPitch.guiActive = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingPitch.guiActiveEditor = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingPitchMinField.guiActive = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingPitchMinField.guiActiveEditor = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingPitchMaxField.guiActive = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingPitchMaxField.guiActiveEditor = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingPitchFactorField.guiActive = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingPitchFactorField.guiActiveEditor = CustomDynamicAxisFields && dynamicSteerDamping;

            DynamicYawLabel.guiActive = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicYawLabel.guiActiveEditor = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingYaw.guiActive = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingYaw.guiActiveEditor = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingYawMinField.guiActive = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingYawMinField.guiActiveEditor = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingYawMaxField.guiActive = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingYawMaxField.guiActiveEditor = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingYawFactorField.guiActive = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingYawFactorField.guiActiveEditor = CustomDynamicAxisFields && dynamicSteerDamping;

            DynamicRollLabel.guiActive = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicRollLabel.guiActiveEditor = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingRoll.guiActive = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingRoll.guiActiveEditor = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingRollMinField.guiActive = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingRollMinField.guiActiveEditor = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingRollMaxField.guiActive = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingRollMaxField.guiActiveEditor = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingRollFactorField.guiActive = CustomDynamicAxisFields && dynamicSteerDamping;
            DynamicDampingRollFactorField.guiActiveEditor = CustomDynamicAxisFields && dynamicSteerDamping;

            StartCoroutine(ToggleDynamicDampingButtons());

            if (autoTune)
            {
                autoTune = false; // Disable auto-tuning if the damping configuration is changed.
                OnAutoTuneChanged(null, null);
            }
        }

        IEnumerator ToggleDynamicDampingButtons()
        {
            // Toggle the visibility of buttons, then re-enable them to avoid messing up the order in the GUI.
            var dynamicSteerDampingField = Fields["dynamicSteerDamping"];
            var customDynamicAxisField = Fields["CustomDynamicAxisFields"];
            dynamicSteerDampingField.guiActive = false;
            dynamicSteerDampingField.guiActiveEditor = false;
            customDynamicAxisField.guiActive = false;
            customDynamicAxisField.guiActiveEditor = false;
            yield return new WaitForFixedUpdate();
            dynamicSteerDampingField.guiActive = true;
            dynamicSteerDampingField.guiActiveEditor = true;
            customDynamicAxisField.guiActive = dynamicDamping;
            customDynamicAxisField.guiActiveEditor = dynamicDamping;
        }

        [KSPAction("Toggle Max Altitude (AGL)")]
        public void ToggleMaxAltitudeAG(KSPActionParam param)
        {
            maxAltitudeToggle = !maxAltitudeEnabled;
            ToggleMaxAltitude();
        }
        [KSPAction("Enable Max Altitude (AGL)")]
        public void EnableMaxAltitudeAG(KSPActionParam param)
        {
            maxAltitudeToggle = true;
            ToggleMaxAltitude();
        }
        [KSPAction("Disable Max Altitude (AGL)")]
        public void DisableMaxAltitudeAG(KSPActionParam param)
        {
            maxAltitudeToggle = false;
            ToggleMaxAltitude();
        }
        void ToggleMaxAltitude()
        {
            maxAltitudeEnabled = maxAltitudeToggle;
            var maxAltitudeField = Fields["maxAltitude"];
            maxAltitudeField.guiActive = maxAltitudeToggle;
            maxAltitudeField.guiActiveEditor = maxAltitudeToggle;
            if (!maxAltitudeToggle)
                StartCoroutine(FixAltitudesSectionLayout());
        }
        void SetMinCollisionAvoidanceLookAheadPeriod()
        {
            var minCollisionAvoidanceLookAheadPeriod = (UI_FloatRange)Fields["vesselCollisionAvoidanceLookAheadPeriod"].uiControlEditor;
            minCollisionAvoidanceLookAheadPeriod.minValue = vesselCollisionAvoidanceTickerFreq * Time.fixedDeltaTime;
            minCollisionAvoidanceLookAheadPeriod = (UI_FloatRange)Fields["vesselCollisionAvoidanceLookAheadPeriod"].uiControlFlight;
            minCollisionAvoidanceLookAheadPeriod.minValue = vesselCollisionAvoidanceTickerFreq * Time.fixedDeltaTime;
        }

        public void SetOnExtendAngleA2AChanged()
        {
            UI_FloatRange field = (UI_FloatRange)Fields["extendAngleAirToAir"].uiControlEditor;
            field.onFieldChanged = OnExtendAngleA2AChanged;
            field = (UI_FloatRange)Fields["extendAngleAirToAir"].uiControlFlight;
            field.onFieldChanged = OnExtendAngleA2AChanged;
            OnExtendAngleA2AChanged(null, null);
        }
        void OnExtendAngleA2AChanged(BaseField field, object obj)
        {
            _extendAngleAirToAir = Mathf.Sin(extendAngleAirToAir * Mathf.Deg2Rad);
        }

        IEnumerator FixAltitudesSectionLayout() // Fix the layout of the Altitudes section by briefly disabling the fields underneath the one that was removed.
        {
            var maxAltitudeToggleField = Fields["maxAltitudeToggle"];
            maxAltitudeToggleField.guiActive = false;
            maxAltitudeToggleField.guiActiveEditor = false;
            yield return null;
            maxAltitudeToggleField.guiActive = true;
            maxAltitudeToggleField.guiActiveEditor = true;
        }

        protected void SetupSliderResolution()
        {
            var sliderResolutionField = (UI_ChooseOption)Fields["sliderResolution"].uiControlEditor;
            sliderResolutionField.onFieldChanged = OnSliderResolutionUpdated;
            sliderResolutionField = (UI_ChooseOption)Fields["sliderResolution"].uiControlFlight;
            sliderResolutionField.onFieldChanged = OnSliderResolutionUpdated;
            OnSliderResolutionUpdated(null, null);
        }
        public float sliderResolutionAsFloat(string res, float factor = 10f)
        {
            switch (res)
            {
                case "Low": return factor;
                case "High": return 1f / factor;
                case "Insane": return 1f / factor / factor;
                default: return 1f;
            }
        }
        void OnSliderResolutionUpdated(BaseField field, object obj)
        {
            if (previousSliderResolution != sliderResolution)
            {
                var factor = Mathf.Pow(10f, Mathf.Round(Mathf.Log10(sliderResolutionAsFloat(sliderResolution) / sliderResolutionAsFloat(previousSliderResolution))));
                foreach (var PIDField in Fields)
                {
                    if (PIDField.group.name == "pilotAI_PID")
                    {
                        if (PIDField.name.StartsWith("autoTuning")) continue;
                        var uiControl = HighLogic.LoadedSceneIsFlight ? PIDField.uiControlFlight : PIDField.uiControlEditor;
                        if (uiControl.GetType() == typeof(UI_FloatRange))
                        {
                            var slider = (UI_FloatRange)uiControl;
                            var alsoMinValue = (slider.minValue == slider.stepIncrement);
                            slider.stepIncrement *= factor;
                            slider.stepIncrement = BDAMath.RoundToUnit(slider.stepIncrement, slider.stepIncrement);
                            // var precision = Mathf.Pow(10, -Mathf.Floor(Mathf.Log10(slider.stepIncrement)) + 1);
                            // slider.stepIncrement = Mathf.Round(precision * slider.stepIncrement) / precision;
                            if (alsoMinValue) slider.minValue = slider.stepIncrement;
                        }
                    }
                    if (PIDField.group.name == "pilotAI_Altitudes")
                    {
                        var uiControl = HighLogic.LoadedSceneIsFlight ? PIDField.uiControlFlight : PIDField.uiControlEditor;
                        if (uiControl.GetType() == typeof(UI_FloatRange))
                        {
                            var slider = (UI_FloatRange)uiControl;
                            var alsoMinValue = (slider.minValue == slider.stepIncrement);
                            slider.stepIncrement *= factor;
                            slider.stepIncrement = BDAMath.RoundToUnit(slider.stepIncrement, slider.stepIncrement);
                            // var precision = Mathf.Pow(10, -Mathf.Floor(Mathf.Log10(slider.stepIncrement)) + 1);
                            // slider.stepIncrement = Mathf.Round(precision * slider.stepIncrement) / precision;
                            if (alsoMinValue) slider.minValue = slider.stepIncrement;
                        }
                    }
                    if (PIDField.group.name == "pilotAI_Speeds")
                    {
                        var uiControl = HighLogic.LoadedSceneIsFlight ? PIDField.uiControlFlight : PIDField.uiControlEditor;
                        if (uiControl.GetType() == typeof(UI_FloatRange))
                        {
                            var slider = (UI_FloatRange)uiControl;
                            slider.stepIncrement *= factor;
                            slider.stepIncrement = BDAMath.RoundToUnit(slider.stepIncrement, slider.stepIncrement);
                            // var precision = Mathf.Pow(10, -Mathf.Floor(Mathf.Log10(slider.stepIncrement)) + 1);
                            // slider.stepIncrement = Mathf.Round(precision * slider.stepIncrement) / precision;
                        }
                    }
                    if (PIDField.group.name == "pilotAI_EvadeExtend")
                    {
                        if (PIDField.name.StartsWith("extendDistance"))
                        {
                            var uiControl = HighLogic.LoadedSceneIsFlight ? PIDField.uiControlFlight : PIDField.uiControlEditor;
                            if (uiControl.GetType() == typeof(UI_FloatRange))
                            {
                                var slider = (UI_FloatRange)uiControl;
                                slider.stepIncrement *= factor;
                                slider.stepIncrement = BDAMath.RoundToUnit(slider.stepIncrement, slider.stepIncrement);
                                // var precision = Mathf.Pow(10, -Mathf.Floor(Mathf.Log10(slider.stepIncrement)) + 1);
                                // slider.stepIncrement = Mathf.Round(precision * slider.stepIncrement) / precision;
                            }
                        }
                    }
                }
                previousSliderResolution = sliderResolution;
            }
        }

        void SetAutoTuneToggle()
        {
            if (HighLogic.LoadedSceneIsEditor) // Make sure they're disabled in the editor. Use 'Store' in flight and 'Restore' in the editor to save the slider.
            {
                Fields["autoTune"].guiActiveEditor = false;
                Fields["autoTuningLossLabel"].guiActiveEditor = false;
                Fields["autoTuningLossLabel2"].guiActiveEditor = false;
                Fields["autoTuningLearningRate"].guiActiveEditor = false;
                Fields["autoTuningNumSamples"].guiActiveEditor = false;
                Fields["autoTuningRollRelevance"].guiActiveEditor = false;
                Fields["autoTuningFastResponseRelevance"].guiActiveEditor = false;
                Fields["autoTuningFixedP"].guiActiveEditor = false;
                // Fields["autoTuningLossRatio"].guiActiveEditor = false;
                autoTune = false;
            }
            if (!HighLogic.LoadedSceneIsFlight) return;
            pidAutoTuning = new PIDAutoTuning(this);
            UI_Toggle field = (UI_Toggle)Fields["autoTune"].uiControlFlight;
            field.onFieldChanged = OnAutoTuneChanged;
            OnAutoTuneChanged(null, null);
            field = (UI_Toggle)Fields["autoTuningFixedP"].uiControlFlight;
            field.onFieldChanged = OnAutoTuneFixedPChanged;
            OnAutoTuneFixedPChanged(null, null);
        }
        void OnAutoTuneChanged(BaseField field, object obj)
        {
            if (!autoTune) pidAutoTuning.RevertPIDValues();
            pidAutoTuning.ResetMeasurements();
            Fields["autoTuningLossLabel"].guiActive = autoTune;
            Fields["autoTuningLossLabel2"].guiActive = autoTune;
            Fields["autoTuningLearningRate"].guiActive = autoTune;
            Fields["autoTuningNumSamples"].guiActive = autoTune;
            Fields["autoTuningRollRelevance"].guiActive = autoTune;
            Fields["autoTuningFastResponseRelevance"].guiActive = autoTune;
            Fields["autoTuningFixedP"].guiActive = autoTune;
            // Fields["autoTuningLossRatio"].guiActive = autoTune;

            MaintainFuelLevels(autoTune); // Prevent fuel drain while auto-tuning.
        }
        void OnAutoTuneFixedPChanged(BaseField field, object obj)
        {
            pidAutoTuning.ToggleFixedP();
        }
        #endregion

        protected override void Start()
        {
            base.Start();

            if (HighLogic.LoadedSceneIsFlight)
            {
                maxAllowedCosAoA = (float)Math.Cos(maxAllowedAoA * Math.PI / 180.0);
                lastAllowedAoA = maxAllowedAoA;
                GameEvents.onVesselPartCountChanged.Add(UpdateTerrainAlertDetectionRadius);
                UpdateTerrainAlertDetectionRadius(vessel);
                dynDecayRate = Mathf.Exp(Mathf.Log(0.5f) * Time.fixedDeltaTime / 60f); // Decay rate for a half-life of 60s.
                dynVelSmoothingCoef = Mathf.Exp(Mathf.Log(0.5f) * Time.fixedDeltaTime / 5f); // Smoothing rate with a half-life of 5s.
            }

            SetupSliderResolution();
            SetSliderClamps("turnRadiusTwiddleFactorMin", "turnRadiusTwiddleFactorMax");
            // SetSliderClamps("DynamicDampingMin", "DynamicDampingMax");
            // SetSliderClamps("DynamicDampingPitchMin", "DynamicDampingPitchMax");
            // SetSliderClamps("DynamicDampingYawMin", "DynamicDampingYawMax");
            // SetSliderClamps("DynamicDampingRollMin", "DynamicDampingRollMax");
            SetAltitudeClamps();
            SetMinCollisionAvoidanceLookAheadPeriod();
            SetWaypointTerrainAvoidance();
            dynamicDamping = dynamicSteerDamping;
            CustomDynamicAxisField = CustomDynamicAxisFields;
            ToggleDynamicDampingFields();
            ToggleMaxAltitude();
            SetOnExtendAngleA2AChanged();
            SetAutoTuneToggle();
            // InitSteerDamping();
            if ((HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor) && storedSettings != null && storedSettings.ContainsKey(HighLogic.LoadedSceneIsFlight ? vessel.GetDisplayName() : EditorLogic.fetch.ship.shipName))
            {
                Events["RestoreSettings"].active = true;
            }
            if (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor)
            {
                var vesselName = HighLogic.LoadedSceneIsFlight ? vessel.GetDisplayName() : EditorLogic.fetch.ship.shipName;
                if ((storedControlSurfaceSettings != null && storedControlSurfaceSettings.ContainsKey(vesselName)) || (storedFARControlSurfaceSettings != null && storedFARControlSurfaceSettings.ContainsKey(vesselName)))
                {
                    Events["RestoreControlSurfaceSettings"].active = true;
                }
            }
        }

        protected override void OnDestroy()
        {
            GameEvents.onVesselPartCountChanged.Remove(UpdateTerrainAlertDetectionRadius);
            base.OnDestroy();
        }

        public override void ActivatePilot()
        {
            base.ActivatePilot();

            belowMinAltitude = vessel.LandedOrSplashed;
            prevTargetDir = vesselTransform.up;
            if (initialTakeOff && !vessel.LandedOrSplashed) // In case we activate pilot after taking off manually.
                initialTakeOff = false;

            bodyGravity = (float)PhysicsGlobals.GravitationalAcceleration * (float)vessel.orbit.referenceBody.GeeASL; // Set gravity for calculations;
        }

        void Update()
        {
            if (BDArmorySettings.DEBUG_LINES && pilotEnabled)
            {
                lr = GetComponent<LineRenderer>();
                if (lr == null)
                {
                    lr = gameObject.AddComponent<LineRenderer>();
                    lr.positionCount = 2;
                    lr.startWidth = 0.5f;
                    lr.endWidth = 0.5f;
                }
                lr.enabled = true;
                lr.SetPosition(0, vessel.ReferenceTransform.position);
                lr.SetPosition(1, flyingToPosition);

                minSpeed = Mathf.Clamp(minSpeed, 0, idleSpeed - 20);
                minSpeed = Mathf.Clamp(minSpeed, 0, maxSpeed - 20);
            }
            else { if (lr != null) { lr.enabled = false; } }

            // switch up the alt values if up to eleven is toggled
            if (UpToEleven != toEleven)
            {
                TurnItUpToEleven(UpToEleven);
            }

            //hide dynamic steer damping fields if dynamic damping isn't toggled
            if (dynamicSteerDamping != dynamicDamping)
            {
                // InitSteerDamping();
                dynamicDamping = dynamicSteerDamping;
                ToggleDynamicDampingFields();
            }
            //hide custom dynamic axis fields when it isn't toggled
            if (CustomDynamicAxisFields != CustomDynamicAxisField)
            {
                CustomDynamicAxisField = CustomDynamicAxisFields;
                ToggleDynamicDampingFields();
            }

            // Enable Max Altitude slider when toggled.
            if (maxAltitudeEnabled != maxAltitudeToggle)
            {
                ToggleMaxAltitude();
            }
        }

        IEnumerator setVar(string name, float value)
        {
            yield return new WaitForFixedUpdate();
            typeof(BDModulePilotAI).GetField(name).SetValue(this, value);
        }
        float targetStalenessTimer = 0;
        void FixedUpdate()
        {
            //floating origin and velocity offloading corrections
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (!FloatingOrigin.Offset.IsZero() || !Krakensbane.GetFrameVelocity().IsZero())
            {
                if (lastTargetPosition != null) lastTargetPosition -= FloatingOrigin.OffsetNonKrakensbane;
            }
            if (weaponManager && weaponManager.guardMode && weaponManager.detectedTargetTimeout > weaponManager.targetScanInterval)
            {
                targetStalenessTimer += Time.fixedDeltaTime;
                if (targetStalenessTimer >= 50) //add some error to the predicted position every second
                {
                    Vector3 staleTargetPosition = new Vector3();
                    staleTargetPosition.x = UnityEngine.Random.Range(0f, (float)staleTargetVelocity.magnitude / 2);
                    staleTargetPosition.y = UnityEngine.Random.Range(0f, (float)staleTargetVelocity.magnitude / 2);
                    staleTargetPosition.z = UnityEngine.Random.Range((float)staleTargetVelocity.magnitude * 0.75f, (float)staleTargetVelocity.magnitude * 1.25f);
                    targetStalenessTimer = 0;
                }
            }
            else
            {
                if (targetStalenessTimer != 0) targetStalenessTimer = 0;
            }
        }

        // This is triggered every Time.fixedDeltaTime.
        protected override void AutoPilot(FlightCtrlState s)
        {
            finalMaxSteer = 1f; // Reset finalMaxSteer, is adjusted in subsequent methods

            if (terrainAlertCoolDown > 0)
                terrainAlertCoolDown -= Time.fixedDeltaTime;

            //default brakes off full throttle
            //s.mainThrottle = 1;

            //vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, false);
            AdjustThrottle(maxSpeed, true);
            useAB = true;
            useBrakes = true;
            vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
            if (vessel.atmDensity < 0.05)
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);
            }

            steerMode = SteerModes.NormalFlight;
            useVelRollTarget = false;

            // landed and still, chill out
            if (vessel.LandedOrSplashed && standbyMode && weaponManager && (BDATargetManager.GetClosestTarget(this.weaponManager) == null || BDArmorySettings.PEACE_MODE)) //TheDog: replaced querying of targetdatabase with actual check if a target can be detected
            {
                //s.mainThrottle = 0;
                //vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                AdjustThrottle(0, true);
                return;
            }

            //upDirection = -FlightGlobals.getGeeForceAtPosition(transform.position).normalized;
            upDirection = VectorUtils.GetUpDirection(vessel.transform.position);

            CalculateAccelerationAndTurningCircle();

            if ((float)vessel.radarAltitude < minAltitude)
            { belowMinAltitude = true; }

            if (gainAltInhibited && (!belowMinAltitude || !(currentStatus == "Engaging" || currentStatus == "Evading" || currentStatus.StartsWith("Gain Alt"))))
            { // Allow switching between "Engaging", "Evading" and "Gain Alt." while below minimum altitude without disabling the gain altitude inhibitor.
                gainAltInhibited = false;
                if (BDArmorySettings.DEBUG_AI) Debug.Log("[BDArmory.BDModulePilotAI]: " + vessel.vesselName + " is no longer inhibiting gain alt");
            }

            if (!gainAltInhibited && belowMinAltitude && (currentStatus == "Engaging" || currentStatus == "Evading"))
            { // Vessel went below minimum altitude while "Engaging" or "Evading", enable the gain altitude inhibitor.
                gainAltInhibited = true;
                if (BDArmorySettings.DEBUG_AI) Debug.Log("[BDArmory.BDModulePilotAI]: " + vessel.vesselName + " was " + currentStatus + " and went below min altitude, inhibiting gain alt.");
            }

            if (vessel.srfSpeed < minSpeed)
            { regainEnergy = true; }
            else if (!belowMinAltitude && vessel.srfSpeed > Mathf.Min(minSpeed + 20f, idleSpeed))
            { regainEnergy = false; }


            UpdateVelocityRelativeDirections();
            CheckLandingGear();
            if (IsRunningWaypoints) UpdateWaypoint(); // Update the waypoint state.

            if (!vessel.LandedOrSplashed && (FlyAvoidTerrain(s) || (!ramming && FlyAvoidOthers(s)))) // Avoid terrain and other planes.
            { turningTimer = 0; }
            else if (initialTakeOff) // Take off.
            {
                TakeOff(s);
                turningTimer = 0;
            }
            else
            {
                if (!(command == PilotCommands.Free || command == PilotCommands.Waypoints))
                {
                    if (belowMinAltitude && !(gainAltInhibited || BDArmorySettings.SF_REPULSOR)) // If we're below minimum altitude, gain altitude unless we're being inhibited or the space friction repulsor field is enabled.
                    {
                        TakeOff(s);
                        turningTimer = 0;
                    }
                    else // Follow the current command.
                    { UpdateCommand(s); }
                }
                else // Do combat stuff or orbit. (minAlt is handled in UpdateAI for Free and Waypoints modes.)
                { UpdateAI(s); }
            }
            UpdateGAndAoALimits(s);
            AdjustPitchForGAndAoALimits(s);

            // Perform the check here since we're now allowing evading/engaging while below mininum altitude.
            if (belowMinAltitude && vessel.radarAltitude > minAltitude && Vector3.Dot(vessel.Velocity(), vessel.upAxis) > 0) // We're good.
            {
                terrainAlertCoolDown = 1.0f; // 1s cool down after gaining altitude.
                belowMinAltitude = false;
            }

            if (BDArmorySettings.DEBUG_AI)
            {
                if (lastStatus != currentStatus && !(lastStatus.StartsWith("Gain Alt.") && currentStatus.StartsWith("Gain Alt.")) && !(lastStatus.StartsWith("Terrain") && currentStatus.StartsWith("Terrain")) && !(lastStatus.StartsWith("Waypoint") && currentStatus.StartsWith("Waypoint")))
                {
                    Debug.Log("[BDArmory.BDModulePilotAI]: Status of " + vessel.vesselName + " changed from " + lastStatus + " to " + currentStatus);
                }
                lastStatus = currentStatus;
            }
        }

        void UpdateAI(FlightCtrlState s)
        {
            SetStatus("Free");

            CheckExtend(ExtendChecks.RequestsOnly);

            // Calculate threat rating from any threats
            float minimumEvasionTime = minEvasionTime;
            threatRating = evasionThreshold + 1f; // Don't evade by default
            wasEvading = evading;
            evading = false;
            if (weaponManager != null)
            {
                if (weaponManager.incomingMissileTime <= weaponManager.cmThreshold)
                {
                    threatRating = -1f; // Allow entering evasion code if we're under missile fire
                    minimumEvasionTime = 0f; //  Trying to evade missile threats when they don't exist will result in NREs
                }
                else if (weaponManager.underFire && !ramming) // If we're ramming, ignore gunfire.
                {
                    if (weaponManager.incomingMissTime >= evasionTimeThreshold) // If we haven't been under fire long enough, ignore gunfire
                        threatRating = weaponManager.incomingMissDistance;
                }
            }

            debugString.AppendLine($"Threat Rating: {threatRating:G3}");

            // If we're currently evading or a threat is significant and we're not ramming.
            if ((evasiveTimer < minimumEvasionTime && evasiveTimer != 0) || threatRating < evasionThreshold)
            {
                if (evasiveTimer < minimumEvasionTime)
                {
                    threatRelativePosition = vessel.Velocity().normalized + vesselTransform.right;

                    if (weaponManager)
                    {
                        if (weaponManager.rwr != null ? weaponManager.rwr.rwrEnabled : false) //use rwr to check missile threat direction
                        {
                            Vector3 missileThreat = Vector3.zero;
                            bool missileThreatDetected = false;
                            float closestMissileThreat = float.MaxValue;
                            for (int i = 0; i < weaponManager.rwr.pingsData.Length; i++)
                            {
                                TargetSignatureData threat = weaponManager.rwr.pingsData[i];
                                if (threat.exists && threat.signalStrength == (float)RadarWarningReceiver.RWRThreatTypes.MissileLock)
                                {
                                    missileThreatDetected = true;
                                    float dist = (weaponManager.rwr.pingWorldPositions[i] - vesselTransform.position).sqrMagnitude;
                                    if (dist < closestMissileThreat)
                                    {
                                        closestMissileThreat = dist;
                                        missileThreat = weaponManager.rwr.pingWorldPositions[i];
                                    }
                                }
                            }
                            if (missileThreatDetected)
                            {
                                threatRelativePosition = missileThreat - vesselTransform.position;
                                if (extending)
                                    StopExtending("missile threat"); // Don't keep trying to extend if under fire from missiles
                            }
                        }

                        if (weaponManager.underFire)
                        {
                            threatRelativePosition = weaponManager.incomingThreatPosition - vesselTransform.position;
                        }
                    }
                }
                Evasive(s);
                evasiveTimer += Time.fixedDeltaTime;
                turningTimer = 0;

                if (evasiveTimer >= minimumEvasionTime)
                {
                    evasiveTimer = 0;
                    collisionDetectionTicker = vesselCollisionAvoidanceTickerFreq + 1; //check for collision again after exiting evasion routine
                }
                if (evading) return;
            }
            else if (belowMinAltitude && !(gainAltInhibited || BDArmorySettings.SF_REPULSOR)) // If we're below minimum altitude, gain altitude unless we're being inhibited or the space friction repulsor field is enabled.
            {
                TakeOff(s); // Gain Altitude
                turningTimer = 0;
                return;
            }
            else if (!extending && IsRunningWaypoints)
            {
                // FIXME To avoid getting stuck circling a waypoint, a check should be made (maybe use the turningTimer for this?), in which case the plane should RequestExtend away from the waypoint.
                FlyWaypoints(s);
                return;
            }
            else if (!extending && weaponManager && targetVessel != null && targetVessel.transform != null)
            {
                evasiveTimer = 0;
                if (!targetVessel.LandedOrSplashed)
                {
                    Vector3 targetVesselRelPos = targetVessel.vesselTransform.position - vesselTransform.position;
                    if (canExtend && vessel.altitude < defaultAltitude && Vector3.Angle(targetVesselRelPos, -upDirection) < 35) // Target is at a steep angle below us and we're below default altitude, extend to get a better angle instead of attacking now.
                    {
                        RequestExtend("too steeply below", targetVessel);
                    }

                    if (Vector3.Angle(targetVessel.vesselTransform.position - vesselTransform.position, vesselTransform.up) > 35) // If target is outside of 35° cone ahead of us then keep flying straight.
                    {
                        turningTimer += Time.fixedDeltaTime;
                    }
                    else
                    {
                        turningTimer = 0;
                    }

                    debugString.AppendLine($"turningTimer: {turningTimer}");

                    float targetForwardDot = Vector3.Dot(targetVesselRelPos.normalized, vesselTransform.up); // Cosine of angle between us and target (1 if target is in front of us , -1 if target is behind us)
                    float targetVelFrac = (float)(targetVessel.srfSpeed / vessel.srfSpeed);      //this is the ratio of the target vessel's velocity to this vessel's srfSpeed in the forward direction; this allows smart decisions about when to break off the attack

                    float extendTargetDot = Mathf.Cos(extendTargetAngle * Mathf.Deg2Rad);
                    if (canExtend && targetVelFrac < extendTargetVel && targetForwardDot < extendTargetDot && targetVesselRelPos.sqrMagnitude < extendTargetDist * extendTargetDist) // Default values: Target is outside of ~78° cone ahead, closer than 400m and slower than us, so we won't be able to turn to attack it now.
                    {
                        RequestExtend("can't turn fast enough", targetVessel);
                        weaponManager.ForceScan();
                    }
                    if (canExtend && turningTimer > 15)
                    {
                        RequestExtend("turning too long", targetVessel); //extend if turning circles for too long
                        turningTimer = 0;
                        weaponManager.ForceScan();
                    }
                }
                else //extend if too close for an air-to-ground attack
                {
                    CheckExtend(ExtendChecks.AirToGroundOnly);
                }

                if (!extending)
                {
                    if (weaponManager.HasWeaponsAndAmmo() || !RamTarget(s, targetVessel)) // If we're out of ammo, see if we can ram someone, otherwise, behave as normal.
                    {
                        ramming = false;
                        SetStatus("Engaging");
                        debugString.AppendLine($"Flying to target " + targetVessel.vesselName);
                        FlyToTargetVessel(s, targetVessel);
                        return;
                    }
                }
            }
            else
            {
                evasiveTimer = 0;
                if (!extending && !(terrainAlertCoolDown > 0))
                {
                    SetStatus("Orbiting");
                    FlyOrbit(s, assignedPositionGeo, 2000, idleSpeed, ClockwiseOrbit);
                    return;
                }
            }

            if (CheckExtend())
            {
                weaponManager.ForceScan();
                evasiveTimer = 0;
                SetStatus("Extending");
                debugString.AppendLine($"Extending");
                FlyExtend(s, lastTargetPosition);
                return;
            }
        }

        bool PredictCollisionWithVessel(Vessel v, float maxTime, out Vector3 badDirection)
        {
            if (vessel == null || v == null || v == (weaponManager != null ? weaponManager.incomingMissileVessel : null)
                || v.rootPart.FindModuleImplementing<MissileBase>() != null) //evasive will handle avoiding missiles
            {
                badDirection = Vector3.zero;
                return false;
            }

            // Adjust some values for asteroids.
            var targetRadius = v.GetRadius(true);
            var threshold = collisionAvoidanceThreshold + targetRadius; // Add the target's average radius to the threshold.
            if (v.vesselType == VesselType.SpaceObject) // Give asteroids some extra room.
            {
                maxTime += targetRadius / (float)vessel.srfSpeed * (turnRadiusTwiddleFactorMin + turnRadiusTwiddleFactorMax);
            }

            // Use the nearest time to closest point of approach to check separation instead of iteratively sampling. Should give faster, more accurate results.
            float timeToCPA = vessel.ClosestTimeToCPA(v, maxTime); // This uses the same kinematics as AIUtils.PredictPosition.
            if (timeToCPA > 0 && timeToCPA < maxTime)
            {
                Vector3 tPos = AIUtils.PredictPosition(v, timeToCPA);
                Vector3 myPos = AIUtils.PredictPosition(vessel, timeToCPA);
                if (Vector3.SqrMagnitude(tPos - myPos) < threshold * threshold) // Within collisionAvoidanceThreshold of each other. Danger Will Robinson!
                {
                    badDirection = tPos - vesselTransform.position;
                    return true;
                }
            }

            badDirection = Vector3.zero;
            return false;
        }

        bool RamTarget(FlightCtrlState s, Vessel v)
        {
            if (BDArmorySettings.DISABLE_RAMMING || !allowRamming) return false; // Override from BDArmory settings and local config.
            if (v == null) return false; // We don't have a target.
            if (Vector3.Dot(vessel.srf_vel_direction, v.srf_vel_direction) * (float)v.srfSpeed / (float)vessel.srfSpeed > 0.95f) return false; // We're not approaching them fast enough.
            Vector3 relVelocity = v.Velocity() - vessel.Velocity();
            Vector3 relPosition = v.transform.position - vessel.transform.position;
            Vector3 relAcceleration = v.acceleration - vessel.acceleration;
            float timeToCPA = vessel.ClosestTimeToCPA(v, 16f);

            // Let's try to ram someone!
            if (!ramming)
                ramming = true;
            SetStatus("Ramming speed!");

            // Ease in velocity from 16s to 8s, ease in acceleration from 8s to 2s using the logistic function to give smooth adjustments to target point.
            float easeAccel = Mathf.Clamp01(1.1f / (1f + Mathf.Exp((timeToCPA - 5f))) - 0.05f);
            float easeVel = Mathf.Clamp01(2f - timeToCPA / 8f);
            Vector3 predictedPosition = AIUtils.PredictPosition(v.transform.position, v.Velocity() * easeVel, v.acceleration * easeAccel, timeToCPA + TimeWarp.fixedDeltaTime); // Compensate for the off-by-one frame issue.

            // Set steer mode to aiming for less than 8s left
            if (timeToCPA < 8f)
                steerMode = SteerModes.Aiming;
            else
                steerMode = SteerModes.NormalFlight;

            if (controlSurfaceLag > 0)
                predictedPosition += -1 * controlSurfaceLag * controlSurfaceLag * (timeToCPA / controlSurfaceLag - 1f + Mathf.Exp(-timeToCPA / controlSurfaceLag)) * vessel.acceleration * easeAccel; // Compensation for control surface lag.
            FlyToPosition(s, predictedPosition);
            AdjustThrottle(maxSpeed, false, true); // Ramming speed!

            return true;
        }

        Vector3 staleTargetPosition = Vector3.zero;
        Vector3 staleTargetVelocity = Vector3.zero;
        void FlyToTargetVessel(FlightCtrlState s, Vessel v)
        {
            Vector3 target = AIUtils.PredictPosition(v, TimeWarp.fixedDeltaTime);//v.CoM;
            MissileBase missile = null;
            Vector3 vectorToTarget = v.transform.position - vesselTransform.position;
            float distanceToTarget = vectorToTarget.magnitude;
            float planarDistanceToTarget = Vector3.ProjectOnPlane(vectorToTarget, upDirection).magnitude;
            float angleToTarget = Vector3.Angle(target - vesselTransform.position, vesselTransform.up);
            float strafingDistance = -1f;
            float relativeVelocity = (float)(vessel.srf_velocity - v.srf_velocity).magnitude;

            if (weaponManager)
            {
                if (weaponManager.detectedTargetTimeout <= weaponManager.targetScanInterval) staleTargetVelocity = Vector3.zero; //if actively tracking target, reset last known velocity vector
                missile = weaponManager.CurrentMissile;
                if (missile != null)
                {
                    if (missile.GetWeaponClass() == WeaponClasses.Missile)
                    {
                        if (distanceToTarget > 5500f)
                        {
                            finalMaxSteer = GetSteerLimiterForSpeedAndPower();
                        }

                        if (missile.TargetingMode == MissileBase.TargetingModes.Heat && !weaponManager.heatTarget.exists)
                        {
                            debugString.AppendLine($"Attempting heat lock");
                            target += v.srf_velocity.normalized * 10;
                        }
                        else
                        {
                            target = MissileGuidance.GetAirToAirFireSolution(missile, v);
                        }

                        if (angleToTarget < 20f)
                        {
                            steerMode = SteerModes.Aiming;
                        }
                    }
                    else //bombing
                    {
                        if (distanceToTarget > 4500f)
                        {
                            finalMaxSteer = GetSteerLimiterForSpeedAndPower();
                        }

                        if (angleToTarget < 45f)
                        {
                            target = target + (Mathf.Max(defaultAltitude - 500f, minAltitude) * upDirection);
                            Vector3 tDir = (target - vesselTransform.position).normalized;
                            tDir = (1000 * tDir) - (vessel.Velocity().normalized * 600);
                            target = vesselTransform.position + tDir;
                        }
                        else
                        {
                            target = target + (Mathf.Max(defaultAltitude - 500f, minAltitude) * upDirection);
                        }
                    }
                }
                else if (weaponManager.currentGun)
                {
                    ModuleWeapon weapon = weaponManager.currentGun;
                    if (weapon != null)
                    {
                        Vector3 leadOffset = weapon.GetLeadOffset();

                        float targetAngVel = Vector3.Angle(v.transform.position - vessel.transform.position, v.transform.position + (vessel.Velocity()) - vessel.transform.position);
                        float magnifier = Mathf.Clamp(targetAngVel, 1f, 2f);
                        magnifier += ((magnifier - 1f) * Mathf.Sin(Time.time * 0.75f));
                        debugString.AppendLine($"targetAngVel: {targetAngVel:F4}, magnifier: {magnifier:F2}");
                        target -= magnifier * leadOffset; // The effect of this is to exagerate the lead if the angular velocity is > 1
                        angleToTarget = Vector3.Angle(vesselTransform.up, target - vesselTransform.position);
                        if (distanceToTarget < weaponManager.gunRange && angleToTarget < 20)
                        {
                            steerMode = SteerModes.Aiming; //steer to aim
                        }
                        else
                        {
                            if (distanceToTarget > 3500f || angleToTarget > 90f || vessel.srfSpeed < takeOffSpeed)
                            {
                                finalMaxSteer = GetSteerLimiterForSpeedAndPower();
                            }
                            else
                            {
                                //figuring how much to lead the target's movement to get there after its movement assuming we can manage a constant speed turn
                                //this only runs if we're not aiming and not that far from the target and the target is in front of us
                                float curVesselMaxAccel = Math.Min(dynDynPresGRecorded * (float)vessel.dynamicPressurekPa, maxAllowedGForce * bodyGravity);
                                if (curVesselMaxAccel > 0)
                                {
                                    float timeToTurn = (float)vessel.srfSpeed * angleToTarget * Mathf.Deg2Rad / curVesselMaxAccel;
                                    target += v.Velocity() * timeToTurn;
                                    target += 0.5f * v.acceleration * timeToTurn * timeToTurn;
                                }
                            }
                        }

                        if (v.LandedOrSplashed)
                        {
                            if (distanceToTarget < weapon.engageRangeMax + relativeVelocity) // Distance until starting to strafe plus 1s for changing speed.
                            {
                                strafingDistance = Mathf.Max(0f, distanceToTarget - weapon.engageRangeMax);
                            }
                            if (distanceToTarget > weapon.engageRangeMax)
                            {
                                target = FlightPosition(target, defaultAltitude);
                            }
                            else
                            {
                                steerMode = SteerModes.Aiming;
                            }
                        }
                        else if (distanceToTarget > weaponManager.gunRange * 1.5f || Vector3.Dot(target - vesselTransform.position, vesselTransform.up) < 0) // Target is airborne a long way away or behind us.
                        {
                            target = v.CoM; // Don't bother with the off-by-one physics frame correction as this doesn't need to be so accurate here.
                        }
                    }
                }
                else if (planarDistanceToTarget > weaponManager.gunRange * 1.25f && (vessel.altitude < v.altitude || (float)vessel.radarAltitude < defaultAltitude)) //climb to target vessel's altitude if lower and still too far for guns
                {
                    finalMaxSteer = GetSteerLimiterForSpeedAndPower();
                    if (v.LandedOrSplashed) vectorToTarget += upDirection * defaultAltitude; // If the target is landed or splashed, aim for the default altitude whiel we're outside our gun's range.
                    target = vesselTransform.position + GetLimitedClimbDirectionForSpeed(vectorToTarget);
                }
                else
                {
                    finalMaxSteer = GetSteerLimiterForSpeedAndPower();
                }
                if (weaponManager.detectedTargetTimeout > weaponManager.targetScanInterval) //lost track of target, but know it's in general area, simulate location estimate precision decay over time
                {
                    if (staleTargetVelocity == Vector3.zero) staleTargetVelocity = v.Velocity(); //if lost target, follow last known velocity vector
                    target += (staleTargetVelocity * weaponManager.detectedTargetTimeout) + (staleTargetPosition * weaponManager.detectedTargetTimeout);
                }
            }

            float targetDot = Vector3.Dot(vesselTransform.up, v.transform.position - vessel.transform.position);

            //manage speed when close to enemy
            float finalMaxSpeed = maxSpeed;
            if (targetDot > 0f) // Target is ahead.
            {
                if (strafingDistance < 0f) // target flying, or beyond range of beginning strafing run for landed/splashed targets.
                {
                    if (distanceToTarget > vesselStandoffDistance) // Adjust target speed based on distance from desired stand-off distance.
                        finalMaxSpeed = (distanceToTarget - vesselStandoffDistance) / 8f + (float)v.srfSpeed; // Beyond stand-off distance, approach a little faster.
                    else
                    {
                        //Mathf.Max(finalMaxSpeed = (distanceToTarget - vesselStandoffDistance) / 8f + (float)v.srfSpeed, 0); //for less aggressive braking
                        finalMaxSpeed = distanceToTarget / vesselStandoffDistance * (float)v.srfSpeed; // Within stand-off distance, back off the thottle a bit.
                        debugString.AppendLine($"Getting too close to Enemy. Braking!");
                    }
                }
                else
                {
                    finalMaxSpeed = strafingSpeed + (float)v.srfSpeed;
                }
            }
            finalMaxSpeed = Mathf.Clamp(finalMaxSpeed, minSpeed, maxSpeed);
            AdjustThrottle(finalMaxSpeed, true);

            if ((targetDot < 0 && vessel.srfSpeed > finalMaxSpeed)
                && distanceToTarget < 300 && vessel.srfSpeed < v.srfSpeed * 1.25f && Vector3.Dot(vessel.Velocity(), v.Velocity()) > 0) //distance is less than 800m
            {
                debugString.AppendLine($"Enemy on tail. Braking!");
                AdjustThrottle(minSpeed, true);
            }

            if (missile != null)
            {
                var minDynamicLaunchRange = MissileLaunchParams.GetDynamicLaunchParams(missile, v.Velocity(), v.transform.position).minLaunchRange;
                if (canExtend && targetDot > 0 && distanceToTarget < minDynamicLaunchRange && vessel.srfSpeed > idleSpeed)
                {
                    RequestExtend("too close for missile", v, minDynamicLaunchRange); // Get far enough away to use the missile.
                }
            }

            if (regainEnergy && angleToTarget > 30f)
            {
                RegainEnergy(s, target - vesselTransform.position);
                return;
            }
            else
            {
                useVelRollTarget = true;
                FlyToPosition(s, target);
                return;
            }
        }

        void RegainEnergy(FlightCtrlState s, Vector3 direction, float throttleOverride = -1f)
        {
            debugString.AppendLine($"Regaining energy");

            steerMode = SteerModes.Aiming;
            Vector3 planarDirection = Vector3.ProjectOnPlane(direction, upDirection);
            float angle = (Mathf.Clamp((float)vessel.radarAltitude - minAltitude, 0, 1500) / 1500) * 90;
            angle = Mathf.Clamp(angle, 0, 55) * Mathf.Deg2Rad;

            Vector3 targetDirection = Vector3.RotateTowards(planarDirection, -upDirection, angle, 0);
            targetDirection = Vector3.RotateTowards(vessel.Velocity(), targetDirection, 15f * Mathf.Deg2Rad, 0).normalized;

            if (throttleOverride >= 0)
                AdjustThrottle(maxSpeed, false, true, false, throttleOverride);
            else
                AdjustThrottle(maxSpeed, false, true);

            FlyToPosition(s, vesselTransform.position + (targetDirection * 100), true);
        }

        float GetSteerLimiterForSpeedAndPower()
        {
            float possibleAccel = speedController.GetPossibleAccel();
            float speed = (float)vessel.srfSpeed;

            debugString.AppendLine($"possibleAccel: {possibleAccel}");

            float limiter = ((speed - minSpeed) / 2 / minSpeed) + possibleAccel / 15f; // FIXME The calculation for possibleAccel needs further investigation.
            debugString.AppendLine($"unclamped limiter: {limiter}");

            return Mathf.Clamp01(limiter);
        }

        float GetUserDefinedSteerLimit()
        {
            float limiter = 1;
            if (maxSteer > maxSteerAtMaxSpeed)
                limiter *= Mathf.Clamp((maxSteerAtMaxSpeed - maxSteer) / (cornerSpeed - lowSpeedSwitch + 0.001f) * ((float)vessel.srfSpeed - lowSpeedSwitch) + maxSteer, maxSteerAtMaxSpeed, maxSteer); // Linearly varies between two limits, clamped at limit values
            else
                limiter *= Mathf.Clamp((maxSteerAtMaxSpeed - maxSteer) / (cornerSpeed - lowSpeedSwitch + 0.001f) * ((float)vessel.srfSpeed - lowSpeedSwitch) + maxSteer, maxSteer, maxSteerAtMaxSpeed); // Linearly varies between two limits, clamped at limit values
            limiter *= 1.225f / (float)vessel.atmDensity; // Scale based on atmospheric density relative to sea level Kerbin (since dynamic pressure depends on density)

            return Mathf.Clamp01(limiter);
        }

        Vector3 prevTargetDir;
        Vector3 debugPos;
        bool useVelRollTarget;

        void FlyToPosition(FlightCtrlState s, Vector3 targetPosition, bool overrideThrottle = false)
        {
            if (!belowMinAltitude) // Includes avoidingTerrain
            {
                if (weaponManager && Time.time - weaponManager.timeBombReleased < 1.5f)
                {
                    targetPosition = vessel.transform.position + vessel.Velocity();
                }

                targetPosition = FlightPosition(targetPosition, minAltitude);
                targetPosition = vesselTransform.position + ((targetPosition - vesselTransform.position).normalized * 100);
            }

            Vector3d srfVel = vessel.Velocity();
            if (srfVel != Vector3d.zero)
            {
                velocityTransform.rotation = Quaternion.LookRotation(srfVel, -vesselTransform.forward);
            }
            velocityTransform.rotation = Quaternion.AngleAxis(90, velocityTransform.right) * velocityTransform.rotation;

            //ang vel
            Vector3 localAngVel = vessel.angularVelocity;
            //test
            Vector3 currTargetDir = (targetPosition - vesselTransform.position).normalized;
            // if (steerMode == SteerModes.NormalFlight) // This block was doing nothing... what was it originally for?
            // {
            //     float gRotVel = ((10f * maxAllowedGForce) / ((float)vessel.srfSpeed));
            //     //currTargetDir = Vector3.RotateTowards(prevTargetDir, currTargetDir, gRotVel*Mathf.Deg2Rad, 0);
            // }
            if (IsExtending || IsEvading) // If we're extending or evading, add a deviation to the fly-to direction to make us harder to hit.
            {
                var squigglySquidTime = 90f * (float)vessel.missionTime + 8f * Mathf.Sin((float)vessel.missionTime * 6.28f) + 16f * Mathf.Sin((float)vessel.missionTime * 3.14f); // Vary the rate around 90°/s to be more unpredictable.
                var squigglySquidDirection = Quaternion.AngleAxis(evasionNonlinearityDirection * squigglySquidTime, currTargetDir) * Vector3.ProjectOnPlane(upDirection, currTargetDir).normalized;
#if DEBUG
                DEBUG_vector = squigglySquidDirection;
#endif
                debugString.AppendLine($"Squiggly Squid: {Vector3.Angle(currTargetDir, Vector3.RotateTowards(currTargetDir, squigglySquidDirection, evasionNonlinearity * Mathf.Deg2Rad, 0f))}° at {((squigglySquidTime) % 360f).ToString("G3")}°");
                currTargetDir = Vector3.RotateTowards(currTargetDir, squigglySquidDirection, evasionNonlinearity * Mathf.Deg2Rad, 0f);
            }
            Vector3 targetAngVel = Vector3.Cross(prevTargetDir, currTargetDir) / Time.fixedDeltaTime;
            Vector3 localTargetAngVel = vesselTransform.InverseTransformVector(targetAngVel);
            prevTargetDir = currTargetDir;
            targetPosition = vessel.transform.position + (currTargetDir * 100);

            flyingToPosition = targetPosition;

            //test poststall
            float AoA = Vector3.Angle(vessel.ReferenceTransform.up, vessel.Velocity());
            if (AoA > maxAllowedAoA)
            {
                steerMode = SteerModes.Aiming;
            }

            //slow down for tighter turns
            float velAngleToTarget = Mathf.Clamp(Vector3.Angle(targetPosition - vesselTransform.position, vessel.Velocity()), 0, 90);
            float speedReductionFactor = 1.25f;
            float finalSpeed = Mathf.Min(speedController.targetSpeed, Mathf.Clamp(maxSpeed - (speedReductionFactor * velAngleToTarget), idleSpeed, maxSpeed));
            debugString.AppendLine($"Final Target Speed: {finalSpeed}");

            if (!overrideThrottle)
            {
                AdjustThrottle(finalSpeed, useBrakes, useAB);
            }

            if (steerMode == SteerModes.Aiming)
            {
                localAngVel -= localTargetAngVel;
            }

            Vector3 targetDirection;
            Vector3 targetDirectionYaw;
            float yawError;
            float pitchError;
            //float postYawFactor;
            //float postPitchFactor;
            if (steerMode == SteerModes.NormalFlight)
            {
                targetDirection = velocityTransform.InverseTransformDirection(targetPosition - velocityTransform.position).normalized;
                targetDirection = Vector3.RotateTowards(Vector3.up, targetDirection, 45 * Mathf.Deg2Rad, 0);

                if (useWaypointYawAuthority && IsRunningWaypoints)
                {
                    var refYawDir = Vector3.RotateTowards(Vector3.up, vesselTransform.InverseTransformDirection(targetPosition - vesselTransform.position), 25 * Mathf.Deg2Rad, 0).normalized;
                    var velYawDir = Vector3.RotateTowards(Vector3.up, vesselTransform.InverseTransformDirection(vessel.Velocity()), 45 * Mathf.Deg2Rad, 0).normalized;
                    targetDirectionYaw = waypointYawAuthorityStrength * refYawDir + (1f - waypointYawAuthorityStrength) * velYawDir;
                }
                else
                {
                    targetDirectionYaw = vesselTransform.InverseTransformDirection(vessel.Velocity()).normalized;
                    targetDirectionYaw = Vector3.RotateTowards(Vector3.up, targetDirectionYaw, 45 * Mathf.Deg2Rad, 0);
                }
            }
            else//(steerMode == SteerModes.Aiming)
            {
                targetDirection = vesselTransform.InverseTransformDirection(targetPosition - vesselTransform.position).normalized;
                targetDirection = Vector3.RotateTowards(Vector3.up, targetDirection, 25 * Mathf.Deg2Rad, 0);
                targetDirectionYaw = targetDirection;
            }
            debugPos = vessel.transform.position + (targetPosition - vesselTransform.position) * 5000;

            //// Adjust targetDirection based on ATTITUDE limits
            //var horizonUp = Vector3.ProjectOnPlane(vesselTransform.up, upDirection).normalized;
            //var horizonRight = -Vector3.Cross(horizonUp, upDirection);
            //float attitude = Vector3.SignedAngle(horizonUp, vesselTransform.up, horizonRight);
            //if ((Mathf.Abs(attitude) > maxAttitude) && (maxAttitude != 90f))
            //{
            //    var projectPlane = Vector3.RotateTowards(upDirection, horizonUp, attitude * Mathf.PI / 180f, 0f);
            //    targetDirection = Vector3.ProjectOnPlane(targetDirection, projectPlane);
            //}
            //debugString.AppendLine($"Attitude: " + attitude);

            pitchError = VectorUtils.SignedAngle(Vector3.up, Vector3.ProjectOnPlane(targetDirection, Vector3.right), Vector3.back);
            yawError = VectorUtils.SignedAngle(Vector3.up, Vector3.ProjectOnPlane(targetDirectionYaw, Vector3.forward), Vector3.right);

            // User-set steer limits
            float userLimit = GetUserDefinedSteerLimit();
            finalMaxSteer *= userLimit;
            finalMaxSteer = Mathf.Clamp(finalMaxSteer, 0.1f, 1f); // added just in case to ensure some input is retained no matter what happens

            //roll
            Vector3 currentRoll = -vesselTransform.forward;
            float rollUp = (steerMode == SteerModes.Aiming ? 5f : 10f);
            if (steerMode == SteerModes.NormalFlight)
            {
                rollUp += (1 - finalMaxSteer) * 10f;
            }
            rollTarget = (targetPosition + (rollUp * upDirection)) - vesselTransform.position;

            //test
            if (steerMode == SteerModes.Aiming && !belowMinAltitude)
            {
                angVelRollTarget = -140 * vesselTransform.TransformVector(Quaternion.AngleAxis(90f, Vector3.up) * localTargetAngVel);
                rollTarget += angVelRollTarget;
            }

            if (command == PilotCommands.Follow && useRollHint)
            {
                rollTarget = -commandLeader.vessel.ReferenceTransform.forward;
            }

            bool requiresLowAltitudeRollTargetCorrection = false;
            if (avoidingTerrain)
                rollTarget = terrainAlertNormal * 100;
            else if (belowMinAltitude && !gainAltInhibited)
                rollTarget = vessel.upAxis * 100;
            else if (!avoidingTerrain && vessel.verticalSpeed < 0 && Vector3.Dot(rollTarget, upDirection) < 0 && Vector3.Dot(rollTarget, vessel.Velocity()) < 0) // If we're not avoiding terrain, heading downwards and the roll target is behind us and downwards, check that a circle arc of radius "turn radius" (scaled by twiddle factor minimum) tilted at angle of rollTarget has enough room to avoid hitting the ground.
            {
                // The following calculates the altitude required to turn in the direction of the rollTarget based on the current velocity and turn radius.
                // The setup is a circle in the plane of the rollTarget, which is tilted by angle phi from vertical, with the vessel at the point subtending an angle theta as measured from the top of the circle.
                var n = Vector3.Cross(vessel.srf_vel_direction, rollTarget).normalized; // Normal of the plane of rollTarget.
                var m = Vector3.Cross(n, upDirection).normalized; // cos(theta) = dot(m,v).
                if (m.magnitude < 0.1f) m = upDirection; // In case n and upDirection are colinear.
                var a = Vector3.Dot(n, upDirection); // sin(phi) = dot(n,up)
                var b = Mathf.Sqrt(1f - a * a); // cos(phi) = sqrt(1-sin(phi)^2)
                var r = turnRadius * turnRadiusTwiddleFactorMin; // Radius of turning circle.
                var h = r * (1 + Vector3.Dot(m, vessel.srf_vel_direction)) * b; // Required altitude: h = r * (1+cos(theta)) * cos(phi).
                if (vessel.radarAltitude < h) // Too low for this manoeuvre.
                {
                    requiresLowAltitudeRollTargetCorrection = true; // For simplicity, we'll apply the correction after the projections have occurred.
                }
            }
            if (useWaypointRollTarget && IsRunningWaypoints)
            {
                var angle = waypointRollTargetStrength * Vector3.Angle(waypointRollTarget, rollTarget);
                rollTarget = Vector3.ProjectOnPlane(Vector3.RotateTowards(rollTarget, waypointRollTarget, angle * Mathf.Deg2Rad, 0f), vessel.Velocity());
            }
            else if (useVelRollTarget && !belowMinAltitude)
            {
                rollTarget = Vector3.ProjectOnPlane(rollTarget, vessel.Velocity());
                currentRoll = Vector3.ProjectOnPlane(currentRoll, vessel.Velocity());
            }
            else
            {
                rollTarget = Vector3.ProjectOnPlane(rollTarget, vesselTransform.up);
            }

            //ramming
            if (ramming)
                rollTarget = Vector3.ProjectOnPlane(targetPosition - vesselTransform.position + rollUp * Mathf.Clamp((targetPosition - vesselTransform.position).magnitude / 500f, 0f, 1f) * upDirection, vesselTransform.up);

            if (requiresLowAltitudeRollTargetCorrection) // Low altitude downwards loop prevention to avoid triggering terrain avoidance.
            {
                // Set the roll target to be horizontal.
                rollTarget = Vector3.ProjectOnPlane(rollTarget, upDirection).normalized * 100;
            }

            // Limit Bank Angle, this should probably be re-worked using quaternions or something like that, SignedAngle doesn't work well for angles > 90
            Vector3 horizonNormal = Vector3.ProjectOnPlane(vessel.transform.position - vessel.mainBody.transform.position, vesselTransform.up);
            float bankAngle = Vector3.SignedAngle(horizonNormal, rollTarget, vesselTransform.up);
            if ((Mathf.Abs(bankAngle) > maxBank) && (maxBank != 180))
                rollTarget = Vector3.RotateTowards(horizonNormal, rollTarget, maxBank / 180 * Mathf.PI, 0.0f);
            bankAngle = Vector3.SignedAngle(horizonNormal, rollTarget, vesselTransform.up);

            float rollError = BDAMath.SignedAngle(currentRoll, rollTarget, vesselTransform.right);
            if (steerMode == SteerModes.NormalFlight && !avoidingTerrain && evasiveTimer == 0 && currentlyAvoidedVessel == null) // Don't apply this fix while avoiding terrain, makes it difficult for craft to exit dives; or evading or avoiding other vessels as we need a quick reaction
            {
                //premature dive fix
                pitchError = pitchError * Mathf.Clamp01((21 - Mathf.Exp(Mathf.Abs(rollError) / 30)) / 20);
            }

            #region PID calculations
            // FIXME Why are there various constants in here that mess with the scaling of the PID in the various axes? Ratios between the axes are 1:0.33:0.1
            float pitchProportional = 0.015f * steerMult * pitchError;
            float yawProportional = 0.005f * steerMult * yawError;
            float rollProportional = 0.0015f * steerMult * rollError;

            float pitchDamping = SteerDamping(Mathf.Abs(Vector3.Angle(targetPosition - vesselTransform.position, vesselTransform.up)), Vector3.Angle(targetPosition - vesselTransform.position, vesselTransform.up), 1) * -localAngVel.x;
            float yawDamping = 0.33f * SteerDamping(Mathf.Abs(yawError * (steerMode == SteerModes.Aiming ? (180f / 25f) : 4f)), Vector3.Angle(targetPosition - vesselTransform.position, vesselTransform.up), 2) * -localAngVel.z;
            float rollDamping = 0.1f * SteerDamping(Mathf.Abs(rollError), Vector3.Angle(targetPosition - vesselTransform.position, vesselTransform.up), 3) * -localAngVel.y;

            // For the integral, we track the vector of the pitch and yaw in the 2D plane of the vessel's forward pointing vector so that the pitch and yaw components translate between the axes when the vessel rolls.
            directionIntegral = Vector3.ProjectOnPlane(directionIntegral + (pitchError * -vesselTransform.forward + yawError * vesselTransform.right) * Time.deltaTime, vesselTransform.up);
            if (directionIntegral.sqrMagnitude > 1f) directionIntegral = directionIntegral.normalized;
            pitchIntegral = steerKiAdjust * Vector3.Dot(directionIntegral, -vesselTransform.forward);
            yawIntegral = 0.33f * steerKiAdjust * Vector3.Dot(directionIntegral, vesselTransform.right);
            rollIntegral = 0.1f * steerKiAdjust * Mathf.Clamp(rollIntegral + rollError * Time.deltaTime, -1f, 1f);

            var steerPitch = pitchProportional + pitchIntegral - pitchDamping;
            var steerYaw = yawProportional + yawIntegral - yawDamping;
            var steerRoll = rollProportional + rollIntegral - rollDamping;
            #endregion

            //v/q
            float dynamicAdjustment = Mathf.Clamp(16 * (float)(vessel.srfSpeed / vessel.dynamicPressurekPa), 0, 1.2f);
            steerPitch *= dynamicAdjustment;
            steerYaw *= dynamicAdjustment;
            steerRoll *= dynamicAdjustment;

            s.pitch = Mathf.Clamp(steerPitch, Mathf.Min(-finalMaxSteer, -0.2f), finalMaxSteer); // finalMaxSteer for pitch and yaw, user-defined steer limit for roll.
            s.yaw = Mathf.Clamp(steerYaw, -finalMaxSteer, finalMaxSteer);
            s.roll = Mathf.Clamp(steerRoll, -userLimit, userLimit);

            if (autoTune)
            { pidAutoTuning.Update(pitchError, rollError, yawError); }

            if (BDArmorySettings.DEBUG_TELEMETRY)
            {
                debugString.AppendLine(String.Format("steerMode: {0}, rollError: {1,7:F4}, pitchError: {2,7:F4}, yawError: {3,7:F4}", steerMode, rollError, pitchError, yawError));
                debugString.AppendLine($"finalMaxSteer: {finalMaxSteer:G3}, dynAdj: {dynamicAdjustment:G3}");
                // debugString.AppendLine($"Bank Angle: " + bankAngle);
                debugString.AppendLine(String.Format("Pitch: P: {0,7:F4}, I: {1,7:F4}, D: {2,7:F4}", pitchProportional, pitchIntegral, pitchDamping));
                debugString.AppendLine(String.Format("Yaw: P: {0,7:F4}, I: {1,7:F4}, D: {2,7:F4}", yawProportional, yawIntegral, yawDamping));
                debugString.AppendLine(String.Format("Roll: P: {0,7:F4}, I: {1,7:F4}, D: {2,7:F4}", rollProportional, rollIntegral, rollDamping));
            }
        }

        enum ExtendChecks { All, RequestsOnly, AirToGroundOnly };
        bool CheckExtend(ExtendChecks checkType = ExtendChecks.All)
        {
            // Sanity checks.
            if (weaponManager == null)
            {
                StopExtending("no weapon manager");
                return false;
            }
            if (weaponManager.TargetOverride) // Target is overridden, follow others' instructions.
            {
                StopExtending("target override");
                return false;
            }
            if (!extending)
            {
                extendParametersSet = false; // Reset this flag for new extends.
                extendHorizontally = true;
            }
            if (requestedExtend)
            {
                requestedExtend = false;
                if (CheckRequestedExtendDistance())
                {
                    extending = true;
                    lastTargetPosition = requestedExtendTpos;
                }
            }
            if (checkType == ExtendChecks.RequestsOnly) return extending;
            if (extending && extendParametersSet)
            {
                if (extendTarget != null) // Update the last known target position.
                { lastTargetPosition = extendTarget.CoM; }
                return true; // Already extending.
            }
            if (!wasEvading) evasionNonlinearityDirection = Mathf.Sign(UnityEngine.Random.Range(-1f, 1f)); // This applies to extending too.

            // Dropping a bomb.
            if (extending && weaponManager.CurrentMissile && weaponManager.CurrentMissile.GetWeaponClass() == WeaponClasses.Bomb) // Run away from the bomb!
            {
                extendDistance = extendRequestMinDistance; //4500; //what, are we running from nukes? blast radius * 1.5 should be sufficient
                desiredMinAltitude = defaultAltitude;
                extendParametersSet = true;
                if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI]: {vessel.vesselName} is extending due to dropping a bomb!");
                return true;
            }

            // Ground targets.
            if (targetVessel != null && targetVessel.LandedOrSplashed)
            {
                var selectedGun = weaponManager.currentGun;
                if (selectedGun == null && weaponManager.selectedWeapon == null) selectedGun = weaponManager.previousGun;
                if (selectedGun != null && !selectedGun.engageGround) // Don't extend from ground targets when using a weapon that can't target ground targets.
                {
                    weaponManager.ForceScan(); // Look for another target instead.
                    return false;
                }
                if (selectedGun != null) // If using a gun or no weapon is selected, take the extend multiplier into account.
                {
                    // extendDistance = Mathf.Clamp(weaponManager.guardRange - 1800, 500, 4000) * extendMult; // General extending distance.
                    extendDistance = extendDistanceAirToGroundGuns;
                    desiredMinAltitude = minAltitude + 0.5f * extendDistance; // Desired minimum altitude after extending. (30° attack vector plus min alt.)
                }
                else
                {
                    // extendDistance = Mathf.Clamp(weaponManager.guardRange - 1800, 2500, 4000);
                    // desiredMinAltitude = (float)vessel.radarAltitude + (defaultAltitude - (float)vessel.radarAltitude) * extendMult; // Desired minimum altitude after extending.
                    extendDistance = extendDistanceAirToGround;
                    desiredMinAltitude = defaultAltitude; // Desired minimum altitude after extending.
                }
                float srfDist = (GetSurfacePosition(targetVessel.transform.position) - GetSurfacePosition(vessel.transform.position)).sqrMagnitude;
                if (srfDist < extendDistance * extendDistance && Vector3.Angle(vesselTransform.up, targetVessel.transform.position - vessel.transform.position) > 45)
                {
                    extending = true;
                    extendingReason = "Surface target";
                    lastTargetPosition = targetVessel.transform.position;
                    extendTarget = targetVessel;
                    extendParametersSet = true;
                    if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI]: {vessel.vesselName} is extending due to a ground target.");
                    return true;
                }
            }
            if (checkType == ExtendChecks.AirToGroundOnly) return false;

            // Air target (from requests, where extendParameters haven't been set yet).
            if (extending && extendTarget != null && !extendTarget.LandedOrSplashed) // We have a flying target, only extend a short distance and don't climb.
            {
                extendDistance = Mathf.Max(extendDistanceAirToAir, extendRequestMinDistance);
                extendHorizontally = false;
                desiredMinAltitude = Mathf.Max((float)vessel.radarAltitude + _extendAngleAirToAir * extendDistance, minAltitude);
                extendParametersSet = true;
                if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI]: {vessel.vesselName} is extending due to an air target ({extendingReason}).");
                return true;
            }

            if (extending) StopExtending("no valid extend reason");
            return false;
        }

        /// <summary>
        /// Check whether the extend distance condition would not already be satisfied.
        /// </summary>
        /// <returns>True if the requested extend distance is not already satisfied.</returns>
        bool CheckRequestedExtendDistance()
        {
            if (extendTarget == null) return true; // Dropping a bomb or similar.
            float localExtendDistance = 1f;
            Vector3 extendVector = default;
            if (!extendTarget.LandedOrSplashed) // Airborne target.
            {
                localExtendDistance = Mathf.Max(extendDistanceAirToAir, extendRequestMinDistance);
                extendVector = vessel.transform.position - requestedExtendTpos;
            }
            else return true; // Ignore non-airborne targets for now. Currently, requests are only made for air-to-air targets and for dropping bombs.
            return extendVector.sqrMagnitude < localExtendDistance * localExtendDistance; // Extend from position is further than the extend distance.
        }

        void FlyExtend(FlightCtrlState s, Vector3 tPosition)
        {
            var extendVector = extendHorizontally ? Vector3.ProjectOnPlane(vessel.transform.position - tPosition, upDirection) : vessel.transform.position - tPosition;
            if (extendVector.sqrMagnitude < extendDistance * extendDistance) // Extend from position is closer (horizontally) than the extend distance.
            {
                Vector3 targetDirection = extendVector.normalized * extendDistance;
                Vector3 target = vessel.transform.position + targetDirection; // Target extend position horizontally.
                target = GetTerrainSurfacePosition(target) + (vessel.upAxis * Mathf.Min(defaultAltitude, MissileGuidance.GetRaycastRadarAltitude(vesselTransform.position))); // Adjust for terrain changes at target extend position.
                target = FlightPosition(target, desiredMinAltitude); // Further adjustments for speed, situation, etc. and desired minimum altitude after extending.
                if (regainEnergy)
                {
                    RegainEnergy(s, target - vesselTransform.position);
                    return;
                }
                else
                {
                    FlyToPosition(s, target);
                }
            }
            else // We're far enough away, stop extending.
            {
                StopExtending($"gone far enough (" + extendVector.magnitude + " of " + extendDistance + ")");
            }
        }

        void FlyOrbit(FlightCtrlState s, Vector3d centerGPS, float radius, float speed, bool clockwise)
        {
            if (regainEnergy)
            {
                RegainEnergy(s, vessel.Velocity());
                return;
            }
            finalMaxSteer = GetSteerLimiterForSpeedAndPower();

            debugString.AppendLine($"Flying orbit");
            Vector3 flightCenter = GetTerrainSurfacePosition(VectorUtils.GetWorldSurfacePostion(centerGPS, vessel.mainBody)) + (defaultAltitude * upDirection);

            Vector3 myVectorFromCenter = Vector3.ProjectOnPlane(vessel.transform.position - flightCenter, upDirection);
            Vector3 myVectorOnOrbit = myVectorFromCenter.normalized * radius;

            Vector3 targetVectorFromCenter = Quaternion.AngleAxis(clockwise ? 15f : -15f, upDirection) * myVectorOnOrbit;

            Vector3 verticalVelVector = Vector3.Project(vessel.Velocity(), upDirection); //for vv damping

            Vector3 targetPosition = flightCenter + targetVectorFromCenter - (verticalVelVector * 0.25f);

            Vector3 vectorToTarget = targetPosition - vesselTransform.position;
            //Vector3 planarVel = Vector3.ProjectOnPlane(vessel.Velocity(), upDirection);
            //vectorToTarget = Vector3.RotateTowards(planarVel, vectorToTarget, 25f * Mathf.Deg2Rad, 0);
            vectorToTarget = GetLimitedClimbDirectionForSpeed(vectorToTarget);
            targetPosition = vesselTransform.position + vectorToTarget;

            if (command != PilotCommands.Free && (vessel.transform.position - flightCenter).sqrMagnitude < radius * radius * 1.5f)
            {
                if (BDArmorySettings.DEBUG_AI) Debug.Log("[BDArmory.BDModulePilotAI]: AI Pilot reached command destination.");
                ReleaseCommand();
            }

            useVelRollTarget = true;

            AdjustThrottle(speed, false);
            FlyToPosition(s, targetPosition);
        }

        #region Waypoints
        Vector3 waypointRollTarget = default;
        float waypointRollTargetStrength = 0;
        bool useWaypointRollTarget = false;
        float waypointYawAuthorityStrength = 0;
        bool useWaypointYawAuthority = false;
        Ray waypointRay;
        RaycastHit waypointRayHit;
        bool waypointTerrainAvoidanceActive = false;
        Vector3 waypointTerrainSmoothedNormal = default;
        void FlyWaypoints(FlightCtrlState s)
        {
            // Note: UpdateWaypoint is called separately before this in case FlyWaypoints doesn't get called.
            if (BDArmorySettings.WAYPOINT_LOOP_INDEX > 1)
            {
                SetStatus($"Lap {activeWaypointLap}, Waypoint {activeWaypointIndex} ({waypointRange:F0}m)");
            }
            else
            {
                SetStatus($"Waypoint {activeWaypointIndex} ({waypointRange:F0}m)");
            }
            var waypointDirection = (waypointPosition - vessel.transform.position).normalized;
            // var waypointDirection = (WaypointSpline() - vessel.transform.position).normalized;
            waypointRay = new Ray(vessel.transform.position, waypointDirection);
            if (Physics.Raycast(waypointRay, out waypointRayHit, waypointRange, (int)LayerMasks.Scenery))
            {
                var angle = 90f + 90f * (1f - waypointTerrainAvoidance) * (waypointRayHit.distance - defaultAltitude) / (waypointRange + 1000f); // Parallel to the terrain at the default altitude (in the direction of the waypoint), adjusted for relative distance to the terrain and the waypoint. 1000 added to waypointRange to provide a stronger effect if the distance to the waypoint is small.
                waypointTerrainSmoothedNormal = waypointTerrainAvoidanceActive ? Vector3.Lerp(waypointTerrainSmoothedNormal, waypointRayHit.normal, 0.5f - 0.4862327f * waypointTerrainAvoidanceSmoothingFactor) : waypointRayHit.normal; // Smooth out varying terrain normals at a rate depending on the terrain avoidance strength (half-life of 1s at max avoidance, 0.29s at mid and 0.02s at min avoidance).
                waypointDirection = Vector3.RotateTowards(waypointTerrainSmoothedNormal, waypointDirection, angle * Mathf.Deg2Rad, 0f);
                waypointTerrainAvoidanceActive = true;
                if (BDArmorySettings.DEBUG_TELEMETRY) debugString.AppendLine($"Waypoint Terrain: {waypointRayHit.distance:F1}m @ {angle:F2}°");
            }
            else
            {
                if (waypointTerrainAvoidanceActive) // Reset stuff
                {
                    waypointTerrainAvoidanceActive = false;
                }
            }
            SetWaypointRollAndYaw();
            steerMode = SteerModes.NormalFlight; // Make sure we're using the correct steering mode.
            FlyToPosition(s, vessel.transform.position + waypointDirection * Mathf.Min(500f, waypointRange), false); // Target up to 500m ahead so that max altitude restrictions apply reasonably.
        }

        private Vector3 WaypointSpline() // FIXME This doesn't work that well yet.
        {
            // Note: here we're using distance instead of time as the waypoint parameter.
            float minDistance = (float)vessel.speed * 2f; // Consider the radius of 2s around the waypoint.

            Vector3 point1 = waypointPosition + (vessel.transform.position - waypointPosition).normalized * minDistance; //waypointsRange > minDistance ? vessel.transform.position : waypointPosition + (vessel.transform.position - waypointPosition).normalized * minDistance;
            Vector3 point2 = waypointPosition;
            Vector3 point3;
            if (activeWaypointIndex < waypoints.Count() - 1)
            {
                var nextWaypoint = waypoints[activeWaypointIndex + 1];
                var terrainAltitude = FlightGlobals.currentMainBody.TerrainAltitude(nextWaypoint.x, nextWaypoint.y);
                var nextWaypointPosition = FlightGlobals.currentMainBody.GetWorldSurfacePosition(nextWaypoint.x, nextWaypoint.y, nextWaypoint.z + terrainAltitude);
                point3 = waypointPosition + (nextWaypointPosition - waypointPosition).normalized * minDistance;
            }
            else
            {
                point3 = waypointPosition + (waypointPosition - vessel.transform.position).normalized * minDistance; // Straight out the other side.
            }
            var distance1 = (point2 - point1).magnitude;
            var distance2 = (point3 - point2).magnitude;
            Vector3 slope1 = SplineUtils.EstimateSlope(point1, point2, distance1);
            Vector3 slope2 = SplineUtils.EstimateSlope(point1, point2, point3, distance1, distance2);
            if (Mathf.Max(minDistance - waypointRange + (float)vessel.speed * 0.1f, 0f) < distance1)
            {
                return SplineUtils.EvaluateSpline(point1, slope1, point2, slope2, Mathf.Max(minDistance - waypointRange + (float)vessel.speed * 0.1f, 0f), 0f, distance1); // 0.1s ahead along the spline. 
            }
            else
            {
                var slope3 = SplineUtils.EstimateSlope(point2, point3, distance2);
                return SplineUtils.EvaluateSpline(point2, slope2, point3, slope3, Mathf.Max(minDistance - waypointRange + (float)vessel.speed * 0.1f - distance1, 0f), 0f, distance2); // 0.1s ahead along the next section of the spline.
            }
        }

        private void SetWaypointRollAndYaw()
        {
            if (waypointPreRollTime > 0)
            {
                var range = (float)vessel.speed * waypointPreRollTime; // Pre-roll ahead of the waypoint.
                if (waypointRange < range && activeWaypointIndex < waypoints.Count() - 1) // Within range of a waypoint and it's not the final one => use the waypoint roll target.
                {
                    var nextWaypoint = waypoints[activeWaypointIndex + 1];
                    var terrainAltitude = FlightGlobals.currentMainBody.TerrainAltitude(nextWaypoint.x, nextWaypoint.y);
                    var nextWaypointPosition = FlightGlobals.currentMainBody.GetWorldSurfacePosition(nextWaypoint.x, nextWaypoint.y, nextWaypoint.z + terrainAltitude);
                    waypointRollTarget = Vector3.ProjectOnPlane(nextWaypointPosition - waypointPosition, vessel.Velocity()).normalized;
                    waypointRollTargetStrength = Mathf.Min(1f, Vector3.Angle(nextWaypointPosition - waypointPosition, vessel.Velocity()) / maxAllowedAoA) * Mathf.Max(0, 1f - waypointRange / range); // Full strength at maxAllowedAoA and at the waypoint.
                    useWaypointRollTarget = true;
                }
            }
            if (waypointYawAuthorityTime > 0)
            {
                var range = (float)vessel.speed * waypointYawAuthorityTime;
                waypointYawAuthorityStrength = Mathf.Clamp01((2f * range - waypointRange) / range);
                useWaypointYawAuthority = true;
            }
        }

        protected override void UpdateWaypoint()
        {
            base.UpdateWaypoint();
            useWaypointRollTarget = false; // Reset this so that it's only set when actively flying waypoints.
            useWaypointYawAuthority = false; // Reset this so that it's only set when actively flying waypoints.
        }

        void SetWaypointTerrainAvoidance()
        {
            UI_FloatRange field = (UI_FloatRange)Fields["waypointTerrainAvoidance"].uiControlEditor;
            field.onFieldChanged = OnWaypointTerrainAvoidanceUpdated;
            field = (UI_FloatRange)Fields["waypointTerrainAvoidance"].uiControlFlight;
            field.onFieldChanged = OnWaypointTerrainAvoidanceUpdated;
            OnWaypointTerrainAvoidanceUpdated(null, null);
        }
        void OnWaypointTerrainAvoidanceUpdated(BaseField field, object obj)
        {
            waypointTerrainAvoidanceSmoothingFactor = Mathf.Pow(waypointTerrainAvoidance, 0.1f);
        }
        #endregion

        //sends target speed to speedController
        void AdjustThrottle(float targetSpeed, bool useBrakes, bool allowAfterburner = true, bool forceAfterburner = false, float throttleOverride = -1f)
        {
            speedController.targetSpeed = targetSpeed;
            speedController.useBrakes = useBrakes;
            speedController.allowAfterburner = allowAfterburner;
            speedController.forceAfterburner = forceAfterburner;
            speedController.throttleOverride = throttleOverride;
            speedController.afterburnerPriority = ABPriority;
        }

        Vector3 threatRelativePosition;

        void Evasive(FlightCtrlState s)
        {
            if (s == null) return;
            if (vessel == null) return;
            if (weaponManager == null) return;

            SetStatus("Evading");
            debugString.AppendLine($"Evasive {evasiveTimer}s");
            debugString.AppendLine($"Threat Distance: {weaponManager.incomingMissileDistance}");
            evading = true;
            steerMode = SteerModes.NormalFlight;
            if (!wasEvading) evasionNonlinearityDirection = Mathf.Sign(UnityEngine.Random.Range(-1f, 1f));

            bool hasABEngines = (speedController.multiModeEngines.Count > 0);

            collisionDetectionTicker += 2;

            if (weaponManager)
            {
                if (weaponManager.isFlaring)
                {
                    useAB = vessel.srfSpeed < minSpeed;
                    useBrakes = false;
                    float targetSpeed = minSpeed;
                    if (weaponManager.isChaffing)
                        targetSpeed = maxSpeed;
                    AdjustThrottle(targetSpeed, false, useAB);
                }

                if (weaponManager.incomingMissileVessel != null && (weaponManager.ThreatClosingTime(weaponManager.incomingMissileVessel) <= weaponManager.cmThreshold)) // Missile evasion
                {
                    if ((weaponManager.ThreatClosingTime(weaponManager.incomingMissileVessel) <= 1.5f) && (!weaponManager.isChaffing)) // Missile is about to impact, pull a hard turn
                    {
                        debugString.AppendLine($"Missile about to impact! pull away!");

                        AdjustThrottle(maxSpeed, false, !weaponManager.isFlaring);

                        Vector3 cross = Vector3.Cross(weaponManager.incomingMissileVessel.transform.position - vesselTransform.position, vessel.Velocity()).normalized;
                        if (Vector3.Dot(cross, -vesselTransform.forward) < 0)
                        {
                            cross = -cross;
                        }
                        FlyToPosition(s, vesselTransform.position + (50 * vessel.Velocity() / vessel.srfSpeed) + (100 * cross));
                        return;
                    }
                    else // Fly at 90 deg to missile to put max distance between ourselves and dispensed flares/chaff
                    {
                        debugString.AppendLine($"Breaking from missile threat!");

                        // Break off at 90 deg to missile
                        Vector3 threatDirection = -1f * weaponManager.incomingMissileVessel.Velocity();
                        threatDirection = Vector3.ProjectOnPlane(threatDirection, upDirection);
                        float sign = Vector3.SignedAngle(threatDirection, Vector3.ProjectOnPlane(vessel.Velocity(), upDirection), upDirection);
                        Vector3 breakDirection = Vector3.ProjectOnPlane(Vector3.Cross(Mathf.Sign(sign) * upDirection, threatDirection), upDirection);

                        // Dive to gain energy and hopefully lead missile into ground
                        float angle = (Mathf.Clamp((float)vessel.radarAltitude - minAltitude, 0, 1500) / 1500) * 90;
                        angle = Mathf.Clamp(angle, 0, 75) * Mathf.Deg2Rad;
                        Vector3 targetDirection = Vector3.RotateTowards(breakDirection, -upDirection, angle, 0);
                        targetDirection = Vector3.RotateTowards(vessel.Velocity(), targetDirection, 15f * Mathf.Deg2Rad, 0).normalized;

                        steerMode = SteerModes.Aiming;

                        if (weaponManager.isFlaring)
                            if (!hasABEngines)
                                AdjustThrottle(maxSpeed, false, useAB, false, 0.66f);
                            else
                                AdjustThrottle(maxSpeed, false, useAB);
                        else
                        {
                            useAB = true;
                            AdjustThrottle(maxSpeed, false, useAB);
                        }

                        FlyToPosition(s, vesselTransform.position + (targetDirection * 100), true);
                        return;
                    }
                }
                else if (weaponManager.underFire)
                {
                    debugString.Append($"Dodging gunfire");
                    float threatDirectionFactor = Vector3.Dot(vesselTransform.up, threatRelativePosition.normalized);
                    //Vector3 axis = -Vector3.Cross(vesselTransform.up, threatRelativePosition);
                    // FIXME When evading while in waypoint following mode, the breakTarget ought to be roughly in the direction of the waypoint.

                    Vector3 breakTarget = threatRelativePosition * 2f;       //for the most part, we want to turn _towards_ the threat in order to increase the rel ang vel and get under its guns

                    if (weaponManager.incomingThreatVessel != null && weaponManager.incomingThreatVessel.LandedOrSplashed) // Surface threat.
                    {
                        // Break horizontally away at maxAoA initially, then directly away once past 90°.
                        breakTarget = Vector3.RotateTowards(vessel.srf_vel_direction, -threatRelativePosition, maxAllowedAoA * Mathf.Deg2Rad, 0);
                        if (threatDirectionFactor > 0)
                            breakTarget = Vector3.ProjectOnPlane(breakTarget, upDirection);
                        breakTarget = breakTarget.normalized * 100f;
                        var breakTargetAlt = BodyUtils.GetRadarAltitudeAtPos(vessel.transform.position + breakTarget);
                        if (breakTargetAlt > defaultAltitude) breakTarget -= (breakTargetAlt - defaultAltitude) * upDirection;
                        debugString.AppendLine($" from ground target.");
                    }
                    else // Airborne threat.
                    {
                        if (threatDirectionFactor > 0.9f)     //within 28 degrees in front
                        { // This adds +-500/(threat distance) to the left or right relative to the breakTarget vector, regardless of the size of breakTarget
                            breakTarget += 500f / threatRelativePosition.magnitude * Vector3.Cross(threatRelativePosition.normalized, Mathf.Sign(Mathf.Sin((float)vessel.missionTime / 2)) * vessel.upAxis);
                            debugString.AppendLine($" from directly ahead!");
                        }
                        else if (threatDirectionFactor < -0.9) //within ~28 degrees behind
                        {
                            float threatDistanceSqr = threatRelativePosition.sqrMagnitude;
                            if (threatDistanceSqr > 400 * 400)
                            { // This sets breakTarget 1500m ahead and 500m down, then adds a 1000m offset at 90° to ahead based on missionTime. If the target is kinda close, brakes are also applied.
                                breakTarget = vesselTransform.up * 1500 - 500 * vessel.upAxis;
                                breakTarget += Mathf.Sin((float)vessel.missionTime / 2) * vesselTransform.right * 1000 - Mathf.Cos((float)vessel.missionTime / 2) * vesselTransform.forward * 1000;
                                if (threatDistanceSqr > 800 * 800)
                                    debugString.AppendLine($" from behind afar; engaging barrel roll");
                                else
                                {
                                    debugString.AppendLine($" from behind moderate distance; engaging aggressvie barrel roll and braking");
                                    steerMode = SteerModes.Aiming;
                                    AdjustThrottle(minSpeed, true, false);
                                }
                            }
                            else
                            { // This sets breakTarget to the attackers position, then applies an up to 500m offset to the right or left (relative to the vessel) for the first half of the default evading period, then sets the breakTarget to be 150m right or left of the attacker.
                                breakTarget = threatRelativePosition;
                                if (evasiveTimer < 1.5f)
                                    breakTarget += Mathf.Sin((float)vessel.missionTime * 2) * vesselTransform.right * 500;
                                else
                                    breakTarget += -Math.Sign(Mathf.Sin((float)vessel.missionTime * 2)) * vesselTransform.right * 150;

                                debugString.AppendLine($" from directly behind and close; breaking hard");
                                steerMode = SteerModes.Aiming;
                                AdjustThrottle(minSpeed, true, false); // Brake to slow down and turn faster while breaking target
                            }
                        }
                        else
                        {
                            float threatDistanceSqr = threatRelativePosition.sqrMagnitude;
                            if (threatDistanceSqr < 400 * 400) // Within 400m to the side.
                            { // This sets breakTarget to be behind the attacker (relative to the evader) with a small offset to the left or right.
                                breakTarget += Mathf.Sin((float)vessel.missionTime * 2) * vesselTransform.right * 100;

                                steerMode = SteerModes.Aiming;
                                debugString.AppendLine($" from near side; turning towards attacker");
                            }
                            else // More than 400m to the side.
                            { // This sets breakTarget to be 1500m ahead, then adds a 1000m offset at 90° to ahead.
                                breakTarget = vesselTransform.up * 1500;
                                breakTarget += Mathf.Sin((float)vessel.missionTime / 2) * vesselTransform.right * 1000 - Mathf.Cos((float)vessel.missionTime / 2) * vesselTransform.forward * 1000;
                                debugString.AppendLine($" from far side; engaging barrel roll");
                            }
                        }

                        float threatAltitudeDiff = Vector3.Dot(threatRelativePosition, vessel.upAxis);
                        if (threatAltitudeDiff > 500)
                            breakTarget += threatAltitudeDiff * vessel.upAxis;      //if it's trying to spike us from below, don't go crazy trying to dive below it
                        else
                            breakTarget += -150 * vessel.upAxis;   //dive a bit to escape

                        float breakTargetVerticalComponent = Vector3.Dot(breakTarget, upDirection);
                        if (belowMinAltitude && breakTargetVerticalComponent < 0) // If we're below minimum altitude, enforce the evade direction to gain altitude.
                        {
                            breakTarget += -2f * breakTargetVerticalComponent * upDirection;
                        }
                    }

                    breakTarget = GetLimitedClimbDirectionForSpeed(breakTarget);
                    breakTarget += vessel.transform.position;
                    FlyToPosition(s, FlightPosition(breakTarget, minAltitude));
                    return;
                }
            }

            Vector3 target = (vessel.srfSpeed < 200) ? FlightPosition(vessel.transform.position, minAltitude) : vesselTransform.position;
            float angleOff = Mathf.Sin(Time.time * 0.75f) * 180;
            angleOff = Mathf.Clamp(angleOff, -45, 45);
            target += (Quaternion.AngleAxis(angleOff, upDirection) * Vector3.ProjectOnPlane(vesselTransform.up * 500, upDirection));
            //+ (Mathf.Sin (Time.time/3) * upDirection * minAltitude/3);
            debugString.AppendLine($"Evading unknown attacker");
            FlyToPosition(s, target);
        }

        void UpdateVelocityRelativeDirections() // Vectors that are used in TakeOff and FlyAvoidTerrain.
        {
            relativeVelocityRightDirection = Vector3.Cross(upDirection, vessel.srf_vel_direction).normalized;
            relativeVelocityDownDirection = Vector3.Cross(relativeVelocityRightDirection, vessel.srf_vel_direction).normalized;
        }

        void CheckLandingGear()
        {
            if (!vessel.LandedOrSplashed)
            {
                if (vessel.radarAltitude > Mathf.Min(50f, minAltitude / 2f))
                    vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, false);
                else
                    vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, true);
            }
        }

        void TakeOff(FlightCtrlState s)
        {
            debugString.AppendLine($"Taking off/Gaining altitude");

            if (vessel.LandedOrSplashed && vessel.srfSpeed < takeOffSpeed)
            {
                SetStatus(initialTakeOff ? "Taking off" : vessel.Splashed ? "Splashed" : "Landed");
                if (vessel.Splashed)
                { vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, false); }
                assignedPositionWorld = vessel.transform.position;
                return;
            }
            SetStatus("Gain Alt. (" + (int)minAltitude + "m)");

            steerMode = initialTakeOff ? SteerModes.Aiming : SteerModes.NormalFlight;

            float radarAlt = (float)vessel.radarAltitude;

            if (initialTakeOff && radarAlt > terrainAlertDetectionRadius)
                initialTakeOff = false;

            // Get surface normal relative to our velocity direction below the vessel and where the vessel is heading.
            RaycastHit rayHit;
            Vector3 forwardDirection = (vessel.horizontalSrfSpeed < 10 ? vesselTransform.up : (Vector3)vessel.srf_vel_direction) * 100; // Forward direction not adjusted for terrain.
            Vector3 forwardPoint = vessel.transform.position + forwardDirection * 100; // Forward point not adjusted for terrain.
            Ray ray = new Ray(forwardPoint, relativeVelocityDownDirection); // Check ahead and below.
            Vector3 terrainBelowAheadNormal = (Physics.Raycast(ray, out rayHit, minAltitude + 1.0f, (int)LayerMasks.Scenery)) ? rayHit.normal : upDirection; // Terrain normal below point ahead.
            ray = new Ray(vessel.transform.position, relativeVelocityDownDirection); // Check here below.
            Vector3 terrainBelowNormal = (Physics.Raycast(ray, out rayHit, minAltitude + 1.0f, (int)LayerMasks.Scenery)) ? rayHit.normal : upDirection; // Terrain normal below here.
            Vector3 normalToUse = Vector3.Dot(vessel.srf_vel_direction, terrainBelowNormal) < Vector3.Dot(vessel.srf_vel_direction, terrainBelowAheadNormal) ? terrainBelowNormal : terrainBelowAheadNormal; // Use the normal that has the steepest slope relative to our velocity.
            forwardPoint = vessel.transform.position + Vector3.ProjectOnPlane(forwardDirection, normalToUse).normalized * 100; // Forward point adjusted for terrain.
            float rise = Mathf.Clamp((float)vessel.srfSpeed * 0.215f, 5, 100); // Up to 45° rise angle above terrain changes at 465m/s.
            FlyToPosition(s, forwardPoint + upDirection * rise);
        }

        void UpdateTerrainAlertDetectionRadius(Vessel v)
        {
            if (v == vessel)
            {
                terrainAlertDetectionRadius = 2f * vessel.GetRadius();
            }
        }

        bool FlyAvoidTerrain(FlightCtrlState s) // Check for terrain ahead.
        {
            if (initialTakeOff) return false; // Don't do anything during the initial take-off.
            bool initialCorrection = !avoidingTerrain;
            float controlLagTime = 1.5f; // Time to fully adjust control surfaces. (Typical values seem to be 0.286s -- 1s for neutral to deployed according to wing lift comparison.) FIXME maybe this could also be a slider.

            ++terrainAlertTicker;
            int terrainAlertTickerThreshold = BDArmorySettings.TERRAIN_ALERT_FREQUENCY * (int)(1 + Mathf.Pow((float)vessel.radarAltitude / 500.0f, 2.0f) / Mathf.Max(1.0f, (float)vessel.srfSpeed / 150.0f)); // Scale with altitude^2 / speed.
            if (terrainAlertTicker >= terrainAlertTickerThreshold)
            {
                terrainAlertTicker = 0;

                // Reset/initialise some variables.
                avoidingTerrain = false; // Reset the alert.
                if (vessel.radarAltitude > minAltitude)
                    belowMinAltitude = false; // Also, reset the belowMinAltitude alert if it's active because of avoiding terrain.
                terrainAlertDistance = -1.0f; // Reset the terrain alert distance.
                float turnRadiusTwiddleFactor = turnRadiusTwiddleFactorMax; // A twiddle factor based on the orientation of the vessel, since it often takes considerable time to re-orient before avoiding the terrain. Start with the worst value.
                terrainAlertThreatRange = turnRadiusTwiddleFactor * turnRadius + (float)vessel.srfSpeed * controlLagTime; // The distance to the terrain to consider.

                // First, look 45° down, up, left and right from our velocity direction for immediate danger. (This should cover most immediate dangers.)
                Ray rayForwardUp = new Ray(vessel.transform.position, (vessel.srf_vel_direction - relativeVelocityDownDirection).normalized);
                Ray rayForwardDown = new Ray(vessel.transform.position, (vessel.srf_vel_direction + relativeVelocityDownDirection).normalized);
                Ray rayForwardLeft = new Ray(vessel.transform.position, (vessel.srf_vel_direction - relativeVelocityRightDirection).normalized);
                Ray rayForwardRight = new Ray(vessel.transform.position, (vessel.srf_vel_direction + relativeVelocityRightDirection).normalized);
                RaycastHit rayHit;
                if (Physics.Raycast(rayForwardDown, out rayHit, 1.5f * terrainAlertDetectionRadius, (int)LayerMasks.Scenery)) // sqrt(2) should be sufficient, so 1.5 will cover it.
                {
                    terrainAlertDistance = rayHit.distance * -Vector3.Dot(rayHit.normal, vessel.srf_vel_direction);
                    terrainAlertNormal = rayHit.normal;
                }
                if (Physics.Raycast(rayForwardUp, out rayHit, 1.5f * terrainAlertDetectionRadius, (int)LayerMasks.Scenery) && (terrainAlertDistance < 0.0f || rayHit.distance < terrainAlertDistance))
                {
                    terrainAlertDistance = rayHit.distance * -Vector3.Dot(rayHit.normal, vessel.srf_vel_direction);
                    terrainAlertNormal = rayHit.normal;
                }
                if (Physics.Raycast(rayForwardLeft, out rayHit, 1.5f * terrainAlertDetectionRadius, (int)LayerMasks.Scenery) && (terrainAlertDistance < 0.0f || rayHit.distance < terrainAlertDistance))
                {
                    terrainAlertDistance = rayHit.distance * -Vector3.Dot(rayHit.normal, vessel.srf_vel_direction);
                    terrainAlertNormal = rayHit.normal;
                }
                if (Physics.Raycast(rayForwardRight, out rayHit, 1.5f * terrainAlertDetectionRadius, (int)LayerMasks.Scenery) && (terrainAlertDistance < 0.0f || rayHit.distance < terrainAlertDistance))
                {
                    terrainAlertDistance = rayHit.distance * -Vector3.Dot(rayHit.normal, vessel.srf_vel_direction);
                    terrainAlertNormal = rayHit.normal;
                }
                if (terrainAlertDistance > 0)
                {
                    terrainAlertDirection = Vector3.ProjectOnPlane(vessel.srf_vel_direction, terrainAlertNormal).normalized;
                    avoidingTerrain = true;
                }
                else
                {
                    // Next, cast a sphere forwards to check for upcoming dangers.
                    Ray ray = new Ray(vessel.transform.position, vessel.srf_vel_direction);
                    if (Physics.SphereCast(ray, terrainAlertDetectionRadius, out rayHit, terrainAlertThreatRange, (int)LayerMasks.Scenery)) // Found something. 
                    {
                        // Check if there's anything directly ahead.
                        ray = new Ray(vessel.transform.position, vessel.srf_vel_direction);
                        terrainAlertDistance = rayHit.distance * -Vector3.Dot(rayHit.normal, vessel.srf_vel_direction); // Distance to terrain along direction of terrain normal.
                        terrainAlertNormal = rayHit.normal;
                        if (BDArmorySettings.DEBUG_LINES)
                        {
                            terrainAlertDebugPos = rayHit.point;
                            terrainAlertDebugDir = rayHit.normal;
                        }
                        if (!Physics.Raycast(ray, out rayHit, terrainAlertThreatRange, (int)LayerMasks.Scenery)) // Nothing directly ahead, so we're just barely avoiding terrain.
                        {
                            // Change the terrain normal and direction as we want to just fly over it instead of banking away from it.
                            terrainAlertNormal = upDirection;
                            terrainAlertDirection = vessel.srf_vel_direction;
                        }
                        else
                        { terrainAlertDirection = Vector3.ProjectOnPlane(vessel.srf_vel_direction, terrainAlertNormal).normalized; }
                        float sinTheta = Math.Min(0.0f, Vector3.Dot(vessel.srf_vel_direction, terrainAlertNormal)); // sin(theta) (measured relative to the plane of the surface).
                        float oneMinusCosTheta = 1.0f - Mathf.Sqrt(Math.Max(0.0f, 1.0f - sinTheta * sinTheta));
                        turnRadiusTwiddleFactor = (turnRadiusTwiddleFactorMin + turnRadiusTwiddleFactorMax) / 2.0f - (turnRadiusTwiddleFactorMax - turnRadiusTwiddleFactorMin) / 2.0f * Vector3.Dot(terrainAlertNormal, -vessel.transform.forward); // This would depend on roll rate (i.e., how quickly the vessel can reorient itself to perform the terrain avoidance maneuver) and probably other things.
                        float controlLagCompensation = Mathf.Max(0f, -Vector3.Dot(AIUtils.PredictPosition(vessel, controlLagTime * turnRadiusTwiddleFactor) - vessel.transform.position, terrainAlertNormal)); // Include twiddle factor as more re-orienting requires more control surface movement.
                        terrainAlertThreshold = turnRadiusTwiddleFactor * turnRadius * oneMinusCosTheta + controlLagCompensation;
                        if (terrainAlertDistance < terrainAlertThreshold) // Only do something about it if the estimated turn amount is a problem.
                        {
                            avoidingTerrain = true;

                            // Shoot new ray in direction theta/2 (i.e., the point where we should be parallel to the surface) above velocity direction to check if the terrain slope is increasing.
                            float phi = -Mathf.Asin(sinTheta) / 2f;
                            Vector3 upcoming = Vector3.RotateTowards(vessel.srf_vel_direction, terrainAlertNormal, phi, 0f);
                            ray = new Ray(vessel.transform.position, upcoming);
                            if (BDArmorySettings.DEBUG_LINES)
                                terrainAlertDebugDraw2 = false;
                            if (Physics.Raycast(ray, out rayHit, terrainAlertThreatRange, (int)LayerMasks.Scenery))
                            {
                                if (rayHit.distance < terrainAlertDistance / Mathf.Sin(phi)) // Hit terrain closer than expected => terrain slope is increasing relative to our velocity direction.
                                {
                                    if (BDArmorySettings.DEBUG_LINES)
                                    {
                                        terrainAlertDebugDraw2 = true;
                                        terrainAlertDebugPos2 = rayHit.point;
                                        terrainAlertDebugDir2 = rayHit.normal;
                                    }
                                    terrainAlertNormal = rayHit.normal; // Use the normal of the steeper terrain (relative to our velocity).
                                    terrainAlertDirection = Vector3.ProjectOnPlane(vessel.srf_vel_direction, terrainAlertNormal).normalized;
                                }
                            }
                        }
                    }
                }
                // Finally, check the distance to sea-level as water doesn't act like a collider, so it's getting ignored. Also, for planets without surfaces.
                if (vessel.mainBody.ocean || !vessel.mainBody.hasSolidSurface)
                {
                    float sinTheta = Vector3.Dot(vessel.srf_vel_direction, upDirection); // sin(theta) (measured relative to the ocean surface).
                    if (sinTheta < 0f) // Heading downwards
                    {
                        float oneMinusCosTheta = 1.0f - Mathf.Sqrt(Math.Max(0.0f, 1.0f - sinTheta * sinTheta));
                        turnRadiusTwiddleFactor = (turnRadiusTwiddleFactorMin + turnRadiusTwiddleFactorMax) / 2.0f - (turnRadiusTwiddleFactorMax - turnRadiusTwiddleFactorMin) / 2.0f * Vector3.Dot(upDirection, -vessel.transform.forward); // This would depend on roll rate (i.e., how quickly the vessel can reorient itself to perform the terrain avoidance maneuver) and probably other things.
                        float controlLagCompensation = Mathf.Max(0f, -Vector3.Dot(AIUtils.PredictPosition(vessel, controlLagTime * turnRadiusTwiddleFactor) - vessel.transform.position, upDirection)); // Include twiddle factor as more re-orienting requires more control surface movement.
                        terrainAlertThreshold = turnRadiusTwiddleFactor * turnRadius * oneMinusCosTheta + controlLagCompensation;

                        if ((float)vessel.altitude < terrainAlertThreshold && (terrainAlertDistance < 0 || (float)vessel.altitude < terrainAlertDistance)) // If the ocean surface is closer than the terrain (if any), then override the terrain alert values.
                        {
                            terrainAlertDistance = (float)vessel.altitude;
                            terrainAlertNormal = upDirection;
                            terrainAlertDirection = Vector3.ProjectOnPlane(vessel.srf_vel_direction, upDirection).normalized;
                            avoidingTerrain = true;

                            if (BDArmorySettings.DEBUG_LINES)
                            {
                                terrainAlertDebugPos = vessel.transform.position + vessel.srf_vel_direction * (float)vessel.altitude / -sinTheta;
                                terrainAlertDebugDir = upDirection;
                            }
                        }
                    }
                }
            }

            if (avoidingTerrain)
            {
                belowMinAltitude = true; // Inform other parts of the code to behave as if we're below minimum altitude.

                float maxAngle = 70.0f * Mathf.Deg2Rad; // Maximum angle (towards surface normal) to aim.
                float adjustmentFactor = 1f; // Mathf.Clamp(1.0f - Mathf.Pow(terrainAlertDistance / terrainAlertThreatRange, 2.0f), 0.0f, 1.0f); // Don't yank too hard as it kills our speed too much. (This doesn't seem necessary.)
                                             // First, aim up to maxAngle towards the surface normal.
                if (BDArmorySettings.SPACE_HACKS) //no need to worry about stalling in null atmo
                {
                    FlyToPosition(s, vessel.transform.position + terrainAlertNormal * 100); //so point nose perpendicular to surface for maximum vertical thrust.
                }
                else
                {
                    Vector3 correctionDirection = Vector3.RotateTowards(terrainAlertDirection, terrainAlertNormal, maxAngle * adjustmentFactor, 0.0f);
                    // Then, adjust the vertical pitch for our speed (to try to avoid stalling).
                    Vector3 horizontalCorrectionDirection = Vector3.ProjectOnPlane(correctionDirection, upDirection).normalized;
                    correctionDirection = Vector3.RotateTowards(correctionDirection, horizontalCorrectionDirection, Mathf.Max(0.0f, (1.0f - (float)vessel.srfSpeed / 120.0f) * 0.8f * maxAngle) * adjustmentFactor, 0.0f); // Rotate up to 0.8*maxAngle back towards horizontal depending on speed < 120m/s.
                    float alpha = Time.fixedDeltaTime * 2f; // 0.04 seems OK.
                    float beta = Mathf.Pow(1.0f - alpha, terrainAlertTickerThreshold);
                    terrainAlertCorrectionDirection = initialCorrection ? correctionDirection : (beta * terrainAlertCorrectionDirection + (1.0f - beta) * correctionDirection).normalized; // Update our target direction over several frames (if it's not the initial correction) due to changing terrain. (Expansion of N iterations of A = A*(1-a) + B*a. Not exact due to normalisation in the loop, but good enough.)
                    FlyToPosition(s, vessel.transform.position + terrainAlertCorrectionDirection * 100);
                }
                // Update status and book keeping.
                SetStatus("Terrain (" + (int)terrainAlertDistance + "m)");
                terrainAlertCoolDown = 0.5f; // 0.5s cool down after avoiding terrain.
                return true;
            }

            // Hurray, we've avoided the terrain!
            avoidingTerrain = false;
            return false;
        }

        bool FlyAvoidOthers(FlightCtrlState s) // Check for collisions with other vessels and try to avoid them.
        {
            if (vesselCollisionAvoidanceStrength == 0 || collisionAvoidanceThreshold == 0) return false;
            if (currentlyAvoidedVessel != null) // Avoidance has been triggered.
            {
                SetStatus("AvoidCollision");
                debugString.AppendLine($"Avoiding Collision");

                // Monitor collision avoidance, adjusting or stopping as necessary.
                if (currentlyAvoidedVessel != null && PredictCollisionWithVessel(currentlyAvoidedVessel, vesselCollisionAvoidanceLookAheadPeriod * 1.2f, out collisionAvoidDirection)) // *1.2f for hysteresis.
                {
                    FlyAvoidVessel(s);
                    return true;
                }
                else // Stop avoiding, but immediately check again for new collisions.
                {
                    currentlyAvoidedVessel = null;
                    collisionDetectionTicker = vesselCollisionAvoidanceTickerFreq + 1;
                    return FlyAvoidOthers(s);
                }
            }
            else if (collisionDetectionTicker > vesselCollisionAvoidanceTickerFreq) // Only check every vesselCollisionAvoidanceTickerFreq frames.
            {
                collisionDetectionTicker = 0;

                // Check for collisions with other vessels.
                bool vesselCollision = false;
                VesselType collisionVesselType = VesselType.Plane;
                collisionAvoidDirection = vessel.srf_vel_direction;
                using (var vs = BDATargetManager.LoadedVessels.GetEnumerator()) // Note: we can't ignore some vessel types here as we also need to avoid debris, etc.
                    while (vs.MoveNext())
                    {
                        if (vs.Current == null) continue;
                        if (vs.Current == vessel || vs.Current.Landed) continue;
                        if (!PredictCollisionWithVessel(vs.Current, vesselCollisionAvoidanceLookAheadPeriod, out collisionAvoidDirection)) continue;
                        if (!VesselModuleRegistry.ignoredVesselTypes.Contains(vs.Current.vesselType))
                        {
                            var ibdaiControl = VesselModuleRegistry.GetModule<IBDAIControl>(vs.Current);
                            if (ibdaiControl != null && ibdaiControl.commandLeader != null && ibdaiControl.commandLeader.vessel == vessel) continue;
                        }
                        vesselCollision = true;
                        collisionVesselType = vs.Current.vesselType;
                        currentlyAvoidedVessel = vs.Current;
                        break; // Early exit on first detected vessel collision. Chances of multiple vessel collisions are low.
                    }
                if (vesselCollision)
                {
                    FlyAvoidVessel(s);
                    return true;
                }
                else
                { currentlyAvoidedVessel = null; }
            }
            else
            { ++collisionDetectionTicker; }
            return false;
        }

        void FlyAvoidVessel(FlightCtrlState s)
        {
            // Rotate the current flyingToPosition away from the direction to avoid.
            Vector3 axis = Vector3.Cross(vessel.srf_vel_direction, collisionAvoidDirection);
            FlyToPosition(s, vesselTransform.position + Quaternion.AngleAxis(-vesselCollisionAvoidanceStrength, axis) * (flyingToPosition - vesselTransform.position)); // Rotate the flyingToPosition around the axis by the collision avoidance strength (each frame).
        }

        Vector3 GetLimitedClimbDirectionForSpeed(Vector3 direction)
        {
            if (Vector3.Dot(direction, upDirection) < 0)
            {
                debugString.AppendLine($"climb limit angle: unlimited");
                return direction; //only use this if climbing
            }

            Vector3 planarDirection = Vector3.ProjectOnPlane(direction, upDirection).normalized * 100;

            float angle = Mathf.Clamp((float)vessel.srfSpeed * 0.13f, 5, 90);

            debugString.AppendLine($"climb limit angle: {angle}");
            return Vector3.RotateTowards(planarDirection, direction, angle * Mathf.Deg2Rad, 0);
        }

        void UpdateGAndAoALimits(FlightCtrlState s)
        {
            if (vessel.dynamicPressurekPa <= 0 || vessel.srfSpeed < takeOffSpeed || belowMinAltitude && -Vector3.Dot(vessel.ReferenceTransform.forward, vessel.upAxis) < 0.8f)
            {
                return;
            }

            if (lastAllowedAoA != maxAllowedAoA)
            {
                lastAllowedAoA = maxAllowedAoA;
                maxAllowedCosAoA = (float)Math.Cos(lastAllowedAoA * Math.PI / 180.0);
            }
            float pitchG = -Vector3.Dot(vessel.acceleration, vessel.ReferenceTransform.forward);       //should provide g force in vessel up / down direction, assuming a standard plane
            float pitchGPerDynPres = pitchG / (float)vessel.dynamicPressurekPa;

            float curCosAoA = Vector3.Dot(vessel.Velocity().normalized, vessel.ReferenceTransform.forward);

            //adjust moving averages
            //adjust gLoad average
            gLoadMovingAvg *= 32f;
            gLoadMovingAvg -= gLoadMovingAvgArray[movingAvgIndex];
            gLoadMovingAvgArray[movingAvgIndex] = pitchGPerDynPres;
            gLoadMovingAvg += pitchGPerDynPres;
            gLoadMovingAvg /= 32f;

            //adjusting cosAoAAvg
            cosAoAMovingAvg *= 32f;
            cosAoAMovingAvg -= cosAoAMovingAvgArray[movingAvgIndex];
            cosAoAMovingAvgArray[movingAvgIndex] = curCosAoA;
            cosAoAMovingAvg += curCosAoA;
            cosAoAMovingAvg /= 32f;

            ++movingAvgIndex;
            if (movingAvgIndex == gLoadMovingAvgArray.Length)
                movingAvgIndex = 0;

            if (gLoadMovingAvg < maxNegG || Math.Abs(cosAoAMovingAvg - cosAoAAtMaxNegG) < 0.005f)
            {
                maxNegG = gLoadMovingAvg;
                cosAoAAtMaxNegG = cosAoAMovingAvg;
            }
            if (gLoadMovingAvg > maxPosG || Math.Abs(cosAoAMovingAvg - cosAoAAtMaxPosG) < 0.005f)
            {
                maxPosG = gLoadMovingAvg;
                cosAoAAtMaxPosG = cosAoAMovingAvg;
            }

            if (cosAoAAtMaxNegG >= cosAoAAtMaxPosG)
            {
                cosAoAAtMaxNegG = cosAoAAtMaxPosG = maxNegG = maxPosG = 0;
                gOffsetPerDynPres = gaoASlopePerDynPres = 0;
                return;
            }

            // if (maxPosG > maxDynPresGRecorded)
            //     maxDynPresGRecorded = maxPosG;

            if (command != PilotCommands.Waypoints) // Don't decay the highest recorded G-force when following waypoints as we're likely to be heading in straight lines for longer periods.
                dynDynPresGRecorded *= dynDecayRate; // Decay the highest observed G-force from dynamic pressure (we want a fairly recent value in case the planes dynamics have changed).
            if (!vessel.LandedOrSplashed && Math.Abs(gLoadMovingAvg) > dynDynPresGRecorded)
                dynDynPresGRecorded = Math.Abs(gLoadMovingAvg);

            if (!vessel.LandedOrSplashed)
            {
                dynVelocityMagSqr = dynVelocityMagSqr * dynVelSmoothingCoef + (1f - dynVelSmoothingCoef) * (float)vessel.Velocity().sqrMagnitude; // Smooth the recently measured speed for determining the turn radius.
            }

            float aoADiff = cosAoAAtMaxPosG - cosAoAAtMaxNegG;

            //if (Math.Abs(pitchControlDiff) < 0.005f)
            //    return;                 //if the pitch control values are too similar, don't bother to avoid numerical errors

            gaoASlopePerDynPres = (maxPosG - maxNegG) / aoADiff;
            gOffsetPerDynPres = maxPosG - gaoASlopePerDynPres * cosAoAAtMaxPosG;     //g force offset
        }

        void AdjustPitchForGAndAoALimits(FlightCtrlState s)
        {
            float minCosAoA, maxCosAoA;
            //debugString += "\nMax Pos G: " + maxPosG + " @ " + cosAoAAtMaxPosG;
            //debugString += "\nMax Neg G: " + maxNegG + " @ " + cosAoAAtMaxNegG;

            if (vessel.LandedOrSplashed || vessel.srfSpeed < Math.Min(minSpeed, takeOffSpeed))         //if we're going too slow, don't use this
            {
                float speed = Math.Max(takeOffSpeed, minSpeed);
                negPitchDynPresLimitIntegrator = -1f * 0.001f * 0.5f * 1.225f * speed * speed;
                posPitchDynPresLimitIntegrator = 1f * 0.001f * 0.5f * 1.225f * speed * speed;
                return;
            }

            float invVesselDynPreskPa = 1f / (float)vessel.dynamicPressurekPa;

            maxCosAoA = maxAllowedGForce * bodyGravity * invVesselDynPreskPa;
            minCosAoA = -maxCosAoA;

            maxCosAoA -= gOffsetPerDynPres;
            minCosAoA -= gOffsetPerDynPres;

            maxCosAoA /= gaoASlopePerDynPres;
            minCosAoA /= gaoASlopePerDynPres;

            if (maxCosAoA > maxAllowedCosAoA)
                maxCosAoA = maxAllowedCosAoA;

            if (minCosAoA < -maxAllowedCosAoA)
                minCosAoA = -maxAllowedCosAoA;

            float curCosAoA = Vector3.Dot(vessel.Velocity() / vessel.srfSpeed, vessel.ReferenceTransform.forward);

            float centerCosAoA = (minCosAoA + maxCosAoA) * 0.5f;
            float curCosAoACentered = curCosAoA - centerCosAoA;
            float cosAoADiff = 0.5f * Math.Abs(maxCosAoA - minCosAoA);
            float curCosAoANorm = curCosAoACentered / cosAoADiff;      //scaled so that from centerAoA to maxAoA is 1

            float negPitchScalar, posPitchScalar;
            negPitchScalar = negPitchDynPresLimitIntegrator * invVesselDynPreskPa - lastPitchInput;
            posPitchScalar = lastPitchInput - posPitchDynPresLimitIntegrator * invVesselDynPreskPa;

            //update pitch control limits as needed
            float negPitchDynPresLimit, posPitchDynPresLimit;
            negPitchDynPresLimit = posPitchDynPresLimit = 0;
            if (curCosAoANorm < -0.15f)// || Math.Abs(negPitchScalar) < 0.01f)
            {
                float cosAoAOffset = curCosAoANorm + 1;     //set max neg aoa to be 0
                float aoALimScalar = Math.Abs(curCosAoANorm);
                aoALimScalar *= aoALimScalar;
                aoALimScalar *= aoALimScalar;
                aoALimScalar *= aoALimScalar;
                if (aoALimScalar > 1)
                    aoALimScalar = 1;

                float pitchInputScalar = negPitchScalar;
                pitchInputScalar = 1 - Mathf.Clamp01(Math.Abs(pitchInputScalar));
                pitchInputScalar *= pitchInputScalar;
                pitchInputScalar *= pitchInputScalar;
                pitchInputScalar *= pitchInputScalar;
                if (pitchInputScalar < 0)
                    pitchInputScalar = 0;

                float deltaCosAoANorm = curCosAoA - lastCosAoA;
                deltaCosAoANorm /= cosAoADiff;

                debugString.AppendLine($"Updating Neg Gs");
                negPitchDynPresLimitIntegrator -= 0.01f * Mathf.Clamp01(aoALimScalar + pitchInputScalar) * cosAoAOffset * (float)vessel.dynamicPressurekPa;
                negPitchDynPresLimitIntegrator -= 0.005f * deltaCosAoANorm * (float)vessel.dynamicPressurekPa;
                if (cosAoAOffset < 0)
                    negPitchDynPresLimit = -0.3f * cosAoAOffset;
            }
            if (curCosAoANorm > 0.15f)// || Math.Abs(posPitchScalar) < 0.01f)
            {
                float cosAoAOffset = curCosAoANorm - 1;     //set max pos aoa to be 0
                float aoALimScalar = Math.Abs(curCosAoANorm);
                aoALimScalar *= aoALimScalar;
                aoALimScalar *= aoALimScalar;
                aoALimScalar *= aoALimScalar;
                if (aoALimScalar > 1)
                    aoALimScalar = 1;

                float pitchInputScalar = posPitchScalar;
                pitchInputScalar = 1 - Mathf.Clamp01(Math.Abs(pitchInputScalar));
                pitchInputScalar *= pitchInputScalar;
                pitchInputScalar *= pitchInputScalar;
                pitchInputScalar *= pitchInputScalar;
                if (pitchInputScalar < 0)
                    pitchInputScalar = 0;

                float deltaCosAoANorm = curCosAoA - lastCosAoA;
                deltaCosAoANorm /= cosAoADiff;

                debugString.AppendLine($"Updating Pos Gs");
                posPitchDynPresLimitIntegrator -= 0.01f * Mathf.Clamp01(aoALimScalar + pitchInputScalar) * cosAoAOffset * (float)vessel.dynamicPressurekPa;
                posPitchDynPresLimitIntegrator -= 0.005f * deltaCosAoANorm * (float)vessel.dynamicPressurekPa;
                if (cosAoAOffset > 0)
                    posPitchDynPresLimit = -0.3f * cosAoAOffset;
            }

            float currentG = -Vector3.Dot(vessel.acceleration, vessel.ReferenceTransform.forward);
            float negLim, posLim;
            negLim = negPitchDynPresLimitIntegrator * invVesselDynPreskPa + negPitchDynPresLimit;
            if (negLim > s.pitch)
            {
                if (currentG > -(maxAllowedGForce * 0.97f * bodyGravity))
                {
                    negPitchDynPresLimitIntegrator -= (float)(0.15 * vessel.dynamicPressurekPa);        //jsut an override in case things break

                    maxNegG = currentG * invVesselDynPreskPa;
                    cosAoAAtMaxNegG = curCosAoA;

                    negPitchDynPresLimit = 0;

                    //maxPosG = 0;
                    //cosAoAAtMaxPosG = 0;
                }

                s.pitch = negLim;
                debugString.AppendLine($"Limiting Neg Gs");
            }
            posLim = posPitchDynPresLimitIntegrator * invVesselDynPreskPa + posPitchDynPresLimit;
            if (posLim < s.pitch)
            {
                if (currentG < (maxAllowedGForce * 0.97f * bodyGravity))
                {
                    posPitchDynPresLimitIntegrator += (float)(0.15 * vessel.dynamicPressurekPa);        //jsut an override in case things break

                    maxPosG = currentG * invVesselDynPreskPa;
                    cosAoAAtMaxPosG = curCosAoA;

                    posPitchDynPresLimit = 0;

                    //maxNegG = 0;
                    //cosAoAAtMaxNegG = 0;
                }

                s.pitch = posLim;
                debugString.AppendLine($"Limiting Pos Gs");
            }

            lastPitchInput = s.pitch;
            lastCosAoA = curCosAoA;

            debugString.AppendLine(String.Format("Final Pitch: {0,7:F4}  (Limits: {1,7:F4} — {2,6:F4})", s.pitch, negLim, posLim));
        }

        void CalculateAccelerationAndTurningCircle()
        {
            maxLiftAcceleration = dynDynPresGRecorded * (float)vessel.dynamicPressurekPa; //maximum acceleration from lift that the vehicle can provide

            maxLiftAcceleration = Mathf.Clamp(maxLiftAcceleration, bodyGravity, maxAllowedGForce * bodyGravity); //limit it to whichever is smaller, what we can provide or what we can handle. Assume minimum of 1G to avoid extremely high turn radiuses.

            turnRadius = dynVelocityMagSqr / maxLiftAcceleration; //radius that we can turn in assuming constant velocity, assuming simple circular motion (this is a terrible assumption, the AI usually turns on afterboosters!)
        }

        Vector3 DefaultAltPosition()
        {
            return (vessel.transform.position + (-(float)vessel.altitude * upDirection) + (defaultAltitude * upDirection));
        }

        Vector3 GetSurfacePosition(Vector3 position)
        {
            return position - ((float)FlightGlobals.getAltitudeAtPos(position) * upDirection);
        }

        Vector3 GetTerrainSurfacePosition(Vector3 position)
        {
            return position - (MissileGuidance.GetRaycastRadarAltitude(position) * upDirection);
        }

        Vector3 FlightPosition(Vector3 targetPosition, float minAlt)
        {
            Vector3 forwardDirection = vesselTransform.up;
            Vector3 targetDirection = (targetPosition - vesselTransform.position).normalized;

            float vertFactor = 0;
            vertFactor += (((float)vessel.srfSpeed / minSpeed) - 2f) * 0.3f;          //speeds greater than 2x minSpeed encourage going upwards; below encourages downwards
            vertFactor += (((targetPosition - vesselTransform.position).magnitude / 1000f) - 1f) * 0.3f;    //distances greater than 1000m encourage going upwards; closer encourages going downwards
            vertFactor -= Mathf.Clamp01(Vector3.Dot(vesselTransform.position - targetPosition, upDirection) / 1600f - 1f) * 0.5f;       //being higher than 1600m above a target encourages going downwards
            if (targetVessel)
                vertFactor += Vector3.Dot(targetVessel.Velocity() / targetVessel.srfSpeed, (targetVessel.ReferenceTransform.position - vesselTransform.position).normalized) * 0.3f;   //the target moving away from us encourages upward motion, moving towards us encourages downward motion
            else
                vertFactor += 0.4f;
            vertFactor -= (weaponManager != null && weaponManager.underFire) ? 0.5f : 0;   //being under fire encourages going downwards as well, to gain energy

            float alt = (float)vessel.radarAltitude;

            if (vertFactor > 2)
                vertFactor = 2;
            if (vertFactor < -2)
                vertFactor = -2;

            vertFactor += 0.15f * Mathf.Sin((float)vessel.missionTime * 0.25f);     //some randomness in there

            Vector3 projectedDirection = Vector3.ProjectOnPlane(forwardDirection, upDirection);
            Vector3 projectedTargetDirection = Vector3.ProjectOnPlane(targetDirection, upDirection);
            if (Vector3.Dot(targetDirection, forwardDirection) < 0)
            {
                if (Vector3.Angle(targetDirection, forwardDirection) > 165f)
                {
                    targetPosition = vesselTransform.position + (Quaternion.AngleAxis(Mathf.Sign(Mathf.Sin((float)vessel.missionTime / 4)) * 45, upDirection) * (projectedDirection.normalized * 200));
                    targetDirection = (targetPosition - vesselTransform.position).normalized;
                }

                targetPosition = vesselTransform.position + Vector3.Cross(Vector3.Cross(forwardDirection, targetDirection), forwardDirection).normalized * 200;
            }
            else if (steerMode != SteerModes.Aiming)
            {
                float distance = (targetPosition - vesselTransform.position).magnitude;
                if (vertFactor < 0)
                    distance = Math.Min(distance, Math.Abs((alt - minAlt) / vertFactor));

                targetPosition += upDirection * Math.Min(distance, 1000) * vertFactor * Mathf.Clamp01(0.7f - Math.Abs(Vector3.Dot(projectedTargetDirection, projectedDirection)));
                if (maxAltitudeEnabled)
                {
                    var targetRadarAlt = BodyUtils.GetRadarAltitudeAtPos(targetPosition);
                    if (targetRadarAlt > maxAltitude)
                    {
                        targetPosition -= (targetRadarAlt - maxAltitude) * upDirection;
                    }
                }
            }

            if ((float)vessel.radarAltitude > minAlt * 1.1f)
            {
                return targetPosition;
            }

            float pointRadarAlt = MissileGuidance.GetRaycastRadarAltitude(targetPosition);
            if (pointRadarAlt < minAlt)
            {
                float adjustment = (minAlt - pointRadarAlt);
                debugString.AppendLine($"Target position is below minAlt. Adjusting by {adjustment}");
                return targetPosition + (adjustment * upDirection);
            }
            else
            {
                return targetPosition;
            }
        }

        private float SteerDamping(float angleToTarget, float defaultTargetPosition, int axis)
        { //adjusts steer damping relative to a vessel's angle to its target position
            if (!dynamicSteerDamping) // Check if enabled.
            {
                DynamicDampingLabel = "Dyn Damping Not Toggled";
                PitchLabel = "Dyn Damping Not Toggled";
                YawLabel = "Dyn Damping Not Toggled";
                RollLabel = "Dyn Damping Not Toggled";
                return steerDamping;
            }
            else if (angleToTarget >= 180 || angleToTarget < 0) // Check for valid angle to target.
            {
                if (!CustomDynamicAxisFields)
                    DynamicDampingLabel = "N/A";
                switch (axis)
                {
                    case 1:
                        PitchLabel = "N/A";
                        break;
                    case 2:
                        YawLabel = "N/A";
                        break;
                    case 3:
                        RollLabel = "N/A";
                        break;
                }
                return steerDamping;
            }

            if (CustomDynamicAxisFields)
            {
                switch (axis)
                {
                    case 1:
                        if (dynamicDampingPitch)
                        {
                            dynSteerDampingPitchValue = GetDampingFactor(angleToTarget, dynamicSteerDampingPitchFactor, DynamicDampingPitchMin, DynamicDampingPitchMax);
                            PitchLabel = dynSteerDampingPitchValue.ToString();
                            return dynSteerDampingPitchValue;
                        }
                        break;
                    case 2:
                        if (dynamicDampingYaw)
                        {
                            dynSteerDampingYawValue = GetDampingFactor(angleToTarget, dynamicSteerDampingYawFactor, DynamicDampingYawMin, DynamicDampingYawMax);
                            YawLabel = dynSteerDampingYawValue.ToString();
                            return dynSteerDampingYawValue;
                        }
                        break;
                    case 3:
                        if (dynamicDampingRoll)
                        {
                            dynSteerDampingRollValue = GetDampingFactor(angleToTarget, dynamicSteerDampingRollFactor, DynamicDampingRollMin, DynamicDampingRollMax);
                            RollLabel = dynSteerDampingRollValue.ToString();
                            return dynSteerDampingRollValue;
                        }
                        break;
                }
                // The specific axis wasn't enabled, use the global value
                dynSteerDampingValue = steerDamping;
                switch (axis)
                {
                    case 1:
                        PitchLabel = dynSteerDampingValue.ToString();
                        break;
                    case 2:
                        YawLabel = dynSteerDampingValue.ToString();
                        break;
                    case 3:
                        RollLabel = dynSteerDampingValue.ToString();
                        break;
                }
                return dynSteerDampingValue;
            }
            else //if custom axis groups is disabled
            {
                dynSteerDampingValue = GetDampingFactor(defaultTargetPosition, dynamicSteerDampingFactor, DynamicDampingMin, DynamicDampingMax);
                DynamicDampingLabel = dynSteerDampingValue.ToString();
                return dynSteerDampingValue;
            }
        }

        private float GetDampingFactor(float angleToTarget, float dynamicSteerDampingFactorAxis, float DynamicDampingMinAxis, float DynamicDampingMaxAxis)
        {
            return Mathf.Clamp(
                (float)(Math.Pow((180 - angleToTarget) / 175, dynamicSteerDampingFactorAxis) * (DynamicDampingMaxAxis - DynamicDampingMinAxis) + DynamicDampingMinAxis), // Make a 5° dead zone around being on target.
                Mathf.Min(DynamicDampingMinAxis, DynamicDampingMaxAxis),
                Mathf.Max(DynamicDampingMinAxis, DynamicDampingMaxAxis)
            );
        }

        public override bool IsValidFixedWeaponTarget(Vessel target)
        {
            if (!vessel) return false;
            // aircraft can aim at anything
            return true;
        }

        bool DetectCollision(Vector3 direction, out Vector3 badDirection)
        {
            badDirection = Vector3.zero;
            if ((float)vessel.radarAltitude < 20) return false;

            direction = direction.normalized;
            Ray ray = new Ray(vesselTransform.position + (50 * vesselTransform.up), direction);
            float distance = Mathf.Clamp((float)vessel.srfSpeed * 4f, 125f, 2500);
            RaycastHit hit;
            if (!Physics.SphereCast(ray, 10, out hit, distance, (int)LayerMasks.Scenery)) return false;
            Rigidbody otherRb = hit.collider.attachedRigidbody;
            if (otherRb)
            {
                if (!(Vector3.Dot(otherRb.velocity, vessel.Velocity()) < 0)) return false;
                badDirection = hit.point - ray.origin;
                return true;
            }
            badDirection = hit.point - ray.origin;
            return true;
        }

        void UpdateCommand(FlightCtrlState s)
        {
            if (command == PilotCommands.Follow && !commandLeader)
            {
                ReleaseCommand();
                return;
            }

            if (command == PilotCommands.Follow)
            {
                SetStatus("Follow");
                UpdateFollowCommand(s);
            }
            else if (command == PilotCommands.FlyTo)
            {
                if (autoTune) // Actually fly to the specified point.
                {
                    SetStatus("AutoTuning");
                    AdjustThrottle(pidAutoTuning.flyToSpeed, true);
                    FlyToPosition(s, assignedPositionWorld);
                }
                else // Orbit around the assigned point at the default altitude.
                {
                    SetStatus("Fly To");
                    FlyOrbit(s, assignedPositionGeo, 2500, idleSpeed, ClockwiseOrbit);
                }
            }
            else if (command == PilotCommands.Attack)
            {
                if (targetVessel != null && (BDArmorySettings.RUNWAY_PROJECT || (targetVessel.vesselTransform.position - vessel.vesselTransform.position).sqrMagnitude <= weaponManager.gunRange * weaponManager.gunRange)
                    && (targetVessel.vesselTransform.position - vessel.vesselTransform.position).sqrMagnitude <= weaponManager.guardRange * weaponManager.guardRange) // If the vessel has a target within visual range, let it fight!
                {
                    ReleaseCommand();
                    return;
                }
                else if (weaponManager.underAttack || weaponManager.underFire)
                {
                    ReleaseCommand();
                    return;
                }
                else
                {
                    SetStatus("Attack");
                    FlyOrbit(s, assignedPositionGeo, 2500, maxSpeed, ClockwiseOrbit);
                }
            }
        }

        void UpdateFollowCommand(FlightCtrlState s)
        {
            steerMode = SteerModes.NormalFlight;
            vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, false);

            commandSpeed = commandLeader.vessel.srfSpeed;
            commandHeading = commandLeader.vessel.Velocity().normalized;

            //formation position
            Vector3d commandPosition = GetFormationPosition();
            debugFollowPosition = commandPosition;

            float distanceToPos = Vector3.Distance(vesselTransform.position, commandPosition);

            float dotToPos = Vector3.Dot(vesselTransform.up, commandPosition - vesselTransform.position);
            Vector3 flyPos;
            useRollHint = false;

            float ctrlModeThresh = 1000;

            if (distanceToPos < ctrlModeThresh)
            {
                flyPos = commandPosition + (ctrlModeThresh * commandHeading);

                Vector3 vectorToFlyPos = flyPos - vessel.ReferenceTransform.position;
                Vector3 projectedPosOffset = Vector3.ProjectOnPlane(commandPosition - vessel.ReferenceTransform.position, commandHeading);
                float posOffsetMag = projectedPosOffset.magnitude;
                float adjustAngle = (Mathf.Clamp(posOffsetMag * 0.27f, 0, 25));
                Vector3 projVel = Vector3.Project(vessel.Velocity() - commandLeader.vessel.Velocity(), projectedPosOffset);
                adjustAngle -= Mathf.Clamp(Mathf.Sign(Vector3.Dot(projVel, projectedPosOffset)) * projVel.magnitude * 0.12f, -10, 10);

                adjustAngle *= Mathf.Deg2Rad;

                vectorToFlyPos = Vector3.RotateTowards(vectorToFlyPos, projectedPosOffset, adjustAngle, 0);

                flyPos = vessel.ReferenceTransform.position + vectorToFlyPos;

                if (distanceToPos < 400)
                {
                    steerMode = SteerModes.Aiming;
                }
                else
                {
                    steerMode = SteerModes.NormalFlight;
                }

                if (distanceToPos < 10)
                {
                    useRollHint = true;
                }
            }
            else
            {
                steerMode = SteerModes.NormalFlight;
                flyPos = commandPosition;
            }

            double finalMaxSpeed = commandSpeed;
            if (dotToPos > 0)
            {
                finalMaxSpeed += (distanceToPos / 8);
            }
            else
            {
                finalMaxSpeed -= (distanceToPos / 2);
            }

            AdjustThrottle((float)finalMaxSpeed, true);

            FlyToPosition(s, flyPos);
        }

        Vector3d GetFormationPosition()
        {
            Quaternion origVRot = velocityTransform.rotation;
            Vector3 origVLPos = velocityTransform.localPosition;

            velocityTransform.position = commandLeader.vessel.ReferenceTransform.position;
            if (commandLeader.vessel.Velocity() != Vector3d.zero)
            {
                velocityTransform.rotation = Quaternion.LookRotation(commandLeader.vessel.Velocity(), upDirection);
                velocityTransform.rotation = Quaternion.AngleAxis(90, velocityTransform.right) * velocityTransform.rotation;
            }
            else
            {
                velocityTransform.rotation = commandLeader.vessel.ReferenceTransform.rotation;
            }

            Vector3d pos = velocityTransform.TransformPoint(this.GetLocalFormationPosition(commandFollowIndex));// - lateralVelVector - verticalVelVector;

            velocityTransform.localPosition = origVLPos;
            velocityTransform.rotation = origVRot;

            return pos;
        }

        public override void CommandTakeOff()
        {
            base.CommandTakeOff();
            standbyMode = false;
        }

        public override void CommandFollowWaypoints()
        {
            if (standbyMode) CommandTakeOff();
            base.CommandFollowWaypoints();
        }

        protected override void OnGUI()
        {
            base.OnGUI();

            if (!pilotEnabled || !vessel.isActiveVessel) return;

            if (!BDArmorySettings.DEBUG_LINES) return;
            if (command == PilotCommands.Follow)
            {
                GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, debugFollowPosition, 2, Color.red);
            }

            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, debugPos, 5, Color.red);
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + vesselTransform.up * 1000, 3, Color.white);
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + -vesselTransform.forward * 100, 3, Color.yellow);
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + vessel.Velocity().normalized * 100, 3, Color.magenta);

            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + rollTarget, 2, Color.blue);
#if DEBUG
            if (IsEvading || IsExtending) GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position, vesselTransform.position + DEBUG_vector.normalized * 10, 5, Color.cyan);
#endif
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransform.position + (0.05f * vesselTransform.right), vesselTransform.position + (0.05f * vesselTransform.right) + angVelRollTarget, 2, Color.green);
            if (avoidingTerrain)
            {
                GUIUtils.DrawLineBetweenWorldPositions(vessel.transform.position, terrainAlertDebugPos, 2, Color.cyan);
                GUIUtils.DrawLineBetweenWorldPositions(terrainAlertDebugPos, terrainAlertDebugPos + (terrainAlertThreshold - terrainAlertDistance) * terrainAlertDebugDir, 2, Color.cyan);
                if (terrainAlertDebugDraw2)
                {
                    GUIUtils.DrawLineBetweenWorldPositions(vessel.transform.position, terrainAlertDebugPos2, 2, Color.yellow);
                    GUIUtils.DrawLineBetweenWorldPositions(terrainAlertDebugPos2, terrainAlertDebugPos2 + (terrainAlertThreshold - terrainAlertDistance) * terrainAlertDebugDir2, 2, Color.yellow);
                }
                GUIUtils.DrawLineBetweenWorldPositions(vessel.transform.position, vessel.transform.position + 1.5f * terrainAlertDetectionRadius * (vessel.srf_vel_direction - relativeVelocityDownDirection).normalized, 1, Color.grey);
                GUIUtils.DrawLineBetweenWorldPositions(vessel.transform.position, vessel.transform.position + 1.5f * terrainAlertDetectionRadius * (vessel.srf_vel_direction + relativeVelocityDownDirection).normalized, 1, Color.grey);
                GUIUtils.DrawLineBetweenWorldPositions(vessel.transform.position, vessel.transform.position + 1.5f * terrainAlertDetectionRadius * (vessel.srf_vel_direction - relativeVelocityRightDirection).normalized, 1, Color.grey);
                GUIUtils.DrawLineBetweenWorldPositions(vessel.transform.position, vessel.transform.position + 1.5f * terrainAlertDetectionRadius * (vessel.srf_vel_direction + relativeVelocityRightDirection).normalized, 1, Color.grey);
            }
            if (waypointTerrainAvoidanceActive)
            {
                GUIUtils.DrawLineBetweenWorldPositions(vessel.transform.position, waypointRayHit.point, 2, Color.cyan); // Technically, it's from 1 frame behind the current position, but close enough for visualisation.
                GUIUtils.DrawLineBetweenWorldPositions(waypointRayHit.point, waypointRayHit.point + waypointTerrainSmoothedNormal * 50f, 2, Color.cyan);
            }
        }
    }

    /// <summary>
    /// A class to auto-tune the PID values of a pilot AI.
    ///
    /// Running with 5x time scaling once the plane is up to it's default altitude is recommended.
    ///
    /// Things to try:
    /// - Take N samples for each direction change (ignoring the guard mode approach for now), drop outliers and average the rest to get a smoother estimate of the loss f.
    /// - Sample at x-dx and x+dx to use a centred finite difference to approximate df/dx. This will require nearly twice as many samples, since we can't reuse those at x.
    /// - Take dx along each axis individually instead of random directions in R^d. This would require iterating through the axes and shuffling the order each epoch or weighting them based on the size of df/dx.
    /// - Build up the full gradient at each step by sampling at x±dx for each axis, then step in the direction of the gradient.
    /// </summary>
    public class PIDAutoTuning
    {
        // The AI being tuned.
        public PIDAutoTuning(BDModulePilotAI AI)
        {
            this.AI = AI;
            if (AI.vessel == null) { Debug.LogError($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: PIDAutoTuning triggered on null vessel!"); return; }
            WM = VesselModuleRegistry.GetMissileFire(AI.vessel);
            partCount = AI.vessel.Parts.Count;
            maxObservedSpeed = AI.minSpeed;
            flyToSpeed = AI.minSpeed;
        }

        // External flags.
        public bool measuring = false; // Whether a measurement is taking place or not.
        public float flyToSpeed = 0; // Speed to fly to the designated position.

        #region Internal parameters
        BDModulePilotAI AI;
        MissileFire WM;
        float timeout = 15; // Measure for at most 10s.
        float pointingTolerance = 0.1f; // Pointing tolerance for stopping measurements.
        float rollTolerance = 5f; // Roll tolerance for stopping measurements.
        // float onTargetTolerance = 1f; // Tolerance for beginning to consider being on target for "first min" checks.
        float onTargetTimer = 0;
        int partCount = 0;
        float measurementStartTime = -1;
        float measurementTime = 0;
        float maxPointingErrorSqr = 0;
        float maxRollErrorSqr = 0;
        // float pointingFirstMinAfterMaxStartTime = -1;
        // float rollFirstMinAfterMaxStartTime = -1;
        // float pointingFirstMinAfterMaxTime = -1;
        // float rollFirstMinAfterMaxTime = -1;
        float pointingOscillationAreaSqr = 0;
        float rollOscillationAreaSqr = 0;
        Vessel lastTargetVessel;
        float lastPointingErrorSqr = float.MaxValue;
        float lastAbsRollErrorSqr = float.MaxValue;
        float maxObservedSpeed = 0;
        float absHeadingChange = 0;
        // float pitchChange = 0;

        #region Gradient Descent (approx)
        class LR // Learning rate
        {
            // public float current = 1f;
            // int persistence = 5;
            // int count = 0;
            // float factor = 0.8f;
            // float last = 0;
            // float best = float.MaxValue;
            public void Update(float value)
            {
                return;
                // if (current > 1e-5)
                // {
                //     if (value < last && value < best)
                //     {
                //         best = value;
                //         count = 0;
                //     }
                //     if (value < best)
                //     {
                //         count = 0;
                //     }
                //     else
                //     {
                //         ++count;
                //     }
                //     if (count > persistence)
                //     {
                //         current *= factor;
                //         count = 0;
                //         best = float.MaxValue;
                //         last = 0;
                //     }
                // }
            }
        }
        Dictionary<string, BaseField> fields;
        Dictionary<string, string> fixedFields;
        Dictionary<string, float> baseValues;
        Dictionary<string, float> bestValues;
        Dictionary<string, Tuple<float, float>> limits;
        Dictionary<string, List<float>> lossSamples;
        Dictionary<string, float> dx;
        Dictionary<string, float> gradient;
        float lowestSumGradients = float.MaxValue;
        List<string> fieldNames;
        string currentField = "";
        int currentFieldIndex = 0;
        int passNumber = 0;
        float headingChange = 30f;
        float momentum = 0.3f;
        LR lr = new LR();
        #endregion
        #endregion

        /// <summary>
        /// Perform auto-tuning analysis.
        /// </summary>
        /// <param name="pitchError"></param>
        /// <param name="rollError"></param>
        /// <param name="yawError"></param>
        public void Update(float pitchError, float rollError, float yawError)
        {
            if (AI == null || AI.vessel == null) return; // Sanity check.
            if (AI.vessel.Parts.Count != partCount) // Don't tune a plane if it's lost parts.
            {
                AI.autoTune = false;
                Debug.LogWarning($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Vessel {AI.vessel.vesselName} has lost parts since spawning, auto-tuning disabled.");
                return;
            }
            measurementTime = Time.time - measurementStartTime;
            var pointingErrorSqr = pitchError * pitchError + yawError * yawError; // Combine pitch and yaw errors as a single pointing error.
            var rollErrorSqr = rollError * rollError;
            if ((float)AI.vessel.srfSpeed > maxObservedSpeed) maxObservedSpeed = (float)AI.vessel.srfSpeed;
            if (measuring)
            {
                if (pointingErrorSqr > maxPointingErrorSqr)
                {
                    maxPointingErrorSqr = pointingErrorSqr;
                    // pointingFirstMinAfterMaxStartTime = Time.time;
                    // pointingFirstMinAfterMaxTime = -1f;
                }
                if (rollErrorSqr > maxRollErrorSqr)
                {
                    maxRollErrorSqr = rollErrorSqr;
                    // rollFirstMinAfterMaxStartTime = Time.time;
                    // rollFirstMinAfterMaxTime = -1f;
                }

                if (pointingErrorSqr < pointingTolerance && rollErrorSqr < rollTolerance) { onTargetTimer += Time.fixedDeltaTime; }
                else { onTargetTimer = 0; }

                // Measuring timed out or completed to within tolerance (on target for 0.2s if in combat, 1s outside of combat).
                if (Time.time - measurementStartTime > timeout || onTargetTimer > (WM != null && WM.guardMode ? 0.2f : 1f))
                {
                    measurementTime = Time.time - measurementStartTime;
                    // if (pointingFirstMinAfterMaxTime < 0) pointingFirstMinAfterMaxTime = measurementTime;
                    // if (rollFirstMinAfterMaxTime < 0) rollFirstMinAfterMaxTime = measurementTime;
                    if (BDArmorySettings.DEBUG_AI && BDArmorySettings.DEBUG_TELEMETRY)
                    {
                        if (Time.time - measurementStartTime > timeout)
                        {
                            Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Measuring timed out. PointingError: {pointingErrorSqr}, RollError: {rollError}, onTargetTimer: {onTargetTimer:G2}s");
                        }
                        else if (onTargetTimer > (WM != null && WM.guardMode ? 0.2f : 1f))
                        {
                            Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Completed to within tolerance in {measurementTime}s.");
                        }
                        Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Max error sqr (pointing) {maxPointingErrorSqr} ({headingChange}), (roll) {maxRollErrorSqr}.");
                        // Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Time to first min (pointing) {pointingFirstMinAfterMaxTime}s, (roll) {rollFirstMinAfterMaxTime}s.");
                        Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Oscillation error sqr (pointing) {pointingOscillationAreaSqr}, (roll) {rollOscillationAreaSqr}. (Normalised: {pointingOscillationAreaSqr / absHeadingChange}, {rollOscillationAreaSqr / absHeadingChange}).");
                    }
                    TakeSample();
                    ResetMeasurements();
                }
                else if (WM != null && WM.guardMode && WM.currentTarget != null && WM.currentTarget.Vessel != lastTargetVessel) // Target changed while in combat. Reset, but don't update PID.
                {
                    if (BDArmorySettings.DEBUG_AI && BDArmorySettings.DEBUG_TELEMETRY) Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Changed target.");
                    ResetMeasurements();
                }
                else // Update internal parameters.
                {
                    // if (pointingFirstMinAfterMaxStartTime > 0 && pointingFirstMinAfterMaxTime < 0 && pointingErrorSqr >= lastPointingErrorSqr) pointingFirstMinAfterMaxTime = Time.time - pointingFirstMinAfterMaxStartTime - Time.fixedDeltaTime;
                    pointingOscillationAreaSqr += pointingErrorSqr * (AI.autoTuningFastResponseRelevance + measurementTime * measurementTime);
                    lastPointingErrorSqr = pointingErrorSqr;

                    // if (rollFirstMinAfterMaxStartTime > 0 && rollFirstMinAfterMaxTime < 0 && rollErrorSqr >= lastAbsRollErrorSqr) rollFirstMinAfterMaxTime = Time.time - rollFirstMinAfterMaxStartTime - Time.fixedDeltaTime;
                    rollOscillationAreaSqr += rollErrorSqr * (AI.autoTuningFastResponseRelevance + measurementTime * measurementTime);
                    lastAbsRollErrorSqr = rollErrorSqr;
                }
            }
            else
            {
                if (WM != null && WM.guardMode) // If guard mode is enabled, watch for target changes or something else to trigger a new measurement. This is going to be less reliable due to not using controlled fly-to directions. Don't use yet.
                {
                    // Significantly off-target, start measuring again.
                    if (pointingErrorSqr > 10f)
                    {
                        if (BDArmorySettings.DEBUG_AI && BDArmorySettings.DEBUG_TELEMETRY) Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Starting measuring due to being significantly off-target.");
                        StartMeasuring();
                    }
                }
                else // Just cruising, assign a fly-to position and begin measuring again.
                {
                    var upDirection = (AI.vessel.transform.position - AI.vessel.mainBody.transform.position).normalized;
                    var newDirection = Vector3.ProjectOnPlane(Quaternion.AngleAxis(headingChange, upDirection) * AI.vessel.srf_vel_direction, upDirection).normalized;
                    // newDirection = Quaternion.AngleAxis(pitchChange, Vector3.Cross(upDirection, newDirection)) * newDirection;
                    var newFlyToPoint = AI.vessel.transform.position + newDirection * maxObservedSpeed * timeout;
                    var altitudeAtFlyToPoint = BodyUtils.GetRadarAltitudeAtPos(newFlyToPoint, false);
                    if (altitudeAtFlyToPoint < 2f * AI.minAltitude) newFlyToPoint += (2f * AI.minAltitude - altitudeAtFlyToPoint) * upDirection; // Restrict altitude to above twice min altitude.
                    if (altitudeAtFlyToPoint > AI.defaultAltitude + AI.minAltitude) newFlyToPoint += (AI.defaultAltitude + AI.minAltitude - altitudeAtFlyToPoint) * upDirection; // Restrict altitude to below the default + min altitude.
                    Vector3d flyTo;
                    FlightGlobals.currentMainBody.GetLatLonAlt(newFlyToPoint, out flyTo.x, out flyTo.y, out flyTo.z);
                    AI.CommandFlyTo((Vector3)flyTo);
                    if (BDArmorySettings.DEBUG_AI && BDArmorySettings.DEBUG_TELEMETRY) Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Starting measuring with new fly-to position.");
                    StartMeasuring();
                }
            }
        }

        void StartMeasuring()
        {
            measuring = true;
            measurementStartTime = Time.time;
            partCount = AI.vessel.Parts.Count;
            if (WM != null && WM.currentTarget != null) lastTargetVessel = WM.currentTarget.Vessel;
        }

        public void ResetMeasurements()
        {
            measurementStartTime = -1;
            measurementTime = 0;
            maxPointingErrorSqr = 0;
            maxRollErrorSqr = 0;
            // pointingFirstMinAfterMaxStartTime = -1;
            // rollFirstMinAfterMaxStartTime = -1;
            // pointingFirstMinAfterMaxTime = -1;
            // rollFirstMinAfterMaxTime = -1;
            pointingOscillationAreaSqr = 0;
            rollOscillationAreaSqr = 0;
            if (!AI.autoTune && AI.currentCommand == PilotCommands.FlyTo) AI.ReleaseCommand(); // Release the AI if we've been commanding it.
            lastPointingErrorSqr = float.MaxValue;
            lastAbsRollErrorSqr = float.MaxValue;
            onTargetTimer = 0;
            if (!AI.autoTune) gradient = null;
            else if (gradient == null) ResetGradient();
            measuring = false; // Set this false last so we can use it in checks above.
        }

        void ResetSamples()
        {
            lossSamples.Clear();
            foreach (var fieldName in fieldNames) lossSamples.Add(fieldName, new List<float>());
            currentField = "base";
            currentFieldIndex = 0;
            passNumber = 0;
            headingChange = -(30f + 0.5f * (90f / AI.autoTuningNumSamples)) * Mathf.Sign(headingChange); // Initial θ for the midpoint rule approximation to ∫f(x, θ)dθ.
            absHeadingChange = Mathf.Abs(headingChange);

            // Update UI.
            if (string.IsNullOrEmpty(AI.autoTuningLossLabel)) AI.autoTuningLossLabel = $"measuring";
            AI.autoTuningLossLabel2 = $"{currentField}, sample nr: {passNumber + 1}";
            if ((AI.autoTuningFixedP && !fixedFields.ContainsKey("steerMult")) || (!AI.autoTuningFixedP && fixedFields.ContainsKey("steerMult")))
            {
                ToggleFixedP();
            }

            // pitchChange = 30f * UnityEngine.Random.Range(-1f, 1f) * UnityEngine.Random.Range(-1f, 1f); // Adjust pitch by ±30°, biased towards 0°.
            // flyToSpeed = UnityEngine.Random.Range(AI.minSpeed, (AI.maxSpeed + maxObservedSpeed) / 2f);
            flyToSpeed = AI.maxSpeed;
        }

        void ResetGradient()
        {
            fieldNames = new List<string> { "base" };
            fields = new Dictionary<string, BaseField>();
            fixedFields = new Dictionary<string, string>();
            baseValues = new Dictionary<string, float>();
            dx = new Dictionary<string, float>();
            gradient = new Dictionary<string, float>();
            limits = new Dictionary<string, Tuple<float, float>>();
            lossSamples = new Dictionary<string, List<float>>();

            // Check which PID controls are in use and set up the required dictionaries.
            foreach (var field in AI.Fields)
            {
                if (field.group.name == "pilotAI_PID" && field.guiActive && field.uiControlFlight.GetType() == typeof(UI_FloatRange))
                {
                    if (field.name.StartsWith("autoTuning")) continue;
                    if (AI.autoTuningFixedP && field.name == "steerMult")
                    {
                        fixedFields.Add(field.name, field.guiName);
                        continue;
                    }
                    var uiControl = (UI_FloatRange)field.uiControlFlight;
                    if (BDArmorySettings.DEBUG_AI && BDArmorySettings.DEBUG_TELEMETRY) Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Found PID field: {field.guiName} with value {field.GetValue(AI)} and limits {uiControl.minValue} — {uiControl.maxValue}");
                    fieldNames.Add(field.guiName);
                    fields.Add(field.guiName, field);
                    baseValues.Add(field.guiName, (float)field.GetValue(AI));
                    gradient.Add(field.guiName, 0);
                    dx.Add(field.guiName, 0.1f * AI.autoTuningLearningRate * (uiControl.maxValue - uiControl.minValue));
                    limits.Add(field.guiName, new Tuple<float, float>(uiControl.minValue, uiControl.maxValue));
                }
            }
            ResetSamples();
        }

        /// <summary>
        /// Take a sample of the loss at the current sample position.
        /// </summary>
        void TakeSample()
        {
            // Measure loss at the current sample point.
            // var fastResponseLoss = (1f - AI.autoTuningLossRatio) * (pointingFirstMinAfterMaxTime * pointingFirstMinAfterMaxTime + rollFirstMinAfterMaxTime * rollFirstMinAfterMaxTime);
            // var oscillationLoss = AI.autoTuningLossRatio * (pointingOscillationAreaSqr / absHeadingChange + rollOscillationAreaSqr / absHeadingChange / timeout);
            // lossSamples[currentField].Add(fastResponseLoss + oscillationLoss);
            var oscillationLoss = (pointingOscillationAreaSqr + AI.autoTuningRollRelevance * rollOscillationAreaSqr) / (absHeadingChange * absHeadingChange); // This normalisation seems to give a roughly flat distribution over the 30°—120° range for the test craft.
            lossSamples[currentField].Add(oscillationLoss);

            // Advance to the next sample.
            ++passNumber;
            if ((passNumber %= (int)AI.autoTuningNumSamples) == 0)
            {
                if (currentField == "base") // Update the UI with the current loss values at the base point.
                {
                    AI.autoTuningLossLabel = $"{lossSamples[currentField].Average():F4}";
                }
                ++currentFieldIndex;
                UpdatePIDValues((currentFieldIndex %= fieldNames.Count) == 0);
            }

            // Change heading for next sample
            headingChange = Mathf.Sign(headingChange) * (30f + (passNumber + 0.5f) * (90f / AI.autoTuningNumSamples)); // Midpoint rule for approximation to ∫f(x, θ)dθ.
            absHeadingChange = Mathf.Abs(headingChange);

            AI.autoTuningLossLabel2 = $"{currentField}, sample nr: {passNumber + 1}";
        }

        /// <summary>
        /// Update the PID values either for the new sample point or based on the gradient once we've got enough samples.
        /// </summary>
        /// <param name="samplingComplete"></param>
        void UpdatePIDValues(bool samplingComplete)
        {
            if (samplingComplete) // Perform a step in the downward direction of the gradient: x -> x - lr * df/dx
            {
                var sampleAverages = lossSamples.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Average()); // Average the samples (∫f(x, θ)dθ with the domain normalised to 1).
                foreach (var fieldName in gradient.Keys.ToList()) gradient[fieldName] = gradient[fieldName] * momentum + (1f - momentum) * Mathf.Clamp((sampleAverages[fieldName] - sampleAverages["base"]) / dx[fieldName], -0.1f, 0.1f) * (limits[fieldName].Item2 - limits[fieldName].Item1); // Update the gradient using momentum, clamping to ±0.1 of the scale of the field.
                if (gradient.Any(kvp => float.IsNaN(kvp.Value)))
                {
                    var message = "Gradient is giving NaN values, aborting auto-tuning.";
                    Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: " + message);
                    BDACompetitionMode.Instance.competitionStatus.Add(message);
                    RevertPIDValues();
                    AI.autoTune = false;
                    return;
                }
                if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Current: " + string.Join(", ", baseValues.Select(kvp => kvp.Key + ":" + kvp.Value)) + $", Loss: {sampleAverages["base"]}");
                if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Gradient: " + string.Join(", ", gradient.Select(kvp => kvp.Key + ":" + kvp.Value)));
                Dictionary<string, float> absoluteGradient = new Dictionary<string, float>();
                foreach (var fieldName in absoluteGradient.Keys.ToList()) absoluteGradient[fieldName] = Mathf.Abs(gradient[fieldName]);
                if (absoluteGradient.Values.Sum() < lowestSumGradients)
                {
                    lowestSumGradients = absoluteGradient.Values.Sum();
                    bestValues = baseValues;
                }
                lr.Update(gradient.Values.Select(x => x * x).Sum()); // Update learning rate based on the size of the gradient.
                foreach (var fieldName in baseValues.Keys.ToList()) baseValues[fieldName] = Mathf.Clamp(baseValues[fieldName] - AI.autoTuningLearningRate * gradient[fieldName], limits[fieldName].Item1, limits[fieldName].Item2); // Update PID values for gradient: x -> x - lr * df/dx (clamped to limits).
                foreach (var fieldName in fields.Keys.ToList()) fields[fieldName].SetValue(baseValues[fieldName], AI); // Set them in the AI.
                ResetSamples(); // Reset everything for the next gradient.
            }
            else // Update which axis we're measuring and reset the other ones back to the base value.
            {
                currentField = fieldNames[currentFieldIndex];
                foreach (var fieldName in fields.Keys.ToList()) fields[fieldName].SetValue(baseValues[fieldName] + (fieldName == currentField ? dx[fieldName] : 0), AI);
            }
        }

        public void RevertPIDValues()
        {
            if ((!AI.autoTune) && (bestValues != null))
            {
                lowestSumGradients = float.MaxValue;
                foreach (var fieldName in fields.Keys.ToList()) fields[fieldName].SetValue(bestValues[fieldName], AI);
            }
            else if (baseValues != null)
            { foreach (var fieldName in fields.Keys.ToList()) fields[fieldName].SetValue(baseValues[fieldName], AI); }
        }

        public void ToggleFixedP()
        {
            if (fixedFields != null && fields != null && fixedFields.ContainsKey("steerMult") && fields.ContainsKey(fixedFields["steerMult"]))
                fields[fixedFields["steerMult"]].SetValue(baseValues[fixedFields["steerMult"]], AI);
            ResetGradient();
        }
    }
}