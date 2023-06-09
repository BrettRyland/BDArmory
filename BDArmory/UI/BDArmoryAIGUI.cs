using KSP.UI.Screens;
using System.Collections.Generic;
using System.Collections;
using System;
using UnityEngine;
using static UnityEngine.GUILayout;

using BDArmory.Control;
using BDArmory.Settings;
using BDArmory.Utils;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.FlightAndEditor, false)]
    public class BDArmoryAIGUI : MonoBehaviour
    {
        //toolbar gui
        public static bool infoLinkEnabled = false;
        public static bool contextTipsEnabled = false;
        public static bool NumFieldsEnabled = false;
        public static bool windowBDAAIGUIEnabled;
        internal static bool resizingWindow = false;
        internal static int _guiCheckIndex = -1;

        public static ApplicationLauncherButton button;

        float WindowWidth = 500;
        float WindowHeight = 350;
        float contentHeight = 0;
        float height = 0;
        float ColumnWidth = 350;
        float _buttonSize = 26;
        float _windowMargin = 4;
        float contentTop = 10;
        float entryHeight = 20;
        float labelWidth = 200;
        bool showPID;
        bool showAltitude;
        bool showSpeed;
        bool showControl;
        bool showEvade;
        bool showTerrain;
        bool showRam;
        bool showMisc;
        bool fixedAutoTuneFields = false;

        int Drivertype = 0;
        int broadsideDir = 0;
        bool oldClamp;
        public AIUtils.VehicleMovementType[] VehicleMovementTypes = (AIUtils.VehicleMovementType[])Enum.GetValues(typeof(AIUtils.VehicleMovementType)); // Get the VehicleMovementType as an array of enum values.

        private Vector2 scrollViewVector;
        private Vector2 scrollViewSAIVector;
        private Vector2 scrollInfoVector;

        public BDModulePilotAI ActivePilot;
        public BDModuleSurfaceAI ActiveDriver;

        public static BDArmoryAIGUI Instance;
        public static bool buttonSetup;

        Dictionary<string, NumericInputField> inputFields;

        GUIStyle BoldLabel;
        GUIStyle Label;
        GUIStyle rightLabel;
        GUIStyle Title;
        GUIStyle contextLabel;
        GUIStyle infoLinkStyle;
        GUIStyle inputFieldStyle;
        bool stylesConfigured = false;


        void Awake()
        {
            if (Instance != null) Destroy(Instance);
            Instance = this;
        }

        void Start()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                GameEvents.onVesselChange.Add(OnVesselChange);
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onEditorLoad.Add(OnEditorLoad);
            }

            if (BDArmorySettings.AI_TOOLBAR_BUTTON) AddToolbarButton();

            BDArmorySetup.WindowRectAI = new Rect(BDArmorySetup.WindowRectAI.x, BDArmorySetup.WindowRectAI.y, WindowWidth, BDArmorySetup.WindowRectAI.height);
            WindowHeight = Mathf.Max(BDArmorySetup.WindowRectAI.height, 305);

            if (HighLogic.LoadedSceneIsFlight)
            {
                GetAI();
            }
            if (HighLogic.LoadedSceneIsEditor)
            {
                GetAIEditor();
                GameEvents.onEditorPartPlaced.Add(OnEditorPartPlacedEvent); //do per part placement instead of calling a findModule call every time *anything* changes on thevessel
                GameEvents.onEditorPartDeleted.Add(OnEditorPartDeletedEvent);
            }
            if (_guiCheckIndex < 0) _guiCheckIndex = GUIUtils.RegisterGUIRect(BDArmorySetup.WindowRectAI);
        }
        public void AddToolbarButton()
        {
            StartCoroutine(ToolbarButtonRoutine());
        }
        public void RemoveToolbarButton()
        {
            if (button == null) return;
            if (!HighLogic.LoadedSceneIsFlight && !HighLogic.LoadedSceneIsEditor) return;
            ApplicationLauncher.Instance.RemoveModApplication(button);
            button = null;
            buttonSetup = false;
        }

        IEnumerator ToolbarButtonRoutine()
        {
            if (buttonSetup) yield break;
            if (!HighLogic.LoadedSceneIsFlight && !HighLogic.LoadedSceneIsEditor) yield break;
            yield return new WaitUntil(() => ApplicationLauncher.Ready && BDArmorySetup.toolbarButtonAdded); // Wait until after the main BDA toolbar button.

            if (!buttonSetup)
            {
                Texture buttonTexture = GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "icon_ai", false);
                button = ApplicationLauncher.Instance.AddModApplication(ShowAIGUI, HideAIGUI, Dummy, Dummy, Dummy, Dummy, ApplicationLauncher.AppScenes.SPH | ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.FLIGHT, buttonTexture);
                buttonSetup = true;
                if (windowBDAAIGUIEnabled) button.SetTrue(false);
            }
        }

        public void ToggleAIGUI()
        {
            if (windowBDAAIGUIEnabled) HideAIGUI();
            else ShowAIGUI();
        }

        public void ShowAIGUI()
        {
            windowBDAAIGUIEnabled = true;
            GUIUtils.SetGUIRectVisible(_guiCheckIndex, windowBDAAIGUIEnabled);
            if (HighLogic.LoadedSceneIsFlight) Instance.GetAI(); // Call via Instance to avoid issue with the toolbar button holding a reference to a null gameobject causing an NRE when starting a coroutine.
            else Instance.GetAIEditor();
            if (button != null) button.SetTrue(false);
        }

        public void HideAIGUI()
        {
            windowBDAAIGUIEnabled = false;
            GUIUtils.SetGUIRectVisible(_guiCheckIndex, windowBDAAIGUIEnabled);
            BDAWindowSettingsField.Save(); // Save window settings.
            if (button != null) button.SetFalse(false);
        }

        void Dummy()
        { }

        void Update()
        {
            if (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor)
            {
                if (BDInputUtils.GetKeyDown(BDInputSettingsFields.GUI_AI_TOGGLE))
                {
                    ToggleAIGUI();
                }
            }
        }

        void OnVesselChange(Vessel v)
        {
            if (!windowBDAAIGUIEnabled) return;
            if (v == null) return;
            if (v.isActiveVessel)
            {
                GetAI();
            }
        }
        void OnEditorLoad(ShipConstruct ship, CraftBrowserDialog.LoadType loadType)
        {
            GetAIEditor();
        }
        private void OnEditorPartPlacedEvent(Part p)
        {
            if (p == null) return;

            // Prioritise Pilot AIs
            if (ActivePilot == null)
            {
                var AI = p.FindModuleImplementing<BDModulePilotAI>();
                if (AI != null)
                {
                    ActivePilot = AI;
                    inputFields = null; // Reset the input fields.
                    SetInputFields(ActivePilot.GetType());
                    return;
                }
            }
            else return; // A Pilot AI is already active.

            // No Pilot AIs, check for Surface AIs.
            if (ActiveDriver == null)
            {
                var DAI = p.FindModuleImplementing<BDModuleSurfaceAI>();
                if (DAI != null)
                {
                    ActiveDriver = DAI;
                    inputFields = null; // Reset the input fields
                    SetInputFields(ActiveDriver.GetType());
                    return;
                }
            }
            else return; // A Surface AI is already active.
        }

        private void OnEditorPartDeletedEvent(Part p)
        {
            if (ActivePilot != null || ActiveDriver != null) // If we had an active AI, we need to check to see if it's disappeared.
            {
                GetAIEditor(); // We can't just check the part as it's now null.
            }
        }

        void GetAI()
        {
            // Make sure we're synced between the sliders and input fields in case something changed just before the switch.
            SyncInputFieldsNow(NumFieldsEnabled);
            if (_getAICoroutine != null) StopCoroutine(_getAICoroutine);
            _getAICoroutine = StartCoroutine(GetAICoroutine());
        }
        Coroutine _getAICoroutine;
        IEnumerator GetAICoroutine()
        {
            // Then, reset all the fields as this is only occurring on vessel change, so they need resetting anyway.
            ActivePilot = null;
            ActiveDriver = null;
            inputFields = null;
            var tic = Time.time;
            if (FlightGlobals.ActiveVessel == null)
                yield return new WaitUntilFixed(() => FlightGlobals.ActiveVessel != null || Time.time - tic > 1); // Give it up to a second to find the active vessel.
            if (FlightGlobals.ActiveVessel == null) yield break;
            // Now, get the new AI and update stuff.
            ActivePilot = VesselModuleRegistry.GetBDModulePilotAI(FlightGlobals.ActiveVessel, true);
            if (ActivePilot == null)
            {
                ActiveDriver = VesselModuleRegistry.GetBDModuleSurfaceAI(FlightGlobals.ActiveVessel, true);
            }
            if (ActivePilot != null)
            {
                SetInputFields(ActivePilot.GetType());
                SetChooseOptionSliders(); // For later, if we want to add similar things to the pilot AI.
            }
            else if (ActiveDriver != null)
            {
                SetInputFields(ActiveDriver.GetType());
                SetChooseOptionSliders();
            }
        }

        void GetAIEditor()
        {
            if (_getAIEditorCoroutine != null) StopCoroutine(_getAIEditorCoroutine);
            _getAIEditorCoroutine = StartCoroutine(GetAIEditorCoroutine());
        }
        Coroutine _getAIEditorCoroutine;
        IEnumerator GetAIEditorCoroutine()
        {
            var tic = Time.time;
            if (EditorLogic.fetch.ship == null || EditorLogic.fetch.ship.Parts == null)
                yield return new WaitUntilFixed(() => (EditorLogic.fetch.ship != null && EditorLogic.fetch.ship.Parts != null) || Time.time - tic > 1); // Give it up to a second to find the editor ship and parts.
            if (EditorLogic.fetch.ship != null && EditorLogic.fetch.ship.Parts != null)
            {
                foreach (var p in EditorLogic.fetch.ship.Parts) // Take the AIs in the order they were placed on the ship.
                {
                    foreach (var AI in p.FindModulesImplementing<BDModulePilotAI>())
                    {
                        if (AI == null) continue;
                        if (AI == ActivePilot) yield break; // We found the current ActivePilot!
                        ActivePilot = AI;
                        inputFields = null; // Reset the input fields to the current AI.
                        SetInputFields(ActivePilot.GetType());
                        yield break;
                    }
                    foreach (var AI in p.FindModulesImplementing<BDModuleSurfaceAI>())
                    {
                        if (AI == null) continue;
                        if (AI == ActiveDriver) yield break; // We found the current ActiveDriver!
                        ActiveDriver = AI;
                        inputFields = null; // Reset the input fields to the current AI.
                        SetInputFields(ActiveDriver.GetType());
                        yield break;
                    }
                }
            }

            // No AIs were found, clear everything.
            ActivePilot = null;
            ActiveDriver = null;
            inputFields = null;
        }

        void SetInputFields(Type AIType)
        {
            // Clear other Active AIs.
            if (AIType != typeof(BDModulePilotAI)) ActivePilot = null;
            if (AIType != typeof(BDModuleSurfaceAI)) ActiveDriver = null;

            if (inputFields == null) // Initialise the input fields if they're not initialised.
            {
                oldClamp = false;
                if (AIType == typeof(BDModulePilotAI))
                {
                    inputFields = new Dictionary<string, NumericInputField> {
                        { "steerMult", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.steerMult, 0.1, 20) },
                        { "steerKiAdjust", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.steerKiAdjust, 0.01, 1) },
                        { "steerDamping", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.steerDamping, 0.1, 8) },

                        { "DynamicDampingMin", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.DynamicDampingMin, 0.1, 8) },
                        { "DynamicDampingMax", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.DynamicDampingMax, 0.1, 8) },
                        { "dynamicSteerDampingFactor", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.dynamicSteerDampingFactor, 0.1, 10) },

                        { "DynamicDampingPitchMin", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.DynamicDampingPitchMin, 0.1, 8) },
                        { "DynamicDampingPitchMax", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.DynamicDampingPitchMax, 0.1, 8) },
                        { "dynamicSteerDampingPitchFactor", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.dynamicSteerDampingPitchFactor, 0.1, 10) },

                        { "DynamicDampingYawMin", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.DynamicDampingYawMin, 0.1, 8) },
                        { "DynamicDampingYawMax", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.DynamicDampingYawMax, 0.1, 8) },
                        { "dynamicSteerDampingYawFactor", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.dynamicSteerDampingYawFactor, 0.1, 10) },

                        { "DynamicDampingRollMin", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.DynamicDampingRollMin, 0.1, 8) },
                        { "DynamicDampingRollMax", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.DynamicDampingRollMax, 0.1, 8) },
                        { "dynamicSteerDampingRollFactor", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.dynamicSteerDampingRollFactor, 0.1, 10) },

                        { "autoTuningOptionNumSamples", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.autoTuningOptionNumSamples, 1, 10) },
                        { "autoTuningOptionFastResponseRelevance", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.autoTuningOptionFastResponseRelevance, 0, 0.5) },
                        { "autoTuningOptionInitialLearningRate", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.autoTuningOptionInitialLearningRate, 1e-3, 1) },
                        { "autoTuningAltitude", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.autoTuningAltitude, 50, 5000) },
                        { "autoTuningSpeed", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.autoTuningSpeed, 50, 800) },

                        { "defaultAltitude", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.defaultAltitude, 50, 15000) },
                        { "minAltitude", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.minAltitude, 25, 6000) },
                        { "maxAltitude", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.maxAltitude, 100, 15000) },

                        { "maxSpeed", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.maxSpeed, 20, (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 55)? 600 :800) },
                        { "takeOffSpeed", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.takeOffSpeed, 10, 200) },
                        { "minSpeed", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.minSpeed, 10, 200) },
                        { "strafingSpeed", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.strafingSpeed, 10, 200) },
                        { "idleSpeed", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.idleSpeed, 10, 200) },
                        { "ABPriority", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.ABPriority, 0, 100) },
                        { "ABOverrideThreshold", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.ABOverrideThreshold, 0, 200) },

                        { "maxSteer", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.maxSteer, 0.1, 1) },
                        { "lowSpeedSwitch", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.lowSpeedSwitch, 10, 500) },
                        { "maxSteerAtMaxSpeed", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.maxSteerAtMaxSpeed, 0.1, 1) },
                        { "cornerSpeed", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.cornerSpeed, 10, 500) },
                        { "altitudeSteerLimiterFactor", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.altitudeSteerLimiterFactor, -1, 1) },
                        { "altitudeSteerLimiterAltitude", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.altitudeSteerLimiterAltitude, 100, 10000) },
                        { "maxBank", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.maxBank, 10, (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 55)? 40 : 180) },
                        { "waypointPreRollTime", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.waypointPreRollTime, 0, 2) },
                        { "waypointYawAuthorityTime", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.waypointYawAuthorityTime, 0, 10) },
                        { "maxAllowedGForce", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.maxAllowedGForce, 2, 45) },
                        { "maxAllowedAoA", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.maxAllowedAoA, 0, 90) },
                        { "postStallAoA", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.postStallAoA, 0, (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 55)? 0 : 90) },
                        { "ImmelmannTurnAngle", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.ImmelmannTurnAngle, 0, 90) },

                        { "minEvasionTime", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.minEvasionTime, 0, 1) },
                        { "evasionNonlinearity", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.evasionNonlinearity, 0, 10) },
                        { "evasionThreshold", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.evasionThreshold, 0, 100) },
                        { "evasionTimeThreshold", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.evasionTimeThreshold, 0, 1) },
                        { "collisionAvoidanceThreshold", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.collisionAvoidanceThreshold, 0, 50) },
                        { "vesselCollisionAvoidanceLookAheadPeriod", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.vesselCollisionAvoidanceLookAheadPeriod, 0, 3) },
                        { "vesselCollisionAvoidanceStrength", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.vesselCollisionAvoidanceStrength, 0, 4) },
                        { "vesselStandoffDistance", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.vesselStandoffDistance, 0, 1000) },
                        { "extendDistanceAirToAir", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.extendDistanceAirToAir, 0, 2000) },
                        { "extendAngleAirToAir", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.extendAngleAirToAir, -10, 45) },
                        { "extendDistanceAirToGroundGuns", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.extendDistanceAirToGroundGuns, 0, 5000) },
                        { "extendDistanceAirToGround", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.extendDistanceAirToGround, 0, 5000) },
                        { "extendTargetVel", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.extendTargetVel, 0, 2) },
                        { "extendTargetAngle", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.extendTargetAngle, 0, 180) },
                        { "extendTargetDist", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.extendTargetDist, 0, 5000) },
                        { "extendAbortTime", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.extendAbortTime, 5, 30) },

                        { "turnRadiusTwiddleFactorMin", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.turnRadiusTwiddleFactorMin, 0.1, 5) },
                        { "turnRadiusTwiddleFactorMax", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.turnRadiusTwiddleFactorMax, 0.1, 5) },
                        { "terrainAvoidanceCriticalAngle", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.terrainAvoidanceCriticalAngle, 90f, 180f) },
                        { "controlSurfaceDeploymentTime", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.controlSurfaceDeploymentTime, 0f, 4f) },
                        { "waypointTerrainAvoidance", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.waypointTerrainAvoidance, 0, 1) },

                        { "controlSurfaceLag", gameObject.AddComponent<NumericInputField>().Initialise(0, ActivePilot.controlSurfaceLag, 0, 0.2) },
                    };
                }
                else if (AIType == typeof(BDModuleSurfaceAI))
                {
                    inputFields = new Dictionary<string, NumericInputField> {
                        { "MaxSlopeAngle", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveDriver.MaxSlopeAngle, 1, 30) },
                        { "CruiseSpeed", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveDriver.CruiseSpeed, 5, 60) },
                        { "MaxSpeed", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveDriver.MaxSpeed, 5,  80) },
                        { "MaxDrift", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveDriver.MaxDrift, 1, 180) },
                        { "TargetPitch", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveDriver.TargetPitch, -10, 10) },
                        { "BankAngle", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveDriver.BankAngle, -45, 45) },
                        { "steerMult", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveDriver.steerMult, 0.2,  20) },
                        { "steerDamping", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveDriver.steerDamping, 0.1, 10) },
                        { "MinEngagementRange", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveDriver.MinEngagementRange, 0, 6000) },
                        { "MaxEngagementRange", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveDriver.MaxEngagementRange, 0, 8000) },
                        { "AvoidMass", gameObject.AddComponent<NumericInputField>().Initialise(0, ActiveDriver.AvoidMass, 0, 100) },
                    };
                }
            }

            if (AIType == typeof(BDModulePilotAI))
            {
                if (oldClamp != ActivePilot.UpToEleven)
                {
                    oldClamp = ActivePilot.UpToEleven;

                    inputFields["steerMult"].maxValue = ActivePilot.UpToEleven ? 200 : 20;
                    inputFields["steerKiAdjust"].maxValue = ActivePilot.UpToEleven ? 20 : 1;
                    inputFields["steerDamping"].maxValue = ActivePilot.UpToEleven ? 100 : 8;

                    inputFields["DynamicDampingMin"].maxValue = ActivePilot.UpToEleven ? 100 : 8;
                    inputFields["DynamicDampingMax"].maxValue = ActivePilot.UpToEleven ? 100 : 8;
                    inputFields["dynamicSteerDampingFactor"].maxValue = ActivePilot.UpToEleven ? 100 : 10;
                    inputFields["DynamicDampingPitchMin"].maxValue = ActivePilot.UpToEleven ? 100 : 8;
                    inputFields["DynamicDampingPitchMax"].maxValue = ActivePilot.UpToEleven ? 100 : 8;
                    inputFields["dynamicSteerDampingPitchFactor"].maxValue = ActivePilot.UpToEleven ? 100 : 10;
                    inputFields["DynamicDampingYawMin"].maxValue = ActivePilot.UpToEleven ? 100 : 8;
                    inputFields["DynamicDampingYawMax"].maxValue = ActivePilot.UpToEleven ? 100 : 8;
                    inputFields["dynamicSteerDampingYawFactor"].maxValue = ActivePilot.UpToEleven ? 100 : 10;
                    inputFields["DynamicDampingRollMin"].maxValue = ActivePilot.UpToEleven ? 100 : 8;
                    inputFields["DynamicDampingRollMax"].maxValue = ActivePilot.UpToEleven ? 100 : 8;
                    inputFields["dynamicSteerDampingRollFactor"].maxValue = ActivePilot.UpToEleven ? 100 : 10;

                    inputFields["defaultAltitude"].maxValue = ActivePilot.UpToEleven ? 100000 : 15000;
                    inputFields["minAltitude"].maxValue = ActivePilot.UpToEleven ? 60000 : 6000;
                    inputFields["maxAltitude"].maxValue = ActivePilot.UpToEleven ? 100000 : 15000;

                    if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 55) inputFields["maxSpeed"].maxValue = 600;
                    else inputFields["maxSpeed"].maxValue = ActivePilot.UpToEleven ? 3000 : 800;
                    inputFields["takeOffSpeed"].maxValue = ActivePilot.UpToEleven ? 2000 : 200;
                    inputFields["minSpeed"].maxValue = ActivePilot.UpToEleven ? 2000 : 200;
                    inputFields["idleSpeed"].maxValue = ActivePilot.UpToEleven ? 3000 : 200;

                    inputFields["maxAllowedGForce"].maxValue = ActivePilot.UpToEleven ? 1000 : 45;
                    inputFields["maxAllowedAoA"].maxValue = ActivePilot.UpToEleven ? 180 : 90;
                    inputFields["postStallAoA"].maxValue = ActivePilot.UpToEleven ? 180 : (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 55) ? 0 : 90;

                    inputFields["minEvasionTime"].maxValue = ActivePilot.UpToEleven ? 10 : 1;
                    inputFields["evasionNonlinearity"].maxValue = ActivePilot.UpToEleven ? 90 : 10;
                    inputFields["evasionThreshold"].maxValue = ActivePilot.UpToEleven ? 300 : 100;
                    inputFields["evasionTimeThreshold"].maxValue = ActivePilot.UpToEleven ? 1 : 3;
                    inputFields["vesselStandoffDistance"].maxValue = ActivePilot.UpToEleven ? 5000 : 1000;
                    inputFields["extendDistanceAirToAir"].maxValue = ActivePilot.UpToEleven ? 20000 : 2000;
                    inputFields["extendAngleAirToAir"].maxValue = ActivePilot.UpToEleven ? 90 : 45;
                    inputFields["extendAngleAirToAir"].minValue = ActivePilot.UpToEleven ? -90 : -10;
                    inputFields["extendDistanceAirToGroundGuns"].maxValue = ActivePilot.UpToEleven ? 20000 : 5000;
                    inputFields["extendDistanceAirToGround"].maxValue = ActivePilot.UpToEleven ? 20000 : 5000;

                    inputFields["turnRadiusTwiddleFactorMin"].maxValue = ActivePilot.UpToEleven ? 10 : 5;
                    inputFields["turnRadiusTwiddleFactorMax"].maxValue = ActivePilot.UpToEleven ? 10 : 5;
                    inputFields["controlSurfaceDeploymentTime"].maxValue = ActivePilot.UpToEleven ? 10 : 4;

                    inputFields["controlSurfaceLag"].maxValue = ActivePilot.UpToEleven ? 1 : 0.2f;
                }
            }
            else if (AIType == typeof(BDModuleSurfaceAI))
            {
                if (oldClamp != ActiveDriver.UpToEleven)
                {
                    oldClamp = ActiveDriver.UpToEleven;

                    inputFields["MaxSlopeAngle"].maxValue = ActiveDriver.UpToEleven ? 90 : 30;
                    inputFields["CruiseSpeed"].maxValue = ActiveDriver.UpToEleven ? 300 : 60;
                    inputFields["MaxSpeed"].maxValue = ActiveDriver.UpToEleven ? 400 : 80;
                    inputFields["steerMult"].maxValue = ActiveDriver.UpToEleven ? 200 : 20;
                    inputFields["steerDamping"].maxValue = ActiveDriver.UpToEleven ? 100 : 10;
                    inputFields["MinEngagementRange"].maxValue = ActiveDriver.UpToEleven ? 20000 : 6000;
                    inputFields["MaxEngagementRange"].maxValue = ActiveDriver.UpToEleven ? 30000 : 8000;
                    inputFields["AvoidMass"].maxValue = ActiveDriver.UpToEleven ? 1000000 : 100;
                }
            }
        }

        public void SyncInputFieldsNow(bool fromInputFields)
        {
            if (inputFields == null) return;
            if (fromInputFields)
            {
                // Try to parse all the fields immediately so that they're up to date.
                foreach (var field in inputFields.Keys)
                { inputFields[field].tryParseValueNow(); }
                if (ActivePilot != null)
                {
                    foreach (var field in inputFields.Keys)
                    {
                        try
                        {
                            var fieldInfo = typeof(BDModulePilotAI).GetField(field);
                            if (fieldInfo != null)
                            { fieldInfo.SetValue(ActivePilot, Convert.ChangeType(inputFields[field].currentValue, fieldInfo.FieldType)); }
                            else // Check if it's a property instead of a field.
                            {
                                var propInfo = typeof(BDModulePilotAI).GetProperty(field);
                                propInfo.SetValue(ActivePilot, Convert.ChangeType(inputFields[field].currentValue, propInfo.PropertyType));
                            }
                        }
                        catch (Exception e) { Debug.LogError($"[BDArmory.BDArmoryAIGUI]: Failed to set current value of {field}: " + e.Message); }
                    }
                }
                else if (ActiveDriver != null)
                {
                    foreach (var field in inputFields.Keys)
                    {
                        try
                        {
                            var fieldInfo = typeof(BDModuleSurfaceAI).GetField(field);
                            if (fieldInfo != null)
                            { fieldInfo.SetValue(ActiveDriver, Convert.ChangeType(inputFields[field].currentValue, fieldInfo.FieldType)); }
                            else // Check if it's a property instead of a field.
                            {
                                var propInfo = typeof(BDModuleSurfaceAI).GetProperty(field);
                                propInfo.SetValue(ActiveDriver, Convert.ChangeType(inputFields[field].currentValue, propInfo.PropertyType));
                            }
                        }
                        catch (Exception e) { Debug.LogError($"[BDArmory.BDArmoryAIGUI]: Failed to set current value of {field}: " + e.Message); }
                    }
                }
                // Then make any special conversions here.
            }
            else // Set the input fields to their current values.
            {
                // Make any special conversions first.
                // Then set each of the field values to the current slider value.
                if (ActivePilot != null)
                {
                    foreach (var field in inputFields.Keys)
                    {
                        try
                        {
                            var fieldInfo = typeof(BDModulePilotAI).GetField(field);
                            if (fieldInfo != null)
                            { inputFields[field].currentValue = Convert.ToDouble(fieldInfo.GetValue(ActivePilot)); }
                            else // Check if it's a property instead of a field.
                            {
                                var propInfo = typeof(BDModulePilotAI).GetProperty(field);
                                inputFields[field].currentValue = Convert.ToDouble(propInfo.GetValue(ActivePilot));
                            }
                        }
                        catch (Exception e) { Debug.LogError($"[BDArmory.BDArmoryAIGUI]: Failed to set current value of {field}: " + e.Message + "\n" + e.StackTrace); }
                    }
                }
                else if (ActiveDriver != null)
                {
                    foreach (var field in inputFields.Keys)
                    {
                        try
                        {
                            var fieldInfo = typeof(BDModuleSurfaceAI).GetField(field);
                            if (fieldInfo != null)
                            { inputFields[field].currentValue = Convert.ToDouble(fieldInfo.GetValue(ActiveDriver)); }
                            else // Check if it's a property instead of a field.
                            {
                                var propInfo = typeof(BDModuleSurfaceAI).GetProperty(field);
                                inputFields[field].currentValue = Convert.ToDouble(propInfo.GetValue(ActiveDriver));
                            }
                        }
                        catch (Exception e) { Debug.LogError($"[BDArmory.BDArmoryAIGUI]: Failed to set current value of {field}: " + e.Message); }
                    }
                }
            }
        }

        public void SetChooseOptionSliders()
        {
            if (ActiveDriver != null)
            {
                Drivertype = VehicleMovementTypes.IndexOf(ActiveDriver.SurfaceType);
                broadsideDir = ActiveDriver.orbitDirections.IndexOf(ActiveDriver.OrbitDirectionName);
            }
        }

        #region GUI

        void OnGUI()
        {
            if (!BDArmorySetup.GAME_UI_ENABLED) return;

            if (!windowBDAAIGUIEnabled || (!HighLogic.LoadedSceneIsFlight && !HighLogic.LoadedSceneIsEditor)) return;
            if (!stylesConfigured) ConfigureStyles();
            if (HighLogic.LoadedSceneIsFlight) BDArmorySetup.SetGUIOpacity();
            if (Event.current.type == EventType.MouseUp && resizingWindow) { resizingWindow = false; }
            BDArmorySetup.WindowRectAI = GUI.Window(GUIUtility.GetControlID(FocusType.Passive), BDArmorySetup.WindowRectAI, WindowRectAI, "", BDArmorySetup.BDGuiSkin.window);//"BDA Weapon Manager"
            if (HighLogic.LoadedSceneIsFlight) BDArmorySetup.SetGUIOpacity(false);
            GUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectAI);
            GUIUtils.UpdateGUIRect(BDArmorySetup.WindowRectAI, _guiCheckIndex);
            GUIUtils.UseMouseEventInRect(BDArmorySetup.WindowRectAI);
        }

        void ConfigureStyles()
        {
            Label = new GUIStyle();
            Label.alignment = TextAnchor.UpperLeft;
            Label.normal.textColor = Color.white;

            rightLabel = new GUIStyle();
            rightLabel.alignment = TextAnchor.UpperRight;
            rightLabel.normal.textColor = Color.white;

            contextLabel = new GUIStyle(Label);

            inputFieldStyle = new GUIStyle(GUI.skin.textField);
            inputFieldStyle.alignment = TextAnchor.UpperRight;

            BoldLabel = new GUIStyle();
            BoldLabel.alignment = TextAnchor.UpperLeft;
            BoldLabel.fontStyle = FontStyle.Bold;
            BoldLabel.normal.textColor = Color.white;

            Title = new GUIStyle();
            Title.normal.textColor = BDArmorySetup.BDGuiSkin.window.normal.textColor;
            Title.font = BDArmorySetup.BDGuiSkin.window.font;
            Title.fontSize = BDArmorySetup.BDGuiSkin.window.fontSize;
            Title.fontStyle = BDArmorySetup.BDGuiSkin.window.fontStyle;
            Title.alignment = TextAnchor.UpperCenter;

            infoLinkStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.label);
            infoLinkStyle.alignment = TextAnchor.UpperLeft;
            infoLinkStyle.normal.textColor = Color.white;

            stylesConfigured = true;
        }

        float pidHeight;
        float speedHeight;
        float altitudeHeight;
        float evasionHeight;
        float controlHeight;
        float terrainHeight;
        float rammingHeight;
        float miscHeight;

        Rect TitleButtonRect(float offset)
        {
            return new Rect((ColumnWidth * 2) - _windowMargin - (offset * _buttonSize), _windowMargin, _buttonSize, _buttonSize);
        }

        Rect SubsectionRect(float indent, float line)
        {
            return new Rect(indent, contentTop + (line * entryHeight), 100, entryHeight);
        }

        Rect SettinglabelRect(float indent, float lines)
        {
            return new Rect(indent, (lines * entryHeight), labelWidth, entryHeight);
        }
        Rect SettingSliderRect(float indent, float lines, float contentWidth)
        {
            return new Rect(indent + labelWidth, (lines + 0.2f) * entryHeight, contentWidth - (indent * 2) - labelWidth, entryHeight);
        }
        Rect SettingTextRect(float indent, float lines, float contentWidth)
        {
            return new Rect(indent + labelWidth, lines * entryHeight, contentWidth - (indent * 2) - labelWidth, entryHeight);
        }
        Rect ContextLabelRect(float indent, float lines)
        {
            return new Rect(labelWidth + indent, lines * entryHeight, 100, entryHeight);
        }
        Rect ContextLabelRectRight(float indent, float lines, float contentWidth)
        {
            return new Rect(contentWidth - 100 - 2 * indent, lines * entryHeight, 100, entryHeight);
        }

        Rect ToggleButtonRect(float indent, float lines, float contentWidth)
        {
            return new Rect(indent, (lines * entryHeight), contentWidth - (2 * indent), entryHeight);
        }

        Rect ToggleButtonRects(float indent, float lines, float pos, float of, float contentWidth)
        {
            var gap = indent / 2f;
            return new Rect(indent + pos / of * (contentWidth - gap * (of - 1f) - 2f * indent) + pos * gap, lines * entryHeight, 1f / of * (contentWidth - gap * (of - 1f) - 2f * indent), entryHeight);
        }

        void WindowRectAI(int windowID)
        {
            float line = 0;
            float leftIndent = 10;
            float windowColumns = 2;
            float contentWidth = ((ColumnWidth * 2) - 100 - 20);

            GUI.DragWindow(new Rect(_windowMargin + _buttonSize * 6, 0, (ColumnWidth * 2) - (2 * _windowMargin) - (10 * _buttonSize), _windowMargin + _buttonSize));

            GUI.Label(new Rect(100, contentTop, contentWidth, entryHeight), StringUtils.Localize("#LOC_BDArmory_AIWindow_title"), Title);// "No AI found."

            line += 1.25f;
            line += 0.25f;

            //Exit Button
            GUIStyle buttonStyle = windowBDAAIGUIEnabled ? BDArmorySetup.BDGuiSkin.button : BDArmorySetup.BDGuiSkin.box;
            if (GUI.Button(TitleButtonRect(1), "X", buttonStyle))
            {
                ToggleAIGUI();
            }

            //Infolink button
            buttonStyle = infoLinkEnabled ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button;
            if (GUI.Button(TitleButtonRect(2), "i", buttonStyle))
            {
                infoLinkEnabled = !infoLinkEnabled;
            }

            //Context labels button
            buttonStyle = contextTipsEnabled ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button;
            if (GUI.Button(TitleButtonRect(3), "?", buttonStyle))
            {
                contextTipsEnabled = !contextTipsEnabled;
            }

            //Numeric fields button
            buttonStyle = NumFieldsEnabled ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button;
            if (GUI.Button(TitleButtonRect(4), "#", buttonStyle))
            {
                NumFieldsEnabled = !NumFieldsEnabled;
                SyncInputFieldsNow(!NumFieldsEnabled);
            }

            if (ActivePilot == null && ActiveDriver == null)
            {
                GUI.Label(new Rect(leftIndent, contentTop + (1.75f * entryHeight), contentWidth, entryHeight),
                   StringUtils.Localize("#LOC_BDArmory_AIWindow_NoAI"), Title);// "No AI found."
                line += 4;
            }
            else
            {
                height = Mathf.Lerp(height, contentHeight, 0.15f);
                if (ActivePilot != null)
                {
                    GUIStyle saveStyle = BDArmorySetup.BDGuiSkin.button;
                    if (GUI.Button(new Rect(_windowMargin, _windowMargin, _buttonSize * 3, _buttonSize), "Save", saveStyle))
                    {
                        ActivePilot.StoreSettings();
                    }

                    if (ActivePilot.Events["RestoreSettings"].active == true)
                    {
                        GUIStyle restoreStyle = BDArmorySetup.BDGuiSkin.button;
                        if (GUI.Button(new Rect(_windowMargin + _buttonSize * 3, _windowMargin, _buttonSize * 3, _buttonSize), "Restore", restoreStyle))
                        {
                            ActivePilot.RestoreSettings();
                        }
                    }

                    showPID = GUI.Toggle(SubsectionRect(leftIndent, line),
                        showPID, StringUtils.Localize("#LOC_BDArmory_PilotAI_PID"), showPID ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"PiD"
                    line += 1.5f;

                    showAltitude = GUI.Toggle(SubsectionRect(leftIndent, line),
                        showAltitude, StringUtils.Localize("#LOC_BDArmory_PilotAI_Altitudes"), showAltitude ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Altitude"
                    line += 1.5f;

                    showSpeed = GUI.Toggle(SubsectionRect(leftIndent, line),
                        showSpeed, StringUtils.Localize("#LOC_BDArmory_PilotAI_Speeds"), showSpeed ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Speed"
                    line += 1.5f;

                    showControl = GUI.Toggle(SubsectionRect(leftIndent, line),
                        showControl, StringUtils.Localize("#LOC_BDArmory_AIWindow_ControlLimits"), showControl ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Control"
                    line += 1.5f;

                    showEvade = GUI.Toggle(SubsectionRect(leftIndent, line),
                        showEvade, StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeExtend"), showEvade ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Evasion"
                    line += 1.5f;

                    showTerrain = GUI.Toggle(SubsectionRect(leftIndent, line),
                        showTerrain, StringUtils.Localize("#LOC_BDArmory_AIWindow_Terrain"), showTerrain ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Terrain"
                    line += 1.5f;

                    showRam = GUI.Toggle(SubsectionRect(leftIndent, line),
                        showRam, StringUtils.Localize("#LOC_BDArmory_PilotAI_Ramming"), showRam ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Ramming"
                    line += 1.5f;

                    showMisc = GUI.Toggle(SubsectionRect(leftIndent, line),
                        showMisc, StringUtils.Localize("#LOC_BDArmory_PilotAI_Misc"), showMisc ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Misc"
                    line += 1.5f;

                    ActivePilot.UpToEleven = GUI.Toggle(SubsectionRect(leftIndent, line),
                        ActivePilot.UpToEleven, ActivePilot.UpToEleven ? StringUtils.Localize("#LOC_BDArmory_UnclampTuning_enabledText") : StringUtils.Localize("#LOC_BDArmory_UnclampTuning_disabledText"), ActivePilot.UpToEleven ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Misc"
                    if (ActivePilot.UpToEleven != oldClamp)
                    {
                        SetInputFields(ActivePilot.GetType());
                    }
                    line += 5;

                    float pidLines = 0;
                    float altLines = 0;
                    float spdLines = 0;
                    float ctrlLines = 0;
                    float evadeLines = 0;
                    float gndLines = 0;
                    float ramLines = 0;
                    float miscLines = 0;

                    if (infoLinkEnabled)
                    {
                        windowColumns = 3;

                        GUI.Label(new Rect(leftIndent + ColumnWidth * 2, contentTop, ColumnWidth - leftIndent, entryHeight), StringUtils.Localize("#LOC_BDArmory_AIWindow_infoLink"), Title);//"infolink"
                        BeginArea(new Rect(leftIndent + ColumnWidth * 2, contentTop + entryHeight * 1.5f, ColumnWidth - leftIndent, WindowHeight - entryHeight * 1.5f - 2 * contentTop));
                        using (var scrollViewScope = new ScrollViewScope(scrollInfoVector, Width(ColumnWidth - leftIndent), Height(WindowHeight - entryHeight * 1.5f - 2 * contentTop)))
                        {
                            scrollInfoVector = scrollViewScope.scrollPosition;

                            if (showPID) //these autoalign, so if new entries need to be added, they can just be slotted in
                            {
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_PilotAI_PID"), BoldLabel, Width(ColumnWidth - leftIndent * 4 - 20)); //PID label
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_PidHelp"), infoLinkStyle, Width(ColumnWidth - leftIndent * 4 - 20)); //Pid desc
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_PidHelp_SteerMult"), infoLinkStyle, Width(ColumnWidth - leftIndent * 4 - 20)); //steer mult desc
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_PidHelp_SteerKi"), infoLinkStyle, Width(ColumnWidth - leftIndent * 4 - 20)); //steer ki desc.
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_PidHelp_Steerdamp"), infoLinkStyle, Width(ColumnWidth - leftIndent * 4 - 20)); //steer damp description
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_PidHelp_Dyndamp"), infoLinkStyle, Width(ColumnWidth - leftIndent * 4 - 20)); //dynamic damping desc
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_PidHelp_AutoTune") + (ActivePilot.AutoTune ? StringUtils.Localize("#LOC_BDArmory_AIWindow_PidHelp_AutoTune_details") : ""), infoLinkStyle, Width(ColumnWidth - leftIndent * 4 - 20)); //auto-tuning desc
                            }
                            if (showAltitude)
                            {
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_PilotAI_Altitudes"), BoldLabel, Width(ColumnWidth - (leftIndent * 4) - 20)); //Altitude label
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_AltHelp"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //altitude description
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_AltHelp_Def"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //default alt desc
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_AltHelp_min"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //min alt desc
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_AltHelp_max"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //max alt desc
                            }
                            if (showSpeed)
                            {
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_PilotAI_Speeds"), BoldLabel, Width(ColumnWidth - (leftIndent * 4) - 20)); //Speed header
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_SpeedHelp"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //speed explanation
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_SpeedHelp_min"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //min+max speed desc
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_SpeedHelp_takeoff"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //takeoff speed
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_SpeedHelp_gnd"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //strafe speed
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_SpeedHelp_idle"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //idle speed
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_SpeedHelp_ABpriority"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //AB priority
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_SpeedHelp_ABOverrideThreshold"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //AB override threshold
                            }
                            if (showControl)
                            {
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_PilotAI_ControlLimits"), BoldLabel, Width(ColumnWidth - (leftIndent * 4) - 20)); //conrrol header
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_ControlHelp"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //control desc
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_ControlHelp_limiters"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //low + high speed limiters
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_ControlHelp_bank"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //max bank desc
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_ControlHelp_clamps"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //max G + max AoA
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_ControlHelp_modeSwitches"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //post-stall + Immelmann turn angle
                            }
                            if (showEvade)
                            {
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_PilotAI_EvadeExtend"), BoldLabel, Width(ColumnWidth - (leftIndent * 4) - 20)); //evade header
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeHelp"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //evade description
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeHelp_Evade"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //evade dist/ time/ time threshold
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeHelp_Nonlinearity"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //evade/extend nonlinearity
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeHelp_Dodge"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //collision avoid
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeHelp_standoff"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //standoff distance
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeHelp_Extend"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //extend distances
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeHelp_ExtendVars"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //extend target dist/angle/vel
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeHelp_ExtendVel"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //extend target velocity
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeHelp_ExtendAngle"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //extend target angle
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeHelp_ExtendDist"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //extend target dist
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeHelp_ExtendAbortTime"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //extend abort time
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeHelp_ExtendToggle"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //evade/extend toggle
                            }
                            if (showTerrain)
                            {
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_PilotAI_Terrain"), BoldLabel, Width(ColumnWidth - (leftIndent * 4) - 20)); //Terrain avoid header
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_TerrainHelp"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //terrain avoid desc
                            }
                            if (showRam)
                            {
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_PilotAI_Ramming"), BoldLabel, Width(ColumnWidth - (leftIndent * 4) - 20)); //ramming header
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_RamHelp"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20));// ramming desc
                            }
                            if (showMisc)
                            {
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_PilotAI_Misc"), BoldLabel, Width(ColumnWidth - (leftIndent * 4) - 20)); //misc header
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_miscHelp"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //misc desc
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_orbitHelp"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //orbit dir
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_standbyHelp"), infoLinkStyle, Width(ColumnWidth - (leftIndent * 4) - 20)); //standby
                            }
                        }
                        EndArea();
                    }

                    if (showPID || showAltitude || showSpeed || showControl || showEvade || showTerrain || showRam || showMisc)
                    {
                        scrollViewVector = GUI.BeginScrollView(new Rect(leftIndent + 100, contentTop + (entryHeight * 1.5f), (ColumnWidth * 2) - 100 - (leftIndent), WindowHeight - entryHeight * 1.5f - (2 * contentTop)), scrollViewVector, new Rect(0, 0, (ColumnWidth * 2) - 120 - (leftIndent * 2), height + 5));

                        GUI.BeginGroup(new Rect(leftIndent, 0, (ColumnWidth * 2) - 120 - (leftIndent * 2), height + 5), GUIContent.none, BDArmorySetup.BDGuiSkin.box); //darker box

                        contentWidth -= 24;
                        leftIndent += 3;

                        if (showPID)
                        {
                            pidLines += 0.2f;
                            GUI.BeginGroup(new Rect(0, (pidLines * entryHeight), contentWidth, pidHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                            pidLines += 0.25f;

                            GUI.Label(SettinglabelRect(leftIndent, pidLines), StringUtils.Localize("#LOC_BDArmory_PilotAI_PID"), BoldLabel);//"Pid Controller"
                            pidLines++;

                            if (!NumFieldsEnabled)
                            {
                                if (ActivePilot.steerMult != (ActivePilot.steerMult = GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines, contentWidth), ActivePilot.steerMult, 0.1f, ActivePilot.UpToEleven ? 200 : 20)))
                                    ActivePilot.steerMult = BDAMath.RoundToUnit(ActivePilot.steerMult, 0.1f);
                            }
                            else
                            {
                                inputFields["steerMult"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines, contentWidth), inputFields["steerMult"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.steerMult = (float)inputFields["steerMult"].currentValue;
                            }
                            GUI.Label(SettinglabelRect(leftIndent, pidLines), StringUtils.Localize("#LOC_BDArmory_SteerFactor") + ": " + ActivePilot.steerMult.ToString("0.0"), Label);//"Steer Mult"


                            pidLines++;
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, pidLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_SteerMultLow"), Label);//"sluggish"
                                GUI.Label(ContextLabelRectRight(leftIndent, pidLines, contentWidth), StringUtils.Localize("#LOC_BDArmory_AIWindow_SteerMultHi"), rightLabel);//"twitchy"
                                pidLines++;
                            }
                            if (!NumFieldsEnabled)
                            {
                                if (ActivePilot.steerKiAdjust != (ActivePilot.steerKiAdjust = GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines, contentWidth), ActivePilot.steerKiAdjust, 0.01f, ActivePilot.UpToEleven ? 20 : 1)))
                                    ActivePilot.steerKiAdjust = BDAMath.RoundToUnit(ActivePilot.steerKiAdjust, 0.01f);
                            }
                            else
                            {
                                inputFields["steerKiAdjust"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines, contentWidth), inputFields["steerKiAdjust"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.steerKiAdjust = (float)inputFields["steerKiAdjust"].currentValue;
                            }
                            GUI.Label(SettinglabelRect(leftIndent, pidLines), StringUtils.Localize("#LOC_BDArmory_SteerKi") + ": " + ActivePilot.steerKiAdjust.ToString("0.00"), Label);//"Steer Ki"
                            pidLines++;

                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, pidLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_SteerKiLow"), Label);//"undershoot"
                                GUI.Label(ContextLabelRectRight(leftIndent, pidLines, contentWidth), StringUtils.Localize("#LOC_BDArmory_AIWindow_SteerKiHi"), rightLabel);//"Overshoot"
                                pidLines++;
                            }
                            if (!NumFieldsEnabled)
                            {
                                if (ActivePilot.steerDamping != (ActivePilot.steerDamping = GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines, contentWidth), ActivePilot.steerDamping, 0.1f, ActivePilot.UpToEleven ? 100 : 8)))
                                    ActivePilot.steerDamping = BDAMath.RoundToUnit(ActivePilot.steerDamping, 0.1f);
                            }
                            else
                            {
                                inputFields["steerDamping"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines, contentWidth), inputFields["steerDamping"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.steerDamping = (float)inputFields["steerDamping"].currentValue;
                            }
                            GUI.Label(SettinglabelRect(leftIndent, pidLines), StringUtils.Localize("#LOC_BDArmory_SteerDamping") + ": " + ActivePilot.steerDamping.ToString("0.00"), Label);//"Steer Damping"

                            pidLines++;
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, pidLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_SteerDampLow"), Label);//"Wobbly"
                                GUI.Label(ContextLabelRectRight(leftIndent, pidLines, contentWidth), StringUtils.Localize("#LOC_BDArmory_AIWindow_SteerDampHi"), rightLabel);//"Stiff"
                                pidLines++;
                            }

                            ActivePilot.dynamicSteerDamping = GUI.Toggle(ToggleButtonRect(leftIndent, pidLines, contentWidth), ActivePilot.dynamicSteerDamping, StringUtils.Localize("#LOC_BDArmory_DynamicDamping"), ActivePilot.dynamicSteerDamping ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Dynamic damping"
                            pidLines += 1.25f;

                            if (ActivePilot.dynamicSteerDamping)
                            {
                                float dynPidLines = 0;
                                ActivePilot.CustomDynamicAxisFields = GUI.Toggle(ToggleButtonRect(leftIndent, pidLines, contentWidth), ActivePilot.CustomDynamicAxisFields, StringUtils.Localize("#LOC_BDArmory_3AxisDynamicSteerDamping"), ActivePilot.CustomDynamicAxisFields ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"3 axis damping"
                                dynPidLines++;
                                if (!ActivePilot.CustomDynamicAxisFields)
                                {
                                    dynPidLines += 0.25f;

                                    GUI.Label(SettinglabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_DynamicDamping") + ": " + ActivePilot.dynSteerDampingValue.ToString(), Label);//"Dynamic Damping"
                                    dynPidLines++;
                                    if (!NumFieldsEnabled)
                                    {
                                        if (ActivePilot.DynamicDampingMin != (ActivePilot.DynamicDampingMin = GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines + dynPidLines, contentWidth), ActivePilot.DynamicDampingMin, 0.1f, ActivePilot.UpToEleven ? 100 : 8)))
                                            ActivePilot.DynamicDampingMin = BDAMath.RoundToUnit(ActivePilot.DynamicDampingMin, 0.1f);
                                    }
                                    else
                                    {
                                        inputFields["DynamicDampingMin"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines + dynPidLines, contentWidth), inputFields["DynamicDampingMin"].possibleValue, 6, inputFieldStyle));
                                        ActivePilot.DynamicDampingMin = (float)inputFields["DynamicDampingMin"].currentValue;
                                    }
                                    GUI.Label(SettinglabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_DynamicDampingMin") + ": " + ActivePilot.DynamicDampingMin.ToString("0.0"), Label);//"dynamic damping min"
                                    dynPidLines++;
                                    if (contextTipsEnabled)
                                    {
                                        GUI.Label(ContextLabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_DynDampMin"), Label);//"dynamic damp min"
                                        dynPidLines++;
                                    }
                                    if (!NumFieldsEnabled)
                                    {
                                        if (ActivePilot.DynamicDampingMax != (ActivePilot.DynamicDampingMax = GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines + dynPidLines, contentWidth), ActivePilot.DynamicDampingMax, 0.1f, ActivePilot.UpToEleven ? 100 : 8)))
                                            ActivePilot.DynamicDampingMax = BDAMath.RoundToUnit(ActivePilot.DynamicDampingMax, 0.1f);
                                    }
                                    else
                                    {
                                        inputFields["DynamicDampingMax"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines + dynPidLines, contentWidth), inputFields["DynamicDampingMax"].possibleValue, 6, inputFieldStyle));
                                        ActivePilot.DynamicDampingMax = (float)inputFields["DynamicDampingMax"].currentValue;
                                    }
                                    GUI.Label(SettinglabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_DynamicDampingMax") + ": " + ActivePilot.DynamicDampingMax.ToString("0.0"), Label);//"dynamic damping max"

                                    dynPidLines++;
                                    if (contextTipsEnabled)
                                    {
                                        GUI.Label(ContextLabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_DynDampMax"), Label);//"dynamic damp max"
                                        dynPidLines++;
                                    }
                                    if (!NumFieldsEnabled)
                                    {
                                        if (ActivePilot.dynamicSteerDampingFactor != (ActivePilot.dynamicSteerDampingFactor = GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines + dynPidLines, contentWidth), ActivePilot.dynamicSteerDampingFactor, 0.1f, ActivePilot.UpToEleven ? 100 : 10)))
                                            ActivePilot.dynamicSteerDampingFactor = BDAMath.RoundToUnit(ActivePilot.dynamicSteerDampingFactor, 0.1f);
                                    }
                                    else
                                    {
                                        inputFields["dynamicSteerDampingFactor"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines + dynPidLines, contentWidth), inputFields["dynamicSteerDampingFactor"].possibleValue, 6, inputFieldStyle));
                                        ActivePilot.dynamicSteerDampingFactor = (float)inputFields["dynamicSteerDampingFactor"].currentValue;
                                    }
                                    GUI.Label(SettinglabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_DynDampMult") + ": " + ActivePilot.dynamicSteerDampingFactor.ToString("0.0"), Label);//"dynamic damping mult"

                                    dynPidLines++;
                                    if (contextTipsEnabled)
                                    {
                                        GUI.Label(ContextLabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_DynDampMult"), Label);//"dynamic damp mult"
                                        dynPidLines++;
                                    }
                                }
                                else
                                {
                                    ActivePilot.dynamicDampingPitch = GUI.Toggle(ToggleButtonRect(leftIndent, pidLines + dynPidLines, contentWidth), ActivePilot.dynamicDampingPitch, StringUtils.Localize("#LOC_BDArmory_DynamicDampingPitch"), ActivePilot.dynamicDampingPitch ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Dynamic damp pitch"
                                    dynPidLines += 1.25f;

                                    if (ActivePilot.dynamicDampingPitch)
                                    {
                                        GUI.Label(SettinglabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_DynamicDampingPitch") + ": " + ActivePilot.dynSteerDampingPitchValue.ToString(), Label);//"dynamic damp pitch"
                                        dynPidLines++;
                                        if (!NumFieldsEnabled)
                                        {
                                            if (ActivePilot.DynamicDampingPitchMin != (ActivePilot.DynamicDampingPitchMin = GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines + dynPidLines, contentWidth), ActivePilot.DynamicDampingPitchMin, 0.1f, ActivePilot.UpToEleven ? 100 : 8)))
                                                ActivePilot.DynamicDampingPitchMin = BDAMath.RoundToUnit(ActivePilot.DynamicDampingPitchMin, 0.1f);
                                        }
                                        else
                                        {
                                            inputFields["DynamicDampingPitchMin"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines + dynPidLines, contentWidth), inputFields["DynamicDampingPitchMin"].possibleValue, 6, inputFieldStyle));
                                            ActivePilot.DynamicDampingPitchMin = (float)inputFields["DynamicDampingPitchMin"].currentValue;
                                        }
                                        GUI.Label(SettinglabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_DynamicDampingPitchMin") + ": " + ActivePilot.DynamicDampingPitchMin.ToString("0.0"), Label);//"dynamic damping min"
                                        dynPidLines++;
                                        if (contextTipsEnabled)
                                        {
                                            GUI.Label(ContextLabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_DynDampMin"), contextLabel);//"dynamic damp min"
                                            dynPidLines++;
                                        }
                                        if (!NumFieldsEnabled)
                                        {
                                            if (ActivePilot.DynamicDampingPitchMax != (ActivePilot.DynamicDampingPitchMax = GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines + dynPidLines, contentWidth), ActivePilot.DynamicDampingPitchMax, 0.1f, ActivePilot.UpToEleven ? 100 : 8)))
                                                ActivePilot.DynamicDampingPitchMax = BDAMath.RoundToUnit(ActivePilot.DynamicDampingPitchMax, 0.1f);
                                        }
                                        else
                                        {
                                            inputFields["DynamicDampingPitchMax"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines + dynPidLines, contentWidth), inputFields["DynamicDampingPitchMax"].possibleValue, 6, inputFieldStyle));
                                            ActivePilot.DynamicDampingPitchMax = (float)inputFields["DynamicDampingPitchMax"].currentValue;
                                        }
                                        GUI.Label(SettinglabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_DynamicDampingMax") + ": " + ActivePilot.DynamicDampingPitchMax.ToString("0.0"), Label);//"dynamic damping max"

                                        dynPidLines++;
                                        if (contextTipsEnabled)
                                        {
                                            GUI.Label(ContextLabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_DynDampMax"), contextLabel);//"damp max"
                                            dynPidLines++;
                                        }
                                        if (!NumFieldsEnabled)
                                        {
                                            if (ActivePilot.dynamicSteerDampingPitchFactor != (ActivePilot.dynamicSteerDampingPitchFactor = GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines + dynPidLines, contentWidth), ActivePilot.dynamicSteerDampingPitchFactor, 0.1f, ActivePilot.UpToEleven ? 100 : 10)))
                                                ActivePilot.dynamicSteerDampingPitchFactor = BDAMath.RoundToUnit(ActivePilot.dynamicSteerDampingPitchFactor, 0.1f);
                                        }
                                        else
                                        {
                                            inputFields["dynamicSteerDampingPitchFactor"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines + dynPidLines, contentWidth), inputFields["dynamicSteerDampingPitchFactor"].possibleValue, 6, inputFieldStyle));
                                            ActivePilot.dynamicSteerDampingPitchFactor = (float)inputFields["dynamicSteerDampingPitchFactor"].currentValue;
                                        }
                                        GUI.Label(SettinglabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_DynamicDampingPitchFactor") + ": " + ActivePilot.dynamicSteerDampingPitchFactor.ToString("0.0"), Label);//"dynamic damping mult"

                                        dynPidLines++;
                                        if (contextTipsEnabled)
                                        {
                                            GUI.Label(ContextLabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_DynDampMult"), contextLabel);//"dynamic damp Mult"
                                            dynPidLines++;
                                        }
                                    }

                                    ActivePilot.dynamicDampingYaw = GUI.Toggle(ToggleButtonRect(leftIndent, pidLines + dynPidLines, contentWidth), ActivePilot.dynamicDampingYaw, StringUtils.Localize("#LOC_BDArmory_DynamicDampingYaw"), ActivePilot.dynamicDampingYaw ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Dynamic damp yaw"
                                    dynPidLines += 1.25f;
                                    if (ActivePilot.dynamicDampingYaw)
                                    {
                                        GUI.Label(SettinglabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_DynamicDampingYaw") + ": " + ActivePilot.dynSteerDampingYawValue.ToString(), Label);//"dynamic damp yaw"
                                        dynPidLines++;
                                        if (!NumFieldsEnabled)
                                        {
                                            if (ActivePilot.DynamicDampingYawMin != (ActivePilot.DynamicDampingYawMin = GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines + dynPidLines, contentWidth), ActivePilot.DynamicDampingYawMin, 0.1f, ActivePilot.UpToEleven ? 100 : 8)))
                                                ActivePilot.DynamicDampingYawMin = BDAMath.RoundToUnit(ActivePilot.DynamicDampingYawMin, 0.1f);
                                        }
                                        else
                                        {
                                            inputFields["DynamicDampingYawMin"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines + dynPidLines, contentWidth), inputFields["DynamicDampingYawMin"].possibleValue, 6, inputFieldStyle));
                                            ActivePilot.DynamicDampingYawMin = (float)inputFields["DynamicDampingYawMin"].currentValue;
                                        }
                                        GUI.Label(SettinglabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_DynamicDampingYawMin") + ": " + ActivePilot.DynamicDampingYawMin.ToString("0.0"), Label);//"dynamic damping min"

                                        dynPidLines++;
                                        if (contextTipsEnabled)
                                        {
                                            GUI.Label(ContextLabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_DynDampMin"), contextLabel);//"dynamic damp min"
                                            dynPidLines++;
                                        }
                                        if (!NumFieldsEnabled)
                                        {
                                            if (ActivePilot.DynamicDampingYawMax != (ActivePilot.DynamicDampingYawMax = GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines + dynPidLines, contentWidth), ActivePilot.DynamicDampingYawMax, 0.1f, ActivePilot.UpToEleven ? 100 : 8)))
                                                ActivePilot.DynamicDampingYawMax = BDAMath.RoundToUnit(ActivePilot.DynamicDampingYawMax, 0.1f);
                                        }
                                        else
                                        {
                                            inputFields["DynamicDampingYawMax"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines + dynPidLines, contentWidth), inputFields["DynamicDampingYawMax"].possibleValue, 6, inputFieldStyle));
                                            ActivePilot.DynamicDampingYawMax = (float)inputFields["DynamicDampingYawMax"].currentValue;
                                        }
                                        GUI.Label(SettinglabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_DynamicDampingYawMax") + ": " + ActivePilot.DynamicDampingYawMax.ToString("0.0"), Label);//"dynamic damping max"

                                        dynPidLines++;
                                        if (contextTipsEnabled)
                                        {
                                            GUI.Label(ContextLabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_DynDampMax"), contextLabel);//"dynamic damp max"
                                            dynPidLines++;
                                        }
                                        if (!NumFieldsEnabled)
                                        {
                                            if (ActivePilot.dynamicSteerDampingYawFactor != (ActivePilot.dynamicSteerDampingYawFactor = GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines + dynPidLines, contentWidth), ActivePilot.dynamicSteerDampingYawFactor, 0.1f, ActivePilot.UpToEleven ? 100 : 10)))
                                                ActivePilot.dynamicSteerDampingYawFactor = BDAMath.RoundToUnit(ActivePilot.dynamicSteerDampingYawFactor, 0.1f);
                                        }
                                        else
                                        {
                                            inputFields["dynamicSteerDampingYawFactor"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines + dynPidLines, contentWidth), inputFields["dynamicSteerDampingYawFactor"].possibleValue, 6, inputFieldStyle));
                                            ActivePilot.dynamicSteerDampingYawFactor = (float)inputFields["dynamicSteerDampingYawFactor"].currentValue;
                                        }
                                        GUI.Label(SettinglabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_DynamicDampingYawFactor") + ": " + ActivePilot.dynamicSteerDampingYawFactor.ToString("0.0"), Label);//"dynamic damping yaw mult"

                                        dynPidLines++;
                                        if (contextTipsEnabled)
                                        {
                                            GUI.Label(ContextLabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_DynDampMult"), contextLabel);//"dynamic damp mult"
                                            dynPidLines++;
                                        }
                                    }

                                    ActivePilot.dynamicDampingRoll = GUI.Toggle(ToggleButtonRect(leftIndent, pidLines + dynPidLines, contentWidth), ActivePilot.dynamicDampingRoll, StringUtils.Localize("#LOC_BDArmory_DynamicDampingRoll"), ActivePilot.dynamicDampingRoll ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Dynamic damp roll"
                                    dynPidLines += 1.25f;
                                    if (ActivePilot.dynamicDampingRoll)
                                    {
                                        GUI.Label(SettinglabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_DynamicDampingRoll") + ": " + ActivePilot.dynSteerDampingRollValue.ToString(), Label);//"dynamic damp roll"
                                        dynPidLines++;
                                        if (!NumFieldsEnabled)
                                        {
                                            if (ActivePilot.DynamicDampingRollMin != (ActivePilot.DynamicDampingRollMin = GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines + dynPidLines, contentWidth), ActivePilot.DynamicDampingRollMin, 0.1f, ActivePilot.UpToEleven ? 100 : 8)))
                                                ActivePilot.DynamicDampingRollMin = BDAMath.RoundToUnit(ActivePilot.DynamicDampingRollMin, 0.1f);
                                        }
                                        else
                                        {
                                            inputFields["DynamicDampingRollMin"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines + dynPidLines, contentWidth), inputFields["DynamicDampingRollMin"].possibleValue, 6, inputFieldStyle));
                                            ActivePilot.DynamicDampingRollMin = (float)inputFields["DynamicDampingRollMin"].currentValue;
                                        }
                                        GUI.Label(SettinglabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_DynamicDampingRollMin") + ": " + ActivePilot.DynamicDampingRollMin.ToString("0.0"), Label);//"dynamic damping min"

                                        dynPidLines++;
                                        if (contextTipsEnabled)
                                        {
                                            GUI.Label(ContextLabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_DynDampMin"), contextLabel);//"dynamic damp min"
                                            dynPidLines++;
                                        }
                                        if (!NumFieldsEnabled)
                                        {
                                            if (ActivePilot.DynamicDampingRollMax != (ActivePilot.DynamicDampingRollMax = GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines + dynPidLines, contentWidth), ActivePilot.DynamicDampingRollMax, 0.1f, ActivePilot.UpToEleven ? 100 : 8)))
                                                ActivePilot.DynamicDampingRollMax = BDAMath.RoundToUnit(ActivePilot.DynamicDampingRollMax, 0.1f);
                                        }
                                        else
                                        {
                                            inputFields["DynamicDampingRollMax"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines + dynPidLines, contentWidth), inputFields["DynamicDampingRollMax"].possibleValue, 6, inputFieldStyle));
                                            ActivePilot.DynamicDampingRollMax = (float)inputFields["DynamicDampingRollMax"].currentValue;
                                        }
                                        GUI.Label(SettinglabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_DynamicDampingRollMax") + ": " + ActivePilot.DynamicDampingRollMax.ToString("0.0"), Label);//"dynamic damping max"

                                        dynPidLines++;
                                        if (contextTipsEnabled)
                                        {
                                            GUI.Label(ContextLabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_DynDampMax"), contextLabel);//"dynamic damp max"
                                            dynPidLines++;
                                        }
                                        if (!NumFieldsEnabled)
                                        {
                                            if (ActivePilot.dynamicSteerDampingRollFactor != (ActivePilot.dynamicSteerDampingRollFactor = GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines + dynPidLines, contentWidth), ActivePilot.dynamicSteerDampingRollFactor, 0.1f, ActivePilot.UpToEleven ? 100 : 10)))
                                                ActivePilot.dynamicSteerDampingRollFactor = BDAMath.RoundToUnit(ActivePilot.dynamicSteerDampingRollFactor, 0.1f);
                                        }
                                        else
                                        {
                                            inputFields["dynamicSteerDampingRollFactor"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines + dynPidLines, contentWidth), inputFields["dynamicSteerDampingRollFactor"].possibleValue, 6, inputFieldStyle));
                                            ActivePilot.dynamicSteerDampingRollFactor = (float)inputFields["dynamicSteerDampingRollFactor"].currentValue;
                                        }
                                        GUI.Label(SettinglabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_DynamicDampingRollFactor") + ": " + ActivePilot.dynamicSteerDampingRollFactor.ToString("0.0"), Label);//"dynamic damping roll mult"
                                        dynPidLines++;
                                        if (contextTipsEnabled)
                                        {
                                            GUI.Label(ContextLabelRect(leftIndent, pidLines + dynPidLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_DynDampMult"), contextLabel);//"dynamic damp mult"
                                            dynPidLines++;
                                        }
                                    }
                                }
                                pidLines += dynPidLines;
                            }

                            #region AutoTune
                            if (ActivePilot.AutoTune != GUI.Toggle(ToggleButtonRect(leftIndent, pidLines, contentWidth), ActivePilot.AutoTune, StringUtils.Localize("#LOC_BDArmory_PIDAutoTune"), ActivePilot.AutoTune ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
                            {
                                ActivePilot.AutoTune = !ActivePilot.AutoTune; // Only actually toggle it when needed as the setter does extra stuff.
                            }
                            pidLines += 1.25f;
                            if (ActivePilot.AutoTune) // Auto-tuning
                            {
                                float autoTuneLines = 0.25f;
                                GUI.Label(SettinglabelRect(leftIndent, pidLines + autoTuneLines++), StringUtils.Localize("#LOC_BDArmory_AutoTuningLoss") + $": {ActivePilot.autoTuningLossLabel}", Label);
                                GUI.Label(SettinglabelRect(leftIndent, pidLines + autoTuneLines++), $"\tParams: {ActivePilot.autoTuningLossLabel2}", Label);
                                GUI.Label(SettinglabelRect(leftIndent, pidLines + autoTuneLines++), $"\tField: {ActivePilot.autoTuningLossLabel3}", Label);

                                if (!NumFieldsEnabled) ActivePilot.autoTuningOptionNumSamples = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines + autoTuneLines, contentWidth), ActivePilot.autoTuningOptionNumSamples, 1f, 10f), 1f);
                                else
                                {
                                    inputFields["autoTuningOptionNumSamples"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines + autoTuneLines, contentWidth), inputFields["autoTuningOptionNumSamples"].possibleValue, 6, inputFieldStyle));
                                    ActivePilot.autoTuningOptionNumSamples = (float)inputFields["autoTuningOptionNumSamples"].currentValue;
                                }
                                GUI.Label(SettinglabelRect(leftIndent, pidLines + autoTuneLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningNumSamples") + $": {ActivePilot.autoTuningOptionNumSamples}", Label);
                                if (contextTipsEnabled)
                                {
                                    GUI.Label(ContextLabelRect(leftIndent, pidLines + autoTuneLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningNumSamplesMin"), Label);
                                    GUI.Label(ContextLabelRectRight(leftIndent, pidLines + autoTuneLines, contentWidth), StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningNumSamplesMax"), rightLabel);
                                    ++autoTuneLines;
                                }

                                if (!NumFieldsEnabled) ActivePilot.autoTuningOptionFastResponseRelevance = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines + autoTuneLines, contentWidth), ActivePilot.autoTuningOptionFastResponseRelevance, 0f, 0.5f), 0.01f);
                                else
                                {
                                    inputFields["autoTuningOptionFastResponseRelevance"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines + autoTuneLines, contentWidth), inputFields["autoTuningOptionFastResponseRelevance"].possibleValue, 6, inputFieldStyle));
                                    ActivePilot.autoTuningOptionFastResponseRelevance = (float)inputFields["autoTuningOptionFastResponseRelevance"].currentValue;
                                }
                                GUI.Label(SettinglabelRect(leftIndent, pidLines + autoTuneLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFastResponseRelevance") + $": {ActivePilot.autoTuningOptionFastResponseRelevance:G3}", Label);
                                if (contextTipsEnabled)
                                {
                                    GUI.Label(ContextLabelRect(leftIndent, pidLines + autoTuneLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFastResponseRelevanceMin"), Label);
                                    GUI.Label(ContextLabelRectRight(leftIndent, pidLines + autoTuneLines, contentWidth), StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFastResponseRelevanceMax"), rightLabel);
                                    ++autoTuneLines;
                                }

                                if (!NumFieldsEnabled) ActivePilot.autoTuningOptionInitialLearningRate = Mathf.Pow(10f, BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines + autoTuneLines, contentWidth), Mathf.Log10(ActivePilot.autoTuningOptionInitialLearningRate), -3f, 0f), 0.5f));
                                else
                                {
                                    inputFields["autoTuningOptionInitialLearningRate"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines + autoTuneLines, contentWidth), inputFields["autoTuningOptionInitialLearningRate"].possibleValue, 7, inputFieldStyle));
                                    ActivePilot.autoTuningOptionInitialLearningRate = (float)inputFields["autoTuningOptionInitialLearningRate"].currentValue;
                                }
                                GUI.Label(SettinglabelRect(leftIndent, pidLines + autoTuneLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningInitialLearningRate") + $": {ActivePilot.autoTuningOptionInitialLearningRate:G3}", Label);
                                if (contextTipsEnabled)
                                {
                                    GUI.Label(ContextLabelRect(leftIndent, pidLines + autoTuneLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningInitialLearningRateContext"), Label);
                                    ++autoTuneLines;
                                }

                                if (!NumFieldsEnabled) ActivePilot.autoTuningAltitude = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines + autoTuneLines, contentWidth), ActivePilot.autoTuningAltitude, 50f, 5000f), 50f);
                                else
                                {
                                    inputFields["autoTuningAltitude"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines + autoTuneLines, contentWidth), inputFields["autoTuningAltitude"].possibleValue, 6, inputFieldStyle));
                                    ActivePilot.autoTuningAltitude = (float)inputFields["autoTuningAltitude"].currentValue;
                                }
                                GUI.Label(SettinglabelRect(leftIndent, pidLines + autoTuneLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningAltitude") + $": {ActivePilot.autoTuningAltitude}", Label);
                                if (contextTipsEnabled)
                                {
                                    GUI.Label(ContextLabelRect(leftIndent, pidLines + autoTuneLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningAltitudeContext"), Label);
                                }

                                if (!NumFieldsEnabled) ActivePilot.autoTuningSpeed = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(leftIndent, pidLines + autoTuneLines, contentWidth), ActivePilot.autoTuningSpeed, 50f, 800f), 5f);
                                else
                                {
                                    inputFields["autoTuningSpeed"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, pidLines + autoTuneLines, contentWidth), inputFields["autoTuningSpeed"].possibleValue, 6, inputFieldStyle));
                                    ActivePilot.autoTuningSpeed = (float)inputFields["autoTuningSpeed"].currentValue;
                                }
                                GUI.Label(SettinglabelRect(leftIndent, pidLines + autoTuneLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningSpeed") + $": {ActivePilot.autoTuningSpeed}", Label);
                                if (contextTipsEnabled)
                                {
                                    GUI.Label(ContextLabelRect(leftIndent, pidLines + autoTuneLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningSpeedContext"), Label);
                                }

                                fixedAutoTuneFields = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 0, 2, contentWidth), fixedAutoTuneFields, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixedFields"), fixedAutoTuneFields ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);

                                ActivePilot.autoTuningOptionClampMaximums = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 1, 2, contentWidth), ActivePilot.autoTuningOptionClampMaximums, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningClampMaximums"), ActivePilot.autoTuningOptionClampMaximums ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                                ++autoTuneLines;

                                if (fixedAutoTuneFields)
                                {
                                    bool resetGradient = false;
                                    if (!ActivePilot.dynamicSteerDamping)
                                    {
                                        if (ActivePilot.autoTuningOptionFixedP != (ActivePilot.autoTuningOptionFixedP = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 0, 3, contentWidth), ActivePilot.autoTuningOptionFixedP, StringUtils.Localize("P"), ActivePilot.autoTuningOptionFixedP ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                        if (ActivePilot.autoTuningOptionFixedI != (ActivePilot.autoTuningOptionFixedI = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 1, 3, contentWidth), ActivePilot.autoTuningOptionFixedI, StringUtils.Localize("I"), ActivePilot.autoTuningOptionFixedI ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                        if (ActivePilot.autoTuningOptionFixedD != (ActivePilot.autoTuningOptionFixedD = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 2, 3, contentWidth), ActivePilot.autoTuningOptionFixedD, StringUtils.Localize("D"), ActivePilot.autoTuningOptionFixedD ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                    }
                                    else if (!ActivePilot.CustomDynamicAxisFields)
                                    {
                                        if (ActivePilot.autoTuningOptionFixedP != (ActivePilot.autoTuningOptionFixedP = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 0, 5, contentWidth), ActivePilot.autoTuningOptionFixedP, StringUtils.Localize("P"), ActivePilot.autoTuningOptionFixedP ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                        if (ActivePilot.autoTuningOptionFixedI != (ActivePilot.autoTuningOptionFixedI = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 1, 5, contentWidth), ActivePilot.autoTuningOptionFixedI, StringUtils.Localize("I"), ActivePilot.autoTuningOptionFixedI ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                        if (ActivePilot.autoTuningOptionFixedDOff != (ActivePilot.autoTuningOptionFixedDOff = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 2, 5, contentWidth), ActivePilot.autoTuningOptionFixedDOff, StringUtils.Localize("DOff"), ActivePilot.autoTuningOptionFixedDOff ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                        if (ActivePilot.autoTuningOptionFixedDOn != (ActivePilot.autoTuningOptionFixedDOn = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 3, 5, contentWidth), ActivePilot.autoTuningOptionFixedDOn, StringUtils.Localize("DOn"), ActivePilot.autoTuningOptionFixedDOn ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                        if (ActivePilot.autoTuningOptionFixedDF != (ActivePilot.autoTuningOptionFixedDF = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 4, 5, contentWidth), ActivePilot.autoTuningOptionFixedDF, StringUtils.Localize("DF"), ActivePilot.autoTuningOptionFixedDF ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                    }
                                    else
                                    {
                                        if (ActivePilot.autoTuningOptionFixedP != (ActivePilot.autoTuningOptionFixedP = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 0, 11, contentWidth), ActivePilot.autoTuningOptionFixedP, StringUtils.Localize("P"), ActivePilot.autoTuningOptionFixedP ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                        if (ActivePilot.autoTuningOptionFixedI != (ActivePilot.autoTuningOptionFixedI = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 1, 11, contentWidth), ActivePilot.autoTuningOptionFixedI, StringUtils.Localize("I"), ActivePilot.autoTuningOptionFixedI ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                        if (ActivePilot.autoTuningOptionFixedDPOff != (ActivePilot.autoTuningOptionFixedDPOff = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 2, 11, contentWidth), ActivePilot.autoTuningOptionFixedDPOff, StringUtils.Localize("DPOff"), ActivePilot.autoTuningOptionFixedDPOff ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                        if (ActivePilot.autoTuningOptionFixedDPOn != (ActivePilot.autoTuningOptionFixedDPOn = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 3, 11, contentWidth), ActivePilot.autoTuningOptionFixedDPOn, StringUtils.Localize("DPOn"), ActivePilot.autoTuningOptionFixedDPOn ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                        if (ActivePilot.autoTuningOptionFixedDPF != (ActivePilot.autoTuningOptionFixedDPF = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 4, 11, contentWidth), ActivePilot.autoTuningOptionFixedDPF, StringUtils.Localize("DPF"), ActivePilot.autoTuningOptionFixedDPF ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                        if (ActivePilot.autoTuningOptionFixedDYOff != (ActivePilot.autoTuningOptionFixedDYOff = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 5, 11, contentWidth), ActivePilot.autoTuningOptionFixedDYOff, StringUtils.Localize("DYOff"), ActivePilot.autoTuningOptionFixedDYOff ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                        if (ActivePilot.autoTuningOptionFixedDYOn != (ActivePilot.autoTuningOptionFixedDYOn = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 6, 11, contentWidth), ActivePilot.autoTuningOptionFixedDYOn, StringUtils.Localize("DYOn"), ActivePilot.autoTuningOptionFixedDYOn ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                        if (ActivePilot.autoTuningOptionFixedDYF != (ActivePilot.autoTuningOptionFixedDYF = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 7, 11, contentWidth), ActivePilot.autoTuningOptionFixedDYF, StringUtils.Localize("DYF"), ActivePilot.autoTuningOptionFixedDYF ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                        if (ActivePilot.autoTuningOptionFixedDROff != (ActivePilot.autoTuningOptionFixedDROff = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 8, 11, contentWidth), ActivePilot.autoTuningOptionFixedDROff, StringUtils.Localize("DROff"), ActivePilot.autoTuningOptionFixedDROff ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                        if (ActivePilot.autoTuningOptionFixedDROn != (ActivePilot.autoTuningOptionFixedDROn = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 9, 11, contentWidth), ActivePilot.autoTuningOptionFixedDROn, StringUtils.Localize("DROn"), ActivePilot.autoTuningOptionFixedDROn ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                        if (ActivePilot.autoTuningOptionFixedDRF != (ActivePilot.autoTuningOptionFixedDRF = GUI.Toggle(ToggleButtonRects(leftIndent, pidLines + autoTuneLines, 10, 11, contentWidth), ActivePilot.autoTuningOptionFixedDRF, StringUtils.Localize("DRF"), ActivePilot.autoTuningOptionFixedDRF ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                    }
                                    if (resetGradient && HighLogic.LoadedSceneIsFlight) ActivePilot.pidAutoTuning.ResetGradient();
                                    ++autoTuneLines;
                                }

                                pidLines += autoTuneLines + 0.25f;
                            }
                            #endregion

                            GUI.EndGroup();
                            pidHeight = Mathf.Lerp(pidHeight, pidLines, 0.15f);
                            pidLines += 0.1f;

                        }

                        if (showAltitude)
                        {
                            altLines += 0.2f;
                            GUI.BeginGroup(
                                new Rect(0, ((pidLines + altLines) * entryHeight), contentWidth, altitudeHeight * entryHeight),
                                GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                            altLines += 0.25f;

                            GUI.Label(SettinglabelRect(leftIndent, altLines), StringUtils.Localize("#LOC_BDArmory_PilotAI_Altitudes"), BoldLabel);//"Altitudes"
                            altLines++;
                            var oldDefaultAlt = ActivePilot.defaultAltitude;
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.defaultAltitude =
                                    GUI.HorizontalSlider(SettingSliderRect(leftIndent, altLines, contentWidth),
                                        ActivePilot.defaultAltitude, 100, ActivePilot.UpToEleven ? 100000 : 15000);
                                ActivePilot.defaultAltitude = Mathf.Round(ActivePilot.defaultAltitude / 50) * 50;
                            }
                            else
                            {
                                inputFields["defaultAltitude"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, altLines, contentWidth), inputFields["defaultAltitude"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.defaultAltitude = (float)inputFields["defaultAltitude"].currentValue;
                            }
                            if (ActivePilot.defaultAltitude != oldDefaultAlt)
                            {
                                ActivePilot.ClampFields("defaultAltitude");
                                inputFields["minAltitude"].currentValue = ActivePilot.minAltitude;
                                inputFields["maxAltitude"].currentValue = ActivePilot.maxAltitude;
                            }
                            GUI.Label(SettinglabelRect(leftIndent, altLines), StringUtils.Localize("#LOC_BDArmory_DefaultAltitude") + ": " + ActivePilot.defaultAltitude.ToString("0"), Label);//"default altitude"
                            altLines++;
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, altLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_DefAlt"), contextLabel);//"defalult alt"
                                altLines++;
                            }
                            var oldMinAlt = ActivePilot.minAltitude;
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.minAltitude =
                                    GUI.HorizontalSlider(SettingSliderRect(leftIndent, altLines, contentWidth),
                                        ActivePilot.minAltitude, 25, ActivePilot.UpToEleven ? 60000 : 6000);
                                ActivePilot.minAltitude = Mathf.Round(ActivePilot.minAltitude / 10) * 10;
                            }
                            else
                            {
                                inputFields["minAltitude"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, altLines, contentWidth), inputFields["minAltitude"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.minAltitude = (float)inputFields["minAltitude"].currentValue;
                            }
                            if (ActivePilot.minAltitude != oldMinAlt)
                            {
                                ActivePilot.ClampFields("minAltitude");
                                inputFields["defaultAltitude"].currentValue = ActivePilot.defaultAltitude;
                                inputFields["maxAltitude"].currentValue = ActivePilot.maxAltitude;
                            }
                            GUI.Label(SettinglabelRect(leftIndent, altLines), StringUtils.Localize("#LOC_BDArmory_MinAltitude") + ": " + ActivePilot.minAltitude.ToString("0"), Label);//"min altitude"
                            altLines++;
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, altLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_MinAlt"), contextLabel);//"min alt"
                                altLines++;
                            }

                            ActivePilot.maxAltitudeToggle = GUI.Toggle(new Rect(leftIndent, altLines * entryHeight, contentWidth - (2 * leftIndent), entryHeight),
                            ActivePilot.maxAltitudeToggle, StringUtils.Localize("#LOC_BDArmory_MaxAltitude"), ActivePilot.maxAltitudeToggle ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"max altitude AGL"
                            altLines += 1.25f;

                            if (ActivePilot.maxAltitudeToggle)
                            {
                                var oldMaxAlt = ActivePilot.maxAltitude;
                                if (!NumFieldsEnabled)
                                {
                                    ActivePilot.maxAltitude =
                                        GUI.HorizontalSlider(SettingSliderRect(leftIndent, altLines, contentWidth),
                                            ActivePilot.maxAltitude, 100, ActivePilot.UpToEleven ? 100000 : 15000);
                                    ActivePilot.maxAltitude = Mathf.Round(ActivePilot.maxAltitude / 100) * 100;
                                }
                                else
                                {
                                    inputFields["maxAltitude"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, altLines, contentWidth), inputFields["maxAltitude"].possibleValue, 6, inputFieldStyle));
                                    ActivePilot.maxAltitude = (float)inputFields["maxAltitude"].currentValue;
                                }
                                if (ActivePilot.maxAltitude != oldMaxAlt)
                                {
                                    ActivePilot.ClampFields("maxAltitude");
                                    inputFields["minAltitude"].currentValue = ActivePilot.minAltitude;
                                    inputFields["defaultAltitude"].currentValue = ActivePilot.defaultAltitude;
                                }
                                GUI.Label(SettinglabelRect(leftIndent, altLines), StringUtils.Localize("#LOC_BDArmory_MaxAltitude") + ": " + ActivePilot.maxAltitude.ToString("0"), Label);//"max altitude"
                                altLines++;
                                if (contextTipsEnabled)
                                {
                                    GUI.Label(ContextLabelRect(leftIndent, altLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_MaxAlt"), contextLabel);//"max alt"
                                    altLines++;
                                }
                            }
                            GUI.EndGroup();
                            altitudeHeight = Mathf.Lerp(altitudeHeight, altLines, 0.15f);
                            altLines += 0.1f;
                        }

                        if (showSpeed)
                        {
                            spdLines += 0.2f;
                            GUI.BeginGroup(
                                new Rect(0, ((pidLines + altLines + spdLines) * entryHeight), contentWidth, speedHeight * entryHeight),
                                GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                            spdLines += 0.25f;

                            GUI.Label(SettinglabelRect(leftIndent, spdLines), StringUtils.Localize("#LOC_BDArmory_PilotAI_Speeds"), BoldLabel);//"Speed"
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.maxSpeed = GUI.HorizontalSlider(SettingSliderRect(leftIndent, ++spdLines, contentWidth), ActivePilot.maxSpeed, 20, ActivePilot.UpToEleven ? 3000 : 800);
                                ActivePilot.maxSpeed = Mathf.Round(ActivePilot.maxSpeed / 5) * 5;
                            }
                            else
                            {
                                inputFields["maxSpeed"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ++spdLines, contentWidth), inputFields["maxSpeed"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.maxSpeed = (float)inputFields["maxSpeed"].currentValue;
                            }
                            GUI.Label(SettinglabelRect(leftIndent, spdLines), StringUtils.Localize("#LOC_BDArmory_MaxSpeed") + ": " + ActivePilot.maxSpeed.ToString("0"), Label);//"max speed"
                            if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++spdLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_maxSpeed"), contextLabel);//"max speed"

                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.takeOffSpeed = GUI.HorizontalSlider(SettingSliderRect(leftIndent, ++spdLines, contentWidth), ActivePilot.takeOffSpeed, 10f, ActivePilot.UpToEleven ? 2000 : 200);
                                ActivePilot.takeOffSpeed = Mathf.Round(ActivePilot.takeOffSpeed);
                            }
                            else
                            {
                                inputFields["takeOffSpeed"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ++spdLines, contentWidth), inputFields["takeOffSpeed"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.takeOffSpeed = (float)inputFields["takeOffSpeed"].currentValue;
                            }
                            GUI.Label(SettinglabelRect(leftIndent, spdLines), StringUtils.Localize("#LOC_BDArmory_TakeOffSpeed") + ": " + ActivePilot.takeOffSpeed.ToString("0"), Label);//"takeoff speed"
                            if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++spdLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_takeoff"), contextLabel);//"takeoff speed help"

                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.minSpeed = GUI.HorizontalSlider(SettingSliderRect(leftIndent, ++spdLines, contentWidth), ActivePilot.minSpeed, 10, ActivePilot.UpToEleven ? 2000 : 200);
                                ActivePilot.minSpeed = Mathf.Round(ActivePilot.minSpeed);
                            }
                            else
                            {
                                inputFields["minSpeed"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ++spdLines, contentWidth), inputFields["minSpeed"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.minSpeed = (float)inputFields["minSpeed"].currentValue;
                            }
                            GUI.Label(SettinglabelRect(leftIndent, spdLines), StringUtils.Localize("#LOC_BDArmory_MinSpeed") + ": " + ActivePilot.minSpeed.ToString("0"), Label);//"min speed"
                            if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++spdLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_minSpeed"), contextLabel);//"min speed help"

                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.strafingSpeed = GUI.HorizontalSlider(SettingSliderRect(leftIndent, ++spdLines, contentWidth), ActivePilot.strafingSpeed, 10, 200);
                                ActivePilot.strafingSpeed = Mathf.Round(ActivePilot.strafingSpeed);
                            }
                            else
                            {
                                inputFields["strafingSpeed"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ++spdLines, contentWidth), inputFields["strafingSpeed"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.strafingSpeed = (float)inputFields["strafingSpeed"].currentValue;
                            }
                            GUI.Label(SettinglabelRect(leftIndent, spdLines), StringUtils.Localize("#LOC_BDArmory_StrafingSpeed") + ": " + ActivePilot.strafingSpeed.ToString("0"), Label);//"strafing speed"
                            if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++spdLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_atkSpeed"), contextLabel);//"strafe speed"

                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.idleSpeed = GUI.HorizontalSlider(SettingSliderRect(leftIndent, ++spdLines, contentWidth), ActivePilot.idleSpeed, 10, ActivePilot.UpToEleven ? 3000 : 200);
                                ActivePilot.idleSpeed = Mathf.Round(ActivePilot.idleSpeed);
                            }
                            else
                            {
                                inputFields["idleSpeed"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ++spdLines, contentWidth), inputFields["idleSpeed"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.idleSpeed = (float)inputFields["idleSpeed"].currentValue;
                            }
                            GUI.Label(SettinglabelRect(leftIndent, spdLines), StringUtils.Localize("#LOC_BDArmory_IdleSpeed") + ": " + ActivePilot.idleSpeed.ToString("0"), Label);//"idle speed"
                            if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++spdLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_idleSpeed"), contextLabel);//"idle speed context help"

                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.ABPriority = GUI.HorizontalSlider(SettingSliderRect(leftIndent, ++spdLines, contentWidth), ActivePilot.ABPriority, 0, 100);
                                ActivePilot.ABPriority = Mathf.Round(ActivePilot.ABPriority);
                            }
                            else
                            {
                                inputFields["ABPriority"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ++spdLines, contentWidth), inputFields["ABPriority"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.ABPriority = (float)inputFields["ABPriority"].currentValue;
                            }
                            GUI.Label(SettinglabelRect(leftIndent, spdLines), StringUtils.Localize("#LOC_BDArmory_ABPriority") + ": " + ActivePilot.ABPriority.ToString("0"), Label);//"AB priority"
                            if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++spdLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ABPriority"), contextLabel);//"AB priority context help"

                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.ABOverrideThreshold = GUI.HorizontalSlider(SettingSliderRect(leftIndent, ++spdLines, contentWidth), ActivePilot.ABOverrideThreshold, 0, 200);
                                ActivePilot.ABOverrideThreshold = Mathf.Round(ActivePilot.ABOverrideThreshold);
                            }
                            else
                            {
                                inputFields["ABOverrideThreshold"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ++spdLines, contentWidth), inputFields["ABOverrideThreshold"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.ABOverrideThreshold = (float)inputFields["ABOverrideThreshold"].currentValue;
                            }
                            GUI.Label(SettinglabelRect(leftIndent, spdLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ABOverrideThreshold") + ": " + ActivePilot.ABOverrideThreshold.ToString("0"), Label);//"AB Override Threshold"
                            if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++spdLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ABOverrideThreshold_Context"), contextLabel);//"AB priority context help"

                            GUI.EndGroup();
                            speedHeight = Mathf.Lerp(speedHeight, ++spdLines, 0.15f);
                            spdLines += 0.1f;
                        }

                        if (showControl)
                        {
                            ctrlLines += 0.2f;
                            GUI.BeginGroup(
                                new Rect(0, ((pidLines + altLines + spdLines + ctrlLines) * entryHeight), contentWidth, controlHeight * entryHeight),
                                GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                            ctrlLines += 0.25f;
                            GUI.Label(SettinglabelRect(leftIndent, ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ControlLimits"), BoldLabel);//"Control"

                            GUI.Label(SettinglabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_LowSpeedSteerLimiter") + ": " + ActivePilot.maxSteer.ToString("0.00"), Label);//"Low speed Limiter"
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.maxSteer = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(leftIndent, ctrlLines, contentWidth), ActivePilot.maxSteer, 0.1f, 1), 0.05f);
                            }
                            else
                            {
                                inputFields["maxSteer"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ctrlLines, contentWidth), inputFields["maxSteer"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.maxSteer = (float)inputFields["maxSteer"].currentValue;
                            }
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_LSSL"), contextLabel);//"Low limiter context"
                            }

                            GUI.Label(SettinglabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_LowSpeedLimiterSpeed") + ": " + ActivePilot.lowSpeedSwitch.ToString("0"), Label);//"dynamic damping max"
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.lowSpeedSwitch = Mathf.Round(GUI.HorizontalSlider(SettingSliderRect(leftIndent, ctrlLines, contentWidth), ActivePilot.lowSpeedSwitch, 10f, 500));
                            }
                            else
                            {
                                inputFields["lowSpeedSwitch"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ctrlLines, contentWidth), inputFields["lowSpeedSwitch"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.lowSpeedSwitch = (float)inputFields["lowSpeedSwitch"].currentValue;
                            }
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_LSLS"), contextLabel);//"dynamic damp max"
                            }

                            GUI.Label(SettinglabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_HighSpeedSteerLimiter") + ": " + ActivePilot.maxSteerAtMaxSpeed.ToString("0.00"), Label);//"dynamic damping min"
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.maxSteerAtMaxSpeed =
                                    GUI.HorizontalSlider(SettingSliderRect(leftIndent, ctrlLines, contentWidth),
                                        ActivePilot.maxSteerAtMaxSpeed, 0.1f, 1);
                                ActivePilot.maxSteerAtMaxSpeed = Mathf.Round(ActivePilot.maxSteerAtMaxSpeed * 20f) / 20f;
                            }
                            else
                            {
                                inputFields["maxSteerAtMaxSpeed"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ctrlLines, contentWidth), inputFields["maxSteerAtMaxSpeed"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.maxSteerAtMaxSpeed = (float)inputFields["maxSteerAtMaxSpeed"].currentValue;
                            }
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_HSSL"), contextLabel);//"dynamic damp min"
                            }

                            GUI.Label(SettinglabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_HighSpeedLimiterSpeed") + ": " + ActivePilot.cornerSpeed.ToString("0"), Label);//"dynamic damping min"
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.cornerSpeed = Mathf.Round(GUI.HorizontalSlider(SettingSliderRect(leftIndent, ctrlLines, contentWidth), ActivePilot.cornerSpeed, 10, 500));
                            }
                            else
                            {
                                inputFields["cornerSpeed"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ctrlLines, contentWidth), inputFields["cornerSpeed"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.cornerSpeed = (float)inputFields["cornerSpeed"].currentValue;
                            }
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_HSLS"), contextLabel);//"dynamic damp min"
                            }

                            GUI.Label(SettinglabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_AltitudeSteerLimiterFactor") + ": " + ActivePilot.altitudeSteerLimiterFactor.ToString("0.00"), Label);
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.altitudeSteerLimiterFactor = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(leftIndent, ctrlLines, contentWidth), ActivePilot.altitudeSteerLimiterFactor, -1f, 1f), 0.05f);
                            }
                            else
                            {
                                inputFields["altitudeSteerLimiterFactor"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ctrlLines, contentWidth), inputFields["altitudeSteerLimiterFactor"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.altitudeSteerLimiterFactor = (float)inputFields["altitudeSteerLimiterFactor"].currentValue;
                            }
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ASLF"), contextLabel);//"Altitude Steer Limiter Factor"
                            }

                            GUI.Label(SettinglabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_AltitudeSteerLimiterAltitude") + ": " + ActivePilot.altitudeSteerLimiterAltitude.ToString("0"), Label);
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.altitudeSteerLimiterAltitude = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(leftIndent, ctrlLines, contentWidth), ActivePilot.altitudeSteerLimiterAltitude, 100f, 10000f), 100f);
                            }
                            else
                            {
                                inputFields["altitudeSteerLimiterAltitude"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ctrlLines, contentWidth), inputFields["altitudeSteerLimiterAltitude"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.altitudeSteerLimiterAltitude = (float)inputFields["altitudeSteerLimiterAltitude"].currentValue;
                            }
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ASLA"), contextLabel);//"Altitude Steer Limiter Altitude"
                            }

                            GUI.Label(SettinglabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_BankLimiter") + ": " + ActivePilot.maxBank.ToString("0"), Label);//"dynamic damping min"
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.maxBank = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(leftIndent, ctrlLines, contentWidth), ActivePilot.maxBank, 10, (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 55) ? 40 : 180), 5f);
                            }
                            else
                            {
                                inputFields["maxBank"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ctrlLines, contentWidth), inputFields["maxBank"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.maxBank = (float)inputFields["maxBank"].currentValue;
                            }
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_bankLimit"), contextLabel);//"dynamic damp min"
                            }

                            GUI.Label(SettinglabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_WaypointPreRollTime") + ": " + ActivePilot.waypointPreRollTime.ToString("0.00"), Label);//
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.waypointPreRollTime = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(leftIndent, ctrlLines, contentWidth), ActivePilot.waypointPreRollTime, 0, 2), 0.05f);
                            }
                            else
                            {
                                inputFields["waypointPreRollTime"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ctrlLines, contentWidth), inputFields["waypointPreRollTime"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.waypointPreRollTime = (float)inputFields["waypointPreRollTime"].currentValue;
                            }
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_WPPreRoll"), contextLabel);// Waypoint Pre-Roll Time
                            }

                            GUI.Label(SettinglabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_WaypointYawAuthorityTime") + ": " + ActivePilot.waypointYawAuthorityTime.ToString("0.00"), Label);//
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.waypointYawAuthorityTime = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(leftIndent, ctrlLines, contentWidth), ActivePilot.waypointYawAuthorityTime, 0, 10), 0.1f);
                            }
                            else
                            {
                                inputFields["waypointYawAuthorityTime"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ctrlLines, contentWidth), inputFields["waypointYawAuthorityTime"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.waypointYawAuthorityTime = (float)inputFields["waypointYawAuthorityTime"].currentValue;
                            }
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_WPYawAuth"), contextLabel);// Waypoint Yaw Authority Time
                            }

                            GUI.Label(SettinglabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_maxAllowedGForce") + ": " + ActivePilot.maxAllowedGForce.ToString("0.00"), Label);
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.maxAllowedGForce = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(leftIndent, ctrlLines, contentWidth), ActivePilot.maxAllowedGForce, 2, ActivePilot.UpToEleven ? 1000 : 45), 0.25f);
                            }
                            else
                            {
                                inputFields["maxAllowedGForce"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ctrlLines, contentWidth), inputFields["maxAllowedGForce"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.maxAllowedGForce = (float)inputFields["maxAllowedGForce"].currentValue;
                            }
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_GForce"), contextLabel);
                            }

                            GUI.Label(SettinglabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_maxAllowedAoA") + ": " + ActivePilot.maxAllowedAoA.ToString("0.0"), Label);
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.maxAllowedAoA = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(leftIndent, ctrlLines, contentWidth), ActivePilot.maxAllowedAoA, 0f, ActivePilot.UpToEleven ? 180f : 90f), 2.5f);
                            }
                            else
                            {
                                inputFields["maxAllowedAoA"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ctrlLines, contentWidth), inputFields["maxAllowedAoA"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.maxAllowedAoA = (float)inputFields["maxAllowedAoA"].currentValue;
                            }
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_AoA"), contextLabel);
                            }

                            if (!(BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 55))
                            {
                                GUI.Label(SettinglabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_postStallAoA") + ": " + ActivePilot.postStallAoA.ToString("0.0"), Label);
                                if (!NumFieldsEnabled)
                                {
                                    ActivePilot.postStallAoA = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(leftIndent, ctrlLines, contentWidth), ActivePilot.postStallAoA, 0f, ActivePilot.UpToEleven ? 180f : 90f), 2.5f);
                                }
                                else
                                {
                                    inputFields["postStallAoA"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ctrlLines, contentWidth), inputFields["postStallAoA"].possibleValue, 6, inputFieldStyle));
                                    ActivePilot.postStallAoA = (float)inputFields["postStallAoA"].currentValue;
                                }
                                if (contextTipsEnabled)
                                {
                                    GUI.Label(ContextLabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_AoAPostStall"), contextLabel);
                                }
                            }

                            GUI.Label(SettinglabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ImmelmannTurnAngle") + ": " + ActivePilot.ImmelmannTurnAngle.ToString("0"), Label);
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.ImmelmannTurnAngle = Mathf.Round(GUI.HorizontalSlider(SettingSliderRect(leftIndent, ctrlLines, contentWidth), ActivePilot.ImmelmannTurnAngle, 0f, 90f));
                            }
                            else
                            {
                                inputFields["ImmelmannTurnAngle"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ctrlLines, contentWidth), inputFields["ImmelmannTurnAngle"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.ImmelmannTurnAngle = (float)inputFields["ImmelmannTurnAngle"].currentValue;
                            }
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, ++ctrlLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ImmelmannTurnAngleContext"), contextLabel);
                            }

                            ++ctrlLines;
                            GUI.EndGroup();
                            controlHeight = Mathf.Lerp(controlHeight, ctrlLines, 0.15f);
                            ctrlLines += 0.1f;
                        }

                        if (showEvade)
                        {
                            evadeLines += 0.2f;
                            GUI.BeginGroup(
                                new Rect(0, ((pidLines + altLines + spdLines + ctrlLines + evadeLines) * entryHeight), contentWidth, evasionHeight * entryHeight),
                                GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                            #region Evasion
                            evadeLines += 0.25f;
                            GUI.Label(SettinglabelRect(leftIndent, evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_Evade"), BoldLabel);

                            GUI.Label(SettinglabelRect(leftIndent, ++evadeLines), $"{StringUtils.Localize("#LOC_BDArmory_MinEvasionTime")}: {ActivePilot.minEvasionTime:0.00}s", Label);
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.minEvasionTime =
                                    GUI.HorizontalSlider(SettingSliderRect(leftIndent, evadeLines, contentWidth),
                                        ActivePilot.minEvasionTime, 0f, ActivePilot.UpToEleven ? 10 : 1);
                                ActivePilot.minEvasionTime = Mathf.Round(ActivePilot.minEvasionTime * 20f) / 20f;
                            }
                            else
                            {
                                inputFields["minEvasionTime"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, evadeLines, contentWidth), inputFields["minEvasionTime"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.minEvasionTime = (float)inputFields["minEvasionTime"].currentValue;
                            }
                            if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_MinEvade"), contextLabel);

                            GUI.Label(SettinglabelRect(leftIndent, ++evadeLines), $"{StringUtils.Localize("#LOC_BDArmory_AIWindow_EvasionThreshold")}: {ActivePilot.evasionThreshold:0}m", Label);
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.evasionThreshold =
                                    GUI.HorizontalSlider(
                                        SettingSliderRect(leftIndent, evadeLines, contentWidth),
                                        ActivePilot.evasionThreshold, 0, ActivePilot.UpToEleven ? 300 : 100);
                                ActivePilot.evasionThreshold = Mathf.Round(ActivePilot.evasionThreshold);
                            }
                            else
                            {
                                inputFields["evasionThreshold"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, evadeLines, contentWidth), inputFields["evasionThreshold"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.evasionThreshold = (float)inputFields["evasionThreshold"].currentValue;
                            }
                            if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_evadeDist"), contextLabel);

                            GUI.Label(SettinglabelRect(leftIndent, ++evadeLines), $"{StringUtils.Localize("#LOC_BDArmory_AIWindow_EvasionTimeThreshold")}: {ActivePilot.evasionTimeThreshold:0.00}s", Label);
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.evasionTimeThreshold =
                                    GUI.HorizontalSlider(SettingSliderRect(leftIndent, evadeLines, contentWidth),
                                        ActivePilot.evasionTimeThreshold, 0, ActivePilot.UpToEleven ? 1 : 3);
                                ActivePilot.evasionTimeThreshold = Mathf.Round(ActivePilot.evasionTimeThreshold * 100f) / 100f;
                            }
                            else
                            {
                                inputFields["evasionTimeThreshold"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, evadeLines, contentWidth), inputFields["evasionTimeThreshold"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.evasionTimeThreshold = (float)inputFields["evasionTimeThreshold"].currentValue;
                            }
                            if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_evadetimeDist"), contextLabel);

                            GUI.Label(SettinglabelRect(leftIndent, ++evadeLines), $"{StringUtils.Localize("#LOC_BDArmory_AIWindow_EvasionNonlinearity")}: {ActivePilot.evasionNonlinearity:0.0}°", Label);//"Evasion/Extension Nonlinearity"
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.evasionNonlinearity =
                                    GUI.HorizontalSlider(
                                        SettingSliderRect(leftIndent, evadeLines, contentWidth),
                                        ActivePilot.evasionNonlinearity, 0, ActivePilot.UpToEleven ? 90 : 10);
                                ActivePilot.evasionNonlinearity = Mathf.Round(ActivePilot.evasionNonlinearity * 10f) / 10f;
                            }
                            else
                            {
                                inputFields["evasionNonlinearity"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, evadeLines, contentWidth), inputFields["evasionNonlinearity"].possibleValue, 4, inputFieldStyle));
                                ActivePilot.evasionNonlinearity = (float)inputFields["evasionNonlinearity"].currentValue;
                            }
                            if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_EvExNonlin"), contextLabel);

                            ActivePilot.evasionIgnoreMyTargetTargetingMe = GUI.Toggle(ToggleButtonRect(leftIndent, ++evadeLines, contentWidth), ActivePilot.evasionIgnoreMyTargetTargetingMe, StringUtils.Localize("#LOC_BDArmory_EvasionIgnoreMyTargetTargetingMe"), ActivePilot.evasionIgnoreMyTargetTargetingMe ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                            #endregion

                            #region Craft Avoidance
                            evadeLines += 1.5f;
                            GUI.Label(SettinglabelRect(leftIndent, evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_Avoidance"), BoldLabel);

                            GUI.Label(SettinglabelRect(leftIndent, ++evadeLines), $"{StringUtils.Localize("#LOC_BDArmory_AIWindow_CollisionAvoidanceThreshold")}: {ActivePilot.collisionAvoidanceThreshold:0}m", Label);
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.collisionAvoidanceThreshold =
                                    GUI.HorizontalSlider(SettingSliderRect(leftIndent, evadeLines, contentWidth),
                                        ActivePilot.collisionAvoidanceThreshold, 0, 50);
                                ActivePilot.collisionAvoidanceThreshold = Mathf.Round(ActivePilot.collisionAvoidanceThreshold);
                            }
                            else
                            {
                                inputFields["collisionAvoidanceThreshold"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, evadeLines, contentWidth), inputFields["collisionAvoidanceThreshold"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.collisionAvoidanceThreshold = (float)inputFields["collisionAvoidanceThreshold"].currentValue;
                            }
                            if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ColDist"), contextLabel);

                            GUI.Label(SettinglabelRect(leftIndent, ++evadeLines), $"{StringUtils.Localize("#LOC_BDArmory_AIWindow_CollisionAvoidanceLookAheadPeriod")}: {ActivePilot.vesselCollisionAvoidanceLookAheadPeriod:0.0}s", Label);
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.vesselCollisionAvoidanceLookAheadPeriod =
                                    GUI.HorizontalSlider(SettingSliderRect(leftIndent, evadeLines, contentWidth),
                                        ActivePilot.vesselCollisionAvoidanceLookAheadPeriod, 0, 3);
                                ActivePilot.vesselCollisionAvoidanceLookAheadPeriod = Mathf.Round(ActivePilot.vesselCollisionAvoidanceLookAheadPeriod * 10f) / 10f;
                            }
                            else
                            {
                                inputFields["vesselCollisionAvoidanceLookAheadPeriod"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, evadeLines, contentWidth), inputFields["vesselCollisionAvoidanceLookAheadPeriod"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.vesselCollisionAvoidanceLookAheadPeriod = (float)inputFields["vesselCollisionAvoidanceLookAheadPeriod"].currentValue;
                            }
                            if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ColTime"), contextLabel);

                            GUI.Label(SettinglabelRect(leftIndent, ++evadeLines), $"{StringUtils.Localize("#LOC_BDArmory_AIWindow_CollisionAvoidanceStrength")}: {ActivePilot.vesselCollisionAvoidanceStrength:0.0} ({ActivePilot.vesselCollisionAvoidanceStrength / Time.fixedDeltaTime:0}°/s)", Label);
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.vesselCollisionAvoidanceStrength =
                                    GUI.HorizontalSlider(SettingSliderRect(leftIndent, evadeLines, contentWidth),
                                        ActivePilot.vesselCollisionAvoidanceStrength, 0, 4);
                                ActivePilot.vesselCollisionAvoidanceStrength = BDAMath.RoundToUnit(ActivePilot.vesselCollisionAvoidanceStrength, 0.1f);
                            }
                            else
                            {
                                inputFields["vesselCollisionAvoidanceStrength"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, evadeLines, contentWidth), inputFields["vesselCollisionAvoidanceStrength"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.vesselCollisionAvoidanceStrength = (float)inputFields["vesselCollisionAvoidanceStrength"].currentValue;
                            }
                            if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ColStrength"), contextLabel);

                            GUI.Label(SettinglabelRect(leftIndent, ++evadeLines), $"{StringUtils.Localize("#LOC_BDArmory_AIWindow_StandoffDistance")}: {ActivePilot.vesselStandoffDistance:0}m", Label);
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.vesselStandoffDistance =
                                    GUI.HorizontalSlider(SettingSliderRect(leftIndent, evadeLines, contentWidth),
                                        ActivePilot.vesselStandoffDistance, 2, ActivePilot.UpToEleven ? 5000 : 1000);
                                ActivePilot.vesselStandoffDistance = Mathf.Round(ActivePilot.vesselStandoffDistance / 50) * 50;
                            }
                            else
                            {
                                inputFields["vesselStandoffDistance"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, evadeLines, contentWidth), inputFields["vesselStandoffDistance"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.vesselStandoffDistance = (float)inputFields["vesselStandoffDistance"].currentValue;
                            }
                            if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_standoff"), contextLabel);
                            #endregion

                            #region Extending
                            if (ActivePilot.canExtend)
                            {
                                evadeLines += 1.5f;
                                GUI.Label(SettinglabelRect(leftIndent, evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_Extend"), BoldLabel);
                                #region Extend Distance Air-to-Air
                                GUI.Label(SettinglabelRect(leftIndent, ++evadeLines), $"{StringUtils.Localize("#LOC_BDArmory_AIWindow_ExtendDistanceAirToAir")}: {ActivePilot.extendDistanceAirToAir:0}m", Label); // Extend Distance Air-To-Air
                                if (!NumFieldsEnabled)
                                {
                                    ActivePilot.extendDistanceAirToAir =
                                        GUI.HorizontalSlider(SettingSliderRect(leftIndent, evadeLines, contentWidth),
                                            ActivePilot.extendDistanceAirToAir, 0, ActivePilot.UpToEleven ? 20000 : 2000);
                                    ActivePilot.extendDistanceAirToAir = BDAMath.RoundToUnit(ActivePilot.extendDistanceAirToAir, 10f);
                                }
                                else
                                {
                                    inputFields["extendDistanceAirToAir"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, evadeLines, contentWidth), inputFields["extendDistanceAirToAir"].possibleValue, 6, inputFieldStyle));
                                    ActivePilot.extendDistanceAirToAir = (float)inputFields["extendDistanceAirToAir"].currentValue;
                                }
                                if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ExtendDistanceAirToAir_Context"), contextLabel);
                                #endregion

                                #region Extend Angle Air-to-Air
                                GUI.Label(SettinglabelRect(leftIndent, ++evadeLines), $"{StringUtils.Localize("#LOC_BDArmory_AIWindow_ExtendAngleAirToAir")}: {ActivePilot.extendAngleAirToAir:0}°", Label); // Extend Angle Air-To-Air
                                if (!NumFieldsEnabled)
                                {
                                    ActivePilot.extendAngleAirToAir =
                                        GUI.HorizontalSlider(SettingSliderRect(leftIndent, evadeLines, contentWidth),
                                            ActivePilot.extendAngleAirToAir, ActivePilot.UpToEleven ? -90 : -10, ActivePilot.UpToEleven ? 90 : 45);
                                    ActivePilot.extendAngleAirToAir = BDAMath.RoundToUnit(ActivePilot.extendAngleAirToAir, 1f);
                                }
                                else
                                {
                                    inputFields["extendAngleAirToAir"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, evadeLines, contentWidth), inputFields["extendAngleAirToAir"].possibleValue, 6, inputFieldStyle));
                                    ActivePilot.extendAngleAirToAir = (float)inputFields["extendAngleAirToAir"].currentValue;
                                }
                                if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ExtendAngleAirToAir_Context"), contextLabel);
                                #endregion

                                #region Extend Distance Air-to-Ground (Guns)
                                GUI.Label(SettinglabelRect(leftIndent, ++evadeLines), $"{StringUtils.Localize("#LOC_BDArmory_AIWindow_ExtendDistanceAirToGroundGuns")}: {ActivePilot.extendDistanceAirToGroundGuns:0}m", Label); // Extend Distance Air-To-Ground (Guns)
                                if (!NumFieldsEnabled)
                                {
                                    ActivePilot.extendDistanceAirToGroundGuns =
                                        GUI.HorizontalSlider(SettingSliderRect(leftIndent, evadeLines, contentWidth),
                                            ActivePilot.extendDistanceAirToGroundGuns, 0, ActivePilot.UpToEleven ? 20000 : 5000);
                                    ActivePilot.extendDistanceAirToGroundGuns = BDAMath.RoundToUnit(ActivePilot.extendDistanceAirToGroundGuns, 50f);
                                }
                                else
                                {
                                    inputFields["extendDistanceAirToGroundGuns"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, evadeLines, contentWidth), inputFields["extendDistanceAirToGroundGuns"].possibleValue, 6, inputFieldStyle));
                                    ActivePilot.extendDistanceAirToGroundGuns = (float)inputFields["extendDistanceAirToGroundGuns"].currentValue;
                                }
                                if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ExtendDistanceAirToGroundGuns_Context"), contextLabel);
                                #endregion

                                #region Extend Distance Air-to-Ground
                                GUI.Label(SettinglabelRect(leftIndent, ++evadeLines), $"{StringUtils.Localize("#LOC_BDArmory_AIWindow_ExtendDistanceAirToGround")}: {ActivePilot.extendDistanceAirToGround:0}m", Label); // Extend Distance Air-To-Ground
                                if (!NumFieldsEnabled)
                                {
                                    ActivePilot.extendDistanceAirToGround =
                                        GUI.HorizontalSlider(SettingSliderRect(leftIndent, evadeLines, contentWidth),
                                            ActivePilot.extendDistanceAirToGround, 0, ActivePilot.UpToEleven ? 20000 : 5000);
                                    ActivePilot.extendDistanceAirToGround = BDAMath.RoundToUnit(ActivePilot.extendDistanceAirToGround, 50f);
                                }
                                else
                                {
                                    inputFields["extendDistanceAirToGround"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, evadeLines, contentWidth), inputFields["extendDistanceAirToGround"].possibleValue, 6, inputFieldStyle));
                                    ActivePilot.extendDistanceAirToGround = (float)inputFields["extendDistanceAirToGround"].currentValue;
                                }
                                if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ExtendDistanceAirToGround_Context"), contextLabel);
                                #endregion

                                #region Extend Target triggers
                                GUI.Label(SettinglabelRect(leftIndent, ++evadeLines), $"{StringUtils.Localize("#LOC_BDArmory_AIWindow_ExtendTargetVel")}: {ActivePilot.extendTargetVel:0.0}", Label);
                                if (!NumFieldsEnabled)
                                {
                                    ActivePilot.extendTargetVel = GUI.HorizontalSlider(SettingSliderRect(leftIndent, evadeLines, contentWidth), ActivePilot.extendTargetVel, 0, 2);
                                    ActivePilot.extendTargetVel = BDAMath.RoundToUnit(ActivePilot.extendTargetVel, 0.1f);
                                }
                                else
                                {
                                    inputFields["extendTargetVel"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, evadeLines, contentWidth), inputFields["extendTargetVel"].possibleValue, 6, inputFieldStyle));
                                    ActivePilot.extendTargetVel = (float)inputFields["extendTargetVel"].currentValue;
                                }
                                if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_Extendvel"), contextLabel);

                                GUI.Label(SettinglabelRect(leftIndent, ++evadeLines), $"{StringUtils.Localize("#LOC_BDArmory_AIWindow_ExtendTargetAngle")}: {ActivePilot.extendTargetAngle}°", Label);
                                if (!NumFieldsEnabled)
                                {
                                    ActivePilot.extendTargetAngle = GUI.HorizontalSlider(SettingSliderRect(leftIndent, evadeLines, contentWidth), ActivePilot.extendTargetAngle, 0, 180);
                                    ActivePilot.extendTargetAngle = Mathf.Round(ActivePilot.extendTargetAngle);
                                }
                                else
                                {
                                    inputFields["extendTargetAngle"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, evadeLines, contentWidth), inputFields["extendTargetAngle"].possibleValue, 6, inputFieldStyle));
                                    ActivePilot.extendTargetAngle = (float)inputFields["extendTargetAngle"].currentValue;
                                }
                                if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ExtendAngle"), contextLabel);

                                GUI.Label(SettinglabelRect(leftIndent, ++evadeLines), $"{StringUtils.Localize("#LOC_BDArmory_AIWindow_ExtendTargetDist")}: {ActivePilot.extendTargetDist}m", Label);
                                if (!NumFieldsEnabled)
                                {
                                    ActivePilot.extendTargetDist = GUI.HorizontalSlider(SettingSliderRect(leftIndent, evadeLines, contentWidth), ActivePilot.extendTargetDist, 0, 5000);
                                    ActivePilot.extendTargetDist = BDAMath.RoundToUnit(ActivePilot.extendTargetDist, 25);
                                }
                                else
                                {
                                    inputFields["extendTargetDist"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, evadeLines, contentWidth), inputFields["extendTargetDist"].possibleValue, 6, inputFieldStyle));
                                    ActivePilot.extendTargetDist = (float)inputFields["extendTargetDist"].currentValue;
                                }
                                if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ExtendDist"), contextLabel);

                                GUI.Label(SettinglabelRect(leftIndent, ++evadeLines), $"{StringUtils.Localize("#LOC_BDArmory_AIWindow_ExtendAbortTime")}: {ActivePilot.extendAbortTime}s", Label);
                                if (!NumFieldsEnabled)
                                {
                                    ActivePilot.extendAbortTime = GUI.HorizontalSlider(SettingSliderRect(leftIndent, evadeLines, contentWidth), ActivePilot.extendAbortTime, 5, 30);
                                    ActivePilot.extendAbortTime = Mathf.Round(ActivePilot.extendAbortTime);
                                }
                                else
                                {
                                    inputFields["extendAbortTime"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, evadeLines, contentWidth), inputFields["extendAbortTime"].possibleValue, 4, inputFieldStyle));
                                    ActivePilot.extendAbortTime = (float)inputFields["extendAbortTime"].currentValue;
                                }
                                if (contextTipsEnabled) GUI.Label(ContextLabelRect(leftIndent, ++evadeLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ExtendAbortTimeContext"), contextLabel);
                                #endregion
                            }
                            ActivePilot.canExtend = GUI.Toggle(ToggleButtonRect(leftIndent, ++evadeLines, contentWidth), ActivePilot.canExtend, StringUtils.Localize("#LOC_BDArmory_ExtendToggle"), ActivePilot.canExtend ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Dynamic pid"
                            evadeLines += 1.25f;
                            #endregion

                            GUI.EndGroup();
                            evasionHeight = Mathf.Lerp(evasionHeight, evadeLines, 0.15f);
                            evadeLines += 0.1f;
                        }

                        if (showTerrain)
                        {
                            gndLines += 0.2f;
                            GUI.BeginGroup(
                                new Rect(0, ((pidLines + altLines + spdLines + ctrlLines + evadeLines + gndLines) * entryHeight), contentWidth, terrainHeight * entryHeight),
                                GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                            gndLines += 0.25f;

                            GUI.Label(SettinglabelRect(leftIndent, gndLines), StringUtils.Localize("#LOC_BDArmory_PilotAI_Terrain"), BoldLabel);//"Speed"

                            #region Terrain Avoidance Min
                            GUI.Label(SettinglabelRect(leftIndent, ++gndLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_TurnRadiusMin") + ": " + ActivePilot.turnRadiusTwiddleFactorMin.ToString("0.0"), Label);
                            var oldMinTwiddle = ActivePilot.turnRadiusTwiddleFactorMin;
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.turnRadiusTwiddleFactorMin = GUI.HorizontalSlider(SettingSliderRect(leftIndent, gndLines, contentWidth), ActivePilot.turnRadiusTwiddleFactorMin, 0.1f, ActivePilot.UpToEleven ? 10 : 5);
                                ActivePilot.turnRadiusTwiddleFactorMin = Mathf.Round(ActivePilot.turnRadiusTwiddleFactorMin * 10f) / 10f;
                            }
                            else
                            {
                                inputFields["turnRadiusTwiddleFactorMin"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, gndLines, contentWidth), inputFields["turnRadiusTwiddleFactorMin"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.turnRadiusTwiddleFactorMin = (float)inputFields["turnRadiusTwiddleFactorMin"].currentValue;
                            }
                            if (ActivePilot.turnRadiusTwiddleFactorMin != oldMinTwiddle)
                            {
                                ActivePilot.OnMinUpdated(null, null);
                                inputFields["turnRadiusTwiddleFactorMax"].currentValue = ActivePilot.turnRadiusTwiddleFactorMax;
                            }
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, ++gndLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_terrainMin"), contextLabel);
                            }
                            #endregion

                            #region Terrain Avoidance Max
                            GUI.Label(SettinglabelRect(leftIndent, ++gndLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_TurnRadiusMax") + ": " + ActivePilot.turnRadiusTwiddleFactorMax.ToString("0.0"), Label);
                            var oldMaxTwiddle = ActivePilot.turnRadiusTwiddleFactorMax;
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.turnRadiusTwiddleFactorMax = GUI.HorizontalSlider(SettingSliderRect(leftIndent, gndLines, contentWidth), ActivePilot.turnRadiusTwiddleFactorMax, 0.1f, ActivePilot.UpToEleven ? 10 : 5);
                                ActivePilot.turnRadiusTwiddleFactorMax = Mathf.Round(ActivePilot.turnRadiusTwiddleFactorMax * 10) / 10;
                            }
                            else
                            {
                                inputFields["turnRadiusTwiddleFactorMax"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, gndLines, contentWidth), inputFields["turnRadiusTwiddleFactorMax"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.turnRadiusTwiddleFactorMax = (float)inputFields["turnRadiusTwiddleFactorMax"].currentValue;
                            }
                            if (ActivePilot.turnRadiusTwiddleFactorMax != oldMaxTwiddle)
                            {
                                ActivePilot.OnMaxUpdated(null, null);
                                inputFields["turnRadiusTwiddleFactorMin"].currentValue = ActivePilot.turnRadiusTwiddleFactorMin;
                            }
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, ++gndLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_terrainMax"), contextLabel);
                            }
                            #endregion

                            #region Inverted Terrain Avoidance Critical Angle
                            GUI.Label(SettinglabelRect(leftIndent, ++gndLines), $"{StringUtils.Localize("#LOC_BDArmory_AIWindow_InvertedTerrainAvoidanceCriticalAngle")}: {ActivePilot.terrainAvoidanceCriticalAngle:0}", Label);
                            var oldTerrainAvoidanceCriticalAngle = ActivePilot.terrainAvoidanceCriticalAngle;
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.terrainAvoidanceCriticalAngle = GUI.HorizontalSlider(SettingSliderRect(leftIndent, gndLines, contentWidth), ActivePilot.terrainAvoidanceCriticalAngle, 90f, 180f);
                                ActivePilot.terrainAvoidanceCriticalAngle = Mathf.Round(ActivePilot.terrainAvoidanceCriticalAngle);
                            }
                            else
                            {
                                inputFields["terrainAvoidanceCriticalAngle"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, gndLines, contentWidth), inputFields["terrainAvoidanceCriticalAngle"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.terrainAvoidanceCriticalAngle = (float)inputFields["terrainAvoidanceCriticalAngle"].currentValue;
                            }
                            if (ActivePilot.terrainAvoidanceCriticalAngle != oldTerrainAvoidanceCriticalAngle)
                            {
                                ActivePilot.OnTerrainAvoidanceCriticalAngleChanged(null, null);
                            }
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, ++gndLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_InvertedTerrainAvoidanceCriticalAngleContext"), contextLabel);
                            }
                            #endregion

                            #region Terrain Avoidance Control Surface Deployment Time
                            GUI.Label(SettinglabelRect(leftIndent, ++gndLines), $"{StringUtils.Localize("#LOC_BDArmory_AIWindow_TerrainAvoidanceVesselReactionTime")}: {ActivePilot.controlSurfaceDeploymentTime:0.0}", Label);
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.controlSurfaceDeploymentTime = GUI.HorizontalSlider(SettingSliderRect(leftIndent, gndLines, contentWidth), ActivePilot.controlSurfaceDeploymentTime, 0f, 4f);
                                ActivePilot.controlSurfaceDeploymentTime = BDAMath.RoundToUnit(ActivePilot.controlSurfaceDeploymentTime, 0.1f);
                            }
                            else
                            {
                                inputFields["controlSurfaceDeploymentTime"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, gndLines, contentWidth), inputFields["controlSurfaceDeploymentTime"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.controlSurfaceDeploymentTime = (float)inputFields["controlSurfaceDeploymentTime"].currentValue;
                            }
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, ++gndLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_TerrainAvoidanceVesselReactionTimeContext"), contextLabel);
                            }
                            #endregion

                            #region Waypoint Terrain Avoidance
                            GUI.Label(SettinglabelRect(leftIndent, ++gndLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_WaypointTerrainAvoidance") + ": " + ActivePilot.waypointTerrainAvoidance.ToString("0.00"), Label);
                            if (!NumFieldsEnabled)
                            {
                                ActivePilot.waypointTerrainAvoidance = GUI.HorizontalSlider(SettingSliderRect(leftIndent, gndLines, contentWidth), ActivePilot.waypointTerrainAvoidance, 0f, 1f);
                                ActivePilot.waypointTerrainAvoidance = BDAMath.RoundToUnit(ActivePilot.waypointTerrainAvoidance, 0.01f);
                            }
                            else
                            {
                                inputFields["waypointTerrainAvoidance"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, gndLines, contentWidth), inputFields["waypointTerrainAvoidance"].possibleValue, 6, inputFieldStyle));
                                ActivePilot.waypointTerrainAvoidance = (float)inputFields["waypointTerrainAvoidance"].currentValue;
                            }
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, ++gndLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_WaypointTerrainAvoidanceContext"), contextLabel);
                            }
                            #endregion

                            ++gndLines;
                            GUI.EndGroup();
                            terrainHeight = Mathf.Lerp(terrainHeight, gndLines, 0.15f);
                            gndLines += 0.1f;
                        }

                        if (showRam)
                        {
                            ramLines += 0.2f;
                            GUI.BeginGroup(
                                new Rect(0, ((pidLines + altLines + spdLines + ctrlLines + evadeLines + gndLines + ramLines) * entryHeight), contentWidth, rammingHeight * entryHeight),
                                GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                            ramLines += 0.25f;

                            GUI.Label(SettinglabelRect(leftIndent, ramLines), StringUtils.Localize("#LOC_BDArmory_PilotAI_Ramming"), BoldLabel);//"Speed"
                            ramLines++;

                            ActivePilot.allowRamming = GUI.Toggle(ToggleButtonRect(leftIndent, ramLines, contentWidth),
                            ActivePilot.allowRamming, StringUtils.Localize("#LOC_BDArmory_AllowRamming"), ActivePilot.allowRamming ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Dynamic pid"
                            ramLines += 1.25f;

                            if (ActivePilot.allowRamming)
                            {
                                if (!NumFieldsEnabled)
                                {
                                    ActivePilot.controlSurfaceLag =
                                        GUI.HorizontalSlider(SettingSliderRect(leftIndent, ramLines, contentWidth),
                                            ActivePilot.controlSurfaceLag, 0, ActivePilot.UpToEleven ? 1f : 0.2f);
                                    ActivePilot.controlSurfaceLag = Mathf.Round(ActivePilot.controlSurfaceLag * 100) / 100;
                                }
                                else
                                {
                                    inputFields["controlSurfaceLag"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, ramLines, contentWidth), inputFields["controlSurfaceLag"].possibleValue, 6, inputFieldStyle));
                                    ActivePilot.controlSurfaceLag = (float)inputFields["controlSurfaceLag"].currentValue;
                                }
                                GUI.Label(SettinglabelRect(leftIndent, ramLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ControlSurfaceLag") + ": " + ActivePilot.controlSurfaceLag.ToString("0.00"), Label);

                                ramLines++;
                                if (contextTipsEnabled)
                                {
                                    GUI.Label(ContextLabelRect(leftIndent, ramLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_ramLag"), contextLabel);
                                    ramLines++;
                                }
                            }
                            GUI.EndGroup();
                            rammingHeight = Mathf.Lerp(rammingHeight, ramLines, 0.15f);
                            ramLines += 0.1f;
                        }

                        if (showMisc)
                        {
                            miscLines += 0.2f;
                            GUI.BeginGroup(
                                new Rect(0, ((pidLines + altLines + spdLines + ctrlLines + evadeLines + gndLines + ramLines + miscLines) * entryHeight), contentWidth, miscHeight * entryHeight),
                                GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                            miscLines += 0.25f;

                            GUI.Label(SettinglabelRect(leftIndent, miscLines), StringUtils.Localize("#LOC_BDArmory_Orbit"), BoldLabel);//"orbit"
                            miscLines++;

                            ActivePilot.ClockwiseOrbit = GUI.Toggle(ToggleButtonRect(leftIndent, miscLines, contentWidth),
                            ActivePilot.ClockwiseOrbit, ActivePilot.ClockwiseOrbit ? StringUtils.Localize("#LOC_BDArmory_Orbit_Starboard") : StringUtils.Localize("#LOC_BDArmory_Orbit_Port"), ActivePilot.ClockwiseOrbit ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Dynamic pid"
                            miscLines += 1.25f;
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, miscLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_orbit"), Label);//"orbit direction"
                                miscLines++;
                            }

                            GUI.Label(SettinglabelRect(leftIndent, miscLines), StringUtils.Localize("#LOC_BDArmory_StandbyMode"), BoldLabel);//"Standby"
                            miscLines++;

                            ActivePilot.standbyMode = GUI.Toggle(ToggleButtonRect(leftIndent, miscLines, contentWidth),
                            ActivePilot.standbyMode, ActivePilot.standbyMode ? StringUtils.Localize("#LOC_BDArmory_On") : StringUtils.Localize("#LOC_BDArmory_Off"), ActivePilot.standbyMode ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Dynamic pid"
                            miscLines += 1.25f;
                            if (contextTipsEnabled)
                            {
                                GUI.Label(ContextLabelRect(leftIndent, miscLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_standby"), Label);//"Activate when target in guard range"
                                miscLines++;
                            }

                            GUI.Label(SettinglabelRect(leftIndent, miscLines), StringUtils.Localize("#LOC_BDArmory_ControlSurfaceSettings"), BoldLabel);//"Control Surface Settings"
                            miscLines++;

                            if (GUI.Button(ToggleButtonRect(leftIndent, miscLines, contentWidth), StringUtils.Localize("#LOC_BDArmory_StoreControlSurfaceSettings"), BDArmorySetup.BDGuiSkin.button))
                            {
                                ActivePilot.StoreControlSurfaceSettings(); //Hiding these in misc is probably not the best place to put them, but only so much space on the window header bar
                            }
                            miscLines += 1.25f;
                            if (ActivePilot.Events["RestoreControlSurfaceSettings"].active == true)
                            {
                                GUIStyle restoreStyle = BDArmorySetup.BDGuiSkin.button;
                                if (GUI.Button(ToggleButtonRect(leftIndent, miscLines, contentWidth), StringUtils.Localize("#LOC_BDArmory_RestoreControlSurfaceSettings"), restoreStyle))
                                {
                                    ActivePilot.RestoreControlSurfaceSettings();
                                }
                                miscLines += 1.25f;
                            }

                            GUI.EndGroup();
                            miscHeight = Mathf.Lerp(miscHeight, miscLines, 0.15f);
                            miscLines += 0.1f;
                        }
                        contentHeight = (pidLines + altLines + spdLines + ctrlLines + evadeLines + gndLines + ramLines + miscLines) * entryHeight;
                        GUI.EndGroup();
                        GUI.EndScrollView();
                    }
                    else contentHeight = 0;
                }
                else if (ActiveDriver != null)
                {
                    line++;
                    ActiveDriver.UpToEleven = GUI.Toggle(SubsectionRect(leftIndent, line),
                        ActiveDriver.UpToEleven, ActiveDriver.UpToEleven ? StringUtils.Localize("#LOC_BDArmory_UnclampTuning_enabledText") : StringUtils.Localize("#LOC_BDArmory_UnclampTuning_disabledText"), ActiveDriver.UpToEleven ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Misc"
                    if (ActiveDriver.UpToEleven != oldClamp)
                    {
                        SetInputFields(ActiveDriver.GetType());
                    }
                    line += 12;

                    float driverLines = 0;

                    if (infoLinkEnabled)
                    {
                        windowColumns = 3;

                        GUI.Label(new Rect(leftIndent + ColumnWidth * 2, contentTop, ColumnWidth - leftIndent, entryHeight), StringUtils.Localize("#LOC_BDArmory_AIWindow_infoLink"), Title);//"infolink"
                        BeginArea(new Rect(leftIndent + ColumnWidth * 2, contentTop + entryHeight * 1.5f, ColumnWidth - leftIndent, WindowHeight - entryHeight * 1.5f - 2 * contentTop));
                        using (var scrollViewScope = new ScrollViewScope(scrollInfoVector, Width(ColumnWidth - leftIndent), Height(WindowHeight - entryHeight * 1.5f - 2 * contentTop)))
                        {
                            scrollInfoVector = scrollViewScope.scrollPosition;

                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_DriverAI_Help"), infoLinkStyle, Width(ColumnWidth - leftIndent * 4 - 20)); //Pid desc
                            if (ActiveDriver.SurfaceType != AIUtils.VehicleMovementType.Stationary)
                            {
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_DriverAI_Slopes"), infoLinkStyle, Width(ColumnWidth - leftIndent * 4 - 20)); //tgt pitch, slope angle desc
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_DriverAI_Speeds"), infoLinkStyle, Width(ColumnWidth - leftIndent * 4 - 20)); //cruise, flank speed desc
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_DriverAI_Drift"), infoLinkStyle, Width(ColumnWidth - leftIndent * 4 - 20)); //drift angle desc
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_DriverAI_bank"), infoLinkStyle, Width(ColumnWidth - leftIndent * 4 - 20)); //bank angle desc
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_DriverAI_steerMult"), infoLinkStyle, Width(ColumnWidth - leftIndent * 4 - 20)); //steer mult desc
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_DriverAI_SteerDamp"), infoLinkStyle, Width(ColumnWidth - leftIndent * 4 - 20)); //steer damp desc
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_DriverAI_Orientation"), infoLinkStyle, Width(ColumnWidth - leftIndent * 4 - 20)); //attack vector, broadside desc
                            }
                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_DriverAI_Engagement"), infoLinkStyle, Width(ColumnWidth - leftIndent * 4 - 20)); //engage ranges desc
                            if (ActiveDriver.SurfaceType != AIUtils.VehicleMovementType.Stationary)
                            {
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_DriverAI_RCS"), infoLinkStyle, Width(ColumnWidth - leftIndent * 4 - 20)); //RCS desc
                                GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_DriverAI_Mass"), infoLinkStyle, Width(ColumnWidth - leftIndent * 4 - 20)); //avoid mass desc
                            }
                        }
                        EndArea();
                    }

                    scrollViewSAIVector = GUI.BeginScrollView(new Rect(leftIndent + 100, contentTop + entryHeight * 1.5f, (ColumnWidth * 2) - 100 - leftIndent, WindowHeight - entryHeight * 1.5f - 2 * contentTop), scrollViewSAIVector,
                                           new Rect(0, 0, ColumnWidth * 2 - 120 - leftIndent * 2, height + contentTop));

                    GUI.BeginGroup(new Rect(leftIndent, 0, ColumnWidth * 2 - 120 - leftIndent * 2, height + 2 * contentTop), GUIContent.none, BDArmorySetup.BDGuiSkin.box); //darker box

                    contentWidth -= 24;
                    leftIndent += 3;

                    driverLines += 0.2f;
                    GUI.BeginGroup(
                        new Rect(0, (driverLines * entryHeight), contentWidth, height * entryHeight),
                        GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                    driverLines += 0.25f;

                    if (Drivertype != (Drivertype = Mathf.RoundToInt(GUI.HorizontalSlider(SettingSliderRect(leftIndent, driverLines, contentWidth), Drivertype, 0, VehicleMovementTypes.Length - 1))))
                    {
                        ActiveDriver.SurfaceTypeName = VehicleMovementTypes[Drivertype].ToString();
                        ActiveDriver.ChooseOptionsUpdated(null, null);
                    }
                    GUI.Label(SettinglabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_VehicleType") + ": " + ActiveDriver.SurfaceTypeName, Label);//"Wobbly"

                    driverLines++;
                    if (contextTipsEnabled)
                    {
                        GUI.Label(ContextLabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_DriverAI_VeeType"), contextLabel);//"Wobbly"
                        driverLines++;
                    }

                    if (ActiveDriver.SurfaceType != AIUtils.VehicleMovementType.Stationary)
                    {
                        if (!NumFieldsEnabled)
                        {
                            ActiveDriver.MaxSlopeAngle =
                                GUI.HorizontalSlider(SettingSliderRect(leftIndent, driverLines, contentWidth),
                                    ActiveDriver.MaxSlopeAngle, 10, ActiveDriver.UpToEleven ? 90 : 30);
                            ActiveDriver.MaxSlopeAngle = Mathf.Round(ActiveDriver.MaxSlopeAngle);
                        }
                        else
                        {
                            inputFields["MaxSlopeAngle"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, driverLines, contentWidth), inputFields["MaxSlopeAngle"].possibleValue, 6, inputFieldStyle));
                            ActiveDriver.MaxSlopeAngle = (float)inputFields["MaxSlopeAngle"].currentValue;
                        }
                        GUI.Label(SettinglabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_MaxSlopeAngle") + ": " + ActiveDriver.MaxSlopeAngle.ToString("0"), Label);//"Steer Ki"
                        driverLines++;
                        if (contextTipsEnabled)
                        {
                            GUI.Label(ContextLabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_DriverAI_SlopeAngle"), contextLabel);//"undershoot"
                            driverLines++;
                        }

                        if (!NumFieldsEnabled)
                        {
                            ActiveDriver.CruiseSpeed =
                                    GUI.HorizontalSlider(SettingSliderRect(leftIndent, driverLines, contentWidth),
                                        ActiveDriver.CruiseSpeed, 5, ActiveDriver.UpToEleven ? 300 : 60);
                            ActiveDriver.CruiseSpeed = Mathf.Round(ActiveDriver.CruiseSpeed);
                        }
                        else
                        {
                            inputFields["CruiseSpeed"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, driverLines, contentWidth), inputFields["CruiseSpeed"].possibleValue, 6, inputFieldStyle));
                            ActiveDriver.CruiseSpeed = (float)inputFields["CruiseSpeed"].currentValue;
                        }
                        GUI.Label(SettinglabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_CruiseSpeed") + ": " + ActiveDriver.CruiseSpeed.ToString("0"), Label);//"Steer Damping"
                        driverLines++;
                        if (contextTipsEnabled)
                        {
                            GUI.Label(ContextLabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_idleSpeed"), contextLabel);//"Wobbly"
                            driverLines++;
                        }

                        if (!NumFieldsEnabled)
                        {
                            ActiveDriver.MaxSpeed =
                                   GUI.HorizontalSlider(SettingSliderRect(leftIndent, driverLines, contentWidth),
                                ActiveDriver.MaxSpeed, 5, ActiveDriver.UpToEleven ? 400 : 80);
                            ActiveDriver.MaxSpeed = Mathf.Round(ActiveDriver.MaxSpeed);
                        }
                        else
                        {
                            inputFields["MaxSpeed"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, driverLines, contentWidth), inputFields["MaxSpeed"].possibleValue, 6, inputFieldStyle));
                            ActiveDriver.MaxSpeed = (float)inputFields["MaxSpeed"].currentValue;
                        }
                        GUI.Label(SettinglabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_MaxSpeed") + ": " + ActiveDriver.MaxSpeed.ToString("0"), Label);//"Steer Damping"
                        driverLines++;
                        if (contextTipsEnabled)
                        {
                            GUI.Label(ContextLabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_DriverAI_MaxSpeed"), contextLabel);//"Wobbly"
                            driverLines++;
                        }

                        if (!NumFieldsEnabled)
                        {
                            ActiveDriver.MaxDrift =
                            GUI.HorizontalSlider(SettingSliderRect(leftIndent, driverLines, contentWidth),
                                ActiveDriver.MaxDrift, 1, 180);
                            ActiveDriver.MaxDrift = Mathf.Round(ActiveDriver.MaxDrift);
                        }
                        else
                        {
                            inputFields["MaxDrift"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, driverLines, contentWidth), inputFields["MaxDrift"].possibleValue, 6, inputFieldStyle));
                            ActiveDriver.MaxDrift = (float)inputFields["MaxDrift"].currentValue;
                        }
                        GUI.Label(SettinglabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_MaxDrift") + ": " + ActiveDriver.MaxDrift.ToString("0"), Label);//"Steer Damping"
                        driverLines++;
                        if (contextTipsEnabled)
                        {
                            GUI.Label(ContextLabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_DriverAI_MaxDrift"), contextLabel);//"Wobbly"
                            driverLines++;
                        }

                        if (!NumFieldsEnabled)
                        {
                            ActiveDriver.TargetPitch =
                                GUI.HorizontalSlider(SettingSliderRect(leftIndent, driverLines, contentWidth),
                                    ActiveDriver.TargetPitch, -10, 10);
                            ActiveDriver.TargetPitch = Mathf.Round(ActiveDriver.TargetPitch * 10) / 10;
                        }
                        else
                        {
                            inputFields["TargetPitch"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, driverLines, contentWidth), inputFields["TargetPitch"].possibleValue, 6, inputFieldStyle));
                            ActiveDriver.TargetPitch = (float)inputFields["TargetPitch"].currentValue;
                        }
                        GUI.Label(SettinglabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_TargetPitch") + ": " + ActiveDriver.TargetPitch.ToString("0.0"), Label);//"Steer Damping"
                        driverLines++;
                        if (contextTipsEnabled)
                        {
                            GUI.Label(ContextLabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_DriverAI_Pitch"), contextLabel);//"Wobbly"
                            driverLines++;
                        }

                        if (!NumFieldsEnabled)
                        {
                            ActiveDriver.BankAngle =
                                GUI.HorizontalSlider(SettingSliderRect(leftIndent, driverLines, contentWidth),
                                    ActiveDriver.BankAngle, -45, 45);
                            ActiveDriver.BankAngle = Mathf.Round(ActiveDriver.BankAngle);
                        }
                        else
                        {
                            inputFields["BankAngle"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, driverLines, contentWidth), inputFields["BankAngle"].possibleValue, 6, inputFieldStyle));
                            ActiveDriver.BankAngle = (float)inputFields["BankAngle"].currentValue;
                        }
                        GUI.Label(SettinglabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_BankAngle") + ": " + ActiveDriver.BankAngle.ToString("0"), Label);//"Steer Damping"
                        driverLines++;
                        if (contextTipsEnabled)
                        {
                            GUI.Label(ContextLabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_bankLimit"), contextLabel);//"Wobbly"
                            driverLines++;
                        }
                    }

                    if (!NumFieldsEnabled)
                    {
                        ActiveDriver.steerMult =
                            GUI.HorizontalSlider(SettingSliderRect(leftIndent, driverLines, contentWidth),
                            ActiveDriver.steerMult, 0.2f, ActiveDriver.UpToEleven ? 200 : 20);
                        ActiveDriver.steerMult = Mathf.Round(ActiveDriver.steerMult * 10) / 10;
                    }
                    else
                    {
                        inputFields["steerMult"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, driverLines, contentWidth), inputFields["steerMult"].possibleValue, 6, inputFieldStyle));
                        ActiveDriver.steerMult = (float)inputFields["steerMult"].currentValue;
                    }
                    GUI.Label(SettinglabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_SteerFactor") + ": " + ActiveDriver.steerMult.ToString("0.0"), Label);//"Steer Damping"
                    driverLines++;
                    if (contextTipsEnabled)
                    {
                        GUI.Label(ContextLabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_DriverAI_SteerMult"), contextLabel);//"Wobbly"
                        driverLines++;
                    }

                    if (!NumFieldsEnabled)
                    {
                        ActiveDriver.steerDamping =
                            GUI.HorizontalSlider(SettingSliderRect(leftIndent, driverLines, contentWidth),
                            ActiveDriver.steerDamping, 0.1f, ActiveDriver.UpToEleven ? 100 : 10);
                        ActiveDriver.steerDamping = Mathf.Round(ActiveDriver.steerDamping * 10) / 10;
                    }
                    else
                    {
                        inputFields["steerDamping"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, driverLines, contentWidth), inputFields["steerDamping"].possibleValue, 6, inputFieldStyle));
                        ActiveDriver.steerDamping = (float)inputFields["steerDamping"].currentValue;
                    }
                    GUI.Label(SettinglabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_SteerDamping") + ": " + ActiveDriver.steerDamping.ToString("0.0"), Label);//"Steer Damping"
                    driverLines++;
                    if (contextTipsEnabled)
                    {
                        GUI.Label(ContextLabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_AIWindow_DynDampMult"), contextLabel);//"Wobbly"
                        driverLines++;
                    }

                    if (ActiveDriver.SurfaceType != AIUtils.VehicleMovementType.Stationary)
                    {
                        ActiveDriver.BroadsideAttack = GUI.Toggle(ToggleButtonRect(leftIndent, driverLines, contentWidth),
                            ActiveDriver.BroadsideAttack, StringUtils.Localize("#LOC_BDArmory_BroadsideAttack") + " : " + (ActiveDriver.BroadsideAttack ? StringUtils.Localize("#LOC_BDArmory_BroadsideAttack_enabledText") : StringUtils.Localize("#LOC_BDArmory_BroadsideAttack_disabledText")), ActiveDriver.BroadsideAttack ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Dynamic pid"
                        driverLines += 1.25f;
                        if (contextTipsEnabled)
                        {
                            GUI.Label(ContextLabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_DriverAI_AtkVector"), contextLabel);
                            driverLines++;
                        }
                    }

                    if (!NumFieldsEnabled)
                    {
                        ActiveDriver.MinEngagementRange =
                            GUI.HorizontalSlider(SettingSliderRect(leftIndent, driverLines, contentWidth),
                            ActiveDriver.MinEngagementRange, 0, ActiveDriver.UpToEleven ? 20000 : 6000);
                        ActiveDriver.MinEngagementRange = Mathf.Round(ActiveDriver.MinEngagementRange / 100) * 100;
                    }
                    else
                    {
                        inputFields["MinEngagementRange"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, driverLines, contentWidth), inputFields["MinEngagementRange"].possibleValue, 6, inputFieldStyle));
                        ActiveDriver.MinEngagementRange = (float)inputFields["MinEngagementRange"].currentValue;
                    }
                    GUI.Label(SettinglabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_EngageRangeMin") + ": " + ActiveDriver.MinEngagementRange.ToString("0"), Label);//"Steer Damping"
                    driverLines++;
                    if (contextTipsEnabled)
                    {
                        GUI.Label(ContextLabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_DriverAI_MinEngage"), contextLabel);//"Wobbly"
                        driverLines++;
                    }

                    if (!NumFieldsEnabled)
                    {
                        ActiveDriver.MaxEngagementRange =
                            GUI.HorizontalSlider(SettingSliderRect(leftIndent, driverLines, contentWidth),
                            ActiveDriver.MaxEngagementRange, 500, ActiveDriver.UpToEleven ? 20000 : 8000);
                        ActiveDriver.MaxEngagementRange = Mathf.Round(ActiveDriver.MaxEngagementRange / 100) * 100;
                    }
                    else
                    {
                        inputFields["MaxEngagementRange"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, driverLines, contentWidth), inputFields["MaxEngagementRange"].possibleValue, 6, inputFieldStyle));
                        ActiveDriver.MaxEngagementRange = (float)inputFields["MaxEngagementRange"].currentValue;
                    }
                    GUI.Label(SettinglabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_EngageRangeMax") + ": " + ActiveDriver.MaxEngagementRange.ToString("0"), Label);//"Steer Damping"
                    driverLines++;
                    if (contextTipsEnabled)
                    {
                        GUI.Label(ContextLabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_DriverAI_MaxEngage"), contextLabel);//"Wobbly"
                        driverLines++;
                    }

                    ActiveDriver.ManeuverRCS = GUI.Toggle(ToggleButtonRect(leftIndent, driverLines, contentWidth),
                        ActiveDriver.ManeuverRCS, StringUtils.Localize("#LOC_BDArmory_ManeuverRCS") + " : " + (ActiveDriver.ManeuverRCS ? StringUtils.Localize("#LOC_BDArmory_ManeuverRCS_enabledText") : StringUtils.Localize("#LOC_BDArmory_ManeuverRCS_disabledText")), ActiveDriver.BroadsideAttack ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Dynamic pid"
                    driverLines += 1.25f;
                    if (contextTipsEnabled)
                    {
                        GUI.Label(ContextLabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_DriverAI_RCS"), contextLabel);
                        driverLines++;
                    }
                    if (ActiveDriver.SurfaceType != AIUtils.VehicleMovementType.Stationary)
                    {
                        if (!NumFieldsEnabled)
                        {
                            ActiveDriver.AvoidMass =
                                GUI.HorizontalSlider(SettingSliderRect(leftIndent, driverLines, contentWidth),
                                ActiveDriver.AvoidMass, 0, ActiveDriver.UpToEleven ? 1000000f : 100);
                            ActiveDriver.AvoidMass = Mathf.Round(ActiveDriver.AvoidMass);
                        }
                        else
                        {
                            inputFields["AvoidMass"].tryParseValue(GUI.TextField(SettingTextRect(leftIndent, driverLines, contentWidth), inputFields["AvoidMass"].possibleValue, 7, inputFieldStyle));
                            ActiveDriver.AvoidMass = (float)inputFields["AvoidMass"].currentValue;
                        }
                        GUI.Label(SettinglabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_MinObstacleMass") + ": " + ActiveDriver.AvoidMass.ToString("0"), Label);//"Steer Damping"
                        driverLines++;
                        if (contextTipsEnabled)
                        {
                            GUI.Label(ContextLabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_DriverAI_Mass"), contextLabel);//"Wobbly"
                            driverLines++;
                        }

                        if (broadsideDir != (broadsideDir = Mathf.RoundToInt(GUI.HorizontalSlider(SettingSliderRect(leftIndent, driverLines, contentWidth), broadsideDir, 0, ActiveDriver.orbitDirections.Length - 1))))
                        {
                            ActiveDriver.SetBroadsideDirection(ActiveDriver.orbitDirections[broadsideDir]);
                            ActiveDriver.ChooseOptionsUpdated(null, null);
                        }
                        GUI.Label(SettinglabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_PreferredBroadsideDirection") + ": " + ActiveDriver.OrbitDirectionName, Label);//"Wobbly"
                        driverLines++;
                        if (contextTipsEnabled)
                        {
                            GUI.Label(ContextLabelRect(leftIndent, driverLines), StringUtils.Localize("#LOC_BDArmory_DriverAI_BroadsideDir"), contextLabel);//"Wobbly"
                            driverLines++;
                        }
                    }
                    GUI.EndGroup();

                    contentHeight = driverLines * entryHeight;

                    GUI.EndGroup();
                    GUI.EndScrollView();
                }
            }
            WindowWidth = Mathf.Lerp(WindowWidth, windowColumns * ColumnWidth, 0.15f);

            #region Resizing
            var resizeRect = new Rect(WindowWidth - 16, WindowHeight - 16, 16, 16);
            GUI.DrawTexture(resizeRect, GUIUtils.resizeTexture, ScaleMode.StretchToFill, true);
            if (Event.current.type == EventType.MouseDown && resizeRect.Contains(Event.current.mousePosition))
            {
                resizingWindow = true;
            }

            if (Event.current.type == EventType.Repaint && resizingWindow)
            {
                WindowHeight += Mouse.delta.y;
                WindowHeight = Mathf.Max(WindowHeight, 305);
                GUI.Label(new Rect(WindowWidth / 2, WindowHeight - 26, WindowWidth / 2 - 26, 26), $"Resizing: {WindowHeight}", Label);
            }
            #endregion

            var previousWindowHeight = BDArmorySetup.WindowRectAI.height;
            BDArmorySetup.WindowRectAI.height = WindowHeight;
            BDArmorySetup.WindowRectAI.width = WindowWidth;
            if (!resizingWindow && BDArmorySettings.STRICT_WINDOW_BOUNDARIES && WindowHeight < previousWindowHeight && Mathf.Round(BDArmorySetup.WindowRectAI.y + previousWindowHeight) == Screen.height) // Window shrunk while being at edge of screen.
                BDArmorySetup.WindowRectAI.y = Screen.height - BDArmorySetup.WindowRectAI.height;
        }
        #endregion GUI

        internal void OnDestroy()
        {
            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onEditorLoad.Remove(OnEditorLoad);
            GameEvents.onEditorPartPlaced.Remove(OnEditorPartPlacedEvent);
            GameEvents.onEditorPartDeleted.Remove(OnEditorPartDeletedEvent);
        }
    }
}
