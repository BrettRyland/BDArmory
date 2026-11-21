using BDArmory.Competition;
using BDArmory.Competition.OrchestrationStrategies;
using BDArmory.Extensions;
using BDArmory.GameModes.Waypoints;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.VesselSpawning;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CourseBuilderGUI : MonoBehaviour
    {
        #region Fields
        public static CourseBuilderGUI Instance;
        private static int _guiCheckIndex = -1;
        private static readonly float _buttonSize = 20;
        private static readonly float _margin = 5;
        private static readonly float _lineHeight = _buttonSize;
        private float _windowHeight; //auto adjusting
        private float _windowWidth;
        public bool _ready = false;
        Dictionary<string, NumericInputField> spawnFields;

        int selected_index = -1;
        int selected_gate_index = -1;
        public float SelectedGate = 0;
        public static string Gatepath;
        public string SelectedModel;
        double movementIncrement = 1;
        float recordingIncrement = 10;

        private bool ShowLoadMenu = false;
        private bool ShowNewCourse = false;
        private string newCourseName = "";
        private bool recording = false;
        private bool showCourseWPsComboBox = false;
        private bool showPositioningControls = false;
        private bool showCoursePath = false;
        bool moddingSpawnPoint = false;
        public List<WayPointMarker> loadedGates;
        #endregion

        #region Styles
        /// <summary>
        /// Need Left 1/3 width button
        /// Need Right 2/3 textfield
        /// Needleft/mid/right numfied boxes
        /// need Left <</>> Mid <</>> right <</>> buttons to flank the L/M/R numfields
        /// Need 1/2 width buttons
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>

        Rect SLineRect(float line)
        {
            return new Rect(_margin, line * _lineHeight, _windowWidth - 2 * _margin, _lineHeight);
        }

        Rect SLeftButtonRect(float line)
        {
            return new Rect(_margin, line * _lineHeight, (_windowWidth - 2 * _margin) / 2 - _margin / 4, _lineHeight);
        }

        Rect SRightButtonRect(float line)
        {
            return new Rect(_windowWidth / 2 + _margin / 4, line * _lineHeight, (_windowWidth - 2 * _margin) / 2 - _margin / 4, _lineHeight);
        }

        Rect SQuarterRect(float line, int pos, int span = 1, float indent = 0)
        {
            return new Rect(_margin + (pos % 4) * (_windowWidth - 2f * _margin) / 4f + indent, (line + (int)(pos / 4)) * _lineHeight, span * (_windowWidth - 2f * _margin) / 4f - indent, _lineHeight);
        }

        Rect SFieldButtonRect(float line, float offset)
        {
            var rectGap = _windowWidth / 24;
            return new Rect(rectGap * offset, line * _lineHeight, rectGap, _lineHeight);
        }

        List<Rect> SRight3Rects(float line)
        {
            var rectGap = _windowWidth / 24;
            var rectWidth = _windowWidth / 6;
            var rects = new List<Rect>();
            rects.Add(new Rect(rectGap * 2.5f, line * _lineHeight, rectWidth, _lineHeight));
            rects.Add(new Rect(rectGap * 10, line * _lineHeight, rectWidth, _lineHeight));
            rects.Add(new Rect(rectGap * 17.5f, line * _lineHeight, rectWidth, _lineHeight));
            return rects;
        }

        GUIStyle leftLabel;
        GUIStyle listStyle;
        GUIStyle centreLabel;
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
            centreLabel = new GUIStyle
            {
                alignment = TextAnchor.UpperCenter,
                normal = new GUIStyleState { textColor = Color.white }
            };

            // Spawn fields
            spawnFields = new Dictionary<string, NumericInputField> {
                { "lat", gameObject.AddComponent<NumericInputField>().Initialise(0, FlightGlobals.currentMainBody.GetLatitudeAndLongitude(FlightGlobals.ActiveVessel.CoM).x, -90, 90) },
                { "lon", gameObject.AddComponent<NumericInputField>().Initialise(0, FlightGlobals.currentMainBody.GetLatitudeAndLongitude(FlightGlobals.ActiveVessel.CoM).y, -180, 180) },
                { "alt", gameObject.AddComponent<NumericInputField>().Initialise(0, BDArmorySettings.VESSEL_SPAWN_ALTITUDE) },
                { "increment", gameObject.AddComponent<NumericInputField>().Initialise(0.001f, movementIncrement) },
                { "diameter", gameObject.AddComponent<NumericInputField>().Initialise(0, 500) },
                { "interval", gameObject.AddComponent<NumericInputField>().Initialise(0, recordingIncrement) },
                { "speed", gameObject.AddComponent<NumericInputField>().Initialise(0, -1) },
            };
            loadedGates = new List<WayPointMarker>();
        }

        private IEnumerator WaitForBdaSettings()
        {
            yield return new WaitUntil(() => BDArmorySetup.Instance is not null);

            BDArmorySetup.Instance.hasWPCourseSpawner = true;
            if (_guiCheckIndex < 0) _guiCheckIndex = GUIUtils.RegisterGUIRect(new Rect());
            _ready = true;
            SetVisible(BDArmorySetup.showWPBuilderGUI);
        }
        float startTime = 0;

        IEnumerator RecordCourse()
        {
            startTime = Time.time;
            while (recording)
            {
                while (Time.time - recordingIncrement < startTime)
                {
                    yield return null;
                }
                if (!recording) yield break;
                string newName = "Waypoint " + WaypointCourses.CourseLocations[selected_index].waypoints.Count.ToString();
                AddGate(newName);
                startTime = Time.time;
            }
        }

        void SnapCameraToGate(bool revert = false)
        {
            if (!revert)
            {
                if (selected_gate_index < 0 || selected_index < 0 || selected_index >= WaypointCourses.CourseLocations.Count || selected_gate_index >= WaypointCourses.CourseLocations[selected_index].waypoints.Count) return;
            }
            var flightCamera = FlightCamera.fetch;
            var cameraHeading = FlightCamera.CamHdg;
            var cameraPitch = FlightCamera.CamPitch;
            var distance = 1000;

            double terrainAltitude;
            Vector3d spawnPoint;
            if (!revert)
            {
                Waypoint gate = WaypointCourses.CourseLocations[selected_index].waypoints[selected_gate_index];
                terrainAltitude = FlightGlobals.currentMainBody.TerrainAltitude(gate.location.x, gate.location.y);
                spawnPoint = FlightGlobals.currentMainBody.BDGetWorldSurfacePosition(gate.location.x, gate.location.y, terrainAltitude + gate.location.z);
            }
            else
            {
                terrainAltitude = FlightGlobals.ActiveVessel.radarAltitude;
                spawnPoint = FlightGlobals.currentMainBody.BDGetWorldSurfacePosition(FlightGlobals.ActiveVessel.CoM.x, FlightGlobals.ActiveVessel.CoM.y, terrainAltitude + FlightGlobals.ActiveVessel.CoM.z);
            }
            Vector3 origin;
            if (moddingSpawnPoint)
            {
                float terrainAlt = (float)FlightGlobals.currentMainBody.TerrainAltitude(WaypointCourses.CourseLocations[selected_index].spawnPoint.x, WaypointCourses.CourseLocations[selected_index].spawnPoint.y);
                Vector3d SpawnCoords = new Vector3((float)WaypointCourses.CourseLocations[selected_index].spawnPoint.x, (float)WaypointCourses.CourseLocations[selected_index].spawnPoint.y, BDArmorySettings.VESSEL_SPAWN_ALTITUDE + terrainAlt);
                origin = SpawnCoords;
            }
            else
            {
                if (!revert)
                    origin = loadedGates[selected_gate_index].transform.position;
                else origin = FlightGlobals.ActiveVessel.CoM;
            }
            FloatingOrigin.SetOffset(origin);// This adjusts local coordinates, such that gate is (0,0,0).
            flightCamera.transform.parent.position = Vector3.zero;
            if (!moddingSpawnPoint) flightCamera.SetTarget(revert ? FlightGlobals.ActiveVessel.ReferenceTransform : loadedGates[selected_gate_index].transform);
            var radialUnitVector = -FlightGlobals.currentMainBody.transform.position.normalized;
            var cameraPosition = Vector3.RotateTowards(distance * radialUnitVector, Quaternion.AngleAxis(cameraHeading * Mathf.Rad2Deg, radialUnitVector) * -VectorUtils.GetNorthVector(spawnPoint, FlightGlobals.currentMainBody), 70f * Mathf.Deg2Rad, 0);
            flightCamera.transform.localPosition = cameraPosition;
            flightCamera.transform.localRotation = Quaternion.identity;
            flightCamera.ActivateUpdate();

            flightCamera.SetDistanceImmediate(distance);
            FlightCamera.CamHdg = cameraHeading;
            FlightCamera.CamPitch = cameraPitch;
        }

        private void OnGUI()
        {
            if (!(_ready && BDArmorySetup.GAME_UI_ENABLED && BDArmorySetup.showWPBuilderGUI && HighLogic.LoadedSceneIsFlight))
                return;

            _windowWidth = BDArmorySettings.VESSEL_WAYPOINT_WINDOW_WIDTH;
            SetNewHeight(_windowHeight);
            BDArmorySetup.WindowRectWayPointSpawner = new Rect(
                BDArmorySetup.WindowRectWayPointSpawner.x,
                BDArmorySetup.WindowRectWayPointSpawner.y,
                _windowWidth,
                _windowHeight
            );
            BDArmorySetup.SetGUIOpacity();
            var guiMatrix = GUI.matrix;
            if (BDArmorySettings.UI_SCALE_ACTUAL != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, BDArmorySetup.WindowRectWayPointSpawner.position);
            BDArmorySetup.WindowRectWayPointSpawner = GUI.Window(
                GUIUtility.GetControlID(FocusType.Passive),
                BDArmorySetup.WindowRectWayPointSpawner,
                WindowWaypointSpawner,
                StringUtils.Localize("#LOC_BDArmory_BDAWaypointBuilder_Title"),//"BDA Vessel Spawner"
                BDArmorySetup.BDGuiSkin.window
            );
            BDArmorySetup.SetGUIOpacity(false);
            GUIUtils.UpdateGUIRect(BDArmorySetup.WindowRectWayPointSpawner, _guiCheckIndex);
            if (showCourseWPsComboBox)
            {
                //draw spawnpoint
                float terrainAlt = (float)FlightGlobals.currentMainBody.TerrainAltitude(WaypointCourses.CourseLocations[selected_index].spawnPoint.x, WaypointCourses.CourseLocations[selected_index].spawnPoint.y);
                Vector3d SpawnCoords = new Vector3((float)WaypointCourses.CourseLocations[selected_index].spawnPoint.x, (float)WaypointCourses.CourseLocations[selected_index].spawnPoint.y, BDArmorySettings.VESSEL_SPAWN_ALTITUDE + terrainAlt);
                GUIUtils.DrawTextureOnWorldPos(VectorUtils.GetWorldSurfacePosition(SpawnCoords, FlightGlobals.currentMainBody), BDArmorySetup.Instance.greenPointCircleTexture, new Vector2(96, 96), 0);
                if (selected_index >= 0 && selected_gate_index >= 0)
                {
                    terrainAlt = (float)FlightGlobals.currentMainBody.TerrainAltitude(WaypointCourses.CourseLocations[selected_index].waypoints[selected_gate_index].location.x, WaypointCourses.CourseLocations[selected_index].waypoints[selected_gate_index].location.y);
                    terrainAlt += (WaypointCourses.CourseLocations[selected_index].waypoints[selected_gate_index].scale * 1.1f);
                    SpawnCoords = new Vector3((float)WaypointCourses.CourseLocations[selected_index].waypoints[selected_gate_index].location.x, (float)WaypointCourses.CourseLocations[selected_index].waypoints[selected_gate_index].location.y, (BDArmorySettings.WAYPOINTS_ALTITUDE < 0 ? WaypointCourses.CourseLocations[selected_index].waypoints[selected_gate_index].location.z : BDArmorySettings.WAYPOINTS_ALTITUDE) + terrainAlt);
                    GUIUtils.DrawTextureOnWorldPos(VectorUtils.GetWorldSurfacePosition(SpawnCoords, FlightGlobals.currentMainBody), BDArmorySetup.Instance.greenDiamondTexture, new Vector2(36, 36), 0);
                }
            }
            if (selected_index >= 0 && showCoursePath && HighLogic.LoadedSceneIsFlight && BDArmorySetup.GAME_UI_ENABLED && !MapView.MapIsEnabled)
            {
                for (int gate = 0; gate < loadedGates.Count; gate++)
                {
                    GUIUtils.DrawLineBetweenWorldPositions(loadedGates[gate].Position, loadedGates[Math.Max(gate - 1, 0)].Position, 4, Color.red);
                }
            }
        }

        private void SetNewHeight(float windowHeight)
        {
            var previousWindowHeight = BDArmorySetup.WindowRectWayPointSpawner.height;
            BDArmorySetup.WindowRectWayPointSpawner.height = windowHeight;
            GUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectWayPointSpawner, previousWindowHeight);
        }

        private void WindowWaypointSpawner(int id)
        {
            if (GUI.Button(new Rect(_windowWidth - _buttonSize - (_margin - 2), _margin, _buttonSize - 2, _buttonSize - 2), " X", BDArmorySetup.CloseButtonStyle))
            {
                SetVisible(false);
                BDArmorySetup.SaveConfig();
            }

            float line = 0.25f;
            var rects = new List<Rect>();

            if (GUI.Button(SLeftButtonRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_WP_LoadCourse")}", ShowLoadMenu ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))//Load Course
            {
                ShowLoadMenu = !ShowLoadMenu;
            }

            if (GUI.Button(SRightButtonRect(line), $"{StringUtils.Localize("#LOC_BDArmory_WP_NewCourse")}", ShowNewCourse ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))//Load Course
            {
                ShowNewCourse = !ShowNewCourse;
            }
            if (ShowLoadMenu)
            {
                line++;
                int i = 0;
                foreach (var wpCourse in WaypointCourses.CourseLocations)
                {
                    if (GUI.Button(SQuarterRect(line, i++), wpCourse.name, i - 1 == selected_index ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
                    {
                        switch (Event.current.button)
                        {
                            case 1: // right click
                                if (selected_index == i - 1) //deleting currently loaded course
                                {
                                    foreach (var gate in loadedGates)
                                    {
                                        gate.disabled = true;
                                        gate.gameObject.SetActive(false);
                                    }
                                    selected_gate_index = -1;
                                    showCourseWPsComboBox = false;
                                    moddingSpawnPoint = false;
                                }
                                WaypointCourses.CourseLocations.Remove(wpCourse);
                                //WaypointField.Save();
                                if (selected_index >= WaypointCourses.CourseLocations.Count) selected_index = WaypointCourses.CourseLocations.Count - 1;
                                break;
                            default:
                                //if loading an off-world course, warp to world
                                //if (wpCourse.worldIndex != FlightGlobals.GetBodyIndex(FlightGlobals.currentMainBody))
                                SpawnUtils.ShowSpawnPoint(wpCourse.worldIndex, wpCourse.spawnPoint.x, wpCourse.spawnPoint.y, 500);
                                Vector3 previousLocation = FlightGlobals.ActiveVessel.transform.position;
                                //clear any previously loaded gates
                                foreach (var gate in loadedGates)
                                {
                                    gate.disabled = true;
                                    gate.gameObject.SetActive(false);
                                }
                                loadedGates.Clear();
                                ShowLoadMenu = false;
                                selected_index = i - 1;
                                selected_gate_index = -1;
                                showCourseWPsComboBox = true;
                                moddingSpawnPoint = false;
                                //spawn in course gates
                                Debug.Log($"Loading Course; selected index: {selected_index}, ({WaypointCourses.CourseLocations[selected_index].name}) starting gate spawn");
                                for (int wp = 0; wp < wpCourse.waypoints.Count; wp++)
                                {
                                    if (!string.IsNullOrEmpty(wpCourse.waypoints[wp].model))
                                        SelectedModel = wpCourse.waypoints[wp].model;
                                    else SelectedModel = "Ring";
                                    float terrainAltitude = (float)FlightGlobals.currentMainBody.TerrainAltitude(wpCourse.waypoints[wp].location.x, wpCourse.waypoints[wp].location.y);
                                    Vector3d WorldCoords = VectorUtils.GetWorldSurfacePosition(new Vector3(wpCourse.waypoints[wp].location.x, wpCourse.waypoints[wp].location.y, (BDArmorySettings.WAYPOINTS_ALTITUDE < 0 ? wpCourse.waypoints[wp].location.z : BDArmorySettings.WAYPOINTS_ALTITUDE) + terrainAltitude), FlightGlobals.currentMainBody);
                                    var direction = (WorldCoords - previousLocation).normalized;
                                    WayPointMarker.CreateWaypoint(WorldCoords, direction, SelectedModel, wpCourse.waypoints[wp].scale);

                                    previousLocation = WorldCoords;
                                    var location = string.Format("({0:##.###}, {1:##.###}, {2:####}", wpCourse.waypoints[wp].location.x, wpCourse.waypoints[wp].location.y, wpCourse.waypoints[wp].location.z);
                                    Debug.Log("[BDArmory.Waypoints]: Creating waypoint marker at  " + " " + location + " World: " + FlightGlobals.currentMainBody.flightGlobalsIndex + " scale: " + wpCourse.waypoints[wp].scale + " model:" + wpCourse.waypoints[wp].model);
                                }
                                break;
                        }
                    }
                }
                line += (int)((i - 1) / 4);
                line += 0.3f;
            }

            if (ShowNewCourse)
            {
                newCourseName = GUI.TextField(SLeftButtonRect(++line), newCourseName);
                if (!string.IsNullOrEmpty(newCourseName))
                {
                    if (GUI.Button(SQuarterRect(line, 2), StringUtils.Localize("#LOC_BDArmory_WP_Create"), BDArmorySetup.ButtonStyle))
                    {
                        Vector3d spawnCoords = Vector3d.zero;
                        FlightGlobals.currentMainBody.GetLatLonAlt(FlightGlobals.ActiveVessel.CoM, out spawnCoords.x, out spawnCoords.y, out spawnCoords.z);

                        if (!WaypointCourses.CourseLocations.Select(l => l.name).ToList().Contains(newCourseName))
                            WaypointCourses.CourseLocations.Add(new WaypointCourse(newCourseName, FlightGlobals.GetBodyIndex(FlightGlobals.currentMainBody), new Vector2d(spawnCoords.x, spawnCoords.y), new List<Waypoint>()));
                        //WaypointField.Save();
                        selected_index = WaypointCourses.CourseLocations.FindIndex(l => l.name == newCourseName);
                        newCourseName = "";
                        ShowNewCourse = false;
                        showCourseWPsComboBox = true;
                        //clear any previously loaded gates
                        foreach (var gate in loadedGates)
                        {
                            gate.disabled = true;
                            gate.gameObject.SetActive(false);
                        }
                        loadedGates.Clear();
                    }
                    if (GUI.Button(SQuarterRect(line, 3), StringUtils.Localize("#LOC_BDArmory_WP_Record"), BDArmorySetup.ButtonStyle))
                    {
                        Vector3d spawnCoords = Vector3d.zero;
                        FlightGlobals.currentMainBody.GetLatLonAlt(FlightGlobals.ActiveVessel.CoM, out spawnCoords.x, out spawnCoords.y, out spawnCoords.z);

                        if (!WaypointCourses.CourseLocations.Select(l => l.name).ToList().Contains(newCourseName))
                            WaypointCourses.CourseLocations.Add(new WaypointCourse(newCourseName, FlightGlobals.GetBodyIndex(FlightGlobals.currentMainBody), new Vector2d(spawnCoords.x, spawnCoords.y), new List<Waypoint>()));
                        //WaypointField.Save();
                        selected_index = WaypointCourses.CourseLocations.FindIndex(l => l.name == newCourseName);
                        newCourseName = "";
                        ShowNewCourse = false;
                        showCourseWPsComboBox = true;
                        recording = true;
                        selected_gate_index = -1;
                        //clear any previously loaded gates
                        foreach (var gate in loadedGates)
                        {
                            gate.disabled = true;
                            gate.gameObject.SetActive(false);
                        }
                        loadedGates.Clear();
                        StartCoroutine(RecordCourse());
                    }
                    GUI.Label(SQuarterRect(++line, 2), StringUtils.Localize("#LOC_BDArmory_WP_TimeStep"), leftLabel);
                    spawnFields["interval"].TryParseValue(GUI.TextField(SQuarterRect(line, 3), spawnFields["interval"].possibleValue, 8, spawnFields["interval"].style));
                    if (spawnFields["interval"].CurrentValue != recordingIncrement) recordingIncrement = (float)spawnFields["interval"].CurrentValue;
                }
            }
            if (showCourseWPsComboBox)
            {
                if (WaypointCourses.CourseLocations[selected_index].worldIndex != FlightGlobals.GetBodyIndex(FlightGlobals.currentMainBody)) return;
                line += 1.25f;
                GUI.Label(SLineRect(line++), WaypointCourses.CourseLocations[selected_index].name, centreLabel);
                line += 0.25f;
                if (recording)
                {
                    GUI.Label(SLineRect(line++), StringUtils.Localize("#LOC_BDArmory_WP_Recording"), centreLabel);
                    line += 0.25f;
                    GUI.Label(SQuarterRect(++line, 1), StringUtils.Localize("#LOC_BDArmory_WP_TimeStep"), leftLabel);
                    spawnFields["interval"].TryParseValue(GUI.TextField(SQuarterRect(line, 2), spawnFields["interval"].possibleValue, 8, spawnFields["interval"].style));
                    if (spawnFields["interval"].CurrentValue != recordingIncrement) recordingIncrement = (float)spawnFields["interval"].CurrentValue;

                    line += 0.25f;
                    if (GUI.Button(SLineRect(++line), StringUtils.Localize("#LOC_BDArmory_WP_FinishRecording"), BDArmorySetup.ButtonStyle))
                    {
                        recording = false;
                    }
                    line++;
                }
                if (GUI.Button(SQuarterRect(line, 0), StringUtils.Localize("#LOC_BDArmory_WP_Spawnpoint"), moddingSpawnPoint ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
                {
                    if (Event.current.button == 0)
                    {
                        //SnapCameraToGate(); // Don't snap every frame when the spawn point button is selected... unless that's intended behaviour?
                    }
                    //show gate position buttons/location textboxes
                    spawnFields["lat"].SetCurrentValue(WaypointCourses.CourseLocations[selected_index].spawnPoint.x);
                    spawnFields["lon"].SetCurrentValue(WaypointCourses.CourseLocations[selected_index].spawnPoint.y);
                    spawnFields["alt"].SetCurrentValue(BDArmorySettings.VESSEL_SPAWN_ALTITUDE);
                    showPositioningControls = true;
                    moddingSpawnPoint = true;
                    selected_gate_index = -1;
                }
                int i = 1;
                foreach (var gate in WaypointCourses.CourseLocations[selected_index].waypoints)
                {
                    if (GUI.Button(SQuarterRect(line, i++), gate.name, Math.Max(i - 2, 0) == selected_gate_index ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle))
                    {
                        moddingSpawnPoint = false;
                        selected_gate_index = Math.Max(i - 2, 0);
                        Debug.Log($"selected gate index: {selected_gate_index}, ({WaypointCourses.CourseLocations[selected_index].waypoints[selected_gate_index].name})");
                        moddingSpawnPoint = false;
                        switch (Event.current.button)
                        {
                            case 1: // right click, remove gate from course
                                SnapCameraToGate(true);
                                loadedGates[selected_gate_index].disabled = true;
                                loadedGates[selected_gate_index].gameObject.SetActive(false);
                                WaypointCourses.CourseLocations[selected_index].waypoints.Remove(gate);

                                if (selected_gate_index >= WaypointCourses.CourseLocations[selected_index].waypoints.Count)
                                {
                                    if (WaypointCourses.CourseLocations[selected_index].waypoints.Count > 0)
                                    {
                                        selected_gate_index = WaypointCourses.CourseLocations[selected_index].waypoints.Count - 1;
                                        spawnFields["lat"].SetCurrentValue(WaypointCourses.CourseLocations[selected_index].waypoints[selected_gate_index].location.x);
                                        spawnFields["lon"].SetCurrentValue(WaypointCourses.CourseLocations[selected_index].waypoints[selected_gate_index].location.y);
                                        spawnFields["alt"].SetCurrentValue(WaypointCourses.CourseLocations[selected_index].waypoints[selected_gate_index].location.z);
                                        spawnFields["diameter"].SetCurrentValue(WaypointCourses.CourseLocations[selected_index].waypoints[selected_gate_index].scale);
                                        spawnFields["speed"].SetCurrentValue(WaypointCourses.CourseLocations[selected_index].waypoints[selected_gate_index].maxSpeed);
                                        txtName = WaypointCourses.CourseLocations[selected_index].waypoints[selected_gate_index].name;
                                    }
                                    else selected_gate_index = -1;
                                }
                                //WaypointField.Save();
                                break;
                            default:
                                //snap camera to selected gate
                                //SnapCameraToGate();
                                //show gate position buttons/location textboxes
                                spawnFields["lat"].SetCurrentValue(gate.location.x);
                                spawnFields["lon"].SetCurrentValue(gate.location.y);
                                spawnFields["alt"].SetCurrentValue(gate.location.z);
                                spawnFields["diameter"].SetCurrentValue(gate.scale);
                                spawnFields["speed"].SetCurrentValue(gate.maxSpeed);
                                txtName = WaypointCourses.CourseLocations[selected_index].waypoints[selected_gate_index].name;
                                showPositioningControls = true;
                                break;
                        }
                    }
                }
                line += (int)((i - 1) / 4);
                line += 0.5f;
                if (!recording)
                {
                    txtName = GUI.TextField(SRightButtonRect(++line), txtName);
                    if (GUI.Button(SLeftButtonRect(line), selected_gate_index < WaypointCourses.CourseLocations[selected_index].waypoints.Count - 1 ? StringUtils.Localize("InsertGate") : StringUtils.Localize("#LOC_BDArmory_WP_AddGate"), BDArmorySetup.ButtonStyle))
                    {
                        string newName = string.IsNullOrEmpty(txtName.Trim()) ? $"{StringUtils.Localize("#LOC_BDArmory_WP_AddGate")} {(WaypointCourses.CourseLocations[selected_index].waypoints.Count.ToString())}" : txtName.Trim();
                        AddGate(newName);
                    }
                    GUI.Label(SLeftButtonRect(++line), $"{StringUtils.Localize("#LOC_BDArmory_WP_SelectModel")}: {Path.GetFileNameWithoutExtension(VesselSpawnerWindow.Instance.gateFiles[(int)SelectedGate])}", leftLabel); //Waypoint Type
                    if (VesselSpawnerWindow.Instance.gateModelsCount > 0)
                    {
                        if (SelectedGate != (SelectedGate = BDAMath.RoundToUnit(GUI.HorizontalSlider(SRightButtonRect(line), SelectedGate, -1, VesselSpawnerWindow.Instance.gateModelsCount), 1)))
                        {
                            SelectedModel = SelectedGate >= 0 ? Path.GetFileNameWithoutExtension(VesselSpawnerWindow.Instance.gateFiles[(int)SelectedGate]) : "";
                        }
                    }
                }
            }
            else
            {
                showPositioningControls = false;
                moddingSpawnPoint = false;
            }

            if (showPositioningControls)
            {
                //need to get these setup and configured - TODO
                if (selected_gate_index < 0 && !moddingSpawnPoint) return;
                Waypoint currGate = null;
                if (selected_gate_index >= 0)
                {
                    currGate = WaypointCourses.CourseLocations[selected_index].waypoints[selected_gate_index];
                    if (loadedGates[selected_gate_index] == null) return;
                }
                line += 0.5f;
                rects = SRight3Rects(++line);
                GUI.Label(rects[0], StringUtils.Localize("#autoLOC_463474"), centreLabel); //latitude
                GUI.Label(rects[1], StringUtils.Localize("#autoLOC_463478"), centreLabel); //longitude
                GUI.Label(rects[2], StringUtils.Localize("#autoLOC_463493"), centreLabel); //Altitude
                rects = SRight3Rects(++line);
                if (GUI.RepeatButton(SFieldButtonRect(line, 1), "<", BDArmorySetup.ButtonStyle))
                {
                    spawnFields["lat"].SetCurrentValue(spawnFields["lat"].CurrentValue - (movementIncrement / 100)); //having lat/long increase by 1 per frame while the button is held is going to cause gates to go *flying* across the continent
                    if (spawnFields["lat"].CurrentValue < -90) spawnFields["lat"].SetCurrentValue(spawnFields["lat"].CurrentValue + 180);
                }
                spawnFields["lat"].TryParseValue(GUI.TextField(rects[0], spawnFields["lat"].possibleValue, 8, spawnFields["lat"].style));
                if (GUI.RepeatButton(SFieldButtonRect(line, 7), ">", BDArmorySetup.ButtonStyle))
                {
                    spawnFields["lat"].SetCurrentValue(spawnFields["lat"].CurrentValue + (movementIncrement / 100));
                    if (spawnFields["lat"].CurrentValue > 90) spawnFields["lat"].SetCurrentValue(spawnFields["lat"].CurrentValue - 180);
                }

                if (GUI.RepeatButton(SFieldButtonRect(line, 8.5f), "<", BDArmorySetup.ButtonStyle))
                {
                    spawnFields["lon"].SetCurrentValue(spawnFields["lon"].CurrentValue - (movementIncrement / 100));
                    if (spawnFields["lon"].CurrentValue < -180) spawnFields["lon"].SetCurrentValue(spawnFields["lon"].CurrentValue + 360);
                }
                spawnFields["lon"].TryParseValue(GUI.TextField(rects[1], spawnFields["lon"].possibleValue, 8, spawnFields["lon"].style));
                if (GUI.RepeatButton(SFieldButtonRect(line, 14.5f), ">", BDArmorySetup.ButtonStyle))
                {
                    spawnFields["lon"].SetCurrentValue(spawnFields["lon"].CurrentValue + (movementIncrement / 100));
                    if (spawnFields["lon"].CurrentValue > 180) spawnFields["lon"].SetCurrentValue(spawnFields["lon"].CurrentValue - 360);
                }

                if (GUI.RepeatButton(SFieldButtonRect(line, 16), "<", BDArmorySetup.ButtonStyle))
                {
                    spawnFields["alt"].SetCurrentValue(spawnFields["alt"].CurrentValue - movementIncrement);
                    if (spawnFields["alt"].CurrentValue < 0) spawnFields["alt"].SetCurrentValue(0);
                }
                spawnFields["alt"].TryParseValue(GUI.TextField(rects[2], spawnFields["alt"].possibleValue, 8, spawnFields["alt"].style));
                if (GUI.RepeatButton(SFieldButtonRect(line, 22), ">", BDArmorySetup.ButtonStyle))
                {
                    spawnFields["alt"].SetCurrentValue(spawnFields["alt"].CurrentValue + movementIncrement);
                    if (spawnFields["alt"].CurrentValue > (FlightGlobals.currentMainBody.atmosphere ? FlightGlobals.currentMainBody.atmosphereDepth : 40000)) spawnFields["alt"].SetCurrentValue((FlightGlobals.currentMainBody.atmosphere ? FlightGlobals.currentMainBody.atmosphereDepth : 40000));
                }

                rects = SRight3Rects(++line);
                if (!moddingSpawnPoint)
                {
                    GUI.Label(rects[0], StringUtils.Localize("#autoLOC_8200035"), centreLabel); //Radius
                    GUI.Label(rects[1], StringUtils.Localize("#LOC_BDArmory_WP_SpeedLimit"), centreLabel);
                }
                GUI.Label(rects[moddingSpawnPoint ? 0 : 2], StringUtils.Localize("#LOC_BDArmory_WP_Increment"), centreLabel);
                rects = SRight3Rects(++line);
                if (!moddingSpawnPoint)
                {
                    if (GUI.RepeatButton(SFieldButtonRect(line, 1), "<", BDArmorySetup.ButtonStyle))
                    {
                        spawnFields["diameter"].SetCurrentValue(spawnFields["diameter"].CurrentValue - movementIncrement);
                    }
                    spawnFields["diameter"].TryParseValue(GUI.TextField(rects[0], spawnFields["diameter"].possibleValue, 8, spawnFields["diameter"].style));
                    if (GUI.RepeatButton(SFieldButtonRect(line, 7), ">", BDArmorySetup.ButtonStyle))
                    {
                        spawnFields["diameter"].SetCurrentValue(spawnFields["diameter"].CurrentValue + movementIncrement);
                        if (spawnFields["diameter"].CurrentValue > 1000) spawnFields["diameter"].SetCurrentValue(1000);
                    }
                    if (spawnFields["diameter"].CurrentValue < 5) spawnFields["diameter"].SetCurrentValue(5);
                    if (GUI.RepeatButton(SFieldButtonRect(line, 8.5f), "<", BDArmorySetup.ButtonStyle))
                    {
                        spawnFields["speed"].SetCurrentValue(spawnFields["speed"].CurrentValue - (movementIncrement));
                        if (spawnFields["speed"].CurrentValue < 0) spawnFields["speed"].SetCurrentValue(-1);
                    }
                    spawnFields["speed"].TryParseValue(GUI.TextField(rects[1], spawnFields["speed"].possibleValue, 8, spawnFields["speed"].style));
                    if (GUI.RepeatButton(SFieldButtonRect(line, 14.5f), ">", BDArmorySetup.ButtonStyle))
                    {
                        spawnFields["speed"].SetCurrentValue(spawnFields["speed"].CurrentValue + movementIncrement);
                        if (spawnFields["speed"].CurrentValue > 3000) spawnFields["speed"].SetCurrentValue(3000);
                    }
                }
                if (GUI.Button(SFieldButtonRect(line, moddingSpawnPoint ? 1 : 16), "<", BDArmorySetup.ButtonStyle))
                {
                    if (movementIncrement >= 2)
                        spawnFields["increment"].SetCurrentValue(spawnFields["increment"].CurrentValue - 1);
                    else
                    {
                        if (movementIncrement >= 0.2)
                            spawnFields["increment"].SetCurrentValue(spawnFields["increment"].CurrentValue - 0.1); //there is almost certainly a more elegant way to do scaling, FIXME later
                        else
                            spawnFields["increment"].SetCurrentValue(spawnFields["increment"].CurrentValue - 0.01);
                    }
                    if (spawnFields["increment"].CurrentValue < 0.001f) spawnFields["increment"].SetCurrentValue(0.001f);
                }
                spawnFields["increment"].TryParseValue(GUI.TextField(rects[moddingSpawnPoint ? 0 : 2], spawnFields["increment"].possibleValue, 8, spawnFields["increment"].style));
                if (GUI.Button(SFieldButtonRect(line, moddingSpawnPoint ? 7 : 22), ">", BDArmorySetup.ButtonStyle))
                {
                    spawnFields["increment"].SetCurrentValue(spawnFields["increment"].CurrentValue + 1);
                    if (spawnFields["increment"].CurrentValue > 1000) spawnFields["increment"].SetCurrentValue(1000);
                }
                movementIncrement = spawnFields["increment"].CurrentValue;
                if (!moddingSpawnPoint)
                {
                    if (spawnFields["lat"].CurrentValue != currGate.location.x ||
                    spawnFields["lon"].CurrentValue != currGate.location.y ||
                    spawnFields["alt"].CurrentValue != currGate.location.z ||
                    spawnFields["speed"].CurrentValue != currGate.maxSpeed ||
                    spawnFields["diameter"].CurrentValue != currGate.scale)
                    {
                        currGate.location = new Vector3d(spawnFields["lat"].CurrentValue, spawnFields["lon"].CurrentValue, spawnFields["alt"].CurrentValue);
                        currGate.scale = (float)spawnFields["diameter"].CurrentValue;
                        currGate.maxSpeed = (float)spawnFields["speed"].CurrentValue;
                        //WaypointField.Save(); //instead have a separate button and do saving manually?
                        currGate.model = SelectedModel;
                        loadedGates[selected_gate_index].UpdateWaypoint(currGate, selected_gate_index, WaypointCourses.CourseLocations[selected_index].waypoints);
                        //SnapCameraToGate(false);

                    }
                }
                else
                {
                    if (spawnFields["lat"].CurrentValue != WaypointCourses.CourseLocations[selected_index].spawnPoint.x ||
spawnFields["lon"].CurrentValue != WaypointCourses.CourseLocations[selected_index].spawnPoint.y ||
spawnFields["alt"].CurrentValue != BDArmorySettings.VESSEL_SPAWN_ALTITUDE)
                    {
                        WaypointCourses.CourseLocations[selected_index].spawnPoint = new Vector2d(spawnFields["lat"].CurrentValue, spawnFields["lon"].CurrentValue);
                        BDArmorySettings.VESSEL_SPAWN_ALTITUDE = (float)spawnFields["alt"].CurrentValue;

                        //WaypointField.Save(); //instead have a separate button and do saving manually?
                        //SnapCameraToGate(false);
                    }
                }
                if (currGate != null && !string.IsNullOrEmpty(txtName.Trim()) && txtName != currGate.name)
                    currGate.name = txtName;
                line += 0.3f;
            }

            if (selected_index >= 0 && GUI.Button(SLeftButtonRect(++line), StringUtils.Localize("#autoLOC_900627") + StringUtils.Localize("#autoLOC_6003085"), showCoursePath ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)) //view path
            {
                showCoursePath = !showCoursePath;
            }

            if (selected_index >= 0 && GUI.Button(SRightButtonRect(line), StringUtils.Localize("Snap Camera"), BDArmorySetup.ButtonStyle)) //view path
            {
                SnapCameraToGate();
            }

            line += 1.25f; // Bottom internal margin
            _windowHeight = (line * _lineHeight);
            GUIUtils.DragWindow();
        }

        public void SetVisible(bool visible)
        {
            BDArmorySetup.showWPBuilderGUI = visible;
            GUIUtils.SetGUIRectVisible(_guiCheckIndex, visible);
            if (!visible && !TournamentCoordinator.Instance.IsRunning) //don't delete gates if running course
            {
                foreach (var gate in loadedGates)
                {
                    gate.disabled = true;
                    gate.gameObject.SetActive(false);
                }
                loadedGates.Clear();
            }
        }
        void AddGate(string newName)
        {
            Vector3d gateCoords;
            FlightGlobals.currentMainBody.GetLatLonAlt(FlightGlobals.ActiveVessel.CoM, out gateCoords.x, out gateCoords.y, out gateCoords.z);
            int wp;
            if (!recording && selected_gate_index < WaypointCourses.CourseLocations[selected_index].waypoints.Count - 1)
            {
                WaypointCourses.CourseLocations[selected_index].waypoints.Insert(selected_gate_index + 1, (new Waypoint(newName, gateCoords, 500, -1, SelectedModel)));
                wp = selected_gate_index + 1;
            }
            else
            {
                WaypointCourses.CourseLocations[selected_index].waypoints.Add(new Waypoint(newName, gateCoords, 500, -1, SelectedModel));
                wp = WaypointCourses.CourseLocations[selected_index].waypoints.Count - 1;
            }
            //WaypointField.Save();
            if (string.IsNullOrEmpty(SelectedModel))
                SelectedModel = "Ring";

            Vector3d WorldCoords = VectorUtils.GetWorldSurfacePosition(new Vector3(WaypointCourses.CourseLocations[selected_index].waypoints[wp].location.x, WaypointCourses.CourseLocations[selected_index].waypoints[wp].location.y, (BDArmorySettings.WAYPOINTS_ALTITUDE < 0 ? WaypointCourses.CourseLocations[selected_index].waypoints[wp].location.z : BDArmorySettings.WAYPOINTS_ALTITUDE)), FlightGlobals.currentMainBody);
            Vector3d previousLocation = FlightGlobals.ActiveVessel.transform.position;
            if (wp > 0) previousLocation = VectorUtils.GetWorldSurfacePosition(new Vector3(WaypointCourses.CourseLocations[selected_index].waypoints[wp - 1].location.x, WaypointCourses.CourseLocations[selected_index].waypoints[wp - 1].location.y, (BDArmorySettings.WAYPOINTS_ALTITUDE < 0 ? WaypointCourses.CourseLocations[selected_index].waypoints[wp - 1].location.z : BDArmorySettings.WAYPOINTS_ALTITUDE)), FlightGlobals.currentMainBody);

            var direction = (WorldCoords - previousLocation).normalized;
            WayPointMarker.CreateWaypoint(WorldCoords, direction, SelectedModel, WaypointCourses.CourseLocations[selected_index].waypoints[wp].scale);

            var location = string.Format("({0:##.###}, {1:##.###}, {2:####}", WaypointCourses.CourseLocations[selected_index].waypoints[wp].location.x, WaypointCourses.CourseLocations[selected_index].waypoints[wp].location.y, WaypointCourses.CourseLocations[selected_index].waypoints[wp].location.z);
            Debug.Log("[BDArmory.Waypoints]: Creating waypoint marker at  " + " " + location + " World: " + FlightGlobals.currentMainBody.flightGlobalsIndex + " scale: " + WaypointCourses.CourseLocations[selected_index].waypoints[wp].scale + " model:" + WaypointCourses.CourseLocations[selected_index].waypoints[wp].model);
            if (!recording)
            {
                if (selected_gate_index < WaypointCourses.CourseLocations[selected_index].waypoints.Count - 1)
                    selected_gate_index++;
                else
                    selected_gate_index = wp;
                //SnapCameraToGate(false); //this will need to change if adding ability to insert gates into middle of course, not at end

                var newGate = WaypointCourses.CourseLocations[selected_index].waypoints[wp];
                spawnFields["lat"].SetCurrentValue(newGate.location.x);
                spawnFields["lon"].SetCurrentValue(newGate.location.y);
                spawnFields["alt"].SetCurrentValue(newGate.location.z);
                spawnFields["diameter"].SetCurrentValue(500);
                spawnFields["speed"].SetCurrentValue(-1);

                moddingSpawnPoint = false;
                txtName = WaypointCourses.CourseLocations[selected_index].waypoints[selected_gate_index].name;
                showPositioningControls = true;
            }
        }
    }
}