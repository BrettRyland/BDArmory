using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using System;
using UnityEngine;

using BDArmory.Competition;
using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.VesselSpawning;
using BDArmory.Weapons.Missiles;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class LoadedVesselSwitcher : MonoBehaviour
    {
        private readonly float _buttonGap = 1;
        private readonly float _buttonHeight = 20;

        private static int _guiCheckIndex = -1;
        public static LoadedVesselSwitcher Instance;
        private readonly float _margin = 5;

        private bool _ready;
        private bool _showGui;
        private bool _autoCameraSwitch = false;

        private readonly float _titleHeight = 30;
        private double lastCameraSwitch = 0;
        private double lastCameraCheck = 0;
        double minCameraCheckInterval = 0.25;
        private Vessel lastActiveVessel = null;
        public bool currentVesselDied = false;
        private double currentVesselDiedAt = 0;

        //gui params
        bool resizingWindow = false;
        Vector2 windowSize = new(350, 32); // Height is auto-adjusting
        float previousWindowHeight = 32;
        private string camMode = "A";
        private int currentMode = 1;
        private SortedList<string, List<MissileFire>> weaponManagers = new SortedList<string, List<MissileFire>>();
        private Dictionary<string, float> cameraScores = new Dictionary<string, float>();

        private bool upToDateWMs = false;
        public SortedList<string, List<MissileFire>> WeaponManagers
        {
            get
            {
                if (!upToDateWMs)
                    UpdateList();
                return weaponManagers;
            }
        }

        private Dictionary<string, List<Vessel>> _vessels = new Dictionary<string, List<Vessel>>();
        public Dictionary<string, List<Vessel>> Vessels
        {
            get
            {
                if (!upToDateWMs) UpdateList();
                return _vessels;
            }
        }

        // booleans to track state of buttons affecting everyone
        private bool _teamsAssigned = false;
        private bool _autoPilotEnabled = false;
        private bool _guardModeEnabled = false;
        public bool vesselTraceEnabled = false;

        // Vessel spawning
        // private bool _vesselsSpawned = false;
        // private bool _continuousVesselSpawning = false;

        // button styles for info buttons
        private static GUIStyle redLight = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
        private static GUIStyle yellowLight = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
        private static GUIStyle greenLight = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
        private static GUIStyle blueLight = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
        private static GUIStyle ItVessel = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
        private static GUIStyle ItVesselSelected = new GUIStyle(BDArmorySetup.BDGuiSkin.box);

        public static GUISkin VSPUISkin = HighLogic.Skin;

        private static System.Random rng;

        private void Awake()
        {
            if (Instance)
                Destroy(this);
            Instance = this;

            redLight.normal.textColor = Color.red;
            yellowLight.normal.textColor = Color.yellow;
            greenLight.normal.textColor = Color.green;
            blueLight.normal.textColor = Color.blue;
            ItVessel.normal.textColor = Color.cyan;
            ItVesselSelected.normal.textColor = Color.cyan;
            redLight.fontStyle = FontStyle.Bold;
            yellowLight.fontStyle = FontStyle.Bold;
            greenLight.fontStyle = FontStyle.Bold;
            blueLight.fontStyle = FontStyle.Bold;
            ItVessel.fontStyle = FontStyle.Bold;
            ItVesselSelected.fontStyle = FontStyle.Bold;
            rng = new System.Random();
        }

        private void Start()
        {
            UpdateList();
            GameEvents.onVesselCreate.Add(VesselEventUpdate);
            GameEvents.onVesselDestroy.Add(VesselEventUpdate);
            GameEvents.onVesselGoOffRails.Add(VesselEventUpdate);
            GameEvents.onVesselGoOnRails.Add(VesselEventUpdate);
            GameEvents.onVesselWillDestroy.Add(CurrentVesselWillDestroy);
            MissileFire.OnChangeTeam += MissileFireOnToggleTeam;

            _ready = false;

            StartCoroutine(WaitForBdaSettings());

            // Set floating origin thresholds
            FloatingOrigin.fetch.threshold = 20000; //20km
            FloatingOrigin.fetch.thresholdSqr = 20000 * 20000; //20km
        }

        private void OnDestroy()
        {
            GameEvents.onVesselCreate.Remove(VesselEventUpdate);
            GameEvents.onVesselDestroy.Remove(VesselEventUpdate);
            GameEvents.onVesselGoOffRails.Remove(VesselEventUpdate);
            GameEvents.onVesselGoOnRails.Remove(VesselEventUpdate);
            GameEvents.onVesselWillDestroy.Remove(CurrentVesselWillDestroy);
            MissileFire.OnChangeTeam -= MissileFireOnToggleTeam;

            _ready = false;

            // TEST
            // Debug.Log($"[BDArmory.LoadedVesselSwitcher]: FLOATINGORIGIN: threshold is {FloatingOrigin.fetch.threshold}");
        }

        private IEnumerator WaitForBdaSettings()
        {
            yield return new WaitUntil(() => BDArmorySetup.Instance is not null);

            _ready = true;
            BDArmorySetup.Instance.hasVesselSwitcher = true;
            if (BDArmorySetup.WindowRectVesselSwitcher.size != default) windowSize = BDArmorySetup.WindowRectVesselSwitcher.size;
            windowSize.x = Math.Max(windowSize.x, 350);
            if (_guiCheckIndex < 0) _guiCheckIndex = GUIUtils.RegisterGUIRect(new Rect());
            SetVisible(BDArmorySetup.showVesselSwitcherGUI);
        }

        private void MissileFireOnToggleTeam(MissileFire wm, BDTeam team)
        {
            UpdateList();
        }

        private void VesselEventUpdate(Vessel v)
        {
            UpdateList();
        }

        private void Update()
        {
            if (!_ready) return;
            if (BDArmorySetup.showVesselSwitcherGUI != _showGui)
            {
                _showGui = BDArmorySetup.showVesselSwitcherGUI;
                UpdateList();
            }

            if (_showGui)
            {
                Hotkeys();
            }

            // check for camera changes
            if (_autoCameraSwitch)
            {
                UpdateCamera();
            }
        }

        void FixedUpdate()
        {
            if (!_ready) return;

            if (WeaponManagers.SelectMany(tm => tm.Value).Any(wm => wm == null)) upToDateWMs = false;

            if (vesselTraceEnabled)
            {
                if (BDKrakensbane.IsActive)
                    floatingOriginCorrection += BDKrakensbane.FloatingOriginOffsetNonKrakensbane;
                var survivingVessels = weaponManagers.SelectMany(tm => tm.Value).Where(wm => wm != null).Select(wm => wm.vessel).ToList();
                foreach (var vessel in survivingVessels)
                {
                    if (vessel == null) continue;
                    if (!vesselTraces.ContainsKey(vessel.vesselName)) vesselTraces[vessel.vesselName] = new List<Tuple<float, Vector3, Quaternion>>();
                    vesselTraces[vessel.vesselName].Add(new Tuple<float, Vector3, Quaternion>(Time.time, referenceRotationCorrection * (vessel.transform.position + floatingOriginCorrection), referenceRotationCorrection * vessel.transform.rotation));
                }
                if (survivingVessels.Count == 0) vesselTraceEnabled = false;
            }
        }

        private void Hotkeys()
        {
            if (BDInputUtils.GetKeyDown(BDInputSettingsFields.VS_SWITCH_NEXT))
                SwitchToNextVessel();
            if (BDInputUtils.GetKeyDown(BDInputSettingsFields.VS_SWITCH_PREV))
                SwitchToPreviousVessel();
        }

        public void UpdateList()
        {
            weaponManagers.Clear();

            try { if (FlightGlobals.Vessels == null) return; } // Sometimes this gets called multiple times when exiting KSP due to something repeatedly calling DestroyImmediate on a vessel!
            catch { return; }
            using (var v = FlightGlobals.Vessels.GetEnumerator())
                while (v.MoveNext())
                {
                    if (v.Current == null || !v.Current.loaded || v.Current.packed) continue;
                    if (VesselModuleRegistry.ignoredVesselTypes.Contains(v.Current.vesselType)) continue;
                    var wms = VesselModuleRegistry.GetMissileFire(v.Current);
                    if (wms != null)
                    {
                        if (weaponManagers.TryGetValue(wms.Team.Name, out var teamManagers))
                            teamManagers.Add(wms);
                        else
                            weaponManagers.Add(wms.Team.Name, new List<MissileFire> { wms });
                    }
                }

            _vessels.Clear();
            using (var team = weaponManagers.Keys.GetEnumerator())
                while (team.MoveNext())
                {
                    _vessels.Add(team.Current, new List<Vessel>());
                    using (var wm = weaponManagers[team.Current].GetEnumerator())
                        while (wm.MoveNext())
                        {
                            _vessels[team.Current].Add(wm.Current.vessel);
                        }
                }
            upToDateWMs = true;
        }

        private void ToggleGuardModes()
        {
            _guardModeEnabled = !_guardModeEnabled;
            using (var teamManagers = weaponManagers.GetEnumerator())
                while (teamManagers.MoveNext())
                    using (var wm = teamManagers.Current.Value.GetEnumerator())
                        while (wm.MoveNext())
                        {
                            if (wm.Current == null) continue;
                            wm.Current.guardMode = _guardModeEnabled;
                        }
        }

        private void ToggleAutopilots()
        {
            // toggle the state
            _autoPilotEnabled = !_autoPilotEnabled;
            var autopilotsToToggle = weaponManagers.SelectMany(tm => tm.Value).ToList(); // Get a copy in case activating stages causes the weaponManager list to change.
            foreach (var weaponManager in autopilotsToToggle)
            {
                if (weaponManager == null) continue;
                if (weaponManager.AI == null) continue;
                if (_autoPilotEnabled)
                {
                    weaponManager.AI.ActivatePilot();
                    // Utils.fireNextNonEmptyStage(weaponManager.vessel);
                    // Trigger AG10 and then activate all engines if nothing was set on AG10.
                    weaponManager.vessel.ActionGroups.ToggleGroup(BDACompetitionMode.KM_dictAG[10]);
                    if (!BDArmorySettings.NO_ENGINES && SpawnUtils.CountActiveEngines(weaponManager.vessel) == 0)
                    {
                        if (SpawnUtils.CountActiveEngines(weaponManager.vessel) == 0)
                            SpawnUtils.ActivateAllEngines(weaponManager.vessel);
                    }
                }
                else
                {
                    weaponManager.AI.DeactivatePilot();
                }
            }
        }

        private void OnGUI()
        {
            if (!(_ready && HighLogic.LoadedSceneIsFlight))
                return;
            if (resizingWindow && Event.current.type == EventType.MouseUp) { resizingWindow = false; }
            if (_showGui && (BDArmorySetup.GAME_UI_ENABLED || BDArmorySettings.VESSEL_SWITCHER_PERSIST_UI))
            {
                string windowTitle = StringUtils.Localize("#LOC_BDArmory_BDAVesselSwitcher_Title");
                if (BDArmorySettings.GRAVITY_HACKS)
                    windowTitle = windowTitle + " (" + BDACompetitionMode.gravityMultiplier.ToString("0.0") + "G)";

                BDArmorySetup.SetGUIOpacity();
                if (BDArmorySettings._UI_SCALE != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings._UI_SCALE * Vector2.one, BDArmorySetup.WindowRectVesselSwitcher.position);
                previousWindowHeight = BDArmorySetup.WindowRectVesselSwitcher.height;
                BDArmorySetup.WindowRectVesselSwitcher = GUI.Window(10293444, BDArmorySetup.WindowRectVesselSwitcher, WindowVesselSwitcher, windowTitle, BDArmorySetup.BDGuiSkin.window); //"BDA Vessel Switcher"
                ResizeWindow();
                BDArmorySetup.SetGUIOpacity(false);
            }
            else
            {
                GUIUtils.UpdateGUIRect(new Rect(), _guiCheckIndex);
            }
        }

        public void SetVisible(bool visible)
        {
            BDArmorySetup.showVesselSwitcherGUI = visible;
            GUIUtils.SetGUIRectVisible(_guiCheckIndex, visible);
            if (!visible && BDTeamSelector.Instance) BDTeamSelector.Instance.SetVisible(false);
        }

        private void ResizeWindow()
        {
            if (resizingWindow) windowSize.x = Mathf.Clamp(windowSize.x, 350, Screen.width - BDArmorySetup.WindowRectVesselSwitcher.x);
            BDArmorySetup.WindowRectVesselSwitcher.size = windowSize;
            GUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectVesselSwitcher, previousWindowHeight);
            GUIUtils.UpdateGUIRect(BDArmorySetup.WindowRectVesselSwitcher, _guiCheckIndex);
        }

        public void ResetDeadVessels() => deadVesselStrings.Clear(); // Reset the dead vessel strings so that they get recalculated.
        Dictionary<string, string> deadVesselStrings = new Dictionary<string, string>(); // Cache dead vessel strings (they could potentially change during a competition, so we'll reset them at the end of competitions).
        StringBuilder deadVesselString = new StringBuilder();

        float WaypointRank(string player)
        {
            if (!BDACompetitionMode.Instance.Scores.ScoreData.ContainsKey(player)) return 0f;
            var score = BDACompetitionMode.Instance.Scores.ScoreData[player];
            return (float)score.totalWPReached - 0.001f * score.totalWPTime; // Rank in the VS based primarily on #waypoints passed and secondly on time.
        }

        private void WindowVesselSwitcher(int id)
        {
            int numButtons = 11;
            int numButtonsOnLeft = 4, leftButtonCount = 0;
            GUI.DragWindow(new Rect(numButtonsOnLeft * _buttonHeight + _margin, 0f, windowSize.x - numButtons * _buttonHeight - 3f * _margin, _titleHeight));
            GUI.Label(new Rect(windowSize.x - (numButtons - numButtonsOnLeft) * _buttonHeight - _margin - 70f, 4f, 70f, _titleHeight - 4f), BDArmorySetup.Version);
            if (GUI.Button(new Rect(leftButtonCount++ * _buttonHeight + _margin, 4f, _buttonHeight, _buttonHeight), "↕", BDArmorySettings.VESSEL_SWITCHER_WINDOW_SORTING ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                BDArmorySettings.VESSEL_SWITCHER_WINDOW_SORTING = !BDArmorySettings.VESSEL_SWITCHER_WINDOW_SORTING;
                BDArmorySetup.SaveConfig();
            }
            if (GUI.Button(new Rect(leftButtonCount++ * _buttonHeight + _margin, 4f, _buttonHeight, _buttonHeight), "t", BDArmorySettings.VESSEL_SWITCHER_WINDOW_OLD_DISPLAY_STYLE ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                BDArmorySettings.VESSEL_SWITCHER_WINDOW_OLD_DISPLAY_STYLE = !BDArmorySettings.VESSEL_SWITCHER_WINDOW_OLD_DISPLAY_STYLE;
                BDArmorySetup.SaveConfig();
            }
            if (GUI.Button(new Rect(leftButtonCount++ * _buttonHeight + _margin, 4f, _buttonHeight, _buttonHeight), "Sc", ScoreWindow.Instance.IsVisible ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                ScoreWindow.Instance.SetVisible(!ScoreWindow.Instance.IsVisible);
            }
            if (GUI.Button(new Rect(leftButtonCount++ * _buttonHeight + _margin, 4f, _buttonHeight, _buttonHeight), "UI", BDArmorySettings.VESSEL_SWITCHER_PERSIST_UI ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                BDArmorySettings.VESSEL_SWITCHER_PERSIST_UI = !BDArmorySettings.VESSEL_SWITCHER_PERSIST_UI;
                BDArmorySetup.SaveConfig();
            }
            if (GUI.Button(new Rect(windowSize.x - 6 * _buttonHeight - _margin, 4, _buttonHeight, _buttonHeight), "M", BDACompetitionMode.Instance.killerGMenabled ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                if (Event.current.button == 1)
                {
                    // start the slowboat killer GM
                    if (BDArmorySettings.RUNWAY_PROJECT)
                        BDACompetitionMode.Instance.killerGMenabled = !BDACompetitionMode.Instance.killerGMenabled;
                }
                else
                {
                    BDACompetitionMode.Instance.LogResults();
                }
            }

            if (GUI.Button(new Rect(windowSize.x - 5 * _buttonHeight - _margin, 4, _buttonHeight, _buttonHeight), camMode, _autoCameraSwitch ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                if (Event.current.button == 1) //right click
                {
                    switch (++currentMode)
                    {
                        case 2:
                            camMode = "S"; //Score-based camera tracking
                            break;
                        case 3:
                            camMode = "D";  //Distance-based camera tracking
                            break;
                        default:
                            camMode = "A"; //Algorithm-based camera tracking
                            currentMode = 1;
                            break;
                    }
                }
                else if (Event.current.button == 2) //mouse 3
                {
                    camMode = "A";
                    currentMode = 1;
                }
                else
                {
                    // set/disable automatic camera switching
                    _autoCameraSwitch = !_autoCameraSwitch;
                    Debug.Log("[BDArmory.LoadedVesselSwitcher]: Setting AutoCameraSwitch");
                }
            }

            if (GUI.Button(new Rect(windowSize.x - 4 * _buttonHeight - _margin, 4, _buttonHeight, _buttonHeight), "G", _guardModeEnabled ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                // switch everyon onto different teams
                ToggleGuardModes();
            }

            if (GUI.Button(new Rect(windowSize.x - 3 * _buttonHeight - _margin, 4, _buttonHeight, _buttonHeight), "P", _autoPilotEnabled ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                // Toggle autopilots for everyone
                ToggleAutopilots();
            }

            if (GUI.Button(new Rect(windowSize.x - 2 * _buttonHeight - _margin, 4, _buttonHeight, _buttonHeight), "T", _teamsAssigned ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                if (Event.current.button == 1) // Right click => original teams.
                {
                    _teamsAssigned = true;
                    MassTeamSwitch(false, true);
                }
                else
                {
                    // switch everyone onto different teams
                    _teamsAssigned = !_teamsAssigned;
                    MassTeamSwitch(_teamsAssigned);
                }
            }

            if (GUI.Button(new Rect(windowSize.x - _buttonHeight - _margin, 4, _buttonHeight, _buttonHeight), " X", BDArmorySetup.CloseButtonStyle))
            {
                SetVisible(false);
                return;
            }

            float height = _titleHeight;
            float vesselButtonWidth = windowSize.x - 2 * _margin - (!BDArmorySettings.VESSEL_SWITCHER_WINDOW_OLD_DISPLAY_STYLE || BDArmorySettings.TAG_MODE ? 6f : 5f) * _buttonHeight;
            float teamMargin = (!BDArmorySettings.VESSEL_SWITCHER_WINDOW_OLD_DISPLAY_STYLE && weaponManagers.All(tm => tm.Value.Count() == 1)) ? 0 : _margin;

            // Show all the active vessels
            if (BDArmorySettings.VESSEL_SWITCHER_WINDOW_SORTING)
            {
                if (BDArmorySettings.TAG_MODE)
                { // Sort vessels based on total tag time or tag scores.
                    var orderedWMs = weaponManagers.SelectMany(tm => tm.Value, (tm, weaponManager) => new Tuple<string, MissileFire>(tm.Key, weaponManager)).ToList(); // Use a local copy.
                    if (ContinuousSpawning.Instance.vesselsSpawningContinuously && orderedWMs.All(mf => mf != null && BDACompetitionMode.Instance.Scores.ScoreData.ContainsKey(mf.Item2.vessel.vesselName) && ContinuousSpawning.Instance.continuousSpawningScores.ContainsKey(mf.Item2.vessel.vesselName)))
                        orderedWMs.Sort((mf1, mf2) => ((ContinuousSpawning.Instance.continuousSpawningScores[mf2.Item2.vessel.vesselName].cumulativeTagTime + BDACompetitionMode.Instance.Scores.ScoreData[mf2.Item2.vessel.vesselName].tagTotalTime).CompareTo(ContinuousSpawning.Instance.continuousSpawningScores[mf1.Item2.vessel.vesselName].cumulativeTagTime + BDACompetitionMode.Instance.Scores.ScoreData[mf1.Item2.vessel.vesselName].tagTotalTime)));
                    else if (orderedWMs.All(mf => mf != null && BDACompetitionMode.Instance.Scores.ScoreData.ContainsKey(mf.Item2.vessel.vesselName)))
                        orderedWMs.Sort((mf1, mf2) => (BDACompetitionMode.Instance.Scores.ScoreData[mf2.Item2.vessel.vesselName].tagScore.CompareTo(BDACompetitionMode.Instance.Scores.ScoreData[mf1.Item2.vessel.vesselName].tagScore)));
                    foreach (var weaponManagerPair in orderedWMs)
                    {
                        if (weaponManagerPair.Item2 == null) continue;
                        try
                        {
                            AddVesselSwitcherWindowEntry(weaponManagerPair.Item2, weaponManagerPair.Item1, height, vesselButtonWidth);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[BDArmory.LoadedVesselSwitcher]: AddVesselSwitcherWindowEntry threw an exception trying to add {weaponManagerPair.Item2.vessel.vesselName} on team {weaponManagerPair.Item1} to the list: {e.Message}");
                        }
                        height += _buttonHeight + _buttonGap;
                    }
                }
                else if (BDArmorySettings.WAYPOINTS_MODE)
                {
                    var orderedWMs = weaponManagers.SelectMany(tm => tm.Value, (tm, weaponManager) => new Tuple<string, MissileFire>(tm.Key, weaponManager)).Where(t => t.Item2 != null && t.Item2.vessel != null).ToList(); // Use a local copy.
                    orderedWMs.Sort((mf1, mf2) => WaypointRank(mf2.Item2.vessel.vesselName).CompareTo(WaypointRank(mf1.Item2.vessel.vesselName)));
                    foreach (var weaponManagerPair in orderedWMs)
                    {
                        if (weaponManagerPair.Item1 == null) continue;
                        try
                        {
                            AddVesselSwitcherWindowEntry(weaponManagerPair.Item2, weaponManagerPair.Item1, height, vesselButtonWidth);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[BDArmory.LoadedVesselSwitcher]: AddVesselSwitcherWindowEntry threw an exception trying to add {weaponManagerPair.Item2.vessel.vesselName} on team {weaponManagerPair.Item1} to the list: {e.Message}");
                        }
                        height += _buttonHeight + _buttonGap;
                    }
                }
                else // Sorting of teams by hit counts.
                {
                    var orderedTeamManagers = weaponManagers.Select(tm => new Tuple<string, List<MissileFire>>(tm.Key, tm.Value)).ToList();
                    if (ContinuousSpawning.Instance.vesselsSpawningContinuously)
                    {
                        foreach (var teamManager in orderedTeamManagers)
                            teamManager.Item2.Sort((wm1, wm2) => ((ContinuousSpawning.Instance.continuousSpawningScores.ContainsKey(wm2.vessel.vesselName) ? ContinuousSpawning.Instance.continuousSpawningScores[wm2.vessel.vesselName].cumulativeHits : 0) + (BDACompetitionMode.Instance.Scores.Players.Contains(wm2.vessel.vesselName) ? BDACompetitionMode.Instance.Scores.ScoreData[wm2.vessel.vesselName].hits : 0)).CompareTo((ContinuousSpawning.Instance.continuousSpawningScores.ContainsKey(wm1.vessel.vesselName) ? ContinuousSpawning.Instance.continuousSpawningScores[wm1.vessel.vesselName].cumulativeHits : 0) + (BDACompetitionMode.Instance.Scores.Players.Contains(wm1.vessel.vesselName) ? BDACompetitionMode.Instance.Scores.ScoreData[wm1.vessel.vesselName].hits : 0))); // Sort within each team by cumulative hits.
                        orderedTeamManagers.Sort((tm1, tm2) => (tm2.Item2.Sum(wm => (ContinuousSpawning.Instance.continuousSpawningScores.ContainsKey(wm.vessel.vesselName) ? ContinuousSpawning.Instance.continuousSpawningScores[wm.vessel.vesselName].cumulativeHits : 0) + (BDACompetitionMode.Instance.Scores.Players.Contains(wm.vessel.vesselName) ? BDACompetitionMode.Instance.Scores.ScoreData[wm.vessel.vesselName].hits : 0)).CompareTo(tm1.Item2.Sum(wm => (ContinuousSpawning.Instance.continuousSpawningScores.ContainsKey(wm.vessel.vesselName) ? ContinuousSpawning.Instance.continuousSpawningScores[wm.vessel.vesselName].cumulativeHits : 0) + (BDACompetitionMode.Instance.Scores.Players.Contains(wm.vessel.vesselName) ? BDACompetitionMode.Instance.Scores.ScoreData[wm.vessel.vesselName].hits : 0))))); // Sort teams by total cumulative hits.
                    }
                    else
                    {
                        foreach (var teamManager in orderedTeamManagers)
                            teamManager.Item2.Sort((wm1, wm2) => (BDACompetitionMode.Instance.Scores.Players.Contains(wm2.vessel.vesselName) ? BDACompetitionMode.Instance.Scores.ScoreData[wm2.vessel.vesselName].hits : 0).CompareTo(BDACompetitionMode.Instance.Scores.Players.Contains(wm1.vessel.vesselName) ? BDACompetitionMode.Instance.Scores.ScoreData[wm1.vessel.vesselName].hits : 0)); // Sort within each team by hits.
                        orderedTeamManagers.Sort((tm1, tm2) => (tm2.Item2.Sum(wm => BDACompetitionMode.Instance.Scores.Players.Contains(wm.vessel.vesselName) ? BDACompetitionMode.Instance.Scores.ScoreData[wm.vessel.vesselName].hits : 0).CompareTo(tm1.Item2.Sum(wm => BDACompetitionMode.Instance.Scores.Players.Contains(wm.vessel.vesselName) ? BDACompetitionMode.Instance.Scores.ScoreData[wm.vessel.vesselName].hits : 0)))); // Sort teams by total hits.
                    }
                    foreach (var teamManager in orderedTeamManagers)
                    {
                        height += teamMargin;
                        bool teamNameShowing = false;
                        foreach (var weaponManager in teamManager.Item2)
                        {
                            if (weaponManager == null) continue;
                            if (BDArmorySettings.VESSEL_SWITCHER_WINDOW_OLD_DISPLAY_STYLE && !teamNameShowing)
                            {
                                if (BDTISetup.Instance.ColorAssignments.ContainsKey(teamManager.Item1))
                                {
                                    BDTISetup.TILabel.normal.textColor = BDTISetup.Instance.ColorAssignments[teamManager.Item1];
                                }
                                GUI.Label(new Rect(_margin, height, windowSize.x - 2 * _margin, _buttonHeight), $"{teamManager.Item1}:{(weaponManager.Team.Neutral ? (weaponManager.Team.Name != "Neutral" ? "(Neutral)" : "") : "")}", BDTISetup.TILabel);

                                teamNameShowing = true;
                                height += _buttonHeight + _buttonGap;
                            }
                            try
                            {
                                AddVesselSwitcherWindowEntry(weaponManager, teamManager.Item1, height, vesselButtonWidth);
                            }
                            catch (Exception e)
                            {
                                Debug.LogError($"[BDArmory.LoadedVesselSwitcher]: AddVesselSwitcherWindowEntry threw an exception trying to add {weaponManager.vessel.vesselName} on team {teamManager.Item1} to the list: {e.Message}");
                            }
                            height += _buttonHeight + _buttonGap;
                        }
                    }
                }
            }
            else // Regular sorting.
                foreach (var teamManagers in weaponManagers.ToList()) // Use a copy as something seems to be modifying the list occassionally.
                {
                    height += teamMargin;
                    bool teamNameShowing = false;
                    foreach (var weaponManager in teamManagers.Value)
                    {
                        if (weaponManager == null) continue;
                        if (BDArmorySettings.VESSEL_SWITCHER_WINDOW_OLD_DISPLAY_STYLE && !teamNameShowing)
                        {
                            if (BDTISetup.Instance.ColorAssignments.ContainsKey(teamManagers.Key))
                            {
                                BDTISetup.TILabel.normal.textColor = BDTISetup.Instance.ColorAssignments[teamManagers.Key];
                            }
                            GUI.Label(new Rect(_margin, height, windowSize.x - 2 * _margin, _buttonHeight), $"{teamManagers.Key}:{(weaponManager.team != "Neutral" ? (weaponManager.Team.Neutral ? "(Neutral)" : "") : "")}", BDTISetup.TILabel);
                            teamNameShowing = true;
                            height += _buttonHeight + _buttonGap;
                        }
                        try
                        {
                            AddVesselSwitcherWindowEntry(weaponManager, teamManagers.Key, height, vesselButtonWidth);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[BDArmory.LoadedVesselSwitcher]: AddVesselSwitcherWindowEntry threw an exception trying to add {weaponManager.vessel.vesselName} on team {teamManagers.Key} to the list: {e.Message}");
                        }
                        height += _buttonHeight + _buttonGap;
                    }
                }

            height += _margin;
            // add all the lost pilots at the bottom
            if (!ContinuousSpawning.Instance.vesselsSpawningContinuously) // Don't show the dead vessels when continuously spawning. (Especially as command seats trigger all vessels as showing up as dead.)
            {
                foreach (var player in BDACompetitionMode.Instance.Scores.deathOrder)
                {
                    if (BDACompetitionMode.Instance.hasPinata && player == BDArmorySettings.PINATA_NAME) continue; // Ignore the piñata.
                    if (!deadVesselStrings.ContainsKey(player))
                    {
                        deadVesselString.Clear();
                        // DEAD <death order>:<death time>: vesselName(<Score>[, <MissileScore>][, <RammingScore>])[ KILLED|RAMMED BY <otherVesselName>], where <Score> is the number of hits made  <RammingScore> is the number of parts destroyed.
                        deadVesselString.Append($"DEAD {BDACompetitionMode.Instance.Scores.ScoreData[player].deathOrder}:{BDACompetitionMode.Instance.Scores.ScoreData[player].deathTime:0.0} : {player} ({BDACompetitionMode.Instance.Scores.ScoreData[player].hits} hits");
                        if (BDACompetitionMode.Instance.Scores.ScoreData[player].totalDamagedPartsDueToRockets > 0)
                            deadVesselString.Append($", {BDACompetitionMode.Instance.Scores.ScoreData[player].totalDamagedPartsDueToRockets} rkt");
                        if (BDACompetitionMode.Instance.Scores.ScoreData[player].totalDamagedPartsDueToMissiles > 0)
                            deadVesselString.Append($", {BDACompetitionMode.Instance.Scores.ScoreData[player].totalDamagedPartsDueToMissiles} mis");
                        if (BDACompetitionMode.Instance.Scores.ScoreData[player].totalDamagedPartsDueToRamming > 0)
                            deadVesselString.Append($", {BDACompetitionMode.Instance.Scores.ScoreData[player].totalDamagedPartsDueToRamming} ram");
                        if (ContinuousSpawning.Instance.vesselsSpawningContinuously && BDACompetitionMode.Instance.Scores.ScoreData[player].tagTotalTime > 0)
                            deadVesselString.Append($", {BDACompetitionMode.Instance.Scores.ScoreData[player].tagTotalTime:0.0} tag");
                        else if (BDACompetitionMode.Instance.Scores.ScoreData[player].tagScore > 0)
                            deadVesselString.Append($", {BDACompetitionMode.Instance.Scores.ScoreData[player].tagScore:0.0} tag");
                        switch (BDACompetitionMode.Instance.Scores.ScoreData[player].lastDamageWasFrom)
                        {
                            case DamageFrom.Guns:
                                deadVesselString.Append($") KILLED BY {BDACompetitionMode.Instance.Scores.ScoreData[player].lastPersonWhoDamagedMe}");
                                break;
                            case DamageFrom.Rockets:
                                deadVesselString.Append($") FRAGGED BY {BDACompetitionMode.Instance.Scores.ScoreData[player].lastPersonWhoDamagedMe}");
                                break;
                            case DamageFrom.Missiles:
                                deadVesselString.Append($") EXPLODED BY {BDACompetitionMode.Instance.Scores.ScoreData[player].lastPersonWhoDamagedMe}");
                                break;
                            case DamageFrom.Ramming:
                                deadVesselString.Append($") RAMMED BY {BDACompetitionMode.Instance.Scores.ScoreData[player].lastPersonWhoDamagedMe}");
                                break;
                            case DamageFrom.Asteroids:
                                deadVesselString.Append($") FLEW INTO AN ASTEROID!");
                                break;
                            case DamageFrom.Incompetence:
                                deadVesselString.Append(") CRASHED AND BURNED!");
                                break;
                            case DamageFrom.None:
                                deadVesselString.Append($") {BDACompetitionMode.Instance.Scores.ScoreData[player].gmKillReason}");
                                break;
                            default: // Note: All the cases ought to be covered above.
                                deadVesselString.Append(")");
                                break;
                        }
                        switch (BDACompetitionMode.Instance.Scores.ScoreData[player].aliveState)
                        {
                            case AliveState.CleanKill:
                                deadVesselString.Append(" (Clean-Kill!)");
                                break;
                            case AliveState.HeadShot:
                                deadVesselString.Append(" (Head-Shot!)");
                                break;
                            case AliveState.KillSteal:
                                deadVesselString.Append(" (Kill-Steal!)");
                                break;
                            case AliveState.AssistedKill:
                                deadVesselString.Append(", et al.");
                                break;
                            case AliveState.Dead:
                                break;
                        }
                        deadVesselStrings.Add(player, deadVesselString.ToString());
                    }
                    GUI.Label(new Rect(_margin, height, windowSize.x - 2 * _margin, _buttonHeight), deadVesselStrings[player], BDArmorySetup.BDGuiSkin.label); // Use the full width since we're not showing buttons here.
                    height += _buttonHeight + _buttonGap;
                }
            }
            // Piñata killers.
            if (BDACompetitionMode.Instance.hasPinata && !BDACompetitionMode.Instance.pinataAlive)
            {
                if (!deadVesselStrings.ContainsKey(BDArmorySettings.PINATA_NAME))
                {
                    deadVesselString.Clear();
                    deadVesselString.Append("Pinata Killers: ");
                    foreach (var player in BDACompetitionMode.Instance.Scores.Players)
                    {
                        if (BDACompetitionMode.Instance.Scores.ScoreData[player].PinataHits > 0) //not reporting any players?
                        {
                            deadVesselString.Append($" {player};");
                            //BDACompetitionMode.Instance.Scores.ScoreData[BDArmorySettings.PINATA_NAME].lastPersonWhoDamagedMe
                        }
                    }
                    deadVesselStrings.Add(BDArmorySettings.PINATA_NAME, deadVesselString.ToString());
                }
                GUI.Label(new Rect(_margin, height, vesselButtonWidth, _buttonHeight), deadVesselStrings[BDArmorySettings.PINATA_NAME], BDArmorySetup.BDGuiSkin.label);
                height += _buttonHeight + _buttonGap;
            }

            height += _margin;
            #region Resizing
            windowSize.y = Mathf.Lerp(windowSize.y, Mathf.Max(height, _titleHeight + _buttonHeight), 0.15f);
            var resizeRect = new Rect(windowSize.x - 16, windowSize.y - 16, 16, 16);
            GUI.DrawTexture(resizeRect, GUIUtils.resizeTexture, ScaleMode.StretchToFill, true);
            if (Event.current.type == EventType.MouseDown && resizeRect.Contains(Event.current.mousePosition)) resizingWindow = true;
            if (resizingWindow && Event.current.type == EventType.Repaint) windowSize.x += Mouse.delta.x / BDArmorySettings._UI_SCALE;
            #endregion
        }

        StringBuilder VSEntryString = new StringBuilder();
        void AddVesselSwitcherWindowEntry(MissileFire wm, string team, float height, float vesselButtonWidth)
        {
            float _offset = 0;
            if (!BDArmorySettings.VESSEL_SWITCHER_WINDOW_OLD_DISPLAY_STYLE || BDArmorySettings.TAG_MODE)
            {
                if (BDTISetup.Instance.ColorAssignments.ContainsKey(team))
                {
                    BDTISetup.TILabel.normal.textColor = BDTISetup.Instance.ColorAssignments[team];
                }
                GUI.Label(new Rect(_margin, height, _buttonHeight, _buttonHeight), $"{(team.Length > 2 ? team.Remove(2) : team)}", BDTISetup.TILabel);
                _offset = _buttonHeight;
            }
            Rect buttonRect = new Rect(_margin + _offset, height, vesselButtonWidth, _buttonHeight);
            GUIStyle vButtonStyle = team == "IT" ? (wm.vessel.isActiveVessel ? ItVesselSelected : ItVessel) : wm.vessel.isActiveVessel ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button;

            VSEntryString.Clear();
            string vesselName = wm.vessel.vesselName;
            if (ContinuousSpawning.Instance.vesselsSpawningContinuously && BDArmorySettings.VESSEL_SPAWN_LIVES_PER_VESSEL > 0)
            {
                if (ContinuousSpawning.Instance.continuousSpawningScores.ContainsKey(vesselName))
                {
                    VSEntryString.Append($"(Lives:{(int)BDArmorySettings.VESSEL_SPAWN_LIVES_PER_VESSEL - (ContinuousSpawning.Instance.continuousSpawningScores[vesselName].spawnCount - 1)}) ");
                }
            }
            VSEntryString.Append(vesselName);
            if (BDArmorySettings.HALL_OF_SHAME_LIST.Contains(vesselName))
            {
                // vesselName += " (HoS)";
                VSEntryString.Append(" (HoS)");
            }
            VSEntryString.Append(UpdateVesselStatus(wm, vButtonStyle)); // status
            ScoringData scoreData = null;
            int currentScore = 0;
            int currentRocketScore = 0;
            int currentRamScore = 0;
            int currentMissileScore = 0;
            double currentTagTime = 0;
            double currentTagScore = 0;
            int currentTimesIt = 0;

            if (BDACompetitionMode.Instance.Scores.Players.Contains(vesselName))
            {
                scoreData = BDACompetitionMode.Instance.Scores.ScoreData[vesselName];
                currentScore = scoreData.hits;
                currentRocketScore = scoreData.totalDamagedPartsDueToRockets;
                currentRamScore = scoreData.totalDamagedPartsDueToRamming;
                currentMissileScore = scoreData.totalDamagedPartsDueToMissiles;
                if (BDArmorySettings.TAG_MODE)
                {
                    currentTagTime = scoreData.tagTotalTime;
                    currentTagScore = scoreData.tagScore;
                    currentTimesIt = scoreData.tagTimesIt;
                }
            }
            if (ContinuousSpawning.Instance.vesselsSpawningContinuously)
            {
                if (ContinuousSpawning.Instance.continuousSpawningScores.ContainsKey(vesselName))
                {
                    currentScore += ContinuousSpawning.Instance.continuousSpawningScores[vesselName].cumulativeHits;
                    currentRocketScore += ContinuousSpawning.Instance.continuousSpawningScores[vesselName].cumulativeDamagedPartsDueToRockets;
                    currentRamScore += ContinuousSpawning.Instance.continuousSpawningScores[vesselName].cumulativeDamagedPartsDueToRamming;
                    currentMissileScore += ContinuousSpawning.Instance.continuousSpawningScores[vesselName].cumulativeDamagedPartsDueToMissiles;
                }
                if (BDArmorySettings.TAG_MODE && ContinuousSpawning.Instance.continuousSpawningScores.ContainsKey(wm.vessel.vesselName))
                    currentTagTime += ContinuousSpawning.Instance.continuousSpawningScores[wm.vessel.vesselName].cumulativeTagTime;
            }

            // string postStatus = " (" + currentScore.ToString();
            // if (currentRocketScore > 0) postStatus += ", " + currentRocketScore.ToString();
            // if (currentRamScore > 0) postStatus += ", " + currentRamScore.ToString();
            // if (currentMissileScore > 0) postStatus += ", " + currentMissileScore.ToString();
            // if (BDArmorySettings.TAG_MODE)
            //     postStatus += ", " + (ContinuousSpawning.Instance.vesselsSpawningContinuously ? currentTagTime.ToString("0.0") : currentTagScore.ToString("0.0"));
            // postStatus += ")";
            if (BDArmorySettings.WAYPOINTS_MODE)
            {
                if (scoreData != null) // This probably won't work if running waypoints in continuous spawning mode, but that probably doesn't work anyway!
                {
                    if (BDArmorySettings.WAYPOINT_GUARD_INDEX >= 0 && currentScore > 0) VSEntryString.Append($" ({currentScore} hits)");
                    VSEntryString.Append($"  ({scoreData.totalWPReached:0}, {scoreData.totalWPTime:0.00}s, {scoreData.totalWPDeviation:0.00}m), ");
                }
            }
            else
            {
                VSEntryString.Append($" ({currentScore}");
                if (currentRocketScore > 0) VSEntryString.Append($", {currentRocketScore} rkt");
                if (currentMissileScore > 0) VSEntryString.Append($", {currentMissileScore} mis");
                if (currentRamScore > 0) VSEntryString.Append($", {currentRamScore} ram");
                if (BDArmorySettings.TAG_MODE)
                    VSEntryString.Append($", {(ContinuousSpawning.Instance.vesselsSpawningContinuously ? currentTagTime : currentTagScore):0.0} tag");
                VSEntryString.Append(")");
            }

            if (wm.AI != null && wm.AI.currentStatus != null)
            {
                // postStatus += " " + wm.AI.currentStatus;
                VSEntryString.Append($" {wm.AI.currentStatus}");
            }
            float targetDistance = 5000;
            if (wm.currentTarget != null)
            {
                targetDistance = Vector3.Distance(wm.vessel.GetWorldPos3D(), wm.currentTarget.position);
            }

            //postStatus += " :" + Convert.ToInt32(wm.vessel.srfSpeed).ToString();
            // display killerGM stats
            //if ((BDACompetitionMode.Instance.killerGMenabled) && BDACompetitionMode.Instance.FireCount.ContainsKey(vesselName))
            //{
            //    postStatus += " " + (BDACompetitionMode.Instance.FireCount[vesselName] + BDACompetitionMode.Instance.FireCount2[vesselName]).ToString() + ":" + Convert.ToInt32(BDACompetitionMode.Instance.AverageSpeed[vesselName] / BDACompetitionMode.Instance.averageCount).ToString();
            //}

            if (BDACompetitionMode.Instance.KillTimer.ContainsKey(vesselName))
            {
                // postStatus += " x" + BDACompetitionMode.Instance.KillTimer[vesselName].ToString() + "x";
                VSEntryString.Append($" x{BDACompetitionMode.Instance.KillTimer[vesselName]}x");
            }

            // current target 
            string targetName = "";
            Vessel targetVessel = wm.vessel;
            bool incomingThreat = false;
            if (wm.incomingThreatVessel != null)
            {
                incomingThreat = true;
                targetName = $"<<<{wm.incomingThreatVessel.vesselName}";
                targetVessel = wm.incomingThreatVessel;
            }
            else if (wm.currentTarget)
            {
                targetName = $">>>{wm.currentTarget.Vessel.vesselName}";
                targetVessel = wm.currentTarget.Vessel;
            }

            if (targetName != "")
            {
                // postStatus += " " + targetName;
                VSEntryString.Append($" {targetName}");
            }

            /*if (cameraScores.ContainsKey(vesselName))
            {
                int sc = (int)(cameraScores[vesselName]);
                postStatus += " [" + sc.ToString() + "]";
            }
            */
            if (GUI.Button(buttonRect, VSEntryString.ToString(), vButtonStyle))
                ForceSwitchVessel(wm.vessel);

            // selects current target
            if (targetName != "")
            {
                Rect targetingButtonRect = new Rect(_margin + vesselButtonWidth + _offset, height, _buttonHeight, _buttonHeight);
                GUIStyle targButton = BDArmorySetup.BDGuiSkin.button;
                if (wm.currentGun != null && wm.currentGun.recentlyFiring)
                {
                    if (targetDistance < 500)
                    {
                        targButton = redLight;
                    }
                    else if (targetDistance < 1000)
                    {
                        targButton = yellowLight;
                    }
                    else
                    {
                        targButton = blueLight;
                    }
                }
                if (GUI.Button(targetingButtonRect, incomingThreat ? "><" : "[]", targButton))
                    ForceSwitchVessel(targetVessel);
            }

            //guard toggle
            GUIStyle guardStyle = wm.guardMode ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button;
            Rect guardButtonRect = new Rect(_margin + vesselButtonWidth + _offset + _buttonHeight, height, _buttonHeight, _buttonHeight);
            if (GUI.Button(guardButtonRect, "G", guardStyle))
                wm.ToggleGuardMode();

            //AI toggle
            if (wm.AI != null)
            {
                GUIStyle aiStyle = new GUIStyle(wm.AI.pilotEnabled ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button);
                if (wm.underFire)
                {
                    var distance = Vector3.Distance(wm.vessel.GetWorldPos3D(), wm.incomingThreatPosition);
                    if (distance < 500)
                    {
                        aiStyle.normal.textColor = Color.red;
                    }
                    else if (distance < 1000)
                    {
                        aiStyle.normal.textColor = Color.yellow;
                    }
                    else
                    {
                        aiStyle.normal.textColor = Color.blue;
                    }
                }
                Rect aiButtonRect = new Rect(_margin + vesselButtonWidth + _offset + 2 * _buttonHeight, height, _buttonHeight,
                    _buttonHeight);
                if (GUI.Button(aiButtonRect, "P", aiStyle))
                {
                    wm.AI.TogglePilot();
                    if (Event.current.button == 1 && !wm.AI.pilotEnabled) // Right click, trigger AG10 / activate engines
                    {
                        // Trigger AG10 and then activate all engines if nothing was set on AG10.
                        wm.vessel.ActionGroups.ToggleGroup(BDACompetitionMode.KM_dictAG[10]);
                        if (!BDArmorySettings.NO_ENGINES && SpawnUtils.CountActiveEngines(wm.vessel) == 0)
                        {
                            if (SpawnUtils.CountActiveEngines(wm.vessel) == 0)
                                SpawnUtils.ActivateAllEngines(wm.vessel);
                        }
                    }
                }
            }

            //team toggle
            Rect teamButtonRect = new Rect(_margin + vesselButtonWidth + _offset + 3 * _buttonHeight, height,
                _buttonHeight, _buttonHeight);
            if (GUI.Button(teamButtonRect, "T", BDArmorySetup.BDGuiSkin.button))
            {
                if (Event.current.button == 1)
                {
                    BDTeamSelector.Instance.Open(wm, new Vector2(Input.mousePosition.x + 2 * _buttonHeight + _margin, Screen.height - Input.mousePosition.y));
                }
                else if (Event.current.button == 2)
                {
                    //wm.SetTeam(BDTeam.Get("Neutral"));
                    //if (wm.Team.Name != "Neutral" && wm.Team.Name != "A" && wm.Team.Name != "B") wm.Team.Neutral = !wm.Team.Neutral;
                    wm.NextTeam(true);
                }
                else
                {
                    wm.NextTeam();
                }
            }

            // boom
            Rect killButtonRect = new Rect(_margin + vesselButtonWidth + _offset + 4 * _buttonHeight, height, _buttonHeight, _buttonHeight);
            GUIStyle xStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
            var currentParts = wm.vessel.parts.Count;
            if (scoreData != null)
            {
                if (currentParts < scoreData.previousPartCount)
                {
                    xStyle.normal.textColor = Color.red;
                }
                else if (Planetarium.GetUniversalTime() - scoreData.lastDamageTime < 4)
                {
                    xStyle.normal.textColor = Color.yellow;
                }
            }
            if (wm.vessel != null && GUI.Button(killButtonRect, "X", xStyle))
            {
                // must use right button
                if (Event.current.button == 1)
                {
                    if (scoreData != null)
                    {
                        if (scoreData.lastPersonWhoDamagedMe == "")
                        {
                            scoreData.lastPersonWhoDamagedMe = "BIG RED BUTTON"; // only do this if it's not already damaged
                        }
                        BDACompetitionMode.Instance.Scores.RegisterDeath(vesselName, GMKillReason.BigRedButton); // Indicate that it was us who killed it.
                        BDACompetitionMode.Instance.competitionStatus.Add($"{vesselName} {(BDArmorySettings.HALL_OF_SHAME_LIST.Contains(vesselName) ? " (HoS)" : "")} was killed by the BIG RED BUTTON.");
                    }
                    VesselUtils.ForceDeadVessel(wm.vessel);
                }
            }
        }

        StringBuilder vesselStatusString = new StringBuilder();
        private string UpdateVesselStatus(MissileFire wm, GUIStyle vButtonStyle)
        {
            vesselStatusString.Clear();
            if (wm.vessel.LandedOrSplashed)
            {
                vesselStatusString.Append(" ");
                if (wm.vessel.Landed)
                    vesselStatusString.Append(StringUtils.Localize("#LOC_BDArmory_VesselStatus_Landed"));//"(Landed)"
                else if (wm.vessel.IsUnderwater())
                    vesselStatusString.Append(StringUtils.Localize("#LOC_BDArmory_VesselStatus_Underwater")); // "(Underwater)"
                else
                    vesselStatusString.Append(StringUtils.Localize("#LOC_BDArmory_VesselStatus_Splashed"));//"(Splashed)"
                vButtonStyle.fontStyle = FontStyle.Italic;
            }
            else
            {
                vButtonStyle.fontStyle = FontStyle.Normal;
            }
            return vesselStatusString.ToString();
        }

        private void SwitchToNextVessel()
        {
            if (weaponManagers.Count == 0) return;

            bool switchNext = false;

            using (var teamManagers = weaponManagers.GetEnumerator())
                while (teamManagers.MoveNext())
                    using (var wm = teamManagers.Current.Value.GetEnumerator())
                        while (wm.MoveNext())
                        {
                            if (wm.Current.vessel.isActiveVessel)
                                switchNext = true;
                            else if (switchNext)
                            {
                                ForceSwitchVessel(wm.Current.vessel);
                                return;
                            }
                        }
            var firstVessel = weaponManagers.Values[0][0].vessel;
            if (!firstVessel.isActiveVessel)
                ForceSwitchVessel(firstVessel);
        }

        /* If groups or specific are specified, then they take preference.
         * groups is a list of ints of the number of vessels to assign to each team.
         * specific is a list of lists of craft names.
         * If the sum of groups is less than the number of vessels, then the extras get assigned to their own team.
         * If specific does not contain all the vessel names, then the unmentioned vessels get assigned to team 'A'.
         */
        public void MassTeamSwitch(bool separateTeams = false, bool originalTeams = false, List<int> groups = null, List<List<string>> specific = null)
        {
            if (originalTeams)
            {
                foreach (var weaponManager in weaponManagers.SelectMany(tm => tm.Value).Where(wm => wm != null).ToList()) // Get a copy in case activating stages causes the weaponManager list to change.
                {
                    if (SpawnUtils.originalTeams.ContainsKey(weaponManager.vessel.vesselName))
                    {
                        if (BDArmorySettings.DEBUG_AI) Debug.Log("[BDArmory.LoadedVesselSwitcher]: assigning " + weaponManager.vessel.GetName() + " to team " + SpawnUtils.originalTeams[weaponManager.vessel.vesselName]);
                        weaponManager.SetTeam(BDTeam.Get(SpawnUtils.originalTeams[weaponManager.vessel.vesselName]));
                        if (BDACompetitionMode.Instance.Scores != null && BDACompetitionMode.Instance.Scores.Players.Contains(weaponManager.vessel.vesselName))
                            BDACompetitionMode.Instance.Scores.ScoreData[weaponManager.vessel.vesselName].team = weaponManager.Team.Name;
                    }
                }
                return;
            }
            char T = 'A';
            if (specific != null)
            {
                var weaponManagersByName = weaponManagers.SelectMany(tm => tm.Value).Where(wm => wm != null).ToDictionary(wm => wm.vessel.vesselName);
                foreach (var craftList in specific)
                {
                    foreach (var craftName in craftList)
                    {
                        if (weaponManagersByName.ContainsKey(craftName))
                            weaponManagersByName[craftName].SetTeam(BDTeam.Get(T.ToString()));
                        else
                            Debug.Log("[BDArmory.LoadedVesselSwitcher]: Specified vessel (" + craftName + ") not found amongst active vessels.");
                        weaponManagersByName.Remove(craftName); // Remove the vessel from our dictionary once it's assigned.
                    }
                    ++T;
                }
                foreach (var craftName in weaponManagersByName.Keys)
                {
                    Debug.Log("[BDArmory.LoadedVesselSwitcher]: Vessel " + craftName + " was not specified to be part of a team, but is active. Assigning to team " + T.ToString() + ".");
                    weaponManagersByName[craftName].SetTeam(BDTeam.Get(T.ToString())); // Assign anyone who wasn't specified to a separate team.
                    weaponManagersByName[craftName].Team.Neutral = false;
                    if (BDACompetitionMode.Instance.Scores != null && BDACompetitionMode.Instance.Scores.Players.Contains(craftName))
                        BDACompetitionMode.Instance.Scores.ScoreData[craftName].team = weaponManagersByName[craftName].Team.Name;
                }
                return;
            }
            if (groups != null)
            {
                int groupIndex = 0;
                int count = 0;
                foreach (var weaponManager in weaponManagers.SelectMany(tm => tm.Value).Where(wm => wm != null).ToList())
                {
                    while (groupIndex < groups.Count && count == groups[groupIndex])
                    {
                        ++groupIndex;
                        count = 0;
                        ++T;
                    }
                    weaponManager.SetTeam(BDTeam.Get(T.ToString())); // Otherwise, assign them to team T.
                    weaponManager.Team.Neutral = false;
                    if (BDACompetitionMode.Instance.Scores != null && BDACompetitionMode.Instance.Scores.Players.Contains(weaponManager.vessel.vesselName))
                        BDACompetitionMode.Instance.Scores.ScoreData[weaponManager.vessel.vesselName].team = weaponManager.Team.Name;
                    ++count;
                }
                return;
            }
            // switch everyone to their own teams
            foreach (var weaponManager in weaponManagers.SelectMany(tm => tm.Value).Where(wm => wm != null).ToList()) // Get a copy in case activating stages causes the weaponManager list to change.
            {
                if (BDArmorySettings.DEBUG_AI) Debug.Log("[BDArmory.LoadedVesselSwitcher]: assigning " + weaponManager.vessel.GetName() + " to team " + T.ToString());
                weaponManager.SetTeam(BDTeam.Get(T.ToString()));
                weaponManager.Team.Neutral = false;
                if (BDACompetitionMode.Instance.Scores != null && BDACompetitionMode.Instance.Scores.Players.Contains(weaponManager.vessel.vesselName))
                    BDACompetitionMode.Instance.Scores.ScoreData[weaponManager.vessel.vesselName].team = weaponManager.Team.Name;
                if (separateTeams) T++;
            }
        }

        private void SwitchToPreviousVessel()
        {
            if (weaponManagers.Count == 0) return;

            Vessel previousVessel = weaponManagers.Values[weaponManagers.Count - 1][weaponManagers.Values[weaponManagers.Count - 1].Count - 1].vessel;

            using (var teamManagers = weaponManagers.GetEnumerator())
                while (teamManagers.MoveNext())
                    using (var wm = teamManagers.Current.Value.GetEnumerator())
                        while (wm.MoveNext())
                        {
                            if (wm.Current.vessel.isActiveVessel)
                            {
                                ForceSwitchVessel(previousVessel);
                                return;
                            }
                            previousVessel = wm.Current.vessel;
                        }
            if (!previousVessel.isActiveVessel)
                ForceSwitchVessel(previousVessel);
        }

        void CurrentVesselWillDestroy(Vessel v)
        {
            if (_autoCameraSwitch && lastActiveVessel == v)
            {
                currentVesselDied = true;
                if (v.IsMissile())
                {
                    currentVesselDiedAt = Time.time - (BDArmorySettings.DEATH_CAMERA_SWITCH_INHIBIT_PERIOD == 0 ? BDArmorySettings.CAMERA_SWITCH_FREQUENCY / 2f : BDArmorySettings.DEATH_CAMERA_SWITCH_INHIBIT_PERIOD) / 2f; // Wait half the death cam period on missile death.
                    // FIXME If the missile is a clustermissile, we should immediately switch to one of the sub-missiles.
                }
                else
                {
                    currentVesselDiedAt = Time.time;
                }
            }
        }

        private void UpdateCamera()
        {
            var now = Time.time;
            double timeSinceLastCheck = now - lastCameraCheck;
            if (currentVesselDied)
            {
                if (now - currentVesselDiedAt < (BDArmorySettings.DEATH_CAMERA_SWITCH_INHIBIT_PERIOD == 0 ? BDArmorySettings.CAMERA_SWITCH_FREQUENCY / 2f : BDArmorySettings.DEATH_CAMERA_SWITCH_INHIBIT_PERIOD)) // Prevent camera changes for a bit.
                    return;
                else
                {
                    currentVesselDied = false;
                    lastCameraSwitch = 0;
                    lastActiveVessel = null;
                    timeSinceLastCheck = minCameraCheckInterval + 1f;
                }
            }

            if (ModIntegration.MouseAimFlight.IsMouseAimActive) return; // Don't switch while MouseAimFlight is active.

            if (timeSinceLastCheck > minCameraCheckInterval)
            {
                lastCameraCheck = now;

                // first check to see if we've changed the vessel recently
                if (lastActiveVessel != null)
                {
                    if (!lastActiveVessel.isActiveVessel)
                    {
                        // active vessel was changed 
                        lastCameraSwitch = now;
                    }
                }
                double timeSinceChange = now - lastCameraSwitch;

                float bestScore = currentMode > 1 ? 0 : 10000000;
                Vessel bestVessel = null;
                var activeVessel = FlightGlobals.ActiveVessel;
                if (activeVessel != null && activeVessel.loaded && !activeVessel.packed && activeVessel.IsMissile())
                {
                    var mb = VesselModuleRegistry.GetMissileBase(activeVessel);
                    // Don't switch away from an active missile until it misses or is off-target, or if it is within 1 km of its target position
                    bool stayOnMissile = mb != null &&
                        !mb.HasMissed &&
                        Vector3.Dot((mb.TargetPosition - mb.vessel.transform.position).normalized, mb.vessel.transform.up) < 0.5f &&
                        (mb.vessel.transform.position - mb.TargetPosition).sqrMagnitude < 1e6;
                    if (stayOnMissile) return;
                    lastCameraCheck -= TimeWarp.deltaTime; // Speed up moving away from less relevant missiles.
                }
                bool foundActiveVessel = false;
                Vector3 centroid = Vector3.zero;
                if (currentMode == 3) //distance-based
                {
                    int count = 1;

                    foreach (var v in WeaponManagers.SelectMany(tm => tm.Value).Where(wm => wm != null && wm.vessel != null).Select(wm => wm.vessel))
                    {
                        if (v.vesselType != VesselType.Debris)
                        {
                            centroid += v.CoM;
                            ++count;
                        }
                    }
                    centroid /= (float)count;
                }
                if (BDArmorySettings.CAMERA_SWITCH_INCLUDE_MISSILES) // Prioritise active missiles.
                {
                    foreach (MissileBase missile in BDATargetManager.FiredMissiles.Cast<MissileBase>())
                    {
                        if (missile == null || missile.HasMissed) continue; // Ignore missed missiles.
                        var targetDirection = missile.TargetPosition - missile.transform.position;
                        var targetDistance = targetDirection.magnitude;
                        if (Vector3.Dot(targetDirection, missile.GetForwardTransform()) < 0.5f * targetDistance) continue; // Ignore off-target missiles.
                        if (missile.targetVessel != null && missile.targetVessel.Vessel.IsMissile()) continue; // Ignore missiles targeting missiles.
                        if (Vector3.Dot(missile.TargetVelocity - missile.vessel.Velocity(), missile.GetForwardTransform()) > -1f) continue; // Ignore missiles that aren't gaining on their targets.
                        float missileScore = targetDistance < 1e3f ? 0.1f : 0.1f + (targetDistance - 1e3f) * (targetDistance - 1e3f) * 5e-8f; // Prioritise missiles that are within 1km from their targets and de-prioritise those more than 5km away.
                        if (missileScore < bestScore)
                        {
                            bestScore = missileScore;
                            bestVessel = missile.vessel;
                        }
                    }
                }
                using (var wm = WeaponManagers.SelectMany(tm => tm.Value).Where(wm => wm != null && wm.vessel != null).ToList().GetEnumerator())
                    // redo the math
                    // check all the planes
                    while (wm.MoveNext())
                    {
                        //if ((v.Current.GetCrewCapacity()) > 0 && (v.Current.GetCrewCount() == 0)) continue; //They're dead, Jim //really should be a isControllable tage, else this will never look at ProbeCore ships
                        if (wm.Current == null || wm.Current.vessel == null) continue;
                        if (!wm.Current.vessel.IsControllable) continue;
                        float vesselScore = 1000;
                        switch (currentMode)
                        {
                            case 2: //score-based
                                {
                                    ScoringData scoreData = null;
                                    int score = 0;
                                    if (BDACompetitionMode.Instance.Scores.ScoreData.ContainsKey(wm.Current.vessel.vesselName))
                                    {
                                        scoreData = BDACompetitionMode.Instance.Scores.ScoreData[wm.Current.vessel.vesselName];
                                        score = scoreData.hits; //expand to something closer to the score parser score?
                                    }
                                    if (ContinuousSpawning.Instance.vesselsSpawningContinuously)
                                    {
                                        if (ContinuousSpawning.Instance.continuousSpawningScores.ContainsKey(wm.Current.vessel.vesselName))
                                        {
                                            score += ContinuousSpawning.Instance.continuousSpawningScores[wm.Current.vessel.vesselName].cumulativeHits;
                                        }
                                    }
                                    if (wm.Current.vessel.isActiveVessel)
                                    {
                                        foundActiveVessel = true;
                                    }
                                    if (score > 0) vesselScore = score;
                                    if (vesselScore > bestScore)
                                    {
                                        bestVessel = wm.Current.vessel;
                                        bestScore = vesselScore;
                                    }
                                    cameraScores[wm.Current.vessel.vesselName] = vesselScore;
                                    break;
                                }
                            case 3: //distance based - look for most distant vessel from centroid; use with CameraTools centroid option
                                {
                                    vesselScore = (centroid - wm.Current.vessel.CoM).magnitude;
                                    if (vesselScore > bestScore)
                                    {
                                        bestVessel = wm.Current.vessel;
                                        bestScore = vesselScore;
                                    }
                                    if (wm.Current.vessel.isActiveVessel)
                                    {
                                        foundActiveVessel = true;
                                    }
                                    cameraScores[wm.Current.vessel.vesselName] = vesselScore;
                                    break;
                                }
                            default:
                                {
                                    float targetDistance = 5000 + (float)(rng.NextDouble() * 100.0);
                                    float crashTime = 30;
                                    string vesselName = wm.Current.vessel.vesselName;
                                    // avoid lingering on dying things
                                    bool recentlyDamaged = false;
                                    bool recentlyLanded = false;

                                    // check for damage & landed status

                                    if (BDACompetitionMode.Instance.Scores.Players.Contains(vesselName))
                                    {
                                        var currentParts = wm.Current.vessel.parts.Count;
                                        var vdat = BDACompetitionMode.Instance.Scores.ScoreData[vesselName];
                                        if (now - vdat.lastLostPartTime < 5d) // Lost parts within the last 5s.
                                        {
                                            recentlyDamaged = true;
                                        }

                                        if (vdat.landedState)
                                        {
                                            var timeSinceLanded = now - vdat.lastLandedTime;
                                            if (timeSinceLanded < 2)
                                            {
                                                recentlyLanded = true;
                                            }
                                        }
                                    }
                                    vesselScore = Math.Abs(vesselScore);
                                    float HP = 0;
                                    float WreckFactor = 0;
                                    var AI = VesselModuleRegistry.GetBDModulePilotAI(wm.Current.vessel, true);
                                    var OAI = VesselModuleRegistry.GetModule<BDModuleOrbitalAI>(wm.Current.vessel, true);

                                    // If we're running a waypoints competition (without combat), only focus on vessels still running waypoints.
                                    if (BDACompetitionMode.Instance.competitionType == CompetitionType.WAYPOINTS)
                                    {
                                        if (AI == null || (BDArmorySettings.WAYPOINT_GUARD_INDEX < 0 && !AI.IsRunningWaypoints)) continue;
                                        vesselScore *= 2f - Mathf.Clamp01((float)wm.Current.vessel.speed / AI.maxSpeed); // For waypoints races, craft going near their max speed are more interesting.
                                        vesselScore *= Mathf.Max(0.5f, 1f - 15.8f / BDAMath.Sqrt(AI.waypointRange)); // Favour craft the are approaching a gate (capped at 1km).
                                    }

                                    HP = wm.Current.currentHP / wm.Current.totalHP;
                                    if (HP < 1)
                                    {
                                        WreckFactor += 1f - HP * HP; //the less plane remaining, the greater the chance it's a wreck
                                    }
                                    if (wm.Current.vessel.verticalSpeed < -30) //falling out of the sky? Could be an intact plane diving to default alt, could be a cockpit
                                    {
                                        WreckFactor += 0.5f;
                                        if (AI == null || wm.Current.vessel.radarAltitude < AI.defaultAltitude) //craft is uncontrollably diving, not returning from high alt to cruising alt
                                        {
                                            WreckFactor += 0.5f;
                                        }
                                    }
                                    if (VesselModuleRegistry.GetModuleCount<ModuleEngines>(wm.Current.vessel) > 0)
                                    {
                                        int engineOut = 0;
                                        foreach (var engine in VesselModuleRegistry.GetModules<ModuleEngines>(wm.Current.vessel))
                                        {
                                            if (engine == null || engine.flameout || engine.finalThrust <= 0)
                                                engineOut++;
                                        }
                                        WreckFactor += (engineOut / VesselModuleRegistry.GetModuleCount<ModuleEngines>(wm.Current.vessel)) / 2;
                                    }
                                    else
                                    {
                                        WreckFactor += 0.5f; //could be a glider, could be missing engines
                                    }
                                    if (WreckFactor > 1f) // 'wrecked' requires some combination of diving, no engines, and missing parts
                                    {
                                        WreckFactor *= 2;
                                        vesselScore *= WreckFactor; //disincentivise switching to wrecks
                                    }
                                    if (!recentlyLanded && wm.Current.vessel.verticalSpeed < -15) // Vessels gently floating to the ground aren't interesting
                                    {
                                        crashTime = (float)(-Math.Abs(wm.Current.vessel.radarAltitude) / wm.Current.vessel.verticalSpeed);
                                    }
                                    if (crashTime < 30 && HP > 0.5f)
                                    {
                                        vesselScore *= crashTime / 30;
                                    }
                                    if (wm.Current.currentTarget != null)
                                    {
                                        targetDistance = Vector3.Distance(wm.Current.vessel.GetWorldPos3D(), wm.Current.currentTarget.position);
                                        if (!wm.Current.HasWeaponsAndAmmo()) // no remaining weapons
                                        {
                                            if (!BDArmorySettings.DISABLE_RAMMING && AI != null && AI.allowRamming) //ramming's fun to watch
                                            {
                                                vesselScore *= (0.031623f * BDAMath.Sqrt(targetDistance) / 2);
                                            }
                                            else
                                            {
                                                vesselScore *= 3; //ramming disabled. Boring!
                                            }
                                        }
                                        //else got weapons and engaging
                                    }
                                    if (OAI) // Maneuvering is interesting, other statuses are not
                                    {
                                        if (OAI.currentStatusMode == BDModuleOrbitalAI.StatusMode.Maneuvering)
                                            vesselScore *= 0.5f;
                                        else if (OAI.currentStatusMode == BDModuleOrbitalAI.StatusMode.CorrectingOrbit)
                                            vesselScore *= 1.5f;
                                        else if (OAI.currentStatusMode == BDModuleOrbitalAI.StatusMode.Idle)
                                            vesselScore *= 2f;
                                        else if (OAI.currentStatusMode == BDModuleOrbitalAI.StatusMode.Stranded)
                                            vesselScore *= 3f;
                                        // else -- Firing, Evading covered by weapon manager checks
                                    }
                                    vesselScore *= 0.031623f * BDAMath.Sqrt(targetDistance); // Equal to 1 at 1000m
                                    if (wm.Current.recentlyFiring) // Firing guns or missiles at stuff is more interesting. (Uses 1/2 the camera switch frequency on all guns.)
                                        vesselScore *= 0.25f;
                                    if (wm.Current.guardFiringMissile) // Firing missiles is a bit more interesting than firing guns.
                                        vesselScore *= 0.8f;
                                    if (wm.Current.currentGun != null && wm.Current.currentGun.recentlyFiring && wm.Current.vessel == FlightGlobals.ActiveVessel) // 1s timer on current gun.
                                        vesselScore *= 0.1f; // Actively firing guns on the current vessel are even more interesting, try not to switch away at the last second!
                                    // scoring for automagic camera check should not be in here
                                    if (wm.Current.underAttack || wm.Current.underFire)
                                    {
                                        vesselScore *= 0.5f;
                                        var distance = Vector3.Distance(wm.Current.vessel.GetWorldPos3D(), wm.Current.incomingThreatPosition);
                                        vesselScore *= 0.031623f * BDAMath.Sqrt(distance); // Equal to 1 at 1000m, we don't want to overly disadvantage craft that are super far away, but could be firing missiles or doing other interesting things
                                                                                           //we're very interested when threat and target are the same
                                        if (wm.Current.incomingThreatVessel != null && wm.Current.currentTarget != null)
                                        {
                                            if (wm.Current.incomingThreatVessel.vesselName == wm.Current.currentTarget.Vessel.vesselName)
                                            {
                                                vesselScore *= 0.25f;
                                            }
                                        }
                                    }
                                    if (wm.Current.incomingMissileVessel != null)
                                    {
                                        float timeToImpact = wm.Current.incomingMissileTime;
                                        vesselScore *= Mathf.Clamp(0.0005f * timeToImpact * timeToImpact, 0, 1); // Missiles about to hit are interesting, scale score with time to impact

                                        if (wm.Current.isFlaring || wm.Current.isChaffing)
                                            vesselScore *= 0.8f;
                                    }
                                    if (recentlyDamaged)
                                    {
                                        vesselScore *= 0.3f; // because taking hits is very interesting;
                                    }
                                    if (wm.Current.vessel.LandedOrSplashed)
                                    {
                                        if (wm.Current.vessel.srfSpeed > 2) //margin for physics jitter
                                        {
                                            vesselScore *= Mathf.Min(((80 / (float)wm.Current.vessel.srfSpeed) / 2), 4); //srf Ai driven stuff thats still mobile
                                        }
                                        else
                                        {
                                            if (recentlyLanded)
                                                vesselScore *= 2; // less interesting.
                                            else
                                                vesselScore *= 4; // not interesting.
                                        }
                                    }
                                    // if we're the active vessel add a penalty over time to force it to switch away eventually
                                    if (wm.Current.vessel.isActiveVessel)
                                    {
                                        vesselScore = (float)(vesselScore * timeSinceChange / 8.0);
                                        foundActiveVessel = true;
                                    }
                                    if ((BDArmorySettings.TAG_MODE) && (wm.Current.Team.Name == "IT"))
                                    {
                                        vesselScore = 0f; // Keep camera focused on "IT" vessel during tag
                                    }

                                    // if the score is better then update this
                                    if (vesselScore < bestScore)
                                    {
                                        bestVessel = wm.Current.vessel;
                                        bestScore = vesselScore;
                                    }
                                    cameraScores[wm.Current.vessel.vesselName] = vesselScore;
                                    break;
                                }
                        }
                    }
                lastActiveVessel = FlightGlobals.ActiveVessel;
                if (!foundActiveVessel)
                {
                    var score = 100 * timeSinceChange;
                    if (score < bestScore)
                    {
                        bestVessel = null; // stop switching
                    }
                }
                if (timeSinceChange > BDArmorySettings.CAMERA_SWITCH_FREQUENCY * timeScaleSqrt)
                {
                    if (bestVessel != null && bestVessel.loaded && !bestVessel.packed && !(bestVessel.isActiveVessel)) // if a vessel dies it'll use a default score for a few seconds
                    {
                        if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.LoadedVesselSwitcher]: Switching vessel to " + bestVessel.GetName());
                        ForceSwitchVessel(bestVessel);
                    }
                }
            }
        }
        float _timeScaleSqrt = 1f;
        float timeScaleSqrt // For scaling the camera switch frequency with the sqrt of the time scale.
        {
            get
            {
                if (!BDArmorySettings.TIME_OVERRIDE || Time.timeScale <= 1) return 1f;
                if (Mathf.Abs(Time.timeScale - _timeScaleSqrt * _timeScaleSqrt) > 1e-3f)
                    _timeScaleSqrt = BDAMath.Sqrt(Time.timeScale);
                return _timeScaleSqrt;
            }
        }

        public void EnableAutoVesselSwitching(bool enable)
        {
            _autoCameraSwitch = enable;
        }

        // Extracted method, so we dont have to call these two lines everywhere
        public void ForceSwitchVessel(Vessel v)
        {
            if (v == null || !v.loaded)
                return;
            lastCameraSwitch = Time.time;
            lastActiveVessel = v;
            var camHeading = FlightCamera.CamHdg;
            var camPitch = FlightCamera.CamPitch;
            FlightGlobals.ForceSetActiveVessel(v);
            FlightInputHandler.ResumeVesselCtrlState(v);
            FlightCamera.CamHdg = camHeading;
            FlightCamera.CamPitch = camPitch;
        }

        public IEnumerator SwitchToVesselWhenPossible(Vessel vessel, float distance = 0)
        {
            var wait = new WaitForFixedUpdate();
            while (vessel != null && (!vessel.loaded || vessel.packed)) yield return wait;
            while (vessel != null && vessel.loaded && vessel != FlightGlobals.ActiveVessel) { ForceSwitchVessel(vessel); yield return wait; }
            if (vessel != null && vessel.loaded && !vessel.packed)
            {
                var flightCam = FlightCamera.fetch;
                if (flightCam != null && distance > 0) flightCam.SetDistance(distance);
            }
        }

        public void TriggerSwitchVessel(float delay = 0)
        {
            lastCameraSwitch = delay > 0 ? Time.time - (BDArmorySettings.CAMERA_SWITCH_FREQUENCY * timeScaleSqrt - delay) : 0f;
            lastCameraCheck = 0f;
            UpdateCamera();
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

        #region Vessel Tracing
        Vector3d floatingOriginCorrection = Vector3d.zero;
        Quaternion referenceRotationCorrection = Quaternion.identity;
        Dictionary<string, List<Tuple<float, Vector3, Quaternion>>> vesselTraces = new Dictionary<string, List<Tuple<float, Vector3, Quaternion>>>();

        public void StartVesselTracing()
        {
            if (vesselTraceEnabled) return;
            vesselTraceEnabled = true;
            Debug.Log("[BDArmory.LoadedVesselSwitcher]: Starting vessel tracing.");
            vesselTraces.Clear();

            // Set the reference Up and Rotation based on the current FloatingOrigin.
            var geoCoords = FlightGlobals.currentMainBody.GetLatitudeAndLongitude(Vector3.zero);
            var altitude = FlightGlobals.getAltitudeAtPos(Vector3.zero);
            var localUp = VectorUtils.GetUpDirection(Vector3.zero);
            var q1 = Quaternion.FromToRotation(Vector3.up, localUp);
            var q2 = Quaternion.AngleAxis(Vector3.SignedAngle(q1 * Vector3.forward, Vector3.up, localUp), localUp);
            var referenceRotation = q2 * q1; // Plane tangential to the surface and aligned with north,
            referenceRotationCorrection = Quaternion.Inverse(referenceRotation);
            floatingOriginCorrection = altitude * localUp;

            // Record starting points
            var survivingVessels = weaponManagers.SelectMany(tm => tm.Value).Where(wm => wm != null).Select(wm => wm.vessel).ToList();
            foreach (var vessel in survivingVessels)
            {
                if (vessel == null) continue;
                vesselTraces[vessel.vesselName] = new List<Tuple<float, Vector3, Quaternion>>();
                vesselTraces[vessel.vesselName].Add(new Tuple<float, Vector3, Quaternion>(Time.time, new Vector3((float)geoCoords.x, (float)geoCoords.y, altitude), referenceRotation));
            }
        }
        public void StopVesselTracing()
        {
            if (!vesselTraceEnabled) return;
            vesselTraceEnabled = false;
            Debug.Log("[BDArmory.LoadedVesselSwitcher]: Stopping vessel tracing.");
            var folder = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "BDArmory", "Logs", "VesselTraces");
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            foreach (var vesselName in vesselTraces.Keys)
            {
                var traceFile = Path.Combine(folder, vesselName + "-" + vesselTraces[vesselName][0].Item1.ToString("0.000") + ".json");
                Debug.Log("[BDArmory.LoadedVesselSwitcher]: Dumping trace for " + vesselName + " to " + traceFile);
                List<string> strings = new List<string>();
                strings.Add("[");
                strings.Add(string.Join(",\n", vesselTraces[vesselName].Select(entry => "  { \"time\": " + entry.Item1.ToString("0.000") + ", \"position\": [" + entry.Item2.x.ToString("0.0") + ", " + entry.Item2.y.ToString("0.0") + ", " + entry.Item2.z.ToString("0.0") + "], \"rotation\": [" + entry.Item3.x.ToString("0.000") + ", " + entry.Item3.y.ToString("0.000") + ", " + entry.Item3.z.ToString("0.000") + ", " + entry.Item3.w.ToString("0.000") + "] }")));
                strings.Add("]");
                File.WriteAllLines(traceFile, strings);
            }
            vesselTraces.Clear();
        }
        #endregion
    }
}
