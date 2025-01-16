using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using KSP.UI.Screens;

using BDArmory.Competition;
using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;

namespace BDArmory.VesselSpawning
{
    /// <summary>
    /// Spawn teams of craft in a custom template.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CustomTemplateSpawning : VesselSpawnerBase
    {
        public static CustomTemplateSpawning Instance;
        void LogMessage(string message, bool toScreen = true, bool toLog = true) => LogMessageFrom("CustomTemplateSpawning", message, toScreen, toLog);

        [CustomSpawnTemplateField] public static List<CustomSpawnConfig> customSpawnConfigs = null;
        bool startCompetitionAfterSpawning = false;
        protected override void Awake()
        {
            base.Awake();
            if (Instance != null) Destroy(Instance);
            Instance = this;

            if (customSpawnConfigs == null) customSpawnConfigs = new List<CustomSpawnConfig>();
            LoadTemplate(null, true);
        }

        void Start()
        {
            StartCoroutine(WaitForBDASettings());
        }

        IEnumerator WaitForBDASettings()
        {
            yield return new WaitUntil(() => BDArmorySetup.Instance is not null);
            if (_crewGUICheckIndex < 0) _crewGUICheckIndex = GUIUtils.RegisterGUIRect(new Rect());
            if (_vesselGUICheckIndex < 0) _vesselGUICheckIndex = GUIUtils.RegisterGUIRect(new Rect());
        }

        void OnDestroy()
        {
            CustomSpawnTemplateField.Save();
        }

        public override IEnumerator Spawn(SpawnConfig spawnConfig)
        {
            var customSpawnConfig = spawnConfig as CustomSpawnConfig;
            if (customSpawnConfig == null) yield break;
            SpawnCustomTemplateAsCoroutine(customSpawnConfig);
        }

        public void CancelSpawning()
        {
            if (vesselsSpawning)
            {
                vesselsSpawning = false;
                LogMessage("Vessel spawning cancelled.");
            }
            if (spawnCustomTemplateCoroutine != null)
            {
                StopCoroutine(spawnCustomTemplateCoroutine);
                spawnCustomTemplateCoroutine = null;
            }
        }

        #region Custom template spawning
        /// <summary>
        /// Prespawn initialisation to handle camera and body changes and to ensure that only a single spawning coroutine is running.
        /// </summary>
        /// <param name="spawnConfig">The spawn config for the new spawning.</param>
        public override void PreSpawnInitialisation(SpawnConfig spawnConfig)
        {
            if (craftBrowser != null) craftBrowser = null; // Clean up the craft browser.
            HideOtherWindows(null); // Make sure the template/vessel/crew selection windows are hidden.

            base.PreSpawnInitialisation(spawnConfig);

            vesselsSpawning = true; // Signal that we've started the spawning vessels routine.
            vesselSpawnSuccess = false; // Set our success flag to false for now.
            spawnFailureReason = SpawnFailureReason.None; // Reset the spawn failure reason.
            if (spawnCustomTemplateCoroutine != null)
                StopCoroutine(spawnCustomTemplateCoroutine);
        }

        public void SpawnCustomTemplate(CustomSpawnConfig spawnConfig)
        {
            if (spawnConfig == null) return;
            PreSpawnInitialisation(spawnConfig);
            spawnCustomTemplateCoroutine = StartCoroutine(SpawnCustomTemplateCoroutine(spawnConfig));
            LogMessage("Triggering vessel spawning at " + spawnConfig.latitude.ToString("G6") + ", " + spawnConfig.longitude.ToString("G6") + ", with altitude " + spawnConfig.altitude + "m.", false);
        }

        /// <summary>
        /// A coroutine version of the SpawnCustomTemplate function that performs the required prespawn initialisation.
        /// </summary>
        /// <param name="spawnConfig">The spawn config to use.</param>
        public IEnumerator SpawnCustomTemplateAsCoroutine(CustomSpawnConfig spawnConfig)
        {
            PreSpawnInitialisation(spawnConfig);
            LogMessage("Triggering vessel spawning at " + spawnConfig.latitude.ToString("G6") + ", " + spawnConfig.longitude.ToString("G6") + ", with altitude " + spawnConfig.altitude + "m.", false);
            yield return SpawnCustomTemplateCoroutine(spawnConfig);
        }

        private Coroutine spawnCustomTemplateCoroutine;
        // Spawns all vessels in an outward facing ring and lowers them to the ground. An altitude of 5m should be suitable for most cases.
        private IEnumerator SpawnCustomTemplateCoroutine(CustomSpawnConfig spawnConfig)
        {
            #region Initialisation and sanity checks
            // Tally up the craft to spawn and figure out teams.
            spawnConfig.craftFiles = spawnConfig.customVesselSpawnConfigs.SelectMany(team => team).Select(config => config.craftURL).Where(craftURL => !string.IsNullOrEmpty(craftURL)).ToList();
            var spawnAirborne = spawnConfig.altitude > 10f;
            var spawnBody = FlightGlobals.Bodies[spawnConfig.worldIndex];
            var spawnInOrbit = spawnConfig.altitude >= spawnBody.MinSafeAltitude(); // Min safe orbital altitude
            var withInitialVelocity = spawnAirborne && BDArmorySettings.VESSEL_SPAWN_INITIAL_VELOCITY;
            var spawnPitch = withInitialVelocity ? 0f : -80f;
            LogMessage($"Spawning {spawnConfig.craftFiles.Count} vessels at an altitude of {(spawnConfig.altitude < 1000 ? $"{spawnConfig.altitude:G5}m" : $"{spawnConfig.altitude / 1000:G5}km")} ({(spawnInOrbit ? "in orbit" : spawnAirborne ? "airborne" : "landed")}).");
            #endregion

            yield return AcquireSpawnPoint(spawnConfig, 100f, false);
            if (spawnFailureReason != SpawnFailureReason.None)
            {
                vesselsSpawning = false;
                SpawnUtils.RevertSpawnLocationCamera(true, true);
                yield break;
            }

            // Configure the vessels' individual spawn configs.
            var vesselSpawnConfigs = new List<VesselSpawnConfig>();
            foreach (var customVesselSpawnConfig in spawnConfig.customVesselSpawnConfigs.SelectMany(config => config))
            {
                if (string.IsNullOrEmpty(customVesselSpawnConfig.craftURL)) continue;

                var vesselSpawnPoint = FlightGlobals.currentMainBody.GetWorldSurfacePosition(customVesselSpawnConfig.latitude, customVesselSpawnConfig.longitude, spawnConfig.altitude);
                var radialUnitVector = (vesselSpawnPoint - FlightGlobals.currentMainBody.transform.position).normalized;
                var refDirection = Math.Abs(Vector3.Dot(Vector3.up, radialUnitVector)) < 0.71f ? Vector3.up : Vector3.forward; // Avoid that the reference direction is colinear with the local surface normal.
                var crew = new List<ProtoCrewMember>();
                if (!string.IsNullOrEmpty(customVesselSpawnConfig.kerbalName)) crew.Add(HighLogic.CurrentGame.CrewRoster[customVesselSpawnConfig.kerbalName]);
                vesselSpawnConfigs.Add(new VesselSpawnConfig(
                    customVesselSpawnConfig.craftURL,
                    vesselSpawnPoint,
                    (Quaternion.AngleAxis(customVesselSpawnConfig.heading, radialUnitVector) * refDirection).ProjectOnPlanePreNormalized(radialUnitVector).normalized,
                    (float)spawnConfig.altitude,
                    spawnPitch,
                    spawnAirborne,
                    spawnInOrbit,
                    customVesselSpawnConfig.teamIndex,
                    false,
                    crew
                ));
            }
            VesselSpawner.ReservedCrew = vesselSpawnConfigs.Where(config => config.crew.Count > 0).SelectMany(config => config.crew).Select(crew => crew.name).ToHashSet();
            foreach (var crew in vesselSpawnConfigs.Where(config => config.crew.Count > 0).SelectMany(config => config.crew)) crew.rosterStatus = ProtoCrewMember.RosterStatus.Available; // Set all the requested crew as available since we've just killed off everything.

            yield return SpawnVessels(vesselSpawnConfigs);
            VesselSpawner.ReservedCrew.Clear();
            if (spawnFailureReason != SpawnFailureReason.None)
            {
                vesselsSpawning = false;
                SpawnUtils.RevertSpawnLocationCamera(true, true);
                yield break;
            }

            #region Post-spawning
            // Revert back to the KSP's proper camera.
            SpawnUtils.RevertSpawnLocationCamera(true);

            // Spawning has succeeded, vessels have been renamed where necessary and vessels are ready. Time to assign teams and any other stuff.
            yield return PostSpawnMainSequence(spawnConfig, spawnAirborne, withInitialVelocity, !startCompetitionAfterSpawning);
            if (spawnFailureReason != SpawnFailureReason.None)
            {
                LogMessage("Vessel spawning FAILED! " + spawnFailureReason);
                vesselsSpawning = false;
                SpawnUtils.RevertSpawnLocationCamera(true, true);
                yield break;
            }

            // Revert the camera and focus on one of the vessels.
            if ((FlightGlobals.ActiveVessel == null || FlightGlobals.ActiveVessel.state == Vessel.State.DEAD) && spawnedVessels.Count > 0)
            {
                yield return LoadedVesselSwitcher.Instance.SwitchToVesselWhenPossible(spawnedVessels.Take(UnityEngine.Random.Range(1, spawnedVessels.Count)).Last().Value); // Update the camera.
            }
            FlightCamera.fetch.SetDistance(50);

            // Assign the vessels to teams.
            LogMessage("Assigning vessels to teams.", false);
            var teamVesselNames = new List<List<string>>();
            for (int i = 0; i < spawnedVesselsTeamIndex.Max(kvp => kvp.Value) + 1; ++i)
                teamVesselNames.Add(spawnedVesselsTeamIndex.Where(kvp => kvp.Value == i).Select(kvp => kvp.Key).ToList());
            bool useOriginalTeamNames = spawnConfig.assignTeams && (spawnConfig.numberOfTeams == 1 || spawnConfig.numberOfTeams == -1); // Flag to use per file / per team organisation.
            if (useOriginalTeamNames)
            {
                if (spawnConfig.numberOfTeams == 1) // Folders
                {
                    foreach (var vesselName in spawnedVesselURLs.Keys)
                        SpawnUtils.originalTeams[vesselName] = Path.GetFileName(Path.GetDirectoryName(spawnedVesselURLs[vesselName]));
                }
                else // Files as folders
                {
                    foreach (var vesselName in spawnedVesselURLs.Keys)
                        SpawnUtils.originalTeams[vesselName] = Path.GetFileNameWithoutExtension(spawnedVesselURLs[vesselName]);
                }
            }
            if (BDArmorySettings.VESSEL_SPAWN_SMART_REASSIGN_TEAMS && spawnConfig.assignTeams && spawnConfig.numberOfTeams == 11)
            {
                HashSet<string> teamNames = [];
                SpawnUtils.originalTeams.Clear();
                foreach (var team in teamVesselNames)
                {
                    foreach (var vesselName in team)
                    {
                        if (!spawnedVessels.ContainsKey(vesselName)) continue;
                        var wm = VesselModuleRegistry.GetMissileFire(spawnedVessels[vesselName]);
                        if (wm == null) continue;
                        if (wm.Team.Name.Length < 2) continue; // If it's a one-letter name, ignore it.
                        if (teamNames.Contains(wm.Team.Name)) continue; // The team name already exists, ignore it.
                        teamNames.Add(wm.Team.Name); // Found a valid non-default team name that isn't already taken.
                        foreach (var otherVesselName in team) // Set all the craft in this team to this team name.
                            SpawnUtils.originalTeams[otherVesselName] = wm.Team.Name;
                        break; // Go to the next team.
                    }
                }
                // Do a MassTeamSwitch without using original teams to give everyone A, B, ...
                LoadedVesselSwitcher.Instance.MassTeamSwitch(true, false, null, teamVesselNames);
                useOriginalTeamNames = true; // Next call of MassTeamSwitch with originalTeams=true to override the defaults with the custom team names.
            }
            LoadedVesselSwitcher.Instance.MassTeamSwitch(true, useOriginalTeamNames, null, teamVesselNames); // Assign A, B, ...
            #endregion

            LogMessage("Vessel spawning SUCCEEDED!", true, BDArmorySettings.DEBUG_SPAWNING);
            vesselSpawnSuccess = true;
            vesselsSpawning = false;

            if (startCompetitionAfterSpawning)
            {
                // Run the competition.
                BDACompetitionMode.Instance.StartCompetitionMode(BDArmorySettings.COMPETITION_DISTANCE, BDArmorySettings.COMPETITION_START_DESPITE_FAILURES);
            }
        }
        #endregion

        #region Templates
        public static CustomSpawnConfig customSpawnConfig = null;
        /// <summary>
        /// Reload all the templates from disk and return the specified one or an empty one if no name (or an invalid one) was specified.
        /// </summary>
        /// <param name="templateName">The name of the template to load.</param>
        public static void LoadTemplate(string templateName = null, bool fromDisk = false)
        {
            if (fromDisk) // Reload the templates from disk.
                CustomSpawnTemplateField.Load();
            else if (templateName != null && templateName == customSpawnConfig.name)
            {
                if (Instance) Instance.RefreshSelectedCrew();
                return; // It's the same config, which hasn't been adjusted, so return it without clearing the fields.
            }

            // Find a matching config.
            if (templateName != null) customSpawnConfig = customSpawnConfigs.Find(config => config.name == templateName);
            // Otherwise, return an empty one.
            if (customSpawnConfig == null)
            {
                customSpawnConfig = new CustomSpawnConfig(
                    "",
                    new SpawnConfig(
                        worldIndex: BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
                        latitude: BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                        longitude: BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                        altitude: BDArmorySettings.VESSEL_SPAWN_ALTITUDE
                    ),
                    []
                );
            }
            if (Instance) Instance.RefreshSelectedCrew();
        }

        /// <summary>
        /// Update the current template with new spawn points from the LoadedVesselSwitcher.
        /// </summary>
        public void SaveTemplate()
        {
            if (LoadedVesselSwitcher.Instance.WeaponManagers.Count == 0) return; // Safe-guard, don't save over an existing template when the slots are empty. 
            var geoCoords = FlightGlobals.currentMainBody.GetLatitudeAndLongitude(LoadedVesselSwitcher.Instance.WeaponManagers.SelectMany(tm => tm.Value).Select(wm => wm.vessel.transform.position).Aggregate(Vector3.zero, (l, r) => l + r) / LoadedVesselSwitcher.Instance.WeaponManagers.SelectMany(tm => tm.Value).Count()); // Set the central spawn location at the centroid of the craft.
            customSpawnConfig.worldIndex = BDArmorySettings.VESSEL_SPAWN_WORLDINDEX;
            customSpawnConfig.latitude = geoCoords.x;
            customSpawnConfig.longitude = geoCoords.y;
            customSpawnConfig.altitude = BDArmorySettings.VESSEL_SPAWN_ALTITUDE;
            customSpawnConfig.customVesselSpawnConfigs.Clear();
            int teamCount = 0;
            foreach (var team in LoadedVesselSwitcher.Instance.WeaponManagers)
            {
                var teamConfigs = new List<CustomVesselSpawnConfig>();
                foreach (var member in team.Value)
                {
                    geoCoords = FlightGlobals.currentMainBody.GetLatitudeAndLongitude(member.vessel.transform.position);
                    CustomVesselSpawnConfig vesselSpawnConfig = new CustomVesselSpawnConfig(
                        geoCoords.x,
                        geoCoords.y,
                        (Vector3.SignedAngle(member.vessel.north, member.vessel.ReferenceTransform.up, member.vessel.up) + 360f) % 360f,
                        teamCount,
                        null
                    );
                    teamConfigs.Add(vesselSpawnConfig);
                }
                customSpawnConfig.customVesselSpawnConfigs.Add(teamConfigs);
                ++teamCount;
            }
            if (!customSpawnConfigs.Contains(customSpawnConfig)) customSpawnConfigs.Add(customSpawnConfig); // Add the template if it isn't already there.
            CustomSpawnTemplateField.Save();
            PopulateEntriesFromLVS(); // Populate the slots to show the layout.
        }

        /// <summary>
        /// Create a template from the current vessels in the Vessel Switcher.
        /// Vessel positions, rotations and teams are saved.
        /// </summary>
        /// <param name="templateName"></param>
        public CustomSpawnConfig NewTemplate(string templateName = "")
        {
            // Remove any invalid or unnamed entries.
            customSpawnConfigs = customSpawnConfigs.Where(config => !string.IsNullOrEmpty(config.name) && config.customVesselSpawnConfigs.Count > 0).ToList();

            // Then make a new one.
            var geoCoords = FlightGlobals.currentMainBody.GetLatitudeAndLongitude(LoadedVesselSwitcher.Instance.WeaponManagers.SelectMany(tm => tm.Value).Select(wm => wm.vessel.transform.position).Aggregate(Vector3.zero, (l, r) => l + r) / LoadedVesselSwitcher.Instance.WeaponManagers.SelectMany(tm => tm.Value).Count()); // Set the central spawn location at the centroid of the craft.
            customSpawnConfig = new CustomSpawnConfig(
                templateName,
                new SpawnConfig(
                    worldIndex: BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
                    latitude: geoCoords.x,
                    longitude: geoCoords.y,
                    altitude: BDArmorySettings.VESSEL_SPAWN_ALTITUDE
                ),
                new List<List<CustomVesselSpawnConfig>>()
            );
            int teamCount = 0;
            foreach (var team in LoadedVesselSwitcher.Instance.WeaponManagers)
            {
                var teamConfigs = new List<CustomVesselSpawnConfig>();
                foreach (var member in team.Value)
                {
                    geoCoords = FlightGlobals.currentMainBody.GetLatitudeAndLongitude(member.vessel.transform.position);
                    CustomVesselSpawnConfig vesselSpawnConfig = new CustomVesselSpawnConfig(
                        geoCoords.x,
                        geoCoords.y,
                        (Vector3.SignedAngle(member.vessel.north, member.vessel.ReferenceTransform.up, member.vessel.up) + 360f) % 360f,
                        teamCount,
                        null
                    );
                    teamConfigs.Add(vesselSpawnConfig);
                }
                customSpawnConfig.customVesselSpawnConfigs.Add(teamConfigs);
                ++teamCount;
            }
            customSpawnConfigs.Add(customSpawnConfig);
            CustomSpawnTemplateField.Save();
            PopulateEntriesFromLVS(); // Populate the slots to show the layout.
            return customSpawnConfig;
        }

        /// <summary>
        /// Populate the spawn slots from the current vessels and kerbals in the loaded vessel switcher.
        /// </summary>
        void PopulateEntriesFromLVS()
        {
            Func<Part, int> distanceToRoot = null;
            distanceToRoot = (p) => { return p.parent != null ? distanceToRoot(p.parent) : 0; };
            if (CustomCraftBrowserDialog.shipNames.Count == 0)
            {
                craftBrowser = new CustomCraftBrowserDialog();
                craftBrowser.UpdateList();
            }
            SelectedCrewMembers.Clear();

            using (var team = LoadedVesselSwitcher.Instance.WeaponManagers.GetEnumerator())
            using (var teamSlot = customSpawnConfig.customVesselSpawnConfigs.GetEnumerator())
                while (team.MoveNext() && teamSlot.MoveNext())
                {
                    using (var member = team.Current.Value.GetEnumerator())
                    using (var memberSlot = teamSlot.Current.GetEnumerator())
                        while (member.MoveNext() && memberSlot.MoveNext())
                        {
                            // Find the craft with the matching name.
                            memberSlot.Current.craftURL = CustomCraftBrowserDialog.shipNames.FirstOrDefault(c => c.Value == member.Current.vessel.vesselName).Key;
                            if (string.IsNullOrEmpty(memberSlot.Current.craftURL))
                            {
                                // Try stripping _1, etc. from the end of the vesselName
                                var lastIndex = member.Current.vessel.vesselName.LastIndexOf("_");
                                if (lastIndex > 0)
                                {
                                    var possibleName = member.Current.vessel.vesselName.Substring(0, lastIndex);
                                    memberSlot.Current.craftURL = CustomCraftBrowserDialog.shipNames.FirstOrDefault(c => c.Value == possibleName).Key;
                                }
                            }
                            // Find the primary crew onboard.
                            var crewParts = member.Current.vessel.parts.FindAll(p => p.protoModuleCrew.Count > 0).OrderBy(p => distanceToRoot(p)).ToList();
                            if (crewParts.Count > 0)
                            {
                                memberSlot.Current.kerbalName = crewParts.First().protoModuleCrew.First().name;
                                SelectedCrewMembers.Add(memberSlot.Current.kerbalName);
                            }
                            else
                            {
                                memberSlot.Current.kerbalName = null;
                            }
                        }
                }
        }

        public void PopulateEntriesFromTournament(CustomSpawnConfig customSpawnConfig)
        {
            foreach (var team in customSpawnConfig.customVesselSpawnConfigs)
                foreach (var member in team)
                {
                    if (string.IsNullOrEmpty(member.craftURL)) continue;
                    if (CustomCraftBrowserDialog.shipNames.ContainsKey(member.craftURL)) continue; // Already exists.
                    var craftMeta = $"{Path.GetFileNameWithoutExtension(member.craftURL)}.loadmeta";
                    var craftProfile = new CraftProfileInfo();
                    if (File.Exists(craftMeta) && File.GetLastWriteTime(craftMeta) > File.GetLastWriteTime(member.craftURL)) // If the loadMeta file exists and has a timestamp that's later than the craft file (because WTF KSP‽), use it, otherwise generate one.
                    {
                        craftProfile.LoadFromMetaFile(craftMeta);
                    }
                    else
                    {
                        var craftNode = ConfigNode.Load(member.craftURL);
                        craftProfile.LoadDetailsFromCraftFile(craftNode, member.craftURL);
                        if (File.Exists(craftMeta)) // If the file existed, but was out of date, update it. Otherwise, don't to avoid polluting folders with .loadmeta files.
                            craftProfile.SaveToMetaFile(craftMeta);
                    }
                    CustomCraftBrowserDialog.shipNames.Add(member.craftURL, craftProfile.shipName);
                }
            CustomTemplateSpawning.customSpawnConfig.customVesselSpawnConfigs = customSpawnConfig.customVesselSpawnConfigs;
        }

        /// <summary>
        /// Configure the spawn template with locally settable config values and perform a sanity check for being able to run a competition.
        /// </summary>
        /// <returns>true if there are sufficient non-empty teams for a competition, false otherwise</returns>
        public bool ConfigureTemplate(bool startCompetitionAfterSpawning)
        {
            // Sanity check
            if (startCompetitionAfterSpawning && customSpawnConfig.customVesselSpawnConfigs.Count(team => team.Count(cfg => !string.IsNullOrEmpty(cfg.craftURL)) > 0) < 2) // At least two non-empty teams.
            {
                BDACompetitionMode.Instance.competitionStatus.Add("Not enough vessels selected for a competition.");
                return false;
            }

            // Set the locally settable config values.
            customSpawnConfig.altitude = Mathf.Max(BDArmorySettings.VESSEL_SPAWN_ALTITUDE, 2f);
            customSpawnConfig.killEverythingFirst = true;
            customSpawnConfig.numberOfTeams = BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS;

            this.startCompetitionAfterSpawning = startCompetitionAfterSpawning;
            return true;
        }

        #endregion

        #region UI
        void OnGUI()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (!BDArmorySetup.GAME_UI_ENABLED) return;
            var guiMatrix = GUI.matrix;
            if (showTemplateSelection)
            {
                if (Event.current.type == EventType.MouseDown && !templateSelectionWindowRect.Contains(Event.current.mousePosition))
                    HideTemplateSelection();
                else
                {
                    if (BDArmorySettings._UI_SCALE != 1) { GUI.matrix = guiMatrix; GUIUtility.ScaleAroundPivot(BDArmorySettings._UI_SCALE * Vector2.one, templateSelectionWindowRect.position); }
                    templateSelectionWindowRect = GUILayout.Window(GUIUtility.GetControlID(FocusType.Passive), templateSelectionWindowRect, TemplateSelectionWindow, StringUtils.Localize("#LOC_BDArmory_Settings_CustomSpawnTemplate_TemplateSelection"), BDArmorySetup.BDGuiSkin.window);
                }
            }
            if (showCrewSelection)
            {
                if (Event.current.type == EventType.MouseDown && !crewSelectionWindowRect.Contains(Event.current.mousePosition))
                    HideCrewSelection();
                else
                {
                    if (BDArmorySettings._UI_SCALE != 1) { GUI.matrix = guiMatrix; GUIUtility.ScaleAroundPivot(BDArmorySettings._UI_SCALE * Vector2.one, crewSelectionWindowRect.position); }
                    crewSelectionWindowRect = GUILayout.Window(GUIUtility.GetControlID(FocusType.Passive), crewSelectionWindowRect, CrewSelectionWindow, StringUtils.Localize("#LOC_BDArmory_VesselMover_CrewSelection"), BDArmorySetup.BDGuiSkin.window);
                }
            }
            if (showVesselSelection)
            {
                if (Event.current.type == EventType.MouseDown && !vesselSelectionWindowRect.Contains(Event.current.mousePosition))
                    HideVesselSelection();
                else
                {
                    if (BDArmorySettings._UI_SCALE != 1) { GUI.matrix = guiMatrix; GUIUtility.ScaleAroundPivot(BDArmorySettings._UI_SCALE * Vector2.one, vesselSelectionWindowRect.position); }
                    vesselSelectionWindowRect = GUILayout.Window(GUIUtility.GetControlID(FocusType.Passive), vesselSelectionWindowRect, VesselSelectionWindow, StringUtils.Localize("#LOC_BDArmory_VesselMover_VesselSelection"), BDArmorySetup.BDGuiSkin.window);
                }
            }
        }

        #region Template Selection
        internal static int _templateGUICheckIndex = -1;
        bool showTemplateSelection = false;
        bool bringTemplateSelectionToFront = false;
        Rect templateSelectionWindowRect = new Rect(0, 0, 300, 200);
        Vector2 templateSelectionScrollPos = default;
        /// <summary>
        /// Show the template section window.
        /// </summary>
        /// <param name="position">The mouse click position.</param>
        public void ShowTemplateSelection(Vector2 position)
        {
            HideOtherWindows("template");
            templateSelectionWindowRect.position = position + BDArmorySettings._UI_SCALE * new Vector2(-templateSelectionWindowRect.width / 2, 20); // Centred and slightly below.
            showTemplateSelection = true;
            bringTemplateSelectionToFront = true;
            GUIUtils.SetGUIRectVisible(_templateGUICheckIndex, true);
        }

        /// <summary>
        /// Hide the template selection window.
        /// </summary>
        void HideTemplateSelection()
        {
            showTemplateSelection = false;
            GUIUtils.SetGUIRectVisible(_templateGUICheckIndex, false);
        }

        CustomSpawnConfig templateToRemove = null;
        public void TemplateSelectionWindow(int windowID)
        {
            GUI.DragWindow(new Rect(0, 0, templateSelectionWindowRect.width, 20));
            GUILayout.BeginVertical();
            templateSelectionScrollPos = GUILayout.BeginScrollView(templateSelectionScrollPos, GUI.skin.box, GUILayout.Width(templateSelectionWindowRect.width - 15), GUILayout.MaxHeight(templateSelectionWindowRect.height - 10));
            using (var templateName = customSpawnConfigs.GetEnumerator())
                while (templateName.MoveNext())
                {
                    if (string.IsNullOrEmpty(templateName.Current.name) || templateName.Current.customVesselSpawnConfigs.Count == 0) continue; // Skip any empty or unnamed templates.
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(templateName.Current.name, BDArmorySetup.BDGuiSkin.button))
                    {
                        LoadTemplate(templateName.Current.name, Event.current.button == 1); // Right click to reload templates from disk.
                        HideTemplateSelection();
                    }
                    if (GUILayout.Button(" X", BDArmorySetup.CloseButtonStyle, GUILayout.Width(24)))
                    {
                        templateToRemove = templateName.Current;
                    }
                    GUILayout.EndHorizontal();
                }
            if (templateToRemove != null)
            {
                customSpawnConfigs.Remove(templateToRemove);
                if (templateToRemove == customSpawnConfig) customSpawnConfig = NewTemplate();
                templateToRemove = null;
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUIUtils.RepositionWindow(ref templateSelectionWindowRect);
            GUIUtils.UpdateGUIRect(templateSelectionWindowRect, _templateGUICheckIndex);
            GUIUtils.UseMouseEventInRect(templateSelectionWindowRect);
            if (bringTemplateSelectionToFront)
            {
                GUI.BringWindowToFront(windowID);
                bringTemplateSelectionToFront = false;
            }
        }
        #endregion

        #region Vessel Selection
        CustomVesselSpawnConfig currentVesselSpawnConfig;
        List<CustomVesselSpawnConfig> currentTeamSpawnConfigs;
        internal static int _vesselGUICheckIndex = -1;
        bool showVesselSelection = false;
        bool bringVesselSelectionToFront = false;
        Rect vesselSelectionWindowRect = new(0, 0, 600, 800);
        Vector2 vesselSelectionScrollPos = default;
        string selectionFilter = "";
        bool focusFilterField = false;
        bool folderSelectionMode = false; // Show SPH/VAB and folders instead of craft files.

        CustomCraftBrowserDialog craftBrowser;
        GUIStyle ButtonStyle = new(CustomCraftBrowserDialog.ButtonStyle);
        GUIStyle InfoStyle = new(CustomCraftBrowserDialog.InfoStyle);
        public static string ShipName(string craft) => (!string.IsNullOrEmpty(craft) && CustomCraftBrowserDialog.shipNames.TryGetValue(craft, out string shipName)) ? shipName : "";

        /// <summary>
        /// Show the vessel selection window.
        /// </summary>
        /// <param name="position">Position of the mouse click.</param>
        /// <param name="craftURL">The URL of the craft.</param>
        public void ShowVesselSelection(Vector2 position, CustomVesselSpawnConfig vesselSpawnConfig, List<CustomVesselSpawnConfig> teamSpawnConfigs)
        {
            HideOtherWindows("vessel");
            if (showVesselSelection && vesselSpawnConfig == currentVesselSpawnConfig)
            {
                HideVesselSelection();
                return;
            }
            currentVesselSpawnConfig = vesselSpawnConfig;
            currentTeamSpawnConfigs = teamSpawnConfigs;
            if (craftBrowser == null)
            {
                craftBrowser = new CustomCraftBrowserDialog();
                craftBrowser.UpdateList();
                ButtonStyle.fontSize = 18;
                InfoStyle.fontSize = 12;
            }
            vesselSelectionWindowRect.position = position + BDArmorySettings._UI_SCALE * new Vector2(-vesselSelectionWindowRect.width - 120, -vesselSelectionWindowRect.height / 2); // Centred and slightly offset to allow clicking the same spot.
            showVesselSelection = true;
            focusFilterField = true; // Focus the filter text field.
            bringVesselSelectionToFront = true;
            craftBrowser.CheckCurrent();
            GUIUtils.SetGUIRectVisible(_vesselGUICheckIndex, true);
        }

        /// <summary>
        /// Hide the vessel selection window.
        /// </summary>
        public void HideVesselSelection(CustomVesselSpawnConfig vesselSpawnConfig = null)
        {
            if (vesselSpawnConfig != null)
            {
                vesselSpawnConfig.craftURL = null;
            }
            showVesselSelection = false;
            GUIUtils.SetGUIRectVisible(_vesselGUICheckIndex, false);
        }

        public void VesselSelectionWindow(int windowID)
        {
            GUI.DragWindow(new Rect(0, 0, vesselSelectionWindowRect.width, 20));
            GUILayout.BeginVertical();
            selectionFilter = GUIUtils.TextField(selectionFilter, " Filter", "CSTFilterField");
            if (focusFilterField)
            {
                GUI.FocusControl("CSTFilterField");
                focusFilterField = false;
            }
            vesselSelectionScrollPos = GUILayout.BeginScrollView(vesselSelectionScrollPos, GUI.skin.box, GUILayout.MaxWidth(vesselSelectionWindowRect.width - 15), GUILayout.MaxHeight(vesselSelectionWindowRect.height - 60));
            if (folderSelectionMode)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("SPH", CustomCraftBrowserDialog.ButtonStyle, GUILayout.Height(80))) craftBrowser.ChangeFolder(EditorFacility.SPH);
                if (GUILayout.Button("VAB", CustomCraftBrowserDialog.ButtonStyle, GUILayout.Height(80))) craftBrowser.ChangeFolder(EditorFacility.VAB);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label(craftBrowser.DisplayFolder, CustomCraftBrowserDialog.LabelStyle, GUILayout.Height(50), GUILayout.ExpandWidth(true));
                if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_Generic_Select"), CustomCraftBrowserDialog.ButtonStyle, GUILayout.Height(50), GUILayout.MaxWidth(vesselSelectionWindowRect.width / 3))) folderSelectionMode = false;
                GUILayout.EndHorizontal();
                using (var folder = craftBrowser.subfolders.GetEnumerator())
                    while (folder.MoveNext())
                    {
                        if (GUILayout.Button($"{folder.Current}", CustomCraftBrowserDialog.ButtonStyle, GUILayout.MaxHeight(60)))
                        {
                            craftBrowser.ChangeFolder(craftBrowser.Facility, folder.Current);
                            break; // The enumerator can't continue since subfolders has changed.
                        }
                    }
            }
            else
            {
                using (var vessels = craftBrowser.craftList.GetEnumerator())
                    while (vessels.MoveNext())
                    {
                        var vesselURL = vessels.Current.Key;
                        var vesselInfo = vessels.Current.Value;
                        if (vesselURL == null || vesselInfo == null) continue;
                        if (!string.IsNullOrEmpty(selectionFilter)) // Filter selection, case insensitive.
                        {
                            if (!vesselInfo.shipName.ToLower().Contains(selectionFilter.ToLower())) continue;
                        }
                        GUILayout.BeginHorizontal(); // Vessel buttons
                        if (GUILayout.Button($"{vesselInfo.shipName}", ButtonStyle, GUILayout.MaxHeight(48), GUILayout.Width(vesselSelectionWindowRect.width - 240)))
                        {
                            currentVesselSpawnConfig.craftURL = vesselURL;
                            foreach (var vesselSpawnConfig in currentTeamSpawnConfigs) // Set the other empty slots for the team to the same vessel.
                            {
                                if (BDArmorySettings.CUSTOM_SPAWN_TEMPLATE_REPLACE_TEAM || string.IsNullOrEmpty(vesselSpawnConfig.craftURL))
                                {
                                    vesselSpawnConfig.craftURL = vesselURL;
                                }
                            }
                            HideVesselSelection();
                        }
                        GUILayout.Label(VesselMover.Instance.VesselInfoEntry(vesselURL, vesselInfo, false), InfoStyle, GUILayout.Width(142));
                        GUILayout.Label(craftBrowser.craftThumbnails.GetValueOrDefault(vesselURL), InfoStyle, GUILayout.Height(48), GUILayout.Width(48));
                        GUILayout.EndHorizontal();
                    }
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal(); // A line for various options
            BDArmorySettings.CUSTOM_SPAWN_TEMPLATE_REPLACE_TEAM = GUILayout.Toggle(BDArmorySettings.CUSTOM_SPAWN_TEMPLATE_REPLACE_TEAM, StringUtils.Localize("#LOC_BDArmory_Settings_CustomSpawnTemplate_ReplaceTeam"));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_CraftBrowser_Clear"), BDArmorySetup.BDGuiSkin.button))
            {
                currentVesselSpawnConfig.craftURL = null;
                HideVesselSelection();
            }
            if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_CraftBrowser_ClearAll"), BDArmorySetup.BDGuiSkin.button))
            {
                foreach (var team in customSpawnConfig.customVesselSpawnConfigs)
                    foreach (var member in team)
                        member.craftURL = null;
            }
            if (GUILayout.Button(folderSelectionMode ? StringUtils.Localize("#LOC_BDArmory_CraftBrowser_Craft") : StringUtils.Localize("#LOC_BDArmory_CraftBrowser_Folder"), folderSelectionMode ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle, GUILayout.Width(vesselSelectionWindowRect.width / 6)))
            {
                folderSelectionMode = !folderSelectionMode;
            }
            if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_CraftBrowser_Refresh"), BDArmorySetup.BDGuiSkin.button, GUILayout.Width(vesselSelectionWindowRect.width / 6)))
            {
                craftBrowser.UpdateList();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUIUtils.RepositionWindow(ref vesselSelectionWindowRect);
            GUIUtils.UpdateGUIRect(vesselSelectionWindowRect, _vesselGUICheckIndex);
            GUIUtils.UseMouseEventInRect(vesselSelectionWindowRect);
            if (bringVesselSelectionToFront)
            {
                bringVesselSelectionToFront = false;
                GUI.BringWindowToFront(windowID);
            }
        }

        #endregion

        #region Crew Selection
        internal static int _crewGUICheckIndex = -1;
        bool showCrewSelection = false;
        bool bringCrewSelectionToFront = false;
        Rect crewSelectionWindowRect = new Rect(0, 0, 300, 400);
        Vector2 crewSelectionScrollPos = default;
        HashSet<string> SelectedCrewMembers = new HashSet<string>();
        HashSet<string> ObserverCrewMembers = new HashSet<string>();
        HashSet<string> ActiveCrewMembers = new HashSet<string>();
        public bool IsCrewSelectionShowing => showCrewSelection;

        /// <summary>
        /// Show the crew selection window.
        /// </summary>
        /// <param name="position">Position of the mouse click.</param>
        /// <param name="vesselSpawnConfig">The VesselSpawnConfig clicked on.</param>
        public void ShowCrewSelection(Vector2 position, CustomVesselSpawnConfig vesselSpawnConfig, bool ignoreActive = false)
        {
            HideOtherWindows("crew");
            if (showCrewSelection && vesselSpawnConfig == currentVesselSpawnConfig)
            {
                HideCrewSelection();
                return;
            }
            currentVesselSpawnConfig = vesselSpawnConfig;
            crewSelectionWindowRect.position = position + BDArmorySettings._UI_SCALE * new Vector2(50, -crewSelectionWindowRect.height / 2); // Centred and slightly offset to allow clicking the same spot.
            showCrewSelection = true;
            bringCrewSelectionToFront = true;
            if (ignoreActive)
            {
                // Find any crew on active vessels.
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || !vessel.loaded) continue;
                    foreach (var part in vessel.Parts)
                    {
                        if (part == null) continue;
                        foreach (var crew in part.protoModuleCrew)
                        {
                            if (crew == null) continue;
                            ActiveCrewMembers.Add(crew.name);
                        }
                    }
                }
            }
            else { ActiveCrewMembers.Clear(); }
            GUIUtils.SetGUIRectVisible(_crewGUICheckIndex, true);
            foreach (var crew in HighLogic.CurrentGame.CrewRoster.Kerbals(ProtoCrewMember.KerbalType.Crew)) // Set any non-assigned crew as available.
            {
                if (crew.rosterStatus != ProtoCrewMember.RosterStatus.Assigned)
                    crew.rosterStatus = ProtoCrewMember.RosterStatus.Available;
            }
            RefreshObserverCrewMembers();
        }

        /// <summary>
        /// Hide the crew selection window.
        /// </summary>
        public void HideCrewSelection(CustomVesselSpawnConfig vesselSpawnConfig = null)
        {
            if (vesselSpawnConfig != null)
            {
                SelectedCrewMembers.Remove(vesselSpawnConfig.kerbalName);
                vesselSpawnConfig.kerbalName = null;
            }
            showCrewSelection = false;
            currentVesselSpawnConfig = null;
            GUIUtils.SetGUIRectVisible(_crewGUICheckIndex, false);
        }

        /// <summary>
        /// Crew selection window borrowed from VesselMover and modified.
        /// </summary>
        /// <param name="windowID"></param>
        public void CrewSelectionWindow(int windowID)
        {
            KerbalRoster kerbalRoster = HighLogic.CurrentGame.CrewRoster;
            GUI.DragWindow(new Rect(0, 0, crewSelectionWindowRect.width, 20));
            GUILayout.BeginVertical();
            crewSelectionScrollPos = GUILayout.BeginScrollView(crewSelectionScrollPos, GUI.skin.box, GUILayout.Width(crewSelectionWindowRect.width - 15), GUILayout.MaxHeight(crewSelectionWindowRect.height - 60));
            using (var kerbals = kerbalRoster.Kerbals(ProtoCrewMember.KerbalType.Crew).GetEnumerator())
                while (kerbals.MoveNext())
                {
                    ProtoCrewMember crewMember = kerbals.Current;
                    if (crewMember == null || SelectedCrewMembers.Contains(crewMember.name) || ObserverCrewMembers.Contains(crewMember.name) || ActiveCrewMembers.Contains(crewMember.name)) continue;
                    if (GUILayout.Button($"{crewMember.name}, {crewMember.gender}, {crewMember.trait}", BDArmorySetup.BDGuiSkin.button))
                    {
                        SelectedCrewMembers.Remove(currentVesselSpawnConfig.kerbalName);
                        SelectedCrewMembers.Add(crewMember.name);
                        currentVesselSpawnConfig.kerbalName = crewMember.name;
                        HideCrewSelection();
                    }
                }
            GUILayout.EndScrollView();
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_CraftBrowser_Clear"), BDArmorySetup.BDGuiSkin.button))
            {
                SelectedCrewMembers.Remove(currentVesselSpawnConfig.kerbalName);
                currentVesselSpawnConfig.kerbalName = null;
                HideCrewSelection();
            }
            if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_CraftBrowser_ClearAll"), BDArmorySetup.BDGuiSkin.button))
            {
                SelectedCrewMembers.Clear();
                foreach (var team in customSpawnConfig.customVesselSpawnConfigs)
                    foreach (var member in team)
                        member.kerbalName = null;
            }
            if (GUILayout.Button(StringUtils.Localize("#LOC_BDArmory_CraftBrowser_Refresh"), BDArmorySetup.BDGuiSkin.button))
            { RefreshSelectedCrew(); }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUIUtils.RepositionWindow(ref crewSelectionWindowRect);
            GUIUtils.UpdateGUIRect(crewSelectionWindowRect, _crewGUICheckIndex);
            GUIUtils.UseMouseEventInRect(crewSelectionWindowRect);
            if (bringCrewSelectionToFront)
            {
                bringCrewSelectionToFront = false;
                GUI.BringWindowToFront(windowID);
            }
        }

        /// <summary>
        /// Refresh the list of who's been selected.
        /// </summary>
        void RefreshSelectedCrew()
        {
            SelectedCrewMembers.Clear();
            foreach (var team in customSpawnConfig.customVesselSpawnConfigs)
                foreach (var member in team)
                    if (!string.IsNullOrEmpty(member.kerbalName))
                        SelectedCrewMembers.Add(member.kerbalName);
        }

        /// <summary>
        /// Refresh the crew members that are on observer craft.
        /// </summary>
        public void RefreshObserverCrewMembers()
        {
            ObserverCrewMembers.Clear();
            // Find any crew on observer vessels.
            foreach (var vessel in VesselSpawnerWindow.Instance.Observers)
            {
                if (vessel == null || !vessel.loaded) continue;
                foreach (var part in vessel.Parts)
                {
                    if (part == null) continue;
                    foreach (var crew in part.protoModuleCrew)
                    {
                        if (crew == null) continue;
                        ObserverCrewMembers.Add(crew.name);
                    }
                }
            }
            // Remove any observers from already assigned slots.
            foreach (var team in customSpawnConfig.customVesselSpawnConfigs)
                foreach (var member in team)
                    if (!string.IsNullOrEmpty(member.kerbalName) && ObserverCrewMembers.Contains(member.kerbalName))
                        member.kerbalName = null;
            // Then refresh the selected crew.
            RefreshSelectedCrew();
        }
        #endregion

        /// <summary>
        /// Hide other custom spawn template windows except for the named one.
        /// </summary>
        /// <param name="keep">The window to keep open.</param>
        public void HideOtherWindows(string keep)
        {
            if (showTemplateSelection && keep != "template") HideTemplateSelection();
            if (showCrewSelection && keep != "crew") HideCrewSelection();
            if (showVesselSelection && keep != "vessel") HideVesselSelection();
        }

        #endregion
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class CustomSpawnTemplateField : Attribute
    {
        public static string customSpawnTemplateFileLocation = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "BDArmory", "PluginData", "spawn_templates.cfg"));

        /// <summary>
        /// Save the custom spawn templates to disk.
        /// </summary>
        public static void Save()
        {
            ConfigNode fileNode = ConfigNode.Load(customSpawnTemplateFileLocation);
            if (fileNode == null)
                fileNode = new ConfigNode();

            if (!fileNode.HasNode("CustomSpawnTemplates"))
                fileNode.AddNode("CustomSpawnTemplates");

            ConfigNode spawnTemplates = fileNode.GetNode("CustomSpawnTemplates");

            spawnTemplates.ClearNodes();
            foreach (var spawnTemplate in CustomTemplateSpawning.customSpawnConfigs)
            {
                if (string.IsNullOrEmpty(spawnTemplate.name) || spawnTemplate.customVesselSpawnConfigs.Count == 0) continue; // Skip unnamed or invalid templates.

                var templateNode = spawnTemplates.AddNode("TEMPLATE");
                templateNode.AddValue("name", spawnTemplate.name);
                templateNode.AddValue("worldIndex", spawnTemplate.worldIndex);
                templateNode.AddValue("latitude", spawnTemplate.latitude);
                templateNode.AddValue("longitude", spawnTemplate.longitude);
                templateNode.AddValue("altitude", spawnTemplate.altitude);
                foreach (var team in spawnTemplate.customVesselSpawnConfigs)
                {
                    var teamNode = templateNode.AddNode("TEAM");
                    foreach (var member in team)
                    {
                        var memberNode = teamNode.AddNode("MEMBER");
                        memberNode.AddValue("latitude", member.latitude);
                        memberNode.AddValue("longitude", member.longitude);
                        memberNode.AddValue("heading", member.heading);
                        memberNode.AddValue("craftURL", member.craftURL);
                    }
                }
            }

            if (!Directory.GetParent(customSpawnTemplateFileLocation).Exists)
            { Directory.GetParent(customSpawnTemplateFileLocation).Create(); }
            fileNode.Save(customSpawnTemplateFileLocation);
        }

        /// <summary>
        /// Load the custom spawn templates from disk.
        /// </summary>
        public static void Load()
        {
            ConfigNode fileNode = ConfigNode.Load(customSpawnTemplateFileLocation);
            CustomTemplateSpawning.customSpawnConfigs = new List<CustomSpawnConfig>();
            if (fileNode != null)
            {
                if (fileNode.HasNode("CustomSpawnTemplates"))
                {
                    ConfigNode spawnTemplates = fileNode.GetNode("CustomSpawnTemplates");
                    foreach (var templateNode in spawnTemplates.GetNodes("TEMPLATE"))
                    {
                        try
                        {
                            var customSpawnConfig = new CustomSpawnConfig(
                                (string)ParseField(templateNode, "name", typeof(string)),
                                new SpawnConfig(
                                    worldIndex: (int)ParseField(templateNode, "worldIndex", typeof(int)),
                                    latitude: (float)ParseField(templateNode, "latitude", typeof(float)),
                                    longitude: (float)ParseField(templateNode, "longitude", typeof(float)),
                                    altitude: (float)ParseField(templateNode, "altitude", typeof(float))
                                ),
                                new List<List<CustomVesselSpawnConfig>>()
                            );
                            int teamCount = 0;
                            foreach (var teamNode in templateNode.GetNodes("TEAM"))
                            {
                                if (teamNode == null) continue;
                                var team = new List<CustomVesselSpawnConfig>();
                                foreach (var memberNode in teamNode.GetNodes("MEMBER"))
                                {
                                    if (memberNode == null) continue;
                                    team.Add(new CustomVesselSpawnConfig(
                                        latitude: (double)ParseField(memberNode, "latitude", typeof(double)),
                                        longitude: (double)ParseField(memberNode, "longitude", typeof(double)),
                                        heading: (float)ParseField(memberNode, "heading", typeof(float)),
                                        craftURL: (string)ParseField(memberNode, "craftURL", typeof(string)),
                                        teamIndex: teamCount
                                    ));
                                }
                                if (team.Count > 0)
                                    customSpawnConfig.customVesselSpawnConfigs.Add(team);
                                ++teamCount;
                            }
                            if (customSpawnConfig.customVesselSpawnConfigs.Count() > 0)
                                CustomTemplateSpawning.customSpawnConfigs.Add(customSpawnConfig);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Try to parse the named field from the config node as the specified type.
        /// </summary>
        /// <param name="node">The config node</param>
        /// <param name="field">The field name</param>
        /// <param name="type">The type to parse as</param>
        /// <returns>The value as an object or null</returns>
        private static object ParseField(ConfigNode node, string field, Type type)
        {
            try
            {
                if (!node.HasValue(field))
                {
                    throw new ArgumentNullException(field, $"Field '{field}' is missing.");
                }
                var value = node.GetValue(field);
                try
                {
                    if (type == typeof(string))
                    { return value; }
                    else if (type == typeof(bool))
                    { return bool.Parse(value); }
                    else if (type == typeof(int))
                    { return int.Parse(value); }
                    else if (type == typeof(float))
                    { return float.Parse(value); }
                    else if (type == typeof(double))
                    { return double.Parse(value); }
                    else
                    { throw new ArgumentException("Invalid type specified."); }
                }
                catch (Exception e)
                { throw new ArgumentException($"Field '{field}': '{value}' could not be parsed as '{type}' | {e.ToString()}", field); }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            Debug.LogError($"[BDArmory.CustomSpawnTemplate]: Failed to parse field '{field}' of type '{type}' on node '{node.name}'");
            return null;
        }
    }
}
