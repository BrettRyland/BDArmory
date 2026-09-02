using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

using BDArmory.Competition.OrchestrationStrategies;
using BDArmory.Competition;
using BDArmory.GameModes.Waypoints;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.VesselSpawning.SpawnStrategies;
using BDArmory.VesselSpawning;
using BDArmory.Extensions;
using BDArmory.Modules;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class VesselSpawnerWindow : MonoBehaviour
    {
        #region Fields
        public static VesselSpawnerWindow Instance;
        private static int _guiCheckIndex = -1;
        private static readonly float _buttonSize = 20;
        private static readonly float _margin = 5;
        private static readonly float _lineHeight = _buttonSize;
        private float _windowHeight; //auto adjusting
        private float _windowWidth;
        public bool _ready = false;
        private bool _vesselsSpawned = false;
        Dictionary<string, NumericInputField> spawnFields;

        private GUIContent[] planetGUI;
        private GUIContent planetText;
        private BDGUIComboBox planetBox;
        private int previous_index = 1;
        private bool planetslist = false;
        int selected_index = 1;
        int WaygateCount = -1;
        public int gateModelsCount => WaygateCount;
        public float SelectedGate = -1;
        public static string Gatepath;
        public string SelectedModel;
        public string[] gateFiles;
        private bool waypointsRunning = false;
        #endregion
        #region GUI strings
        string tournamentStyle = $"{(TournamentStyle)0}";
        string tournamentRoundType = $"{(TournamentRoundType)0}";
        int tournamentStyleMax = Enum.GetNames(typeof(TournamentStyle)).Length - 1;
        int tournamentRoundTypeMax = Enum.GetNames(typeof(TournamentRoundType)).Length - 1;
        #endregion

        #region Styles
        Rect SLineRect(float line)
        {
            return new Rect(_margin, line * _lineHeight, _windowWidth - 2 * _margin, _lineHeight);
        }

        Rect SLeftRect(float line)
        {
            return new Rect(_margin, line * _lineHeight, _windowWidth / 2 - _margin - _margin / 4, _lineHeight);
        }

        Rect SRightRect(float line)
        {
            return new Rect(_windowWidth / 2 + _margin / 4, line * _lineHeight, _windowWidth / 2 - _margin - _margin / 4, _lineHeight);
        }

        Rect SLeftSliderRect(float line)
        {
            return new Rect(_margin, line * _lineHeight, _windowWidth / 2 + _margin / 2, _lineHeight);
        }

        Rect SRightSliderRect(float line)
        {
            return new Rect(_margin + _windowWidth / 2 + _margin / 2, line * _lineHeight, _windowWidth / 2 - 7 / 2 * _margin, _lineHeight);
        }

        Rect SLeftButtonRect(float line)
        {
            return new Rect(_margin, line * _lineHeight, (_windowWidth - 2 * _margin) / 2 - _margin / 4, _lineHeight);
        }

        Rect SRightButtonRect(float line)
        {
            return new Rect(_windowWidth / 2 + _margin / 4, line * _lineHeight, (_windowWidth - 2 * _margin) / 2 - _margin / 4, _lineHeight);
        }

        Rect SThirdRect(float line, int pos, int span = 1, float indent = 0)
        {
            return new Rect(_margin + pos * (_windowWidth - 2f * _margin) / 3f + indent, line * _lineHeight, span * (_windowWidth - 2f * _margin) / 3f - indent, _lineHeight);
        }

        Rect SQuarterRect(float line, int pos, int span = 1, float indent = 0)
        {
            return new Rect(_margin + (pos % 4) * (_windowWidth - 2f * _margin) / 4f + indent, (line + (int)(pos / 4)) * _lineHeight, span * (_windowWidth - 2f * _margin) / 4f - indent, _lineHeight);
        }

        Rect SEighthRect(float line, int pos, int span = 1, float indent = 0)
        {
            return new Rect(_margin + (pos % 8) * (_windowWidth - 2f * _margin) / 8f + indent, (line + (int)(pos / 8)) * _lineHeight, span * (_windowWidth - 2f * _margin) / 8f - indent, _lineHeight);
        }

        Rect ShortLabel(float line, float width)
        {
            return new Rect(_margin, line * _lineHeight, width, _lineHeight);
        }

        List<Rect> SRight2Rects(float line)
        {
            var rectGap = _margin / 2;
            var rectWidth = ((_windowWidth - 2 * _margin) / 2 - 2 * rectGap) / 2;
            var rects = new List<Rect>();
            rects.Add(new Rect(_windowWidth / 2 + rectGap / 2, line * _lineHeight, rectWidth, _lineHeight));
            rects.Add(new Rect(_windowWidth / 2 + rectWidth + rectGap * 3 / 2, line * _lineHeight, rectWidth, _lineHeight));
            return rects;
        }

        List<Rect> SRight3Rects(float line)
        {
            var rectGap = _margin / 3;
            var rectWidth = ((_windowWidth - 2 * _margin) / 2 - 3 * rectGap) / 3;
            var rects = new List<Rect>();
            rects.Add(new Rect(_windowWidth / 2 + rectGap / 2, line * _lineHeight, rectWidth, _lineHeight));
            rects.Add(new Rect(_windowWidth / 2 + rectWidth + rectGap * 3 / 2, line * _lineHeight, rectWidth, _lineHeight));
            rects.Add(new Rect(_windowWidth / 2 + 2 * rectWidth + rectGap * 5 / 2, line * _lineHeight, rectWidth, _lineHeight));
            return rects;
        }

        GUIStyle leftLabel;
        GUIStyle listStyle;
        #endregion
        private string txtName = string.Empty;
        private void Awake()
        {
            if (Instance)
                Destroy(this);
            Instance = this;
        }

        private void Start()
        {
            _ready = false;
            StartCoroutine(WaitForBdaSettings());

            leftLabel = new GUIStyle
            {
                alignment = TextAnchor.UpperLeft,
                normal = new GUIStyleState { textColor = Color.white }
            };
            listStyle = new GUIStyle(BDArmorySetup.ButtonStyle)
            {
                fixedHeight = 18 //make list contents slightly smaller
            };

            // Spawn fields
            spawnFields = new Dictionary<string, NumericInputField> {
                { "lat", gameObject.AddComponent<NumericInputField>().Initialise(0, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x, -90, 90) },
                { "lon", gameObject.AddComponent<NumericInputField>().Initialise(0, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y, -180, 180) },
                { "alt", gameObject.AddComponent<NumericInputField>().Initialise(0, BDArmorySettings.VESSEL_SPAWN_ALTITUDE) },
            };
            selected_index = FlightGlobals.currentMainBody != null ? FlightGlobals.currentMainBody.flightGlobalsIndex : 1;

            tournamentStyle = $"{(TournamentStyle)BDArmorySettings.TOURNAMENT_STYLE}";
            tournamentRoundType = $"{(TournamentRoundType)BDArmorySettings.TOURNAMENT_ROUND_TYPE}";

            try
            {
                Gatepath = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData", WaypointFollowingStrategy.ModelPath)));
                gateFiles = Directory.GetFiles(Gatepath, "*.mu").Select(f => Path.GetFileName(f)).ToArray();
                Array.Sort(gateFiles, StringComparer.Ordinal); // Sort them alphabetically, uppercase first.
                WaygateCount = gateFiles.Count() - 1;
            }
            catch (Exception e)
            {
                Debug.LogError($"[BDArmory.VesselSpawnerWindow]: Failed to locate waypoint marker models: {e.Message}");
            }
            if (WaygateCount >= 0)
            {
                if (SelectedGate >= 0) SelectedModel = Path.GetFileNameWithoutExtension(gateFiles[(int)Mathf.Clamp(SelectedGate, 0, WaygateCount)]);
            }
            else Debug.LogWarning($"[BDArmory.VesselSpawnerWindow]: No waypoint gate models found in {Gatepath}!");
            if (BDArmorySettings.WAYPOINT_COURSE_INDEX >= WaypointCourses.CourseLocations.Count) BDArmorySettings.WAYPOINT_COURSE_INDEX = 0; // Sanitise the index in case the course list has changed.
        }

        private IEnumerator WaitForBdaSettings()
        {
            yield return new WaitUntil(() => BDArmorySetup.Instance is not null);

            BDArmorySetup.Instance.hasVesselSpawner = true;
            if (_guiCheckIndex < 0) _guiCheckIndex = GUIUtils.RegisterGUIRect(new Rect());
            if (_observerGUICheckIndex < 0) _observerGUICheckIndex = GUIUtils.RegisterGUIRect(new Rect());
            _ready = true;
            SetVisible(BDArmorySetup.showVesselSpawnerGUI);
        }

        private void FillPlanetList()
        {
            planetGUI = new GUIContent[FlightGlobals.Bodies.Count];
            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                GUIContent gui = new GUIContent(FlightGlobals.Bodies[i].name);
                planetGUI[i] = gui;
            }

            planetText = new GUIContent();
            planetText.text = StringUtils.Localize("#LOC_BDArmory_Settings_Planet");//"Select Planet"
        }

        private void Update()
        {
            HotKeys();
            if (potentialObserversNeedsRefreshing) RefreshObservers();
        }

        private void OnGUI()
        {
            if (!(_ready && BDArmorySetup.GAME_UI_ENABLED && BDArmorySetup.showVesselSpawnerGUI && HighLogic.LoadedSceneIsFlight))
                return;

            _windowWidth = BDArmorySettings.VESSEL_SPAWNER_WINDOW_WIDTH;
            SetNewHeight(_windowHeight);
            BDArmorySetup.WindowRectVesselSpawner = new Rect(
                BDArmorySetup.WindowRectVesselSpawner.x,
                BDArmorySetup.WindowRectVesselSpawner.y,
                _windowWidth,
                _windowHeight
            );
            BDArmorySetup.SetGUIOpacity();
            var guiMatrix = GUI.matrix;
            if (BDArmorySettings.UI_SCALE_ACTUAL != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, BDArmorySetup.WindowRectVesselSpawner.position);
            BDArmorySetup.WindowRectVesselSpawner = GUI.Window(
                GUIUtility.GetControlID(FocusType.Passive),
                BDArmorySetup.WindowRectVesselSpawner,
                WindowVesselSpawner,
                StringUtils.Localize("#LOC_BDArmory_BDAVesselSpawner_Title"),//"BDA Vessel Spawner"
                BDArmorySetup.BDGuiSkin.window
            );
            BDArmorySetup.SetGUIOpacity(false);
            GUIUtils.UpdateGUIRect(BDArmorySetup.WindowRectVesselSpawner, _guiCheckIndex);
            if (showObserverWindow)
            {
                if (Event.current.type == EventType.MouseDown && !observerWindowRect.Contains(Event.current.mousePosition))
                    ShowObserverWindow(false);
                else
                {
                    if (BDArmorySettings.UI_SCALE_ACTUAL != 1) { GUI.matrix = guiMatrix; GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, observerWindowRect.position); }
                    observerWindowRect = GUILayout.Window(GUIUtility.GetControlID(FocusType.Passive), observerWindowRect, ObserverWindow, StringUtils.Localize("#LOC_BDArmory_ObserverSelection_Title"), BDArmorySetup.BDGuiSkin.window);
                }
            }
        }

        void HotKeys()
        {
            if (BDInputUtils.GetKeyDown(BDInputSettingsFields.TOURNAMENT_SETUP))
                BDATournament.Instance.SetupTournament(
                    BDArmorySettings.VESSEL_SPAWN_FILES_LOCATION,
                    BDArmorySettings.TOURNAMENT_ROUNDS,
                    BDArmorySettings.TOURNAMENT_VESSELS_PER_HEAT,
                    BDArmorySettings.TOURNAMENT_NPCS_PER_HEAT,
                    BDArmorySettings.TOURNAMENT_TEAMS_PER_HEAT,
                    BDArmorySettings.TOURNAMENT_VESSELS_PER_TEAM,
                    BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS,
                    (TournamentStyle)BDArmorySettings.TOURNAMENT_STYLE,
                    (TournamentRoundType)BDArmorySettings.TOURNAMENT_ROUND_TYPE
                );
            if (BDInputUtils.GetKeyDown(BDInputSettingsFields.TOURNAMENT_RUN))
                BDATournament.Instance.RunTournament();
        }

        void ParseAllSpawnFieldsNow()
        {
            spawnFields["lat"].tryParseValueNow();
            spawnFields["lon"].tryParseValueNow();
            spawnFields["alt"].tryParseValueNow();
            BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x = spawnFields["lat"].CurrentValue;
            BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y = spawnFields["lon"].CurrentValue;
            BDArmorySettings.VESSEL_SPAWN_WORLDINDEX = FlightGlobals.currentMainBody != null ? FlightGlobals.currentMainBody.flightGlobalsIndex : 1; //selected_index?
            BDArmorySettings.VESSEL_SPAWN_ALTITUDE = (float)spawnFields["alt"].CurrentValue;
        }

        /// <summary>
        /// Update the value of the spawn fields from the current settings values.
        /// </summary>
        public void RefreshSpawnFieldsFromSettings()
        {
            spawnFields["lat"].SetCurrentValue(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x);
            spawnFields["lon"].SetCurrentValue(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y);
            spawnFields["alt"].SetCurrentValue(BDArmorySettings.VESSEL_SPAWN_ALTITUDE);
        }

        private void SetNewHeight(float windowHeight)
        {
            var previousWindowHeight = BDArmorySetup.WindowRectVesselSpawner.height;
            BDArmorySetup.WindowRectVesselSpawner.height = windowHeight;
            GUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectVesselSpawner, previousWindowHeight);
        }

        (float, float)[] cacheVesselSpawnDistance;
        private void WindowVesselSpawner(int id)
        {
            if (GUI.Button(new Rect(_windowWidth - _buttonSize - (_margin - 2), _margin, _buttonSize - 2, _buttonSize - 2), " X", BDArmorySetup.CloseButtonStyle))
            {
                SetVisible(false);
                BDArmorySetup.SaveConfig();
            }

            float line = 0.25f;
            var rects = new List<Rect>();

            if (GUI.Button(SLineRect(++line), $"{(BDArmorySettings.SHOW_SPAWN_OPTIONS ? StringUtils.Localize("#LOC_BDArmory_Generic_Hide") : StringUtils.Localize("#LOC_BDArmory_Generic_Show"))} {StringUtils.Localize("#LOC_BDArmory_Settings_SpawnOptions")}", BDArmorySettings.SHOW_SPAWN_OPTIONS ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))//Show/hide spawn options
            {
                BDArmorySettings.SHOW_SPAWN_OPTIONS = !BDArmorySettings.SHOW_SPAWN_OPTIONS;
            }
            if (BDArmorySettings.SHOW_SPAWN_OPTIONS)
            {
                if (BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE)
                { // Absolute distance
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_SpawnDistance")}:  ({(BDArmorySettings.VESSEL_SPAWN_DISTANCE < 1000 ? $"{BDArmorySettings.VESSEL_SPAWN_DISTANCE:G4}m" : $"{BDArmorySettings.VESSEL_SPAWN_DISTANCE / 1000:G4}km")})", leftLabel);//Spawn Distance
                    BDArmorySettings.VESSEL_SPAWN_DISTANCE = GUIUtils.HorizontalSemiLogSlider(SRightSliderRect(line), BDArmorySettings.VESSEL_SPAWN_DISTANCE, 10, 200000, 1.5f, false, false, ref cacheVesselSpawnDistance);
                }
                else
                { // Distance factor
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_SpawnDistanceFactor")}:  ({BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR})", leftLabel);//Spawn Distance Factor
                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR / 10f, 1f, 10f) * 10f);
                }
                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_SpawnRefHeading")}:  ({BDArmorySettings.VESSEL_SPAWN_REF_HEADING:000}°)", leftLabel); // Reference Heading
                BDArmorySettings.VESSEL_SPAWN_REF_HEADING = Mathf.Round(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.VESSEL_SPAWN_REF_HEADING, 0, 359));

                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_SpawnEaseInSpeed")}:  ({BDArmorySettings.VESSEL_MOVER_MIN_LOWER_SPEED})", leftLabel);//Spawn Ease-In Speed (actually the VM min lower speed)
                BDArmorySettings.VESSEL_MOVER_MIN_LOWER_SPEED = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.VESSEL_MOVER_MIN_LOWER_SPEED, 0.1f, 1f), 0.1f);

                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_SpawnConcurrentVessels")}:  ({(BDArmorySettings.VESSEL_SPAWN_CONCURRENT_VESSELS > 0 ? BDArmorySettings.VESSEL_SPAWN_CONCURRENT_VESSELS.ToString() : "Inf")})", leftLabel);//Max Concurrent Vessels (CS)
                BDArmorySettings.VESSEL_SPAWN_CONCURRENT_VESSELS = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.VESSEL_SPAWN_CONCURRENT_VESSELS, 0f, 20f));

                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_SpawnLivesPerVessel")}:  ({(BDArmorySettings.VESSEL_SPAWN_LIVES_PER_VESSEL > 0 ? BDArmorySettings.VESSEL_SPAWN_LIVES_PER_VESSEL.ToString() : "Inf")})", leftLabel);//Respawns (CS)
                BDArmorySettings.VESSEL_SPAWN_LIVES_PER_VESSEL = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.VESSEL_SPAWN_LIVES_PER_VESSEL, 0f, 20f));

                var outOfAmmoKillTimeStr = "never";
                if (BDArmorySettings.OUT_OF_AMMO_KILL_TIME > -1 && BDArmorySettings.OUT_OF_AMMO_KILL_TIME < 60)
                    outOfAmmoKillTimeStr = $"{BDArmorySettings.OUT_OF_AMMO_KILL_TIME:G0}s";
                else if (BDArmorySettings.OUT_OF_AMMO_KILL_TIME > 59 && BDArmorySettings.OUT_OF_AMMO_KILL_TIME < 61)
                    outOfAmmoKillTimeStr = "1min";
                else if (BDArmorySettings.OUT_OF_AMMO_KILL_TIME > 60)
                    outOfAmmoKillTimeStr = $"{Mathf.RoundToInt(BDArmorySettings.OUT_OF_AMMO_KILL_TIME / 60f):G0}mins";
                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_OutOfAmmoKillTime")}: ({outOfAmmoKillTimeStr})", leftLabel); // Out of ammo kill timer for continuous spawning mode.
                float outOfAmmoKillTime;
                switch (Mathf.RoundToInt(BDArmorySettings.OUT_OF_AMMO_KILL_TIME))
                {
                    case 0:
                        outOfAmmoKillTime = 1f;
                        break;
                    case 10:
                        outOfAmmoKillTime = 2f;
                        break;
                    case 20:
                        outOfAmmoKillTime = 3f;
                        break;
                    case 30:
                        outOfAmmoKillTime = 4f;
                        break;
                    case 45:
                        outOfAmmoKillTime = 5f;
                        break;
                    case 60:
                        outOfAmmoKillTime = 6f;
                        break;
                    case 120:
                        outOfAmmoKillTime = 7f;
                        break;
                    case 300:
                        outOfAmmoKillTime = 8f;
                        break;
                    default:
                        outOfAmmoKillTime = 9f;
                        break;
                }
                outOfAmmoKillTime = GUI.HorizontalSlider(SRightSliderRect(line), outOfAmmoKillTime, 1f, 9f);
                switch (Mathf.RoundToInt(outOfAmmoKillTime))
                {
                    case 1:
                        BDArmorySettings.OUT_OF_AMMO_KILL_TIME = 0f; // 0s
                        break;
                    case 2:
                        BDArmorySettings.OUT_OF_AMMO_KILL_TIME = 10f; // 10s
                        break;
                    case 3:
                        BDArmorySettings.OUT_OF_AMMO_KILL_TIME = 20f; // 20s
                        break;
                    case 4:
                        BDArmorySettings.OUT_OF_AMMO_KILL_TIME = 30f; // 30s
                        break;
                    case 5:
                        BDArmorySettings.OUT_OF_AMMO_KILL_TIME = 45f; // 45s
                        break;
                    case 6:
                        BDArmorySettings.OUT_OF_AMMO_KILL_TIME = 60f; // 1 min
                        break;
                    case 7:
                        BDArmorySettings.OUT_OF_AMMO_KILL_TIME = 120f;// 2 mins
                        break;
                    case 8:
                        BDArmorySettings.OUT_OF_AMMO_KILL_TIME = 300f; // 5 mins
                        break;
                    default:
                        BDArmorySettings.OUT_OF_AMMO_KILL_TIME = -1f; // Never
                        break;
                }

                string fillSeats = "";
                switch (BDArmorySettings.VESSEL_SPAWN_FILL_SEATS)
                {
                    case 0:
                        fillSeats = StringUtils.Localize("#LOC_BDArmory_Settings_SpawnFillSeats_Minimal");
                        break;
                    case 1:
                        fillSeats = StringUtils.Localize("#LOC_BDArmory_Settings_SpawnFillSeats_Default"); // Full cockpits or the first combat seat if no cockpits are found.
                        break;
                    case 2:
                        fillSeats = StringUtils.Localize("#LOC_BDArmory_Settings_SpawnFillSeats_AllControlPoints");
                        break;
                    case 3:
                        fillSeats = StringUtils.Localize("#LOC_BDArmory_Settings_SpawnFillSeats_Cabins");
                        break;
                }
                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_SpawnFillSeats")}:  ({fillSeats})", leftLabel); // Fill Seats
                BDArmorySettings.VESSEL_SPAWN_FILL_SEATS = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.VESSEL_SPAWN_FILL_SEATS, 0f, 3f));

                string numberOfTeams;
                switch (BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS)
                {
                    case 0: // FFA
                        numberOfTeams = StringUtils.Localize("#LOC_BDArmory_Settings_Teams_FFA");
                        break;
                    case 1: // Folders
                        numberOfTeams = StringUtils.Localize("#LOC_BDArmory_Settings_Teams_Folders");
                        break;
                    case 11: // Custom Template
                        numberOfTeams = StringUtils.Localize("#LOC_BDArmory_Settings_Teams_Custom_Template");
                        break;
                    default: // Specified directly
                        numberOfTeams = $"{StringUtils.Localize("#LOC_BDArmory_Settings_Teams_SplitEvenly")} {BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS:0}";
                        break;
                }
                GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_Teams")}:  ({numberOfTeams})", leftLabel); // Number of teams.
                BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS, 0f, 11f));

                if (!BDArmorySettings.DISABLE_KERBAL_SUIT_SELECTION)
                {
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_KerbalSuitTypeOverride")}: {(KerbalSuitSelector.KerbalSuit)BDArmorySettings.VESSEL_SPAWN_KERBAL_SUIT_TYPE}", leftLabel); // Default Kerbal Suit
                    BDArmorySettings.VESSEL_SPAWN_KERBAL_SUIT_TYPE = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.VESSEL_SPAWN_KERBAL_SUIT_TYPE, -1f, 4f));
                }

                GUI.Label(SLeftRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_SpawnFilesLocation")} (AutoSpawn{Path.DirectorySeparatorChar}): ", leftLabel); // Craft files location
                BDArmorySettings.VESSEL_SPAWN_FILES_LOCATION = GUI.TextField(SRightRect(line), BDArmorySettings.VESSEL_SPAWN_FILES_LOCATION);

                BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE, StringUtils.Localize("#LOC_BDArmory_Settings_SpawnDistanceToggle"));  // Toggle between distance factor and absolute distance.
                BDArmorySettings.VESSEL_SPAWN_REASSIGN_TEAMS = GUI.Toggle(SRightRect(line), BDArmorySettings.VESSEL_SPAWN_REASSIGN_TEAMS, StringUtils.Localize("#LOC_BDArmory_Settings_SpawnReassignTeams")); // Reassign Teams
                BDArmorySettings.VESSEL_SPAWN_START_COMPETITION_AUTOMATICALLY = GUI.Toggle(SLeftRect(++line), BDArmorySettings.VESSEL_SPAWN_START_COMPETITION_AUTOMATICALLY, StringUtils.Localize("#LOC_BDArmory_Settings_SpawnStartCompetitionAutomatically")); // Automatically start a competition if spawning succeeds.
                BDArmorySettings.VESSEL_SPAWN_RANDOM_ORDER = GUI.Toggle(SRightRect(line), BDArmorySettings.VESSEL_SPAWN_RANDOM_ORDER, StringUtils.Localize("#LOC_BDArmory_Settings_SpawnRandomOrder"));  // Toggle between random spawn order or fixed.
                BDArmorySettings.VESSEL_SPAWN_CONTINUE_SINGLE_SPAWNING = GUI.Toggle(SLeftRect(++line), BDArmorySettings.VESSEL_SPAWN_CONTINUE_SINGLE_SPAWNING, StringUtils.Localize("#LOC_BDArmory_Settings_SpawnContinueSingleSpawning"));  // Spawn craft again after single spawn competition finishes.
                BDArmorySettings.VESSEL_SPAWN_INITIAL_VELOCITY = GUI.Toggle(SRightRect(line), BDArmorySettings.VESSEL_SPAWN_INITIAL_VELOCITY, StringUtils.Localize("#LOC_BDArmory_Settings_SpawnInitialVelocity")); // Planes spawn at their idle speed.
                ++line; // Placeholder for a removed entry.
                BDArmorySettings.VESSEL_SPAWN_CS_FOLLOWS_CENTROID = GUI.Toggle(SRightRect(line), BDArmorySettings.VESSEL_SPAWN_CS_FOLLOWS_CENTROID, StringUtils.Localize("#LOC_BDArmory_Settings_CSFollowsCentroid")); //CS spawn-point follows centroid.

                if (GUI.Button(SLeftButtonRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_WarpHere"), BDArmorySetup.ButtonStyle))
                {
                    SpawnUtils.ShowSpawnPoint(selected_index, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y, BDArmorySettings.VESSEL_SPAWN_ALTITUDE);
                }
                if (GUI.Button(SRightButtonRect(line), StringUtils.Localize("#LOC_BDArmory_Settings_SpawnSpawnProbeHere"), BDArmorySetup.ButtonStyle))
                {
                    var spawnProbe = VesselSpawner.SpawnSpawnProbe(FlightCamera.fetch.Distance * FlightCamera.fetch.mainCamera.transform.forward);
                    if (spawnProbe != null)
                    {
                        spawnProbe.Landed = false;
                        StartCoroutine(LoadedVesselSwitcher.Instance.SwitchToVesselWhenPossible(spawnProbe));
                    }
                }

                if (GUI.Button(SLeftButtonRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_VesselSpawnGeoCoords"), BDArmorySetup.ButtonStyle)) //"Vessel Spawning Location"
                {
                    Ray ray = new Ray(FlightCamera.fetch.mainCamera.transform.position, FlightCamera.fetch.mainCamera.transform.forward);
                    RaycastHit hit;
                    if (Physics.Raycast(ray, out hit, 10000, (int)LayerMasks.Scenery))
                    {
                        BDArmorySettings.VESSEL_SPAWN_GEOCOORDS = FlightGlobals.currentMainBody.GetLatitudeAndLongitude(hit.point);
                        spawnFields["lat"].SetCurrentValue(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x);
                        spawnFields["lon"].SetCurrentValue(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y);
                    }
                }
                rects = SRight3Rects(line);
                spawnFields["lat"].TryParseValue(GUI.TextField(rects[0], spawnFields["lat"].possibleValue, 8, spawnFields["lat"].style));
                spawnFields["lon"].TryParseValue(GUI.TextField(rects[1], spawnFields["lon"].possibleValue, 8, spawnFields["lon"].style));
                spawnFields["alt"].TryParseValue(GUI.TextField(rects[2], spawnFields["alt"].possibleValue, 8, spawnFields["alt"].style));
                BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x = spawnFields["lat"].CurrentValue;
                BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y = spawnFields["lon"].CurrentValue;
                BDArmorySettings.VESSEL_SPAWN_WORLDINDEX = FlightGlobals.currentMainBody != null ? FlightGlobals.currentMainBody.flightGlobalsIndex : 1;
                BDArmorySettings.VESSEL_SPAWN_ALTITUDE = (float)spawnFields["alt"].CurrentValue;

                txtName = GUI.TextField(SRightButtonRect(++line), txtName);
                if (GUI.Button(SLeftButtonRect(line), StringUtils.Localize("#LOC_BDArmory_Settings_SaveSpawnLoc"), BDArmorySetup.ButtonStyle))
                {
                    string newName = string.IsNullOrEmpty(txtName.Trim()) ? "New Location" : txtName.Trim();
                    SpawnLocations.spawnLocations.Add(new SpawnLocation(newName, new Vector2d(spawnFields["lat"].CurrentValue, spawnFields["lon"].CurrentValue), selected_index));
                    VesselSpawnerField.Save();
                }

                if (GUI.Button(SThirdRect(++line, 0), StringUtils.Localize("#LOC_BDArmory_Settings_ClearDebrisNow"), BDArmorySetup.ButtonStyle))
                {
                    // Clean up debris now
                    BDACompetitionMode.Instance.RemoveDebrisNow();
                }
                if (GUI.Button(SThirdRect(line, 1), StringUtils.Localize("#LOC_BDArmory_Settings_ClearBystandersNow"), BDArmorySetup.ButtonStyle))
                {
                    // Clean up bystanders now
                    BDACompetitionMode.Instance.RemoveNonCompetitors(true);
                }
                if (GUI.Button(SThirdRect(line, 2), StringUtils.Localize("#LOC_BDArmory_Settings_Observers"), BDArmorySetup.ButtonStyle))
                {
                    ShowObserverWindow(true, BDArmorySettings.UI_SCALE_ACTUAL * Event.current.mousePosition + BDArmorySetup.WindowRectVesselSpawner.position);
                }
                line += 0.3f;
            }

            if (GUI.Button(SLineRect(++line), $"{(BDArmorySettings.SHOW_SPAWN_LOCATIONS ? StringUtils.Localize("#LOC_BDArmory_Generic_Hide") : StringUtils.Localize("#LOC_BDArmory_Generic_Show"))} {StringUtils.Localize("#LOC_BDArmory_Settings_SpawnLocations")}", BDArmorySettings.SHOW_SPAWN_LOCATIONS ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))//Show/hide spawn locations
            {
                BDArmorySettings.SHOW_SPAWN_LOCATIONS = !BDArmorySettings.SHOW_SPAWN_LOCATIONS;
            }
            if (BDArmorySettings.SHOW_SPAWN_LOCATIONS)
            {
                line++;
                ///////////////////
                if (!planetslist)
                {
                    FillPlanetList();
                    planetBox = new BDGUIComboBox(SLeftButtonRect(line), SLineRect(line), planetText, planetGUI, _lineHeight * 6, listStyle, 3);
                    planetslist = true;
                }
                planetBox.UpdateRect(SLeftButtonRect(line));
                selected_index = planetBox.Show();
                if (planetBox.IsOpen)
                {
                    line += planetBox.Height / _lineHeight;
                }
                if (selected_index != previous_index)
                {
                    if (selected_index != -1)
                    {
                        //selectedWorld = FlightGlobals.Bodies[selected_index].name;
                        //Debug.Log("selected World Index: " + selected_index);
                        BDArmorySettings.VESSEL_SPAWN_WORLDINDEX = selected_index;
                    }
                    previous_index = selected_index;
                }
                if (selected_index == -1)
                {
                    selected_index = 1;
                    previous_index = 1;
                }
                ////////////////////
                ++line;
                int i = 0;
                foreach (var spawnLocation in SpawnLocations.spawnLocations)
                {
                    if (spawnLocation.worldIndex != selected_index) continue;
                    if (GUI.Button(SQuarterRect(line, i++), spawnLocation.name, BDArmorySetup.ButtonStyle))
                    {
                        switch (Event.current.button)
                        {
                            case 1: // right click
                                SpawnLocations.spawnLocations.Remove(spawnLocation);
                                break;
                            default:
                                BDArmorySettings.VESSEL_SPAWN_GEOCOORDS = spawnLocation.location;
                                BDArmorySettings.VESSEL_SPAWN_WORLDINDEX = spawnLocation.worldIndex;
                                spawnFields["lat"].SetCurrentValue(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x);
                                spawnFields["lon"].SetCurrentValue(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y);
                                SpawnUtils.ShowSpawnPoint(selected_index, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y, BDArmorySettings.VESSEL_SPAWN_ALTITUDE);
                                break;
                        }
                    }
                }
                line += (int)((i - 1) / 4);
                line += 0.3f;
            }

            if (BDArmorySettings.WAYPOINTS_MODE)
            {
                if (GUI.Button(SLineRect(++line), $"{(BDArmorySettings.SHOW_WAYPOINTS_OPTIONS ? StringUtils.Localize("#LOC_BDArmory_Generic_Hide") : StringUtils.Localize("#LOC_BDArmory_Generic_Show"))} {StringUtils.Localize("#LOC_BDArmory_Settings_WaypointsOptions")}", BDArmorySettings.SHOW_WAYPOINTS_OPTIONS ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))//Show/hide waypoints section
                {
                    BDArmorySettings.SHOW_WAYPOINTS_OPTIONS = !BDArmorySettings.SHOW_WAYPOINTS_OPTIONS;
                }
                if (BDArmorySettings.SHOW_WAYPOINTS_OPTIONS)
                {
                    // Sanitise the waypoint course index in case the course list has changed.
                    BDArmorySettings.WAYPOINT_COURSE_INDEX = Mathf.Clamp(BDArmorySettings.WAYPOINT_COURSE_INDEX, 0, WaypointCourses.CourseLocations.Count - 1);

                    // Select waypoint course
                    string waypointCourseName;
                    /*
                    switch (BDArmorySettings.WAYPOINT_COURSE_INDEX)
                    {
                        default:
                        case 0: waypointCourseName = "Canyon"; break;
                        case 1: waypointCourseName = "Slalom"; break;
                        case 2: waypointCourseName = "Coastal"; break;
                    }
                    */
                    waypointCourseName = WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].name;

                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_WP_ChooseCourse")}: ({waypointCourseName})", leftLabel); //Select COurse
                    BDArmorySettings.WAYPOINT_COURSE_INDEX = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.WAYPOINT_COURSE_INDEX, 0, WaypointCourses.CourseLocations.Count - 1));

                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_WP_Waypoint")} {StringUtils.Localize("#autoLOC_463493")}: {(BDArmorySettings.WAYPOINTS_ALTITUDE >= 0 ? $"({BDArmorySettings.WAYPOINTS_ALTITUDE:F0}m)" : StringUtils.Localize("#LOC_BDArmory_WP_CourseDefaults"))}", leftLabel); //Waypoint Altitude /use Course Settings
                    BDArmorySettings.WAYPOINTS_ALTITUDE = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.WAYPOINTS_ALTITUDE, -50, 1000f), 50);

                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_WP_MaxLaps")}: {BDArmorySettings.WAYPOINT_LOOP_INDEX:F0}", leftLabel); //max Laps
                    BDArmorySettings.WAYPOINT_LOOP_INDEX = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.WAYPOINT_LOOP_INDEX, 1, BDArmorySettings.WAYPOINT_MAX_LAPS));

                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_WP_GuardActivate")}: {(BDArmorySettings.WAYPOINT_GUARD_INDEX < 0 ? "Never" : $"{StringUtils.Localize("#LOC_BDArmory_WP_Waypoint")} {BDArmorySettings.WAYPOINT_GUARD_INDEX.ToString("F0")}")}", leftLabel); //Activate Guard After
                    BDArmorySettings.WAYPOINT_GUARD_INDEX = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.WAYPOINT_GUARD_INDEX, -1, WaypointCourses.highestWaypointIndex));

                    if (BDArmorySettings.WAYPOINTS_VISUALIZE)
                    {
                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_WP_Waypoint")} {StringUtils.Localize("#autoLOC_8200035")}: {(BDArmorySettings.WAYPOINTS_SCALE > 0 ? $"({BDArmorySettings.WAYPOINTS_SCALE:F0}m)" : StringUtils.Localize("#LOC_BDArmory_WP_CourseDefaults"))}", leftLabel); //Waypoint Radius; //use Course Settings
                        BDArmorySettings.WAYPOINTS_SCALE = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.WAYPOINTS_SCALE, 0, 1000f), 50f);

                        if (WaygateCount >= 0)
                        {
                            GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_WP_SelectModel")}: {SelectedModel}", leftLabel); //Waypoint Type
                            if (SelectedGate != (SelectedGate = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), SelectedGate, -1, WaygateCount), 1)))
                            {
                                if (SelectedGate >= 0) SelectedModel = Path.GetFileNameWithoutExtension(gateFiles[(int)Mathf.Clamp(SelectedGate, 0, WaygateCount)]);
                                else SelectedModel = string.Empty;
                            }
                        }
                    }
                    if (BDArmorySetup.Instance.hasWPCourseSpawner)
                    {
                        if (GUI.Button(SLineRect(++line), StringUtils.Localize("#LOC_BDArmory_BDAWaypointBuilder_Title"), BDArmorySetup.showWPBuilderGUI ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))//Show/hide waypoints section
                        {
                            CourseBuilderGUI.Instance.SetVisible(!BDArmorySetup.showWPBuilderGUI);
                            if (!BDArmorySetup.showWPBuilderGUI)
                                BDArmorySetup.SaveConfig();
                        }
                    }
                    BDArmorySettings.WAYPOINTS_ONE_AT_A_TIME = GUI.Toggle(SLeftRect(++line), BDArmorySettings.WAYPOINTS_ONE_AT_A_TIME, StringUtils.Localize("#LOC_BDArmory_Settings_WaypointsOneAtATime"));
                    BDArmorySettings.WAYPOINTS_INFINITE_FUEL_AT_START = GUI.Toggle(SRightRect(line), BDArmorySettings.WAYPOINTS_INFINITE_FUEL_AT_START, StringUtils.Localize("#LOC_BDArmory_Settings_WaypointsInfFuelAtStart"));
                    BDArmorySettings.WAYPOINTS_VISUALIZE = GUI.Toggle(SLeftRect(++line), BDArmorySettings.WAYPOINTS_VISUALIZE, StringUtils.Localize("#LOC_BDArmory_Settings_WaypointsShow"));
                }
            }

            // Custom Spawn Template
            if (BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS == 11)
            {
                if (GUI.Button(SLineRect(++line), $"{(BDArmorySettings.CUSTOM_SPAWN_TEMPLATE_SHOW_OPTIONS ? StringUtils.Localize("#LOC_BDArmory_Generic_Hide") : StringUtils.Localize("#LOC_BDArmory_Generic_Show"))} {StringUtils.Localize("#LOC_BDArmory_Settings_CustomSpawnTemplateOptions")}", BDArmorySettings.CUSTOM_SPAWN_TEMPLATE_SHOW_OPTIONS ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))//Show/hide tournament options
                {
                    BDArmorySettings.CUSTOM_SPAWN_TEMPLATE_SHOW_OPTIONS = !BDArmorySettings.CUSTOM_SPAWN_TEMPLATE_SHOW_OPTIONS;
                }
                if (BDArmorySettings.CUSTOM_SPAWN_TEMPLATE_SHOW_OPTIONS)
                {
                    line += 0.25f;
                    var spawnTemplate = CustomTemplateSpawning.customSpawnConfig;
                    spawnTemplate.name = GUIUtils.TextField(spawnTemplate.name, "Specify a name then save the template.", rect: SQuarterRect(++line, 0, 2)); // Writing in the text field updates the name of the current template.
                    if (GUI.Button(SQuarterRect(line, 2), StringUtils.Localize("#LOC_BDArmory_Generic_Load"), BDArmorySetup.ButtonStyle))
                    {
                        CustomTemplateSpawning.Instance.ShowTemplateSelection(BDArmorySettings.UI_SCALE_ACTUAL * Event.current.mousePosition + BDArmorySetup.WindowRectVesselSpawner.position);
                    }
                    if (GUI.Button(SQuarterRect(line, 3), StringUtils.Localize("#LOC_BDArmory_Generic_Save"), BDArmorySetup.ButtonStyle)) // Save overwrites the current template with the current vessel positions in the LoadedVesselSwitcher.
                    {
                        CustomTemplateSpawning.Instance.SaveTemplate();
                    }
                    if (GUI.Button(SQuarterRect(++line, 2), StringUtils.Localize("#LOC_BDArmory_Generic_New"), BDArmorySetup.ButtonStyle)) // New generates a new template from the current vessels in the LoadedVesselSwitcher.
                    {
                        spawnTemplate = CustomTemplateSpawning.Instance.NewTemplate();
                    }
                    if (GUI.Button(SQuarterRect(line, 3), StringUtils.Localize("#LOC_BDArmory_Settings_CustomSpawnTemplate_SaveCraftToTemplate"), BDArmorySetup.ButtonStyle)) // New generates a new template from the current vessels in the LoadedVesselSwitcher.
                    {
                        CustomTemplateSpawning.Instance.SaveCraftToTemplate();
                    }
                    line += 0.25f;
                    // We then want a table of teams of craft buttons for selecting the craft with kerbal buttons beside them for selecting the kerbals.
                    char teamName = 'A';
                    foreach (var team in spawnTemplate.customVesselSpawnConfigs)
                    {
                        foreach (var member in team)
                        {
                            GUI.Label(ShortLabel(++line, 20), $"{teamName}: ");
                            // if (GUI.Button(SQuarterRect(line, 0, 3, 20), Path.GetFileNameWithoutExtension(member.craftURL), BDArmorySetup.ButtonStyle))
                            if (GUI.Button(SQuarterRect(line, 0, 3, 20), CustomTemplateSpawning.ShipName(member.craftURL), BDArmorySetup.ButtonStyle))
                            {
                                if (Event.current.button == 1)//Right click
                                    CustomTemplateSpawning.Instance.HideVesselSelection(member);
                                else
                                    CustomTemplateSpawning.Instance.ShowVesselSelection(BDArmorySettings.UI_SCALE_ACTUAL * Event.current.mousePosition + BDArmorySetup.WindowRectVesselSpawner.position, member, team);
                            }
                            if (GUI.Button(SQuarterRect(line, 3, 1), string.IsNullOrEmpty(member.kerbalName) ? "random" : member.kerbalName, BDArmorySetup.ButtonStyle))
                            {
                                if (Event.current.button == 1) // Right click
                                    CustomTemplateSpawning.Instance.HideCrewSelection(member);
                                else
                                    CustomTemplateSpawning.Instance.ShowCrewSelection(BDArmorySettings.UI_SCALE_ACTUAL * Event.current.mousePosition + BDArmorySetup.WindowRectVesselSpawner.position, member);
                            }
                        }
                        ++teamName;
                        line += 0.25f;
                    }
                }
            }
            // Tournament options
            {
                if (GUI.Button(SLineRect(++line), $"{(BDArmorySettings.SHOW_TOURNAMENT_OPTIONS ? StringUtils.Localize("#LOC_BDArmory_Generic_Hide") : StringUtils.Localize("#LOC_BDArmory_Generic_Show"))} {StringUtils.Localize("#LOC_BDArmory_Settings_TournamentOptions")}", BDArmorySettings.SHOW_TOURNAMENT_OPTIONS ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))//Show/hide tournament options
                {
                    BDArmorySettings.SHOW_TOURNAMENT_OPTIONS = !BDArmorySettings.SHOW_TOURNAMENT_OPTIONS;
                }
                if (BDArmorySettings.SHOW_TOURNAMENT_OPTIONS)
                {
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_TournamentDelayBetweenHeats")}: ({BDArmorySettings.TOURNAMENT_DELAY_BETWEEN_HEATS}s)", leftLabel); // Delay between heats
                    BDArmorySettings.TOURNAMENT_DELAY_BETWEEN_HEATS = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TOURNAMENT_DELAY_BETWEEN_HEATS, 0f, 15f));

                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_TournamentTimeWarpBetweenRounds")}: ({BDArmorySettings.TOURNAMENT_TIMEWARP_BETWEEN_ROUNDS switch
                    {
                        0 => StringUtils.Localize("#LOC_BDArmory_Generic_Off"),
                        -5 => StringUtils.Localize("#LOC_BDArmory_Settings_TournamentTimeWarpDaylight"),
                        _ => $"{BDArmorySettings.TOURNAMENT_TIMEWARP_BETWEEN_ROUNDS}min"
                    }})", leftLabel); // TimeWarp Between Rounds
                    BDArmorySettings.TOURNAMENT_TIMEWARP_BETWEEN_ROUNDS = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TOURNAMENT_TIMEWARP_BETWEEN_ROUNDS, -5f, 360f), 5);

                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_TournamentStyle")}: ({tournamentStyle})", leftLabel); // Tournament Style
                    if (BDArmorySettings.TOURNAMENT_STYLE != (BDArmorySettings.TOURNAMENT_STYLE = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TOURNAMENT_STYLE, 0f, tournamentStyleMax))))
                    { tournamentStyle = $"{(TournamentStyle)BDArmorySettings.TOURNAMENT_STYLE}"; }

                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_TournamentRoundType")}: ({tournamentRoundType})", leftLabel); // Tournament Round Type
                    if (BDArmorySettings.TOURNAMENT_ROUND_TYPE != (BDArmorySettings.TOURNAMENT_ROUND_TYPE = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TOURNAMENT_ROUND_TYPE, 0f, tournamentRoundTypeMax))))
                    { tournamentRoundType = $"{(TournamentRoundType)BDArmorySettings.TOURNAMENT_ROUND_TYPE}"; }

                    var value = BDArmorySettings.TOURNAMENT_ROUNDS <= 20 ? BDArmorySettings.TOURNAMENT_ROUNDS : BDArmorySettings.TOURNAMENT_ROUNDS <= 100 ? (16 + BDArmorySettings.TOURNAMENT_ROUNDS / 5) : 37;
                    GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_TournamentRounds")}:  ({BDArmorySettings.TOURNAMENT_ROUNDS})", leftLabel); // Rounds
                    value = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), value, 1f, 37f));
                    BDArmorySettings.TOURNAMENT_ROUNDS = value <= 20 ? value : value <= 36 ? (value - 16) * 5 : BDArmorySettings.TOURNAMENT_ROUNDS_CUSTOM;

                    if (BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS == 0) // FFA
                    {
                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_TournamentVesselsPerHeat")}:  ({(BDArmorySettings.TOURNAMENT_VESSELS_PER_HEAT > 0 ? BDArmorySettings.TOURNAMENT_VESSELS_PER_HEAT.ToString() : (BDArmorySettings.TOURNAMENT_VESSELS_PER_HEAT == -1 ? "Auto" : "Inf"))})", leftLabel); // Vessels Per Heat
                        BDArmorySettings.TOURNAMENT_VESSELS_PER_HEAT = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TOURNAMENT_VESSELS_PER_HEAT, -1f, BDArmorySettings.TOURNAMENT_VESSELS_PER_HEAT_MAX));

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_TournamentNPCsPerHeat")}:  ({BDArmorySettings.TOURNAMENT_NPCS_PER_HEAT})", leftLabel); // NPCs Per Heat
                        BDArmorySettings.TOURNAMENT_NPCS_PER_HEAT = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TOURNAMENT_NPCS_PER_HEAT, 0f, BDArmorySettings.TOURNAMENT_NPCS_PER_HEAT_MAX));
                    }
                    else // Teams
                    {
                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_TournamentTeamsPerHeat")}:  ({BDArmorySettings.TOURNAMENT_TEAMS_PER_HEAT})", leftLabel); // Teams Per Heat
                        BDArmorySettings.TOURNAMENT_TEAMS_PER_HEAT = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TOURNAMENT_TEAMS_PER_HEAT, BDArmorySettings.TOURNAMENT_STYLE == 2 ? 1f : 2f, BDArmorySettings.TOURNAMENT_TEAMS_PER_HEAT_MAX));

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_TournamentVesselsPerTeam")}:  ({(BDArmorySettings.TOURNAMENT_VESSELS_PER_TEAM > 0 ? BDArmorySettings.TOURNAMENT_VESSELS_PER_TEAM.ToString() : "auto")})", leftLabel); // Vessels Per Team
                        BDArmorySettings.TOURNAMENT_VESSELS_PER_TEAM = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TOURNAMENT_VESSELS_PER_TEAM, 0f, BDArmorySettings.TOURNAMENT_VESSELS_PER_TEAM_MAX));

                        BDArmorySettings.TOURNAMENT_FULL_TEAMS = GUI.Toggle(SLeftRect(++line), BDArmorySettings.TOURNAMENT_FULL_TEAMS, StringUtils.Localize("#LOC_BDArmory_Settings_TournamentFullTeams"));  // Re-use craft to fill teams
                    }

                    if (BDArmorySettings.TOURNAMENT_STYLE == 2) // Gauntlet settings
                    {
                        GUI.Label(SLeftRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_GauntletOpponentsFilesLocation")} (AutoSpawn{Path.DirectorySeparatorChar}): ", leftLabel); // Gauntlet opponent craft files location
                        BDArmorySettings.VESSEL_SPAWN_GAUNTLET_OPPONENTS_FILES_LOCATION = GUI.TextField(SRightRect(line), BDArmorySettings.VESSEL_SPAWN_GAUNTLET_OPPONENTS_FILES_LOCATION);

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_TournamentOpponentTeamsPerHeat")}:  ({BDArmorySettings.TOURNAMENT_OPPONENT_TEAMS_PER_HEAT})", leftLabel); // Opponent Teams Per Heat
                        BDArmorySettings.TOURNAMENT_OPPONENT_TEAMS_PER_HEAT = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TOURNAMENT_OPPONENT_TEAMS_PER_HEAT, 1f, 8f));

                        GUI.Label(SLeftSliderRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_Settings_TournamentOpponentVesselsPerTeam")}:  ({BDArmorySettings.TOURNAMENT_OPPONENT_VESSELS_PER_TEAM})", leftLabel); // Opponent Vessels Per Team
                        BDArmorySettings.TOURNAMENT_OPPONENT_VESSELS_PER_TEAM = Mathf.RoundToInt(GUI.HorizontalSlider(SRightSliderRect(line), BDArmorySettings.TOURNAMENT_OPPONENT_VESSELS_PER_TEAM, 1f, 8f));
                    }
                    else { BDArmorySettings.TOURNAMENT_TEAMS_PER_HEAT = Math.Max(2, BDArmorySettings.TOURNAMENT_TEAMS_PER_HEAT); }

                    // Tournament status
                    if (BDATournament.Instance.tournamentType == TournamentType.FFA)
                    {
                        GUI.Label(SLineRect(++line), $"ID: {BDATournament.Instance.tournamentID}, {BDATournament.Instance.vesselCount} vessels, {BDATournament.Instance.numberOfRounds} rounds, {BDATournament.Instance.numberOfHeats} heats per round ({BDATournament.Instance.heatsRemaining} remaining).", leftLabel);
                    }
                    else
                    {
                        GUI.Label(SLineRect(++line), $"ID: {BDATournament.Instance.tournamentID}, {BDATournament.Instance.teamCount} teams, {BDATournament.Instance.numberOfRounds} rounds, {BDATournament.Instance.teamsPerHeat} teams per heat, {BDATournament.Instance.numberOfHeats} heats per round,", leftLabel);
                        GUI.Label(SLineRect(++line), $"{BDATournament.Instance.vesselCount} vessels,{(BDATournament.Instance.fullTeams ? "" : " up to")} {(BDATournament.Instance.vesselsPerTeam == 0 ? "auto" : BDATournament.Instance.vesselsPerTeam)} vessels per team per heat, {BDATournament.Instance.heatsRemaining} heats remaining.", leftLabel);
                    }
                    switch (BDATournament.Instance.tournamentStatus)
                    {
                        case TournamentStatus.Running:
                        case TournamentStatus.Waiting:
                            if (GUI.Button(SLeftRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_TournamentStop"), BDArmorySetup.ButtonStyle)) // Stop tournament
                                BDATournament.Instance.StopTournament();
                            GUI.Label(SRightRect(line), $" Status: {BDATournament.Instance.tournamentStatus},  Round {BDATournament.Instance.currentRound},  Heat {BDATournament.Instance.currentHeat}");
                            break;
                        default:
                            if (GUI.Button(SLeftRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_TournamentSetup"), BDArmorySetup.ButtonStyle)) // Setup tournament
                            {
                                ParseAllSpawnFieldsNow();
                                if (BDArmorySettings.WAYPOINTS_MODE)
                                {
                                    float spawnLatitude, spawnLongitude;
                                    List<Waypoint> course;
                                    spawnLatitude = (float)WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].spawnPoint.x;
                                    spawnLongitude = (float)WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].spawnPoint.y;
                                    course = WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].waypoints;
                                    float gridStartAngle = 0;
                                    gridStartAngle = Vector3.Angle(FlightGlobals.currentMainBody.GetWorldSurfacePosition(spawnLatitude, spawnLongitude, 5) - FlightGlobals.currentMainBody.GetWorldSurfacePosition(90, 0, 5),
                                        FlightGlobals.currentMainBody.GetWorldSurfacePosition(spawnLatitude, spawnLongitude, 5) - FlightGlobals.currentMainBody.GetWorldSurfacePosition(course[0].location.x, course[0].location.y, 5));
                                    if (gridStartAngle < 0) gridStartAngle = Mathf.Abs(gridStartAngle) + 180;
                                    BDArmorySettings.VESSEL_SPAWN_REF_HEADING = gridStartAngle;
                                }
                                BDATournament.Instance.SetupTournament(
                                    BDArmorySettings.VESSEL_SPAWN_FILES_LOCATION,
                                    BDArmorySettings.TOURNAMENT_ROUNDS,
                                    BDArmorySettings.TOURNAMENT_VESSELS_PER_HEAT,
                                    BDArmorySettings.TOURNAMENT_NPCS_PER_HEAT,
                                    BDArmorySettings.TOURNAMENT_TEAMS_PER_HEAT,
                                    BDArmorySettings.TOURNAMENT_VESSELS_PER_TEAM,
                                    BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS == 11 ? 1 : BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS,
                                    BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS == 11 ? TournamentStyle.TemplateRNG : (TournamentStyle)BDArmorySettings.TOURNAMENT_STYLE,
                                    (TournamentRoundType)BDArmorySettings.TOURNAMENT_ROUND_TYPE
                                );
                                BDArmorySetup.SaveConfig();
                            }

                            if (BDATournament.Instance.tournamentStatus != TournamentStatus.Completed)
                            {
                                if (GUI.Button(SRightRect(line), StringUtils.Localize("#LOC_BDArmory_Settings_TournamentRun"), BDArmorySetup.ButtonStyle)) // Run tournament
                                {
                                    _vesselsSpawned = false;
                                    SpawnUtils.CancelSpawning(); // Stop any spawning that's currently happening.
                                    BDATournament.Instance.RunTournament();
                                    if (BDArmorySettings.VESSEL_SPAWNER_WINDOW_WIDTH < 480 && BDATournament.Instance.numberOfRounds * BDATournament.Instance.numberOfHeats > 99) // Expand the window a bit to compensate for long tournaments.
                                    {
                                        BDArmorySettings.VESSEL_SPAWNER_WINDOW_WIDTH = 480;
                                    }
                                }
                            }
                            break;
                    }
                }
            }
            ++line;
            if (BDArmorySettings.WAYPOINTS_MODE)
            {
                if (!waypointsRunning)
                {
                    // Note:
                    // - left click is run with waypoint spawn point
                    // - right click is run with current spawn point
                    // - middle click is run current vessel through waypoints
                    if (GUI.Button(SLineRect(++line), "Run waypoints", BDArmorySetup.ButtonStyle))
                    {
                        if (BDArmorySetup.showWPBuilderGUI && !TournamentCoordinator.Instance.IsRunning) //delete loaded gates if builder is closed, but not if WP course is currently running
                        {
                            foreach (var gate in CourseBuilderGUI.Instance.loadedGates)
                            {
                                gate.disabled = true;
                                gate.gameObject.SetActive(false);
                            }
                            CourseBuilderGUI.Instance.loadedGates.Clear();
                        }
                        BDATournament.Instance.StopTournament();
                        if (TournamentCoordinator.Instance.IsRunning) // Stop either case.
                        {
                            TournamentCoordinator.Instance.Stop();
                            TournamentCoordinator.Instance.StopForEach();
                        }

                        float spawnLatitude, spawnLongitude;
                        List<Waypoint> course;
                        spawnLatitude = (float)WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].spawnPoint.x;
                        spawnLongitude = (float)WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].spawnPoint.y;
                        course = WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].waypoints;
                        float gridStartAngle = 0;
                        gridStartAngle = Vector3.Angle(FlightGlobals.currentMainBody.GetWorldSurfacePosition(spawnLatitude, spawnLongitude, 5) - FlightGlobals.currentMainBody.GetWorldSurfacePosition(90, 0, 5),
                            FlightGlobals.currentMainBody.GetWorldSurfacePosition(spawnLatitude, spawnLongitude, 5) - FlightGlobals.currentMainBody.GetWorldSurfacePosition(course[0].location.x, course[0].location.y, 5));
                        if (gridStartAngle < 0) gridStartAngle = Mathf.Abs(gridStartAngle) + 180;
                        SpawnUtils.ResetVesselNamingDeconfliction();
                        if (Event.current.button == 2) // Middle click => Move the current craft to the spawn point and set it running waypoints.
                        {
                            SpawnUtils.ShowSpawnPoint(
                                WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].worldIndex,
                                spawnLatitude,
                                spawnLongitude,
                                BDArmorySettings.VESSEL_SPAWN_ALTITUDE
                            );
                            TournamentCoordinator.Instance.Configure(null, new WaypointFollowingStrategy(course), null);
                            TournamentCoordinator.Instance.Run();
                        }
                        else if (!BDArmorySettings.WAYPOINTS_ONE_AT_A_TIME)
                        {
                            if (BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS == 11)
                            {
                                if (CustomTemplateSpawning.Instance.ConfigureTemplate(true))
                                {
                                    CustomTemplateSpawning.Instance.SpawnCustomTemplate(CustomTemplateSpawning.customSpawnConfig);
                                    TournamentCoordinator.Instance.Configure(null, new WaypointFollowingStrategy(course), null);
                                    TournamentCoordinator.Instance.Run();
                                }
                            }
                            else
                            {
                                TournamentCoordinator.Instance.Configure(new SpawnConfigStrategy(
                                    new CircularSpawnConfig(
                                        new SpawnConfig(
                                            Event.current.button == 1 ? BDArmorySettings.VESSEL_SPAWN_WORLDINDEX : WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].worldIndex, // Right-click => use the VesselSpawnerWindow settings instead of the defaults.
                                            Event.current.button == 1 ? BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x : spawnLatitude,
                                            Event.current.button == 1 ? BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y : spawnLongitude,
                                            BDArmorySettings.VESSEL_SPAWN_ALTITUDE,
                                            killEverythingFirst: true,
                                            assignTeams: BDArmorySettings.VESSEL_SPAWN_REASSIGN_TEAMS,
                                            numberOfTeams: BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS,
                                            teamCounts: null,
                                            teamsSpecific: null,
                                            folder: BDArmorySettings.VESSEL_SPAWN_FILES_LOCATION
                                        ),
                                        BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                                        BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                                        //BDArmorySettings.VESSEL_SPAWN_REF_HEADING) //this needs to be angle from spawnpoint to first gate, relative to North
                                        gridStartAngle)
                                    ),
                                    new WaypointFollowingStrategy(course),
                                    CircularSpawning.Instance
                                );
                                // Run the waypoint competition.
                                TournamentCoordinator.Instance.Run();
                            }
                        }
                        else
                        {
                            var craftFiles = Directory.GetFiles(Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "AutoSpawn", BDArmorySettings.VESSEL_SPAWN_FILES_LOCATION)), "*.craft").ToList();
                            var strategies = craftFiles.Select(craftFile => new SpawnConfigStrategy(
                                new CircularSpawnConfig(
                                    new SpawnConfig(
                                        Event.current.button == 1 ? BDArmorySettings.VESSEL_SPAWN_WORLDINDEX : WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].worldIndex,
                                        Event.current.button == 1 ? BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x : spawnLatitude,
                                        Event.current.button == 1 ? BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y : spawnLongitude,
                                        BDArmorySettings.VESSEL_SPAWN_ALTITUDE,
                                        killEverythingFirst: true,
                                        assignTeams: BDArmorySettings.VESSEL_SPAWN_REASSIGN_TEAMS,
                                        numberOfTeams: 0, // This should always be 0 (FFA) to avoid the logic for spawning teams in one-at-a-time mode.
                                        teamCounts: null,
                                        teamsSpecific: null,
                                        folder: null,
                                        craftFiles: new List<string>() { craftFile }
                                    ),
                                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                                    BDArmorySettings.VESSEL_SPAWN_REF_HEADING
                                ))).ToList();
                            TournamentCoordinator.Instance.RunForEach(strategies,
                                new WaypointFollowingStrategy(course),
                                CircularSpawning.Instance
                            );
                        }
                        waypointsRunning = true;
                    }
                }
                else
                {
                    if (GUI.Button(SLineRect(++line), "Stop waypoints", BDArmorySetup.ButtonStyle))
                    {
                        waypointsRunning = false;
                        BDATournament.Instance.StopTournament();
                        if (TournamentCoordinator.Instance.IsRunning) // Stop either case.
                        {
                            TournamentCoordinator.Instance.Stop();
                            TournamentCoordinator.Instance.StopForEach();
                        }
                    }
                }
            }
            else if (BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS == 11) // Custom Spawn Template
            {
                if (BDACompetitionMode.Instance.competitionIsActive || BDACompetitionMode.Instance.competitionStarting)
                {
                    if (GUI.Button(SLineRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_StopCompetition"), BDArmorySetup.SelectedButtonStyle)) // Stop competition.
                        BDACompetitionMode.Instance.StopCompetition();
                }
                else
                {
                    var spawnAndStartCompetition = GUI.Button(SLeftButtonRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_SpawnAndStartCompetition"), BDArmorySetup.ButtonStyle);
                    var spawnOnly = GUI.Button(SRightButtonRect(line), StringUtils.Localize("#LOC_BDArmory_Settings_SpawnOnly"), BDArmorySetup.ButtonStyle);
                    if (spawnOnly || spawnAndStartCompetition)
                    {
                        // Stop any currently running tournament.
                        BDATournament.Instance.StopTournament();
                        if (TournamentCoordinator.Instance.IsRunning)
                        {
                            TournamentCoordinator.Instance.Stop();
                            TournamentCoordinator.Instance.StopForEach();
                        }
                        // Configure the current custom spawn template.
                        if (CustomTemplateSpawning.Instance.ConfigureTemplate(spawnAndStartCompetition))
                        {
                            // Spawn the craft and maybe start the competition.
                            SpawnUtils.ResetVesselNamingDeconfliction();
                            CustomTemplateSpawning.Instance.SpawnCustomTemplate(CustomTemplateSpawning.customSpawnConfig);
                        }
                    }
                }
            }
            else
            {
                if (GUI.Button(SLineRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_SingleSpawn"), _vesselsSpawned ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
                {
                    BDATournament.Instance.StopTournament();
                    ParseAllSpawnFieldsNow();
                    if (!_vesselsSpawned && !ContinuousSpawning.Instance.vesselsSpawningContinuously && Event.current.button == 0) // Left click
                    {
                        if (BDArmorySettings.VESSEL_SPAWN_CONTINUE_SINGLE_SPAWNING)
                        {
                            CircularSpawning.Instance.SpawnAllVesselsOnceContinuously(
                                BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
                                BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                                BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                                BDArmorySettings.VESSEL_SPAWN_ALTITUDE,
                                BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                                BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                                BDArmorySettings.VESSEL_SPAWN_REF_HEADING,
                                killEverythingFirst: true,
                                assignTeams: BDArmorySettings.VESSEL_SPAWN_REASSIGN_TEAMS,
                                numberOfTeams: BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS,
                                teamCounts: null,
                                teamsSpecific: null,
                                spawnFolder: BDArmorySettings.VESSEL_SPAWN_FILES_LOCATION
                            ); // Spawn vessels.
                        }
                        else
                        {
                            SpawnUtils.ResetVesselNamingDeconfliction();
                            CircularSpawning.Instance.SpawnAllVesselsOnce(
                                BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
                                BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                                BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                                BDArmorySettings.VESSEL_SPAWN_ALTITUDE_,
                                BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                                BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                                BDArmorySettings.VESSEL_SPAWN_REF_HEADING,
                                killEverythingFirst: true,
                                assignTeams: BDArmorySettings.VESSEL_SPAWN_REASSIGN_TEAMS,
                                numberOfTeams: BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS,
                                teamCounts: null,
                                teamsSpecific: null,
                                spawnFolder: BDArmorySettings.VESSEL_SPAWN_FILES_LOCATION
                            ); // Spawn vessels.
                            if (BDArmorySettings.VESSEL_SPAWN_START_COMPETITION_AUTOMATICALLY)
                                StartCoroutine(StartCompetitionOnceSpawned());
                        }
                        _vesselsSpawned = true;
                    }
                    else if (Event.current.button == 2) // Middle click, add a new spawn of vessels to the currently spawned vessels.
                    {
                        CircularSpawning.Instance.SpawnAllVesselsOnce(
                            BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
                            BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                            BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                            BDArmorySettings.VESSEL_SPAWN_ALTITUDE,
                            BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                            BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                            BDArmorySettings.VESSEL_SPAWN_REF_HEADING,
                            killEverythingFirst: false,
                            assignTeams: false,
                            numberOfTeams: 0,
                            teamCounts: null,
                            teamsSpecific: null,
                            spawnFolder: BDArmorySettings.VESSEL_SPAWN_FILES_LOCATION
                        ); // Spawn vessels, without killing off other vessels or changing camera positions.
                    }
                }
                if (GUI.Button(SLineRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_ContinuousSpawning"), ContinuousSpawning.Instance.vesselsSpawningContinuously ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
                {
                    BDATournament.Instance.StopTournament();
                    ParseAllSpawnFieldsNow();
                    if (!ContinuousSpawning.Instance.vesselsSpawningContinuously && !_vesselsSpawned && Event.current.button == 0) // Left click
                    {
                        ContinuousSpawning.Instance.SpawnVesselsContinuously(
                            new CircularSpawnConfig(
                                new SpawnConfig(
                                    BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
                                    BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                                    BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                                    BDArmorySettings.VESSEL_SPAWN_ALTITUDE_,
                                    killEverythingFirst: true,
                                    assignTeams: BDArmorySettings.VESSEL_SPAWN_REASSIGN_TEAMS,
                                    numberOfTeams: 1,
                                    teamCounts: null,
                                    teamsSpecific: null,
                                    folder: BDArmorySettings.VESSEL_SPAWN_FILES_LOCATION
                                ),
                                BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                                BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                                BDArmorySettings.VESSEL_SPAWN_REF_HEADING
                            )
                            ); // Spawn vessels continuously at 1km above terrain.
                    }
                }
                if (GUI.Button(SLineRect(++line), StringUtils.Localize("#LOC_BDArmory_Settings_CancelSpawning"), (_vesselsSpawned || ContinuousSpawning.Instance.vesselsSpawningContinuously) ? BDArmorySetup.ButtonStyle : BDArmorySetup.SelectedButtonStyle))
                {
                    if (_vesselsSpawned)
                        Debug.Log("[BDArmory.VesselSpawnerWindow]: Resetting spawning vessel button.");
                    _vesselsSpawned = false;
                    if (ContinuousSpawning.Instance.vesselsSpawningContinuously)
                        Debug.Log("[BDArmory.VesselSpawnerWindow]: Resetting continuous spawning button.");
                    BDATournament.Instance.StopTournament();
                    SpawnUtils.CancelSpawning();
                }
            }
            // #if DEBUG
            //             if (BDArmorySettings.DEBUG_SPAWNING && GUI.Button(SLineRect(++line), "Test point spawn", BDArmorySetup.ButtonStyle))
            //             {
            //                 StartCoroutine(SingleVesselSpawning.Instance.Spawn(
            //                     new CircularSpawnConfig(
            //                         new SpawnConfig(
            //                             BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
            //                             BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
            //                             BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
            //                             BDArmorySettings.VESSEL_SPAWN_ALTITUDE,
            //                             false,
            //                             false,
            //                             0,
            //                             null,
            //                             null,
            //                             BDArmorySettings.VESSEL_SPAWN_FILES_LOCATION
            //                         ),
            //                         BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
            //                         BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE
            //                     )
            //                 ));
            //             }
            // #endif

            line += 1.25f; // Bottom internal margin
            _windowHeight = (line * _lineHeight);
            GUIUtils.DragWindow();
        }

        IEnumerator StartCompetitionOnceSpawned()
        {
            yield return new WaitWhile(() => VesselSpawnerStatus.vesselsSpawning);
            if (!VesselSpawnerStatus.vesselSpawnSuccess) yield break;
            if (BDArmorySettings.RUNWAY_PROJECT)
            {
                switch (BDArmorySettings.RUNWAY_PROJECT_ROUND)
                {
                    case 33:
                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                        yield break;
                    case 44:
                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                        yield break;
                    case 53:
                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                        yield break;
                    case 67:
                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                        yield break;
                    case 77:
                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                        yield break;
                }
            }
            BDACompetitionMode.Instance.StartCompetitionMode(BDArmorySettings.COMPETITION_DISTANCE, BDArmorySettings.COMPETITION_START_DESPITE_FAILURES);
        }

        public void SetVisible(bool visible)
        {
            BDArmorySetup.showVesselSpawnerGUI = visible;
            GUIUtils.SetGUIRectVisible(_guiCheckIndex, visible);
            if (!visible) ParseAllSpawnFieldsNow();
        }

        #region Observers
        static int _observerGUICheckIndex = -1;
        bool showObserverWindow = false;
        bool bringObserverWindowToFront = false;
        bool potentialObserversNeedsRefreshing = false;
        Rect observerWindowRect = new Rect(0, 0, 300, 250);
        Vector2 observerSelectionScrollPos = default;
        List<Vessel> potentialObservers = new List<Vessel>();
        public HashSet<Vessel> Observers = new HashSet<Vessel>();
        void ShowObserverWindow(bool show, Vector2 position = default)
        {
            if (show)
            {
                observerWindowRect.position = position + new Vector2(50, -BDArmorySettings.UI_SCALE_ACTUAL * observerWindowRect.height / 2); // Centred and slightly offset to allow clicking the same spot.
                RefreshObservers();
                bringObserverWindowToFront = true;
            }
            else
            {
                potentialObservers.Clear();
                if (BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS == 11) // Custom Spawn Template
                    CustomTemplateSpawning.Instance.RefreshObserverCrewMembers();
            }
            showObserverWindow = show;
            GUIUtils.SetGUIRectVisible(_observerGUICheckIndex, show);
        }
        void ObserverWindow(int windowID)
        {
            GUILayout.BeginVertical();
            observerSelectionScrollPos = GUILayout.BeginScrollView(observerSelectionScrollPos, GUI.skin.box, GUILayout.Width(observerWindowRect.width - 15), GUILayout.MaxHeight(observerWindowRect.height - 20));
            int count = 0;
            using (var potentialObserver = potentialObservers.GetEnumerator())
                while (potentialObserver.MoveNext())
                {
                    if (potentialObserver.Current == null) { potentialObserversNeedsRefreshing = true; continue; }
                    bool isSelected = Observers.Contains(potentialObserver.Current);
                    if (isSelected) ++count;
                    if (GUILayout.Button(potentialObserver.Current.vesselName, isSelected ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle, GUILayout.Height(30)))
                    {
                        if (isSelected) Observers.Remove(potentialObserver.Current);
                        else Observers.Add(potentialObserver.Current);
                    }
                }
            GUILayout.EndScrollView();
            if (count == potentialObservers.Count)
            {
                if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_ObserverSelection_SelectNone"), BDArmorySetup.SelectedButtonStyle, GUILayout.Height(30)))
                    Observers.Clear();
            }
            else
            {
                if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_ObserverSelection_SelectAll"), BDArmorySetup.ButtonStyle, GUILayout.Height(30)))
                    Observers = potentialObservers.Where(o => o != null).ToHashSet();
            }
            GUILayout.EndVertical();
            GUIUtils.DragWindow();
            GUIUtils.RepositionWindow(ref observerWindowRect);
            GUIUtils.UpdateGUIRect(observerWindowRect, _observerGUICheckIndex);
            GUIUtils.UseMouseEventInRect(observerWindowRect);
            if (bringObserverWindowToFront)
            {
                bringObserverWindowToFront = false;
                GUI.BringWindowToFront(windowID);
            }
        }
        void RefreshObservers()
        {
            potentialObservers.Clear();
            foreach (var vessel in FlightGlobals.Vessels)
            {
                if (vessel == null) continue;
                if (vessel.vesselType == VesselType.Debris || vessel.vesselType == VesselType.SpaceObject) continue; // Ignore debris and space objects.
                if (vessel.ActiveController().AI != null // Check for an AI.
                    && vessel.ActiveController().WM != null // Check for a WM.
                    && vessel.IsControllable
                ) continue; // It's an active vessel, skip it.
                potentialObservers.Add(vessel);
            }
            Observers = Observers.Where(o => potentialObservers.Contains(o)).ToHashSet();
        }
        #endregion
    }
}