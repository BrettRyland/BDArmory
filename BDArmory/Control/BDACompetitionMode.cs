using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BDArmory.Core;
using BDArmory.Misc;
using BDArmory.Modules;
using BDArmory.UI;
using Mono.Cecil;
using UnityEngine;

namespace BDArmory.Control
{
    // trivial score keeping structure
    public class ScoringData
    {
        public int Score;
        public int PinataHits;
        public int DeathOrder;
        public string lastPersonWhoHitMe;
        public double lastHitTime;
        public double lastFiredTime;
        public string whoGotCleanKill;
        public bool landedState;
        public double lastLandedTime;
        public double landerKillTimer;
        public double AverageSpeed;
        public double AverageAltitude;
        public int averageCount;
        public int previousPartCount;
        public int kills;
        public int cleanKills;
        public int deaths;
        public bool isSpawning = false; // Keep track of respawn attempts
        public HashSet<string> everyoneWhoHitMe = new HashSet<string>();
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class BDACompetitionMode : MonoBehaviour
    {
        public static BDACompetitionMode Instance;

        // keep track of scores, these probably need to be somewhere else
        public Dictionary<string,ScoringData> Scores = new Dictionary<string, ScoringData>();
        //public Dictionary<string, int> Scores = new Dictionary<string, int>();
        //public Dictionary<string, int> PinataHits = new Dictionary<string, int>();

        //public Dictionary<string, string> whoKilledVessels = new Dictionary<string, string>();
        //public Dictionary<string, double> lastHitTime = new Dictionary<string, double>();


        // KILLER GM - how we look for slowest planes
        //public Dictionary<string, double> AverageSpeed = new Dictionary<string, double>();
        //public Dictionary<string, double> AverageAltitude = new Dictionary<string, double>();
        //public Dictionary<string, int> FireCount = new Dictionary<string, int>();
        //public Dictionary<string, int> FireCount2 = new Dictionary<string, int>();

        public Dictionary<string, int> DeathOrder = new Dictionary<string, int>();
        public Dictionary<string, int> whoShotWho = new Dictionary<string, int>();
        public Dictionary<string, string> craftUrls = new Dictionary<string, string>();
        public Dictionary<string, string> teams = new Dictionary<string, string>();

        public bool killerGMenabled = false;
        public bool pinataAlive = false;

        private double competitionStartTime = -1;
        private double nextUpdateTick = -1;
        private double gracePeriod = -1;
        private double decisionTick = -1;
        private int dumpedResults = 4;

        public bool OneOfAKind = false;

        // count up until killing the object 
        public Dictionary<string, int> KillTimer = new Dictionary<string, int>();


        private HashSet<int> ammoIds = new HashSet<int>();

        // time competition was started
        int CompetitionID;

        // pilot actions
        private Dictionary<string, string> pilotActions = new Dictionary<string, string>();
        private string deadOrAlive = "";

        void Awake()
        {
            if (Instance)
            {
                Destroy(Instance);
            }

            Instance = this;
        }

        void OnGUI()
        {
            if (competitionStarting || competitionStartTime > 0)
            {
                GUIStyle cStyle = new GUIStyle(BDArmorySetup.BDGuiSkin.label);
                cStyle.fontStyle = FontStyle.Bold;
                cStyle.fontSize = 22;
                cStyle.alignment = TextAnchor.UpperLeft;

                var displayRow = 100;
                if(!BDArmorySetup.GAME_UI_ENABLED)
                {
                    displayRow = 30;
                }

                Rect cLabelRect = new Rect(30, displayRow, Screen.width, 100);

                GUIStyle cShadowStyle = new GUIStyle(cStyle);
                Rect cShadowRect = new Rect(cLabelRect);
                cShadowRect.x += 2;
                cShadowRect.y += 2;
                cShadowStyle.normal.textColor = new Color(0, 0, 0, 0.75f);
                string message = competitionStatus;
                if(competitionStatus == "")
                {
                    if(FlightGlobals.ActiveVessel != null)
                    {
                        string postFix = "";
                        if (pilotActions.ContainsKey(message))
                        {
                            postFix = " " + pilotActions[message];
                        }
                        if (Scores.ContainsKey(message))
                        {
                            ScoringData vData = Scores[message];
                            if (Planetarium.GetUniversalTime() - vData.lastHitTime < 2)
                            {
                                postFix = " is taking damage from " + vData.lastPersonWhoHitMe;
                            }
                        }
                        message = FlightGlobals.ActiveVessel.GetName() + postFix;
                    }
                }

                GUI.Label(cShadowRect, message, cShadowStyle);
                GUI.Label(cLabelRect, message, cStyle);
                if(!BDArmorySetup.GAME_UI_ENABLED && competitionStartTime > 0)
                {
                    Rect clockRect = new Rect(10, 6, Screen.width, 20);
                    GUIStyle clockStyle = new GUIStyle(cStyle);
                    clockStyle.fontSize = 14;
                    GUIStyle clockShadowStyle = new GUIStyle(clockStyle);
                    clockShadowStyle.normal.textColor = new Color(0, 0, 0, 0.75f);
                    Rect clockShadowRect = new Rect(clockRect);
                    clockShadowRect.x += 2;
                    clockShadowRect.y += 2;
                    var gTime = Planetarium.GetUniversalTime() - competitionStartTime;
                    var minutes = (int)(Math.Floor(gTime/60));
                    var seconds = (int)(gTime % 60);
                    string pTime = minutes.ToString("00") + ":" + seconds.ToString("00") + "     " + deadOrAlive;
                    GUI.Label(clockShadowRect, pTime, clockShadowStyle);
                    GUI.Label(clockRect, pTime, clockStyle);
                }
            }
        }

        public void ResetCompetitionScores()
        {
            // reinitilize everything when the button get hit.
            // ammo names
            // 50CalAmmo, 30x173Ammo, 20x102Ammo, CannonShells
            if (ammoIds.Count == 0)
            {
                ammoIds.Add(PartResourceLibrary.Instance.GetDefinition("50CalAmmo").id);
                ammoIds.Add(PartResourceLibrary.Instance.GetDefinition("30x173Ammo").id);
                ammoIds.Add(PartResourceLibrary.Instance.GetDefinition("20x102Ammo").id);
                ammoIds.Add(PartResourceLibrary.Instance.GetDefinition("CannonShells").id);
            }
            CompetitionID = (int)DateTime.UtcNow.Subtract(new DateTime(2020, 1, 1)).TotalSeconds;
            DoPreflightChecks();
            Scores.Clear();
            DeathOrder.Clear();
            whoShotWho.Clear();
            KillTimer.Clear();
            dumpedResults = 5;
            competitionStartTime = Planetarium.GetUniversalTime();
            nextUpdateTick = competitionStartTime + 2; // 2 seconds before we start tracking
            gracePeriod = competitionStartTime + 60;
            decisionTick = competitionStartTime + 60; // every 60 seconds we do nasty things
            
            // now find all vessels with weapons managers
            foreach (var v in BDATargetManager.LoadedVessels.Where(v => v != null && v.loaded))
            {
                IBDAIControl pilot = v.FindPartModuleImplementing<IBDAIControl>();
                if (pilot == null || !pilot.weaponManager || pilot.weaponManager.Team.Neutral)
                    continue;
                
                // put these in the scoring dictionary - these are the active participants
                ScoringData vDat = new ScoringData();
                vDat.lastPersonWhoHitMe = "";
                vDat.lastFiredTime = Planetarium.GetUniversalTime();
                vDat.previousPartCount = v.parts.Count();
                Scores[v.GetName()] = vDat;
            }
        }

        //Competition mode
        public bool competitionStarting;
        string competitionStatus = "";
        Coroutine competitionRoutine;

        public void StartCompetitionMode(float distance, bool spawn = false)
        {
            if (!competitionStarting)
            {
                Debug.Log("[BDArmoryCompetition:" + CompetitionID.ToString() + "]: Starting Competition ");
                competitionRoutine = StartCoroutine(DogfightCompetitionModeRoutine(distance, spawn));
            }
        }

        public void StartRapidDeployment(float distance)
        {
            if (!competitionStarting)
            {
                ResetCompetitionScores();
                Debug.Log("[BDArmoryCompetition:" + CompetitionID.ToString() + "]: Starting Rapid Deployment ");
                string commandString = "0:SetThrottle:100\n0:ActionGroup:14:0\n0:Stage\n35:ActionGroup:1\n10:ActionGroup:2\n3:RemoveFairings\n0:ActionGroup:3\n0:ActionGroup:12:1\n1:TogglePilot:1\n6:ToggleGuard:1\n0:ActionGroup:16:0\n5:EnableGM\n5:RemoveDebris\n0:ActionGroup:16:0\n";
                competitionRoutine = StartCoroutine(SequencedCompetition(commandString));
            }
        }

        public void StopCompetition()
        {
            if (competitionRoutine != null)
            {
                StopCoroutine(competitionRoutine);
            }

            competitionStarting = false;
        }

        IEnumerator DogfightCompetitionModeRoutine(float distance, bool spawn = false)
        {
            competitionStarting = true;
            if (spawn)
            {
                var pos = 0;
                var team = "A";
                var spawnCenter = FlightGlobals.camera_position;
                var crafts = Directory.GetFiles(Environment.CurrentDirectory + $"/AutoSpawn").Where(f => f.EndsWith(".craft")).ToList();
                foreach (var craftUrl in crafts)
                {
                    yield return null;
                    yield return null;
                    var vessel = SpawnCraft(craftUrl, pos++ / 10d, spawnCenter);
            
                    while (!vessel.loaded || BDATargetManager.LoadedVessels.All(vv => vessel.id != vv.id))
                    {
                        yield return new WaitForSeconds(.1f);
                    }
                    
                    yield return new WaitForSeconds(.5f);
                    
                    var pilot = vessel.FindPartModuleImplementing<IBDAIControl>();
                    // The code below was used to automatically set teams, but this is now being done by team set in weapon Manager
                    // var wpnMgr = vessel.FindPartModulesImplementing<MissileFire>().GetEnumerator();
                    // team = wpnMgr.Current.team;
                    // team = pilot.weaponManager.SetTeam(BDTeam.Get(team.ToString())); 
                    // team++;
                }
                
                yield return new WaitForSeconds(1f);

                foreach (var vessel in BDATargetManager.LoadedVessels.Where(v => v != null && v.loaded))
                {
                    vessel.Landed = false;
                    vessel.Splashed = false;
                    vessel.situation = Vessel.Situations.FLYING;
                    vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, false);
                }
            }
            
            ResetCompetitionScores();
            
            competitionStatus = "Competition: Pilots are taking off.";
            var pilots = new Dictionary<BDTeam, List<IBDAIControl>>();
            HashSet<IBDAIControl> readyToLaunch = new HashSet<IBDAIControl>();
            foreach (var v in BDATargetManager.LoadedVessels.Where(v => v != null && v.loaded))
            {
                IBDAIControl pilot = v.FindPartModuleImplementing<IBDAIControl>();
                if (pilot == null || !pilot.weaponManager || pilot.weaponManager.Team.Neutral)
                    continue;
                
                if (!pilots.TryGetValue(pilot.weaponManager.Team, out List<IBDAIControl> teamPilots))
                {
                    teamPilots = new List<IBDAIControl>();
                    pilots.Add(pilot.weaponManager.Team, teamPilots);
                    Debug.Log("[BDArmoryCompetition:" + CompetitionID.ToString() + "]: Adding Team " + pilot.weaponManager.Team.Name);
                }
                teamPilots.Add(pilot);
                Debug.Log("[BDArmoryCompetition:" + CompetitionID.ToString() + "]: Adding Pilot " + pilot.vessel.GetName());
                readyToLaunch.Add(pilot);
            }

            foreach (var pilot in readyToLaunch)
            {
                //pilot.vessel.ActionGroups.ToggleGroup(KM_dictAG[1]);
                BDArmory.Misc.Misc.fireNextNonEmptyStage(pilot.vessel);
                pilot.CommandTakeOff();
                if (pilot.weaponManager.guardMode)
                {
                    pilot.weaponManager.ToggleGuardMode();
                }
            }

            //clear target database so pilots don't attack yet
            BDATargetManager.ClearDatabase();

            foreach(var vname in Scores.Keys)
            {
                Debug.Log("[BDACompetitionMode] Adding Score Tracker For " + vname);
            }

            if (pilots.Count < 2)
            {
                Debug.Log("[BDArmory]: Unable to start competition mode - one or more teams is empty");
                competitionStatus = "Competition: Failed!  One or more teams is empty.";
                yield return new WaitForSeconds(2);
                competitionStarting = false;
                yield break;
            }

            var leaders = new List<IBDAIControl>();
            using (var pilotList = pilots.GetEnumerator())
                while (pilotList.MoveNext())
                {
                    leaders.Add(pilotList.Current.Value[0]);
                    pilotList.Current.Value[0].weaponManager.wingCommander.CommandAllFollow();
                }

            //wait till the leaders are ready to engage (airborne for PilotAI)
            bool ready = false;
            while (!ready)
            {
                ready = true;
                using (var leader = leaders.GetEnumerator())
                    while (leader.MoveNext())
                        if (leader.Current != null && !leader.Current.CanEngage())
                        {
                            ready = false;
                            yield return new WaitForSeconds(1);
                            break;
                        }
            }

            using (var leader = leaders.GetEnumerator())
                while (leader.MoveNext())
                    if (leader.Current == null)
                        StopCompetition();

            competitionStatus = "Competition: Sending pilots to start position.";
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
                leaders[i].CommandFlyTo(VectorUtils.WorldPositionToGeoCoords(startDirection, FlightGlobals.currentMainBody));
                startDirection = directionStep * startDirection;
            }

            Vector3 centerGPS = VectorUtils.WorldPositionToGeoCoords(center, FlightGlobals.currentMainBody);

            //wait till everyone is in position
            competitionStatus = "Competition: Waiting for teams to get in position.";
            bool waiting = true;
            var sqrDistance = distance * distance;
            while (waiting)
            {
                waiting = false;

                using (var leader = leaders.GetEnumerator())
                    while (leader.MoveNext())
                    {
                        if (leader.Current == null)
                            StopCompetition();

                        using (var otherLeader = leaders.GetEnumerator())
                            while (otherLeader.MoveNext())
                            {
                                if (leader.Current == otherLeader.Current)
                                    continue;
                                if ((leader.Current.transform.position - otherLeader.Current.transform.position).sqrMagnitude < sqrDistance)
                                    waiting = true;
                            }

                        // Increase the distance for large teams
                        var sqrTeamDistance = (800 + 100 * pilots[leader.Current.weaponManager.Team].Count) * (800 + 100 * pilots[leader.Current.weaponManager.Team].Count);
                        using (var pilot = pilots[leader.Current.weaponManager.Team].GetEnumerator())
                            while (pilot.MoveNext())
                                if (pilot.Current != null
                                        && pilot.Current.currentCommand == PilotCommands.Follow
                                        && (pilot.Current.vessel.CoM - pilot.Current.commandLeader.vessel.CoM).sqrMagnitude > 1000f * 1000f)
                                    waiting = true;

                        if (waiting) break;
                    }

                yield return null;
            }

            //start the match
            using (var teamPilots = pilots.GetEnumerator())
                while (teamPilots.MoveNext())
                    using (var pilot = teamPilots.Current.Value.GetEnumerator())
                        while (pilot.MoveNext())
                        {
                            if (pilot.Current == null) continue;

                            if (!pilot.Current.weaponManager.guardMode)
                                pilot.Current.weaponManager.ToggleGuardMode();

                            using (var leader = leaders.GetEnumerator())
                                while (leader.MoveNext())
                                    BDATargetManager.ReportVessel(pilot.Current.vessel, leader.Current.weaponManager);

                            pilot.Current.ReleaseCommand();
                            pilot.Current.CommandAttack(centerGPS);
                            pilot.Current.vessel.altimeterDisplayState = AltimeterDisplayState.AGL;
                        }

            LoadedVesselSwitcher.AutoCameraSwitch(true);
            competitionStatus = "Competition starting!  Good luck!";
            yield return new WaitForSeconds(2);
            competitionStatus = "";
            competitionStarting = false;
        }

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

        // transmits a bunch of commands to make things happen
        // this is a really dumb sequencer with text commands
        // 0:ThrottleMax
        // 0:Stage
        // 30:ActionGroup:1
        // 35:ActionGroup:2
        // 40:ActionGroup:3
        // 41:TogglePilot
        // 45:ToggleGuard

        private List<IBDAIControl> getAllPilots()
        {
            var pilots = new List<IBDAIControl>();
            using (var loadedVessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedVessels.MoveNext())
                {
                    if (loadedVessels.Current == null || !loadedVessels.Current.loaded)
                        continue;
                    IBDAIControl pilot = loadedVessels.Current.FindPartModuleImplementing<IBDAIControl>();
                    if (pilot == null || !pilot.weaponManager || pilot.weaponManager.Team.Neutral)
                        continue;
                    pilots.Add(pilot);
                }
            return pilots;
        }

        private void DoPreflightChecks()
        {
            var pilots = getAllPilots();
            foreach (var pilot in pilots)
            {
                if (pilot.vessel == null) continue;

                enforcePartCount(pilot.vessel);
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
            if (!OneOfAKind) return;
            List<Part>.Enumerator parts = vessel.parts.GetEnumerator();
            Dictionary<string, int> partCounts = new Dictionary<string, int>();
            List<Part> partsToKill = new List<Part>();
            List<Part> ammoBoxes = new List<Part>();
            int engineCount = 0;
            while (parts.MoveNext())
            {
                if (parts.Current == null) continue;
                var partName = parts.Current.name;
                //Debug.Log("Part " + vessel.GetName() + " " + partName);
                if(partCounts.ContainsKey(partName))
                {
                    partCounts[partName]++;
                } else
                {
                    partCounts[partName] = 1;
                }
                if(allowedEngines.Contains(partName))
                {
                    engineCount++;
                }
                if(bannedParts.Contains(partName))
                {
                    partsToKill.Add(parts.Current);
                }
                if(allowedLandingGear.Contains(partName))
                {
                    // duplicates allowed
                    continue;
                }
                if(ammoParts.Contains(partName))
                {
                    // can only figure out limits after counting engines.
                    ammoBoxes.Add(parts.Current);
                    continue;
                }
                if(partCounts[partName] > 1)
                {
                    partsToKill.Add(parts.Current);
                }
            }
            
            if(engineCount == 0)
            {
                engineCount = 1;
            }

            while (ammoBoxes.Count > engineCount * 3)
            {
                partsToKill.Add(ammoBoxes[ammoBoxes.Count - 1]);
                ammoBoxes.RemoveAt(ammoBoxes.Count - 1);
            }
            
            if(partsToKill.Count > 0)
            {
                Debug.Log("[BDArmoryCompetition:" + CompetitionID.ToString() + "] Vessel Breaking Part Count Rules " + vessel.GetName());
                foreach(var part in partsToKill)
                {
                    Debug.Log("[BDArmoryCompetition:" + CompetitionID.ToString() + "] KILLPART:" + part.name + ":" + vessel.GetName());
                    PartExploderSystem.AddPartToExplode(part);
                }
            }
        }

        private static SemaphoreSlim spawnLock = new SemaphoreSlim(1,1);
        public IEnumerator SpawnCraftRoutine(string craftUrl, string name)
        {
            while (!spawnLock.Wait(10))
            {
                yield return new WaitForSeconds(1f);
            }
            try
            {
                yield return new WaitForSeconds(1f);
                Debug.Log("[BDArmory]: Starting spawn coroutine for: " + name); // SPAWN DEBUGGING
                if (!BDATargetManager.LoadedVessels.Any(v => v.loaded && v.GetName() == name))
                {
                    var vessel = SpawnCraft(craftUrl, .5d, FlightGlobals.camera_position);

                    while (!vessel.loaded || BDATargetManager.LoadedVessels.All(vv => vessel.id != vv.id))
                    {
                        yield return new WaitForSeconds(.1f);
                    }

                    Scores[name].deaths++;
                   //  Debug.Log("[BDArmory]: Last person who hit " + name + ", " + craftUrl + " is: " + Scores[name].lastPersonWhoHitMe); // SCORE DEBUGGING
                   // Debug.Log("[BDArmory]: All hitters are: " + Scores[name].everyoneWhoHitMe); // SCORE DEBUGGING
                   // Debug.Log("[BDArmory]: Who got clean kill is: " + Scores[name].whoGotCleanKill); // SCORE DEBUGGING
                    if (!string.IsNullOrEmpty(Scores[name].whoGotCleanKill))
                    {
                        Scores[Scores[name].whoGotCleanKill].cleanKills++;
                    }
                    else if (Scores[name].everyoneWhoHitMe != null)
                    {
                        foreach (var hitter in Scores[name].everyoneWhoHitMe)
                        {
                            Scores[hitter].kills++;
                        }
                    }
                    
                    Scores[name].lastPersonWhoHitMe = "";
                    Scores[name].whoGotCleanKill = "";
                    Scores[name].everyoneWhoHitMe.Clear();
                    DeathOrder.Remove(name);
                    yield return new WaitForSeconds(.5f);

                    var team = teams[vessel.GetName()];
                    var pilot = vessel.FindPartModuleImplementing<IBDAIControl>();
                    Debug.Log("[BDArmoryCompetition:" + CompetitionID.ToString() + "] Setting team " + team + " for " + vessel.GetName());
                    pilot.weaponManager.SetTeam(BDTeam.Get(team)); 

                    yield return new WaitForSeconds(1f);
                    vessel.Landed = false;
                    vessel.Splashed = false;
                    vessel.situation = Vessel.Situations.FLYING;
                    vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, false);

                    if (pilot != null && pilot.weaponManager)
                    {
                        //pilot.vessel.ActionGroups.ToggleGroup(KM_dictAG[1]);
                        BDArmory.Misc.Misc.fireNextNonEmptyStage(pilot.vessel);
                        pilot.CommandTakeOff();
                        if (!pilot.weaponManager.guardMode)
                        {
                            pilot.vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, false); // Try retracting the gear one more time in case it didn't happen before
                            pilot.weaponManager.ToggleGuardMode();
                        }
                    }
                }
            }
            finally
            {
                Scores[name].isSpawning = false; // Craft no longer spawning
                spawnLock.Release();
                Debug.Log("[BDArmory]: Spawn coroutine finished for: " + name); // SPAWN DEBUGGING
            }
        }

        private int angle = 0;
        private Vessel SpawnCraft(string craftUrl, double pos, Vector3d center)
        {
            var thisAngle = pos * 2d * Math.PI;
            var craftPosition = center + new Vector3d(Math.Cos(thisAngle) * 300, Math.Sin(thisAngle) * 300);
            var position = new Vector3d(FlightGlobals.currentMainBody.GetLatitude(craftPosition), FlightGlobals.currentMainBody.GetLongitude(craftPosition), 2500);
            var vessel = VesselSpawner.Instance.SpawnVesselFromCraftFile(craftUrl, position, 90, 0);
            Debug.Log("[BDArmoryCompetition:" + CompetitionID.ToString() + "] Spawning vessel " + vessel.GetName() + $" P:{pos} A:{thisAngle} X:{position.x} Y:{position.y} Z:{position.z}");
            craftUrls[vessel.GetName()] = craftUrl;
            // vessel.SetPosition(position, true);
            return vessel;
        }

        private void DoRapidDeploymentMassTrim()
        {
            // in rapid deployment this verified masses etc. 
            var oreID = PartResourceLibrary.Instance.GetDefinition("Ore").id;
            var pilots = getAllPilots();
            var lowestMass = 100000000000000f;
            var highestMass = 0f;
            foreach(var pilot in pilots)
            {

                if (pilot.vessel == null) continue;
                
                var notShieldedCount = 0;
                List<Part>.Enumerator parts = pilot.vessel.parts.GetEnumerator();

                while (parts.MoveNext())
                {
                    if (parts.Current == null) continue;
                    // count the unshielded parts
                    if(!parts.Current.ShieldedFromAirstream)
                    {
                        notShieldedCount++;
                    }
                    IEnumerator<PartResource> resources = parts.Current.Resources.GetEnumerator();
                    while (resources.MoveNext())
                    {
                        if (resources.Current == null) continue;

                        if (resources.Current.resourceName == "Ore")
                        {
                            if(resources.Current.maxAmount == 1500)
                            {
                                resources.Current.amount = 0;
                            }
                            // oreMass = 10;
                            // ore to add = difference / 10;
                            // is mass in tons or KG?
                            //Debug.Log("[BDACompetitionMode] RESOURCE:" + parts.Current.partName + ":" + resources.Current.maxAmount);
                            
                        }
                        else if(resources.Current.resourceName == "LiquidFuel")
                        {
                            if(resources.Current.maxAmount == 3240)
                            {
                                resources.Current.amount = 2160;
                            }
                        }
                        else if(resources.Current.resourceName == "Oxidizer")
                        {
                            if(resources.Current.maxAmount == 3960)
                            {
                                resources.Current.amount = 2640;
                            }
                        }
                    }

                }
                var mass = pilot.vessel.GetTotalMass();

                Debug.Log("[BDArmoryCompetition:" + CompetitionID.ToString() + "] UNSHIELDED:" + notShieldedCount.ToString() + ":" + pilot.vessel.GetName());

                Debug.Log("[BDArmoryCompetition:" + CompetitionID.ToString() + "] MASS:" + mass.ToString() + ":" + pilot.vessel.GetName());
                if(mass < lowestMass)
                {
                    lowestMass = mass;
                } 
                if(mass > highestMass)
                {
                    highestMass = mass;
                }
            }

            var difference = highestMass - lowestMass;
            //
            foreach(var pilot in pilots)
            {
                if (pilot.vessel == null) continue;
                var mass = pilot.vessel.GetTotalMass();
                var extraMass = highestMass - mass;
                List<Part>.Enumerator parts = pilot.vessel.parts.GetEnumerator();
                
                while (parts.MoveNext())
                {
                    bool massAdded = false;
                    if (parts.Current == null) continue;
                    IEnumerator<PartResource> resources = parts.Current.Resources.GetEnumerator();
                    while (resources.MoveNext())
                    {
                        if (resources.Current == null) continue;
                        if (resources.Current.resourceName == "Ore")
                        {
                            // oreMass = 10;
                            // ore to add = difference / 10;
                            // is mass in tons or KG?
                            if (resources.Current.maxAmount == 1500)
                            {
                                var oreAmount = extraMass / 0.01; // 10kg per unit of ore
                                if (oreAmount > 1500) oreAmount = 1500;
                                resources.Current.amount = oreAmount;
                            }
                            Debug.Log("[BDArmoryCompetition:" + CompetitionID.ToString() + "] RESOURCEUPDATE:" + pilot.vessel.GetName() + ":" + resources.Current.amount);
                            massAdded = true;
                        }
                    }
                    if (massAdded) break;
                }
            }
        }

        IEnumerator SequencedCompetition(string commandList)
        {
            competitionStarting = true;
            double startTime = Planetarium.GetUniversalTime();
            double nextStep = startTime;
            // split list of events into lines
            var events = commandList.Split('\n');

            foreach (var cmdEvent in events)
            {
                // parse the event
                competitionStatus = cmdEvent;
                var parts = cmdEvent.Split(':');
                if (parts.Count() == 1)
                {
                    Debug.Log("[BDACompetitionMode] Competition Command not parsed correctly " + cmdEvent);
                    break;
                }
                var timeStep = int.Parse(parts[0]);
                nextStep = Planetarium.GetUniversalTime() + timeStep;
                while (Planetarium.GetUniversalTime() < nextStep)
                {
                    yield return null;
                }

                List<IBDAIControl> pilots;
                var command = parts[1];

                switch (command)
                {
                    case "Stage":
                        // activate stage
                        pilots = getAllPilots();
                        foreach (var pilot in pilots)
                        {
                            Misc.Misc.fireNextNonEmptyStage(pilot.vessel);
                        }
                        break;
                    case "ActionGroup":
                        pilots = getAllPilots();
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
                            else
                            {
                                Debug.Log("[BDACompetitionMode] Competition Command not parsed correctly " + cmdEvent);
                            }
                        }
                        break;
                    case "TogglePilot":
                        if (parts.Count() == 3)
                        {
                            var newState = true;
                            if (parts[2] == "0")
                            {
                                newState = false;
                            }
                            pilots = getAllPilots();
                            foreach (var pilot in pilots)
                            {
                                if(newState != pilot.pilotEnabled)
                                    pilot.TogglePilot();    
                            }
                        }
                        else
                        {
                            pilots = getAllPilots();
                            foreach (var pilot in pilots)
                            {
                                pilot.TogglePilot();
                            }
                        }
                        break;
                    case "ToggleGuard":
                        if (parts.Count() == 3)
                        {
                            var newState = true;
                            if (parts[2] == "0")
                            {
                                newState = false;
                            }
                            pilots = getAllPilots();
                            foreach (var pilot in pilots)
                            {
                                if (pilot.weaponManager != null && pilot.weaponManager.guardMode != newState)
                                    pilot.weaponManager.ToggleGuardMode();
                            }
                        }
                        else
                        {
                            pilots = getAllPilots();
                            foreach (var pilot in pilots)
                            {
                                if (pilot.weaponManager != null) pilot.weaponManager.ToggleGuardMode();
                            }
                        }
                        
                        break;
                    case "SetThrottle":
                        if (parts.Count() == 3)
                        {
                            pilots = getAllPilots();
                            foreach (var pilot in pilots)
                            {
                                var throttle = int.Parse(parts[2]) * 0.01f;
                                pilot.vessel.ctrlState.mainThrottle = throttle;
                                pilot.vessel.ctrlState.killRot = true;
                            }
                        }
                        break;
                    case "RemoveDebris":
                        // remove anything that doesn't contain BD Armory modules
                        RemoveDebris();
                        break;
                    case "RemoveFairings":
                        // removes the fairings after deplyment to stop the physical objects consuming CPU
                        var rmObj = new List<physicalObject>();
                        foreach (var phyObj in FlightGlobals.physicalObjects) {
                            if (phyObj.name == "FairingPanel") rmObj.Add(phyObj);
                            Debug.Log("[RemoveFairings] " + phyObj.name);
                        }
                        foreach(var phyObj in rmObj)
                        {
                            FlightGlobals.removePhysicalObject(phyObj);
                        }

                        break;
                    case "EnableGM":
                        killerGMenabled = true;
                        decisionTick = Planetarium.GetUniversalTime() + 60;
                        ResetSpeeds();
                        break;
                }
            }
            competitionStatus = "";
            // will need a terminator routine
            competitionStarting = false;
        }

        private void RemoveDebris()
        {
            // only call this if enabled
            // remove anything that doesn't contain BD Armory modules
            var debrisToKill = new List<Vessel>();
            foreach (var vessel in FlightGlobals.Vessels)
            {
                bool activePilot = false;
                if (vessel.GetName() == "Pinata")
                {
                    activePilot = true;
                }
                else
                {
                    int foundActiveParts = 0;
                    using (var wms = vessel.FindPartModulesImplementing<MissileFire>().GetEnumerator())
                        while (wms.MoveNext())
                            if (wms.Current != null)
                            {
                                foundActiveParts++;
                                break;
                            }

                    using (var wms = vessel.FindPartModulesImplementing<IBDAIControl>().GetEnumerator())
                        while (wms.MoveNext())
                            if (wms.Current != null)
                            {
                                foundActiveParts++;
                                break;
                            }

                    using (var wms = vessel.FindPartModulesImplementing<ModuleCommand>().GetEnumerator())
                        while (wms.MoveNext())
                            if (wms.Current != null)
                            {
                                foundActiveParts++;
                                break;
                            }
                    activePilot = foundActiveParts == 3;
                }
                if (!activePilot)
                    debrisToKill.Add(vessel);
            }
            foreach (var vessel in debrisToKill)
            {
                Debug.Log("[RemoveObjects] " + vessel.GetName());
                vessel.Die();
            }
        }


        // ask the GM to find a 'victim' which means a slow pilot who's not shooting very much
        // obviosly this is evil. 
        // it's enabled by right clicking the M button.
        // I also had it hooked up to the death of the Pinata but that's disconnected right now
        private void FindVictim()
        {
            if (decisionTick < 0) return;
            if (Planetarium.GetUniversalTime() < decisionTick) return;
            decisionTick = Planetarium.GetUniversalTime() + 60;
            RemoveDebris();
            if (!killerGMenabled) return;
            if (Planetarium.GetUniversalTime() - competitionStartTime < 150) return;
            // arbitrary and capbricious decisions of life and death

            bool hasFired = true;
            Vessel worstVessel = null;
            double slowestSpeed = 100000;
            int vesselCount = 0;
            using (var loadedVessels = BDATargetManager.LoadedVessels.GetEnumerator())
                while (loadedVessels.MoveNext())
                {
                    if (loadedVessels.Current == null || !loadedVessels.Current.loaded)
                        continue;
                    IBDAIControl pilot = loadedVessels.Current.FindPartModuleImplementing<IBDAIControl>();
                    if (pilot == null || !pilot.weaponManager || pilot.weaponManager.Team.Neutral)
                        continue;



                    var vesselName = loadedVessels.Current.GetName();
                    if (!Scores.ContainsKey(vesselName))
                        continue;

                    vesselCount++;
                    ScoringData vData = Scores[vesselName];

                    var averageSpeed = vData.AverageSpeed / vData.averageCount;
                    var averageAltitude = vData.AverageAltitude / vData.averageCount;
                    averageSpeed = averageAltitude + (averageSpeed * averageSpeed / 200); // kinetic & potential energy
                    if(pilot.weaponManager != null)
                    {
                        if (!pilot.weaponManager.guardMode) averageSpeed *= 0.5;
                    }

                    bool vesselNotFired = (Planetarium.GetUniversalTime() -  vData.lastFiredTime) > 120; // if you can't shoot in 2 minutes you're at the front of line

                    Debug.Log("[BDArmory] Victim Check " + vesselName + " " + averageSpeed.ToString() + " " + vesselNotFired.ToString());
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
                if (!Scores.ContainsKey(worstVessel.GetName()))
                {
                    if (Scores[worstVessel.GetName()].lastPersonWhoHitMe == "")
                    {
                        Scores[worstVessel.GetName()].lastPersonWhoHitMe = "GM";
                    }
                }
                Debug.Log("[BDArmory] killing " + worstVessel.GetName());
                Misc.Misc.ForceDeadVessel(worstVessel);
            }
            ResetSpeeds();
        }

        // reset all the tracked speeds, and copy the shot clock over, because I wanted 2 minutes of shooting to count
        private void ResetSpeeds()
        {
            Debug.Log("[BDArmory] resetting kill clock");
            foreach (var vname in Scores.Keys)
            {
                if (Scores[vname].averageCount == 0)
                {
                    Scores[vname].AverageAltitude = 0;
                    Scores[vname].AverageSpeed = 0;
                }
                else
                {
                    // ensures we always have a sensible value in here
                    Scores[vname].AverageAltitude /= Scores[vname].averageCount;
                    Scores[vname].AverageSpeed /= Scores[vname].averageCount;
                    Scores[vname].averageCount = 1;
                }
            }
        }

        // the competition update system
        // cleans up dead vessels, tries to track kills (badly)
        // all of these are based on the vessel name which is probably sub optimal
        public void DoUpdate()
        {
            // should be called every frame during flight scenes
            if (competitionStartTime < 0) return;
            if (Planetarium.GetUniversalTime() < nextUpdateTick)
                return;
            int updateTickLength = 2;
            HashSet<Vessel> vesselsToKill = new HashSet<Vessel>();
            nextUpdateTick = nextUpdateTick + updateTickLength;
            int numberOfCompetitiveVessels = 0;
            if(!competitionStarting)
                competitionStatus = "";
            HashSet<string> alive = new HashSet<string>();
            string doaUpdate = "ALIVE: ";
            //Debug.Log("[BDArmoryCompetitionMode] Calling Update");
            // check all the planes
            List<Vessel>.Enumerator v = FlightGlobals.Vessels.GetEnumerator();
            while (v.MoveNext())
            {
                if (v.Current == null || !v.Current.loaded || v.Current.packed)
                    continue;

                // keep teams updated (this is super lazy)
                var pilot = v.Current.FindPartModuleImplementing<IBDAIControl>();
                if (pilot != null)
                {
                    teams[pilot.vessel.vesselName] = pilot.weaponManager.Team.Name; 
                }

                MissileFire mf = null;
                using (var wms = v.Current.FindPartModulesImplementing<MissileFire>().GetEnumerator())
                    while (wms.MoveNext())
                        if (wms.Current != null)
                        {
                            mf = wms.Current;
                            break;
                        }

                if (mf != null)
                {
                    // things to check
                    // does it have fuel?
                    string vesselName = v.Current.GetName();
                    ScoringData vData = null;
                    if(Scores.ContainsKey(vesselName) )
                    {
                        vData = Scores[vesselName];
                    }

                    // this vessel really is alive
                    if (!vesselName.EndsWith("Debris") && !vesselName.EndsWith("Plane") && !vesselName.EndsWith("Probe"))
                    {
                        if (DeathOrder.ContainsKey(vesselName))
                        {
                            Debug.Log("[BDArmoryCompetition] Dead vessel found alive " + vesselName);
                            //DeathOrder.Remove(vesselName);
                        }
                        // vessel is still alive
                        alive.Add(vesselName);
                        doaUpdate += " *" + vesselName + "* ";
                        numberOfCompetitiveVessels++;
                    }
                    pilotActions[vesselName] = "";

                    // try to create meaningful activity strings
                    if (mf.AI != null && mf.AI.currentStatus != null)
                    {
                        pilotActions[vesselName] = "";
                        if(mf.vessel.LandedOrSplashed)
                        {
                            if (mf.vessel.Landed)
                            {
                                pilotActions[vesselName] = " landed";
                            }
                            else
                            {
                                pilotActions[vesselName] = " splashed";
                            }
                        }
                        var activity = mf.AI.currentStatus;
                        if (activity == "Follow")
                        {
                            if (mf.AI.commandLeader != null && mf.AI.commandLeader.vessel != null)
                            {
                                pilotActions[vesselName] = " following " + mf.AI.commandLeader.vessel.GetName();
                            }
                        } else if(activity == "Gain Alt")
                        {
                            pilotActions[vesselName] = " gaining altitude";
                        } else if(activity == "Orbiting")
                        {
                            pilotActions[vesselName] = " orbiting";
                        } else if(activity == "Extending")
                        {
                            pilotActions[vesselName] = " extending ";
                        } else if(activity == "AvoidCollision")
                        {
                            pilotActions[vesselName] = " avoiding collision";
                        } else if(activity == "Evading")
                        {
                            if(mf.incomingThreatVessel != null)
                            {
                                pilotActions[vesselName] = " evading " + mf.incomingThreatVessel.GetName();
                            } else
                            {
                                pilotActions[vesselName] = " taking evasive action";
                            }
                        } else if(activity == "Attack") {  
                            if (mf.currentTarget != null && mf.currentTarget.name != null)
                            {
                                pilotActions[vesselName] = " attacking " + mf.currentTarget.Vessel.GetName();
                            }
                            else
                            {
                                pilotActions[vesselName] = " attacking ";
                            }
                        }
                    }

                    // update the vessel scoring structure
                    if (vData != null)
                    {
                        var partCount = v.Current.parts.Count();
                        if (partCount != vData.previousPartCount)
                        {
                            // part count has changed, check for broken stuff
                            enforcePartCount(v.Current);
                        }
                        vData.previousPartCount = v.Current.parts.Count();

                        if (v.Current.LandedOrSplashed)
                        {
                            if (!vData.landedState)
                            {
                                // was flying, is now landed
                                vData.lastLandedTime = Planetarium.GetUniversalTime();
                                vData.landedState = true;
                                if (vData.landerKillTimer == 0)
                                {
                                    vData.landerKillTimer = Planetarium.GetUniversalTime();
                                }
                            }
                        }
                        else
                        {
                            if (vData.landedState)
                            {
                                vData.lastLandedTime = Planetarium.GetUniversalTime();
                                vData.landedState = false;
                            }
                            if (vData.landerKillTimer != 0)
                            {
                                // safely airborne for 15 seconds
                                if (Planetarium.GetUniversalTime() - vData.landerKillTimer > 15)
                                {
                                    vData.landerKillTimer = 0;
                                }
                            }
                        }
                    }

                    // after this poitn we're checking things that might result in kills.
                    if (Planetarium.GetUniversalTime() < gracePeriod) continue;
                    
                    // keep track if they're shooting for the GM
                    if (mf.currentGun != null)
                    {
                        if (mf.currentGun.recentlyFiring)
                        {
                            // keep track that this aircraft was shooting things
                            if (vData != null)
                            {
                                vData.lastFiredTime = Planetarium.GetUniversalTime();
                            }
                            if (mf.currentTarget != null && mf.currentTarget.Vessel != null)
                            {
                                pilotActions[vesselName] = " shooting at " + mf.currentTarget.Vessel.GetName();
                            }
                        }
                    }
                    // does it have ammunition: no ammo => Disable guard mode
                    if (!BDArmorySettings.INFINITE_AMMO)
                    {
                        double totalAmmo = 0;
                        foreach (var ammoID in ammoIds)
                        {
                            v.Current.GetConnectedResourceTotals(ammoID, out double ammoCurrent, out double ammoMax);
                            totalAmmo += ammoCurrent;
                        }
                        
                        if (totalAmmo == 0)
                        {
                            // disable guard mode when out of ammo
                            if (mf.guardMode)
                            {
                                mf.guardMode = false;
                                if (vData != null && (Planetarium.GetUniversalTime() - vData.lastHitTime < 2))
                                {
                                    competitionStatus = vesselName + " damaged by " + vData.lastPersonWhoHitMe + " and lost weapons";
                                } else {                                
                                    competitionStatus = vesselName + " is out of Ammunition";
                                }
                            }
                        }
                    }

                    // update the vessel scoring structure
                    if (vData != null)
                    {
                        vData.AverageSpeed += v.Current.srfSpeed;
                        vData.AverageAltitude += v.Current.altitude;
                        vData.averageCount++;
                        if(vData.landedState)
                        {
                            if(Planetarium.GetUniversalTime() - vData.landerKillTimer > 6)
                            {
                                vesselsToKill.Add(mf.vessel);
                                competitionStatus = vesselName + " landed too long.";
                            }
                        }
                    }


                    bool shouldKillThis = false;

                    // if vessels is Debris, kill it
                    if (vesselName.Contains("Debris"))
                    {
                        // reap this vessel
                        shouldKillThis = true;
                    }

                    if (vData == null) shouldKillThis = true;

                    // 15 second time until kill, maybe they recover?
                    if (KillTimer.ContainsKey(vesselName))
                    {
                        if (shouldKillThis)
                        {
                            KillTimer[vesselName] += updateTickLength;
                        }
                        else
                        {
                            KillTimer[vesselName] -= updateTickLength;
                        }
                        if (KillTimer[vesselName] > 15)
                        {
                            vesselsToKill.Add(mf.vessel);
                            competitionStatus = vesselName + " exceeded kill timer";
                        }
                        else if (KillTimer[vesselName] < 0)
                        {
                            KillTimer.Remove(vesselName);
                        }
                    }
                    else
                    {
                        if (shouldKillThis)
                            KillTimer[vesselName] = updateTickLength;
                    }
                    
                }
            }
            v.Dispose();
            string aliveString = string.Join(",", alive.ToArray());
            Debug.Log("[BDArmoryCompetition:" + CompetitionID.ToString() + "] STILLALIVE: " + aliveString);
            // If we find a vessel named "Pinata" that's a special case object
            // this should probably be configurable.
            if (!pinataAlive && alive.Contains("Pinata"))
            {
                Debug.Log("[BDACompetitionMode] Setting Pinata Flag to Alive!");
                pinataAlive = true;
                competitionStatus = "Enabling Pinata";
            } else if(pinataAlive && !alive.Contains("Pinata"))
            {
                // switch everyone onto separate teams when the Pinata Dies
                LoadedVesselSwitcher.MassTeamSwitch();
                pinataAlive = false;
                competitionStatus = "Pinata is dead - competition is now a Free for all";
                // start kill clock
                if (!killerGMenabled)
                {
                    // disabled for now, should be in a competition settings UI
                    //BDACompetitionMode.Instance.killerGMenabled = true;
                }
            }
            doaUpdate += "     DEAD: ";
            foreach (string key in Scores.Keys)
            {
                // check everyone who's no longer alive
                if (!alive.Contains(key))
                {
                    if (key != "Pinata")
                    {
                        if (!DeathOrder.ContainsKey(key))
                        {
                            // adding pilot into death order
                            DeathOrder[key] = DeathOrder.Count;
                            pilotActions[key] = " is Dead";
                            var whoKilledMe = "";

                            if (Scores.ContainsKey(key))
                            {
                                if (Planetarium.GetUniversalTime() - Scores[key].lastHitTime < 10)
                                {
                                    // if last hit was recent that person gets the kill
                                    whoKilledMe = Scores[key].lastPersonWhoHitMe;
                                    Scores[key].whoGotCleanKill = Scores[key].lastPersonWhoHitMe;
                                    // twice - so 2 points
                                    Debug.Log("[BDArmoryCompetition: " + CompetitionID.ToString() + "]: " + key + ":CLEANKILL:" + whoKilledMe);
                                    Debug.Log("[BDArmoryCompetition: " + CompetitionID.ToString() + "]: " + key + ":KILLED:" + whoKilledMe);
                                    whoKilledMe += " (BOOM! HEADSHOT!)";
                                }
                                else if (Scores[key].everyoneWhoHitMe.Count > 0)
                                {
                                    whoKilledMe = String.Join(", ", Scores[key].everyoneWhoHitMe);
                                    foreach (var killer in Scores[key].everyoneWhoHitMe)
                                    {
                                        Debug.Log("[BDArmoryCompetition: " + CompetitionID.ToString() + "]: " + key + ":KILLED:" + killer);
                                    }
                                }
                            }
                            if (whoKilledMe != "")
                            {
                                competitionStatus = key + " was killed by " + whoKilledMe;        
                            }
                            else
                            {
                                competitionStatus = key + " was killed";
                                Debug.Log("[BDArmoryCompetition: " + CompetitionID.ToString() + "]: " + key + ":KILLED:NOBODY");
                            }
                        }
                        doaUpdate += " :" + key + ": ";
                    }
                }
            }
            deadOrAlive = doaUpdate;

            if ((Planetarium.GetUniversalTime() > gracePeriod) && numberOfCompetitiveVessels < 2)
            {
                competitionStatus = "All Pilots are Dead";
                foreach (string key in alive)
                {
                    competitionStatus = key + " wins the round!";
                }
                if (dumpedResults > 0)
                {
                    dumpedResults--;
                }
                else if(dumpedResults == 0)
                {
                    Debug.Log("[BDArmoryCompetition: " + CompetitionID.ToString() + "]:No viable competitors, Automatically dumping scores");
                    LogResults();
                    dumpedResults--;
                    //competitionStartTime = -1;
                }
            } else
            {
                dumpedResults = 5;
            }

            // use the exploder system to remove vessels that should be nuked
            foreach(var vessel in vesselsToKill) {
                var vesselName = vessel.GetName();
                var killerName = "";
                if(Scores.ContainsKey(vesselName))
                {
                    killerName = Scores[vesselName].lastPersonWhoHitMe;
                    if (killerName == "")
                    {
                        Scores[vesselName].lastPersonWhoHitMe = "Landed Too Long"; // only do this if it's not already damaged
                        killerName = "Landed Too Long"; 
                    }
                }
                Debug.Log("[BDArmoryCompetition: " + CompetitionID.ToString() + "]: " + vesselName + ":REMOVED:" + killerName);
                Misc.Misc.ForceDeadVessel(vessel);
                KillTimer.Remove(vesselName);
            }

            FindVictim();

            if (BDArmorySettings.SPAWN_VESSELS)
            {
                foreach (string key in Scores.Keys)
                {
                    // check everyone who's no longer alive
                    if (!alive.Contains(key))
                    {
                        // Don't try to respawn the craft if we're already trying to respawn it
                        if (!Scores[key].isSpawning)
                        {

                            if (craftUrls.TryGetValue(key, out var craftUrl))
                            {
                                Scores[key].isSpawning = true; // We're trying to respawn the craft now
                                StartCoroutine(SpawnCraftRoutine(craftUrl, key));
                            }
                        }
                        else
                        {
                            if (craftUrls.TryGetValue(key, out var craftUrl))
                            {
                                StopCoroutine(SpawnCraftRoutine(craftUrl, key)); // I don't know if this does anything, but seemed like it was worth a shot.                               
                            }
                        }
                    }
                }
                
            }

            Debug.Log("[BDArmoryCompetition] Done With Update");
        }

        public void LogResults()
        {
            // get everyone who's still alive
            HashSet<string> alive = new HashSet<string>();
            Debug.Log("[BDArmoryCompetition:" + CompetitionID.ToString() + "]: Dumping Results ");
            
            foreach (var v in FlightGlobals.Vessels.Where(v => v != null && v.loaded && !v.packed))
            {
                foreach (var m in v.FindPartModulesImplementing<MissileFire>().Where(m => m != null && m.vessel != null))
                {
                    alive.Add(m.vessel.GetName());
                    Debug.Log("[BDArmoryCompetition:" + CompetitionID.ToString() + "]: ALIVE:" + m.vessel.GetName());
                }
            }

            // find out who's still alive
            foreach (string key in Scores.Keys)
            {
                if(!alive.Contains(key))
                    Debug.Log("[BDArmoryCompetition:" + CompetitionID.ToString() + "]: DEAD:" + DeathOrder[key] + ":" + key);
            }

            foreach(string key in whoShotWho.Keys)
            {
                Debug.Log("[BDArmoryCompetition:" + CompetitionID.ToString() + "]: WHOSHOTWHO:" + whoShotWho[key] + ":" + key);
            }

            if (!Directory.Exists(Environment.CurrentDirectory + "/BDArmoryCompetition"))
            {
                Directory.CreateDirectory(Environment.CurrentDirectory + "/BDArmoryCompetition");
            }

            var totalDeaths = DeathOrder.Count;
            using (var stream = File.OpenWrite(Environment.CurrentDirectory + $"/BDArmoryCompetition/{CompetitionID}_results.csv"))
            using (var writer = new StreamWriter(stream))
            {
                writer.WriteLine("User,Survival,Deaths,Kills,CleanKills");
                foreach (var pair in Scores)
                {
                    var survival = BDArmorySettings.SPAWN_VESSELS ? 0 : DeathOrder.TryGetValue(pair.Key, out var v) ? v + 1 : totalDeaths + 1;
                    writer.WriteLine($"{pair.Key.Replace(',','_')},{survival},{pair.Value.deaths},{pair.Value.kills},{pair.Value.cleanKills}");
                }
                
                writer.Flush();
                stream.Flush();
            }
        }

        public class FinalScore
        {
            public int CleanKills { get; set; }
            public int Kills { get; set; }
            public int Survival { get; set; }
        }
    }
}
