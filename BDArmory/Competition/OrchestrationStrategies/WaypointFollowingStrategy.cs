using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using BDArmory.Competition.RemoteOrchestration;
using BDArmory.Control;
using BDArmory.Damage;
using BDArmory.FX;
using BDArmory.GameModes.Waypoints;
using BDArmory.Modules;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Extensions;

namespace BDArmory.Competition.OrchestrationStrategies
{
    public class WaypointFollowingStrategy : OrchestrationStrategy
    {
        private List<Waypoint> waypoints;
        private List<BDGenericAIBase> pilots;
        public static List<BDGenericAIBase> activePilots;
        public static List<WayPointTracing> Ghosts = new List<WayPointTracing>();

        public static string ModelPath = "BDArmory/Models/WayPoint/model";

        public WaypointFollowingStrategy(List<Waypoint> waypoints)
        {
            this.waypoints = waypoints;
        }

        float liftMultiplier = 0;

        public IEnumerator Execute(BDAScoreClient client, BDAScoreService service)
        {
            if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.WaypointFollowingStrategy]: Started");
            pilots = BDACompetitionMode.Instance.GetAllPilots().Select(p => p.vessel.ActiveController().AI as BDGenericAIBase).ToList();
            if (BDACompetitionMode.Instance.competitionIsActive) BDACompetitionMode.Instance.StopCompetition(); // Stop any currently active competition.
            BDACompetitionMode.Instance.ResetCompetitionStuff(preSpawn: true); // Reset a bunch of stuff related to competitions so they don't interfere.
            BDACompetitionMode.Instance.StartCompetitionMode(BDArmorySettings.COMPETITION_DISTANCE, BDArmorySettings.COMPETITION_START_DESPITE_FAILURES, "", CompetitionType.WAYPOINTS);
            if (BDArmorySettings.WAYPOINTS_INFINITE_FUEL_AT_START)
            {
                foreach (var pilot in pilots) pilot.MaintainFuelLevels(true);
            } //waypoints is dependent on PilotCommands.Waypoint, which gets overridden to PilotCommands.FlyTo by the start of comp.
            yield return new WaitWhile(() => BDACompetitionMode.Instance.competitionStarting);
            yield return new WaitWhile(() => BDACompetitionMode.Instance.pinataAlive);
            PrepareCompetition();

            // Configure the pilots' waypoints.
            var mappedWaypoints = BDArmorySettings.WAYPOINTS_ALTITUDE < 0 ? waypoints.Select(e => e.location).ToList() : waypoints.Select(wp => new Vector3(wp.location.x, wp.location.y, BDArmorySettings.WAYPOINTS_ALTITUDE)).ToList();
            BDACompetitionMode.Instance.competitionStatus.Add($"Starting waypoints competition {BDACompetitionMode.Instance.CompetitionID}.");
            if (BDArmorySettings.DEBUG_OTHER) Debug.Log(string.Format("[BDArmory.WaypointFollowingStrategy]: Setting {0} waypoints", mappedWaypoints.Count));

            foreach (var pilot in pilots)
            {
                pilot.SetWaypoints(mappedWaypoints);
                pilot.MaintainFuelLevelsUntilWaypoint();
                foreach (var kerbal in VesselModuleRegistry.GetKerbalEVAs(pilot.vessel))
                {
                    if (kerbal == null) continue;
                    // Remove drag from EVA kerbals on seats.
                    kerbal.part.dragModel = Part.DragModel.SPHERICAL; // Use the spherical drag model for which the min/max drag values work properly.
                    kerbal.part.ShieldedFromAirstream = true;
                }
            }

            // Wait for the pilots to complete the course.
            var startedAt = Planetarium.GetUniversalTime();
            if (BDArmorySettings.WAYPOINT_GUARD_INDEX != -1)
            {
                if (pilots.Count > 1)
                    yield return new WaitWhile(() => BDACompetitionMode.Instance.competitionIsActive); //DoUpdate handles the deathmatch half of the combat waypoint race and ends things when only 1 team left
                else yield return new WaitWhile(() => pilots.Any(
                    pilot => pilot != null && pilot.WeaponManager != null && pilot.IsRunningWaypoints &&
                    (pilot.aiType == AIType.SurfaceAI || (pilot.TakingOff || !pilot.vessel.LandedOrSplashed)))
                    );                
            }
            else
            {
                yield return new WaitWhile(() => pilots.Any(
                    pilot => pilot != null && pilot.WeaponManager != null && pilot.IsRunningWaypoints &&
                    (pilot.aiType == AIType.SurfaceAI || (pilot.TakingOff || !pilot.vessel.LandedOrSplashed)))
                    );    
            }
            var endedAt = Planetarium.GetUniversalTime();

            BDACompetitionMode.Instance.competitionStatus.Add("Waypoints competition finished. Scores:");
            foreach (var player in BDACompetitionMode.Instance.Scores.Players)
            {
                var waypointScores = BDACompetitionMode.Instance.Scores.ScoreData[player].waypointsReached;
                var waypointCount = waypointScores.Count();
                var deviation = waypointScores.Sum(w => w.deviation);
                var elapsedTime = waypointCount == 0 ? 0 : waypointScores.Last().timestamp - waypointScores.First().timestamp;
                if (service != null) service.TrackWaypoint(player, (float)elapsedTime, waypointCount, deviation);

                var displayName = player;
                if (BDArmorySettings.ENABLE_HOS && BDArmorySettings.HALL_OF_SHAME_LIST.Contains(player) && !string.IsNullOrEmpty(BDArmorySettings.HOS_BADGE))
                {
                    displayName += " (" + BDArmorySettings.HOS_BADGE + ")";
                }
                BDACompetitionMode.Instance.competitionStatus.Add($"  - {displayName}: Time: {elapsedTime:F2}s, Waypoints reached: {waypointCount}, Deviation: {deviation}");

                Debug.Log(string.Format("[BDArmory.WaypointFollowingStrategy]: Finished {0}, elapsed={1:0.00}, count={2}, deviation={3:0.00}", player, elapsedTime, waypointCount, deviation));
            }

            CleanUp();
        }

        void PrepareCompetition()
        {
            // Scores are already configure in ResetCompetitionStuff prior to this being called.
            pilots = pilots.Where(p => p != null).ToList(); // Remove any already dead pilots.
            if (pilots.Count > 1) //running multiple craft through the waypoints at the same time
                LoadedVesselSwitcher.Instance.MassTeamSwitch(true);
            else //increment team each heat
            {
                char T = (char)(Convert.ToUInt16('A') + BDATournament.Instance.currentHeat);
                pilots[0].WeaponManager.SetTeam(BDTeam.Get(T.ToString()));
            }
            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 55)
            {
                liftMultiplier = PhysicsGlobals.LiftMultiplier;
                PhysicsGlobals.LiftMultiplier = 0.1f;
            }
            if (BDArmorySettings.WAYPOINTS_VISUALIZE)
            {
                Vector3 previousLocation = FlightGlobals.ActiveVessel.transform.position;
                //FlightGlobals.currentMainBody.GetLatLonAlt(FlightGlobals.ActiveVessel.transform.position, out previousLocation.x, out previousLocation.y, out previousLocation.z);
                //previousLocation.z = BDArmorySettings.WAYPOINTS_ALTITUDE;
                for (int i = 0; i < waypoints.Count; i++)
                {
                    if (!string.IsNullOrEmpty(waypoints[i].model)) ModelPath = ModelPath = "BDArmory/Models/WayPoint/" + waypoints[i].model;
                    if (!string.IsNullOrEmpty(VesselSpawnerWindow.Instance.SelectedModel) && VesselSpawnerWindow.Instance.SelectedGate >= 0)
                        ModelPath = "BDArmory/Models/WayPoint/" + VesselSpawnerWindow.Instance.SelectedModel;
                    float terrainAltitude = (float)FlightGlobals.currentMainBody.TerrainAltitude(waypoints[i].location.x, waypoints[i].location.y);
                    Vector3d WorldCoords = VectorUtils.GetWorldSurfacePosition(new Vector3(waypoints[i].location.x, waypoints[i].location.y, (BDArmorySettings.WAYPOINTS_ALTITUDE < 0 ? waypoints[i].location.z : BDArmorySettings.WAYPOINTS_ALTITUDE) + terrainAltitude), FlightGlobals.currentMainBody);
                    //FlightGlobals.currentMainBody.GetLatLonAlt(new Vector3(waypoints[i].latitude, waypoints[i].longitude, waypoints[i].altitude), out WorldCoords.x, out WorldCoords.y, out WorldCoords.z);
                    var direction = (WorldCoords - previousLocation).normalized;
                    //WayPointMarker.CreateWaypoint(WorldCoords, direction, ModelPath, BDArmorySettings.WAYPOINTS_SCALE);
                    WayPointMarker.CreateWaypoint(WorldCoords, direction, ModelPath, BDArmorySettings.WAYPOINTS_SCALE > 0 ? BDArmorySettings.WAYPOINTS_SCALE : waypoints[i].scale);

                    previousLocation = WorldCoords;
                    var location = string.Format("({0:##.###}, {1:##.###}, {2:####}", waypoints[i].location.x, waypoints[i].location.y, waypoints[i].location.z);
                    Debug.Log("[BDArmory.Waypoints]: Creating waypoint marker at  " + " " + location + " World: " + FlightGlobals.currentMainBody.flightGlobalsIndex + " scale: " + (BDArmorySettings.WAYPOINTS_SCALE > 0 ? BDArmorySettings.WAYPOINTS_SCALE : waypoints[i].scale) + " Model: " + ModelPath);
                }
            }

            if (BDArmorySettings.WAYPOINTS_MODE)
            {
                float terrainAltitude = (float)FlightGlobals.currentMainBody.TerrainAltitude(waypoints[0].location.x, waypoints[0].location.y);
                Vector3d WorldCoords = VectorUtils.GetWorldSurfacePosition(new Vector3(waypoints[0].location.x, waypoints[0].location.y, waypoints[0].location.z + terrainAltitude), FlightGlobals.currentMainBody);
                foreach (var pilot in pilots)
                {
                    if (BDArmorySettings.RUNWAY_PROJECT && (BDArmorySettings.RUNWAY_PROJECT_ROUND == 50 || BDArmorySettings.RUNWAY_PROJECT_ROUND == 55)) // S4R10 alt limiter
                    {
                        var pilotAI = pilot as BDModulePilotAI;
                        if (pilotAI != null)
                        {
                            // Max Altitude must be 100.
                            pilotAI.maxAltitudeToggle = true;
                            pilotAI.maxAltitude = Mathf.Min(pilotAI.maxAltitude, 100f);
                            pilotAI.minAltitude = Mathf.Min(pilotAI.minAltitude, 50f); // Waypoints are at 50, so anything higher than this is going to trigger gain alt all the time.
                            pilotAI.defaultAltitude = Mathf.Clamp(pilotAI.defaultAltitude, pilotAI.minAltitude, pilotAI.maxAltitude);
                            if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 55) pilotAI.ImmelmannTurnAngle = 0; // Set the Immelmann turn angle to 0 since most of these craft dont't pitch well.
                        }
                    }
                    /*
                    if (pilots.Count > 1) //running multiple craft through the waypoints at the same time
                    {
                        if (Ghosts.Count > 0)
                        {
                            foreach (var tracer in Ghosts)
                            {
                                if (tracer != null)
                                    tracer.gameObject.SetActive(false);
                            }
                        }
                        Ghosts.Clear(); //need to have Ghosts also clear every round start
                        WayPointTracing.CreateTracer(WorldCoords, pilot);
                    }
                    else
                    {
                        if (Ghosts.Count > 0)
                        {
                            foreach (var tracer in Ghosts)
                            {
                                if (tracer != null)
                                    tracer.resetRenderer();
                            }
                        }
                        WayPointTracing.CreateTracer(WorldCoords, pilots[0]);
                    }
                    */
                }
            }
        }

        public void CleanUp()
        {
            if (BDACompetitionMode.Instance.competitionIsActive) BDACompetitionMode.Instance.StopCompetition(); // Competition is done, so stop it and do the rest of the book-keeping.
            if (liftMultiplier > 0)
            {
                PhysicsGlobals.LiftMultiplier = liftMultiplier;
                liftMultiplier = 0;
            }
        }
    }

    public class WayPointMarker : MonoBehaviour
    {
        //public static ObjectPool WaypointPool;
        public static Dictionary<string, ObjectPool> WaypointPools = new Dictionary<string, ObjectPool>();
        public Vector3 Position { get; set; }
        public bool disabled = false;
        static void CreateObjectPool(string ModelPath)
        {
            var key = ModelPath;
            if (!WaypointPools.ContainsKey(key) || WaypointPools[key] == null)
            {
                var WPTemplate = GameDatabase.Instance.GetModel(ModelPath);
                if (WPTemplate == null)
                {
                    Debug.LogError("[BDArmory.WayPointMarker]: " + ModelPath + " was not found, using the default model instead. Please fix your model.");
                    WPTemplate = GameDatabase.Instance.GetModel("BDArmory/Models/WayPoint/Ring");
                }
                WPTemplate.SetActive(false);
                WPTemplate.AddComponent<WayPointMarker>();
                WaypointPools[key] = ObjectPool.CreateObjectPool(WPTemplate, 10, true, true);
            }
        }
        public static void CreateWaypoint(Vector3 position, Vector3 direction, string ModelPath, float scale)
        {
            CreateObjectPool(ModelPath);

            GameObject newWayPoint = WaypointPools[ModelPath].GetPooledObject();
            Vector3d WorldCoords = VectorUtils.GetWorldSurfacePosition(position, FlightGlobals.currentMainBody);
            Quaternion rotation = Quaternion.LookRotation(direction, VectorUtils.GetUpDirection(WorldCoords)); //this needed, so the model is aligned to the ground normal, not the body transform orientation

            newWayPoint.transform.SetPositionAndRotation(position, rotation);

            newWayPoint.transform.RotateAround(position, newWayPoint.transform.up, VectorUtils.Angle(newWayPoint.transform.forward, direction)); //rotate model on horizontal plane towards last gate
            newWayPoint.transform.RotateAround(position, newWayPoint.transform.right, VectorUtils.Angle(newWayPoint.transform.forward, direction)); //and on vertical plane if elevation change between the two

            float WPScale = scale / 500; //default ring/torii models scaled for 500m
            newWayPoint.transform.localScale = new Vector3(WPScale, WPScale, WPScale);
            WayPointMarker NWP = newWayPoint.GetComponent<WayPointMarker>();
            NWP.Position = position;
            if (BDArmorySetup.Instance.hasWPCourseSpawner) CourseBuilderGUI.Instance.loadedGates.Add(NWP);
            newWayPoint.SetActive(true);
        }

        public void UpdateWaypoint(Waypoint waypoint, int wpIndex, List<Waypoint> wpList)
        {
            var terrainAltitude = FlightGlobals.currentMainBody.TerrainAltitude(waypoint.location.x, waypoint.location.y);
            Vector3d WorldCoords = VectorUtils.GetWorldSurfacePosition(new Vector3(waypoint.location.x, waypoint.location.y, (BDArmorySettings.WAYPOINTS_ALTITUDE < 0 ? waypoint.location.z : BDArmorySettings.WAYPOINTS_ALTITUDE) + (float)terrainAltitude), FlightGlobals.currentMainBody);
            Vector3d previousLocation = WorldCoords;
            if (wpIndex > 0)
                previousLocation = VectorUtils.GetWorldSurfacePosition(new Vector3(wpList[wpIndex - 1].location.x, wpList[wpIndex - 1].location.y, (BDArmorySettings.WAYPOINTS_ALTITUDE < 0 ? wpList[wpIndex - 1].location.z : BDArmorySettings.WAYPOINTS_ALTITUDE) + (float)terrainAltitude), FlightGlobals.currentMainBody);

            var direction = (WorldCoords - previousLocation).normalized;
            Quaternion rotation = Quaternion.LookRotation(direction, VectorUtils.GetUpDirection(WorldCoords)); //this needed, so the model is aligned to the ground normal, not the body transform orientation

            transform.SetPositionAndRotation(WorldCoords, rotation);

            transform.RotateAround(WorldCoords, transform.up, VectorUtils.Angle(transform.forward, direction)); //rotate model on horizontal plane towards last gate
            transform.RotateAround(WorldCoords, transform.right, VectorUtils.Angle(transform.forward, direction)); //and on vertical plane if elevation change between the two

            float WPScale = waypoint.scale / 500; //default ring/torii models scaled for 500m
            transform.localScale = new Vector3(WPScale, WPScale, WPScale);
            Position = waypoint.location;
        }

        void Awake()
        {
            transform.parent = FlightGlobals.ActiveVessel.mainBody.transform;
        }
        private void OnEnable()
        {
            disabled = false;
        }
        void Update()
        {
            if (!gameObject.activeInHierarchy) return;
            if (disabled || (!BDACompetitionMode.Instance.competitionIsActive && !BDArmorySetup.showWPBuilderGUI) || !HighLogic.LoadedSceneIsFlight)
            {
                gameObject.SetActive(false);
                return;
            }
            //this.transform.LookAt(FlightCamera.fetch.mainCamera.transform); //Always face the camera
            //if adding Races! style waypoint customization for building custom tracks, have the camera follow be a toggle, to allow for different models? Aub's Torii, etc.
        }
    }
    public class WayPointTracing : MonoBehaviour
    {
        public static ObjectPool TracePool;
        public Vector3 Position { get; set; }
        public BDGenericAIBase AI { get; set; }

        private List<Vector3> pathPoints = new List<Vector3>();

        LineRenderer tracerRenderer;

        public bool disabled = false;
        public bool replayGhost = false;
        static void CreateObjectPool()
        {
            GameObject ghost = GameDatabase.Instance.GetModel("BDArmory/Models/shell/model"); //could have just done this as a gameObject instead of a tiny model.
            ghost.SetActive(false);
            ghost.AddComponent<WayPointTracing>();
            TracePool = ObjectPool.CreateObjectPool(ghost, 120, true, true);
        }

        public static void CreateTracer(Vector3 position, BDGenericAIBase AI)
        {
            if (TracePool == null)
            {
                CreateObjectPool();
            }
            GameObject newTrace = TracePool.GetPooledObject();
            newTrace.transform.position = position;

            WayPointTracing NWP = newTrace.GetComponent<WayPointTracing>();
            NWP.Position = position;
            NWP.AI = AI;
            NWP.setupRenderer();
            newTrace.SetActive(true);
            WaypointFollowingStrategy.Ghosts.Add(NWP);
        }

        void Awake()
        {
            transform.parent = FlightGlobals.ActiveVessel.mainBody.transform; //FIXME need to update this to grab worldindex for non-kerbin spawns for custom track building
        }
        void Start()
        {
            setupRenderer(); //one linerenderer per vessel
            nodes = 0;
            timer = 0;
        }
        private void OnEnable()
        {
            disabled = false;
            setupRenderer();
            pathPoints.Clear();
            nodes = 0;
            timer = 0;
        }
        void setupRenderer()
        {
            if (tracerRenderer == null)
            {
                tracerRenderer = new LineRenderer();
            }
            Debug.Log("[WayPointTracer] setting up Renderer");
            Transform tf = this.transform;
            tracerRenderer = tf.gameObject.AddOrGetComponent<LineRenderer>();
            Color Color = BDTISetup.Instance.ColorAssignments[AI.WeaponManager.Team.Name]; //hence the incrementing teams in One-at-a-Time mode
            tracerRenderer.material = new Material(Shader.Find("KSP/Particles/Alpha Blended"));
            tracerRenderer.material.SetColor("_TintColor", Color);
            tracerRenderer.material.mainTexture = GameDatabase.Instance.GetTexture("BDArmory/Textures/laser", false);
            tracerRenderer.material.SetTextureScale("_MainTex", new Vector2(0.01f, 1));
            tracerRenderer.textureMode = LineTextureMode.Tile;
            tracerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; //= false;
            tracerRenderer.receiveShadows = false;
            tracerRenderer.startWidth = 5;
            tracerRenderer.endWidth = 5;
            tracerRenderer.positionCount = 2;
            tracerRenderer.SetPosition(0, Vector3.zero);
            tracerRenderer.SetPosition(1, Vector3.zero);
            tracerRenderer.useWorldSpace = false;
            tracerRenderer.enabled = false;
        }
        public void resetRenderer()
        {//reset things for ghost mode replay while the current vessel races
            if (tracerRenderer == null)
            {
                return;
            }
            Transform tf = this.transform;
            tracerRenderer = tf.gameObject.AddOrGetComponent<LineRenderer>();
            tracerRenderer.positionCount = 2;
            tracerRenderer.SetPosition(0, Vector3.zero);
            tracerRenderer.SetPosition(1, Vector3.zero);
            tracerRenderer.useWorldSpace = false;
            tracerRenderer.enabled = false;
            timer = 0;
            nodes = 0;
            replayGhost = true;
        }
        private float timer = 0;
        private int nodes = 0;
        void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (BDArmorySetup.GameIsPaused) return;
                if (AI.vessel == null) return;
                if (AI.vessel.situation == Vessel.Situations.ORBITING || AI.vessel.situation == Vessel.Situations.ESCAPING) return;

                if ((!replayGhost && AI.CurrentWaypointIndex > 0) || (replayGhost && WaypointFollowingStrategy.activePilots[0].CurrentWaypointIndex > 0))
                {    //don't record before first WP
                    timer += Time.fixedDeltaTime;
                    if (timer > 1)
                    {
                        timer = 0;
                        nodes++;
                        //Vector3d WorldCoords = VectorUtils.GetWorldSurfacePostion(wm.Current.vessel.transform.position, FlightGlobals.currentMainBody);                       
                        if (!replayGhost)
                        {
                            pathPoints.Add(AI.vessel.transform.position);
                        }

                        //if (BDArmorySettings.DRAW_VESSEL_TRAILS)
                        {
                            if (tracerRenderer == null)
                            {
                                setupRenderer();
                            }
                            tracerRenderer.enabled = true;
                            tracerRenderer.positionCount = nodes; //clamp count to elapsed time, for replaying ghosts from prior heats 
                            for (int i = 0; i < nodes - 1; i++)
                            {
                                tracerRenderer.SetPosition(i, pathPoints[i]); //add Linerender positions for all but last position
                            } //this is working, to a point, at which the render diverges from where the vessel has gone. Need a krakenbane offset?
                              //renderer was attached to a WayPointTrace class so positions would always remain consistant relative the tracer, not the ship
                        }
                    }
                    if (!replayGhost && nodes > 1) tracerRenderer.SetPosition(tracerRenderer.positionCount - 1, AI.vessel.CoM); //have last position update real-time with vessel position
                }
                else
                {
                    if (tracerRenderer != null)
                    {
                        tracerRenderer.enabled = false;
                        tracerRenderer = null;
                    }
                }
            }
        }
    }
}

