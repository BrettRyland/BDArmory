using KSP.UI.Screens;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System;
using UnityEngine;
using static UnityEngine.GUILayout;

using BDArmory.Control;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.Extensions;

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
        const float ColumnWidth = 350;
        const float _buttonSize = 26;
        const float _windowMargin = 4;
        const float contentTop = 10;
        const float entryHeight = 20;
        bool checkForAI = false; // Flag to indicate that a new check for AI needs to happen (instead of responding to every event).

        int Drivertype = 0;
        int broadsideDir = 0;
        int pidMode = 0;
        int rollTowards = 0;
        public AIUtils.VehicleMovementType[] VehicleMovementTypes = (AIUtils.VehicleMovementType[])Enum.GetValues(typeof(AIUtils.VehicleMovementType)); // Get the VehicleMovementType as an array of enum values.
        public BDModuleOrbitalAI.PIDModeTypes[] PIDModeTypes = (BDModuleOrbitalAI.PIDModeTypes[])Enum.GetValues(typeof(BDModuleOrbitalAI.PIDModeTypes)); // Get the PID mode as an array of enum values.
        public BDModuleOrbitalAI.RollModeTypes[] RollModeTypes = (BDModuleOrbitalAI.RollModeTypes[])Enum.GetValues(typeof(BDModuleOrbitalAI.RollModeTypes)); // Get the roll mode as an array of enum values.

        public enum ActiveAIType { PilotAI, SurfaceAI, VTOLAI, OrbitalAI, None }; // Order of priority of AIs.
        public ActiveAIType activeAIType = ActiveAIType.None;
        public BDGenericAIBase ActiveAI;

        Dictionary<ActiveAIType, Vector2> scrollViewVectors = [];
        private Vector2 scrollInfoVector;

        public static BDArmoryAIGUI Instance;
        public static bool buttonSetup;

        Dictionary<string, NumericInputField> inputFields;

        GUIStyle BoldLabel;
        GUIStyle Label;
        GUIStyle Title;
        GUIStyle contextLabel;
        GUIStyle contextLabelRight;
        GUIStyle infoLinkStyle;
        bool stylesConfigured = false;


        void Awake()
        {
            if (Instance != null) Destroy(Instance);
            Instance = this;
        }

        void Start()
        {
            if (BDArmorySettings.AI_TOOLBAR_BUTTON) AddToolbarButton();

            BDArmorySetup.WindowRectAI = new Rect(BDArmorySetup.WindowRectAI.x, BDArmorySetup.WindowRectAI.y, WindowWidth, BDArmorySetup.WindowRectAI.height);
            WindowHeight = Mathf.Max(BDArmorySetup.WindowRectAI.height, 305);

            if (HighLogic.LoadedSceneIsFlight)
            {
                GetAI();
                GameEvents.onVesselChange.Add(OnVesselChange);
                GameEvents.onVesselPartCountChanged.Add(OnVesselModified);
                GameEvents.onPartDestroyed.Add(OnPartDestroyed);
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                GetAIEditor();
                GameEvents.onEditorLoad.Add(OnEditorLoad);
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
            if (!(HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor)) return;
            if (BDInputUtils.GetKeyDown(BDInputSettingsFields.GUI_AI_TOGGLE))
            {
                ToggleAIGUI();
            }
            if (checkForAI) // Only happens during flight.
            {
                GetAI();
                checkForAI = false;
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

        void OnVesselModified(Vessel v) // Active AI was on a part that got detached from the active vessel.
        {
            if (!windowBDAAIGUIEnabled || activeAIType == ActiveAIType.None) return;
            if (v == null) return;
            if (v.isActiveVessel && (ActiveAI == null || ActiveAI.vessel != v)) // Was an active vessel with an AI, but the AI is now gone or on another vessel.
            {
                activeAIType = ActiveAIType.None;
                checkForAI = true;
            }
        }

        void OnPartDestroyed(Part p)
        {
            if (!windowBDAAIGUIEnabled || activeAIType == ActiveAIType.None) return;
            if (ActiveAI == null) // We had an AI, but now it's gone...
            {
                activeAIType = ActiveAIType.None;
                checkForAI = true;
            }
        }

        void OnEditorLoad(ShipConstruct ship, CraftBrowserDialog.LoadType loadType)
        {
            GetAIEditor();
        }

        private void OnEditorPartPlacedEvent(Part p)
        {
            if (p == null) return;

            foreach (var aiType in Enum.GetValues(typeof(ActiveAIType)) as ActiveAIType[]) // Check for AIs in the order defined in the enum.
            {
                if (aiType == activeAIType && ActiveAI != null) return; // We have an active AI of this type already.
                BDGenericAIBase aiQuery = aiType switch
                {
                    ActiveAIType.PilotAI => p.FindModuleImplementing<BDModulePilotAI>(),
                    ActiveAIType.SurfaceAI => p.FindModuleImplementing<BDModuleSurfaceAI>(),
                    ActiveAIType.VTOLAI => p.FindModuleImplementing<BDModuleVTOLAI>(),
                    ActiveAIType.OrbitalAI => p.FindModuleImplementing<BDModuleOrbitalAI>(),
                    _ => null
                };
                if (aiQuery == null) continue; // None of this type found.
                activeAIType = aiType;
                ActiveAI = aiQuery;
                SetInputFields(aiType);
                SetChooseOptionSliders();
                return;
            }
            // Nothing found.
            activeAIType = ActiveAIType.None;
            ActiveAI = null;
        }

        private void OnEditorPartDeletedEvent(Part p)
        {
            if (activeAIType != ActiveAIType.None || ActiveAI != null) // If we had an active AI, we need to check to see if it's disappeared.
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
            ActiveAI = null;
            activeAIType = ActiveAIType.None;
            inputFields = null;
            var tic = Time.time;
            if (FlightGlobals.ActiveVessel == null)
            {
                yield return new WaitUntilFixed(() => FlightGlobals.ActiveVessel != null || Time.time - tic > 1); // Give it up to a second to find the active vessel.
                if (FlightGlobals.ActiveVessel == null) yield break;
            }
            // Now, get the new AI and update stuff.
            foreach (var aiType in Enum.GetValues(typeof(ActiveAIType)) as ActiveAIType[])
            {
                BDGenericAIBase aiQuery = aiType switch
                {
                    ActiveAIType.PilotAI => VesselModuleRegistry.GetBDModulePilotAI(FlightGlobals.ActiveVessel, true),
                    ActiveAIType.SurfaceAI => VesselModuleRegistry.GetBDModuleSurfaceAI(FlightGlobals.ActiveVessel, true),
                    ActiveAIType.VTOLAI => VesselModuleRegistry.GetModule<BDModuleVTOLAI>(FlightGlobals.ActiveVessel, true),
                    ActiveAIType.OrbitalAI => VesselModuleRegistry.GetModule<BDModuleOrbitalAI>(FlightGlobals.ActiveVessel, true),
                    _ => null
                };
                if (aiQuery == null) continue; // None of this type found.
                activeAIType = aiType;
                ActiveAI = aiQuery;
                SetInputFields(aiType);
                SetChooseOptionSliders();
                yield break;
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
                    foreach (var aiType in Enum.GetValues(typeof(ActiveAIType)) as ActiveAIType[])
                    {
                        List<BDGenericAIBase> aiQuery = aiType switch
                        {
                            ActiveAIType.PilotAI => p.FindModulesImplementing<BDModulePilotAI>().ConvertAll(ai => ai as BDGenericAIBase),
                            ActiveAIType.SurfaceAI => p.FindModulesImplementing<BDModuleSurfaceAI>().ConvertAll(ai => ai as BDGenericAIBase),
                            ActiveAIType.VTOLAI => p.FindModulesImplementing<BDModuleVTOLAI>().ConvertAll(ai => ai as BDGenericAIBase),
                            ActiveAIType.OrbitalAI => p.FindModulesImplementing<BDModuleOrbitalAI>().ConvertAll(ai => ai as BDGenericAIBase),
                            _ => null
                        };
                        if (aiQuery == null || aiQuery.Count == 0) continue; // None of this type found.
                        foreach (var ai in aiQuery)
                        {
                            if (ai == null) continue;
                            if (ai == ActiveAI) yield break; // We found the current active AI!
                            activeAIType = aiType;
                            ActiveAI = ai;
                            SetInputFields(aiType);
                            SetChooseOptionSliders();
                            yield break;
                        }
                    }
                }
            }

            // No AIs were found, clear everything.
            activeAIType = ActiveAIType.None;
            ActiveAI = null;
            inputFields = null;
        }

        /// <summary>
        /// Get the value, minValue and maxValue of a UI_FloatRange derived control.
        /// </summary>
        /// <param name="aiType">The type of the AI.</param>
        /// <param name="gAI">The AI.</param>
        /// <param name="fieldName">The name of the field to look at.</param>
        /// <returns>value, minValue, maxValue, (rounding, sigFig, withZero)</returns>
        (float, float, float, (float, float, bool)) GetAIFieldLimits(ActiveAIType aiType, BDGenericAIBase gAI, string fieldName)
        {
            float value = 0, minValue = 0, maxValue = 0, rounding = 0, sigFig = 0;
            bool withZero = false;
            try
            {
                (float, float, float, float, bool) GetLimits(UI_FloatRange uic)
                {
                    if (uic is UI_FloatSemiLogRange) (minValue, maxValue, rounding, sigFig, withZero) = (uic as UI_FloatSemiLogRange).GetLimits();
                    else if (uic is UI_FloatPowerRange) (minValue, maxValue, rounding, sigFig) = (uic as UI_FloatPowerRange).GetLimits();
                    else
                    {
                        minValue = uic.minValue;
                        maxValue = uic.maxValue;
                        rounding = uic.stepIncrement;
                    }
                    return (minValue, maxValue, rounding, sigFig, withZero);
                }
                switch (aiType)
                {
                    case ActiveAIType.PilotAI:
                        {
                            var AI = gAI as BDModulePilotAI;
                            var uic = (HighLogic.LoadedSceneIsFlight ? AI.Fields[fieldName].uiControlFlight : AI.Fields[fieldName].uiControlEditor) as UI_FloatRange;
                            (minValue, maxValue, rounding, sigFig, withZero) = GetLimits(uic);
                            value = (float)typeof(BDModulePilotAI).GetField(fieldName).GetValue(AI);
                        }
                        break;
                    case ActiveAIType.SurfaceAI:
                        {
                            var AI = gAI as BDModuleSurfaceAI;
                            var uic = (HighLogic.LoadedSceneIsFlight ? AI.Fields[fieldName].uiControlFlight : AI.Fields[fieldName].uiControlEditor) as UI_FloatRange;
                            (minValue, maxValue, rounding, sigFig, withZero) = GetLimits(uic);
                            value = (float)typeof(BDModuleSurfaceAI).GetField(fieldName).GetValue(AI);
                        }
                        break;
                    case ActiveAIType.VTOLAI:
                        {
                            var AI = gAI as BDModuleVTOLAI;
                            var uic = (HighLogic.LoadedSceneIsFlight ? AI.Fields[fieldName].uiControlFlight : AI.Fields[fieldName].uiControlEditor) as UI_FloatRange;
                            (minValue, maxValue, rounding, sigFig, withZero) = GetLimits(uic);
                            value = (float)typeof(BDModuleVTOLAI).GetField(fieldName).GetValue(AI);
                        }
                        break;
                    case ActiveAIType.OrbitalAI:
                        {
                            var AI = gAI as BDModuleOrbitalAI;
                            var uic = (HighLogic.LoadedSceneIsFlight ? AI.Fields[fieldName].uiControlFlight : AI.Fields[fieldName].uiControlEditor) as UI_FloatRange;
                            (minValue, maxValue, rounding, sigFig, withZero) = GetLimits(uic);
                            value = (float)typeof(BDModuleOrbitalAI).GetField(fieldName).GetValue(AI);
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                var errorMsg = e.Message;
#if DEBUG
                errorMsg += $"\n{e.StackTrace}";
#endif
                Debug.LogError($"[BDArmory.BDArmoryAIGUI]: Failed to retrieve field limits from {fieldName} on AI of type {aiType}: {errorMsg}");
            }
            return (value, minValue, maxValue, (rounding, sigFig, withZero));
        }

        /// <summary>
        /// Get the current field limits from the inputFields data (which may change due to UpToEleven).
        /// Note: if the values require significant post-processing (e.g., Log10), then it may be better to just use fixed values.
        /// </summary>
        /// <param name="fieldName"></param>
        /// <returns>minValue, maxValue, rounding, sig.fig., with zero</returns>
        (float, float, float, float, bool) GetFieldLimits(string fieldName)
        {
            if (!inputFields.ContainsKey(fieldName)) return (0, 0, 0, 0, false);
            var field = inputFields[fieldName];
            return ((float)field.minValue, (float)field.maxValue, field.rounding, field.sigFig, field.withZero);
        }

        /// <summary>
        /// Set the inputFields entries for the given AI type.
        /// Note: only UI_FloatRange derived entries should be included here.
        /// </summary>
        /// <param name="aiType">The type of the currently active AI.</param>
        void SetInputFields(ActiveAIType aiType)
        {
            // Note: We use nameof(AI.field) to get the fieldname to avoid typos.
            switch (aiType)
            {
                case ActiveAIType.PilotAI:
                    {
                        var AI = ActiveAI as BDModulePilotAI;
                        if (AI == null)
                        {
                            Debug.LogError($"[BDArmory.BDArmoryAIGUI]: Mismatch between AI type and actual AI.");
                            activeAIType = ActiveAIType.None;
                            inputFields = null;
                            return;
                        }
                        inputFields = new List<string> {
                            nameof(AI.steerMult),
                            nameof(AI.steerKiAdjust),
                            nameof(AI.steerDamping),
                            nameof(AI.DynamicDampingMin),
                            nameof(AI.DynamicDampingMax),
                            nameof(AI.dynamicSteerDampingFactor),
                            nameof(AI.DynamicDampingPitchMin),
                            nameof(AI.DynamicDampingPitchMax),
                            nameof(AI.dynamicSteerDampingPitchFactor),
                            nameof(AI.DynamicDampingYawMin),
                            nameof(AI.DynamicDampingYawMax),
                            nameof(AI.dynamicSteerDampingYawFactor),
                            nameof(AI.DynamicDampingRollMin),
                            nameof(AI.DynamicDampingRollMax),
                            nameof(AI.dynamicSteerDampingRollFactor),

                            nameof(AI.autoTuningOptionNumSamples),
                            nameof(AI.autoTuningOptionFastResponseRelevance),
                            nameof(AI.autoTuningOptionInitialLearningRate),
                            nameof(AI.autoTuningOptionInitialRollRelevance),
                            nameof(AI.autoTuningAltitude),
                            nameof(AI.autoTuningSpeed),
                            nameof(AI.autoTuningRecenteringDistance),

                            nameof(AI.defaultAltitude),
                            nameof(AI.minAltitude),
                            nameof(AI.maxAltitude),

                            nameof(AI.maxSpeed),
                            nameof(AI.takeOffSpeed),
                            nameof(AI.minSpeed),
                            nameof(AI.strafingSpeed),
                            nameof(AI.idleSpeed),
                            nameof(AI.ABPriority),
                            nameof(AI.ABOverrideThreshold),
                            nameof(AI.brakingPriority),

                            nameof(AI.maxSteer),
                            nameof(AI.lowSpeedSwitch),
                            nameof(AI.maxSteerAtMaxSpeed),
                            nameof(AI.cornerSpeed),
                            nameof(AI.altitudeSteerLimiterFactor),
                            nameof(AI.altitudeSteerLimiterAltitude),
                            nameof(AI.maxBank),
                            nameof(AI.waypointPreRollTime),
                            nameof(AI.waypointYawAuthorityTime),
                            nameof(AI.maxAllowedGForce),
                            nameof(AI.maxAllowedAoA),
                            nameof(AI.postStallAoA),
                            nameof(AI.ImmelmannTurnAngle),
                            nameof(AI.ImmelmannPitchUpBias),

                            nameof(AI.minEvasionTime),
                            nameof(AI.evasionNonlinearity),
                            nameof(AI.evasionThreshold),
                            nameof(AI.evasionTimeThreshold),
                            nameof(AI.evasionMinRangeThreshold),
                            nameof(AI.collisionAvoidanceThreshold),
                            nameof(AI.vesselCollisionAvoidanceLookAheadPeriod),
                            nameof(AI.vesselCollisionAvoidanceStrength),
                            nameof(AI.vesselStandoffDistance),
                            nameof(AI.extendDistanceAirToAir),
                            nameof(AI.extendAngleAirToAir),
                            nameof(AI.extendDistanceAirToGroundGuns),
                            nameof(AI.extendDistanceAirToGround),
                            nameof(AI.extendTargetVel),
                            nameof(AI.extendTargetAngle),
                            nameof(AI.extendTargetDist),
                            nameof(AI.extendAbortTime),

                            nameof(AI.turnRadiusTwiddleFactorMin),
                            nameof(AI.turnRadiusTwiddleFactorMax),
                            nameof(AI.terrainAvoidanceCriticalAngle),
                            nameof(AI.controlSurfaceDeploymentTime),
                            nameof(AI.postTerrainAvoidanceCoolDownDuration),
                            nameof(AI.waypointTerrainAvoidance),

                            nameof(AI.controlSurfaceLag),
                        }.ToDictionary(key => key, key =>
                        {
                            var (value, minValue, maxValue, meta) = GetAIFieldLimits(aiType, ActiveAI, key);
                            return gameObject.AddComponent<NumericInputField>().Initialise(0, value, minValue, maxValue, meta);
                        });
                        showSection[Section.UpToEleven] = AI.UpToEleven;
                    }
                    break;
                case ActiveAIType.SurfaceAI:
                    {
                        var AI = ActiveAI as BDModuleSurfaceAI;
                        if (AI == null)
                        {
                            Debug.LogError($"[BDArmory.BDArmoryAIGUI]: Mismatch between AI type and actual AI.");
                            activeAIType = ActiveAIType.None;
                            inputFields = null;
                            return;
                        }
                        inputFields = new List<string> {
                            nameof(AI.MaxSlopeAngle),
                            nameof(AI.CruiseSpeed),
                            nameof(AI.MaxSpeed),
                            nameof(AI.MaxDrift),
                            nameof(AI.TargetPitch),
                            nameof(AI.BankAngle),
                            nameof(AI.WeaveFactor),
                            nameof(AI.steerMult),
                            nameof(AI.steerDamping),
                            nameof(AI.MinEngagementRange),
                            nameof(AI.MaxEngagementRange),
                            nameof(AI.AvoidMass),
                        }.ToDictionary(key => key, key =>
                        {
                            var (value, minValue, maxValue, meta) = GetAIFieldLimits(aiType, ActiveAI, key);
                            return gameObject.AddComponent<NumericInputField>().Initialise(0, value, minValue, maxValue, meta);
                        });
                        showSection[Section.UpToEleven] = AI.UpToEleven;
                    }
                    break;
                case ActiveAIType.VTOLAI:
                    {
                        var AI = ActiveAI as BDModuleVTOLAI;
                        if (AI == null)
                        {
                            Debug.LogError($"[BDArmory.BDArmoryAIGUI]: Mismatch between AI type and actual AI.");
                            activeAIType = ActiveAIType.None;
                            inputFields = null;
                            return;
                        }
                        inputFields = new List<string> {
                            nameof(AI.steerMult),
                            nameof(AI.steerKiAdjust),
                            nameof(AI.steerDamping),
                            nameof(AI.defaultAltitude),
                            nameof(AI.CombatAltitude),
                            nameof(AI.minAltitude),
                            nameof(AI.MaxSpeed),
                            nameof(AI.CombatSpeed),
                            nameof(AI.MaxPitchAngle),
                            nameof(AI.MaxBankAngle),
                            nameof(AI.WeaveFactor),
                            nameof(AI.MinEngagementRange),
                            nameof(AI.MaxEngagementRange),
                        }.ToDictionary(key => key, key =>
                        {
                            var (value, minValue, maxValue, meta) = GetAIFieldLimits(aiType, ActiveAI, key);
                            return gameObject.AddComponent<NumericInputField>().Initialise(0, value, minValue, maxValue, meta);
                        });
                        showSection[Section.UpToEleven] = AI.UpToEleven;
                    }
                    break;
                case ActiveAIType.OrbitalAI:
                    {
                        var AI = ActiveAI as BDModuleOrbitalAI;
                        if (AI == null)
                        {
                            Debug.LogError($"[BDArmory.BDArmoryAIGUI]: Mismatch between AI type and actual AI.");
                            activeAIType = ActiveAIType.None;
                            inputFields = null;
                            return;
                        }
                        inputFields = new List<string> {
                            nameof(AI.steerMult),
                            nameof(AI.steerKiAdjust),
                            nameof(AI.steerDamping),
                            nameof(AI.MinEngagementRange),
                            nameof(AI.ManeuverSpeed),
                            nameof(AI.firingSpeed),
                            nameof(AI.firingAngularVelocityLimit),
                            nameof(AI.minEvasionTime),
                            nameof(AI.evasionThreshold),
                            nameof(AI.evasionTimeThreshold),
                            nameof(AI.evasionErraticness),
                            nameof(AI.evasionMinRangeThreshold),
                            nameof(AI.collisionAvoidanceThreshold),
                            nameof(AI.vesselCollisionAvoidanceLookAheadPeriod),
                        }.ToDictionary(key => key, key =>
                        {
                            var (value, minValue, maxValue, meta) = GetAIFieldLimits(aiType, ActiveAI, key);
                            return gameObject.AddComponent<NumericInputField>().Initialise(0, value, minValue, maxValue, meta);
                        });
                    }
                    break;
                default:
                    inputFields = null;
                    break;
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
            }
            switch (activeAIType)
            {
                case ActiveAIType.PilotAI: SetInputFieldValues(ActiveAI as BDModulePilotAI, fromInputFields); break;
                case ActiveAIType.SurfaceAI: SetInputFieldValues(ActiveAI as BDModuleSurfaceAI, fromInputFields); break;
                case ActiveAIType.VTOLAI: SetInputFieldValues(ActiveAI as BDModuleVTOLAI, fromInputFields); break;
                case ActiveAIType.OrbitalAI: SetInputFieldValues(ActiveAI as BDModuleOrbitalAI, fromInputFields); break;
                default: return;
            }
        }

        void SetInputFieldValues<T>(T AI, bool fromInputFields) where T : BDGenericAIBase
        {
            if (AI == null) return;
            if (fromInputFields)
            {
                foreach (var field in inputFields.Keys)
                {
                    try
                    {
                        var fieldInfo = AI.GetType().GetField(field);
                        if (fieldInfo != null)
                        { fieldInfo.SetValue(AI, Convert.ChangeType(inputFields[field].currentValue, fieldInfo.FieldType)); }
                        else // Check if it's a property instead of a field.
                        {
                            var propInfo = AI.GetType().GetProperty(field);
                            propInfo.SetValue(AI, Convert.ChangeType(inputFields[field].currentValue, propInfo.PropertyType));
                        }
                    }
                    catch (Exception e) { Debug.LogError($"[BDArmory.BDArmoryAIGUI]: Failed to set current value of {field}: " + e.Message); }
                }
            }
            else
            {
                foreach (var field in inputFields.Keys)
                {
                    try
                    {
                        var fieldInfo = AI.GetType().GetField(field);
                        if (fieldInfo != null)
                        { inputFields[field].SetCurrentValue(Convert.ToDouble(fieldInfo.GetValue(AI))); }
                        else // Check if it's a property instead of a field.
                        {
                            var propInfo = AI.GetType().GetProperty(field);
                            inputFields[field].SetCurrentValue(Convert.ToDouble(propInfo.GetValue(AI)));
                        }
                    }
                    catch (Exception e) { Debug.LogError($"[BDArmory.BDArmoryAIGUI]: Failed to set current value of {field}: " + e.Message + "\n" + e.StackTrace); }
                }
            }
        }

        public void SetChooseOptionSliders()
        {
            if (ActiveAI == null) return;
            switch (activeAIType)
            {
                case ActiveAIType.SurfaceAI:
                    {
                        var AI = ActiveAI as BDModuleSurfaceAI;
                        Drivertype = VehicleMovementTypes.IndexOf(AI.SurfaceType);
                        broadsideDir = AI.orbitDirections.IndexOf(AI.OrbitDirectionName);
                    }
                    break;
                case ActiveAIType.VTOLAI:
                    {
                        var AI = ActiveAI as BDModuleVTOLAI;
                        broadsideDir = AI.orbitDirections.IndexOf(AI.OrbitDirectionName);
                    }
                    break;
                case ActiveAIType.OrbitalAI:
                    {
                        var AI = ActiveAI as BDModuleOrbitalAI;
                        pidMode = AI.pidModes.IndexOf(AI.pidMode);
                        rollTowards = AI.rollTowardsModes.IndexOf(AI.rollTowards);
                    }
                    break;
            }
        }

        #region GUI

        void OnGUI()
        {
            if (!BDArmorySetup.GAME_UI_ENABLED) return;

            if (!windowBDAAIGUIEnabled || (!HighLogic.LoadedSceneIsFlight && !HighLogic.LoadedSceneIsEditor)) return;
            if (!stylesConfigured) ConfigureStyles();
            if (HighLogic.LoadedSceneIsFlight) BDArmorySetup.SetGUIOpacity();
            if (resizingWindow && Event.current.type == EventType.MouseUp) { resizingWindow = false; }
            if (BDArmorySettings.UI_SCALE != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE * Vector2.one, BDArmorySetup.WindowRectAI.position);
            BDArmorySetup.WindowRectAI = GUI.Window(GUIUtility.GetControlID(FocusType.Passive), BDArmorySetup.WindowRectAI, WindowRectAI, "", BDArmorySetup.BDGuiSkin.window);//"BDA Weapon Manager"
            if (HighLogic.LoadedSceneIsFlight) BDArmorySetup.SetGUIOpacity(false);
        }

        void ConfigureStyles()
        {
            Label = new GUIStyle();
            Label.alignment = TextAnchor.UpperLeft;
            Label.normal.textColor = Color.white;

            contextLabelRight = new GUIStyle();
            contextLabelRight.alignment = TextAnchor.UpperRight;
            contextLabelRight.normal.textColor = Color.white;

            contextLabel = new GUIStyle(Label);

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

        enum Section { UpToEleven, PID, Altitude, Speed, Control, Evasion, Terrain, Ramming, Combat, Misc, FixedAutoTuneFields, VehicleType }; // Sections and other important toggles.
        readonly Dictionary<Section, bool> showSection = Enum.GetValues(typeof(Section)).Cast<Section>().ToDictionary(s => s, s => false);
        readonly Dictionary<Section, float> sectionHeights = [];
        const float contentBorder = 0.2f * entryHeight;
        const float contentMargin = 10;
        const float contentInnerMargin = contentMargin + contentBorder;
        const float columnIndent = 100;
        const float labelWidth = 200;
        const float sliderIndent = contentInnerMargin + labelWidth;

        Rect TitleButtonRect(float offset)
        {
            return new Rect((ColumnWidth * 2) - _windowMargin - (offset * _buttonSize), _windowMargin, _buttonSize, _buttonSize);
        }
        Rect SubsectionRect(float line)
        {
            return new Rect(contentMargin, contentTop + line * entryHeight, columnIndent, entryHeight);
        }
        Rect SettinglabelRect(float lines)
        {
            return new Rect(contentInnerMargin, lines * entryHeight, labelWidth, entryHeight);
        }
        Rect SettingSliderRect(float lines, float contentWidth)
        {
            return new Rect(sliderIndent, (lines + 0.2f) * entryHeight, contentWidth - 2 * contentMargin - labelWidth, entryHeight);
        }
        Rect SettingTextRect(float lines, float contentWidth)
        {
            return new Rect(sliderIndent, lines * entryHeight, contentWidth - 2 * contentMargin - labelWidth, entryHeight);
        }
        Rect ContextLabelRect(float lines, float contentWidth) => SettingTextRect(lines, contentWidth);
        Rect ContextLabelRect(float lines)
        {
            return new Rect(sliderIndent, lines * entryHeight, 100, entryHeight);
        }
        Rect ContextLabelRectRight(float lines, float contentWidth)
        {
            return new Rect(contentWidth - columnIndent - 2 * contentMargin, lines * entryHeight, columnIndent, entryHeight);
        }

        Rect ToggleButtonRect(float lines, float contentWidth)
        {
            return new Rect(contentInnerMargin, lines * entryHeight, contentWidth - 2 * contentInnerMargin, entryHeight);
        }

        Rect ToggleButtonRects(float lines, float pos, float of, float contentWidth)
        {
            var gap = contentInnerMargin / 2f;
            return new Rect(contentInnerMargin + pos / of * (contentWidth - gap * (of - 1f) - 2f * contentInnerMargin) + pos * gap, lines * entryHeight, 1f / of * (contentWidth - gap * (of - 1f) - 2f * contentInnerMargin), entryHeight);
        }

        enum ContentType { FloatSlider, SemiLogSlider, Toggle, Button };
        float ContentEntry(ContentType contentType, float line, float width, ref float value, string fieldName, string baseLOC, string formattedValue, bool splitContext = false)
        {
            switch (contentType)
            {
                case ContentType.FloatSlider:
                    {
                        GUI.Label(SettinglabelRect(line), StringUtils.Localize($"#LOC_BDArmory_AIWindow_{baseLOC}") + ": " + formattedValue, Label);
                        if (!NumFieldsEnabled)
                        {
                            var (min, max, rounding, _, _) = GetFieldLimits(fieldName);
                            if (fieldName == "firingSpeed") Debug.Log($"DEBUG min: {min}, max: {max}, rounding: {rounding}");
                            if (value != (value = GUI.HorizontalSlider(SettingSliderRect(line, width), value, min, max)) && rounding > 0)
                                value = BDAMath.RoundToUnit(value, rounding);
                        }
                        else
                        {
                            var field = inputFields[fieldName];
                            field.tryParseValue(GUI.TextField(SettingTextRect(line, width), field.possibleValue, 8, field.style));
                            value = (float)field.currentValue;
                        }
                        if (contextTipsEnabled)
                        {
                            if (splitContext)
                            {
                                GUI.Label(ContextLabelRect(++line), StringUtils.Localize($"#LOC_BDArmory_AIWindow_{baseLOC}_ContextLow"), Label);
                                GUI.Label(ContextLabelRectRight(line, width), StringUtils.Localize($"#LOC_BDArmory_AIWindow_{baseLOC}_ContextHigh"), contextLabelRight);
                            }
                            else GUI.Label(ContextLabelRect(++line, width), StringUtils.Localize($"#LOC_BDArmory_AIWindow_{baseLOC}_Context"), contextLabel);
                        }
                        ++line;
                    }
                    break;
                case ContentType.SemiLogSlider:
                    {
                        GUI.Label(SettinglabelRect(line), StringUtils.Localize($"#LOC_BDArmory_AIWindow_{baseLOC}") + ": " + formattedValue, Label);
                        if (!NumFieldsEnabled)
                        {
                            var (min, max, rounding, sigFig, withZero) = GetFieldLimits(fieldName);
                            if (!cacheSemiLogLimits.ContainsKey(fieldName)) { cacheSemiLogLimits[fieldName] = null; }
                            var cache = cacheSemiLogLimits[fieldName];
                            if (value != (value = GUIUtils.HorizontalSemiLogSlider(SettingSliderRect(line, width), value, min, max, sigFig, withZero, ref cache)) && rounding > 0)
                                value = BDAMath.RoundToUnit(value, rounding);
                        }
                        else
                        {
                            var field = inputFields[fieldName];
                            field.tryParseValue(GUI.TextField(SettingTextRect(line, width), field.possibleValue, 8, field.style));
                            value = (float)field.currentValue;
                        }
                        if (contextTipsEnabled)
                        {
                            if (splitContext)
                            {
                                GUI.Label(ContextLabelRect(++line), StringUtils.Localize($"#LOC_BDArmory_AIWindow_{baseLOC}_ContextLow"), Label);
                                GUI.Label(ContextLabelRectRight(line, width), StringUtils.Localize($"#LOC_BDArmory_AIWindow_{baseLOC}_ContextHigh"), contextLabelRight);
                            }
                            else GUI.Label(ContextLabelRect(++line, width), StringUtils.Localize($"#LOC_BDArmory_AIWindow_{baseLOC}_Context"), contextLabel);
                        }
                        ++line;
                    }
                    break;
                case ContentType.Toggle:
                    {
                        line += 1.25f;
                    }
                    break;
                case ContentType.Button:
                    {
                        line += 1.25f;
                    }
                    break;
            }
            return line;
        }
        readonly Dictionary<string, (float, float)[]> cacheSemiLogLimits = [];
        void WindowRectAI(int windowID)
        {
            float windowColumns = 2;
            float contentIndent = contentMargin + columnIndent;
            float contentWidth = 2 * ColumnWidth - 2 * contentMargin - columnIndent;

            GUI.DragWindow(new Rect(_windowMargin + _buttonSize * 6, 0, 2 * ColumnWidth - 2 * _windowMargin - 10 * _buttonSize, _windowMargin + _buttonSize));

            GUI.Label(new Rect(100, contentTop, contentWidth, entryHeight), StringUtils.Localize("#LOC_BDArmory_AIWindow_title"), Title);

            if (GUI.Button(TitleButtonRect(1), "X", windowBDAAIGUIEnabled ? BDArmorySetup.BDGuiSkin.button : BDArmorySetup.BDGuiSkin.box)) //Exit Button
            { ToggleAIGUI(); }
            if (GUI.Button(TitleButtonRect(2), "i", infoLinkEnabled ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button)) //Infolink button
            { infoLinkEnabled = !infoLinkEnabled; }
            if (GUI.Button(TitleButtonRect(3), "?", contextTipsEnabled ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button)) //Context labels button
            { contextTipsEnabled = !contextTipsEnabled; }
            if (GUI.Button(TitleButtonRect(4), "#", NumFieldsEnabled ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button)) //Numeric fields button
            {
                NumFieldsEnabled = !NumFieldsEnabled;
                SyncInputFieldsNow(!NumFieldsEnabled);
            }

            if (activeAIType == ActiveAIType.None || ActiveAI == null)
            {
                GUI.Label(new Rect(contentMargin, contentTop + (1.75f * entryHeight), contentWidth, entryHeight),
                   StringUtils.Localize("#LOC_BDArmory_AIWindow_NoAI"), Title);// "No AI found."
            }
            else
            {
                height = Mathf.Lerp(height, contentHeight, 0.15f);
                contentHeight = 0;
                switch (activeAIType)
                {
                    case ActiveAIType.PilotAI:
                        {
                            var AI = ActiveAI as BDModulePilotAI;
                            if (AI == null) { Debug.LogError($"[BDArmory.BDArmoryAIGUI]: AI module mismatch!"); activeAIType = ActiveAIType.None; break; }

                            { // Section buttons
                                GUIStyle saveStyle = BDArmorySetup.BDGuiSkin.button;
                                if (GUI.Button(new Rect(_windowMargin, _windowMargin, _buttonSize * 3, _buttonSize), "Save", saveStyle))
                                {
                                    AI.StoreSettings();
                                }

                                if (AI.Events["RestoreSettings"].active == true)
                                {
                                    GUIStyle restoreStyle = BDArmorySetup.BDGuiSkin.button;
                                    if (GUI.Button(new Rect(_windowMargin + _buttonSize * 3, _windowMargin, _buttonSize * 3, _buttonSize), "Restore", restoreStyle))
                                    {
                                        AI.RestoreSettings();
                                    }
                                }

                                float line = 1.5f;
                                showSection[Section.PID] = GUI.Toggle(SubsectionRect(line), showSection[Section.PID], StringUtils.Localize("#LOC_BDArmory_AIWindow_PID"), showSection[Section.PID] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"PiD"

                                line += 1.5f;
                                showSection[Section.Altitude] = GUI.Toggle(SubsectionRect(line), showSection[Section.Altitude], StringUtils.Localize("#LOC_BDArmory_AIWindow_Altitudes"), showSection[Section.Altitude] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Altitude"

                                line += 1.5f;
                                showSection[Section.Speed] = GUI.Toggle(SubsectionRect(line), showSection[Section.Speed], StringUtils.Localize("#LOC_BDArmory_AIWindow_Speeds"), showSection[Section.Speed] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Speed"

                                line += 1.5f;
                                showSection[Section.Control] = GUI.Toggle(SubsectionRect(line), showSection[Section.Control], StringUtils.Localize("#LOC_BDArmory_AIWindow_Control"), showSection[Section.Control] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Control"

                                line += 1.5f;
                                showSection[Section.Evasion] = GUI.Toggle(SubsectionRect(line), showSection[Section.Evasion], StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeExtend"), showSection[Section.Evasion] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Evasion"

                                line += 1.5f;
                                showSection[Section.Terrain] = GUI.Toggle(SubsectionRect(line), showSection[Section.Terrain], StringUtils.Localize("#LOC_BDArmory_AIWindow_Terrain"), showSection[Section.Terrain] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Terrain"

                                line += 1.5f;
                                showSection[Section.Ramming] = GUI.Toggle(SubsectionRect(line), showSection[Section.Ramming], StringUtils.Localize("#LOC_BDArmory_AIWindow_Ramming"), showSection[Section.Ramming] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Ramming"

                                line += 1.5f;
                                showSection[Section.Misc] = GUI.Toggle(SubsectionRect(line), showSection[Section.Misc], StringUtils.Localize("#LOC_BDArmory_AIWindow_Misc"), showSection[Section.Misc] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Misc"

                                line += 1.5f;
                                if (showSection[Section.UpToEleven] != (AI.UpToEleven = GUI.Toggle(SubsectionRect(line), AI.UpToEleven,
                                    AI.UpToEleven ? StringUtils.Localize("#LOC_BDArmory_AI_UnclampTuning_enabledText") : StringUtils.Localize("#LOC_BDArmory_AI_UnclampTuning_disabledText"),
                                    AI.UpToEleven ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button)))//"Misc"
                                {
                                    SetInputFields(activeAIType);
                                }
                            }

                            if (showSection[Section.PID] || showSection[Section.Altitude] || showSection[Section.Speed] || showSection[Section.Control] || showSection[Section.Evasion] || showSection[Section.Terrain] || showSection[Section.Ramming] || showSection[Section.Misc])
                            {
                                scrollViewVectors[ActiveAIType.PilotAI] = GUI.BeginScrollView(new Rect(contentIndent, contentTop + entryHeight * 1.5f, ColumnWidth * 2 - contentIndent, WindowHeight - entryHeight * 1.5f - 2 * contentTop), scrollViewVectors.GetValueOrDefault(ActiveAIType.PilotAI), new Rect(0, 0, contentWidth - contentMargin * 2, height + contentTop));

                                GUI.BeginGroup(new Rect(contentMargin, 0, contentWidth - contentMargin * 2, height + 2 * contentBorder), GUIContent.none, BDArmorySetup.BDGuiSkin.box); //darker box

                                contentWidth -= 24 + contentBorder;

                                if (showSection[Section.PID])
                                {
                                    float pidLines = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.PID);
                                    GUI.BeginGroup(new Rect(contentBorder, pidLines * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    pidLines += 0.25f;

                                    GUI.Label(SettinglabelRect(pidLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_PID"), BoldLabel);//"Pid Controller"
                                    pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.steerMult, nameof(AI.steerMult), "SteerPower", $"{AI.steerMult:0.0}", splitContext: true);
                                    pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.steerKiAdjust, nameof(AI.steerKiAdjust), "SteerKi", $"{AI.steerKiAdjust:0.00}", splitContext: true);
                                    pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.steerDamping, nameof(AI.steerDamping), "SteerDamping", $"{AI.steerDamping:0.00}", splitContext: true);

                                    AI.dynamicSteerDamping = GUI.Toggle(ToggleButtonRect(pidLines, contentWidth), AI.dynamicSteerDamping, StringUtils.Localize("#LOC_BDArmory_AI_DynamicDamping"), AI.dynamicSteerDamping ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Dynamic damping"
                                    pidLines += 1.25f;

                                    if (AI.dynamicSteerDamping)
                                    {
                                        AI.CustomDynamicAxisFields = GUI.Toggle(ToggleButtonRect(pidLines, contentWidth), AI.CustomDynamicAxisFields, StringUtils.Localize("#LOC_BDArmory_AI_3AxisDynamicSteerDamping"), AI.CustomDynamicAxisFields ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"3 axis damping"
                                        pidLines += 1.25f;

                                        if (!AI.CustomDynamicAxisFields)
                                        {
                                            GUI.Label(SettinglabelRect(pidLines++), StringUtils.Localize("#LOC_BDArmory_AI_DynamicDamping") + $": {AI.dynSteerDampingValue}", Label);
                                            pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.DynamicDampingMin, nameof(AI.DynamicDampingMin), "DynDampMin", $"{AI.DynamicDampingMin:0.0}");
                                            pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.DynamicDampingMax, nameof(AI.DynamicDampingMax), "DynDampMax", $"{AI.DynamicDampingMax:0.0}");
                                            pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.dynamicSteerDampingFactor, nameof(AI.dynamicSteerDampingFactor), "DynDampMult", $"{AI.dynamicSteerDampingFactor:0.0}");
                                        }
                                        else
                                        {
                                            AI.dynamicDampingPitch = GUI.Toggle(ToggleButtonRect(pidLines, contentWidth), AI.dynamicDampingPitch, StringUtils.Localize("#LOC_BDArmory_AI_DynamicDampingPitch"), AI.dynamicDampingPitch ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Dynamic damp pitch"
                                            pidLines += 1.25f;
                                            if (AI.dynamicDampingPitch)
                                            {
                                                GUI.Label(SettinglabelRect(pidLines++), StringUtils.Localize("#LOC_BDArmory_AI_DynamicDampingPitch") + $": {AI.dynSteerDampingPitchValue}", Label);
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.DynamicDampingPitchMin, nameof(AI.DynamicDampingPitchMin), "DynDampMin", $"{AI.DynamicDampingPitchMin:0.0}");
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.DynamicDampingPitchMax, nameof(AI.DynamicDampingPitchMax), "DynDampMax", $"{AI.DynamicDampingPitchMax:0.0}");
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.dynamicSteerDampingPitchFactor, nameof(AI.dynamicSteerDampingPitchFactor), "DynDampMult", $"{AI.dynamicSteerDampingPitchFactor:0.0}");
                                            }

                                            AI.dynamicDampingYaw = GUI.Toggle(ToggleButtonRect(pidLines, contentWidth), AI.dynamicDampingYaw, StringUtils.Localize("#LOC_BDArmory_AI_DynamicDampingYaw"), AI.dynamicDampingYaw ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Dynamic damp yaw"
                                            pidLines += 1.25f;
                                            if (AI.dynamicDampingYaw)
                                            {
                                                GUI.Label(SettinglabelRect(pidLines++), StringUtils.Localize("#LOC_BDArmory_AI_DynamicDampingYaw") + $": {AI.dynSteerDampingYawValue}", Label);
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.DynamicDampingYawMin, nameof(AI.DynamicDampingYawMin), "DynDampMin", $"{AI.DynamicDampingYawMin:0.0}");
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.DynamicDampingYawMax, nameof(AI.DynamicDampingYawMax), "DynDampMax", $"{AI.DynamicDampingYawMax:0.0}");
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.dynamicSteerDampingYawFactor, nameof(AI.dynamicSteerDampingYawFactor), "DynDampMult", $"{AI.dynamicSteerDampingYawFactor:0.0}");
                                            }

                                            AI.dynamicDampingRoll = GUI.Toggle(ToggleButtonRect(pidLines, contentWidth), AI.dynamicDampingRoll, StringUtils.Localize("#LOC_BDArmory_AI_DynamicDampingRoll"), AI.dynamicDampingRoll ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Dynamic damp roll"
                                            pidLines += 1.25f;
                                            if (AI.dynamicDampingRoll)
                                            {
                                                GUI.Label(SettinglabelRect(pidLines++), StringUtils.Localize("#LOC_BDArmory_AI_DynamicDampingRoll") + $": {AI.dynSteerDampingRollValue}", Label);//"dynamic damp roll"
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.DynamicDampingRollMin, nameof(AI.DynamicDampingRollMin), "DynDampMin", $"{AI.DynamicDampingRollMin:0.0}");
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.DynamicDampingRollMax, nameof(AI.DynamicDampingRollMax), "DynDampMax", $"{AI.DynamicDampingRollMax:0.0}");
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.dynamicSteerDampingRollFactor, nameof(AI.dynamicSteerDampingRollFactor), "DynDampMult", $"{AI.dynamicSteerDampingRollFactor:0.0}");
                                            }
                                        }
                                    }

                                    #region AutoTune
                                    if (AI.AutoTune != GUI.Toggle(ToggleButtonRect(pidLines, contentWidth), AI.AutoTune, StringUtils.Localize("#LOC_BDArmory_AI_PID_AutoTune"), AI.AutoTune ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
                                    {
                                        AI.AutoTune = !AI.AutoTune; // Only actually toggle it when needed as the setter does extra stuff.
                                    }
                                    pidLines += 1.25f;
                                    if (AI.AutoTune) // Auto-tuning
                                    {
                                        pidLines += 0.25f;
                                        GUI.Label(SettinglabelRect(pidLines++), StringUtils.Localize("#LOC_BDArmory_AI_PID_AutoTuning_Loss") + $": {AI.autoTuningLossLabel}", Label);
                                        GUI.Label(SettinglabelRect(pidLines++), $"\tParams: {AI.autoTuningLossLabel2}", Label);
                                        GUI.Label(SettinglabelRect(pidLines++), $"\tField: {AI.autoTuningLossLabel3}", Label);

                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.autoTuningOptionNumSamples, nameof(AI.autoTuningOptionNumSamples), "PIDAutoTuningNumSamples", $"{AI.autoTuningOptionNumSamples:0}", splitContext: true);
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.autoTuningOptionFastResponseRelevance, nameof(AI.autoTuningOptionFastResponseRelevance), "PIDAutoTuningFastResponseRelevance", $"{AI.autoTuningOptionFastResponseRelevance:G3}", splitContext: true);
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.autoTuningOptionInitialLearningRate, nameof(AI.autoTuningOptionInitialLearningRate), "PIDAutoTuningInitialLearningRate", $"{AI.autoTuningOptionInitialLearningRate:G3}");
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.autoTuningOptionInitialRollRelevance, nameof(AI.autoTuningOptionInitialRollRelevance), "PIDAutoTuningInitialRollRelevance", $"{AI.autoTuningOptionInitialRollRelevance:G3}");
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.autoTuningAltitude, nameof(AI.autoTuningAltitude), "PIDAutoTuningAltitude", $"{AI.autoTuningAltitude:0}");
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.autoTuningSpeed, nameof(AI.autoTuningSpeed), "PIDAutoTuningSpeed", $"{AI.autoTuningSpeed:0}");
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.autoTuningRecenteringDistance, nameof(AI.autoTuningRecenteringDistance), "PIDAutoTuningRecenteringDistance", $"{AI.autoTuningRecenteringDistance:0}km");

                                        showSection[Section.FixedAutoTuneFields] = GUI.Toggle(ToggleButtonRects(pidLines, 0, 2, contentWidth), showSection[Section.FixedAutoTuneFields], StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixedFields"), showSection[Section.FixedAutoTuneFields] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                                        AI.autoTuningOptionClampMaximums = GUI.Toggle(ToggleButtonRects(pidLines, 1, 2, contentWidth), AI.autoTuningOptionClampMaximums, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningClampMaximums"), AI.autoTuningOptionClampMaximums ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                                        pidLines += 1.25f;

                                        if (showSection[Section.FixedAutoTuneFields])
                                        {
                                            bool resetGradient = false;
                                            if (!AI.dynamicSteerDamping)
                                            {
                                                if (AI.autoTuningOptionFixedP != (AI.autoTuningOptionFixedP = GUI.Toggle(ToggleButtonRects(pidLines, 0, 3, contentWidth), AI.autoTuningOptionFixedP, StringUtils.Localize("P"), AI.autoTuningOptionFixedP ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                                if (AI.autoTuningOptionFixedI != (AI.autoTuningOptionFixedI = GUI.Toggle(ToggleButtonRects(pidLines, 1, 3, contentWidth), AI.autoTuningOptionFixedI, StringUtils.Localize("I"), AI.autoTuningOptionFixedI ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                                if (AI.autoTuningOptionFixedD != (AI.autoTuningOptionFixedD = GUI.Toggle(ToggleButtonRects(pidLines, 2, 3, contentWidth), AI.autoTuningOptionFixedD, StringUtils.Localize("D"), AI.autoTuningOptionFixedD ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                            }
                                            else if (!AI.CustomDynamicAxisFields)
                                            {
                                                if (AI.autoTuningOptionFixedP != (AI.autoTuningOptionFixedP = GUI.Toggle(ToggleButtonRects(pidLines, 0, 5, contentWidth), AI.autoTuningOptionFixedP, StringUtils.Localize("P"), AI.autoTuningOptionFixedP ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                                if (AI.autoTuningOptionFixedI != (AI.autoTuningOptionFixedI = GUI.Toggle(ToggleButtonRects(pidLines, 1, 5, contentWidth), AI.autoTuningOptionFixedI, StringUtils.Localize("I"), AI.autoTuningOptionFixedI ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                                if (AI.autoTuningOptionFixedDOff != (AI.autoTuningOptionFixedDOff = GUI.Toggle(ToggleButtonRects(pidLines, 2, 5, contentWidth), AI.autoTuningOptionFixedDOff, StringUtils.Localize("DOff"), AI.autoTuningOptionFixedDOff ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                                if (AI.autoTuningOptionFixedDOn != (AI.autoTuningOptionFixedDOn = GUI.Toggle(ToggleButtonRects(pidLines, 3, 5, contentWidth), AI.autoTuningOptionFixedDOn, StringUtils.Localize("DOn"), AI.autoTuningOptionFixedDOn ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                                if (AI.autoTuningOptionFixedDF != (AI.autoTuningOptionFixedDF = GUI.Toggle(ToggleButtonRects(pidLines, 4, 5, contentWidth), AI.autoTuningOptionFixedDF, StringUtils.Localize("DF"), AI.autoTuningOptionFixedDF ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                            }
                                            else
                                            {
                                                if (AI.autoTuningOptionFixedP != (AI.autoTuningOptionFixedP = GUI.Toggle(ToggleButtonRects(pidLines, 0, 11, contentWidth), AI.autoTuningOptionFixedP, StringUtils.Localize("P"), AI.autoTuningOptionFixedP ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                                if (AI.autoTuningOptionFixedI != (AI.autoTuningOptionFixedI = GUI.Toggle(ToggleButtonRects(pidLines, 1, 11, contentWidth), AI.autoTuningOptionFixedI, StringUtils.Localize("I"), AI.autoTuningOptionFixedI ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                                if (AI.autoTuningOptionFixedDPOff != (AI.autoTuningOptionFixedDPOff = GUI.Toggle(ToggleButtonRects(pidLines, 2, 11, contentWidth), AI.autoTuningOptionFixedDPOff, StringUtils.Localize("DPOff"), AI.autoTuningOptionFixedDPOff ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                                if (AI.autoTuningOptionFixedDPOn != (AI.autoTuningOptionFixedDPOn = GUI.Toggle(ToggleButtonRects(pidLines, 3, 11, contentWidth), AI.autoTuningOptionFixedDPOn, StringUtils.Localize("DPOn"), AI.autoTuningOptionFixedDPOn ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                                if (AI.autoTuningOptionFixedDPF != (AI.autoTuningOptionFixedDPF = GUI.Toggle(ToggleButtonRects(pidLines, 4, 11, contentWidth), AI.autoTuningOptionFixedDPF, StringUtils.Localize("DPF"), AI.autoTuningOptionFixedDPF ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                                if (AI.autoTuningOptionFixedDYOff != (AI.autoTuningOptionFixedDYOff = GUI.Toggle(ToggleButtonRects(pidLines, 5, 11, contentWidth), AI.autoTuningOptionFixedDYOff, StringUtils.Localize("DYOff"), AI.autoTuningOptionFixedDYOff ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                                if (AI.autoTuningOptionFixedDYOn != (AI.autoTuningOptionFixedDYOn = GUI.Toggle(ToggleButtonRects(pidLines, 6, 11, contentWidth), AI.autoTuningOptionFixedDYOn, StringUtils.Localize("DYOn"), AI.autoTuningOptionFixedDYOn ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                                if (AI.autoTuningOptionFixedDYF != (AI.autoTuningOptionFixedDYF = GUI.Toggle(ToggleButtonRects(pidLines, 7, 11, contentWidth), AI.autoTuningOptionFixedDYF, StringUtils.Localize("DYF"), AI.autoTuningOptionFixedDYF ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                                if (AI.autoTuningOptionFixedDROff != (AI.autoTuningOptionFixedDROff = GUI.Toggle(ToggleButtonRects(pidLines, 8, 11, contentWidth), AI.autoTuningOptionFixedDROff, StringUtils.Localize("DROff"), AI.autoTuningOptionFixedDROff ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                                if (AI.autoTuningOptionFixedDROn != (AI.autoTuningOptionFixedDROn = GUI.Toggle(ToggleButtonRects(pidLines, 9, 11, contentWidth), AI.autoTuningOptionFixedDROn, StringUtils.Localize("DROn"), AI.autoTuningOptionFixedDROn ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                                if (AI.autoTuningOptionFixedDRF != (AI.autoTuningOptionFixedDRF = GUI.Toggle(ToggleButtonRects(pidLines, 10, 11, contentWidth), AI.autoTuningOptionFixedDRF, StringUtils.Localize("DRF"), AI.autoTuningOptionFixedDRF ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))) resetGradient = true;
                                            }
                                            if (resetGradient && HighLogic.LoadedSceneIsFlight) AI.pidAutoTuning.ResetGradient();
                                            pidLines += 1.25f;
                                        }
                                    }
                                    else if (!string.IsNullOrEmpty(AI.autoTuningLossLabel)) // Not auto-tuning, but have been previously => show a summary of the last results.
                                    {
                                        GUI.Label(new Rect(contentInnerMargin + labelWidth / 6, pidLines * entryHeight, labelWidth, entryHeight),
                                            StringUtils.Localize("#LOC_BDArmory_AI_PID_AutoTuning_Summary") + $":   Loss: {AI.autoTuningLossLabel}, {AI.autoTuningLossLabel2}", Label);
                                        pidLines += 1.25f;
                                    }
                                    #endregion

                                    GUI.EndGroup();
                                    sectionHeights[Section.PID] = Mathf.Lerp(sectionHeight, pidLines, 0.15f);
                                    pidLines += 0.1f;
                                    contentHeight += pidLines * entryHeight;
                                }
                                if (showSection[Section.Altitude])
                                {
                                    float altLines = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Altitude);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + altLines * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    altLines += 0.25f;

                                    GUI.Label(SettinglabelRect(altLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Altitudes"), BoldLabel);//"Altitudes"

                                    var oldDefaultAlt = AI.defaultAltitude;
                                    altLines = ContentEntry(ContentType.FloatSlider, altLines, contentWidth, ref AI.defaultAltitude, nameof(AI.defaultAltitude), "DefaultAltitude", $"{AI.defaultAltitude:0}m");
                                    if (AI.defaultAltitude != oldDefaultAlt)
                                    {
                                        AI.ClampFields("defaultAltitude");
                                        inputFields["minAltitude"].SetCurrentValue(AI.minAltitude);
                                        inputFields["maxAltitude"].SetCurrentValue(AI.maxAltitude);
                                    }

                                    var oldMinAlt = AI.minAltitude;
                                    altLines = ContentEntry(ContentType.FloatSlider, altLines, contentWidth, ref AI.minAltitude, nameof(AI.minAltitude), "MinAltitude", $"{AI.minAltitude:0}m");
                                    if (AI.minAltitude != oldMinAlt)
                                    {
                                        AI.ClampFields("minAltitude");
                                        inputFields["defaultAltitude"].SetCurrentValue(AI.defaultAltitude);
                                        inputFields["maxAltitude"].SetCurrentValue(AI.maxAltitude);
                                    }

                                    AI.hardMinAltitude = GUI.Toggle(ToggleButtonRects(altLines, 0, 2, contentWidth), AI.hardMinAltitude,
                                        StringUtils.Localize("#LOC_BDArmory_AI_HardMinAltitude"), AI.hardMinAltitude ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Hard Min Altitude"
                                    AI.maxAltitudeToggle = GUI.Toggle(ToggleButtonRects(altLines, 1, 2, contentWidth), AI.maxAltitudeToggle,
                                        StringUtils.Localize("#LOC_BDArmory_AIWindow_MaxAltitude"), AI.maxAltitudeToggle ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"max altitude AGL"
                                    altLines += 1.25f;

                                    if (AI.maxAltitudeToggle)
                                    {
                                        var oldMaxAlt = AI.maxAltitude;
                                        altLines = ContentEntry(ContentType.FloatSlider, altLines, contentWidth, ref AI.maxAltitude, nameof(AI.maxAltitude), "MaxAltitude", $"{AI.maxAltitude:0}m");
                                        if (AI.maxAltitude != oldMaxAlt)
                                        {
                                            AI.ClampFields("maxAltitude");
                                            inputFields["minAltitude"].SetCurrentValue(AI.minAltitude);
                                            inputFields["defaultAltitude"].SetCurrentValue(AI.defaultAltitude);
                                        }
                                    }

                                    GUI.EndGroup();
                                    sectionHeights[Section.Altitude] = Mathf.Lerp(sectionHeight, altLines, 0.15f);
                                    altLines += 0.1f;
                                    contentHeight += altLines * entryHeight;
                                }
                                if (showSection[Section.Speed])
                                {
                                    float spdLines = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Speed);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + spdLines * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    spdLines += 0.25f;

                                    GUI.Label(SettinglabelRect(spdLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Speeds"), BoldLabel);//"Speed"
                                    spdLines = ContentEntry(ContentType.FloatSlider, spdLines, contentWidth, ref AI.maxSpeed, nameof(AI.maxSpeed), "MaxSpeed", $"{AI.maxSpeed:0}m/s");
                                    spdLines = ContentEntry(ContentType.FloatSlider, spdLines, contentWidth, ref AI.takeOffSpeed, nameof(AI.takeOffSpeed), "TakeOffSpeed", $"{AI.takeOffSpeed:0}m/s");
                                    spdLines = ContentEntry(ContentType.FloatSlider, spdLines, contentWidth, ref AI.minSpeed, nameof(AI.minSpeed), "MinSpeed", $"{AI.minSpeed:0}m/s");
                                    spdLines = ContentEntry(ContentType.FloatSlider, spdLines, contentWidth, ref AI.strafingSpeed, nameof(AI.strafingSpeed), "StrafingSpeed", $"{AI.strafingSpeed:0}m/s");
                                    spdLines = ContentEntry(ContentType.FloatSlider, spdLines, contentWidth, ref AI.idleSpeed, nameof(AI.idleSpeed), "IdleSpeed", $"{AI.idleSpeed:0}m/s");
                                    spdLines = ContentEntry(ContentType.FloatSlider, spdLines, contentWidth, ref AI.ABPriority, nameof(AI.ABPriority), "ABPriority", $"{AI.ABPriority:0}%");
                                    spdLines = ContentEntry(ContentType.FloatSlider, spdLines, contentWidth, ref AI.ABOverrideThreshold, nameof(AI.ABOverrideThreshold), "ABOverrideThreshold", $"{AI.ABOverrideThreshold:0}m/s");
                                    spdLines = ContentEntry(ContentType.FloatSlider, spdLines, contentWidth, ref AI.brakingPriority, nameof(AI.brakingPriority), "BrakingPriority", $"{AI.brakingPriority:0}%");

                                    GUI.EndGroup();
                                    sectionHeights[Section.Speed] = Mathf.Lerp(sectionHeight, spdLines, 0.15f);
                                    spdLines += 0.1f;
                                    contentHeight += spdLines * entryHeight;
                                }
                                if (showSection[Section.Control])
                                {
                                    float ctrlLines = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Control);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + ctrlLines * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    ctrlLines += 0.25f;

                                    GUI.Label(SettinglabelRect(ctrlLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Control"), BoldLabel);//"Control"
                                    ctrlLines = ContentEntry(ContentType.FloatSlider, ctrlLines, contentWidth, ref AI.maxSteer, nameof(AI.maxSteer), "LowSpeedSteerLimiter", $"{AI.maxSteer:0.00}");
                                    ctrlLines = ContentEntry(ContentType.FloatSlider, ctrlLines, contentWidth, ref AI.lowSpeedSwitch, nameof(AI.lowSpeedSwitch), "LowSpeedLimiterSpeed", $"{AI.lowSpeedSwitch:0}m/s");
                                    ctrlLines = ContentEntry(ContentType.FloatSlider, ctrlLines, contentWidth, ref AI.maxSteerAtMaxSpeed, nameof(AI.maxSteerAtMaxSpeed), "HighSpeedSteerLimiter", $"{AI.maxSteerAtMaxSpeed:0.00}");
                                    ctrlLines = ContentEntry(ContentType.FloatSlider, ctrlLines, contentWidth, ref AI.cornerSpeed, nameof(AI.cornerSpeed), "HighSpeedLimiterSpeed", $"{AI.cornerSpeed:0}m/s");
                                    ctrlLines = ContentEntry(ContentType.FloatSlider, ctrlLines, contentWidth, ref AI.altitudeSteerLimiterFactor, nameof(AI.altitudeSteerLimiterFactor), "AltitudeSteerLimiterFactor", $"{AI.altitudeSteerLimiterFactor:0}");
                                    ctrlLines = ContentEntry(ContentType.FloatSlider, ctrlLines, contentWidth, ref AI.altitudeSteerLimiterAltitude, nameof(AI.altitudeSteerLimiterAltitude), "AltitudeSteerLimiterAltitude", $"{AI.altitudeSteerLimiterAltitude:0}m");
                                    ctrlLines = ContentEntry(ContentType.FloatSlider, ctrlLines, contentWidth, ref AI.maxBank, nameof(AI.maxBank), "BankLimiter", $"{AI.maxBank:0}°");
                                    ctrlLines = ContentEntry(ContentType.FloatSlider, ctrlLines, contentWidth, ref AI.waypointPreRollTime, nameof(AI.waypointPreRollTime), "WaypointPreRollTime", $"{AI.waypointPreRollTime:0.00}s");
                                    ctrlLines = ContentEntry(ContentType.FloatSlider, ctrlLines, contentWidth, ref AI.waypointYawAuthorityTime, nameof(AI.waypointYawAuthorityTime), "WaypointYawAuthorityTime", $"{AI.waypointYawAuthorityTime:0.0}s");
                                    ctrlLines = ContentEntry(ContentType.FloatSlider, ctrlLines, contentWidth, ref AI.maxAllowedGForce, nameof(AI.maxAllowedGForce), "MaxAllowedGForce", $"{AI.maxAllowedGForce:0.0}<i>g</i>");
                                    ctrlLines = ContentEntry(ContentType.FloatSlider, ctrlLines, contentWidth, ref AI.maxAllowedAoA, nameof(AI.maxAllowedAoA), "MaxAllowedAoA", $"{AI.maxAllowedAoA:0.0}°");
                                    if (!(BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 55))
                                    { ctrlLines = ContentEntry(ContentType.FloatSlider, ctrlLines, contentWidth, ref AI.postStallAoA, nameof(AI.postStallAoA), "PostStallAoA", $"{AI.postStallAoA:0.0}"); }
                                    ctrlLines = ContentEntry(ContentType.FloatSlider, ctrlLines, contentWidth, ref AI.ImmelmannTurnAngle, nameof(AI.ImmelmannTurnAngle), "ImmelmannTurnAngle", $"{AI.ImmelmannTurnAngle:0}°");
                                    ctrlLines = ContentEntry(ContentType.FloatSlider, ctrlLines, contentWidth, ref AI.ImmelmannPitchUpBias, nameof(AI.ImmelmannPitchUpBias), "ImmelmannPitchUpBias", $"{AI.ImmelmannPitchUpBias:0}°/s");

                                    GUI.EndGroup();
                                    sectionHeights[Section.Control] = Mathf.Lerp(sectionHeight, ctrlLines, 0.15f);
                                    ctrlLines += 0.1f;
                                    contentHeight += ctrlLines * entryHeight;
                                }
                                if (showSection[Section.Evasion])
                                {
                                    float evadeLines = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Evasion);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + evadeLines * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    evadeLines += 0.25f;

                                    #region Evasion
                                    GUI.Label(SettinglabelRect(evadeLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Evade"), BoldLabel);
                                    evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.minEvasionTime, nameof(AI.minEvasionTime), "MinEvasionTime", $"{AI.minEvasionTime:0.00}s");
                                    evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.evasionThreshold, nameof(AI.evasionThreshold), "EvasionThreshold", $"{AI.evasionThreshold:0}m");
                                    evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.evasionTimeThreshold, nameof(AI.evasionTimeThreshold), "EvasionTimeThreshold", $"{AI.evasionTimeThreshold:0.00}s");
                                    evadeLines = ContentEntry(ContentType.SemiLogSlider, evadeLines, contentWidth, ref AI.evasionMinRangeThreshold, nameof(AI.evasionMinRangeThreshold), "EvasionMinRangeThreshold", AI.evasionMinRangeThreshold < 1000 ? $"{AI.evasionMinRangeThreshold:0}m" : $"{AI.evasionMinRangeThreshold / 1000:0}km");
                                    evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.evasionNonlinearity, nameof(AI.evasionNonlinearity), "EvasionNonlinearity", $"{AI.evasionNonlinearity:0.0}°");

                                    AI.evasionIgnoreMyTargetTargetingMe = GUI.Toggle(ToggleButtonRect(evadeLines, contentWidth), AI.evasionIgnoreMyTargetTargetingMe, StringUtils.Localize("#LOC_BDArmory_AI_EvasionIgnoreMyTargetTargetingMe"), AI.evasionIgnoreMyTargetTargetingMe ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                                    evadeLines += 1.25f;

                                    AI.evasionMissileKinematic = GUI.Toggle(ToggleButtonRect(evadeLines, contentWidth), AI.evasionMissileKinematic, StringUtils.Localize("#LOC_BDArmory_AI_EvasionMissileKinematic"), AI.evasionMissileKinematic ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                                    evadeLines += 1.25f;
                                    #endregion

                                    #region Craft Avoidance
                                    evadeLines += 0.5f;
                                    GUI.Label(SettinglabelRect(evadeLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Avoidance"), BoldLabel);
                                    evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.collisionAvoidanceThreshold, nameof(AI.collisionAvoidanceThreshold), "CollisionAvoidanceThreshold", $"{AI.collisionAvoidanceThreshold:0}m");
                                    evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.vesselCollisionAvoidanceLookAheadPeriod, nameof(AI.vesselCollisionAvoidanceLookAheadPeriod), "CollisionAvoidanceLookAheadPeriod", $"{AI.vesselCollisionAvoidanceLookAheadPeriod:0.0}s");
                                    evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.vesselCollisionAvoidanceStrength, nameof(AI.vesselCollisionAvoidanceStrength), "CollisionAvoidanceStrength", $"{AI.vesselCollisionAvoidanceStrength:0.0} ({AI.vesselCollisionAvoidanceStrength / Time.fixedDeltaTime:0}°/s)");
                                    evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.vesselStandoffDistance, nameof(AI.vesselStandoffDistance), "StandoffDistance", $"{AI.vesselStandoffDistance:0}m");
                                    #endregion

                                    #region Extending
                                    if (AI.canExtend)
                                    {
                                        evadeLines += 0.5f;
                                        GUI.Label(SettinglabelRect(evadeLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Extend"), BoldLabel);
                                        evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.extendDistanceAirToAir, nameof(AI.extendDistanceAirToAir), "ExtendDistanceAirToAir", $"{AI.extendDistanceAirToAir:0}m");
                                        evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.extendAngleAirToAir, nameof(AI.extendAngleAirToAir), "ExtendAngleAirToAir", $"{AI.extendAngleAirToAir:0}°");
                                        evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.extendDistanceAirToGroundGuns, nameof(AI.extendDistanceAirToGroundGuns), "ExtendDistanceAirToGroundGuns", $"{AI.extendDistanceAirToGroundGuns:0}m");
                                        evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.extendDistanceAirToGround, nameof(AI.extendDistanceAirToGround), "ExtendDistanceAirToGround", $"{AI.extendDistanceAirToGround:0}m");
                                        evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.extendTargetVel, nameof(AI.extendTargetVel), "ExtendTargetVel", $"{AI.extendTargetVel:0.0}m/s");
                                        evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.extendTargetAngle, nameof(AI.extendTargetAngle), "ExtendTargetAngle", $"{AI.extendTargetAngle:0}°");
                                        evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.extendTargetDist, nameof(AI.extendTargetDist), "ExtendTargetDist", $"{AI.extendTargetDist:0}m");
                                        evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.extendAbortTime, nameof(AI.extendAbortTime), "ExtendAbortTime", $"{AI.extendAbortTime:0}s");
                                    }
                                    AI.canExtend = GUI.Toggle(ToggleButtonRect(evadeLines, contentWidth), AI.canExtend, StringUtils.Localize("#LOC_BDArmory_AI_ExtendToggle"), AI.canExtend ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Dynamic pid"
                                    evadeLines += 1.25f;
                                    #endregion

                                    GUI.EndGroup();
                                    sectionHeights[Section.Evasion] = Mathf.Lerp(sectionHeight, evadeLines, 0.15f);
                                    evadeLines += 0.1f;
                                    contentHeight += evadeLines * entryHeight;
                                }
                                if (showSection[Section.Terrain])
                                {
                                    float gndLines = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Terrain);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + gndLines * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    gndLines += 0.25f;

                                    GUI.Label(SettinglabelRect(gndLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Terrain"), BoldLabel);//"Speed"

                                    var oldMinTwiddle = AI.turnRadiusTwiddleFactorMin;
                                    gndLines = ContentEntry(ContentType.FloatSlider, gndLines, contentWidth, ref AI.turnRadiusTwiddleFactorMin, nameof(AI.turnRadiusTwiddleFactorMin), "TerrainAvoidanceMin", $"{AI.turnRadiusTwiddleFactorMin:0.0}");
                                    if (AI.turnRadiusTwiddleFactorMin != oldMinTwiddle)
                                    {
                                        AI.OnMinUpdated(null, null);
                                        var field = inputFields["turnRadiusTwiddleFactorMax"];
                                        field.SetCurrentValue(AI.turnRadiusTwiddleFactorMax);
                                    }

                                    var oldMaxTwiddle = AI.turnRadiusTwiddleFactorMax;
                                    gndLines = ContentEntry(ContentType.FloatSlider, gndLines, contentWidth, ref AI.turnRadiusTwiddleFactorMax, nameof(AI.turnRadiusTwiddleFactorMax), "TerrainAvoidanceMax", $"{AI.turnRadiusTwiddleFactorMax:0.0}");
                                    if (AI.turnRadiusTwiddleFactorMax != oldMaxTwiddle)
                                    {
                                        AI.OnMaxUpdated(null, null);
                                        var field = inputFields["turnRadiusTwiddleFactorMin"];
                                        field.SetCurrentValue(AI.turnRadiusTwiddleFactorMin);
                                    }

                                    var oldTerrainAvoidanceCriticalAngle = AI.terrainAvoidanceCriticalAngle;
                                    gndLines = ContentEntry(ContentType.FloatSlider, gndLines, contentWidth, ref AI.terrainAvoidanceCriticalAngle, nameof(AI.terrainAvoidanceCriticalAngle), "InvertedTerrainAvoidanceCriticalAngle", $"{AI.terrainAvoidanceCriticalAngle:0}°");
                                    if (AI.terrainAvoidanceCriticalAngle != oldTerrainAvoidanceCriticalAngle) { AI.OnTerrainAvoidanceCriticalAngleChanged(); }

                                    gndLines = ContentEntry(ContentType.FloatSlider, gndLines, contentWidth, ref AI.controlSurfaceDeploymentTime, nameof(AI.controlSurfaceDeploymentTime), "TerrainAvoidanceVesselReactionTime", $"{AI.controlSurfaceDeploymentTime:0.0}s");
                                    gndLines = ContentEntry(ContentType.FloatSlider, gndLines, contentWidth, ref AI.postTerrainAvoidanceCoolDownDuration, nameof(AI.postTerrainAvoidanceCoolDownDuration), "TerrainAvoidancePostAvoidanceCoolDown", $"{AI.postTerrainAvoidanceCoolDownDuration:0.00}s");
                                    gndLines = ContentEntry(ContentType.FloatSlider, gndLines, contentWidth, ref AI.waypointTerrainAvoidance, nameof(AI.waypointTerrainAvoidance), "WaypointTerrainAvoidance", $"{AI.waypointTerrainAvoidance:0.00}");

                                    GUI.EndGroup();
                                    sectionHeights[Section.Terrain] = Mathf.Lerp(sectionHeight, gndLines, 0.15f);
                                    gndLines += 0.1f;
                                    contentHeight += gndLines * entryHeight;
                                }
                                if (showSection[Section.Ramming])
                                {
                                    float ramLines = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Ramming);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + ramLines * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    ramLines += 0.25f;

                                    GUI.Label(SettinglabelRect(ramLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Ramming"), BoldLabel);//"Ramming"

                                    AI.allowRamming = GUI.Toggle(ToggleButtonRect(ramLines, contentWidth), AI.allowRamming,
                                        StringUtils.Localize("#LOC_BDArmory_AI_AllowRamming"), AI.allowRamming ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Allow Ramming"
                                    ramLines += 1.25f;

                                    if (AI.allowRamming)
                                    {
                                        AI.allowRammingGroundTargets = GUI.Toggle(ToggleButtonRect(ramLines, contentWidth), AI.allowRammingGroundTargets,
                                            StringUtils.Localize("#LOC_BDArmory_AI_AllowRammingGroundTargets"), AI.allowRammingGroundTargets ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Include Ground Targets"
                                        ramLines += 1.25f;
                                        ramLines = ContentEntry(ContentType.FloatSlider, ramLines, contentWidth, ref AI.controlSurfaceLag, nameof(AI.controlSurfaceLag), "ControlSurfaceLag", $"{AI.controlSurfaceLag:0.00}s");
                                    }

                                    GUI.EndGroup();
                                    sectionHeights[Section.Ramming] = Mathf.Lerp(sectionHeight, ramLines, 0.15f);
                                    ramLines += 0.1f;
                                    contentHeight += ramLines * entryHeight;
                                }
                                if (showSection[Section.Misc])
                                {
                                    float miscLines = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Misc);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + miscLines * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    miscLines += 0.25f;

                                    GUI.Label(SettinglabelRect(miscLines++), StringUtils.Localize("#LOC_BDArmory_AI_Orbit"), BoldLabel);//"orbit"
                                    AI.ClockwiseOrbit = GUI.Toggle(ToggleButtonRect(miscLines, contentWidth), AI.ClockwiseOrbit,
                                        AI.ClockwiseOrbit ? StringUtils.Localize("#LOC_BDArmory_AI_Orbit_Starboard") : StringUtils.Localize("#LOC_BDArmory_AI_Orbit_Port"),
                                        AI.ClockwiseOrbit ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                                    miscLines += 1.25f;
                                    if (contextTipsEnabled) GUI.Label(ContextLabelRect(miscLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Orbit_Context"), Label);//"orbit direction"

                                    GUI.Label(SettinglabelRect(miscLines++), StringUtils.Localize("#LOC_BDArmory_AI_Standby"), BoldLabel);//"Standby"
                                    AI.standbyMode = GUI.Toggle(ToggleButtonRect(miscLines, contentWidth),
                                    AI.standbyMode, AI.standbyMode ? StringUtils.Localize("#LOC_BDArmory_On") : StringUtils.Localize("#LOC_BDArmory_Off"), AI.standbyMode ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Dynamic pid"
                                    miscLines += 1.25f;
                                    if (contextTipsEnabled) GUI.Label(ContextLabelRect(miscLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Standby_Context"), Label);//"Activate when target in guard range"

                                    GUI.Label(SettinglabelRect(miscLines++), StringUtils.Localize("#LOC_BDArmory_ControlSurfaceSettings"), BoldLabel);//"Control Surface Settings"
                                    if (GUI.Button(ToggleButtonRect(miscLines, contentWidth), StringUtils.Localize("#LOC_BDArmory_StoreControlSurfaceSettings"), BDArmorySetup.BDGuiSkin.button))
                                    {
                                        AI.StoreControlSurfaceSettings(); //Hiding these in misc is probably not the best place to put them, but only so much space on the window header bar
                                    }
                                    miscLines += 1.25f;
                                    if (AI.Events["RestoreControlSurfaceSettings"].active == true)
                                    {
                                        GUIStyle restoreStyle = BDArmorySetup.BDGuiSkin.button;
                                        if (GUI.Button(ToggleButtonRect(miscLines, contentWidth), StringUtils.Localize("#LOC_BDArmory_RestoreControlSurfaceSettings"), restoreStyle))
                                        {
                                            AI.RestoreControlSurfaceSettings();
                                        }
                                        miscLines += 1.25f;
                                    }

                                    GUI.EndGroup();
                                    sectionHeights[Section.Misc] = Mathf.Lerp(sectionHeight, miscLines, 0.15f);
                                    miscLines += 0.1f;
                                    contentHeight += miscLines * entryHeight;
                                }

                                GUI.EndGroup();
                                GUI.EndScrollView();
                            }

                            if (infoLinkEnabled)
                            {
                                windowColumns = 3;

                                GUI.Label(new Rect(contentMargin + ColumnWidth * 2, contentTop, ColumnWidth - contentMargin, entryHeight), StringUtils.Localize("#LOC_BDArmory_AIWindow_infoLink"), Title);//"infolink"
                                BeginArea(new Rect(contentMargin + ColumnWidth * 2, contentTop + entryHeight * 1.5f, ColumnWidth - contentMargin, WindowHeight - entryHeight * 1.5f - 2 * contentTop));
                                using (var scrollViewScope = new ScrollViewScope(scrollInfoVector, Width(ColumnWidth - contentMargin), Height(WindowHeight - entryHeight * 1.5f - 2 * contentTop)))
                                {
                                    scrollInfoVector = scrollViewScope.scrollPosition;

                                    if (showSection[Section.PID]) //these autoalign, so if new entries need to be added, they can just be slotted in
                                    {
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_PID"), BoldLabel, Width(ColumnWidth - contentMargin * 4 - 20)); //PID label
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_PidHelp"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //Pid desc
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_PidHelp_SteerPower"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //steer mult desc
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_PidHelp_SteerKi"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //steer ki desc.
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_PidHelp_Steerdamp"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //steer damp description
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_PidHelp_Dyndamp"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //dynamic damping desc
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_PidHelp_AutoTune") + (AI.AutoTune ? StringUtils.Localize("#LOC_BDArmory_AIWindow_PidHelp_AutoTune_details") : ""), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //auto-tuning desc
                                    }
                                    if (showSection[Section.Altitude])
                                    {
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_Altitudes"), BoldLabel, Width(ColumnWidth - (contentMargin * 4) - 20)); //Altitude label
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_AltHelp"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //altitude description
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_AltHelp_Def"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //default alt desc
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_AltHelp_min"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //min alt desc
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_AltHelp_max"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //max alt desc
                                    }
                                    if (showSection[Section.Speed])
                                    {
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_Speeds"), BoldLabel, Width(ColumnWidth - (contentMargin * 4) - 20)); //Speed header
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_SpeedHelp"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //speed explanation
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_SpeedHelp_min"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //min+max speed desc
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_SpeedHelp_takeoff"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //takeoff speed
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_SpeedHelp_gnd"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //strafe speed
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_SpeedHelp_idle"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //idle speed
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_SpeedHelp_ABpriority"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //AB priority
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_SpeedHelp_ABOverrideThreshold"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //AB override threshold
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_SpeedHelp_BrakingPriority"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //Braking priority
                                    }
                                    if (showSection[Section.Control])
                                    {
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_Control"), BoldLabel, Width(ColumnWidth - (contentMargin * 4) - 20)); //conrrol header
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_ControlHelp"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //control desc
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_ControlHelp_limiters"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //low + high speed limiters
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_ControlHelp_bank"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //max bank desc
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_ControlHelp_clamps"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //max G + max AoA
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_ControlHelp_modeSwitches"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //post-stall
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_ControlHelp_Immelmann"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //Immelmann turn angle + bias
                                    }
                                    if (showSection[Section.Evasion])
                                    {
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeExtend"), BoldLabel, Width(ColumnWidth - (contentMargin * 4) - 20)); //evade header
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //evade description
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp_Evade"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //evade dist/ time/ time threshold
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp_Nonlinearity"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //evade/extend nonlinearity
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp_Dodge"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //collision avoid
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp_standoff"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //standoff distance
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp_Extend"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //extend distances
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp_ExtendVars"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //extend target dist/angle/vel
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp_ExtendVel"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //extend target velocity
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp_ExtendAngle"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //extend target angle
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp_ExtendDist"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //extend target dist
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp_ExtendAbortTime"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //extend abort time
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp_ExtendToggle"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //evade/extend toggle
                                    }
                                    if (showSection[Section.Terrain])
                                    {
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_Terrain"), BoldLabel, Width(ColumnWidth - (contentMargin * 4) - 20)); //Terrain avoid header
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_TerrainHelp"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //terrain avoid desc
                                    }
                                    if (showSection[Section.Ramming])
                                    {
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AI_Ramming"), BoldLabel, Width(ColumnWidth - (contentMargin * 4) - 20)); //ramming header
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_RamHelp"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20));// ramming desc
                                    }
                                    if (showSection[Section.Misc])
                                    {
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_Misc"), BoldLabel, Width(ColumnWidth - (contentMargin * 4) - 20)); //misc header
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_miscHelp"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //misc desc
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_orbitHelp"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //orbit dir
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_standbyHelp"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //standby
                                    }
                                }
                                EndArea();
                            }
                        }
                        break;
                    case ActiveAIType.SurfaceAI:
                        {
                            var AI = ActiveAI as BDModuleSurfaceAI;
                            if (AI == null) { Debug.LogError($"[BDArmory.BDArmoryAIGUI]: AI module mismatch!"); activeAIType = ActiveAIType.None; break; }

                            { // Section buttons
                                float line = 1.5f;
                                showSection[Section.PID] = GUI.Toggle(SubsectionRect(line), showSection[Section.PID], StringUtils.Localize("#LOC_BDArmory_AIWindow_PID"), showSection[Section.PID] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"PiD"

                                line += 1.5f;
                                showSection[Section.Speed] = GUI.Toggle(SubsectionRect(line), showSection[Section.Speed], StringUtils.Localize("#LOC_BDArmory_AIWindow_Speeds"), showSection[Section.Speed] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Speed"

                                line += 1.5f;
                                showSection[Section.Control] = GUI.Toggle(SubsectionRect(line), showSection[Section.Control], StringUtils.Localize("#LOC_BDArmory_AIWindow_Control"), showSection[Section.Control] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Control"

                                line += 1.5f;
                                showSection[Section.Combat] = GUI.Toggle(SubsectionRect(line), showSection[Section.Combat], StringUtils.Localize("#LOC_BDArmory_AIWindow_Combat"), showSection[Section.Combat] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Combat"

                                line += 1.5f;
                                if (showSection[Section.UpToEleven] != (AI.UpToEleven = GUI.Toggle(SubsectionRect(line), AI.UpToEleven,
                                    AI.UpToEleven ? StringUtils.Localize("#LOC_BDArmory_AI_UnclampTuning_enabledText") : StringUtils.Localize("#LOC_BDArmory_AI_UnclampTuning_disabledText"),
                                    AI.UpToEleven ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button)))//"Misc"
                                {
                                    SetInputFields(activeAIType);
                                }
                            }

                            { // Controls panel
                                scrollViewVectors[ActiveAIType.SurfaceAI] = GUI.BeginScrollView(
                                    new Rect(contentMargin + 100, contentTop + entryHeight * 1.5f, (ColumnWidth * 2) - 100 - contentMargin, WindowHeight - entryHeight * 1.5f - 2 * contentTop),
                                    scrollViewVectors.GetValueOrDefault(ActiveAIType.SurfaceAI),
                                    new Rect(0, 0, ColumnWidth * 2 - 120 - contentMargin * 2, height + contentTop)
                                );

                                GUI.BeginGroup(new Rect(contentMargin, 0, ColumnWidth * 2 - 120 - contentMargin * 2, height + 2 * contentBorder), GUIContent.none, BDArmorySetup.BDGuiSkin.box); //darker box

                                contentWidth -= 24 + contentBorder;

                                { // Vehicle Type
                                    float line = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.VehicleType);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    line += 0.25f;

                                    GUI.Label(SettinglabelRect(line), StringUtils.Localize("#LOC_BDArmory_AIWindow_VehicleType") + $": {AI.SurfaceTypeName}", Label);
                                    if (Drivertype != (Drivertype = Mathf.RoundToInt(GUI.HorizontalSlider(SettingSliderRect(line++, contentWidth), Drivertype, 0, VehicleMovementTypes.Length - 1))))
                                    {
                                        AI.SurfaceTypeName = VehicleMovementTypes[Drivertype].ToString();
                                        AI.ChooseOptionsUpdated(null, null);
                                    }
                                    if (contextTipsEnabled)
                                    {
                                        GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_VehicleType_Context"), contextLabel);
                                    }

                                    GUI.EndGroup();
                                    sectionHeights[Section.VehicleType] = Mathf.Lerp(sectionHeight, line, 0.15f);
                                    line += 0.1f;
                                    contentHeight += line * entryHeight;
                                }

                                if (AI.SurfaceType != AIUtils.VehicleMovementType.Stationary)
                                {
                                    if (showSection[Section.PID])
                                    {
                                        float line = 0.2f;
                                        var sectionHeight = sectionHeights.GetValueOrDefault(Section.PID);
                                        GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                        line += 0.25f;

                                        line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.steerMult, nameof(AI.steerMult), "SteerPower", $"{AI.steerMult:0.0}", true);
                                        line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.steerDamping, nameof(AI.steerDamping), "SteerDamping", $"{AI.steerDamping:0.0}", true);

                                        GUI.EndGroup();
                                        sectionHeights[Section.PID] = Mathf.Lerp(sectionHeight, line, 0.15f);
                                        line += 0.1f;
                                        contentHeight += line * entryHeight;
                                    }
                                    if (showSection[Section.Speed])
                                    {
                                        float line = 0.2f;
                                        var sectionHeight = sectionHeights.GetValueOrDefault(Section.Speed);
                                        GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                        line += 0.25f;

                                        line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.CruiseSpeed, nameof(AI.CruiseSpeed), "CruiseSpeed", $"{AI.CruiseSpeed:0}m/s");
                                        line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.MaxSpeed, nameof(AI.MaxSpeed), "MaxSpeed", $"{AI.MaxSpeed:0}m/s");

                                        GUI.EndGroup();
                                        sectionHeights[Section.Speed] = Mathf.Lerp(sectionHeight, line, 0.15f);
                                        line += 0.1f;
                                        contentHeight += line * entryHeight;
                                    }
                                    if (showSection[Section.Control])
                                    {
                                        float line = 0.2f;
                                        var sectionHeight = sectionHeights.GetValueOrDefault(Section.Control);
                                        GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                        line += 0.25f;

                                        line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.MaxSlopeAngle, nameof(AI.MaxSlopeAngle), "MaxSlopeAngle", $"{AI.MaxSlopeAngle:0}°");
                                        line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.MaxDrift, nameof(AI.MaxDrift), "MaxDrift", $"{AI.MaxDrift:0}°");
                                        line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.TargetPitch, nameof(AI.TargetPitch), "TargetPitch", $"{AI.TargetPitch:0.0}°");
                                        line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.BankAngle, nameof(AI.BankAngle), "BankAngle", $"{AI.BankAngle:0}°");
                                        line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.AvoidMass, nameof(AI.AvoidMass), "MinObstacleMass", $"{AI.AvoidMass:0}t");

                                        if (broadsideDir != (broadsideDir = Mathf.RoundToInt(GUI.HorizontalSlider(SettingSliderRect(line, contentWidth), broadsideDir, 0, AI.orbitDirections.Length - 1))))
                                        {
                                            AI.SetBroadsideDirection(AI.orbitDirections[broadsideDir]);
                                            AI.ChooseOptionsUpdated(null, null);
                                        }
                                        GUI.Label(SettinglabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_PreferredBroadsideDirection") + $": {AI.OrbitDirectionName}", Label);
                                        if (contextTipsEnabled)
                                        {
                                            GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_PreferredBroadsideDirection_Context"), contextLabel);
                                        }

                                        AI.ManeuverRCS = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.ManeuverRCS,
                                            StringUtils.Localize("#LOC_BDArmory_AIWindow_ManeuverRCS") + " : " + (AI.ManeuverRCS ? StringUtils.Localize("#LOC_BDArmory_AI_ManeuverRCS_enabledText") : StringUtils.Localize("#LOC_BDArmory_AI_ManeuverRCS_disabledText")),
                                            AI.ManeuverRCS ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                                        line += 1.25f;
                                        if (contextTipsEnabled)
                                        {
                                            GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_ManeuverRCS_Context"), contextLabel);
                                        }

                                        GUI.EndGroup();
                                        sectionHeights[Section.Control] = Mathf.Lerp(sectionHeight, line, 0.15f);
                                        line += 0.1f;
                                        contentHeight += line * entryHeight;
                                    }
                                }
                                if (showSection[Section.Combat])
                                {
                                    float line = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Combat);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    line += 0.25f;

                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.MinEngagementRange, nameof(AI.MinEngagementRange), "MinEngagementRange", $"{AI.MinEngagementRange:0}m");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.MaxEngagementRange, nameof(AI.MaxEngagementRange), "MaxEngagementRange", $"{AI.MaxEngagementRange:0}m");
                                    if (AI.SurfaceType == AIUtils.VehicleMovementType.Submarine)
                                    { line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.CombatAltitude, nameof(AI.CombatAltitude), "CombatAltitude", $"{AI.CombatAltitude:0}m"); }
                                    if (AI.SurfaceType != AIUtils.VehicleMovementType.Stationary)
                                    {
                                        line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.WeaveFactor, nameof(AI.WeaveFactor), "WeaveFactor", $"{AI.WeaveFactor:0.0}");
                                        if (AI.SurfaceType == AIUtils.VehicleMovementType.Land)
                                        {
                                            AI.maintainMinRange = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.maintainMinRange,
                                                StringUtils.Localize("#LOC_BDArmory_AIWindow_MaintainEngagementRange") + " : " + (AI.maintainMinRange ? StringUtils.Localize("#LOC_BDArmory_true") : StringUtils.Localize("#LOC_BDArmory_false")),
                                                AI.maintainMinRange ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Maintain Min range"
                                            line += 1.25f;
                                            if (contextTipsEnabled)
                                            {
                                                GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_MaintainEngagementRange_Context"), contextLabel);
                                            }
                                        }

                                        AI.BroadsideAttack = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.BroadsideAttack,
                                            StringUtils.Localize("#LOC_BDArmory_AIWindow_BroadsideAttack") + " : " + (AI.BroadsideAttack ? StringUtils.Localize("#LOC_BDArmory_AI_BroadsideAttack_enabledText") : StringUtils.Localize("#LOC_BDArmory_AI_BroadsideAttack_disabledText")),
                                            AI.BroadsideAttack ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//Broadside Attack"
                                        line += 1.25f;
                                        if (contextTipsEnabled)
                                        {
                                            GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_BroadsideAttack_Context"), contextLabel);
                                        }
                                    }

                                    GUI.EndGroup();
                                    sectionHeights[Section.Combat] = Mathf.Lerp(sectionHeight, line, 0.15f);
                                    line += 0.1f;
                                    contentHeight += line * entryHeight;
                                }

                                GUI.EndGroup();
                                GUI.EndScrollView();
                            }

                            if (infoLinkEnabled)
                            {
                                windowColumns = 3;

                                GUI.Label(new Rect(contentMargin + ColumnWidth * 2, contentTop, ColumnWidth - contentMargin, entryHeight), StringUtils.Localize("#LOC_BDArmory_AIWindow_infoLink"), Title);//"infolink"
                                BeginArea(new Rect(contentMargin + ColumnWidth * 2, contentTop + entryHeight * 1.5f, ColumnWidth - contentMargin, WindowHeight - entryHeight * 1.5f - 2 * contentTop));
                                using (var scrollViewScope = new ScrollViewScope(scrollInfoVector, Width(ColumnWidth - contentMargin), Height(WindowHeight - entryHeight * 1.5f - 2 * contentTop)))
                                {
                                    scrollInfoVector = scrollViewScope.scrollPosition;

                                    GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Surface_Type"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //Pid desc
                                    if (AI.SurfaceType != AIUtils.VehicleMovementType.Stationary)
                                    {
                                        if (showSection[Section.PID])
                                        {
                                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Surface_SteerPower"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //steer mult desc
                                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Surface_SteerDamping"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //steer damp desc
                                        }
                                        if (showSection[Section.Speed])
                                        {
                                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Surface_Speeds"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //cruise, flank speed desc
                                        }
                                        if (showSection[Section.Control])
                                        {
                                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Surface_Slopes"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //tgt pitch, slope angle desc
                                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Surface_Drift"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //drift angle desc
                                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Surface_Bank"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //bank angle desc
                                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Surface_AvoidMass"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //avoid mass desc
                                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Surface_Orientation"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //attack vector, broadside desc
                                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Surface_RCS"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //RCS desc
                                        }
                                    }
                                    if (showSection[Section.Combat])
                                    {
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Surface_Engagement"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //engage ranges desc
                                        if (AI.SurfaceType == AIUtils.VehicleMovementType.Submarine)
                                        {
                                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Surface_Altitude"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //sub cruise/combat depth
                                        }
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Surface_Weave"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //weave factor desc
                                        if (AI.SurfaceType == AIUtils.VehicleMovementType.Land)
                                        {
                                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Surface_MaintainMinRange"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //maintain min range desc
                                        }
                                    }
                                }
                                EndArea();
                            }
                        }
                        break;
                    case ActiveAIType.VTOLAI:
                        {
                            var AI = ActiveAI as BDModuleVTOLAI;
                            if (AI == null) { Debug.LogError($"[BDArmory.BDArmoryAIGUI]: AI module mismatch!"); activeAIType = ActiveAIType.None; break; }

                            { // Section buttons
                                float line = 1.5f;
                                showSection[Section.PID] = GUI.Toggle(SubsectionRect(line), showSection[Section.PID], StringUtils.Localize("#LOC_BDArmory_AIWindow_PID"), showSection[Section.PID] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"PiD"

                                line += 1.5f;
                                showSection[Section.Altitude] = GUI.Toggle(SubsectionRect(line), showSection[Section.Altitude], StringUtils.Localize("#LOC_BDArmory_AIWindow_Altitudes"), showSection[Section.Altitude] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Altitude"

                                line += 1.5f;
                                showSection[Section.Speed] = GUI.Toggle(SubsectionRect(line), showSection[Section.Speed], StringUtils.Localize("#LOC_BDArmory_AIWindow_Speeds"), showSection[Section.Speed] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Speed"

                                line += 1.5f;
                                showSection[Section.Control] = GUI.Toggle(SubsectionRect(line), showSection[Section.Control], StringUtils.Localize("#LOC_BDArmory_AIWindow_Control"), showSection[Section.Control] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Control"

                                line += 1.5f;
                                showSection[Section.Combat] = GUI.Toggle(SubsectionRect(line), showSection[Section.Combat], StringUtils.Localize("#LOC_BDArmory_AIWindow_Combat"), showSection[Section.Combat] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Combat"

                                line += 1.5f;
                                if (showSection[Section.UpToEleven] != (AI.UpToEleven = GUI.Toggle(SubsectionRect(line), AI.UpToEleven,
                                    AI.UpToEleven ? StringUtils.Localize("#LOC_BDArmory_AI_UnclampTuning_enabledText") : StringUtils.Localize("#LOC_BDArmory_AI_UnclampTuning_disabledText"),
                                    AI.UpToEleven ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button)))//"Misc"
                                {
                                    SetInputFields(activeAIType);
                                }
                            }

                            if (showSection[Section.PID] || showSection[Section.Altitude] || showSection[Section.Speed] || showSection[Section.Control] || showSection[Section.Combat]) // Controls panel
                            {
                                scrollViewVectors[ActiveAIType.VTOLAI] = GUI.BeginScrollView(
                                    new Rect(contentMargin + 100, contentTop + entryHeight * 1.5f, (ColumnWidth * 2) - 100 - contentMargin, WindowHeight - entryHeight * 1.5f - 2 * contentTop),
                                    scrollViewVectors.GetValueOrDefault(ActiveAIType.VTOLAI),
                                    new Rect(0, 0, ColumnWidth * 2 - 120 - contentMargin * 2, height + contentTop)
                                );

                                GUI.BeginGroup(new Rect(contentMargin, 0, ColumnWidth * 2 - 120 - contentMargin * 2, height + 2 * contentBorder), GUIContent.none, BDArmorySetup.BDGuiSkin.box); //darker box

                                contentWidth -= 24 + contentBorder;

                                if (showSection[Section.PID])
                                {
                                    float line = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.PID);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    line += 0.25f;

                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.steerMult, nameof(AI.steerMult), "SteerPower", $"{AI.steerMult:0.0}", true);
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.steerKiAdjust, nameof(AI.steerKiAdjust), "SteerKi", $"{AI.steerKiAdjust:0.00}", true);
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.steerDamping, nameof(AI.steerDamping), "SteerDamping", $"{AI.steerDamping:0.0}", true);

                                    GUI.EndGroup();
                                    sectionHeights[Section.PID] = Mathf.Lerp(sectionHeight, line, 0.15f);
                                    line += 0.1f;
                                    contentHeight += line * entryHeight;
                                }
                                if (showSection[Section.Altitude])
                                {
                                    float line = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Altitude);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    line += 0.25f;

                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.defaultAltitude, nameof(AI.defaultAltitude), "DefaultAltitude", $"{AI.defaultAltitude:0}m");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.CombatAltitude, nameof(AI.CombatAltitude), "CombatAltitude", $"{AI.CombatAltitude:0}m");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.minAltitude, nameof(AI.minAltitude), "MinAltitude", $"{AI.minAltitude:0}m");

                                    GUI.EndGroup();
                                    sectionHeights[Section.Altitude] = Mathf.Lerp(sectionHeight, line, 0.15f);
                                    line += 0.1f;
                                    contentHeight += line * entryHeight;
                                }
                                if (showSection[Section.Speed])
                                {
                                    float line = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Speed);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    line += 0.25f;

                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.MaxSpeed, nameof(AI.MaxSpeed), "MaxSpeed", $"{AI.MaxSpeed:0}m/s");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.CombatSpeed, nameof(AI.CombatSpeed), "CombatSpeed", $"{AI.CombatSpeed:0}m/s");

                                    GUI.EndGroup();
                                    sectionHeights[Section.Speed] = Mathf.Lerp(sectionHeight, line, 0.15f);
                                    line += 0.1f;
                                    contentHeight += line * entryHeight;
                                }
                                if (showSection[Section.Control])
                                {
                                    float line = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Control);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    line += 0.25f;

                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.MaxPitchAngle, nameof(AI.MaxPitchAngle), "MaxPitchAngle", $"{AI.MaxPitchAngle:0}°");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.MaxBankAngle, nameof(AI.MaxBankAngle), "MaxBankAngle", $"{AI.MaxBankAngle:0}°");

                                    GUI.Label(SettinglabelRect(line), StringUtils.Localize("#LOC_BDArmory_AIWindow_PreferredBroadsideDirection") + ": " + AI.OrbitDirectionName, Label);
                                    if (broadsideDir != (broadsideDir = Mathf.RoundToInt(GUI.HorizontalSlider(SettingSliderRect(line++, contentWidth), broadsideDir, 0, AI.orbitDirections.Length - 1))))
                                    {
                                        AI.SetBroadsideDirection(AI.orbitDirections[broadsideDir]);
                                        AI.ChooseOptionsUpdated(null, null);
                                    }
                                    if (contextTipsEnabled)
                                    {
                                        GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_PreferredBroadsideDirection_Context"), contextLabel);
                                    }

                                    AI.ManeuverRCS = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.ManeuverRCS,
                                        StringUtils.Localize("#LOC_BDArmory_AIWindow_ManeuverRCS") + " : " + (AI.ManeuverRCS ? StringUtils.Localize("#LOC_BDArmory_AI_ManeuverRCS_enabledText") : StringUtils.Localize("#LOC_BDArmory_AI_ManeuverRCS_disabledText")),
                                        AI.ManeuverRCS ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                                    line += 1.25f;
                                    if (contextTipsEnabled)
                                    {
                                        GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_ManeuverRCS_Context"), contextLabel);
                                    }

                                    GUI.EndGroup();
                                    sectionHeights[Section.Control] = Mathf.Lerp(sectionHeight, line, 0.15f);
                                    line += 0.1f;
                                    contentHeight += line * entryHeight;
                                }
                                if (showSection[Section.Combat])
                                {
                                    float line = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Combat);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    line += 0.25f;

                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.WeaveFactor, nameof(AI.WeaveFactor), "WeaveFactor", $"{AI.WeaveFactor:0.0}");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.MinEngagementRange, nameof(AI.MinEngagementRange), "MinEngagementRange", $"{AI.MinEngagementRange:0}m");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.MaxEngagementRange, nameof(AI.MaxEngagementRange), "MaxEngagementRange", $"{AI.MaxEngagementRange:0}m");

                                    AI.BroadsideAttack = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.BroadsideAttack,
                                        StringUtils.Localize("#LOC_BDArmory_AIWindow_BroadsideAttack") + " : " + (AI.BroadsideAttack ? StringUtils.Localize("#LOC_BDArmory_AI_BroadsideAttack_enabledText") : StringUtils.Localize("#LOC_BDArmory_AI_BroadsideAttack_disabledText")),
                                        AI.BroadsideAttack ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                                    line += 1.25f;
                                    if (contextTipsEnabled)
                                    {
                                        GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_BroadsideAttack_Context"), contextLabel);
                                    }

                                    GUI.EndGroup();
                                    sectionHeights[Section.Combat] = Mathf.Lerp(sectionHeight, line, 0.15f);
                                    line += 0.1f;
                                    contentHeight += line * entryHeight;
                                }

                                GUI.EndGroup();
                                GUI.EndScrollView();
                            }

                            if (infoLinkEnabled)
                            {
                                windowColumns = 3;

                                GUI.Label(new Rect(contentMargin + ColumnWidth * 2, contentTop, ColumnWidth - contentMargin, entryHeight), StringUtils.Localize("#LOC_BDArmory_AIWindow_infoLink"), Title);//"infolink"
                                BeginArea(new Rect(contentMargin + ColumnWidth * 2, contentTop + entryHeight * 1.5f, ColumnWidth - contentMargin, WindowHeight - entryHeight * 1.5f - 2 * contentTop));
                                using (var scrollViewScope = new ScrollViewScope(scrollInfoVector, Width(ColumnWidth - contentMargin), Height(WindowHeight - entryHeight * 1.5f - 2 * contentTop)))
                                {
                                    scrollInfoVector = scrollViewScope.scrollPosition;

                                    // FIXME
                                    if (showSection[Section.PID]) { GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_VTOL_PID"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); }
                                    if (showSection[Section.Altitude]) { GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_VTOL_Altitudes"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); }
                                    if (showSection[Section.Speed]) { GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_VTOL_Speeds"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); }
                                    if (showSection[Section.Control]) { GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_VTOL_Control"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); }
                                    if (showSection[Section.Combat]) { GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_VTOL_Combat"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); }
                                }
                                EndArea();
                            }
                        }
                        break;
                    case ActiveAIType.OrbitalAI:
                        {
                            var AI = ActiveAI as BDModuleOrbitalAI;
                            if (AI == null) { Debug.LogError($"[BDArmory.BDArmoryAIGUI]: AI module mismatch!"); activeAIType = ActiveAIType.None; break; }

                            { // Section buttons
                                float line = 1.5f;
                                showSection[Section.PID] = GUI.Toggle(SubsectionRect(line), showSection[Section.PID], StringUtils.Localize("#LOC_BDArmory_AIWindow_PID"), showSection[Section.PID] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"PiD"

                                line += 1.5f; 
                                showSection[Section.Combat] = GUI.Toggle(SubsectionRect(line), showSection[Section.Combat], StringUtils.Localize("#LOC_BDArmory_AIWindow_Combat"), showSection[Section.Combat] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Combat"

                                line += 1.5f;
                                showSection[Section.Speed] = GUI.Toggle(SubsectionRect(line), showSection[Section.Speed], StringUtils.Localize("#LOC_BDArmory_AIWindow_Speeds"), showSection[Section.Speed] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Speed"

                                line += 1.5f;
                                showSection[Section.Control] = GUI.Toggle(SubsectionRect(line), showSection[Section.Control], StringUtils.Localize("#LOC_BDArmory_AIWindow_Control"), showSection[Section.Control] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Control"

                                line += 1.5f;
                                showSection[Section.Evasion] = GUI.Toggle(SubsectionRect(line), showSection[Section.Evasion], StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeExtend"), showSection[Section.Evasion] ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);//"Evasion"
                            }

                            if (showSection[Section.PID] || showSection[Section.Combat] || showSection[Section.Speed] || showSection[Section.Control] || showSection[Section.Evasion]) // Controls panel
                            {
                                scrollViewVectors[ActiveAIType.OrbitalAI] = GUI.BeginScrollView(
                                    new Rect(contentMargin + 100, contentTop + entryHeight * 1.5f, (ColumnWidth * 2) - 100 - contentMargin, WindowHeight - entryHeight * 1.5f - 2 * contentTop),
                                    scrollViewVectors.GetValueOrDefault(ActiveAIType.OrbitalAI),
                                    new Rect(0, 0, ColumnWidth * 2 - 120 - contentMargin * 2, height + contentTop)
                                );

                                GUI.BeginGroup(new Rect(contentMargin, 0, ColumnWidth * 2 - 120 - contentMargin * 2, height + 2 * contentBorder), GUIContent.none, BDArmorySetup.BDGuiSkin.box); //darker box

                                contentWidth -= 24 + contentBorder;

                                if (showSection[Section.PID])
                                {
                                    float line = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.PID);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    line += 0.25f;

                                    // PID Mode
                                    GUI.Label(SettinglabelRect(line), StringUtils.Localize("#LOC_BDArmory_AIWindow_OrbitalPIDActive") + $": {AI.pidMode}", Label);
                                    if (pidMode != (pidMode = Mathf.RoundToInt(GUI.HorizontalSlider(SettingSliderRect(line++, contentWidth), pidMode, 0, PIDModeTypes.Length - 1))))
                                    {
                                        AI.pidMode = PIDModeTypes[pidMode].ToString();
                                        AI.ChooseOptionsUpdated(null, null);
                                    }
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.steerMult, nameof(AI.steerMult), "SteerPower", $"{AI.steerMult:0.0}", true);
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.steerKiAdjust, nameof(AI.steerKiAdjust), "SteerKi", $"{AI.steerKiAdjust:0.00}", true);
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.steerDamping, nameof(AI.steerDamping), "SteerDamping", $"{AI.steerDamping:0.0}", true);

                                    GUI.EndGroup();
                                    sectionHeights[Section.PID] = Mathf.Lerp(sectionHeight, line, 0.15f);
                                    line += 0.1f;
                                    contentHeight += line * entryHeight;
                                }
                                if (showSection[Section.Combat])
                                {
                                    float line = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Combat);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    line += 0.25f;

                                    GUI.Label(SettinglabelRect(line), StringUtils.Localize("#LOC_BDArmory_AIWindow_RollMode") + $": {AI.rollTowards}", Label);
                                    if (rollTowards != (rollTowards = Mathf.RoundToInt(GUI.HorizontalSlider(SettingSliderRect(line++, contentWidth), rollTowards, 0, RollModeTypes.Length - 1))))
                                    {
                                        AI.rollTowards = RollModeTypes[rollTowards].ToString();
                                        AI.ChooseOptionsUpdated(null, null);
                                    }
                                    line = ContentEntry(ContentType.SemiLogSlider, line, contentWidth, ref AI.MinEngagementRange, nameof(AI.MinEngagementRange), "MinEngagementRange", $"{AI.MinEngagementRange:0}m");

                                    GUI.EndGroup();
                                    sectionHeights[Section.Combat] = Mathf.Lerp(sectionHeight, line, 0.15f);
                                    line += 0.1f;
                                    contentHeight += line * entryHeight;
                                }
                                if (showSection[Section.Speed])
                                {
                                    float line = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Speed);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    line += 0.25f;

                                    line = ContentEntry(ContentType.SemiLogSlider, line, contentWidth, ref AI.ManeuverSpeed, nameof(AI.ManeuverSpeed), "ManeuverSpeed", $"{AI.ManeuverSpeed:0}m/s");
                                    line = ContentEntry(ContentType.SemiLogSlider, line, contentWidth, ref AI.firingSpeed, nameof(AI.firingSpeed), "FiringSpeed", $"{AI.firingSpeed:0}m/s");
                                    line = ContentEntry(ContentType.SemiLogSlider, line, contentWidth, ref AI.firingAngularVelocityLimit, nameof(AI.firingAngularVelocityLimit), "FiringAngularVelocityLimit", $"{AI.firingAngularVelocityLimit:0}deg/s");

                                    GUI.EndGroup();
                                    sectionHeights[Section.Speed] = Mathf.Lerp(sectionHeight, line, 0.15f);
                                    line += 0.1f;
                                    contentHeight += line * entryHeight;
                                }
                                if (showSection[Section.Control])
                                {
                                    float line = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Control);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    line += 0.25f;

                                    AI.ManeuverRCS = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.ManeuverRCS,
                                        StringUtils.Localize("#LOC_BDArmory_AIWindow_ManeuverRCS") + " : " + (AI.ManeuverRCS ? StringUtils.Localize("#LOC_BDArmory_AI_ManeuverRCS_enabledText") : StringUtils.Localize("#LOC_BDArmory_AI_ManeuverRCS_disabledText")),
                                        AI.ManeuverRCS ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                                    line += 1.25f;
                                    if (contextTipsEnabled)
                                    {
                                        GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_ManeuverRCS_Context"), contextLabel);
                                    }

                                    GUI.EndGroup();
                                    sectionHeights[Section.Control] = Mathf.Lerp(sectionHeight, line, 0.15f);
                                    line += 0.1f;
                                    contentHeight += line * entryHeight;
                                }
                                if (showSection[Section.Evasion])
                                {
                                    float line = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Evasion);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.BDGuiSkin.box);
                                    line += 0.25f;

                                    GUI.Label(SettinglabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Evade"), BoldLabel);
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.minEvasionTime, nameof(AI.minEvasionTime), "MinEvasionTime", $"{AI.minEvasionTime:0.00}s");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.evasionThreshold, nameof(AI.evasionThreshold), "EvasionThreshold", $"{AI.evasionThreshold:0}m");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.evasionTimeThreshold, nameof(AI.evasionTimeThreshold), "EvasionTimeThreshold", $"{AI.evasionTimeThreshold:0.0}s");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.evasionErraticness, nameof(AI.evasionErraticness), "EvasionErraticness", $"{AI.evasionErraticness:0.00}");
                                    line = ContentEntry(ContentType.SemiLogSlider, line, contentWidth, ref AI.evasionMinRangeThreshold, nameof(AI.evasionMinRangeThreshold), "EvasionMinRangeThreshold", AI.evasionMinRangeThreshold < 1000 ? $"{AI.evasionMinRangeThreshold:0}m" : $"{AI.evasionMinRangeThreshold / 1000:0}km");

                                    AI.evasionIgnoreMyTargetTargetingMe = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.evasionIgnoreMyTargetTargetingMe, StringUtils.Localize("#LOC_BDArmory_AI_EvasionIgnoreMyTargetTargetingMe"), AI.evasionIgnoreMyTargetTargetingMe ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                                    line += 1.25f;

                                    #region Craft Avoidance
                                    line += 0.5f;
                                    GUI.Label(SettinglabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Avoidance"), BoldLabel);
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.collisionAvoidanceThreshold, nameof(AI.collisionAvoidanceThreshold), "CollisionAvoidanceThreshold", $"{AI.collisionAvoidanceThreshold:0}m");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.vesselCollisionAvoidanceLookAheadPeriod, nameof(AI.vesselCollisionAvoidanceLookAheadPeriod), "CollisionAvoidanceLookAheadPeriod", $"{AI.vesselCollisionAvoidanceLookAheadPeriod:0.0}s");
                                    #endregion

                                    GUI.EndGroup();
                                    sectionHeights[Section.Evasion] = Mathf.Lerp(sectionHeight, line, 0.15f);
                                    line += 0.1f;
                                    contentHeight += line * entryHeight;
                                }

                                GUI.EndGroup();
                                GUI.EndScrollView();
                            }

                            if (infoLinkEnabled)
                            {
                                windowColumns = 3;

                                GUI.Label(new Rect(contentMargin + ColumnWidth * 2, contentTop, ColumnWidth - contentMargin, entryHeight), StringUtils.Localize("#LOC_BDArmory_AIWindow_infoLink"), Title);//"infolink"
                                BeginArea(new Rect(contentMargin + ColumnWidth * 2, contentTop + entryHeight * 1.5f, ColumnWidth - contentMargin, WindowHeight - entryHeight * 1.5f - 2 * contentTop));
                                using (var scrollViewScope = new ScrollViewScope(scrollInfoVector, Width(ColumnWidth - contentMargin), Height(WindowHeight - entryHeight * 1.5f - 2 * contentTop)))
                                {
                                    scrollInfoVector = scrollViewScope.scrollPosition;

                                    if (showSection[Section.PID]) { GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Orbital_PID"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); }
                                    if (showSection[Section.Combat]) { GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Orbital_Combat"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); }
                                    if (showSection[Section.Speed]) { GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Orbital_Speeds"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); }
                                    if (showSection[Section.Control]) { GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Orbital_Control"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); }
                                    if (showSection[Section.Evasion]) { GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Orbital_Evasion"), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); }
                                }
                                EndArea();
                            }
                        }
                        break;
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
                WindowHeight += Mouse.delta.y / BDArmorySettings.UI_SCALE;
                WindowHeight = Mathf.Max(WindowHeight, 305);
                if (BDArmorySettings.DEBUG_OTHER) GUI.Label(new Rect(WindowWidth / 2, WindowHeight - 26, WindowWidth / 2 - 26, 26), $"Resizing: {Mathf.Round(WindowHeight * BDArmorySettings.UI_SCALE)}", Label);
            }
            #endregion

            var previousWindowHeight = BDArmorySetup.WindowRectAI.height;
            BDArmorySetup.WindowRectAI.height = WindowHeight;
            BDArmorySetup.WindowRectAI.width = WindowWidth;
            GUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectAI, previousWindowHeight);
            GUIUtils.UpdateGUIRect(BDArmorySetup.WindowRectAI, _guiCheckIndex);
            GUIUtils.UseMouseEventInRect(BDArmorySetup.WindowRectAI);
        }
        #endregion GUI

        internal void OnDestroy()
        {
            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onVesselPartCountChanged.Remove(OnVesselModified);
            GameEvents.onPartDestroyed.Remove(OnPartDestroyed);
            GameEvents.onEditorLoad.Remove(OnEditorLoad);
            GameEvents.onEditorPartPlaced.Remove(OnEditorPartPlacedEvent);
            GameEvents.onEditorPartDeleted.Remove(OnEditorPartDeletedEvent);
        }
    }
}
