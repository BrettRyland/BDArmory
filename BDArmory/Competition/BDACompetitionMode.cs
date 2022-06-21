using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSP.Localization;

using BDArmory.Competition.VesselSpawning;
using BDArmory.Control;
using BDArmory.Damage;
using BDArmory.Extensions;
using BDArmory.FX;
using BDArmory.GameModes;
using BDArmory.Modules;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Weapons.Missiles;

namespace BDArmory.Competition
{
    public enum CompetitionStartFailureReason { None, OnlyOneTeam, TeamsChanged, TeamLeaderDisappeared, PilotDisappeared, Other };
    public enum CompetitionType { FFA, SEQUENCED, WAYPOINTS };


    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class BDACompetitionMode : MonoBehaviour
    {
        public static BDACompetitionMode Instance;

        #region Flags and variables
        // Score tracking flags and variables.
        public CompetitionScores Scores = new CompetitionScores();

        // Competition flags and variables
        public CompetitionType competitionType = CompetitionType.FFA;
        public int CompetitionID; // time competition was started
        public double competitionStartTime = -1;
        public double MutatorResetTime = -1;
        public double competitionPreStartTime = -1;
        public double nextUpdateTick = -1;
        private double decisionTick = -1;
        private double finalGracePeriodStart = -1;
        public static float gravityMultiplier = 1f;
        float lastGravityMultiplier;
        private string deadOrAlive = "";
        static HashSet<string> outOfAmmo = new HashSet<string>(); // outOfAmmo register for tracking which planes are out of ammo.

        // Action groups
        public static Dictionary<int, KSPActionGroup> KM_dictAG = new Dictionary<int, KSPActionGroup> {
            { 0,  KSPActionGroup.None },
            { 1,  KSPActionGroup.Custom01 },
            { 2,  KSPActionGroup.Custom02 },
            { 3,  KSPActionGroup.Custom03 },
            { 4,  KSPActionGroup.Custom04 },
            { 5,  KSPActionGroup.Custom05 },
            { 6,  KSPActionGroup.Custom06 },
            { 7,  KSPActionGroup.Custom07 },
            { 8,  KSPActionGroup.Custom08 },
            { 9,  KSPActionGroup.Custom09 },
            { 10, KSPActionGroup.Custom10 },
            { 11, KSPActionGroup.Light },
            { 12, KSPActionGroup.RCS },
            { 13, KSPActionGroup.SAS },
            { 14, KSPActionGroup.Brakes },
            { 15, KSPActionGroup.Abort },
            { 16, KSPActionGroup.Gear }
        };

        // Tag mode flags and variables.
        public bool startTag = false; // For tag mode
        public int previousNumberCompetitive = 2; // Also for tag mode

        // KILLER GM - how we look for slowest planes
        public Dictionary<string, double> KillTimer = new Dictionary<string, double>(); // Note that this is only used as an indicator, not a controller, now.
        //public Dictionary<string, double> AverageSpeed = new Dictionary<string, double>();
        //public Dictionary<string, double> AverageAltitude = new Dictionary<string, double>();
        //public Dictionary<string, int> FireCount = new Dictionary<string, int>();
        //public Dictionary<string, int> FireCount2 = new Dictionary<string, int>();

        // pilot actions
        private Dictionary<string, string> pilotActions = new Dictionary<string, string>();
        #endregion

        #region GUI elements
        GUIStyle statusStyle;
        GUIStyle statusStyleShadow;
        Rect statusRect;
        Rect statusRectShadow;
        Rect clockRect;
        Rect clockRectShadow;
        GUIStyle dateStyle;
        GUIStyle dateStyleShadow;
        Rect dateRect;
        Rect dateRectShadow;
        string guiStatusString;
        #endregion

        void Awake()
        {
            if (Instance)
            {
                Destroy(Instance);
            }

            Instance = this;
        }

        void Start()
        {
            UpdateGUIElements();
        }

        void OnGUI()
        {
            if (BDArmorySettings.DISPLAY_COMPETITION_STATUS)
            {
                // Clock
                if (competitionIsActive || competitionStarting) // Show a competition clock (for post-processing synchronisation).
                {
                    var gTime = (float)(Planetarium.GetUniversalTime() - (competitionIsActive ? competitionStartTime : competitionPreStartTime));
                    var minutes = Mathf.FloorToInt(gTime / 60);
                    var seconds = gTime % 60;
                    // string pTime = minutes.ToString("0") + ":" + seconds.ToString("00.00");
                    string pTime = $"{minutes:0}:{seconds:00.00}";
                    GUI.Label(clockRectShadow, pTime, statusStyleShadow);
                    GUI.Label(clockRect, pTime, statusStyle);
                    string pDate = DateTime.UtcNow.ToString("yyyy-MM-dd\nHH:mm:ss") + " UTC";
                    GUI.Label(dateRectShadow, pDate, dateStyleShadow);
                    GUI.Label(dateRect, pDate, dateStyle);
                }

                // Messages
                guiStatusString = competitionStatus.ToString();
                if (BDArmorySetup.GAME_UI_ENABLED || BDArmorySettings.DISPLAY_COMPETITION_STATUS_WITH_HIDDEN_UI) // Append current pilot action to guiStatusString.
                {
                    if (competitionStarting || competitionStartTime > 0)
                    {
                        string currentVesselStatus = "";
                        if (FlightGlobals.ActiveVessel != null)
                        {
                            var vesselName = FlightGlobals.ActiveVessel.GetName();
                            string postFix = "";
                            if (pilotActions.ContainsKey(vesselName))
                            {
                                postFix = pilotActions[vesselName];
                            }
                            if (Scores.Players.Contains(vesselName))
                            {
                                ScoringData vData = Scores.ScoreData[vesselName];
                                if (Planetarium.GetUniversalTime() - vData.lastDamageTime < 2)
                                {
                                    postFix = " is taking damage from " + vData.lastPersonWhoDamagedMe;
                                    if (BDArmorySettings.ENABLE_HOS && BDArmorySettings.HALL_OF_SHAME_LIST.Contains(vData.lastPersonWhoDamagedMe))
                                    {
                                        if (!string.IsNullOrEmpty(BDArmorySettings.HOS_BADGE))
                                        {
                                            postFix += " (" + BDArmorySettings.HOS_BADGE + ")";
                                        }
                                    }
                                }
                            }
                            if (postFix != "" || vesselName != competitionStatus.lastActiveVessel)
                                currentVesselStatus = vesselName + postFix;
                            competitionStatus.lastActiveVessel = vesselName;
                        }
                        guiStatusString += (string.IsNullOrEmpty(guiStatusString) ? "" : "\n") + currentVesselStatus;
                        if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                        {
                            guiStatusString += $"\nCurrent Firing Rate: {BDArmorySettings.FIRE_RATE_OVERRIDE} shots/min.";
                        }
                    }
                }
                if (!BDArmorySetup.GAME_UI_ENABLED)
                {
                    if (BDArmorySettings.DISPLAY_COMPETITION_STATUS_WITH_HIDDEN_UI)
                    {
                        guiStatusString = deadOrAlive + "\n" + guiStatusString;
                    }
                    else
                    {
                        guiStatusString = deadOrAlive;
                    }
                }
                GUI.Label(statusRectShadow, guiStatusString, statusStyleShadow);
                GUI.Label(statusRect, guiStatusString, statusStyle);
            }
            if (KSP.UI.Dialogs.FlightResultsDialog.isDisplaying && KSP.UI.Dialogs.FlightResultsDialog.showExitControls) // Prevent the Flight Results window from interrupting things when a certain vessel dies.
            {
                KSP.UI.Dialogs.FlightResultsDialog.Close();
            }
        }

        public void UpdateGUIElements()
        {
            statusStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.label);
            statusStyle.fontStyle = FontStyle.Bold;
            statusStyle.alignment = TextAnchor.UpperLeft;
            dateStyle = new GUIStyle(statusStyle);
            if (BDArmorySetup.GAME_UI_ENABLED)
            {
                clockRect = new Rect(10, 42, 100, 30);
                dateRect = new Rect(100, 38, 100, 20);
                statusRect = new Rect(30, 80, Screen.width - 130, Mathf.FloorToInt(Screen.height / 2));
                statusStyle.fontSize = 22;
                dateStyle.fontSize = 14;
            }
            else
            {
                clockRect = new Rect(10, 6, 80, 20);
                dateRect = new Rect(10, 26, 100, 20);
                statusRect = new Rect(80, 6, Screen.width - 80, Mathf.FloorToInt(Screen.height / 2));
                statusStyle.fontSize = 14;
                dateStyle.fontSize = 10;
            }
            clockRectShadow = new Rect(clockRect);
            clockRectShadow.x += 2;
            clockRectShadow.y += 2;
            dateRectShadow = new Rect(dateRect);
            dateRectShadow.x += 2;
            dateRectShadow.y += 2;
            statusRectShadow = new Rect(statusRect);
            statusRectShadow.x += 2;
            statusRectShadow.y += 2;
            statusStyleShadow = new GUIStyle(statusStyle);
            statusStyleShadow.normal.textColor = new Color(0, 0, 0, 0.75f);
            dateStyleShadow = new GUIStyle(dateStyle);
            dateStyleShadow.normal.textColor = new Color(0, 0, 0, 0.75f);
        }

        void OnDestroy()
        {
            StopCompetition();
            StopAllCoroutines();
        }

        #region Competition start/stop routines
        //Competition mode
        public bool competitionStarting = false;
        public bool sequencedCompetitionStarting = false;
        public bool competitionIsActive = false;
        Coroutine competitionRoutine;
        public CompetitionStartFailureReason competitionStartFailureReason;

        public class CompetitionStatus
        {
            private List<Tuple<double, string>> status = new List<Tuple<double, string>>();
            public void Add(string message) { if (BDArmorySettings.DISPLAY_COMPETITION_STATUS) { status.Add(new Tuple<double, string>(Planetarium.GetUniversalTime(), message)); } }
            public void Set(string message) { if (BDArmorySettings.DISPLAY_COMPETITION_STATUS) { status.Clear(); Add(message); } }
            public override string ToString()
            {
                var now = Planetarium.GetUniversalTime();
                status = status.Where(s => now - s.Item1 < 5).ToList(); // Update the list of status messages. Only show messages for 5s.
                return string.Join("\n", status.Select(s => s.Item2)); // Join them together to display them.
            }
            public int Count { get { return status.Count; } }
            public string lastActiveVessel = "";
        }

        public CompetitionStatus competitionStatus = new CompetitionStatus();

        bool startCompetitionNow = false;
        Coroutine startCompetitionNowCoroutine;
        public void StartCompetitionNow(float delay = 0)
        {
            startCompetitionNowCoroutine = StartCoroutine(StartCompetitionNowCoroutine(delay));
        }
        IEnumerator StartCompetitionNowCoroutine(float delay = 0) // Skip the "Competition: Waiting for teams to get in position."
        {
            yield return new WaitForSeconds(delay);
            if (competitionStarting)
            {
                competitionStatus.Add("No longer waiting for teams to get in position.");
                startCompetitionNow = true;
            }
        }

        public void StartCompetitionMode(float distance)
        {
            if (!competitionStarting)
            {
                ResetCompetitionStuff();
                Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Starting Competition");
                startCompetitionNow = false;
                if (BDArmorySettings.GRAVITY_HACKS)
                {
                    lastGravityMultiplier = 1f;
                    gravityMultiplier = 1f;
                    PhysicsGlobals.GraviticForceMultiplier = (double)gravityMultiplier;
                    VehiclePhysics.Gravity.Refresh();
                }
                RemoveDebrisNow();
                GameEvents.onVesselPartCountChanged.Add(OnVesselModified);
                GameEvents.onVesselCreate.Add(OnVesselModified);
                GameEvents.onCrewOnEva.Add(OnCrewOnEVA);
                if (BDArmorySettings.AUTO_ENABLE_VESSEL_SWITCHING)
                    LoadedVesselSwitcher.Instance.EnableAutoVesselSwitching(true);
                competitionStartFailureReason = CompetitionStartFailureReason.None;
                competitionRoutine = StartCoroutine(DogfightCompetitionModeRoutine(distance));
                if (BDArmorySettings.COMPETITION_START_NOW_AFTER < 11)
                {
                    if (BDArmorySettings.COMPETITION_START_NOW_AFTER > 5)
                        StartCompetitionNow((BDArmorySettings.COMPETITION_START_NOW_AFTER - 5) * 60);
                    else
                        StartCompetitionNow(BDArmorySettings.COMPETITION_START_NOW_AFTER * 10);
                }
                if (KerbalSafetyManager.Instance.safetyLevel != KerbalSafetyLevel.Off)
                    KerbalSafetyManager.Instance.CheckAllVesselsForKerbals();
                if (BDArmorySettings.TRACE_VESSELS_DURING_COMPETITIONS)
                    LoadedVesselSwitcher.Instance.StartVesselTracing();
                if (BDArmorySettings.TIME_OVERRIDE && BDArmorySettings.TIME_SCALE != 0)
                { Time.timeScale = BDArmorySettings.TIME_SCALE; }
            }
        }

        public void StopCompetition()
        {
            LogResults();
            if (competitionIsActive && ContinuousSpawning.Instance.vesselsSpawningContinuously)
            {
                SpawnUtils.CancelSpawning();
            }
            if (competitionRoutine != null)
            {
                StopCoroutine(competitionRoutine);
            }
            if (startCompetitionNowCoroutine != null)
            {
                StopCoroutine(startCompetitionNowCoroutine);
            }

            competitionStarting = false;
            competitionIsActive = false;
            sequencedCompetitionStarting = false;
            competitionStartTime = -1;
            competitionType = CompetitionType.FFA;
            if (PhysicsGlobals.GraviticForceMultiplier != 1)
            {
                lastGravityMultiplier = 1f;
                gravityMultiplier = 1f;
                PhysicsGlobals.GraviticForceMultiplier = (double)gravityMultiplier;
                VehiclePhysics.Gravity.Refresh();
            }
            GameEvents.onCollision.Remove(AnalyseCollision);
            GameEvents.onVesselPartCountChanged.Remove(OnVesselModified);
            GameEvents.onVesselCreate.Remove(OnVesselModified);
            GameEvents.onCrewOnEva.Remove(OnCrewOnEVA);
            GameEvents.onVesselCreate.Remove(DebrisDelayedCleanUp);
            GameEvents.onCometSpawned.Remove(RemoveCometVessel);
            rammingInformation = null; // Reset the ramming information.
            deadOrAlive = "";
            if (BDArmorySettings.TRACE_VESSELS_DURING_COMPETITIONS)
                LoadedVesselSwitcher.Instance.StopVesselTracing();
            if (BDArmorySettings.TIME_OVERRIDE)
            { Time.timeScale = 1f; }
        }

        void CompetitionStarted()
        {
            competitionIsActive = true; //start logging ramming now that the competition has officially started
            competitionStarting = false;
            sequencedCompetitionStarting = false;
            GameEvents.onCollision.Add(AnalyseCollision); // Start collision detection
            GameEvents.onVesselCreate.Add(DebrisDelayedCleanUp);
            DisableCometSpawning();
            GameEvents.onCometSpawned.Add(RemoveCometVessel);
            competitionStartTime = Planetarium.GetUniversalTime();
            nextUpdateTick = competitionStartTime + 2; // 2 seconds before we start tracking
            decisionTick = BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY > 60 ? -1 : competitionStartTime + BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY; // every 60 seconds we do nasty things
            finalGracePeriodStart = -1;
            Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Competition Started");
        }

        public void ResetCompetitionStuff()
        {
            // reinitilize everything when the button get hit.
            CompetitionID = (int)DateTime.UtcNow.Subtract(new DateTime(2020, 1, 1)).TotalSeconds;
            VesselModuleRegistry.CleanRegistries();
            DoPreflightChecks();
            KillTimer.Clear();
            nonCompetitorsToRemove.Clear();
            explodingWM.Clear();
            pilotActions.Clear(); // Clear the pilotActions, so we don't get "<pilot> is Dead" on the next round of the competition.
            rammingInformation = null; // Reset the ramming information.
            if (BDArmorySettings.ASTEROID_FIELD) { AsteroidField.Instance.Reset(); RemoveDebrisNow(); }
            if (BDArmorySettings.ASTEROID_RAIN) { AsteroidRain.Instance.Reset(); RemoveDebrisNow(); }
            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41) BDArmorySettings.FIRE_RATE_OVERRIDE = BDArmorySettings.FIRE_RATE_OVERRIDE_CENTER;
            finalGracePeriodStart = -1;
            competitionPreStartTime = Planetarium.GetUniversalTime();
            competitionStartTime = competitionIsActive ? Planetarium.GetUniversalTime() : -1;
            nextUpdateTick = competitionStartTime + 2; // 2 seconds before we start tracking
            decisionTick = BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY > 60 ? -1 : competitionStartTime + BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY; // every 60 seconds we do nasty things
            FX.BulletHitFX.CleanPartsOnFireInfo();
            Scores.ConfigurePlayers(GetAllPilots().Select(p => p.vessel).ToList()); // Get the competitors.
            if (SpawnUtils.originalTeams.Count == 0) SpawnUtils.SaveTeams(); // If the vessels weren't spawned in with Vessel Spawner, save the current teams.
        }

        IEnumerator DogfightCompetitionModeRoutine(float distance)
        {
            competitionStarting = true;
            competitionType = CompetitionType.FFA;
            startTag = true; // Tag entry condition, should be true even if tag is not currently enabled, so if tag is enabled later in the competition it will function
            competitionStatus.Add("Competition: Pilots are taking off.");
            var pilots = new Dictionary<BDTeam, List<IBDAIControl>>();
            HashSet<IBDAIControl> readyToLaunch = new HashSet<IBDAIControl>();
            using (var loadedVessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedVessels.MoveNext())
                {
                    if (loadedVessels.Current == null || !loadedVessels.Current.loaded || VesselModuleRegistry.ignoredVesselTypes.Contains(loadedVessels.Current.vesselType))
                        continue;
                    IBDAIControl pilot = VesselModuleRegistry.GetModule<IBDAIControl>(loadedVessels.Current);
                    if (pilot == null || !pilot.weaponManager || pilot.weaponManager.Team.Neutral)
                        continue;

                    if (!pilots.TryGetValue(pilot.weaponManager.Team, out List<IBDAIControl> teamPilots))
                    {
                        teamPilots = new List<IBDAIControl>();
                        pilots.Add(pilot.weaponManager.Team, teamPilots);
                        if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Adding Team " + pilot.weaponManager.Team.Name);
                    }
                    teamPilots.Add(pilot);
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Adding Pilot " + pilot.vessel.GetName());
                    readyToLaunch.Add(pilot);
                }

            if (BDArmorySettings.MUTATOR_MODE && BDArmorySettings.MUTATOR_LIST.Count > 0)
            {
                ConfigureMutator();
            }
            foreach (var pilot in readyToLaunch)
            {
                pilot.vessel.ActionGroups.ToggleGroup(KM_dictAG[10]); // Modular Missiles use lower AGs (1-3) for staging, use a high AG number to not affect them
                pilot.ActivatePilot();
                pilot.CommandTakeOff();
                if (pilot.weaponManager.guardMode)
                {
                    pilot.weaponManager.ToggleGuardMode();
                    pilot.weaponManager.SetTarget(null);
                }
                if (!BDArmorySettings.NO_ENGINES && SpawnUtils.CountActiveEngines(pilot.vessel) == 0) // Find vessels that didn't activate their engines on AG10 and fire their next stage.
                {
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: " + pilot.vessel.vesselName + " didn't activate engines on AG10! Activating ALL their engines.");
                    SpawnUtils.ActivateAllEngines(pilot.vessel);
                }
                else if (BDArmorySettings.NO_ENGINES && SpawnUtils.CountActiveEngines(pilot.vessel) > 0) // Shutdown engines
                {
                    SpawnUtils.ActivateAllEngines(pilot.vessel, false);
                }
                if (BDArmorySettings.MUTATOR_MODE && BDArmorySettings.MUTATOR_LIST.Count > 0)
                {
                    var MM = pilot.vessel.rootPart.FindModuleImplementing<BDAMutator>();
                    if (MM == null)
                    {
                        MM = (BDAMutator)pilot.vessel.rootPart.AddModule("BDAMutator");
                    }
                    if (BDArmorySettings.MUTATOR_APPLY_GLOBAL) //selected mutator applied globally
                    {
                        MM.EnableMutator(currentMutator);
                    }
                    if (BDArmorySettings.MUTATOR_APPLY_TIMER && !BDArmorySettings.MUTATOR_APPLY_GLOBAL) //mutator applied on a per-craft basis
                    {
                        MM.EnableMutator(); //random mutator
                    }
                }
                if (BDArmorySettings.ENABLE_HOS && BDArmorySettings.HALL_OF_SHAME_LIST.Count > 0)
                {
                    if (BDArmorySettings.HALL_OF_SHAME_LIST.Contains(pilot.vessel.GetName()))
                    {
                        using (List<Part>.Enumerator part = pilot.vessel.Parts.GetEnumerator())
                            while (part.MoveNext())
                            {
                                if (BDArmorySettings.HOS_FIRE > 0.1f)
                                {
                                    BulletHitFX.AttachFire(part.Current.transform.position, part.Current, BDArmorySettings.HOS_FIRE * 50, "GM", BDArmorySettings.COMPETITION_DURATION * 60, 1, true);
                                }
                                if (BDArmorySettings.HOS_MASS != 0)
                                {
                                    var MM = part.Current.FindModuleImplementing<ModuleMassAdjust>();
                                    if (MM == null)
                                    {
                                        MM = (ModuleMassAdjust)part.Current.AddModule("ModuleMassAdjust");
                                    }
                                    MM.duration = BDArmorySettings.COMPETITION_DURATION * 60;
                                    MM.massMod += (float)(BDArmorySettings.HOS_MASS / pilot.vessel.Parts.Count); //evenly distribute mass change across entire vessel
                                }
                                if (BDArmorySettings.HOS_DMG != 1)
                                {
                                    var HPT = part.Current.FindModuleImplementing<HitpointTracker>();
                                    HPT.defenseMutator = (float)(1 / BDArmorySettings.HOS_DMG);
                                }
                                if (BDArmorySettings.HOS_THRUST != 100)
                                {
                                    using (var engine = VesselModuleRegistry.GetModuleEngines(pilot.vessel).GetEnumerator())
                                        while (engine.MoveNext())
                                        {
                                            engine.Current.thrustPercentage = BDArmorySettings.HOS_THRUST;
                                        }
                                }
                            }
                    }
                }
                if (BDArmorySettings.HACK_INTAKES)
                {
                    SpawnUtils.HackIntakes(pilot.vessel, true);
                }
            }

            //clear target database so pilots don't attack yet
            BDATargetManager.ClearDatabase();
            CleanUpKSPsDeadReferences();
            RunDebugChecks();

            if (pilots.Count < 2)
            {
                Debug.Log("[BDArmory.BDACompetitionMode" + CompetitionID.ToString() + "]: Unable to start competition mode - one or more teams is empty");
                competitionStatus.Set("Competition: Failed!  One or more teams is empty.");
                competitionStartFailureReason = CompetitionStartFailureReason.OnlyOneTeam;
                StopCompetition();
                yield break;
            }

            var leaders = new List<IBDAIControl>();
            using (var pilotList = pilots.GetEnumerator())
                while (pilotList.MoveNext())
                {
                    if (pilotList.Current.Value == null)
                    {
                        var message = "Teams got adjusted during competition start-up, aborting.";
                        competitionStatus.Set("Competition: " + message);
                        Debug.Log("[BDArmory.BDACompetitionMode]: " + message);
                        competitionStartFailureReason = CompetitionStartFailureReason.OnlyOneTeam;
                        StopCompetition();
                        yield break;
                    }
                    leaders.Add(pilotList.Current.Value[0]);
                }
            var leaderNames = leaders.Select(l => l.vessel.vesselName).ToList();
            while (leaders.Any(leader => leader == null || leader.weaponManager == null || leader.weaponManager.wingCommander == null || leader.weaponManager.wingCommander.weaponManager == null))
            {
                yield return new WaitForFixedUpdate();
                if (leaders.Any(leader => leader == null || leader.weaponManager == null))
                {
                    var survivingLeaders = leaders.Where(l => l != null && l.weaponManager != null).Select(l => l.vessel.vesselName).ToList();
                    var missingLeaders = leaderNames.Where(l => !survivingLeaders.Contains(l)).ToList();
                    var message = "A team leader disappeared during competition start-up, aborting: " + string.Join(", ", missingLeaders);
                    competitionStatus.Set("Competition: " + message);
                    Debug.Log("[BDArmory.BDACompetitionMode]: 1. " + message);
                    competitionStartFailureReason = CompetitionStartFailureReason.TeamLeaderDisappeared;
                    StopCompetition();
                    yield break;
                }
            }
            foreach (var leader in leaders)
                leader.weaponManager.wingCommander.CommandAllFollow();

            //wait till the leaders are ready to engage (airborne for PilotAI)
            while (true)
            {
                if (leaders.Any(leader => leader == null || leader.weaponManager == null))
                {
                    var survivingLeaders = leaders.Where(l => l != null && l.weaponManager != null).Select(l => l.vessel.vesselName).ToList();
                    var missingLeaders = leaderNames.Where(l => !survivingLeaders.Contains(l)).ToList();
                    var message = "A team leader disappeared during competition start-up, aborting: " + string.Join(", ", missingLeaders);
                    competitionStatus.Set("Competition: " + message);
                    Debug.Log("[BDArmory.BDACompetitionMode]: 2. " + message);
                    competitionStartFailureReason = CompetitionStartFailureReason.TeamLeaderDisappeared;
                    StopCompetition();
                    yield break;
                }
                if (leaders.All(leader => leader.CanEngage()))
                {
                    break;
                }
                yield return new WaitForSeconds(1);
            }

            if (BDArmorySettings.ASTEROID_FIELD) { AsteroidField.Instance.SpawnField(BDArmorySettings.ASTEROID_FIELD_NUMBER, BDArmorySettings.ASTEROID_FIELD_ALTITUDE, BDArmorySettings.ASTEROID_FIELD_RADIUS, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS); }
            if (BDArmorySettings.ASTEROID_RAIN) { AsteroidRain.Instance.SpawnRain(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS); }

            competitionStatus.Add("Competition: Sending pilots to start position.");
            Vector3 center = Vector3.zero;
            using (var leader = leaders.GetEnumerator())
                while (leader.MoveNext())
                    center += leader.Current.vessel.CoM;
            center /= leaders.Count;
            Vector3 startDirection = Vector3.ProjectOnPlane(leaders[0].vessel.CoM - center, VectorUtils.GetUpDirection(center)).normalized;
            startDirection *= (distance * leaders.Count / 4) + 1250f;
            Quaternion directionStep = Quaternion.AngleAxis(360f / leaders.Count, VectorUtils.GetUpDirection(center));

            for (var i = 0; i < leaders.Count; ++i)
            {
                var pilotAI = VesselModuleRegistry.GetBDModulePilotAI(leaders[i].vessel, true); // Adjust initial fly-to point for terrain and default altitudes.
                var startPosition = center + startDirection + (pilotAI != null ? (pilotAI.defaultAltitude - BodyUtils.GetRadarAltitudeAtPos(center + startDirection, false)) * VectorUtils.GetUpDirection(center + startDirection) : Vector3.zero);
                leaders[i].CommandFlyTo(VectorUtils.WorldPositionToGeoCoords(startPosition, FlightGlobals.currentMainBody));
                startDirection = directionStep * startDirection;
            }

            Vector3 centerGPS = VectorUtils.WorldPositionToGeoCoords(center, FlightGlobals.currentMainBody);

            //wait till everyone is in position
            competitionStatus.Add("Competition: Waiting for teams to get in position.");
            bool waiting = true;
            var sqrDistance = distance * distance;
            while (waiting && !startCompetitionNow)
            {
                waiting = false;

                if (leaders.Any(leader => leader == null || leader.weaponManager == null))
                {
                    var survivingLeaders = leaders.Where(l => l != null && l.weaponManager != null).Select(l => l.vessel.vesselName).ToList();
                    var missingLeaders = leaderNames.Where(l => !survivingLeaders.Contains(l)).ToList();
                    var message = "A team leader disappeared during competition start-up, aborting: " + string.Join(", ", missingLeaders);
                    competitionStatus.Set("Competition: " + message);
                    Debug.Log("[BDArmory.BDACompetitionMode]: 3. " + message);
                    competitionStartFailureReason = CompetitionStartFailureReason.TeamLeaderDisappeared;
                    StopCompetition();
                    yield break;
                }

                foreach (var leader in leaders)
                {
                    foreach (var otherLeader in leaders)
                    {
                        if (leader == otherLeader)
                            continue;
                        try // Somehow, if a vessel gets destroyed during competition start, the following can throw a null reference exception despite checking for nulls! This is due to the IBDAIControl.transform getter. How to fix this?
                        {
                            if ((leader.transform.position - otherLeader.transform.position).sqrMagnitude < sqrDistance)
                                waiting = true;
                        }
                        catch (Exception e)
                        {
                            var message = "A team leader has disappeared during competition start-up, aborting.";
                            Debug.LogWarning("[BDArmory.BDACompetitionMode]: Exception thrown in DogfightCompetitionModeRoutine: " + e.Message + "\n" + e.StackTrace);
                            try
                            {
                                var survivingLeaders = leaders.Where(l => l != null && l.weaponManager != null).Select(l => l.vessel.vesselName).ToList();
                                var missingLeaders = leaderNames.Where(l => !survivingLeaders.Contains(l)).ToList();
                                message = "A team leader disappeared during competition start-up, aborting: " + string.Join(", ", missingLeaders);
                            }
                            catch (Exception e2) { Debug.LogWarning($"[BDArmory.BDACompetitionMode]: Exception gathering missing leader names:" + e2.Message); }
                            competitionStatus.Set(message);
                            Debug.Log("[BDArmory.BDACompetitionMode]: 4. " + message);
                            competitionStartFailureReason = CompetitionStartFailureReason.TeamLeaderDisappeared;
                            StopCompetition();
                            yield break;
                        }
                    }

                    // Increase the distance for large teams
                    if (!pilots.ContainsKey(leader.weaponManager.Team))
                    {
                        var message = "The teams were changed during competition start-up, aborting.";
                        competitionStatus.Set("Competition: " + message);
                        Debug.Log("[BDArmory.BDACompetitionMode]: " + message);
                        competitionStartFailureReason = CompetitionStartFailureReason.TeamsChanged;
                        StopCompetition();
                        yield break;
                    }
                    var teamDistance = 800f + 100f * pilots[leader.weaponManager.Team].Count;
                    foreach (var pilot in pilots[leader.weaponManager.Team])
                        if (pilot != null
                                && pilot.currentCommand == PilotCommands.Follow
                                && (pilot.vessel.CoM - pilot.commandLeader.vessel.CoM).sqrMagnitude > teamDistance * teamDistance)
                            waiting = true;

                    if (waiting) break;
                }

                yield return null;
            }
            previousNumberCompetitive = 2; // For entering into tag mode

            //start the match
            foreach (var teamPilots in pilots.Values)
            {
                if (teamPilots == null)
                {
                    var message = "Teams have been changed during competition start-up, aborting.";
                    competitionStatus.Set("Competition: " + message);
                    Debug.Log("[BDArmory.BDACompetitionMode]: " + message);
                    competitionStartFailureReason = CompetitionStartFailureReason.TeamsChanged;
                    StopCompetition();
                    yield break;
                }
                foreach (var pilot in teamPilots)
                    if (pilot == null)
                    {
                        var message = "A pilot has disappeared from team during competition start-up, aborting.";
                        competitionStatus.Set("Competition: " + message);
                        Debug.Log("[BDArmory.BDACompetitionMode]: " + message);
                        competitionStartFailureReason = CompetitionStartFailureReason.PilotDisappeared;
                        StopCompetition(); // Check that the team pilots haven't been changed during the competition startup.
                        yield break;
                    }
            }
            if (BDATargetManager.LoadedVessels.Where(v => !VesselModuleRegistry.ignoredVesselTypes.Contains(v.vesselType)).Any(v => VesselModuleRegistry.GetModuleCount<ModuleRadar>(v) > 0)) // Update RCS if any vessels have radars.
            {
                try
                {
                    RadarUtils.ForceUpdateRadarCrossSections();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BDArmory.BDACompetitionMode]: Exception thrown in DogfightCompetitionModeRoutine: " + e.Message);
                    competitionStatus.Set("Failed to update radar cross sections, aborting.");
                    competitionStartFailureReason = CompetitionStartFailureReason.Other;
                    StopCompetition();
                    yield break;
                }
            }
            foreach (var teamPilots in pilots)
                foreach (var pilot in teamPilots.Value)
                {
                    if (pilot == null) continue;

                    if (!pilot.weaponManager.guardMode)
                        pilot.weaponManager.ToggleGuardMode();

                    //foreach (var leader in leaders)
                        //BDATargetManager.ReportVessel(pilot.vessel, leader.weaponManager);

                    pilot.ReleaseCommand();
                    pilot.CommandAttack(centerGPS);
                    pilot.vessel.altimeterDisplayState = AltimeterDisplayState.AGL;
                }

            competitionStatus.Add("Competition starting!  Good luck!");
            CompetitionStarted();
        }
        #endregion

        HashSet<string> uniqueVesselNames = new HashSet<string>();
        public List<IBDAIControl> GetAllPilots()
        {
            var pilots = new List<IBDAIControl>();
            uniqueVesselNames.Clear();
            foreach (var vessel in BDATargetManager.LoadedVessels)
            {
                if (vessel == null || !vessel.loaded || VesselModuleRegistry.ignoredVesselTypes.Contains(vessel.vesselType)) continue;
                var pilot = VesselModuleRegistry.GetModule<IBDAIControl>(vessel);
                if (pilot == null || pilot.weaponManager == null)
                {
                    VesselModuleRegistry.OnVesselModified(vessel, true);
                    pilot = VesselModuleRegistry.GetModule<IBDAIControl>(vessel);
                    if (pilot == null || pilot.weaponManager == null) continue; // Unfixable, ignore the vessel.
                }
                if (IsValidVessel(vessel) != InvalidVesselReason.None) continue;
                if (pilot.weaponManager.Team.Neutral) continue; // Ignore the neutrals.
                pilots.Add(pilot);
                if (uniqueVesselNames.Contains(vessel.vesselName))
                {
                    var count = 1;
                    var potentialName = vessel.vesselName + "_" + count;
                    while (uniqueVesselNames.Contains(potentialName))
                        potentialName = vessel.vesselName + "_" + (++count);
                    vessel.vesselName = potentialName;
                }
                uniqueVesselNames.Add(vessel.vesselName);
            }
            return pilots;
        }

        public string currentMutator;

        public void ConfigureMutator()
        {
            currentMutator = string.Empty;

            if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: MutatorMode enabled; Mutator count = " + BDArmorySettings.MUTATOR_LIST.Count);
            var indices = Enumerable.Range(0, BDArmorySettings.MUTATOR_LIST.Count).ToList();
            indices.Shuffle();
            currentMutator = string.Join("; ", indices.Take(BDArmorySettings.MUTATOR_APPLY_NUM).Select(i => MutatorInfo.mutators[BDArmorySettings.MUTATOR_LIST[i]].name));

            if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: current mutators: " + currentMutator);
            MutatorResetTime = Planetarium.GetUniversalTime();
            if (BDArmorySettings.MUTATOR_APPLY_GLOBAL) //selected mutator applied globally
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#LOC_BDArmory_UI_MutatorStart") + ": " + currentMutator + ". " + (BDArmorySettings.MUTATOR_APPLY_TIMER ? (BDArmorySettings.MUTATOR_DURATION > 0 ? BDArmorySettings.MUTATOR_DURATION * 60 : BDArmorySettings.COMPETITION_DURATION * 60) + " seconds left" : ""), 5, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        #region Vessel validity
        public enum InvalidVesselReason { None, NullVessel, NoAI, NoWeaponManager, NoCommand };
        public InvalidVesselReason IsValidVessel(Vessel vessel, bool attemptFix = true)
        {
            if (vessel == null)
                return InvalidVesselReason.NullVessel;
            if (VesselModuleRegistry.GetModuleCount<IBDAIControl>(vessel) == 0) // Check for an AI.
                return InvalidVesselReason.NoAI;
            if (VesselModuleRegistry.GetModuleCount<MissileFire>(vessel) == 0) // Check for a weapon manager.
                return InvalidVesselReason.NoWeaponManager;
            if (attemptFix && VesselModuleRegistry.GetModuleCount<ModuleCommand>(vessel) == 0 && VesselModuleRegistry.GetModuleCount<KerbalSeat>(vessel) == 0) // Check for a cockpit or command seat.
                CheckVesselType(vessel); // Attempt to fix it.
            if (VesselModuleRegistry.GetModuleCount<ModuleCommand>(vessel) == 0 && VesselModuleRegistry.GetModuleCount<KerbalSeat>(vessel) == 0) // Check for a cockpit or command seat again.
                return InvalidVesselReason.NoCommand;
            return InvalidVesselReason.None;
        }

        void OnCrewOnEVA(GameEvents.FromToAction<Part, Part> fromToAction)
        {
            if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.BDACompetitionMode]: {fromToAction.to} went on EVA from {fromToAction.from}");
            if (fromToAction.from.vessel != null)
            {
                OnVesselModified(fromToAction.from.vessel);
            }
        }

        public void OnVesselModified(Vessel vessel)
        {
            if (vessel == null) return;
            VesselModuleRegistry.OnVesselModified(vessel);
            CheckVesselType(vessel);
            if (VesselModuleRegistry.ignoredVesselTypes.Contains(vessel.vesselType)) return;
            if (!BDArmorySettings.AUTONOMOUS_COMBAT_SEATS) CheckForAutonomousCombatSeat(vessel);
            if (BDArmorySettings.DESTROY_UNCONTROLLED_WMS) CheckForUncontrolledVessel(vessel);
        }

        HashSet<VesselType> validVesselTypes = new HashSet<VesselType> { VesselType.Plane, VesselType.Ship };
        public void CheckVesselType(Vessel vessel)
        {
            if (!BDArmorySettings.RUNWAY_PROJECT) return;
            if (vessel != null && vessel.vesselName != null)
            {
                var vesselTypeIsValid = validVesselTypes.Contains(vessel.vesselType);
                var hasMissileFire = VesselModuleRegistry.GetModuleCount<MissileFire>(vessel) > 0;
                if (!vesselTypeIsValid && hasMissileFire) // Found an invalid vessel type with a weapon manager.
                {
                    var message = "Found weapon manager on " + vessel.vesselName + " of type " + vessel.vesselType;
                    if (vessel.vesselName.EndsWith(" " + vessel.vesselType.ToString()))
                        vessel.vesselName = vessel.vesselName.Remove(vessel.vesselName.Length - vessel.vesselType.ToString().Length - 1);
                    vessel.vesselType = VesselType.Plane;
                    message += ", changing vessel name and type to " + vessel.vesselName + ", " + vessel.vesselType;
                    Debug.Log("[BDArmory.BDACompetitionMode]: " + message);
                    return;
                }
                if (vesselTypeIsValid && vessel.vesselType == VesselType.Plane && vessel.vesselName.EndsWith(" Plane") && !Scores.Players.Contains(vessel.vesselName) && Scores.Players.Contains(vessel.vesselName.Remove(vessel.vesselName.Length - 6)) && IsValidVessel(vessel, false) == InvalidVesselReason.None)
                {
                    var message = "Found a valid vessel (" + vessel.vesselName + ") tagged with 'Plane' when it shouldn't be, renaming.";
                    Debug.Log("[BDArmory.BDACompetitionMode]: " + message);
                    vessel.vesselName = vessel.vesselName.Remove(vessel.vesselName.Length - 6);
                    return;
                }
            }
        }

        public void CheckForAutonomousCombatSeat(Vessel vessel)
        {
            if (vessel == null) return;
            if (VesselModuleRegistry.GetModuleCount<KerbalSeat>(vessel) > 0)
            {
                if (vessel.parts.Count == 1) // Check for a falling combat seat.
                {
                    Debug.Log("[BDArmory.BDACompetitionMode]: Found a lone combat seat, killing it.");
                    PartExploderSystem.AddPartToExplode(vessel.parts[0]);
                    return;
                }
                // Check for a lack of control.
                var AI = VesselModuleRegistry.GetModule<BDModulePilotAI>(vessel);
                if (VesselModuleRegistry.GetModuleCount<KerbalEVA>(vessel) == 0 && AI != null && AI.pilotEnabled) // If not controlled by a kerbalEVA in a KerbalSeat, check the regular ModuleCommand parts.
                {
                    if (VesselModuleRegistry.GetModules<ModuleCommand>(vessel).All(c => c.GetControlSourceState() == CommNet.VesselControlState.None))
                    {
                        Debug.Log("[BDArmory.BDACompetitionMode]: Kerbal has left the seat of " + vessel.vesselName + " and it has no other controls, disabling the AI.");
                        AI.DeactivatePilot();
                    }
                }
            }
        }

        void CheckForUncontrolledVessel(Vessel vessel)
        {
            if (vessel == null || vessel.vesselName == null) return;
            if (VesselModuleRegistry.GetModuleCount<MissileFire>(vessel) == 0) return; // The weapon managers are already dead.

            // Check for partial or full control state.
            foreach (var moduleCommand in VesselModuleRegistry.GetModuleCommands(vessel)) { moduleCommand.UpdateNetwork(); }
            foreach (var kerbalSeat in VesselModuleRegistry.GetKerbalSeats(vessel)) { kerbalSeat.UpdateNetwork(); }
            // Check for any command modules with partial or full control state
            if (!VesselModuleRegistry.GetModuleCommands(vessel).Any(c => (c.UpdateControlSourceState() & (CommNet.VesselControlState.Partial | CommNet.VesselControlState.Full)) > CommNet.VesselControlState.None)
                && !VesselModuleRegistry.GetKerbalSeats(vessel).Any(c => (c.GetControlSourceState() & (CommNet.VesselControlState.Partial | CommNet.VesselControlState.Full)) > CommNet.VesselControlState.None))
            {
                StartCoroutine(DelayedExplodeWMs(vessel, 5f, UncontrolledReason.Uncontrolled)); // Uncontrolled vessel, destroy its weapon manager in 5s.
            }
            var craftbricked = VesselModuleRegistry.GetModule<ModuleDrainEC>(vessel);
            if (craftbricked != null && craftbricked.bricked)
            {
                StartCoroutine(DelayedExplodeWMs(vessel, 2f, UncontrolledReason.Bricked)); // Vessel fried by EMP, destroy its weapon manager in 2s.
            }
        }

        enum UncontrolledReason { Uncontrolled, Bricked };
        HashSet<Vessel> explodingWM = new HashSet<Vessel>();
        IEnumerator DelayedExplodeWMs(Vessel vessel, float delay = 1f, UncontrolledReason reason = UncontrolledReason.Uncontrolled)
        {
            if (explodingWM.Contains(vessel)) yield break; // Already scheduled for exploding.
            explodingWM.Add(vessel);
            yield return new WaitForSeconds(delay);
            if (vessel == null) // It's already dead.
            {
                explodingWM = explodingWM.Where(v => v != null).ToHashSet(); // Clean the hashset.
                yield break;
            }
            bool stillValid = true;
            switch (reason) // Check that the reason for killing the WMs is still valid.
            {
                case UncontrolledReason.Uncontrolled:
                    if (VesselModuleRegistry.GetModuleCommands(vessel).Any(c => (c.UpdateControlSourceState() & (CommNet.VesselControlState.Partial | CommNet.VesselControlState.Full)) > CommNet.VesselControlState.None)
                        || VesselModuleRegistry.GetKerbalSeats(vessel).Any(c => (c.GetControlSourceState() & (CommNet.VesselControlState.Partial | CommNet.VesselControlState.Full)) > CommNet.VesselControlState.None)) // No longer uncontrolled.
                    {
                        stillValid = false;
                    }
                    break;
                case UncontrolledReason.Bricked: // A craft can't recover from being bricked.
                    break;
            }
            if (stillValid)
            {
                // Kill off all the weapon managers.
                Debug.Log("[BDArmory.BDACompetitionMode]: " + vessel.vesselName + " has no form of control, killing the weapon managers.");
                foreach (var weaponManager in VesselModuleRegistry.GetMissileFires(vessel))
                { PartExploderSystem.AddPartToExplode(weaponManager.part); }
            }
            explodingWM.Remove(vessel);
        }

        void CheckForBadlyNamedVessels()
        {
            foreach (var wm in LoadedVesselSwitcher.Instance.WeaponManagers.SelectMany(tm => tm.Value).Where(wm => wm != null).ToList())
                if (wm != null && wm.vessel != null && wm.vessel.vesselName != null)
                {
                    if (wm.vessel.vesselType == VesselType.Plane && wm.vessel.vesselName.EndsWith(" Plane") && !Scores.Players.Contains(wm.vessel.vesselName) && Scores.Players.Contains(wm.vessel.vesselName.Remove(wm.vessel.vesselName.Length - 6)) && IsValidVessel(wm.vessel) == InvalidVesselReason.None)
                    {
                        var message = "Found a valid vessel (" + wm.vessel.vesselName + ") tagged with 'Plane' when it shouldn't be, renaming.";
                        Debug.Log("[BDArmory.BDACompetitionMode]: " + message);
                        wm.vessel.vesselName = wm.vessel.vesselName.Remove(wm.vessel.vesselName.Length - 6);
                    }
                }
        }
        #endregion

        #region Runway Project
        public bool killerGMenabled = false;
        public bool pinataAlive = false;
        public bool s4r1FiringRateUpdatedFromShotThisFrame = false;
        public bool s4r1FiringRateUpdatedFromHitThisFrame = false;

        public void StartRapidDeployment(float distance)
        {
            if (!BDArmorySettings.RUNWAY_PROJECT) return;
            if (!sequencedCompetitionStarting)
            {
                ResetCompetitionStuff();
                Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Starting Rapid Deployment ");
                RemoveDebrisNow();
                GameEvents.onVesselPartCountChanged.Add(OnVesselModified);
                GameEvents.onVesselCreate.Add(OnVesselModified);
                if (BDArmorySettings.AUTO_ENABLE_VESSEL_SWITCHING)
                    LoadedVesselSwitcher.Instance.EnableAutoVesselSwitching(true);
                if (KerbalSafetyManager.Instance.safetyLevel != KerbalSafetyLevel.Off)
                    KerbalSafetyManager.Instance.CheckAllVesselsForKerbals();
                List<string> commandSequence;
                switch (BDArmorySettings.RUNWAY_PROJECT_ROUND)
                {
                    case 33:
                        commandSequence = new List<string>{
                            "0:MassTrim", // t=0, mass trim
                            "0:ActionGroup:14:0", // t=0, Disable brakes
                            "0:ActionGroup:4", // t=0, AG4 - Launch: Activate base craft engine, retract airbrakes
                            "0:ActionGroup:13:1", // t=0, AG4 - Enable SAS
                            "0:SetThrottle:100", // t=0, Full throttle
                            "35:ActionGroup:1", // t=35, AG1 - Engine shutdown, extend airbrakes
                            "10:ActionGroup:2", // t=45, AG2 - Deploy fairing
                            "3:RemoveFairings", // t=48, Remove fairings from the game
                            "0:ActionGroup:3", // t=48, AG3 - Decouple base craft (-> add your custom engine activations and timers here <-)
                            "0:ActionGroup:12:1", // t=48, Enable RCS
                            "0:ActivateEngines", // t=48, Activate engines (if they're not activated by AG3)
                            "1:TogglePilot:1", // t=49, Activate pilots
                            "0:ActionGroup:16:0", // t=55, Retract gear (if it's not retracted)
                            "6:ToggleGuard:1", // t=55, Activate guard mode (attack)
                            "5:RemoveDebris", // t=60, Remove any other debris and spectators
                            // "0:EnableGM", // t=60, Activate the killer GM
                        };
                        break;
                    case 44: //S4R4 Eve Seaplane spawn
                        commandSequence = new List<string>{
                            "0:ActionGroup:13:1", // t=0, AG4 - Enable SAS
                            "0:ActionGroup:10:1", // t=0, AG10
                            "0:TogglePilot:1", // t=0, Activate pilots
                            "0:ActivateEngines", // t=0, Activate engines
                            "0:ActionGroup:16:0", // t=0, Retract gear (if it's not retracted)
                            "0:ToggleGuard:0", // t=0, Disable guard mode (for those who triggered it early)
                            "24:HackGravity:0.9", // t=24, Lower gravity to 0.9x
                            "2:HackGravity:0.8", // t=26, Lower gravity to 0.8x
                            "2:HackGravity:0.7", // t=28, Lower gravity to 0.7x
                            "2:HackGravity:0.6", // t=30, Lower gravity to 0.6x
                            "2:HackGravity:0.5", // t=32, Lower gravity to 0.5x
                            "2:HackGravity:0.4", // t=34, Lower gravity to 0.4x
                            "2:HackGravity:0.3", // t=36, Lower gravity to 0.3x
                            "2:HackGravity:0.2", // t=38, Lower gravity to 0.2x
                            "2:HackGravity:0.1", // t=40, Lower gravity to 0.1x
                            "5:HackGravity:0.25", //t=45, Raise gravity to 0.25x
                            "5:HackGravity:0.5", //t=50, Raise gravity to 0.5x
                            "5:HackGravity:0.75", //t=55, Raise gravity to 0.75x
                            "5:HackGravity:1", //t=60, Reset gravity
                            "0:RemoveDebris", // t=60, Remove any other debris and spectators
                            "5:ToggleGuard:1", // t=65, Enable guard mode
                        };
                        break;
                    case 60: //change this later (orbital deployment)
                        commandSequence = new List<string>{
                            "0:ActionGroup:13:1", // t=0, AG4 - Enable SAS
                            "0:ActionGroup:16:0", // t=0, Retract gear (if it's not retracted)
                            "0:HackGravity:10", // t=0, Increase gravity to 10x
                            "30:HackGravity:1", //t=30, Reset gravity
                            "0:ActionGroup:10", // t=30, AG10
                            "0:ActivateEngines", // t=30, Activate engines
                            "0:SetThrottle:100", // t=30, Full throttle
                            "0:TogglePilot:1", // t=30, Activate pilots
                            "0:ToggleGuard:1", // t=30, Activate guard mode (attack)
                            "5:RemoveDebris", // t=35, Remove any other debris and spectators
                            // "0:EnableGM", // t=60, Activate the killer GM
                        };
                        break;
                    default: // Same as S3R3 for now, until we do something different.
                        commandSequence = new List<string>{
                            "0:MassTrim", // t=0, mass trim
                            "0:ActionGroup:14:0", // t=0, Disable brakes
                            "0:ActionGroup:4", // t=0, AG4 - Launch: Activate base craft engine, retract airbrakes
                            "0:ActionGroup:13:1", // t=0, AG4 - Enable SAS
                            "0:SetThrottle:100", // t=0, Full throttle
                            "35:ActionGroup:1", // t=35, AG1 - Engine shutdown, extend airbrakes
                            "10:ActionGroup:2", // t=45, AG2 - Deploy fairing
                            "3:RemoveFairings", // t=48, Remove fairings from the game
                            "0:ActionGroup:3", // t=48, AG3 - Decouple base craft (-> add your custom engine activations and timers here <-)
                            "0:ActionGroup:12:1", // t=48, Enable RCS
                            "0:ActivateEngines", // t=48, Activate engines (if they're not activated by AG3)
                            "1:TogglePilot:1", // t=49, Activate pilots
                            "0:ActionGroup:16:0", // t=55, Retract gear (if it's not retracted)
                            "6:ToggleGuard:1", // t=55, Activate guard mode (attack)
                            "5:RemoveDebris", // t=60, Remove any other debris and spectators
                            // "0:EnableGM", // t=60, Activate the killer GM
                        };
                        break;
                }
                competitionRoutine = StartCoroutine(SequencedCompetition(commandSequence));
            }
        }

        private void DoPreflightChecks()
        {
            if (BDArmorySettings.RUNWAY_PROJECT)
            {
                var pilots = GetAllPilots();
                foreach (var pilot in pilots)
                {
                    if (pilot.vessel == null) continue;

                    enforcePartCount(pilot.vessel);
                }
            }
        }
        // "JetEngine", "miniJetEngine", "turboFanEngine", "turboJet", "turboFanSize2", "RAPIER"
        static string[] allowedEngineList = { "JetEngine", "miniJetEngine", "turboFanEngine", "turboJet", "turboFanSize2", "RAPIER" };
        static HashSet<string> allowedEngines = new HashSet<string>(allowedEngineList);

        // allow duplicate landing gear
        static string[] allowedDuplicateList = { "GearLarge", "GearFixed", "GearFree", "GearMedium", "GearSmall", "SmallGearBay", "fuelLine", "strutConnector" };
        static HashSet<string> allowedLandingGear = new HashSet<string>(allowedDuplicateList);

        // don't allow "SaturnAL31"
        static string[] bannedPartList = { "SaturnAL31" };
        static HashSet<string> bannedParts = new HashSet<string>(bannedPartList);

        // ammo boxes
        static string[] ammoPartList = { "baha20mmAmmo", "baha30mmAmmo", "baha50CalAmmo", "BDAcUniversalAmmoBox", "UniversalAmmoBoxBDA" };
        static HashSet<string> ammoParts = new HashSet<string>(ammoPartList);

        public void enforcePartCount(Vessel vessel)
        {
            if (!BDArmorySettings.RUNWAY_PROJECT) return;
            switch (BDArmorySettings.RUNWAY_PROJECT_ROUND)
            {
                case 18:
                    break;
                default:
                    return;
            }
            using (List<Part>.Enumerator parts = vessel.parts.GetEnumerator())
            {
                Dictionary<string, int> partCounts = new Dictionary<string, int>();
                List<Part> partsToKill = new List<Part>();
                List<Part> ammoBoxes = new List<Part>();
                int engineCount = 0;
                while (parts.MoveNext())
                {
                    if (parts.Current == null) continue;
                    var partName = parts.Current.name;
                    if (partCounts.ContainsKey(partName))
                    {
                        partCounts[partName]++;
                    }
                    else
                    {
                        partCounts[partName] = 1;
                    }
                    if (allowedEngines.Contains(partName))
                    {
                        engineCount++;
                    }
                    if (bannedParts.Contains(partName))
                    {
                        partsToKill.Add(parts.Current);
                    }
                    if (allowedLandingGear.Contains(partName))
                    {
                        // duplicates allowed
                        continue;
                    }
                    if (ammoParts.Contains(partName))
                    {
                        // can only figure out limits after counting engines.
                        ammoBoxes.Add(parts.Current);
                        continue;
                    }
                    if (partCounts[partName] > 1)
                    {
                        partsToKill.Add(parts.Current);
                    }
                }
                if (engineCount == 0)
                {
                    engineCount = 1;
                }

                while (ammoBoxes.Count > engineCount * 3)
                {
                    partsToKill.Add(ammoBoxes[ammoBoxes.Count - 1]);
                    ammoBoxes.RemoveAt(ammoBoxes.Count - 1);
                }
                if (partsToKill.Count > 0)
                {
                    Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "] Vessel Breaking Part Count Rules " + vessel.GetName());
                    foreach (var part in partsToKill)
                    {
                        Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "] KILLPART:" + part.name + ":" + vessel.GetName());
                        PartExploderSystem.AddPartToExplode(part);
                    }
                }
            }
        }

        private void DoRapidDeploymentMassTrim(float targetMass = 65f)
        {
            // in rapid deployment this verified masses etc. 
            var oreID = PartResourceLibrary.Instance.GetDefinition("Ore").id;
            var pilots = GetAllPilots();
            var lowestMass = float.MaxValue;
            var highestMass = targetMass; // Trim to highest mass or target mass, whichever is higher.
            foreach (var pilot in pilots)
            {

                if (pilot.vessel == null) continue;

                var notShieldedCount = 0;
                using (List<Part>.Enumerator parts = pilot.vessel.parts.GetEnumerator())
                {
                    while (parts.MoveNext())
                    {
                        if (parts.Current == null) continue;
                        // count the unshielded parts
                        if (!parts.Current.ShieldedFromAirstream)
                        {
                            notShieldedCount++;
                        }
                        // Empty the ore tank and set the fuel tanks to the correct amount.
                        using (IEnumerator<PartResource> resources = parts.Current.Resources.GetEnumerator())
                            while (resources.MoveNext())
                            {
                                if (resources.Current == null) continue;

                                if (resources.Current.resourceName == "Ore")
                                {
                                    if (resources.Current.maxAmount == 1500)
                                    {
                                        resources.Current.amount = 0;
                                    }
                                }
                                else if (resources.Current.resourceName == "LiquidFuel")
                                {
                                    if (resources.Current.maxAmount == 3240)
                                    {
                                        resources.Current.amount = 2160;
                                    }
                                }
                                else if (resources.Current.resourceName == "Oxidizer")
                                {
                                    if (resources.Current.maxAmount == 3960)
                                    {
                                        resources.Current.amount = 2640;
                                    }
                                }
                            }
                    }
                }
                var mass = pilot.vessel.GetTotalMass();

                if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: UNSHIELDED:" + notShieldedCount.ToString() + ":" + pilot.vessel.GetName());
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: MASS:" + mass.ToString() + ":" + pilot.vessel.GetName());
                if (mass < lowestMass)
                {
                    lowestMass = mass;
                }
                if (mass > highestMass)
                {
                    highestMass = mass;
                }
            }

            foreach (var pilot in pilots)
            {
                if (pilot.vessel == null) continue;
                var mass = pilot.vessel.GetTotalMass();
                var extraMass = highestMass - mass;
                using (List<Part>.Enumerator parts = pilot.vessel.parts.GetEnumerator())
                    while (parts.MoveNext())
                    {
                        bool massAdded = false;
                        if (parts.Current == null) continue;
                        using (IEnumerator<PartResource> resources = parts.Current.Resources.GetEnumerator())
                            while (resources.MoveNext())
                            {
                                if (resources.Current == null) continue;
                                if (resources.Current.resourceName == "Ore")
                                {
                                    // oreMass = 10;
                                    // ore to add = difference / 10;
                                    if (resources.Current.maxAmount == 1500)
                                    {
                                        var oreAmount = extraMass / 0.01; // 10kg per unit of ore
                                        if (oreAmount > 1500) oreAmount = 1500;
                                        resources.Current.amount = oreAmount;
                                    }
                                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: RESOURCEUPDATE:" + pilot.vessel.GetName() + ":" + resources.Current.amount);
                                    massAdded = true;
                                }
                            }
                        if (massAdded) break;
                    }
            }
        }

        IEnumerator SequencedCompetition(List<string> commandSequence)
        {
            var pilots = GetAllPilots();
            if (pilots.Count < 2)
            {
                Debug.Log("[BDArmory.BDACompetitionMode" + CompetitionID.ToString() + "]: Unable to start sequenced competition - one or more teams is empty");
                competitionStatus.Set("Competition: Failed!  One or more teams is empty.");
                competitionStartFailureReason = CompetitionStartFailureReason.OnlyOneTeam;
                StopCompetition();
                yield break;
            }
            sequencedCompetitionStarting = true;
            competitionType = CompetitionType.SEQUENCED;
            double startTime = Planetarium.GetUniversalTime();
            double nextStep = startTime;

            if (BDArmorySettings.MUTATOR_MODE && BDArmorySettings.MUTATOR_LIST.Count > 0)
            {
                ConfigureMutator();
            }
            foreach (var cmdEvent in commandSequence)
            {
                // parse the event
                competitionStatus.Add(cmdEvent);
                var parts = cmdEvent.Split(':');
                if (parts.Count() == 1)
                {
                    Debug.LogWarning("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Competition Command not parsed correctly " + cmdEvent);
                    StopCompetition();
                    yield break;
                }
                var timeStep = int.Parse(parts[0]);
                nextStep = Planetarium.GetUniversalTime() + timeStep;
                while (Planetarium.GetUniversalTime() < nextStep)
                {
                    yield return new WaitForFixedUpdate();
                }

                var command = parts[1];

                switch (command)
                {
                    case "Stage":
                        {
                            if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Staging.");
                            // activate stage
                            foreach (var pilot in pilots)
                            {
                                VesselUtils.fireNextNonEmptyStage(pilot.vessel);
                            }
                            break;
                        }
                    case "ActionGroup":
                        {
                            if (parts.Count() < 3 || parts.Count() > 4)
                            {
                                if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Competition Command not parsed correctly " + cmdEvent);
                                StopCompetition();
                                yield break;
                            }
                            if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Jiggling action group " + parts[2] + ".");
                            foreach (var pilot in pilots)
                            {
                                if (parts.Count() == 3)
                                {
                                    pilot.vessel.ActionGroups.ToggleGroup(KM_dictAG[int.Parse(parts[2])]);
                                }
                                else if (parts.Count() == 4)
                                {
                                    bool state = false;
                                    if (parts[3] != "0")
                                    {
                                        state = true;
                                    }
                                    pilot.vessel.ActionGroups.SetGroup(KM_dictAG[int.Parse(parts[2])], state);
                                }
                            }
                            break;
                        }
                    case "TogglePilot":
                        {
                            if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Toggling autopilot.");
                            if (parts.Count() == 3)
                            {
                                var newState = true;
                                if (parts[2] == "0")
                                {
                                    newState = false;
                                }
                                foreach (var pilot in pilots)
                                {
                                    if (newState != pilot.pilotEnabled)
                                        pilot.TogglePilot();
                                }
                                if (newState)
                                    competitionStarting = true;
                            }
                            else
                            {
                                foreach (var pilot in pilots)
                                {
                                    pilot.TogglePilot();
                                }
                            }
                            break;
                        }
                    case "ToggleGuard":
                        {
                            if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Toggling guard mode.");
                            if (parts.Count() == 3)
                            {
                                var newState = true;
                                if (parts[2] == "0")
                                {
                                    newState = false;
                                }
                                foreach (var pilot in pilots)
                                {
                                    if (pilot.weaponManager != null && pilot.weaponManager.guardMode != newState)
                                    {
                                        pilot.weaponManager.ToggleGuardMode();
                                        if (!pilot.weaponManager.guardMode) pilot.weaponManager.SetTarget(null);
                                    }
                                }
                            }
                            else
                            {
                                foreach (var pilot in pilots)
                                {
                                    if (pilot.weaponManager != null)
                                    {
                                        pilot.weaponManager.ToggleGuardMode();
                                        if (!pilot.weaponManager.guardMode) pilot.weaponManager.SetTarget(null);
                                    }
                                    if (BDArmorySettings.MUTATOR_MODE && BDArmorySettings.MUTATOR_LIST.Count > 0)
                                    {
                                        var MM = pilot.vessel.rootPart.FindModuleImplementing<BDAMutator>();
                                        if (MM == null)
                                        {
                                            MM = (BDAMutator)pilot.vessel.rootPart.AddModule("BDAMutator");
                                        }
                                        if (BDArmorySettings.MUTATOR_APPLY_GLOBAL) //selected mutator applied globally
                                        {
                                            MM.EnableMutator(currentMutator);
                                        }
                                        if (BDArmorySettings.MUTATOR_APPLY_TIMER && !BDArmorySettings.MUTATOR_APPLY_GLOBAL) //mutator applied on a per-craft basis
                                        {
                                            MM.EnableMutator(); //random mutator
                                        }
                                    }
                                    if (BDArmorySettings.ENABLE_HOS && BDArmorySettings.HALL_OF_SHAME_LIST.Count > 0)
                                    {
                                        if (BDArmorySettings.HALL_OF_SHAME_LIST.Contains(pilot.vessel.GetName()))
                                        {
                                            using (List<Part>.Enumerator part = pilot.vessel.Parts.GetEnumerator())
                                                while (part.MoveNext())
                                                {
                                                    if (BDArmorySettings.HOS_FIRE > 0.1f)
                                                    {
                                                        BulletHitFX.AttachFire(part.Current.transform.position, part.Current, BDArmorySettings.HOS_FIRE * 50, "GM", BDArmorySettings.COMPETITION_DURATION * 60, 1, true);
                                                    }
                                                    if (BDArmorySettings.HOS_MASS != 0)
                                                    {
                                                        var MM = part.Current.FindModuleImplementing<ModuleMassAdjust>();
                                                        if (MM == null)
                                                        {
                                                            MM = (ModuleMassAdjust)part.Current.AddModule("ModuleMassAdjust");
                                                        }
                                                        MM.duration = BDArmorySettings.COMPETITION_DURATION * 60;
                                                        MM.massMod += (BDArmorySettings.HOS_MASS / pilot.vessel.Parts.Count); //evenly distribute mass change across entire vessel
                                                    }
                                                    if (BDArmorySettings.HOS_DMG != 1)
                                                    {
                                                        var HPT = part.Current.FindModuleImplementing<HitpointTracker>();
                                                        HPT.defenseMutator = (float)(1 / BDArmorySettings.HOS_DMG);
                                                    }
                                                }
                                        }
                                    }
                                }
                            }

                            break;
                        }
                    case "SetThrottle":
                        {
                            if (parts.Count() == 3)
                            {
                                if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Adjusting throttle to " + parts[2] + "%.");
                                var someOtherVessel = pilots[0].vessel == FlightGlobals.ActiveVessel ? pilots[1].vessel : pilots[0].vessel;
                                foreach (var pilot in pilots)
                                {
                                    bool currentVesselIsActive = pilot.vessel == FlightGlobals.ActiveVessel;
                                    if (currentVesselIsActive) LoadedVesselSwitcher.Instance.ForceSwitchVessel(someOtherVessel); // Temporarily switch away so that the throttle change works.
                                    var throttle = int.Parse(parts[2]) * 0.01f;
                                    pilot.vessel.ctrlState.killRot = true;
                                    pilot.vessel.ctrlState.mainThrottle = throttle;
                                    if (currentVesselIsActive) LoadedVesselSwitcher.Instance.ForceSwitchVessel(pilot.vessel); // Switch back again.
                                }
                            }
                            break;
                        }
                    case "RemoveDebris":
                        {
                            if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Removing debris and non-competitors.");
                            // remove anything that doesn't contain BD Armory modules
                            RemoveNonCompetitors(true);
                            RemoveDebrisNow();
                            break;
                        }
                    case "RemoveFairings":
                        {
                            if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Removing fairings.");
                            // removes the fairings after deplyment to stop the physical objects consuming CPU
                            var rmObj = new List<physicalObject>();
                            foreach (var phyObj in FlightGlobals.physicalObjects)
                            {
                                if (phyObj.name == "FairingPanel") rmObj.Add(phyObj);
                            }
                            foreach (var phyObj in rmObj)
                            {
                                FlightGlobals.removePhysicalObject(phyObj);
                            }
                            break;
                        }
                    case "EnableGM":
                        {
                            if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Activating killer GM.");
                            killerGMenabled = true;
                            decisionTick = BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY > 60 ? -1 : Planetarium.GetUniversalTime() + BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY;
                            ResetSpeeds();
                            break;
                        }
                    case "ActivateEngines":
                        {
                            if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Activating engines.");
                            foreach (var pilot in pilots)
                            {
                                if (!BDArmorySettings.NO_ENGINES && SpawnUtils.CountActiveEngines(pilot.vessel) == 0) // If the vessel didn't activate their engines on AG10, then activate all their engines and hope for the best.
                                {
                                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: " + pilot.vessel.GetName() + " didn't activate engines on AG10! Activating ALL their engines.");
                                    SpawnUtils.ActivateAllEngines(pilot.vessel);
                                }
                                else if (BDArmorySettings.NO_ENGINES && SpawnUtils.CountActiveEngines(pilot.vessel) > 0) // Shutdown engines
                                {
                                    SpawnUtils.ActivateAllEngines(pilot.vessel, false);
                                }
                                if (BDArmorySettings.HACK_INTAKES)
                                {
                                    SpawnUtils.HackIntakes(pilot.vessel, true);
                                }
                                if (BDArmorySettings.ENABLE_HOS && BDArmorySettings.HALL_OF_SHAME_LIST.Count > 0)
                                {
                                    if (BDArmorySettings.HALL_OF_SHAME_LIST.Contains(pilot.vessel.GetName()))
                                    {
                                        if (BDArmorySettings.HOS_THRUST != 100)
                                        {
                                            using (var engine = VesselModuleRegistry.GetModuleEngines(pilot.vessel).GetEnumerator())
                                                while (engine.MoveNext())
                                                {
                                                    engine.Current.thrustPercentage = BDArmorySettings.HOS_THRUST;
                                                }
                                        }
                                    }
                                }
                            }
                            break;
                        }
                    case "MassTrim":
                        {
                            if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Performing mass trim.");
                            DoRapidDeploymentMassTrim();
                            break;
                        }
                    case "HackGravity":
                        {
                            if (parts.Count() == 3)
                            {
                                if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Adjusting gravity to " + parts[2] + "x.");
                                double grav = double.Parse(parts[2]);
                                PhysicsGlobals.GraviticForceMultiplier = grav;
                                VehiclePhysics.Gravity.Refresh();
                                //competitionStatus.Add("Competition: Adjusting gravity to " + grav.ToString("0.0") + "G!");
                            }
                            break;
                        }
                    default:
                        {
                            if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Unknown sequenced command: " + command + ".");
                            StopCompetition();
                            yield break;
                        }
                }
            }
            // will need a terminator routine
            CompetitionStarted();
        }

        // ask the GM to find a 'victim' which means a slow pilot who's not shooting very much
        // obviosly this is evil. 
        // it's enabled by right clicking the M button.
        // I also had it hooked up to the death of the Pinata but that's disconnected right now
        private void FindVictim()
        {
            if (!BDArmorySettings.RUNWAY_PROJECT) return;
            if (decisionTick < 0) return;
            if (Planetarium.GetUniversalTime() < decisionTick) return;
            decisionTick = BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY > 60 ? -1 : Planetarium.GetUniversalTime() + BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY;
            if (!killerGMenabled) return;
            if (Planetarium.GetUniversalTime() - competitionStartTime < BDArmorySettings.COMPETITION_KILLER_GM_GRACE_PERIOD) return;
            // arbitrary and capbricious decisions of life and death

            bool hasFired = true;
            Vessel worstVessel = null;
            double slowestSpeed = 100000;
            int vesselCount = 0;
            using (var loadedVessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedVessels.MoveNext())
                {
                    if (loadedVessels.Current == null || !loadedVessels.Current.loaded || VesselModuleRegistry.ignoredVesselTypes.Contains(loadedVessels.Current.vesselType))
                        continue;
                    IBDAIControl pilot = VesselModuleRegistry.GetModule<IBDAIControl>(loadedVessels.Current);
                    if (pilot == null || !pilot.weaponManager || pilot.weaponManager.Team.Neutral)
                        continue;

                    var vesselName = loadedVessels.Current.GetName();
                    if (!Scores.Players.Contains(vesselName))
                        continue;

                    vesselCount++;
                    ScoringData vData = Scores.ScoreData[vesselName];

                    var averageSpeed = vData.AverageSpeed / vData.averageCount;
                    var averageAltitude = vData.AverageAltitude / vData.averageCount;
                    averageSpeed = averageAltitude + (averageSpeed * averageSpeed / 200); // kinetic & potential energy
                    if (pilot.weaponManager != null)
                    {
                        if (!pilot.weaponManager.guardMode) averageSpeed *= 0.5;
                    }

                    bool vesselNotFired = (Planetarium.GetUniversalTime() - vData.lastFiredTime) > 120; // if you can't shoot in 2 minutes you're at the front of line

                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Victim Check " + vesselName + " " + averageSpeed.ToString() + " " + vesselNotFired.ToString());
                    if (hasFired)
                    {
                        if (vesselNotFired)
                        {
                            // we found a vessel which hasn't fired
                            worstVessel = loadedVessels.Current;
                            slowestSpeed = averageSpeed;
                            hasFired = false;
                        }
                        else if (averageSpeed < slowestSpeed)
                        {
                            // this vessel fired but is slow
                            worstVessel = loadedVessels.Current;
                            slowestSpeed = averageSpeed;
                        }
                    }
                    else
                    {
                        if (vesselNotFired)
                        {
                            // this vessel was slow and hasn't fired
                            worstVessel = loadedVessels.Current;
                            slowestSpeed = averageSpeed;
                        }
                    }
                }
            // if we have 3 or more vessels kill the slowest
            if (vesselCount > 2 && worstVessel != null)
            {
                var vesselName = worstVessel.GetName();
                if (Scores.Players.Contains(vesselName))
                {
                    Scores.ScoreData[vesselName].lastPersonWhoDamagedMe = "GM";
                }
                Scores.RegisterDeath(vesselName, GMKillReason.GM);
                competitionStatus.Add(vesselName + " was killed by the GM for being too slow.");
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: GM killing " + vesselName + " for being too slow.");
                VesselUtils.ForceDeadVessel(worstVessel);
            }
            ResetSpeeds();
        }

        private void CheckAltitudeLimits()
        {
            if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH < 55f) // Kill off those flying too high.
            {
                var limit = (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH < 20f ? BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH / 10f : BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH < 39f ? BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH - 18f : (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH - 38f) * 5f + 20f) * 1000f;
                foreach (var weaponManager in LoadedVesselSwitcher.Instance.WeaponManagers.SelectMany(tm => tm.Value).ToList())
                {
                    if (alive.Contains(weaponManager.vessel.vesselName) && weaponManager.vessel.radarAltitude > limit)
                    {
                        var killerName = Scores.ScoreData[weaponManager.vessel.vesselName].lastPersonWhoDamagedMe;
                        if (killerName == "")
                        {
                            killerName = "Flew too high!";
                            Scores.ScoreData[weaponManager.vessel.vesselName].lastPersonWhoDamagedMe = killerName;
                        }
                        Scores.RegisterDeath(weaponManager.vessel.vesselName, GMKillReason.GM);
                        competitionStatus.Add(weaponManager.vessel.vesselName + " flew too high!");
                        if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: " + weaponManager.vessel.vesselName + ":REMOVED:" + killerName);
                        if (KillTimer.ContainsKey(weaponManager.vessel.vesselName)) KillTimer.Remove(weaponManager.vessel.vesselName);
                        VesselUtils.ForceDeadVessel(weaponManager.vessel);
                    }
                }
            }
            if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW > -39f) // Kill off those flying too low.
            {
                float limit;
                if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < -28f) limit = (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW + 28f) * 1000f; // -10km — -1km @ 1km
                else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < -19f) limit = (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW + 19f) * 100f; // -900m — -100m @ 100m
                else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < 0f) limit = BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW * 5f; // -95m — -5m  @ 5m
                else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < 20f) limit = BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW * 100f; // 0m — 1900m @ 100m
                else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < 39f) limit = (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW - 18f) * 1000f; // 2km — 20km @ 1km
                else limit = ((BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW - 38f) * 5f + 20f) * 1000f; // 25km — 50km @ 5km
                foreach (var weaponManager in LoadedVesselSwitcher.Instance.WeaponManagers.SelectMany(tm => tm.Value).ToList())
                {
                    if (alive.Contains(weaponManager.vessel.vesselName) && weaponManager.vessel.radarAltitude < limit)
                    {
                        var killerName = Scores.ScoreData[weaponManager.vessel.vesselName].lastPersonWhoDamagedMe;
                        if (killerName == "")
                        {
                            killerName = "Flew too low!";
                            Scores.ScoreData[weaponManager.vessel.vesselName].lastPersonWhoDamagedMe = killerName;
                        }
                        Scores.RegisterDeath(weaponManager.vessel.vesselName, GMKillReason.GM);
                        competitionStatus.Add(weaponManager.vessel.vesselName + " flew too low!");
                        if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: " + weaponManager.vessel.vesselName + ":REMOVED:" + killerName);
                        if (KillTimer.ContainsKey(weaponManager.vessel.vesselName)) KillTimer.Remove(weaponManager.vessel.vesselName);
                        VesselUtils.ForceDeadVessel(weaponManager.vessel);
                    }
                }
            }
        }
        // reset all the tracked speeds, and copy the shot clock over, because I wanted 2 minutes of shooting to count
        private void ResetSpeeds()
        {
            if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "] resetting kill clock");
            foreach (var player in Scores.Players)
            {
                if (Scores.ScoreData[player].averageCount == 0)
                {
                    Scores.ScoreData[player].AverageAltitude = 0;
                    Scores.ScoreData[player].AverageSpeed = 0;
                }
                else
                {
                    // ensures we always have a sensible value in here
                    Scores.ScoreData[player].AverageAltitude /= Scores.ScoreData[player].averageCount;
                    Scores.ScoreData[player].AverageSpeed /= Scores.ScoreData[player].averageCount;
                    Scores.ScoreData[player].averageCount = 1;
                }
            }
        }

        #endregion

        #region Debris clean-up
        private HashSet<Vessel> nonCompetitorsToRemove = new HashSet<Vessel>();
        public void RemoveNonCompetitors(bool now = false)
        {
            foreach (var vessel in FlightGlobals.Vessels)
            {
                if (vessel == null) continue;
                if (VesselModuleRegistry.ignoredVesselTypes.Contains(vessel.vesselType)) continue;  // Debris handled by DebrisDelayedCleanUp, others are ignored.
                if (nonCompetitorsToRemove.Contains(vessel)) continue; // Already scheduled for removal.
                bool activePilot = false;
                if (BDArmorySettings.RUNWAY_PROJECT && vessel.GetName() == "Pinata")
                {
                    activePilot = true;
                }
                else
                {
                    int foundActiveParts = 0; // Note: this checks for exactly one of each part.
                    if (VesselModuleRegistry.GetModule<MissileFire>(vessel) != null) // Has a weapon manager
                    { ++foundActiveParts; }


                    if (VesselModuleRegistry.GetModule<IBDAIControl>(vessel) != null) // Has an AI
                    { ++foundActiveParts; }

                    if (VesselModuleRegistry.GetModule<ModuleCommand>(vessel) != null || VesselModuleRegistry.GetModule<KerbalSeat>(vessel) != null) // Has a command module or command seat.
                    { ++foundActiveParts; }
                    activePilot = foundActiveParts == 3;

                    if (VesselModuleRegistry.GetModule<MissileBase>(vessel) != null) // Allow missiles.
                    { activePilot = true; }
                }
                if (!activePilot)
                {
                    nonCompetitorsToRemove.Add(vessel);
                    if (vessel.vesselType == VesselType.SpaceObject) // Deal with any new comets or asteroids that have appeared immediately.
                    {
                        RemoveSpaceObject(vessel);
                    }
                    else
                        StartCoroutine(DelayedVesselRemovalCoroutine(vessel, now ? 0f : BDArmorySettings.COMPETITION_NONCOMPETITOR_REMOVAL_DELAY));
                }
            }
        }

        public void RemoveDebrisNow()
        {
            foreach (var vessel in FlightGlobals.Vessels)
            {
                if (vessel == null) continue;
                if (vessel.vesselType == VesselType.Debris) // Clean up any old debris.
                    StartCoroutine(DelayedVesselRemovalCoroutine(vessel, 0));
                if (vessel.vesselType == VesselType.SpaceObject) // Remove comets and asteroids to try to avoid null refs. (Still get null refs from comets, but it seems better with this than without it.)
                    RemoveSpaceObject(vessel);
            }
        }

        public void RemoveSpaceObject(Vessel vessel)
        {
            StartCoroutine(DelayedVesselRemovalCoroutine(vessel, 0.1f)); // We need a small delay to make sure the new asteroids get registered if they're being used for Asteroid Rain or Asteroid Field.
        }

        HashSet<VesselType> debrisTypes = new HashSet<VesselType> { VesselType.Debris, VesselType.SpaceObject }; // Consider space objects as debris.
        void DebrisDelayedCleanUp(Vessel debris)
        {
            try
            {
                if (debris != null && debrisTypes.Contains(debris.vesselType))
                {
                    if (debris.vesselType == VesselType.SpaceObject)
                        RemoveSpaceObject(debris);
                    else
                        StartCoroutine(DelayedVesselRemovalCoroutine(debris, BDArmorySettings.DEBRIS_CLEANUP_DELAY));
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[BDArmory.BDACompetitionMode]: Exception in DebrisDelayedCleanup: debris " + debris + " is a component? " + (debris is Component) + ", is a monobehaviour? " + (debris is MonoBehaviour) + ". Exception: " + e.GetType() + ": " + e.Message + "\n" + e.StackTrace);
            }
        }

        void RemoveCometVessel(Vessel vessel)
        {
            if (vessel.vesselType == VesselType.SpaceObject)
            {
                Debug.Log("[BDArmory.BDACompetitionMode]: Found a newly spawned " + (vessel.FindVesselModuleImplementing<CometVessel>() != null ? "comet" : "asteroid") + " vessel! Removing it.");
                RemoveSpaceObject(vessel);
            }
        }

        private IEnumerator DelayedVesselRemovalCoroutine(Vessel vessel, float delay)
        {
            var vesselType = vessel.vesselType;
            yield return new WaitForSeconds(delay);
            if (vessel != null && debrisTypes.Contains(vesselType) && !debrisTypes.Contains(vessel.vesselType))
            {
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode]: Debris " + vessel.vesselName + " is no longer labelled as debris, not removing.");
                yield break;
            }
            if (vessel != null)
            {
                if (vessel.parts.Count == 1 && vessel.parts[0].IsKerbalEVA()) // The vessel is a kerbal on EVA. Ignore it for now.
                {
                    // KerbalSafetyManager.Instance.CheckForFallingKerbals(vessel);
                    if (nonCompetitorsToRemove.Contains(vessel)) nonCompetitorsToRemove.Remove(vessel);
                    yield break;
                    // var kerbalEVA = VesselModuleRegistry.GetKerbalEVA(vessel);
                    // if (kerbalEVA != null)
                    //     StartCoroutine(KerbalSafetyManager.Instance.RecoverWhenPossible(kerbalEVA));
                }
                else
                {
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode]: Removing " + vessel.vesselName);
                    yield return SpawnUtilsInstance.Instance.RemoveVesselCoroutine(vessel);
                }
            }
            if (nonCompetitorsToRemove.Contains(vessel))
            {
                if (BDArmorySettings.DEBUG_OTHER && vessel.vesselName != null) { Debug.Log($"[BDArmory.BDACompetitionMode]: {vessel.vesselName} removed."); }
                nonCompetitorsToRemove.Remove(vessel);
            }
        }

        void DisableCometSpawning()
        {
            var cometManager = CometManager.Instance;
            if (!cometManager.isActiveAndEnabled) return;
            cometManager.spawnChance = new FloatCurve(new Keyframe[] { new Keyframe(0f, 0f), new Keyframe(1f, 0f) }); // Set the spawn chance to 0.
            foreach (var comet in cometManager.DiscoveredComets) // Clear known comets.
                RemoveCometVessel(comet);
            foreach (var comet in cometManager.Comets) // Clear all comets.
                RemoveCometVessel(comet);
        }
        #endregion

        void FixedUpdate()
        {
            s4r1FiringRateUpdatedFromShotThisFrame = false;
            s4r1FiringRateUpdatedFromHitThisFrame = false;
            if (competitionIsActive)
            {
                //Do the per-frame stuff.
                LogRamming();
                // Do the lower frequency stuff.
                DoUpdate();
            }
        }

        // the competition update system
        // cleans up dead vessels, tries to track kills (badly)
        // all of these are based on the vessel name which is probably sub optimal
        // This is triggered every Time.deltaTime.
        HashSet<Vessel> vesselsToKill = new HashSet<Vessel>();
        HashSet<string> alive = new HashSet<string>();
        public void DoUpdate()
        {
            if (competitionStartTime < 0) return; // Note: this is the same condition as competitionIsActive and could probably be dropped.
            if (competitionType == CompetitionType.WAYPOINTS) return; // Don't do anything below when running waypoints (for now).
            // Example usage of UpcomingCollisions(). Note that the timeToCPA values are only updated after an interval of half the current timeToCPA.
            // if (competitionIsActive)
            //     foreach (var upcomingCollision in UpcomingCollisions(100f).Take(3))
            //         Debug.Log("[BDArmory.BDACompetitionMode]: Upcoming potential collision between " + upcomingCollision.Key.Item1 + " and " + upcomingCollision.Key.Item2 + " at distance " + Mathf.Sqrt(upcomingCollision.Value.Item1) + "m in " + upcomingCollision.Value.Item2 + "s.");
            var now = Planetarium.GetUniversalTime();
            if (now < nextUpdateTick)
                return;
            CheckForBadlyNamedVessels();
            double updateTickLength = BDArmorySettings.TAG_MODE ? 0.1 : BDArmorySettings.GRAVITY_HACKS ? 0.5 : 1;
            vesselsToKill.Clear();
            nextUpdateTick = nextUpdateTick + updateTickLength;
            int numberOfCompetitiveVessels = 0;
            alive.Clear();
            string deadOrAliveString = "ALIVE: ";
            // check all the planes
            foreach (var vessel in FlightGlobals.Vessels)
            {
                if (vessel == null || !vessel.loaded || VesselModuleRegistry.ignoredVesselTypes.Contains(vessel.vesselType)) // || vessel.packed) // Allow packed craft to avoid the packed craft being considered dead (e.g., when command seats spawn).
                    continue;

                var mf = VesselModuleRegistry.GetModule<MissileFire>(vessel);

                if (mf != null)
                {
                    // things to check
                    // does it have fuel?
                    string vesselName = vessel.GetName();
                    ScoringData vData = null;
                    if (Scores.Players.Contains(vesselName))
                    {
                        vData = Scores.ScoreData[vesselName];
                    }

                    // this vessel really is alive
                    if ((vessel.vesselType != VesselType.Debris) && !vesselName.EndsWith("Debris")) // && !vesselName.EndsWith("Plane") && !vesselName.EndsWith("Probe"))
                    {
                        // vessel is still alive
                        alive.Add(vesselName);
                        deadOrAliveString += " *" + vesselName + "* ";
                        numberOfCompetitiveVessels++;
                    }
                    pilotActions[vesselName] = "";

                    // try to create meaningful activity strings
                    if (mf.AI != null && mf.AI.currentStatus != null && BDArmorySettings.DISPLAY_COMPETITION_STATUS)
                    {
                        pilotActions[vesselName] = "";
                        if (mf.vessel.LandedOrSplashed)
                        {
                            if (mf.vessel.Landed)
                                pilotActions[vesselName] = " is landed";
                            else
                                pilotActions[vesselName] = " is splashed";
                        }
                        var activity = mf.AI.currentStatus;
                        if (activity == "Taking off")
                            pilotActions[vesselName] = " is taking off";
                        else if (activity == "Follow")
                        {
                            if (mf.AI.commandLeader != null && mf.AI.commandLeader.vessel != null)
                                pilotActions[vesselName] = " is following " + mf.AI.commandLeader.vessel.GetName();
                        }
                        else if (activity.StartsWith("Gain Alt"))
                            pilotActions[vesselName] = " is gaining altitude";
                        else if (activity.StartsWith("Terrain"))
                            pilotActions[vesselName] = " is avoiding terrain";
                        else if (activity == "Orbiting")
                            pilotActions[vesselName] = " is orbiting";
                        else if (activity == "Extending")
                            pilotActions[vesselName] = " is extending ";
                        else if (activity == "AvoidCollision")
                            pilotActions[vesselName] = " is avoiding collision";
                        else if (activity == "Evading")
                        {
                            if (mf.incomingThreatVessel != null)
                                pilotActions[vesselName] = " is evading " + mf.incomingThreatVessel.GetName();
                            else
                                pilotActions[vesselName] = " is taking evasive action";
                        }
                        else if (activity == "Attack")
                        {
                            if (mf.currentTarget != null && mf.currentTarget.name != null)
                                pilotActions[vesselName] = " is attacking " + mf.currentTarget.Vessel.GetName();
                            else
                                pilotActions[vesselName] = " is attacking";
                        }
                        else if (activity == "Ramming Speed!")
                        {
                            if (mf.currentTarget != null && mf.currentTarget.name != null)
                                pilotActions[vesselName] = " is trying to ram " + mf.currentTarget.Vessel.GetName();
                            else
                                pilotActions[vesselName] = " is in ramming speed";
                        }
                    }

                    // update the vessel scoring structure
                    if (vData != null)
                    {
                        var partCount = vessel.parts.Count();
                        if (BDArmorySettings.RUNWAY_PROJECT)
                        {
                            if (partCount != vData.previousPartCount)
                            {
                                // part count has changed, check for broken stuff
                                enforcePartCount(vessel);
                            }
                        }
                        if (vData.previousPartCount < vessel.parts.Count)
                            vData.lastLostPartTime = now;
                        vData.previousPartCount = vessel.parts.Count;

                        if (vessel.LandedOrSplashed)
                        {
                            if (!vData.landedState)
                            {
                                // was flying, is now landed
                                vData.lastLandedTime = now;
                                vData.landedState = true;
                                if (vData.landedKillTimer == 0)
                                {
                                    vData.landedKillTimer = now;
                                }
                            }
                        }
                        else
                        {
                            if (vData.landedState)
                            {
                                vData.lastLandedTime = now;
                                vData.landedState = false;
                            }
                            if (vData.landedKillTimer != 0)
                            {
                                // safely airborne for 15 seconds
                                if (now - vData.landedKillTimer > 15)
                                {
                                    vData.landedKillTimer = 0;
                                }
                            }
                        }
                    }

                    // after this point we're checking things that might result in kills.
                    if (now - competitionStartTime < BDArmorySettings.COMPETITION_INITIAL_GRACE_PERIOD) continue;

                    // keep track if they're shooting for the GM
                    if (mf.currentGun != null)
                    {
                        if (mf.currentGun.recentlyFiring)
                        {
                            // keep track that this aircraft was shooting things
                            if (vData != null)
                            {
                                vData.lastFiredTime = now;
                            }
                            if (mf.currentTarget != null && mf.currentTarget.Vessel != null)
                            {
                                pilotActions[vesselName] = " is shooting at " + mf.currentTarget.Vessel.GetName();
                            }
                        }
                    }
                    // does it have ammunition: no ammo => Disable guard mode
                    if (!BDArmorySettings.INFINITE_AMMO)
                    {
                        if (mf.outOfAmmo && !outOfAmmo.Contains(vesselName)) // Report being out of weapons/ammo once.
                        {
                            outOfAmmo.Add(vesselName);
                            if (vData != null && (now - vData.lastDamageTime < 2))
                            {
                                competitionStatus.Add(vesselName + " damaged by " + vData.lastPersonWhoDamagedMe + " and lost weapons");
                            }
                            else
                            {
                                competitionStatus.Add(vesselName + " is out of Ammunition");
                            }
                        }
                        if (mf.guardMode) // If we're in guard mode, check to see if we should disable it.
                        {
                            var pilotAI = VesselModuleRegistry.GetModule<BDModulePilotAI>(vessel); // Get the pilot AI if the vessel has one.
                            var surfaceAI = VesselModuleRegistry.GetModule<BDModuleSurfaceAI>(vessel); // Get the surface AI if the vessel has one.
                            var vtolAI = VesselModuleRegistry.GetModule<BDModulePilotAI>(vessel); // Get the VTOL AI if the vessel has one.
                            if ((pilotAI == null && surfaceAI == null && vtolAI == null) || (mf.outOfAmmo && (BDArmorySettings.DISABLE_RAMMING || !(pilotAI != null && pilotAI.allowRamming)))) // if we've lost the AI or the vessel is out of weapons/ammo and ramming is not allowed.
                                mf.guardMode = false;
                        }
                    }

                    // update the vessel scoring structure
                    if (vData != null)
                    {
                        vData.AverageSpeed += vessel.srfSpeed;
                        vData.AverageAltitude += vessel.altitude;
                        vData.averageCount++;
                        if (vData.landedState && BDArmorySettings.COMPETITION_KILL_TIMER > 0)
                        {
                            KillTimer[vesselName] = (int)(now - vData.landedKillTimer);
                            if (now - vData.landedKillTimer > BDArmorySettings.COMPETITION_KILL_TIMER)
                            {
                                var srfVee = VesselModuleRegistry.GetBDModuleSurfaceAI(vessel, true);
                                if (srfVee == null) vesselsToKill.Add(mf.vessel); //don't kill timer remove surface Ai vessels
                            }
                        }
                        else if (KillTimer.ContainsKey(vesselName))
                            KillTimer.Remove(vesselName);
                    }
                }
            }
            string aliveString = string.Join(",", alive.ToArray());
            previousNumberCompetitive = numberOfCompetitiveVessels;
            // if (BDArmorySettings.DEBUG_LABELS) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "] STILLALIVE: " + aliveString); // This just fills the logs needlessly.
            if (BDArmorySettings.RUNWAY_PROJECT)
            {
                // If we find a vessel named "Pinata" that's a special case object
                // this should probably be configurable.
                if (!pinataAlive && alive.Contains("Pinata"))
                {
                    Debug.Log("[BDArmory.BDACompetitionMode" + CompetitionID.ToString() + "]: Setting Pinata Flag to Alive!");
                    pinataAlive = true;
                    competitionStatus.Add("Enabling Pinata");
                }
                else if (pinataAlive && !alive.Contains("Pinata"))
                {
                    // switch everyone onto separate teams when the Pinata Dies
                    LoadedVesselSwitcher.Instance.MassTeamSwitch(true);
                    pinataAlive = false;
                    competitionStatus.Add("Pinata is dead - competition is now a Free for all");
                    // start kill clock
                    if (!killerGMenabled)
                    {
                        // disabled for now, should be in a competition settings UI
                        //killerGMenabled = true;
                    }

                }
            }
            deadOrAliveString += "     DEAD: ";
            foreach (string player in Scores.Players)
            {
                // check everyone who's no longer alive
                if (!alive.Contains(player))
                {
                    if (BDArmorySettings.RUNWAY_PROJECT && player == "Pinata") continue;
                    if (Scores.ScoreData[player].aliveState == AliveState.Alive)
                    {
                        var timeOfDeath = now;
                        // If player was involved in a collision, we need to wait until the collision is resolved before registering the death.
                        if (rammingInformation.ContainsKey(player) && rammingInformation[player].targetInformation.Values.Any(other => other.collisionDetected))
                        {
                            rammingInformation[player].timeOfDeath = rammingInformation[player].targetInformation.Values.Where(other => other.collisionDetected).Select(other => other.collisionDetectedTime).Max();
                            if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.BDACompetitionMode:{CompetitionID}]: Delaying death of {player} due to being involved in a collision {now - rammingInformation[player].timeOfDeath}s ago at {rammingInformation[player].timeOfDeath - competitionStartTime:F3}.");
                            continue; // Involved in a collision, delay registering death.
                        }
                        switch (Scores.ScoreData[player].lastDamageWasFrom)
                        {
                            case DamageFrom.Ramming:
                                timeOfDeath = rammingInformation[player].timeOfDeath;
                                break;
                        }
                        Scores.RegisterDeath(player, GMKillReason.None, timeOfDeath);
                        pilotActions[player] = " is Dead";
                        var statusMessage = player;
                        if (BDArmorySettings.ENABLE_HOS && BDArmorySettings.HALL_OF_SHAME_LIST.Contains(player) && !string.IsNullOrEmpty(BDArmorySettings.HOS_BADGE))
                        {
                            statusMessage += $" ({BDArmorySettings.HOS_BADGE})";
                        }
                        switch (Scores.ScoreData[player].lastDamageWasFrom)
                        {
                            case DamageFrom.Guns:
                                statusMessage += " was killed by ";
                                break;
                            case DamageFrom.Rockets:
                                statusMessage += " was fragged by ";
                                break;
                            case DamageFrom.Missiles:
                                statusMessage += " was exploded by ";
                                break;
                            case DamageFrom.Ramming:
                                statusMessage += " was rammed by ";
                                break;
                            case DamageFrom.Incompetence:
                                statusMessage += " CRASHED and BURNED.";
                                break;
                            case DamageFrom.None:
                                statusMessage += $" {Scores.ScoreData[player].gmKillReason}";
                                break;
                        }
                        bool canAssignMutator = false;
                        switch (Scores.ScoreData[player].aliveState)
                        {
                            case AliveState.CleanKill: // Damaged recently and only ever took damage from the killer.
                                if (BDArmorySettings.ENABLE_HOS && BDArmorySettings.HALL_OF_SHAME_LIST.Contains(Scores.ScoreData[player].lastPersonWhoDamagedMe) && !string.IsNullOrEmpty(BDArmorySettings.HOS_BADGE))
                                {
                                    statusMessage += Scores.ScoreData[player].lastPersonWhoDamagedMe + " (" + BDArmorySettings.HOS_BADGE + ")" + " (NAILED 'EM! CLEAN KILL!)";
                                }
                                else
                                {
                                    statusMessage += Scores.ScoreData[player].lastPersonWhoDamagedMe + " (NAILED 'EM! CLEAN KILL!)";
                                }
                                canAssignMutator = true;
                                break;
                            case AliveState.HeadShot: // Damaged recently, but took damage a while ago from someone else.
                                if (BDArmorySettings.ENABLE_HOS && BDArmorySettings.HALL_OF_SHAME_LIST.Contains(Scores.ScoreData[player].lastPersonWhoDamagedMe) && !string.IsNullOrEmpty(BDArmorySettings.HOS_BADGE))
                                {
                                    statusMessage += Scores.ScoreData[player].lastPersonWhoDamagedMe + " (" + BDArmorySettings.HOS_BADGE + ")" + " (BOOM! HEAD SHOT!)";
                                }
                                else
                                {
                                    statusMessage += Scores.ScoreData[player].lastPersonWhoDamagedMe + " (BOOM! HEAD SHOT!)";
                                }
                                canAssignMutator = true;
                                break;
                            case AliveState.KillSteal: // Damaged recently, but took damage from someone else recently too.
                                if (BDArmorySettings.ENABLE_HOS && BDArmorySettings.HALL_OF_SHAME_LIST.Contains(Scores.ScoreData[player].lastPersonWhoDamagedMe) && !string.IsNullOrEmpty(BDArmorySettings.HOS_BADGE))
                                {
                                    statusMessage += Scores.ScoreData[player].lastPersonWhoDamagedMe + " (" + BDArmorySettings.HOS_BADGE + ")" + " (KILL STEAL!)";
                                }
                                else
                                {
                                    statusMessage += Scores.ScoreData[player].lastPersonWhoDamagedMe + " (KILL STEAL!)";
                                }
                                canAssignMutator = true;
                                break;
                            case AliveState.AssistedKill: // Assist (not damaged recently or GM kill).
                                if (Scores.ScoreData[player].gmKillReason != GMKillReason.None) Scores.ScoreData[player].everyoneWhoDamagedMe.Add(Scores.ScoreData[player].gmKillReason.ToString());
                                statusMessage += string.Join(", ", Scores.ScoreData[player].everyoneWhoDamagedMe) + " (" + string.Join(", ", Scores.ScoreData[player].damageTypesTaken) + ")";
                                break;
                            case AliveState.Dead: // Suicide/Incompetance (never took damage from others).
                                break;
                        }
                        competitionStatus.Add(statusMessage);
                        if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.BDACompetitionMode:{CompetitionID}]: " + statusMessage);

                        if (BDArmorySettings.MUTATOR_MODE && BDArmorySettings.MUTATOR_APPLY_KILL)
                        {
                            if (BDArmorySettings.MUTATOR_LIST.Count > 0 && canAssignMutator)
                            {
                                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.BDACompetitionMode:{CompetitionID}]: Assigning On Kill mutator to " + Scores.ScoreData[player].lastPersonWhoDamagedMe);

                                using (var loadedVessels = BDATargetManager.LoadedVessels.GetEnumerator())
                                    while (loadedVessels.MoveNext())
                                    {
                                        if (loadedVessels.Current == null || !loadedVessels.Current.loaded || VesselModuleRegistry.ignoredVesselTypes.Contains(loadedVessels.Current.vesselType))
                                            continue;
                                        if (loadedVessels.Current.GetName() == Scores.ScoreData[player].lastPersonWhoDamagedMe)
                                        {
                                            var MM = loadedVessels.Current.rootPart.FindModuleImplementing<BDAMutator>(); //replace with vesselregistry?
                                            if (MM == null)
                                            {
                                                MM = (BDAMutator)loadedVessels.Current.rootPart.AddModule("BDAMutator");
                                            }
                                            MM.EnableMutator(); //random mutator    
                                            competitionStatus.Add(Scores.ScoreData[player].lastPersonWhoDamagedMe + " gains " + MM.mutatorName + (BDArmorySettings.MUTATOR_DURATION > 0 ? " for " + BDArmorySettings.MUTATOR_DURATION * 60 + " seconds!" : "!"));

                                        }
                                    }

                            }
                            else
                            {
                                Debug.Log($"[BDArmory.BDACompetitionMode]: Mutator mode, but no assigned mutators! Can't apply mutator on Kill!");
                            }
                        }
                    }
                    deadOrAliveString += " :" + player + ": ";
                }
            }
            deadOrAlive = deadOrAliveString;

            var numberOfCompetitiveTeams = LoadedVesselSwitcher.Instance.WeaponManagers.Count;
            if (now - competitionStartTime > BDArmorySettings.COMPETITION_INITIAL_GRACE_PERIOD && (numberOfCompetitiveVessels < 2 || (!BDArmorySettings.TAG_MODE && numberOfCompetitiveTeams < 2)) && !ContinuousSpawning.Instance.vesselsSpawningContinuously)
            {
                if (finalGracePeriodStart < 0)
                    finalGracePeriodStart = now;
                if (!(BDArmorySettings.COMPETITION_FINAL_GRACE_PERIOD > 60) && now - finalGracePeriodStart > BDArmorySettings.COMPETITION_FINAL_GRACE_PERIOD)
                {
                    competitionStatus.Add("All Pilots are Dead");
                    foreach (string key in alive)
                    {
                        competitionStatus.Add(key + " wins the round!");
                    }
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]:No viable competitors, Automatically dumping scores");
                    StopCompetition();
                }
            }

            //Reset gravity
            if (BDArmorySettings.GRAVITY_HACKS && competitionIsActive)
            {
                int maxVesselsActive = (ContinuousSpawning.Instance.vesselsSpawningContinuously && BDArmorySettings.VESSEL_SPAWN_CONCURRENT_VESSELS > 0) ? BDArmorySettings.VESSEL_SPAWN_CONCURRENT_VESSELS : Scores.Players.Count;
                double time = now - competitionStartTime;
                gravityMultiplier = 1f + 7f * (float)(Scores.deathCount % maxVesselsActive) / (float)(maxVesselsActive - 1); // From 1G to 8G.
                gravityMultiplier += ContinuousSpawning.Instance.vesselsSpawningContinuously ? Mathf.Sqrt(5f - 5f * Mathf.Cos((float)time / 600f * Mathf.PI)) : Mathf.Sqrt((float)time / 60f); // Plus up to 3.16G.
                PhysicsGlobals.GraviticForceMultiplier = (double)gravityMultiplier;
                VehiclePhysics.Gravity.Refresh();
                if (Mathf.RoundToInt(10 * gravityMultiplier) != Mathf.RoundToInt(10 * lastGravityMultiplier)) // Only write a message when it shows something different.
                {
                    lastGravityMultiplier = gravityMultiplier;
                    competitionStatus.Add("Competition: Adjusting gravity to " + gravityMultiplier.ToString("0.0") + "G!");
                }
            }

            // use the exploder system to remove vessels that should be nuked
            foreach (var vessel in vesselsToKill)
            {
                var vesselName = vessel.GetName();
                var killerName = "";
                if (Scores.Players.Contains(vesselName))
                {
                    killerName = Scores.ScoreData[vesselName].lastPersonWhoDamagedMe;
                    if (killerName == "")
                    {
                        Scores.ScoreData[vesselName].lastPersonWhoDamagedMe = "Landed Too Long"; // only do this if it's not already damaged
                        killerName = "Landed Too Long";
                    }
                    Scores.RegisterDeath(vesselName, GMKillReason.LandedTooLong);
                    competitionStatus.Add(vesselName + " was landed too long.");
                }
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: " + vesselName + ":REMOVED:" + killerName);
                if (KillTimer.ContainsKey(vesselName)) KillTimer.Remove(vesselName);
                VesselUtils.ForceDeadVessel(vessel);
            }

            if (!(BDArmorySettings.COMPETITION_NONCOMPETITOR_REMOVAL_DELAY > 60))
                RemoveNonCompetitors();

            CheckAltitudeLimits();
            if (BDArmorySettings.RUNWAY_PROJECT)
            {
                FindVictim();
            }
            // Debug.Log("[BDArmory.BDACompetitionMode" + CompetitionID.ToString() + "]: Done With Update");

            if (BDArmorySettings.COMPETITION_DURATION > 0 && now - competitionStartTime >= BDArmorySettings.COMPETITION_DURATION * 60d)
            {
                var message = "Ending competition due to out-of-time.";
                competitionStatus.Add(message);
                Debug.Log($"[BDArmory.BDACompetitionMode:{CompetitionID.ToString()}]: " + message);
                LogResults("due to out-of-time");
                StopCompetition();
            }

            if ((BDArmorySettings.MUTATOR_MODE && BDArmorySettings.MUTATOR_APPLY_TIMER) && BDArmorySettings.MUTATOR_DURATION > 0 && now - MutatorResetTime >= BDArmorySettings.MUTATOR_DURATION * 60d && BDArmorySettings.MUTATOR_LIST.Count > 0)
            {

                ScreenMessages.PostScreenMessage(Localizer.Format("#LOC_BDArmory_UI_MutatorShuffle"), 5, ScreenMessageStyle.UPPER_CENTER);
                ConfigureMutator();
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || !vessel.loaded || VesselModuleRegistry.ignoredVesselTypes.Contains(vessel.vesselType)) // || vessel.packed) // Allow packed craft to avoid the packed craft being considered dead (e.g., when command seats spawn).
                        continue;

                    var mf = VesselModuleRegistry.GetModule<MissileFire>(vessel);

                    if (mf != null)
                    {
                        var MM = vessel.rootPart.FindModuleImplementing<BDAMutator>();
                        if (MM == null)
                        {
                            MM = (BDAMutator)vessel.rootPart.AddModule("BDAMutator");
                        }
                        if (BDArmorySettings.MUTATOR_APPLY_GLOBAL)
                        {
                            MM.EnableMutator(currentMutator);
                        }
                        else
                        {
                            MM.EnableMutator();
                        }
                    }
                }
            }
        }

        // This now also writes the competition logs to GameData/BDArmory/Logs/<CompetitionID>[-tag].log
        public void LogResults(string message = "", string tag = "")
        {
            if (competitionStartTime < 0)
            {
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode]: No active competition, not dumping results.");
                return;
            }
            // RunDebugChecks();
            // CheckMemoryUsage();
            if (ContinuousSpawning.Instance.vesselsSpawningContinuously) // Dump continuous spawning scores instead.
            {
                ContinuousSpawning.Instance.DumpContinuousSpawningScores(tag);
                return;
            }

            competitionStatus.Add("Dumping scores for competition " + CompetitionID.ToString() + (tag != "" ? " " + tag : ""));
            Scores.LogResults(CompetitionID.ToString(), message, tag);
        }

        #region Ramming
        // Ramming Logging
        public class RammingTargetInformation
        {
            public Vessel vessel; // The other vessel involved in a collision.
            public double lastUpdateTime = 0; // Last time the timeToCPA was updated.
            public float timeToCPA = 0f; // Time to closest point of approach.
            public bool potentialCollision = false; // Whether a collision might happen shortly.
            public double potentialCollisionDetectionTime = 0; // The latest time the potential collision was detected.
            public int partCountJustPriorToCollision; // The part count of the colliding vessel just prior to the collision.
            public float sqrDistance; // Distance^2 at the time of collision.
            public float angleToCoM = 0f; // The angle from a vessel's velocity direction to the center of mass of the target.
            public bool collisionDetected = false; // Whether a collision has actually been detected.
            public double collisionDetectedTime; // The time that the collision actually occurs.
            public bool ramming = false; // True if a ram was attempted between the detection of a potential ram and the actual collision.
        };
        public class RammingInformation
        {
            public Vessel vessel; // This vessel.
            public string vesselName; // The GetName() name of the vessel (in case vessel gets destroyed and we can't get it from there).
            public int partCount; // The part count of a vessel.
            public float radius; // The vessels "radius" at the time the potential collision was detected.
            public double timeOfDeath = -1; // The time of death of a vessel, for keeping track of when it died.
            public Dictionary<string, RammingTargetInformation> targetInformation; // Information about the ramming target.
        };
        public Dictionary<string, RammingInformation> rammingInformation;

        // Initialise the rammingInformation dictionary with the required vessels.
        public void InitialiseRammingInformation()
        {
            double currentTime = Planetarium.GetUniversalTime();
            rammingInformation = new Dictionary<string, RammingInformation>();
            var pilots = GetAllPilots();
            foreach (var pilot in pilots)
            {
                var pilotAI = VesselModuleRegistry.GetModule<BDModulePilotAI>(pilot.vessel); // Get the pilot AI if the vessel has one.
                if (pilotAI == null) continue;
                var targetRammingInformation = new Dictionary<string, RammingTargetInformation>();
                foreach (var otherPilot in pilots)
                {
                    if (otherPilot == pilot) continue; // Don't include same-vessel information.
                    var otherPilotAI = VesselModuleRegistry.GetModule<BDModulePilotAI>(otherPilot.vessel); // Get the pilot AI if the vessel has one.
                    if (otherPilotAI == null) continue;
                    targetRammingInformation.Add(otherPilot.vessel.vesselName, new RammingTargetInformation { vessel = otherPilot.vessel });
                }
                rammingInformation.Add(pilot.vessel.vesselName, new RammingInformation
                {
                    vessel = pilot.vessel,
                    vesselName = pilot.vessel.GetName(),
                    partCount = pilot.vessel.parts.Count,
                    radius = pilot.vessel.GetRadius(),
                    targetInformation = targetRammingInformation,
                });
            }
        }

        /// <summary>
        /// Add a vessel to the rammingInformation datastructure after a competition has started.
        /// </summary>
        /// <param name="vessel"></param>
        public void AddPlayerToRammingInformation(Vessel vessel)
        {
            if (rammingInformation == null) return; // Not set up yet.
            if (!rammingInformation.ContainsKey(vessel.vesselName)) // Vessel information hasn't been added to rammingInformation datastructure yet.
            {
                rammingInformation.Add(vessel.vesselName, new RammingInformation { vesselName = vessel.vesselName, targetInformation = new Dictionary<string, RammingTargetInformation>() });
                foreach (var otherVesselName in rammingInformation.Keys)
                {
                    if (otherVesselName == vessel.vesselName) continue;
                    rammingInformation[vessel.vesselName].targetInformation.Add(otherVesselName, new RammingTargetInformation { vessel = rammingInformation[otherVesselName].vessel });
                }
            }
            // Create or update ramming information for the vesselName.
            rammingInformation[vessel.vesselName].vessel = vessel;
            rammingInformation[vessel.vesselName].partCount = vessel.parts.Count;
            rammingInformation[vessel.vesselName].radius = vessel.GetRadius();
            // Update each of the other vesselNames in the rammingInformation.
            foreach (var otherVesselName in rammingInformation.Keys)
            {
                if (otherVesselName == vessel.vesselName) continue;
                rammingInformation[otherVesselName].targetInformation[vessel.vesselName] = new RammingTargetInformation { vessel = vessel };
            }

        }
        /// <summary>
        /// Remove a vessel from the rammingInformation datastructure after a competition has started.
        /// </summary>
        /// <param name="player"></param>
        public void RemovePlayerFromRammingInformation(string player)
        {
            if (rammingInformation == null) return; // Not set up yet.
            if (!rammingInformation.ContainsKey(player)) return; // Player isn't in the ramming information
            rammingInformation.Remove(player); // Remove the player.
            foreach (var otherVesselName in rammingInformation.Keys) // Remove the player's target information from the other players.
            {
                if (rammingInformation[otherVesselName].targetInformation.ContainsKey(player)) // It should unless something has gone wrong.
                { rammingInformation[otherVesselName].targetInformation.Remove(player); }
            }
        }

        // Update the ramming information dictionary with expected times to closest point of approach.
        private float maxTimeToCPA = 5f; // Don't look more than 5s ahead.
        public void UpdateTimesToCPAs()
        {
            double currentTime = Planetarium.GetUniversalTime();
            foreach (var vesselName in rammingInformation.Keys)
            {
                var vessel = rammingInformation[vesselName].vessel;
                var pilotAI = vessel != null ? VesselModuleRegistry.GetModule<BDModulePilotAI>(vessel) : null; // Get the pilot AI if the vessel has one.

                foreach (var otherVesselName in rammingInformation[vesselName].targetInformation.Keys)
                {
                    var otherVessel = rammingInformation[vesselName].targetInformation[otherVesselName].vessel;
                    var otherPilotAI = otherVessel != null ? VesselModuleRegistry.GetModule<BDModulePilotAI>(otherVessel) : null; // Get the pilot AI if the vessel has one.
                    if (pilotAI == null || otherPilotAI == null) // One of the vessels or pilot AIs has been destroyed.
                    {
                        rammingInformation[vesselName].targetInformation[otherVesselName].timeToCPA = maxTimeToCPA; // Set the timeToCPA to maxTimeToCPA, so that it's not considered for new potential collisions.
                        rammingInformation[otherVesselName].targetInformation[vesselName].timeToCPA = maxTimeToCPA; // Set the timeToCPA to maxTimeToCPA, so that it's not considered for new potential collisions.
                    }
                    else
                    {
                        if (currentTime - rammingInformation[vesselName].targetInformation[otherVesselName].lastUpdateTime > rammingInformation[vesselName].targetInformation[otherVesselName].timeToCPA / 2f) // When half the time is gone, update it.
                        {
                            float timeToCPA = AIUtils.ClosestTimeToCPA(vessel, otherVessel, maxTimeToCPA); // Look up to maxTimeToCPA ahead.
                            if (timeToCPA > 0f && timeToCPA < maxTimeToCPA) // If the closest approach is within the next maxTimeToCPA, log it.
                                rammingInformation[vesselName].targetInformation[otherVesselName].timeToCPA = timeToCPA;
                            else // Otherwise set it to the max value.
                                rammingInformation[vesselName].targetInformation[otherVesselName].timeToCPA = maxTimeToCPA;
                            // This is symmetric, so update the symmetric value and set the lastUpdateTime for both so that we don't bother calculating the same thing twice.
                            rammingInformation[otherVesselName].targetInformation[vesselName].timeToCPA = rammingInformation[vesselName].targetInformation[otherVesselName].timeToCPA;
                            rammingInformation[vesselName].targetInformation[otherVesselName].lastUpdateTime = currentTime;
                            rammingInformation[otherVesselName].targetInformation[vesselName].lastUpdateTime = currentTime;
                        }
                    }
                }
            }
        }

        // Get the upcoming collisions ordered by predicted separation^2 (for Scott to adjust camera views).
        public IOrderedEnumerable<KeyValuePair<Tuple<string, string>, Tuple<float, float>>> UpcomingCollisions(float distanceThreshold, bool sortByDistance = true)
        {
            var upcomingCollisions = new Dictionary<Tuple<string, string>, Tuple<float, float>>();
            if (rammingInformation != null)
                foreach (var vesselName in rammingInformation.Keys)
                    foreach (var otherVesselName in rammingInformation[vesselName].targetInformation.Keys)
                        if (rammingInformation[vesselName].targetInformation[otherVesselName].potentialCollision && rammingInformation[vesselName].targetInformation[otherVesselName].timeToCPA < maxTimeToCPA && String.Compare(vesselName, otherVesselName) < 0)
                            if (rammingInformation[vesselName].vessel != null && rammingInformation[otherVesselName].vessel != null)
                            {
                                var predictedSqrSeparation = Vector3.SqrMagnitude(rammingInformation[vesselName].vessel.CoM - rammingInformation[otherVesselName].vessel.CoM);
                                if (predictedSqrSeparation < distanceThreshold * distanceThreshold)
                                    upcomingCollisions.Add(
                                        new Tuple<string, string>(vesselName, otherVesselName),
                                        new Tuple<float, float>(predictedSqrSeparation, rammingInformation[vesselName].targetInformation[otherVesselName].timeToCPA)
                                    );
                            }
            return upcomingCollisions.OrderBy(d => sortByDistance ? d.Value.Item1 : d.Value.Item2);
        }

        // Check for potential collisions in the near future and update data structures as necessary.
        private float potentialCollisionDetectionTime = 1f; // 1s ought to be plenty.
        private void CheckForPotentialCollisions()
        {
            float collisionMargin = 4f; // Sum of radii is less than this factor times the separation.
            double currentTime = Planetarium.GetUniversalTime();
            foreach (var vesselName in rammingInformation.Keys)
            {
                var vessel = rammingInformation[vesselName].vessel;
                foreach (var otherVesselName in rammingInformation[vesselName].targetInformation.Keys)
                {
                    if (!rammingInformation.ContainsKey(otherVesselName))
                    {
                        Debug.Log("[BDArmory.BDACompetitionMode]: other vessel (" + otherVesselName + ") is missing from rammingInformation!");
                        return;
                    }
                    if (!rammingInformation[vesselName].targetInformation.ContainsKey(otherVesselName))
                    {
                        Debug.Log("[BDArmory.BDACompetitionMode]: other vessel (" + otherVesselName + ") is missing from rammingInformation[vessel].targetInformation!");
                        return;
                    }
                    if (!rammingInformation[otherVesselName].targetInformation.ContainsKey(vesselName))
                    {
                        Debug.Log("[BDArmory.BDACompetitionMode]: vessel (" + vesselName + ") is missing from rammingInformation[otherVessel].targetInformation!");
                        return;
                    }
                    var otherVessel = rammingInformation[vesselName].targetInformation[otherVesselName].vessel;
                    if (rammingInformation[vesselName].targetInformation[otherVesselName].timeToCPA < potentialCollisionDetectionTime) // Closest point of approach is within the detection time.
                    {
                        if (vessel != null && otherVessel != null) // If one of the vessels has been destroyed, don't calculate new potential collisions, but allow the timer on existing potential collisions to run out so that collision analysis can still use it.
                        {
                            var separation = Vector3.Magnitude(vessel.transform.position - otherVessel.transform.position);
                            if (separation < collisionMargin * (vessel.GetRadius() + otherVessel.GetRadius())) // Potential collision detected.
                            {
                                if (!rammingInformation[vesselName].targetInformation[otherVesselName].potentialCollision) // Register the part counts and angles when the potential collision is first detected.
                                { // Note: part counts and vessel radii get updated whenever a new potential collision is detected, but not angleToCoM (which is specific to a colliding pair).
                                    rammingInformation[vesselName].partCount = vessel.parts.Count;
                                    rammingInformation[otherVesselName].partCount = otherVessel.parts.Count;
                                    rammingInformation[vesselName].radius = vessel.GetRadius();
                                    rammingInformation[otherVesselName].radius = otherVessel.GetRadius();
                                    rammingInformation[vesselName].targetInformation[otherVesselName].angleToCoM = Vector3.Angle(vessel.srf_vel_direction, otherVessel.CoM - vessel.CoM);
                                    rammingInformation[otherVesselName].targetInformation[vesselName].angleToCoM = Vector3.Angle(otherVessel.srf_vel_direction, vessel.CoM - otherVessel.CoM);
                                }

                                // Update part counts if vessels get shot and potentially lose parts before the collision happens.
                                if (!Scores.Players.Contains(rammingInformation[vesselName].vesselName)) CheckVesselType(rammingInformation[vesselName].vessel); // It may have become a "vesselName Plane" if the WM is badly placed.
                                try
                                {
                                    if (Scores.ScoreData[rammingInformation[vesselName].vesselName].lastDamageWasFrom != DamageFrom.Ramming && Scores.ScoreData[rammingInformation[vesselName].vesselName].lastDamageTime > rammingInformation[otherVesselName].targetInformation[vesselName].potentialCollisionDetectionTime)
                                    {
                                        if (rammingInformation[vesselName].partCount != vessel.parts.Count)
                                        {
                                            if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode]: Ram logging: " + vesselName + " lost " + (rammingInformation[vesselName].partCount - vessel.parts.Count) + " parts from getting shot.");
                                            rammingInformation[vesselName].partCount = vessel.parts.Count;
                                        }
                                    }
                                    if (!Scores.Players.Contains(rammingInformation[otherVesselName].vesselName)) CheckVesselType(rammingInformation[otherVesselName].vessel); // It may have become a "vesselName Plane" if the WM is badly placed.
                                    if (Scores.ScoreData[rammingInformation[otherVesselName].vesselName].lastDamageWasFrom != DamageFrom.Ramming && Scores.ScoreData[rammingInformation[otherVesselName].vesselName].lastDamageTime > rammingInformation[vesselName].targetInformation[otherVesselName].potentialCollisionDetectionTime)
                                    {
                                        if (rammingInformation[otherVesselName].partCount != otherVessel.parts.Count)
                                        {
                                            if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode]: Ram logging: " + otherVesselName + " lost " + (rammingInformation[otherVesselName].partCount - otherVessel.parts.Count) + " parts from getting shot.");
                                            rammingInformation[otherVesselName].partCount = otherVessel.parts.Count;
                                        }
                                    }
                                }
                                catch (KeyNotFoundException e)
                                {
                                    List<string> badVesselNames = new List<string>();
                                    if (!Scores.Players.Contains(rammingInformation[vesselName].vesselName)) badVesselNames.Add(rammingInformation[vesselName].vesselName);
                                    if (!Scores.Players.Contains(rammingInformation[otherVesselName].vesselName)) badVesselNames.Add(rammingInformation[otherVesselName].vesselName);
                                    Debug.LogWarning("[BDArmory.BDACompetitionMode]: A badly named vessel is messing up the collision detection: " + string.Join(", ", badVesselNames) + " | " + e.Message);
                                }

                                // Set the potentialCollision flag to true and update the latest potential collision detection time.
                                rammingInformation[vesselName].targetInformation[otherVesselName].potentialCollision = true;
                                rammingInformation[vesselName].targetInformation[otherVesselName].potentialCollisionDetectionTime = currentTime;
                                rammingInformation[otherVesselName].targetInformation[vesselName].potentialCollision = true;
                                rammingInformation[otherVesselName].targetInformation[vesselName].potentialCollisionDetectionTime = currentTime;

                                // Register intent to ram.
                                var pilotAI = VesselModuleRegistry.GetModule<BDModulePilotAI>(vessel);
                                rammingInformation[vesselName].targetInformation[otherVesselName].ramming |= (pilotAI != null && pilotAI.ramming); // Pilot AI is alive and trying to ram.
                                var otherPilotAI = VesselModuleRegistry.GetModule<BDModulePilotAI>(otherVessel);
                                rammingInformation[otherVesselName].targetInformation[vesselName].ramming |= (otherPilotAI != null && otherPilotAI.ramming); // Other pilot AI is alive and trying to ram.
                            }
                        }
                    }
                    else if (currentTime - rammingInformation[vesselName].targetInformation[otherVesselName].potentialCollisionDetectionTime > 2f * potentialCollisionDetectionTime) // Potential collision is no longer relevant.
                    {
                        rammingInformation[vesselName].targetInformation[otherVesselName].potentialCollision = false;
                        rammingInformation[otherVesselName].targetInformation[vesselName].potentialCollision = false;
                    }
                }
            }
        }

        // Analyse a collision to figure out if someone rammed someone else and who should get awarded for it.
        private void AnalyseCollision(EventReport data)
        {
            if (rammingInformation == null) return; // Ramming information hasn't been set up yet (e.g., between competitions).
            if (data.origin == null) return; // The part is gone. Nothing much we can do about it.
            double currentTime = Planetarium.GetUniversalTime();
            float collisionMargin = 2f; // Compare the separation to this factor times the sum of radii to account for inaccuracies in the vessels size and position. Hopefully, this won't include other nearby vessels.
            var vessel = data.origin.vessel;
            if (vessel == null) // Can vessel be null here? It doesn't appear so.
            {
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode]: Ram logging: in AnalyseCollision the colliding part belonged to a null vessel!");
                return;
            }
            try
            {
                bool hitVessel = false;
                if (rammingInformation.ContainsKey(vessel.vesselName)) // If the part was attached to a vessel,
                {
                    var vesselName = vessel.vesselName; // For convenience.
                    var destroyedPotentialColliders = new List<string>();
                    foreach (var otherVesselName in rammingInformation[vesselName].targetInformation.Keys) // for each other vessel,
                        if (rammingInformation[vesselName].targetInformation[otherVesselName].potentialCollision) // if it was potentially about to collide,
                        {
                            var otherVessel = rammingInformation[vesselName].targetInformation[otherVesselName].vessel;
                            if (otherVessel == null) // Vessel that was potentially colliding has been destroyed. It's more likely that an alive potential collider is the real collider, so remember it in case there are no living potential colliders.
                            {
                                destroyedPotentialColliders.Add(otherVesselName);
                                continue;
                            }
                            var separation = Vector3.Magnitude(vessel.transform.position - otherVessel.transform.position);
                            if (separation < collisionMargin * (rammingInformation[vesselName].radius + rammingInformation[otherVesselName].radius)) // and their separation is less than the sum of their radii,
                            {
                                if (!rammingInformation[vesselName].targetInformation[otherVesselName].collisionDetected) // Take the values when the collision is first detected.
                                {
                                    rammingInformation[vesselName].targetInformation[otherVesselName].collisionDetected = true; // register it as involved in the collision. We'll check for damaged parts in CheckForDamagedParts.
                                    rammingInformation[otherVesselName].targetInformation[vesselName].collisionDetected = true; // The information is symmetric.
                                    rammingInformation[vesselName].targetInformation[otherVesselName].partCountJustPriorToCollision = rammingInformation[otherVesselName].partCount;
                                    rammingInformation[otherVesselName].targetInformation[vesselName].partCountJustPriorToCollision = rammingInformation[vesselName].partCount;
                                    rammingInformation[vesselName].targetInformation[otherVesselName].sqrDistance = (otherVessel != null) ? Vector3.SqrMagnitude(vessel.CoM - otherVessel.CoM) : (Mathf.Pow(collisionMargin * (rammingInformation[vesselName].radius + rammingInformation[otherVesselName].radius), 2f) + 1f); // FIXME Should destroyed vessels have 0 sqrDistance instead?
                                    rammingInformation[otherVesselName].targetInformation[vesselName].sqrDistance = rammingInformation[vesselName].targetInformation[otherVesselName].sqrDistance;
                                    rammingInformation[vesselName].targetInformation[otherVesselName].collisionDetectedTime = currentTime;
                                    rammingInformation[otherVesselName].targetInformation[vesselName].collisionDetectedTime = currentTime;
                                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode]: Ram logging: Collision detected between " + vesselName + " and " + otherVesselName);
                                }
                                hitVessel = true;
                            }
                        }
                    if (!hitVessel) // No other living vessels were potential targets, add in the destroyed ones (if any).
                    {
                        foreach (var otherVesselName in destroyedPotentialColliders) // Note: if there are more than 1, then multiple craft could be credited with the kill, but this is unlikely.
                        {
                            rammingInformation[vesselName].targetInformation[otherVesselName].collisionDetected = true; // register it as involved in the collision. We'll check for damaged parts in CheckForDamagedParts.
                            rammingInformation[otherVesselName].targetInformation[vesselName].collisionDetected = true; // The information is symmetric.
                            rammingInformation[vesselName].targetInformation[otherVesselName].partCountJustPriorToCollision = rammingInformation[otherVesselName].partCount;
                            rammingInformation[otherVesselName].targetInformation[vesselName].partCountJustPriorToCollision = rammingInformation[vesselName].partCount;
                            rammingInformation[vesselName].targetInformation[otherVesselName].collisionDetectedTime = currentTime;
                            rammingInformation[otherVesselName].targetInformation[vesselName].collisionDetectedTime = currentTime;
                            hitVessel = true;
                        }
                    }
                    if (!hitVessel) // We didn't hit another vessel, maybe it crashed and died.
                    {
                        if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode]: Ram logging: " + vesselName + " hit something else.");
                        foreach (var otherVesselName in rammingInformation[vesselName].targetInformation.Keys)
                        {
                            rammingInformation[vesselName].targetInformation[otherVesselName].potentialCollision = false; // Set potential collisions to false.
                            rammingInformation[otherVesselName].targetInformation[vesselName].potentialCollision = false; // Set potential collisions to false.
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[BDArmory.BDACompetitionMode]: Exception in AnalyseCollision: " + e.Message + "\n" + e.StackTrace);
                try { Debug.Log("[BDArmory.DEBUG] rammingInformation is null? " + (rammingInformation == null)); } catch (Exception e2) { Debug.Log("[BDArmory.DEBUG]: rammingInformation: " + e2.Message); }
                try { Debug.Log("[BDArmory.DEBUG] rammingInformation[vesselName] is null? " + (rammingInformation[vessel.vesselName] == null)); } catch (Exception e2) { Debug.Log("[BDArmory.DEBUG]: rammingInformation[vesselName]: " + e2.Message); }
                try { Debug.Log("[BDArmory.DEBUG] rammingInformation[vesselName].targetInformation is null? " + (rammingInformation[vessel.vesselName].targetInformation == null)); } catch (Exception e2) { Debug.Log("[BDArmory.DEBUG]: rammingInformation[vesselName].targetInformation: " + e2.Message); }
                try
                {
                    foreach (var otherVesselName in rammingInformation[vessel.vesselName].targetInformation.Keys)
                    {
                        try { Debug.Log("[BDArmory.DEBUG] rammingInformation[" + vessel.vesselName + "].targetInformation[" + otherVesselName + "] is null? " + (rammingInformation[vessel.vesselName].targetInformation[otherVesselName] == null)); } catch (Exception e2) { Debug.Log("[BDArmory.DEBUG]: rammingInformation[" + vessel.vesselName + "].targetInformation[" + otherVesselName + "]: " + e2.Message); }
                        try { Debug.Log("[BDArmory.DEBUG] rammingInformation[" + otherVesselName + "].targetInformation[" + vessel.vesselName + "] is null? " + (rammingInformation[otherVesselName].targetInformation[vessel.vesselName] == null)); } catch (Exception e2) { Debug.Log("[BDArmory.DEBUG]: rammingInformation[" + otherVesselName + "].targetInformation[" + vessel.vesselName + "]: " + e2.Message); }
                    }
                }
                catch (Exception e3)
                {
                    Debug.Log("[BDArmory.DEBUG]: " + e3.Message);
                }
            }
        }

        // Check for parts being lost on the various vessels for which collisions have been detected.
        private void CheckForDamagedParts()
        {
            double currentTime = Planetarium.GetUniversalTime();
            float headOnLimit = 20f;
            var collidingVesselsBySeparation = new Dictionary<string, KeyValuePair<float, IOrderedEnumerable<KeyValuePair<string, float>>>>();

            // First, we're going to filter the potentially colliding vessels and sort them by separation.
            foreach (var vesselName in rammingInformation.Keys)
            {
                var vessel = rammingInformation[vesselName].vessel;
                var collidingVesselDistances = new Dictionary<string, float>();

                // For each potential collision that we've waited long enough for, refine the potential colliding vessels.
                foreach (var otherVesselName in rammingInformation[vesselName].targetInformation.Keys)
                {
                    if (!rammingInformation[vesselName].targetInformation[otherVesselName].collisionDetected) continue; // Other vessel wasn't involved in a collision with this vessel.
                    if (currentTime - rammingInformation[vesselName].targetInformation[otherVesselName].potentialCollisionDetectionTime > potentialCollisionDetectionTime) // We've waited long enough for the parts that are going to explode to explode.
                    {
                        // First, check the vessels marked as colliding with this vessel for lost parts. If at least one other lost parts or was destroyed, exclude any that didn't lose parts (set collisionDetected to false).
                        bool someOneElseLostParts = false;
                        foreach (var tmpVesselName in rammingInformation[vesselName].targetInformation.Keys)
                        {
                            if (!rammingInformation[vesselName].targetInformation[tmpVesselName].collisionDetected) continue; // Other vessel wasn't involved in a collision with this vessel.
                            var tmpVessel = rammingInformation[vesselName].targetInformation[tmpVesselName].vessel;
                            if (tmpVessel == null || rammingInformation[vesselName].targetInformation[tmpVesselName].partCountJustPriorToCollision - tmpVessel.parts.Count > 0)
                            {
                                someOneElseLostParts = true;
                                break;
                            }
                        }
                        if (someOneElseLostParts) // At least one other vessel lost parts or was destroyed.
                        {
                            foreach (var tmpVesselName in rammingInformation[vesselName].targetInformation.Keys)
                            {
                                if (!rammingInformation[vesselName].targetInformation[tmpVesselName].collisionDetected) continue; // Other vessel wasn't involved in a collision with this vessel.
                                var tmpVessel = rammingInformation[vesselName].targetInformation[tmpVesselName].vessel;
                                if (tmpVessel != null && rammingInformation[vesselName].targetInformation[tmpVesselName].partCountJustPriorToCollision == tmpVessel.parts.Count) // Other vessel didn't lose parts, mark it as not involved in this collision.
                                {
                                    rammingInformation[vesselName].targetInformation[tmpVesselName].collisionDetected = false;
                                    rammingInformation[tmpVesselName].targetInformation[vesselName].collisionDetected = false;
                                }
                            }
                        } // Else, the collided with vessels didn't lose any parts, so we don't know who this vessel really collided with.

                        // If the other vessel is still a potential collider, add it to the colliding vessels dictionary with its distance to this vessel.
                        if (rammingInformation[vesselName].targetInformation[otherVesselName].collisionDetected)
                            collidingVesselDistances.Add(otherVesselName, rammingInformation[vesselName].targetInformation[otherVesselName].sqrDistance);
                    }
                }

                // If multiple vessels are involved in a collision with this vessel, the lost parts counts are going to be skewed towards the first vessel processed. To counteract this, we'll sort the colliding vessels by their distance from this vessel.
                var collidingVessels = collidingVesselDistances.OrderBy(d => d.Value);
                if (collidingVesselDistances.Count > 0)
                    collidingVesselsBySeparation.Add(vesselName, new KeyValuePair<float, IOrderedEnumerable<KeyValuePair<string, float>>>(collidingVessels.First().Value, collidingVessels));

                if (BDArmorySettings.DEBUG_OTHER && collidingVesselDistances.Count > 1) // DEBUG
                {
                    foreach (var otherVesselName in collidingVesselDistances.Keys) Debug.Log("[BDArmory.BDACompetitionMode]: Ram logging: colliding vessel distances^2 from " + vesselName + ": " + otherVesselName + " " + collidingVesselDistances[otherVesselName]);
                    foreach (var otherVesselName in collidingVessels) Debug.Log("[BDArmory.BDACompetitionMode]: Ram logging: sorted order: " + otherVesselName.Key);
                }
            }
            var sortedCollidingVesselsBySeparation = collidingVesselsBySeparation.OrderBy(d => d.Value.Key); // Sort the outer dictionary by minimum separation from the nearest colliding vessel.

            // Then we're going to try to figure out who should be awarded the ram.
            foreach (var vesselNameKVP in sortedCollidingVesselsBySeparation)
            {
                var vesselName = vesselNameKVP.Key;
                var vessel = rammingInformation[vesselName].vessel;
                foreach (var otherVesselNameKVP in vesselNameKVP.Value.Value)
                {
                    var otherVesselName = otherVesselNameKVP.Key;
                    if (!rammingInformation[vesselName].targetInformation[otherVesselName].collisionDetected) continue; // Other vessel wasn't involved in a collision with this vessel.
                    if (currentTime - rammingInformation[vesselName].targetInformation[otherVesselName].potentialCollisionDetectionTime > potentialCollisionDetectionTime) // We've waited long enough for the parts that are going to explode to explode.
                    {
                        var otherVessel = rammingInformation[vesselName].targetInformation[otherVesselName].vessel;
                        var pilotAI = vessel != null ? VesselModuleRegistry.GetModule<BDModulePilotAI>(vessel) : null;
                        var otherPilotAI = otherVessel != null ? VesselModuleRegistry.GetModule<BDModulePilotAI>(otherVessel) : null;

                        // Count the number of parts lost.
                        var rammedPartsLost = (otherPilotAI == null) ? rammingInformation[vesselName].targetInformation[otherVesselName].partCountJustPriorToCollision : rammingInformation[vesselName].targetInformation[otherVesselName].partCountJustPriorToCollision - otherVessel.parts.Count;
                        var rammingPartsLost = (pilotAI == null) ? rammingInformation[otherVesselName].targetInformation[vesselName].partCountJustPriorToCollision : rammingInformation[otherVesselName].targetInformation[vesselName].partCountJustPriorToCollision - vessel.parts.Count;
                        if (rammedPartsLost < 0 || rammingPartsLost < 0) // BUG! A plane involved in two collisions close together apparently can cause this?
                        {
                            Debug.LogWarning($"[BDArmory.BDACompetitionMode]: Negative parts lost in ram! Clamping to 0.");
                            if (rammedPartsLost < 0)
                            {
                                Debug.LogWarning($"[BDArmory.BDACompetitionMode]: {otherVesselName} had {rammingInformation[vesselName].targetInformation[otherVesselName].partCountJustPriorToCollision} parts and lost {rammedPartsLost} parts (current part count: {(otherPilotAI == null ? "none" : $"{otherVessel.parts.Count}")})");
                                rammedPartsLost = 0;
                            }
                            if (rammingPartsLost < 0)
                            {
                                Debug.LogWarning($"[BDArmory.BDACompetitionMode]: {vesselName} had {rammingInformation[otherVesselName].targetInformation[vesselName].partCountJustPriorToCollision} parts and lost {rammingPartsLost} parts (current part count: {(pilotAI == null ? "none" : $"{vessel.parts.Count}")})");
                                rammingPartsLost = 0;
                            }
                        }
                        rammingInformation[otherVesselName].partCount -= rammedPartsLost; // Immediately adjust the parts count for more accurate tracking.
                        rammingInformation[vesselName].partCount -= rammingPartsLost;
                        // Update any other collisions that are waiting to count parts.
                        foreach (var tmpVesselName in rammingInformation[vesselName].targetInformation.Keys)
                            if (rammingInformation[tmpVesselName].targetInformation[vesselName].collisionDetected)
                                rammingInformation[tmpVesselName].targetInformation[vesselName].partCountJustPriorToCollision = rammingInformation[vesselName].partCount;
                        foreach (var tmpVesselName in rammingInformation[otherVesselName].targetInformation.Keys)
                            if (rammingInformation[tmpVesselName].targetInformation[otherVesselName].collisionDetected)
                                rammingInformation[tmpVesselName].targetInformation[otherVesselName].partCountJustPriorToCollision = rammingInformation[otherVesselName].partCount;

                        // Figure out who should be awarded the ram.
                        var rammingVessel = rammingInformation[vesselName].vesselName;
                        var rammedVessel = rammingInformation[otherVesselName].vesselName;
                        var headOn = false;
                        var accidental = false;
                        if (rammingInformation[vesselName].targetInformation[otherVesselName].ramming ^ rammingInformation[otherVesselName].targetInformation[vesselName].ramming) // Only one of the vessels was ramming.
                        {
                            if (!rammingInformation[vesselName].targetInformation[otherVesselName].ramming) // Switch who rammed who if the default is backwards.
                            {
                                rammingVessel = rammingInformation[otherVesselName].vesselName;
                                rammedVessel = rammingInformation[vesselName].vesselName;
                                var tmp = rammingPartsLost;
                                rammingPartsLost = rammedPartsLost;
                                rammedPartsLost = tmp;
                            }
                        }
                        else // Both or neither of the vessels were ramming.
                        {
                            if (rammingInformation[vesselName].targetInformation[otherVesselName].angleToCoM < headOnLimit && rammingInformation[otherVesselName].targetInformation[vesselName].angleToCoM < headOnLimit) // Head-on collision detected, both get awarded with ramming the other.
                            {
                                headOn = true;
                            }
                            else
                            {
                                if (rammingInformation[vesselName].targetInformation[otherVesselName].angleToCoM > rammingInformation[otherVesselName].targetInformation[vesselName].angleToCoM) // Other vessel had a better angleToCoM, so switch who rammed who.
                                {
                                    rammingVessel = rammingInformation[otherVesselName].vesselName;
                                    rammedVessel = rammingInformation[vesselName].vesselName;
                                    var tmp = rammingPartsLost;
                                    rammingPartsLost = rammedPartsLost;
                                    rammedPartsLost = tmp;
                                }
                                if (!rammingInformation[rammingVessel].targetInformation[rammedVessel].ramming && rammingInformation[rammingVessel].targetInformation[rammedVessel].angleToCoM > headOnLimit) accidental = true;
                            }
                        }

                        LogRammingVesselScore(rammingVessel, rammedVessel, rammedPartsLost, rammingPartsLost, headOn, accidental, true, false, rammingInformation[vesselName].targetInformation[otherVesselName].collisionDetectedTime); // Log the ram.

                        // Set the collisionDetected flag to false, since we've now logged this collision. We set both so that the collision only gets logged once.
                        rammingInformation[vesselName].targetInformation[otherVesselName].collisionDetected = false;
                        rammingInformation[otherVesselName].targetInformation[vesselName].collisionDetected = false;
                    }
                }
            }
        }

        // Actually log the ram to various places. Note: vesselName and targetVesselName need to be those returned by the GetName() function to match the keys in Scores.
        public void LogRammingVesselScore(string rammingVesselName, string rammedVesselName, int rammedPartsLost, int rammingPartsLost, bool headOn, bool accidental, bool logToCompetitionStatus, bool logToDebug, double timeOfCollision)
        {
            if (logToCompetitionStatus)
            {
                if (!headOn)
                    competitionStatus.Add(rammedVesselName + " got " + (accidental ? "ACCIDENTALLY " : "") + "RAMMED by " + rammingVesselName + " and lost " + rammedPartsLost + " parts (" + rammingVesselName + " lost " + rammingPartsLost + " parts).");
                else
                    competitionStatus.Add(rammedVesselName + " and " + rammingVesselName + " RAMMED each other and lost " + rammedPartsLost + " and " + rammingPartsLost + " parts, respectively.");
            }
            if (logToDebug)
            {
                if (!headOn)
                    Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: " + rammedVesselName + " got " + (accidental ? "ACCIDENTALLY " : "") + "RAMMED by " + rammingVesselName + " and lost " + rammedPartsLost + " parts (" + rammingVesselName + " lost " + rammingPartsLost + " parts).");
                else
                    Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: " + rammedVesselName + " and " + rammingVesselName + " RAMMED each other and lost " + rammedPartsLost + " and " + rammingPartsLost + " parts, respectively.");
            }
            if (accidental) return; // Don't score from accidental rams.

            // Log score information for the ramming vessel.
            Scores.RegisterRam(rammingVesselName, rammedVesselName, timeOfCollision, rammedPartsLost);
            // If it was a head-on, log scores for the rammed vessel too.
            if (headOn)
            {
                Scores.RegisterRam(rammedVesselName, rammingVesselName, timeOfCollision, rammingPartsLost);
            }
        }

        Dictionary<string, int> partsCheck;
        void CheckForMissingParts()
        {
            if (partsCheck == null)
            {
                partsCheck = new Dictionary<string, int>();
                foreach (var vesselName in rammingInformation.Keys)
                {
                    if (rammingInformation[vesselName].vessel == null) continue;
                    partsCheck.Add(vesselName, rammingInformation[vesselName].vessel.parts.Count);
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode]: Ram logging: " + vesselName + " started with " + partsCheck[vesselName] + " parts.");
                }
            }
            foreach (var vesselName in rammingInformation.Keys)
            {
                if (!partsCheck.ContainsKey(vesselName)) continue;
                var vessel = rammingInformation[vesselName].vessel;
                if (vessel != null)
                {
                    if (partsCheck[vesselName] != vessel.parts.Count)
                    {
                        if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode]: Ram logging: Parts Check: " + vesselName + " has lost " + (partsCheck[vesselName] - vessel.parts.Count) + " parts." + (vessel.parts.Count > 0 ? "" : " and is no more."));
                        partsCheck[vesselName] = vessel.parts.Count;
                    }
                }
                else if (partsCheck[vesselName] > 0)
                {
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDACompetitionMode]: Ram logging: Parts Check: " + vesselName + " has been destroyed, losing " + partsCheck[vesselName] + " parts.");
                    partsCheck[vesselName] = 0;
                }
            }
        }

        // Main calling function to control ramming logging.
        private void LogRamming()
        {
            if (!competitionIsActive) return;
            if (rammingInformation == null) InitialiseRammingInformation();
            UpdateTimesToCPAs();
            CheckForPotentialCollisions();
            CheckForDamagedParts();
            if (BDArmorySettings.DEBUG_OTHER) CheckForMissingParts(); // DEBUG
        }
        #endregion

        #region Tag
        // Note: most of tag is now handled directly in the scoring datastructure.
        public void TagResetTeams()
        {
            char T = 'A';
            var pilots = GetAllPilots();
            foreach (var pilot in pilots)
            {
                if (!Scores.Players.Contains(pilot.vessel.GetName())) { Debug.Log("[BDArmory.BDACompetitionMode]: Scores doesn't contain " + pilot.vessel.GetName()); continue; }
                pilot.weaponManager.SetTeam(BDTeam.Get(T.ToString()));
                Scores.ScoreData[pilot.vessel.GetName()].tagIsIt = false;
                pilot.vessel.ActionGroups.ToggleGroup(KM_dictAG[9]); // Trigger AG9 on becoming "NOT IT"
                T++;
            }
            foreach (var pilot in pilots)
                pilot.weaponManager.ForceScan(); // Update targets.
            startTag = true;
        }
        #endregion

        public void CheckMemoryUsage() // DEBUG
        {
            List<string> strings = new List<string>();
            strings.Add("System memory: " + SystemInfo.systemMemorySize + "MB");
            strings.Add("Reserved: " + UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1024 / 1024 + "MB");
            strings.Add("Allocated: " + UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1024 / 1024 + "MB");
            strings.Add("Mono heap: " + UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / 1024 / 1024 + "MB");
            strings.Add("Mono used: " + UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1024 / 1024 + "MB");
            strings.Add("GfxDriver: " + UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / 1024 / 1024 + "MB");
            strings.Add("plus unspecified runtime (native) memory.");
            Debug.Log("[BDArmory.BDACompetitionMode]: DEBUG Memory Usage: " + string.Join(", ", strings));
        }

        public void CheckNumbersOfThings() // DEBUG
        {
            List<string> strings = new List<string>();
            strings.Add("FlightGlobals.Vessels: " + FlightGlobals.Vessels.Count);
            strings.Add("Non-competitors to remove: " + nonCompetitorsToRemove.Count);
            strings.Add("EffectBehaviour<ParticleSystem>: " + EffectBehaviour.FindObjectsOfType<ParticleSystem>().Length);
            strings.Add("EffectBehaviour<KSPParticleEmitter>: " + EffectBehaviour.FindObjectsOfType<KSPParticleEmitter>().Length);
            strings.Add("KSPParticleEmitters: " + FindObjectsOfType<KSPParticleEmitter>().Length);
            strings.Add("KSPParticleEmitters including inactive: " + Resources.FindObjectsOfTypeAll(typeof(KSPParticleEmitter)).Length);
            Debug.Log("DEBUG " + string.Join(", ", strings));
            Dictionary<string, int> emitterNames = new Dictionary<string, int>();
            foreach (var pe in Resources.FindObjectsOfTypeAll(typeof(KSPParticleEmitter)).Cast<KSPParticleEmitter>())
            {
                if (!pe.isActiveAndEnabled)
                {
                    if (emitterNames.ContainsKey(pe.gameObject.name))
                        ++emitterNames[pe.gameObject.name];
                    else
                        emitterNames.Add(pe.gameObject.name, 1);
                }
            }
            Debug.Log("DEBUG inactive/disabled emitter names: " + string.Join(", ", emitterNames.OrderByDescending(kvp => kvp.Value).Select(pe => pe.Key + ":" + pe.Value)));

            strings.Clear();
            strings.Add("Parts: " + FindObjectsOfType<Part>().Length + " active of " + Resources.FindObjectsOfTypeAll(typeof(Part)).Length);
            strings.Add("Vessels: " + FindObjectsOfType<Vessel>().Length + " active of " + Resources.FindObjectsOfTypeAll(typeof(Vessel)).Length);
            strings.Add("GameObjects: " + FindObjectsOfType<GameObject>().Length + " active of " + Resources.FindObjectsOfTypeAll(typeof(GameObject)).Length);
            strings.Add($"FlightState ProtoVessels: {HighLogic.CurrentGame.flightState.protoVessels.Where(pv => pv.vesselRef != null).Count()} active of {HighLogic.CurrentGame.flightState.protoVessels.Count}");
            Debug.Log("DEBUG " + string.Join(", ", strings));

            strings.Clear();
            foreach (var pool in FindObjectsOfType<ObjectPool>())
                strings.Add($"{pool.poolObjectName}:{pool.size}");
            Debug.Log("DEBUG Object Pools: " + string.Join(", ", strings));
        }

        public void RunDebugChecks()
        {
            CheckMemoryUsage();
#if DEBUG
            CheckNumbersOfThings();
#endif
        }

        public IEnumerator CheckGCPerformance()
        {
            var wait = new WaitForFixedUpdate();
            var wait2 = new WaitForSeconds(0.5f);
            var startRealTime = Time.realtimeSinceStartup;
            var startTime = Time.time;
            int countFrames = 0;
            int countVessels = 0;
            int countParts = 0;
            while (Time.time - startTime < 1d)
            {
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || vessel.packed || !vessel.loaded) continue;
                    var moduleList = vessel.FindPartModulesImplementing<HitpointTracker>();
                    foreach (var module in moduleList)
                        if (module != null) ++countParts;
                    ++countVessels;
                }
                ++countFrames;
                yield return wait;
            }
            Debug.Log($"DEBUG {Time.realtimeSinceStartup - startRealTime}s {countFrames} frames: {countParts} on {countVessels} vessels have HP (using FindPartModulesImplementing)");

            startRealTime = Time.realtimeSinceStartup;
            startTime = Time.time;
            countFrames = 0;
            countVessels = 0;
            countParts = 0;
            while (Time.time - startTime < 1d)
            {
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || vessel.packed || !vessel.loaded) continue;
                    foreach (var module in VesselModuleRegistry.GetModules<HitpointTracker>(vessel)) if (module != null) ++countParts;
                    ++countVessels;
                }
                ++countFrames;
                yield return wait;
            }
            Debug.Log($"DEBUG {Time.realtimeSinceStartup - startRealTime}s {countFrames} frames: {countParts} on {countVessels} vessels have HP (using VesselModuleRegistry)");

            startRealTime = Time.realtimeSinceStartup;
            startTime = Time.time;
            countFrames = 0;
            countVessels = 0;
            countParts = 0;
            while (Time.time - startTime < 1d)
            {
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || vessel.packed || !vessel.loaded) continue;
                    var moduleList = vessel.FindPartModulesImplementing<ModuleEngines>();
                    foreach (var module in moduleList)
                        if (module != null) ++countParts;
                    ++countVessels;
                }
                ++countFrames;
                yield return wait;
            }
            Debug.Log($"DEBUG {Time.realtimeSinceStartup - startRealTime}s {countFrames} frames: {countParts} engines on {countVessels} vessels (using FindPartModulesImplementing)");

            startRealTime = Time.realtimeSinceStartup;
            startTime = Time.time;
            countFrames = 0;
            countVessels = 0;
            countParts = 0;
            while (Time.time - startTime < 1d)
            {
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || vessel.packed || !vessel.loaded) continue;
                    foreach (var module in VesselModuleRegistry.GetModules<ModuleEngines>(vessel)) if (module != null) ++countParts;
                    ++countVessels;
                }
                ++countFrames;
                yield return wait;
            }
            Debug.Log($"DEBUG {Time.realtimeSinceStartup - startRealTime}s {countFrames} frames: {countParts} engines on {countVessels} vessels (using VesselModuleRegistry)");

            startRealTime = Time.realtimeSinceStartup;
            startTime = Time.time;
            countFrames = 0;
            countVessels = 0;
            countParts = 0;
            while (Time.time - startTime < 1d)
            {
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || vessel.packed || !vessel.loaded) continue;
                    var module = vessel.FindPartModuleImplementing<MissileFire>();
                    if (module != null) ++countParts;
                    ++countVessels;
                }
                ++countFrames;
                yield return wait;
            }
            Debug.Log($"DEBUG {Time.realtimeSinceStartup - startRealTime}s {countFrames} frames: {countParts} of {countVessels} vessels have MF (using FindPartModuleImplementing)");

            startRealTime = Time.realtimeSinceStartup;
            startTime = Time.time;
            countFrames = 0;
            countVessels = 0;
            countParts = 0;
            while (Time.time - startTime < 1d)
            {
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || vessel.packed || !vessel.loaded) continue;
                    var module = VesselModuleRegistry.GetModule<MissileFire>(vessel);
                    if (module != null) ++countParts;
                    ++countVessels;
                }
                ++countFrames;
                yield return wait;
            }
            Debug.Log($"DEBUG {Time.realtimeSinceStartup - startRealTime}s {countFrames} frames: {countParts} of {countVessels} vessels have MF (using VesselModuleRegistry)");


            // Single frame performance
            yield return wait2;

            startRealTime = Time.realtimeSinceStartup;
            countVessels = 0;
            countParts = 0;
            for (int i = 0; i < 500; ++i)
            {
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || vessel.packed || !vessel.loaded) continue;
                    var moduleList = vessel.FindPartModulesImplementing<HitpointTracker>();
                    foreach (var module in moduleList)
                        if (module != null) ++countParts;
                    ++countVessels;
                }
            }
            Debug.Log($"DEBUG {Time.realtimeSinceStartup - startRealTime}s {500} iters: {countParts} on {countVessels} vessels have HP (using FindPartModulesImplementing)");

            yield return wait2;

            startRealTime = Time.realtimeSinceStartup;
            countVessels = 0;
            countParts = 0;
            for (int i = 0; i < 500; ++i)
            {
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || vessel.packed || !vessel.loaded) continue;
                    foreach (var module in VesselModuleRegistry.GetModules<HitpointTracker>(vessel)) if (module != null) ++countParts;
                    ++countVessels;
                }
            }
            Debug.Log($"DEBUG {Time.realtimeSinceStartup - startRealTime}s {500} iters: {countParts} on {countVessels} vessels have HP (using VesselModueeRegistry)");

            yield return wait2;

            startRealTime = Time.realtimeSinceStartup;
            countVessels = 0;
            countParts = 0;
            for (int i = 0; i < 500; ++i)
            {
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || vessel.packed || !vessel.loaded) continue;
                    var moduleList = vessel.FindPartModulesImplementing<ModuleEngines>();
                    foreach (var module in moduleList)
                        if (module != null) ++countParts;
                    ++countVessels;
                }
            }
            Debug.Log($"DEBUG {Time.realtimeSinceStartup - startRealTime}s {500} iters: {countParts} engines on {countVessels} vessels (using FindPartModulesImplementing)");

            startRealTime = Time.realtimeSinceStartup;
            countVessels = 0;
            countParts = 0;
            for (int i = 0; i < 500; ++i)
            {
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || vessel.packed || !vessel.loaded) continue;
                    foreach (var module in VesselModuleRegistry.GetModules<ModuleEngines>(vessel)) if (module != null) ++countParts;
                    ++countVessels;
                }
            }
            Debug.Log($"DEBUG {Time.realtimeSinceStartup - startRealTime}s {500} iters: {countParts} engines on {countVessels} vessels (using VesselModuleRegistry)");

            yield return wait2;

            startRealTime = Time.realtimeSinceStartup;
            countVessels = 0;
            countParts = 0;
            for (int i = 0; i < 500; ++i)
            {
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || vessel.packed || !vessel.loaded) continue;
                    var moduleList = vessel.FindPartModulesImplementing<MissileFire>();
                    foreach (var module in moduleList)
                        if (module != null) ++countParts;
                    ++countVessels;
                }
            }
            Debug.Log($"DEBUG {Time.realtimeSinceStartup - startRealTime}s {500} iters: {countParts} WMs on {countVessels} vessels (using FindPartModulesImplementing)");

            yield return wait2;

            startRealTime = Time.realtimeSinceStartup;
            countVessels = 0;
            countParts = 0;
            for (int i = 0; i < 500; ++i)
            {
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || vessel.packed || !vessel.loaded) continue;
                    foreach (var module in VesselModuleRegistry.GetModules<MissileFire>(vessel)) if (module != null) ++countParts;
                    ++countVessels;
                }
            }
            Debug.Log($"DEBUG {Time.realtimeSinceStartup - startRealTime}s {500} iters: {countParts} WMs on {countVessels} vessels (using VesselModuleRegistry)");

            yield return wait2;

            startRealTime = Time.realtimeSinceStartup;
            countVessels = 0;
            countParts = 0;
            for (int i = 0; i < 500; ++i)
            {
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || vessel.packed || !vessel.loaded) continue;
                    var module = vessel.FindPartModuleImplementing<MissileFire>();
                    if (module != null) ++countParts;
                    ++countVessels;
                }
            }
            Debug.Log($"DEBUG {Time.realtimeSinceStartup - startRealTime}s {500} iters: {countParts} WMs on {countVessels} vessels (using Find single)");

            yield return wait2;

            startRealTime = Time.realtimeSinceStartup;
            countVessels = 0;
            countParts = 0;
            for (int i = 0; i < 500; ++i)
            {
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || vessel.packed || !vessel.loaded) continue;
                    var module = VesselModuleRegistry.GetModule<MissileFire>(vessel);
                    if (module != null) ++countParts;
                    ++countVessels;
                }
            }
            Debug.Log($"DEBUG {Time.realtimeSinceStartup - startRealTime}s {500} iters: {countParts} WMs on {countVessels} vessels (using VesselModuleRegistry)");

            competitionStatus.Add("Done.");
        }

        public void CleanUpKSPsDeadReferences()
        {
            var toRemove = new List<uint>();
            foreach (var key in FlightGlobals.PersistentVesselIds.Keys)
                if (FlightGlobals.PersistentVesselIds[key] == null) toRemove.Add(key);
            Debug.Log($"DEBUG Found {toRemove.Count} null persistent vessel references.");
            foreach (var key in toRemove) FlightGlobals.PersistentVesselIds.Remove(key);

            toRemove.Clear();
            foreach (var key in FlightGlobals.PersistentLoadedPartIds.Keys)
                if (FlightGlobals.PersistentLoadedPartIds[key] == null) toRemove.Add(key);
            Debug.Log($"DEBUG Found {toRemove.Count} null persistent loaded part references.");
            foreach (var key in toRemove) FlightGlobals.PersistentLoadedPartIds.Remove(key);

            // Usually doesn't find any.
            toRemove.Clear();
            foreach (var key in FlightGlobals.PersistentUnloadedPartIds.Keys)
                if (FlightGlobals.PersistentUnloadedPartIds[key] == null) toRemove.Add(key);
            Debug.Log($"DEBUG Found {toRemove.Count} null persistent unloaded part references.");
            foreach (var key in toRemove) FlightGlobals.PersistentUnloadedPartIds.Remove(key);

            var protoVessels = HighLogic.CurrentGame.flightState.protoVessels.Where(pv => pv.vesselRef == null).ToList();
            if (protoVessels.Count > 0) { Debug.Log($"DEBUG Found {protoVessels.Count} inactive ProtoVessels in flightState."); }
            foreach (var protoVessel in protoVessels)
            {
                if (protoVessel == null) continue;
                ShipConstruction.RecoverVesselFromFlight(protoVessel, HighLogic.CurrentGame.flightState, true);
                if (protoVessel == null) continue;
                if (protoVessel.protoPartSnapshots != null)
                {
                    foreach (var protoPart in protoVessel.protoPartSnapshots)
                    {
                        protoPart.modules.Clear();
                        protoPart.pVesselRef = null;
                        protoPart.partRef = null;
                    }
                    protoVessel.protoPartSnapshots.Clear();
                }
            }
        }
    }
}
