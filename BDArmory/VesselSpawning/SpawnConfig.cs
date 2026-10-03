using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace BDArmory.VesselSpawning
{
    /// <summary>
    /// Configuration for spawning groups of vessels.
    /// 
    /// Note:
    /// This is currently partially specific to SpawnAllVesselsOnce and SpawnVesselsContinuosly.
    /// TODO: Make this generic and make CircularSpawnConfig a derived class of this.
    /// </summary>
    [Serializable]
    public class SpawnConfig
    {
        public SpawnConfig(int worldIndex, double latitude, double longitude, double altitude, bool killEverythingFirst = true, bool assignTeams = true, int numberOfTeams = 0, List<int> teamCounts = null, List<List<string>> teamsSpecific = null, string folder = "", List<string> craftFiles = null)
        {
            this.worldIndex = worldIndex;
            this.latitude = latitude;
            this.longitude = longitude;
            this.altitude = altitude;
            this.killEverythingFirst = killEverythingFirst;
            this.assignTeams = assignTeams;
            this.numberOfTeams = numberOfTeams;
            this.teamCounts = teamCounts; if (teamCounts != null) this.numberOfTeams = this.teamCounts.Count;
            this.teamsSpecific = teamsSpecific;
            this.folder = folder ?? "";
            this.craftFiles = craftFiles;
        }
        public SpawnConfig(SpawnConfig other)
        {
            worldIndex = other.worldIndex;
            latitude = other.latitude;
            longitude = other.longitude;
            altitude = other.altitude;
            killEverythingFirst = other.killEverythingFirst;
            assignTeams = other.assignTeams;
            numberOfTeams = other.numberOfTeams;
            teamCounts = other.teamCounts;
            teamsSpecific = other.teamsSpecific;
            folder = other.folder;
            craftFiles = other.craftFiles?.ToList();
        }
        public int worldIndex;
        public double latitude;
        public double longitude;
        public double altitude;
        public bool killEverythingFirst = true;
        public bool assignTeams = true;
        public int numberOfTeams = 0; // Number of teams (or FFA, Folders or Inf). For evenly (as possible) splitting vessels into teams.
        public List<int> teamCounts; // List of team numbers. For unevenly splitting vessels into teams based on their order in the tournament state file for the round. E.g., when spawning from folders.
        public List<List<string>> teamsSpecific; // Dictionary of vessels and teams. For splitting specific vessels into specific teams.
        public string folder = "";
        public List<string> craftFiles = null;
    }

    /// <summary>
    /// Configuration for spawning individual vessels. 
    /// @Note: this has to be a class so that setting editorFacility during spawning persists back to the calling function.
    /// </summary>
    [Serializable]
    public class VesselSpawnConfig(string craftURL, Vector3 position, Vector3 direction, float altitude, float pitch, bool airborne, bool inOrbit, int teamIndex = 0, bool reuseURLVesselName = false, bool deconflictVesselName = true, List<ProtoCrewMember> crew = null)
    {
        public string craftURL = craftURL; // The craft file.
        public Vector3 position = position; // World-space coordinates (x,y,z) to place the vessel once spawned (before adjusting for terrain altitude).
        public Vector3 direction = direction; // Direction to point the plane horizontally (i.e., heading).
        public float altitude = altitude; // Altitude above terrain / water to adjust spawning position to.
        public float pitch = pitch; // Pitch if spawning airborne.
        public bool airborne = airborne; // Whether the vessel should be spawned in an airborne configuration or not.
        public bool inOrbit = inOrbit; // Whether the vessel should be spawned in orbit or not (overrides airborne).
        public int teamIndex = teamIndex; // Index for team assignment.
        public bool reuseURLVesselName = reuseURLVesselName; // Reuse the vesselName for the same craftURL (for continuous spawning / tournaments).
        public bool deconflictVesselName = deconflictVesselName; // Apply vessel name deconfliction during spawning for consistent and unique vessel naming.
        public List<ProtoCrewMember> crew = crew?.ToList(); // Override the crew.
        public EditorFacility editorFacility = EditorFacility.SPH; // Which editorFacility the craft belongs to (found out during spawning).
    }

    /// <summary>
    /// Spawn config for circular spawning.
    /// Probably more of the fields from SpawnConfig should be in here.
    /// </summary>
    [Serializable]
    public class CircularSpawnConfig : SpawnConfig
    {
        public CircularSpawnConfig(SpawnConfig spawnConfig, float distance, bool absDistanceOrFactor, float intraTeamSeparation, float refHeading = 0) : base(spawnConfig)
        {
            this.distance = distance;
            this.absDistanceOrFactor = absDistanceOrFactor;
            this.intraTeamSeparation = intraTeamSeparation;
            this.refHeading = refHeading;
        }
        public CircularSpawnConfig(CircularSpawnConfig other) : base(other)
        {
            this.distance = other.distance;
            this.absDistanceOrFactor = other.absDistanceOrFactor;
            this.intraTeamSeparation = other.intraTeamSeparation;
            this.refHeading = other.refHeading;
        }
        public CircularSpawnConfig(int worldIndex, double latitude, double longitude, double altitude, float distance, bool absDistanceOrFactor, float intraTeamSeparation = 0, float refHeading = 0, bool killEverythingFirst = true, bool assignTeams = true, int numberOfTeams = 0, List<int> teamCounts = null, List<List<string>> teamsSpecific = null, string folder = "", List<string> craftFiles = null) : this(
                new SpawnConfig(
                    worldIndex,
                    latitude,
                    longitude,
                    altitude,
                    killEverythingFirst,
                    assignTeams,
                    numberOfTeams,
                    teamCounts,
                    teamsSpecific,
                    folder,
                    craftFiles
                ),
                distance,
                absDistanceOrFactor,
                intraTeamSeparation,
                refHeading
            )
        { } // Constructor for legacy SpawnConfigs that should be CircularSpawnConfigs.
        public float distance;
        public bool absDistanceOrFactor; // If true, the distance value is used as-is, otherwise it is used as a factor giving the actual distance: (N+1)*distance, where N is the number of vessels.
        public float intraTeamSeparation; // Separation between craft (0 => auto).
        public float refHeading; // Reference heading for the first craft.
    }

    /// <summary>
    /// Spawn config for custom templates.
    /// </summary>
    [Serializable]
    public class CustomSpawnConfig : SpawnConfig
    {
        public CustomSpawnConfig(string name, SpawnConfig spawnConfig, List<List<CustomVesselSpawnConfig>> vesselSpawnConfigs) : base(spawnConfig)
        {
            this.name = name;
            this.customVesselSpawnConfigs = vesselSpawnConfigs;
        }
        /// <summary>
        /// Note: this only makes a shallow copy of customVesselSpawnConfigs.
        /// </summary>
        /// <param name="other"></param>
        public CustomSpawnConfig(CustomSpawnConfig other) : base(other)
        {
            name = other.name;
            customVesselSpawnConfigs = other.customVesselSpawnConfigs?.Select(config => config?.ToList()).ToList();
            includeCraftURLs = other.includeCraftURLs;
        }
        public string name;
        public List<List<CustomVesselSpawnConfig>> customVesselSpawnConfigs;
        public bool includeCraftURLs = false;
        public override string ToString() => $"{{name: {name}, worldIndex: {worldIndex}, lat: {latitude:F3}, lon: {longitude:F3}, alt: {altitude:F0}, URLs: {includeCraftURLs}; {(customVesselSpawnConfigs == null ? "" : string.Join("; ", customVesselSpawnConfigs.Select(cfgs => string.Join(", ", cfgs))))}}}";
    }

    /// <summary>
    /// The individual custom vessel spawn configs.
    /// </summary>
    [Serializable]
    public class CustomVesselSpawnConfig
    {
        public CustomVesselSpawnConfig(double latitude, double longitude, float heading, int teamIndex)
        {
            this.latitude = latitude;
            this.longitude = longitude;
            this.heading = heading;
            this.teamIndex = teamIndex;
        }
        public string craftURL;
        public string kerbalName;
        public double latitude;
        public double longitude;
        public float heading;
        public int teamIndex;
        public override string ToString() => $"{{{(string.IsNullOrEmpty(craftURL) ? "" : $"{Path.GetFileNameWithoutExtension(craftURL)}, ")}{(string.IsNullOrEmpty(kerbalName) ? "" : $"{kerbalName}, ")}lat: {latitude:G3}, lon: {longitude:G3}, heading: {heading:F0}°, team: {teamIndex}}}";
    }
}