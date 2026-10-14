using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
using BDArmory.VesselSpawning;
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
        public string competitionTag = "";
        public double competitionStartTime = -1;
        public double MutatorResetTime = -1;
        public double competitionPreStartTime = -1;
        public double nextUpdateTick = -1;
        private double decisionTick = -1;
        private double finalGracePeriodStart = -1;
        int competitiveTeamsAliveLimit = 2;
        double altitudeLimitGracePeriod = -1;
        public static float gravityMultiplier = 1f;
        float lastGravityMultiplier;
        public float MinAlt = 1f;
        float lastMinAlt;
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
        /*
        #region Competition Announcer //Competition on-kill soundclips, searchtag Announcer
        AudioClip headshotClip;
        AudioClip 2KillClip 
        AudioClip 3KillClip;
        AudioClip 4KillClip;
        AudioClip 5KillClip;
        AudioClip 6KillClip;
        AudioClip 7KillClip;
        AudioClip 8KillClip;

        AudioSource audioSource;
        List<AudioClip> announcerBarks;
        #endregion
        */

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
        Rect versionRect;
        Rect versionRectShadow;
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
            CleanDebrisAlways = BDArmorySettings.CLEAN_DEBRIS_ALWAYS;
            /*
            //Announcer
            headshotClip = SoundUtils.GetAudioClip("BDArmory/Sounds/Announcer/Headshot", true);
            2KillClip = SoundUtils.GetAudioClip("BDArmory/Sounds/Announcer/2Kills", true);
            3KillClip = SoundUtils.GetAudioClip("BDArmory/Sounds/Announcer/3Kills", true);
            4KillClip = SoundUtils.GetAudioClip("BDArmory/Sounds/Announcer/4Kills", true);
            5KillClip = SoundUtils.GetAudioClip("BDArmory/Sounds/Announcer/5Kills", true);
            6KillClip = SoundUtils.GetAudioClip("BDArmory/Sounds/Announcer/6Kills", true);
            7KillClip = SoundUtils.GetAudioClip("BDArmory/Sounds/Announcer/7Kills", true);
            8KillClip = SoundUtils.GetAudioClip("BDArmory/Sounds/Announcer/8Kills", true);
            audioSource = gameObject.AddComponent<AudioSource>();
            announcerBarks = [2KillClip, 3KillClip, 4KillClip, 5KillClip, 6KillClip, 7KillClip, 8KillClip];
            */
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
                    GUI.Label(versionRectShadow, BDArmorySetup.Version, dateStyleShadow);
                    GUI.Label(versionRect, BDArmorySetup.Version, dateStyle);
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
                        if (BDArmorySettings.RUNWAY_PROJECT)
                        {
                            if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                            {
                                guiStatusString += $"\nCurrent Firing Rate: {BDArmorySettings.FIRE_RATE_OVERRIDE} shots/min.";
                            }
                            if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 67 && pinataAlive && BDArmorySetup.GAME_UI_ENABLED && !MapView.MapIsEnabled)
                            {
                                double hpPercent = 1;
                                float DmgTaken = Scores.ScoreData[BDArmorySettings.PINATA_NAME].damageFromGuns.Values.Sum() + Scores.ScoreData[BDArmorySettings.PINATA_NAME].damageFromRockets.Values.Sum() + Scores.ScoreData[BDArmorySettings.PINATA_NAME].damageFromMissiles.Values.Sum();
                                hpPercent = Mathf.Clamp((BDArmorySettings.MAX_ACTIVE_RADAR_RANGE - DmgTaken) / BDArmorySettings.MAX_ACTIVE_RADAR_RANGE, 0, 1);
                                if (hpPercent > 0)
                                {
                                    Rect barRect = new Rect((Screen.width / 2) - (Screen.width / 6) - 5, Screen.height / 6 + 10, (Screen.width / 3) + 10, 60);
                                    Rect healthRect = new Rect((Screen.width / 2) - ((Screen.width / 6)), (Screen.height / 6) + 5, ((Screen.width / 3) * (float)hpPercent), 50);
                                    Color temp = XKCDColors.Grey;
                                    GUIUtils.DrawRectangle(barRect, temp);
                                    temp = Color.HSVToRGB((85f * (float)hpPercent) / 255, 1f, 1f);
                                    GUIUtils.DrawRectangle(healthRect, temp);

                                }
                                Rect labelrect = new Rect((Screen.width / 2) - 75, (Screen.height / 6) + 70, Screen.width / 3, 60);
                                Rect shadowRect = new Rect((labelrect.x + 1), (labelrect.y + 1), Screen.width / 3, 60);
                                GUI.Label(shadowRect, "Asteroid HP:" + (BDArmorySettings.MAX_ACTIVE_RADAR_RANGE - DmgTaken).ToString("0"), statusStyleShadow);
                                GUI.Label(labelrect, "Asteroid HP:" + (BDArmorySettings.MAX_ACTIVE_RADAR_RANGE - DmgTaken).ToString("0"), statusStyle);
                            }
                        }
                    }
                }
                if (!BDArmorySetup.GAME_UI_ENABLED)
                {
                    if (ContinuousSpawning.Instance.vesselsSpawningContinuously) // Don't do the ALIVE / DEAD string in continuous spawn.
                    { if (!BDArmorySettings.DISPLAY_COMPETITION_STATUS_WITH_HIDDEN_UI) guiStatusString = ""; }
                    else
                    {
                        if (BDArmorySettings.DISPLAY_COMPETITION_STATUS_WITH_HIDDEN_UI) { guiStatusString = deadOrAlive + "\n" + guiStatusString; }
                        else { guiStatusString = deadOrAlive; }
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
            statusStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft
            };
            dateStyle = new GUIStyle(statusStyle);
            int shadowOffset = 2;
            if (BDArmorySetup.GAME_UI_ENABLED)
            {
                float rectOffset = Mathf.Max(100, Mathf.CeilToInt(100 * BDArmorySettings.UI_SCALE_ACTUAL));
                clockRect = new Rect(10, Mathf.CeilToInt(42 * GameSettings.UI_SCALE), rectOffset, 30);
                dateRect = new Rect(rectOffset, Mathf.CeilToInt(38 * GameSettings.UI_SCALE), rectOffset, 20);
                versionRect = new Rect(rectOffset * 2, Mathf.CeilToInt(46 * GameSettings.UI_SCALE), rectOffset, 20);
                statusRect = new Rect(30, Mathf.CeilToInt(60 * GameSettings.UI_SCALE) + rectOffset / 5, Screen.width - 130, Mathf.FloorToInt(Screen.height / 2));
                statusStyle.fontSize = Mathf.Max(22, Mathf.CeilToInt(22 * BDArmorySettings.UI_SCALE_ACTUAL));
                dateStyle.fontSize = Mathf.Max(14, Mathf.CeilToInt(14 * BDArmorySettings.UI_SCALE_ACTUAL));
            }
            else
            {
                float RectLength = Mathf.Max(100, Mathf.CeilToInt(100 * BDArmorySettings.UI_SCALE_ACTUAL));
                float RectHeight = Mathf.Max(20, Mathf.CeilToInt(20 * BDArmorySettings.UI_SCALE_ACTUAL));
                clockRect = new Rect(10, 6, RectLength, RectHeight);
                dateRect = new Rect(10, RectHeight + 6, RectLength, RectHeight);
                versionRect = new Rect(10, (RectHeight * 2) + 8, RectLength, RectHeight);
                statusRect = new Rect(RectLength, 6, Screen.width - 80, Mathf.FloorToInt(Screen.height / 2));
                shadowOffset = 1;
                statusStyle.fontSize = Mathf.Max(14, Mathf.CeilToInt(14 * BDArmorySettings.UI_SCALE_ACTUAL));
                dateStyle.fontSize = Mathf.Max(10, Mathf.CeilToInt(10 * BDArmorySettings.UI_SCALE_ACTUAL));
            }
            clockRectShadow = new Rect(clockRect);
            clockRectShadow.x += shadowOffset;
            clockRectShadow.y += shadowOffset;
            dateRectShadow = new Rect(dateRect);
            dateRectShadow.x += shadowOffset;
            dateRectShadow.y += shadowOffset;
            versionRectShadow = new Rect(versionRect);
            versionRectShadow.x += shadowOffset;
            versionRectShadow.y += shadowOffset;
            statusRectShadow = new Rect(statusRect);
            statusRectShadow.x += shadowOffset;
            statusRectShadow.y += shadowOffset;
            statusStyleShadow = new GUIStyle(statusStyle);
            statusStyleShadow.normal.textColor = new Color(0, 0, 0, 0.75f);
            dateStyleShadow = new GUIStyle(dateStyle);
            dateStyleShadow.normal.textColor = new Color(0, 0, 0, 0.75f);
        }

        void OnDestroy()
        {
            CleanDebrisAlways = false;
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

        public void StartCompetitionMode(float distance, bool startDespiteFailures = false, string tag = "", CompetitionType compType = CompetitionType.FFA)
        {
            if (competitionStarting) return;
            ResetCompetitionStuff(tag);
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
            SpawnUtils.RestoreKALGlobally(BDArmorySettings.RESTORE_KAL);
            GameEvents.onVesselPartCountChanged.Add(OnVesselModified);
            GameEvents.onVesselCreate.Add(OnVesselModified);
            GameEvents.onCrewOnEva.Add(OnCrewOnEVA);
            if (BDArmorySettings.AUTO_ENABLE_VESSEL_SWITCHING)
                LoadedVesselSwitcher.Instance.EnableAutoVesselSwitching(!hasPinata || (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 67));
            competitionStartFailureReason = CompetitionStartFailureReason.None;
            competitionRoutine = StartCoroutine(DogfightCompetitionModeRoutine(distance, startDespiteFailures, compType));
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
            if (BDArmorySettings.AUTO_LOG_TIME_SYNC)
                BDArmorySetup.Instance.SetTimeSyncLogging(true);
            if (BDArmorySettings.TIME_OVERRIDE && BDArmorySettings.TIME_SCALE != 0)
            { Time.timeScale = BDArmorySettings.TIME_SCALE; }
            if (BDArmorySettings.VESSEL_MOVER_CLOSE_ON_COMPETITION_START && BDArmorySetup.showVesselMoverGUI) VesselMover.Instance.SetVisible(false);
        }

        public void StopCompetition()
        {
            if (ContinuousSpawning.Instance.vesselsSpawningContinuously) Scores.deathOrder.Clear(); // Clear the death order in cts spawning mode as we don't want to show it in the LVS.
            if (LoadedVesselSwitcher.Instance is not null) LoadedVesselSwitcher.Instance.ResetDeadVessels(); // Reset the dead vessels in the LVS so that the final corrected results are shown.
            LogResults(tag: competitionTag);
            if (BDArmorySettings.AUTO_LOG_TIME_SYNC)
                BDArmorySetup.Instance.SetTimeSyncLogging(false, !string.IsNullOrEmpty(competitionTag) ? competitionTag : CompetitionID.ToString());
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
            competitionTag = "";
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
            if (!CleanDebrisAlways) GameEvents.onVesselCreate.Remove(DebrisDelayedCleanUp);
            CometCleanup();
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
            if (!CleanDebrisAlways) GameEvents.onVesselCreate.Add(DebrisDelayedCleanUp);
            CometCleanup(true);
            competitionStartTime = Planetarium.GetUniversalTime();
            nextUpdateTick = competitionStartTime + 2; // 2 seconds before we start tracking
            decisionTick = BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY > 60 ? -1 : competitionStartTime + BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY; // every 60 seconds we do nasty things
            finalGracePeriodStart = -1;
            Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Competition Started");
        }

        public void ResetCompetitionStuff(string tag = "", bool preSpawn = false)
        {
            // reinitialize everything when the button get hit.
            CompetitionID = (int)DateTime.UtcNow.Subtract(new DateTime(2020, 1, 1)).TotalSeconds;
            competitionTag = tag;
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
            competitiveTeamsAliveLimit = (BDArmorySettings.WAYPOINTS_MODE && BDArmorySettings.WAYPOINT_GUARD_INDEX < 0) ? 1 : 2;
            altitudeLimitGracePeriod = BDArmorySettings.COMPETITION_INITIAL_GRACE_PERIOD;
            competitionPreStartTime = Planetarium.GetUniversalTime();
            competitionStartTime = competitionIsActive ? Planetarium.GetUniversalTime() : -1;
            nextUpdateTick = competitionStartTime + 2; // 2 seconds before we start tracking
            decisionTick = BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY > 60 ? -1 : competitionStartTime + BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY; // every 60 seconds we do nasty things
            killerGMenabled = false;
            BulletHitFX.CleanPartsOnFireInfo();
            dragLimiting.Clear();
            if (preSpawn)
            {
                Scores.ConfigurePlayers([]); // Clear the scores.
            }
            else
            {
                // Get a list of pilot vessels with unique names for the scoring.
                var pilotVessels = GetAllPilots().Select(p => p.vessel).ToList();
                foreach (var vessel in pilotVessels) SpawnUtils.DeconflictVesselName(vessel); // Make sure the names are unique.
                Scores.ConfigurePlayers(pilotVessels); // Get the competitors.
                if (!string.IsNullOrEmpty(BDArmorySettings.PINATA_NAME) && Scores.Players.Contains(BDArmorySettings.PINATA_NAME)) { hasPinata = true; pinataAlive = false; } else { hasPinata = false; pinataAlive = false; } // Piñata.
                if (SpawnUtils.originalTeams.Count == 0) SpawnUtils.SaveTeams(); // If the vessels weren't spawned in with Vessel Spawner, save the current teams.
            }
            if (LoadedVesselSwitcher.Instance is not null) LoadedVesselSwitcher.Instance.ResetDeadVessels();
            GC.Collect(); // Clear out garbage at a convenient time.
        }

        IEnumerator DogfightCompetitionModeRoutine(float distance, bool startDespiteFailures = false, CompetitionType compMode = CompetitionType.FFA)
        {
            competitionStarting = true;
            competitionType = compMode;
            startTag = true; // Tag entry condition, should be true even if tag is not currently enabled, so if tag is enabled later in the competition it will function
            competitionStatus.Add("Competition: Pilots are taking off.");
            var pilots = new Dictionary<BDTeam, List<IBDAIControl>>();
            HashSet<IBDAIControl> readyToLaunch = new HashSet<IBDAIControl>();
            using (var loadedVessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedVessels.MoveNext())
                {
                    if (loadedVessels.Current == null || !loadedVessels.Current.loaded || VesselModuleRegistry.IgnoredVesselTypes.Contains(loadedVessels.Current.vesselType))
                        continue;
                    IBDAIControl pilot = loadedVessels.Current.ActiveController().AI;
                    if (pilot == null || pilot.WeaponManager == null || pilot.WeaponManager.Team.Neutral)
                        continue;
                    //so, for NPC on NPC violence prevention - have NPCs set to be allies of each other, or set to the same team? Should also probably have a toggle for if NPCs are friends w/ each other

                    if (!string.IsNullOrEmpty(BDArmorySettings.REMOTE_ORC_NPCS_TEAM) && loadedVessels.Current.GetName().Contains(BDArmorySettings.REMOTE_ORCHESTRATION_NPC_SWAPPER)) pilot.WeaponManager.SetTeam(BDTeam.Get(BDArmorySettings.REMOTE_ORC_NPCS_TEAM));

                    if (!pilots.TryGetValue(pilot.WeaponManager.Team, out List<IBDAIControl> teamPilots))
                    {
                        teamPilots = new List<IBDAIControl>();
                        pilots.Add(pilot.WeaponManager.Team, teamPilots);
                        if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Adding Team " + pilot.WeaponManager.Team.Name);
                    }
                    teamPilots.Add(pilot);
                    if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Adding Pilot " + pilot.vessel.GetName());
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
                if (pilot.WeaponManager.guardMode)
                {
                    pilot.WeaponManager.ToggleGuardMode();
                    pilot.WeaponManager.SetTarget(null);
                }
                if (!BDArmorySettings.NO_ENGINES && SpawnUtils.CountActiveEngines(pilot.vessel) == 0) // Find vessels that didn't activate their engines on AG10 and fire their next stage.
                {
                    if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: " + pilot.vessel.vesselName + " didn't activate engines on AG10! Activating ALL their engines.");
                    SpawnUtils.ActivateAllEngines(pilot.vessel);
                }
                else if (BDArmorySettings.NO_ENGINES && SpawnUtils.CountActiveEngines(pilot.vessel) > 0) // Shutdown engines
                {
                    SpawnUtils.ActivateAllEngines(pilot.vessel, false);
                }
                if (BDArmorySettings.HACK_INTAKES) SpawnUtils.HackIntakes(pilot.vessel, true);
                if (BDArmorySettings.MUTATOR_MODE) SpawnUtils.ApplyMutators(pilot.vessel, true);
                if (BDArmorySettings.ENABLE_HOS) SpawnUtils.ApplyHOS(pilot.vessel);
                if (BDArmorySettings.RUNWAY_PROJECT) SpawnUtils.ApplyRWP(pilot.vessel);
                if (BDArmorySettings.COMP_CONVENIENCE_CHECKS) SpawnUtils.ApplyCompSettingsChecks(pilot.vessel);
                /*
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
                                if (BDArmorySettings.HOS_SAS)
                                {
                                    if (part.Current.GetComponent<ModuleReactionWheel>() != null)
                                    {
                                        ModuleReactionWheel SAS;
                                        SAS = part.Current.GetComponent<ModuleReactionWheel>();
                                        //if (part.Current.CrewCapacity == 0)
                                        part.Current.RemoveModule(SAS); //don't strip reaction wheels from cockpits, as those are allowed
                                    }
                                }
                                if (BDArmorySettings.HOS_THRUST != 100)
                                {
                                    using (var engine = VesselModuleRegistry.GetModuleEngines(pilot.vessel).GetEnumerator())
                                        while (engine.MoveNext())
                                        {
                                            engine.Current.thrustPercentage = BDArmorySettings.HOS_THRUST;
                                        }
                                }
                                if (!string.IsNullOrEmpty(BDArmorySettings.HOS_MUTATOR))
                                {
                                    var MM = pilot.vessel.rootPart.FindModuleImplementing<BDAMutator>();
                                    if (MM == null)
                                    {
                                        MM = (BDAMutator)pilot.vessel.rootPart.AddModule("BDAMutator");
                                        if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log($"[BDArmory.BDACompetitionMode]: adding Mutator module {pilot.vessel.vesselName}");
                                    }
                                    if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log($"[BDArmory.BDACompetitionMode]: Applying ({BDArmorySettings.HOS_MUTATOR})");
                                    MM.EnableMutator(BDArmorySettings.HOS_MUTATOR, true);
                                }
                            }
                    }
                }
                if (BDArmorySettings.HACK_INTAKES)
                {
                    SpawnUtils.HackIntakes(pilot.vessel, true);
                }
                if (BDArmorySettings.RUNWAY_PROJECT)
                {
                    float torqueQuantity = 0;
                    int APSquantity = 0;
                    SpawnUtils.HackActuators(pilot.vessel, true);

                    using (List<Part>.Enumerator part = pilot.vessel.Parts.GetEnumerator())
                        while (part.MoveNext())
                        {
                            if (part.Current.GetComponent<ModuleReactionWheel>() != null)
                            {
                                ModuleReactionWheel SAS;
                                SAS = part.Current.GetComponent<ModuleReactionWheel>();
                                if (part.Current.CrewCapacity == 0 || BDArmorySettings.RUNWAY_PROJECT_ROUND == 60)
                                {
                                    torqueQuantity += ((SAS.PitchTorque + SAS.RollTorque + SAS.YawTorque) / 3) * (SAS.authorityLimiter / 100);
                                    if (torqueQuantity > (BDArmorySettings.RUNWAY_PROJECT_ROUND == 60 ? 10 : BDArmorySettings.MAX_SAS_TORQUE))
                                    {
                                        float excessTorque = torqueQuantity - (BDArmorySettings.RUNWAY_PROJECT_ROUND == 60 ? 10 : BDArmorySettings.MAX_SAS_TORQUE);
                                        SAS.authorityLimiter = 100 - Mathf.Clamp(((excessTorque / ((SAS.PitchTorque + SAS.RollTorque + SAS.YawTorque) / 3)) * 100), 0, 100);
                                    }
                                }
                            }
                            if (part.Current.GetComponent<ModuleCommand>() != null)
                            {
                                ModuleCommand MC;
                                MC = part.Current.GetComponent<ModuleCommand>();
                                if (part.Current.CrewCapacity == 0 && MC.minimumCrew == 0 && !SpawnUtils.IsModularMissilePart(part.Current)) //Non-MMG drone core, nuke it
                                    part.Current.RemoveModule(MC);
                            }
                            if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 59)
                            {
                                if (part.Current.GetComponent<ModuleWeapon>() != null)
                                {
                                    ModuleWeapon gun;
                                    gun = part.Current.GetComponent<ModuleWeapon>();
                                    if (gun.isAPS) APSquantity++;
                                    if (APSquantity > 4)
                                    {
                                        part.Current.RemoveModule(gun);
                                        IEnumerator<PartResource> resource = part.Current.Resources.GetEnumerator();
                                        while (resource.MoveNext())
                                        {
                                            if (resource.Current == null) continue;
                                            if (resource.Current.flowState)
                                            {
                                                resource.Current.flowState = false;
                                            }
                                        }
                                        resource.Dispose();
                                    }
                                }
                            }
                        }
                    if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 60)
                    {
                        var nuke = pilot.vessel.rootPart.FindModuleImplementing<BDModuleNuke>();
                        if (nuke == null)
                        {
                            nuke = (BDModuleNuke)pilot.vessel.rootPart.AddModule("BDModuleNuke");
                            nuke.engineCore = true;
                            nuke.meltDownDuration = 15;
                            nuke.thermalRadius = 200;
                            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMOde]: Adding Nuke Module to " + pilot.vessel.GetName());
                        }
                        BDModulePilotAI pilotAI = pilot.vessel.ActiveController().PilotAI;
                        if (pilotAI != null)
                        {
                            pilotAI.minAltitude = Mathf.Max(pilotAI.minAltitude, 750);
                            pilotAI.defaultAltitude = BDArmorySettings.VESSEL_SPAWN_ALTITUDE;
                            pilotAI.maxAllowedAoA = 2.5f;
                            pilotAI.postStallAoA = 5;
                            pilotAI.maxSpeed = Mathf.Min(250, pilotAI.maxSpeed);
                            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMOde]: Setting SpaceMode Ai settings on " + pilot.vessel.GetName());
                        }
                    }
                }
                */
            }

            //clear target database so pilots don't attack yet
            BDATargetManager.ClearDatabase();
            CleanUpKSPsDeadReferences();
            RunDebugChecks();

            if (pilots.Count < (competitionType != CompetitionType.WAYPOINTS ? 2 : 1))
            {
                Debug.LogWarning("[BDArmory.BDACompetitionMode" + CompetitionID.ToString() + "]: Unable to start competition mode - one or more teams is empty");
                competitionStatus.Set("Competition: Failed!  One or more teams is empty.");
                competitionStartFailureReason = CompetitionStartFailureReason.OnlyOneTeam;
                StopCompetition();
                yield break;
            }

            var leaders = new List<IBDAIControl>();
            var leaderNames = RefreshPilots(out pilots, out leaders, false);
            while (leaders.Any(leader => leader == null || leader.WeaponManager == null || leader.WeaponManager.wingCommander == null || leader.WeaponManager.wingCommander.WeaponManager == null))
            {
                yield return new WaitForFixedUpdate();
                if (leaders.Any(leader => leader == null || leader.WeaponManager == null))
                {
                    var survivingLeaders = leaders.Where(l => l != null && l.WeaponManager != null).Select(l => l.vessel.vesselName).ToList();
                    var missingLeaders = leaderNames.Where(l => !survivingLeaders.Contains(l)).ToList();
                    var message = "A team leader disappeared during competition start-up, " + (startDespiteFailures ? "continuing anyway" : "aborting") + ": " + string.Join(", ", missingLeaders);
                    Debug.LogWarning("[BDArmory.BDACompetitionMode]: " + message);
                    if (startDespiteFailures)
                    {
                        competitionStatus.Add("Competition: " + message);
                        leaderNames = RefreshPilots(out pilots, out leaders, false);
                    }
                    else
                    {
                        competitionStatus.Set("Competition: " + message);
                        competitionStartFailureReason = CompetitionStartFailureReason.TeamLeaderDisappeared;
                        StopCompetition();
                        yield break;
                    }
                }
            }
            foreach (var leader in leaders)
                leader.WeaponManager.wingCommander.CommandAllFollow();

            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 67)
            { // For S6R7 switch to piñata teams and enable guard mode prior to take-off to avoid orbiting issues.
                if (!string.IsNullOrEmpty(BDArmorySettings.PINATA_NAME) && hasPinata)
                {
                    SpawnUtils.SaveTeams();
                    foreach (var pilot in GetAllPilots())
                    {
                        if (!pilot.vessel.GetName().Contains(BDArmorySettings.PINATA_NAME))
                        {
                            pilot.WeaponManager.SetTeam(BDTeam.Get("PinataPoppers"));
                            pilot.WeaponManager.guardMode = true; // Enable guard mode prior to take-off to avoid orbiting issues.
                        }
                        else
                        {
                            pilot.WeaponManager.SetTeam(BDTeam.Get("Pinata"));
                        }
                        Scores.ScoreData[pilot.vessel.vesselName].team = pilot.WeaponManager.Team.Name;
                    }
                    leaderNames = RefreshPilots(out pilots, out leaders, true);
                }
            }

            //wait till the leaders are ready to engage (airborne for PilotAI)
            while (true)
            {
                if (leaders.Any(leader => leader == null || leader.WeaponManager == null))
                {
                    var survivingLeaders = leaders.Where(l => l != null && l.WeaponManager != null).Select(l => l.vessel.vesselName).ToList();
                    var missingLeaders = leaderNames.Where(l => !survivingLeaders.Contains(l)).ToList();
                    var message = "A team leader disappeared during competition start-up, " + (startDespiteFailures ? "continuing anyway" : "aborting") + ": " + string.Join(", ", missingLeaders);
                    Debug.LogWarning("[BDArmory.BDACompetitionMode]: " + message);
                    if (startDespiteFailures)
                    {
                        competitionStatus.Add("Competition: " + message);
                        leaderNames = RefreshPilots(out pilots, out leaders, true);
                    }
                    else
                    {
                        competitionStatus.Set("Competition: " + message);
                        competitionStartFailureReason = CompetitionStartFailureReason.TeamLeaderDisappeared;
                        StopCompetition();
                        yield break;
                    }
                }
                if (leaders.All(leader => leader.CanEngage()))
                {
                    break;
                }
                if (startCompetitionNow)
                {
                    var readyLeaders = leaders.Where(leader => leader.CanEngage()).Select(leader => leader.vessel.vesselName).ToList();
                    var message = "A team leader still isn't ready to engage and the start-now timer has run out: " + string.Join(", ", leaderNames.Where(leader => !readyLeaders.Contains(leader)));
                    Debug.LogWarning("[BDArmory.BDACompetitionMode]: " + message);
                    if (startDespiteFailures)
                    {
                        competitionStatus.Add("Competition: " + message);
                        break;
                    }
                    else
                    {
                        competitionStatus.Set("Competition: " + message);
                        competitionStartFailureReason = CompetitionStartFailureReason.Other;
                        StopCompetition();
                        yield break;
                    }
                }
                yield return new WaitForSeconds(1);
            }

            if (!(BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 67))
            { // Switch to piñata teams after everyone is ready.
                if (!string.IsNullOrEmpty(BDArmorySettings.PINATA_NAME) && hasPinata)
                {
                    SpawnUtils.SaveTeams();
                    foreach (var pilot in GetAllPilots())
                    {
                        if (!pilot.vessel.GetName().Contains(BDArmorySettings.PINATA_NAME))
                            pilot.WeaponManager.SetTeam(BDTeam.Get("PinataPoppers"));
                        else
                        {
                            pilot.WeaponManager.SetTeam(BDTeam.Get("Pinata"));
                            if (FlightGlobals.ActiveVessel != pilot.vessel)
                            {
                                LoadedVesselSwitcher.Instance.ForceSwitchVessel(pilot.vessel);
                            }
                        }
                        Scores.ScoreData[pilot.vessel.vesselName].team = pilot.WeaponManager.Team.Name;
                    }
                    leaderNames = RefreshPilots(out pilots, out leaders, true);
                }
            }
            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 67) startCompetitionNow = true;

            if (BDArmorySettings.ASTEROID_FIELD) { AsteroidField.Instance.SpawnField(BDArmorySettings.ASTEROID_FIELD_NUMBER, BDArmorySettings.ASTEROID_FIELD_ALTITUDE, BDArmorySettings.ASTEROID_FIELD_RADIUS, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS); }
            if (BDArmorySettings.ASTEROID_RAIN) { AsteroidRain.Instance.SpawnRain(BDArmorySettings.VESSEL_SPAWN_GEOCOORDS); }

            competitionStatus.Add("Competition: Sending pilots to start position.");
            Vector3 center = Vector3.zero;
            using (var leader = leaders.GetEnumerator())
                while (leader.MoveNext())
                    center += leader.Current.vessel.CoM;
            center /= leaders.Count;
            Vector3 startDirection = (leaders[0].vessel.CoM - center).ProjectOnPlanePreNormalized(VectorUtils.GetUpDirection(center)).normalized;
            startDirection *= (distance + 2 * 2000) / 2 / Mathf.Sin(Mathf.PI / leaders.Count); // 2000 is the orbiting radius of each team.
            Quaternion directionStep = Quaternion.AngleAxis(360f / leaders.Count, VectorUtils.GetUpDirection(center));

            for (var i = 0; i < leaders.Count; ++i)
            {
                var pilotAI = leaders[i].vessel.ActiveController().PilotAI; // Adjust initial fly-to point for terrain and default altitudes.
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

                if (leaders.Any(leader => leader == null || leader.WeaponManager == null))
                {
                    var survivingLeaders = leaders.Where(l => l != null && l.WeaponManager != null).Select(l => l.vessel.vesselName).ToList();
                    var missingLeaders = leaderNames.Where(l => !survivingLeaders.Contains(l)).ToList();
                    var message = "A team leader disappeared during competition start-up, " + (startDespiteFailures ? "continuing anyway" : "aborting") + ": " + string.Join(", ", missingLeaders);
                    Debug.LogWarning("[BDArmory.BDACompetitionMode]: " + message);
                    if (startDespiteFailures)
                    {
                        competitionStatus.Add("Competition: " + message);
                        leaderNames = RefreshPilots(out pilots, out leaders, true);
                    }
                    else
                    {
                        competitionStatus.Set("Competition: " + message);
                        competitionStartFailureReason = CompetitionStartFailureReason.TeamLeaderDisappeared;
                        StopCompetition();
                        yield break;
                    }
                }

                try // Somehow, if a vessel gets destroyed during competition start, the following can throw a null reference exception despite checking for nulls! This is due to the IBDAIControl.transform getter.
                {
                    if (startDespiteFailures && pilots.Values.SelectMany(p => p).Any(p => p == null || p.WeaponManager == null)) leaderNames = RefreshPilots(out pilots, out leaders, true);
                    foreach (var leader in leaders)
                    {
                        foreach (var otherLeader in leaders)
                        {
                            if (leader == otherLeader)
                                continue;
                            if ((leader.transform.position - otherLeader.transform.position).sqrMagnitude < sqrDistance)
                                waiting = true;
                        }

                        // Increase the distance for large teams
                        if (!pilots.ContainsKey(leader.WeaponManager.Team))
                        {
                            var message = "The teams were changed during competition start-up, aborting";
                            competitionStatus.Set("Competition: " + message);
                            Debug.LogWarning("[BDArmory.BDACompetitionMode]: " + message);
                            competitionStartFailureReason = CompetitionStartFailureReason.TeamsChanged;
                            StopCompetition();
                            yield break;
                        }
                        var teamDistance = BDArmorySettings.COMPETITION_INTRA_TEAM_SEPARATION_BASE + BDArmorySettings.COMPETITION_INTRA_TEAM_SEPARATION_PER_MEMBER * pilots[leader.WeaponManager.Team].Count;
                        foreach (var pilot in pilots[leader.WeaponManager.Team])
                            if (pilot != null
                                    && pilot.currentCommand == PilotCommands.Follow
                                    && pilot.vessel.CoM.FurtherFromThan(pilot.commandLeader.vessel.CoM, teamDistance))
                                waiting = true;

                        if (waiting) break;
                    }
                }
                catch (Exception e)
                {
                    var message = "A team leader has disappeared during competition start-up, " + (startDespiteFailures ? "continuing anyway" : "aborting");
                    Debug.LogWarning("[BDArmory.BDACompetitionMode]: Exception thrown in DogfightCompetitionModeRoutine: " + e.Message + "\n" + e.StackTrace);
                    try
                    {
                        var survivingLeaders = leaders.Where(l => l != null && l.WeaponManager != null).Select(l => l.vessel.vesselName).ToList();
                        var missingLeaders = leaderNames.Where(l => !survivingLeaders.Contains(l)).ToList();
                        message = "A team leader disappeared during competition start-up, " + (startDespiteFailures ? "continuing anyway" : "aborting") + ": " + string.Join(", ", missingLeaders);
                    }
                    catch (Exception e2) { Debug.LogWarning($"[BDArmory.BDACompetitionMode]: Exception gathering missing leader names:" + e2.Message); }
                    Debug.LogWarning("[BDArmory.BDACompetitionMode]: " + message);
                    if (startDespiteFailures)
                    {
                        competitionStatus.Add(message);
                        leaderNames = RefreshPilots(out pilots, out leaders, true);
                        waiting = true;
                    }
                    else
                    {
                        competitionStatus.Set(message);
                        competitionStartFailureReason = CompetitionStartFailureReason.TeamLeaderDisappeared;
                        StopCompetition();
                        yield break;
                    }
                }

                yield return null;
            }
            previousNumberCompetitive = 2; // For entering into tag mode

            //start the match
            if (startDespiteFailures && pilots.Values.SelectMany(p => p).Any(p => p == null || p.WeaponManager == null)) leaderNames = RefreshPilots(out pilots, out leaders, true);
            foreach (var teamPilots in pilots.Values)
            {
                if (teamPilots == null)
                {
                    var message = "Teams have been changed during competition start-up, aborting";
                    competitionStatus.Set("Competition: " + message);
                    Debug.LogWarning("[BDArmory.BDACompetitionMode]: " + message);
                    competitionStartFailureReason = CompetitionStartFailureReason.TeamsChanged;
                    StopCompetition();
                    yield break;
                }
                foreach (var pilot in teamPilots)
                    if (pilot == null)
                    {
                        var message = "A pilot has disappeared from team during competition start-up, aborting";
                        competitionStatus.Set("Competition: " + message);
                        Debug.LogWarning("[BDArmory.BDACompetitionMode]: " + message);
                        competitionStartFailureReason = CompetitionStartFailureReason.PilotDisappeared;
                        StopCompetition(); // Check that the team pilots haven't been changed during the competition startup.
                        yield break;
                    }
            }
            // Refresh teams (after the above checks) in case fighters have split off from their motherships and we now have more pilots.
            leaderNames = RefreshPilots(out pilots, out leaders, true);
            if (BDATargetManager.LoadedVessels.Where(v => !VesselModuleRegistry.IgnoredVesselTypes.Contains(v.vesselType)).Any(v => VesselModuleRegistry.GetModuleCount<ModuleRadar>(v) > 0)) // Update RCS if any vessels have radars.
            {
                try
                {
                    RadarUtils.ForceUpdateRadarCrossSections();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BDArmory.BDACompetitionMode]: Exception thrown in DogfightCompetitionModeRoutine: " + e.Message + "\n" + e.StackTrace);
                    if (startDespiteFailures)
                    {
                        competitionStatus.Add("Failed to update radar cross sections, continuing anyway");
                    }
                    else
                    {
                        competitionStatus.Set("Failed to update radar cross sections, aborting");
                        competitionStartFailureReason = CompetitionStartFailureReason.Other;
                        StopCompetition();
                        yield break;
                    }
                }
            }
            // Update attack point (necessary for orbit)
            var allPilots = pilots.Values.SelectMany(p => p).Where(pilot => pilot != null && pilot.vessel != null && gameObject != null).ToList();
            foreach (var pilot in allPilots) center += pilot.vessel.CoM;
            center /= allPilots.Count;
            centerGPS = VectorUtils.WorldPositionToGeoCoords(center, FlightGlobals.currentMainBody);

            // Command attack
            if (competitionType != CompetitionType.WAYPOINTS)
            {
                foreach (var teamPilots in pilots)
                    foreach (var pilot in teamPilots.Value)
                    {
                        if (pilot == null) continue;

                        if (!pilot.WeaponManager.guardMode)
                            pilot.WeaponManager.ToggleGuardMode();

                        // foreach (var leader in leaders)
                        //     BDATargetManager.ReportVessel(pilot.vessel, leader.WeaponManager);

                        pilot.ReleaseCommand(false, true);
                        pilot.CommandAttack(centerGPS);
                        pilot.vessel.altimeterDisplayState = AltimeterDisplayState.AGL;
                    }
            }
            competitionStatus.Add("Competition starting!  Good luck!");
            CompetitionStarted();
        }
        #endregion

        public List<IBDAIControl> GetAllPilots()
        {
            var pilots = new List<IBDAIControl>();
            foreach (var vessel in BDATargetManager.LoadedVessels)
            {
                if (vessel == null || !vessel.loaded || VesselModuleRegistry.IgnoredVesselTypes.Contains(vessel.vesselType)) continue;
                var pilot = vessel.ActiveController().AI;
                if (pilot == null || pilot.WeaponManager == null)
                {
                    VesselModuleRegistry.OnVesselModified(vessel, true);
                    pilot = vessel.ActiveController().AI;
                    if (pilot == null || pilot.WeaponManager == null) continue; // Unfixable, ignore the vessel.
                }
                if (IsValidVessel(vessel) != InvalidVesselReason.None) continue;
                if (pilot.WeaponManager.Team.Neutral) continue; // Ignore the neutrals.
                pilots.Add(pilot);
            }
            return pilots;
        }

        /// <summary>
        /// Refresh the pilots and leaders after a team change or vessel breaks or disappears.
        /// Note: team changes don't always seem to trigger this, but vessel loss does.
        /// </summary>
        /// <param name="pilots"></param>
        /// <param name="leaders"></param>
        /// <param name="followLeaders"></param>
        /// <returns></returns>
        List<string> RefreshPilots(out Dictionary<BDTeam, List<IBDAIControl>> pilots, out List<IBDAIControl> leaders, bool followLeaders)
        {
            var allPilots = GetAllPilots();
            var teams = allPilots.Select(p => p.WeaponManager.Team).ToHashSet(); // Unique list
            pilots = teams.ToDictionary(t => t, t => allPilots.Where(p => p.WeaponManager.Team == t).ToList());
            leaders = pilots.Select(kvp => kvp.Value.First()).ToList();
            if (followLeaders)
            {
                foreach (var leader in leaders)
                    if (leader.currentCommand != PilotCommands.Free)
                        leader.WeaponManager.wingCommander.CommandAllFollow();
            }
            return leaders.Select(l => l.vessel.vesselName).ToList();
        }

        public string currentMutator;

        public void ConfigureMutator()
        {
            currentMutator = string.Empty;

            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: MutatorMode enabled; Mutator count = " + BDArmorySettings.MUTATOR_LIST.Count);
            var indices = Enumerable.Range(0, BDArmorySettings.MUTATOR_LIST.Count).ToList();
            indices.Shuffle();
            currentMutator = string.Join("; ", indices.Take(BDArmorySettings.MUTATOR_APPLY_NUM).Select(i => MutatorInfo.mutators[BDArmorySettings.MUTATOR_LIST[i]].name)); //no check if mutator_list contains a mutator not defined in the loaded mutatordefs
            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log($"[BDArmory.BDACompetitionMode: {CompetitionID.ToString()}: current mutators: {currentMutator}");
            MutatorResetTime = Planetarium.GetUniversalTime();
            if (BDArmorySettings.MUTATOR_APPLY_GLOBAL) //selected mutator applied globally
            {
                ScreenMessages.PostScreenMessage(StringUtils.Localize("#LOC_BDArmory_UI_MutatorStart") + ": " + currentMutator + ". " + (BDArmorySettings.MUTATOR_APPLY_TIMER ? (BDArmorySettings.MUTATOR_DURATION > 0 ? BDArmorySettings.MUTATOR_DURATION * 60 : BDArmorySettings.COMPETITION_DURATION * 60) + " seconds left" : ""), 5, ScreenMessageStyle.UPPER_CENTER);
            }
        }
        /*
        //Announcer function for playing sequential soundclips on kill
        public void PlayAnnouncer(int killcount, bool headshot, string killerVessel)
        {
            if (FlightGlobals.ActiveVessel.vesselName != killerVessel) return;
            if (!BDArmorySettings.GG_ANNOUNCER) return;
            killcount -= 1; //first bark is doublekill, adjust to account for that
            if (headshot) audioSource.PlayOneShot(headshotClip);
            else
            {
                if (killcount > announcerBarks.Count - 1) killcount = announcerBarks.Count - 1;
                if (killcount >= 0)
                {
                    if (announcerBarks[killcount] != null) audioSource.PlayOneShot(announcerBarks[killcount]); //only play barks if killsThisLife > 1
                }
            }
        }
        */
        #region Vessel validity
        public enum InvalidVesselReason { None, NullVessel, NoAI, NoWeaponManager, NoCommand };
        /// <summary>
        /// Check that a vessel is valid for a competition.
        /// </summary>
        /// <param name="vessel"></param>
        /// <param name="attemptFix"></param>
        /// <returns></returns>
        public InvalidVesselReason IsValidVessel(Vessel vessel, bool attemptFix = true)
        {
            if (vessel == null)
                return InvalidVesselReason.NullVessel;
            var ac = vessel.ActiveController();
            if (ac == null || ac.WM == null) // Check for a weapon manager.
                return InvalidVesselReason.NoWeaponManager;
            if (ac.AI == null) // Check for an AI.
                return InvalidVesselReason.NoAI;
            if (attemptFix && VesselModuleRegistry.GetModuleCount<ModuleCommand>(vessel) == 0 && VesselModuleRegistry.GetModuleCount<KerbalSeat>(vessel) == 0) // Check for a cockpit or command seat.
                CheckVesselType(vessel); // Attempt to fix it.
            if (VesselModuleRegistry.GetModuleCount<ModuleCommand>(vessel) == 0 && VesselModuleRegistry.GetModuleCount<KerbalSeat>(vessel) == 0) // Check for a cockpit or command seat again.
                return InvalidVesselReason.NoCommand;
            return InvalidVesselReason.None;
        }

        void OnCrewOnEVA(GameEvents.FromToAction<Part, Part> fromToAction)
        {
            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log($"[BDArmory.BDACompetitionMode]: {fromToAction.to} went on EVA from {fromToAction.from}");
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
            if (VesselModuleRegistry.IgnoredVesselTypes.Contains(vessel.vesselType)) return;
            if (!BDArmorySettings.AUTONOMOUS_COMBAT_SEATS) CheckForAutonomousCombatSeat(vessel);
            if (BDArmorySettings.DESTROY_UNCONTROLLED_WMS) CheckForUncontrolledVessel(vessel);
            if (BDArmorySettings.COMPETITION_GM_KILL_TIME > -1 && (BDArmorySettings.COMPETITION_GM_KILL_WEAPON || BDArmorySettings.COMPETITION_GM_KILL_ENGINE || BDArmorySettings.COMPETITION_GM_KILL_DISABLED || (BDArmorySettings.COMPETITION_GM_KILL_HP > 0))) CheckForGMCulling(vessel);
        }

        public void CheckVesselType(Vessel vessel)
        {
            if (!BDArmorySettings.RUNWAY_PROJECT) return;
            if (vessel != null && vessel.vesselName != null)
            {
                var vesselTypeIsValid = VesselModuleRegistry.ValidVesselTypes.Contains(vessel.vesselType);
                if (!vesselTypeIsValid && vessel.ActiveController().WM != null) // Found an invalid vessel type with a weapon manager.
                {
                    var message = $"Found weapon manager on {vessel.vesselName} of type {vessel.vesselType}";
                    if (vessel.vesselName.EndsWith($" {vessel.vesselType}"))
                        vessel.vesselName = vessel.vesselName.Remove(vessel.vesselName.Length - vessel.vesselType.ToString().Length - 1);
                    vessel.vesselType = VesselType.Plane;
                    message += $", changing vessel name and type to {vessel.vesselName}, {vessel.vesselType}";
                    Debug.Log("[BDArmory.BDACompetitionMode]: " + message);
                    return;
                }
                if (vesselTypeIsValid)
                {
                    if (vessel.vesselName.EndsWith($" {vessel.vesselType}") && !Scores.Players.Contains(vessel.vesselName) && Scores.Players.Contains(vessel.vesselName.Remove(vessel.vesselName.Length - $" {vessel.vesselType}".Length)) && IsValidVessel(vessel, false) == InvalidVesselReason.None)
                    {
                        var message = $"Found a valid vessel ({vessel.vesselName}) tagged with '{vessel.vesselType}' when it shouldn't be, renaming.";
                        Debug.Log("[BDArmory.BDACompetitionMode]: " + message);
                        vessel.vesselName = vessel.vesselName.Remove(vessel.vesselName.Length - $" {vessel.vesselType}".Length);
                        return;
                    }
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
                    Debug.Log($"[BDArmory.BDACompetitionMode]: Found a lone combat seat ({vessel.vesselName}), killing it.");
                    PartExploderSystem.AddPartToExplode(vessel.parts[0]);
                    return;
                }
                // Check for a lack of control.
                var AI = vessel.ActiveController().AI;
                if (VesselModuleRegistry.GetModuleCount<KerbalEVA>(vessel) == 0 && AI != null && AI.pilotEnabled) // If not controlled by a kerbalEVA in a KerbalSeat, check the regular ModuleCommand parts.
                {
                    if (VesselModuleRegistry.GetModules<ModuleCommand>(vessel).All(c => c.GetControlSourceState() == CommNet.VesselControlState.None))
                    {
                        Debug.Log($"[BDArmory.BDACompetitionMode]: Kerbal has left the seat of {vessel.vesselName} and it has no other controls, disabling the AI.");
                        AI.DeactivatePilot();
                    }
                }//no srfAI/VTOLAI/OAI crew check? FIXME later
            }
        }

        void CheckForUncontrolledVessel(Vessel vessel)
        {
            if (vessel == null || vessel.vesselName == null) return;
            if (vessel.ActiveController().WM == null) return; // The weapon managers are already dead.
            if (vessel.GetName().Contains(BDArmorySettings.PINATA_NAME)) return; //don't delete uncontrolled pinata
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
        void CheckForGMCulling(Vessel vessel)
        {
            if (vessel == null || vessel.vesselName == null) return;
            if (BDArmorySettings.COMPETITION_GM_KILL_ENGINE)
            {
                if (SpawnUtils.CountActiveEngines(vessel, true) == 0)
                    StartCoroutine(DelayedGMKill(vessel, BDArmorySettings.COMPETITION_GM_KILL_TIME, " lost all engines. Terminated by GM."));
            }
            if (BDArmorySettings.COMPETITION_GM_KILL_WEAPON)
            {
                var mf = vessel.ActiveController().WM;
                if (mf != null)
                {
                    if (!vessel.IsControllable || !mf.HasWeaponsAndAmmo()) // Check first for not controllable or no weapons or ammo
                        StartCoroutine(DelayedGMKill(vessel, BDArmorySettings.COMPETITION_GM_KILL_TIME, " lost all weapons. Terminated by GM."));
                }
            }
            if (BDArmorySettings.COMPETITION_GM_KILL_DISABLED)
            {
                var mf = vessel.ActiveController().WM;
                if (mf != null)
                {
                    if (!vessel.IsControllable || !mf.HasWeaponsAndAmmo()) // Check first for not controllable or no weapons or ammo
                        StartCoroutine(DelayedGMKill(vessel, BDArmorySettings.COMPETITION_GM_KILL_TIME, " lost all weapons or ammo. Terminated by GM."));
                    else // Check for engines first, then wheels for tanks/amphibious if needed 
                    {
                        if (SpawnUtils.CountActiveEngines(vessel, true) == 0)
                        {
                            var surfaceAI = vessel.ActiveController().SurfaceAI; // Get the surface AI if the vessel has one.
                            if (surfaceAI == null || !surfaceAI.pilotEnabled) // No engines on an AI that needs them, craft is disabled
                                StartCoroutine(DelayedGMKill(vessel, BDArmorySettings.COMPETITION_GM_KILL_TIME, " lost all engines. Terminated by GM."));
                            else if ((surfaceAI.SurfaceType & AIUtils.VehicleMovementType.Land) != 0) // Check for wheels on craft capable of moving on land
                            {
                                if ((VesselModuleRegistry.GetModuleCount<ModuleWheelBase>(vessel) +
                                        VesselModuleRegistry.GetModuleCount(vessel, "KSPWheelBase") +
                                        VesselModuleRegistry.GetModuleCount(vessel, "FSwheel")) == 0)
                                    StartCoroutine(DelayedGMKill(vessel, BDArmorySettings.COMPETITION_GM_KILL_TIME, " lost wheels or tracks. Terminated by GM."));
                            }
                        }
                    }
                }
            }
            if (BDArmorySettings.COMPETITION_GM_KILL_HP > 0)
            {
                var mf = vessel.ActiveController().WM;
                if (mf != null)
                    if (mf.currentHP / mf.totalHP * 100 < BDArmorySettings.COMPETITION_GM_KILL_HP)
                        StartCoroutine(DelayedGMKill(vessel, BDArmorySettings.COMPETITION_GM_KILL_TIME, " crippled. Terminated by GM."));
            }
        }

        enum UncontrolledReason { Uncontrolled, Bricked };
        HashSet<Vessel> explodingWM = [];
        IEnumerator DelayedExplodeWMs(Vessel vessel, float delay = 1f, UncontrolledReason reason = UncontrolledReason.Uncontrolled)
        {
            if (explodingWM.Contains(vessel)) yield break; // Already scheduled for exploding.
            explodingWM.Add(vessel);
            yield return new WaitForSecondsFixed(delay);
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

        public IEnumerator DelayedGMKill(Vessel vessel, float delay, string killReason)
        {
            if (explodingWM.Contains(vessel)) yield break; // Already scheduled for exploding.
            explodingWM.Add(vessel);
            yield return new WaitForSecondsFixed(delay);
            if (vessel == null) // It's already dead.
            {
                explodingWM = [.. explodingWM.Where(v => v != null)]; // Clean the hashset.
                yield break;
            }
            if (killReason.Contains("engines") && SpawnUtils.CountActiveEngines(vessel, true) != 0)
            {
                explodingWM.Remove(vessel); //reset this so future DelayedGMKill calls don't immediately abort
                yield break; //engine(s) (re)activated since delayedGMKill triggered, abort
            }

            var vesselName = vessel.GetName();
            var killerName = "";
            if (Scores.Players.Contains(vesselName))
            {
                killerName = Scores.ScoreData[vesselName].lastPersonWhoDamagedMe;
                if (killerName == "")
                {
                    Scores.ScoreData[vesselName].lastPersonWhoDamagedMe = "Killed by GM"; // only do this if it's not already damaged
                    killerName = "Killed By GM";
                }
                Scores.RegisterDeath(vesselName, GMKillReason.GM);
                competitionStatus.Add(vesselName + killReason);
            }
            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: " + vesselName + ":REMOVED:" + killerName);
            VesselUtils.ForceDeadVessel(vessel);
            explodingWM.Remove(vessel);
        }

        void CheckForBadlyNamedVessels()
        {
            foreach (var wm in LoadedVesselSwitcher.Instance.WeaponManagers.SelectMany(tm => tm.Value).Where(wm => wm != null).ToList())
                if (wm != null && wm.vessel != null && wm.vessel.vesselName != null && VesselModuleRegistry.ValidVesselTypes.Contains(wm.vessel.vesselType))
                {
                    if (wm.vessel.vesselName.EndsWith($" {wm.vessel.vesselType}") && !Scores.Players.Contains(wm.vessel.vesselName) && Scores.Players.Contains(wm.vessel.vesselName.Remove(wm.vessel.vesselName.Length - $" {wm.vessel.vesselType}".Length)) && IsValidVessel(wm.vessel) == InvalidVesselReason.None)
                    {
                        var message = "Found a valid vessel (" + wm.vessel.vesselName + ") tagged with 'Plane' when it shouldn't be, renaming.";
                        Debug.Log("[BDArmory.BDACompetitionMode]: " + message);
                        wm.vessel.vesselName = wm.vessel.vesselName.Remove(wm.vessel.vesselName.Length - $" {wm.vessel.vesselType}".Length);
                    }
                }
        }
        #endregion

        #region Runway Project
        public bool killerGMenabled = false;
        public bool hasPinata = false;
        public bool pinataAlive = false;
        public bool s4r1FiringRateUpdatedFromShotThisFrame = false;
        public bool s4r1FiringRateUpdatedFromHitThisFrame = false;

        public void StartRapidDeployment(float distance, string tag = "")
        {
            if (!BDArmorySettings.RUNWAY_PROJECT) return;
            if (sequencedCompetitionStarting) return;
            ResetCompetitionStuff(tag);
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
                case 33: //S1R7/S3R3 Rapid deployment I/II
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
                case 53: //change this later (orbital deployment)
                    commandSequence = new List<string>{
                        "0:ActionGroup:13:1", // t=0, AG4 - Enable SAS
                        "0:ActionGroup:16:0", // t=0, Retract gear (if it's not retracted)
                        "0:ActionGroup:14:0", // t=0, Disable brakes
                        "0:ActionGroup:10", // t=30, AG10
                        "0:ActivateEngines", // t=30, Activate engines
                        "0:HackGravity:10", // t=0, Increase gravity to 10x
                        "0:TimeScale:2", // t=0, scale time for faster falling
                        "0:ToggleGuard:0", // t=0, Disable guard mode (for those who triggered it early)
                        "0:TogglePilot:0", // t=0, Disable pilots (for those who triggered it early)
                        "30:HackGravity:1", //t=30, Reset gravity
                        "0:TimeScale:1", // t=0, reset time scaling
                        "0:SetThrottle:100", // t=30, Full throttle
                        "0:TogglePilot:1", // t=30, Activate pilots
                        "0:AttackCenter", // t=30, "Attack" center point
                        "0:ToggleGuard:53", // t=30+, Activate guard mode (attack) (delayed)
                        "0:RemoveDebris", // t=30, Remove any other debris and spectators
                        "0:ActivateCompetition", // t=30, mark the competition as active
                        // "30:EnableGM", // t=60, Activate the killer GM
                    };
                    altitudeLimitGracePeriod = 30; // t=60 (30s after the competition starts), activate the altitude limit
                    break;
                case 67: //Asteroid Interception
                    commandSequence = new List<string>{
                        "0:ActionGroup:13:1", // t=0, AG4 - Enable SAS
                        "0:ActionGroup:16:0", // t=0, Retract gear (if it's not retracted)
                        "0:ActionGroup:10", // t=0, AG10
                        "0:ActivateEngines", // t=0, Activate engines
                        "0:SetThrottle:100", // t=0, Full throttle
                        "0:TogglePilot:1", // t=0, Activate pilots
                        "0:SetTeam:1",      //t=0, Set everyone to same team
                        "0:ToggleGuard:1", // t=0, Activate guard mode (attack)
                        "5:RemoveDebris", // t=5, Remove any other debris and spectators
                        // "0:EnableGM", // t=60, Activate the killer GM
                    };
                    break;
                case 77: //Shuttle launch
                    commandSequence = new List<string>{
                        "0:ActionGroup:13:1", // t=0, AG4 - Enable SAS
                        "0:ActionGroup:16:0", // t=0, Retract gear (if it's not retracted)
                        "0:ActionGroup:14:0", // t=0, Disable brakes
                        "0:ActionGroup:10", // t=0, AG10
                        "0:ActivateEngines", // t=0, Activate engines
                        "0:SetThrottle:100", // t=0, Full throttle
                        "0:TogglePilot:1", // t=30, Activate pilots
                        "0:AttackCenter", // t=30, "Attack" center point
                        "0:ToggleGuard:0", // t=0, Disable guard mode (for those who triggered it early)
                        "0:ToggleGuard:77", // t=30, Activate guard mode (attack)
                        "5:RemoveDebris", // t=35, Remove any other debris and spectators
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

                if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: UNSHIELDED:" + notShieldedCount.ToString() + ":" + pilot.vessel.GetName());
                if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: MASS:" + mass.ToString() + ":" + pilot.vessel.GetName());
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
                                    if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: RESOURCEUPDATE:" + pilot.vessel.GetName() + ":" + resources.Current.amount);
                                    massAdded = true;
                                }
                            }
                        if (massAdded) break;
                    }
            }
        }

        IEnumerator SequencedCompetition(List<string> commandSequence)
        {
            var pilots = GetAllPilots(); // We don't check the number of pilots here so that the sequence can be done with a single pilot. Instead, we check later before actually starting the competition.
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
                yield return new WaitWhile(() => (Planetarium.GetUniversalTime() < nextStep));

                pilots = pilots.Where(pilot => pilot != null && pilot.vessel != null && gameObject != null).ToList(); // Clear out any dead pilots. (Apparently we also need to check the gameObject!)

                var command = parts[1];

                switch (command)
                {
                    case "Stage":
                        {
                            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Staging.");
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
                                if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Competition Command not parsed correctly " + cmdEvent);
                                StopCompetition();
                                yield break;
                            }
                            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Jiggling action group " + parts[2] + ".");
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
                            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Toggling autopilot.");
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
                            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Toggling guard mode.");
                            if (parts.Count() == 3)
                            {
                                switch (parts[2])
                                {
                                    case "0":
                                    case "1":
                                        var newState = true;
                                        if (parts[2] == "0")
                                        {
                                            newState = false;
                                        }
                                        foreach (var pilot in pilots)
                                        {
                                            if (pilot.WeaponManager != null && pilot.WeaponManager.guardMode != newState)
                                            {
                                                pilot.WeaponManager.ToggleGuardMode();
                                                if (!pilot.WeaponManager.guardMode) pilot.WeaponManager.SetTarget(null);
                                            }
                                        }
                                        break;
                                    case "53": // Orbital deployment
                                        var limit = (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH < 20f ? BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH / 10f : BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH < 39f ? BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH - 18f : (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH - 38f) * 5f + 20f) * 1000f;
                                        foreach (var pilot in pilots)
                                            StartCoroutine(EnableGuardModeWhen(pilot, () => (pilot == null || pilot.vessel == null || pilot.vessel.radarAltitude < limit)));
                                        break;
                                    case "77": // Shuttle Launch
                                        foreach (var pilot in pilots)
                                            StartCoroutine(EnableGuardModeWhen(pilot, () => (pilot == null || pilot.vessel == null || pilot.vessel.radarAltitude > BDArmorySettings.GUARD_MODE_TRIGGER_ALT)));
                                        break;
                                }
                            }
                            else // FIXME This branch isn't taken as all the ToggleGuard commands have 3 parts.
                            {
                                foreach (var pilot in pilots)
                                {
                                    if (pilot.WeaponManager != null)
                                    {
                                        pilot.WeaponManager.ToggleGuardMode();
                                        if (!pilot.WeaponManager.guardMode) pilot.WeaponManager.SetTarget(null);
                                    }
                                    if (BDArmorySettings.HACK_INTAKES) SpawnUtils.HackIntakes(pilot.vessel, true);
                                    if (BDArmorySettings.MUTATOR_MODE) SpawnUtils.ApplyMutators(pilot.vessel, true);
                                    if (BDArmorySettings.ENABLE_HOS) SpawnUtils.ApplyHOS(pilot.vessel);
                                    if (BDArmorySettings.RUNWAY_PROJECT) SpawnUtils.ApplyRWP(pilot.vessel);
                                    if (BDArmorySettings.COMP_CONVENIENCE_CHECKS) SpawnUtils.ApplyCompSettingsChecks(pilot.vessel);
                                    /*
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
                                    if (BDArmorySettings.RUNWAY_PROJECT)
                                    {
                                        float torqueQuantity = 0;
                                        int APSquantity = 0;
                                        SpawnUtils.HackActuators(pilot.vessel, true);

                                        using (List<Part>.Enumerator part = pilot.vessel.Parts.GetEnumerator())
                                            while (part.MoveNext())
                                            {
                                                if (part.Current.GetComponent<ModuleReactionWheel>() != null)
                                                {
                                                    ModuleReactionWheel SAS;
                                                    SAS = part.Current.GetComponent<ModuleReactionWheel>();
                                                    if (part.Current.CrewCapacity == 0)
                                                    {
                                                        torqueQuantity += ((SAS.PitchTorque + SAS.RollTorque + SAS.YawTorque) / 3) * (SAS.authorityLimiter / 100);
                                                        if (torqueQuantity > BDArmorySettings.MAX_SAS_TORQUE)
                                                        {
                                                            float excessTorque = torqueQuantity - BDArmorySettings.MAX_SAS_TORQUE;
                                                            SAS.authorityLimiter = 100 - Mathf.Clamp(((excessTorque / ((SAS.PitchTorque + SAS.RollTorque + SAS.YawTorque) / 3)) * 100), 0, 100);
                                                        }
                                                    }
                                                }
                                                if (part.Current.GetComponent<ModuleCommand>() != null)
                                                {
                                                    ModuleCommand MC;
                                                    MC = part.Current.GetComponent<ModuleCommand>();
                                                    if (part.Current.CrewCapacity == 0 && MC.minimumCrew == 0) //Drone core, nuke it
                                                        part.Current.RemoveModule(MC);
                                                }
                                                if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 59)
                                                {
                                                    if (part.Current.GetComponent<ModuleWeapon>() != null)
                                                    {
                                                        ModuleWeapon gun;
                                                        gun = part.Current.GetComponent<ModuleWeapon>();
                                                        if (gun.isAPS) APSquantity++;
                                                        if (APSquantity > 4)
                                                        {
                                                            part.Current.RemoveModule(gun);
                                                            IEnumerator<PartResource> resource = part.Current.Resources.GetEnumerator();
                                                            while (resource.MoveNext())
                                                            {
                                                                if (resource.Current == null) continue;
                                                                if (resource.Current.flowState)
                                                                {
                                                                    resource.Current.flowState = false;
                                                                }
                                                            }
                                                            resource.Dispose();
                                                        }
                                                    }
                                                }
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
                                                    if (BDArmorySettings.HOS_SAS)
                                                    {
                                                        if (part.Current.GetComponent<ModuleReactionWheel>() != null)
                                                        {
                                                            ModuleReactionWheel SAS;
                                                            SAS = part.Current.GetComponent<ModuleReactionWheel>();
                                                            part.Current.RemoveModule(SAS);
                                                        }
                                                    }
                                                }
                                            if (!string.IsNullOrEmpty(BDArmorySettings.HOS_MUTATOR))
                                            {
                                                var MM = pilot.vessel.rootPart.FindModuleImplementing<BDAMutator>();
                                                if (MM == null)
                                                {
                                                    MM = (BDAMutator)pilot.vessel.rootPart.AddModule("BDAMutator");
                                                }
                                                    MM.EnableMutator(BDArmorySettings.HOS_MUTATOR, true);
                                            }
                                        }
                                    }*/
                                }
                            }
                            break;
                        }
                    case "AttackCenter":
                        {
                            Vector3 center = Vector3.zero;
                            foreach (var pilot in pilots) center += pilot.vessel.CoM;
                            center /= pilots.Count;
                            Vector3 centerGPS = VectorUtils.WorldPositionToGeoCoords(center, FlightGlobals.currentMainBody);
                            Vector3 attackGPS;
                            foreach (var pilot in pilots)
                            {
                                attackGPS = centerGPS;
                                var pAI = pilot.vessel.ActiveController().PilotAI;
                                if (pAI != null)
                                {
                                    attackGPS.z = (float)BodyUtils.GetTerrainAltitudeAtPos(center) + 1000; // Target 1km above the terrain at the center.
                                    if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 77) pAI.minAltitude = 5; //set minAlt to 5 so AI doesn't go into Gaining Alt routine while below MinAlt and will maintain a stright-up course
                                }
                                pilot.ReleaseCommand(false, true);
                                pilot.CommandAttack(attackGPS);
                            }
                            break;
                        }
                    case "SetTeam":
                        {
                            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: setting team.");
                            SpawnUtils.SaveTeams();
                            foreach (var pilot in pilots)
                            {
                                if (!string.IsNullOrEmpty(BDArmorySettings.PINATA_NAME) && hasPinata)
                                {
                                    if (!pilot.vessel.GetName().Contains(BDArmorySettings.PINATA_NAME))
                                        pilot.WeaponManager.SetTeam(BDTeam.Get("PinataPoppers"));
                                    else
                                        pilot.WeaponManager.SetTeam(BDTeam.Get("Pinata"));
                                }
                                Scores.ScoreData[pilot.vessel.vesselName].team = pilot.WeaponManager.Team.Name;
                            }
                            break;
                        }
                    case "SetThrottle":
                        {
                            if (parts.Count() == 3 && pilots.Count > 1)
                            {
                                if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Adjusting throttle to " + parts[2] + "%.");
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
                            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Removing debris and non-competitors.");
                            // remove anything that doesn't contain BD Armory modules
                            RemoveNonCompetitors(true);
                            RemoveDebrisNow();
                            break;
                        }
                    case "RemoveFairings":
                        {
                            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Removing fairings.");
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
                            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Activating killer GM.");
                            killerGMenabled = true;
                            decisionTick = BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY > 60 ? -1 : Planetarium.GetUniversalTime() + BDArmorySettings.COMPETITION_KILLER_GM_FREQUENCY;
                            ResetSpeeds();
                            break;
                        }
                    case "ActivateEngines":
                        {
                            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Activating engines.");
                            foreach (var pilot in pilots)
                            {
                                if (!BDArmorySettings.NO_ENGINES && SpawnUtils.CountActiveEngines(pilot.vessel) == 0) // If the vessel didn't activate their engines on AG10, then activate all their engines and hope for the best.
                                {
                                    if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: " + pilot.vessel.GetName() + " didn't activate engines on AG10! Activating ALL their engines.");
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
                            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Performing mass trim.");
                            DoRapidDeploymentMassTrim();
                            break;
                        }
                    case "HackGravity":
                        {
                            if (parts.Count() == 3)
                            {
                                if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Adjusting gravity to " + parts[2] + "x.");
                                double grav = double.Parse(parts[2]);
                                PhysicsGlobals.GraviticForceMultiplier = grav;
                                VehiclePhysics.Gravity.Refresh();
                                competitionStatus.Add("Competition: Adjusting gravity to " + grav.ToString("0.0") + "G!");
                            }
                            break;
                        }
                    case "ActivateCompetition":
                        {
                            if (!competitionIsActive && pilots.Count > 1)
                            {
                                competitionStatus.Add("Competition starting!  Good luck!");
                                CompetitionStarted();
                            }
                            break;
                        }
                    case "TimeScale":
                        {
                            if (parts.Count() == 3)
                                Time.timeScale = float.Parse(parts[2]);
                            else
                                Time.timeScale = 1f;
                            break;
                        }
                    default:
                        {
                            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Unknown sequenced command: " + command + ".");
                            StopCompetition();
                            yield break;
                        }
                }
            }
            // will need a terminator routine
            if (pilots.Count < 2)
            {
                Debug.Log("[BDArmory.BDACompetitionMode" + CompetitionID.ToString() + "]: Unable to start sequenced competition - one or more teams is empty");
                competitionStatus.Set("Competition: Failed!  One or more teams is empty.");
                competitionStartFailureReason = CompetitionStartFailureReason.OnlyOneTeam;
                StopCompetition();
                yield break;
            }
            if (!competitionIsActive)
            {
                competitionStatus.Add("Competition starting!  Good luck!");
                CompetitionStarted();
            }
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
            // arbitrary and capricious decisions of life and death

            bool hasFired = true;
            Vessel worstVessel = null;
            double slowestSpeed = 100000;
            int vesselCount = 0;
            using (var loadedVessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedVessels.MoveNext())
                {
                    if (loadedVessels.Current == null || !loadedVessels.Current.loaded || VesselModuleRegistry.IgnoredVesselTypes.Contains(loadedVessels.Current.vesselType))
                        continue;
                    IBDAIControl pilot = loadedVessels.Current.ActiveController().AI;
                    if (pilot == null || !pilot.WeaponManager || pilot.WeaponManager.Team.Neutral)
                        continue;

                    var vesselName = loadedVessels.Current.GetName();
                    if (!Scores.Players.Contains(vesselName))
                        continue;

                    vesselCount++;
                    ScoringData vData = Scores.ScoreData[vesselName];

                    var averageSpeed = vData.AverageSpeed / vData.averageCount;
                    var averageAltitude = vData.AverageAltitude / vData.averageCount;
                    averageSpeed = averageAltitude + (averageSpeed * averageSpeed / 200); // kinetic & potential energy
                    if (pilot.WeaponManager != null)
                    {
                        if (!pilot.WeaponManager.guardMode) averageSpeed *= 0.5;
                    }

                    bool vesselNotFired = (Planetarium.GetUniversalTime() - vData.lastFiredTime) > 120; // if you can't shoot in 2 minutes you're at the front of line

                    if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: Victim Check " + vesselName + " " + averageSpeed.ToString() + " " + vesselNotFired.ToString());
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
                if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: GM killing " + vesselName + " for being too slow.");
                VesselUtils.ForceDeadVessel(worstVessel);
            }
            ResetSpeeds();
        }

        private void CheckAltitudeLimits() //have ths start a timer if alt exceeded, instead of immediately kill? Timing/kill elements would need to be moved to MissileFire, but doable.
        {
            if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH < 55f) // Kill off those flying too high.
            {
                var limit = (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH < 20f ? BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH / 10f : BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH < 39f ? BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH - 18f : (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_HIGH - 38f) * 5f + 20f) * 1000f;
                foreach (var weaponManager in LoadedVesselSwitcher.Instance.WeaponManagers.SelectMany(tm => tm.Value).ToList())
                {
                    if (!Scores.ScoreData.ContainsKey(weaponManager.vessel.vesselName)) continue;
                    if (alive.Contains(weaponManager.vessel.vesselName) && BDArmorySettings.COMPETITION_ALTITUDE__LIMIT_ASL ? weaponManager.vessel.altitude > limit : weaponManager.vessel.radarAltitude > limit)
                    {
                        if (Scores.ScoreData[weaponManager.vessel.vesselName].AltitudeKillTimer == 0)
                        {
                            Scores.ScoreData[weaponManager.vessel.vesselName].AltitudeKillTimer = Planetarium.GetUniversalTime(); ;
                        }
                        /*
                        var killerName = Scores.ScoreData[weaponManager.vessel.vesselName].lastPersonWhoDamagedMe;
                        if (killerName == "")
                        {
                            killerName = "Flew too high!";
                            Scores.ScoreData[weaponManager.vessel.vesselName].lastPersonWhoDamagedMe = killerName;
                        }
                        Scores.RegisterDeath(weaponManager.vessel.vesselName, GMKillReason.GM);
                        competitionStatus.Add(weaponManager.vessel.vesselName + " flew too high!");
                        if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: " + weaponManager.vessel.vesselName + ":REMOVED:" + killerName);
                        if (KillTimer.ContainsKey(weaponManager.vessel.vesselName)) KillTimer.Remove(weaponManager.vessel.vesselName);
                        VesselUtils.ForceDeadVessel(weaponManager.vessel);
                        */
                    }
                    else
                    {
                        if (Scores.ScoreData[weaponManager.vessel.vesselName].AltitudeKillTimer != 0)
                        {
                            // safely below ceiling for 15 seconds
                            if (Planetarium.GetUniversalTime() - Scores.ScoreData[weaponManager.vessel.vesselName].AltitudeKillTimer > 0)
                            {
                                Scores.ScoreData[weaponManager.vessel.vesselName].AltitudeKillTimer = 0;
                            }
                        }
                    }
                }
            }
            if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW > -39f || BDArmorySettings.ALTITUDE_HACKS) // Kill off those flying too low.
            {
                float limit;
                if (BDArmorySettings.ALTITUDE_HACKS)
                {
                    limit = MinAlt;
                }
                else
                {
                    if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < -28f) limit = (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW + 28f) * 1000f; // -10km — -1km @ 1km
                    else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < -19f) limit = (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW + 19f) * 100f; // -900m — -100m @ 100m
                    else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < 0f) limit = BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW * 5f; // -95m — -5m  @ 5m
                    else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < 20f) limit = BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW * 100f; // 0m — 1900m @ 100m
                    else if (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW < 39f) limit = (BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW - 18f) * 1000f; // 2km — 20km @ 1km
                    else limit = ((BDArmorySettings.COMPETITION_ALTITUDE_LIMIT_LOW - 38f) * 5f + 20f) * 1000f; // 25km — 50km @ 5km
                }
                foreach (var weaponManager in LoadedVesselSwitcher.Instance.WeaponManagers.SelectMany(tm => tm.Value).ToList())
                {
                    if (!Scores.ScoreData.ContainsKey(weaponManager.vessel.vesselName)) continue;
                    if (alive.Contains(weaponManager.vessel.vesselName) && BDArmorySettings.COMPETITION_ALTITUDE__LIMIT_ASL ? weaponManager.vessel.altitude < limit : weaponManager.vessel.radarAltitude < limit)
                    {
                        if (Scores.ScoreData[weaponManager.vessel.vesselName].AltitudeKillTimer == 0)
                        {
                            Scores.ScoreData[weaponManager.vessel.vesselName].AltitudeKillTimer = Planetarium.GetUniversalTime(); ;
                        }
                        /*
                        var killerName = Scores.ScoreData[weaponManager.vessel.vesselName].lastPersonWhoDamagedMe;
                        if (killerName == "")
                        {
                            killerName = "Flew too low!";
                            Scores.ScoreData[weaponManager.vessel.vesselName].lastPersonWhoDamagedMe = killerName;
                        }
                        Scores.RegisterDeath(weaponManager.vessel.vesselName, GMKillReason.GM);
                        competitionStatus.Add(weaponManager.vessel.vesselName + " flew too low!");
                        if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: " + weaponManager.vessel.vesselName + ":REMOVED:" + killerName);
                        if (KillTimer.ContainsKey(weaponManager.vessel.vesselName)) KillTimer.Remove(weaponManager.vessel.vesselName);
                        VesselUtils.ForceDeadVessel(weaponManager.vessel);
                        */
                    }
                    else
                    {

                        if (Scores.ScoreData[weaponManager.vessel.vesselName].AltitudeKillTimer != 0)
                        {
                            // safely below ceiling for 15 seconds
                            if (Planetarium.GetUniversalTime() - Scores.ScoreData[weaponManager.vessel.vesselName].AltitudeKillTimer > 0)
                            {
                                Scores.ScoreData[weaponManager.vessel.vesselName].AltitudeKillTimer = 0;
                            }
                        }
                    }
                }
            }
        }
        // reset all the tracked speeds, and copy the shot clock over, because I wanted 2 minutes of shooting to count
        private void ResetSpeeds()
        {
            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "] resetting kill clock");
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

        List<MissileFire> craftToCull = [];
        void CullSlowWaypointRunners(double threshold)
        {
            //if (BDArmorySettings.WAYPOINT_GUARD_INDEX >= 0) return;
            var now = Planetarium.GetUniversalTime();
            craftToCull.Clear();
            foreach (var weaponManager in LoadedVesselSwitcher.Instance.WeaponManagers.SelectMany(tm => tm.Value).ToList())
            {
                if (weaponManager == null || weaponManager.vessel == null) continue;
                var ai = weaponManager.AI as BDGenericAIBase;
                if (ai == null || !ai.IsRunningWaypoints) continue;
                var player = weaponManager.vessel.vesselName;
                if (!Scores.Players.Contains(player)) continue;
                if (Scores.ScoreData[player].waypointsReached.Count == 0) // Hasn't reached the first waypoint.
                {
                    if (now - competitionStartTime > threshold)
                    {
                        // Debug.Log($"DEBUG Culling {player} due to {now - competitionStartTime}s since competition start and no waypoint reached. now: {now}, comp. start time: {competitionStartTime}");
                        craftToCull.Add(weaponManager);
                    }
                }
                else if (now - competitionStartTime - Scores.ScoreData[player].waypointsReached.Last().timestamp > threshold)
                {
                    // Debug.Log($"DEBUG Culling {player} due to {now - competitionStartTime - Scores.ScoreData[player].waypointsReached.Last().timestamp}s since last waypoint reached, now: {now}, last: {Scores.ScoreData[player].waypointsReached.Last().timestamp}, WP passed: {Scores.ScoreData[player].waypointsReached.Count}, comp. start time: {competitionStartTime}");
                    craftToCull.Add(weaponManager);
                }
            }
            foreach (var weaponManager in craftToCull)
            {
                var vesselName = weaponManager.vessel.vesselName;
                Scores.ScoreData[vesselName].lastPersonWhoDamagedMe = $"Failed to reach a waypoint within {threshold:0}s";
                Scores.RegisterDeath(vesselName, GMKillReason.BigRedButton); // Mark it as a Big Red Button GM kill.
                var message = $"{vesselName} failed to reach a waypoint within {threshold:0}s, killing it.";
                if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.WAYPOINTS_MODE) message = $"{vesselName} failed to reach a waypoint within {threshold:0}s and was killed by {(BDArmorySettings.RUNWAY_PROJECT_ROUND == 55 ? "a Tusken Raider" : "the GM")}.";
                competitionStatus.Add(message);
                Debug.Log($"[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: " + message);
                VesselUtils.ForceDeadVessel(weaponManager.vessel);
            }
            craftToCull.Clear();
        }

        /// <summary>
        /// Delay enabling guard mode until the condition is satisfied.
        /// </summary>
        /// <param name="pilot">The pilot to enable guard mode for</param>
        /// <param name="condition">The condition to satisfy first</param>
        IEnumerator EnableGuardModeWhen(IBDAIControl pilot, Func<bool> condition)
        {
            float originalMinAlt = 200;
            if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 77)
            {
                var pAI = VesselModuleRegistry.GetBDModulePilotAI(pilot.vessel);
                if (pAI != null)
                    originalMinAlt = pAI.minAltitude; //store the original minAlt for later
            }
            yield return new WaitUntilFixed(condition);
            if (pilot == null || pilot.vessel == null) yield break;
            if (pilot.WeaponManager != null && !pilot.WeaponManager.guardMode)
            {
                competitionStatus.Add($"Enabling guard mode for {pilot.vessel.vesselName}");
                pilot.WeaponManager.ToggleGuardMode();
                if (BDArmorySettings.RUNWAY_PROJECT_ROUND == 77)
                {
                    var pAI = VesselModuleRegistry.GetBDModulePilotAI(pilot.vessel);
                    if (pAI != null)
                        pAI.minAltitude = originalMinAlt; //combat's started, reset minAlt so craft won't crash later
                }
            }
        }

        void AdjustKerbalDrag(float speedThreshold, float scale)
        {
            foreach (var weaponManager in LoadedVesselSwitcher.Instance.WeaponManagers.SelectMany(tm => tm.Value))
            {
                if (weaponManager.vessel.speed > speedThreshold)
                {
                    if (dragLimiting.Contains(weaponManager.vessel)) continue; // Already limiting.
                    StartCoroutine(DragLimit(weaponManager.vessel, speedThreshold, scale));
                }
            }
        }
        HashSet<Vessel> dragLimiting = new HashSet<Vessel>();
        IEnumerator DragLimit(Vessel vessel, float speedThreshold, float scale)
        {
            if (dragLimiting.Contains(vessel)) yield break; // Already limiting.
            dragLimiting.Add(vessel);
            var kerbals = VesselModuleRegistry.GetKerbalEVAs(vessel).Where(kerbal => kerbal != null).ToList();
            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log($"[BDArmory.BDACompetitionMode]: {vessel.vesselName} is over the speed limit, applying drag to {string.Join(", ", kerbals.Select(kerbal => kerbal.name))}");
            var wait = new WaitForFixedUpdate();
            foreach (var kerbal in kerbals) kerbal.part.ShieldedFromAirstream = false;
            while (vessel != null && vessel.speed > speedThreshold)
            {
                var drag = (float)(vessel.speed - speedThreshold) * scale;
                bool hasKerbal = false;
                foreach (var kerbal in kerbals)
                {
                    if (kerbal == null || kerbal.vessel != vessel) continue;
                    hasKerbal = true;
                    kerbal.part.minimum_drag = drag;
                    kerbal.part.maximum_drag = drag;
                }
                if (!hasKerbal)
                {
                    var AI = vessel.ActiveController().AI;
                    if (AI != null && AI.pilotEnabled) AI.DeactivatePilot();
                    StartCoroutine(DelayedExplodeWMs(vessel, 1f, UncontrolledReason.Uncontrolled));
                }
                yield return wait;
            }
            if (vessel != null)
            {
                if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log($"[BDArmory.BDACompetitionMode]: {vessel.vesselName} is back within the speed limit, removing drag from {string.Join(", ", kerbals.Where(kerbal => kerbal != null).Select(kerbal => kerbal.name))}");
                foreach (var kerbal in kerbals)
                {
                    if (kerbal == null) continue;
                    kerbal.part.ShieldedFromAirstream = true;
                }
                dragLimiting.Remove(vessel);
            }
        }
        #endregion

        #region Debris clean-up
        public bool CleanDebrisAlways // Have the debris cleanup event handlers running all the time or just during competitions.
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                BDArmorySettings.CLEAN_DEBRIS_ALWAYS = value;
                GameEvents.onVesselCreate.Remove(DebrisDelayedCleanUp); // Remove it first to avoid accumulating event handlers.
                if (value || competitionIsActive)
                {
                    GameEvents.onVesselCreate.Add(DebrisDelayedCleanUp);
                    foreach (var vessel in FlightGlobals.VesselsLoaded) DebrisDelayedCleanUp(vessel);
                }
            }
        } = false;
        private HashSet<Vessel> nonCompetitorsToRemove = new HashSet<Vessel>();
        public void RemoveNonCompetitors(bool now = false)
        {
            foreach (var vessel in FlightGlobals.Vessels)
            {
                if (vessel == null) continue;
                if (VesselModuleRegistry.IgnoredVesselTypes.Contains(vessel.vesselType)) continue;  // Debris handled by DebrisDelayedCleanUp, others are ignored.
                if (nonCompetitorsToRemove.Contains(vessel)) continue; // Already scheduled for removal.
                bool activePilot;
                if (vessel.GetName().Contains(BDArmorySettings.PINATA_NAME))
                {
                    activePilot = true;
                }
                else
                {
                    int foundActiveParts = 0; // Note: this checks for exactly one of each part.
                    if (vessel.ActiveController().WM != null) // Has a weapon manager
                    { ++foundActiveParts; }


                    if (vessel.ActiveController().AI != null) // Has an AI
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
                else if (vessel.vesselType == VesselType.SpaceObject) // Remove comets and asteroids to try to avoid null refs. (Still get null refs from comets, but it seems better with this than without it.)
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

        void CometCleanup(bool disableSpawning = false)
        {
            if ((Versioning.version_major == 1 && Versioning.version_minor > 9) || Versioning.version_major > 1) // Introduced in 1.10
            {
                CometCleanup_1_10(disableSpawning);
            }
            else // Nothing, comets didn't exist before
            {
            }
        }

        void CometCleanup_1_10(bool disableSpawning = false) // KSP has issues on older versions if this call is in the parent function.
        {
            if (disableSpawning)
            {
                DisableCometSpawning();
                GameEvents.onCometSpawned.Add(RemoveCometVessel);
            }
            else
            {
                GameEvents.onCometSpawned.Remove(RemoveCometVessel);
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
                if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode]: Debris " + vessel.vesselName + " is no longer labelled as debris, not removing.");
                yield break;
            }
            if (vessel != null)
            {
                if (VesselSpawnerWindow.Instance.Observers.Contains(vessel) // Ignore observers.
                   || (vessel.parts.Count == 1 && vessel.parts[0].IsKerbalEVA()) // The vessel is a kerbal on EVA. Ignore it for now.
                )
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
                    if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode]: Removing " + vessel.vesselName);
                    yield return SpawnUtilsInstance.Instance.RemoveVesselCoroutine(vessel);
                }
            }
            if (nonCompetitorsToRemove.Contains(vessel))
            {
                if (BDArmorySettings.DEBUG_COMPETITION && vessel.vesselName != null) { Debug.Log($"[BDArmory.BDACompetitionMode]: {vessel.vesselName} removed."); }
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
            if (competitionType == CompetitionType.WAYPOINTS && (BDArmorySettings.RUNWAY_PROJECT_ROUND != 55 && BDArmorySettings.COMPETITION_WAYPOINTS_GM_KILL_PERIOD <= 0 && BDArmorySettings.WAYPOINT_GUARD_INDEX < 0)) return; // Don't do anything below when running waypoints unless guardmode is set to activate at somepoint or if set to podracers (for tuskenRaider GM culling of slow pods)
            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 55 && competitionIsActive) AdjustKerbalDrag(605, 0.01f); // Over 605m/s, add drag at a rate of 0.01 per m/s.

            // Example usage of UpcomingCollisions(). Note that the timeToCPA values are only updated after an interval of half the current timeToCPA.
            // if (competitionIsActive)
            //     foreach (var upcomingCollision in UpcomingCollisions(100f).Take(3))
            //         Debug.Log("[BDArmory.BDACompetitionMode]: Upcoming potential collision between " + upcomingCollision.Key.Item1 + " and " + upcomingCollision.Key.Item2 + " at distance " + BDAMath.Sqrt(upcomingCollision.Value.Item1) + "m in " + upcomingCollision.Value.Item2 + "s.");
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
                if (vessel == null || !vessel.loaded || VesselModuleRegistry.IgnoredVesselTypes.Contains(vessel.vesselType)) // || vessel.packed) // Allow packed craft to avoid the packed craft being considered dead (e.g., when command seats spawn).
                    continue;

                var mf = vessel.ActiveController().WM;
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
                    var ai = mf.AI;
                    if (ai != null && ai.currentStatus != null && BDArmorySettings.DISPLAY_COMPETITION_STATUS)
                    {
                        pilotActions[vesselName] = "";
                        if (mf.vessel.LandedOrSplashed)
                        {
                            if (mf.vessel.Landed)
                                pilotActions[vesselName] = " is landed";
                            else
                                pilotActions[vesselName] = " is splashed";
                        }
                        var activity = ai.currentStatus;
                        if (activity == "Taking off")
                            pilotActions[vesselName] = " is taking off";
                        else if (activity == "Follow")
                        {
                            if (ai.commandLeader != null && ai.commandLeader.vessel != null)
                                pilotActions[vesselName] = " is following " + ai.commandLeader.vessel.GetName();
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
                            var surfaceAI = VesselModuleRegistry.GetModule<BDModuleSurfaceAI>(vessel);
                            if (surfaceAI != null)
                            {
                                if (surfaceAI.currentStatusMode == BDModuleSurfaceAI.StatusMode.Panic)
                                {
                                    if (!vData.landedState)
                                    {
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
                                        // safely mobile for 15 seconds
                                        if (now - vData.landedKillTimer > 15)
                                        {
                                            vData.landedKillTimer = 0;
                                        }
                                    }
                                }
                            }
                            else
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
                            if (ai == null || mf.outOfAmmo && (BDArmorySettings.DISABLE_RAMMING || ai.aiType switch
                            {
                                AIType.PilotAI => !(ai as BDModulePilotAI).allowRamming,
                                AIType.OrbitalAI => !(ai as BDModuleOrbitalAI).allowRamming,
                                _ => true
                            })) // if we've lost the AI or the vessel is out of weapons/ammo and ramming is not allowed.
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
                                vesselsToKill.Add(mf.vessel);
                            }
                        }
                        if (vData.AltitudeKillTimer > 0 && BDArmorySettings.COMPETITION_KILL_TIMER > 0)
                        {
                            KillTimer[vesselName] = (int)(now - vData.AltitudeKillTimer);
                            if (now - vData.AltitudeKillTimer > BDArmorySettings.COMPETITION_KILL_TIMER)
                            {
                                var killerName = Scores.ScoreData[vesselName].lastPersonWhoDamagedMe;
                                if (killerName == "")
                                {
                                    killerName = "Restricted Altitude!";
                                    Scores.ScoreData[vesselName].lastPersonWhoDamagedMe = killerName;
                                }
                                Scores.RegisterDeath(vesselName, GMKillReason.GM);
                                competitionStatus.Add(vesselName + " no fly zone!");
                                if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: " + vesselName + ":REMOVED:" + killerName);
                                if (KillTimer.ContainsKey(vesselName)) KillTimer.Remove(vesselName);
                                VesselUtils.ForceDeadVessel(vessel);
                            }
                        }
                        else if (KillTimer.ContainsKey(vesselName))
                            KillTimer.Remove(vesselName);
                    }
                    if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 67 && vesselName.Contains(BDArmorySettings.PINATA_NAME))
                    {
                        if (vessel.radarAltitude <= 1000)
                        {
                            competitionStatus.Add("Failed to stop the Asteroid in time!");
                            PartExploderSystem.AddPartToExplode(vessel.rootPart);
                            NukeFX.CreateExplosion(vessel.CoM, ExplosionSourceType.BattleDamage, "Asteroid", "Impact", 0, 5000, 20, 0, true, "BDArmory/Models/explosion/nuke/nukeBoom", "", "BDArmory/Models/explosion/nuke/nukeShock", "BDArmory/Models/explosion/nuke/nukeBlast", "", "", "", "", nukePart: vessel.rootPart);
                            pinataAlive = false;
                            if (alive.Contains(BDArmorySettings.PINATA_NAME)) alive.Remove(BDArmorySettings.PINATA_NAME);
                            // Don't immediately stop the competition, so that craft caught in the explosion get a chance to die.
                        }
                    }
                }
            }
            string aliveString = string.Join(",", alive.ToArray());
            previousNumberCompetitive = numberOfCompetitiveVessels;
            // if (BDArmorySettings.DEBUG_LABELS) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "] STILLALIVE: " + aliveString); // This just fills the logs needlessly.
            if (hasPinata && !string.IsNullOrEmpty(BDArmorySettings.PINATA_NAME))
            {
                // If we find a vessel named "Pinata" that's a special case object
                // this should probably be configurable.
                if (!pinataAlive && alive.Contains(BDArmorySettings.PINATA_NAME))
                {
                    Debug.Log("[BDArmory.BDACompetitionMode" + CompetitionID.ToString() + "]: Setting Pinata Flag to Alive!");
                    pinataAlive = true;
                    competitionStatus.Add("Enabling Pinata");
                }
                else if (pinataAlive && !alive.Contains(BDArmorySettings.PINATA_NAME))
                {
                    if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 67)
                    {
                        competitionStatus.Add("Asteroid destroyed by " + Scores.ScoreData[BDArmorySettings.PINATA_NAME].lastPersonWhoDamagedMe + "!");
                        Scores.RegisterMissileStrike(Scores.ScoreData[BDArmorySettings.PINATA_NAME].lastPersonWhoDamagedMe, BDArmorySettings.PINATA_NAME); //give a missile strike point to indicate the pinata kill on the web API
                        Scores.RegisterDeath(BDArmorySettings.PINATA_NAME, GMKillReason.None, now);
                        foreach (string key in alive)
                        {
                            competitionStatus.Add(key + " wins the round!");
                        }
                        if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]:Asteroid Intercept complete, Automatically dumping scores");
                        StopCompetition();
                        return;
                    }
                    // switch everyone onto separate teams when the Pinata Dies
                    LoadedVesselSwitcher.Instance.MassTeamSwitch(originalTeams: true);
                    pinataAlive = false;
                    competitionStatus.Add("Pinata killed by " + Scores.ScoreData[BDArmorySettings.PINATA_NAME].lastPersonWhoDamagedMe + "! Competition is now a Free for all");
                    Scores.RegisterMissileStrike(Scores.ScoreData[BDArmorySettings.PINATA_NAME].lastPersonWhoDamagedMe, BDArmorySettings.PINATA_NAME); //give a missile strike point to indicate the pinata kill on the web API
                    if (BDArmorySettings.AUTO_ENABLE_VESSEL_SWITCHING)
                        LoadedVesselSwitcher.Instance.EnableAutoVesselSwitching(true);
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
                    if (player == BDArmorySettings.PINATA_NAME) continue;
                    if (Scores.ScoreData[player].aliveState == AliveState.Alive)
                    {
                        var timeOfDeath = now;
                        // If player was involved in a collision, we need to wait until the collision is resolved before registering the death.
                        if (rammingInformation.ContainsKey(player) && rammingInformation[player].targetInformation.Values.Any(other => other.collisionDetected))
                        {
                            rammingInformation[player].timeOfDeath = rammingInformation[player].targetInformation.Values.Where(other => other.collisionDetected).Select(other => other.collisionDetectedTime).Max();
                            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log($"[BDArmory.BDACompetitionMode:{CompetitionID}]: Delaying death of {player} due to being involved in a collision {now - rammingInformation[player].timeOfDeath}s ago at {rammingInformation[player].timeOfDeath - competitionStartTime:F3}.");
                            continue; // Involved in a collision, delay registering death.
                        }
                        if (asteroidCollisions.Contains(player)) continue; // Also delay registering death if they're colliding with an asteroid.
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
                            case DamageFrom.Asteroids:
                                statusMessage += " flew into an asteroid ";
                                break;
                            case DamageFrom.Incompetence:
                                statusMessage += " CRASHED and BURNED.";
                                break;
                            case DamageFrom.None:
                                statusMessage += $" {Scores.ScoreData[player].gmKillReason}";
                                break;
                        }
                        bool canAssignMutator = true;
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
                                //canAssignMutator = true;
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
                                //canAssignMutator = true;
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
                                //canAssignMutator = true;
                                break;
                            case AliveState.AssistedKill: // Assist (not damaged recently or GM kill).
                                if (Scores.ScoreData[player].gmKillReason != GMKillReason.None) Scores.ScoreData[player].everyoneWhoDamagedMe.Add(Scores.ScoreData[player].gmKillReason.ToString()); // Log the GM kill reason.
                                //canAssignMutator = false; //comment out if wanting last person to deal damage to be awarded a On Kill mutator
                                if (Scores.ScoreData[player].gmKillReason != GMKillReason.None) // Note: LandedTooLong is handled separately.
                                    canAssignMutator = false; //GM kill, no mutator, else award last player to deal damage
                                statusMessage += string.Join(", ", Scores.ScoreData[player].everyoneWhoDamagedMe) + " (" + string.Join(", ", Scores.ScoreData[player].damageTypesTaken) + ")";
                                break;
                            case AliveState.Dead: // Suicide/Incompetance (never took damage from others).
                                canAssignMutator = false;
                                break;
                        }
                        competitionStatus.Add(statusMessage);
                        if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log($"[BDArmory.BDACompetitionMode:{CompetitionID}]: " + statusMessage);

                        if (BDArmorySettings.MUTATOR_MODE && BDArmorySettings.MUTATOR_APPLY_KILL)
                        {
                            if (BDArmorySettings.MUTATOR_LIST.Count > 0 && canAssignMutator) ApplyOnKillMutator(player);
                            else Debug.Log($"[BDArmory.BDACompetitionMode]: Mutator mode, but no assigned mutators! Can't apply mutator on Kill!");
                        }
                    }
                    deadOrAliveString += " :" + player + ": ";
                }
            }
            deadOrAlive = deadOrAliveString;

            var numberOfCompetitiveTeams = LoadedVesselSwitcher.Instance.WeaponManagers.Count;
            if (now - competitionStartTime > BDArmorySettings.COMPETITION_INITIAL_GRACE_PERIOD && (numberOfCompetitiveVessels < competitiveTeamsAliveLimit || (!BDArmorySettings.TAG_MODE && numberOfCompetitiveTeams < competitiveTeamsAliveLimit)) && !ContinuousSpawning.Instance.vesselsSpawningContinuously)
            {
                if (finalGracePeriodStart < 0)
                    finalGracePeriodStart = now;
                bool runningWPs = false;
                if (BDArmorySettings.WAYPOINTS_MODE)
                {
                    using (var loadedVessels = BDATargetManager.LoadedVessels.GetEnumerator())
                        while (loadedVessels.MoveNext())
                        {
                            if (loadedVessels.Current == null || !loadedVessels.Current.loaded || VesselModuleRegistry.IgnoredVesselTypes.Contains(loadedVessels.Current.vesselType)) // || vessel.packed) // Allow packed craft to avoid the packed craft being considered dead (e.g., when command seats spawn).
                                continue;
                            IBDAIControl pilot = loadedVessels.Current.ActiveController().AI;
                            if (pilot == null || !pilot.WeaponManager || pilot.WeaponManager.Team.Neutral) continue;
                            if (((BDGenericAIBase)pilot).IsRunningWaypoints)
                            {
                                runningWPs = true;
                                break;//if only one craft left, but WP mode and craft still running WPs, don't prematurely exit
                            }
                        }
                }
                if (!runningWPs && BDArmorySettings.COMPETITION_FINAL_GRACE_PERIOD <= 60 && now - finalGracePeriodStart > BDArmorySettings.COMPETITION_FINAL_GRACE_PERIOD)
                {
                    if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 67)
                    {
                        foreach (var vessel in FlightGlobals.Vessels)
                        {
                            Scores.RegisterDeath(vessel.vesselName, GMKillReason.GM, now);
                            VesselUtils.ForceDeadVessel(vessel);
                        }
                        alive.Clear();
                    }
                    competitionStatus.Add("All Pilots are Dead");
                    foreach (string key in alive)
                    {
                        competitionStatus.Add(key + " wins the round!");
                    }
                    if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]:No viable competitors, Automatically dumping scores");
                    StopCompetition();
                    return;
                }
            }

            //Reset gravity
            if (BDArmorySettings.GRAVITY_HACKS && competitionIsActive)
            {
                int maxVesselsActive = (ContinuousSpawning.Instance.vesselsSpawningContinuously && BDArmorySettings.VESSEL_SPAWN_CONCURRENT_VESSELS > 0) ? BDArmorySettings.VESSEL_SPAWN_CONCURRENT_VESSELS : Scores.Players.Count;
                double time = now - competitionStartTime;
                gravityMultiplier = 1f + 7f * (float)(Scores.deathCount % maxVesselsActive) / (float)(maxVesselsActive - 1); // From 1G to 8G.
                gravityMultiplier += ContinuousSpawning.Instance.vesselsSpawningContinuously ? BDAMath.Sqrt(5f - 5f * Mathf.Cos((float)time / 600f * Mathf.PI)) : BDAMath.Sqrt((float)time / 60f); // Plus up to 3.16G.
                PhysicsGlobals.GraviticForceMultiplier = (double)gravityMultiplier;
                VehiclePhysics.Gravity.Refresh();
                if (Mathf.RoundToInt(10 * gravityMultiplier) != Mathf.RoundToInt(10 * lastGravityMultiplier)) // Only write a message when it shows something different.
                {
                    lastGravityMultiplier = gravityMultiplier;
                    competitionStatus.Add("Competition: Adjusting gravity to " + gravityMultiplier.ToString("0.0") + "G!");
                }
            }

            //Set MinAlt
            if (BDArmorySettings.ALTITUDE_HACKS && competitionIsActive)
            {
                int maxVesselsActive = (ContinuousSpawning.Instance.vesselsSpawningContinuously && BDArmorySettings.VESSEL_SPAWN_CONCURRENT_VESSELS > 0) ? BDArmorySettings.VESSEL_SPAWN_CONCURRENT_VESSELS : Scores.Players.Count;
                double time = now - competitionStartTime;
                MinAlt = 20 + 8000f * (float)(Scores.deathCount % maxVesselsActive) / (float)(maxVesselsActive - 1); // From 1km to 8km.
                MinAlt += (ContinuousSpawning.Instance.vesselsSpawningContinuously ? BDAMath.Sqrt(5f - 5f * Mathf.Cos((float)time / 600f * Mathf.PI)) : BDAMath.Sqrt((float)time / 60f)) * 1000; // Plus up to 3.16km.

                using (List<IBDAIControl>.Enumerator pilots = GetAllPilots().GetEnumerator())
                {
                    while (pilots.MoveNext())
                    {
                        var pilotAI = pilots.Current.vessel.ActiveController().PilotAI; // Get the pilot AI if the vessel has one.
                        if (pilotAI != null) pilotAI.minAltitude = MinAlt;
                    }
                }
                if (Mathf.RoundToInt(MinAlt / 100) != Mathf.RoundToInt(lastMinAlt / 100)) // Only write a message when it shows something different.
                {
                    lastMinAlt = MinAlt;
                    competitionStatus.Add("Competition: Adjusting min Altitude to " + MinAlt.ToString("0.0") + "m!");
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
                    if (BDArmorySettings.MUTATOR_MODE && BDArmorySettings.MUTATOR_APPLY_KILL && BDArmorySettings.MUTATOR_LIST.Count > 0)
                        ApplyOnKillMutator(vesselName); // Apply mutators for LandedTooLong kills, which count as assists.
                }
                if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode:" + CompetitionID.ToString() + "]: " + vesselName + ":REMOVED:" + killerName);
                if (KillTimer.ContainsKey(vesselName)) KillTimer.Remove(vesselName);
                VesselUtils.ForceDeadVessel(vessel);
            }

            if (!(BDArmorySettings.COMPETITION_NONCOMPETITOR_REMOVAL_DELAY > 60))
                RemoveNonCompetitors();

            if (now - competitionStartTime > altitudeLimitGracePeriod)
                CheckAltitudeLimits();
            if (competitionIsActive && competitionType == CompetitionType.WAYPOINTS && BDArmorySettings.COMPETITION_WAYPOINTS_GM_KILL_PERIOD > 0 && now - competitionStartTime > BDArmorySettings.COMPETITION_INITIAL_GRACE_PERIOD) CullSlowWaypointRunners(BDArmorySettings.COMPETITION_WAYPOINTS_GM_KILL_PERIOD);
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
                LogResults(message: "due to out-of-time", tag: competitionTag);
                StopCompetition();
                return;
            }

            if ((BDArmorySettings.MUTATOR_MODE && BDArmorySettings.MUTATOR_APPLY_TIMER) && BDArmorySettings.MUTATOR_DURATION > 0 && now - MutatorResetTime >= BDArmorySettings.MUTATOR_DURATION * 60d && BDArmorySettings.MUTATOR_LIST.Count > 0)
            {

                ScreenMessages.PostScreenMessage(StringUtils.Localize("#LOC_BDArmory_UI_MutatorShuffle"), 5, ScreenMessageStyle.UPPER_CENTER);
                ConfigureMutator();
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || !vessel.loaded || VesselModuleRegistry.IgnoredVesselTypes.Contains(vessel.vesselType)) // || vessel.packed) // Allow packed craft to avoid the packed craft being considered dead (e.g., when command seats spawn).
                        continue;

                    var mf = vessel.ActiveController().WM;
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

        void ApplyOnKillMutator(string player)
        {
            using var loadedVessels = BDATargetManager.LoadedVessels.GetEnumerator();
            while (loadedVessels.MoveNext())
            {
                if (loadedVessels.Current == null || !loadedVessels.Current.loaded || VesselModuleRegistry.IgnoredVesselTypes.Contains(loadedVessels.Current.vesselType))
                    continue;
                var craftName = loadedVessels.Current.GetName();
                if (!Scores.Players.Contains(craftName)) continue;
                if (BDArmorySettings.MUTATOR_APPLY_GUNGAME && Scores.ScoreData[player].aliveState == AliveState.AssistedKill && Scores.ScoreData[player].everyoneWhoDamagedMe.Contains(craftName))
                    SpawnUtils.ApplyMutators(loadedVessels.Current, true); // Reward everyone involved on assists.
                else if (Scores.ScoreData[player].lastPersonWhoDamagedMe == craftName || (BDArmorySettings.MUTATOR_APPLY_GUNGAME && Scores.ScoreData[player].aliveState == AliveState.KillSteal && Scores.ScoreData[player].previousPersonWhoDamagedMe == craftName))
                    SpawnUtils.ApplyMutators(loadedVessels.Current, true); // Reward clean kills and those whom have had their kills stolen.
                else continue;
                if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log($"[BDArmory.BDACompetitionMode:{CompetitionID}]: Assigning On Kill mutator for {player} to {craftName}");
            }
        }

        // This now also writes the competition logs to GameData/BDArmory/Logs/<CompetitionID>[-tag].log
        public void LogResults(string message = "", string tag = "")
        {
            if (competitionStartTime < 0)
            {
                if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode]: No active competition, not dumping results.");
                return;
            }
            // RunDebugChecks();
            // CheckMemoryUsage();
            if (ContinuousSpawning.Instance.vesselsSpawningContinuously) // Dump continuous spawning scores instead.
            {
                ContinuousSpawning.Instance.DumpContinuousSpawningScores(tag);
                return;
            }

            if (BDArmorySettings.DEBUG_COMPETITION) competitionStatus.Add("Dumping scores for competition " + CompetitionID.ToString() + (tag != "" ? " " + tag : ""));
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
            rammingInformation = [];
            var pilots = GetAllPilots();
            foreach (var pilot in pilots)
            {
                //if (pilot as BDModulePilotAI == null && pilot as BDModuleOrbitalAI == null) continue; // Ignore those without valid AIs.
                var targetRammingInformation = new Dictionary<string, RammingTargetInformation>();
                foreach (var otherPilot in pilots)
                {
                    if (otherPilot == pilot) continue; // Don't include same-vessel information.
                    //if (otherPilot as BDModulePilotAI == null && otherPilot as BDModuleOrbitalAI == null) continue; // Ignore those without valid AIs.
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

        bool GetAIRammingState(IBDAIControl AI)
        {
            var pilotAI = AI as BDModulePilotAI;
            if (pilotAI != null) return pilotAI.ramming;
            var orbitalAI = AI as BDModuleOrbitalAI;
            if (orbitalAI != null) return orbitalAI.currentStatusMode == BDModuleOrbitalAI.StatusMode.Ramming;
            return false; // The other AIs don't have ramming modes currently.
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
                var AI = vessel == null ? null : vessel.ActiveController().AI; // Get the pilot AI if the vessel has one.

                foreach (var otherVesselName in rammingInformation[vesselName].targetInformation.Keys)
                {
                    var otherVessel = rammingInformation[vesselName].targetInformation[otherVesselName].vessel;
                    var otherAI = otherVessel == null ? null : otherVessel.ActiveController().AI; // Get the pilot AI if the vessel has one.
                    if (AI == null || otherAI == null) // One of the vessels or pilot AIs has been destroyed.
                    {
                        rammingInformation[vesselName].targetInformation[otherVesselName].timeToCPA = maxTimeToCPA; // Set the timeToCPA to maxTimeToCPA, so that it's not considered for new potential collisions.
                        rammingInformation[otherVesselName].targetInformation[vesselName].timeToCPA = maxTimeToCPA; // Set the timeToCPA to maxTimeToCPA, so that it's not considered for new potential collisions.
                    }
                    else
                    {
                        if (currentTime - rammingInformation[vesselName].targetInformation[otherVesselName].lastUpdateTime > rammingInformation[vesselName].targetInformation[otherVesselName].timeToCPA / 2f) // When half the time is gone, update it.
                        {
                            float timeToCPA = AIUtils.TimeToCPA(vessel, otherVessel, maxTimeToCPA); // Look up to maxTimeToCPA ahead.
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
                        if (rammingInformation[vesselName].targetInformation[otherVesselName].potentialCollision && rammingInformation[vesselName].targetInformation[otherVesselName].timeToCPA < maxTimeToCPA && string.Compare(vesselName, otherVesselName) < 0)
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
                                    rammingInformation[vesselName].targetInformation[otherVesselName].angleToCoM = VectorUtils.Angle(vessel.srf_vel_direction, otherVessel.CoM - vessel.CoM);
                                    rammingInformation[otherVesselName].targetInformation[vesselName].angleToCoM = VectorUtils.Angle(otherVessel.srf_vel_direction, vessel.CoM - otherVessel.CoM);
                                }

                                // Update part counts if vessels get shot and potentially lose parts before the collision happens.
                                if (!Scores.Players.Contains(rammingInformation[vesselName].vesselName)) CheckVesselType(rammingInformation[vesselName].vessel); // It may have become a "vesselName Plane" if the WM is badly placed.
                                try
                                {
                                    if (Scores.ScoreData[rammingInformation[vesselName].vesselName].lastDamageWasFrom != DamageFrom.Ramming && Scores.ScoreData[rammingInformation[vesselName].vesselName].lastDamageTime > rammingInformation[otherVesselName].targetInformation[vesselName].potentialCollisionDetectionTime)
                                    {
                                        if (rammingInformation[vesselName].partCount != vessel.parts.Count)
                                        {
                                            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode]: Ram logging: " + vesselName + " lost " + (rammingInformation[vesselName].partCount - vessel.parts.Count) + " parts from getting shot.");
                                            rammingInformation[vesselName].partCount = vessel.parts.Count;
                                        }
                                    }
                                    if (!Scores.Players.Contains(rammingInformation[otherVesselName].vesselName)) CheckVesselType(rammingInformation[otherVesselName].vessel); // It may have become a "vesselName Plane" if the WM is badly placed.
                                    if (Scores.ScoreData[rammingInformation[otherVesselName].vesselName].lastDamageWasFrom != DamageFrom.Ramming && Scores.ScoreData[rammingInformation[otherVesselName].vesselName].lastDamageTime > rammingInformation[vesselName].targetInformation[otherVesselName].potentialCollisionDetectionTime)
                                    {
                                        if (rammingInformation[otherVesselName].partCount != otherVessel.parts.Count)
                                        {
                                            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode]: Ram logging: " + otherVesselName + " lost " + (rammingInformation[otherVesselName].partCount - otherVessel.parts.Count) + " parts from getting shot.");
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
                                var AI = vessel.ActiveController().AI;
                                rammingInformation[vesselName].targetInformation[otherVesselName].ramming |= GetAIRammingState(AI); // The AI is alive and trying to ram.
                                var otherAI = otherVessel.ActiveController().AI;
                                rammingInformation[otherVesselName].targetInformation[vesselName].ramming |= GetAIRammingState(otherAI); // The other AI is alive and trying to ram.
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
                if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode]: Ram logging: in AnalyseCollision the colliding part belonged to a null vessel!");
                return;
            }
            try
            {
                // Debug.Log($"DEBUG Collision of {vessel.vesselName} with: other:{data.other}, sender: {data.sender}, stage: {data.stage}, msg: {data.msg}, param: {data.param}, type: {data.eventType}");
                bool hitVessel = false;
                if (rammingInformation.ContainsKey(vessel.vesselName)) // If the part was attached to a vessel,
                {
                    var vesselName = vessel.vesselName; // For convenience.
                    if (data.other.StartsWith("Ast. ")) // We hit an asteroid, most likely due to one of the asteroids game modes.
                    {
                        if (!asteroidCollisions.Contains(vesselName))
                            StartCoroutine(AsteroidCollision(vessel, rammingInformation[vesselName].partCount));
                    }
                    else
                    {
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
                                        if (otherVessel is not null)
                                            rammingInformation[vesselName].targetInformation[otherVesselName].sqrDistance = (vessel.CoM - otherVessel.CoM).sqrMagnitude;
                                        else
                                        {
                                            var distance = collisionMargin * (rammingInformation[vesselName].radius + rammingInformation[otherVesselName].radius);
                                            rammingInformation[vesselName].targetInformation[otherVesselName].sqrDistance = distance * distance + 1f;
                                        }
                                        rammingInformation[otherVesselName].targetInformation[vesselName].sqrDistance = rammingInformation[vesselName].targetInformation[otherVesselName].sqrDistance;
                                        rammingInformation[vesselName].targetInformation[otherVesselName].collisionDetectedTime = currentTime;
                                        rammingInformation[otherVesselName].targetInformation[vesselName].collisionDetectedTime = currentTime;
                                        if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode]: Ram logging: Collision detected between " + vesselName + " and " + otherVesselName);
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
                    }
                    if (!hitVessel) // We didn't hit another vessel, maybe it crashed and died.
                    {
                        if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log($"[BDArmory.BDACompetitionMode]: Ram logging: {vesselName} hit {data.other}.");
                        rammingInformation[vesselName].partCount = vessel.parts.Count; // Update the vessel part count.
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

        HashSet<string> asteroidCollisions = new HashSet<string>();
        IEnumerator AsteroidCollision(Vessel vessel, int preCollisionPartCount)
        {
            if (vessel == null) yield break;
            var vesselName = vessel.vesselName;
            var partsLost = preCollisionPartCount;
            var timeOfDeath = Planetarium.GetUniversalTime(); // In case they die.
            asteroidCollisions.Add(vesselName);
            yield return new WaitForSecondsFixed(potentialCollisionDetectionTime);
            if (rammingInformation == null) // The competition is finished / KSP is changing scenes or exiting.
            {
                asteroidCollisions.Remove(vesselName);
                yield break;
            }
            if (vessel == null || vessel.ActiveController().WM == null)
            {
                rammingInformation[vesselName].partCount = 0;
                if (Scores.ScoreData[vesselName].aliveState == AliveState.Alive)
                {
                    Scores.RegisterAsteroidCollision(vesselName, partsLost);
                    Scores.RegisterDeath(vesselName, GMKillReason.Asteroids, timeOfDeath);
                    competitionStatus.Add($"{vesselName} flew into an asteroid and died!");
                    if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log($"[BDArmory.BDACompetitionMode]: {vesselName} flew into an asteroid and died!");
                }
            }
            else
            {
                partsLost -= vessel.parts.Count;
                rammingInformation[vesselName].partCount = vessel.parts.Count;
                if (Scores.ScoreData[vesselName].aliveState == AliveState.Alive)
                {
                    Scores.RegisterAsteroidCollision(vesselName, partsLost);
                    competitionStatus.Add($"{vesselName} flew into an asteroid and lost {partsLost} parts!");
                    if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log($"[BDArmory.BDACompetitionMode]: {vesselName} flew into an asteroid and lost {partsLost} parts!");
                }
            }
            asteroidCollisions.Remove(vesselName);
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

                if (BDArmorySettings.DEBUG_COMPETITION && collidingVesselDistances.Count > 1) // DEBUG
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
                        var AI = vessel == null ? null : vessel.ActiveController().AI;
                        var otherAI = otherVessel == null ? null : otherVessel.ActiveController().AI;

                        // Count the number of parts lost.
                        var rammedPartsLost = (otherAI == null) ? rammingInformation[vesselName].targetInformation[otherVesselName].partCountJustPriorToCollision : rammingInformation[vesselName].targetInformation[otherVesselName].partCountJustPriorToCollision - otherVessel.parts.Count;
                        var rammingPartsLost = (AI == null) ? rammingInformation[otherVesselName].targetInformation[vesselName].partCountJustPriorToCollision : rammingInformation[otherVesselName].targetInformation[vesselName].partCountJustPriorToCollision - vessel.parts.Count;
                        if (rammedPartsLost < 0 || rammingPartsLost < 0) // BUG! A plane involved in two collisions close together apparently can cause this?
                        {
                            Debug.LogWarning($"[BDArmory.BDACompetitionMode]: Negative parts lost in ram! Clamping to 0.");
                            if (rammedPartsLost < 0)
                            {
                                Debug.LogWarning($"[BDArmory.BDACompetitionMode]: {otherVesselName} had {rammingInformation[vesselName].targetInformation[otherVesselName].partCountJustPriorToCollision} parts and lost {rammedPartsLost} parts (current part count: {(otherAI == null ? "none" : $"{otherVessel.parts.Count}")})");
                                rammedPartsLost = 0;
                            }
                            if (rammingPartsLost < 0)
                            {
                                Debug.LogWarning($"[BDArmory.BDACompetitionMode]: {vesselName} had {rammingInformation[otherVesselName].targetInformation[vesselName].partCountJustPriorToCollision} parts and lost {rammingPartsLost} parts (current part count: {(AI == null ? "none" : $"{vessel.parts.Count}")})");
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
                    if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode]: Ram logging: " + vesselName + " started with " + partsCheck[vesselName] + " parts.");
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
                        if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode]: Ram logging: Parts Check: " + vesselName + " has lost " + (partsCheck[vesselName] - vessel.parts.Count) + " parts." + (vessel.parts.Count > 0 ? "" : " and is no more."));
                        partsCheck[vesselName] = vessel.parts.Count;
                    }
                }
                else if (partsCheck[vesselName] > 0)
                {
                    if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log("[BDArmory.BDACompetitionMode]: Ram logging: Parts Check: " + vesselName + " has been destroyed, losing " + partsCheck[vesselName] + " parts.");
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
            if (BDArmorySettings.DEBUG_COMPETITION) CheckForMissingParts(); // DEBUG
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
                pilot.WeaponManager.SetTeam(BDTeam.Get(T.ToString()));
                Scores.ScoreData[pilot.vessel.GetName()].tagIsIt = false;
                pilot.vessel.ActionGroups.ToggleGroup(KM_dictAG[9]); // Trigger AG9 on becoming "NOT IT"
                T++;
            }
            foreach (var pilot in pilots)
                pilot.WeaponManager.ForceScan(); // Update targets.
            startTag = true;
        }
        #endregion

        #region Post-start helper functions
        /// <summary>
        /// Add the vessel the WM is on to an active (or starting) competition when it's loaded and ready.
        /// Note: this is only called for "fighters" that are detached from other vessels and times out after 10s.
        /// </summary>
        /// <param name="weaponManager"></param>
        public void AddToCompetitionWhenReady(MissileFire weaponManager, bool weaponsFree) => StartCoroutine(AddToCompetitionWhenReadyCoroutine(weaponManager, weaponsFree));
        IEnumerator AddToCompetitionWhenReadyCoroutine(MissileFire weaponManager, bool weaponsFree)
        {
            Vessel vessel = weaponManager.vessel;
            var start = Time.time;
            bool notValid() => IsValidVessel(vessel) != InvalidVesselReason.None || !vessel.loaded || string.IsNullOrEmpty(vessel.vesselName);
            yield return new WaitWhileFixed(() => Time.time - start < 10 && notValid());
            if (notValid())
            {
                if (!SpawnUtils.IsModularMissilePart(vessel.rootPart)) yield break; // Silently abort for MMG missiles containing a WM.
                Debug.Log($"[BDArmory.BDACompetitionMode]: Vessel '{(vessel != null ? vessel.vesselName : "null")}' failed to become valid within 10s (loaded: {vessel.loaded}, invalid: {IsValidVessel(vessel)}), unable to add to competition.");
                yield break;
            }

            // Fix any naming issues.
            SpawnUtils.DeconflictVesselName(vessel, reuse: ContinuousSpawning.Instance.vesselsSpawningContinuously);

            AddToActiveCompetition(vessel, false, weaponsFree); // Don't assign a new team to detached fighters.
        }

        /// <summary>
        /// Add a vessel to an active competition.
        /// 
        /// Note: this can be called before a competition actually starts, e.g., during the initial spawn of continuous spawn.
        /// </summary>
        /// <param name="vessel">The vessel to add.</param>
        /// <param name="assignTeam">Assign a new team to the vessel.</param>
        /// <param name="weaponsFree">Enable guard mode and free the AI to attack.</param>
        public void AddToActiveCompetition(Vessel vessel, bool assignTeam = true, bool weaponsFree = true)
        {
            var weaponManager = vessel.ActiveController().WM;
            if (weaponManager == null) return; // Not a valid competition craft.

            var vesselName = vessel.vesselName;
            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log($"[BDArmory.BDACompetitionMode]: Adding {vessel.vesselName} ({vessel.persistentId}) to the competition.");

            // If a competition is active, update the scoring structure.
            bool competitionStartingOrStarted = competitionStarting || competitionIsActive;
            if (competitionStartingOrStarted && !Scores.Players.Contains(vesselName))
                Scores.AddPlayer(vessel);
            if (ContinuousSpawning.Instance.vesselsSpawningContinuously)
            {
                if (!ContinuousSpawning.Instance.continuousSpawningScores.ContainsKey(vesselName))
                    ContinuousSpawning.Instance.continuousSpawningScores.Add(vesselName, new ContinuousSpawning.ContinuousSpawningScores());
                ContinuousSpawning.Instance.continuousSpawningScores[vesselName].vessel = vessel; // Update some values in the scoring structure.
                ContinuousSpawning.Instance.continuousSpawningScores[vesselName].outOfAmmoTime = 0;
            }
            if (BDATournament.Instance.tournamentStatus == TournamentStatus.Running)
                BDATournament.Instance.AddPlayer(vessel);

            // Set the vessel on the appropriate team.
            if (BDArmorySettings.TAG_MODE && !string.IsNullOrEmpty(Scores.currentlyIT))
            { weaponManager.SetTeam(BDTeam.Get("NO")); }
            else if (assignTeam)
            {
                // Assign the vessel to an unassigned team.
                var weaponManagers = LoadedVesselSwitcher.Instance.WeaponManagers.SelectMany(tm => tm.Value).ToList();
                var currentTeams = weaponManagers.Where(wm => wm != weaponManager).Select(wm => wm.Team).ToHashSet(); // Current teams, excluding us.
                char team = 'A';
                while (currentTeams.Contains(BDTeam.Get(team.ToString())))
                    ++team;
                weaponManager.SetTeam(BDTeam.Get(team.ToString()));
            }

            if (weaponsFree)
            {
                // Update the WM's internal lists (weapons, radars, etc.).
                weaponManager.UpdateList();

                // Enable guard mode if a competition is active, otherwise deactivate it.
                if (weaponManager.guardMode) weaponManager.ToggleGuardMode(); // First, disable guard mode to reset weapon stuff.
                if (competitionIsActive) weaponManager.ToggleGuardMode(); // Then, if the competition has actually started, enable guard mode.
                var ai = weaponManager.AI;
                if (ai != null)
                {
                    ai.ActivatePilot(); // Make sure the AI is active.
                    ai.ReleaseCommand(); // Make sure it's free to attack.
                }
                weaponManager.ForceScan();
            }

            if (ContinuousSpawning.Instance.vesselsSpawningContinuously)
            {
                // Adjust BDACompetitionMode's scoring structures.
                ContinuousSpawning.Instance.UpdateCompetitionScores(vessel, true);
                ++ContinuousSpawning.Instance.continuousSpawningScores[vesselName].spawnCount;
            }
            if (competitionIsActive) // For competitions that are starting these should already be applied.
            {
                if (BDArmorySettings.HACK_INTAKES) SpawnUtils.HackIntakes(vessel, true);
                if (BDArmorySettings.MUTATOR_MODE) SpawnUtils.ApplyMutators(vessel, true);
                if (BDArmorySettings.ENABLE_HOS) SpawnUtils.ApplyHOS(vessel);
                if (BDArmorySettings.RUNWAY_PROJECT) SpawnUtils.ApplyRWP(vessel);
                if (BDArmorySettings.COMP_CONVENIENCE_CHECKS) SpawnUtils.ApplyCompSettingsChecks(vessel);
            }
        }
        #endregion

        public void CheckMemoryUsage() // DEBUG
        {
            List<string> strings =
            [
                "System memory: " + SystemInfo.systemMemorySize + "MB",
                "Reserved: " + UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1024 / 1024 + "MB",
                "Allocated: " + UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1024 / 1024 + "MB",
                "Mono heap: " + UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / 1024 / 1024 + "MB",
                "Mono used: " + UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1024 / 1024 + "MB",
                "GfxDriver: " + UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / 1024 / 1024 + "MB",
                "plus unspecified runtime (native) memory.",
            ];
            Debug.Log("[BDArmory.BDACompetitionMode]: Memory Usage: " + string.Join(", ", strings));
        }

        public void CheckNumbersOfThings() // DEBUG
        {
            List<string> strings =
            [
                "FlightGlobals.Vessels: " + FlightGlobals.Vessels.Count,
                "Non-competitors to remove: " + nonCompetitorsToRemove.Count,
                "EffectBehaviour<ParticleSystem>: " + EffectBehaviour.FindObjectsOfType<ParticleSystem>().Length,
                "EffectBehaviour<KSPParticleEmitter>: " + EffectBehaviour.FindObjectsOfType<KSPParticleEmitter>().Length,
                "KSPParticleEmitters: " + FindObjectsOfType<KSPParticleEmitter>().Length,
                "KSPParticleEmitters including inactive: " + Resources.FindObjectsOfTypeAll(typeof(KSPParticleEmitter)).Length,
            ];
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
            if (BDArmorySettings.DEBUG_SETTINGS_TOGGLE) CheckNumbersOfThings();
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
                    foreach (var module in VesselModuleRegistry.GetModuleEngines(vessel)) if (module != null) ++countParts;
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
                    foreach (var module in VesselModuleRegistry.GetModuleEngines(vessel)) if (module != null) ++countParts;
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
            if (BDArmorySettings.DEBUG_SETTINGS_TOGGLE) Debug.Log($"[BDArmory.BDACompetitionMode]: DEBUG Found {toRemove.Count} null persistent vessel references.");
            foreach (var key in toRemove) FlightGlobals.PersistentVesselIds.Remove(key);

            toRemove.Clear();
            foreach (var key in FlightGlobals.PersistentLoadedPartIds.Keys)
                if (FlightGlobals.PersistentLoadedPartIds[key] == null) toRemove.Add(key);
            if (BDArmorySettings.DEBUG_SETTINGS_TOGGLE) Debug.Log($"[BDArmory.BDACompetitionMode]: DEBUG Found {toRemove.Count} null persistent loaded part references.");
            foreach (var key in toRemove) FlightGlobals.PersistentLoadedPartIds.Remove(key);

            // Usually doesn't find any.
            toRemove.Clear();
            foreach (var key in FlightGlobals.PersistentUnloadedPartIds.Keys)
                if (FlightGlobals.PersistentUnloadedPartIds[key] == null) toRemove.Add(key);
            if (BDArmorySettings.DEBUG_SETTINGS_TOGGLE) Debug.Log($"[BDArmory.BDACompetitionMode]: DEBUG Found {toRemove.Count} null persistent unloaded part references.");
            foreach (var key in toRemove) FlightGlobals.PersistentUnloadedPartIds.Remove(key);

            var protoVessels = HighLogic.CurrentGame.flightState.protoVessels.Where(pv => pv.vesselRef == null).ToList();
            if (BDArmorySettings.DEBUG_SETTINGS_TOGGLE) if (protoVessels.Count > 0) { Debug.Log($"[BDArmory.BDACompetitionMode]: DEBUG Found {protoVessels.Count} inactive ProtoVessels in flightState."); }
            foreach (var protoVessel in protoVessels)
            {
                if (protoVessel == null) continue;
                try
                {
                    ShipConstruction.RecoverVesselFromFlight(protoVessel, HighLogic.CurrentGame.flightState, true);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BDArmory.BDACompetitionMode]: Exception thrown while removing vessel: {e.Message}");
                }
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
