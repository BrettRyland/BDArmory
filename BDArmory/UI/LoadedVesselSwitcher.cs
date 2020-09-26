using System.Collections;
using System.Collections.Generic;
using BDArmory.Misc;
using BDArmory.Modules;
using BDArmory.Control;
using BDArmory.Core;
using UnityEngine;
using KSP.Localization;
using KSP.UI.Screens;
using BDArmory.FX;
using Expansions;
using System;
using VehiclePhysics;
using System.Net;
using System.IO;
using System.Linq;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class LoadedVesselSwitcher : MonoBehaviour
    {
        private readonly float _buttonGap = 1;
        private readonly float _buttonHeight = 20;

        private int _guiCheckIndex;
        public static LoadedVesselSwitcher Instance;
        private readonly float _margin = 5;

        private bool _ready;
        private bool _showGui;
        private bool _autoCameraSwitch = false;

        private readonly float _titleHeight = 30;
        private double lastCameraSwitch = 0;
        private double lastCameraCheck = 0;
        private bool lostActiveVessel = false;
        private Vessel lastActiveVessel = null;
        private float updateTimer = 0;


        //gui params
        private float _windowHeight; //auto adjusting

        public SortedList<string, List<MissileFire>> weaponManagers = new SortedList<string, List<MissileFire>>();
        private Dictionary<string, float> cameraScores = new Dictionary<string, float>();


        // booleans to track state of buttons affecting everyone
        private bool _freeForAll = false;
        private bool _autoPilotEnabled = false;
        private bool _guardModeEnabled = false;

        // Vessel spawning
        private bool _vesselsSpawned = false;
        private bool _continuousVesselSpawning = false;

        // button styles for info buttons
        private static GUIStyle redLight = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
        private static GUIStyle yellowLight = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
        private static GUIStyle greenLight = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
        private static GUIStyle blueLight = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
        private static GUIStyle ItVessel = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
        private static GUIStyle ItVesselSelected = new GUIStyle(BDArmorySetup.BDGuiSkin.box);

        private static System.Random rng;

        static LoadedVesselSwitcher()
        {

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

        private void Awake()
        {
            if (Instance)
                Destroy(this);
            Instance = this;
        }

        private void Start()
        {
            UpdateList();
            GameEvents.onVesselCreate.Add(VesselEventUpdate);
            GameEvents.onVesselDestroy.Add(VesselEventUpdate);
            GameEvents.onVesselGoOffRails.Add(VesselEventUpdate);
            GameEvents.onVesselGoOnRails.Add(VesselEventUpdate);
            MissileFire.OnChangeTeam += MissileFireOnToggleTeam;

            _ready = false;

            StartCoroutine(WaitForBdaSettings());

            // TEST
            FloatingOrigin.fetch.threshold = 20000; //20km
            FloatingOrigin.fetch.thresholdSqr = 20000 * 20000; //20km
            Debug.Log($"FLOATINGORIGIN: threshold is {FloatingOrigin.fetch.threshold}");

            //BDArmorySetup.WindowRectVesselSwitcher = new Rect(10, Screen.height / 6f, BDArmorySettings.VESSEL_SWITCHER_WINDOW_WIDTH, 10);
        }

        private void OnDestroy()
        {
            GameEvents.onVesselCreate.Remove(VesselEventUpdate);
            GameEvents.onVesselDestroy.Remove(VesselEventUpdate);
            GameEvents.onVesselGoOffRails.Remove(VesselEventUpdate);
            GameEvents.onVesselGoOnRails.Remove(VesselEventUpdate);
            MissileFire.OnChangeTeam -= MissileFireOnToggleTeam;

            _ready = false;

            // TEST
            Debug.Log($"FLOATINGORIGIN: threshold is {FloatingOrigin.fetch.threshold}");
        }

        private IEnumerator WaitForBdaSettings()
        {
            while (BDArmorySetup.Instance == null)
                yield return null;

            _ready = true;
            BDArmorySetup.Instance.hasVS = true;
            _guiCheckIndex = Misc.Misc.RegisterGUIRect(new Rect());
        }

        private void MissileFireOnToggleTeam(MissileFire wm, BDTeam team)
        {
            if (_showGui)
                UpdateList();
        }

        private void VesselEventUpdate(Vessel v)
        {
            if (_showGui)
                UpdateList();
        }

        private void Update()
        {
            if (_ready)
            {
                if (BDArmorySetup.Instance.showVSGUI != _showGui)
                {
                    updateTimer -= Time.fixedDeltaTime;
                    _showGui = BDArmorySetup.Instance.showVSGUI;
                    if (_showGui && updateTimer < 0)
                    {
                        UpdateList();
                        updateTimer = 0.5f;    //next update in half a sec only
                    }
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

                BDACompetitionMode.Instance.DoUpdate();
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

            using (List<Vessel>.Enumerator v = FlightGlobals.Vessels.GetEnumerator())
                while (v.MoveNext())
                {
                    if (v.Current == null || !v.Current.loaded || v.Current.packed)
                        continue;
                    using (var wms = v.Current.FindPartModulesImplementing<MissileFire>().GetEnumerator())
                        while (wms.MoveNext())
                            if (wms.Current != null)
                            {
                                if (weaponManagers.TryGetValue(wms.Current.Team.Name, out var teamManagers))
                                    teamManagers.Add(wms.Current);
                                else
                                    weaponManagers.Add(wms.Current.Team.Name, new List<MissileFire> { wms.Current });
                                break;
                            }
                }
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
                    BDArmory.Misc.Misc.fireNextNonEmptyStage(weaponManager.vessel);
                }
                else
                {
                    weaponManager.AI.DeactivatePilot();
                }
            }
        }

        private void OnGUI()
        {
            if (_ready)
            {
                if (_showGui && BDArmorySetup.GAME_UI_ENABLED)
                {
                    string windowTitle = Localizer.Format("#LOC_BDArmory_BDAVesselSwitcher_Title");
                    if (BDArmorySettings.GRAVITY_HACKS)
                        windowTitle = windowTitle + " (" + BDACompetitionMode.gravityMultiplier.ToString("0.0") + "G)";

                    SetNewHeight(_windowHeight);
                    // this Rect initialization ensures any save issues with height or width of the window are resolved
                    BDArmorySetup.WindowRectVesselSwitcher = new Rect(BDArmorySetup.WindowRectVesselSwitcher.x, BDArmorySetup.WindowRectVesselSwitcher.y, BDArmorySettings.VESSEL_SWITCHER_WINDOW_WIDTH, _windowHeight);
                    BDArmorySetup.WindowRectVesselSwitcher = GUI.Window(10293444, BDArmorySetup.WindowRectVesselSwitcher, WindowVesselSwitcher, windowTitle,//"BDA Vessel Switcher"
                        BDArmorySetup.BDGuiSkin.window);
                    Misc.Misc.UpdateGUIRect(BDArmorySetup.WindowRectVesselSwitcher, _guiCheckIndex);
                }
                else
                {
                    Misc.Misc.UpdateGUIRect(new Rect(), _guiCheckIndex);
                }
            }
        }

        private void SetNewHeight(float windowHeight)
        {
            var previousWindowHeight = BDArmorySetup.WindowRectVesselSwitcher.height;
            BDArmorySetup.WindowRectVesselSwitcher.height = windowHeight;
            if (windowHeight < previousWindowHeight && BDArmorySetup.WindowRectVesselSwitcher.y + previousWindowHeight == Screen.height) // Window shrunk while being at edge of screen.
                BDArmorySetup.WindowRectVesselSwitcher.y = Screen.height - BDArmorySetup.WindowRectVesselSwitcher.height;
            if (BDArmorySettings.STRICT_WINDOW_BOUNDARIES)
            {
                BDGUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectVesselSwitcher);
            }
        }

        private void WindowVesselSwitcher(int id)
        {
            int numButtons = 11;
            GUI.DragWindow(new Rect(3f * _buttonHeight + _margin, 0f, BDArmorySettings.VESSEL_SWITCHER_WINDOW_WIDTH - numButtons * _buttonHeight - 2f * _margin, _titleHeight));

            if (GUI.Button(new Rect(0f * _buttonHeight + _margin, 4f, _buttonHeight, _buttonHeight), "><", BDArmorySetup.BDGuiSkin.button)) // Don't get so small that the buttons get hidden.
            {
                BDArmorySettings.VESSEL_SWITCHER_WINDOW_WIDTH -= 50f;
                if (BDArmorySettings.VESSEL_SWITCHER_WINDOW_WIDTH - 50f < 2f * _margin + numButtons * _buttonHeight)
                    BDArmorySettings.VESSEL_SWITCHER_WINDOW_WIDTH = 2f * _margin + numButtons * _buttonHeight;
                BDArmorySetup.SaveConfig();
            }
            if (GUI.Button(new Rect(1f * _buttonHeight + _margin, 4, _buttonHeight, _buttonHeight), "<>", BDArmorySetup.BDGuiSkin.button))
            {
                BDArmorySettings.VESSEL_SWITCHER_WINDOW_WIDTH += 50f;
                if (BDArmorySettings.VESSEL_SWITCHER_WINDOW_WIDTH > Screen.width) // Don't go off the screen.
                    BDArmorySettings.VESSEL_SWITCHER_WINDOW_WIDTH = Screen.width;
                BDArmorySetup.SaveConfig();
            }
            if (GUI.Button(new Rect(2f * _buttonHeight + _margin, 4, _buttonHeight, _buttonHeight), "↕", BDArmorySettings.VESSEL_SWITCHER_WINDOW_SORTING ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                BDArmorySettings.VESSEL_SWITCHER_WINDOW_SORTING = !BDArmorySettings.VESSEL_SWITCHER_WINDOW_SORTING;
                BDArmorySetup.SaveConfig();
            }

            if (GUI.Button(new Rect(BDArmorySettings.VESSEL_SWITCHER_WINDOW_WIDTH - 8f * _buttonHeight - _margin, 4, _buttonHeight, _buttonHeight), "CS", _continuousVesselSpawning ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                if (!_continuousVesselSpawning && !_vesselsSpawned && Event.current.button == 0) // Left click
                {
                    VesselSpawner.Instance.SpawnVesselsContinuously(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS, BDArmorySettings.VESSEL_SPAWN_ALTITUDE, BDArmorySettings.VESSEL_SPAWN_DISTANCE, true); // Spawn vessels continuously at 1km above terrain.
                    _continuousVesselSpawning = true;
                    _autoPilotEnabled = false;
                }
                else if (Event.current.button == 1)
                {
                    VesselSpawner.Instance.CancelVesselSpawn();
                    if (_continuousVesselSpawning)
                        Debug.Log("[BDArmory]: Resetting continuous spawning button.");
                    _continuousVesselSpawning = false;
                }
            }

            if (GUI.Button(new Rect(BDArmorySettings.VESSEL_SWITCHER_WINDOW_WIDTH - 7f * _buttonHeight - _margin, 4, _buttonHeight, _buttonHeight), "S", _vesselsSpawned ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                if (!_vesselsSpawned && !_continuousVesselSpawning && Event.current.button == 0) // Left click
                {
                    if (BDArmorySettings.VESSEL_SPAWN_CONTINUE_SINGLE_SPAWNING)
                        VesselSpawner.Instance.SpawnAllVesselsOnceContinuously(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS, BDArmorySettings.VESSEL_SPAWN_ALTITUDE, BDArmorySettings.VESSEL_SPAWN_DISTANCE, BDArmorySettings.VESSEL_SPAWN_EASE_IN_SPEED, true); // Spawn vessels.
                    else
                        VesselSpawner.Instance.SpawnAllVesselsOnce(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS, BDArmorySettings.VESSEL_SPAWN_ALTITUDE, BDArmorySettings.VESSEL_SPAWN_DISTANCE, BDArmorySettings.VESSEL_SPAWN_EASE_IN_SPEED, true); // Spawn vessels.
                    _vesselsSpawned = true;
                    _autoPilotEnabled = false;
                }
                else if (!_vesselsSpawned && Event.current.button == 2) // Middle click
                {
                    // VesselSpawner.Instance.SpawnAllVesselsOnce(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS, BDArmorySettings.VESSEL_SPAWN_ALTITUDE, BDArmorySettings.VESSEL_SPAWN_DISTANCE, BDArmorySettings.VESSEL_SPAWN_EASE_IN_SPEED, false); // Spawn vessels, without killing off other vessels or changing camera positions.
                    VesselSpawner.Instance.Round3Spawn(); // FIXME, this is temporary
                    _vesselsSpawned = true;
                    _autoPilotEnabled = false;
                }
                else if (Event.current.button == 1) // Right click
                {
                    VesselSpawner.Instance.CancelVesselSpawn();
                    if (_vesselsSpawned)
                        Debug.Log("[BDArmory]: Resetting spawning vessel button.");
                    _vesselsSpawned = false;
                }
            }

            if (GUI.Button(new Rect(BDArmorySettings.VESSEL_SWITCHER_WINDOW_WIDTH - 6 * _buttonHeight - _margin, 4, _buttonHeight, _buttonHeight), "M", BDACompetitionMode.Instance.killerGMenabled ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
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

            if (GUI.Button(new Rect(BDArmorySettings.VESSEL_SWITCHER_WINDOW_WIDTH - 5 * _buttonHeight - _margin, 4, _buttonHeight, _buttonHeight), "A", _autoCameraSwitch ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                // set/disable automatic camera switching
                _autoCameraSwitch = !_autoCameraSwitch;
                Debug.Log("[BDArmory]: Setting AutoCameraSwitch");
            }

            if (GUI.Button(new Rect(BDArmorySettings.VESSEL_SWITCHER_WINDOW_WIDTH - 4 * _buttonHeight - _margin, 4, _buttonHeight, _buttonHeight), "G", _guardModeEnabled ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                // switch everyon onto different teams
                ToggleGuardModes();
            }

            if (GUI.Button(new Rect(BDArmorySettings.VESSEL_SWITCHER_WINDOW_WIDTH - 3 * _buttonHeight - _margin, 4, _buttonHeight, _buttonHeight), "P", _autoPilotEnabled ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                // Toggle autopilots for everyone
                ToggleAutopilots();
            }

            if (GUI.Button(new Rect(BDArmorySettings.VESSEL_SWITCHER_WINDOW_WIDTH - 2 * _buttonHeight - _margin, 4, _buttonHeight, _buttonHeight), "T", _freeForAll ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button))
            {
                // switch everyone onto different teams
                _freeForAll = !_freeForAll;
                MassTeamSwitch(_freeForAll);
            }

            if (GUI.Button(new Rect(BDArmorySettings.VESSEL_SWITCHER_WINDOW_WIDTH - _buttonHeight - _margin, 4, _buttonHeight, _buttonHeight), "X", BDArmorySetup.BDGuiSkin.button))
            {
                BDArmorySetup.Instance.showVSGUI = false;
                return;
            }

            float height = _titleHeight;
            float vesselButtonWidth = BDArmorySettings.VESSEL_SWITCHER_WINDOW_WIDTH - 2 * _margin - 6f * _buttonHeight;

            // Show all the active vessels
            if (BDArmorySettings.VESSEL_SWITCHER_WINDOW_SORTING)
            {
                if (BDArmorySettings.TAG_MODE)
                { // Sort vessels based on total tag time or tag scores.
                    var orderedWMs = weaponManagers.SelectMany(tm => tm.Value, (tm, weaponManager) => new Tuple<string, MissileFire>(tm.Key, weaponManager)).ToList(); // Use a local copy.
                    if (_continuousVesselSpawning && orderedWMs.All(mf => mf != null && BDACompetitionMode.Instance.Scores.ContainsKey(mf.Item2.vessel.vesselName) && VesselSpawner.Instance.continuousSpawningScores.ContainsKey(mf.Item2.vessel.vesselName)))
                        orderedWMs.Sort((mf1, mf2) => ((VesselSpawner.Instance.continuousSpawningScores[mf2.Item2.vessel.vesselName].cumulativeTagTime + BDACompetitionMode.Instance.Scores[mf2.Item2.vessel.vesselName].tagTotalTime).CompareTo(VesselSpawner.Instance.continuousSpawningScores[mf1.Item2.vessel.vesselName].cumulativeTagTime + BDACompetitionMode.Instance.Scores[mf1.Item2.vessel.vesselName].tagTotalTime)));
                    else if (orderedWMs.All(mf => mf != null && BDACompetitionMode.Instance.Scores.ContainsKey(mf.Item2.vessel.vesselName)))
                        orderedWMs.Sort((mf1, mf2) => (BDACompetitionMode.Instance.Scores[mf2.Item2.vessel.vesselName].tagScore.CompareTo(BDACompetitionMode.Instance.Scores[mf1.Item2.vessel.vesselName].tagScore)));
                    foreach (var weaponManagerPair in orderedWMs)
                    {
                        if (weaponManagerPair.Item2 == null) continue;
                        try
                        {
                            AddVesselSwitcherWindowEntry(weaponManagerPair.Item2, weaponManagerPair.Item1, height, vesselButtonWidth);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError("DEBUG AddVesselSwitcherWindowEntry threw an exception trying to add " + weaponManagerPair.Item2.vessel.vesselName + " on team " + weaponManagerPair.Item1 + " to the list: " + e.Message);
                        }
                        height += _buttonHeight + _buttonGap;
                    }
                }
                else // Sorting of teams by hit counts.
                {
                    var orderedTeamManagers = weaponManagers.Select(tm => new Tuple<string, List<MissileFire>>(tm.Key, tm.Value)).ToList();
                    if (_continuousVesselSpawning)
                    {
                        foreach (var teamManager in orderedTeamManagers)
                            teamManager.Item2.Sort((wm1, wm2) => ((VesselSpawner.Instance.continuousSpawningScores.ContainsKey(wm2.vessel.vesselName) ? VesselSpawner.Instance.continuousSpawningScores[wm2.vessel.vesselName].cumulativeHits : 0) + (BDACompetitionMode.Instance.Scores.ContainsKey(wm2.vessel.vesselName) ? BDACompetitionMode.Instance.Scores[wm2.vessel.vesselName].Score : 0)).CompareTo((VesselSpawner.Instance.continuousSpawningScores.ContainsKey(wm1.vessel.vesselName) ? VesselSpawner.Instance.continuousSpawningScores[wm1.vessel.vesselName].cumulativeHits : 0) + (BDACompetitionMode.Instance.Scores.ContainsKey(wm1.vessel.vesselName) ? BDACompetitionMode.Instance.Scores[wm1.vessel.vesselName].Score : 0))); // Sort within each team by cumulative hits.
                        orderedTeamManagers.Sort((tm1, tm2) => (tm2.Item2.Sum(wm => (VesselSpawner.Instance.continuousSpawningScores.ContainsKey(wm.vessel.vesselName) ? VesselSpawner.Instance.continuousSpawningScores[wm.vessel.vesselName].cumulativeHits : 0) + (BDACompetitionMode.Instance.Scores.ContainsKey(wm.vessel.vesselName) ? BDACompetitionMode.Instance.Scores[wm.vessel.GetName()].Score : 0)).CompareTo(tm1.Item2.Sum(wm => (VesselSpawner.Instance.continuousSpawningScores.ContainsKey(wm.vessel.vesselName) ? VesselSpawner.Instance.continuousSpawningScores[wm.vessel.vesselName].cumulativeHits : 0) + (BDACompetitionMode.Instance.Scores.ContainsKey(wm.vessel.vesselName) ? BDACompetitionMode.Instance.Scores[wm.vessel.GetName()].Score : 0))))); // Sort teams by total cumulative hits.
                    }
                    else
                    {
                        foreach (var teamManager in orderedTeamManagers)
                            teamManager.Item2.Sort((wm1, wm2) => (BDACompetitionMode.Instance.Scores.ContainsKey(wm2.vessel.vesselName) ? BDACompetitionMode.Instance.Scores[wm2.vessel.vesselName].Score : 0).CompareTo(BDACompetitionMode.Instance.Scores.ContainsKey(wm1.vessel.vesselName) ? BDACompetitionMode.Instance.Scores[wm1.vessel.vesselName].Score : 0)); // Sort within each team by hits.
                        orderedTeamManagers.Sort((tm1, tm2) => (tm2.Item2.Sum(wm => BDACompetitionMode.Instance.Scores.ContainsKey(wm.vessel.vesselName) ? BDACompetitionMode.Instance.Scores[wm.vessel.GetName()].Score : 0).CompareTo(tm1.Item2.Sum(wm => BDACompetitionMode.Instance.Scores.ContainsKey(wm.vessel.vesselName) ? BDACompetitionMode.Instance.Scores[wm.vessel.GetName()].Score : 0)))); // Sort teams by total hits.
                    }
                    foreach (var teamManager in orderedTeamManagers)
                    {
                        height += _margin;
                        foreach (var weaponManager in teamManager.Item2)
                        {
                            if (weaponManager == null) continue;
                            try
                            {
                                AddVesselSwitcherWindowEntry(weaponManager, teamManager.Item1, height, vesselButtonWidth);
                            }
                            catch (Exception e)
                            {
                                Debug.LogError("DEBUG AddVesselSwitcherWindowEntry threw an exception trying to add " + weaponManager.vessel.vesselName + " on team " + teamManager.Item1 + " to the list: " + e.Message);
                            }
                            height += _buttonHeight + _buttonGap;
                        }
                    }
                }
            }
            else // Regular sorting.
                foreach (var teamManagers in weaponManagers)
                {
                    height += _margin;
                    foreach (var weaponManager in teamManagers.Value)
                    {
                        if (weaponManager == null) continue;
                        try
                        {
                            AddVesselSwitcherWindowEntry(weaponManager, teamManagers.Key, height, vesselButtonWidth);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError("DEBUG AddVesselSwitcherWindowEntry threw an exception trying to add " + weaponManager.vessel.vesselName + " on team " + teamManagers.Key + " to the list: " + e.Message);
                        }
                        height += _buttonHeight + _buttonGap;
                    }
                }

            height += _margin;
            // add all the lost pilots at the bottom
            if (!VesselSpawner.Instance.vesselsSpawningContinuously) // Don't show the dead vessels when continuously spawning. (Especially as command seats trigger all vessels as showing up as dead.)
                foreach (var key in BDACompetitionMode.Instance.DeathOrder.Keys)
                {
                    string statusString = "";
                    if (BDACompetitionMode.Instance.Scores.ContainsKey(key))
                    {
                        // DEAD <death order>: vesselName(<Score>[, <RammingScore>])[ KILLED|RAMMED BY <otherVesselName>], where <Score> is the number of hits made  <RammingScore> is the number of parts destroyed.
                        statusString += "DEAD " + BDACompetitionMode.Instance.DeathOrder[key] + " : " + key + " (" + BDACompetitionMode.Instance.Scores[key].Score.ToString();
                        if (BDACompetitionMode.Instance.Scores[key].totalDamagedPartsDueToMissiles > 0)
                            statusString += ", " + BDACompetitionMode.Instance.Scores[key].totalDamagedPartsDueToMissiles;
                        if (BDACompetitionMode.Instance.Scores[key].totalDamagedPartsDueToRamming > 0)
                            statusString += ", " + BDACompetitionMode.Instance.Scores[key].totalDamagedPartsDueToRamming;
                        if (_continuousVesselSpawning && BDACompetitionMode.Instance.Scores[key].tagTotalTime > 0)
                            statusString += ", " + BDACompetitionMode.Instance.Scores[key].tagTotalTime.ToString("0.0");
                        else if (BDACompetitionMode.Instance.Scores[key].tagScore > 0)
                            statusString += ", " + BDACompetitionMode.Instance.Scores[key].tagScore.ToString("0.0");
                        switch (BDACompetitionMode.Instance.Scores[key].LastDamageWasFrom())
                        {
                            case DamageFrom.Bullet:
                                statusString += ") KILLED BY " + BDACompetitionMode.Instance.Scores[key].LastPersonWhoDamagedMe() + (BDACompetitionMode.Instance.Scores[key].cleanDeath ? " (Head-shot!)" : ", et al.");
                                break;
                            case DamageFrom.Missile:
                                statusString += ") EXPLODED BY " + BDACompetitionMode.Instance.Scores[key].LastPersonWhoDamagedMe() + (BDACompetitionMode.Instance.Scores[key].cleanDeath ? " (Head-shot!)" : ", et al.");
                                break;
                            case DamageFrom.Ram:
                                statusString += ") RAMMED BY " + BDACompetitionMode.Instance.Scores[key].LastPersonWhoDamagedMe() + (BDACompetitionMode.Instance.Scores[key].cleanDeath ? " (Head-shot!)" : ", et al.");
                                break;
                            default:
                                statusString += ")";
                                break;
                        }
                        GUI.Label(new Rect(_margin, height, vesselButtonWidth, _buttonHeight), statusString, BDArmorySetup.BDGuiSkin.label);
                        height += _buttonHeight + _buttonGap;
                    }
                }
            // Piñata killers.
            if (!BDACompetitionMode.Instance.pinataAlive)
            {
                string postString = "";
                foreach (var killer in BDACompetitionMode.Instance.Scores.Keys)
                {
                    if (BDACompetitionMode.Instance.Scores[killer].PinataHits > 0)
                    {
                        postString += " " + killer;
                    }
                }
                if (postString != "")
                {
                    GUI.Label(new Rect(_margin, height, vesselButtonWidth, _buttonHeight), "Pinata Killers: " + postString, BDArmorySetup.BDGuiSkin.label);
                    height += _buttonHeight + _buttonGap;
                }
            }

            height += _margin;
            _windowHeight = height;
        }

        void AddVesselSwitcherWindowEntry(MissileFire wm, string team, float height, float vesselButtonWidth)
        {
            GUI.Label(new Rect(_margin, height, _buttonHeight, _buttonHeight), $"{team}", BDArmorySetup.BDGuiSkin.label);
            Rect buttonRect = new Rect(_margin + _buttonHeight, height, vesselButtonWidth, _buttonHeight);
            GUIStyle vButtonStyle = team == "IT" ? (wm.vessel.isActiveVessel ? ItVesselSelected : ItVessel) : wm.vessel.isActiveVessel ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button;

            string vesselName = wm.vessel.GetName();
            BDArmory.Control.ScoringData scoreData = null;
            string status = UpdateVesselStatus(wm, vButtonStyle);
            int currentScore = 0;
            int currentRamScore = 0;
            int currentMissileScore = 0;
            double currentTagTime = 0;
            double currentTagScore = 0;
            int currentTimesIt = 0;

            if (BDACompetitionMode.Instance.Scores.ContainsKey(vesselName))
            {
                scoreData = BDACompetitionMode.Instance.Scores[vesselName];
                currentScore = scoreData.Score;
                currentRamScore = scoreData.totalDamagedPartsDueToRamming;
                currentMissileScore = scoreData.totalDamagedPartsDueToMissiles;
                if (BDArmorySettings.TAG_MODE)
                {
                    currentTagTime = scoreData.tagTotalTime;
                    currentTagScore = scoreData.tagScore;
                    currentTimesIt = scoreData.tagTimesIt;
                }
            }
            if (_continuousVesselSpawning)
            {
                if (VesselSpawner.Instance.continuousSpawningScores.ContainsKey(vesselName))
                {
                    currentScore += VesselSpawner.Instance.continuousSpawningScores[vesselName].cumulativeHits;
                    currentRamScore += VesselSpawner.Instance.continuousSpawningScores[vesselName].cumulativeDamagedPartsDueToRamming;
                    currentMissileScore += VesselSpawner.Instance.continuousSpawningScores[vesselName].cumulativeDamagedPartsDueToMissiles;
                }
                if (BDArmorySettings.TAG_MODE && VesselSpawner.Instance.continuousSpawningScores.ContainsKey(wm.vessel.vesselName))
                    currentTagTime += VesselSpawner.Instance.continuousSpawningScores[wm.vessel.vesselName].cumulativeTagTime;
            }

            // current target 
            string targetName = "";
            Vessel targetVessel = wm.vessel;
            bool incomingThreat = false;
            if (wm.incomingThreatVessel != null)
            {
                incomingThreat = true;
                targetName = "<<<" + wm.incomingThreatVessel.GetName();
                targetVessel = wm.incomingThreatVessel;
            }
            else if (wm.currentTarget)
            {
                targetName = ">>>" + wm.currentTarget.Vessel.GetName();
                targetVessel = wm.currentTarget.Vessel;
            }

            string postStatus = " (" + currentScore.ToString();
            if (currentMissileScore > 0) postStatus += ", " + currentMissileScore.ToString();
            if (currentRamScore > 0) postStatus += ", " + currentRamScore.ToString();
            if (BDArmorySettings.TAG_MODE)
                postStatus += ", " + (_continuousVesselSpawning ? currentTagTime.ToString("0.0") : currentTagScore.ToString("0.0"));
            postStatus += ")";

            if (wm.AI != null && wm.AI.currentStatus != null)
            {
                postStatus += " " + wm.AI.currentStatus;
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
                postStatus += " x" + BDACompetitionMode.Instance.KillTimer[vesselName].ToString() + "x";
            }

            if (targetName != "")
            {
                postStatus += " " + targetName;
            }

            /*if (cameraScores.ContainsKey(vesselName))
            {
                int sc = (int)(cameraScores[vesselName]);
                postStatus += " [" + sc.ToString() + "]";
            }
            */

            if (GUI.Button(buttonRect, status + vesselName + postStatus, vButtonStyle))
                ForceSwitchVessel(wm.vessel);

            // selects current target
            if (targetName != "")
            {
                Rect targettingButtonRect = new Rect(_margin + vesselButtonWidth + _buttonHeight, height, _buttonHeight, _buttonHeight);
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
                if (GUI.Button(targettingButtonRect, incomingThreat ? "><" : "[]", targButton))
                    ForceSwitchVessel(targetVessel);
            }

            //guard toggle
            GUIStyle guardStyle = wm.guardMode ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button;
            Rect guardButtonRect = new Rect(_margin + vesselButtonWidth + 2 * _buttonHeight, height, _buttonHeight, _buttonHeight);
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
                Rect aiButtonRect = new Rect(_margin + vesselButtonWidth + 3 * _buttonHeight, height, _buttonHeight,
                    _buttonHeight);
                if (GUI.Button(aiButtonRect, "P", aiStyle))
                    wm.AI.TogglePilot();
            }

            //team toggle
            Rect teamButtonRect = new Rect(_margin + vesselButtonWidth + 4 * _buttonHeight, height,
                _buttonHeight, _buttonHeight);
            if (GUI.Button(teamButtonRect, "T", BDArmorySetup.BDGuiSkin.button))
            {
                if (Event.current.button == 1)
                {
                    BDTeamSelector.Instance.Open(wm, new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
                }
                else
                {
                    wm.NextTeam();
                }
            }

            // boom
            Rect killButtonRect = new Rect(_margin + vesselButtonWidth + 5 * _buttonHeight, height, _buttonHeight, _buttonHeight);
            GUIStyle xStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.button);
            var currentParts = wm.vessel.parts.Count;
            if (scoreData != null)
            {
                if (currentParts < scoreData.previousPartCount)
                {
                    xStyle.normal.textColor = Color.red;
                }
                else if (Planetarium.GetUniversalTime() - scoreData.lastHitTime < 4 || Planetarium.GetUniversalTime() - scoreData.lastRammedTime < 4)
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
                        if (scoreData.LastPersonWhoDamagedMe() == "")
                        {
                            scoreData.lastPersonWhoHitMe = "BIG RED BUTTON"; // only do this if it's not already damaged
                        }
                        scoreData.gmKillReason = GMKillReason.BigRedButton; // Indicate that it was us who killed it and remove any "clean" kills.
                        if (BDACompetitionMode.Instance.whoCleanShotWho.ContainsKey(vesselName)) BDACompetitionMode.Instance.whoCleanShotWho.Remove(vesselName);
                        if (BDACompetitionMode.Instance.whoCleanRammedWho.ContainsKey(vesselName)) BDACompetitionMode.Instance.whoCleanRammedWho.Remove(vesselName);
                        if (BDACompetitionMode.Instance.whoCleanShotWhoWithMissiles.ContainsKey(vesselName)) BDACompetitionMode.Instance.whoCleanShotWhoWithMissiles.Remove(vesselName);
                    }
                    Misc.Misc.ForceDeadVessel(wm.vessel);
                }
            }
        }

        private string UpdateVesselStatus(MissileFire wm, GUIStyle vButtonStyle)
        {
            string status = "";
            if (wm.vessel.LandedOrSplashed)
            {
                if (wm.vessel.Landed)
                    status = Localizer.Format("#LOC_BDArmory_VesselStatus_Landed");//"(Landed)"
                else
                    status = Localizer.Format("#LOC_BDArmory_VesselStatus_Splashed");//"(Splashed)"
                vButtonStyle.fontStyle = FontStyle.Italic;
            }
            else
            {
                vButtonStyle.fontStyle = FontStyle.Normal;
            }
            return status;
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
         * specific is a dictionary of craft names and teams.
         * If the sum of groups is less than the number of vessels, then the extras get assigned to their own team.
         * If specific does not contain all the vessel names, then the unmentioned vessels get assigned to team 'A'.
         */
        public void MassTeamSwitch(bool separateTeams = false, List<int> groups = null, Dictionary<string, char> specific = null)
        {
            Debug.Log("DEBUG team switch with groups " + groups);
            char T = 'A';
            if (specific != null)
            {
                foreach (var weaponManager in weaponManagers.SelectMany(tm => tm.Value).Where(wm => wm != null).ToList())
                {
                    if (specific.ContainsKey(weaponManager.vessel.vesselName))
                        weaponManager.SetTeam(BDTeam.Get(specific[weaponManager.vessel.vesselName].ToString())); // Assign the vessel to the specfied team.
                    else
                        weaponManager.SetTeam(BDTeam.Get('A'.ToString())); // Otherwise, assign them to team 'A'.
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
                        Debug.Log("DEBUG changing team to " + T.ToString());
                    }
                    weaponManager.SetTeam(BDTeam.Get(T.ToString())); // Otherwise, assign them to team T.
                    ++count;
                }
                return;
            }
            // switch everyone to their own teams
            foreach (var weaponManager in weaponManagers.SelectMany(tm => tm.Value).Where(wm => wm != null).ToList()) // Get a copy in case activating stages causes the weaponManager list to change.
            {
                Debug.Log("[BDArmory]: assigning " + weaponManager.vessel.GetDisplayName() + " to team " + T.ToString());
                weaponManager.SetTeam(BDTeam.Get(T.ToString()));
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

        private void UpdateCamera()
        {
            double timeSinceLastCheck = Planetarium.GetUniversalTime() - lastCameraCheck;


            if (timeSinceLastCheck > 0.25)
            {
                lastCameraCheck = Planetarium.GetUniversalTime();

                // first check to see if we've changed the vessel recently
                if (lastActiveVessel != null)
                {
                    if (!lastActiveVessel.isActiveVessel)
                    {
                        // active vessel was changed 
                        lastCameraSwitch = Planetarium.GetUniversalTime();
                        lostActiveVessel = false;
                    }
                }
                lastActiveVessel = FlightGlobals.ActiveVessel;
                double timeSinceChange = Planetarium.GetUniversalTime() - lastCameraSwitch;

                float bestScore = 10000000;
                Vessel bestVessel = null;
                bool foundActiveVessel = false;
                // redo the math
                using (List<Vessel>.Enumerator v = FlightGlobals.Vessels.GetEnumerator())
                    // check all the planes
                    while (v.MoveNext())
                    {
                        if (v.Current == null || !v.Current.loaded || v.Current.packed)
                            continue;
                        using (var wms = v.Current.FindPartModulesImplementing<MissileFire>().GetEnumerator())
                            while (wms.MoveNext())
                                if (wms.Current != null && wms.Current.vessel != null)
                                {
                                    float vesselScore = 1000;
                                    float targetDistance = 5000 + (float)(rng.NextDouble() * 100.0);
                                    float crashTime = 30;
                                    string vesselName = v.Current.GetName();
                                    // avoid lingering on dying things
                                    bool recentlyDamaged = false;
                                    bool recentlyLanded = false;

                                    // check for damage & landed status

                                    if (BDACompetitionMode.Instance.Scores.ContainsKey(vesselName))
                                    {
                                        var currentParts = v.Current.parts.Count;
                                        var vdat = BDACompetitionMode.Instance.Scores[vesselName];
                                        if (currentParts < vdat.previousPartCount)
                                        {
                                            recentlyDamaged = true;
                                        }

                                        if (vdat.landedState)
                                        {
                                            var timeSinceLanded = Planetarium.GetUniversalTime() - vdat.lastLandedTime;
                                            if (timeSinceLanded < 2)
                                            {
                                                recentlyLanded = true;
                                            }
                                        }
                                    }
                                    vesselScore = Math.Abs(vesselScore);

                                    if (!recentlyLanded && v.Current.verticalSpeed < -15) // Vessels gently floating to the ground aren't interesting
                                    {
                                        crashTime = (float)(-Math.Abs(v.Current.radarAltitude) / v.Current.verticalSpeed);
                                    }
                                    if (wms.Current.currentTarget != null)
                                    {
                                        targetDistance = Vector3.Distance(wms.Current.vessel.GetWorldPos3D(), wms.Current.currentTarget.position);
                                    }
                                    vesselScore *= 0.031623f * Mathf.Sqrt(targetDistance); // Equal to 1 at 1000m
                                    if (crashTime < 30)
                                    {
                                        vesselScore *= crashTime / 30;
                                    }
                                    if (wms.Current.currentGun != null)
                                    {
                                        if (wms.Current.currentGun.recentlyFiring)
                                        {
                                            // shooting at things is more interesting
                                            vesselScore *= 0.25f;
                                        }
                                    }
                                    if (wms.Current.guardFiringMissile)
                                    {
                                        // firing a missile at things is more interesting
                                        vesselScore *= 0.2f;
                                    }
                                    // scoring for automagic camera check should not be in here
                                    if (wms.Current.underAttack || wms.Current.underFire)
                                    {
                                        vesselScore *= 0.5f;
                                        var distance = Vector3.Distance(wms.Current.vessel.GetWorldPos3D(), wms.Current.incomingThreatPosition);
                                        vesselScore *= 0.031623f * Mathf.Sqrt(distance); // Equal to 1 at 1000m, we don't want to overly disadvantage craft that are super far away, but could be firing missiles or doing other interesting things
                                        //we're very interested when threat and target are the same
                                        if (wms.Current.incomingThreatVessel != null && wms.Current.currentTarget != null)
                                        {
                                            if (wms.Current.incomingThreatVessel.GetName() == wms.Current.currentTarget.Vessel.GetName())
                                            {
                                                vesselScore *= 0.25f;
                                            }
                                        }

                                    }
                                    if (wms.Current.incomingMissileVessel != null)
                                    {
                                        float timeToImpact = wms.Current.incomingMissileDistance / (float)wms.Current.incomingMissileVessel.srfSpeed;
                                        vesselScore *= Mathf.Clamp(0.0005f * timeToImpact * timeToImpact, 0, 1); // Missiles about to hit are interesting, scale score with time to impact

                                        if (wms.Current.isFlaring || wms.Current.isChaffing)
                                            vesselScore *= 0.8f;
                                    }
                                    if (recentlyDamaged)
                                    {
                                        vesselScore *= 0.3f; // because taking hits is very interesting;
                                    }
                                    if (!recentlyLanded && wms.Current.vessel.LandedOrSplashed)
                                    {
                                        vesselScore *= 3; // not interesting.
                                    }
                                    // if we're the active vessel add a penalty over time to force it to switch away eventually
                                    if (wms.Current.vessel.isActiveVessel)
                                    {
                                        vesselScore = (float)(vesselScore * timeSinceChange / 8.0);
                                        foundActiveVessel = true;
                                    }
                                    if ((BDArmorySettings.TAG_MODE) && (wms.Current.Team.Name == "IT"))
                                    {
                                        vesselScore = 0f; // Keep camera focused on "IT" vessel during tag
                                    }


                                    // if the score is better then update this
                                    if (vesselScore < bestScore)
                                    {
                                        bestVessel = wms.Current.vessel;
                                        bestScore = vesselScore;
                                    }
                                    cameraScores[wms.Current.vessel.GetName()] = vesselScore;
                                }
                    }
                if (!foundActiveVessel)
                {
                    if (!lostActiveVessel)
                    {
                        // the active vessel is no longer in our list, so it probably just got shot
                        // we need to make sure we follow it for a few seconds
                        lastCameraSwitch = Planetarium.GetUniversalTime();
                        lostActiveVessel = true;
                    }
                    else
                    {
                        var score = 100 * timeSinceChange;
                        if (score < bestScore)
                        {
                            bestVessel = null; // stop switching
                        }
                    }
                }
                if (timeSinceChange > BDArmorySettings.CAMERA_SWITCH_FREQUENCY)
                {
                    if (bestVessel != null && !(bestVessel.isActiveVessel)) // if a vessel dies it'll use a default score for a few seconds
                    {
                        Debug.Log("[BDArmory]: Switching vessel to " + bestVessel.GetDisplayName());
                        ForceSwitchVessel(bestVessel);
                    }
                }
            }
        }



        // Extracted method, so we dont have to call these two lines everywhere
        public void ForceSwitchVessel(Vessel v)
        {
            lastCameraSwitch = Planetarium.GetUniversalTime();
            lostActiveVessel = false;
            FlightGlobals.ForceSetActiveVessel(v);
            FlightInputHandler.ResumeVesselCtrlState(v);
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
}
