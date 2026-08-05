using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

using BDArmory.Competition;
using BDArmory.Extensions;
using BDArmory.GameModes.Waypoints;
using BDArmory.Guidances;
using BDArmory.ModIntegration;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.VesselSpawning;
using BDArmory.Weapons.Missiles;
using BDArmory.Weapons;

namespace BDArmory.Control
{
    public class BDModulePilotAI : BDGenericAIBase, IBDAIControl
    {
        public override AIType aiType => AIType.PilotAI;
        #region Pilot AI Settings GUI
        #region PID
        enum Axis { Pitch, Yaw, Roll }

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerPower", //Steer Factor
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 20f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float steerMult = 14f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerKi", //Steer Ki
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.01f, maxValue = 1f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float steerKiAdjust = 0.4f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerDamping", //Steer Damping
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float steerDamping = 5f;

        #region Toggles for 3-axis and dynamic damping (before the sliders to keep them together)
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_3AxisPID", advancedTweakable = true, // 3-axis PID toggle
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_Toggle(scene = UI_Scene.All, disabledText = "#LOC_BDArmory_Disabled", enabledText = "#LOC_BDArmory_Enabled")]
        public bool threeAxisPID = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_3AxisSteerDamping", advancedTweakable = true, // 3-axis damping toggle
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_Toggle(scene = UI_Scene.All, disabledText = "#LOC_BDArmory_Disabled", enabledText = "#LOC_BDArmory_Enabled")]
        public bool threeAxisSteerDamping = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_DynamicSteerDamping", advancedTweakable = true, // Dynamic damping toggle
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_Toggle(scene = UI_Scene.All, disabledText = "#LOC_BDArmory_Disabled", enabledText = "#LOC_BDArmory_Enabled")]
        public bool dynamicSteerDamping = false;
        #endregion

        #region 3-axis Steer Damping (static)
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerDampingPitch", //Steer Damping Pitch
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float steerDampingPitch = 5f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerDampingYaw", //Steer Damping Yaw
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float steerDampingYaw = 5f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SteerDampingRoll", //Steer Damping Roll
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float steerDampingRoll = 5f;
        #endregion

        #region Dynamic Damping
        // Deprecated in favor of independent toggles.
        // UpgradeDamping() takes care of upgrading craft that used this.
        // Reverting to an older version of BDA and loading a craft will cause this setting to be lost.
        [KSPField(isPersistant = false)]
        public bool CustomDynamicAxisFields = false;

        // Note: min/max is replaced by off-target/on-target in localisation, but the variable names are kept to avoid reconfiguring existing craft.
        // Dynamic Damping
        [KSPField(guiName = "#LOC_BDArmory_AI_DynamicDamping", groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true), UI_Label(scene = UI_Scene.All)]
        private string DynamicDampingLabel = "";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_DynamicDampingMin", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float DynamicDampingMin = 6f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_DynamicDampingMax", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float DynamicDampingMax = 6.7f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_DynamicDampingFactor", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float dynamicSteerDampingFactor = 5f;

        // Dynamic Pitch
        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_AI_DynamicDampingPitch", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_Toggle(scene = UI_Scene.All, enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled")]
        public bool dynamicDampingPitch = true;

        [KSPField(guiName = "#LOC_BDArmory_AI_DynamicDampingPitch", groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true), UI_Label(scene = UI_Scene.All)]
        private string PitchLabel = "";

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_AI_DynamicDampingPitchMin", advancedTweakable = true, //Dynamic steer damping Clamp min
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float DynamicDampingPitchMin = 6f;

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_AI_DynamicDampingPitchMax", advancedTweakable = true, //Dynamic steer damping Clamp max
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float DynamicDampingPitchMax = 6.5f;

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_AI_DynamicDampingPitchFactor", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float dynamicSteerDampingPitchFactor = 8f;

        // Dynamic Yaw
        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_AI_DynamicDampingYaw", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_Toggle(scene = UI_Scene.All, enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled")]
        public bool dynamicDampingYaw = true;

        [KSPField(guiName = "#LOC_BDArmory_AI_DynamicDampingYaw", groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true), UI_Label(scene = UI_Scene.All)]
        private string YawLabel = "";

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_AI_DynamicDampingYawMin", advancedTweakable = true, //Dynamic steer damping Clamp min
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float DynamicDampingYawMin = 6f;

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_AI_DynamicDampingYawMax", advancedTweakable = true, //Dynamic steer damping Clamp max
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float DynamicDampingYawMax = 6.5f;

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_AI_DynamicDampingYawFactor", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float dynamicSteerDampingYawFactor = 8f;

        // Dynamic Roll
        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_AI_DynamicDampingRoll", advancedTweakable = true,
             groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
             UI_Toggle(scene = UI_Scene.All, enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled")]
        public bool dynamicDampingRoll = true;

        [KSPField(guiName = "#LOC_BDArmory_AI_DynamicDampingRoll", groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true), UI_Label(scene = UI_Scene.All)]
        private string RollLabel = "";

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_AI_DynamicDampingRollMin", advancedTweakable = true,
           groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
           UI_FloatRange(minValue = 0.1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float DynamicDampingRollMin = 6f;

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_AI_DynamicDampingRollMax", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float DynamicDampingRollMax = 6.5f;

        [KSPField(isPersistant = true, guiName = "#LOC_BDArmory_AI_DynamicDampingRollFactor", advancedTweakable = true, //Dynamic steer dampening Factor
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float dynamicSteerDampingRollFactor = 8f;
        #endregion

        #region 3-axis PID
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_3AxisPIDPitchMult",
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 20f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float threeAxisPIDPitchMult = 14f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_3AxisPIDPitchKi",
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.01f, maxValue = 1f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float threeAxisPIDPitchKi = 0.4f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_3AxisPIDPitchDamping",
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float threeAxisPIDPitchDamping = 5f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_3AxisPIDYawMult",
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 20f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float threeAxisPIDYawMult = 14f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_3AxisPIDYawKi",
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.01f, maxValue = 1f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float threeAxisPIDYawKi = 0.4f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_3AxisPIDYawDamping",
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float threeAxisPIDYawDamping = 5f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_3AxisPIDRollMult",
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 20f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float threeAxisPIDRollMult = 14f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_3AxisPIDRollKi",
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.01f, maxValue = 1f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float threeAxisPIDRollKi = 0.4f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_3AxisPIDRollDamping",
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 8f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float threeAxisPIDRollDamping = 5f;
        #endregion

        #region AutoTuning
        //Toggle AutoTuning
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_PID_AutoTune", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All)]
        bool autoTune = false;
        public bool AutoTune { get { return autoTune; } set { autoTune = value; OnAutoTuneChanged(); } }
        public PIDAutoTuning pidAutoTuning;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_AI_PID_AutoTuning_Loss", groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true), UI_Label(scene = UI_Scene.All)]
        public string autoTuningLossLabel = "";
        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "\tParams", groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true), UI_Label(scene = UI_Scene.All)]
        public string autoTuningLossLabel2 = "";
        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "\tField", groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true), UI_Label(scene = UI_Scene.All)]
        public string autoTuningLossLabel3 = "";
        public string autoTuningSummary = "";

        //AutoTuning Number Of Samples
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_AI_PID_AutoTuning_NumSamples", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 1f, maxValue = 10f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float autoTuningOptionNumSamples = 5f;

        //AutoTuning Fast Response Relevance
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_AI_PID_AutoTuning_FastResponseRelevance", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 0.5f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float autoTuningOptionFastResponseRelevance = 0.2f;

        //AutoTuning Initial Learning Rate
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_AI_PID_AutoTuning_InitialLearningRate", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatLogRange(minValue = 0.001f, maxValue = 1f, steps = 6, scene = UI_Scene.All)]
        public float autoTuningOptionInitialLearningRate = 1f;

        //AutoTuning Initial Roll Relevance
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_AI_PID_AutoTuning_InitialRollRelevance", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float autoTuningOptionInitialRollRelevance = 0.5f;

        //AutoTuning Altitude
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_AI_PID_AutoTuning_Altitude", //Auto-tuning Altitude
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 50f, maxValue = 5000f, stepIncrement = 50f, scene = UI_Scene.All)]
        public float autoTuningAltitude = 1000f;

        //AutoTuning Speed
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_AI_PID_AutoTuning_Speed", //Auto-tuning Speed
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 50f, maxValue = 800f, stepIncrement = 5f, scene = UI_Scene.All)]
        public float autoTuningSpeed = 200f;

        // Re-centering Distance
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_AI_PID_AutoTuning_RecenteringDistance",
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_FloatRange(minValue = 5f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float autoTuningRecenteringDistance = 15f;
        public float autoTuningRecenteringDistanceSqr { get; private set; }

        // Fixed fields for auto-tuning (only accessible via the AI GUI for now)
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedP = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedI = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedD = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedDP = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedDY = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedDR = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedDOff = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedDOn = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedDF = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedDPOff = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedDPOn = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedDPF = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedDYOff = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedDYOn = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedDYF = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedDROff = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedDROn = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedDRF = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedPp = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedIp = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedDp = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedPy = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedIy = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedDy = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedPr = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedIr = false;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false)] public bool autoTuningOptionFixedDr = false;

        //Clamp Maximums
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_AI_PID_AutoTuning_ClampMaximums", advancedTweakable = true,
            groupName = "pilotAI_PID", groupDisplayName = "#LOC_BDArmory_AI_PID", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All)]
        public bool autoTuningOptionClampMaximums = false;
        #endregion
        #endregion

        #region Altitudes
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_DefaultAltitude", //Default Alt.
            groupName = "pilotAI_Altitudes", groupDisplayName = "#LOC_BDArmory_AI_Altitudes", groupStartCollapsed = true),
            UI_FloatRange(minValue = 50f, maxValue = 5000f, stepIncrement = 50f, scene = UI_Scene.All)]
        public float defaultAltitude = 2000;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MinAltitude", //Min Altitude
            groupName = "pilotAI_Altitudes", groupDisplayName = "#LOC_BDArmory_AI_Altitudes", groupStartCollapsed = true),
            UI_FloatRange(minValue = 10f, maxValue = 1000, stepIncrement = 10f, scene = UI_Scene.All)]
        public float minAltitude = 200f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_HardMinAltitude", advancedTweakable = true,
            groupName = "pilotAI_Altitudes", groupDisplayName = "#LOC_BDArmory_AI_Altitudes", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All)]
        public bool hardMinAltitude = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MaxAltitude", //Max Altitude
            groupName = "pilotAI_Altitudes", groupDisplayName = "#LOC_BDArmory_AI_Altitudes", groupStartCollapsed = true),
            UI_FloatRange(minValue = 100f, maxValue = 10000, stepIncrement = 100f, scene = UI_Scene.All)]
        public float maxAltitude = 7500f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MaxAltitude", advancedTweakable = true,
            groupName = "pilotAI_Altitudes", groupDisplayName = "#LOC_BDArmory_AI_Altitudes", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All)]
        public bool maxAltitudeToggle = false;
        #endregion

        #region Speeds
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MaxSpeed", //Max Speed
            groupName = "pilotAI_Speeds", groupDisplayName = "#LOC_BDArmory_AI_Speeds", groupStartCollapsed = true),
            UI_FloatRange(minValue = 50f, maxValue = 800f, stepIncrement = 5f, scene = UI_Scene.All)]
        public float maxSpeed = 350;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_TakeOffSpeed", //TakeOff Speed
            groupName = "pilotAI_Speeds", groupDisplayName = "#LOC_BDArmory_AI_Speeds", groupStartCollapsed = true),
            UI_FloatRange(minValue = 10f, maxValue = 200f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float takeOffSpeed = 60;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MinSpeed", //MinCombatSpeed
            groupName = "pilotAI_Speeds", groupDisplayName = "#LOC_BDArmory_AI_Speeds", groupStartCollapsed = true),
            UI_FloatRange(minValue = 10f, maxValue = 200, stepIncrement = 1f, scene = UI_Scene.All)]
        public float minSpeed = 60f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_StrafingSpeed", //Strafing Speed
            groupName = "pilotAI_Speeds", groupDisplayName = "#LOC_BDArmory_AI_Speeds", groupStartCollapsed = true),
            UI_FloatRange(minValue = 10f, maxValue = 200, stepIncrement = 1f, scene = UI_Scene.All)]
        public float strafingSpeed = 100f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_IdleSpeed", //Idle Speed
            groupName = "pilotAI_Speeds", groupDisplayName = "#LOC_BDArmory_AI_Speeds", groupStartCollapsed = true),
            UI_FloatRange(minValue = 10f, maxValue = 200f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float idleSpeed = 200f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ABPriority", advancedTweakable = true, //Afterburner Priority
            groupName = "pilotAI_Speeds", groupDisplayName = "#LOC_BDArmory_AI_Speeds", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float ABPriority = 50f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ABOverrideThreshold", advancedTweakable = true, //Afterburner Override Threshold
            groupName = "pilotAI_Speeds", groupDisplayName = "#LOC_BDArmory_AI_Speeds", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 200f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float ABOverrideThreshold = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_BrakingPriority", advancedTweakable = true, //Afterburner Priority
            groupName = "pilotAI_Speeds", groupDisplayName = "#LOC_BDArmory_AI_Speeds", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float brakingPriority = 50f;
        #endregion

        #region Control Limits
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_LowSpeedSteerLimiter", advancedTweakable = true, // Low-Speed Steer Limiter
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_AI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = .1f, maxValue = 1f, stepIncrement = .05f, scene = UI_Scene.All)]
        public float maxSteer = 1;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_LowSpeedLimiterSpeed", advancedTweakable = true, // Low-Speed Limiter Switch Speed 
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_AI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = 10f, maxValue = 500f, stepIncrement = 1.0f, scene = UI_Scene.All)]
        public float lowSpeedSwitch = 100f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_HighSpeedSteerLimiter", advancedTweakable = true, // High-Speed Steer Limiter
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_AI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = .1f, maxValue = 1f, stepIncrement = .05f, scene = UI_Scene.All)]
        public float maxSteerAtMaxSpeed = 1;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_HighSpeedLimiterSpeed", advancedTweakable = true, // High-Speed Limiter Switch Speed 
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_AI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = 10f, maxValue = 500f, stepIncrement = 1.0f, scene = UI_Scene.All)]
        public float cornerSpeed = 200f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_AltitudeSteerLimiterFactor", advancedTweakable = true, // Altitude Steer Limiter Factor
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_AI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = -1f, maxValue = 1f, stepIncrement = .05f, scene = UI_Scene.All)]
        public float altitudeSteerLimiterFactor = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_AltitudeSteerLimiterAltitude", advancedTweakable = true, // Altitude Steer Limiter Altitude 
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_AI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = 100f, maxValue = 10000f, stepIncrement = 100f, scene = UI_Scene.All)]
        public float altitudeSteerLimiterAltitude = 5000f;

        //[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_AttitudeLimiter", advancedTweakable = true, //Attitude Limiter, not currently functional
        //    groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_AI_ControlLimits", groupStartCollapsed = true),
        // UI_FloatRange(minValue = 10f, maxValue = 90f, stepIncrement = 5f, scene = UI_Scene.All)]
        //public float maxAttitude = 90f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_BankLimiter", advancedTweakable = true, //Bank Angle Limiter
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_AI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = 10f, maxValue = 180f, stepIncrement = 5f, scene = UI_Scene.All)]
        public float maxBank = 180f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_WaypointPreRollTime", advancedTweakable = true, //Waypoint Pre-Roll Time
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_AI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 2f, stepIncrement = 0.05f, scene = UI_Scene.All)]
        public float waypointPreRollTime = 0.5f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_WaypointYawAuthorityTime", advancedTweakable = true, //Waypoint Yaw Authority Time
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_AI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float waypointYawAuthorityTime = 5f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MaxAllowedGForce", //Max G
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_AI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = 2f, maxValue = 45f, stepIncrement = 0.5f, scene = UI_Scene.All)]
        public float maxAllowedGForce = 25;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MaxAllowedAoA", //Max AoA
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_AI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 90f, stepIncrement = 2.5f, scene = UI_Scene.All)]
        public float maxAllowedAoA = 35;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_PostStallAoA", //Post-stall AoA
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_AI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 90f, stepIncrement = 2.5f, scene = UI_Scene.All)]
        public float postStallAoA = 35;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ImmelmannTurnAngle", advancedTweakable = true, // Immelmann Turn Angle
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_AI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 90f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float ImmelmannTurnAngle = 30f; // 30° from directly behind -> 150°
        float ImmelmannTurnCosAngle = -0.866f;
        float BankedTurnDistance = 2800f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ImmelmannPitchUpBias", advancedTweakable = true, // Immelmann Pitch-Up Bias
            groupName = "pilotAI_ControlLimits", groupDisplayName = "#LOC_BDArmory_AI_ControlLimits", groupStartCollapsed = true),
            UI_FloatRange(minValue = -90f, maxValue = 90f, stepIncrement = 5f, scene = UI_Scene.All)]
        public float ImmelmannPitchUpBias = 10f; // °/s
        #endregion

        #region Bombing
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_DiveBombing", advancedTweakable = true,
            groupName = "pilotAI_Bombing", groupDisplayName = "#LOC_BDArmory_AI_Bombing", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All)]
        public bool divebombing = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_BombingAltitude",
            groupName = "pilotAI_Bombing", groupDisplayName = "#LOC_BDArmory_AI_Bombing", groupStartCollapsed = true),
            UI_FloatRange(minValue = 100f, maxValue = 3000, stepIncrement = 50f, scene = UI_Scene.All)]
        public float bombingAltitude = 500;
        public float finalBombingAltitude;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_DiveBombingAngle",
            groupName = "pilotAI_Bombing", groupDisplayName = "#LOC_BDArmory_AI_Bombing", groupStartCollapsed = true),
            UI_FloatRange(minValue = 30f, maxValue = 90f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float divebombingAngle = 60f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_BombingExtendDistance", advancedTweakable = true, //Bombing Extend Distance Bombing
            groupName = "pilotAI_Bombing", groupDisplayName = "#LOC_BDArmory_AI_Bombing", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 5000f, stepIncrement = 50f, scene = UI_Scene.All)]
        public float extendDistanceBombing = 1000f; // Doesn't include expected (unguided) bomb drop distance.
        #endregion

        #region EvadeExtend
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MinEvasionTime", advancedTweakable = true, // Min Evasion Time
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = .05f, scene = UI_Scene.All)]
        public float minEvasionTime = 0.2f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_EvasionThreshold", advancedTweakable = true, //Evasion Distance Threshold
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float evasionThreshold = 25f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_EvasionTimeThreshold", advancedTweakable = true, // Evasion Time Threshold
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 5f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float evasionTimeThreshold = 0.1f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_EvasionMinRangeThreshold", advancedTweakable = true, // Evasion Min Range Threshold
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatSemiLogRange(minValue = 10f, maxValue = 10000f, sigFig = 1, withZero = true)]
        public float evasionMinRangeThreshold = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_EvasionNonlinearity", advancedTweakable = true, // Evasion/Extension Nonlinearity
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 10f, stepIncrement = .1f, scene = UI_Scene.All)]
        public float evasionNonlinearity = 2f;
        float evasionNonlinearityDirection = 1;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_EvasionIgnoreMyTargetTargetingMe", advancedTweakable = true,//Ignore my target targeting me
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All),]
        public bool evasionIgnoreMyTargetTargetingMe = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_EvasionMissileKinematic", advancedTweakable = true,//Kinematic missile evasion
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All),]
        public bool evasionMissileKinematic = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_EvasionMissileEmergencyNotch", advancedTweakable = true, //Missile Emergency Notch Threshold
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float evasionMissileEmergencyNotchVel = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_CollisionAvoidanceThreshold", advancedTweakable = true, //Vessel collision avoidance threshold
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 50f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float collisionAvoidanceThreshold = 20f; // 20m + target's average radius.

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_CollisionAvoidanceLookAheadPeriod", advancedTweakable = true, //Vessel collision avoidance look ahead period
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 3f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float vesselCollisionAvoidanceLookAheadPeriod = 1.5f; // Look 1.5s ahead for potential collisions.

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_CollisionAvoidanceStrength", advancedTweakable = true, //Vessel collision avoidance strength
           groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
           UI_FloatRange(minValue = 0f, maxValue = 4f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float vesselCollisionAvoidanceStrength = 2f; // 2° per frame (100°/s).

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_MinObstacleMass", advancedTweakable = true,//Min obstacle mass
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatSemiLogRange(minValue = 0.1f, maxValue = 100f, sigFig = 2, withZero = true, scene = UI_Scene.All)]
        public float AvoidMass = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_StandoffDistance", advancedTweakable = true, //Min Approach Distance
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 1000f, stepIncrement = 50f, scene = UI_Scene.All)]
        public float vesselStandoffDistance = 200f; // try to avoid getting closer than 200m

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_RetreatDistance", advancedTweakable = true,
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatSemiLogRange(minValue = 1000f, maxValue = 20000f, sigFig = 2, withZero = false, scene = UI_Scene.All)]
        public float retreatDistance = 4000f; // Repeatedly retreat this far when under attack while orbiting. The new 2km orbit is centered at this distance from the current position.

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ExtendDistanceAirToAir", advancedTweakable = true, //Extend Distance Air-To-Air
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 2000f, stepIncrement = 10f, scene = UI_Scene.All)]
        public float extendDistanceAirToAir = 300f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ExtendAngleAirToAir", advancedTweakable = true, //Extend Angle Air-To-Air
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = -10f, maxValue = 45f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float extendAngleAirToAir = 0f;
        float _extendAngleAirToAir = 0;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ExtendDistanceAirToGroundGuns", advancedTweakable = true, //Extend Distance Air-To-Ground (Guns)
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 5000f, stepIncrement = 50f, scene = UI_Scene.All)]
        public float extendDistanceAirToGroundGuns = 1500f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ExtendTargetVel", advancedTweakable = true, //Extend Target Velocity Factor
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 2f, stepIncrement = .1f, scene = UI_Scene.All)]
        public float extendTargetVel = 0.8f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ExtendTargetAngle", advancedTweakable = true, //Extend Target Angle
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 180f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float extendTargetAngle = 78f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ExtendTargetDist", advancedTweakable = true, //Extend Target Distance
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 5000f, stepIncrement = 25f, scene = UI_Scene.All)]
        public float extendTargetDist = 300f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ExtendAbortTime", advancedTweakable = true, //Extend Abort Time
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 1f, maxValue = 30f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float extendAbortTime = 10f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ExtendMinGainRate", advancedTweakable = true, //Extend Min Gain Rate
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1, scene = UI_Scene.All)]
        public float extendMinGainRate = 10f;

        float extensionCutoffTimer = 0; //For FJRT/P:S extension termination to prevent overly long extensions from poorly tuned extension settings
        public float extensionCutoffTime = 0;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ExtendToggle", advancedTweakable = true,//Extend Toggle
            groupName = "pilotAI_EvadeExtend", groupDisplayName = "#LOC_BDArmory_AI_EvadeExtend", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All),]
        public bool canExtend = true;
        #endregion

        #region Terrain
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, category = "DoubleSlider", guiName = "#LOC_BDArmory_AI_TurnRadiusTwiddleFactorMin", advancedTweakable = true,//Turn radius twiddle factors (category seems to have no effect)
            groupName = "pilotAI_Terrain", groupDisplayName = "#LOC_BDArmory_AI_Terrain", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 5f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float turnRadiusTwiddleFactorMin = 2.0f; // Minimum and maximum twiddle factors for the turn radius. Depends on roll rate and how the vessel behaves under fire.

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, category = "DoubleSlider", guiName = "#LOC_BDArmory_AI_TurnRadiusTwiddleFactorMax", advancedTweakable = true,//Turn radius twiddle factors (category seems to have no effect)
            groupName = "pilotAI_Terrain", groupDisplayName = "#LOC_BDArmory_AI_Terrain", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0.1f, maxValue = 5f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float turnRadiusTwiddleFactorMax = 3.0f; // Minimum and maximum twiddle factors for the turn radius. Depends on roll rate and how the vessel behaves under fire.

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_TerrainAvoidanceCriticalAngle", advancedTweakable = true, // Critical angle for inverted terrain avoidance.
            groupName = "pilotAI_Terrain", groupDisplayName = "#LOC_BDArmory_AI_Terrain", groupStartCollapsed = true),
            UI_FloatRange(minValue = 90f, maxValue = 180f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float terrainAvoidanceCriticalAngle = 135f;
        float terrainAvoidanceCriticalCosAngle = -0.5f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_TerrainAvoidanceVesselReactionTime", advancedTweakable = true, // Vessel reaction time.
            groupName = "pilotAI_Terrain", groupDisplayName = "#LOC_BDArmory_AI_Terrain", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 4f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float controlSurfaceDeploymentTime = 2f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_TerrainAvoidancePostAvoidanceCoolDown", advancedTweakable = true, // Post-avoidance cool-down.
            groupName = "pilotAI_Terrain", groupDisplayName = "#LOC_BDArmory_AI_Terrain", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 2f, stepIncrement = 0.02f, scene = UI_Scene.All)]
        public float postTerrainAvoidanceCoolDownDuration = 1f; // Duration after exiting terrain avoidance to ease out of pulling away from terrain.

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_WaypointTerrainAvoidance", advancedTweakable = true,//Waypoint terrain avoidance.
            groupName = "pilotAI_Terrain", groupDisplayName = "#LOC_BDArmory_AI_Terrain", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float waypointTerrainAvoidance = 0.5f;
        float waypointTerrainAvoidanceSmoothingFactor = 0.933f;
        #endregion

        #region Ramming
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_AllowRamming", advancedTweakable = true, //Toggle Allow Ramming
            groupName = "pilotAI_Ramming", groupDisplayName = "#LOC_BDArmory_AI_Ramming", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All),]
        public bool allowRamming = true; // Allow switching to ramming mode.

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_AllowRammingGroundTargets", advancedTweakable = true, //Toggle Allow Ramming Ground Targets
            groupName = "pilotAI_Ramming", groupDisplayName = "#LOC_BDArmory_AI_Ramming", groupStartCollapsed = true),
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All),]
        public bool allowRammingGroundTargets = true; // Allow ramming ground targets.

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_ControlSurfaceLag", advancedTweakable = true,//Control surface lag (for getting an accurate intercept for ramming).
            groupName = "pilotAI_Ramming", groupDisplayName = "#LOC_BDArmory_AI_Ramming", groupStartCollapsed = true),
            UI_FloatRange(minValue = 0f, maxValue = 0.2f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float controlSurfaceLag = 0.01f; // Lag time in response of control surfaces.
        #endregion

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_SliderResolution", advancedTweakable = true), // Slider Resolution
             UI_ChooseOption(options = new string[4] { "Low", "Normal", "High", "Insane" }, scene = UI_Scene.All)]
        public string sliderResolution = "Normal";
        string previousSliderResolution = "Normal";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_Orbit", advancedTweakable = true),//Orbit 
            UI_Toggle(enabledText = "#LOC_BDArmory_AI_Orbit_Starboard", disabledText = "#LOC_BDArmory_AI_Orbit_Port", scene = UI_Scene.All),]//Starboard (CW)--Port (CCW)
        public bool ClockwiseOrbit = true;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_UnclampTuning", advancedTweakable = true),//Unclamp tuning 
            UI_Toggle(enabledText = "#LOC_BDArmory_AI_UnclampTuning_enabledText", disabledText = "#LOC_BDArmory_AI_UnclampTuning_disabledText", scene = UI_Scene.All),]//Unclamped--Clamped
        bool upToEleven = false;
        public bool UpToEleven { get { return upToEleven; } set { if (upToEleven != value) { upToEleven = value; TurnItUpToEleven(); } } }

        Dictionary<string, float> altMaxValues = new Dictionary<string, float>
        {
            { nameof(defaultAltitude), 100000f },
            { nameof(minAltitude), 100000f },
            { nameof(maxAltitude), 150000f },
            { nameof(bombingAltitude), 10000f},
            { nameof(steerMult), 200f },
            { nameof(steerKiAdjust), 20f },
            { nameof(steerDamping), 100f },
            { nameof(maxSteer), 1f},
            { nameof(maxSpeed), (BDArmorySettings.RUNWAY_PROJECT_ROUND == 55) ? 600f : 3000f },
            { nameof(takeOffSpeed), 2000f },
            { nameof(minSpeed), 2000f },
            { nameof(strafingSpeed), 2000f },
            { nameof(idleSpeed), 3000f },
            { nameof(lowSpeedSwitch), 3000f },
            { nameof(cornerSpeed), 3000f },
            { nameof(altitudeSteerLimiterFactor), 10f },
            { nameof(altitudeSteerLimiterAltitude), 100000f },
            { nameof(maxAllowedGForce), 1000f },
            { nameof(maxAllowedAoA), 180f },
            { nameof(postStallAoA), 180f },
            { nameof(extendDistanceAirToAir), 20000f },
            { nameof(extendAngleAirToAir), 90f },
            { nameof(extendDistanceAirToGroundGuns), 20000f },
            { nameof(extendDistanceBombing), 20000f },
            { nameof(minEvasionTime), 10f },
            { nameof(evasionNonlinearity), 90f },
            { nameof(evasionThreshold), 300f },
            { nameof(evasionTimeThreshold), 30f },
            { nameof(evasionMissileEmergencyNotchVel), 200f },
            { nameof(vesselStandoffDistance), 5000f },
            { nameof(turnRadiusTwiddleFactorMin), 10f},
            { nameof(turnRadiusTwiddleFactorMax), 10f},
            { nameof(controlSurfaceDeploymentTime), 10f },
            { nameof(controlSurfaceLag), 1f},
            { nameof(steerDampingPitch), 100f},
            { nameof(steerDampingYaw), 100f},
            { nameof(steerDampingRoll), 100f},
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
            { nameof(dynamicSteerDampingRollFactor), 100f },
            { nameof(threeAxisPIDPitchMult), 200f },
            { nameof(threeAxisPIDPitchKi), 20f },
            { nameof(threeAxisPIDPitchDamping), 100f },
            { nameof(threeAxisPIDYawMult), 200f },
            { nameof(threeAxisPIDYawKi), 20f },
            { nameof(threeAxisPIDYawDamping), 100f },
            { nameof(threeAxisPIDRollMult), 200f },
            { nameof(threeAxisPIDRollKi), 20f },
            { nameof(threeAxisPIDRollDamping), 100f },
            { nameof(autoTuningAltitude), 100000f },
            { nameof(autoTuningSpeed), 3000f }
        };
        Dictionary<string, float> altMinValues = new Dictionary<string, float> {
            { nameof(extendAngleAirToAir), -90f },
            { nameof(altitudeSteerLimiterFactor), -10f },
        };
        Dictionary<string, (float, float, float)> altSemiLogValues = new Dictionary<string, (float, float, float)> {
            { nameof(evasionMinRangeThreshold), (1f, 1000000f, 1f) },
            { nameof(AvoidMass), (10f, 1000000f, 2f) },
        };

        void TurnItUpToEleven(BaseField _field = null, object _obj = null)
        {
            if (AutoTune && pidAutoTuning is not null)
            {
                // Reset PID values and stop measurement before switching alt values so the correct PID values are used.
                pidAutoTuning.RevertPIDValues();
                pidAutoTuning.ResetMeasurements();
            }
            using (var s = altMaxValues.Keys.ToList().GetEnumerator())
                while (s.MoveNext())
                {
                    var field = (UI_FloatRange)(HighLogic.LoadedSceneIsFlight ? Fields[s.Current].uiControlFlight : Fields[s.Current].uiControlEditor);
                    if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI]: Swapping max value of {s.Current} from {field.maxValue} to {altMaxValues[s.Current]}, current value is {(float)typeof(BDModulePilotAI).GetField(s.Current).GetValue(this)}");
                    (altMaxValues[s.Current], field.maxValue) = (field.maxValue, altMaxValues[s.Current]);
                    // change the value back to what it is now after fixed update, because changing the max value will clamp it down
                    // using reflection here, don't look at me like that, this does not run often
                    StartCoroutine(SetVar(s.Current, (float)typeof(BDModulePilotAI).GetField(s.Current).GetValue(this)));
                }
            using (var s = altMinValues.Keys.ToList().GetEnumerator())
                while (s.MoveNext())
                {
                    var field = (UI_FloatRange)(HighLogic.LoadedSceneIsFlight ? Fields[s.Current].uiControlFlight : Fields[s.Current].uiControlEditor);
                    if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI]: Swapping min value of {s.Current} from {field.minValue} to {altMinValues[s.Current]}, current value is {(float)typeof(BDModulePilotAI).GetField(s.Current).GetValue(this)}");
                    (altMinValues[s.Current], field.minValue) = (field.minValue, altMinValues[s.Current]);
                    // change the value back to what it is now after fixed update, because changing the min value will clamp it down
                    // using reflection here, don't look at me like that, this does not run often
                    StartCoroutine(SetVar(s.Current, (float)typeof(BDModulePilotAI).GetField(s.Current).GetValue(this)));
                }
            foreach (var fieldName in altSemiLogValues.Keys.ToList())
            {
                var field = (UI_FloatSemiLogRange)(HighLogic.LoadedSceneIsFlight ? Fields[fieldName].uiControlFlight : Fields[fieldName].uiControlEditor);
                var temp = (field.minValue, field.maxValue, field.sigFig);
                var altValues = altSemiLogValues[fieldName];
                if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI]: Swapping semiLog limits of {fieldName} from {temp} to {altValues}");
                field.UpdateLimits(altValues.Item1, altValues.Item2, altValues.Item3);
                altSemiLogValues[fieldName] = temp;
            }
            OnAutoTuneOptionsChanged(); // Reset auto-tuning again (including the gradient) so that the correct PID limits are used.
        }

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_AI_Standby"),//Standby Mode
            UI_Toggle(enabledText = "#LOC_BDArmory_On", disabledText = "#LOC_BDArmory_Off")]//On--Off
        public bool standbyMode = false;
        bool standbyModeEnabled = false;

        #region Store/Restore
        private static Dictionary<string, List<System.Tuple<string, object>>> storedSettings; // Stored settings for each vessel.
        [KSPEvent(advancedTweakable = false, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_StoreSettings", active = true)]//Store Settings
        public void StoreSettings()
        {
            StoreSettings(null);
        }
        void StoreSettings(string vesselName)
        {
            if (vesselName is null)
                vesselName = HighLogic.LoadedSceneIsFlight ? vessel.GetName() : EditorLogic.fetch.ship.shipName;
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
                if (field.FieldType == typeof(PIDAutoTuning) || field.FieldType == typeof(Vessel)) // Skip fields that are references to other objects that ought to revert to null.
                {
                    if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI]: Skipping {field.Name} of type {field.FieldType} as it's a reference type.");
                    continue;
                }
                storedSettings[vesselName].Add(new System.Tuple<string, object>(field.Name, field.GetValue(this)));
            }
            Events[nameof(RestoreSettings)].active = true;
            if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI]: Stored AI settings for {vesselName}: " + string.Join(", ", storedSettings[vesselName].Select(s => s.Item1 + "=" + s.Item2)));
        }
        [KSPEvent(advancedTweakable = false, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_RestoreSettings", active = false)]//Restore Settings
        public void RestoreSettings()
        {
            var vesselName = HighLogic.LoadedSceneIsFlight ? vessel.GetName() : EditorLogic.fetch.ship.shipName;
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
            if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI]: Restored AI settings for {vesselName}: " + string.Join(", ", storedSettings[vesselName].Select(s => s.Item1 + "=" + s.Item2)));
        }

        // This uses the parts' persistentId to reference the parts. Possibly, it should use some other identifier (what's used as a tag at the end of the "part = ..." and "link = ..." lines?) in case of duplicate persistentIds?
        private static Dictionary<string, Dictionary<uint, List<System.Tuple<string, object>>>> storedControlSurfaceSettings; // Stored control surface settings for each vessel.
        [KSPEvent(advancedTweakable = false, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_StoreControlSurfaceSettings", active = true)]//Store Control Surfaces
        public void StoreControlSurfaceSettings()
        {
            var vesselName = HighLogic.LoadedSceneIsFlight ? vessel.GetName() : EditorLogic.fetch.ship.shipName;
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
            Events[nameof(RestoreControlSurfaceSettings)].active = true;
        }
        private static Dictionary<string, Dictionary<uint, List<System.Tuple<string, object>>>> storedFARControlSurfaceSettings; // Stored control surface settings for each vessel.
        void StoreFARControlSurfaceSettings()
        {
            if (!FerramAerospace.hasFARControllableSurface) return;
            var vesselName = HighLogic.LoadedSceneIsFlight ? vessel.GetName() : EditorLogic.fetch.ship.shipName;
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
            var vesselName = HighLogic.LoadedSceneIsFlight ? vessel.GetName() : EditorLogic.fetch.ship.shipName;
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
            var vesselName = HighLogic.LoadedSceneIsFlight ? vessel.GetName() : EditorLogic.fetch.ship.shipName;
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
        Vector3 upDirection = Vector3.up;

        #region Status / Steer Mode
        enum StatusMode { Free, Orbiting, Engaging, Evading, Extending, TerrainAvoidance, CollisionAvoidance, RammingSpeed, TakingOff, GainingAltitude, Retreating, Custom }
        StatusMode currentStatusMode = StatusMode.Free;
        StatusMode lastStatusMode = StatusMode.Free;
        protected override void SetStatus(string status)
        {
            base.SetStatus(status);
            if (status.StartsWith("Free")) currentStatusMode = StatusMode.Free;
            else if (status.StartsWith("Engaging")) currentStatusMode = StatusMode.Engaging;
            else if (status.StartsWith("Evading")) currentStatusMode = StatusMode.Evading;
            else if (status.StartsWith("Orbiting")) currentStatusMode = StatusMode.Orbiting;
            else if (status.StartsWith("Extending")) currentStatusMode = StatusMode.Extending;
            else if (status.StartsWith("Ramming")) currentStatusMode = StatusMode.RammingSpeed;
            else if (status.StartsWith("Taking off")) currentStatusMode = StatusMode.TakingOff;
            else if (status.StartsWith("Gain Alt")) currentStatusMode = StatusMode.GainingAltitude;
            else if (status.StartsWith("Terrain")) currentStatusMode = StatusMode.TerrainAvoidance;
            else if (status.StartsWith("AvoidCollision")) currentStatusMode = StatusMode.CollisionAvoidance;
            else if (status.StartsWith("Retreating")) currentStatusMode = StatusMode.Retreating;
            else currentStatusMode = StatusMode.Custom;
        }

        public enum SteerModes
        {
            NormalFlight, // For most flight situations where the velocity direction is more important.
            Manoeuvering, // For high-speed manoeuvering (e.g., evading, avoiding collisions), between NormalFlight and Aiming (less incentive to "roll up").
            Aiming // For actually aiming or for when the orientation of the plane is preferable to use instead of the velocity, e.g., regain energy, PSM. 
        }
        SteerModes steerMode = SteerModes.NormalFlight;
        #endregion

        #region PID Internals
        //Controller Integral
        Vector3 directionIntegral;
        float pitchIntegral;
        float yawIntegral;
        float rollIntegral;

        //Dynamic Steer Damping values for the AI GUI
        public float dynSteerDampingValue;
        public float dynSteerDampingPitchValue;
        public float dynSteerDampingYawValue;
        public float dynSteerDampingRollValue;

        bool dirtyPAW_PID = false; // Flag for when the PID part of the PAW needs fixing.
        #endregion

        #region Manoeuvrability and G-loading
        //manueuverability and g loading data
        // float maxDynPresGRecorded;
        float dynDynPresGRecorded = 1f; // Start at reasonable non-zero value.
        float dynVelocityMagSqr = 1f; // Start at reasonable non-zero value.
        float dynDecayRate = 1f; // Decay rate for dynamic measurements. Set to a half-life of 60s in Start.
        float dynVelSmoothingCoef = 1f; // Decay rate for smoothing the dynVelocityMagSqr
        float dynUserSteerLimitMax = 1f; // Track the recently used max user steer limit.

        float maxAllowedSinAoA;
        float lastAllowedAoA;

        float maxPosG;
        float sinAoAAtMaxPosG;

        float maxNegG;
        float sinAoAAtMaxNegG;

        // float[] gLoadMovingAvgArray = new float[32];
        // float[] cosAoAMovingAvgArray = new float[32];
        // int movingAvgIndex;
        // float gLoadMovingAvg;
        // float cosAoAMovingAvg;
        SmoothingF smoothedGLoad;
        SmoothingF smoothedSinAoA;

        float gAoASlopePerDynPres;        //used to limit control input at very high dynamic pressures to avoid structural failure
        float gOffsetPerDynPres;

        float posPitchDynPresLimitIntegrator = 1;
        float negPitchDynPresLimitIntegrator = -1;

        float lastSinAoA;
        float lastPitchInput;

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
        #endregion

        #region Ramming / Extending / Evading
        // Ramming
        public bool ramming = false; // Whether or not we're currently trying to ram someone.

        // Extending
        bool extending;
        bool extendParametersSet = false;
        bool extendingForBombing = false;
        float extendDistance;
        float lastExtendDistance = 0;
        bool extendHorizontally = true; // Measure the extendDistance horizonally (for A2G) or not (for A2A).
        float extendDesiredMinRadarAltitude;
        public string extendingReason = "";
        public Vessel extendTarget = null;
        Vector3 lastExtendTargetPosition;
        float turningTimer;

        bool requestedExtend;
        Vector3 requestedExtendTpos;
        float extendRequestMinDistance = 0;
        MissileBase extendForMissile = null; // Extend to fire this missile, or extending after dropping this bomb.
        float extendAbortTimer = 0;

        public bool IsExtending
        {
            get { return extending || requestedExtend; }
        }

        public void StopExtending(string reason, bool cooldown = false)
        {
            if (!extending) return;
            extending = false;
            requestedExtend = false;
            extendingReason = "";
            extendTarget = null;
            extendRequestMinDistance = 0;
            extendAbortTimer = cooldown ? -5f : 0f;
            lastExtendDistance = 0;
            extensionCutoffTimer = 0;
            extendForMissile = null;
            if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI]: {Time.time:F3} {vessel.vesselName} stopped extending due to {reason}.");
        }

        /// <summary>
        ///  Request extending away from a target position or vessel.
        ///  If a vessel is specified, it overrides the specified position.
        /// </summary>
        /// <param name="reason">Reason for extending</param>
        /// <param name="target">The target to extend from</param>
        /// <param name="minDistance">The minimum distance to extend for</param>
        /// <param name="tPosition">The position to extend from if the target is null</param>
        /// <param name="missile">The missile to fire if extending to fire a missile</param>
        /// <param name="ignoreCooldown">Override the cooldown period</param>
        public void RequestExtend(string reason = "requested", Vessel target = null, float minDistance = 0, Vector3 tPosition = default, MissileBase missile = null, bool ignoreCooldown = false)
        {
            if (ignoreCooldown) extendAbortTimer = 0f; // Disable the cooldown.
            else if (extendAbortTimer < 0) return; // Ignore request while in cooldown.
            requestedExtend = true;
            extendTarget = target;
            extendRequestMinDistance = minDistance;
            requestedExtendTpos = extendTarget != null ? target.CoM : tPosition;
            extendForMissile = missile;
            extendingReason = reason;
        }
        public void DebugExtending() // Debug being stuck in extending (enable DEBUG_AI, then click the "Debug Extending" button)
        {
            if (!extending) return;
            var extendVector = extendHorizontally ? (vessel.transform.position - lastExtendTargetPosition).ProjectOnPlanePreNormalized(upDirection) : vessel.transform.position - lastExtendTargetPosition;
            var message = $"{vessel.vesselName} is extending due to: {extendingReason}, extendTarget: {extendTarget}, distance: {extendVector.magnitude}m of {extendDistance}m {(extendHorizontally ? "horizontally" : "total")}";
            BDACompetitionMode.Instance.competitionStatus.Add(message);
            Debug.Log($"DEBUG EXTENDING {message}");
        }

        // Evading
        bool evading = false;
        bool wasEvading = false;
        public bool IsEvading => evading;

        float evasiveTimer;
        float threatRating;
        Vector3 threatRelativePosition;
        Vessel incomingMissileVessel;
        enum KinematicEvasionStates { None, ToTarget, Crank, Notch, TurnAway, NotchDive }
        KinematicEvasionStates kinematicEvasionState = KinematicEvasionStates.None;

        // Retreating
        bool retreating = false;
        #endregion

        #region Speed Controller / Steering / Altitude
        bool useAB = true;
        bool useBrakes = true;
        bool regainEnergy = false;

        bool maxAltitudeEnabled = false;
        bool belowMinAltitude; // True when below minAltitude or avoiding terrain.
        bool gainAltInhibited = false; // Inhibit gain altitude to minimum altitude when chasing or evading someone as long as we're pointing upwards.
        bool gainingAlt = false, wasGainingAlt = false; // Flags for tracking when we're gaining altitude.
        Vector3 gainAltSmoothedForwardPoint = default; // Smoothing for the terrain adjustments of gaining altitude.
        public bool IsBombing => isBombing; // Public flag for when on a bombing run.
        bool isBombing = false; // Flag for changing altitude behaviour when bombing.
        public bool DivebombStarted
        {
            get; private set
            {
                if (field != value)
                {
                    divebombCorrection.Reset(0);
                    bombingLateralCorrection.Reset(0);
                    DivebombProgress = 0;
                }
                field = value;
            }
        } = false;
        public float DivebombProgress { get; private set; } // How far through the divebomb we've progressed.
        public float DivebombReleaseMaxAltitude { get; private set; } // Don't release bombs too early when diving.
        public float DivebombReleaseMinSafeAltitude { get; private set; } // Below this we may trigger terrain avoidance (against ground targets).
        public PID divebombCorrection; // For fine-tuning the divebombing dive angle.
        [KSPField(isPersistant = true, guiActive = false)] public string divebombCorrectionPIDConfig = "";
        public PID bombingLateralCorrection; // For fine-tuning the side-to-side fly-to position when bombing or divebombing.
        [KSPField(isPersistant = true, guiActive = false)] public string bombingLateralCorrectionPIDConfig = "";

        Vector3 prevTargetDir;
        bool useVelRollTarget;
        float finalMaxSteer = 1;
        float userSteerLimit = 1;

        float targetStalenessTimer = 0;
        Vector3 staleTargetPosition = Vector3.zero;
        Vector3 staleTargetVelocity = Vector3.zero;
        #endregion

        #region Flat-spin / PSM Detection
        public float FlatSpin = 0; // 0 is not in FlatSpin, -1 is clockwise spin, 1 is counter-clockwise spin (set up this way instead of bool to allow future implementation for asymmetric thrust)
        float flatSpinStartTime = float.MaxValue;
        bool isPSM = false; // Is the plane doing post-stall manoeuvering? Note: this isn't really being used for anything other than debugging at the moment.
        bool invertRollTarget = false; // Invert the roll target under some conditions.
        #endregion

        #region Collision Detection (between vessels)
        //collision detection (for other vessels).
        const int vesselCollisionAvoidanceTickerFreq = 10; // Number of fixedDeltaTime steps between vessel-vessel collision checks.
        int collisionDetectionTicker = 0;
        Vector3 collisionAvoidDirection;
        public Vessel currentlyAvoidedVessel;
        #endregion

        #region Terrain Avoidance
        // Terrain avoidance and below minimum altitude globals.
        bool avoidingTerrain = false; // True when avoiding terrain.
        int terrainAlertTicker = 0; // A ticker to reduce the frequency of terrain alert checks.
        float terrainAlertDetectionRadius = 30.0f; // Sphere radius that the vessel occupies. Should cover most vessels. FIXME This could be based on the vessel's maximum width/height.
        float terrainAlertThreatRange; // The distance to the terrain to consider (based on turn radius).
        float terrainAlertThreshold; // The current threshold for triggering terrain avoidance based on various factors.
        float terrainAlertDistance; // Distance to the terrain (in the direction of the terrain normal).
        Vector3 terrainAlertNormal; // Approximate surface normal at the terrain intercept.
        Vector3 terrainAlertDirection; // Terrain slope in the direction of the velocity at the terrain intercept.
        Vector3 relativeVelocityRightDirection; // Right relative to current velocity and upDirection.
        Vector3 relativeVelocityDownDirection; // Down relative to current velocity and upDirection.
        Vector3 terrainAlertDebugPos, terrainAlertDebugDir; // Debug vector3's for drawing lines.
        Color terrainAlertNormalColour = Color.green; // Color of terrain alert normal indicator.
        List<Ray> terrainAlertDebugRays = []; // Adjusted normals of terrain alerts used to get the final terrain alert normal.
        RaycastHit[] terrainAvoidanceHits = new RaycastHit[10];
        float postTerrainAvoidanceCoolDownTimer = -1; // Timer to track how long since exiting terrain avoidance.
        #endregion

        #region Debug Lines
        LineRenderer lr;
        Vector3 debugTargetPosition;
        Vector3 debugTargetDirection;
        Vector3 flyingToPosition;
        Vector3 rollTarget;
#if DEBUG
        Vector3 debugSquigglySquidDirection;
#endif
        Vector3 angVelRollTarget;
        Vector3 debugBreakDirection = default;
        public Vector3 bombingTargetPrediction; // Override for the position of the bombing target in MissileFire for divebombing.
        #endregion

        #region Wing Command
        bool useFollowHints = false;
        float followHintDistance = 0, followHintThreshold = 500;
        float followSpeedI = 0, followSpeedD = 0;
        private Vector3d debugFollowPosition;
        #endregion

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

        public override bool CanEngage()
        {
            return !vessel.LandedOrSplashed;
        }
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
        protected void SetSliderPairClamps(string fieldNameMin, string fieldNameMax)
        {
            // Enforce min <= max for pairs of sliders
            UI_FloatRange field = (UI_FloatRange)(HighLogic.LoadedSceneIsFlight ? Fields[fieldNameMin].uiControlFlight : Fields[fieldNameMin].uiControlEditor);
            field.onFieldChanged = OnMinUpdated;
            field = (UI_FloatRange)(HighLogic.LoadedSceneIsFlight ? Fields[fieldNameMax].uiControlFlight : Fields[fieldNameMax].uiControlEditor);
            field.onFieldChanged = OnMaxUpdated;
        }

        public void OnMinUpdated(BaseField field = null, object obj = null)
        {
            if (turnRadiusTwiddleFactorMax < turnRadiusTwiddleFactorMin) { turnRadiusTwiddleFactorMax = turnRadiusTwiddleFactorMin; } // Enforce min < max for turn radius twiddle factor.
            // if (DynamicDampingMax < DynamicDampingMin) { DynamicDampingMax = DynamicDampingMin; } // Enforce min < max for dynamic steer damping.
            // if (DynamicDampingPitchMax < DynamicDampingPitchMin) { DynamicDampingPitchMax = DynamicDampingPitchMin; }
            // if (DynamicDampingYawMax < DynamicDampingYawMin) { DynamicDampingYawMax = DynamicDampingYawMin; }
            // if (DynamicDampingRollMax < DynamicDampingRollMin) { DynamicDampingRollMax = DynamicDampingRollMin; } // reversed roll dynamic damp behavior
        }

        public void OnMaxUpdated(BaseField field = null, object obj = null)
        {
            if (turnRadiusTwiddleFactorMin > turnRadiusTwiddleFactorMax) { turnRadiusTwiddleFactorMin = turnRadiusTwiddleFactorMax; } // Enforce min < max for turn radius twiddle factor.
            // if (DynamicDampingMin > DynamicDampingMax) { DynamicDampingMin = DynamicDampingMax; } // Enforce min < max for dynamic steer damping.
            // if (DynamicDampingPitchMin > DynamicDampingPitchMax) { DynamicDampingPitchMin = DynamicDampingPitchMax; }
            // if (DynamicDampingYawMin > DynamicDampingYawMax) { DynamicDampingYawMin = DynamicDampingYawMax; }
            // if (DynamicDampingRollMin > DynamicDampingRollMax) { DynamicDampingRollMin = DynamicDampingRollMax; } // reversed roll dynamic damp behavior
        }

        void SetFieldClamps()
        {
            var minAltField = (UI_FloatRange)Fields[nameof(minAltitude)].uiControlEditor;
            minAltField.onFieldChanged = ClampFields;
            minAltField = (UI_FloatRange)Fields[nameof(minAltitude)].uiControlFlight;
            minAltField.onFieldChanged = ClampFields;
            var defaultAltField = (UI_FloatRange)Fields[nameof(defaultAltitude)].uiControlEditor;
            defaultAltField.onFieldChanged = ClampFields;
            defaultAltField = (UI_FloatRange)Fields[nameof(defaultAltitude)].uiControlFlight;
            defaultAltField.onFieldChanged = ClampFields;
            var maxAltField = (UI_FloatRange)Fields[nameof(maxAltitude)].uiControlEditor;
            maxAltField.onFieldChanged = ClampFields;
            maxAltField = (UI_FloatRange)Fields[nameof(maxAltitude)].uiControlFlight;
            maxAltField.onFieldChanged = ClampFields;
            var autoTuningAltField = (UI_FloatRange)Fields[nameof(autoTuningAltitude)].uiControlFlight;
            autoTuningAltField.onFieldChanged = ClampFields;
            var autoTuningSpeedField = (UI_FloatRange)Fields[nameof(autoTuningSpeed)].uiControlFlight;
            autoTuningSpeedField.onFieldChanged = ClampFields;
        }

        void ClampFields(BaseField field, object obj)
        {
            ClampFields(field.name);
        }
        public void ClampFields(string fieldName)
        {
            switch (fieldName)
            {
                case "minAltitude":
                    if (defaultAltitude < minAltitude) { defaultAltitude = minAltitude; }
                    if (maxAltitude < minAltitude) { maxAltitude = minAltitude; }
                    UpdateTerrainAlertDetectionRadius(vessel);
                    break;
                case "defaultAltitude":
                    if (maxAltitude < defaultAltitude) { maxAltitude = defaultAltitude; }
                    if (minAltitude > defaultAltitude) { minAltitude = defaultAltitude; }
                    break;
                case "maxAltitude":
                    if (minAltitude > maxAltitude) { minAltitude = maxAltitude; }
                    if (defaultAltitude > maxAltitude) { defaultAltitude = maxAltitude; }
                    break;
                case "autoTuningAltitude":
                    autoTuningAltitude = Mathf.Clamp(autoTuningAltitude, 2f * minAltitude, maxAltitude - minAltitude); // Keep the auto-tuning altitude at least minAlt away from the min/max altitudes.
                    break;
                case "autoTuningSpeed":
                    autoTuningSpeed = Mathf.Clamp(autoTuningSpeed, minSpeed, maxSpeed); // Keep the auto-tuning speed within the combat speed range.
                    break;
                default:
                    Debug.LogError($"[BDArmory.BDModulePilotAI]: Invalid field name {fieldName} in ClampFields.");
                    break;
            }
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

        void SetOnMaxAltitudeChanged()
        {
            UI_Toggle field = (UI_Toggle)(HighLogic.LoadedSceneIsFlight ? Fields[nameof(maxAltitudeToggle)].uiControlFlight : Fields[nameof(maxAltitudeToggle)].uiControlEditor);
            field.onFieldChanged = ToggleMaxAltitude;
            ToggleMaxAltitude();
        }
        void ToggleMaxAltitude(BaseField field = null, object obj = null)
        {
            maxAltitudeEnabled = maxAltitudeToggle;
            var maxAltitudeField = Fields[nameof(maxAltitude)];
            maxAltitudeField.guiActive = maxAltitudeToggle;
            maxAltitudeField.guiActiveEditor = maxAltitudeToggle;
            if (!maxAltitudeToggle)
                StartCoroutine(FixAltitudesSectionLayout());
        }
        void SetMinCollisionAvoidanceLookAheadPeriod()
        {
            var minCollisionAvoidanceLookAheadPeriod = (UI_FloatRange)Fields[nameof(vesselCollisionAvoidanceLookAheadPeriod)].uiControlEditor;
            minCollisionAvoidanceLookAheadPeriod.minValue = vesselCollisionAvoidanceTickerFreq * Time.fixedDeltaTime;
            minCollisionAvoidanceLookAheadPeriod = (UI_FloatRange)Fields[nameof(vesselCollisionAvoidanceLookAheadPeriod)].uiControlFlight;
            minCollisionAvoidanceLookAheadPeriod.minValue = vesselCollisionAvoidanceTickerFreq * Time.fixedDeltaTime;
        }

        void SetOnExtendAngleA2AChanged()
        {
            UI_FloatRange field = (UI_FloatRange)Fields[nameof(extendAngleAirToAir)].uiControlEditor;
            field.onFieldChanged = OnExtendAngleA2AChanged;
            field = (UI_FloatRange)Fields[nameof(extendAngleAirToAir)].uiControlFlight;
            field.onFieldChanged = OnExtendAngleA2AChanged;
            OnExtendAngleA2AChanged();
        }
        void OnExtendAngleA2AChanged(BaseField field = null, object obj = null)
        {
            _extendAngleAirToAir = Mathf.Sin(extendAngleAirToAir * Mathf.Deg2Rad);
        }

        void SetOnTerrainAvoidanceCriticalAngleChanged()
        {
            UI_FloatRange field = (UI_FloatRange)Fields[nameof(terrainAvoidanceCriticalAngle)].uiControlEditor;
            field.onFieldChanged = OnTerrainAvoidanceCriticalAngleChanged;
            field = (UI_FloatRange)Fields[nameof(terrainAvoidanceCriticalAngle)].uiControlFlight;
            field.onFieldChanged = OnTerrainAvoidanceCriticalAngleChanged;
            OnTerrainAvoidanceCriticalAngleChanged();
        }
        public void OnTerrainAvoidanceCriticalAngleChanged(BaseField field = null, object obj = null)
        {
            terrainAvoidanceCriticalCosAngle = Mathf.Cos(terrainAvoidanceCriticalAngle * Mathf.Deg2Rad);
        }

        void SetOnImmelmannTurnAngleChanged()
        {
            var field = (UI_FloatRange)Fields[nameof(ImmelmannTurnAngle)].uiControlFlight;
            field.onFieldChanged = OnImmelmannTurnAngleChanged;
            OnImmelmannTurnAngleChanged();
        }
        void OnImmelmannTurnAngleChanged(BaseField field = null, object obj = null)
        {
            ImmelmannTurnCosAngle = -Mathf.Cos(ImmelmannTurnAngle * Mathf.Deg2Rad);
        }

        void SetOnBrakingPriorityChanged()
        {
            var field = (UI_FloatRange)Fields[nameof(brakingPriority)].uiControlFlight;
            field.onFieldChanged = OnBrakingPriorityChanged;
            OnBrakingPriorityChanged();
        }
        void OnBrakingPriorityChanged(BaseField field = null, object obj = null)
        {
            speedController.brakingPriority = brakingPriority / 100f;
        }

        void SetOnMaxSpeedChanged()
        {
            UI_FloatRange field = (UI_FloatRange)Fields[nameof(maxSpeed)].uiControlFlight;
            field.onFieldChanged = OnMaxSpeedChanged;
            OnMaxSpeedChanged();
        }
        public void OnMaxSpeedChanged(BaseField field = null, object obj = null)
        {
            BankedTurnDistance = Mathf.Clamp(8f * maxSpeed, 1000f, 4000f);
        }

        /// <summary>
        /// Upgrade the damping settings to two independent toggles for dynamic and 3-axis.
        /// This allows non-dynamic 3-axis damping.
        /// 
        /// Non-persistent KSPField get loaded from craft files, but not saved.
        /// </summary>
        void UpgradeDamping()
        {
            threeAxisSteerDamping |= dynamicSteerDamping && CustomDynamicAxisFields; // Enable 3-axis if it was enabled previously for 3-axis dynamic damping.
            if (dynamicSteerDamping && CustomDynamicAxisFields) // Upgrade non-dynamic axes in mixed setups to the static one, which was used before.
            {
                if (!dynamicDampingPitch) steerDampingPitch = steerDamping;
                if (!dynamicDampingYaw) steerDampingYaw = steerDamping;
                if (!dynamicDampingRoll) steerDampingRoll = steerDamping;
            }
        }
        void SetOnDampingTogglesChanged()
        {
            foreach (var fieldName in new List<string> { "dynamicSteerDamping", "threeAxisSteerDamping", "dynamicDampingPitch", "dynamicDampingRoll", "dynamicDampingYaw", "threeAxisPID" })
            {
                var field = (UI_Toggle)(HighLogic.LoadedSceneIsFlight ? Fields[fieldName].uiControlFlight : Fields[fieldName].uiControlEditor);
                field.onFieldChanged = OnPIDTogglesChanged;
            }
            OnPIDTogglesChanged();
        }
        public void OnPIDTogglesChanged(BaseField field = null, object obj = null)
        {
            ToggleDynamicPIDFields(); // Reconfigure the dynamic PID fields in the PAW.
            if (field != null) this.UpdateToggle(field, obj);
        }
        public void ToggleDynamicPIDFields()
        {
            if (HighLogic.LoadedSceneIsFlight && AutoTune) pidAutoTuning.RevertPIDValues(); // Revert to the PID values to best/base values.

            // Static damping
            {
                var field = Fields[nameof(steerDamping)];
                field.guiActive = field.guiActiveEditor = !dynamicSteerDamping && !threeAxisSteerDamping && !threeAxisPID;
            }

            // 3-axis static damping
            foreach (var fieldName in new List<string> { "steerDampingPitch", "steerDampingYaw", "steerDampingRoll" })
            {
                var field = Fields[fieldName];
                field.guiActive = field.guiActiveEditor = !threeAxisPID && threeAxisSteerDamping && (
                    !dynamicSteerDamping || // 3-axis static damping
                    dynamicSteerDamping && ( // 3-axis mixed damping
                        !dynamicDampingPitch && fieldName == "steerDampingPitch"
                        || !dynamicDampingYaw && fieldName == "steerDampingYaw"
                        || !dynamicDampingRoll && fieldName == "steerDampingRoll"
                    )
                );
            }

            // Dynamic damping
            foreach (var fieldName in new List<string> { "DynamicDampingLabel", "DynamicDampingMin", "DynamicDampingMax", "dynamicSteerDampingFactor" })
            {
                var field = Fields[fieldName];
                field.guiActive = field.guiActiveEditor = dynamicSteerDamping && !threeAxisSteerDamping && !threeAxisPID;
            }

            // 3-axis dynamic damping
            foreach (var axis in new List<string> { "Pitch", "Yaw", "Roll" })
            {
                var axisField = Fields[$"dynamicDamping{axis}"];
                axisField.guiActive = axisField.guiActiveEditor = dynamicSteerDamping && threeAxisSteerDamping && !threeAxisPID;
                var enabled = (bool)axisField.GetValue(this);
                foreach (var fieldName in new List<string> { $"{axis}Label", $"DynamicDamping{axis}Max", $"DynamicDamping{axis}Min", $"dynamicSteerDamping{axis}Factor" })
                {
                    var field = Fields[fieldName];
                    field.guiActive = field.guiActiveEditor = dynamicSteerDamping && threeAxisSteerDamping && !threeAxisPID && enabled;
                }
            }

            // Full 3-axis PID (overrides other modes)
            foreach (var fieldName in new List<string> { "dynamicSteerDamping", "threeAxisSteerDamping", "steerMult", "steerKiAdjust" })
            {
                var field = Fields[fieldName];
                field.guiActive = field.guiActiveEditor = !threeAxisPID;
            }
            foreach (var axis in new List<string> { "Pitch", "Yaw", "Roll" })
            {
                foreach (var pid in new List<string> { "Mult", "Ki", "Damping" })
                {
                    var field = Fields[$"threeAxisPID{axis}{pid}"];
                    field.guiActive = field.guiActiveEditor = threeAxisPID;
                }
            }

            dirtyPAW_PID = true;
            if (HighLogic.LoadedSceneIsFlight && AutoTune) pidAutoTuning.ResetGradient(); // Reset the auto-tuning if we were autotuning.
        }

        void SetOnAutoTuningRecenteringDistanceChanged()
        {
            UI_FloatRange field = (UI_FloatRange)(HighLogic.LoadedSceneIsFlight ? Fields[nameof(autoTuningRecenteringDistance)].uiControlFlight : Fields[nameof(autoTuningRecenteringDistance)].uiControlEditor);
            field.onFieldChanged = OnAutoTuningRecenteringDistanceChanged;
            OnAutoTuningRecenteringDistanceChanged();
        }
        void OnAutoTuningRecenteringDistanceChanged(BaseField field = null, object ob = null)
        {
            autoTuningRecenteringDistanceSqr = autoTuningRecenteringDistance * autoTuningRecenteringDistance * 1e6f;
        }

        IEnumerator FixAltitudesSectionLayout() // Fix the layout of the Altitudes section by briefly disabling the fields underneath the one that was removed.
        {
            var maxAltitudeToggleField = Fields[nameof(maxAltitudeToggle)];
            maxAltitudeToggleField.guiActive = false;
            maxAltitudeToggleField.guiActiveEditor = false;
            yield return null;
            maxAltitudeToggleField.guiActive = true;
            maxAltitudeToggleField.guiActiveEditor = true;
        }

        void SetupSliderResolution()
        {
            var sliderResolutionField = (UI_ChooseOption)(HighLogic.LoadedSceneIsFlight ? Fields[nameof(sliderResolution)].uiControlFlight : Fields[nameof(sliderResolution)].uiControlEditor);
            sliderResolutionField.onFieldChanged = OnSliderResolutionUpdated;
            OnSliderResolutionUpdated();
        }
        float sliderResolutionAsFloat(string res, float factor = 10f)
        {
            switch (res)
            {
                case "Low": return factor;
                case "High": return 1f / factor;
                case "Insane": return 1f / factor / factor;
                default: return 1f;
            }
        }
        void OnSliderResolutionUpdated(BaseField field = null, object obj = null)
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
                            var alsoMinValue = slider.minValue == slider.stepIncrement;
                            slider.stepIncrement *= factor;
                            slider.stepIncrement = BDAMath.RoundToUnit(slider.stepIncrement, slider.stepIncrement);
                            if (alsoMinValue) slider.minValue = slider.stepIncrement;
                        }
                    }
                    if (PIDField.group.name == "pilotAI_Altitudes")
                    {
                        var uiControl = HighLogic.LoadedSceneIsFlight ? PIDField.uiControlFlight : PIDField.uiControlEditor;
                        if (uiControl.GetType() == typeof(UI_FloatRange))
                        {
                            var slider = (UI_FloatRange)uiControl;
                            var alsoMinValue = slider.minValue == slider.stepIncrement;
                            slider.stepIncrement *= factor;
                            slider.stepIncrement = BDAMath.RoundToUnit(slider.stepIncrement, slider.stepIncrement);
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
                            }
                        }
                    }
                }
                previousSliderResolution = sliderResolution;
            }
        }

        void SetupAutoTuneSliders()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                UI_Toggle autoTuneToggle = (UI_Toggle)Fields[nameof(autoTune)].uiControlEditor;
                autoTuneToggle.onFieldChanged = OnAutoTuneChanged;
            }
            else if (HighLogic.LoadedSceneIsFlight)
            {
                pidAutoTuning = new PIDAutoTuning(this);
                UI_Toggle autoTuneToggle = (UI_Toggle)Fields[nameof(autoTune)].uiControlFlight;
                autoTuneToggle.onFieldChanged = OnAutoTuneChanged;
                foreach (var field in Fields)
                {
                    var fieldName = field.name;
                    if (!fieldName.StartsWith("autoTuningOption")) continue;
                    if (fieldName.StartsWith("autoTuningOptionFixed")) continue;
                    if (Fields.TryGetFieldUIControl<UI_Control>(fieldName, out UI_Control autoTuneField))
                    {
                        autoTuneField.onFieldChanged = OnAutoTuneOptionsChanged;
                    }
                }
            }
            SetAutoTuneFields();
        }
        void OnAutoTuneChanged(BaseField field = null, object obj = null)
        {
            if (HighLogic.LoadedSceneIsEditor) SetAutoTuneFields();
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (!autoTune)
            {
                pidAutoTuning.RevertPIDValues();
                StoreSettings(pidAutoTuning.vesselName); // Store the current settings for recall in the SPH.
            }
            pidAutoTuning.SetStartCoords();
            pidAutoTuning.ResetMeasurements();
            if (FlightInputHandler.fetch.precisionMode)
            {
                if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI]: Precision input mode is enabled, disabling it.");
                FlightInputHandler.fetch.precisionMode = false; // If precision control mode is enabled, disable it.
            }

            SetAutoTuneFields();
            MaintainFuelLevels(autoTune); // Prevent fuel drain while auto-tuning.
            DisableBattleDamage(autoTune); // Disable battle damage while auto-tuning.
            OtherUtils.SetTimeOverride(autoTune);
        }
        void SetAutoTuneFields()
        {
            if (!(HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)) return;
            if (HighLogic.LoadedSceneIsEditor)
            {
                foreach (var field in Fields)
                {
                    if (field.name.StartsWith("autoTuningOptionFixed")) continue;
                    if (field.name.StartsWith("autoTuning"))
                    {
                        field.guiActiveEditor = autoTune;
                    }
                }
            }
            else
            {
                foreach (var field in Fields)
                {
                    if (field.name.StartsWith("autoTuningOptionFixed")) continue;
                    if (field.name.StartsWith("autoTuning"))
                    {
                        field.guiActive = autoTune;
                    }
                }
            }
            dirtyPAW_PID = true;
        }
        void OnAutoTuneOptionsChanged(BaseField field = null, object obj = null)
        {
            if (!AutoTune || pidAutoTuning is null) return;
            pidAutoTuning.RevertPIDValues();
            pidAutoTuning.ResetMeasurements();
            pidAutoTuning.ResetGradient();
        }

        void SetOnUpToElevenChanged()
        {
            var field = (UI_Toggle)(HighLogic.LoadedSceneIsFlight ? Fields[nameof(upToEleven)].uiControlFlight : Fields[nameof(upToEleven)].uiControlEditor);
            field.onFieldChanged = TurnItUpToEleven; // Only triggered on UI interaction.
            if (upToEleven) TurnItUpToEleven(); // The initially loaded values are not the alternate ones.
        }

        bool fixFieldOrderingRunning = false;
        /// <summary>
        /// Fix the field ordering in the PAW due to setting fields active or inactive.
        /// </summary>
        /// <param name="groupName"></param>
        /// <param name="startFieldName"></param>
        IEnumerator FixFieldOrdering(string groupName, string startFieldName = null)
        {
            if (fixFieldOrderingRunning || !(HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)) yield break;
            fixFieldOrderingRunning = true;
            Dictionary<string, bool> fieldStates = new Dictionary<string, bool>();
            bool foundStartField = (startFieldName is null);
            foreach (var field in Fields)
            {
                if (field.group.name != groupName) continue;
                if (!foundStartField && field.name != startFieldName) continue;
                foundStartField = true;
                if (HighLogic.LoadedSceneIsEditor)
                {
                    fieldStates.Add(field.name, field.guiActiveEditor);
                    field.guiActiveEditor = false;
                }
                else
                {
                    fieldStates.Add(field.name, field.guiActive);
                    field.guiActive = false;
                }
            }
            yield return null;
            foreach (var field in Fields)
            {
                if (fieldStates.ContainsKey(field.name))
                {
                    if (HighLogic.LoadedSceneIsEditor)
                        field.guiActiveEditor = fieldStates[field.name];
                    else
                        field.guiActive = fieldStates[field.name];
                }
            }
            dirtyPAW_PID = false;
            fixFieldOrderingRunning = false;
        }

        void PAWFirstOpened(UIPartActionWindow paw, Part p) // Fix the ordering of fields when the PAW is first opened. This is required since KSP messes up the field ordering if the first KSPField is in a collapsed group.
        {
            if (p != part) return;
            dirtyPAW_PID = true;
            GameEvents.onPartActionUIShown.Remove(PAWFirstOpened);
        }
        #endregion

        protected override void Start()
        {
            base.Start();
            if (!(HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor)) return;

            if (HighLogic.LoadedSceneIsFlight)
            {
                maxAllowedSinAoA = (float)Math.Sin(maxAllowedAoA * Mathf.Deg2Rad);
                lastAllowedAoA = maxAllowedAoA;
                GameEvents.onVesselPartCountChanged.Add(UpdateTerrainAlertDetectionRadius);
                UpdateTerrainAlertDetectionRadius(vessel);
                dynDecayRate = Mathf.Exp(Mathf.Log(0.5f) * Time.fixedDeltaTime / 60f); // Decay rate for a half-life of 60s.
                dynVelSmoothingCoef = Mathf.Exp(Mathf.Log(0.5f) * Time.fixedDeltaTime); // Smoothing rate with a half-life of 1s.
                smoothedGLoad = new SmoothingF(Mathf.Exp(Mathf.Log(0.5f) * Time.fixedDeltaTime * 10f)); // Half-life of 0.1s.
                smoothedSinAoA = new SmoothingF(Mathf.Exp(Mathf.Log(0.5f) * Time.fixedDeltaTime * 10f)); // Half-life of 0.1s.
            }
            if ((divebombCorrection = PID.Deserialize(divebombCorrectionPIDConfig, overrides: new Dictionary<string, string>{
                {nameof(PID.SetPoint), $"{0}"},
                {nameof(PID.smoothInput), $"{true}"},
                {nameof(PID.smoothingHalflife), $"{0.1f}"},
                {nameof(PID.IntegralRescaling), $"{2f}"},
            })) == null)
            {
                divebombCorrection = new() { P = 5f, I = 0.5f, D = 0.5f, TimeStep = Time.fixedDeltaTime, ILimit = 1000f, smoothInput = true, smoothingHalflife = 0.1f, debug = false };
            }
            if ((bombingLateralCorrection = PID.Deserialize(bombingLateralCorrectionPIDConfig, overrides: new Dictionary<string, string>{
                {nameof(PID.SetPoint), $"{0}"},
                {nameof(PID.smoothInput), $"{true}"},
                {nameof(PID.smoothingHalflife), $"{0.1f}"},
                {nameof(PID.IntegralRescaling), $"{2f}"},
            })) == null)
            {
                bombingLateralCorrection = new() { P = 1f, I = 0.3f, D = 0.5f, TimeStep = Time.fixedDeltaTime, ILimit = 100f, smoothInput = true, smoothingHalflife = 0.1f, debug = false };
            }

            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 55)
            {
                maxBank = Mathf.Min(maxBank, 40);
                postStallAoA = 0.0f;
                maxSpeed = Mathf.Min(maxSpeed, 600);
                if (HighLogic.LoadedSceneIsFlight)
                {
                    UI_FloatRange bank = (UI_FloatRange)Fields[nameof(maxBank)].uiControlFlight;
                    bank.maxValue = 40;
                    UI_FloatRange spd = (UI_FloatRange)Fields[nameof(maxSpeed)].uiControlFlight;
                    spd.maxValue = 600;
                }
                else
                {
                    UI_FloatRange bank = (UI_FloatRange)Fields[nameof(maxBank)].uiControlEditor;
                    bank.maxValue = 40;
                    UI_FloatRange spd = (UI_FloatRange)Fields[nameof(maxSpeed)].uiControlEditor;
                    spd.maxValue = 600;
                }
                Fields[nameof(postStallAoA)].guiActiveEditor = false;
                Fields[nameof(postStallAoA)].guiActive = false;
            }
            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 60)
            {
                minAltitude = Mathf.Max(minAltitude, 750);
                UI_FloatRange minAlt = (UI_FloatRange)Fields[nameof(minAltitude)].uiControlFlight;
                minAlt.minValue = 750;
                defaultAltitude = BDArmorySettings.VESSEL_SPAWN_ALTITUDE;
                Fields[nameof(defaultAltitude)].guiActiveEditor = false;
                Fields[nameof(defaultAltitude)].guiActive = false;
                maxAllowedAoA = 2.5f;
                postStallAoA = 5;
                maxSpeed = Mathf.Min(250, maxSpeed);
                UI_FloatRange spd = (UI_FloatRange)Fields[nameof(maxSpeed)].uiControlFlight;
                spd.maxValue = 250;
                Fields[nameof(postStallAoA)].guiActiveEditor = false;
                Fields[nameof(postStallAoA)].guiActive = false;
                Fields[nameof(maxAllowedAoA)].guiActiveEditor = false;
                Fields[nameof(maxAllowedAoA)].guiActive = false;
            }
            SetupSliderResolution();
            SetSliderPairClamps("turnRadiusTwiddleFactorMin", "turnRadiusTwiddleFactorMax");
            // SetSliderClamps("DynamicDampingMin", "DynamicDampingMax");
            // SetSliderClamps("DynamicDampingPitchMin", "DynamicDampingPitchMax");
            // SetSliderClamps("DynamicDampingYawMin", "DynamicDampingYawMax");
            // SetSliderClamps("DynamicDampingRollMin", "DynamicDampingRollMax");
            SetFieldClamps();
            SetMinCollisionAvoidanceLookAheadPeriod();
            SetWaypointTerrainAvoidance();
            UpgradeDamping(); // Upgrade old configurations to the new independent dynamic and 3-axis toggles.
            SetOnDampingTogglesChanged();
            SetOnMaxAltitudeChanged();
            SetOnExtendAngleA2AChanged();
            SetOnTerrainAvoidanceCriticalAngleChanged();
            SetOnImmelmannTurnAngleChanged();
            SetOnMaxSpeedChanged();
            SetOnAutoTuningRecenteringDistanceChanged();
            SetupAutoTuneSliders();
            SetOnUpToElevenChanged();
            if ((HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor) && storedSettings != null && storedSettings.ContainsKey(HighLogic.LoadedSceneIsFlight ? vessel.GetName() : EditorLogic.fetch.ship.shipName))
            {
                Events[nameof(RestoreSettings)].active = true;
            }
            if (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor)
            {
                var vesselName = HighLogic.LoadedSceneIsFlight ? vessel.GetName() : EditorLogic.fetch.ship.shipName;
                if ((storedControlSurfaceSettings != null && storedControlSurfaceSettings.ContainsKey(vesselName)) || (storedFARControlSurfaceSettings != null && storedFARControlSurfaceSettings.ContainsKey(vesselName)))
                {
                    Events[nameof(RestoreControlSurfaceSettings)].active = true;
                }
            }
            GameEvents.onPartActionUIShown.Add(PAWFirstOpened);
            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onAboutToSaveShip.Add(OnAboutToSaveShip);
            }
        }

        void OnAboutToSaveShip(ShipConstruct ship)
        {
            divebombCorrectionPIDConfig = divebombCorrection.Serialize();
            bombingLateralCorrectionPIDConfig = bombingLateralCorrection.Serialize();
        }

        protected override void OnDestroy()
        {
            GameEvents.onPartActionUIShown.Remove(PAWFirstOpened);
            GameEvents.onVesselPartCountChanged.Remove(UpdateTerrainAlertDetectionRadius);
            GameEvents.onAboutToSaveShip.Remove(OnAboutToSaveShip);
            if (autoTune)
            {
                if (pidAutoTuning is not null) // If we were auto-tuning, revert to the best values and store them.
                {
                    pidAutoTuning.RevertPIDValues();
                    StoreSettings(pidAutoTuning.vesselName);
                }
                OtherUtils.SetTimeOverride(false); // Make sure we disable the Time Override if we were auto-tuning.
            }
            base.OnDestroy();
        }

        public override bool ActivatePilot()
        {
            // Note: this gets called whenever part counts change (in flight) due to ActiveController.UpdateModules.
            if (!base.ActivatePilot()) return false;
            originalMaxSpeed = maxSpeed;
            belowMinAltitude = vessel.LandedOrSplashed;
            prevTargetDir = vesselTransform.up;
            if (TakingOff && !vessel.LandedOrSplashed) // In case we activate pilot after taking off manually.
            {
                TakingOff = false;
            }

            SetOnBrakingPriorityChanged(); // Has to be set after the speed controller exists.

            bodyGravity = (float)PhysicsGlobals.GravitationalAcceleration * (float)vessel.orbit.referenceBody.GeeASL; // Set gravity for calculations;
            return true;
        }
        public override void DeactivatePilot()
        {
            base.DeactivatePilot();
            DivebombStarted = false;
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

            if (dirtyPAW_PID) StartCoroutine(FixFieldOrdering("pilotAI_PID"));
        }

        IEnumerator SetVar(string name, float value)
        {
            yield return null;
            typeof(BDModulePilotAI).GetField(name).SetValue(this, value);
        }

        void FixedUpdate()
        {
            //floating origin and velocity offloading corrections
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (BDKrakensbane.IsActive)
            {
                if (lastExtendTargetPosition != null) lastExtendTargetPosition -= BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
            }
            var weaponManager = WeaponManager;
            if (weaponManager && weaponManager.guardMode && weaponManager.currentTarget && weaponManager.staleTarget.ContainsKey(weaponManager.currentTarget.Vessel) && weaponManager.staleTarget[weaponManager.currentTarget.Vessel])
            {
                targetStalenessTimer += Time.fixedDeltaTime;
                if (targetStalenessTimer >= 1) //add some error to the predicted position every second
                {
                    /*
                    staleTargetPosition = new Vector3();
                    staleTargetPosition.x = UnityEngine.Random.Range(-(float)staleTargetVelocity.magnitude / 2, (float)staleTargetVelocity.magnitude / 2);
                    staleTargetPosition.y = UnityEngine.Random.Range(-(float)staleTargetVelocity.magnitude / 2, (float)staleTargetVelocity.magnitude / 2);
					staleTargetPosition.z = UnityEngine.Random.Range(-(float)staleTargetVelocity.magnitude / 2, (float)staleTargetVelocity.magnitude / 2);
                    */
                    staleTargetPosition = UnityEngine.Random.insideUnitSphere * staleTargetVelocity.magnitude / 2;
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
            // Reset and update various internal values and checks. Then update the pilot logic for the physics frame.

            //default brakes off full throttle
            //s.mainThrottle = 1;

            //vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, false);
            AdjustThrottle(maxSpeed, true);
            useAB = true;
            useBrakes = true;
            vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
            if (vessel.InNearVacuum())
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);
            }

            if (!ramming) steerMode = SteerModes.NormalFlight; // Reset the steer mode, unless we're ramming.
            useVelRollTarget = false;

            // landed and still, chill out
            var weaponManager = WeaponManager;
            if (vessel.LandedOrSplashed && standbyMode && weaponManager && (BDATargetManager.GetClosestTarget(weaponManager) == null || BDArmorySettings.PEACE_MODE))
            {
                standbyModeEnabled = true;
                //s.mainThrottle = 0;
                //vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                AdjustThrottle(0, true);
                return;
            }
            if (standbyModeEnabled && standbyMode) // Was in standby, but now there's something to engage, disable standby and engage.
            {
                CommandTakeOff();
                if (SpawnUtils.CountActiveEngines(vessel) == 0) // If no engines are active, trigger AG10 and then activate all engines if necessary.
                {
                    vessel.ActionGroups.ToggleGroup(BDACompetitionMode.KM_dictAG[10]);
                    if (SpawnUtils.CountActiveEngines(vessel) == 0)
                    {
                        SpawnUtils.ActivateAllEngines(vessel);
                    }
                }
            }

            upDirection = vessel.up;

            finalMaxSteer = 1f; // Reset finalMaxSteer, is adjusted in subsequent methods
            userSteerLimit = GetUserDefinedSteerLimit(); // Get the current user-defined steer limit.
            CalculateAccelerationAndTurningCircle();
            CheckFlatSpin();

            if ((float)vessel.radarAltitude < minAltitude)// && !isBombing) //TODO - refinement of torp bombing temporary minAlt ignore needed
            { belowMinAltitude = true; }

            if (gainAltInhibited && (!belowMinAltitude || !(isBombing || currentStatusMode == StatusMode.Engaging || currentStatusMode == StatusMode.Evading || currentStatusMode == StatusMode.RammingSpeed || currentStatusMode == StatusMode.GainingAltitude)))
            { // Allow switching between "Engaging", "Bombing", "Evading", "Ramming speed!" and "Gain Alt." while below minimum altitude without disabling the gain altitude inhibitor.
                gainAltInhibited = false;
                if (BDArmorySettings.DEBUG_AI) Debug.Log("[BDArmory.BDModulePilotAI]: " + vessel.vesselName + " is no longer inhibiting gain alt");
            }

            if (!gainAltInhibited && belowMinAltitude && (!hardMinAltitude && (isBombing || currentStatusMode == StatusMode.Engaging || currentStatusMode == StatusMode.Evading) || currentStatusMode == StatusMode.RammingSpeed) && !vessel.InNearVacuum())
            { // Vessel went below minimum altitude while "Engaging", "Bombing", "Evading" or "Ramming speed!", enable the gain altitude inhibitor.
                gainAltInhibited = true;
                if (BDArmorySettings.DEBUG_AI) Debug.Log("[BDArmory.BDModulePilotAI]: " + vessel.vesselName + " was " + currentStatus + " and went below min altitude, inhibiting gain alt.");
            }

            if ((vessel.srfSpeed < minSpeed) || (FlatSpin != 0))
            { regainEnergy = true; }
            else if (!belowMinAltitude && vessel.srfSpeed > Mathf.Min(minSpeed + 20f, idleSpeed))
            { regainEnergy = false; }

            UpdateVelocityRelativeDirections();
            CheckLandingGear();
            if (IsRunningWaypoints) UpdateWaypoint(); // Update the waypoint state.

            wasGainingAlt = gainingAlt; gainingAlt = false;
            if (!vessel.LandedOrSplashed && ((!(ramming && steerMode == SteerModes.Manoeuvering) && FlyAvoidTerrain(s)) || (!ramming && FlyAvoidOthers(s)))) // Avoid terrain and other planes, unless we're trying to ram stuff.
            { turningTimer = 0; }
            else if (TakingOff) // Take off.
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
                belowMinAltitude = false;
            }
            DivebombStarted &= isBombing && currentStatusMode == StatusMode.Engaging; // Clear the divebombStarted flag if we're no longer actively bombing.

            if (lastStatusMode != currentStatusMode)
            {
                if (BDArmorySettings.DEBUG_AI)
                {
                    Debug.Log("[BDArmory.BDModulePilotAI]: Status of " + vessel.vesselName + " changed from " + lastStatusMode + " to " + currentStatus);
                }
                bombingLateralCorrection.Reset(0); // Make sure the lateral bomb correction is reset.
                lastStatusMode = currentStatusMode;
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
            isBombing = false;
            if (extendAbortTimer < 0) // Extending is in cooldown.
            {
                extendAbortTimer += TimeWarp.fixedDeltaTime;
                if (extendAbortTimer > 0) extendAbortTimer = 0;
            }
            var weaponManager = WeaponManager;
            if (weaponManager != null)
            {
                bool evadeMissile = weaponManager.incomingMissileTime <= weaponManager.evadeThreshold;
                if (evadeMissile && evasionMissileKinematic && weaponManager.incomingMissileVessel) // Ignore missiles when they are post-thrust and we are turning back towards target
                {
                    MissileBase mb = VesselModuleRegistry.GetMissileBase(weaponManager.incomingMissileVessel);
                    if (mb != null)
                        evadeMissile = !(kinematicEvasionState == KinematicEvasionStates.ToTarget && incomingMissileVessel == weaponManager.incomingMissileVessel && mb.MissileState == MissileBase.MissileStates.PostThrust);
                }
                else
                    kinematicEvasionState = KinematicEvasionStates.None; // Reset missile kinematic evasion state

                if (evadeMissile)
                {
                    threatRating = -1f; // Allow entering evasion code if we're under missile fire
                    minimumEvasionTime = 0f; //  Trying to evade missile threats when they don't exist will result in NREs
                    incomingMissileVessel = weaponManager.incomingMissileVessel;
                }
                else if (weaponManager.underFire && !ramming) // If we're ramming, ignore gunfire.
                {
                    if (weaponManager.incomingMissTime >= evasionTimeThreshold && weaponManager.incomingThreatDistanceSqr >= evasionMinRangeThreshold * evasionMinRangeThreshold) // If we haven't been under fire long enough or they're too close, ignore gunfire
                    {
                        threatRating = weaponManager.incomingMissDistance;
                    }
                }
            }

            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Threat Rating: {threatRating:G3}");

            Vector3 vesselTransformPosition = vesselTransform.position;

            // If we're currently evading or a threat is significant.
            if ((evasiveTimer < minimumEvasionTime && evasiveTimer != 0) || threatRating < evasionThreshold)
            {
                if (evasiveTimer < minimumEvasionTime)
                {
                    threatRelativePosition = vessel.Velocity().normalized + vesselTransform.right;

                    if (weaponManager)
                    {
                        if (weaponManager.incomingMissileVessel)//switch to weaponManager.missileisIncoming?
                        {
                            threatRelativePosition = weaponManager.incomingThreatPosition - vesselTransformPosition;
                            if (extending)
                                StopExtending("missile threat"); // Don't keep trying to extend if under fire from missiles
                        }

                        if (weaponManager.underFire)
                        {
                            threatRelativePosition = weaponManager.incomingThreatPosition - vesselTransformPosition;
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
                // Check if we need to extend.
                if (!targetVessel.LandedOrSplashed && !(weaponManager.currentGun == null && weaponManager.selectedWeapon != null && weaponManager.selectedWeapon.GetWeaponClass() == WeaponClasses.Bomb)) // Extend for guns or bombs against airborne targets.
                {
                    Vector3 targetVesselRelPos = targetVessel.CoM - vesselTransformPosition;
                    if (canExtend && vessel.radarAltitude < defaultAltitude && VectorUtils.Angle(targetVesselRelPos, -upDirection) < 35) // Target is at a steep angle below us and we're below default altitude, extend to get a better angle instead of attacking now.
                    {
                        RequestExtend("too steeply below", targetVessel);
                    }

                    if (VectorUtils.Angle(targetVessel.CoM - vesselTransformPosition, vesselTransform.up) > 35) // If target is outside of 35° cone ahead of us then keep flying straight.
                    {
                        turningTimer += Time.fixedDeltaTime;
                    }
                    else
                    {
                        turningTimer = 0;
                    }

                    if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"turningTimer: {turningTimer}");

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
                    if (weaponManager.HasWeaponsAndAmmo()) // Let's shoot something.
                    {
                        ramming = false;
                        SetStatus("Engaging");
                        if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Flying to target " + targetVessel.vesselName);
                        FlyToTargetVessel(s, targetVessel);
                        return;
                    }
                    else if (RamTarget(s, targetVessel)) // If we're out of ammo, see if we can ram someone.
                    {
                        return;
                    }
                    // Otherwise, fall back to orbiting below.
                }
            }
            if (!extending)
            {
                // Under fire while orbiting => retreat away from the threat for a bit.
                if (BDArmorySettings.ALLOW_RETREAT_IF_ORBITING && weaponManager != null && (retreating || weaponManager.underFire && weaponManager.incomingThreatVessel != null && weaponManager.incomingMissDistance < 2 * vessel.GetRadius() + evasionThreshold))
                {
                    Vector3 flyTo;
                    if (weaponManager.incomingThreatVessel != null)
                    {
                        flyTo = vessel.CoM + (vessel.CoM - weaponManager.incomingThreatVessel.CoM).ProjectOnPlanePreNormalized(upDirection).normalized * retreatDistance; // Aim for the retreat distance.
                        flyTo += (float)(defaultAltitude - BodyUtils.GetRadarAltitudeAtPos(flyTo)) * upDirection; // Aim for the default altitude.
                        assignedPositionWorld = flyTo; // Update the orbiting position for once we've finished retreating.
                    }
                    else
                    {
                        flyTo = assignedPositionWorld;
                    }
                    var distanceSqr = (flyTo - vessel.CoM).sqrMagnitude;
                    if (distanceSqr > 9e6f) // But stop retreating once within 3km of the target position (orbit radius is 2km, so this ought to give a smoother transition).
                    {
                        retreating = true;
                        var retreatDistanceRemaining = BDAMath.Sqrt(distanceSqr) - 3000;
                        SetStatus($"Retreating {Mathf.Min(retreatDistanceRemaining, 1000):0}{(retreatDistanceRemaining >= 1000 ? "+" : "")}m");
                        AdjustThrottle(maxSpeed, false);
                        FlyToPosition(s, flyTo);
                        return;
                    }
                }
                retreating = false;
                if (ResumeCommand())
                {
                    UpdateCommand(s);
                    return;
                }
                SetStatus("Orbiting");
                FlyOrbit(s, assignedPositionGeo, 2000, idleSpeed, ClockwiseOrbit);
                return;
            }

            if (CheckExtend())
            {
                weaponManager.AdvanceScanTimer(); // Just speed up the next scan, don't force it every frame.
                FlyExtend(s, lastExtendTargetPosition);
                return;
            }
        }

        bool PredictCollisionWithVessel(Vessel v, float maxTime, out Vector3 badDirection, float ignoreMinDistanceSqr)
        {
            var weaponManager = WeaponManager;
            if (vessel == null || v == null || v == (weaponManager != null ? weaponManager.incomingMissileVessel : null)
                || v.GetTotalMass() < AvoidMass
                || (vessel.CoM - v.CoM).sqrMagnitude < ignoreMinDistanceSqr //something within vessel radius, no chance to evade at this point. Should cover parasite fighters detaching/debris stuck on something
                || (v.rootPart != null && v.rootPart.FindModuleImplementing<MissileBase>() != null)) //evasive will handle avoiding missiles
            {
                badDirection = Vector3.zero;
                return false;
            }
            // Adjust some values for asteroids.
            var targetRadius = v.GetRadius();
            var threshold = collisionAvoidanceThreshold + targetRadius; // Add the target's average radius to the threshold.
            if (v.vesselType == VesselType.SpaceObject) // Give asteroids some extra room.
            {
                maxTime += targetRadius / (float)vessel.srfSpeed * (turnRadiusTwiddleFactorMin + turnRadiusTwiddleFactorMax);
            }

            // Use the nearest time to closest point of approach to check separation instead of iteratively sampling. Should give faster, more accurate results.
            float timeToCPA = vessel.TimeToCPA(v, maxTime); // This uses the same kinematics as AIUtils.PredictPosition.
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
            if (BDArmorySettings.DISABLE_RAMMING || !allowRamming || (!allowRammingGroundTargets && v.LandedOrSplashed)) return false; // Override from BDArmory settings and local config.
            if (v == null) return false; // We don't have a target.
            if (Vector3.Dot(vessel.srf_vel_direction, v.srf_vel_direction) * (float)v.srfSpeed / (float)vessel.srfSpeed > 0.95f) return false; // We're not approaching them fast enough.
            float timeToCPA = vessel.TimeToCPA(v, 16f);

            // Set steer mode to manoeuvering for less than 8s left, we're trying to collide, not aim.
            if (timeToCPA < 8f)
                steerMode = SteerModes.Manoeuvering;
            else
                steerMode = SteerModes.NormalFlight;

            // Let's try to ram someone!
            if (!ramming)
                ramming = true;
            SetStatus("Ramming speed!");

            // If they're also in ramming speed and trying to ram us, then just aim straight for them until the last moment.
            var targetAI = v.ActiveController().PilotAI;
            if (timeToCPA > 1f && targetAI != null && targetAI.pilotEnabled && targetAI.ramming)
            {
                var targetWM = v.ActiveController().WM;
                if (targetWM != null && targetWM.currentTarget != null && targetWM.currentTarget.Vessel == vessel && Vector3.Dot(vessel.srf_vel_direction, v.srf_vel_direction) < -0.866f) // They're trying to ram us and are mostly head-on! Two can play at that game!
                {
                    FlyToPosition(s, AIUtils.PredictPosition(v.CoM, v.Velocity(), v.acceleration, TimeWarp.fixedDeltaTime)); // Ultimate Chicken!!!
                    AdjustThrottle(maxSpeed, false, true);
                    return true;
                }
            }

            // Ease in velocity from 16s to 8s, ease in acceleration from 8s to 2s using the logistic function to give smooth adjustments to target point.
            float easeAccel = Mathf.Clamp01(1.1f / (1f + Mathf.Exp(timeToCPA - 5f)) - 0.05f);
            float easeVel = Mathf.Clamp01(2f - timeToCPA / 8f);
            Vector3 predictedPosition = AIUtils.PredictPosition(v.CoM, v.Velocity() * easeVel, v.acceleration * easeAccel, timeToCPA + TimeWarp.fixedDeltaTime); // Compensate for the off-by-one frame issue.

            if (controlSurfaceLag > 0)
                predictedPosition += -1 * controlSurfaceLag * controlSurfaceLag * (timeToCPA / controlSurfaceLag - 1f + Mathf.Exp(-timeToCPA / controlSurfaceLag)) * vessel.acceleration * easeAccel; // Compensation for control surface lag.
            FlyToPosition(s, predictedPosition);
            AdjustThrottle(maxSpeed, false, true); // Ramming speed!

            return true;
        }
        void FlyToTargetVessel(FlightCtrlState s, Vessel v)
        {
            Vector3 target = AIUtils.PredictPosition(v, TimeWarp.fixedDeltaTime);
            MissileBase missile = null;
            Vector3 vesselPos = vesselTransform.position;
            Vector3 targetVesselPos = v.transform.position;
            Vector3 vectorToTarget = targetVesselPos - vesselPos;
            Vector3 vesselUp = vesselTransform.up;
            float distanceToTarget = vectorToTarget.magnitude;
            float planarDistanceToTarget = vectorToTarget.ProjectOnPlanePreNormalized(upDirection).magnitude;
            float angleToTarget = VectorUtils.Angle(target - vesselPos, vesselUp);
            float strafingDistance = -1f;
            float relativeVelocity = (float)(vessel.srf_velocity - v.srf_velocity).magnitude;
            float divebombSpeed = strafingSpeed;

            var weaponManager = WeaponManager;
            if (weaponManager && weaponManager.currentTarget != null && weaponManager.currentTarget.Vessel == v)
            { // If the WM's current target isn't v, then most of the rest of this doesn't make any sense.                
                if (weaponManager.staleTarget.ContainsKey(v) && !weaponManager.staleTarget[v]) staleTargetVelocity = Vector3.zero; //if actively tracking target, reset last known velocity vector
                missile = weaponManager.CurrentMissile;
                if (missile != null)
                {
                    switch (missile.GetWeaponClass())
                    {
                        case WeaponClasses.Missile:
                            {
                                if (distanceToTarget > 5500f) //why 5.5km?
                                {
                                    finalMaxSteer = GetSteerLimiterForSpeedAndPower();
                                }

                                if (missile.TargetingMode == MissileBase.TargetingModes.Heat && (!weaponManager.heatTarget.exists || (!missile.uncagedLock && missile.GuidanceMode != MissileBase.GuidanceModes.SLW)))
                                {
                                    if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Attempting heat lock");
                                    target += v.srf_velocity.normalized * 10; //TODO this should be based on heater boresight?
                                }
                                else
                                {
                                    target = MissileGuidance.GetAirToAirFireSolution(missile, v);
                                }
                                //Vector3 leadOffset = (missile.MissileReferenceTransform.position + (missile.MissileReferenceTransform.forward * distanceToTarget)) - (vesselTransform.position + (vesselTransform.up * distanceToTarget));
                                //target -= leadOffset; //correctly account for missiles mounted at an angle (important if heater to keep them pointed at heatsource and/or keep target within boresight)
                                if (!missile.isMMG) target = Quaternion.FromToRotation(missile.MissileReferenceTransform.forward, vesselUp) * (target - vesselPos) + vesselPos;
                                angleToTarget = VectorUtils.Angle(vesselUp, target - vesselPos);
                                if (angleToTarget < 20f)
                                {
                                    steerMode = SteerModes.Aiming;
                                }
                                break;
                            }
                        case WeaponClasses.SLW:
                            {
                                // FIXME I'm not sure how much of this is sensible. It's just the SLW stuff extracted from the bomb dropping section.
                                finalBombingAltitude = 200f; //drop to the deck for torpedo run // sources suggest torp drop height varies (based on torp) from ~15m to ~260m. 200 seems a decent mid ground.
                                // target = GetSurfacePosition(target); //set submerged targets to surface for future bombingAlt vectoring
                                if (v.altitude < 0) target -= (float)v.altitude * upDirection; // Submerged targets are targeted at the surface. Note: GetSurfacePosition ignores buildings and other structures.
                                if (distanceToTarget > Mathf.Max(4500f, extendDistanceBombing + ((float)vessel.horizontalSrfSpeed * BDAMath.Sqrt(2 * finalBombingAltitude / bodyGravity)))) //lead based on estimate of fall time at desired alt, regardless if we're there yet
                                {
                                    finalMaxSteer = GetSteerLimiterForSpeedAndPower();
                                    target += finalBombingAltitude * upDirection; //leisurely aim for target alt while still out of range, AI might be above coastline so don't go low early
                                }
                                else
                                {
                                    if (weaponManager.firedMissiles >= weaponManager.maxMissilesOnTarget) finalBombingAltitude = bombingAltitude; //have craft break off as soon as torps away so AI doesn't continue to fly towards enemy guns
                                    if (distanceToTarget < missile.engageRangeMax + relativeVelocity) // Distance until starting to strafe plus 1s for changing speed.
                                    {
                                        if (weaponManager.firedMissiles < weaponManager.maxMissilesOnTarget)
                                            strafingDistance = Mathf.Max(0f, distanceToTarget - missile.engageRangeMax); //slow to strafing speed so torps survive hitting the water
                                    }
                                    target = MissileGuidance.GetAirToAirFireSolution(missile, v);
                                    if (angleToTarget < 45f)
                                    {
                                        //get to target alt semi-aggressively. 0.75 is a bit too leisurely for torp bombing, but 0.85 seems to do reasonably well.
                                        target += 0.85f * (vessel.CoM - target).ProjectOnPlanePreNormalized(upDirection) + finalBombingAltitude * upDirection;
                                    }
                                    else
                                    {
                                        target += finalBombingAltitude * upDirection;
                                    }
                                }
                                break;
                            }
                        case WeaponClasses.Bomb:
                            {
                                float bombingAltOverTarget = v.LandedOrSplashed || divebombing ? bombingAltitude : 2f * missile.GetBlastRadius(); // get close for level bombing airships to try and ensure hits
                                if (distanceToTarget > Mathf.Max(4500f, extendDistanceBombing + ((float)vessel.horizontalSrfSpeed * BDAMath.Sqrt(2 * bombingAltOverTarget / bodyGravity)))) //lead based on estimate of fall time at desired alt, regardless if we're there yet
                                {
                                    finalMaxSteer = GetSteerLimiterForSpeedAndPower();
                                    if (v.altitude < 0) target -= (float)v.altitude * upDirection; // Submerged targets are targeted at the surface.
                                    target += 0.5f * (vesselPos - target).ProjectOnPlanePreNormalized(upDirection) + bombingAltOverTarget * upDirection; // Aim for the bombing altitude at half-way to the target
                                }
                                else
                                {
                                    (float dropTime, bombingTargetPrediction) = PredictBombingTarget(v);
                                    target = bombingTargetPrediction;
                                    if (v.altitude < 0) target -= (float)v.altitude * upDirection; // Submerged targets are targeted at the surface.
                                    if (divebombing)
                                    {
                                        Vector3 vectorToDiveTarget = target - vesselPos;
                                        float distanceToDiveTarget = vectorToDiveTarget.magnitude;
                                        float angleToDiveTarget = Mathf.Asin(-Vector3.Dot(vectorToDiveTarget, upDirection) / distanceToDiveTarget) * Mathf.Rad2Deg;
                                        DivebombStarted &= angleToDiveTarget > 0.5f * divebombingAngle; // Abort if the dive shallows out too much.

                                        if (DivebombStarted |= angleToDiveTarget > Mathf.Clamp(1f - 0.0005f * (float)(vessel.srfSpeed - v.srfSpeed), 0.9f, 1f) * divebombingAngle) // Start the dive a bit before the critical angle to allow the craft time to turn.
                                        {
                                            bombingAltOverTarget = minAltitude; // Aim almost directly at the target (we'll correct for the bomb aimer below).
                                        }
                                        target -= missile.MissileReferenceTransform.position - vessel.ReferenceTransform.position; // Account for bomb placement on vessel.
                                        target += bombingAltOverTarget * upDirection;
                                        if (DivebombStarted)
                                        {
                                            // Adjust target based on the bomb aimer's overshoot as the dive progresses, fully correcting by halfway through the dive.
                                            float targetAltitude = Mathf.Max(0, (float)v.altitude);
                                            DivebombProgress = Mathf.Clamp01(1f - ((float)vessel.altitude - targetAltitude) / bombingAltitude);
                                            divebombCorrection.Update(Mathf.Lerp(0, weaponManager.bombAimerOvershoot, 4f * DivebombProgress * DivebombProgress));
                                            target += divebombCorrection.Value * (bombingTargetPrediction - vessel.CoM).ProjectOnPlanePreNormalized(vessel.up).normalized;
                                            bombingLateralCorrection.Update(Mathf.Lerp(0, weaponManager.bombAimerLateralError, 4f * DivebombProgress * DivebombProgress));
                                            target += bombingLateralCorrection.Value * weaponManager.bombAimerLateralDirection;
                                            // Adjust speed to try to maintain a good angle to the target.
                                            divebombSpeed = Mathf.Clamp(strafingSpeed + (float)v.horizontalSrfSpeed + 0.5f * Vector3.Dot(v.Velocity() - vessel.Velocity(), vessel.srf_vel_direction), strafingSpeed, maxSpeed);
                                        }
                                        else if (angleToTarget < 90f)
                                        {
                                            target += 0.5f * (vessel.CoM - target).ProjectOnPlanePreNormalized(upDirection); // Aim to get the the target alt semi-agressively when approaching prior to the dive.
                                        }
                                        if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI)
                                        {
                                            if (DivebombStarted)
                                                debugString.AppendLine($"Divebombing: {DivebombProgress * 100:0.0}%, {divebombSpeed:0}m/s @ {angleToDiveTarget:0.0}°, drop time: {dropTime:0.00}s, overshoot: {weaponManager.bombAimerOvershoot:0}m ⇒ {divebombCorrection.Value:0}m{(divebombCorrection.debug ? $"\n- Overshoot: {divebombCorrection.DebugString}" : "")}");
                                            else
                                                debugString.AppendLine($"Approaching divebomb, angle: {angleToDiveTarget:0.0}°, drop time: {dropTime:0.00}s");
                                        }
                                    }
                                    else // level bombing
                                    {
                                        target -= missile.MissileReferenceTransform.position - vessel.ReferenceTransform.position; // Account for bomb placement on vessel.
                                        target += bombingAltOverTarget * upDirection;
                                        var targetAlignment = Mathf.Clamp01((target - vessel.CoM).DotNormalized(vessel.Velocity()));
                                        bombingLateralCorrection.Update(weaponManager.bombAimerLateralError * targetAlignment * targetAlignment);
                                        target += bombingLateralCorrection.Value * weaponManager.bombAimerLateralDirection;
                                        if (angleToTarget < 90f)
                                            target += 0.5f * (vessel.CoM - target).ProjectOnPlanePreNormalized(upDirection); // Aim to get the the target alt semi-agressively when approaching.
                                    }
                                    steerMode = SteerModes.Manoeuvering;
                                    isBombing = true;
                                }
                                target = vesselPos + GetLimitedClimbDirectionForSpeed(target - vesselPos); // Limit climb rate to avoid stalling.
                                finalBombingAltitude = Mathf.Max(0, (float)v.altitude) + bombingAltOverTarget;
                                DivebombReleaseMaxAltitude = finalBombingAltitude + terrainAlertThreatRange;
                                DivebombReleaseMinSafeAltitude = Mathf.Max(0, (float)v.altitude) + terrainAlertThreshold;
                                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Bombing altitude: {DivebombReleaseMinSafeAltitude:0}—{DivebombReleaseMaxAltitude:0}m vs {v.altitude:0}m, lateral error: {weaponManager.bombAimerLateralError:0}m ⇒ {bombingLateralCorrection.Value:0}m{(bombingLateralCorrection.debug && !string.IsNullOrEmpty(bombingLateralCorrection.DebugString) ? $"\n- Lateral: {bombingLateralCorrection.DebugString}" : "")}");
                                break;
                            }
                        default:
                            Debug.LogError($"[BDArmory.BDModulePilot]: Invalid weapon class for attacking target!");
                            break;
                    }
                }
                else if (weaponManager.currentGun)
                {
                    ModuleWeapon weapon = weaponManager.currentGun;
                    if (weapon != null)
                    {
                        Vector3 weaponPosition, weaponDirection;
                        if (weapon.turret && (weapon.yawRange > 0 || weapon.maxPitch > weapon.minPitch)) // Don't apply lead offset and weapon offsets for turrets.
                        {
                            weaponPosition = vesselPos;
                            weaponDirection = vesselUp;
                        }
                        else
                        {
                            Vector3 leadOffset = weapon.GetLeadOffset();
                            target -= leadOffset;  // Lead offset from aiming assuming the gun is forward aligned and centred.

                            // Note: depending on the airframe, there is an island of stability around -2°—30° in pitch and ±10° in yaw where the vessel can stably aim with offset weapons.
                            weaponPosition = weapon.offsetWeaponPosition + vesselPos;
                            weaponDirection = vessel.ReferenceTransform.TransformDirection(weapon.offsetWeaponDirection);

                            target = Quaternion.FromToRotation(weaponDirection, vesselUp) * (target - vesselPos) + vesselPos; // correctly account for angular offset guns/schrage Musik
                            var weaponOffset = vesselPos - weaponPosition;

                            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"WeaponOffset ({v.vesselName}): {weaponOffset.x}x m; {weaponOffset.y}y m; {weaponOffset.z}z m");
                            target += weaponOffset; //account for weapons with translational offset from longitudinal axis
                        }

                        angleToTarget = VectorUtils.Angle(weaponDirection, target - weaponPosition);
                        if (distanceToTarget < weaponManager.gunRange && angleToTarget < 20) // FIXME This ought to be changed to a dynamic angle like the firing angle.
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
                                    target += timeToTurn * v.Velocity();
                                    target += 0.5f * timeToTurn * timeToTurn * v.acceleration;
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
                                target = FlightPosition(target, Mathf.Min(defaultAltitude, weapon.engageRangeMax / 2f)); // Clamp target minAlt to give at most a 30° dive slope.
                            }
                            else
                            {
                                steerMode = SteerModes.Aiming;
                            }
                        }
                        else if (Vector3.Dot(target - weaponPosition, weaponDirection) < 0) //If a gun is selected, craft is probably already within gunrange, or a couple of seconds of being in gunrange
                        {
                            // Don't bother with the off-by-one physics frame correction as this doesn't need to be so accurate here.
                            target = Quaternion.FromToRotation(weaponDirection, vesselUp) * (targetVesselPos - vesselPos) + vesselPos;
                        }
                    }
                }
                else if (planarDistanceToTarget > weaponManager.gunRange * 1.25f && (vessel.altitude < v.altitude || (float)vessel.radarAltitude < defaultAltitude)) //climb to target vessel's altitude if lower and still too far for guns
                {
                    finalMaxSteer = GetSteerLimiterForSpeedAndPower();
                    if (v.LandedOrSplashed) vectorToTarget += upDirection * defaultAltitude; // If the target is landed or splashed, aim for the default altitude while we're outside our gun's range.
                    target = vesselPos + GetLimitedClimbDirectionForSpeed(vectorToTarget);
                }
                //change target offset if no selected weapon and at target alt? target += targetVelocity * closing time?
                else
                {
                    finalMaxSteer = GetSteerLimiterForSpeedAndPower();
                }
                if (weaponManager.staleTarget.ContainsKey(v) && weaponManager.staleTarget[v]) //lost track of target, but know it's in general area, simulate location estimate precision decay over time
                {
                    if (staleTargetVelocity == Vector3.zero) staleTargetVelocity = v.Velocity(); //if lost target, follow last known velocity vector
                    if (weaponManager.detectedTargetTimeout.TryGetValue(v, out float timeout))
                        target += staleTargetPosition + staleTargetVelocity * timeout;
                }
            }

            float targetDot = Vector3.Dot(vesselUp, targetVesselPos - vesselPos);

            //manage speed when close to enemy
            float finalMaxSpeed = maxSpeed;
            if (steerMode == SteerModes.Aiming) // Target is ahead and we're trying to aim at them. Outside this angle, we want full thrust to turn faster onto the target.
            {
                if (strafingDistance < 0f) // target flying, or beyond range of beginning strafing run for landed/splashed targets.
                {
                    if (distanceToTarget > vesselStandoffDistance) // Adjust target speed based on distance from desired stand-off distance.
                        finalMaxSpeed = (distanceToTarget - vesselStandoffDistance) / 8f + (float)v.srfSpeed; // Beyond stand-off distance, approach a little faster.
                    else
                    {
                        //Mathf.Max(finalMaxSpeed = (distanceToTarget - vesselStandoffDistance) / 8f + (float)v.srfSpeed, 0); //for less aggressive braking
                        finalMaxSpeed = distanceToTarget / vesselStandoffDistance * (float)v.srfSpeed; // Within stand-off distance, back off the thottle a bit.
                        if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Getting too close to Enemy. Braking!");
                    }
                }
                else
                {
                    finalMaxSpeed = strafingSpeed + (float)v.srfSpeed;
                }
            }
            else if (steerMode == SteerModes.Manoeuvering) // Bombing.
            {
                if (DivebombStarted)
                { // Slow down as we progress through the dive to give us more time to get on target.
                    finalMaxSpeed = Mathf.Lerp(maxSpeed, divebombSpeed, 4f * DivebombProgress);
                }
            }
            finalMaxSpeed = Mathf.Clamp(finalMaxSpeed, minSpeed, maxSpeed);
            AdjustThrottle(finalMaxSpeed, true);

            if ((targetDot < 0 && vessel.srfSpeed > finalMaxSpeed)
                && distanceToTarget < 300 && vessel.srfSpeed < v.srfSpeed * 1.25f && Vector3.Dot(vessel.Velocity(), v.Velocity()) > 0) //distance is less than 800m
            {
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Enemy on tail. Braking!");
                AdjustThrottle(minSpeed, true);
            }

            if (missile != null)
            {
                float boresightFactor = (vessel.LandedOrSplashed || v.LandedOrSplashed || missile.uncagedLock) ? 0.75f : 0.35f;
                float minOffBoresight = missile.maxOffBoresight * boresightFactor;
                float missileAngleToTarget = VectorUtils.Angle(missile.GetForwardTransform(), targetVesselPos - missile.transform.position);
                var minDynamicLaunchRange = MissileLaunchParams.GetDynamicLaunchParams(
                    missile,
                    v.Velocity(),
                    targetVesselPos,
                    // minOffBoresight + (180f - minOffBoresight) * Mathf.Clamp01(((missile.transform.position - v.transform.position).magnitude - missile.minStaticLaunchRange) / (Mathf.Max(100f + missile.minStaticLaunchRange * 1.5f, 0.1f * missile.maxStaticLaunchRange) - missile.minStaticLaunchRange)) // Reduce the effect of being off-target while extending to prevent super long extends.
                    // missileAngleToTarget <= minOffBoresight ? -1 : (missile.transform.position - v.transform.position).sqrMagnitude < (missile.minStaticLaunchRange * missile.minStaticLaunchRange) ? 180 : -1
                    missileAngleToTarget <= minOffBoresight ? -1 : minOffBoresight
                ).minLaunchRange;
                //all we should be concerned about here is: 
                //1) we're on target, but too close - extend back to min launch range, AI will handle coming about so by time it does so we should still be outside min range; all we need is missile absolute min distance
                //2) off target, and too close - extend to min launch range, ditto
                //3) on target, and beyond min range - no need to extend
                //4) off target, and beyond min launch range - are we within kinematic min range?
                //so: the FoV value should then be (missileAngleToTarget <= maxBoresight ? -1 : (missile.transform.position - v.transform.position).sqrmagnitude < (missile.minStaticLaunchRange * missile.minStaticLaunchRange) ? 180 : -1)

                if (canExtend && targetDot > 0 && distanceToTarget < minDynamicLaunchRange && vessel.srfSpeed > idleSpeed)
                {
                    RequestExtend($"too close for missile: {minDynamicLaunchRange}m", v, minDynamicLaunchRange, missile: missile); // Get far enough away to use the missile.
                }
            }

            if (regainEnergy && angleToTarget > 30f)
            {
                RegainEnergy(s, target - vesselPos);
                return;
            }
            else
            {
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"AngleToTarget ({v.vesselName}): {angleToTarget:0.00}° Dot: {Vector3.Dot((target - vesselPos).normalized, vesselUp):F6}");
                useVelRollTarget = true;
                FlyToPosition(s, target);
                return;
            }
        }

        (float, Vector3) PredictBombingTarget(Vessel v)
        {
            var weaponManager = WeaponManager;
            if (!weaponManager) return (0, v.CoM);
            float dropTime = weaponManager.bombAirTime; // Only valid for level-bombing ground targets.
            if (!v.LandedOrSplashed || divebombing)
            {
                // s = u*t + 1/2*a*t^2 => bodyGravity/2*t^2 - vel*t + dist = 0
                float dist = (float)(v.altitude - vessel.altitude);
                float vel = (divebombing ? -Mathf.Max(strafingSpeed, 0.7f * (float)vessel.srfSpeed) * Mathf.Sin(Mathf.Deg2Rad * divebombingAngle) : (float)vessel.verticalSpeed) - (float)v.verticalSpeed; // Use an estimate of the vertical dive speed for divebombing.
                float det = vel * vel - 2 * bodyGravity * dist;
                dropTime = det <= 0 ? 0 : (vel + BDAMath.Sqrt(det)) / bodyGravity; // Fall time for bomb to reach same altitude as target (and falling downwards).
                if (DivebombStarted) dropTime = Mathf.Lerp(dropTime, weaponManager.bombAirTime, Mathf.Clamp01(2f * DivebombProgress)); // Switch over to the WM's more accurate estimate as we progress through the dive.
            }
            return (dropTime, v.PredictPosition(dropTime, v.LandedOrSplashed ? AIUtils.UseAccel.None : AIUtils.UseAccel.Immediate)); // Ignore acceleration of ground targets due to noise and weave distractions.
        }

        void RegainEnergy(FlightCtrlState s, Vector3 direction, float throttleOverride = -1f)
        {
            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Regaining energy");

            steerMode = SteerModes.Aiming; // Just point the plane in the direction we want to go to minimise drag.
            Vector3 planarDirection = direction.ProjectOnPlanePreNormalized(upDirection);
            float angle = Mathf.Clamp((float)vessel.radarAltitude - minAltitude, 0, 1500) / 1500 * 90;
            angle = Mathf.Clamp(angle, 0, 55) * Mathf.Deg2Rad;

            Vector3 targetDirection = Vector3.RotateTowards(planarDirection, -upDirection, angle, 0);
            targetDirection = Vector3.RotateTowards(vessel.Velocity(), targetDirection, 15f * Mathf.Deg2Rad, 0).normalized;

            throttleOverride = (FlatSpin == 0) ? throttleOverride : 0f;

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

            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"possibleAccel: {possibleAccel}");

            float limiter = ((speed - minSpeed) / 2 / minSpeed) + possibleAccel / 15f; // FIXME The calculation for possibleAccel needs further investigation.
            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"unclamped limiter: {limiter}");

            return Mathf.Clamp01(limiter);
        }

        float GetUserDefinedSteerLimit()
        {
            float limiter = 1;
            if (maxSteer > maxSteerAtMaxSpeed)
                limiter *= Mathf.Clamp((maxSteerAtMaxSpeed - maxSteer) / (cornerSpeed - lowSpeedSwitch + 0.001f) * ((float)vessel.srfSpeed - lowSpeedSwitch) + maxSteer, maxSteerAtMaxSpeed, maxSteer); // Linearly varies between two limits, clamped at limit values
            else
                limiter *= Mathf.Clamp((maxSteerAtMaxSpeed - maxSteer) / (cornerSpeed - lowSpeedSwitch + 0.001f) * ((float)vessel.srfSpeed - lowSpeedSwitch) + maxSteer, maxSteer, maxSteerAtMaxSpeed); // Linearly varies between two limits, clamped at limit values
            if (altitudeSteerLimiterFactor != 0 && vessel.altitude > altitudeSteerLimiterAltitude)
                limiter *= Mathf.Pow((float)vessel.altitude / altitudeSteerLimiterAltitude, altitudeSteerLimiterFactor); // Scale based on altitude relative to the user-defined limit.
            limiter *= 1.225f / Mathf.Clamp((float)vessel.atmDensity, 0, 1.225f); // Scale based on atmospheric density relative to sea level Kerbin (since dynamic pressure depends on density)

            return Mathf.Clamp01(limiter);
        }

        /// <summary>
        /// Fly in the direction of the specified position.
        /// </summary>
        /// <param name="s">The flight state.</param>
        /// <param name="targetPosition">The position to fly to.</param>
        /// <param name="overrideThrottle"></param>
        /// <param name="allowCorrections">Allow situational corrections to targetPosition.</param>
        void FlyToPosition(FlightCtrlState s, Vector3 targetPosition, bool overrideThrottle = false, bool allowCorrections = true)
        {
            var weaponManager = WeaponManager;
            Vector3 vesselPos = vesselTransform.position;
            Vector3 vesselForward = vesselTransform.forward;
            Vector3 vesselUp = vesselTransform.up;
            Vector3 vesselRight = vesselTransform.right;

            //test poststall (before FlightPosition is called so we're using the right steerMode)
            float AoA = VectorUtils.Angle(vessel.ReferenceTransform.up, vessel.Velocity());
            if (AoA > postStallAoA)
            {
                isPSM = true;
                steerMode = SteerModes.Aiming; // Too far off-axis for the velocity direction to be relevant.
            }
            else
            {
                isPSM = false;
            }
            Vector3 targetDirection = (targetPosition - vesselPos).normalized;
            if (AutoTune && (Vector3.Dot(targetDirection, vesselUp) > 0.9397f)) // <20°
            {
                steerMode = SteerModes.Aiming; // Pretend to aim when on target.
            }

            if (allowCorrections && !belowMinAltitude && command != PilotCommands.Follow) // Includes avoidingTerrain
            {
                targetPosition = LongRangeAltitudeCorrection(targetPosition); //have this only trigger in atmo?
                targetPosition = FlightPosition(targetPosition, isBombing ? Mathf.Min(finalBombingAltitude, minAltitude) : minAltitude);
                targetDirection = (targetPosition - vesselPos).normalized;
                targetPosition = vesselPos + 100 * targetDirection;
            }

            Vector3 srfVel = vessel.Velocity();
            if (srfVel.sqrMagnitude > Vector3.kEpsilon) // vel < 3mm/s
            {
                velocityTransform.rotation = Quaternion.LookRotation(srfVel, -vesselForward);
            }
            velocityTransform.rotation = Quaternion.AngleAxis(90, velocityTransform.right) * velocityTransform.rotation;

            //ang vel
            Vector3 localAngVel = vessel.angularVelocity;
            //test
            Vector3 currTargetDir = targetDirection;
            if (allowCorrections && evasionNonlinearity > 0 && (IsExtending || IsEvading || // If we're extending or evading, add a deviation to the fly-to direction to make us harder to hit.
                weaponManager && (((steerMode == SteerModes.NormalFlight || steerMode == SteerModes.Aiming && weaponManager.CurrentMissile != null) || IsRunningWaypoints) && weaponManager.guardMode && // Also, if we know enemies are near, but they're beyond gun or visual range and we're not aiming a gun, or we're running a WP course and standard evasion isn't ideal
                    BDATargetManager.TargetList(weaponManager.Team).Where(target =>
                        !target.isMissile &&
                        weaponManager.CanSeeTarget(target, true, true) >= MissileFire.TargetVisibility.RecentlyVisible
                    ).AllAndNotEmpty(target =>
                        (target.Vessel.CoM - vesselPos).sqrMagnitude > weaponManager.maxVisualGunRangeSqr
                    ))))
            {
                var squigglySquidTime = 90f * (float)vessel.missionTime + 8f * Mathf.Sin((float)vessel.missionTime * 6.28f) + 16f * Mathf.Sin((float)vessel.missionTime * 3.14f); // Vary the rate around 90°/s to be more unpredictable.
                var squigglySquidDirection = Quaternion.AngleAxis(evasionNonlinearityDirection * squigglySquidTime, targetDirection) * upDirection.ProjectOnPlanePreNormalized(targetDirection).normalized;
#if DEBUG
                debugSquigglySquidDirection = squigglySquidDirection;
#endif
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Squiggly Squid: {VectorUtils.Angle(targetDirection, Vector3.RotateTowards(targetDirection, squigglySquidDirection, evasionNonlinearity * Mathf.Deg2Rad, 0f)):G3}° at {squigglySquidTime % 360f:G3}°");
                targetDirection = Vector3.RotateTowards(targetDirection, squigglySquidDirection, evasionNonlinearity * Mathf.Deg2Rad, 0f);
            }
            Vector3 targetAngVel = Vector3.Cross(prevTargetDir, targetDirection) / Time.fixedDeltaTime;
            Vector3 localTargetAngVel = vesselTransform.InverseTransformVector(targetAngVel);
            prevTargetDir = targetDirection;
            targetPosition = vesselPos + 100 * targetDirection;
            flyingToPosition = targetPosition;
            float angleToTarget = VectorUtils.Angle(targetDirection, vesselUp);

            //slow down for tighter turns, unless we're already at high AoA, in which case we want more thrust
            float speedReductionFactor = 1.25f;
            float finalSpeed;
            // float velAngleToTarget = Mathf.Clamp(VectorUtils.Angle(targetDirection, vessel.Velocity()), 0, 90);
            // if (vessel.atmDensity > 0.05f) finalSpeed = Mathf.Min(speedController.targetSpeed, Mathf.Clamp(maxSpeed - (speedReductionFactor * velAngleToTarget), idleSpeed, maxSpeed));
            if (!vessel.InNearVacuum()) finalSpeed = Mathf.Min(speedController.targetSpeed, Mathf.Clamp(maxSpeed - speedReductionFactor * (angleToTarget - AoA), idleSpeed, maxSpeed));
            else finalSpeed = Mathf.Min(speedController.targetSpeed, maxSpeed);
            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Final Target Speed: {finalSpeed:0}m/s");

            if (!overrideThrottle)
            {
                AdjustThrottle(finalSpeed, useBrakes, useAB);
            }

            if (steerMode == SteerModes.Aiming)
            {
                localAngVel -= localTargetAngVel;
            }

            Vector3 localTargetDirection;
            Vector3 localTargetDirectionYaw;
            if (steerMode == SteerModes.NormalFlight || steerMode == SteerModes.Manoeuvering)
            {
                localTargetDirection = velocityTransform.InverseTransformDirection(targetPosition - velocityTransform.position).normalized;
                localTargetDirection = Vector3.RotateTowards(Vector3.up, localTargetDirection, 45 * Mathf.Deg2Rad, 0);

                if (useWaypointYawAuthority && IsRunningWaypoints)
                {
                    var refYawDir = Vector3.RotateTowards(Vector3.up, vesselTransform.InverseTransformDirection(targetDirection), 25 * Mathf.Deg2Rad, 0).normalized;
                    var velYawDir = Vector3.RotateTowards(Vector3.up, vesselTransform.InverseTransformDirection(vessel.Velocity()), 45 * Mathf.Deg2Rad, 0).normalized;
                    localTargetDirectionYaw = waypointYawAuthorityStrength * refYawDir + (1f - waypointYawAuthorityStrength) * velYawDir;
                }
                else
                {
                    localTargetDirectionYaw = vesselTransform.InverseTransformDirection(vessel.Velocity()).normalized;
                    localTargetDirectionYaw = Vector3.RotateTowards(Vector3.up, localTargetDirectionYaw, 45 * Mathf.Deg2Rad, 0);
                }
            }
            else//(steerMode == SteerModes.Aiming)
            {
                localTargetDirection = vesselTransform.InverseTransformDirection(targetDirection).normalized;
                localTargetDirection = Vector3.RotateTowards(Vector3.up, localTargetDirection, 25 * Mathf.Deg2Rad, 0);
                localTargetDirectionYaw = localTargetDirection;
            }

            //// Adjust targetDirection based on ATTITUDE limits
            // var horizonUp = vesselTransform.up.ProjectOnPlanePreNormalized(upDirection).normalized;
            //var horizonRight = -Vector3.Cross(horizonUp, upDirection);
            //float attitude = Vector3.SignedAngle(horizonUp, vesselTransform.up, horizonRight);
            //if ((Mathf.Abs(attitude) > maxAttitude) && (maxAttitude != 90f))
            //{
            //    var projectPlane = Vector3.RotateTowards(upDirection, horizonUp, attitude * Mathf.PI / 180f, 0f);
            //    targetDirection = targetDirection.ProjectOnPlanePreNormalized(projectPlane);
            //}
            //debugString.AppendLine($"Attitude: " + attitude);

            // User-set steer limits
            finalMaxSteer *= userSteerLimit;
            finalMaxSteer = Mathf.Clamp(finalMaxSteer, 0.1f, 1f); // added just in case to ensure some input is retained no matter what happens

            //roll
            Vector3 currentRoll = -vesselForward;
            float rollUp = steerMode == SteerModes.NormalFlight ? 10f : 5f; // Reduced roll-up for Aiming and Manoeuvering.
            if (steerMode == SteerModes.NormalFlight)
            {
                rollUp += (1 - finalMaxSteer) * 10f;
            }
            rollTarget = targetPosition - vesselPos + rollUp * upDirection;
            if (DivebombStarted)
            {
                var factor = VectorUtils.Angle(vessel.srf_vel_direction, targetPosition - vesselPos) / 90f; // Prevent rolling when far off-target.
                if (factor > 0)
                {
                    // Try hard not to roll while dive-bombing initially, just dive with pitch down (yaw should already be mostly aligned).
                    rollTarget = Vector3.RotateTowards(rollTarget, upDirection, factor * VectorUtils.Angle(rollTarget, upDirection) * Mathf.Deg2Rad, 0);
                }
            }

            //test
            if (steerMode == SteerModes.Aiming && !belowMinAltitude && !invertRollTarget)
            {
                angVelRollTarget = -140 * vesselTransform.TransformVector(Quaternion.AngleAxis(90f, Vector3.up) * localTargetAngVel);
                rollTarget += angVelRollTarget;
            }

            if (command == PilotCommands.Follow && useFollowHints)
            {
                rollTarget *= Mathf.Clamp(followHintDistance / followHintThreshold, 0.2f, 1f); // Reduce our own rollTarget requirements as we get close to the formation position.
                rollTarget += Mathf.Clamp01(1 - followHintDistance / followHintThreshold) * rollUp * -commandLeader.vessel.ReferenceTransform.forward; // Use stronger hint from leader as we get closer to being in position.
            }

            if (invertRollTarget) rollTarget = -rollTarget;

            bool requiresLowAltitudeRollTargetCorrection = false;
            if (avoidingTerrain || postTerrainAvoidanceCoolDownTimer >= 0)
            {
                rollTarget = terrainAlertNormal * 100;
                var terrainAvoidanceRollCosAngle = Vector3.Dot(-vesselForward, terrainAlertNormal.ProjectOnPlanePreNormalized(vesselUp).normalized);
                if (terrainAvoidanceRollCosAngle < terrainAvoidanceCriticalCosAngle)
                {
                    if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Inverting rollTarget: {rollTarget}, cosAngle: {terrainAvoidanceRollCosAngle} vs {terrainAvoidanceCriticalCosAngle}, isPSM: {isPSM}");
                    rollTarget = -rollTarget; // Avoid terrain fully inverted if the plane is mostly inverted (>30°) to begin with.
                }
                if (postTerrainAvoidanceCoolDownTimer >= 0 && postTerrainAvoidanceCoolDownDuration > 0)
                {
                    localTargetDirection = Vector3.RotateTowards(localTargetDirection, Vector3.forward, (terrainAvoidanceRollCosAngle < terrainAvoidanceCriticalCosAngle ? 30f : -30f) * Mathf.Deg2Rad * Mathf.Clamp01(1f - postTerrainAvoidanceCoolDownTimer / postTerrainAvoidanceCoolDownDuration), 0);
                }
            }
            else if (belowMinAltitude && !gainAltInhibited)
            {
                rollTarget = Vector3.Lerp(BodyUtils.GetSurfaceNormal(vesselPos), upDirection, (float)vessel.radarAltitude / minAltitude) * 100; // Adjust the roll target smoothly from the surface normal to upwards to avoid clipping wings into terrain on take-off.
            }
            else if (!avoidingTerrain && Vector3.Dot(rollTarget, upDirection) < 0 && Vector3.Dot(rollTarget, vessel.Velocity()) < 0) // If we're not avoiding terrain and the roll target is behind us and downwards, check that a circle arc of radius "turn radius" (scaled by twiddle factor maximum) tilted at angle of rollTarget has enough room to avoid hitting the ground.
            {
                if (belowMinAltitude) // Never do inverted loops below min altitude.
                { requiresLowAltitudeRollTargetCorrection = true; }
                else // Otherwise, check the turning circle.
                {
                    // The following calculates the altitude required to turn in the direction of the rollTarget based on the current velocity and turn radius.
                    // The setup is a circle in the plane of the rollTarget, which is tilted by angle phi from vertical, with the vessel at the point subtending an angle theta as measured from the top of the circle.
                    var n = Vector3.Cross(vessel.srf_vel_direction, rollTarget).normalized; // Normal of the plane of rollTarget.
                    var m = Vector3.Cross(n, upDirection).normalized; // cos(theta) = dot(m,v).
                    if (m.magnitude < 0.1f) m = upDirection; // In case n and upDirection are colinear.
                    var a = Vector3.Dot(n, upDirection); // sin(phi) = dot(n,up)
                    var b = BDAMath.Sqrt(1f - a * a); // cos(phi) = sqrt(1-sin(phi)^2)
                    var r = turnRadiusTwiddleFactorMax * turnRadius; // Worst-case radius of turning circle.

                    var h = r * (1 + Vector3.Dot(m, vessel.srf_vel_direction)) * b; // Required altitude: h = r * (1+cos(theta)) * cos(phi).
                    if (vessel.radarAltitude + Vector3.Dot(vessel.srf_velocity, upDirection) * controlSurfaceDeploymentTime < h) // Too low for this manoeuvre.
                    {
                        requiresLowAltitudeRollTargetCorrection = true; // For simplicity, we'll apply the correction after the projections have occurred.
                    }
                    if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Low-alt loop: {requiresLowAltitudeRollTargetCorrection:G4}: {vessel.radarAltitude:G4} < {h:G4}, r: {r}");
                }
            }

            // rollTarget overrides
            if (ramming)
            {
                rollTarget = (targetPosition - vesselPos + rollUp * Mathf.Clamp((targetPosition - vesselPos).magnitude / 500f, 0f, 1f) * upDirection).ProjectOnPlanePreNormalized(vesselUp);
            }
            else if (useWaypointRollTarget && IsRunningWaypoints)
            {
                var angle = waypointRollTargetStrength * VectorUtils.Angle(waypointRollTarget, rollTarget);
                rollTarget = Vector3.RotateTowards(rollTarget, waypointRollTarget, angle * Mathf.Deg2Rad, 0f).ProjectOnPlane(vessel.Velocity());
            }
            else if (useVelRollTarget && !belowMinAltitude)
            {
                Vector3 normVel = vessel.Velocity().normalized;
                rollTarget = rollTarget.ProjectOnPlanePreNormalized(normVel);
                currentRoll = currentRoll.ProjectOnPlanePreNormalized(normVel);
            }
            else
            {
                rollTarget = rollTarget.ProjectOnPlanePreNormalized(vesselUp);
            }

            if (requiresLowAltitudeRollTargetCorrection) // Low altitude downwards loop prevention to avoid triggering terrain avoidance.
            {
                // Set the roll target to be horizontal.
                rollTarget = 100 * rollTarget.ProjectOnPlanePreNormalized(upDirection).normalized;
            }

            // Limit Bank Angle, this should probably be re-worked using quaternions or something like that, SignedAngle doesn't work well for angles > 90
            Vector3 horizonNormal = (vesselPos - vessel.mainBody.transform.position).ProjectOnPlanePreNormalized(vesselUp);
            float bankAngle = Vector3.SignedAngle(horizonNormal, rollTarget, vesselUp);
            if ((Mathf.Abs(bankAngle) > maxBank) && (maxBank != 180))
                rollTarget = Vector3.RotateTowards(horizonNormal, rollTarget, maxBank / 180 * Mathf.PI, 0.0f);
            bankAngle = Vector3.SignedAngle(horizonNormal, rollTarget, vesselUp);

            float pitchError = VectorUtils.GetAngleOnPlane(localTargetDirection, Vector3.up, Vector3.back);
            float yawError = VectorUtils.GetAngleOnPlane(localTargetDirectionYaw, Vector3.up, Vector3.right);
            float rollError;
            if (useVelRollTarget)
                rollError = VectorUtils.SignedAngle(currentRoll, rollTarget, vesselRight);
            else
                rollError = VectorUtils.GetAngleOnPlane(rollTarget, currentRoll, vesselRight);

            if (BDArmorySettings.DEBUG_LINES)
            {
                debugTargetPosition = vesselPos + targetDirection * 1000; // The asked for target position's direction
                debugTargetDirection = vesselPos + vesselTransform.TransformDirection(localTargetDirection) * 200; // The actual direction to match the "up" direction of the craft with for pitch (used for PID calculations).
            }

            #region PID calculations
            // FIXME Why are there various constants in here that mess with the scaling of the PID in the various axes? Ratios between the axes are 1:0.33:0.1
            float pitchProportional = 0.015f * SteerPower(Axis.Pitch) * pitchError;
            float yawProportional = 0.005f * SteerPower(Axis.Yaw) * yawError;
            float rollProportional = 0.0015f * SteerPower(Axis.Roll) * rollError;

            float pitchDamping = SteerDamping(Mathf.Abs(angleToTarget), angleToTarget, Axis.Pitch) * -localAngVel.x;
            float yawDamping = 0.33f * SteerDamping(Mathf.Abs(yawError * (steerMode == SteerModes.Aiming ? (180f / 25f) : 4f)), angleToTarget, Axis.Yaw) * -localAngVel.z;
            float rollDamping = 0.1f * SteerDamping(Mathf.Abs(rollError), angleToTarget, Axis.Roll) * -localAngVel.y;

            // For the integral, we track the vector of the pitch and yaw in the 2D plane of the vessel's forward pointing vector so that the pitch and yaw components translate between the axes when the vessel rolls.
            directionIntegral = (directionIntegral + (pitchError * -vesselForward + yawError * vesselRight) * Time.fixedDeltaTime).ProjectOnPlanePreNormalized(vesselUp);
            if (directionIntegral.sqrMagnitude > 1f) directionIntegral = directionIntegral.normalized;
            pitchIntegral = SteerCorrection(Axis.Pitch) * Vector3.Dot(directionIntegral, -vesselForward);
            yawIntegral = 0.33f * SteerCorrection(Axis.Yaw) * Vector3.Dot(directionIntegral, vesselRight);
            rollIntegral = 0.1f * SteerCorrection(Axis.Roll) * Mathf.Clamp(rollIntegral + rollError * Time.fixedDeltaTime, -1f, 1f);

            var steerPitch = pitchProportional + pitchIntegral - pitchDamping;
            var steerYaw = yawProportional + yawIntegral - yawDamping;
            var steerRoll = rollProportional + rollIntegral - rollDamping;
            #endregion

            //v/q
            float dynamicAdjustment = Mathf.Clamp(16 * (float)(vessel.srfSpeed / vessel.dynamicPressurekPa), 0, 1.2f);
            steerPitch *= dynamicAdjustment;
            steerYaw *= dynamicAdjustment;
            steerRoll *= dynamicAdjustment;

            SetFlightControlState(s,
                Mathf.Clamp(steerPitch, -finalMaxSteer, finalMaxSteer), // pitch
                Mathf.Clamp(steerYaw, -finalMaxSteer, finalMaxSteer), // yaw
                Mathf.Clamp(steerRoll, -userSteerLimit, userSteerLimit)); // roll

            if (AutoTune)
            { pidAutoTuning.Update(pitchError, rollError, yawError); }

            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI)
            {
                debugString.AppendLine($"steerMode: {steerMode}, rollError: {rollError,7:F4}°, pitchError: {pitchError,7:F4}°, yawError: {yawError,7:F4}°");
                debugString.AppendLine($"finalMaxSteer: {finalMaxSteer:G3}, dynAdj: {dynamicAdjustment:G3}");
                // debugString.AppendLine($"Bank Angle: {bankAngle}");
                debugString.AppendLine($"Pitch: P: {pitchProportional,7:F4}, I: {pitchIntegral,7:F4}, D: {pitchDamping,7:F4}");
                debugString.AppendLine($"Yaw: P: {yawProportional,7:F4}, I: {yawIntegral,7:F4}, D: {yawDamping,7:F4}");
                debugString.AppendLine($"Roll: P: {rollProportional,7:F4}, I: {rollIntegral,7:F4}, D: {rollDamping,7:F4}");
                // debugString.AppendLine($"ω.x: {vessel.angularVelocity.x:F3} rad/s, I.x: {vessel.angularMomentum.x / vessel.angularVelocity.x:F3} kg•m²");
            }
        }

        enum ExtendChecks { All, RequestsOnly, AirToGroundOnly };
        bool CheckExtend(ExtendChecks checkType = ExtendChecks.All)
        {
            // Sanity checks.
            var weaponManager = WeaponManager;
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
            if (extendAbortTimer < 0) // In cooldown, extending disabled.
            {
                StopExtending("in cooldown");
                return false;
            }
            if (ramming) // Disable extending if in ramming mode.
            {
                StopExtending("ramming speed");
                return false;
            }
            if (!extending)
            {
                extendParametersSet = false; // Reset this flag for new extends.
                extendHorizontally = true;
                extendingForBombing = false;
            }
            if (requestedExtend)
            {
                requestedExtend = false;
                if (CheckRequestedExtendDistance())
                {
                    extending = true;
                    lastExtendTargetPosition = requestedExtendTpos;
                }
            }
            // TODO - Extend vectors - for something like a bombing run that fails, or seeking to extend from a enemy plane that's getting too close, makes more sense to continue forward, or with something like a 45deg deflection, vs a full 180, to conserve energy.

            if (checkType == ExtendChecks.RequestsOnly) return extending;
            if (extending && extendParametersSet)
            {
                if (extendTarget != null) // Update the last known target position.
                {
                    Vector3 extendPos = extendTarget.CoM;
                    if (extendingForBombing) // For bombing we want the predicted bombing target position, not the vessel's CoM.
                    {
                        extendPos = bombingTargetPrediction = PredictBombingTarget(extendTarget).Item2;
                    }
                    lastExtendTargetPosition = extendPos;
                    if (!extendingForBombing && extendForMissile != null) // If extending to fire a missile, update the extend distance for the dynamic launch range.
                    {
                        float boresightFactor = (vessel.LandedOrSplashed || extendTarget.LandedOrSplashed || extendForMissile.uncagedLock) ? 0.75f : 0.35f;
                        float minOffBoresight = extendForMissile.maxOffBoresight * boresightFactor;
                        float missileAngleToTarget = VectorUtils.Angle(extendForMissile.GetForwardTransform(), extendPos - extendForMissile.transform.position);
                        var minDynamicLaunchRange = MissileLaunchParams.GetDynamicLaunchParams(
                            extendForMissile,
                            extendTarget.Velocity(),
                            extendPos,
                            //minOffBoresight + (180f - minOffBoresight) * Mathf.Clamp01(((missile.transform.position - v.transform.position).magnitude - missile.minStaticLaunchRange) / (Mathf.Max(100f + missile.minStaticLaunchRange * 1.5f, 0.1f * missile.maxStaticLaunchRange) - missile.minStaticLaunchRange)) // Reduce the effect of being off-target while extending to prevent super long extends.
                            //missileAngleToTarget <= minOffBoresight ? -1 : (extendForMissile.transform.position - extendTarget.transform.position).sqrMagnitude < (extendForMissile.minStaticLaunchRange * extendForMissile.minStaticLaunchRange) ? 180 : -1
                            missileAngleToTarget <= minOffBoresight ? -1 : minOffBoresight
                        ).minLaunchRange;
                        extendDistance = Mathf.Max(extendDistanceAirToAir, minDynamicLaunchRange);
                        extendDesiredMinRadarAltitude = minAltitude;
                        //(weaponManager.currentTarget != null && weaponManager.currentTarget.Vessel != null && weaponManager.currentTarget.Vessel.LandedOrSplashed) ? (extendForMissile.GetWeaponClass() == WeaponClasses.SLW ? 10 : //drop to the deck for torpedo run
                        //Mathf.Max(defaultAltitude - 500f, minAltitude)) : //else commence level bombing
                        //extendForMissile.GetBlastRadius() * 2; //else target flying; get close for bombing airships to try and ensure hits
                    }
                }
                return true; // Already extending.
            }
            if (!wasEvading) evasionNonlinearityDirection = Mathf.Sign(UnityEngine.Random.Range(-1f, 1f)); // This applies to extending too.

            // Dropping a bomb.
            if (extending && (extendingReason == "bombs away!" || extendingReason == "too close to bomb" || extendingReason == "too low for dive-bombing"))
            {
                extendDistance = extendRequestMinDistance;
                if (extendingReason == "bombs away!")
                    extendDesiredMinRadarAltitude = Mathf.Max((float)vessel.radarAltitude, minAltitude); // Extend horizontally after dropping a bomb.
                else
                    extendDesiredMinRadarAltitude = divebombing ? bombingAltitude : finalBombingAltitude;
                extendingForBombing = true;
                extendHorizontally = true;
                extendParametersSet = true;
                if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI]: {Time.time:F3} {vessel.vesselName} is extending due to {extendingReason}");
                return true;
            }

            // Ground targets and bombing.
            var selectedGun = weaponManager.currentGun;
            var selectedWeapon = weaponManager.selectedWeapon;
            if (selectedGun == null && selectedWeapon == null) selectedGun = weaponManager.previousGun;
            bool bombSelected = selectedGun == null && selectedWeapon != null && selectedWeapon.GetWeaponClass() == WeaponClasses.Bomb;
            if (targetVessel != null && (
                targetVessel.LandedOrSplashed // Ground target (guns or bombs)
                || bombSelected // Bombing an air target.
            ))
            {
                if (selectedGun != null && !selectedGun.engageGround) // Don't extend from ground targets when using a weapon that can't target ground targets.
                {
                    weaponManager.ForceScan(); // Look for another target instead.
                    return false;
                }
                Vector3 vecToTarget = (bombSelected ? bombingTargetPrediction : targetVessel.CoM) - vessel.CoM;
                Vector3 hVecToTarget = vecToTarget.ProjectOnPlanePreNormalized(vessel.up);
                float hDistSqr = hVecToTarget.sqrMagnitude;
                bool shouldExtend;
                string groundTargetExtendReason = "Surface target"; // Also includes bombing...
                bool bombingAirTarget = false;
                float bombingAltOverTarget = bombingAltitude;
                if (selectedGun != null)
                {
                    extendDistance = extendDistanceAirToGroundGuns;
                    extendDesiredMinRadarAltitude = minAltitude + 0.5f * extendDistance; // Desired minimum altitude after extending. (30° attack vector plus min alt.)
                    shouldExtend = hDistSqr < extendDistance * extendDistance && VectorUtils.Angle(vesselTransform.up, targetVessel.CoM - vessel.CoM) > 45; // Not pointing near the target.
                }
                else // Bombs/missiles (should this really apply without modification for missiles too?)
                {
                    // Note: finalBombingRadarAltitude may not be set or be out-dated at this point as CheckExtend(A2G) is called before FlyToTargetVessel, so we use bombingAltitude instead.
                    var missile = weaponManager.CurrentMissile;
                    if (missile == null) // No bomb/missile selected!
                    {
                        extendDistance = extendDistanceBombing;
                        shouldExtend = hDistSqr < extendDistance * extendDistance;
                    }
                    else
                    {
                        bombingAirTarget = !targetVessel.LandedOrSplashed;
                        if (!divebombing && bombingAirTarget) bombingAltOverTarget = 2f * missile.GetBlastRadius();
                        if (divebombing)
                        {
                            extendDistance = extendDistanceBombing + bombingAltOverTarget / Mathf.Tan(Mathf.Deg2Rad * Mathf.Min(divebombingAngle, 89f)); // Avoid 1/Inf.
                            if (hDistSqr < extendDistance * extendDistance && !DivebombStarted)
                            {
                                if (Vector3.Dot(vesselTransform.up.ProjectOnPlanePreNormalized(vessel.up), hVecToTarget) < 0) // Pointing away from the target in the horizontal plane.
                                {
                                    shouldExtend = true;
                                    groundTargetExtendReason = $"too close and pointing away from divebomb {(bombingAirTarget ? "airborne" : "surface")} target";
                                }
                                else if (vessel.radarAltitude < (targetVessel.LandedOrSplashed ? 0 : targetVessel.radarAltitude) + 0.5f * bombingAltitude) // Too low to start dive.
                                {
                                    shouldExtend = true;
                                    groundTargetExtendReason = $"too close and too low to divebomb {(bombingAirTarget ? "airborne" : "surface")} target";
                                }
                                else shouldExtend = false;
                            }
                            else shouldExtend = false;
                        }
                        else
                        {
                            if (targetVessel.LandedOrSplashed) // Level bombing ground target.
                            {
                                extendDistance = extendDistanceBombing + Mathf.Max((float)vessel.srfSpeed, idleSpeed) * BDAMath.Sqrt(2f * bombingAltOverTarget / bodyGravity);
                                shouldExtend = hDistSqr < extendDistance * extendDistance && Vector3.Dot(vesselTransform.up, targetVessel.CoM - vessel.CoM) < 0; // Pointing away from the target. Overshooting extend request is handled in MissileFire.GuardBombRoutine.
                                groundTargetExtendReason = $"too low to level-bomb surface target";
                            }
                            else // Level bombing airborne target.
                            {
                                // Assume the current speed is around the actual max speed of the craft and use a drop time corresponding to 2x blast radius.
                                extendDistance = extendDistanceBombing + Mathf.Max((float)vessel.srfSpeed, idleSpeed) * BDAMath.Sqrt(2f * bombingAltOverTarget / bodyGravity);
                                shouldExtend = hDistSqr < extendDistance * extendDistance && (
                                    Vector3.Dot(vesselTransform.up, targetVessel.CoM - vessel.CoM) < 0 // Pointing away from the target. Overshooting extend request is handled in MissileFire.GuardBombRoutine.
                                    || vessel.altitude < targetVessel.altitude // Target is above us.
                                );
                                groundTargetExtendReason = $"too low to level-bomb airborne target";
                            }
                        }
                    }
                    if (shouldExtend)
                    {
                        extendDesiredMinRadarAltitude = bombingAirTarget ? (float)targetVessel.altitude + bombingAltOverTarget : bombingAltitude;
                        extendHorizontally = true;
                        extendingForBombing = true;
                    }
                }
                if (shouldExtend)
                {
                    extending = true;
                    extendingReason = groundTargetExtendReason;
                    lastExtendTargetPosition = extendingForBombing ? bombingTargetPrediction = PredictBombingTarget(targetVessel).Item2 : targetVessel.CoM;
                    extendTarget = targetVessel;
                    extendParametersSet = true;
                    if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI]: {Time.time:F3} {vessel.vesselName} is extending due to {groundTargetExtendReason}.");
                    return true;
                }
            }
            if (checkType == ExtendChecks.AirToGroundOnly) return false;

            // Air target (from requests, where extendParameters haven't been set yet).
            if (extending && extendTarget != null && !extendTarget.LandedOrSplashed) // We have a flying target, only extend a short distance and don't climb.
            {
                extendDistance = Mathf.Max(extendDistanceAirToAir, extendRequestMinDistance);
                extendHorizontally = false;
                extendDesiredMinRadarAltitude = Mathf.Max((float)vessel.radarAltitude + _extendAngleAirToAir * extendDistance, minAltitude);
                extendParametersSet = true;
                if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI]: {Time.time:F3} {vessel.vesselName} is extending due to an air target ({extendingReason}).");
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
                extendVector = vessel.CoM - requestedExtendTpos;
            }
            else return true; // Ignore non-airborne targets for now. Currently, requests are only made for air-to-air targets and for dropping bombs.
            return extendVector.sqrMagnitude < localExtendDistance * localExtendDistance; // Extend from position is further than the extend distance.
        }

        void FlyExtend(FlightCtrlState s, Vector3 tPosition)
        {
            Vector3 vesselPos = vesselTransform.position;

            var (currentDistance, currentDirection) = (extendHorizontally ? (vesselPos - tPosition).ProjectOnPlanePreNormalized(upDirection) : vesselPos - tPosition).MagNorm();
            SetStatus($"Extending ({currentDistance:0}m / {extendDistance:0}m)");
            if (BDArmorySettings.COMP_CONVENIENCE_CHECKS && extensionCutoffTime > 0)
            {
                extensionCutoffTimer += Time.fixedDeltaTime;
                if (extensionCutoffTimer > extensionCutoffTime) //there are reasons a hard cutoff for extension is a bad idea, and will probably break any sort of bombing routine, but, well, the customer is always right...
                {
                    StopExtending($"extend time limit exceeded", true);
                    return;
                }
            }
            if (currentDistance < extendDistance) // Extend from position is closer (horizontally) than the extend distance.
            {
                if (currentDistance > lastExtendDistance + extendMinGainRate * Time.fixedDeltaTime) // Gaining distance fast enough.
                {
                    if (extendAbortTimer > 0) // Reduce the timer to 0.
                    {
                        extendAbortTimer -= 0.5f * TimeWarp.fixedDeltaTime; // Reduce at half the rate of increase, so oscillating pairs of craft eventually time out and abort.
                        if (extendAbortTimer < 0) extendAbortTimer = 0;
                    }
                }
                else // Not gaining distance fast enough.
                {
                    extendAbortTimer += TimeWarp.fixedDeltaTime;
                    if (extendAbortTimer > extendAbortTime) // Abort if not gaining enough distance.
                    {
                        StopExtending($"extend abort time ({extendAbortTime}s) reached at distance {currentDistance}m of {extendDistance}m", true);
                        return;
                    }
                }
                lastExtendDistance = currentDistance;

                Vector3 target = vesselPos + extendDistance * currentDirection; // Target extend position.
                if (extendingForBombing)
                {
                    isBombing = true;
                    if (extendForMissile != null)
                    {
                        float vesselRadius = vessel.GetRadius();
                        float vesselRadiusSqr = vesselRadius * vesselRadius;
                        Vector3 projBombRelativePos = (extendForMissile.transform.position - vesselPos).ProjectOnPlanePreNormalized(vesselTransform.right); // Projected to the centerline of the plane.
                        float bombSqrDist = projBombRelativePos.sqrMagnitude;
                        if (bombSqrDist < vesselRadiusSqr) // While the most recently dropped bomb is too close, just pitch up away from it.
                        {
                            target = vesselPos - 50f / vesselRadius * Vector3.Lerp(vesselRadius * vesselTransform.forward, projBombRelativePos, bombSqrDist / vesselRadiusSqr) + 100f * vessel.srf_vel_direction; // Aim roughly 30° up and forwards.
                            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Extending: {currentDistance:0}m of {extendDistance:0}m{(extendAbortTimer > 0 ? $" ({extendAbortTimer:F1}s of {extendAbortTime:F1}s)" : "")}. Avoiding wing-slapping bombs {bombSqrDist.Sqrt():0.00} / {vesselRadius:0.00}.");
                            FlyToPosition(s, target, allowCorrections: false);
                            return;
                        }
                        else
                        {
                            extendForMissile = null; // Clear the bomb once we're safely away from it.
                        }
                    }
                    target += upDirection * (extendDesiredMinRadarAltitude - (float)vessel.radarAltitude);
                }
                float targetRadarAltitude = BodyUtils.GetRadarAltitudeAtPos(target);
                if (targetRadarAltitude < defaultAltitude)
                    target += upDirection * ((float)vessel.radarAltitude - targetRadarAltitude); // Account for terrain changes if below defaultAltitude.
                target = FlightPosition(target, extendDesiredMinRadarAltitude); // Further adjustments for speed, situation, etc. and desired minimum altitude after extending.
                target = vesselPos + GetLimitedClimbDirectionForSpeed(target - vesselPos); // Limit climb rate to avoid stalling.
                if (regainEnergy)
                {
                    RegainEnergy(s, target - vessel.CoM);
                    return;
                }
                else
                {
                    if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Extending: {currentDistance:0}m of {extendDistance:0}m{(extendAbortTimer > 0 ? $" ({extendAbortTimer:F1}s of {extendAbortTime:F1}s)" : "")}. For bombing: {extendingForBombing}. Desired min alt: {extendDesiredMinRadarAltitude:0}.");
                    FlyToPosition(s, target);
                }
            }
            else // We're far enough away, stop extending.
            {
                StopExtending($"gone far enough ({currentDistance}m of {extendDistance}m)");
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

            Vector3 vesselPos = vesselTransform.position;

            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Flying orbit");
            Vector3 flightCenter = GetTerrainSurfacePosition(VectorUtils.GetWorldSurfacePostion(centerGPS, vessel.mainBody)) + (defaultAltitude * upDirection);
            Vector3 myVectorFromCenter = (vesselPos - flightCenter).ProjectOnPlanePreNormalized(upDirection);
            Vector3 myVectorOnOrbit = myVectorFromCenter.normalized * radius;
            Vector3 targetVectorFromCenter = Quaternion.AngleAxis(clockwise ? 15f : -15f, upDirection) * myVectorOnOrbit; // 15° ahead in the orbit. Distance = π*radius/12
            Vector3 verticalVelVector = Vector3.Project(vessel.Velocity(), upDirection); //for vv damping
            Vector3 targetPosition = flightCenter + targetVectorFromCenter - (verticalVelVector * 0.1f);
            if (vessel.radarAltitude < 1000)
            {
                var terrainAdjustment = (BodyUtils.GetTerrainAltitudeAtPos(targetPosition) - BodyUtils.GetTerrainAltitudeAtPos(flightCenter)); // Terrain adjustment to avoid throwing planes at terrain when at low altitude.
                targetPosition += (1f - (float)vessel.radarAltitude / 1000f) * (float)terrainAdjustment * upDirection; // Fade in adjustment from 1km altitude.
            }
            if (vessel.radarAltitude < 500) // Terrain slope adjustment when at <500m.
            {
                Ray ray = new Ray(vesselPos, (targetPosition - vesselPos).normalized);
                var distance = Mathf.PI * radius / 12f;
                if (Physics.Raycast(ray, out RaycastHit hit, distance, (int)LayerMasks.Scenery))
                {
                    var slope = ray.direction.ProjectOnPlane(Vector3.Cross(hit.normal, ray.direction));
                    targetPosition = targetPosition * (hit.distance / distance) + (1 - hit.distance / distance) * (vesselPos + slope * distance);
                }
            }
            Vector3 vectorToTarget = targetPosition - vesselPos;
            // Vector3 planarVel = vessel.Velocity().ProjectOnPlanePreNormalized(upDirection);
            //vectorToTarget = Vector3.RotateTowards(planarVel, vectorToTarget, 25f * Mathf.Deg2Rad, 0);
            vectorToTarget = GetLimitedClimbDirectionForSpeed(vectorToTarget);
            targetPosition = vesselPos + vectorToTarget;

            if (command != PilotCommands.Free && (vesselPos - flightCenter).sqrMagnitude < radius * radius * 1.5f)
            {
                if (BDArmorySettings.DEBUG_AI) Debug.Log("[BDArmory.BDModulePilotAI]: AI Pilot reached command destination.");
                ReleaseCommand(false);
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
            Vector3 vesselPos = vessel.transform.position;
            // Note: UpdateWaypoint is called separately before this in case FlyWaypoints doesn't get called.
            if (BDArmorySettings.WAYPOINT_LOOP_INDEX > 1)
            {
                SetStatus($"Lap {activeWaypointLap}, Waypoint {activeWaypointIndex} ({waypointRange:F0}m)");
            }
            else
            {
                var wpName = WaypointCourses.CourseLocations[waypointCourseIndex].waypoints[activeWaypointIndex].name;
                SetStatus($"Waypoint {activeWaypointIndex}{(string.IsNullOrEmpty(wpName) ? "" : $" {wpName}")} ({waypointRange:F0}m)");
            }
            var waypointDirection = (waypointPosition - vesselPos).normalized;
            if (waypointRange < (BDArmorySettings.WAYPOINTS_SCALE > 0 ? BDArmorySettings.WAYPOINTS_SCALE : (WaypointCourses.CourseLocations[waypointCourseIndex].waypoints[activeWaypointIndex].scale)) / 2) //gate radius
            {
                //if (VectorUtils.Angle(waypointDirection, vessel.ReferenceTransform.up) > maxAllowedAoA)//as we get closer angle to WP is going to very rapidly increase from ~0 to 90 if not *perfectly* aligned
                //    waypointDirection = vessel.Velocity(); //so if within [gate radius] distance of the WP, if the angle to the gate exceeds max AOA angle, commit to current direaction to prevent control jerk at the last second as the AI tries to correct off-targetness
                waypointDirection = Vector3.RotateTowards(vessel.srf_vel_direction, waypointDirection, Mathf.Deg2Rad * Mathf.Min(maxAllowedAoA, Mathf.Min(0.5f, 200f / (float)vessel.srfSpeed) * waypointRange), 0); //- maxAllowedAoA goes from 0 - 90; at default 35deg, would need to be going 400m/s through a 70m gate before speed and diameter matter; figure out different formula
                //
            }
            waypointRay = new Ray(vesselPos, waypointDirection);
            if (Physics.Raycast(waypointRay, out waypointRayHit, waypointRange, (int)LayerMasks.Scenery))
            {
                var angle = 90f + 90f * (1f - waypointTerrainAvoidance) * (waypointRayHit.distance - defaultAltitude) / (waypointRange + 1000f); // Parallel to the terrain at the default altitude (in the direction of the waypoint), adjusted for relative distance to the terrain and the waypoint. 1000 added to waypointRange to provide a stronger effect if the distance to the waypoint is small.
                waypointTerrainSmoothedNormal = waypointTerrainAvoidanceActive ? Vector3.Lerp(waypointTerrainSmoothedNormal, waypointRayHit.normal, 0.5f - 0.4862327f * waypointTerrainAvoidanceSmoothingFactor) : waypointRayHit.normal; // Smooth out varying terrain normals at a rate depending on the terrain avoidance strength (half-life of 1s at max avoidance, 0.29s at mid and 0.02s at min avoidance).
                waypointDirection = Vector3.RotateTowards(waypointTerrainSmoothedNormal, waypointDirection, angle * Mathf.Deg2Rad, 0f);
                waypointTerrainAvoidanceActive = true;
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Waypoint Terrain: {waypointRayHit.distance:F1}m @ {angle:F2}°");
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
            FlyToPosition(s, vesselPos + waypointDirection * Mathf.Min(500f, waypointRange), false); // Target up to 500m ahead so that max altitude restrictions apply reasonably.
        }

        private Vector3 WaypointSpline() // FIXME This doesn't work that well yet.
        {
            // Note: here we're using distance instead of time as the spline interpolation parameter.
            float minDistance = (float)vessel.speed * 2f; // Consider the radius of 2s around the waypoint.

            Vector3 vesselPos = vessel.transform.position;

            Vector3 point1 = waypointPosition + (vesselPos - waypointPosition).normalized * minDistance; //waypointsRange > minDistance ? vessel.transform.position : waypointPosition + (vessel.transform.position - waypointPosition).normalized * minDistance;
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
                point3 = waypointPosition + (waypointPosition - vesselPos).normalized * minDistance; // Straight out the other side.
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
                    waypointRollTarget = (nextWaypointPosition - waypointPosition).ProjectOnPlane(vessel.Velocity()).normalized;
                    waypointRollTargetStrength = Mathf.Min(1f, VectorUtils.Angle(nextWaypointPosition - waypointPosition, vessel.Velocity()) / maxAllowedAoA) * Mathf.Max(0, 1f - waypointRange / range); // Full strength at maxAllowedAoA and at the waypoint.
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
            UI_FloatRange field = (UI_FloatRange)Fields[nameof(waypointTerrainAvoidance)].uiControlEditor;
            field.onFieldChanged = OnWaypointTerrainAvoidanceUpdated;
            field = (UI_FloatRange)Fields[nameof(waypointTerrainAvoidance)].uiControlFlight;
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
            speedController.forceAfterburnerIfMaxThrottle = vessel.srfSpeed < ABOverrideThreshold;
        }

        static string[] kinematicEvasionStateStrings = ["", " (Turning Hot)", " (Cranking)", " (Notching)", " (Turning Cold)", " (Notching & Diving)"];
        bool emergencyNotch = false;

        void Evasive(FlightCtrlState s)
        {
            if (s == null) return;
            if (vessel == null) return;
            var weaponManager = WeaponManager;
            if (weaponManager == null) return;

            SetStatus("Evading");
            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI)
            {
                debugString.AppendLine($"Evasive {evasiveTimer}s");
                debugString.AppendLine($"Threat Distance: {weaponManager.incomingMissileDistance}");
            }
            evading = true;
            if (!wasEvading) evasionNonlinearityDirection = Mathf.Sign(UnityEngine.Random.Range(-1f, 1f));

            bool hasABEngines = speedController.multiModeEngines.Count > 0;

            collisionDetectionTicker += 2;
            if (BDArmorySettings.DEBUG_LINES) debugBreakDirection = default;

            steerMode = SteerModes.Manoeuvering;
            if (weaponManager.isFlaring)
            {
                useAB = vessel.srfSpeed < minSpeed;
                useBrakes = false;
                float targetSpeed = minSpeed;
                if (weaponManager.isChaffing)
                    targetSpeed = maxSpeed;
                AdjustThrottle(targetSpeed, false, useAB);
            }

            Vessel missileThreat = weaponManager.missileIsIncoming ? weaponManager.incomingMissileVessel : null;
            MissileBase missileThreatMB;
            float closingTime;
            string kinematicEvasionStatus = "";

            if (
                missileThreat != null
                && (missileThreatMB = VesselModuleRegistry.GetMissileBase(missileThreat)) != null // Modular missiles can lose the MMG part.
                && ((closingTime = weaponManager.ThreatClosingTime(missileThreat)) <= weaponManager.evadeThreshold)
            ) // Missile evasion
            {
                Vector3 targetDirection;
                bool overrideThrottle = false;
                Vector3d vesselVel = vessel.Velocity();
                if ((closingTime <= 1.5f) && (!weaponManager.isChaffing)) // Missile is about to impact, pull a hard turn
                {
                    if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Missile about to impact! pull away!");

                    AdjustThrottle(maxSpeed, false, !weaponManager.isFlaring);

                    Vector3 cross = Vector3.Cross(missileThreat.CoM - vessel.CoM, vesselVel).normalized;
                    if (Vector3.Dot(cross, -vesselTransform.forward) < 0)
                    {
                        cross = -cross;
                    }
                    targetDirection = ((50.0 / vessel.srfSpeed) * vesselVel + 100 * cross).normalized;
                }
                else // Fly at 90 deg to missile to put max distance between ourselves and dispensed flares/chaff
                {
                    bool inVacuum = vessel.InNearVacuum();
                    // Break off at 90 deg to missile
                    Vector3 threatDirection = -1f * missileThreat.Velocity().ProjectOnPlanePreNormalized(upDirection);
                    float sign = Vector3.SignedAngle(threatDirection, vesselVel.ProjectOnPlanePreNormalized(upDirection), upDirection);
                    Vector3 breakDirection = Vector3.Cross(Mathf.Sign(sign) * upDirection, threatDirection).ProjectOnPlanePreNormalized(upDirection); // Break left or right depending on which side the missile is coming in on.

                    // Missile kinematics check to see if alternate break directions are better (crank or turn around and run)
                    bool dive = true;
                    bool notch = true;
                    float diveAngle = 75f;
                    if (evasionMissileKinematic && !inVacuum)
                    {
                        dive = false;
                        notch = false;
                        breakDirection = MissileKinematicEvasion(breakDirection, threatDirection);
                        if (kinematicEvasionState == KinematicEvasionStates.NotchDive)
                        {
                            notch = true;
                            dive = true;
                        }
                        else if (kinematicEvasionState == KinematicEvasionStates.Notch)
                        {
                            notch = true;
                        }

                        kinematicEvasionStatus = kinematicEvasionStateStrings[(int)kinematicEvasionState];
                    }
                    else
                        if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine("Breaking from missile threat!");

                    // Dive to gain energy and hopefully lead missile into ground when not in space
                    if (!inVacuum && dive)
                    {
                        float diveScale = Mathf.Max(1000f, 2f * turnRadius);
                        float angle = Mathf.Clamp((float)vessel.radarAltitude - minAltitude, 0, diveScale) / diveScale * 90;
                        float angleAdjMissile = Mathf.Max(Mathf.Asin(((float)vessel.radarAltitude - (float)weaponManager.incomingMissileVessel.radarAltitude) /
                            weaponManager.incomingMissileDistance) * Mathf.Rad2Deg, 0f); // Don't dive into the missile if it's coming from below

                        if (weaponManager.incomingMissileTime > weaponManager.cmThreshold)
                        {
                            float t = Mathf.Clamp01((weaponManager.incomingMissileTime - weaponManager.cmThreshold) / (weaponManager.evadeThreshold - weaponManager.cmThreshold));
                            t = -1.72f * t * t * t + 4.06f * t * t - 3.34f * t + 1f;
                            diveAngle = Mathf.Lerp(35f, 75f, t); // Gradually dive more as missile gets closer
                        }

                        diveAngle = Mathf.Clamp(angle - angleAdjMissile, 0, diveAngle) * Mathf.Deg2Rad;
                        //breakDirection = Vector3.RotateTowards(breakDirection, -upDirection, angle, 0);
                    }
                    if (notch)
                    {
                        Vector3 radarDir;
                        // If in space or not SARH (note that no radarTarget.exists check is made, but this *should* be fine)
                        if (!BDArmorySettings.RADAR_NOTCHING || inVacuum || missileThreatMB.TargetingMode != MissileBase.TargetingModes.Radar || !missileThreatMB.vrd || !missileThreatMB.radarTarget.lockedByRadar)
                        {
                            radarDir = VectorUtils.NormalizedDiff(missileThreat.CoM, vessel.CoM); // Use missile dir
                        }
                        else
                        {
                            radarDir = VectorUtils.NormalizedDiff(missileThreatMB.radarTarget.lockedByRadar.vessel.CoM, vessel.CoM); // Use radar parent vessel dir
                        }

                        if (!inVacuum)
                        {
                            emergencyNotch = emergencyNotch && weaponManager.isChaffing;
                            if (evasionMissileEmergencyNotchVel > 0 && weaponManager.isChaffing && (emergencyNotch || Mathf.Abs(Vector3.Dot(vesselVel, radarDir)) > evasionMissileEmergencyNotchVel))
                            {
                                emergencyNotch = true;
                                // Emergency notch, get us notched ASAP!
                                kinematicEvasionStatus = " (Emergency Notch)";
                                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.Append(" Emergency Notch!");
                                breakDirection = vesselVel.ProjectOnPlanePreNormalized(radarDir);
                            }
                            else
                            {
                                emergencyNotch = false;
                                // Standard notch
                                //breakDirection = NotchDir(breakDirection, radarDir, dive ? diveAngle : 0f);
                                const float halfPi = Mathf.PI * 0.5f;
                                breakDirection = VectorUtils.ConePlaneIntercept(breakDirection, radarDir, -vessel.up, (dive ? (halfPi - diveAngle) : halfPi));
                            }
                        }
                        else
                        {
                            // No need to worry about diving or emergencyNotch in a vacuum...
                            emergencyNotch = false;
                            breakDirection = vesselVel.ProjectOnPlanePreNormalized(radarDir);
                        }
                    }
                    else
                    {
                        emergencyNotch = false;
                    }
                    if (BDArmorySettings.DEBUG_LINES) debugBreakDirection = breakDirection;

                    // Rotate target direction towards break direction, starting with 15 deg, and increasing to maxAllowedAoA as missile gets closer
                    float rotAngle = Mathf.Deg2Rad * Mathf.Lerp(Mathf.Min(maxAllowedAoA, 90), 15f, Mathf.Clamp01(weaponManager.incomingMissileTime / weaponManager.evadeThreshold));
                    targetDirection = Vector3.RotateTowards(vesselTransform.up, breakDirection, rotAngle, 0).normalized;

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
                    overrideThrottle = true;
                }
                if (belowMinAltitude)
                {
                    float rise = 0.5f * Mathf.Max(5f, (float)vessel.srfSpeed * 0.25f) * Mathf.Max(speedController.TWR, 1f); // Add some climb like in TakeOff (at half the rate) to get back above min altitude.
                    targetDirection += rise * upDirection;

                    float verticalComponent = Vector3.Dot(targetDirection, upDirection);
                    if (verticalComponent < 0) // If we're below minimum altitude, enforce the evade direction to gain altitude.
                    {
                        targetDirection += -2f * verticalComponent * upDirection;
                    }
                }
                SetStatus("Evading" + kinematicEvasionStatus);
                RCSEvade(s, targetDirection);//add spacemode RCS dodging; missile evasion, fire in targetDirection
                FlyToPosition(s, vesselTransform.position + targetDirection * 100, overrideThrottle);
                return;
            }
            else if (weaponManager.underFire)
            {
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.Append($"Dodging gunfire");
                float threatDirectionFactor = Vector3.Dot(vesselTransform.up, threatRelativePosition.normalized);
                //Vector3 axis = -Vector3.Cross(vesselTransform.up, threatRelativePosition);

                //for the most part, we want to turn _towards_ the threat in order to increase the rel ang vel and get under its guns
                //for Waypoint Race evasion, keep pointing towards next gate; dodging is handled by evasion non-linearity waggle in FlyToPosition
                Vector3 breakTarget = (IsRunningWaypoints ? (waypointPosition - vessel.transform.position) : threatRelativePosition) * 2f;

                if (weaponManager.incomingThreatVessel != null && weaponManager.incomingThreatVessel.LandedOrSplashed) // Surface threat.
                {
                    // Break horizontally away at maxAoA initially, then directly away once past 90°.
                    breakTarget = Vector3.RotateTowards(vessel.srf_vel_direction, -threatRelativePosition, maxAllowedAoA * Mathf.Deg2Rad, 0);
                    if (threatDirectionFactor > 0)
                        breakTarget = breakTarget.ProjectOnPlanePreNormalized(upDirection);
                    breakTarget = breakTarget.normalized * 100f;
                    var breakTargetAlt = BodyUtils.GetRadarAltitudeAtPos(vessel.transform.position + breakTarget);
                    if (breakTargetAlt > defaultAltitude) breakTarget -= (breakTargetAlt - defaultAltitude) * upDirection;
                    if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($" from ground target.");
                }
                else // Airborne threat.
                {
                    if (threatDirectionFactor > 0.9f)     //within 28 degrees in front
                    { // This adds +-500/(threat distance) to the left or right relative to the breakTarget vector, regardless of the size of breakTarget
                        breakTarget += 500f / threatRelativePosition.magnitude * Vector3.Cross(threatRelativePosition.normalized, Mathf.Sign(Mathf.Sin((float)vessel.missionTime / 2)) * vessel.upAxis);
                        if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($" from directly ahead!");
                        RCSEvade(s, new Vector3(1 * evasionNonlinearityDirection, 0, 0));//add spacemode RCS dodging; forward incoming fire, flank L/R
                    }
                    else if (threatDirectionFactor < -0.9) //within ~28 degrees behind
                    {
                        float threatDistanceSqr = threatRelativePosition.sqrMagnitude;
                        if (threatDistanceSqr > 400 * 400)
                        { // This sets breakTarget 1500m ahead and 500m down, then adds a 1000m offset at 90° to ahead based on missionTime. If the target is kinda close, brakes are also applied.
                            breakTarget = vesselTransform.up * 1500 - 500 * vessel.upAxis;
                            breakTarget += Mathf.Sin((float)vessel.missionTime / 2) * vesselTransform.right * 1000 - Mathf.Cos((float)vessel.missionTime / 2) * vesselTransform.forward * 1000;
                            if (threatDistanceSqr > 800 * 800)
                            {
                                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($" from behind afar; engaging barrel roll");
                            }
                            else
                            {
                                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($" from behind moderate distance; engaging aggressvie barrel roll and braking");
                                AdjustThrottle(minSpeed, true, false);
                            }
                            RCSEvade(s, new Vector3(Mathf.Sin((float)vessel.missionTime / 2), Mathf.Cos((float)vessel.missionTime / 2), 0));//add spacemode RCS dodging; aft incoming fire, orbit about prograde
                        }
                        else
                        { // This sets breakTarget to the attackers position, then applies an up to 500m offset to the right or left (relative to the vessel) for the first half of the default evading period, then sets the breakTarget to be 150m right or left of the attacker.
                            breakTarget = threatRelativePosition;
                            if (evasiveTimer < 1.5f)
                                breakTarget += Mathf.Sin((float)vessel.missionTime * 2) * vesselTransform.right * 500;
                            else
                                breakTarget += -Math.Sign(Mathf.Sin((float)vessel.missionTime * 2)) * vesselTransform.right * 150;

                            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($" from directly behind and close; breaking hard");
                            AdjustThrottle(minSpeed, true, false); // Brake to slow down and turn faster while breaking target
                            RCSEvade(s, new Vector3(0, 0, -1));//add spacemode RCS dodging; fire available retrothrusters
                        }
                    }
                    else
                    {
                        float threatDistanceSqr = threatRelativePosition.sqrMagnitude;
                        if (threatDistanceSqr < 400 * 400) // Within 400m to the side.
                        { // This sets breakTarget to be behind the attacker (relative to the evader) with a small offset to the left or right.
                            breakTarget += Mathf.Sin((float)vessel.missionTime * 2) * vesselTransform.right * 100;

                            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($" from near side; turning towards attacker");
                        }
                        else // More than 400m to the side.
                        { // This sets breakTarget to be 1500m ahead, then adds a 1000m offset at 90° to ahead.
                            breakTarget = vesselTransform.up * 1500;
                            breakTarget += Mathf.Sin((float)vessel.missionTime / 2) * vesselTransform.right * 1000 - Mathf.Cos((float)vessel.missionTime / 2) * vesselTransform.forward * 1000;
                            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($" from far side; engaging barrel roll");
                            RCSEvade(s, new Vector3(0, 1 * evasionNonlinearityDirection, 0));//add spacemode RCS dodging; flank incoming fire, flank U/D
                        }
                    }

                    float threatAltitudeDiff = Vector3.Dot(threatRelativePosition, vessel.upAxis);
                    if (threatAltitudeDiff > 500)
                        breakTarget += threatAltitudeDiff * vessel.upAxis;      //if it's trying to spike us from below, don't go crazy trying to dive below it
                    else
                        breakTarget += -150 * vessel.upAxis;   //dive a bit to escape

                    if (belowMinAltitude)
                    {
                        float rise = 0.5f * Mathf.Max(5f, (float)vessel.srfSpeed * 0.25f) * Mathf.Max(speedController.TWR, 1f); // Add some climb like in TakeOff (at half the rate) to get back above min altitude.
                        breakTarget += rise * upDirection;

                        float breakTargetVerticalComponent = Vector3.Dot(breakTarget, upDirection);
                        if (breakTargetVerticalComponent < 0) // If we're below minimum altitude, enforce the evade direction to gain altitude.
                        {
                            breakTarget += -2f * breakTargetVerticalComponent * upDirection;
                        }
                    }
                }

                breakTarget = GetLimitedClimbDirectionForSpeed(breakTarget);
                breakTarget += vessel.transform.position;
                FlyToPosition(s, FlightPosition(breakTarget, minAltitude));
                return;
            }

            Vector3 target = (vessel.srfSpeed < 200) ? FlightPosition(vessel.transform.position, minAltitude) : vesselTransform.position;
            float angleOff = Mathf.Sin(Time.time * 0.75f) * 180;
            angleOff = Mathf.Clamp(angleOff, -45, 45);
            target += Quaternion.AngleAxis(angleOff, upDirection) * vesselTransform.up.ProjectOnPlanePreNormalized(upDirection) * 500f;
            //+ (Mathf.Sin (Time.time/3) * upDirection * minAltitude/3);
            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Evading unknown attacker");
            FlyToPosition(s, target);
        }

        /*/// <summary>
        /// Calculates the appropriate notch direction based on the direction to the radar, "radarDir",
        /// the commanded "diveAngle" (radians, +ve down, 0 is level flight), and "breakDirection", the preferred
        /// direction of travel (as there are potential two solutions).
        /// </summary>
        /// <param name="breakDirection">Preferred direction of travel, does not need to be normalized.</param>
        /// <param name="radarDir">Direction to the radar, must be normalized.</param>
        /// <param name="diveAngle">Dive angle in radians</param>
        /// <returns>Vector to fly towards to notch the radar threat, normalized.</returns>
        Vector3 NotchDir(Vector3 breakDirection, Vector3 radarDir, float diveAngle)
        {
            // This function solves the system of equations in the form:
            // V_down dot V = cos(theta)
            // V_radar dot V = 0
            // ||V|| = 1

            // It essentially solves for the intersection of a plane and a cone, both centered at the origin,
            // where V_down is the cone's direction vector, and V_radar is the normal vector of the plane.
            // Because the solutions are lines, we close the system by enforcing unit-vector results.

            Vector3 down = -vessel.up;
            float cosCone = Mathf.Sin(diveAngle); // Using sin instead of cos as we want the angle relative to the down vector, cos(90 deg - diveAngle) = sin(diveAngle)

            float x1 = down.x;
            float y1 = down.y;
            float z1 = down.z;

            float x2 = radarDir.x;
            float y2 = radarDir.y;
            float z2 = radarDir.z;

            float dot = Vector3.Dot(down, radarDir);

            // Because the equation solved requires division by the appropriate component of the cross product, we
            // must check if it isn't 0.
            float crossx = y1 * z2 - z1 * y2;
            if (Mathf.Abs(crossx) > Mathf.Epsilon)
            {
                // If it is, we can solve for the two x-solutions
                float newCosCone = calcNotch(x1, x2, y2, z2, crossx, dot, cosCone, out float xsol1, out float xsol2);
                // Giving us the two vector solutions
                calcNotchYZ(x1, x2, y1, y2, z1, z2, xsol1, xsol2, crossx, newCosCone, out Vector3 sol1, out Vector3 sol2);
                // We pick the vector most aligned with breakDirection
                if (Vector3.Dot(breakDirection, sol1) > Vector3.Dot(breakDirection, sol2))
                {
                    return sol1;
                }
                else
                {
                    return sol2;
                }
            }

            // If the solving for the x-component isn't possible due to crossx being 0, we try y
            float crossy = z1 * x2 - x1 * z2;
            if (Mathf.Abs(crossy) > Mathf.Epsilon)
            {
                float newCosCone = calcNotch(y1, y2, x2, z2, crossy, dot, cosCone, out float ysol1, out float ysol2);
                calcNotchXZ(x1, x2, y1, y2, z1, z2, ysol1, ysol2, crossy, newCosCone, out Vector3 sol1, out Vector3 sol2);
                if (Vector3.Dot(breakDirection, sol1) > Vector3.Dot(breakDirection, sol2))
                {
                    return sol1;
                }
                else
                {
                    return sol2;
                }
            }

            // And if y is also zero we try z
            float crossz = x1 * y2 - y1 * x2;
            if (Mathf.Abs(crossz) > Mathf.Epsilon)
            {
                float newCosCone = calcNotch(z1, z2, x2, y2, crossz, dot, cosCone, out float zsol1, out float zsol2);
                calcNotchXY(x1, x2, y1, y2, z1, z2, zsol1, zsol2, crossz, newCosCone, out Vector3 sol1, out Vector3 sol2);
                if (Vector3.Dot(breakDirection, sol1) > Vector3.Dot(breakDirection, sol2))
                {
                    return sol1;
                }
                else
                {
                    return sol2;
                }
            }

            // If the down and radar vectors are in-line, then we have a planar solution, and we just return breakDirection
            return breakDirection;
        }

        /// <summary>
        /// This solves for the component corresponding to the d1/d2 values (I.E. x1/x2 solves for x) of the cone-plane intersection
        /// problem. a2 and b2 are the two other components of the normal vector of the plane (I.E. y2, z2 for x1/x2). Due to these
        /// being added, the order is irrelevant.
        /// Note that if a solution isn't found, then cosCone is adjusted until there is a single solution.
        /// </summary>
        /// <param name="d1">Component of the cone's vector corresponding to the component to be solved for.</param>
        /// <param name="d2">Component of the plane's normal vector corresponding to the component to be solved for.</param>
        /// <param name="a2">Other component of the plane's normal vector (I.E. y/z for x).</param>
        /// <param name="b2">Other component of the plane's normal vector (I.E. y/z for x).</param>
        /// <param name="crossd">Cross product component in the direction to solve for (cone cross plane).</param>
        /// <param name="dot">Dot product of the two vectors.</param>
        /// <param name="cosCone">Cosine of the cone's half-angle.</param>
        /// <param name="r1">Solution 1 of the problem.</param>
        /// <param name="r2">Solution 2 of the problem.</param>
        /// <returns></returns>
        float calcNotch(float d1, float d2, float a2, float b2, float crossd, float dot, float cosCone, out float r1, out float r2)
        {
            float denominator = (1.0f - dot * dot);
            float numerator = cosCone * (d1 - d2 * dot);
            float determinant = numerator * numerator - denominator * (cosCone * cosCone * (a2 * a2 + b2 * b2) - crossd * crossd);

            if (determinant < 0.0f)
            {
                if (determinant < -5e-5f)
                {
                    // Solve for cosCone required for determinant to be zero
                    cosCone = BDAMath.Sqrt(numerator * numerator / (denominator * (a2 * a2 + b2 * b2 - crossd * crossd)));
                    determinant = 0.0f;
                }
                else
                {
                    determinant = 0.0f;
                }
            }
            else
                determinant = BDAMath.Sqrt(determinant);

            r1 = (numerator - determinant) / denominator;
            r2 = (numerator + determinant) / denominator;
            return cosCone;
        }

        // Solves for the two other components of the solution given the X-component
        static void calcNotchYZ(float x1, float x2, float y1, float y2, float z1, float z2, float xsol1, float xsol2, float crossx, float cosCone, out Vector3 sol1, out Vector3 sol2)
        {
            crossx = 1.0f / crossx;
            float ysol1 = (z1 * x2 * xsol1 + z2 * (cosCone - x1 * xsol1)) * crossx;
            float zsol1 = -(y1 * x2 * xsol1 + y2 * (cosCone - x1 * xsol1)) * crossx;
            float ysol2 = (z1 * x2 * xsol2 + z2 * (cosCone - x1 * xsol2)) * crossx;
            float zsol2 = -(y1 * x2 * xsol2 + y2 * (cosCone - x1 * xsol2)) * crossx;

            sol1 = new Vector3(xsol1, ysol1, zsol1);
            sol2 = new Vector3(xsol2, ysol2, zsol2);
        }

        // Solves for the two other components of the solution given the Y-component
        static void calcNotchXZ(float x1, float x2, float y1, float y2, float z1, float z2, float ysol1, float ysol2, float crossy, float cosCone, out Vector3 sol1, out Vector3 sol2)
        {
            crossy = 1.0f / crossy;
            float xsol1 = -(z1 * y2 * ysol1 + z2 * (cosCone - y1 * ysol1)) * crossy;
            float zsol1 = (x1 * y2 * ysol1 + x2 * (cosCone - y1 * ysol1)) * crossy;
            float xsol2 = -(z1 * y2 * ysol2 + z2 * (cosCone - y1 * ysol2)) * crossy;
            float zsol2 = (x1 * y2 * ysol2 + x2 * (cosCone - y1 * ysol2)) * crossy;

            sol1 = new Vector3(xsol1, ysol1, zsol1);
            sol2 = new Vector3(xsol2, ysol2, zsol2);
        }

        // Solves for the two other components of the solution given the Z-component
        static void calcNotchXY(float x1, float x2, float y1, float y2, float z1, float z2, float zsol1, float zsol2, float crossz, float cosCone, out Vector3 sol1, out Vector3 sol2)
        {
            crossz = 1.0f / crossz;
            float xsol1 = (y1 * z2 * zsol1 + y2 * (cosCone - z1 * zsol1)) * crossz;
            float ysol1 = -(x1 * z2 * zsol1 + x2 * (cosCone - z1 * zsol1)) * crossz;
            float xsol2 = (y1 * z2 * zsol2 + y2 * (cosCone - z1 * zsol2)) * crossz;
            float ysol2 = -(x1 * z2 * zsol2 + x2 * (cosCone - z1 * zsol2)) * crossz;

            sol1 = new Vector3(xsol1, ysol1, zsol1);
            sol2 = new Vector3(xsol2, ysol2, zsol2);
        }*/

        Vector3 MissileKinematicEvasion(Vector3 breakDirection, Vector3 threatDirection)
        {
            breakDirection = breakDirection.normalized;
            Vector3 debugBreakDirection = breakDirection;
            string missileEvasionStatus;
            float newEvasionStateMult = (float)kinematicEvasionState + 2f;

            // Constants
            float safeDistMult = 5f;

            // Get missile information
            var weaponManager = WeaponManager;
            MissileBase missile = VesselModuleRegistry.GetMissileBase(weaponManager.incomingMissileVessel);
            float missileKinematicSpeed = missile.GetKinematicSpeed();
            float missileKinematicTime = missile.GetKinematicTime(missileKinematicSpeed, out float missileKinematicRange);
            float missileSpeed = (float)weaponManager.incomingMissileVessel.srfSpeed;
            float missileAccel = (missileKinematicSpeed - missileSpeed) / (missileKinematicTime == 0 ? Mathf.Sign(missileKinematicTime) * 0.001f : missileKinematicTime);
            float missileSafeDist = safeDistMult * missile.GetBlastRadius(); // Comfortable safe distance
            float missileSafeDistSqr = missileSafeDist * missileSafeDist;
            Vector3 missilePos = weaponManager.incomingMissileVessel.transform.position;
            Vector3 missileVel = weaponManager.incomingMissileVessel.Velocity();

            // Get current vessel information
            Vector3 currentPos = vesselTransform.position;
            float currentSpeed = (float)vessel.srfSpeed;
            Vector3 currentVel = vessel.Velocity();
            float currentAccel = speedController.GetPossibleAccel();
            float distToMissile = (currentPos - missilePos).magnitude;

            // Initialize maneuver variables
            Vector3 notchDir = breakDirection;
            Vector3 crankDir = breakDirection;
            Vector3 turnDir = breakDirection;
            Vector3 targetDir = (targetVessel != null) ?
                (targetVessel != missile.vessel ? (targetVessel.CoM - currentPos).normalized : (missile.SourceVessel.CoM - currentPos).normalized)
                : (missilePos - currentPos).normalized;
            float crankDistSqr = 0f;
            float notchDistSqr = 0f;
            float turnDistSqr = 0f;
            float crankTime = 0f;
            float notchTime = 0f;
            float turnTime = 0f;

            // Calculate estimated time to impact if we execute no maneuvers
            // if we are kin evading, see if current direction is sufficient, if so don't change anything
            float impactDistSqr = 0f;
            if (kinematicEvasionState > KinematicEvasionStates.None)
                impactDistSqr = vessel.PredictClosestApproachSqrSeparation(weaponManager.incomingMissileVessel, missileKinematicTime * 2f);

            // Turn to target / Turn hot (always evaluate to see if it is time to end maneuver)
            float targetDistSqr = KinematicManeuverEval(KinematicEvasionStates.ToTarget, breakDirection, threatDirection, targetDir, distToMissile,
                            currentPos, currentVel, currentSpeed, currentAccel, missilePos, missileVel, missileSpeed, missileAccel, missileKinematicSpeed, missileKinematicTime, missileKinematicRange,
                            out float targetTime, out targetDir);

            // Evaluate turning back towards target
            if (targetDistSqr > (kinematicEvasionState == KinematicEvasionStates.ToTarget ? 1f : newEvasionStateMult * 3f) * missileSafeDistSqr) // Be extra conservative when turning back towards missile
            {
                // Missile is defeated or probably won't hit us, we can turn back towards/stay on target to exit evasion
                breakDirection = targetDir;
                kinematicEvasionState = KinematicEvasionStates.ToTarget;
            }
            else if (impactDistSqr > missileSafeDistSqr) // If existing maneuver is sufficient, don't bother evaluating maneuver tree
            {
                breakDirection = KinematicManeuverDirection(kinematicEvasionState, breakDirection, threatDirection, targetDir);
            }
            else // Evaluate all maneuver options
            {
                // Crank
                if (kinematicEvasionState <= KinematicEvasionStates.Crank)
                {
                    crankDistSqr = KinematicManeuverEval(KinematicEvasionStates.Crank, breakDirection, threatDirection, targetDir, distToMissile,
                            currentPos, currentVel, currentSpeed, currentAccel, missilePos, missileVel, missileSpeed, missileAccel, missileKinematicSpeed, missileKinematicTime, missileKinematicRange,
                            out crankTime, out crankDir);
                }
                // Notch
                if (kinematicEvasionState <= KinematicEvasionStates.Notch)
                {
                    notchDistSqr = KinematicManeuverEval(KinematicEvasionStates.Notch, breakDirection, threatDirection, targetDir, distToMissile,
                            currentPos, currentVel, currentSpeed, currentAccel, missilePos, missileVel, missileSpeed, missileAccel, missileKinematicSpeed, missileKinematicTime, missileKinematicRange,
                            out notchTime, out notchDir);
                }
                // Turn Away / Turn Cold
                if (kinematicEvasionState <= KinematicEvasionStates.TurnAway)
                {
                    turnDistSqr = KinematicManeuverEval(KinematicEvasionStates.TurnAway, breakDirection, threatDirection, targetDir, distToMissile,
                            currentPos, currentVel, currentSpeed, currentAccel, missilePos, missileVel, missileSpeed, missileAccel, missileKinematicSpeed, missileKinematicTime, missileKinematicRange,
                            out turnTime, out turnDir);
                }

                // Pick best maneuver
                if (kinematicEvasionState <= KinematicEvasionStates.Crank && (crankDistSqr > (kinematicEvasionState == KinematicEvasionStates.Crank ? 1f : newEvasionStateMult) * missileSafeDistSqr))
                {
                    // Cranking will defeat missile, don't start cranking if we are executing a more conservative maneuver
                    breakDirection = crankDir;
                    kinematicEvasionState = KinematicEvasionStates.Crank;
                }
                else if (kinematicEvasionState <= KinematicEvasionStates.Notch && (notchDistSqr > (kinematicEvasionState == KinematicEvasionStates.Notch ? 1f : newEvasionStateMult) * missileSafeDistSqr))
                {
                    // Notching without a dive will defeat missile, don't start notching if we are executing a more conservative maneuver
                    breakDirection = notchDir;
                    kinematicEvasionState = KinematicEvasionStates.Notch;
                }
                else if (kinematicEvasionState <= KinematicEvasionStates.TurnAway && (turnDistSqr > (kinematicEvasionState == KinematicEvasionStates.TurnAway ? 1f : newEvasionStateMult) * missileSafeDistSqr))
                {
                    // We need to turn away and dive, don't start turning away if we are executing a more conservative maneuver
                    breakDirection = turnDir;
                    kinematicEvasionState = KinematicEvasionStates.TurnAway;
                }
                else //we need to dive and notch to have a chance against the missile
                {
                    breakDirection = notchDir;
                    kinematicEvasionState = KinematicEvasionStates.NotchDive;
                }
            }

            // DEBUGGING
            if (BDArmorySettings.DEBUG_AI || BDArmorySettings.DEBUG_TELEMETRY)
            {
                switch (kinematicEvasionState)
                {
                    case KinematicEvasionStates.ToTarget:
                        missileEvasionStatus = "Turning back towards target!";
                        break;
                    case KinematicEvasionStates.Crank:
                        missileEvasionStatus = "Cranking from missile threat!";
                        break;
                    case KinematicEvasionStates.Notch:
                        missileEvasionStatus = "Notching from missile threat!";
                        break;
                    case KinematicEvasionStates.TurnAway:
                        missileEvasionStatus = "Turning away from missile threat!";
                        break;
                    case KinematicEvasionStates.NotchDive:
                        missileEvasionStatus = "Notching and diving from missile threat";
                        break;
                    default:
                        missileEvasionStatus = "";
                        break;
                }

                notchDistSqr = KinematicManeuverEval(KinematicEvasionStates.Notch, debugBreakDirection, threatDirection, targetDir, distToMissile,
                            currentPos, currentVel, currentSpeed, currentAccel, missilePos, missileVel, missileSpeed, missileAccel, missileKinematicSpeed, missileKinematicTime, missileKinematicRange,
                            out notchTime, out notchDir);
                crankDistSqr = KinematicManeuverEval(KinematicEvasionStates.Crank, debugBreakDirection, threatDirection, targetDir, distToMissile,
                            currentPos, currentVel, currentSpeed, currentAccel, missilePos, missileVel, missileSpeed, missileAccel, missileKinematicSpeed, missileKinematicTime, missileKinematicRange,
                            out crankTime, out crankDir);
                turnDistSqr = KinematicManeuverEval(KinematicEvasionStates.TurnAway, debugBreakDirection, threatDirection, targetDir, distToMissile,
                            currentPos, currentVel, currentSpeed, currentAccel, missilePos, missileVel, missileSpeed, missileAccel, missileKinematicSpeed, missileKinematicTime, missileKinematicRange,
                            out turnTime, out turnDir);
                debugString.AppendLine($"Time to Impact; Target:{targetTime}s; Crank: {crankTime}s; Notch: {notchTime}s; Flee: {turnTime}s;");
                debugString.AppendLine($"Dist. @ Impact; Target: {BDAMath.Sqrt(targetDistSqr)}m, Crank: {BDAMath.Sqrt(crankDistSqr)}m; Notch: {BDAMath.Sqrt(notchDistSqr)}m; Flee: {BDAMath.Sqrt(turnDistSqr)}m;");
                debugString.AppendLine($"Msl Kin. Speed: {missileKinematicSpeed}m/s; Msl Kin. Time: {missileKinematicTime}s; Msl Kin. Range: {missileKinematicRange}m; Msl Safe Dist.: {missileSafeDist}m;");
                debugString.AppendLine(missileEvasionStatus);
            }

            return breakDirection;
        }

        float KinematicManeuverEval(KinematicEvasionStates state, Vector3 breakDirection, Vector3 threatDirection, Vector3 targetDir, float distToMissile, Vector3 currentPos, Vector3 currentVel, float currentSpeed, float currentAccel, Vector3 missilePos, Vector3 missileVel, float missileSpeed, float missileAccel, float missileKinematicSpeed, float missileKinematicTime, float missileKinematicRange, out float breakTime, out Vector3 finalBreakDirection)
        {
            breakDirection = KinematicManeuverDirection(state, breakDirection, threatDirection, targetDir); // Get direction for state
            finalBreakDirection = breakDirection;
            // Calculate initial estimate of time to impact and use that to estimate future positions, velocities, and accelerations
            float breakSpeed = currentSpeed;
            if (state != KinematicEvasionStates.ToTarget) // Don't penalize speed to turn towards target
                breakSpeed *= (Vector3.Angle(currentVel, breakDirection) / 360f); // Estimate speed after performing maneuver, ~50% speed loss to execute 180 deg turn
            Vector3 futureVel = breakSpeed * breakDirection;
            float timeToImpact = distToMissile / (futureVel - missileVel).magnitude;

            Vector3 futureAccel = currentAccel * breakDirection;
            Vector3 futurePos = AIUtils.PredictPosition(currentPos, futureVel, futureAccel, timeToImpact);

            // Evaluate TurnAway based on kinematic range
            if (state == KinematicEvasionStates.TurnAway)
            {
                float selfKinematicRangeSqr = (futurePos - currentPos).sqrMagnitude;
                breakTime = timeToImpact;
                if (selfKinematicRangeSqr < missileKinematicRange * missileKinematicRange) // If missile has greater kinematic range than we do turning away, maneuver will not work
                    return 0f;
                else // Otherwise maneuver is valid
                {
                    futurePos = GetTerrainSurfacePosition(futurePos) + (minAltitude * upDirection) - currentPos; // Incorporate dive towards deck for TurnAway state
                    finalBreakDirection = futurePos - currentPos;
                    return selfKinematicRangeSqr - missileKinematicRange * missileKinematicRange;
                }
            }

            // Update missile estimates based on our future position
            Vector3 missileDirNorm = (futurePos - missilePos).normalized;
            Vector3 futureMissileVel = missileSpeed * missileDirNorm;
            Vector3 futureMissileAccelVec = missileAccel * missileDirNorm;

            // Calculate time to CPA and square distance at CPA
            breakTime = AIUtils.TimeToCPA(currentPos - missilePos, futureVel - futureMissileVel, futureAccel - futureMissileAccelVec, missileKinematicTime * 2f);
            float breakDistSqr = 0f;
            if ((state != KinematicEvasionStates.ToTarget) || (breakTime * missileAccel + missileSpeed < missileKinematicSpeed)) // Only evaluate turning back to target if missile speed will be below kinematic speed at CPA
                breakDistSqr = AIUtils.PredictPosition(currentPos - missilePos, futureVel - futureMissileVel, futureAccel - futureMissileAccelVec, breakTime).sqrMagnitude;

            return breakDistSqr;
        }

        Vector3 KinematicManeuverDirection(KinematicEvasionStates state, Vector3 breakDirection, Vector3 threatDirection, Vector3 targetDir)
        {
            switch (state)
            {
                case KinematicEvasionStates.ToTarget: // Turn to target, ignoring missile
                    breakDirection = targetDir;
                    break;
                case KinematicEvasionStates.Crank: // Keep us within radar FOV of target, but don't fly straight towards missile
                    {
                        float crankAngle;
                        VesselRadarData vrd = vessel.gameObject.GetComponent<VesselRadarData>();
                        if (vrd)
                            crankAngle = Mathf.Clamp(vrd.GetCrankFOV() / 2 - 5f, 5f, 85f);
                        else
                            crankAngle = 60f;
                        breakDirection = Vector3.RotateTowards(breakDirection, threatDirection, (90f - crankAngle) * Mathf.Deg2Rad, 0).normalized;
                    }
                    break;
                case KinematicEvasionStates.TurnAway: // Turn 180 deg from missile
                    breakDirection = (-1f * threatDirection).ProjectOnPlanePreNormalized(upDirection).normalized;
                    break;
                default: // Notch, no change in breakDirection
                    {
                        // Previous implementation: beam missile initially, start to turn away as missile gets closer
                        /*float t = Mathf.Clamp01((WeaponManager.incomingMissileTime - WeaponManager.cmThreshold)/(WeaponManager.evadeThreshold - WeaponManager.cmThreshold));
                        t = -1.72f*t*t*t+4.06f*t*t-3.34f*t+1f;
                        float notchAngle = Mathf.Lerp(90f, 135f, t); // Gradually turn from 90 deg notch to 135 deg as missile gets closer
                        breakDirection = Vector3.RotateTowards(threatDirection, breakDirection, notchAngle * Mathf.Deg2Rad, 0).normalized;*/
                    }
                    break;
            }
            return breakDirection;
        }

        public void RCSEvade(FlightCtrlState s, Vector3 EvadeDir)
        {
            if (!BDArmorySettings.SPACE_HACKS || !vessel.InNearVacuum()) return;
            if (!vessel.ActionGroups[KSPActionGroup.RCS])
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, true);
            }
            //Vector3d RCS needs to be fed a vector based on the direction of dodging we need to do
            // grab list of engines on ship, find all that are independant throttle, find all that are pointed in the right direction(Vector3.Dot(thrustTransform, evadeDir)?
            //then activate them? Alternatively, method for letting non-ModuleRCS engines act like RCS?
            s.X = Mathf.Clamp((float)EvadeDir.x, -1, 1); //left/right
            s.Y = Mathf.Clamp((float)EvadeDir.z, -1, 1); //fore/aft
            s.Z = Mathf.Clamp((float)EvadeDir.y, -1, 1); //up/down
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
            if (vessel.LandedOrSplashed && vessel.srfSpeed < takeOffSpeed)
            {
                SetStatus(TakingOff ? "Taking off" : vessel.Splashed ? "Splashed" : "Landed");
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Taking off");
                if (vessel.Splashed)
                { vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, false); }
                assignedPositionWorld = vessel.transform.position;
                return;
            }
            SetStatus("Gain Alt. (" + (int)minAltitude + "m)");

            steerMode = TakingOff ? SteerModes.Aiming : SteerModes.NormalFlight;

            float radarAlt = vessel.Splashed ? 0 : (float)vessel.radarAltitude;
            if (TakingOff && radarAlt > terrainAlertDetectionRadius)
            {
                TakingOff = false;
            }

            Vector3 normalToUse;
            Vector3 forwardDirection = (vessel.horizontalSrfSpeed < 10 ? vesselTransform.up : (Vector3)vessel.srf_vel_direction) * 100; // Forward direction not adjusted for terrain.
            Vector3 forwardPoint = vessel.transform.position + forwardDirection * 100; // Forward point not adjusted for terrain.
            if (vessel.Splashed) // If the vessel is splashed, then the surface is flat.
            {
                normalToUse = upDirection;
            }
            else
            {
                // Get surface normal relative to our velocity direction below the vessel and where the vessel is heading.
                Ray ray = new(forwardPoint, relativeVelocityDownDirection); // Check ahead and below.
                Vector3 terrainBelowAheadNormal = Physics.Raycast(ray, out RaycastHit rayHit, minAltitude + 1.0f, (int)LayerMasks.Scenery) ? rayHit.normal : upDirection; // Terrain normal below point ahead.
                if (BDArmorySettings.SPACE_HACKS && vessel.InNearVacuum()) //no need to worry about stalling in null atmo
                {
                    if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Gaining altitude");
                    FlyToPosition(s, vessel.transform.position + terrainBelowAheadNormal * 100); //point nose perpendicular to surface for maximum vertical thrust.
                    return;
                }

                ray = new Ray(vessel.transform.position, relativeVelocityDownDirection); // Check here below.
                Vector3 terrainBelowNormal = Physics.Raycast(ray, out rayHit, minAltitude + 1.0f, (int)LayerMasks.Scenery) ? rayHit.normal : upDirection; // Terrain normal below here.
                normalToUse = Vector3.Dot(vessel.srf_vel_direction, terrainBelowNormal) < Vector3.Dot(vessel.srf_vel_direction, terrainBelowAheadNormal) ? terrainBelowNormal : terrainBelowAheadNormal; // Use the normal that has the steepest slope relative to our velocity.
            }
            forwardPoint = forwardDirection.ProjectOnPlanePreNormalized(normalToUse).normalized * 100; // Forward point adjusted for terrain relative to vessel.
            var alpha = Mathf.Clamp(0.9f + 0.1f * radarAlt / minAltitude, 0f, 0.99f);
            gainAltSmoothedForwardPoint = wasGainingAlt ? alpha * gainAltSmoothedForwardPoint + (1f - alpha) * forwardPoint : forwardPoint; // Adjust the forward point a bit more smoothly to avoid sudden jerks.
            gainingAlt = true;
            float rise = Mathf.Max(5f, 10f * (float)vessel.srfSpeed / takeOffSpeed) * 0.5f * (1f + Mathf.Max(speedController.TWR * Mathf.Clamp01(radarAlt / terrainAlertDetectionRadius), 1f)); // Scale climb rate by TWR (if >1 and not really close to terrain) to allow more powerful craft to climb faster.
            rise = Mathf.Min(rise, 1.5f * (defaultAltitude - radarAlt)); // Aim for at most 50% higher than the default altitude.
            if (TakingOff) // During the initial take-off, use a more gentle climb rate. 5°—10° at the take-off speed.
            { rise = Mathf.Min(rise, Mathf.Max(5f, 10f * (float)vessel.srfSpeed / takeOffSpeed) * 0.5f * (1f + Mathf.Clamp01(radarAlt / terrainAlertDetectionRadius))); }
            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Gaining altitude @ {Mathf.Rad2Deg * Mathf.Atan(rise / 100f):0.0}°");
            FlyToPosition(s, vessel.transform.position + gainAltSmoothedForwardPoint + upDirection * rise);
        }

        void UpdateTerrainAlertDetectionRadius(Vessel v)
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (v != vessel) return;
            terrainAlertDetectionRadius = Mathf.Min(2f * vessel.GetRadius(), minAltitude); // Don't go above the min altitude so we're not triggering terrain avoidance while cruising at min alt.
        }

        bool FlyAvoidTerrain(FlightCtrlState s) // Check for terrain ahead.
        {
            if (TakingOff) return false; // Don't do anything during the initial take-off.
            var vesselPosition = vessel.transform.position;
            var vesselSrfVelDir = vessel.srf_vel_direction;
            terrainAlertNormalColour = Color.green;
            terrainAlertDebugRays.Clear();

            ++terrainAlertTicker;
            int terrainAlertTickerThreshold = BDArmorySettings.TERRAIN_ALERT_FREQUENCY * (int)(1 + ((float)(vessel.radarAltitude * vessel.radarAltitude) / 250000.0f) / Mathf.Max(1.0f, (float)vessel.srfSpeed / 150.0f)); // Scale with altitude^2 / speed.
            if (terrainAlertTicker >= terrainAlertTickerThreshold)
            {
                terrainAlertTicker = 0;

                // Reset/initialise some variables.
                avoidingTerrain = false; // Reset the alert.
                if (vessel.radarAltitude > minAltitude)
                    belowMinAltitude = false; // Also, reset the belowMinAltitude alert if it's active because of avoiding terrain.
                terrainAlertDistance = float.MaxValue; // Reset the terrain alert distance.
                float turnRadiusTwiddleFactor = turnRadiusTwiddleFactorMax; // A twiddle factor based on the orientation of the vessel, since it often takes considerable time to re-orient before avoiding the terrain. Start with the worst value.
                terrainAlertThreatRange = turnRadiusTwiddleFactor * turnRadius + (float)vessel.srfSpeed * controlSurfaceDeploymentTime; // The distance to the terrain to consider.
                terrainAlertThreshold = 0; // Reset the threshold in case no threats are within range.

                // First, look 45° down, up, left and right from our velocity direction for immediate danger. (This should cover most immediate dangers.)
                Ray rayForwardUp = new Ray(vesselPosition, (vesselSrfVelDir - relativeVelocityDownDirection).normalized);
                Ray rayForwardDown = new Ray(vesselPosition, (vesselSrfVelDir + relativeVelocityDownDirection).normalized);
                Ray rayForwardLeft = new Ray(vesselPosition, (vesselSrfVelDir - relativeVelocityRightDirection).normalized);
                Ray rayForwardRight = new Ray(vesselPosition, (vesselSrfVelDir + relativeVelocityRightDirection).normalized);
                RaycastHit rayHit;
                if (Physics.Raycast(rayForwardDown, out rayHit, 1.4142f * terrainAlertDetectionRadius, (int)LayerMasks.Scenery))
                {
                    terrainAlertDistance = rayHit.distance * -Vector3.Dot(rayHit.normal, vesselSrfVelDir);
                    terrainAlertNormal = rayHit.normal;
                    if (BDArmorySettings.DEBUG_LINES) terrainAlertDebugRays.Add(new Ray(rayHit.point, rayHit.normal));
                }
                if (Physics.Raycast(rayForwardUp, out rayHit, 1.4142f * terrainAlertDetectionRadius, (int)LayerMasks.Scenery))
                {
                    var distance = rayHit.distance * -Vector3.Dot(rayHit.normal, vesselSrfVelDir);
                    if (distance < terrainAlertDistance)
                    {
                        terrainAlertDistance = distance;
                        terrainAlertNormal = rayHit.normal;
                    }
                    if (BDArmorySettings.DEBUG_LINES) terrainAlertDebugRays.Add(new Ray(rayHit.point, rayHit.normal));
                }
                if (Physics.Raycast(rayForwardLeft, out rayHit, 1.4142f * terrainAlertDetectionRadius, (int)LayerMasks.Scenery))
                {
                    var distance = rayHit.distance * -Vector3.Dot(rayHit.normal, vesselSrfVelDir);
                    if (distance < terrainAlertDistance)
                    {
                        terrainAlertDistance = distance;
                        terrainAlertNormal = rayHit.normal;
                    }
                    if (BDArmorySettings.DEBUG_LINES) terrainAlertDebugRays.Add(new Ray(rayHit.point, rayHit.normal));
                }
                if (Physics.Raycast(rayForwardRight, out rayHit, 1.4142f * terrainAlertDetectionRadius, (int)LayerMasks.Scenery))
                {
                    var distance = rayHit.distance * -Vector3.Dot(rayHit.normal, vesselSrfVelDir);
                    if (distance < terrainAlertDistance)
                    {
                        terrainAlertDistance = distance;
                        terrainAlertNormal = rayHit.normal;
                    }
                    if (BDArmorySettings.DEBUG_LINES) terrainAlertDebugRays.Add(new Ray(rayHit.point, rayHit.normal));
                }
                if (terrainAlertDistance < float.MaxValue)
                {
                    terrainAlertDirection = vesselSrfVelDir.ProjectOnPlanePreNormalized(terrainAlertNormal).normalized;
                    avoidingTerrain = true;
                }
                else
                {
                    // Next, cast a sphere forwards to check for upcoming dangers.
                    Ray ray = new Ray(vesselPosition, vesselSrfVelDir);
                    // For most terrain, the spherecast produces a single hit, but for buildings and special scenery (e.g., Kerbal Konstructs with multiple colliders), multiple hits are detected.
                    int hitCount = Physics.SphereCastNonAlloc(ray, terrainAlertDetectionRadius, terrainAvoidanceHits, terrainAlertThreatRange, (int)LayerMasks.Scenery);
                    if (hitCount == terrainAvoidanceHits.Length)
                    {
                        terrainAvoidanceHits = Physics.SphereCastAll(ray, terrainAlertDetectionRadius, terrainAlertThreatRange, (int)LayerMasks.Scenery);
                        hitCount = terrainAvoidanceHits.Length;
                    }
                    if (hitCount > 0) // Found something. 
                    {
                        Vector3 alertNormal = default;
                        using (var hits = terrainAvoidanceHits.Take(hitCount).GetEnumerator())
                            while (hits.MoveNext())
                            {
                                var alertDistance = hits.Current.distance * -Vector3.Dot(hits.Current.normal, vesselSrfVelDir); // Distance to terrain along direction of terrain normal.
                                if (alertDistance < terrainAlertDistance)
                                {
                                    terrainAlertDistance = alertDistance;
                                    if (BDArmorySettings.DEBUG_LINES) terrainAlertDebugPos = hits.Current.point;
                                }
                                if (hits.Current.collider.gameObject.GetComponentUpwards<DestructibleBuilding>() != null) // Hit a building.
                                {
                                    // Bias building hits towards the up direction to avoid diving into terrain.
                                    var normal = hits.Current.normal;
                                    var hitAltitude = BodyUtils.GetRadarAltitudeAtPos(hits.Current.point); // Note: this might not work properly for Kerbal Konstructs not built at ground level.
                                    if (hitAltitude < terrainAlertThreatRange)
                                    {
                                        normal = Vector3.RotateTowards(normal, upDirection, Mathf.Deg2Rad * 45f * (terrainAlertThreatRange - hitAltitude) / terrainAlertThreatRange, 0f);
                                    }
                                    alertNormal += normal / (1 + alertDistance * alertDistance);
                                    if (BDArmorySettings.DEBUG_LINES) terrainAlertDebugRays.Add(new Ray(hits.Current.point, normal));
                                }
                                else
                                {
                                    alertNormal += hits.Current.normal / (1 + alertDistance * alertDistance); // Normalise multiple hits by distance^2.
                                    if (BDArmorySettings.DEBUG_LINES) terrainAlertDebugRays.Add(new Ray(hits.Current.point, hits.Current.normal));
                                }
                            }
                        terrainAlertNormal = alertNormal.normalized;
                        if (BDArmorySettings.DEBUG_LINES) terrainAlertDebugDir = terrainAlertNormal;
                        // Bias the alert direction by the Immelmann pitch-up bias to favour pulling up if approaching near vertically.
                        terrainAlertDirection = Vector3.RotateTowards(vesselSrfVelDir, vesselTransform.up, Mathf.Deg2Rad * ImmelmannPitchUpBias, 0).ProjectOnPlanePreNormalized(terrainAlertNormal).normalized;
                        float sinTheta = Math.Min(0.0f, Vector3.Dot(vesselSrfVelDir, terrainAlertNormal)); // sin(theta) (measured relative to the plane of the surface).
                        float oneMinusCosTheta = 1.0f - BDAMath.Sqrt(Math.Max(0.0f, 1.0f - sinTheta * sinTheta));
                        turnRadiusTwiddleFactor = (turnRadiusTwiddleFactorMin + turnRadiusTwiddleFactorMax) / 2.0f - (turnRadiusTwiddleFactorMax - turnRadiusTwiddleFactorMin) / 2.0f * Vector3.Dot(terrainAlertNormal, -vessel.transform.forward); // This would depend on roll rate (i.e., how quickly the vessel can reorient itself to perform the terrain avoidance maneuver) and probably other things.
                        float controlLagCompensation = Mathf.Max(0f, -Vector3.Dot(AIUtils.PredictPosition(vessel, controlSurfaceDeploymentTime) - vesselPosition, terrainAlertNormal)); // Use the same deploy time as for the threat range above.
                        terrainAlertThreshold = turnRadiusTwiddleFactor * turnRadius * oneMinusCosTheta + controlLagCompensation;
                        if (terrainAlertDistance < terrainAlertThreshold) // Only do something about it if the estimated turn amount is a problem.
                            avoidingTerrain = true;
                    }
                }
                // Finally, check the distance to sea-level as water doesn't act like a collider, so it's getting ignored. Also, for planets without surfaces.
                if (vessel.mainBody.ocean || !vessel.mainBody.hasSolidSurface)
                {
                    float sinTheta = Vector3.Dot(vesselSrfVelDir, upDirection); // sin(theta) (measured relative to the ocean surface).
                    if (sinTheta < 0f) // Heading downwards
                    {
                        float oneMinusCosTheta = 1.0f - BDAMath.Sqrt(Math.Max(0.0f, 1.0f - sinTheta * sinTheta));
                        turnRadiusTwiddleFactor = (turnRadiusTwiddleFactorMin + turnRadiusTwiddleFactorMax) / 2.0f - (turnRadiusTwiddleFactorMax - turnRadiusTwiddleFactorMin) / 2.0f * Vector3.Dot(upDirection, -vessel.transform.forward); // This would depend on roll rate (i.e., how quickly the vessel can reorient itself to perform the terrain avoidance maneuver) and probably other things.
                        float controlLagCompensation = Mathf.Max(0f, -Vector3.Dot(AIUtils.PredictPosition(vessel, controlSurfaceDeploymentTime) - vesselPosition, upDirection));
                        terrainAlertThreshold = turnRadiusTwiddleFactor * turnRadius * oneMinusCosTheta + controlLagCompensation;

                        if ((float)vessel.altitude < terrainAlertThreshold && (float)vessel.altitude < terrainAlertDistance) // If the ocean surface is closer than the terrain (if any), then override the terrain alert values.
                        {
                            terrainAlertDistance = (float)vessel.altitude;
                            terrainAlertNormal = upDirection;
                            terrainAlertNormalColour = Color.blue;
                            terrainAlertDirection = Vector3.RotateTowards(vesselSrfVelDir, vesselTransform.up, Mathf.Deg2Rad * ImmelmannPitchUpBias, 0).ProjectOnPlanePreNormalized(upDirection).normalized;
                            avoidingTerrain = true;

                            if (BDArmorySettings.DEBUG_LINES)
                            {
                                terrainAlertDebugPos = vesselPosition + vesselSrfVelDir * (float)vessel.altitude / -sinTheta;
                                terrainAlertDebugDir = upDirection;
                            }
                        }
                    }
                }
            }

            if (avoidingTerrain)
            {
                belowMinAltitude = true; // Inform other parts of the code to behave as if we're below minimum altitude.

                float maxAngle = Mathf.Clamp(maxAllowedAoA, 45f, 70f) * Mathf.Deg2Rad; // Maximum angle (towards surface normal) to aim.
                float adjustmentFactor = 1f; // Mathf.Clamp(1.0f - Mathf.Pow(terrainAlertDistance / terrainAlertThreatRange, 2.0f), 0.0f, 1.0f); // Don't yank too hard as it kills our speed too much. (This doesn't seem necessary.)
                                             // First, aim up to maxAngle towards the surface normal.
                if (BDArmorySettings.SPACE_HACKS) //no need to worry about stalling in null atmo
                {
                    FlyToPosition(s, vesselPosition + terrainAlertNormal * 100); //so point nose perpendicular to surface for maximum vertical thrust.
                }
                else
                {
                    Vector3 correctionDirection = Vector3.RotateTowards(terrainAlertDirection, terrainAlertNormal, maxAngle * adjustmentFactor, 0.0f);
                    // Then, adjust the vertical pitch for our speed (to try to avoid stalling).
                    Vector3 horizontalCorrectionDirection = correctionDirection.ProjectOnPlanePreNormalized(upDirection).normalized;
                    correctionDirection = Vector3.RotateTowards(correctionDirection, horizontalCorrectionDirection, Mathf.Max(0.0f, (1.0f - (float)vessel.srfSpeed / 120.0f) * 0.8f * maxAngle) * adjustmentFactor, 0.0f); // Rotate up to 0.8*maxAngle back towards horizontal depending on speed < 120m/s.
                    FlyToPosition(s, vesselPosition + correctionDirection * 100);
                }
                if (postTerrainAvoidanceCoolDownDuration > 0) postTerrainAvoidanceCoolDownTimer = 0;
                steerMode = SteerModes.Manoeuvering;
                // Update status and book keeping.
                SetStatus("Terrain (" + (int)terrainAlertDistance + "m)");
                return true;
            }

            // Hurray, we've avoided the terrain!
            avoidingTerrain = false;
            if (postTerrainAvoidanceCoolDownTimer >= 0)
            {
                postTerrainAvoidanceCoolDownTimer += TimeWarp.fixedDeltaTime;
                if (postTerrainAvoidanceCoolDownTimer >= postTerrainAvoidanceCoolDownDuration)
                    postTerrainAvoidanceCoolDownTimer = -1f;
            }
            return false;
        }

        bool FlyAvoidOthers(FlightCtrlState s) // Check for collisions with other vessels and try to avoid them.
        {
            if (vesselCollisionAvoidanceStrength == 0 || collisionAvoidanceThreshold == 0) return false;
            float ignoreMinDistanceSqr = vessel.GetRadius();
            ignoreMinDistanceSqr *= ignoreMinDistanceSqr;
            if (currentlyAvoidedVessel != null) // Avoidance has been triggered.
            {
                SetStatus("AvoidCollision");
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Avoiding Collision");

                // Monitor collision avoidance, adjusting or stopping as necessary.
                if (currentlyAvoidedVessel != null && PredictCollisionWithVessel(currentlyAvoidedVessel, vesselCollisionAvoidanceLookAheadPeriod * 1.2f, out collisionAvoidDirection, ignoreMinDistanceSqr)) // *1.2f for hysteresis.
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
                VesselType collisionVesselType = VesselType.Unknown; // Start as not debris.
                float collisionTargetLargestSize = -1f;
                collisionAvoidDirection = vessel.srf_vel_direction;
                // First pass, only consider valid vessels.
                using (var vs = BDATargetManager.LoadedVessels.GetEnumerator())
                    while (vs.MoveNext())
                    {
                        if (vs.Current == null) continue;
                        if (vs.Current.vesselType == VesselType.Debris) continue; // Ignore debris on the first pass.
                        if (vs.Current == vessel || vs.Current.Landed) continue;
                        if (!PredictCollisionWithVessel(vs.Current, vesselCollisionAvoidanceLookAheadPeriod, out Vector3 collisionAvoidDir, ignoreMinDistanceSqr)) continue;
                        if (!VesselModuleRegistry.IgnoredVesselTypes.Contains(vs.Current.vesselType))
                        {
                            var ibdaiControl = vs.Current.ActiveController().AI;
                            if (ibdaiControl != null && ibdaiControl.currentCommand == PilotCommands.Follow && ibdaiControl.commandLeader != null && ibdaiControl.commandLeader.vessel == vessel) continue;
                        }
                        var collisionTargetSize = vs.Current.vesselSize.sqrMagnitude; // We're only interested in sorting by size, which is much faster than sorting by mass.
                        if (collisionVesselType == vs.Current.vesselType && collisionTargetSize < collisionTargetLargestSize) continue; // Avoid the largest object.
                        vesselCollision = true;
                        currentlyAvoidedVessel = vs.Current;
                        collisionAvoidDirection = collisionAvoidDir;
                        collisionVesselType = vs.Current.vesselType;
                        collisionTargetLargestSize = collisionTargetSize;
                    }
                // Second pass, only consider debris.
                if (!vesselCollision)
                {
                    using var vs = BDATargetManager.LoadedVessels.GetEnumerator();
                    while (vs.MoveNext())
                    {
                        if (vs.Current == null) continue;
                        if (vs.Current.vesselType != VesselType.Debris) continue; // Only consider debris on the second pass.
                        if (vs.Current == vessel || vs.Current.Landed) continue;
                        if (!PredictCollisionWithVessel(vs.Current, vesselCollisionAvoidanceLookAheadPeriod, out Vector3 collisionAvoidDir, ignoreMinDistanceSqr)) continue;
                        var collisionTargetSize = vs.Current.vesselSize.sqrMagnitude;
                        if (collisionTargetSize < collisionTargetLargestSize) continue; // Avoid the largest debris object.
                        vesselCollision = true;
                        currentlyAvoidedVessel = vs.Current;
                        collisionAvoidDirection = collisionAvoidDir;
                        collisionVesselType = vs.Current.vesselType;
                        collisionTargetLargestSize = collisionTargetSize;
                    }
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
            steerMode = SteerModes.Manoeuvering;
            FlyToPosition(s, vesselTransform.position + Quaternion.AngleAxis(-vesselCollisionAvoidanceStrength, axis) * (flyingToPosition - vesselTransform.position)); // Rotate the flyingToPosition around the axis by the collision avoidance strength (each frame).
        }

        Vector3 GetLimitedClimbDirectionForSpeed(Vector3 direction)
        {
            if (Vector3.Dot(direction, upDirection) < 0)
            {
                return direction; //only use this if climbing
            }

            Vector3 planarDirection = direction.ProjectOnPlanePreNormalized(upDirection);
            float speedRatio = (float)vessel.srfSpeed / minSpeed;
            float angleLimit = Mathf.Clamp(speedRatio * (speedController.TWR + 1f) * (speedController.TWR + speedRatio), 5, 90);
            if (VectorUtils.Angle(direction, planarDirection) < angleLimit)
            {
                return direction; // We're within the limit.
            }

            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"climb limit angle: {angleLimit}");
            return Vector3.RotateTowards(planarDirection, direction, angleLimit * Mathf.Deg2Rad, 0);
        }

        void UpdateGAndAoALimits(FlightCtrlState s)
        {
            if (vessel.dynamicPressurekPa <= 0 || vessel.InNearVacuum() || vessel.LandedOrSplashed) return; // Only measure when airborne and in sufficient atmosphere.

            if (lastAllowedAoA != maxAllowedAoA)
            {
                lastAllowedAoA = maxAllowedAoA;
                maxAllowedSinAoA = (float)Mathf.Sin(lastAllowedAoA * Mathf.Deg2Rad);
            }
            float pitchG = -Vector3.Dot(vessel.acceleration, vessel.ReferenceTransform.forward);       //should provide g force in vessel up / down direction, assuming a standard plane
            float pitchGPerDynPres = pitchG / (float)vessel.dynamicPressurekPa;

            float curSinAoA = Vector3.Dot(vessel.Velocity().normalized, vessel.ReferenceTransform.forward);

            //adjust moving averages
            smoothedGLoad.Update(pitchGPerDynPres);
            var gLoad = smoothedGLoad.Value;
            var gLoadPred = smoothedGLoad.At(0.1f);
            if (BDArmorySettings.DEBUG_AI || BDArmorySettings.DEBUG_TELEMETRY) debugString.AppendLine($"G: {pitchG / VehiclePhysics.Gravity.reference:F1}, G-Load: current {pitchGPerDynPres:F3}, smoothed {gLoad:F3}, pred +0.1s {gLoadPred:F3} ({gLoadPred * vessel.dynamicPressurekPa / VehiclePhysics.Gravity.reference:F1}G)");

            smoothedSinAoA.Update(curSinAoA);
            var sinAoA = smoothedSinAoA.Value;
            var sinAoAPred = smoothedSinAoA.At(0.1f);
            if (BDArmorySettings.DEBUG_AI || BDArmorySettings.DEBUG_TELEMETRY) debugString.AppendLine($"AoA: current: {Mathf.Rad2Deg * Mathf.Asin(curSinAoA):F2}°, smoothed {Mathf.Rad2Deg * Mathf.Asin(sinAoA):F2}°, pred +0.1s {Mathf.Rad2Deg * Mathf.Asin(sinAoAPred):F2}°"); // Note: sinAoA can go beyond ±1, giving NaN in the debug line.

            if (gLoadPred < maxNegG || Math.Abs(sinAoAPred - sinAoAAtMaxNegG) < 0.005f)
            {
                maxNegG = gLoadPred;
                sinAoAAtMaxNegG = sinAoAPred;
            }
            if (gLoadPred > maxPosG || Math.Abs(sinAoAPred - sinAoAAtMaxPosG) < 0.005f)
            {
                maxPosG = gLoadPred;
                sinAoAAtMaxPosG = sinAoAPred;
            }

            if (sinAoAAtMaxNegG >= sinAoAAtMaxPosG)
            {
                sinAoAAtMaxNegG = sinAoAAtMaxPosG = maxNegG = maxPosG = 0;
                gOffsetPerDynPres = gAoASlopePerDynPres = 0;
                return;
            }

            if (command != PilotCommands.Waypoints) // Don't decay the highest recorded G-force when following waypoints as we're likely to be heading in straight lines for longer periods.
                dynDynPresGRecorded *= dynDecayRate; // Decay the highest observed G-force from dynamic pressure (we want a fairly recent value in case the planes dynamics have changed).
            if (!vessel.LandedOrSplashed && Math.Abs(gLoadPred) > dynDynPresGRecorded)
                dynDynPresGRecorded = Math.Abs(gLoadPred);
            dynUserSteerLimitMax = Mathf.Max(userSteerLimit, dynDecayRate * dynUserSteerLimitMax, 0.1f); // Recent-ish max user-defined steer limit, clamped to at least 0.1. Decays at the same rate as dynamic pressure for consistency.

            if (!vessel.LandedOrSplashed)
            {
                dynVelocityMagSqr = dynVelocityMagSqr * dynVelSmoothingCoef + (1f - dynVelSmoothingCoef) * (float)vessel.Velocity().sqrMagnitude; // Smooth the recently measured speed for determining the turn radius.
            }

            float AoADiff = Mathf.Max(sinAoAAtMaxPosG - sinAoAAtMaxNegG, 0.001f); // Avoid divide-by-zero.

            gAoASlopePerDynPres = (maxPosG - maxNegG) / AoADiff;
            gOffsetPerDynPres = maxPosG - gAoASlopePerDynPres * sinAoAAtMaxPosG;     //g force offset
        }

        void AdjustPitchForGAndAoALimits(FlightCtrlState s)
        {
            float minSinAoA = 0, maxSinAoA = 0, curSinAoA = 0;
            float negPitchDynPresLimit = 0, posPitchDynPresLimit = 0;

            if (vessel.LandedOrSplashed || vessel.srfSpeed < Math.Min(minSpeed, takeOffSpeed))         //if we're going too slow, don't use this
            {
                float speed = Math.Max(takeOffSpeed, minSpeed);
                negPitchDynPresLimitIntegrator = -1f * 0.001f * 0.5f * 1.225f * speed * speed;
                posPitchDynPresLimitIntegrator = 1f * 0.001f * 0.5f * 1.225f * speed * speed;
                return;
            }

            float invVesselDynPreskPa = 1f / (float)vessel.dynamicPressurekPa;

            if (maxAllowedAoA < 90)
            {
                maxSinAoA = maxAllowedGForce * bodyGravity * invVesselDynPreskPa;
                minSinAoA = -maxSinAoA;

                maxSinAoA -= gOffsetPerDynPres;
                minSinAoA -= gOffsetPerDynPres;

                maxSinAoA /= gAoASlopePerDynPres;
                minSinAoA /= gAoASlopePerDynPres;

                if (maxSinAoA > maxAllowedSinAoA)
                    maxSinAoA = maxAllowedSinAoA;

                if (minSinAoA < -maxAllowedSinAoA)
                    minSinAoA = -maxAllowedSinAoA;

                curSinAoA = Vector3.Dot(vessel.Velocity().normalized, vessel.ReferenceTransform.forward);

                float centerSinAoA = (minSinAoA + maxSinAoA) * 0.5f;
                float curSinAoACentered = curSinAoA - centerSinAoA;
                float sinAoADiff = Mathf.Max(0.5f * Math.Abs(maxSinAoA - minSinAoA), 0.001f); // Avoid divide-by-zero.
                float curSinAoANorm = curSinAoACentered / sinAoADiff;      //scaled so that from centerAoA to maxAoA is 1

                float negPitchScalar, posPitchScalar;
                negPitchScalar = negPitchDynPresLimitIntegrator * invVesselDynPreskPa - lastPitchInput;
                posPitchScalar = lastPitchInput - posPitchDynPresLimitIntegrator * invVesselDynPreskPa;

                //update pitch control limits as needed
                negPitchDynPresLimit = posPitchDynPresLimit = 0;
                if (curSinAoANorm < -0.15f)
                {
                    float sinAoAOffset = curSinAoANorm + 1;     //set max neg aoa to be 0
                    float AoALimScalar = Math.Abs(curSinAoANorm);
                    AoALimScalar *= AoALimScalar;
                    AoALimScalar *= AoALimScalar;
                    AoALimScalar *= AoALimScalar;
                    if (AoALimScalar > 1)
                        AoALimScalar = 1;

                    float pitchInputScalar = negPitchScalar;
                    pitchInputScalar = 1 - Mathf.Clamp01(Math.Abs(pitchInputScalar));
                    pitchInputScalar *= pitchInputScalar;
                    pitchInputScalar *= pitchInputScalar;
                    pitchInputScalar *= pitchInputScalar;
                    if (pitchInputScalar < 0)
                        pitchInputScalar = 0;

                    float deltaSinAoANorm = curSinAoA - lastSinAoA;
                    deltaSinAoANorm /= sinAoADiff;

                    if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Updating Neg Gs");
                    negPitchDynPresLimitIntegrator -= 0.01f * Mathf.Clamp01(AoALimScalar + pitchInputScalar) * sinAoAOffset * (float)vessel.dynamicPressurekPa;
                    negPitchDynPresLimitIntegrator -= 0.005f * deltaSinAoANorm * (float)vessel.dynamicPressurekPa;
                    if (sinAoAOffset < 0)
                        negPitchDynPresLimit = -0.3f * sinAoAOffset;
                }
                if (curSinAoANorm > 0.15f)
                {
                    float sinAoAOffset = curSinAoANorm - 1;     //set max pos aoa to be 0
                    float AoALimScalar = Math.Abs(curSinAoANorm);
                    AoALimScalar *= AoALimScalar;
                    AoALimScalar *= AoALimScalar;
                    AoALimScalar *= AoALimScalar;
                    if (AoALimScalar > 1)
                        AoALimScalar = 1;

                    float pitchInputScalar = posPitchScalar;
                    pitchInputScalar = 1 - Mathf.Clamp01(Math.Abs(pitchInputScalar));
                    pitchInputScalar *= pitchInputScalar;
                    pitchInputScalar *= pitchInputScalar;
                    pitchInputScalar *= pitchInputScalar;
                    if (pitchInputScalar < 0)
                        pitchInputScalar = 0;

                    float deltaSinAoANorm = curSinAoA - lastSinAoA;
                    deltaSinAoANorm /= sinAoADiff;

                    if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Updating Pos Gs");
                    posPitchDynPresLimitIntegrator -= 0.01f * Mathf.Clamp01(AoALimScalar + pitchInputScalar) * sinAoAOffset * (float)vessel.dynamicPressurekPa;
                    posPitchDynPresLimitIntegrator -= 0.005f * deltaSinAoANorm * (float)vessel.dynamicPressurekPa;
                    if (sinAoAOffset > 0)
                        posPitchDynPresLimit = -0.3f * sinAoAOffset;
                }
            }

            float currentG = -Vector3.Dot(vessel.acceleration, vessel.ReferenceTransform.forward);
            float negLim, posLim;
            negLim = !vessel.InNearVacuum() ? negPitchDynPresLimitIntegrator * invVesselDynPreskPa + negPitchDynPresLimit : -1;
            if (negLim > s.pitch)
            {
                if (currentG > -(maxAllowedGForce * 0.97f * bodyGravity))
                {
                    negPitchDynPresLimitIntegrator -= (float)(0.15 * vessel.dynamicPressurekPa);        //just an override in case things break

                    maxNegG = currentG * invVesselDynPreskPa;
                    sinAoAAtMaxNegG = curSinAoA;

                    negPitchDynPresLimit = 0;
                }

                SetFlightControlState(s, negLim, s.yaw, s.roll);
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Limiting Neg Gs");
            }
            posLim = !vessel.InNearVacuum() ? posPitchDynPresLimitIntegrator * invVesselDynPreskPa + posPitchDynPresLimit : 1;
            if (posLim < s.pitch)
            {
                if (currentG < (maxAllowedGForce * 0.97f * bodyGravity))
                {
                    posPitchDynPresLimitIntegrator += (float)(0.15 * vessel.dynamicPressurekPa);        //just an override in case things break

                    maxPosG = currentG * invVesselDynPreskPa;
                    sinAoAAtMaxPosG = curSinAoA;

                    posPitchDynPresLimit = 0;
                }

                SetFlightControlState(s, posLim, s.yaw, s.roll);
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Limiting Pos Gs");
            }

            lastPitchInput = s.pitch;
            lastSinAoA = curSinAoA;

            // if ((BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) && negLim>posLim) debugString.AppendLine($"Bad limits: curSinAoA: {curSinAoA}, sinAoADiff: {sinAoADiff}, : curSinAoANorm: {curSinAoANorm}, maxAllowedAoA: {maxAllowedAoA}, maxAllowedSinAoA: {maxAllowedSinAoA}");
            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine(string.Format("Final Pitch: {0,7:F4}  (Limits: {1,7:F4} — {2,6:F4})", s.pitch, negLim, posLim));
        }

        void CalculateAccelerationAndTurningCircle()
        {
            maxLiftAcceleration = dynDynPresGRecorded * (float)vessel.dynamicPressurekPa; //maximum acceleration from lift that the vehicle can provide

            maxLiftAcceleration = Mathf.Clamp(maxLiftAcceleration, bodyGravity, maxAllowedGForce * bodyGravity); //limit it to whichever is smaller, what we can provide or what we can handle. Assume minimum of 1G to avoid extremely high turn radiuses.

            // Radius that we can turn in assuming constant velocity, assuming simple circular motion (note: this is a terrible assumption, the AI usually turns on afterboosters!)
            turnRadius = dynVelocityMagSqr / maxLiftAcceleration / (userSteerLimit / dynUserSteerLimitMax);
            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI)
            {
                debugString.AppendLine($"Turn Radius: {turnRadius:0}m (max lift acc: {maxLiftAcceleration:0}m/s²), terrain threat range: {turnRadiusTwiddleFactorMax * turnRadius + (float)vessel.srfSpeed * controlSurfaceDeploymentTime:0}m, threshold: {terrainAlertThreshold:0}m");
            }
        }

        void CheckFlatSpin()
        {
            // Checks to see if craft has a yaw rate of > 20 deg/s with pitch/roll being less (flat spin) for longer than 2s, if so sets the FlatSpin flag which will trigger
            // RegainEnergy with throttle set to idle.

            float spinRate = vessel.angularVelocity.z;
            if ((Mathf.Abs(spinRate) > 0.35f) && (Mathf.Abs(spinRate) > Mathf.Max(Mathf.Abs(vessel.angularVelocity.x), Mathf.Abs(vessel.angularVelocity.y))))
            {
                if (flatSpinStartTime == float.MaxValue)
                    flatSpinStartTime = Time.time;

                if ((Time.time - flatSpinStartTime) > 2f)
                {
                    FlatSpin = Mathf.Sign(spinRate); // 1 for counter-clockwise, -1 for clockwise
                    if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI)
                        debugString.AppendLine($"Flat Spin Detected, {spinRate * 180f / Mathf.PI} deg/s, {(Time.time - flatSpinStartTime)}s");
                }
            }
            else
            {
                FlatSpin = 0; // No flat spin, set to zero
                flatSpinStartTime = float.MaxValue;
            }
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
            Vector3 vesselTransformPos = vesselTransform.position;
            Vector3 forwardDirection = vesselTransform.up;
            Vector3 targetDirection = (targetPosition - vesselTransformPos).normalized;
            float targetDistance = (targetPosition - vesselTransformPos).magnitude;

            float vertFactor = 0;
            vertFactor += ((float)vessel.srfSpeed / minSpeed - 2f) * 0.3f; //speeds greater than 2x minSpeed encourage going upwards; below encourages downwards
            vertFactor += (targetDistance / 1000f - 1f) * 0.3f; //distances greater than 1000m encourage going upwards; closer encourages going downwards
            vertFactor -= Mathf.Clamp01(Vector3.Dot(vesselTransformPos - targetPosition, upDirection) / 1600f - 1f) * 0.5f; //being higher than 1600m above a target encourages going downwards
            if (targetVessel)
                vertFactor += Vector3.Dot(targetVessel.Velocity() / targetVessel.srfSpeed, (targetVessel.CoM - vesselTransformPos).normalized) * 0.3f; //the target moving away from us encourages upward motion, moving towards us encourages downward motion
            else
                vertFactor += 0.4f;
            var weaponManager = WeaponManager;
            if (weaponManager && weaponManager.underFire) vertFactor -= 0.5f; //being under fire encourages going downwards as well, to gain energy

            float alt = (float)vessel.radarAltitude;
            vertFactor = Mathf.Clamp(vertFactor, -2, 2);
            vertFactor += 0.15f * Mathf.Sin((float)vessel.missionTime * 0.25f); //some randomness in there

            Vector3 projectedDirection = forwardDirection.ProjectOnPlanePreNormalized(upDirection);
            Vector3 projectedTargetDirection = targetDirection.ProjectOnPlanePreNormalized(upDirection);
            var cosAngle = Vector3.Dot(targetDirection, forwardDirection);
            invertRollTarget = false;
            if (cosAngle < -1e-8f)
            {
                if (
                    (canExtend && targetDistance > BankedTurnDistance) // For long-range turning, do a banked turn (horizontal) instead to conserve energy, but only if extending is allowed.
                    || (isBombing && !DivebombStarted)) // Or for doing a level bombing run as height changes mess with the approach.
                {
                    targetPosition = vesselTransformPos + Vector3.Cross(Vector3.Cross(projectedDirection, projectedTargetDirection), projectedDirection).normalized * 200;
                }
                else
                {
                    if (cosAngle < ImmelmannTurnCosAngle) // Otherwise, if the target is almost directly behind, do an Immelmann turn.
                    {
                        bool pitchUp = vessel.radarAltitude < minAltitude + 2f * turnRadiusTwiddleFactorMax * turnRadius ? vessel.angularVelocity.x < 0.05f : // Avoid oscillations at low altitude.
                            Mathf.Abs(vessel.angularVelocity.x) < Mathf.Abs(Mathf.Deg2Rad * ImmelmannPitchUpBias) ? ImmelmannPitchUpBias > -0.1f : // Otherwise, if not rotating much, pitch up (or down if biased negatively).
                            vessel.angularVelocity.x < 0; // Otherwise, go with the current pitching direction.

                        targetDirection = Vector3.RotateTowards(-vesselTransform.up, pitchUp ? -vesselTransform.forward : vesselTransform.forward, Mathf.Deg2Rad * ImmelmannTurnAngle, 0); // If the target is in our blind spot, just pitch up (or down) to get a better view (initial part of an Immelmann turn).
                        invertRollTarget = Vector3.Dot(targetDirection, vesselTransform.forward) > 0; // Target is behind and below, pitch down first then roll up.
                    }
                    targetPosition = vesselTransformPos + Vector3.Cross(Vector3.Cross(forwardDirection, targetDirection), forwardDirection).normalized * 200; // Make the target position 90° from vesselTransform.up.
                }
            }
            else if (steerMode == SteerModes.NormalFlight)
            {
                float distance = (targetPosition - vesselTransformPos).magnitude;
                if (vertFactor < 0)
                    distance = Math.Min(distance, Math.Abs((alt - minAlt) / vertFactor));

                targetPosition += upDirection * Math.Min(distance, 1000) * Mathf.Clamp(vertFactor * Mathf.Clamp01(0.7f - Math.Abs(Vector3.Dot(projectedTargetDirection, projectedDirection))), -0.5f, 0.5f);
                if (maxAltitudeEnabled)
                {
                    var targetRadarAlt = BDArmorySettings.COMPETITION_ALTITUDE__LIMIT_ASL ? FlightGlobals.getAltitudeAtPos(targetPosition) : BodyUtils.GetRadarAltitudeAtPos(targetPosition);
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

            float pointRadarAlt = BodyUtils.GetRadarAltitudeAtPos(targetPosition, true); //return 0 when over water
            if (pointRadarAlt < minAlt)//  && !isBombing)
            {
                float adjustment = (minAlt - pointRadarAlt);
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Target position is below minAlt. Adjusting by {adjustment}");
                return targetPosition + (adjustment * upDirection);
            }
            else
            {
                return targetPosition;
            }
        }

        // Wouldn't it make more sense to use a curvature based correction here?
        Vector3 LongRangeAltitudeCorrection(Vector3 targetPosition)
        {
            var weaponManager = WeaponManager;
            var scale = weaponManager != null ? Mathf.Max(2500f, weaponManager.gunRange) : 2500f;
            if (isBombing) scale *= 2; // Double the scale when bombing.
            var scaledDistance = (targetPosition - vessel.transform.position).magnitude / scale;
            if (scaledDistance <= 1) return targetPosition; // No modification if the target is within the gun range.
            scaledDistance = BDAMath.Sqrt(scaledDistance);
            var targetAlt = BodyUtils.GetRadarAltitudeAtPos(targetPosition);
            var newAlt = targetAlt / scaledDistance + defaultAltitude * (scaledDistance - 1) / scaledDistance;
            if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Adjusting fly-to altitude from {targetAlt:0}m to {newAlt:0}m (scaled distance: {scaledDistance:0.0}m)");
            return targetPosition + (newAlt - targetAlt) * upDirection;
        }

        private float SteerPower(Axis axis)
        {
            if (threeAxisPID) return axis switch
            {
                Axis.Pitch => threeAxisPIDPitchMult,
                Axis.Yaw => threeAxisPIDYawMult,
                Axis.Roll => threeAxisPIDRollMult,
                _ => steerMult // Default, shouldn't happen.
            };
            else return steerMult;
        }

        private float SteerCorrection(Axis axis)
        {
            if (threeAxisPID) return axis switch
            {
                Axis.Pitch => threeAxisPIDPitchKi,
                Axis.Yaw => threeAxisPIDYawKi,
                Axis.Roll => threeAxisPIDRollKi,
                _ => steerKiAdjust // Default, shouldn't happen.
            };
            else return steerKiAdjust;
        }

        private float SteerDamping(float angleToTarget, float defaultTargetPosition, Axis axis)
        {
            if (!dynamicSteerDamping)
            {
                if (threeAxisSteerDamping)
                {
                    return axis switch
                    {
                        Axis.Pitch => steerDampingPitch,
                        Axis.Yaw => steerDampingYaw,
                        Axis.Roll => steerDampingRoll,
                        _ => steerDamping // Default, shouldn't happen.
                    };
                }
                else
                {
                    return steerDamping;
                }
            }

            if (angleToTarget >= 180 || angleToTarget < 0) // Check for valid angle to target. This shouldn't happen, but a sanity check is needed for dynamic damping.
            {
                if (part.PartActionWindow is not null && part.PartActionWindow.isActiveAndEnabled)
                {
                    if (threeAxisSteerDamping)
                    {
                        switch (axis)
                        {
                            case Axis.Pitch: PitchLabel = "N/A"; break;
                            case Axis.Yaw: YawLabel = "N/A"; break;
                            case Axis.Roll: RollLabel = "N/A"; break;
                        }
                    }
                    else
                    {
                        DynamicDampingLabel = "N/A";
                    }
                }
                if (threeAxisSteerDamping)
                {
                    return axis switch
                    {
                        Axis.Pitch => steerDampingPitch,
                        Axis.Yaw => steerDampingYaw,
                        Axis.Roll => steerDampingRoll,
                        _ => steerDamping // Default, shouldn't happen.
                    };
                }
                else
                {
                    return steerDamping;
                }
            }

            // Dynamic damping
            if (threeAxisSteerDamping)
            {
                float damping = axis switch
                {
                    Axis.Pitch when dynamicDampingPitch => GetDampingFactor(angleToTarget, dynamicSteerDampingPitchFactor, DynamicDampingPitchMin, DynamicDampingPitchMax),
                    Axis.Pitch when !dynamicDampingPitch => steerDampingPitch,
                    Axis.Yaw when dynamicDampingYaw => GetDampingFactor(angleToTarget, dynamicSteerDampingYawFactor, DynamicDampingYawMin, DynamicDampingYawMax),
                    Axis.Yaw when !dynamicDampingYaw => steerDampingYaw,
                    Axis.Roll when dynamicDampingRoll => GetDampingFactor(angleToTarget, dynamicSteerDampingRollFactor, DynamicDampingRollMin, DynamicDampingRollMax),
                    Axis.Roll when !dynamicDampingRoll => steerDampingRoll,
                    _ => steerDamping // Default, shouldn't happen.
                };
                switch (axis)
                {
                    case Axis.Pitch when dynamicDampingPitch:
                        dynSteerDampingPitchValue = damping;
                        if (part.PartActionWindow is not null && part.PartActionWindow.isActiveAndEnabled)
                            PitchLabel = damping.ToString();
                        break;
                    case Axis.Yaw when dynamicDampingYaw:
                        dynSteerDampingYawValue = damping;
                        if (part.PartActionWindow is not null && part.PartActionWindow.isActiveAndEnabled)
                            YawLabel = damping.ToString();
                        break;
                    case Axis.Roll when dynamicDampingRoll:
                        dynSteerDampingRollValue = damping;
                        if (part.PartActionWindow is not null && part.PartActionWindow.isActiveAndEnabled)
                            RollLabel = damping.ToString();
                        break;
                }
                return damping;
            }
            else
            {
                dynSteerDampingValue = GetDampingFactor(defaultTargetPosition, dynamicSteerDampingFactor, DynamicDampingMin, DynamicDampingMax);
                if (part.PartActionWindow is not null && part.PartActionWindow.isActiveAndEnabled) DynamicDampingLabel = dynSteerDampingValue.ToString();
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

        // Legacy collision avoidance code.
        bool DetectCollision(Vector3 direction, out Vector3 badDirection)
        {
            badDirection = Vector3.zero;
            if ((float)vessel.radarAltitude < 20) return false;

            direction = direction.normalized;
            Ray ray = new Ray(vesselTransform.position + (50 * vesselTransform.up), direction);
            float distance = Mathf.Clamp((float)vessel.srfSpeed * 4f, 125f, 2500);
            if (!Physics.SphereCast(ray, 10, out RaycastHit hit, distance, (int)LayerMasks.Scenery)) return false;
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
            if (command == PilotCommands.Follow && commandLeader is null)
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
                if (AutoTune) // Actually fly to the specified point.
                {
                    SetStatus("AutoTuning");
                    AdjustThrottle(autoTuningSpeed, true);
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
                var weaponManager = WeaponManager;
                if (targetVessel != null || weaponManager == null) // Found a target or lost our WM.
                {
                    ReleaseCommand(false);
                    return;
                }
                else
                {
                    if (weaponManager.underAttack || weaponManager.underFire) // Switch to Free to allow combat behaviours, but continue flying towards the attack point for now.
                        ReleaseCommand(false);
                    SetStatus("Attack");
                    if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 77)
                    {
                        AdjustThrottle(maxSpeed, false);
                        FlyToPosition(s, vesselTransform.position + upDirection * BDArmorySettings.GUARD_MODE_TRIGGER_ALT);
                    }
                    else
                        FlyOrbit(s, assignedPositionGeo, 2500, maxSpeed, ClockwiseOrbit);
                }
            }
        }

        void UpdateFollowCommand(FlightCtrlState s)
        {
            steerMode = SteerModes.NormalFlight;
            vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, false);

            var commandVelocity = commandLeader.vessel.Velocity();
            var (commandSpeed, commandDirection) = commandVelocity.MagNorm();
            var currentVelocity = vessel.Velocity();

            //formation position
            Vector3d commandPosition = GetFormationPosition();
            debugFollowPosition = commandPosition;

            Vector3 flyPos;
            float finalMaxSpeed;
            var currentPosition = vesselTransform.position;
            float distanceToPos = Vector3.Distance(currentPosition, commandPosition);
            useFollowHints = distanceToPos < followHintThreshold;

            if (distanceToPos < 1000)
            {
                // Aim for 1km ahead of the command position, adjusted if we're currently ahead of it.
                flyPos = commandPosition + (1000 + Mathf.Max(0, Vector3.Dot(currentPosition - commandPosition, commandDirection))) * commandDirection;

                Vector3 vectorToFlyPos = flyPos - currentPosition;
                Vector3 projectedPosOffset = (commandPosition - currentPosition).ProjectOnPlanePreNormalized(commandDirection);
                var (offsetMagnitude, offsetDirection) = projectedPosOffset.MagNorm();
                float adjustAngle = Mathf.Clamp(0.5f * (offsetMagnitude < 1 ? 0.5f * offsetMagnitude * offsetMagnitude : offsetMagnitude - 0.5f), 0, 15);
                float dbg1 = adjustAngle; // Position component
                adjustAngle -= Mathf.Clamp(Vector3.Dot(currentVelocity - commandVelocity, offsetDirection), -5, 5);
                float dbg2 = adjustAngle - dbg1; // Velocity component
                vectorToFlyPos = Vector3.RotateTowards(vectorToFlyPos, offsetDirection, Mathf.Deg2Rad * adjustAngle, 0);
                flyPos = currentPosition + vectorToFlyPos;

                var currentDirection = currentVelocity.normalized;
                float dotDistance = Vector3.Dot(commandPosition - currentPosition, currentDirection);
                float followSpeedP = (float)commandSpeed + (0.5f + 0.01f * Mathf.Abs(dotDistance)) * (dotDistance > 0 ? dotDistance / 4 : dotDistance / 2); // Adjust for lag amount. Braking needs to be more agressive than accelerating.
                float followSpeedError = 0.01f * Time.fixedDeltaTime * dotDistance;
                if (followSpeedD != 0) followSpeedD -= dotDistance;
                followSpeedI = Mathf.Clamp((1 - Mathf.Clamp01(Mathf.Abs(followSpeedError))) * followSpeedI + followSpeedError, -1, 1);
                finalMaxSpeed = followSpeedP + 10 * followSpeedI - 50 * followSpeedD;
                finalMaxSpeed = Mathf.Clamp(finalMaxSpeed, 0, maxSpeed); // Don't go over maxSpeed.
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_AI) debugString.AppendLine($"Follow: adj: {adjustAngle:F1}° ({dbg1:F1}°, {dbg2:F1}°), d: {distanceToPos:F1}m, ·d: {dotDistance:F1}m, spdP: {followSpeedP:F0}m/s, spdI: {10 * followSpeedI:F1}, spdD: {50 * followSpeedD:F1}");
                followSpeedD = dotDistance;

                followHintDistance = distanceToPos;
            }
            else
            {
                flyPos = commandPosition;
                finalMaxSpeed = maxSpeed;
                followSpeedD = 0;
                followSpeedI = 0;
            }

            AdjustThrottle(finalMaxSpeed, true);

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

            Vector3d pos = velocityTransform.TransformPoint(commandLeader.GetFormationPosition(commandFollowIndex));

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

            if (!HighLogic.LoadedSceneIsFlight) return;
            if (!pilotEnabled || !vessel || !vessel.isActiveVessel) return;

            if (!BDArmorySettings.DEBUG_LINES) return;

            Vector3 vesselTransformPos = vesselTransform.position;

            if (command == PilotCommands.Follow)
            {
                GUIUtils.DrawLineBetweenWorldPositions(vesselTransformPos, debugFollowPosition, 2, Color.red);
            }
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransformPos, debugTargetPosition, 5, Color.red); // The point we're asked to fly to
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransformPos, debugTargetDirection, 5, Color.green); // The direction FlyToPosition will actually turn to
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransformPos, vesselTransformPos + vesselTransform.up * 1000, 3, Color.white);
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransformPos, vesselTransformPos + -vesselTransform.forward * 100, 3, Color.yellow);
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransformPos, vesselTransformPos + vessel.Velocity().normalized * 100, 3, Color.magenta);

            GUIUtils.DrawLineBetweenWorldPositions(vesselTransformPos, vesselTransformPos + rollTarget, 2, Color.blue);
#if DEBUG
            if (IsEvading || IsExtending) GUIUtils.DrawLineBetweenWorldPositions(vesselTransformPos, vesselTransformPos + debugSquigglySquidDirection.normalized * 10, 1, Color.cyan);
#endif
            if (IsEvading && debugBreakDirection != default) GUIUtils.DrawLineBetweenWorldPositions(vesselTransformPos, vesselTransformPos + debugBreakDirection.normalized * 20, 5, Color.cyan);
            GUIUtils.DrawLineBetweenWorldPositions(vesselTransformPos + (0.05f * vesselTransform.right), vesselTransformPos + (0.05f * vesselTransform.right) + angVelRollTarget, 2, Color.green);
            if (IsExtending) GUIUtils.DrawLineBetweenWorldPositions(vesselTransformPos, lastExtendTargetPosition, 2, Color.grey);

            Vector3 vesseltPos = vessel.transform.position; // Not quite sure if there's a difference between this and vesselTransform.position

            if (avoidingTerrain)
            {
                GUIUtils.DrawLineBetweenWorldPositions(vesseltPos, terrainAlertDebugPos, 2, Color.cyan);
                GUIUtils.DrawLineBetweenWorldPositions(terrainAlertDebugPos, terrainAlertDebugPos + (terrainAlertThreshold - terrainAlertDistance) * terrainAlertDebugDir, 2, Color.cyan);
                GUIUtils.DrawLineBetweenWorldPositions(terrainAlertDebugPos, terrainAlertDebugPos + terrainAlertNormal * 10, 5, terrainAlertNormalColour);
                foreach (var ray in terrainAlertDebugRays) GUIUtils.DrawLineBetweenWorldPositions(ray.origin, ray.origin + ray.direction * 10, 2, Color.red);
            }
            GUIUtils.DrawLineBetweenWorldPositions(vesseltPos, vesseltPos + 1.4142f * terrainAlertDetectionRadius * (vessel.srf_vel_direction - relativeVelocityDownDirection).normalized, 1, Color.grey);
            GUIUtils.DrawLineBetweenWorldPositions(vesseltPos, vesseltPos + 1.4142f * terrainAlertDetectionRadius * (vessel.srf_vel_direction + relativeVelocityDownDirection).normalized, 1, Color.grey);
            GUIUtils.DrawLineBetweenWorldPositions(vesseltPos, vesseltPos + 1.4142f * terrainAlertDetectionRadius * (vessel.srf_vel_direction - relativeVelocityRightDirection).normalized, 1, Color.grey);
            GUIUtils.DrawLineBetweenWorldPositions(vesseltPos, vesseltPos + 1.4142f * terrainAlertDetectionRadius * (vessel.srf_vel_direction + relativeVelocityRightDirection).normalized, 1, Color.grey);
            if (waypointTerrainAvoidanceActive)
            {
                GUIUtils.DrawLineBetweenWorldPositions(vesseltPos, waypointRayHit.point, 2, Color.cyan); // Technically, it's from 1 frame behind the current position, but close enough for visualisation.
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
        public PIDAutoTuning(BDModulePilotAI AI)
        {
            this.AI = AI;
            if (AI.vessel == null) { Debug.LogError($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: PIDAutoTuning triggered on null vessel!"); return; }
            WM = AI.vessel.ActiveController().WM; // Set only once, as it shouldn't change during autotuning.
            partCount = AI.vessel.Parts.Count;
            maxObservedSpeed = AI.idleSpeed;
        }

        // External flags.
        public bool measuring = false; // Whether a measurement is taking place or not.
        public string vesselName = null; // Name of the vessel when auto-tuning began (in case it changes due to crashes, etc.).

        #region Internal parameters
        BDModulePilotAI AI; // The AI being tuned.
        MissileFire WM; // The attached WM (if trying to tune while in combat — not recommended currently).
        float timeout = 15; // Measure for at most 15s.
        float pointingTolerance = 0.1f; // Pointing tolerance for stopping measurements.
        float rollTolerance = 5f; // Roll tolerance for stopping measurements.
        float onTargetTimer = 0;
        int partCount = 0;
        float measurementStartTime = -1;
        float measurementTime = 0;
        float pointingOscillationAreaSqr = 0;
        float rollOscillationAreaSqr = 0;
        Vessel lastTargetVessel;
        float maxObservedSpeed = 0;
        float absHeadingChange = 0;
        // float pitchChange = 0;
        Vector3d startCoords = default;
        bool recentering = false;

        #region Gradient Descent (approx)
        /// <summary>
        /// Learning rate scheduler.
        /// This implements a ReduceLROnPlateau type of scheduler where the learning rate is reduced if no improvement in the loss occurs for a given number of steps.
        /// </summary>
        class LR
        {
            public float current = 1f; // The current learning rate.
            float reductionFactor = BDAMath.Sqrt(0.1f); // Two steps per order of magnitude.
            int patience = 3; // Number of steps without improvement before lowering the learning rate.
            int count = 0; // Count of the number of steps without improvement.
            float _bestLoss = float.MaxValue; // The best result so far for the current learning rate.
            public float bestLoss = float.MaxValue; // The best loss result so far.
            public float lrAtBest = 1f; // The LR at the time the best loss was found (for logging).
            public float rrAtBest = 1f; // The RR at the time the best loss was found (for logging).

            /// <summary>
            /// Update the learning rate based on the current loss.
            /// </summary>
            /// <param name="value">The current loss, or some other metric.</param>
            /// <param name="rollRelevance">The current roll relevance (for logging).</param>
            /// <returns>True if the learning rate decreases, False otherwise.</returns>
            public bool Update(float value, float rollRelevance)
            {
                if (value < _bestLoss)
                {
                    _bestLoss = value;
                    count = 0;
                    if (_bestLoss < bestLoss)
                    {
                        bestLoss = _bestLoss;
                        lrAtBest = current;
                        rrAtBest = rollRelevance;
                    }
                }
                if (++count >= patience)
                {
                    current *= reductionFactor;
                    count = 0;
                    _bestLoss = value; // Reset the best loss to avoid unnecessarily reducing the learning rate due to a fluke best score.
                    return true;
                }
                return false;
            }

            /// <summary>
            /// Reset everything.
            /// </summary>
            public void Reset(float initial)
            {
                current = initial;
                count = 0;
                _bestLoss = float.MaxValue;
                bestLoss = _bestLoss;
                lrAtBest = initial;
                rrAtBest = 1f;
            }
        }

        /// <summary>
        /// Optimise various parameters.
        /// Currently, this just balances the roll relevance factor.
        /// </summary>
        class Optimiser
        {
            public float rollRelevance = 1f; // Start high so that the loss decreases as this converges to a fixed value.
            float rollRelevanceMomentum = 0.8f;

            public void Update()
            {
                rollRelevance = rollRelevanceMomentum * rollRelevance + (1f - rollRelevanceMomentum) * Mathf.Clamp01(_rollRelevance.Average()); // Clamp roll relevance to at most 1 in case of freak measurements.
                _rollRelevance.Clear();
            }

            public void Reset(float initialRollRelevance = 1f)
            {
                rollRelevance = initialRollRelevance;
                _rollRelevance.Clear();
            }

            List<float> _rollRelevance = [];
            public void Accumulate(float rollRelevance)
            {
                _rollRelevance.Add(rollRelevance);
            }
        }

        Dictionary<string, BaseField> fields;
        HashSet<string> fixedFields;
        Dictionary<string, float> baseValues;
        Dictionary<string, float> bestValues;
        Dictionary<string, Tuple<float, float>> limits;
        Dictionary<string, List<List<float>>> lossSamples; // Should really use a tuple, but tuple items aren't settable.
        List<float> baseLossSamples;
        bool firstCFDSample = true;
        Dictionary<string, float> dx;
        Dictionary<string, float> gradient;
        List<string> fieldNames;
        string currentField = "";
        int currentFieldIndex = 0;
        int sampleNumber = 0;
        float headingChange = 30f;
        float momentum = 0.7f;
        LR lr = new();
        Optimiser optimiser = new();
        #endregion
        #endregion

        /// <summary>
        /// Perform auto-tuning analysis.
        /// 
        /// While measuring, this measures error^2*(α+T^2) for the pointing error and error^2*(α+T) for the roll error, where α is the "fast response relevance" and T is the measurement time.
        /// This emphasises errors that don't vanish quickly, with the pointing error being more important than the roll error.
        /// The final loss function is a balanced (by the optimiser) combination of the normalised pointing and roll errors.
        /// 
        /// When between measurements, this either assigns a new fly-to point or watches for a large pointing error if guard mode is enabled (not currently recommended), and then starts a new measurement.
        /// </summary>
        /// <param name="pitchError"></param>
        /// <param name="rollError"></param>
        /// <param name="yawError"></param>
        public void Update(float pitchError, float rollError, float yawError)
        {
            if (AI == null || AI.vessel == null) return; // Sanity check.
            if (AI.vessel.Parts.Count < partCount) // Don't tune a plane if it's lost parts.
            {
                var message = $"Vessel {vesselName} has lost parts since spawning, auto-tuning disabled.";
                Debug.LogWarning($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: " + message);
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                AI.AutoTune = false;
                return;
            }
            measurementTime = Time.time - measurementStartTime;
            var pointingErrorSqr = pitchError * pitchError + yawError * yawError; // Combine pitch and yaw errors as a single pointing error.
            var rollErrorSqr = rollError * rollError;
            if ((float)AI.vessel.srfSpeed > maxObservedSpeed) maxObservedSpeed = (float)AI.vessel.srfSpeed;
            if (measuring)
            {
                if (pointingErrorSqr < pointingTolerance && rollErrorSqr < rollTolerance) { onTargetTimer += Time.fixedDeltaTime; }
                else { onTargetTimer = 0; }

                // Measuring timed out or completed to within tolerance (on target for 0.2s if in combat, 1s outside of combat).
                if (Time.time - measurementStartTime > timeout || onTargetTimer > (WM != null && WM.guardMode ? 0.2f : 1f))
                {
                    measurementTime = Time.time - measurementStartTime;
                    TakeSample();
                    ResetMeasurements();
                }
                else if (WM != null && WM.guardMode && WM.currentTarget != null && WM.currentTarget.Vessel != lastTargetVessel) // Target changed while in combat. Reset, but don't update PID.
                {
                    if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Changed target.");
                    ResetMeasurements();
                }
                else // Update internal parameters.
                {
                    pointingOscillationAreaSqr += pointingErrorSqr * (AI.autoTuningOptionFastResponseRelevance + measurementTime * measurementTime);
                    rollOscillationAreaSqr += rollErrorSqr * (AI.autoTuningOptionFastResponseRelevance + measurementTime); // * measurementTime); // Small roll errors aren't as important as small pointing errors.
                }
            }
            else if (recentering)
            {
                AI.CommandFlyTo((Vector3)startCoords);
                if ((FlightGlobals.currentMainBody.GetWorldSurfacePosition(startCoords.x, startCoords.y, startCoords.z) - AI.vessel.transform.position).sqrMagnitude < 0.01f * AI.autoTuningRecenteringDistanceSqr) // Within 10% of recentering distance.
                {
                    recentering = false;
                    if (AI.autoTuningLossLabel.EndsWith("   re-centering")) AI.autoTuningLossLabel = AI.autoTuningLossLabel.Remove(AI.autoTuningLossLabel.Length - 15);
                }
            }
            else
            {
                if (WM != null && WM.guardMode) // If guard mode is enabled, watch for target changes or something else to trigger a new measurement. This is going to be less reliable due to not using controlled fly-to directions. Don't use yet.
                {
                    // Significantly off-target, start measuring again.
                    if (pointingErrorSqr > 10f)
                    {
                        if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Starting measuring due to being significantly off-target.");
                        StartMeasuring();
                    }
                }
                else // Just cruising, assign a fly-to position and begin measuring again.
                {
                    var upDirection = (AI.vessel.transform.position - AI.vessel.mainBody.transform.position).normalized;
                    var newDirection = (Quaternion.AngleAxis(headingChange, upDirection) * AI.vessel.srf_vel_direction).ProjectOnPlanePreNormalized(upDirection).normalized;
                    // newDirection = Quaternion.AngleAxis(pitchChange, Vector3.Cross(upDirection, newDirection)) * newDirection;
                    var newFlyToPoint = AI.vessel.transform.position + newDirection * maxObservedSpeed * timeout;
                    var altitudeAtFlyToPoint = BodyUtils.GetRadarAltitudeAtPos(newFlyToPoint, false);
                    var clampedAltitude = Mathf.Clamp(altitudeAtFlyToPoint, AI.autoTuningAltitude - AI.minAltitude, AI.autoTuningAltitude + AI.minAltitude); // Restrict altitude to within min altitude of the desired altitude.
                    newFlyToPoint += (clampedAltitude - altitudeAtFlyToPoint) * upDirection;
                    Vector3d flyTo;
                    FlightGlobals.currentMainBody.GetLatLonAlt(newFlyToPoint, out flyTo.x, out flyTo.y, out flyTo.z);
                    AI.CommandFlyTo((Vector3)flyTo);
                    StartMeasuring();
                }
            }
        }

        /// <summary>
        /// Initialise a measurement.
        /// </summary>
        void StartMeasuring()
        {
            measuring = true;
            measurementStartTime = Time.time;
            if (WM != null && WM.currentTarget != null) lastTargetVessel = WM.currentTarget.Vessel;
        }

        /// <summary>
        /// Reset parameters used for each measurement.
        /// Also, perform initial setup for auto-tuning or release the AI when finished.
        /// </summary>
        public void ResetMeasurements()
        {
            measurementStartTime = -1;
            measurementTime = 0;
            pointingOscillationAreaSqr = 0;
            rollOscillationAreaSqr = 0;
            onTargetTimer = 0;
            measuring = false;
            partCount = AI.vessel.Parts.Count;

            // Initial setup for auto-tuning or release the AI when finished.
            if (!AI.AutoTune && AI.currentCommand == PilotCommands.FlyTo) AI.ReleaseCommand(); // Release the AI if we've been commanding it.
            if (!AI.AutoTune) gradient = null;
            else if (gradient == null) ResetGradient();
        }

        /// <summary>
        /// Reset all the samples in preparation for the next gradient and adjust parameters that change between epochs.
        /// </summary>
        void ResetSamples()
        {
            baseLossSamples.Clear();
            lossSamples = fields.ToDictionary(kvp => kvp.Key, kvp => new List<List<float>>());
            currentField = "base";
            currentFieldIndex = 0;
            firstCFDSample = true;
            sampleNumber = 0;
            headingChange = -(30f + 0.5f * (90f / AI.autoTuningOptionNumSamples)) * Mathf.Sign(headingChange); // Initial θ for the midpoint rule approximation to ∫f(x, θ)dθ.
            absHeadingChange = Mathf.Abs(headingChange);

            // Reset the dx values, taking care to avoid negative PID sample points.
            dx = limits.ToDictionary(kvp => kvp.Key, kvp => Mathf.Min((AI.UpToEleven ? 0.01f : 0.1f) * BDAMath.Sqrt(lr.current) * (kvp.Value.Item2 - kvp.Value.Item1), 0.5f * baseValues[kvp.Key])); // Clamp dx when close to the minimum.

            // Update UI.
            if (string.IsNullOrEmpty(AI.autoTuningLossLabel)) AI.autoTuningLossLabel = $"measuring";
            AI.autoTuningLossLabel2 = $"LR: {lr.current:G3}, Roll rel.: {optimiser.rollRelevance:G2}";
            AI.autoTuningLossLabel3 = $"{currentField}, sample nr: {sampleNumber + 1}";

            // pitchChange = 30f * UnityEngine.Random.Range(-1f, 1f) * UnityEngine.Random.Range(-1f, 1f); // Adjust pitch by ±30°, biased towards 0°.

            if ((FlightGlobals.currentMainBody.GetWorldSurfacePosition(startCoords.x, startCoords.y, startCoords.z) - AI.vessel.transform.position).sqrMagnitude > AI.autoTuningRecenteringDistanceSqr)
            {
                recentering = true;
                AI.autoTuningLossLabel += "   re-centering";
            }
        }

        /// <summary>
        /// Reset everything when the auto-tuning configuration has changed (or initialised),
        /// </summary>
        public void ResetGradient()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            vesselName = AI.vessel.GetName();
            fieldNames = ["base"];
            fields = [];
            fixedFields = [];
            baseValues = [];
            gradient = [];
            limits = [];
            lossSamples = [];
            baseLossSamples = [];
            bestValues = null;

            // Check which PID controls are in use and set up the required dictionaries.
            foreach (var field in AI.Fields)
            {
                if (field.group.name != "pilotAI_PID") continue; // Ignore non-PID fields.
                if (!field.guiActive) continue; // Ignore inactive fields. Only and all active fields should be relevant.
                if (field.uiControlFlight.GetType() != typeof(UI_FloatRange)) continue; // Ignore non-FloatRange fields.
                if (field.name.StartsWith("autoTuning")) continue; // Ignore our extra autoTuning fields.

                // Exclude fields selected by the user to be excluded.
                if ((AI.autoTuningOptionFixedP && field.name == "steerMult") ||
                    (AI.autoTuningOptionFixedI && field.name == "steerKiAdjust") ||
                    (AI.autoTuningOptionFixedD && field.name == "steerDamping") ||
                    (AI.autoTuningOptionFixedDP && field.name == "steerDampingPitch") ||
                    (AI.autoTuningOptionFixedDY && field.name == "steerDampingYaw") ||
                    (AI.autoTuningOptionFixedDR && field.name == "steerDampingRoll") ||
                    (AI.autoTuningOptionFixedDOff && field.name == "DynamicDampingMin") ||
                    (AI.autoTuningOptionFixedDOn && field.name == "DynamicDampingMax") ||
                    (AI.autoTuningOptionFixedDF && field.name == "dynamicSteerDampingFactor") ||
                    (AI.autoTuningOptionFixedDPOff && field.name == "DynamicDampingPitchMin") ||
                    (AI.autoTuningOptionFixedDPOn && field.name == "DynamicDampingPitchMax") ||
                    (AI.autoTuningOptionFixedDPF && field.name == "dynamicSteerDampingPitchFactor") ||
                    (AI.autoTuningOptionFixedDYOff && field.name == "DynamicDampingYawMin") ||
                    (AI.autoTuningOptionFixedDYOn && field.name == "DynamicDampingYawMax") ||
                    (AI.autoTuningOptionFixedDYF && field.name == "dynamicSteerDampingYawFactor") ||
                    (AI.autoTuningOptionFixedDROff && field.name == "DynamicDampingRollMin") ||
                    (AI.autoTuningOptionFixedDROn && field.name == "DynamicDampingRollMax") ||
                    (AI.autoTuningOptionFixedDRF && field.name == "dynamicSteerDampingRollFactor") ||
                    (AI.autoTuningOptionFixedPp && field.name == "threeAxisPIDPitchMult") ||
                    (AI.autoTuningOptionFixedIp && field.name == "threeAxisPIDPitchKi") ||
                    (AI.autoTuningOptionFixedDp && field.name == "threeAxisPIDPitchDamping") ||
                    (AI.autoTuningOptionFixedPy && field.name == "threeAxisPIDYawMult") ||
                    (AI.autoTuningOptionFixedIy && field.name == "threeAxisPIDYawKi") ||
                    (AI.autoTuningOptionFixedDy && field.name == "threeAxisPIDYawDamping") ||
                    (AI.autoTuningOptionFixedPr && field.name == "threeAxisPIDRollMult") ||
                    (AI.autoTuningOptionFixedIr && field.name == "threeAxisPIDRollKi") ||
                    (AI.autoTuningOptionFixedDr && field.name == "threeAxisPIDRollDamping"))
                {
                    fixedFields.Add(field.name);
                    continue;
                }
                var uiControl = (UI_FloatRange)field.uiControlFlight;
                if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Found PID field: {field.guiName} with value {field.GetValue(AI)} and limits {uiControl.minValue} — {uiControl.maxValue}");
                fieldNames.Add(field.name);
                fields.Add(field.name, field);
                baseValues.Add(field.name, (float)field.GetValue(AI));
                gradient.Add(field.name, 0);
                limits.Add(field.name, new Tuple<float, float>(uiControl.minValue, uiControl.maxValue));
            }
            optimiser.Reset(AI.autoTuningOptionInitialRollRelevance); // Reset the optimiser before resetting samples so that the RR is up-to-date in the strings.
            ResetSamples();
            lr.Reset(AI.autoTuningOptionInitialLearningRate);
        }

        /// <summary>
        /// Take a sample of the loss at the current sample position, then update internals for the next sample.
        /// </summary>
        void TakeSample()
        {
            // Measure loss at the current sample point.
            var lossSample = pointingOscillationAreaSqr / absHeadingChange / absHeadingChange + optimiser.rollRelevance * 0.0002f * rollOscillationAreaSqr; // This normalisation seems to give a roughly flat distribution over the 30°—120° range for the test craft.
            optimiser.Accumulate(pointingOscillationAreaSqr / absHeadingChange / absHeadingChange / (0.0002f * rollOscillationAreaSqr));
            if (currentField == "base")
            {
                baseLossSamples.Add(lossSample);
                if (++sampleNumber >= (int)AI.autoTuningOptionNumSamples)
                {
                    var loss = baseLossSamples.Average();
                    if (loss < lr.bestLoss)
                    {
                        bestValues = baseValues.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                        Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Updated best values: " + string.Join(", ", bestValues.Select(kvp => fields[kvp.Key].guiName + ":" + kvp.Value)) + $", LR: {lr.current:G3}, RR: {optimiser.rollRelevance}, Loss: {loss}");
                        bestValues["rollRelevance"] = optimiser.rollRelevance; // Store the roll relevance for the best PID settings too.
                        AI.autoTuningOptionInitialRollRelevance = optimiser.rollRelevance;
                    }
                    if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Current: " + string.Join(", ", baseValues.Select(kvp => fields[kvp.Key].guiName + ":" + kvp.Value)) + $", LR: {lr.current:G3}, RR: {optimiser.rollRelevance}, Loss: {loss}");
                    var lrDecreased = lr.Update(loss, optimiser.rollRelevance); // Update learning rate based on the current loss.
                    optimiser.Update();
                    if (lrDecreased)
                    {
                        if (bestValues is not null) RevertPIDValues(); // Revert to the best values when lowering the learning rate.
                        optimiser.Reset(lr.rrAtBest); // Also, revert to the roll relevance when the best values were found.
                    }
                    if (lr.current < 9e-4f) // Tuned about as far as it'll go, time to bail. (9e-4 instead of 1e-3 for some tolerance in the floating point comparison.)
                    {
                        AI.autoTuningLossLabel = $"{lr.bestLoss:G6}, completed.";
                        AI.AutoTune = false; // This also reverts to the best settings and stores them.
                        return;
                    }
                    AI.autoTuningLossLabel = $"{loss:G6}   (best: {lr.bestLoss:G6})";
                    AI.autoTuningLossLabel2 = $"LR: {lr.current:G3}, Roll rel.: {optimiser.rollRelevance:G3}";
                    ++currentFieldIndex;
                    UpdatePIDValues(false);
                    sampleNumber = 0;
                }
            }
            else
            {
                if (firstCFDSample)
                {
                    lossSamples[currentField].Add([lossSample]); // Sample at x - dx
                    firstCFDSample = false;
                    UpdatePIDValues(false);
                }
                else
                {
                    lossSamples[currentField].Last().Add(lossSample); // Sample at x + dx
                    firstCFDSample = true;
                    if (++sampleNumber >= (int)AI.autoTuningOptionNumSamples)
                    {
                        ++currentFieldIndex;
                        sampleNumber = 0;
                    }
                    UpdatePIDValues((currentFieldIndex %= fieldNames.Count) == 0);
                }
            }

            // Change heading for next sample
            headingChange = Mathf.Sign(headingChange) * (30f + (sampleNumber + 0.5f) * (90f / AI.autoTuningOptionNumSamples)); // Midpoint rule for approximation to ∫f(x, θ)dθ.
            absHeadingChange = Mathf.Abs(headingChange);

            if (currentField == "base")
            { AI.autoTuningLossLabel3 = $"{currentField}, sample nr: {sampleNumber + 1}"; }
            else
            { AI.autoTuningLossLabel3 = $"{fields[currentField].guiName}, sample nr: {sampleNumber + 1}{(firstCFDSample ? "-" : "+")}"; }
        }

        /// <summary>
        /// Update the PID values either for the new sample point or based on the gradient once we've got enough samples.
        /// </summary>
        /// <param name="samplingComplete"></param>
        void UpdatePIDValues(bool samplingComplete)
        {
            if (samplingComplete) // Perform a step in the downward direction of the gradient: x -> x - lr * df/dx
            {
                var newGradient = lossSamples.ToDictionary(kvp => kvp.Key, kvp => lr.current * kvp.Value.Select(s => (s[1] - s[0]) / (2f * dx[kvp.Key])).Average()); // 2nd-order centred finite differences, averaged to approximate ∫f(x, θ)dθ with the domain normalised to 1 and pre-scaled by the learning rate.
                foreach (var fieldName in gradient.Keys.ToList())
                {
                    var gradLimit = 0.1f * (limits[fieldName].Item2 - limits[fieldName].Item1); // Limit gradient changes to ±0.1 of the scale of the field.
                    gradient[fieldName] = gradient[fieldName] * momentum + (1f - momentum) * Mathf.Clamp(newGradient[fieldName], -gradLimit, gradLimit); // Update the gradient using momentum.
                }
                if (gradient.Any(kvp => float.IsNaN(kvp.Value)))
                {
                    var message = "Gradient is giving NaN values, aborting auto-tuning.";
                    Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: " + message);
                    BDACompetitionMode.Instance.competitionStatus.Add(message);
                    AI.AutoTune = false;
                    return;
                }
                if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Gradient: " + string.Join(", ", gradient.Select(kvp => fields[kvp.Key].guiName + ":" + kvp.Value)));
                if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Unclamped gradient: " + string.Join(", ", newGradient.Select(kvp => fields[kvp.Key].guiName + ":" + kvp.Value)));
                Dictionary<string, float> absoluteGradient = new Dictionary<string, float>();
                foreach (var fieldName in absoluteGradient.Keys.ToList()) absoluteGradient[fieldName] = Mathf.Abs(gradient[fieldName]);
                foreach (var fieldName in baseValues.Keys.ToList())
                {
                    baseValues[fieldName] = baseValues[fieldName] - gradient[fieldName]; // Update PID values for gradient: x -> x - lr * df/dx.
                    if (AI.autoTuningOptionClampMaximums) baseValues[fieldName] = Mathf.Clamp(baseValues[fieldName], limits[fieldName].Item1, limits[fieldName].Item2); // Clamp to limits.
                    else baseValues[fieldName] = Mathf.Max(baseValues[fieldName], limits[fieldName].Item1); // Only clamp to the minimum.
                }
                foreach (var fieldName in fields.Keys.ToList()) fields[fieldName].SetValue(baseValues[fieldName], AI); // Set them in the AI.
                ResetSamples(); // Reset everything for the next gradient.
            }
            else // Update which axis we're measuring and reset the other ones back to the base value.
            {
                currentField = fieldNames[currentFieldIndex];
                foreach (var fieldName in fields.Keys.ToList()) fields[fieldName].SetValue(baseValues[fieldName] + (fieldName == currentField ? (firstCFDSample ? -1f : 1f) * dx[fieldName] : 0), AI); // FIXME Sometimes these values are getting clamped by the sliders on the next Update/FixedUpdate. This doesn't seem specific to the auto-tuning though as toggling up-to-eleven was also triggering this.
            }
        }

        public void RevertPIDValues()
        {
            if (AI is null) return;
            if (bestValues is not null)
            {
                if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Reverting PID values to best values: {string.Join(", ", fields.Keys.Where(fieldName => bestValues.ContainsKey(fieldName)).Select(fieldName => fields[fieldName].guiName + ":" + bestValues[fieldName]))}");
                foreach (var fieldName in fields.Keys.ToList())
                    if (bestValues.ContainsKey(fieldName))
                    {
                        fields[fieldName].SetValue(bestValues[fieldName], AI);
                        if (baseValues.ContainsKey(fieldName)) // Update the base values too.
                            baseValues[fieldName] = bestValues[fieldName];
                    }
                if (bestValues.ContainsKey("rollRelevance")) AI.autoTuningOptionInitialRollRelevance = bestValues["rollRelevance"]; // Set the latest roll relevance as the AI's starting roll relevance for next time.
                if (lr.current < 9e-4f)
                    AI.autoTuningSummary = $"Best Loss {lr.bestLoss:G6}, tuning completed, RR: {lr.rrAtBest:G3}.";
                else
                    AI.autoTuningSummary = $"Best Loss {lr.bestLoss:G6}, tuning incomplete, LR: {lr.lrAtBest:G3}, RR: {lr.rrAtBest:G3}.";
            }
            else if (baseValues is not null)
            {
                if (BDArmorySettings.DEBUG_AI) Debug.Log($"[BDArmory.BDModulePilotAI.PIDAutoTuning]: Reverting PID values to base values: {string.Join(", ", baseValues.Select(kvp => fields[kvp.Key].guiName + ":" + kvp.Value))}");
                foreach (var fieldName in fields.Keys.ToList())
                    if (baseValues.ContainsKey(fieldName))
                        fields[fieldName].SetValue(baseValues[fieldName], AI);
                AI.autoTuningSummary = "";
            }
        }

        public void SetStartCoords()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            startCoords = FlightGlobals.currentMainBody.GetLatitudeAndLongitude(AI.vessel.transform.position);
            startCoords.z = (float)FlightGlobals.currentMainBody.TerrainAltitude(startCoords.x, startCoords.y) + AI.autoTuningAltitude;

            // Move the vessel to the start position and make sure the AI and engines are active.
            if (AI.vessel.LandedOrSplashed)
            {
                AI.vessel.Landed = false;
                AI.vessel.Splashed = false;
                VesselMover.Instance.PickUpAndDrop(AI.vessel, AI.autoTuningAltitude);
            }
            else
            {
                AI.vessel.SetPosition(FlightGlobals.currentMainBody.GetWorldSurfacePosition(startCoords.x, startCoords.y, startCoords.z));
            }
            if (SpawnUtils.CountActiveEngines(AI.vessel) == 0) SpawnUtils.ActivateAllEngines(AI.vessel);
            AI.ActivatePilot();
            recentering = true;
        }
    }
}
