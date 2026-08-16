using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Utils;
using KSP.UI.Screens;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static UnityEngine.GUILayout;

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
        internal static bool autoResizingWindow = true;
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
        public bool checkForAI = false; // Flag to indicate that a new check for AI needs to happen (instead of responding to every event).

        int Drivertype = 0;
        int broadsideDir = 0;
        int pidMode = 0;
        int rollTowards = 0;
        public AIUtils.VehicleMovementType[] VehicleMovementTypes = (AIUtils.VehicleMovementType[])Enum.GetValues(typeof(AIUtils.VehicleMovementType)); // Get the VehicleMovementType as an array of enum values.
        public BDModuleOrbitalAI.PIDModeTypes[] PIDModeTypes = (BDModuleOrbitalAI.PIDModeTypes[])Enum.GetValues(typeof(BDModuleOrbitalAI.PIDModeTypes)); // Get the PID mode as an array of enum values.
        public BDModuleOrbitalAI.RollModeTypes[] RollModeTypes = (BDModuleOrbitalAI.RollModeTypes[])Enum.GetValues(typeof(BDModuleOrbitalAI.RollModeTypes)); // Get the roll mode as an array of enum values.

        public AIType activeAIType = AIType.None;
        public BDGenericAIBase ActiveAI; // Note: we don't use the usual ActiveController pattern as we need more control for the numeric input fields.
        List<IBDAIControl> AIs = []; // A list of the AIs for use in the Editor.

        Dictionary<AIType, Vector2> scrollViewVectors = [];
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
            if (!HighLogic.LoadedSceneIsFlight && !HighLogic.LoadedSceneIsEditor) return;
            StartCoroutine(ToolbarButtonRoutine());
        }
        IEnumerator ToolbarButtonRoutine()
        {
            if (buttonSetup) // Reconfigure the callbacks to use the current instance.
            {
                button.onTrue = ShowAIGUI;
                button.onFalse = HideAIGUI;
                yield break;
            }
            yield return new WaitUntil(() => ApplicationLauncher.Ready && BDArmorySetup.toolbarButtonAdded); // Wait until after the main BDA toolbar button.
            Texture buttonTexture = GameDatabase.Instance.GetTexture(BDArmorySetup.textureDir + "icon_ai", false);
            button = ApplicationLauncher.Instance.AddModApplication(ShowAIGUI, HideAIGUI, Dummy, Dummy, Dummy, Dummy, ApplicationLauncher.AppScenes.SPH | ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.FLIGHT, buttonTexture);
            buttonSetup = true;
            if (windowBDAAIGUIEnabled) button.SetTrue(false);
        }

        public void RemoveToolbarButton()
        {
            if (button == null) return;
            if (!HighLogic.LoadedSceneIsFlight && !HighLogic.LoadedSceneIsEditor) return;
            ApplicationLauncher.Instance.RemoveModApplication(button);
            button = null;
            buttonSetup = false;
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
            if (HighLogic.LoadedSceneIsFlight) GetAI();
            else GetAIEditor();
            if (button != null) button.SetTrue(false);
        }

        // Doing it this way prevents OnGUI events from below the window from being triggered by the window disappearing.
        public void HideAIGUI() => StartCoroutine(HideAIGUIAtEndOfFrame());
        bool waitingForEndOfFrame = false;
        IEnumerator HideAIGUIAtEndOfFrame()
        {
            if (waitingForEndOfFrame) yield break;
            waitingForEndOfFrame = true;
            yield return new WaitForEndOfFrame();
            waitingForEndOfFrame = false;
            HideAIGUINow();
        }
        void HideAIGUINow()
        {
            windowBDAAIGUIEnabled = false;
            GUIUtils.SetGUIRectVisible(_guiCheckIndex, windowBDAAIGUIEnabled);
            BDAWindowSettingsField.Save(); // Save window settings.
            if (button != null) button.SetFalse(false);
            if (HighLogic.LoadedSceneIsEditor) GUIUtils.PreventClickThrough(BDArmorySetup.WindowRectAI, "AIGUI lock", true);
            AIs.Clear();
            AISelectionComboBox = null;
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
            if (!windowBDAAIGUIEnabled) return;
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
            if (!windowBDAAIGUIEnabled || activeAIType == AIType.None) return;
            if (v == null) return;
            if (v.isActiveVessel && (ActiveAI == null || ActiveAI.vessel != v)) // Was an active vessel with an AI, but the AI is now gone or on another vessel.
            {
                activeAIType = AIType.None;
                checkForAI = true;
            }
        }

        void OnPartDestroyed(Part p)
        {
            if (!windowBDAAIGUIEnabled || activeAIType == AIType.None) return;
            if (ActiveAI == null) // We had an AI, but now it's gone...
            {
                activeAIType = AIType.None;
                checkForAI = true;
            }
        }

        void OnEditorLoad(ShipConstruct ship, CraftBrowserDialog.LoadType loadType)
        {
            GetAIEditor();
        }

        private void OnEditorPartPlacedEvent(Part p)
        {
            if (!windowBDAAIGUIEnabled) return;
            if (p == null) return;
            GetAIEditor(); // We need to check if we have a new AI or if the ordering has changed.
        }

        private void OnEditorPartDeletedEvent(Part p)
        {
            if (!windowBDAAIGUIEnabled) return;
            if (activeAIType != AIType.None) // If we had an active AI, we need to check to see if it's disappeared.
                GetAIEditor(); // We can't just check the part as it's now null.
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
            ActiveAI = null;
            activeAIType = AIType.None;
            inputFields = null;
            AISelectionComboBox = null;
            var tic = Time.time;
            if (FlightGlobals.ActiveVessel == null)
            {
                yield return new WaitUntilFixed(() => FlightGlobals.ActiveVessel != null || Time.time - tic > 1); // Give it up to a second to find the active vessel.
                if (FlightGlobals.ActiveVessel == null) yield break;
            }
            // Now, get the new AI and update stuff.
            ActiveAI = FlightGlobals.ActiveVessel.ActiveController().AI as BDGenericAIBase;
            activeAIType = ActiveAI == null ? AIType.None : ActiveAI.aiType;
            SetInputFields(activeAIType);
            SetChooseOptionSliders();
        }

        void GetAIEditor()
        {
            if (_getAIEditorCoroutine != null) StopCoroutine(_getAIEditorCoroutine);
            _getAIEditorCoroutine = StartCoroutine(GetAIEditorCoroutine());
        }
        Coroutine _getAIEditorCoroutine;
        IEnumerator GetAIEditorCoroutine()
        {
            AISelectionComboBox = null; // Clear the combobox to reset it.
            var tic = Time.time;
            if (EditorLogic.fetch.ship == null || EditorLogic.fetch.ship.Parts == null)
                yield return new WaitUntilFixed(() => (EditorLogic.fetch.ship != null && EditorLogic.fetch.ship.Parts != null) || Time.time - tic > 1); // Give it up to a second to find the editor ship and parts.
            var ship = EditorLogic.fetch.ship;
            if (ship != null && ship.Parts != null)
            {
                AIs = [.. ship.Parts.SelectMany(part => part.FindModulesImplementing<IBDAIControl>()).Where(ai => ai != null)];
                if (AIs.Count > 0)
                {
                    var rootPart = ship.Parts.First(); while (rootPart.parent != null) rootPart = rootPart.parent;
                    VesselModuleRegistry.SortByProximityToRootIBDAI(ref AIs, rootPart);
                    if (ActiveAI == AIs.First() as BDGenericAIBase) yield break; // It's the same AI, do nothing.
                    ActiveAI = AIs.First() as BDGenericAIBase; // Switch back to the primary AI.
                    activeAIType = ActiveAI.aiType;
                    SetInputFields(activeAIType);
                    SetChooseOptionSliders();
                    yield break;
                }
            }

            // No AIs were found, clear everything.
            activeAIType = AIType.None;
            ActiveAI = null;
            inputFields = null;
            AIs.Clear();
        }

        /// <summary>
        /// Get the proximity to the root part.
        /// </summary>
        /// <param name="part"></param>
        /// <returns>Proximity to the root part or int.MaxValue if not connected to the root part.</returns>
        public static int ProximityToRoot(Part part, Part root)
        {
            int proximity = 0;
            Part currentPart = part;
            while (currentPart != null && currentPart != root)
            {
                currentPart = currentPart.parent;
                ++proximity;
            }
            if (currentPart == null)
                return int.MaxValue;
            return proximity;
        }

        /// <summary>
        /// Get the value, minValue and maxValue of a UI_FloatRange derived control.
        /// </summary>
        /// <param name="aiType">The type of the AI.</param>
        /// <param name="gAI">The AI.</param>
        /// <param name="fieldName">The name of the field to look at.</param>
        /// <returns>value, minValue, maxValue, (rounding, sigFig, withZero)</returns>
        (float, float, float, (float, float, bool, bool)) GetAIFieldLimits(AIType aiType, BDGenericAIBase gAI, string fieldName)
        {
            float value = 0, minValue = 0, maxValue = 0, rounding = 0, sigFig = 0;
            bool withZero = false, reducedPrecisionAtMin = false;
            try
            {
                (float, float, float, float, bool, bool) GetLimits(UI_FloatRange uic)
                {
                    if (uic is UI_FloatSemiLogRange) (minValue, maxValue, rounding, sigFig, withZero, reducedPrecisionAtMin) = (uic as UI_FloatSemiLogRange).GetLimits();
                    else if (uic is UI_FloatLogRange) (minValue, maxValue, rounding, sigFig) = (uic as UI_FloatLogRange).GetLimits(); // (min, max, 0, steps)
                    else if (uic is UI_FloatPowerRange) (minValue, maxValue, rounding, sigFig) = (uic as UI_FloatPowerRange).GetLimits();
                    else
                    {
                        minValue = uic.minValue;
                        maxValue = uic.maxValue;
                        rounding = uic.stepIncrement;
                    }
                    return (minValue, maxValue, rounding, sigFig, withZero, reducedPrecisionAtMin);
                }
                switch (aiType)
                {
                    case AIType.PilotAI:
                        {
                            var AI = gAI as BDModulePilotAI;
                            var uic = (HighLogic.LoadedSceneIsFlight ? AI.Fields[fieldName].uiControlFlight : AI.Fields[fieldName].uiControlEditor) as UI_FloatRange;
                            (minValue, maxValue, rounding, sigFig, withZero, reducedPrecisionAtMin) = GetLimits(uic);
                            value = (float)typeof(BDModulePilotAI).GetField(fieldName).GetValue(AI);
                        }
                        break;
                    case AIType.SurfaceAI:
                        {
                            var AI = gAI as BDModuleSurfaceAI;
                            var uic = (HighLogic.LoadedSceneIsFlight ? AI.Fields[fieldName].uiControlFlight : AI.Fields[fieldName].uiControlEditor) as UI_FloatRange;
                            (minValue, maxValue, rounding, sigFig, withZero, reducedPrecisionAtMin) = GetLimits(uic);
                            value = (float)typeof(BDModuleSurfaceAI).GetField(fieldName).GetValue(AI);
                        }
                        break;
                    case AIType.VTOLAI:
                        {
                            var AI = gAI as BDModuleVTOLAI;
                            var uic = (HighLogic.LoadedSceneIsFlight ? AI.Fields[fieldName].uiControlFlight : AI.Fields[fieldName].uiControlEditor) as UI_FloatRange;
                            (minValue, maxValue, rounding, sigFig, withZero, reducedPrecisionAtMin) = GetLimits(uic);
                            value = (float)typeof(BDModuleVTOLAI).GetField(fieldName).GetValue(AI);
                        }
                        break;
                    case AIType.OrbitalAI:
                        {
                            var AI = gAI as BDModuleOrbitalAI;
                            var uic = (HighLogic.LoadedSceneIsFlight ? AI.Fields[fieldName].uiControlFlight : AI.Fields[fieldName].uiControlEditor) as UI_FloatRange;
                            (minValue, maxValue, rounding, sigFig, withZero, reducedPrecisionAtMin) = GetLimits(uic);
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
            return (value, minValue, maxValue, (rounding, sigFig, withZero, reducedPrecisionAtMin));
        }

        /// <summary>
        /// Get the current field limits from the inputFields data (which may change due to UpToEleven).
        /// Note: if the values require significant post-processing (e.g., Log10), then it may be better to just use fixed values.
        /// </summary>
        /// <param name="fieldName"></param>
        /// <returns>minValue, maxValue, rounding, sig.fig., with zero</returns>
        (float, float, float, float, bool, bool) GetFieldLimits(string fieldName)
        {
            if (!inputFields.ContainsKey(fieldName)) return (0, 0, 0, 0, false, false);
            var field = inputFields[fieldName];
            return ((float)field.minValue, (float)field.maxValue, field.rounding, field.sigFig, field.withZero, field.reducedPrecisionAtMin);
        }

        /// <summary>
        /// Set the inputFields entries for the given AI type.
        /// Note: only UI_FloatRange derived entries should be included here.
        /// </summary>
        /// <param name="aiType">The type of the currently active AI.</param>
        void SetInputFields(AIType aiType)
        {
            // Note: We use nameof(AI.field) to get the fieldname to avoid typos.
            switch (aiType)
            {
                case AIType.PilotAI:
                    {
                        var AI = ActiveAI as BDModulePilotAI;
                        if (AI == null)
                        {
                            Debug.LogError($"[BDArmory.BDArmoryAIGUI]: Mismatch between AI type and actual AI.");
                            activeAIType = AIType.None;
                            inputFields = null;
                            return;
                        }
                        inputFields = new List<string> {
                            nameof(AI.steerMult),
                            nameof(AI.steerKiAdjust),
                            nameof(AI.steerDamping),
                            nameof(AI.steerDampingPitch),
                            nameof(AI.steerDampingYaw),
                            nameof(AI.steerDampingRoll),
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
                            nameof(AI.threeAxisPIDPitchMult),
                            nameof(AI.threeAxisPIDPitchKi),
                            nameof(AI.threeAxisPIDPitchDamping),
                            nameof(AI.threeAxisPIDYawMult),
                            nameof(AI.threeAxisPIDYawKi),
                            nameof(AI.threeAxisPIDYawDamping),
                            nameof(AI.threeAxisPIDRollMult),
                            nameof(AI.threeAxisPIDRollKi),
                            nameof(AI.threeAxisPIDRollDamping),

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
                            nameof(AI.bombingAltitude),
                            nameof(AI.divebombingAngle),

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
                            nameof(AI.evasionMissileEmergencyNotchVel),
                            nameof(AI.collisionAvoidanceThreshold),
                            nameof(AI.vesselCollisionAvoidanceLookAheadPeriod),
                            nameof(AI.vesselCollisionAvoidanceStrength),
                            nameof(AI.vesselStandoffDistance),
                            nameof(AI.AvoidMass),
                            nameof(AI.retreatDistance),
                            nameof(AI.extendDistanceAirToAir),
                            nameof(AI.extendAngleAirToAir),
                            nameof(AI.extendDistanceAirToGroundGuns),
                            nameof(AI.extendDistanceBombing),
                            nameof(AI.extendTargetVel),
                            nameof(AI.extendTargetAngle),
                            nameof(AI.extendTargetDist),
                            nameof(AI.extendAbortTime),
                            nameof(AI.extendMinGainRate),

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
                case AIType.SurfaceAI:
                    {
                        var AI = ActiveAI as BDModuleSurfaceAI;
                        if (AI == null)
                        {
                            Debug.LogError($"[BDArmory.BDArmoryAIGUI]: Mismatch between AI type and actual AI.");
                            activeAIType = AIType.None;
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
                case AIType.VTOLAI:
                    {
                        var AI = ActiveAI as BDModuleVTOLAI;
                        if (AI == null)
                        {
                            Debug.LogError($"[BDArmory.BDArmoryAIGUI]: Mismatch between AI type and actual AI.");
                            activeAIType = AIType.None;
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
                            nameof(AI.AvoidMass),
                        }.ToDictionary(key => key, key =>
                        {
                            var (value, minValue, maxValue, meta) = GetAIFieldLimits(aiType, ActiveAI, key);
                            return gameObject.AddComponent<NumericInputField>().Initialise(0, value, minValue, maxValue, meta);
                        });
                        showSection[Section.UpToEleven] = AI.UpToEleven;
                    }
                    break;
                case AIType.OrbitalAI:
                    {
                        var AI = ActiveAI as BDModuleOrbitalAI;
                        if (AI == null)
                        {
                            Debug.LogError($"[BDArmory.BDArmoryAIGUI]: Mismatch between AI type and actual AI.");
                            activeAIType = AIType.None;
                            inputFields = null;
                            return;
                        }
                        inputFields = new List<string> {
                            nameof(AI.steerMult),
                            nameof(AI.steerKiAdjust),
                            nameof(AI.steerDamping),
                            nameof(AI.steerMaxError),
                            nameof(AI.MinEngagementRange),
                            nameof(AI.ForceFiringRange),
                            nameof(AI.ManeuverSpeed),
                            nameof(AI.minFiringSpeed),
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
                case AIType.PilotAI: SetInputFieldValues(ActiveAI as BDModulePilotAI, fromInputFields); break;
                case AIType.SurfaceAI: SetInputFieldValues(ActiveAI as BDModuleSurfaceAI, fromInputFields); break;
                case AIType.VTOLAI: SetInputFieldValues(ActiveAI as BDModuleVTOLAI, fromInputFields); break;
                case AIType.OrbitalAI: SetInputFieldValues(ActiveAI as BDModuleOrbitalAI, fromInputFields); break;
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
                        { fieldInfo.SetValue(AI, Convert.ChangeType(inputFields[field].CurrentValue, fieldInfo.FieldType)); }
                        else // Check if it's a property instead of a field.
                        {
                            var propInfo = AI.GetType().GetProperty(field);
                            propInfo.SetValue(AI, Convert.ChangeType(inputFields[field].CurrentValue, propInfo.PropertyType));
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
                case AIType.SurfaceAI:
                    {
                        var AI = ActiveAI as BDModuleSurfaceAI;
                        Drivertype = VehicleMovementTypes.IndexOf(AI.SurfaceType);
                        broadsideDir = AI.orbitDirections.IndexOf(AI.OrbitDirectionName);
                    }
                    break;
                case AIType.VTOLAI:
                    {
                        var AI = ActiveAI as BDModuleVTOLAI;
                        broadsideDir = AI.orbitDirections.IndexOf(AI.OrbitDirectionName);
                    }
                    break;
                case AIType.OrbitalAI:
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
            if (BDArmorySettings.UI_SCALE_ACTUAL != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, BDArmorySetup.WindowRectAI.position);
            BDArmorySetup.WindowRectAI = GUI.Window(GUIUtility.GetControlID(FocusType.Passive), BDArmorySetup.WindowRectAI, WindowAIGUI, "", BDArmorySetup.BDGuiSkin.window);//"BDA Weapon Manager"
            if (HighLogic.LoadedSceneIsFlight) BDArmorySetup.SetGUIOpacity(false);
        }

        void ConfigureStyles()
        {
            Label = new GUIStyle
            {
                alignment = TextAnchor.UpperLeft,
                normal = new GUIStyleState { textColor = Color.white }
            };

            contextLabelRight = new GUIStyle
            {
                alignment = TextAnchor.UpperRight,
                normal = new GUIStyleState { textColor = Color.white }
            };

            contextLabel = new GUIStyle(Label);

            BoldLabel = new GUIStyle
            {
                alignment = TextAnchor.UpperLeft,
                fontStyle = FontStyle.Bold,
                normal = new GUIStyleState { textColor = Color.white }
            };

            Title = new GUIStyle
            {
                font = BDArmorySetup.BDGuiSkin.window.font,
                fontSize = BDArmorySetup.BDGuiSkin.window.fontSize,
                fontStyle = BDArmorySetup.BDGuiSkin.window.fontStyle,
                alignment = TextAnchor.UpperCenter,
                normal = new GUIStyleState { textColor = BDArmorySetup.BDGuiSkin.window.normal.textColor }
            };

            infoLinkStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.label)
            {
                alignment = TextAnchor.UpperLeft,
                normal = new GUIStyleState { textColor = Color.white }
            };

            stylesConfigured = true;
        }

        enum Section { UpToEleven, PID, Altitude, Speed, Control, Evasion, Terrain, Ramming, Combat, Bombing, Misc, FixedAutoTuneFields, VehicleType }; // Sections and other important toggles.
        static Dictionary<Section, bool> showSection = Enum.GetValues(typeof(Section)).Cast<Section>().ToDictionary(s => s, s => false);
        readonly Dictionary<Section, float> sectionHeights = [];
        const float contentBorder = 0.2f * entryHeight;
        const float contentMargin = 10;
        const float contentInnerMargin = contentMargin + contentBorder;
        const float columnIndent = 100;
        const float labelWidth = 200;
        const float sliderIndent = contentInnerMargin + labelWidth;

        Rect TitleButtonRect(float offset, float width = 1) // offset is counted from the right edge of the window
        {
            return new Rect((ColumnWidth * 2) - _windowMargin - (offset * _buttonSize), _windowMargin, width * _buttonSize, _buttonSize);
        }
        Rect SubsectionRect(float line)
        {
            return new Rect(contentMargin, contentTop + line * entryHeight, columnIndent, entryHeight);
        }
        Rect SettinglabelRect(float lines)
        {
            return new Rect(contentInnerMargin, lines * entryHeight, labelWidth, entryHeight);
        }
        Rect SettingSliderRect(float lines, float contentWidth, int pos, int of, int span)
        {
            var width = (contentWidth - 2 * contentMargin - labelWidth) / of;
            return new Rect(sliderIndent + pos * width, (lines + 0.2f) * entryHeight, span * width, entryHeight);
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

        enum ContentType { FloatSlider, SemiLogSlider, FloatLogSlider, Toggle, Button };
        float ContentEntry(ContentType contentType, float line, float width, ref float value, string fieldName, string baseLOC, string formattedValue, bool splitContext = false)
        {
            switch (contentType)
            {
                case ContentType.FloatSlider:
                    {
                        GUI.Label(SettinglabelRect(line), StringUtils.Localize($"#LOC_BDArmory_AIWindow_{baseLOC}") + ": " + formattedValue, Label);
                        if (!NumFieldsEnabled)
                        {
                            var (min, max, rounding, _, _, _) = GetFieldLimits(fieldName);
                            if (value != (value = GUI.HorizontalSlider(SettingSliderRect(line, width), value, min, max)) && rounding > 0)
                                value = BDAMath.RoundToUnit(value, rounding);
                        }
                        else
                        {
                            var field = inputFields[fieldName];
                            field.TryParseValue(GUI.TextField(SettingTextRect(line, width), field.possibleValue, 8, field.style));
                            value = (float)field.CurrentValue;
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
                            var (min, max, rounding, sigFig, withZero, reducedPrecisionAtMin) = GetFieldLimits(fieldName);
                            if (!cacheSemiLogLimits.ContainsKey(fieldName)) { cacheSemiLogLimits[fieldName] = null; }
                            var cache = cacheSemiLogLimits[fieldName];
                            value = GUIUtils.HorizontalSemiLogSlider(SettingSliderRect(line, width), value, min, max, sigFig, withZero, reducedPrecisionAtMin, ref cache);
                            // Rounding is built into GUIUtils.HorizontalSemiLogSlider.
                        }
                        else
                        {
                            var field = inputFields[fieldName];
                            field.TryParseValue(GUI.TextField(SettingTextRect(line, width), field.possibleValue, 8, field.style));
                            value = (float)field.CurrentValue;
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
                case ContentType.FloatLogSlider:
                    {
                        GUI.Label(SettinglabelRect(line), StringUtils.Localize($"#LOC_BDArmory_AIWindow_{baseLOC}") + ": " + formattedValue, Label);
                        if (!NumFieldsEnabled)
                        {
                            var (min, max, _, steps, _, _) = GetFieldLimits(fieldName);
                            if (!cacheSemiLogLimits.ContainsKey(fieldName)) { cacheSemiLogLimits[fieldName] = null; }
                            var cache = cacheSemiLogLimits[fieldName];
                            value = GUIUtils.HorizontalFloatLogSlider(SettingSliderRect(line, width), value, min, max, (int)steps, ref cache);
                        }
                        else
                        {
                            var field = inputFields[fieldName];
                            field.TryParseValue(GUI.TextField(SettingTextRect(line, width), field.possibleValue, 8, field.style));
                            value = (float)field.CurrentValue;
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

        BDGUIComboBox AISelectionComboBox;
        int AISelectionIndex = -1;
        void UpdateAISelectionComboBox(Rect rect)
        {
            if (!(HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)) return;
            var primaryAI = (HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null) ? FlightGlobals.ActiveVessel.ActiveController().AI : null;
            var ais = HighLogic.LoadedSceneIsEditor ? AIs : VesselModuleRegistry.GetIBDAIControls(FlightGlobals.ActiveVessel);
            GUIContent[] listContent = [.. ais.Select(ai => new GUIContent(ai == primaryAI ? $"* {ai.aiType} *" : $"{ai.aiType}"))];
            if (listContent.Length > 0)
            {
                AISelectionComboBox = new BDGUIComboBox(rect, rect, new GUIContent(ActiveAI as IBDAIControl == primaryAI ? $"* {ActiveAI.aiType} *" : $"{ActiveAI.aiType}"), listContent, (listContent.Length + 1) * _buttonSize + 2 * _windowMargin, BDArmorySetup.ButtonStyle, 1);
                AISelectionIndex = AISelectionComboBox.SetSelectedItemIndex(ais.FindIndex(ai => ai.pilotEnabled));
            }
            else
                AISelectionComboBox = null;
        }

        void WindowAIGUI(int windowID)
        {
            if (HighLogic.LoadedSceneIsEditor) GUIUtils.PreventClickThrough(BDArmorySetup.WindowRectAI, "AIGUI lock");
            float windowColumns = 2;
            float contentIndent = contentMargin + columnIndent;
            float contentWidth = 2 * ColumnWidth - 2 * contentMargin - columnIndent;

            GUI.Label(new Rect(contentIndent, contentTop, contentWidth - 4 * _buttonSize, entryHeight), StringUtils.Localize("#LOC_BDArmory_AIWindow_title"), Title);
            #region AI Selection
            if (ActiveAI != null)
            {
                if (AISelectionComboBox == null) UpdateAISelectionComboBox(new Rect(contentMargin, _windowMargin, columnIndent, _buttonSize));
                if (AISelectionComboBox != null && AISelectionIndex != (AISelectionIndex = AISelectionComboBox.Show()))
                {
                    ActiveAI = (
                        HighLogic.LoadedSceneIsEditor ? AIs[AISelectionIndex]
                        : VesselModuleRegistry.GetIBDAIControls(FlightGlobals.ActiveVessel).Skip(AISelectionIndex).First()
                    ) as BDGenericAIBase;
                    activeAIType = ActiveAI.aiType;
                    SetInputFields(activeAIType);
                }
            }
            #endregion

            if (GUI.Button(TitleButtonRect(1), " X", BDArmorySetup.CloseButtonStyle)) //Exit Button
            {
                if (button) button.SetFalse();
                else HideAIGUI(); // In case the button is disabled.
            }
            if (GUI.Button(TitleButtonRect(3, 2), "Info", infoLinkEnabled ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)) //Infolink button
            { infoLinkEnabled = !infoLinkEnabled; }
            if (GUI.Button(TitleButtonRect(5, 2), "Help", contextTipsEnabled ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)) //Context labels button
            { contextTipsEnabled = !contextTipsEnabled; }
            if (GUI.Button(TitleButtonRect(6), "#", NumFieldsEnabled ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)) //Numeric fields button
            {
                NumFieldsEnabled = !NumFieldsEnabled;
                SyncInputFieldsNow(!NumFieldsEnabled);
            }

            float minHeight = 0;
            if (activeAIType == AIType.None || ActiveAI == null)
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
                    case AIType.PilotAI:
                        {
                            var AI = ActiveAI as BDModulePilotAI;
                            if (AI == null) { Debug.LogError($"[BDArmory.BDArmoryAIGUI]: AI module mismatch!"); activeAIType = AIType.None; break; }

                            if (AISelectionComboBox == null || !AISelectionComboBox.IsOpen)
                            { // Section buttons
                                float line = 1.5f;
                                showSection[Section.PID] = GUI.Toggle(SubsectionRect(line), showSection[Section.PID], StringUtils.Localize("#LOC_BDArmory_AIWindow_PID"), showSection[Section.PID] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"PiD"

                                line += 1.2f;
                                showSection[Section.Altitude] = GUI.Toggle(SubsectionRect(line), showSection[Section.Altitude], StringUtils.Localize("#LOC_BDArmory_AIWindow_Altitudes"), showSection[Section.Altitude] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Altitude"

                                line += 1.2f;
                                showSection[Section.Speed] = GUI.Toggle(SubsectionRect(line), showSection[Section.Speed], StringUtils.Localize("#LOC_BDArmory_AIWindow_Speeds"), showSection[Section.Speed] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Speed"

                                line += 1.2f;
                                showSection[Section.Control] = GUI.Toggle(SubsectionRect(line), showSection[Section.Control], StringUtils.Localize("#LOC_BDArmory_AIWindow_Control"), showSection[Section.Control] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Control"

                                line += 1.2f;
                                showSection[Section.Bombing] = GUI.Toggle(SubsectionRect(line), showSection[Section.Bombing], StringUtils.Localize("#LOC_BDArmory_AIWindow_Bombing"), showSection[Section.Bombing] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Bombing"

                                line += 1.2f;
                                showSection[Section.Evasion] = GUI.Toggle(SubsectionRect(line), showSection[Section.Evasion], StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeExtend"), showSection[Section.Evasion] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Evasion"

                                line += 1.2f;
                                showSection[Section.Terrain] = GUI.Toggle(SubsectionRect(line), showSection[Section.Terrain], StringUtils.Localize("#LOC_BDArmory_AIWindow_Terrain"), showSection[Section.Terrain] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Terrain"

                                line += 1.2f;
                                showSection[Section.Ramming] = GUI.Toggle(SubsectionRect(line), showSection[Section.Ramming], StringUtils.Localize("#LOC_BDArmory_AIWindow_Ramming"), showSection[Section.Ramming] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Ramming"

                                line += 1.2f;
                                showSection[Section.Misc] = GUI.Toggle(SubsectionRect(line), showSection[Section.Misc], StringUtils.Localize("#LOC_BDArmory_AIWindow_Misc"), showSection[Section.Misc] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Misc"

                                line += 1.5f;
                                if (showSection[Section.UpToEleven] != (AI.UpToEleven = GUI.Toggle(SubsectionRect(line), AI.UpToEleven,
                                    AI.UpToEleven ? StringUtils.Localize("#LOC_BDArmory_AI_UnclampTuning_enabledText") : StringUtils.Localize("#LOC_BDArmory_AI_UnclampTuning_disabledText"),
                                    AI.UpToEleven ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))//"Misc"
                                {
                                    SetInputFields(activeAIType);
                                }

                                #region Store/Restore
                                line += 1.5f;
                                GUIStyle saveStyle = BDArmorySetup.ButtonStyle;
                                if (GUI.Button(SubsectionRect(line), "Save", saveStyle))
                                {
                                    AI.StoreSettings();
                                }

                                if (AI.Events[nameof(AI.RestoreSettings)].active == true)
                                {
                                    line += 1f;
                                    GUIStyle restoreStyle = BDArmorySetup.ButtonStyle;
                                    if (GUI.Button(SubsectionRect(line), "Restore", restoreStyle))
                                    {
                                        AI.RestoreSettings();
                                    }
                                }
                                #endregion

                                minHeight = contentTop + (line + 1f) * entryHeight + _windowMargin;
                            }

                            if (showSection[Section.PID] || showSection[Section.Altitude] || showSection[Section.Speed] || showSection[Section.Control] || showSection[Section.Bombing] || showSection[Section.Evasion] || showSection[Section.Terrain] || showSection[Section.Ramming] || showSection[Section.Misc])
                            {
                                scrollViewVectors[AIType.PilotAI] = GUI.BeginScrollView(new Rect(contentIndent, contentTop + entryHeight * 1.5f, ColumnWidth * 2 - contentIndent, WindowHeight - entryHeight * 1.5f - 2 * contentTop), scrollViewVectors.GetValueOrDefault(AIType.PilotAI), new Rect(0, 0, contentWidth - contentMargin * 2, height + contentTop));

                                GUI.BeginGroup(new Rect(contentMargin, 0, contentWidth - contentMargin * 2, height + 2 * contentBorder), GUIContent.none, BDArmorySetup.SelectedButtonStyle); //darker box

                                contentWidth -= 24 + contentBorder;

                                if (showSection[Section.PID])
                                {
                                    float pidLines = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.PID);
                                    GUI.BeginGroup(new Rect(contentBorder, pidLines * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
                                    pidLines += 0.25f;

                                    GUI.Label(SettinglabelRect(pidLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_PID"), BoldLabel);
                                    if (AI.threeAxisPID)
                                    {
                                        if (AI.threeAxisPID != (AI.threeAxisPID = GUI.Toggle(ToggleButtonRects(pidLines, 0, 1, contentWidth), AI.threeAxisPID, StringUtils.Localize("#LOC_BDArmory_AI_3AxisPID"), AI.threeAxisPID ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        { AI.OnPIDTogglesChanged(); }
                                        pidLines += 1.25f;
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.threeAxisPIDPitchMult, nameof(AI.threeAxisPIDPitchMult), "3AxisPIDPitchMult", $"{AI.threeAxisPIDPitchMult:0.0}", splitContext: true);
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.threeAxisPIDPitchKi, nameof(AI.threeAxisPIDPitchKi), "3AxisPIDPitchKi", $"{AI.threeAxisPIDPitchKi:0.00}", splitContext: true);
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.threeAxisPIDPitchDamping, nameof(AI.threeAxisPIDPitchDamping), "3AxisPIDPitchDamping", $"{AI.threeAxisPIDPitchDamping:0.00}", splitContext: true);
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.threeAxisPIDYawMult, nameof(AI.threeAxisPIDYawMult), "3AxisPIDYawMult", $"{AI.threeAxisPIDYawMult:0.0}", splitContext: true);
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.threeAxisPIDYawKi, nameof(AI.threeAxisPIDYawKi), "3AxisPIDYawKi", $"{AI.threeAxisPIDYawKi:0.00}", splitContext: true);
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.threeAxisPIDYawDamping, nameof(AI.threeAxisPIDYawDamping), "3AxisPIDYawDamping", $"{AI.threeAxisPIDYawDamping:0.00}", splitContext: true);
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.threeAxisPIDRollMult, nameof(AI.threeAxisPIDRollMult), "3AxisPIDRollMult", $"{AI.threeAxisPIDRollMult:0.0}", splitContext: true);
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.threeAxisPIDRollKi, nameof(AI.threeAxisPIDRollKi), "3AxisPIDRollKi", $"{AI.threeAxisPIDRollKi:0.00}", splitContext: true);
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.threeAxisPIDRollDamping, nameof(AI.threeAxisPIDRollDamping), "3AxisPIDRollDamping", $"{AI.threeAxisPIDRollDamping:0.00}", splitContext: true);
                                    }
                                    else
                                    {
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.steerMult, nameof(AI.steerMult), "SteerPower", $"{AI.steerMult:0.0}", splitContext: true);
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.steerKiAdjust, nameof(AI.steerKiAdjust), "SteerKi", $"{AI.steerKiAdjust:0.00}", splitContext: true);
                                        if (!AI.threeAxisSteerDamping && !AI.dynamicSteerDamping)
                                        {
                                            pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.steerDamping, nameof(AI.steerDamping), "SteerDamping", $"{AI.steerDamping:0.00}", splitContext: true);
                                        }
                                        if (AI.threeAxisPID != (AI.threeAxisPID = GUI.Toggle(ToggleButtonRects(pidLines, 0, 3, contentWidth), AI.threeAxisPID, StringUtils.Localize("#LOC_BDArmory_AI_3AxisPID"), AI.threeAxisPID ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        { AI.OnPIDTogglesChanged(); }
                                        if (AI.threeAxisSteerDamping != (AI.threeAxisSteerDamping = GUI.Toggle(ToggleButtonRects(pidLines, 1, 3, contentWidth), AI.threeAxisSteerDamping, StringUtils.Localize("#LOC_BDArmory_AI_3AxisSteerDamping"), AI.threeAxisSteerDamping ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        { AI.OnPIDTogglesChanged(); }
                                        if (AI.dynamicSteerDamping != (AI.dynamicSteerDamping = GUI.Toggle(ToggleButtonRects(pidLines, 2, 3, contentWidth), AI.dynamicSteerDamping, StringUtils.Localize("#LOC_BDArmory_AI_DynamicDamping"), AI.dynamicSteerDamping ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        { AI.OnPIDTogglesChanged(); }
                                        pidLines += 1.25f;
                                        if (!AI.threeAxisSteerDamping && AI.dynamicSteerDamping)
                                        {
                                            GUI.Label(SettinglabelRect(pidLines++), StringUtils.Localize("#LOC_BDArmory_AI_DynamicDamping") + $": {AI.dynSteerDampingValue}", Label);
                                            pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.DynamicDampingMin, nameof(AI.DynamicDampingMin), "DynDampMin", $"{AI.DynamicDampingMin:0.0}");
                                            pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.DynamicDampingMax, nameof(AI.DynamicDampingMax), "DynDampMax", $"{AI.DynamicDampingMax:0.0}");
                                            pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.dynamicSteerDampingFactor, nameof(AI.dynamicSteerDampingFactor), "DynDampMult", $"{AI.dynamicSteerDampingFactor:0.0}");
                                        }
                                        if (AI.threeAxisSteerDamping)
                                        {
                                            // Pitch
                                            if (AI.dynamicSteerDamping)
                                            {
                                                if (AI.dynamicDampingPitch != (AI.dynamicDampingPitch = GUI.Toggle(ToggleButtonRect(pidLines, contentWidth), AI.dynamicDampingPitch, StringUtils.Localize("#LOC_BDArmory_AI_DynamicDampingPitch"), AI.dynamicDampingPitch ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                                { AI.OnPIDTogglesChanged(); }
                                                pidLines += 1.25f;
                                            }
                                            if (AI.dynamicSteerDamping && AI.dynamicDampingPitch)
                                            {
                                                GUI.Label(SettinglabelRect(pidLines++), StringUtils.Localize("#LOC_BDArmory_AI_DynamicDampingPitch") + $": {AI.dynSteerDampingPitchValue}", Label);
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.DynamicDampingPitchMin, nameof(AI.DynamicDampingPitchMin), "DynDampMin", $"{AI.DynamicDampingPitchMin:0.0}");
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.DynamicDampingPitchMax, nameof(AI.DynamicDampingPitchMax), "DynDampMax", $"{AI.DynamicDampingPitchMax:0.0}");
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.dynamicSteerDampingPitchFactor, nameof(AI.dynamicSteerDampingPitchFactor), "DynDampMult", $"{AI.dynamicSteerDampingPitchFactor:0.0}");
                                            }
                                            else
                                            {
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.steerDampingPitch, nameof(AI.steerDampingPitch), "SteerDampingPitch", $"{AI.steerDampingPitch:0.00}", splitContext: true);
                                            }
                                            // Yaw
                                            if (AI.dynamicSteerDamping)
                                            {
                                                if (AI.dynamicDampingYaw != (AI.dynamicDampingYaw = GUI.Toggle(ToggleButtonRect(pidLines, contentWidth), AI.dynamicDampingYaw, StringUtils.Localize("#LOC_BDArmory_AI_DynamicDampingYaw"), AI.dynamicDampingYaw ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                                { AI.OnPIDTogglesChanged(); }
                                                pidLines += 1.25f;
                                            }
                                            if (AI.dynamicSteerDamping && AI.dynamicDampingYaw)
                                            {
                                                GUI.Label(SettinglabelRect(pidLines++), StringUtils.Localize("#LOC_BDArmory_AI_DynamicDampingYaw") + $": {AI.dynSteerDampingYawValue}", Label);
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.DynamicDampingYawMin, nameof(AI.DynamicDampingYawMin), "DynDampMin", $"{AI.DynamicDampingYawMin:0.0}");
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.DynamicDampingYawMax, nameof(AI.DynamicDampingYawMax), "DynDampMax", $"{AI.DynamicDampingYawMax:0.0}");
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.dynamicSteerDampingYawFactor, nameof(AI.dynamicSteerDampingYawFactor), "DynDampMult", $"{AI.dynamicSteerDampingYawFactor:0.0}");
                                            }
                                            else
                                            {
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.steerDampingYaw, nameof(AI.steerDampingYaw), "SteerDampingYaw", $"{AI.steerDampingYaw:0.00}", splitContext: true);
                                            }
                                            // Roll
                                            if (AI.dynamicSteerDamping)
                                            {
                                                if (AI.dynamicDampingRoll != (AI.dynamicDampingRoll = GUI.Toggle(ToggleButtonRect(pidLines, contentWidth), AI.dynamicDampingRoll, StringUtils.Localize("#LOC_BDArmory_AI_DynamicDampingRoll"), AI.dynamicDampingRoll ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                                { AI.OnPIDTogglesChanged(); }
                                                pidLines += 1.25f;
                                            }
                                            if (AI.dynamicSteerDamping && AI.dynamicDampingRoll)
                                            {
                                                GUI.Label(SettinglabelRect(pidLines++), StringUtils.Localize("#LOC_BDArmory_AI_DynamicDampingRoll") + $": {AI.dynSteerDampingRollValue}", Label);
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.DynamicDampingRollMin, nameof(AI.DynamicDampingRollMin), "DynDampMin", $"{AI.DynamicDampingRollMin:0.0}");
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.DynamicDampingRollMax, nameof(AI.DynamicDampingRollMax), "DynDampMax", $"{AI.DynamicDampingRollMax:0.0}");
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.dynamicSteerDampingRollFactor, nameof(AI.dynamicSteerDampingRollFactor), "DynDampMult", $"{AI.dynamicSteerDampingRollFactor:0.0}");
                                            }
                                            else
                                            {
                                                pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.steerDampingRoll, nameof(AI.steerDampingRoll), "SteerDampingRoll", $"{AI.steerDampingRoll:0.00}", splitContext: true);
                                            }
                                        }
                                    }

                                    #region AutoTune
                                    if (AI.AutoTune != GUI.Toggle(ToggleButtonRect(pidLines, contentWidth), AI.AutoTune, StringUtils.Localize("#LOC_BDArmory_AI_PID_AutoTune"), AI.AutoTune ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
                                    {
                                        AI.AutoTune = !AI.AutoTune; // Only actually toggle it when needed as the setter does extra stuff.
                                    }
                                    pidLines += 1.25f;
                                    if (AI.AutoTune) // Auto-tuning
                                    {
                                        pidLines += 0.25f;
                                        if (HighLogic.LoadedSceneIsEditor)
                                        {
                                            if (!string.IsNullOrEmpty(AI.autoTuningLossLabel)) // Not auto-tuning, but have been previously => show a summary of the last results.
                                                GUI.Label(new Rect(contentInnerMargin + labelWidth / 8, pidLines++ * entryHeight, labelWidth, entryHeight),
                                                    StringUtils.Localize("#LOC_BDArmory_AI_PID_AutoTuning_Summary") + $":   {AI.autoTuningSummary}", Label);
                                        }
                                        else
                                        {
                                            GUI.Label(SettinglabelRect(pidLines++), StringUtils.Localize("#LOC_BDArmory_AI_PID_AutoTuning_Loss") + $": {AI.autoTuningLossLabel}", Label);
                                            GUI.Label(SettinglabelRect(pidLines++), $"\tParams: {AI.autoTuningLossLabel2}", Label);
                                            GUI.Label(SettinglabelRect(pidLines++), $"\tField: {AI.autoTuningLossLabel3}", Label);
                                        }

                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.autoTuningOptionNumSamples, nameof(AI.autoTuningOptionNumSamples), "PIDAutoTuningNumSamples", $"{AI.autoTuningOptionNumSamples:0}", splitContext: true);
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.autoTuningOptionFastResponseRelevance, nameof(AI.autoTuningOptionFastResponseRelevance), "PIDAutoTuningFastResponseRelevance", $"{AI.autoTuningOptionFastResponseRelevance:G3}", splitContext: true);
                                        pidLines = ContentEntry(ContentType.FloatLogSlider, pidLines, contentWidth, ref AI.autoTuningOptionInitialLearningRate, nameof(AI.autoTuningOptionInitialLearningRate), "PIDAutoTuningInitialLearningRate", $"{AI.autoTuningOptionInitialLearningRate:G3}");
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.autoTuningOptionInitialRollRelevance, nameof(AI.autoTuningOptionInitialRollRelevance), "PIDAutoTuningInitialRollRelevance", $"{AI.autoTuningOptionInitialRollRelevance:G3}");
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.autoTuningAltitude, nameof(AI.autoTuningAltitude), "PIDAutoTuningAltitude", $"{AI.autoTuningAltitude:0}");
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.autoTuningSpeed, nameof(AI.autoTuningSpeed), "PIDAutoTuningSpeed", $"{AI.autoTuningSpeed:0}");
                                        pidLines = ContentEntry(ContentType.FloatSlider, pidLines, contentWidth, ref AI.autoTuningRecenteringDistance, nameof(AI.autoTuningRecenteringDistance), "PIDAutoTuningRecenteringDistance", $"{AI.autoTuningRecenteringDistance:0}km");

                                        showSection[Section.FixedAutoTuneFields] = GUI.Toggle(ToggleButtonRects(pidLines, 0, 2, contentWidth), showSection[Section.FixedAutoTuneFields], StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixedFields"), showSection[Section.FixedAutoTuneFields] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);
                                        AI.autoTuningOptionClampMaximums = GUI.Toggle(ToggleButtonRects(pidLines, 1, 2, contentWidth), AI.autoTuningOptionClampMaximums, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningClampMaximums"), AI.autoTuningOptionClampMaximums ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);
                                        pidLines += 1.25f;

                                        if (showSection[Section.FixedAutoTuneFields])
                                        {
                                            bool resetAutoTuning = false;
                                            if (AI.threeAxisPID) // Full 3-Axis PID
                                            {
                                                if (AI.autoTuningOptionFixedPp != (AI.autoTuningOptionFixedPp = GUI.Toggle(ToggleButtonRects(pidLines, 0, 9, contentWidth), AI.autoTuningOptionFixedPp, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_Pp"), AI.autoTuningOptionFixedPp ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                if (AI.autoTuningOptionFixedIp != (AI.autoTuningOptionFixedIp = GUI.Toggle(ToggleButtonRects(pidLines, 1, 9, contentWidth), AI.autoTuningOptionFixedIp, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_Ip"), AI.autoTuningOptionFixedIp ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                if (AI.autoTuningOptionFixedDp != (AI.autoTuningOptionFixedDp = GUI.Toggle(ToggleButtonRects(pidLines, 2, 9, contentWidth), AI.autoTuningOptionFixedDp, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_Dp"), AI.autoTuningOptionFixedDp ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                if (AI.autoTuningOptionFixedPy != (AI.autoTuningOptionFixedPy = GUI.Toggle(ToggleButtonRects(pidLines, 3, 9, contentWidth), AI.autoTuningOptionFixedPy, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_Py"), AI.autoTuningOptionFixedPy ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                if (AI.autoTuningOptionFixedIy != (AI.autoTuningOptionFixedIy = GUI.Toggle(ToggleButtonRects(pidLines, 4, 9, contentWidth), AI.autoTuningOptionFixedIy, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_Iy"), AI.autoTuningOptionFixedIy ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                if (AI.autoTuningOptionFixedDy != (AI.autoTuningOptionFixedDy = GUI.Toggle(ToggleButtonRects(pidLines, 5, 9, contentWidth), AI.autoTuningOptionFixedDy, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_Dy"), AI.autoTuningOptionFixedDy ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                if (AI.autoTuningOptionFixedPr != (AI.autoTuningOptionFixedPr = GUI.Toggle(ToggleButtonRects(pidLines, 6, 9, contentWidth), AI.autoTuningOptionFixedPr, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_Pr"), AI.autoTuningOptionFixedPr ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                if (AI.autoTuningOptionFixedIr != (AI.autoTuningOptionFixedIr = GUI.Toggle(ToggleButtonRects(pidLines, 7, 9, contentWidth), AI.autoTuningOptionFixedIr, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_Ir"), AI.autoTuningOptionFixedIr ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                if (AI.autoTuningOptionFixedDr != (AI.autoTuningOptionFixedDr = GUI.Toggle(ToggleButtonRects(pidLines, 8, 9, contentWidth), AI.autoTuningOptionFixedDr, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_Dr"), AI.autoTuningOptionFixedDr ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                            }
                                            else if (!AI.dynamicSteerDamping)
                                            {
                                                if (!AI.threeAxisSteerDamping) // Normal PID
                                                {
                                                    if (AI.autoTuningOptionFixedP != (AI.autoTuningOptionFixedP = GUI.Toggle(ToggleButtonRects(pidLines, 0, 3, contentWidth), AI.autoTuningOptionFixedP, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_P"), AI.autoTuningOptionFixedP ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                    if (AI.autoTuningOptionFixedI != (AI.autoTuningOptionFixedI = GUI.Toggle(ToggleButtonRects(pidLines, 1, 3, contentWidth), AI.autoTuningOptionFixedI, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_I"), AI.autoTuningOptionFixedI ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                    if (AI.autoTuningOptionFixedD != (AI.autoTuningOptionFixedD = GUI.Toggle(ToggleButtonRects(pidLines, 2, 3, contentWidth), AI.autoTuningOptionFixedD, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_D"), AI.autoTuningOptionFixedD ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                }
                                                else // PID with 3-axis static damping
                                                {
                                                    if (AI.autoTuningOptionFixedP != (AI.autoTuningOptionFixedP = GUI.Toggle(ToggleButtonRects(pidLines, 0, 5, contentWidth), AI.autoTuningOptionFixedP, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_P"), AI.autoTuningOptionFixedP ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                    if (AI.autoTuningOptionFixedI != (AI.autoTuningOptionFixedI = GUI.Toggle(ToggleButtonRects(pidLines, 1, 5, contentWidth), AI.autoTuningOptionFixedI, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_I"), AI.autoTuningOptionFixedI ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                    if (AI.autoTuningOptionFixedDP != (AI.autoTuningOptionFixedDP = GUI.Toggle(ToggleButtonRects(pidLines, 2, 5, contentWidth), AI.autoTuningOptionFixedDP, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_DPitch"), AI.autoTuningOptionFixedDP ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                    if (AI.autoTuningOptionFixedDY != (AI.autoTuningOptionFixedDY = GUI.Toggle(ToggleButtonRects(pidLines, 3, 5, contentWidth), AI.autoTuningOptionFixedDY, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_DYaw"), AI.autoTuningOptionFixedDY ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                    if (AI.autoTuningOptionFixedDR != (AI.autoTuningOptionFixedDR = GUI.Toggle(ToggleButtonRects(pidLines, 4, 5, contentWidth), AI.autoTuningOptionFixedDR, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_DRoll"), AI.autoTuningOptionFixedDR ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                }
                                            }
                                            else
                                            {
                                                if (!AI.threeAxisSteerDamping) // PID with dynamic damping
                                                {
                                                    if (AI.autoTuningOptionFixedP != (AI.autoTuningOptionFixedP = GUI.Toggle(ToggleButtonRects(pidLines, 0, 5, contentWidth), AI.autoTuningOptionFixedP, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_P"), AI.autoTuningOptionFixedP ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                    if (AI.autoTuningOptionFixedI != (AI.autoTuningOptionFixedI = GUI.Toggle(ToggleButtonRects(pidLines, 1, 5, contentWidth), AI.autoTuningOptionFixedI, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_I"), AI.autoTuningOptionFixedI ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                    if (AI.autoTuningOptionFixedDOff != (AI.autoTuningOptionFixedDOff = GUI.Toggle(ToggleButtonRects(pidLines, 2, 5, contentWidth), AI.autoTuningOptionFixedDOff, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_D_OffTarget"), AI.autoTuningOptionFixedDOff ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                    if (AI.autoTuningOptionFixedDOn != (AI.autoTuningOptionFixedDOn = GUI.Toggle(ToggleButtonRects(pidLines, 3, 5, contentWidth), AI.autoTuningOptionFixedDOn, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_D_OnTarget"), AI.autoTuningOptionFixedDOn ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                    if (AI.autoTuningOptionFixedDF != (AI.autoTuningOptionFixedDF = GUI.Toggle(ToggleButtonRects(pidLines, 4, 5, contentWidth), AI.autoTuningOptionFixedDF, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_D_Factor"), AI.autoTuningOptionFixedDF ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                }
                                                else // PID with 3-axis dynamic damping (or mixed)
                                                {
                                                    int buttonCount = 2 + (AI.dynamicDampingPitch ? 3 : 1) + (AI.dynamicDampingRoll ? 3 : 1) + (AI.dynamicDampingYaw ? 3 : 1);
                                                    int buttonIndex = -1;
                                                    if (AI.autoTuningOptionFixedP != (AI.autoTuningOptionFixedP = GUI.Toggle(ToggleButtonRects(pidLines, ++buttonIndex, buttonCount, contentWidth), AI.autoTuningOptionFixedP, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_P"), AI.autoTuningOptionFixedP ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                    if (AI.autoTuningOptionFixedI != (AI.autoTuningOptionFixedI = GUI.Toggle(ToggleButtonRects(pidLines, ++buttonIndex, buttonCount, contentWidth), AI.autoTuningOptionFixedI, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_I"), AI.autoTuningOptionFixedI ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                    if (AI.dynamicDampingPitch)
                                                    {
                                                        if (AI.autoTuningOptionFixedDPOff != (AI.autoTuningOptionFixedDPOff = GUI.Toggle(ToggleButtonRects(pidLines, ++buttonIndex, buttonCount, contentWidth), AI.autoTuningOptionFixedDPOff, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_DPitch_OffTarget"), AI.autoTuningOptionFixedDPOff ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                        if (AI.autoTuningOptionFixedDPOn != (AI.autoTuningOptionFixedDPOn = GUI.Toggle(ToggleButtonRects(pidLines, ++buttonIndex, buttonCount, contentWidth), AI.autoTuningOptionFixedDPOn, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_DPitch_OnTarget"), AI.autoTuningOptionFixedDPOn ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                        if (AI.autoTuningOptionFixedDPF != (AI.autoTuningOptionFixedDPF = GUI.Toggle(ToggleButtonRects(pidLines, ++buttonIndex, buttonCount, contentWidth), AI.autoTuningOptionFixedDPF, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_DPitch_Factor"), AI.autoTuningOptionFixedDPF ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                    }
                                                    else
                                                    {
                                                        if (AI.autoTuningOptionFixedDP != (AI.autoTuningOptionFixedDP = GUI.Toggle(ToggleButtonRects(pidLines, ++buttonIndex, buttonCount, contentWidth), AI.autoTuningOptionFixedDP, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_DPitch"), AI.autoTuningOptionFixedDP ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                    }
                                                    if (AI.dynamicDampingYaw)
                                                    {
                                                        if (AI.autoTuningOptionFixedDYOff != (AI.autoTuningOptionFixedDYOff = GUI.Toggle(ToggleButtonRects(pidLines, ++buttonIndex, buttonCount, contentWidth), AI.autoTuningOptionFixedDYOff, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_DYaw_OffTarget"), AI.autoTuningOptionFixedDYOff ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                        if (AI.autoTuningOptionFixedDYOn != (AI.autoTuningOptionFixedDYOn = GUI.Toggle(ToggleButtonRects(pidLines, ++buttonIndex, buttonCount, contentWidth), AI.autoTuningOptionFixedDYOn, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_DYaw_OnTarget"), AI.autoTuningOptionFixedDYOn ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                        if (AI.autoTuningOptionFixedDYF != (AI.autoTuningOptionFixedDYF = GUI.Toggle(ToggleButtonRects(pidLines, ++buttonIndex, buttonCount, contentWidth), AI.autoTuningOptionFixedDYF, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_DYaw_Factor"), AI.autoTuningOptionFixedDYF ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                    }
                                                    else
                                                    {
                                                        if (AI.autoTuningOptionFixedDY != (AI.autoTuningOptionFixedDY = GUI.Toggle(ToggleButtonRects(pidLines, ++buttonIndex, buttonCount, contentWidth), AI.autoTuningOptionFixedDY, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_DYaw"), AI.autoTuningOptionFixedDY ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                    }
                                                    if (AI.dynamicDampingRoll)
                                                    {
                                                        if (AI.autoTuningOptionFixedDROff != (AI.autoTuningOptionFixedDROff = GUI.Toggle(ToggleButtonRects(pidLines, ++buttonIndex, buttonCount, contentWidth), AI.autoTuningOptionFixedDROff, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_DRoll_OffTarget"), AI.autoTuningOptionFixedDROff ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                        if (AI.autoTuningOptionFixedDROn != (AI.autoTuningOptionFixedDROn = GUI.Toggle(ToggleButtonRects(pidLines, ++buttonIndex, buttonCount, contentWidth), AI.autoTuningOptionFixedDROn, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_DRoll_OnTarget"), AI.autoTuningOptionFixedDROn ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                        if (AI.autoTuningOptionFixedDRF != (AI.autoTuningOptionFixedDRF = GUI.Toggle(ToggleButtonRects(pidLines, ++buttonIndex, buttonCount, contentWidth), AI.autoTuningOptionFixedDRF, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_DRoll_Factor"), AI.autoTuningOptionFixedDRF ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                    }
                                                    else
                                                    {
                                                        if (AI.autoTuningOptionFixedDR != (AI.autoTuningOptionFixedDR = GUI.Toggle(ToggleButtonRects(pidLines, ++buttonIndex, buttonCount, contentWidth), AI.autoTuningOptionFixedDR, StringUtils.Localize("#LOC_BDArmory_AIWindow_PIDAutoTuningFixed_DRoll"), AI.autoTuningOptionFixedDR ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) resetAutoTuning = true;
                                                    }
                                                }
                                            }
                                            if (resetAutoTuning && HighLogic.LoadedSceneIsFlight) AI.pidAutoTuning.ResetGradient();
                                            pidLines += 1.25f;
                                        }
                                    }
                                    else if (!string.IsNullOrEmpty(AI.autoTuningLossLabel)) // Not auto-tuning, but have been previously => show a summary of the last results.
                                    {
                                        GUI.Label(new Rect(contentInnerMargin + labelWidth / 8, pidLines * entryHeight, labelWidth, entryHeight),
                                            StringUtils.Localize("#LOC_BDArmory_AI_PID_AutoTuning_Summary") + $":   {AI.autoTuningSummary}", Label);
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
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + altLines * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
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

                                    if (AI.hardMinAltitude != (AI.hardMinAltitude = GUI.Toggle(ToggleButtonRects(altLines, 0, 2, contentWidth), AI.hardMinAltitude,
                                    StringUtils.Localize("#LOC_BDArmory_AI_HardMinAltitude"), AI.hardMinAltitude ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))//"Hard Min Altitude"
                                        AI.UpdateToggle(AI.Fields[nameof(AI.hardMinAltitude)]);
                                    if (AI.maxAltitudeToggle != (AI.maxAltitudeToggle = GUI.Toggle(ToggleButtonRects(altLines, 1, 2, contentWidth), AI.maxAltitudeToggle,
                                    StringUtils.Localize("#LOC_BDArmory_AIWindow_MaxAltitude"), AI.maxAltitudeToggle ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))//"max altitude AGL"
                                        AI.UpdateToggle(AI.Fields[nameof(AI.maxAltitudeToggle)]);
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
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + spdLines * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
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
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + ctrlLines * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
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
                                if (showSection[Section.Bombing])
                                {
                                    float bombingLines = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Combat);
                                    GUI.BeginGroup(new(contentBorder, contentHeight + bombingLines * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
                                    bombingLines += 0.25f;
                                    if (AI.divebombing != (AI.divebombing = GUI.Toggle(ToggleButtonRects(bombingLines, 0, 2, contentWidth), AI.divebombing, StringUtils.Localize("#LOC_BDArmory_AIWindow_DiveBomb"), AI.divebombing ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        AI.UpdateToggle(AI.Fields[nameof(AI.divebombing)]);
                                    bombingLines += 1.25f;

                                    bombingLines = ContentEntry(ContentType.FloatSlider, bombingLines, contentWidth, ref AI.bombingAltitude, nameof(AI.bombingAltitude), "BombingAltitude", $"{AI.bombingAltitude:0}m");

                                    if (AI.divebombing)
                                    {
                                        bombingLines = ContentEntry(ContentType.FloatSlider, bombingLines, contentWidth, ref AI.divebombingAngle, nameof(AI.divebombingAngle), "DiveBombingAngle", $"{AI.divebombingAngle:0}°");

                                        GUI.Label(SettinglabelRect(bombingLines), $"{StringUtils.Localize($"#LOC_BDArmory_AIWindow_DiveBombingOvershootPID")}:", Label);
                                        if (BDArmorySettings.DEBUG_TELEMETRY) AI.divebombCorrection.debug = GUI.Toggle(new Rect(contentInnerMargin + labelWidth * 2 / 3, bombingLines * entryHeight, labelWidth / 3, entryHeight), AI.divebombCorrection.debug, "Debug", AI.divebombCorrection.debug ? BoldLabel : Label);
                                        GUI.Label(SettingSliderRect(bombingLines, contentWidth, 0, 9, 1), $"P:{AI.divebombCorrection.P:0.0}", Label);
                                        AI.divebombCorrection.P = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(bombingLines, contentWidth, 1, 9, 2), AI.divebombCorrection.P, 0, 10), 0.1f);
                                        GUI.Label(SettingSliderRect(bombingLines, contentWidth, 3, 9, 1), $"I:{AI.divebombCorrection.I:0.0}", Label);
                                        AI.divebombCorrection.I = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(bombingLines, contentWidth, 4, 9, 2), AI.divebombCorrection.I, 0, 1), 0.1f);
                                        GUI.Label(SettingSliderRect(bombingLines, contentWidth, 6, 9, 1), $"D:{AI.divebombCorrection.D:0.0}", Label);
                                        AI.divebombCorrection.D = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(bombingLines, contentWidth, 7, 9, 2), AI.divebombCorrection.D, 0, 1), 0.1f);
                                        ++bombingLines;
                                    }

                                    GUI.Label(SettinglabelRect(bombingLines), $"{StringUtils.Localize($"#LOC_BDArmory_AIWindow_BombingLateralPID")}:", Label);
                                    if (BDArmorySettings.DEBUG_TELEMETRY) AI.bombingLateralCorrection.debug = GUI.Toggle(new Rect(contentInnerMargin + labelWidth * 2 / 3, bombingLines * entryHeight, labelWidth / 3, entryHeight), AI.bombingLateralCorrection.debug, "Debug", AI.bombingLateralCorrection.debug ? BoldLabel : Label);
                                    GUI.Label(SettingSliderRect(bombingLines, contentWidth, 0, 9, 1), $"P:{AI.bombingLateralCorrection.P:0.0}", Label);
                                    AI.bombingLateralCorrection.P = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(bombingLines, contentWidth, 1, 9, 2), AI.bombingLateralCorrection.P, 0, 2), 0.1f);
                                    GUI.Label(SettingSliderRect(bombingLines, contentWidth, 3, 9, 1), $"I:{AI.bombingLateralCorrection.I:0.0}", Label);
                                    AI.bombingLateralCorrection.I = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(bombingLines, contentWidth, 4, 9, 2), AI.bombingLateralCorrection.I, 0, 1), 0.1f);
                                    GUI.Label(SettingSliderRect(bombingLines, contentWidth, 6, 9, 1), $"D:{AI.bombingLateralCorrection.D:0.0}", Label);
                                    AI.bombingLateralCorrection.D = BDAMath.RoundToUnit(GUI.HorizontalSlider(SettingSliderRect(bombingLines, contentWidth, 7, 9, 2), AI.bombingLateralCorrection.D, 0, 1), 0.1f);
                                    ++bombingLines;

                                    bombingLines = ContentEntry(ContentType.FloatSlider, bombingLines, contentWidth, ref AI.extendDistanceBombing, nameof(AI.extendDistanceBombing), "BombingExtendDistance", $"{AI.extendDistanceBombing:0}m");

                                    GUI.EndGroup();
                                    sectionHeights[Section.Combat] = Mathf.Lerp(sectionHeight, bombingLines, 0.15f);
                                    bombingLines += 0.1f;
                                    contentHeight += bombingLines * entryHeight;
                                }
                                if (showSection[Section.Evasion])
                                {
                                    float evadeLines = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Evasion);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + evadeLines * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
                                    evadeLines += 0.25f;

                                    #region Evasion
                                    GUI.Label(SettinglabelRect(evadeLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Evade"), BoldLabel);
                                    evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.minEvasionTime, nameof(AI.minEvasionTime), "MinEvasionTime", $"{AI.minEvasionTime:0.00}s");
                                    evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.evasionThreshold, nameof(AI.evasionThreshold), "EvasionThreshold", $"{AI.evasionThreshold:0}m");
                                    evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.evasionTimeThreshold, nameof(AI.evasionTimeThreshold), "EvasionTimeThreshold", $"{AI.evasionTimeThreshold:0.00}s");
                                    evadeLines = ContentEntry(ContentType.SemiLogSlider, evadeLines, contentWidth, ref AI.evasionMinRangeThreshold, nameof(AI.evasionMinRangeThreshold), "EvasionMinRangeThreshold", AI.evasionMinRangeThreshold < 1000 ? $"{AI.evasionMinRangeThreshold:0}m" : $"{AI.evasionMinRangeThreshold / 1000:0}km");
                                    evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.evasionNonlinearity, nameof(AI.evasionNonlinearity), "EvasionNonlinearity", $"{AI.evasionNonlinearity:0.0}°");

                                    if (AI.evasionIgnoreMyTargetTargetingMe != (AI.evasionIgnoreMyTargetTargetingMe = GUI.Toggle(ToggleButtonRect(evadeLines, contentWidth), AI.evasionIgnoreMyTargetTargetingMe, StringUtils.Localize("#LOC_BDArmory_AI_EvasionIgnoreMyTargetTargetingMe"), AI.evasionIgnoreMyTargetTargetingMe ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        AI.UpdateToggle(AI.Fields[nameof(AI.evasionIgnoreMyTargetTargetingMe)]);
                                    evadeLines += 1.25f;

                                    if (AI.evasionMissileKinematic != (AI.evasionMissileKinematic = GUI.Toggle(ToggleButtonRect(evadeLines, contentWidth), AI.evasionMissileKinematic, StringUtils.Localize("#LOC_BDArmory_AI_EvasionMissileKinematic"), AI.evasionMissileKinematic ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        AI.UpdateToggle(AI.Fields[nameof(AI.evasionMissileKinematic)]);
                                    evadeLines += 1.25f;
                                    evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.evasionMissileEmergencyNotchVel, nameof(AI.evasionMissileEmergencyNotchVel), "EvasionMissileEmergencyNotch", $"{AI.evasionMissileEmergencyNotchVel:0.0} m/s");
                                    #endregion

                                    #region Craft Avoidance
                                    evadeLines += 0.5f;
                                    GUI.Label(SettinglabelRect(evadeLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Avoidance"), BoldLabel);
                                    evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.collisionAvoidanceThreshold, nameof(AI.collisionAvoidanceThreshold), "CollisionAvoidanceThreshold", $"{AI.collisionAvoidanceThreshold:0}m");
                                    evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.vesselCollisionAvoidanceLookAheadPeriod, nameof(AI.vesselCollisionAvoidanceLookAheadPeriod), "CollisionAvoidanceLookAheadPeriod", $"{AI.vesselCollisionAvoidanceLookAheadPeriod:0.0}s");
                                    evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.vesselCollisionAvoidanceStrength, nameof(AI.vesselCollisionAvoidanceStrength), "CollisionAvoidanceStrength", $"{AI.vesselCollisionAvoidanceStrength:0.0} ({AI.vesselCollisionAvoidanceStrength / Time.fixedDeltaTime:0}°/s)");
                                    evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.vesselStandoffDistance, nameof(AI.vesselStandoffDistance), "StandoffDistance", $"{AI.vesselStandoffDistance:0}m");
                                    evadeLines = ContentEntry(ContentType.SemiLogSlider, evadeLines, contentWidth, ref AI.AvoidMass, nameof(AI.AvoidMass), "MinObstacleMass", AI.AvoidMass < 10 ? $"{AI.AvoidMass:0.0}t" : $"{AI.AvoidMass:0}t");
                                    evadeLines = ContentEntry(ContentType.SemiLogSlider, evadeLines, contentWidth, ref AI.retreatDistance, nameof(AI.retreatDistance), "RetreatDistance", $"{0.001f * AI.retreatDistance:G3}km");
                                    #endregion

                                    #region Extending
                                    if (AI.canExtend)
                                    {
                                        evadeLines += 0.5f;
                                        GUI.Label(SettinglabelRect(evadeLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Extend"), BoldLabel);
                                        evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.extendDistanceAirToAir, nameof(AI.extendDistanceAirToAir), "ExtendDistanceAirToAir", $"{AI.extendDistanceAirToAir:0}m");
                                        evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.extendAngleAirToAir, nameof(AI.extendAngleAirToAir), "ExtendAngleAirToAir", $"{AI.extendAngleAirToAir:0}°");
                                        evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.extendDistanceAirToGroundGuns, nameof(AI.extendDistanceAirToGroundGuns), "ExtendDistanceAirToGroundGuns", $"{AI.extendDistanceAirToGroundGuns:0}m");
                                        evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.extendTargetVel, nameof(AI.extendTargetVel), "ExtendTargetVel", $"{AI.extendTargetVel:0.0}");
                                        evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.extendTargetAngle, nameof(AI.extendTargetAngle), "ExtendTargetAngle", $"{AI.extendTargetAngle:0}°");
                                        evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.extendTargetDist, nameof(AI.extendTargetDist), "ExtendTargetDist", $"{AI.extendTargetDist:0}m");
                                        evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.extendAbortTime, nameof(AI.extendAbortTime), "ExtendAbortTime", $"{AI.extendAbortTime:0}s");
                                        evadeLines = ContentEntry(ContentType.FloatSlider, evadeLines, contentWidth, ref AI.extendMinGainRate, nameof(AI.extendMinGainRate), "ExtendMinGainRate", $"{AI.extendMinGainRate:0}m/s");
                                    }
                                    if (AI.canExtend != (AI.canExtend = GUI.Toggle(ToggleButtonRect(evadeLines, contentWidth), AI.canExtend, StringUtils.Localize("#LOC_BDArmory_AI_ExtendToggle"), AI.canExtend ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))//"Dynamic pid"
                                        AI.UpdateToggle(AI.Fields[nameof(AI.canExtend)]);
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
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + gndLines * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
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
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + ramLines * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
                                    ramLines += 0.25f;

                                    GUI.Label(SettinglabelRect(ramLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Ramming"), BoldLabel);//"Ramming"

                                    if (AI.allowRamming != (AI.allowRamming = GUI.Toggle(ToggleButtonRect(ramLines, contentWidth), AI.allowRamming,
                                        StringUtils.Localize("#LOC_BDArmory_AI_AllowRamming"), AI.allowRamming ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))//"Allow Ramming"
                                        AI.UpdateToggle(AI.Fields[nameof(AI.allowRamming)]);
                                    ramLines += 1.25f;

                                    if (AI.allowRamming)
                                    {
                                        if (AI.allowRammingGroundTargets != (AI.allowRammingGroundTargets = GUI.Toggle(ToggleButtonRect(ramLines, contentWidth), AI.allowRammingGroundTargets,
                                            StringUtils.Localize("#LOC_BDArmory_AI_AllowRammingGroundTargets"), AI.allowRammingGroundTargets ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))//"Include Ground Targets"
                                            AI.UpdateToggle(AI.Fields[nameof(AI.allowRammingGroundTargets)]);
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
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + miscLines * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
                                    miscLines += 0.25f;

                                    GUI.Label(SettinglabelRect(miscLines++), StringUtils.Localize("#LOC_BDArmory_AI_Orbit"), BoldLabel);//"orbit"
                                    if (AI.ClockwiseOrbit != (AI.ClockwiseOrbit = GUI.Toggle(ToggleButtonRect(miscLines, contentWidth), AI.ClockwiseOrbit,
                                        AI.ClockwiseOrbit ? StringUtils.Localize("#LOC_BDArmory_AI_Orbit_Starboard") : StringUtils.Localize("#LOC_BDArmory_AI_Orbit_Port"),
                                        AI.ClockwiseOrbit ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        AI.UpdateToggle(AI.Fields[nameof(AI.ClockwiseOrbit)]);
                                    miscLines += 1.25f;
                                    if (contextTipsEnabled) GUI.Label(ContextLabelRect(miscLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Orbit_Context"), Label);//"orbit direction"

                                    GUI.Label(SettinglabelRect(miscLines++), StringUtils.Localize("#LOC_BDArmory_AI_Standby"), BoldLabel);//"Standby"
                                    if (AI.standbyMode != (AI.standbyMode = GUI.Toggle(ToggleButtonRect(miscLines, contentWidth), AI.standbyMode, AI.standbyMode ? StringUtils.Localize("#LOC_BDArmory_On") : StringUtils.Localize("#LOC_BDArmory_Off"), AI.standbyMode ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        AI.UpdateToggle(AI.Fields[nameof(AI.standbyMode)]);
                                    miscLines += 1.25f;
                                    if (contextTipsEnabled) GUI.Label(ContextLabelRect(miscLines++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Standby_Context"), Label);//"Activate when target in guard range"

                                    GUI.Label(SettinglabelRect(miscLines++), StringUtils.Localize("#LOC_BDArmory_ControlSurfaceSettings"), BoldLabel);//"Control Surface Settings"
                                    if (GUI.Button(ToggleButtonRect(miscLines, contentWidth), StringUtils.Localize("#LOC_BDArmory_StoreControlSurfaceSettings"), BDArmorySetup.ButtonStyle))
                                    {
                                        AI.StoreControlSurfaceSettings(); //Hiding these in misc is probably not the best place to put them, but only so much space on the window header bar
                                    }
                                    miscLines += 1.25f;
                                    if (AI.Events[nameof(AI.RestoreControlSurfaceSettings)].active == true)
                                    {
                                        GUIStyle restoreStyle = BDArmorySetup.ButtonStyle;
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
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_PidHelp_AutoTune") + (AI.AutoTune ? StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_PidHelp_AutoTune_details") : ""), infoLinkStyle, Width(ColumnWidth - contentMargin * 4 - 20)); //auto-tuning desc
                                    }
                                    if (showSection[Section.Altitude])
                                    {
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_Altitudes"), BoldLabel, Width(ColumnWidth - (contentMargin * 4) - 20)); //Altitude label
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_AltHelp"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //altitude description
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_AltHelp_Def"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //default alt desc
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_AltHelp_min"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //min alt desc
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_AltHelp_max"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //max alt desc
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_AltHelp_bombing"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //bombing alt desc
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
                                    if (showSection[Section.Bombing])
                                    {
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_BombingHelp_divebombing"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20));
                                        if (AI.divebombing)
                                        {
                                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_BombingHelp_divebombing_alt"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20));
                                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_BombingHelp_divebombing_angle"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20));
                                        }
                                        else
                                        {
                                            GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_BombingHelp_alt"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20));
                                        }
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_BombingHelp_PID"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20));
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_BombingHelp_Extend"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //extend distances
                                    }
                                    if (showSection[Section.Evasion])
                                    {
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeExtend"), BoldLabel, Width(ColumnWidth - (contentMargin * 4) - 20)); //evade header
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //evade description
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp_Evade"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //evade dist/ time/ time threshold
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp_Nonlinearity"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //evade/extend nonlinearity
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp_Dodge"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //collision avoid
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp_standoff"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //standoff distance
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp_AvoidMass"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //obstacle avoidance mass
                                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_AIWindow_infolink_Pilot_EvadeHelp_RetreatDistance"), infoLinkStyle, Width(ColumnWidth - (contentMargin * 4) - 20)); //retreat distance
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
                    case AIType.SurfaceAI:
                        {
                            var AI = ActiveAI as BDModuleSurfaceAI;
                            if (AI == null) { Debug.LogError($"[BDArmory.BDArmoryAIGUI]: AI module mismatch!"); activeAIType = AIType.None; break; }

                            if (AISelectionComboBox == null || !AISelectionComboBox.IsOpen)
                            { // Section buttons
                                float line = 1.5f;
                                showSection[Section.PID] = GUI.Toggle(SubsectionRect(line), showSection[Section.PID], StringUtils.Localize("#LOC_BDArmory_AIWindow_PID"), showSection[Section.PID] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"PiD"

                                line += 1.2f;
                                showSection[Section.Speed] = GUI.Toggle(SubsectionRect(line), showSection[Section.Speed], StringUtils.Localize("#LOC_BDArmory_AIWindow_Speeds"), showSection[Section.Speed] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Speed"

                                line += 1.2f;
                                showSection[Section.Control] = GUI.Toggle(SubsectionRect(line), showSection[Section.Control], StringUtils.Localize("#LOC_BDArmory_AIWindow_Control"), showSection[Section.Control] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Control"

                                line += 1.2f;
                                showSection[Section.Combat] = GUI.Toggle(SubsectionRect(line), showSection[Section.Combat], StringUtils.Localize("#LOC_BDArmory_AIWindow_Combat"), showSection[Section.Combat] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Combat"

                                line += 1.5f;
                                if (showSection[Section.UpToEleven] != (AI.UpToEleven = GUI.Toggle(SubsectionRect(line), AI.UpToEleven,
                                    AI.UpToEleven ? StringUtils.Localize("#LOC_BDArmory_AI_UnclampTuning_enabledText") : StringUtils.Localize("#LOC_BDArmory_AI_UnclampTuning_disabledText"),
                                    AI.UpToEleven ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))//"Misc"
                                {
                                    SetInputFields(activeAIType);
                                }

                                minHeight = contentTop + (line + 1f) * entryHeight + _windowMargin;
                            }

                            { // Controls panel
                                scrollViewVectors[AIType.SurfaceAI] = GUI.BeginScrollView(
                                    new Rect(contentMargin + 100, contentTop + entryHeight * 1.5f, (ColumnWidth * 2) - 100 - contentMargin, WindowHeight - entryHeight * 1.5f - 2 * contentTop),
                                    scrollViewVectors.GetValueOrDefault(AIType.SurfaceAI),
                                    new Rect(0, 0, ColumnWidth * 2 - 120 - contentMargin * 2, height + contentTop)
                                );

                                GUI.BeginGroup(new Rect(contentMargin, 0, ColumnWidth * 2 - 120 - contentMargin * 2, height + 2 * contentBorder), GUIContent.none, BDArmorySetup.SelectedButtonStyle); //darker box

                                contentWidth -= 24 + contentBorder;

                                { // Vehicle Type
                                    float line = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.VehicleType);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
                                    line += 0.25f;

                                    GUI.Label(SettinglabelRect(line), StringUtils.Localize("#LOC_BDArmory_AIWindow_VehicleType") + $": {AI.SurfaceTypeName}", Label);
                                    if (Drivertype != (Drivertype = Mathf.RoundToInt(GUI.HorizontalSlider(SettingSliderRect(line++, contentWidth), Drivertype, 0, VehicleMovementTypes.Length - 1))))
                                    {
                                        AI.SurfaceTypeName = VehicleMovementTypes[Drivertype].ToString();
                                        AI.UpdateChooseOptionPAW(AI.Fields[nameof(AI.SurfaceTypeName)]);
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
                                        GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
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
                                        GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
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
                                        GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
                                        line += 0.25f;

                                        line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.MaxSlopeAngle, nameof(AI.MaxSlopeAngle), "MaxSlopeAngle", $"{AI.MaxSlopeAngle:0}°");
                                        line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.MaxDrift, nameof(AI.MaxDrift), "MaxDrift", $"{AI.MaxDrift:0}°");
                                        line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.TargetPitch, nameof(AI.TargetPitch), "TargetPitch", $"{AI.TargetPitch:0.0}°");
                                        line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.BankAngle, nameof(AI.BankAngle), "BankAngle", $"{AI.BankAngle:0}°");
                                        line = ContentEntry(ContentType.SemiLogSlider, line, contentWidth, ref AI.AvoidMass, nameof(AI.AvoidMass), "MinObstacleMass", AI.AvoidMass < 10 ? $"{AI.AvoidMass:0.0}t" : $"{AI.AvoidMass:0}t");

                                        if (broadsideDir != (broadsideDir = Mathf.RoundToInt(GUI.HorizontalSlider(SettingSliderRect(line, contentWidth), broadsideDir, 0, AI.orbitDirections.Length - 1))))
                                        {
                                            AI.SetBroadsideDirection(AI.orbitDirections[broadsideDir]);
                                            AI.UpdateChooseOptionPAW(AI.Fields[nameof(AI.OrbitDirectionName)]);
                                        }
                                        GUI.Label(SettinglabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_PreferredBroadsideDirection") + $": {AI.OrbitDirectionName}", Label);
                                        if (contextTipsEnabled)
                                        {
                                            GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_PreferredBroadsideDirection_Context"), contextLabel);
                                        }

                                        if (AI.ManeuverRCS != (AI.ManeuverRCS = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.ManeuverRCS,
                                            StringUtils.Localize("#LOC_BDArmory_AIWindow_ManeuverRCS") + " : " + (AI.ManeuverRCS ? StringUtils.Localize("#LOC_BDArmory_AI_ManeuverRCS_enabledText") : StringUtils.Localize("#LOC_BDArmory_AI_ManeuverRCS_disabledText")),
                                            AI.ManeuverRCS ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                            AI.UpdateToggle(AI.Fields[nameof(AI.ManeuverRCS)]);
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
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
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
                                            if (AI.maintainMinRange != (AI.maintainMinRange = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.maintainMinRange,
                                                StringUtils.Localize("#LOC_BDArmory_AIWindow_MaintainEngagementRange") + " : " + (AI.maintainMinRange ? StringUtils.Localize("#LOC_BDArmory_true") : StringUtils.Localize("#LOC_BDArmory_false")),
                                                AI.maintainMinRange ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))//"Maintain Min range"
                                                AI.UpdateToggle(AI.Fields[nameof(AI.maintainMinRange)]);
                                            line += 1.25f;
                                            if (contextTipsEnabled)
                                            {
                                                GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_MaintainEngagementRange_Context"), contextLabel);
                                            }
                                        }

                                        if (AI.BroadsideAttack != (AI.BroadsideAttack = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.BroadsideAttack,
                                            StringUtils.Localize("#LOC_BDArmory_AIWindow_BroadsideAttack") + " : " + (AI.BroadsideAttack ? StringUtils.Localize("#LOC_BDArmory_AI_BroadsideAttack_enabledText") : StringUtils.Localize("#LOC_BDArmory_AI_BroadsideAttack_disabledText")),
                                            AI.BroadsideAttack ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))//Broadside Attack"
                                            AI.UpdateToggle(AI.Fields[nameof(AI.BroadsideAttack)]);
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
                    case AIType.VTOLAI:
                        {
                            var AI = ActiveAI as BDModuleVTOLAI;
                            if (AI == null) { Debug.LogError($"[BDArmory.BDArmoryAIGUI]: AI module mismatch!"); activeAIType = AIType.None; break; }

                            if (AISelectionComboBox == null || !AISelectionComboBox.IsOpen)
                            { // Section buttons
                                float line = 1.5f;
                                showSection[Section.PID] = GUI.Toggle(SubsectionRect(line), showSection[Section.PID], StringUtils.Localize("#LOC_BDArmory_AIWindow_PID"), showSection[Section.PID] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"PiD"

                                line += 1.2f;
                                showSection[Section.Altitude] = GUI.Toggle(SubsectionRect(line), showSection[Section.Altitude], StringUtils.Localize("#LOC_BDArmory_AIWindow_Altitudes"), showSection[Section.Altitude] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Altitude"

                                line += 1.2f;
                                showSection[Section.Speed] = GUI.Toggle(SubsectionRect(line), showSection[Section.Speed], StringUtils.Localize("#LOC_BDArmory_AIWindow_Speeds"), showSection[Section.Speed] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Speed"

                                line += 1.2f;
                                showSection[Section.Control] = GUI.Toggle(SubsectionRect(line), showSection[Section.Control], StringUtils.Localize("#LOC_BDArmory_AIWindow_Control"), showSection[Section.Control] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Control"

                                line += 1.2f;
                                showSection[Section.Combat] = GUI.Toggle(SubsectionRect(line), showSection[Section.Combat], StringUtils.Localize("#LOC_BDArmory_AIWindow_Combat"), showSection[Section.Combat] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Combat"

                                line += 1.5f;
                                if (showSection[Section.UpToEleven] != (AI.UpToEleven = GUI.Toggle(SubsectionRect(line), AI.UpToEleven,
                                    AI.UpToEleven ? StringUtils.Localize("#LOC_BDArmory_AI_UnclampTuning_enabledText") : StringUtils.Localize("#LOC_BDArmory_AI_UnclampTuning_disabledText"),
                                    AI.UpToEleven ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))//"Misc"
                                {
                                    SetInputFields(activeAIType);
                                }

                                minHeight = contentTop + (line + 1f) * entryHeight + _windowMargin;
                            }

                            if (showSection[Section.PID] || showSection[Section.Altitude] || showSection[Section.Speed] || showSection[Section.Control] || showSection[Section.Combat]) // Controls panel
                            {
                                scrollViewVectors[AIType.VTOLAI] = GUI.BeginScrollView(
                                    new Rect(contentMargin + 100, contentTop + entryHeight * 1.5f, (ColumnWidth * 2) - 100 - contentMargin, WindowHeight - entryHeight * 1.5f - 2 * contentTop),
                                    scrollViewVectors.GetValueOrDefault(AIType.VTOLAI),
                                    new Rect(0, 0, ColumnWidth * 2 - 120 - contentMargin * 2, height + contentTop)
                                );

                                GUI.BeginGroup(new Rect(contentMargin, 0, ColumnWidth * 2 - 120 - contentMargin * 2, height + 2 * contentBorder), GUIContent.none, BDArmorySetup.SelectedButtonStyle); //darker box

                                contentWidth -= 24 + contentBorder;

                                if (showSection[Section.PID])
                                {
                                    float line = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.PID);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
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
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
                                    line += 0.25f;

                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.defaultAltitude, nameof(AI.defaultAltitude), "DefaultAltitude", $"{AI.defaultAltitude:0}m");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.CombatAltitude, nameof(AI.CombatAltitude), "CombatAltitude", $"{AI.CombatAltitude:0}m");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.bombingAltitude, nameof(AI.bombingAltitude), "BombingAltitude", $"{AI.bombingAltitude:0}m");
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
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
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
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
                                    line += 0.25f;

                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.MaxPitchAngle, nameof(AI.MaxPitchAngle), "MaxPitchAngle", $"{AI.MaxPitchAngle:0}°");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.MaxBankAngle, nameof(AI.MaxBankAngle), "MaxBankAngle", $"{AI.MaxBankAngle:0}°");

                                    GUI.Label(SettinglabelRect(line), StringUtils.Localize("#LOC_BDArmory_AIWindow_PreferredBroadsideDirection") + ": " + AI.OrbitDirectionName, Label);
                                    if (broadsideDir != (broadsideDir = Mathf.RoundToInt(GUI.HorizontalSlider(SettingSliderRect(line++, contentWidth), broadsideDir, 0, AI.orbitDirections.Length - 1))))
                                    {
                                        AI.SetBroadsideDirection(AI.orbitDirections[broadsideDir]);
                                        AI.UpdateChooseOptionPAW(AI.Fields[nameof(AI.OrbitDirectionName)]);
                                    }
                                    if (contextTipsEnabled)
                                    {
                                        GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_PreferredBroadsideDirection_Context"), contextLabel);
                                    }

                                    if (AI.ManeuverRCS != (AI.ManeuverRCS = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.ManeuverRCS,
                                        StringUtils.Localize("#LOC_BDArmory_AIWindow_ManeuverRCS") + " : " + (AI.ManeuverRCS ? StringUtils.Localize("#LOC_BDArmory_AI_ManeuverRCS_enabledText") : StringUtils.Localize("#LOC_BDArmory_AI_ManeuverRCS_disabledText")),
                                        AI.ManeuverRCS ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        AI.UpdateToggle(AI.Fields[nameof(AI.ManeuverRCS)]);
                                    line += 1.25f;
                                    if (contextTipsEnabled)
                                    {
                                        GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_ManeuverRCS_Context"), contextLabel);
                                    }
                                    line = ContentEntry(ContentType.SemiLogSlider, line, contentWidth, ref AI.AvoidMass, nameof(AI.AvoidMass), "MinObstacleMass", AI.AvoidMass < 10 ? $"{AI.AvoidMass:0.0}t" : $"{AI.AvoidMass:0}t");
                                    line += 0.25f;
                                    GUI.EndGroup();
                                    sectionHeights[Section.Control] = Mathf.Lerp(sectionHeight, line, 0.15f);
                                    line += 0.1f;
                                    contentHeight += line * entryHeight;
                                }
                                if (showSection[Section.Combat])
                                {
                                    float line = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Combat);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
                                    line += 0.25f;

                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.WeaveFactor, nameof(AI.WeaveFactor), "WeaveFactor", $"{AI.WeaveFactor:0.0}");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.MinEngagementRange, nameof(AI.MinEngagementRange), "MinEngagementRange", $"{AI.MinEngagementRange:0}m");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.MaxEngagementRange, nameof(AI.MaxEngagementRange), "MaxEngagementRange", $"{AI.MaxEngagementRange:0}m");

                                    if (AI.BroadsideAttack != (AI.BroadsideAttack = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.BroadsideAttack,
                                        StringUtils.Localize("#LOC_BDArmory_AIWindow_BroadsideAttack") + " : " + (AI.BroadsideAttack ? StringUtils.Localize("#LOC_BDArmory_AI_BroadsideAttack_enabledText") : StringUtils.Localize("#LOC_BDArmory_AI_BroadsideAttack_disabledText")),
                                        AI.BroadsideAttack ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        AI.UpdateToggle(AI.Fields[nameof(AI.BroadsideAttack)]);
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
                    case AIType.OrbitalAI:
                        {
                            var AI = ActiveAI as BDModuleOrbitalAI;
                            if (AI == null) { Debug.LogError($"[BDArmory.BDArmoryAIGUI]: AI module mismatch!"); activeAIType = AIType.None; break; }

                            if (AISelectionComboBox == null || !AISelectionComboBox.IsOpen)
                            { // Section buttons
                                float line = 1.5f;
                                showSection[Section.PID] = GUI.Toggle(SubsectionRect(line), showSection[Section.PID], StringUtils.Localize("#LOC_BDArmory_AIWindow_PID"), showSection[Section.PID] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"PiD"

                                line += 1.2f;
                                showSection[Section.Combat] = GUI.Toggle(SubsectionRect(line), showSection[Section.Combat], StringUtils.Localize("#LOC_BDArmory_AIWindow_Combat"), showSection[Section.Combat] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Combat"

                                line += 1.2f;
                                showSection[Section.Speed] = GUI.Toggle(SubsectionRect(line), showSection[Section.Speed], StringUtils.Localize("#LOC_BDArmory_AIWindow_Speeds"), showSection[Section.Speed] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Speed"

                                line += 1.2f;
                                showSection[Section.Control] = GUI.Toggle(SubsectionRect(line), showSection[Section.Control], StringUtils.Localize("#LOC_BDArmory_AIWindow_Control"), showSection[Section.Control] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Control"

                                line += 1.2f;
                                showSection[Section.Evasion] = GUI.Toggle(SubsectionRect(line), showSection[Section.Evasion], StringUtils.Localize("#LOC_BDArmory_AIWindow_EvadeExtend"), showSection[Section.Evasion] ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle);//"Evasion"

                                minHeight = contentTop + (line + 1f) * entryHeight + _windowMargin;
                            }

                            if (showSection[Section.PID] || showSection[Section.Combat] || showSection[Section.Speed] || showSection[Section.Control] || showSection[Section.Evasion]) // Controls panel
                            {
                                scrollViewVectors[AIType.OrbitalAI] = GUI.BeginScrollView(
                                    new Rect(contentMargin + 100, contentTop + entryHeight * 1.5f, (ColumnWidth * 2) - 100 - contentMargin, WindowHeight - entryHeight * 1.5f - 2 * contentTop),
                                    scrollViewVectors.GetValueOrDefault(AIType.OrbitalAI),
                                    new Rect(0, 0, ColumnWidth * 2 - 120 - contentMargin * 2, height + contentTop)
                                );

                                GUI.BeginGroup(new Rect(contentMargin, 0, ColumnWidth * 2 - 120 - contentMargin * 2, height + 2 * contentBorder), GUIContent.none, BDArmorySetup.SelectedButtonStyle); //darker box

                                contentWidth -= 24 + contentBorder;

                                if (showSection[Section.PID])
                                {
                                    float line = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.PID);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
                                    line += 0.25f;

                                    // PID Mode
                                    GUI.Label(SettinglabelRect(line), StringUtils.Localize("#LOC_BDArmory_AIWindow_OrbitalPIDActive") + $": {AI.pidMode}", Label);
                                    if (pidMode != (pidMode = Mathf.RoundToInt(GUI.HorizontalSlider(SettingSliderRect(line++, contentWidth), pidMode, 0, PIDModeTypes.Length - 1))))
                                    {
                                        AI.pidMode = PIDModeTypes[pidMode].ToString();
                                        AI.UpdateChooseOptionPAW(AI.Fields[nameof(AI.pidMode)]);
                                    }
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.steerMult, nameof(AI.steerMult), "SteerPower", $"{AI.steerMult:0.0}", true);
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.steerKiAdjust, nameof(AI.steerKiAdjust), "SteerKi", $"{AI.steerKiAdjust:0.00}", true);
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.steerDamping, nameof(AI.steerDamping), "SteerDamping", $"{AI.steerDamping:0.0}", true);
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.steerMaxError, nameof(AI.steerMaxError), "SteerMaxError", $"{AI.steerMaxError:0.0}", true);

                                    GUI.EndGroup();
                                    sectionHeights[Section.PID] = Mathf.Lerp(sectionHeight, line, 0.15f);
                                    line += 0.1f;
                                    contentHeight += line * entryHeight;
                                }
                                if (showSection[Section.Combat])
                                {
                                    float line = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Combat);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
                                    line += 0.25f;

                                    if (AI.BroadsideAttack != (AI.BroadsideAttack = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.BroadsideAttack,
                                        StringUtils.Localize("#LOC_BDArmory_AIWindow_BroadsideAttack") + " : " + (AI.BroadsideAttack ? StringUtils.Localize("#LOC_BDArmory_AI_BroadsideAttack_enabledText") : StringUtils.Localize("#LOC_BDArmory_AI_BroadsideAttack_disabledText")),
                                        AI.BroadsideAttack ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))) //Broadside Attack"
                                    {
                                        AI.UpdateToggle(AI.Fields[nameof(AI.BroadsideAttack)]);
                                    }
                                    line += 1.25f;
                                    if (contextTipsEnabled)
                                    {
                                        GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_BroadsideAttack_Context"), contextLabel);
                                    }

                                    GUI.Label(SettinglabelRect(line), StringUtils.Localize("#LOC_BDArmory_AIWindow_RollMode") + $": {AI.rollTowards}", Label);
                                    if (rollTowards != (rollTowards = Mathf.RoundToInt(GUI.HorizontalSlider(SettingSliderRect(line++, contentWidth), rollTowards, 0, RollModeTypes.Length - 1))))
                                    {
                                        AI.rollTowards = RollModeTypes[rollTowards].ToString();
                                        AI.UpdateChooseOptionPAW(AI.Fields[nameof(AI.rollTowards)]);
                                    }

                                    var oldMinEngagementRange = AI.MinEngagementRange;
                                    line = ContentEntry(ContentType.SemiLogSlider, line, contentWidth, ref AI.MinEngagementRange, nameof(AI.MinEngagementRange), "MinEngagementRange", $"{AI.MinEngagementRange:0}m");
                                    if (AI.MinEngagementRange != oldMinEngagementRange)
                                    {
                                        AI.OnMinUpdated(null, null);
                                        var field = inputFields["ForceFiringRange"];
                                        field.SetCurrentValue(AI.ForceFiringRange);
                                    }

                                    var oldForceFiringRange = AI.ForceFiringRange;
                                    line = ContentEntry(ContentType.SemiLogSlider, line, contentWidth, ref AI.ForceFiringRange, nameof(AI.ForceFiringRange), "ForceFiringRange", $"{AI.ForceFiringRange:0}m");
                                    if (AI.ForceFiringRange != oldForceFiringRange)
                                    {
                                        AI.OnMaxUpdated(null, null);
                                        var field = inputFields["MinEngagementRange"];
                                        field.SetCurrentValue(AI.MinEngagementRange);
                                    }

                                    if (AI.allowRamming != (AI.allowRamming = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.allowRamming,
                                        StringUtils.Localize("#LOC_BDArmory_AI_AllowRamming"), AI.allowRamming ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))//"Allow Ramming"
                                        AI.UpdateToggle(AI.Fields[nameof(AI.allowRamming)]);
                                    line += 1.25f;

                                    GUI.EndGroup();
                                    sectionHeights[Section.Combat] = Mathf.Lerp(sectionHeight, line, 0.15f);
                                    line += 0.1f;
                                    contentHeight += line * entryHeight;
                                }
                                if (showSection[Section.Speed])
                                {
                                    float line = 0.2f;
                                    var sectionHeight = sectionHeights.GetValueOrDefault(Section.Speed);
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
                                    line += 0.25f;

                                    var oldManeuverSpeed = AI.ManeuverSpeed;
                                    line = ContentEntry(ContentType.SemiLogSlider, line, contentWidth, ref AI.ManeuverSpeed, nameof(AI.ManeuverSpeed), "ManeuverSpeed", $"{AI.ManeuverSpeed:0}m/s");
                                    if (AI.ManeuverSpeed != oldManeuverSpeed)
                                    {
                                        AI.OnMaxUpdated(null, null);
                                        var field = inputFields["FiringSpeed"];
                                        field.SetCurrentValue(AI.firingSpeed);
                                    }

                                    var oldMinFiringSpeed = AI.minFiringSpeed;
                                    line = ContentEntry(ContentType.SemiLogSlider, line, contentWidth, ref AI.minFiringSpeed, nameof(AI.minFiringSpeed), "minFiringSpeed", $"{AI.minFiringSpeed:0}m/s");
                                    if (AI.minFiringSpeed != oldMinFiringSpeed)
                                    {
                                        AI.OnMinUpdated(null, null);
                                        var field = inputFields["FiringSpeed"];
                                        field.SetCurrentValue(AI.firingSpeed);
                                    }

                                    var oldFiringSpeed = AI.firingSpeed;
                                    line = ContentEntry(ContentType.SemiLogSlider, line, contentWidth, ref AI.firingSpeed, nameof(AI.firingSpeed), "FiringSpeed", $"{AI.firingSpeed:0}m/s");
                                    if (AI.firingSpeed != oldFiringSpeed)
                                    {
                                        AI.OnMaxUpdated(null, null);
                                        var field = inputFields["minFiringSpeed"];
                                        field.SetCurrentValue(AI.minFiringSpeed);

                                        AI.OnMinUpdated(null, null);
                                        var field2 = inputFields["ManeuverSpeed"];
                                        field2.SetCurrentValue(AI.ManeuverSpeed);
                                    }
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
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
                                    line += 0.25f;

                                    if (AI.FiringRCS != (AI.FiringRCS = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.FiringRCS,
                                        StringUtils.Localize("#LOC_BDArmory_AIWindow_FiringRCS") + " : " + (AI.FiringRCS ? StringUtils.Localize("#LOC_BDArmory_AI_FiringRCS_enabledText") : StringUtils.Localize("#LOC_BDArmory_AI_FiringRCS_disabledText")),
                                        AI.FiringRCS ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        AI.UpdateToggle(AI.Fields[nameof(AI.FiringRCS)]);
                                    line += 1.25f;
                                    if (contextTipsEnabled)
                                    {
                                        GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_FiringRCS_Context"), contextLabel);
                                    }

                                    if (AI.ManeuverRCS != (AI.ManeuverRCS = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.ManeuverRCS,
                                        StringUtils.Localize("#LOC_BDArmory_AIWindow_ManeuverRCS") + " : " + (AI.ManeuverRCS ? StringUtils.Localize("#LOC_BDArmory_AI_ManeuverRCS_enabledText") : StringUtils.Localize("#LOC_BDArmory_AI_ManeuverRCS_disabledText")),
                                        AI.ManeuverRCS ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        AI.UpdateToggle(AI.Fields[nameof(AI.ManeuverRCS)]);
                                    line += 1.25f;
                                    if (contextTipsEnabled)
                                    {
                                        GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_ManeuverRCS_Context"), contextLabel);
                                    }

                                    if (AI.ReverseThrust != (AI.ReverseThrust = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.ReverseThrust,
                                        StringUtils.Localize("#LOC_BDArmory_AIWindow_ReverseEngines") + " : " + (AI.ReverseThrust ? StringUtils.Localize("#LOC_BDArmory_Enabled") : StringUtils.Localize("#LOC_BDArmory_Disabled")),
                                        AI.ReverseThrust ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        AI.UpdateToggle(AI.Fields[nameof(AI.ReverseThrust)]);
                                    line += 1.25f;
                                    if (contextTipsEnabled)
                                    {
                                        GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_ReverseEngines_Context"), contextLabel);
                                    }

                                    if (AI.EngineRCSRotation != (AI.EngineRCSRotation = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.EngineRCSRotation,
                                        StringUtils.Localize("#LOC_BDArmory_AIWindow_EngineRCSRotation") + " : " + (AI.EngineRCSRotation ? StringUtils.Localize("#LOC_BDArmory_Enabled") : StringUtils.Localize("#LOC_BDArmory_Disabled")),
                                        AI.EngineRCSRotation ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        AI.UpdateToggle(AI.Fields[nameof(AI.EngineRCSRotation)]);
                                    line += 1.25f;
                                    if (contextTipsEnabled)
                                    {
                                        GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_EngineRCSRotation_Context"), contextLabel);
                                    }

                                    if (AI.EngineRCSTranslation != (AI.EngineRCSTranslation = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.EngineRCSTranslation,
                                        StringUtils.Localize("#LOC_BDArmory_AIWindow_EngineRCSTranslation") + " : " + (AI.EngineRCSTranslation ? StringUtils.Localize("#LOC_BDArmory_Enabled") : StringUtils.Localize("#LOC_BDArmory_Disabled")),
                                        AI.EngineRCSTranslation ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        AI.UpdateToggle(AI.Fields[nameof(AI.EngineRCSTranslation)]);
                                    line += 1.25f;
                                    if (contextTipsEnabled)
                                    {
                                        GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_EngineRCSTranslation_Context"), contextLabel);
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
                                    GUI.BeginGroup(new Rect(contentBorder, contentHeight + line * entryHeight, contentWidth, sectionHeight * entryHeight), GUIContent.none, BDArmorySetup.SelectedButtonStyle);
                                    line += 0.25f;

                                    GUI.Label(SettinglabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_Evade"), BoldLabel);
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.minEvasionTime, nameof(AI.minEvasionTime), "MinEvasionTime", $"{AI.minEvasionTime:0.00}s");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.evasionThreshold, nameof(AI.evasionThreshold), "EvasionThreshold", $"{AI.evasionThreshold:0}m");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.evasionTimeThreshold, nameof(AI.evasionTimeThreshold), "EvasionTimeThreshold", $"{AI.evasionTimeThreshold:0.0}s");
                                    line = ContentEntry(ContentType.FloatSlider, line, contentWidth, ref AI.evasionErraticness, nameof(AI.evasionErraticness), "EvasionErraticness", $"{AI.evasionErraticness:0.00}");
                                    line = ContentEntry(ContentType.SemiLogSlider, line, contentWidth, ref AI.evasionMinRangeThreshold, nameof(AI.evasionMinRangeThreshold), "EvasionMinRangeThreshold", AI.evasionMinRangeThreshold < 1000 ? $"{AI.evasionMinRangeThreshold:0}m" : $"{AI.evasionMinRangeThreshold / 1000:0}km");
                                    if (AI.evasionRCS != (AI.evasionRCS = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.evasionRCS,
                                        StringUtils.Localize("#LOC_BDArmory_AIWindow_EvasionRCS") + " : " + (AI.evasionRCS ? StringUtils.Localize("#LOC_BDArmory_Enabled") : StringUtils.Localize("#LOC_BDArmory_Disabled")),
                                        AI.evasionRCS ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        AI.UpdateToggle(AI.Fields[nameof(AI.evasionRCS)]);
                                    line += 1.25f;
                                    if (contextTipsEnabled)
                                    {
                                        GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_EvasionRCS_Context"), contextLabel);
                                    }

                                    if (AI.evasionEngines != (AI.evasionEngines = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.evasionEngines,
                                        StringUtils.Localize("#LOC_BDArmory_AIWindow_EvasionEngines") + " : " + (AI.evasionEngines ? StringUtils.Localize("#LOC_BDArmory_Enabled") : StringUtils.Localize("#LOC_BDArmory_Disabled")),
                                        AI.evasionEngines ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        AI.UpdateToggle(AI.Fields[nameof(AI.evasionEngines)]);
                                    line += 1.25f;
                                    if (contextTipsEnabled)
                                    {
                                        GUI.Label(ContextLabelRect(line++), StringUtils.Localize("#LOC_BDArmory_AIWindow_EvasionEngines_Context"), contextLabel);
                                    }

                                    if (AI.evasionIgnoreMyTargetTargetingMe != (AI.evasionIgnoreMyTargetTargetingMe = GUI.Toggle(ToggleButtonRect(line, contentWidth), AI.evasionIgnoreMyTargetTargetingMe, StringUtils.Localize("#LOC_BDArmory_AI_EvasionIgnoreMyTargetTargetingMe"), AI.evasionIgnoreMyTargetTargetingMe ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)))
                                        AI.UpdateToggle(AI.Fields[nameof(AI.evasionIgnoreMyTargetTargetingMe)]);
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
            if (minHeight == 0 && AISelectionComboBox != null) minHeight = 2 * _windowMargin + _buttonSize + AISelectionComboBox.Height;
            WindowHeight = Mathf.Max(WindowHeight, minHeight);

            #region Resizing
            var resizeRect = new Rect(WindowWidth - 16, WindowHeight - 16, 16, 16);
            GUI.DrawTexture(resizeRect, GUIUtils.resizeTexture, ScaleMode.StretchToFill, true);
            if (Event.current.type == EventType.MouseDown && resizeRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 1)
                {
                    resizingWindow = false;
                    autoResizingWindow = true;
                }
                else
                {
                    resizingWindow = true;
                    autoResizingWindow = false;
                }
            }
            else GUIUtils.DragWindow();

            if (Event.current.type == EventType.Repaint)
            {
                if (resizingWindow)
                {
                    WindowHeight += Mouse.delta.y / BDArmorySettings.UI_SCALE_ACTUAL;
                    WindowHeight = Mathf.Max(WindowHeight, minHeight);
                    if (BDArmorySettings.DEBUG_OTHER) GUI.Label(new Rect(WindowWidth / 2, WindowHeight - 26, WindowWidth / 2 - 26, 26), $"Resizing: {Mathf.Round(WindowHeight * BDArmorySettings.UI_SCALE_ACTUAL)}", Label);
                }
                else if (autoResizingWindow)
                {
                    WindowHeight = Mathf.Clamp((_windowMargin + _buttonSize) * 2 + contentHeight + 1, minHeight, (Screen.height - BDArmorySetup.WindowRectAI.yMin) / BDArmorySettings.UI_SCALE_ACTUAL);
                    if (BDArmorySetup.WindowRectAI.height > WindowHeight) BDArmorySetup.WindowRectAI.height = WindowHeight; // Avoid sticking to bottom of screen during RepositionWindow.
                }
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
            if (HighLogic.LoadedSceneIsEditor) GUIUtils.PreventClickThrough(BDArmorySetup.WindowRectAI, "AIGUI lock", true);
        }
    }
}
