using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;

using BDArmory.Competition.OrchestrationStrategies;
using BDArmory.Evolution;
using BDArmory.Extensions;
using BDArmory.GameModes.Waypoints;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.VesselSpawning.SpawnStrategies;
using BDArmory.VesselSpawning;

namespace BDArmory.Competition
{
    // A serializable configuration for loading and saving the tournament state.
    [Serializable]
    public class RoundConfig : CircularSpawnConfig
    {
        public RoundConfig(int round, int heat, bool completed, CircularSpawnConfig config) : base(config) { this.round = round; this.heat = heat; this.completed = completed; SerializeTeams(); }
        public int round;
        public int heat;
        public bool completed;
        [SerializeField] List<string> _teams;
        public void SerializeTeams()
        {
            if (teamsSpecific == null)
            {
                _teams = null;
                return;
            }
            _teams = teamsSpecific.Select(team => JsonUtility.ToJson(new RoundConfigTeam { team = team })).ToList();
            craftFiles = null; // Avoid including the file list twice in the tournament.state file.
        }
        public void DeserializeTeams()
        {
            if (teamsSpecific == null) teamsSpecific = new List<List<string>>();
            else teamsSpecific.Clear();
            if (_teams != null)
            {
                try { teamsSpecific = _teams.Select(team => JsonUtility.FromJson<RoundConfigTeam>(team).team).ToList(); }
                catch (Exception e) { Debug.LogError($"[BDArmory.BDATournament]: Failed to deserialize teams: {e.Message}"); }
            }
            if (teamsSpecific.Count == 0) teamsSpecific = null;
        }

        [Serializable]
        class RoundConfigTeam // Serialisation helper for List<List<string>>
        {
            public List<string> team;
        }
    }

    // A serializable configuration for loading and saving the tournament state for custom template spawning.
    // Note: there's a fair bit of duplication here as C# doesn't allow seem to allow the following:
    // public class RoundConfig<T> where T : SpawnConfig { public RoundConfig(int round, int heat, bool completed, T config) : base(config) { ... } }
    [Serializable]
    public class TemplateRoundConfig : CustomSpawnConfig
    {
        public TemplateRoundConfig(int round, int heat, bool completed, CustomSpawnConfig config) : base(config) { this.round = round; this.heat = heat; this.completed = completed; SerializeTeams(); }
        public int round;
        public int heat;
        public bool completed;
        [SerializeField] List<string> _teams;
        public void SerializeTeams()
        {
            if (teamsSpecific == null)
            {
                _teams = null;
                return;
            }
            _teams = teamsSpecific.Select(team => JsonUtility.ToJson(new RoundConfigTeam { team = team })).ToList();
            craftFiles = null; // Avoid including the file list twice in the tournament.state file.
        }
        public void DeserializeTeams()
        {
            if (teamsSpecific == null) teamsSpecific = new List<List<string>>();
            else teamsSpecific.Clear();
            if (_teams != null)
            {
                try { teamsSpecific = _teams.Select(team => JsonUtility.FromJson<RoundConfigTeam>(team).team).ToList(); }
                catch (Exception e) { Debug.LogError($"[BDArmory.BDATournament]: Failed to deserialize teams: {e.Message}"); }
            }
            if (teamsSpecific.Count == 0) teamsSpecific = null;
        }

        [Serializable]
        class RoundConfigTeam // Serialisation helper for List<List<string>>
        {
            public List<string> team;
        }
    }

    [Serializable]
    public class TournamentScores
    {
        public Dictionary<string, string> playersToFileNames = []; // Match players with craft filenames for extending ranks rounds.
        public Dictionary<string, string> playersToTeamNames = []; // Match the players with team names (for teams competitions).
        public Dictionary<string, bool> playerIsFighter = []; // Track which players are fighters (spawned from other craft).
        public Dictionary<string, float> scores = []; // The current scores for the tournament.
        public float lastUpdated = 0;
        HashSet<string> npcs = [];
        Dictionary<string, List<ScoringData>> scoreDetails = []; // Scores per player per round. Rounds players weren't involved in contain default ScoringData entries.
        List<CompetitionOutcome> competitionOutcomes = [];
        public static readonly Dictionary<string, float> defaultWeights = new()
        {
            {"Wins",                    1f},
            {"Survived",                0f},
            {"MIA",                     0f},
            {"Deaths",                 -1f},
            {"Death Order",             1f},
            {"Death Time",              0.002f},
            {"Clean Kills",             3f},
            {"Assists",                 1.5f},
            {"Hits",                    0.004f},
            {"Hits Taken",              0f},
            {"Bullet Damage",           0.0001f},
            {"Bullet Damage Taken",     4e-05f},
            {"Rocket Hits",             0.01f},
            {"Rocket Hits Taken",       0f},
            {"Rocket Parts Hit",        0.0005f},
            {"Rocket Parts Hit Taken",  0f},
            {"Rocket Damage",           0.0001f},
            {"Rocket Damage Taken",     4e-05f},
            {"Missile Hits",            0.15f},
            {"Missile Hits Taken",      0f},
            {"Missile Parts Hit",       0.002f},
            {"Missile Parts Hit Taken", 0f},
            {"Missile Damage",          3e-05f},
            {"Missile Damage Taken",    1.5e-05f},
            {"RamScore",                0.075f},
            {"RamScore Taken",          0f},
            {"Battle Damage",           0f},
            {"Parts Lost To Asteroids", 0f},
            {"HP Remaining",            0f},
            {"Accuracy",                0f},
            {"Rocket Accuracy",         0f},
            {"Waypoint Count",         1f}, // Waypoint weighting logic: 1 for passing a gate, 1s = 5 deviation, break-even at 60s + 200 deviation per gate.
            {"Waypoint Time",          -0.01f},
            {"Waypoint Deviation",     -0.002f}
        };
        public static Dictionary<string, float> weights = new(defaultWeights);

        /// <summary>
        /// Reset scores for a new tournament.
        /// </summary>
        public void Reset()
        {
            playersToFileNames.Clear();
            playersToTeamNames.Clear();
            scoreDetails.Clear();
            scores.Clear();
            competitionOutcomes.Clear();
            npcs.Clear();
            lastUpdated = Time.time;
        }

        /// <summary>
        /// Add a player to the tournament scoring data.
        /// </summary>
        /// <param name="player">The player (vesselName).</param>
        /// <param name="fileName">The craft file belonging to the player (required for generating ranked rounds).</param>
        /// <param name="currentRound">The current round (fills previous rounds with empty score data).</param>
        /// <param name="npc">Whether the player is an NPC or not.</param>
        /// <param name="fighter">Whether the player is a fighter (detached from parent vessel) or not.</param>
        /// <returns>true if successfully added, false otherwise.</returns>
        public bool AddPlayer(string player, string fileName, int currentRound = -1, bool npc = false, bool fighter = false)
        {
            if (playersToFileNames.ContainsKey(player)) return false; // They're already there.
            if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName)) { Debug.LogWarning($"[BDArmory.BDATournament]: {fileName} does not exist for {player}."); return false; }
            if (currentRound < 0) currentRound = BDATournament.Instance.currentRound;
            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log($"[BDArmory.BDATournament]: Adding {player} with file {fileName} in round {currentRound}");
            playersToFileNames.Add(player, fileName);
            playerIsFighter.Add(player, fighter);
            scoreDetails.Add(player, []);
            if (npc) npcs.Add(player);
            return true;
        }

        public bool IsNPC(string player) => npcs.Contains(player);
        /// <summary>
        /// Update score weights.
        /// Only valid weights in the newWeights dictionary are updated.
        /// </summary>
        /// <param name="newWeights">A dictionary of weights to update.</param>
        public static void ConfigureScoreWeights(Dictionary<string, float> newWeights)
        {
            if (newWeights == null) return;
            foreach (var key in newWeights.Keys)
            {
                if (!weights.ContainsKey(key))
                {
                    Debug.LogWarning($"[BDArmory.BDATournament]: Invalid score key {key}");
                    continue;
                }
                weights[key] = newWeights[key];
            }
            SaveWeights();
        }

        /// <summary>
        /// Add the scores for a heat to the current tournament scores.
        /// Note: this doesn't update the scores data, only the scoreDetails data. Call ComputeScores() between rounds to update the scores data.
        /// </summary>
        /// <param name="competitionScores"></param>
        public void AddHeatScores(CompetitionScores competitionScores)
        {
            competitionOutcomes.Add(new CompetitionOutcome
            {
                competitionResult = competitionScores.competitionResult,
                survivingTeams = competitionScores.survivingTeams.Select(t => t.ToList()).ToList(), // Perform deep copies of the lists.
                deadTeams = competitionScores.deadTeams.Select(t => t.ToList()).ToList()
            });
            foreach (var player in competitionScores.Players)
            {
                if (!scoreDetails.ContainsKey(player)) continue; // Ignore players that weren't registered with the tournament.
                scoreDetails[player].Add(competitionScores.ScoreData[player].Clone()); // Add the player's score to the tournament scores.
            }
        }

        /// <summary>
        /// For each player in the competition, compute a new score based on their scoreDetails.
        /// </summary>
        public void ComputeScores()
        {
            scores.Clear();
            foreach (var player in scoreDetails.Keys)
            {
                if (IsNPC(player)) continue; // Ignore NPCs for overall score totals.
                scores[player] = ComputeScore(player);
            }
            lastUpdated = Time.time;
            if (BDArmorySettings.DEBUG_COMPETITION)
            {
                Debug.Log($"[BDArmory.BDATournament]: Tournament scores: {string.Join(", ", scores.Select(s => $"{s.Key}: {s.Value}"))}");
                Debug.Log($"[BDArmory.BDATournament]: NPC scores: {string.Join(", ", npcs.Select(npc => $"{npc}: {ComputeScore(npc)}"))}");
            }
        }

        HashSet<AliveState> cleanKills = [AliveState.CleanKill, AliveState.HeadShot, AliveState.KillSteal];
        /// <summary>
        /// Compute the score for a player.
        /// </summary>
        /// <param name="player">The player.</param>
        /// <returns>The player's score.</returns>
        public float ComputeScore(string player)
        {
            if (!scoreDetails.ContainsKey(player)) return 0;
            var scoreData = scoreDetails[player];
            var shotsFired = scoreData.Sum(sd => sd.shotsFired);
            var rocketsFired = scoreData.Sum(sd => sd.rocketsFired);
            Dictionary<string, float> playerScore = new()
            {
                {"Wins", competitionOutcomes.Count(comp => comp.competitionResult == CompetitionResult.Win && comp.survivingTeams.Any(team => team.Contains(player)))},
                {"Survived", scoreData.Count(sd => sd.survivalState == SurvivalState.Alive)},
                {"MIA", scoreData.Count(sd => sd.survivalState == SurvivalState.MIA)},
                {"Deaths", scoreData.Count(sd => sd.survivalState == SurvivalState.Dead)},
                {"Death Order", scoreData.Sum(sd => sd.deathOrder > -1 ? sd.deathOrder / (float)sd.numberOfCompetitors : 1f)},
                {"Death Time", (float)scoreData.Sum(sd => sd.deathTime > -1 ? sd.deathTime : sd.compDuration)},
                {"Clean Kills", scoreDetails.Where(details => details.Key != player).Sum(details => details.Value.Count(sd => cleanKills.Contains(sd.aliveState) && sd.gmKillReason == GMKillReason.None && sd.lastPersonWhoDamagedMe == player))},
                {"Assists", scoreDetails.Where(details => details.Key != player).Sum(details => details.Value.Count(sd => sd.aliveState == AliveState.AssistedKill && ((sd.hitCounts.ContainsKey(player) && sd.hitCounts[player] > 0) || (sd.rocketStrikeCounts.ContainsKey(player) && sd.rocketStrikeCounts[player] > 0) || (sd.missileHitCounts.ContainsKey(player) && sd.missileHitCounts[player] > 0) || (sd.rammingPartLossCounts.ContainsKey(player) && sd.rammingPartLossCounts[player] > 0))))},
                {"Hits", scoreData.Sum(sd => sd.hits)},
                {"Hits Taken", scoreData.Sum(sd => sd.hitCounts.Values.Sum())},
                {"Bullet Damage", scoreDetails.Where(details => details.Key != player).Sum(details => details.Value.Sum(sd => sd.damageFromGuns.ContainsKey(player) ? sd.damageFromGuns[player] : 0f))},
                {"Bullet Damage Taken", scoreData.Sum(sd => sd.damageFromGuns.Values.Sum())},
                {"Rocket Hits", scoreData.Sum(sd => sd.rocketStrikes)},
                {"Rocket Hits Taken", scoreData.Sum(sd => sd.rocketStrikeCounts.Values.Sum())},
                {"Rocket Parts Hit", scoreData.Sum(sd => sd.totalDamagedPartsDueToRockets)},
                {"Rocket Parts Hit Taken", scoreData.Sum(sd => sd.rocketPartDamageCounts.Values.Sum())},
                {"Rocket Damage", scoreDetails.Where(details => details.Key != player).Sum(details => details.Value.Sum(sd => sd.damageFromRockets.ContainsKey(player) ? sd.damageFromRockets[player] : 0f))},
                {"Rocket Damage Taken", scoreData.Sum(sd => sd.damageFromRockets.Values.Sum())},
                {"Missile Hits", scoreDetails.Where(details => details.Key != player).Sum(details => details.Value.Sum(sd => sd.missileHitCounts.ContainsKey(player) ? sd.missileHitCounts[player] : 0))},
                {"Missile Hits Taken", scoreData.Sum(sd => sd.missileHitCounts.Values.Sum())},
                {"Missile Parts Hit", scoreData.Sum(sd => sd.totalDamagedPartsDueToMissiles)},
                {"Missile Parts Hit Taken", scoreData.Sum(sd => sd.missilePartDamageCounts.Values.Sum())},
                {"Missile Damage", scoreDetails.Where(details => details.Key != player).Sum(details => details.Value.Sum(sd => sd.damageFromMissiles.ContainsKey(player) ? sd.damageFromMissiles[player] : 0f))},
                {"Missile Damage Taken", scoreData.Sum(sd => sd.damageFromMissiles.Values.Sum())},
                {"RamScore", scoreData.Sum(sd => sd.totalDamagedPartsDueToRamming)},
                {"RamScore Taken", scoreData.Sum(sd => sd.rammingPartLossCounts.Values.Sum())},
                {"Battle Damage", scoreDetails.Where(details => details.Key != player).Sum(details => details.Value.Sum(sd => sd.battleDamageFrom.ContainsKey(player) ? sd.battleDamageFrom[player] : 0f))},
                {"Parts Lost To Asteroids", scoreData.Sum(sd => sd.partsLostToAsteroids)},
                {"HP Remaining", (float)scoreData.Sum(sd => sd.remainingHP)},
                {"Accuracy", shotsFired > 0 ? scoreData.Sum(sd => sd.hits) / (float)shotsFired : 0f},
                {"Rocket Accuracy", rocketsFired > 0 ? scoreData.Sum(sd => sd.rocketStrikes) / (float)rocketsFired : 0f}
            };
            // Waypoints are scored per round and clamped non-negative to avoid excessive negative scores for bad runs.
            var wpScore = scoreData.Sum(sd => Mathf.Max(0, weights["Waypoint Count"] * sd.totalWPReached + weights["Waypoint Time"] * sd.totalWPTime + weights["Waypoint Deviation"] * sd.totalWPDeviation));
            if (BDArmorySettings.DEBUG_COMPETITION) Debug.Log($"[BDArmory.BDATournament]: Score components for {player}: {string.Join(", ", playerScore.Select(kvp => $"{kvp.Key}: {kvp.Value}"))}");
            if (scoreData.Count > 0)
            {
                var teamName = scoreData.First().team;
                if (scoreData.All(sd => sd.team == teamName)) // If the team is consistent, populate the team names dictionary.
                    playersToTeamNames[player] = teamName;
            }
            else playersToTeamNames[player] = "";
            return playerScore.Sum(kvp => kvp.Value * weights[kvp.Key]) + wpScore;
        }

        /// <summary>
        /// Get the craft files in ascending order of the currently computed scores (summing over those deriving from the same craft file, i.e., fighters).
        /// Notes:
        /// - This ignores NPCs since they're not included in the overall scoring.
        /// - Having multiple craft from the same file (other than fighters) in the ranked tournament will mess with this.
        /// </summary>
        public List<string> GetRankedCraftFiles()
        {
            List<string> nonFighters = [.. playerIsFighter.Where(kvp => !kvp.Value).Select(kvp => kvp.Key)];
            var combinedScores = nonFighters.ToDictionary(player => player, player => playersToFileNames.Where(kvp => !string.IsNullOrEmpty(kvp.Value) && kvp.Value == playersToFileNames[player]).Sum(kvp => scores[kvp.Key]));
            List<string> ranking = [.. combinedScores.OrderBy(kvp => kvp.Value).Select(kvp => playersToFileNames[kvp.Key])];
            return ranking;
        }
        public List<int> GetRankedTeams(List<List<string>> teamFiles)
        {
            // While the reverse of playersToFileNames is not necessarily 1-to-1 (due to the full teams option), duplicates of the same craft file should be on the same team.
            // The following accumulates the scores for all players with those vessels in each team, which is then used to rank the teams.
            List<float> teamScores = [.. teamFiles.Select(tm => scores.Where(kvp => tm.Contains(playersToFileNames[kvp.Key])).Sum(kvp => kvp.Value))];
            return [.. Enumerable.Range(0, teamFiles.Count).OrderBy(i => teamScores[i])];
        }

        #region Serialization
        [SerializeField] List<string> _weightKeys;
        [SerializeField] List<float> _weightValues;
        [SerializeField] List<string> _players;
        [SerializeField] List<string> _npcs;
        [SerializeField] List<string> _fighters;
        [SerializeField] List<string> _scores;
        [SerializeField] List<string> _files;
        [SerializeField] List<string> _results;
        public TournamentScores PrepareSerialization()
        {
            _weightKeys = [.. weights.Keys];
            _weightValues = [.. _weightKeys.Select(k => weights[k])];
            _players = [.. scoreDetails.Keys];
            _npcs = [.. npcs];
            _fighters = [.. playerIsFighter.Where(p => p.Value).Select(p => p.Key)];
            _scores = [.. _players.Where(scoreDetails.ContainsKey).Select(p => JsonUtility.ToJson(new SerializedScoreDataList().Serialize(p, scoreDetails[p], _players)))];
            _files = [.. _players.Where(playersToFileNames.ContainsKey).Select(p => playersToFileNames[p])]; // If the craft file has been removed, try to cope without it.
            _results = [.. competitionOutcomes.Select(r => JsonUtility.ToJson(r.PreSerialize()))];
            return this;
        }
        public void PostDeserialization()
        {
            Reset();
            ConfigureScoreWeights(Enumerable.Range(0, _weightKeys.Count).ToDictionary(i => _weightKeys[i], i => _weightValues[i]));
            for (int i = 0; i < _players.Count; ++i)
            {
                var player = _players[i];
                AddPlayer(player: player, fileName: _files[i], 0, npc: _npcs.Contains(player), fighter: _fighters.Contains(player));
            }
            try
            {
                scoreDetails = Enumerable.Range(0, _players.Count).ToDictionary(i => _players[i], i => JsonUtility.FromJson<SerializedScoreDataList>(_scores[i])).ToDictionary(kvp => kvp.Key, kvp =>
                {
                    if (kvp.Value == null)
                    {
                        Debug.LogError($"[BDArmory.BDATournament]: Failed to deserialize List<ScoreData>.");
                        return [];
                    }
                    return kvp.Value.Deserialize(_players);
                });
            }
            catch (Exception e) { Debug.LogError($"[BDArmory.BDATournament]: Failed to deserialize tournament scores: {e.Message}\n{e.StackTrace}"); }
            try
            {
                competitionOutcomes = [.. _results.Select(r => JsonUtility.FromJson<CompetitionOutcome>(r).PostDeserialize())];
            }
            catch (Exception e) { Debug.LogError($"[BDArmory.BDATournament]: Failed to deserialize competition outcomes: {e.Message}\n{e.StackTrace}"); }
        }

        [Serializable]
        class SerializedScoreDataList
        {
            [SerializeField] List<string> serializedScoreData;

            public SerializedScoreDataList Serialize(string player, List<ScoringData> scoreDetails, List<string> players)
            {
                serializedScoreData = [.. scoreDetails.Select(sd => JsonUtility.ToJson(new SerializedScoreData().Serialize(sd, players)))];
                return this;
            }
            public List<ScoringData> Deserialize(List<string> players)
            {
                List<SerializedScoreData> ssdl = [.. serializedScoreData.Select(JsonUtility.FromJson<SerializedScoreData>)];
                List<ScoringData> sdl = [];
                foreach (var ssd in ssdl)
                {
                    if (ssd == null)
                    {
                        Debug.LogError($"[BDArmory.BDATournament]: Failed to deserialize ScoreData.");
                        sdl.Add(new ScoringData());
                    }
                    else
                    {
                        sdl.Add(ssd.Deserialize(players));
                    }
                }
                return sdl;
            }
        }

        /// <summary>
        /// A class for serializing the ScoreData.
        /// All non-basic types and non-Lists need to be converted to strings or basic Lists.
        /// </summary>
        [Serializable]
        class SerializedScoreData
        {
            public string scoreData; // Easily serialisable fields.
            public List<int> hitCounts;
            public List<float> damageFromGuns;
            public List<float> damageFromRockets;
            public List<int> rocketPartDamageCounts;
            public List<int> rocketStrikeCounts;
            public List<int> rammingPartLossCounts;
            public List<float> damageFromMissiles;
            public List<int> missilePartDamageCounts;
            public List<int> missileHitCounts;
            public List<float> battleDamageFrom;
            public List<DamageFrom> damageTypesTaken;
            public List<string> everyoneWhoDamagedMe;

            /// <summary>
            /// Serialize ScoringData to SerializedScoreData prior to saving to JSON.
            /// </summary>
            /// <param name="scores">The scoring data.</param>
            /// <param name="players">The list of players in the tournament.</param>
            public SerializedScoreData Serialize(ScoringData scores, List<string> players)
            {
                scoreData = JsonUtility.ToJson(scores);
                hitCounts = [];
                damageFromGuns = [];
                damageFromRockets = [];
                rocketPartDamageCounts = [];
                rocketStrikeCounts = [];
                rammingPartLossCounts = [];
                damageFromMissiles = [];
                missilePartDamageCounts = [];
                missileHitCounts = [];
                battleDamageFrom = [];
                foreach (var player in players)
                {
                    hitCounts.Add(scores.hitCounts.ContainsKey(player) ? scores.hitCounts[player] : 0);
                    damageFromGuns.Add(scores.damageFromGuns.ContainsKey(player) ? scores.damageFromGuns[player] : 0);
                    damageFromRockets.Add(scores.damageFromRockets.ContainsKey(player) ? scores.damageFromRockets[player] : 0);
                    rocketPartDamageCounts.Add(scores.rocketPartDamageCounts.ContainsKey(player) ? scores.rocketPartDamageCounts[player] : 0);
                    rocketStrikeCounts.Add(scores.rocketStrikeCounts.ContainsKey(player) ? scores.rocketStrikeCounts[player] : 0);
                    rammingPartLossCounts.Add(scores.rammingPartLossCounts.ContainsKey(player) ? scores.rammingPartLossCounts[player] : 0);
                    damageFromMissiles.Add(scores.damageFromMissiles.ContainsKey(player) ? scores.damageFromMissiles[player] : 0);
                    missilePartDamageCounts.Add(scores.missilePartDamageCounts.ContainsKey(player) ? scores.missilePartDamageCounts[player] : 0);
                    missileHitCounts.Add(scores.missileHitCounts.ContainsKey(player) ? scores.missileHitCounts[player] : 0);
                    battleDamageFrom.Add(scores.battleDamageFrom.ContainsKey(player) ? scores.battleDamageFrom[player] : 0);
                }
                damageTypesTaken = [.. scores.damageTypesTaken];
                everyoneWhoDamagedMe = [.. scores.everyoneWhoDamagedMe];
                return this;
            }

            /// <summary>
            /// Deserialize SerializedScoreData after loading from JSON.
            /// </summary>
            /// <param name="players">The players that were originally used to serialize the score data.</param>
            /// <returns>The ScoringData for a player.</returns>
            public ScoringData Deserialize(List<string> players)
            {
                var scores = JsonUtility.FromJson<ScoringData>(scoreData);
                scores.hitCounts = [];
                scores.damageFromGuns = [];
                scores.damageFromRockets = [];
                scores.rocketPartDamageCounts = [];
                scores.rocketStrikeCounts = [];
                scores.rammingPartLossCounts = [];
                scores.damageFromMissiles = [];
                scores.missilePartDamageCounts = [];
                scores.missileHitCounts = [];
                scores.battleDamageFrom = [];
                scores.damageTypesTaken = [];
                scores.everyoneWhoDamagedMe = [];
                try
                {
                    foreach (var i in Enumerable.Range(0, players.Count))
                    {
                        var player = players[i];
                        scores.hitCounts[player] = hitCounts[i];
                        scores.damageFromGuns[player] = damageFromGuns[i];
                        scores.damageFromRockets[player] = damageFromRockets[i];
                        scores.rocketPartDamageCounts[player] = rocketPartDamageCounts[i];
                        scores.rocketStrikeCounts[player] = rocketStrikeCounts[i];
                        scores.rammingPartLossCounts[player] = rammingPartLossCounts[i];
                        scores.damageFromMissiles[player] = damageFromMissiles[i];
                        scores.missilePartDamageCounts[player] = missilePartDamageCounts[i];
                        scores.missileHitCounts[player] = missileHitCounts[i];
                        scores.battleDamageFrom[player] = battleDamageFrom[i];
                    }
                    scores.damageTypesTaken = [.. damageTypesTaken];
                    scores.everyoneWhoDamagedMe = [.. everyoneWhoDamagedMe];
                }
                catch (Exception e) { Debug.LogError($"[BDArmory.BDATournament]: Failed to deserialize tournament score data: {e.Message}\n{e.StackTrace}"); }
                return scores;
            }
        }

        [Serializable]
        class CompetitionOutcome
        {
            public CompetitionResult competitionResult;
            public List<List<string>> survivingTeams;
            public List<List<string>> deadTeams;
            [SerializeField] List<string> _survivingTeams;
            [SerializeField] List<string> _deadTeams;
            public CompetitionOutcome PreSerialize()
            {
                _survivingTeams = survivingTeams.Select(t => JsonUtility.ToJson(new StringList { ls = t })).ToList();
                _deadTeams = deadTeams.Select(t => JsonUtility.ToJson(new StringList { ls = t })).ToList();
                return this;
            }
            public CompetitionOutcome PostDeserialize()
            {
                survivingTeams = _survivingTeams.Select(t => JsonUtility.FromJson<StringList>(t).ls).ToList();
                deadTeams = _deadTeams.Select(t => JsonUtility.FromJson<StringList>(t).ls).ToList();
                return this;
            }
        }

        public static void SaveWeights()
        {
            ConfigNode fileNode = ConfigNode.Load(ScoreWindow.scoreWeightsURL) ?? new ConfigNode();

            if (!fileNode.HasNode("ScoreWeights"))
            {
                fileNode.AddNode("ScoreWeights");
            }

            ConfigNode settings = fileNode.GetNode("ScoreWeights");

            foreach (var kvp in weights)
            {
                settings.SetValue(kvp.Key, kvp.Value.ToString(), true);
            }
            fileNode.Save(ScoreWindow.scoreWeightsURL);
        }

        public static void LoadWeights()
        {
            ConfigNode fileNode = ConfigNode.Load(ScoreWindow.scoreWeightsURL);
            if (fileNode == null || !fileNode.HasNode("ScoreWeights")) return;

            ConfigNode settings = fileNode.GetNode("ScoreWeights");

            foreach (var key in weights.Keys.ToList())
            {
                if (!settings.HasValue(key)) continue;

                object parsedValue = BDAPersistentSettingsField.ParseValue(typeof(float), settings.GetValue(key), key);
                if (parsedValue != null)
                {
                    weights[key] = (float)parsedValue;
                }
            }
        }
        #endregion
    }

    public enum TournamentType { FFA, Teams };
    public enum TournamentRoundType { Shuffled, Ranked };
    public enum TournamentStyle { RNG, nCk, Gauntlet, TemplateRNG };

    [Serializable]
    public class TournamentState
    {
        public static string defaultStateFile = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "BDArmory", "PluginData", "tournament.state"));
        public uint tournamentID;
        public string savegame;
        private List<string> craftFiles; // For FFA style tournaments.
        private List<List<string>> teamFiles; // For teams style tournaments.
        private List<List<string>> opponentTeamFiles; // For gauntlet style tournaments.
        public int vesselCount;
        public int teamCount;
        public int teamsPerHeat;
        public int vesselsPerTeam;
        public bool fullTeams;
        public int vesselsPerHeat;
        public int numberOfRounds;
        public int npcsPerHeat;
        public List<string> npcFiles = [];
        public TournamentType tournamentType = TournamentType.FFA;
        public TournamentStyle tournamentStyle = TournamentStyle.RNG;
        public TournamentRoundType tournamentRoundType = TournamentRoundType.Shuffled;
        [NonSerialized] public Dictionary<int, Dictionary<int, SpawnConfig>> rounds; // <Round, <Heat, CircularSpawnConfig>>
        [NonSerialized] public Dictionary<int, HashSet<int>> completed = [];
        [NonSerialized] private List<Queue<string>> teamSpawnQueues = [];
        [NonSerialized] private List<Queue<string>> opponentTeamSpawnQueues = [];
        private string message;
        public TournamentScores scores = new();
        [SerializeField] string _scores;
        [SerializeField] List<string> _heats;
        [SerializeField] List<string> _teamFiles;
        [SerializeField] List<string> _deconflictionURLs;
        [SerializeField] List<string> _deconflictionSuffixes;

        /// <summary>
        /// Generate a tournament.state file for FFA tournaments.
        /// Any deficit from splitting the vessels into heats is distributed amongst the heats such that there is always N or N-1 vessels per heat.
        /// </summary>
        /// <param name="folder"></param>
        /// <param name="numberOfRounds"></param>
        /// <param name="vesselsPerHeat"></param>
        /// <param name="tournamentStyle"></param>
        /// <param name="tournamentRoundType"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public bool GenerateFFATournament(string folder, int numberOfRounds, int vesselsPerHeat, int npcsPerHeat, TournamentStyle tournamentStyle, TournamentRoundType tournamentRoundType)
        {
            folder ??= ""; // Sanitise null strings.
            tournamentID = (uint)DateTime.UtcNow.Subtract(new DateTime(2020, 1, 1)).TotalSeconds;
            savegame = HighLogic.SaveFolder;
            tournamentType = TournamentType.FFA;
            this.tournamentStyle = tournamentStyle;
            if (tournamentStyle != TournamentStyle.RNG && tournamentRoundType == TournamentRoundType.Ranked)
            {
                message = "Ranked tournament mode is invalid for non-RNG style tournaments.";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.Log($"[BDArmory.BDATournament]: " + message);
                return false;
            }
            this.tournamentRoundType = tournamentRoundType;
            numberOfRounds = tournamentRoundType == TournamentRoundType.Ranked ? 1 : numberOfRounds; // Ranked tournaments generate a single Shuffled round, then just go until the current number of rounds slider +1 is satisfied.
            this.numberOfRounds = numberOfRounds;
            this.vesselsPerHeat = vesselsPerHeat;
            var abs_folder = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "AutoSpawn", folder));
            if (!Directory.Exists(abs_folder))
            {
                message = "Tournament folder (" + folder + ") containing craft files does not exist.";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.Log("[BDArmory.BDATournament]: " + message);
                return false;
            }
            craftFiles = Directory.GetFiles(abs_folder, "*.craft").ToList();
            vesselCount = craftFiles.Count;
            var npc_folder = Path.Combine(abs_folder, "NPCs");
            npcFiles = Directory.Exists(npc_folder) ? Directory.GetFiles(npc_folder, "*.craft").ToList() : new List<string>();
            if (npcsPerHeat > 0 && npcFiles.Count == 0)
            {
                message = $"{npcsPerHeat} NPCs requested, but none exist in {Path.Combine("AutoSpawn", folder, "NPCs")}";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.Log($"[BDArmory.BDATournament]: {message}");
                return false;
            }
            this.npcsPerHeat = npcsPerHeat;
            int fullHeatCount;
            switch (vesselsPerHeat)
            {
                case -1: // Auto
                    var autoVesselsPerHeat = OptimiseVesselsPerHeat(craftFiles.Count, npcsPerHeat);
                    vesselsPerHeat = autoVesselsPerHeat.Item1;
                    fullHeatCount = Mathf.CeilToInt(craftFiles.Count / (float)vesselsPerHeat) - autoVesselsPerHeat.Item2;
                    break;
                case 0: // Unlimited (all vessels in one heat).
                    vesselsPerHeat = craftFiles.Count;
                    fullHeatCount = 1;
                    break;
                default:
                    vesselsPerHeat = Mathf.Clamp(Mathf.Max(1, vesselsPerHeat - npcsPerHeat), 1, craftFiles.Count);
                    fullHeatCount = Mathf.CeilToInt(craftFiles.Count / (float)vesselsPerHeat) - (Mathf.CeilToInt(craftFiles.Count / (float)vesselsPerHeat) * vesselsPerHeat - craftFiles.Count); // Spread the deficit amongst the other heats if possible.
                    if (fullHeatCount <= 0) fullHeatCount = craftFiles.Count / vesselsPerHeat;
                    break;
            }
            rounds = [];
            switch (tournamentStyle)
            {
                case TournamentStyle.RNG: // RNG
                    {
                        message = $"Generating {numberOfRounds} randomised rounds for {(tournamentRoundType == TournamentRoundType.Ranked ? "ranked " : "")}tournament {tournamentID} for {vesselCount} vessels in AutoSpawn{(folder == "" ? "" : "/" + folder)}, each with up to {vesselsPerHeat} vessels per heat{(npcsPerHeat > 0 ? $" and {npcsPerHeat} NPCs per heat" : "")}.";
                        Debug.Log("[BDArmory.BDATournament]: " + message);
                        BDACompetitionMode.Instance.competitionStatus.Add(message);
                        for (int roundIndex = 0; roundIndex < numberOfRounds; ++roundIndex)
                        {
                            craftFiles.Shuffle();
                            int vesselsThisHeat = vesselsPerHeat;
                            int count = 0;
                            List<string> selectedFiles = craftFiles.Take(vesselsThisHeat).ToList();
                            rounds.Add(rounds.Count, []);
                            int heatIndex = 0;
                            while (selectedFiles.Count > 0)
                            {
                                if (npcsPerHeat > 0) // Add in some NPCs.
                                {
                                    npcFiles.Shuffle();
                                    selectedFiles.AddRange(Enumerable.Repeat(npcFiles, Mathf.CeilToInt((float)npcsPerHeat / (float)npcFiles.Count)).SelectMany(x => x).Take(npcsPerHeat)); // Repeat the shuffled npcFiles list enough times, then take the required number.
                                }
                                rounds[roundIndex].Add(rounds[roundIndex].Count, new CircularSpawnConfig(
                                    BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
                                    BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                                    BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                                    BDArmorySettings.VESSEL_SPAWN_ALTITUDE_,
                                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                                    BDArmorySettings.VESSEL_SPAWN_INTRATEAM_DISTANCE,
                                    BDArmorySettings.VESSEL_SPAWN_REF_HEADING,
                                    true, // Kill everything first.
                                    BDArmorySettings.VESSEL_SPAWN_REASSIGN_TEAMS, // Assign teams.
                                    0, // Number of teams.
                                    null, // List of team numbers.
                                    null, // List of List of teams' vessels.
                                    null, // No folder, we're going to specify the craft files.
                                    selectedFiles.ToList() // Add a copy of the craft files list.
                                ));
                                count += vesselsThisHeat;
                                vesselsThisHeat = ++heatIndex < fullHeatCount ? vesselsPerHeat : vesselsPerHeat - 1; // Take one less for the remaining heats to distribute the deficit of craft files.
                                selectedFiles = craftFiles.Skip(count).Take(vesselsThisHeat).ToList();
                            }
                        }
                        break;
                    }
                case TournamentStyle.nCk: // N-choose-K
                    {
                        var nCr = N_Choose_K(vesselCount, vesselsPerHeat);
                        message = $"Generating a round-robin style tournament for {vesselCount} vessels in AutoSpawn{(folder == "" ? "" : "/" + folder)} with up to {vesselsPerHeat} vessels per heat and {numberOfRounds} rounds. This requires {numberOfRounds * nCr} heats.";
                        Debug.Log($"[BDArmory.BDATournament]: " + message);
                        BDACompetitionMode.Instance.competitionStatus.Add(message);
                        // Generate all combinations of vessels for a round.
                        var heatList = new List<CircularSpawnConfig>();
                        foreach (var combination in Combinations(vesselCount, vesselsPerHeat))
                        {
                            heatList.Add(new CircularSpawnConfig(
                                BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
                                BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                                BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                                BDArmorySettings.VESSEL_SPAWN_ALTITUDE_,
                                BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                                BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                                BDArmorySettings.VESSEL_SPAWN_INTRATEAM_DISTANCE,
                                BDArmorySettings.VESSEL_SPAWN_REF_HEADING,
                                true, // Kill everything first.
                                BDArmorySettings.VESSEL_SPAWN_REASSIGN_TEAMS, // Assign teams.
                                0, // Number of teams.
                                null, // List of team numbers.
                                null, // List of List of teams' vessels.
                                null, // No folder, we're going to specify the craft files.
                                combination.Select(i => craftFiles[i]).ToList() // Add a copy of the craft files list.
                            ));
                        }
                        // Populate the rounds.
                        for (int roundIndex = 0; roundIndex < numberOfRounds; ++roundIndex)
                        {
                            heatList.Shuffle(); // Randomise the playing order within each round.
                            rounds.Add(roundIndex, heatList.Select((heat, index) => new KeyValuePair<int, SpawnConfig>(index, heat)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
                        }
                        break;
                    }
                default:
                    {
                        BDACompetitionMode.Instance.competitionStatus.Add($"Tournament style {tournamentStyle} not implemented yet for FFA.");
                        throw new ArgumentOutOfRangeException("tournamentStyle", $"Invalid tournament style {tournamentStyle} - not implemented.");
                    }
            }
            teamFiles = null; // Clear the teams lists.
            return true;
        }

        /// <summary>
        /// Generate a tournament.state file for teams tournaments.
        /// </summary>
        /// <param name="folder"></param>
        /// <param name="numberOfRounds"></param>
        /// <param name="teamsPerHeat"></param>
        /// <param name="vesselsPerTeam"></param>
        /// <param name="numberOfTeams"></param>
        /// <param name="tournamentStyle"></param>
        /// <param name="tournamentRoundType"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public bool GenerateTeamsTournament(string folder, int numberOfRounds, int teamsPerHeat, int vesselsPerTeam, int numberOfTeams, TournamentStyle tournamentStyle, TournamentRoundType tournamentRoundType)
        {
            folder ??= ""; // Sanitise null strings.
            tournamentID = (uint)DateTime.UtcNow.Subtract(new DateTime(2020, 1, 1)).TotalSeconds;
            savegame = HighLogic.SaveFolder;
            tournamentType = TournamentType.Teams;
            this.tournamentStyle = tournamentStyle;
            if (!(tournamentStyle == TournamentStyle.RNG || tournamentStyle == TournamentStyle.TemplateRNG) && tournamentRoundType == TournamentRoundType.Ranked)
            {
                message = "Ranked tournament mode is invalid for non-RNG style tournaments.";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.Log($"[BDArmory.BDATournament]: " + message);
                return false;
            }
            this.tournamentRoundType = tournamentRoundType;
            numberOfRounds = tournamentRoundType == TournamentRoundType.Ranked ? 1 : numberOfRounds; // Ranked tournaments generate a single Shuffled round, then just go until the current number of rounds slider +1 is satisfied.
            var absFolder = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "AutoSpawn", folder));
            if (!Directory.Exists(absFolder))
            {
                message = "Tournament folder (" + folder + ") containing craft files or team folders does not exist.";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.Log("[BDArmory.BDATournament]: " + message);
                return false;
            }
            if (numberOfTeams > 1) // Make teams from the files in the spawn folder.
            {
                craftFiles = Directory.GetFiles(absFolder, "*.craft").ToList();
                if (craftFiles.Count < numberOfTeams)
                {
                    message = "Insufficient vessels in AutoSpawn" + (!string.IsNullOrEmpty(folder) ? "/" + folder : "") + " to make " + numberOfTeams + " teams.";
                    BDACompetitionMode.Instance.competitionStatus.Add(message);
                    Debug.Log("[BDArmory.BDATournament]: " + message);
                    return false;
                }
                craftFiles.Shuffle();

                int numberPerTeam = craftFiles.Count / numberOfTeams;
                int residue = craftFiles.Count - numberPerTeam * numberOfTeams;
                teamFiles = [];
                for (int teamCount = 0, count = 0; teamCount < numberOfTeams; ++teamCount)
                {
                    var toTake = numberPerTeam + (teamCount < residue ? 1 : 0);
                    teamFiles.Add(craftFiles.Skip(count).Take(toTake).ToList());
                    count += toTake;
                }
            }
            else // Make teams from the folders under the spawn folder.
            {
                var teamDirs = Directory.GetDirectories(absFolder);
                if (tournamentStyle == TournamentStyle.Gauntlet) // If it's a gauntlet tournament, ignore the opponents folder if it's in the main folder.
                {
                    var opponentFolder = Path.GetFileName(BDArmorySettings.VESSEL_SPAWN_GAUNTLET_OPPONENTS_FILES_LOCATION.TrimEnd(new char[] { Path.DirectorySeparatorChar }));
                    if (teamDirs.Select(d => Path.GetFileName(d)).Contains(opponentFolder))
                    {
                        teamDirs = teamDirs.Where(d => Path.GetFileName(d) != opponentFolder).ToArray();
                    }
                }
                if (teamDirs.Length < (tournamentStyle != TournamentStyle.Gauntlet ? 2 : 1)) // Make teams from each vessel in the spawn folder. Allow for a single subfolder for putting bad craft or other tmp things in, unless it's a gauntlet competition.
                {
                    numberOfTeams = -1; // Flag for treating craft files as folder names.
                    craftFiles = Directory.GetFiles(absFolder, "*.craft").ToList();
                    teamFiles = craftFiles.Select(f => new List<string> { f }).ToList();
                }
                else
                {
                    teamFiles = [];
                    foreach (var teamDir in teamDirs)
                    {
                        var currentTeamFiles = Directory.GetFiles(teamDir, "*.craft").ToList();
                        if (currentTeamFiles.Count > 0)
                            teamFiles.Add(currentTeamFiles);
                    }
                    foreach (var team in teamFiles)
                        team.Shuffle();
                    craftFiles = teamFiles.SelectMany(v => v).ToList();
                }
            }
            vesselCount = craftFiles.Count;
            npcFiles.Clear(); // NPCs aren't supported in teams tournaments yet.
            if (teamFiles.Count < (tournamentStyle != TournamentStyle.Gauntlet ? 2 : 1))
            {
                message = $"Insufficient {(numberOfTeams != 1 ? "craft files" : "folders")} in '{Path.Combine("AutoSpawn", folder)}' to generate a tournament.";
                if (BDACompetitionMode.Instance) BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.Log("[BDArmory.BDATournament]: " + message);
                return false;
            }
            teamCount = teamFiles.Count;
            teamsPerHeat = Mathf.Clamp(teamsPerHeat, tournamentStyle != TournamentStyle.Gauntlet ? 2 : 1, teamFiles.Count);
            this.teamsPerHeat = teamsPerHeat;
            this.vesselsPerTeam = vesselsPerTeam;
            fullTeams = BDArmorySettings.TOURNAMENT_FULL_TEAMS;
            var teamsIndex = Enumerable.Range(0, teamFiles.Count).ToList();
            teamSpawnQueues.Clear();

            rounds = [];
            int fullHeatCount = teamFiles.Count / teamsPerHeat;
            switch (tournamentStyle)
            {
                case TournamentStyle.RNG: // RNG
                    {
                        message = $"Generating {numberOfRounds} randomised rounds for tournament {tournamentID} for {teamCount} teams in AutoSpawn{(folder == "" ? "" : "/" + folder)}, each with {teamsPerHeat} teams per heat.";
                        Debug.Log("[BDArmory.BDATournament]: " + message);
                        BDACompetitionMode.Instance.competitionStatus.Add(message);
                        for (int roundIndex = 0; roundIndex < numberOfRounds; ++roundIndex)
                        {
                            teamsIndex.Shuffle();
                            int teamsThisHeat = teamsPerHeat;
                            int count = 0;
                            var selectedTeams = teamsIndex.Take(teamsThisHeat).ToList();
                            var selectedCraft = SelectTeamCraft(selectedTeams, vesselsPerTeam, fullTeams);
                            rounds.Add(rounds.Count, []);
                            int heatIndex = 0;
                            while (selectedTeams.Count > 0)
                            {
                                rounds[roundIndex].Add(rounds[roundIndex].Count, new CircularSpawnConfig(
                                    BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
                                    BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                                    BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                                    BDArmorySettings.VESSEL_SPAWN_ALTITUDE_,
                                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                                    BDArmorySettings.VESSEL_SPAWN_INTRATEAM_DISTANCE,
                                    BDArmorySettings.VESSEL_SPAWN_REF_HEADING,
                                    true, // Kill everything first.
                                    BDArmorySettings.VESSEL_SPAWN_REASSIGN_TEAMS, // Assign teams.
                                    numberOfTeams, // Number of teams indicator.
                                    null, //selectedCraft.Select(c => c.Count).ToList(), // Not used here.
                                    selectedCraft, // List of lists of vessels. For splitting specific vessels into specific teams.
                                    null, // No folder, we're going to specify the craft files.
                                    null // No list of craft files, we've specified them directly in selectedCraft.
                                ));
                                count += teamsThisHeat;
                                teamsThisHeat = heatIndex++ < fullHeatCount ? teamsPerHeat : teamsPerHeat - 1; // Take one less for the remaining heats to distribute the deficit of teams.
                                selectedTeams = teamsIndex.Skip(count).Take(teamsThisHeat).ToList();
                                selectedCraft = SelectTeamCraft(selectedTeams, vesselsPerTeam, fullTeams);
                            }
                        }
                        break;
                    }
                case TournamentStyle.TemplateRNG: // RNG with spawn templates
                    {
                        var spawnTemplate = CustomTemplateSpawning.customSpawnConfig;
                        if (string.IsNullOrEmpty(spawnTemplate.name))
                        {
                            message = $"No template selected. Unable to generated template RNG tournament.";
                            Debug.Log("[BDArmory.BDATournament]: " + message);
                            BDACompetitionMode.Instance.competitionStatus.Add(message);
                            return false;
                        }
                        teamsPerHeat = Mathf.Min(teamsPerHeat, spawnTemplate.customVesselSpawnConfigs.Count);
                        this.teamsPerHeat = teamsPerHeat;
                        fullHeatCount = teamFiles.Count / teamsPerHeat;
                        vesselsPerTeam = Mathf.Min(vesselsPerTeam > 1 ? vesselsPerTeam : int.MaxValue, spawnTemplate.customVesselSpawnConfigs.Select(x => x.Count).Max());
                        message = $"Generating {numberOfRounds} randomised templated rounds for tournament {tournamentID} for {teamCount} teams in AutoSpawn{(folder == "" ? "" : "/" + folder)}, each with up to {teamsPerHeat} teams per heat.";
                        Debug.Log("[BDArmory.BDATournament]: " + message);
                        BDACompetitionMode.Instance.competitionStatus.Add(message);
                        List<int> templateSpawnPointOrder = Enumerable.Range(0, spawnTemplate.customVesselSpawnConfigs.Count).ToList();
                        for (int roundIndex = 0; roundIndex < numberOfRounds; ++roundIndex)
                        {
                            teamsIndex.Shuffle();
                            int teamsThisHeat = teamsPerHeat;
                            int count = 0;
                            var selectedTeams = teamsIndex.Take(teamsThisHeat).ToList();
                            templateSpawnPointOrder.Shuffle();
                            var selectedCraft = SelectTeamCraft(
                                selectedTeams,
                                templateSpawnPointOrder.Select(i => Mathf.Min(vesselsPerTeam, spawnTemplate.customVesselSpawnConfigs[i].Count)).ToList(),
                                fullTeams);
                            rounds.Add(rounds.Count, []);
                            int heatIndex = 0;
                            while (selectedTeams.Count > 0)
                            {
                                rounds[roundIndex].Add(
                                    rounds[roundIndex].Count,
                                    new CustomSpawnConfig(CustomTemplateSpawning.customSpawnConfig)
                                    {
                                        altitude = BDArmorySettings.VESSEL_SPAWN_ALTITUDE_,
                                        killEverythingFirst = true,
                                        assignTeams = BDArmorySettings.VESSEL_SPAWN_REASSIGN_TEAMS,
                                        teamsSpecific = selectedCraft,
                                        numberOfTeams = numberOfTeams == -1 ? -1 : 1 // Flag the number of teams as per file / per folder for sourcing craft files.
                                    });
                                count += teamsThisHeat;
                                teamsThisHeat = heatIndex++ < fullHeatCount ? teamsPerHeat : teamsPerHeat - 1; // Take one less for the remaining heats to distribute the deficit of teams.
                                selectedTeams = teamsIndex.Skip(count).Take(teamsThisHeat).ToList();
                                templateSpawnPointOrder.Shuffle();
                                selectedCraft = SelectTeamCraft(
                                    selectedTeams,
                                    templateSpawnPointOrder.Select(i => Mathf.Min(vesselsPerTeam, spawnTemplate.customVesselSpawnConfigs[i].Count)).ToList(),
                                    fullTeams);
                            }
                        }
                        break;
                    }
                case TournamentStyle.nCk: // N-choose-K
                    {
                        var nCr = N_Choose_K(teamCount, teamsPerHeat);
                        message = $"Generating a round-robin style tournament for {teamCount} teams in AutoSpawn{(folder == "" ? "" : "/" + folder)} with {teamsPerHeat} teams per heat and {numberOfRounds} rounds. This requires {numberOfRounds * nCr} heats.";
                        Debug.Log($"[BDArmory.BDATournament]: " + message);
                        BDACompetitionMode.Instance.competitionStatus.Add(message);
                        // Generate all combinations of teams for a round.
                        var combinations = Combinations(teamCount, teamsPerHeat);
                        // Populate the rounds.
                        for (int roundIndex = 0; roundIndex < numberOfRounds; ++roundIndex)
                        {
                            var heatList = new List<CircularSpawnConfig>();
                            foreach (var combination in combinations)
                            {
                                var selectedCraft = SelectTeamCraft(combination.Select(i => teamsIndex[i]).ToList(), vesselsPerTeam, fullTeams); // Vessel selection for a team can vary between rounds if the number of vessels in a team doesn't match the vesselsPerTeam parameter.
                                heatList.Add(new CircularSpawnConfig(
                                    BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
                                    BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                                    BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                                    BDArmorySettings.VESSEL_SPAWN_ALTITUDE_,
                                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                                    BDArmorySettings.VESSEL_SPAWN_INTRATEAM_DISTANCE,
                                    BDArmorySettings.VESSEL_SPAWN_REF_HEADING,
                                    true, // Kill everything first.
                                    BDArmorySettings.VESSEL_SPAWN_REASSIGN_TEAMS, // Assign teams.
                                    numberOfTeams, // Number of teams indicator.
                                    null, //selectedCraft.Select(c => c.Count).ToList(), // Not used here.
                                    selectedCraft, // List of lists of vessels. For splitting specific vessels into specific teams.
                                    null, // No folder, we're going to specify the craft files.
                                    null // No list of craft files, we've specified them directly in selectedCraft.
                                ));
                            }
                            heatList.Shuffle(); // Randomise the playing order within each round.
                            rounds.Add(roundIndex, heatList.Select((heat, index) => new KeyValuePair<int, SpawnConfig>(index, heat)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
                        }
                        break;
                    }
                case TournamentStyle.Gauntlet: // Gauntlet
                    {
                        // Gauntlet is like N-choose-2K except that it's selecting K teams from the main folder and K teams from the opponents (e.g., 2v2, 3v3, 2v3, (2v2)v(2v2), (2v3)v(3v2), etc.)
                        #region Opponent config
                        var opponentFolder = Path.Combine("AutoSpawn", BDArmorySettings.VESSEL_SPAWN_GAUNTLET_OPPONENTS_FILES_LOCATION);
                        var opponentAbsFolder = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, opponentFolder));
                        if (!Directory.Exists(opponentAbsFolder))
                        {
                            message = "Opponents folder (" + opponentFolder + ") containing craft files or team folders does not exist.";
                            BDACompetitionMode.Instance.competitionStatus.Add(message);
                            Debug.Log("[BDArmory.BDATournament]: " + message);
                            return false;
                        }
                        var opponentTeamDirs = Directory.GetDirectories(opponentAbsFolder);
                        List<string> opponentCraftFiles;
                        if (opponentTeamDirs.Length < 1) // Make teams from each vessel in the opponents folder.
                        {
                            opponentCraftFiles = Directory.GetFiles(opponentAbsFolder, "*.craft").ToList();
                            opponentTeamFiles = opponentCraftFiles.Select(f => new List<string> { f }).ToList();
                        }
                        else
                        {
                            opponentTeamFiles = new List<List<string>>();
                            foreach (var teamDir in opponentTeamDirs)
                            {
                                var currentTeamFiles = Directory.GetFiles(teamDir, "*.craft").ToList();
                                if (currentTeamFiles.Count > 0)
                                    opponentTeamFiles.Add(currentTeamFiles);
                            }
                            foreach (var team in opponentTeamFiles)
                                team.Shuffle();
                            opponentCraftFiles = opponentTeamFiles.SelectMany(v => v).ToList();
                        }
                        if (opponentTeamFiles.Count < 1)
                        {
                            message = $"Insufficient {(opponentTeamDirs.Length < 1 ? "craft files" : "folders")} in '{opponentFolder}' to generate a gauntlet tournament.";
                            if (BDACompetitionMode.Instance) BDACompetitionMode.Instance.competitionStatus.Add(message);
                            Debug.Log("[BDArmory.BDATournament]: " + message);
                            return false;
                        }
                        var opponentTeamCount = opponentTeamFiles.Count;
                        var opponentTeamsIndex = Enumerable.Range(0, opponentTeamCount).ToList();
                        #endregion

                        #region Tournament generation
                        var nCr = N_Choose_K(teamCount, teamsPerHeat) * N_Choose_K(opponentTeamCount, BDArmorySettings.TOURNAMENT_OPPONENT_TEAMS_PER_HEAT);
                        message = $"Generating a gauntlet style tournament for {teamCount} teams in AutoSpawn{(folder == "" ? "" : "/" + folder)} and {opponentTeamCount} opponent teams in {opponentFolder} with {BDArmorySettings.TOURNAMENT_OPPONENT_TEAMS_PER_HEAT} teams per heat and {numberOfRounds} rounds. This requires {numberOfRounds * nCr} heats.";
                        BDACompetitionMode.Instance.competitionStatus.Add(message);
                        Debug.Log($"[BDArmory.BDATournament]: " + message);
                        // Generate all combinations of teams for a round.
                        var combinations = Combinations(teamCount, teamsPerHeat);
                        var opponentCombinations = Combinations(opponentTeamCount, BDArmorySettings.TOURNAMENT_OPPONENT_TEAMS_PER_HEAT);
                        // Populate the rounds.
                        for (int roundIndex = 0; roundIndex < numberOfRounds; ++roundIndex)
                        {
                            var heatList = new List<CircularSpawnConfig>();
                            foreach (var combination in combinations)
                            {
                                var selectedCraft = SelectTeamCraft(combination.Select(i => teamsIndex[i]).ToList(), vesselsPerTeam, fullTeams); // Vessel selection for a team can vary between rounds if the number of vessels in a team doesn't match the vesselsPerTeam parameter.
                                foreach (var opponentCombination in opponentCombinations)
                                {
                                    var selectedOpponentCraft = SelectTeamCraft(opponentCombination.Select(i => opponentTeamsIndex[i]).ToList(), BDArmorySettings.TOURNAMENT_OPPONENT_VESSELS_PER_TEAM, fullTeams, true); // Vessel selection for a team can vary between rounds if the number of vessels in a team doesn't match the vesselsPerTeam parameter.
                                    heatList.Add(new CircularSpawnConfig(
                                        BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
                                        BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                                        BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                                        BDArmorySettings.VESSEL_SPAWN_ALTITUDE_,
                                        BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                                        BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                                        BDArmorySettings.VESSEL_SPAWN_INTRATEAM_DISTANCE,
                                        BDArmorySettings.VESSEL_SPAWN_REF_HEADING,
                                        true, // Kill everything first.
                                        BDArmorySettings.VESSEL_SPAWN_REASSIGN_TEAMS, // Assign teams.
                                        numberOfTeams, // Number of teams indicator. (Should be -1 for gauntlets for now.)
                                        null, //selectedCraft.Select(c => c.Count).ToList(), // Not used here.
                                        selectedCraft.Concat(selectedOpponentCraft).ToList(), // List of lists of vessels. For splitting specific vessels into specific teams.
                                        null, // No folder, we're going to specify the craft files.
                                        null // No list of craft files, we've specified them directly in selectedCraft.
                                    ));
                                }
                            }
                            heatList.Shuffle(); // Randomise the playing order within each round.
                            rounds.Add(roundIndex, heatList.Select((heat, index) => new KeyValuePair<int, SpawnConfig>(index, heat)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
                        }
                        #endregion
                        break;
                    }
                default:
                    {
                        BDACompetitionMode.Instance.competitionStatus.Add($"Tournament style {tournamentStyle} not implemented yet for Teams.");
                        throw new ArgumentOutOfRangeException("tournamentStyle", $"Invalid tournament style {tournamentStyle} - not implemented.");
                    }
            }
            return true;
        }

        /// <summary>
        /// Generate a new ranked round for the tournament based on the current scores.
        /// Note: The scores should be computed before calling this.
        /// </summary>
        /// <returns>true on success, false otherwise</returns>
        public bool GenerateRankedRound()
        {
            if (rounds == null || rounds.Values.First() == null || rounds.Values.First().Values.First() == null)
            {
                Debug.LogWarning($"[BDArmory.BDATournament]: The initial round hasn't been set up yet. Unable to extend ranked rounds.");
                return false;
            }
            int roundIndex = rounds.Count;
            message = $"Generating ranked round {roundIndex} for tournament {tournamentID}.";
            BDACompetitionMode.Instance.competitionStatus.Add(message);
            Debug.Log($"[BDArmory.BDATournament]: {message}");
            switch (tournamentType)
            {
                case TournamentType.FFA:
                    {
                        var craftFiles = scores.GetRankedCraftFiles(); // NPCs are already filtered out from the overall scoring.
                        int vesselsPerHeat = this.vesselsPerHeat; // Convert from the flag to the correct number per heat.
                        vesselCount = craftFiles.Count;
                        int fullHeatCount;
                        switch (vesselsPerHeat)
                        {
                            case -1: // Auto
                                var autoVesselsPerHeat = OptimiseVesselsPerHeat(craftFiles.Count, npcsPerHeat);
                                vesselsPerHeat = autoVesselsPerHeat.Item1;
                                fullHeatCount = Mathf.CeilToInt(craftFiles.Count / vesselsPerHeat) - autoVesselsPerHeat.Item2;
                                break;
                            case 0: // Unlimited (all vessels in one heat).
                                vesselsPerHeat = craftFiles.Count;
                                fullHeatCount = 1;
                                break;
                            default:
                                vesselsPerHeat = Mathf.Clamp(Mathf.Max(1, vesselsPerHeat - npcsPerHeat), 1, craftFiles.Count);
                                fullHeatCount = craftFiles.Count / vesselsPerHeat;
                                break;
                        }
                        int vesselsThisHeat = vesselsPerHeat;
                        int count = 0;
                        List<string> selectedFiles = craftFiles.Take(vesselsThisHeat).ToList();
                        var circularSpawnConfigTemplate = rounds.Values.First().Values.First() as CircularSpawnConfig;
                        rounds.Add(roundIndex, []); // Extend the rounds by 1.
                        int heatIndex = 0;
                        while (selectedFiles.Count > 0)
                        {
                            if (npcsPerHeat > 0) // Add in some NPCs.
                            {
                                npcFiles.Shuffle();
                                selectedFiles.AddRange(Enumerable.Repeat(npcFiles, Mathf.CeilToInt((float)npcsPerHeat / (float)npcFiles.Count)).SelectMany(x => x).Take(npcsPerHeat));
                            }
                            rounds[roundIndex].Add(rounds[roundIndex].Count, new CircularSpawnConfig(circularSpawnConfigTemplate)
                            {
                                craftFiles = selectedFiles // Set the craft file list to the currently selected ones.
                            }); // Add a copy of the template to the heats.
                            count += vesselsThisHeat;
                            vesselsThisHeat = heatIndex++ < fullHeatCount ? vesselsPerHeat : vesselsPerHeat - 1; // Take one less for the remaining heats to distribute the deficit of craft files.
                            selectedFiles = craftFiles.Skip(count).Take(vesselsThisHeat).ToList();
                        }
                        break;
                    }
                case TournamentType.Teams:
                    {
                        switch (tournamentStyle)
                        {
                            case TournamentStyle.RNG:
                                {
                                    int fullHeatCount = teamFiles.Count / teamsPerHeat;
                                    var teamsIndex = scores.GetRankedTeams(teamFiles);
                                    int teamsThisHeat = teamsPerHeat;
                                    int count = 0;
                                    var selectedTeams = teamsIndex.Take(teamsThisHeat).ToList();
                                    var selectedCraft = SelectTeamCraft(selectedTeams, vesselsPerTeam, fullTeams);
                                    var circularSpawnConfigTemplate = rounds.Values.First().Values.First() as CircularSpawnConfig;
                                    rounds.Add(roundIndex, []); // Extend the rounds by 1.
                                    int heatIndex = 0;
                                    while (selectedTeams.Count > 0)
                                    {
                                        rounds[roundIndex].Add(rounds[roundIndex].Count, new CircularSpawnConfig(circularSpawnConfigTemplate)
                                        {
                                            teamsSpecific = selectedCraft
                                        }); // Add a copy of the template to the heats, but use the new set of vessels.
                                        count += teamsThisHeat;
                                        teamsThisHeat = heatIndex++ < fullHeatCount ? teamsPerHeat : teamsPerHeat - 1; // Take one less for the remaining heats to distribute the deficit of teams.
                                        selectedTeams = teamsIndex.Skip(count).Take(teamsThisHeat).ToList();
                                        selectedCraft = SelectTeamCraft(selectedTeams, vesselsPerTeam, fullTeams);
                                    }
                                    break;
                                }
                            case TournamentStyle.TemplateRNG:
                                {
                                    var spawnConfig = CustomTemplateSpawning.customSpawnConfig;
                                    int fullHeatCount = teamFiles.Count / teamsPerHeat;
                                    var teamsIndex = scores.GetRankedTeams(teamFiles);
                                    int teamsThisHeat = teamsPerHeat;
                                    int count = 0;
                                    List<int> templateSpawnPointOrder = Enumerable.Range(0, spawnConfig.customVesselSpawnConfigs.Count).ToList();
                                    templateSpawnPointOrder.Shuffle();
                                    var selectedTeams = teamsIndex.Take(teamsThisHeat).ToList();
                                    var selectedCraft = SelectTeamCraft(
                                        selectedTeams,
                                        templateSpawnPointOrder.Select(i => Mathf.Min(vesselsPerTeam, spawnConfig.customVesselSpawnConfigs[i].Count)).ToList(),
                                        fullTeams);
                                    var customSpawnConfigTemplate = rounds.Values.First().Values.First() as CustomSpawnConfig;
                                    rounds.Add(roundIndex, []); // Extend the rounds by 1.
                                    int heatIndex = 0;
                                    while (selectedTeams.Count > 0)
                                    {
                                        rounds[roundIndex].Add(rounds[roundIndex].Count, new CustomSpawnConfig(customSpawnConfigTemplate)
                                        {
                                            teamsSpecific = selectedCraft
                                        }); // Add a copy of the template to the heats, but use the new set of vessels.
                                        count += teamsThisHeat;
                                        teamsThisHeat = heatIndex++ < fullHeatCount ? teamsPerHeat : teamsPerHeat - 1; // Take one less for the remaining heats to distribute the deficit of teams.
                                        selectedTeams = teamsIndex.Skip(count).Take(teamsThisHeat).ToList();
                                        templateSpawnPointOrder.Shuffle();
                                        selectedCraft = SelectTeamCraft(
                                            selectedTeams,
                                            templateSpawnPointOrder.Select(i => Mathf.Min(vesselsPerTeam, spawnConfig.customVesselSpawnConfigs[i].Count)).ToList(),
                                            fullTeams);
                                    }
                                    break;
                                }
                            default:
                                Debug.LogError($"[BDArmory.BDATournament]: Invalid tournament style for ranked tournaments.");
                                break;
                        }
                        break;
                    }
                default:
                    Debug.LogError($"[BDArmory.BDATournament]: Invalid tournament type.");
                    return false;
            }
            return true;
        }

        List<List<string>> SelectTeamCraft(List<int> selectedTeams, List<int> vesselsPerTeam, bool fullTeams)
        {
            if (selectedTeams.Count != vesselsPerTeam.Count) return [];

            List<List<string>> selectedCraft = [];
            for (int i = 0; i < selectedTeams.Count; ++i)
                selectedCraft.Add(SelectTeamCraft([selectedTeams[i]], vesselsPerTeam[i], fullTeams, false).First());
            return selectedCraft;
        }

        List<List<string>> SelectTeamCraft(List<int> selectedTeams, int vesselsPerTeam, bool fullTeams, bool opponentQueue = false)
        {
            if (vesselsPerTeam == 0) // Each team consist of all the craft in the team.
            {
                return selectedTeams.Select(index => (opponentQueue ? opponentTeamFiles : teamFiles)[index].ToList()).ToList();
            }

            // Get the right spawn queues and file lists.
            var spawnQueues = opponentQueue ? opponentTeamSpawnQueues : teamSpawnQueues;
            var teams = opponentQueue ? opponentTeamFiles : teamFiles;

            if (spawnQueues.Count == 0) // Set up the spawn queues if needed.
            {
                foreach (var teamIndex in teams)
                    spawnQueues.Add(new Queue<string>());
            }

            List<List<string>> selectedCraft = [];
            List<string> currentTeam = [];
            foreach (var index in selectedTeams)
            {
                if (spawnQueues[index].Count < vesselsPerTeam)
                {
                    // First append craft files that aren't already in the queue.
                    var craftToAdd = teams[index].Where(c => !spawnQueues[index].Contains(c)).ToList();
                    craftToAdd.Shuffle();
                    foreach (var craft in craftToAdd)
                    {
                        spawnQueues[index].Enqueue(craft);
                    }
                    if (fullTeams)
                    {
                        // Then continue to fill the queue with craft files until we have enough.
                        while (spawnQueues[index].Count < vesselsPerTeam)
                        {
                            craftToAdd = teams[index].ToList();
                            craftToAdd.Shuffle();
                            foreach (var craft in craftToAdd)
                            {
                                spawnQueues[index].Enqueue(craft);
                            }
                        }
                    }
                }
                currentTeam.Clear();
                while (currentTeam.Count < vesselsPerTeam && spawnQueues[index].Count > 0)
                {
                    currentTeam.Add(spawnQueues[index].Dequeue());
                }
                selectedCraft.Add(currentTeam.ToList());
            }
            return selectedCraft;
        }

        Tuple<int, int> OptimiseVesselsPerHeat(int count, int extras)
        {
            if (BDArmorySettings.TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE.y < BDArmorySettings.TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE.x) BDArmorySettings.TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE.y = BDArmorySettings.TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE.x;
            var limits = new Vector2Int(Math.Max(1, BDArmorySettings.TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE.x - extras), Math.Max(1, BDArmorySettings.TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE.y - extras));
            var options = count > limits.y && count < 2 * limits.x - 1 ?
                Enumerable.Range(limits.y / 2, limits.y - limits.y / 2 + 1).Reverse().ToList() // Tweak the range when just over the upper limit to give more balanced heats.
                : Enumerable.Range(limits.x, limits.y - limits.x + 1).Reverse().ToList();
            foreach (var val in options)
            {
                if (count % val == 0)
                    return new Tuple<int, int>(val, 0);
            }
            var result = OptimiseVesselsPerHeat(count + 1, extras);
            return new Tuple<int, int>(result.Item1, result.Item2 + 1);
        }

        public bool SaveState(string stateFile)
        {
            if (rounds == null) return false; // Nothing to save.
            try
            {
                // Encode the scores into the _scores field.
                if (scores != null) _scores = JsonUtility.ToJson(scores.PrepareSerialization());
                else _scores = null;

                // Encode the rounds into the _rounds field.
                if (rounds != null)
                {
                    _heats = [];
                    foreach (var round in rounds.Keys)
                        foreach (var heat in rounds[round].Keys)
                            if (tournamentStyle == TournamentStyle.TemplateRNG)
                            {
                                _heats.Add(JsonUtility.ToJson(new TemplateRoundConfig(round, heat, completed.ContainsKey(round) && completed[round].Contains(heat), rounds[round][heat] as CustomSpawnConfig)));
                            }
                            else
                            {
                                _heats.Add(JsonUtility.ToJson(new RoundConfig(round, heat, completed.ContainsKey(round) && completed[round].Contains(heat), rounds[round][heat] as CircularSpawnConfig)));
                            }
                }
                else _heats = null;

                // Encode team files (for team competitions).
                _teamFiles = teamFiles != null ? teamFiles.Select(t => JsonUtility.ToJson(new StringList { ls = t })).ToList() : null;

                if (!Directory.GetParent(stateFile).Exists)
                { Directory.GetParent(stateFile).Create(); }
                try // Write the state with gzip compression to reduce bloat.
                {
                    using FileStream fileStream = File.Create(stateFile);
                    using GZipStream gzStream = new(fileStream, CompressionMode.Compress);
                    var stateBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(this));
                    gzStream.Write(stateBytes, 0, stateBytes.Length);
                }
                catch // Revert to plain UTF8.
                {
                    File.WriteAllText(stateFile, JsonUtility.ToJson(this));
                }
                Debug.Log($"[BDArmory.BDATournament]: Tournament state saved to {stateFile}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[BDArmory.BDATournament]: Exception thrown in SaveState: " + e.Message + "\n" + e.StackTrace);
                return false;
            }
        }

        public bool LoadState(string stateFile)
        {
            try
            {
                if (!(File.Exists(stateFile) || File.Exists(stateFile))) return false;
                TournamentState data;
                try // Try with gzip compression.
                {
                    using FileStream fileStream = File.OpenRead(stateFile);
                    using GZipStream gZipStream = new(fileStream, CompressionMode.Decompress);
                    using StreamReader streamReader = new(gZipStream, Encoding.UTF8);
                    data = JsonUtility.FromJson<TournamentState>(streamReader.ReadToEnd());
                }
                catch // Revert to plain ASCII text.
                {
                    data = JsonUtility.FromJson<TournamentState>(File.ReadAllText(stateFile));
                }
                tournamentID = data.tournamentID;
                savegame = data.savegame;
                vesselCount = data.vesselCount;
                teamCount = data.teamCount;
                teamsPerHeat = data.teamsPerHeat;
                vesselsPerTeam = data.vesselsPerTeam;
                fullTeams = data.fullTeams;
                vesselsPerHeat = data.vesselsPerHeat;
                numberOfRounds = data.numberOfRounds;
                tournamentType = data.tournamentType;
                tournamentStyle = data.tournamentStyle;
                tournamentRoundType = data.tournamentRoundType;
                npcsPerHeat = data.npcsPerHeat;
                npcFiles = data.npcFiles.ToList();
                _heats = data._heats;
                rounds = [];
                completed = [];
                try // Deserialize team files
                {
                    _teamFiles = data._teamFiles;
                    teamFiles = _teamFiles != null ? _teamFiles.Select(t => JsonUtility.FromJson<StringList>(t).ls).ToList() : null;
                }
                catch (Exception e_scores) { Debug.LogError($"[BDArmory.BDATournament]: Failed to deserialize the team files: {e_scores.Message}\n{e_scores.StackTrace}"); }
                try // Deserialize tournament scores
                {
                    _scores = data._scores;
                    scores = JsonUtility.FromJson<TournamentScores>(_scores);
                    if (scores != null) scores.PostDeserialization();
                    else scores = new TournamentScores();
                    scores.ComputeScores();
                }
                catch (Exception e_scores) { Debug.LogError($"[BDArmory.BDATournament]: Failed to deserialize the tournament scores: {e_scores.Message}\n{e_scores.StackTrace}"); }
                try // Deserialize rounds / heats
                {
                    if (_heats != null)
                    {
                        if (tournamentStyle == TournamentStyle.TemplateRNG)
                        {
                            bool templateLoaded = false;
                            foreach (var serializedRound in _heats)
                            {
                                var roundConfig = JsonUtility.FromJson<TemplateRoundConfig>(serializedRound);
                                if (roundConfig == null) { Debug.LogWarning($"[BDArmory.BDATournament]: Failed to decode a valid round config."); continue; }
                                if (!templateLoaded)
                                {
                                    CustomTemplateSpawning.LoadTemplate(roundConfig.name, fromDisk: true);
                                    if (string.IsNullOrEmpty(CustomTemplateSpawning.customSpawnConfig.name))
                                    {
                                        message = $"Unable to load template tournament as the template {roundConfig.name} does not exist.";
                                        if (BDACompetitionMode.Instance) BDACompetitionMode.Instance.competitionStatus.Add(message);
                                        Debug.LogWarning($"[BDArmory.BDATournament]: " + message);
                                        return false;
                                    }
                                }
                                if (!serializedRound.Contains("worldIndex")) roundConfig.worldIndex = 1; // Default old tournament states to be on Kerbin.
                                roundConfig.DeserializeTeams();
                                if (!rounds.ContainsKey(roundConfig.round)) rounds.Add(roundConfig.round, []);
                                rounds[roundConfig.round].Add(roundConfig.heat, new CustomSpawnConfig(CustomTemplateSpawning.customSpawnConfig)
                                {
                                    altitude = roundConfig.altitude,
                                    killEverythingFirst = true,
                                    assignTeams = roundConfig.assignTeams,
                                    teamsSpecific = roundConfig.teamsSpecific,
                                    numberOfTeams = roundConfig.numberOfTeams // Flag the number of teams as per file / per folder for sourcing craft files.
                                });
                                if (roundConfig.completed)
                                {
                                    if (!completed.ContainsKey(roundConfig.round)) completed.Add(roundConfig.round, new HashSet<int>());
                                    completed[roundConfig.round].Add(roundConfig.heat);
                                }
                            }
                        }
                        else
                        {
                            foreach (var serializedRound in _heats)
                            {
                                var roundConfig = JsonUtility.FromJson<RoundConfig>(serializedRound);
                                if (roundConfig == null) { Debug.LogWarning($"[BDArmory.BDATournament]: Failed to decode a valid round config."); continue; }
                                if (!serializedRound.Contains("worldIndex")) roundConfig.worldIndex = 1; // Default old tournament states to be on Kerbin.
                                roundConfig.DeserializeTeams();
                                if (!rounds.ContainsKey(roundConfig.round)) rounds.Add(roundConfig.round, []);
                                rounds[roundConfig.round].Add(roundConfig.heat, new CircularSpawnConfig(
                                    roundConfig.worldIndex,
                                    roundConfig.latitude,
                                    roundConfig.longitude,
                                    roundConfig.altitude,
                                    roundConfig.distance,
                                    roundConfig.absDistanceOrFactor,
                                    roundConfig.intraTeamSeparation,
                                    roundConfig.refHeading,
                                    roundConfig.killEverythingFirst,
                                    roundConfig.assignTeams,
                                    roundConfig.numberOfTeams,
                                    roundConfig.teamCounts == null || roundConfig.teamCounts.Count == 0 ? null : roundConfig.teamCounts,
                                    roundConfig.teamsSpecific == null || roundConfig.teamsSpecific.Count == 0 ? null : roundConfig.teamsSpecific,
                                    roundConfig.folder,
                                    roundConfig.craftFiles
                                ));
                                if (roundConfig.completed)
                                {
                                    if (!completed.ContainsKey(roundConfig.round)) completed.Add(roundConfig.round, new HashSet<int>());
                                    completed[roundConfig.round].Add(roundConfig.heat);
                                }
                            }
                        }
                    }
                }
                catch (Exception e_rounds) { Debug.LogError($"[BDArmory.BDATournament]: Failed to deserialize the tournament rounds: {e_rounds.Message}\n{e_rounds.StackTrace}"); }
                try // Deserialize deconfliction dictionaries
                {
                    _deconflictionURLs = data._deconflictionURLs;
                    _deconflictionSuffixes = data._deconflictionSuffixes;
                }
                catch (Exception e_deconfliction) { Debug.LogError($"[BDArmory.BDATournament]: Failed to deserialize the vessel naming deconfliction data: {e_deconfliction.Message}\n{e_deconfliction.StackTrace}"); }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[BDArmory.BDATournament]: " + e.Message);
                return false;
            }
        }

        public void StoreDeconflictionData()
        {
            _deconflictionURLs = [.. SpawnUtils.SpawnedVesselURLs.Select(kvp => JsonUtility.ToJson(new StringList { ls = [kvp.Key, kvp.Value] }))];
            _deconflictionSuffixes = [.. SpawnUtils.DeconflictionSuffixes.Select(kvp => JsonUtility.ToJson(new StringList { ls = [kvp.Key, kvp.Value] }))];
        }

        public void RestoreDeconflictionData()
        {
            SpawnUtils.SpawnedVesselURLs = _deconflictionURLs.Select(json => JsonUtility.FromJson<StringList>(json).ls).ToDictionary(ls => ls[0], ls => ls[1]);
            SpawnUtils.DeconflictionSuffixes = _deconflictionSuffixes.Select(json => JsonUtility.FromJson<StringList>(json).ls).ToDictionary(ls => ls[0], ls => ls[1]);
        }

        #region Helper functions
        /// <summary>
        /// Calculate N-choose-K.
        /// </summary>
        /// <param name="n">N</param>
        /// <param name="k">K</param>
        /// <returns>The number of ways of choosing K unique items of a collection of N.</returns>
        public static int N_Choose_K(int n, int k)
        {
            k = Mathf.Clamp(k, 0, n);
            k = Math.Min(n, k);
            var numer = Enumerable.Range(n - k + 1, k).Aggregate(1, (acc, val) => acc * val);
            var denom = Enumerable.Range(1, k).Aggregate(1, (acc, val) => acc * val);
            return Mathf.RoundToInt(numer / denom);
        }
        /// <summary>
        /// Generate all combinations of N-choose-K.
        /// </summary>
        /// <param name="n">N</param>
        /// <param name="k">K</param>
        /// <returns>List of list of unique combinations of K indices from 0 to N-1.</returns>
        public static List<List<int>> Combinations(int n, int k)
        {
            k = Mathf.Clamp(k, 0, n);
            var combinations = new List<List<int>>();
            var temp = new List<int>();
            GenerateCombinations(ref combinations, temp, 0, n, k);
            return combinations;
        }
        /// <summary>
        /// Recursively generate all combinations of N-choose-K.
        /// Helper function.
        /// </summary>
        /// <param name="combinations">The combinations are accumulated in this list of lists.</param>
        /// <param name="temp">Temporary buffer containing current chosen values.</param>
        /// <param name="i">Current choice</param>
        /// <param name="n">N</param>
        /// <param name="k">K remaining to choose</param>
        static void GenerateCombinations(ref List<List<int>> combinations, List<int> temp, int i, int n, int k)
        {
            if (k == 0)
            {
                combinations.Add(temp.ToList()); // Take a copy otherwise C# disposes of it.
                return;
            }
            for (int j = i; j < n; ++j)
            {
                temp.Add(j);
                GenerateCombinations(ref combinations, temp, j + 1, n, k - 1);
                temp.RemoveAt(temp.Count - 1);
            }
        }
        #endregion
    }

    public enum TournamentStatus { Stopped, Running, Waiting, Completed };

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class BDATournament : MonoBehaviour
    {
        public static BDATournament Instance;

        #region Flags and Variables
        TournamentState tournamentState;
        string stateFile;
        string message;
        private Coroutine runTournamentCoroutine;
        public TournamentStatus tournamentStatus = TournamentStatus.Stopped;
        public uint tournamentID = 0;
        public TournamentType tournamentType = TournamentType.FFA;
        public int numberOfRounds = 0;
        public int currentRound = 0;
        public int numberOfHeats = 0;
        public int currentHeat = 0;
        public int heatsRemaining = 0;
        public int vesselCount = 0;
        public int teamCount = 0;
        public int teamsPerHeat = 0;
        public int vesselsPerTeam = 0;
        public bool fullTeams = false;
        bool competitionStarted = false;
        public bool warpingInProgress = false;
        VesselSpawnerBase spawnerBase;
        #endregion

        void Awake()
        {
            if (Instance)
                Destroy(Instance);
            Instance = this;
            stateFile = TournamentState.defaultStateFile;
        }

        void Start()
        {
            BDArmorySettings.LAST_USED_SAVEGAME = HighLogic.SaveFolder;
            StartCoroutine(LoadStateWhenReady());
        }

        IEnumerator LoadStateWhenReady()
        {
            while (BDACompetitionMode.Instance == null)
                yield return null;
            LoadTournamentState(); // Load the last state.
        }

        void OnDestroy()
        {
            StopTournament(); // Stop any running tournament.
            SaveTournamentState(); // Save the last state.
        }

        // Load tournament state from disk
        bool LoadTournamentState(string stateFile = "")
        {
            if (stateFile != "") this.stateFile = stateFile;
            tournamentState = new TournamentState();
            if (tournamentState.LoadState(this.stateFile))
            {
                message = "Tournament state loaded from " + this.stateFile;
                tournamentID = tournamentState.tournamentID;
                tournamentType = tournamentState.tournamentType;
                vesselCount = tournamentState.vesselCount;
                teamCount = tournamentState.teamCount;
                teamsPerHeat = tournamentState.teamsPerHeat;
                vesselsPerTeam = tournamentState.vesselsPerTeam;
                fullTeams = tournamentState.fullTeams;
                numberOfRounds = tournamentState.tournamentRoundType == TournamentRoundType.Ranked ? BDArmorySettings.TOURNAMENT_ROUNDS + 1 : tournamentState.rounds.Count;
                numberOfHeats = numberOfRounds > 0 ? tournamentState.rounds[0].Count : 0;
                heatsRemaining = tournamentState.rounds.Select(r => r.Value.Count).Sum() - tournamentState.completed.Select(c => c.Value.Count).Sum() + (tournamentState.tournamentRoundType == TournamentRoundType.Ranked ? (BDArmorySettings.TOURNAMENT_ROUNDS + 1 - tournamentState.rounds.Count) * tournamentState.rounds.First().Value.Count : 0);
            }
            else
                message = "Failed to load tournament state.";
            Debug.Log("[BDArmory.BDATournament]: " + message);
            // if (BDACompetitionMode.Instance != null)
            //     BDACompetitionMode.Instance.competitionStatus.Add(message);
            tournamentStatus = heatsRemaining > 0 ? TournamentStatus.Stopped : TournamentStatus.Completed;
            return true;
        }

        // Save tournament state to disk
        bool SaveTournamentState(bool backup = false)
        {
            var saveTo = stateFile;
            if (backup)
            {
                var saveToDir = Path.GetDirectoryName(TournamentState.defaultStateFile);
                saveToDir = Path.Combine(saveToDir, "Unfinished Tournaments");
                if (!Directory.Exists(saveToDir)) Directory.CreateDirectory(saveToDir);
                saveTo = Path.ChangeExtension(Path.Combine(saveToDir, Path.GetFileName(stateFile)), $".state-{tournamentID}");
            }
            return tournamentState.SaveState(saveTo);
        }

        /// <summary>
        /// Setup a tournament.
        /// </summary>
        /// <param name="folder">The base folder where the craft files or teams are located</param>
        /// <param name="rounds">The number of rounds to in the tournament.</param>
        /// <param name="vesselsPerHeat">The number of vessels per heat for FFA tournaments.</param>
        /// <param name="teamsPerHeat">The number of teams per heat for teams tournaments.</param>
        /// <param name="vesselsPerTeam">The number of vessels per team for teams tournaments.</param>
        /// <param name="numberOfTeams">The number of teams: 0 for FFA, 1 for auto based on files/folders, >1 for splitting evenly.</param>
        /// <param name="tournamentStyle">The tournament style: RNG, N-choose-K, etc.</param>
        /// <param name="tournamentType">The tournament type: FFA, Teams, RankedFFA, RankedTeams, etc.</param>
        /// <param name="stateFile">The tournament statefile to use (if different from the usual one).</param>
        public void SetupTournament(string folder, int rounds, int vesselsPerHeat = 0, int npcsPerHeat = 0, int teamsPerHeat = 0, int vesselsPerTeam = 0, int numberOfTeams = 0, TournamentStyle tournamentStyle = TournamentStyle.RNG, TournamentRoundType tournamentRoundType = TournamentRoundType.Shuffled, string stateFile = "")
        {
            if (tournamentState != null && tournamentState.rounds != null && tournamentState.rounds.Count > 0)
            {
                heatsRemaining = tournamentState.rounds.Select(r => r.Value.Count).Sum() - tournamentState.completed.Select(c => c.Value.Count).Sum() + (tournamentState.tournamentRoundType == TournamentRoundType.Ranked ? (BDArmorySettings.TOURNAMENT_ROUNDS + 1 - tournamentState.rounds.Count) * tournamentState.rounds.First().Value.Count : 0);
                if (heatsRemaining > 0 && heatsRemaining < numberOfRounds * numberOfHeats) // Started, but incomplete tournament.
                {
                    SaveTournamentState(BDArmorySettings.TOURNAMENT_BACKUPS);
                }
            }
            if (stateFile != "") this.stateFile = stateFile;
            if (BDArmorySettings.WAYPOINTS_MODE && BDArmorySettings.WAYPOINTS_ONE_AT_A_TIME) vesselsPerHeat = 1; // Override vessels per heat.
            tournamentState = new TournamentState();
            if (numberOfTeams == 0) // FFA
            {
                if (!tournamentState.GenerateFFATournament(folder, rounds, vesselsPerHeat, npcsPerHeat, tournamentStyle, tournamentRoundType)) return;
            }
            else // Folders or random teams
            {
                if (!tournamentState.GenerateTeamsTournament(folder, rounds, teamsPerHeat, vesselsPerTeam, numberOfTeams, tournamentStyle, tournamentRoundType)) return;
            }
            tournamentID = tournamentState.tournamentID;
            tournamentType = tournamentState.tournamentType;
            vesselCount = tournamentState.vesselCount;
            teamCount = tournamentState.teamCount;
            this.teamsPerHeat = tournamentState.teamsPerHeat;
            this.vesselsPerTeam = tournamentState.vesselsPerTeam;
            fullTeams = tournamentState.fullTeams;
            numberOfRounds = tournamentState.tournamentRoundType == TournamentRoundType.Ranked ? BDArmorySettings.TOURNAMENT_ROUNDS + 1 : tournamentState.rounds.Count;
            numberOfHeats = numberOfRounds > 0 ? tournamentState.rounds[0].Count : 0;
            heatsRemaining = tournamentState.rounds.Select(r => r.Value.Count).Sum() - tournamentState.completed.Select(c => c.Value.Count).Sum() + (tournamentState.tournamentRoundType == TournamentRoundType.Ranked ? (BDArmorySettings.TOURNAMENT_ROUNDS + 1 - tournamentState.rounds.Count) * tournamentState.rounds.First().Value.Count : 0);
            tournamentStatus = heatsRemaining > 0 ? TournamentStatus.Stopped : TournamentStatus.Completed;
            tournamentState.scores.Reset();
            SaveTournamentState();
        }

        public void RunTournament()
        {
            tournamentState.savegame = HighLogic.SaveFolder;
            BDACompetitionMode.Instance.StopCompetition();
            SpawnUtils.CancelSpawning();
            if (runTournamentCoroutine != null)
                StopCoroutine(runTournamentCoroutine);
            runTournamentCoroutine = StartCoroutine(RunTournamentCoroutine());
            if (BDArmorySettings.AUTO_DISABLE_UI) SetGameUI(false);
            ScoreWindow.SetMode(ScoreWindow.Mode.Tournament, tournamentState.tournamentType == TournamentType.FFA ? Toggle.Off : Toggle.On);
        }

        public void StopTournament()
        {
            if (runTournamentCoroutine != null)
            {
                StopCoroutine(runTournamentCoroutine);
                runTournamentCoroutine = null;
            }
            tournamentStatus = heatsRemaining > 0 ? TournamentStatus.Stopped : TournamentStatus.Completed;
            if (BDArmorySettings.AUTO_DISABLE_UI) SetGameUI(true);
        }

        IEnumerator RunTournamentCoroutine()
        {
            bool firstRun = true; // Whether a heat has been run yet (particularly for loading partway through a tournament).
            yield return new WaitForFixedUpdate();
            int roundIndex = -1;
            spawnerBase = tournamentState.tournamentStyle == TournamentStyle.TemplateRNG ? CustomTemplateSpawning.Instance : CircularSpawning.Instance;
            while (++roundIndex < tournamentState.rounds.Count) // tournamentState.rounds can change during the loop, so we can't just use an iterator now.
            {
                if (BDArmorySettings.TOURNAMENT_TIMEWARP_BETWEEN_ROUNDS < 0)
                {
                    var spawnConfig = tournamentState.rounds[roundIndex][0];
                    var body = FlightGlobals.Bodies[spawnConfig.worldIndex];
                    if (!EarlyBird.IsDayTime(spawnConfig.latitude, spawnConfig.longitude, body, 10))
                    {
                        SpawnUtils.ShowSpawnPoint(spawnConfig.worldIndex, spawnConfig.latitude, spawnConfig.longitude, spawnConfig.altitude);
                        BDACompetitionMode.Instance.competitionStatus.Add($"Warping ahead to morning, then running the next round.");
                        yield return WarpAhead(EarlyBird.TimeToDaylight(
                            spawnConfig.latitude,
                            spawnConfig.longitude,
                            body,
                            10
                        )); // Warp to morning +10mins.
                    }
                }

                currentRound = roundIndex;
                foreach (var heatIndex in tournamentState.rounds[roundIndex].Keys)
                {
                    currentHeat = heatIndex;
                    if (tournamentState.completed.ContainsKey(roundIndex) && tournamentState.completed[roundIndex].Contains(heatIndex)) continue; // We've done that heat.

                    message = $"Running heat {heatIndex} of round {roundIndex} of tournament {tournamentState.tournamentID} ({heatsRemaining} heats remaining in the tournament).";
                    BDACompetitionMode.Instance.competitionStatus.Add(message);
                    Debug.Log("[BDArmory.BDATournament]: " + message);

                    if (firstRun) SpawnUtilsInstance.Instance.gunGameProgress.Clear(); // Clear gun-game progress.
                    int attempts = 0;
                    bool unrecoverable = false;
                    competitionStarted = false;
                    if (roundIndex == 0 && heatIndex == 0)
                        SpawnUtils.ResetVesselNamingDeconfliction(); // Start fresh with vessel naming deconfliction.
                    else
                        tournamentState.RestoreDeconflictionData(); // Restore the deconfliction data to the most recently used in the tournament (to avoid outside interference).
                    while (!competitionStarted && attempts++ < 3) // 3 attempts is plenty
                    {
                        SpawnUtils.ResetVesselNamingDeconfliction(fightersOnly: !fullTeams);
                        tournamentStatus = TournamentStatus.Running;
                        if (BDArmorySettings.WAYPOINTS_MODE)
                            yield return ExecuteWaypointHeat(roundIndex, heatIndex);
                        else
                            yield return ExecuteHeat(roundIndex, heatIndex, attempts == BDArmorySettings.TOURNAMENT_START_DESPITE_FAILURES_ON_ATTEMPT && BDArmorySettings.COMPETITION_START_DESPITE_FAILURES, firstRun); // On the third attempt, start despite failures if the option is set.
                        if (!competitionStarted)
                        {
                            switch (spawnerBase.spawnFailureReason)
                            {
                                case SpawnFailureReason.None: // Successful spawning, but competition failed to start for some reason.
                                    BDACompetitionMode.Instance.competitionStatus.Add("Failed to start heat due to " + BDACompetitionMode.Instance.competitionStartFailureReason + ", trying again.");
                                    break;
                                case SpawnFailureReason.VesselLostParts: // Recoverable spawning failure.
                                    BDACompetitionMode.Instance.competitionStatus.Add("Failed to start heat due to " + spawnerBase.spawnFailureReason + ", trying again with increased altitude.");
                                    if (tournamentState.rounds[roundIndex][heatIndex].altitude < 10) tournamentState.rounds[roundIndex][heatIndex].altitude = Math.Min(tournamentState.rounds[roundIndex][heatIndex].altitude + 3, 10); // Increase the spawning altitude for ground spawns and try again.
                                    break;
                                case SpawnFailureReason.TimedOut: // Recoverable spawning failure.
                                    BDACompetitionMode.Instance.competitionStatus.Add("Failed to start heat due to " + spawnerBase.spawnFailureReason + ", trying again.");
                                    break;
                                case SpawnFailureReason.NoTerrain: // Failed to find the terrain when ground spawning.
                                    BDACompetitionMode.Instance.competitionStatus.Add("Failed to start heat due to " + spawnerBase.spawnFailureReason + ", trying again.");
                                    attempts = Math.Max(attempts, 2); // Try only once more.
                                    break;
                                case SpawnFailureReason.DependencyIssues:
                                    message = $"Failed to start heat due to {spawnerBase.spawnFailureReason}, aborting. Make sure dependencies are installed and enabled, then revert to launch and try again.";
                                    BDACompetitionMode.Instance.competitionStatus.Add(message);
                                    Debug.LogWarning($"[BDArmory.BDATournament]: {message}");
                                    attempts = 3;
                                    unrecoverable = true;
                                    break;
                                default: // Spawning is unrecoverable.
                                    BDACompetitionMode.Instance.competitionStatus.Add("Failed to start heat due to " + spawnerBase.spawnFailureReason + ", aborting.");
                                    attempts = 3;
                                    unrecoverable = true;
                                    break;
                            }
                        }
                    }
                    if (!competitionStarted)
                    {
                        message = $"Failed to run heat {(unrecoverable ? "due to unrecoverable error" : $"after 3 spawning attempts")}, failure reasons: " + spawnerBase.spawnFailureReason + ", " + BDACompetitionMode.Instance.competitionStartFailureReason + ". Stopping tournament. Please fix the failure reason before continuing the tournament.";
                        Debug.Log("[BDArmory.BDATournament]: " + message);
                        BDACompetitionMode.Instance.competitionStatus.Add(message);
                        tournamentStatus = TournamentStatus.Stopped;
                        yield break;
                    }
                    firstRun = false;
                    tournamentState.StoreDeconflictionData(); // Update the deconfliction data from this heat.

                    // Register the heat as completed.
                    if (!tournamentState.completed.ContainsKey(roundIndex)) tournamentState.completed.Add(roundIndex, new HashSet<int>());
                    tournamentState.completed[roundIndex].Add(heatIndex);
                    tournamentState.scores.AddHeatScores(BDACompetitionMode.Instance.Scores); // Note: this is done after LogResults is called.
                    SaveTournamentState();
                    heatsRemaining = tournamentState.rounds.Select(r => r.Value.Count).Sum() - tournamentState.completed.Select(c => c.Value.Count).Sum() + (tournamentState.tournamentRoundType == TournamentRoundType.Ranked ? (BDArmorySettings.TOURNAMENT_ROUNDS - roundIndex) * tournamentState.rounds.First().Value.Count : 0);

                    if (TournamentAutoResume.Instance != null && TournamentAutoResume.Instance.CheckMemoryUsage()) yield break;

                    if (tournamentState.completed[roundIndex].Count < tournamentState.rounds[roundIndex].Count)
                    {
                        // Wait a bit for any user action
                        tournamentStatus = TournamentStatus.Waiting;
                        double startTime = Planetarium.GetUniversalTime();
                        while ((Planetarium.GetUniversalTime() - startTime) < BDArmorySettings.TOURNAMENT_DELAY_BETWEEN_HEATS)
                        {
                            BDACompetitionMode.Instance.competitionStatus.Add("Waiting " + (BDArmorySettings.TOURNAMENT_DELAY_BETWEEN_HEATS - (Planetarium.GetUniversalTime() - startTime)).ToString("0") + "s, then running the next heat.");
                            yield return new WaitForSeconds(1);
                        }
                    }
                }
                if (!firstRun)
                {
                    tournamentState.scores.ComputeScores();
                    message = "All heats in round " + roundIndex + " have been run.";
                    BDACompetitionMode.Instance.competitionStatus.Add(message);
                    Debug.Log("[BDArmory.BDATournament]: " + message);
                    LogScores(tournamentState.tournamentType == TournamentType.Teams);
                    if (BDArmorySettings.WAYPOINTS_MODE)
                    {
                        /* commented out until this is made functional
                        foreach (var tracer in WaypointFollowingStrategy.Ghosts) //clear and reset vessel ghosts each new Round
                        {
                            tracer.gameObject.SetActive(false);
                        }
                        WaypointFollowingStrategy.Ghosts.Clear();
                        */
                    }
                    if (tournamentState.tournamentRoundType == TournamentRoundType.Ranked && roundIndex < BDArmorySettings.TOURNAMENT_ROUNDS) // Generate the next ranked round.
                    {
                        tournamentState.GenerateRankedRound();
                        heatsRemaining = (BDArmorySettings.TOURNAMENT_ROUNDS - roundIndex) * tournamentState.rounds.First().Value.Count;
                    }
                    if (heatsRemaining > 0)
                    {
                        if (BDArmorySettings.TOURNAMENT_TIMEWARP_BETWEEN_ROUNDS > 0)
                        {
                            BDACompetitionMode.Instance.competitionStatus.Add($"Warping ahead {BDArmorySettings.TOURNAMENT_TIMEWARP_BETWEEN_ROUNDS} mins, then running the next round.");
                            yield return WarpAhead(BDArmorySettings.TOURNAMENT_TIMEWARP_BETWEEN_ROUNDS * 60);
                        }
                        else
                        {
                            // Wait a bit for any user action
                            tournamentStatus = TournamentStatus.Waiting;
                            double startTime = Planetarium.GetUniversalTime();
                            while ((Planetarium.GetUniversalTime() - startTime) < BDArmorySettings.TOURNAMENT_DELAY_BETWEEN_HEATS)
                            {
                                BDACompetitionMode.Instance.competitionStatus.Add("Waiting " + (BDArmorySettings.TOURNAMENT_DELAY_BETWEEN_HEATS - (Planetarium.GetUniversalTime() - startTime)).ToString("0") + "s, then running the next round.");
                                yield return new WaitForSeconds(1);
                            }
                        }
                    }
                }
            }
            message = "All rounds in tournament " + tournamentState.tournamentID + " have been run.";
            BDACompetitionMode.Instance.competitionStatus.Add(message);
            Debug.Log("[BDArmory.BDATournament]: " + message);
            tournamentStatus = TournamentStatus.Completed;
            if (BDArmorySettings.AUTO_DISABLE_UI) SetGameUI(true);
            var partialStatePath = Path.ChangeExtension(Path.Combine(Path.GetDirectoryName(TournamentState.defaultStateFile), "Unfinished Tournaments", Path.GetFileName(stateFile)), $".state-{tournamentID}");
            if (File.Exists(partialStatePath)) File.Delete(partialStatePath); // Remove the now completed tournament state file.

            if ((BDArmorySettings.AUTO_RESUME_TOURNAMENT || BDArmorySettings.AUTO_RESUME_CONTINUOUS_SPAWN) && BDArmorySettings.AUTO_QUIT_AT_END_OF_TOURNAMENT && TournamentAutoResume.Instance != null)
            {
                TournamentAutoResume.AutoQuit(5);
                message = "Quitting KSP in 5s due to reaching the end of a tournament.";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.LogWarning("[BDArmory.BDATournament]: " + message);
                yield break;
            }
        }

        IEnumerator ExecuteWaypointHeat(int roundIndex, int heatIndex)
        {
            if (TournamentCoordinator.Instance.IsRunning) TournamentCoordinator.Instance.Stop();
            var spawnConfig = tournamentState.rounds[roundIndex][heatIndex] as CircularSpawnConfig;
            spawnConfig.worldIndex = WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].worldIndex;
            spawnConfig.latitude = WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].spawnPoint.x;
            spawnConfig.longitude = WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].spawnPoint.y;

            TournamentCoordinator.Instance.Configure(new SpawnConfigStrategy(spawnConfig),
                new WaypointFollowingStrategy(WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].waypoints),
                spawnerBase
            );

            // Run the waypoint competition.
            TournamentCoordinator.Instance.Run();
            // Wait until spawning is completed and the competition is actually about to start.
            yield return new WaitWhile(() => TournamentCoordinator.Instance.IsRunning && !BDACompetitionMode.Instance.competitionIsActive);
            competitionStarted = true;
            // Register all the active vessels as part of the tournament.
            foreach (var kvp in SpawnUtils.SpawnedVesselURLs)
                tournamentState.scores.AddPlayer(kvp.Key, kvp.Value, roundIndex, tournamentState.npcFiles.Contains(kvp.Value));
            yield return new WaitWhile(() => TournamentCoordinator.Instance.IsRunning);
        }

        IEnumerator ExecuteHeat(int roundIndex, int heatIndex, bool startDespiteFailures = false, bool firstRun = false)
        {
            if (tournamentState.tournamentStyle == TournamentStyle.TemplateRNG)
            {
                CustomSpawnConfig customSpawnConfig = tournamentState.rounds[roundIndex][heatIndex] as CustomSpawnConfig;
                // Populate the customSpawnConfig.customVesselSpawnConfigs with the vessels
                if (BDArmorySettings.VESSEL_SPAWN_RANDOM_ORDER) customSpawnConfig.customVesselSpawnConfigs.Shuffle(); // Randomise the team spawn points.
                for (int teamIndex = 0; teamIndex < customSpawnConfig.customVesselSpawnConfigs.Count; ++teamIndex)
                {
                    if (BDArmorySettings.VESSEL_SPAWN_RANDOM_ORDER) customSpawnConfig.customVesselSpawnConfigs[teamIndex].Shuffle(); // Randomise the positions within each team spawn point.
                    for (int craftIndex = 0; craftIndex < customSpawnConfig.customVesselSpawnConfigs[teamIndex].Count; ++craftIndex)
                    {
                        if (teamIndex < customSpawnConfig.teamsSpecific.Count && craftIndex < customSpawnConfig.teamsSpecific[teamIndex].Count)
                        {
                            customSpawnConfig.customVesselSpawnConfigs[teamIndex][craftIndex].craftURL = customSpawnConfig.teamsSpecific[teamIndex][craftIndex];
                            customSpawnConfig.customVesselSpawnConfigs[teamIndex][craftIndex].teamIndex = teamIndex;
                        }
                        else // Clear the remaining entries.
                            customSpawnConfig.customVesselSpawnConfigs[teamIndex][craftIndex].craftURL = "";
                        customSpawnConfig.customVesselSpawnConfigs[teamIndex][craftIndex].kerbalName = ""; // Use random crew.
                    }
                }
                CustomTemplateSpawning.Instance.SpawnCustomTemplate(customSpawnConfig);
                while (CustomTemplateSpawning.Instance.vesselsSpawning)
                    yield return new WaitForFixedUpdate();
                if (!CustomTemplateSpawning.Instance.vesselSpawnSuccess)
                {
                    tournamentStatus = TournamentStatus.Stopped;
                    yield break;
                }
                // Populate the VS window's UI entries with the spawned vessels.
                CustomTemplateSpawning.Instance.PopulateEntriesFromConfig(customSpawnConfig);
            }
            else
            {
                CircularSpawning.Instance.SpawnAllVesselsOnce(tournamentState.rounds[roundIndex][heatIndex] as CircularSpawnConfig);
                while (CircularSpawning.Instance.vesselsSpawning)
                    yield return new WaitForFixedUpdate();
                if (!CircularSpawning.Instance.vesselSpawnSuccess)
                {
                    tournamentStatus = TournamentStatus.Stopped;
                    yield break;
                }
            }
            yield return new WaitForFixedUpdate();
            if (firstRun)
            {
                if (!BDTISettings.STORE_TEAM_COLORS) BDTISetup.Instance.ResetColors(); // Get some good colours on the first run instead of random ones.
            }
            // NOTE: runs in separate coroutine
            if (BDArmorySettings.RUNWAY_PROJECT)
            {
                switch (BDArmorySettings.RUNWAY_PROJECT_ROUND)
                {
                    case 33:
                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                        break;
                    case 44:
                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                        break;
                    case 53:
                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                        break;
                    case 67:
                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                        break;
                    case 77:
                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                        break;
                    default:
                        BDACompetitionMode.Instance.StartCompetitionMode(BDArmorySettings.COMPETITION_DISTANCE, startDespiteFailures);
                        break;
                }
            }
            else
                BDACompetitionMode.Instance.StartCompetitionMode(BDArmorySettings.COMPETITION_DISTANCE, startDespiteFailures);
            yield return new WaitForFixedUpdate(); // Give the competition start a frame to get going.

            // start timer coroutine for the duration specified in settings UI
            var duration = BDArmorySettings.COMPETITION_DURATION * 60f;
            message = "Starting " + (duration > 0 ? "a " + duration.ToString("F0") + "s" : "an unlimited") + " duration competition.";
            Debug.Log("[BDArmory.BDATournament]: " + message);
            BDACompetitionMode.Instance.competitionStatus.Add(message);
            while (BDACompetitionMode.Instance.competitionStarting || BDACompetitionMode.Instance.sequencedCompetitionStarting)
                yield return new WaitForFixedUpdate(); // Wait for the competition to actually start.
            if (!BDACompetitionMode.Instance.competitionIsActive)
            {
                var message = "Competition failed to start.";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.Log("[BDArmory.BDATournament]: " + message);
                tournamentStatus = TournamentStatus.Stopped;
                yield break;
            }
            competitionStarted = true;
            // Register all the active vessels as part of the tournament.
            foreach (var kvp in SpawnUtils.SpawnedVesselURLs)
                tournamentState.scores.AddPlayer(kvp.Key, kvp.Value, roundIndex, tournamentState.npcFiles.Contains(kvp.Value));
            // Wait for the competition to finish.
            while (BDACompetitionMode.Instance.competitionIsActive)
                yield return new WaitForSeconds(1);
        }

        #region Warping
        GameObject warpCamera;
        IEnumerator WarpAhead(double warpTimeBetweenHeats)
        {
            if (!FlightGlobals.currentMainBody.hasSolidSurface)
            {
                message = "Sorry, unable to TimeWarp without a solid surface to place the spawn probe on.";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.Log("[BDArmory.BDATournament]: " + message);
                yield return new WaitForSeconds(5f);
                yield break;
            }
            warpingInProgress = true;
            Vessel spawnProbe;
            var vesselsToKill = FlightGlobals.Vessels.ToList();
            int tries = 0;
            do
            {
                spawnProbe = VesselSpawner.SpawnSpawnProbe();
                yield return new WaitWhile(() => spawnProbe != null && (!spawnProbe.loaded || spawnProbe.packed));
                while (spawnProbe != null && FlightGlobals.ActiveVessel != spawnProbe)
                {
                    LoadedVesselSwitcher.Instance.ForceSwitchVessel(spawnProbe);
                    yield return null;
                }
            } while (++tries < 3 && spawnProbe == null);
            if (spawnProbe == null)
            {
                message = "Failed to spawn spawnProbe, aborting warp.";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.LogWarning("[BDArmory.BDATournament]: " + message);
                yield break;
            }
            var up = spawnProbe.up;
            var refDirection = Math.Abs(Vector3.Dot(Vector3.up, up)) < 0.71f ? Vector3.up : Vector3.forward; // Avoid that the reference direction is colinear with the local surface normal.
            spawnProbe.SetPosition(spawnProbe.transform.position - BodyUtils.GetRadarAltitudeAtPos(spawnProbe.transform.position) * up);
            if (spawnProbe.altitude > 0) spawnProbe.Landed = true;
            else spawnProbe.Splashed = true;
            spawnProbe.SetVelocity(Vector3.zero); // Set the velocity to zero so that warp goes in high mode.
                                                        // Kill all other vessels (including debris).
            foreach (var vessel in vesselsToKill)
                SpawnUtils.RemoveVessel(vessel);
            while (SpawnUtils.removingVessels) yield return null;

            // Adjust the camera for a nice view.
            if (warpCamera == null) warpCamera = new GameObject("WarpCamera");
            var cameraLocalPosition = 3f * Vector3.Cross(up, refDirection).normalized + up;
            warpCamera.SetActive(true);
            warpCamera.transform.position = spawnProbe.transform.position;
            warpCamera.transform.rotation = Quaternion.LookRotation(-cameraLocalPosition, up);
            var flightCamera = FlightCamera.fetch;
            var originalCameraParentTransform = flightCamera.transform.parent;
            var originalCameraNearClipPlane = flightCamera.mainCamera.nearClipPlane;
            flightCamera.SetTargetNone();
            flightCamera.transform.parent = warpCamera.transform;
            flightCamera.transform.localPosition = cameraLocalPosition;
            flightCamera.transform.localRotation = Quaternion.identity;
            flightCamera.SetDistance(3000f);

            var warpTo = Planetarium.GetUniversalTime() + warpTimeBetweenHeats;
            var startTime = Time.time;
            do
            {
                if (TimeWarp.WarpMode != TimeWarp.Modes.HIGH && TimeWarp.CurrentRate > 1) // Warping in low mode, abort.
                {
                    TimeWarp.fetch.CancelAutoWarp();
                    TimeWarp.SetRate(0, true, false);
                    while (TimeWarp.CurrentRate > 1) yield return null; // Wait for the warping to stop.
                    spawnProbe.SetPosition(spawnProbe.transform.position - BodyUtils.GetRadarAltitudeAtPos(spawnProbe.transform.position) * up);
                    if (spawnProbe.altitude > 0) spawnProbe.Landed = true;
                    else spawnProbe.Splashed = true;
                    spawnProbe.SetVelocity(Vector3.zero); // Set the velocity to zero so that warp goes in high mode.
                }
                startTime = Time.time;
                while (TimeWarp.WarpMode != TimeWarp.Modes.HIGH && Time.time - startTime < 3)
                {
                    spawnProbe.SetVelocity(Vector3.zero); // Set the velocity to zero so that warp goes in high mode.
                    yield return null; // Give it a second to switch to high warp mode.
                }
                TimeWarp.fetch.WarpTo(warpTo);
                startTime = Time.time;
                while (TimeWarp.CurrentRate < 2 && Time.time - startTime < 1) yield return null; // Give it a second to get going.
            } while (TimeWarp.WarpMode != TimeWarp.Modes.HIGH && TimeWarp.CurrentRate > 1); // Warping, but not high warp, bugger. Try again. FIXME KSP isn't the focused app, it doesn't want to go into high warp!
            while (TimeWarp.CurrentRate > 1) yield return null; // Wait for the warping to stop.

            // Put the camera parent back.
            flightCamera.transform.parent = originalCameraParentTransform;
            flightCamera.mainCamera.nearClipPlane = originalCameraNearClipPlane;
            warpCamera.SetActive(false);

            warpingInProgress = false;
        }

        private class EarlyBird // Based on, but not the same as the EarlyBird mod.
        {
            /// <summary>
            /// The time until the next sunrise (plus offset in minutes).
            /// </summary>
            /// <param name="lat">Latitude</param>
            /// <param name="lon">Longitude</param>
            /// <param name="body">The celestial body.</param>
            /// <param name="offset">An offset in minutes.</param>
            /// <returns>The time until the next sunrise.</returns>
            public static double TimeToDaylight(double lat, double lon, CelestialBody body, double offset = 0)
            {
                if (body.isStar) return 0;
                var sun = Planetarium.fetch.Sun;
                var localTime = GetLocalTime(lon, body, sun);
                offset *= 60 / body.solarDayLength;
                double dayLength = GetDayLength(lat, body, sun);
                double timeOfDawn = 0.5 - dayLength / 2 + offset;
                return (timeOfDawn - localTime + 1.0) % 1.0 * body.solarDayLength;
            }
            /// <summary>
            /// Check whether it's daytime (within the margin) at the give location.
            /// </summary>
            /// <param name="lat">Latitude</param>
            /// <param name="lon">Longitude</param>
            /// <param name="body">The celestial body.</param>
            /// <param name="margin">Margin in minutes.</param>
            /// <returns>True if it's daytime, otherwise false.</returns>
            public static bool IsDayTime(double lat, double lon, CelestialBody body, double margin = 0)
            {
                if (body.isStar) return true;
                var sun = Planetarium.fetch.Sun;
                var localTime = GetLocalTime(lon, body, sun);
                margin *= 60 / body.solarDayLength;
                double dayLength = GetDayLength(lat, body, sun);
                double timeOfDawn = 0.5 - dayLength / 2 + margin;
                double timeOfDusk = 0.5 + dayLength / 2 - margin;
                return localTime > timeOfDawn && localTime < timeOfDusk;
            }
            /// <summary>
            /// Gets the day length in the range 0—1.
            /// </summary>
            /// <param name="lat"></param>
            /// <param name="body"></param>
            /// <param name="sun"></param>
            /// <returns></returns>
            public static double GetDayLength(double lat, CelestialBody body, CelestialBody sun)
            {
                if (body.isStar) return 1;
                // cos ω₀ = -tan φ * tan δ
                // ω₀ = solar hour angle, φ = observer latitude, δ = solar declination
                var solarDeclination = body.GetLatitude(sun.position - body.position, true);
                var cosW0 = -Math.Tan(lat * Math.PI / 180) * Math.Tan(solarDeclination * Math.PI / 180);
                if (cosW0 <= -1) return 1;
                if (cosW0 >= 1) return 0;
                var solarHourAngle = Math.Acos(cosW0);
                return solarHourAngle / Math.PI;
            }
            /// <summary>
            /// Gets the local time in the range 0—1 with 0.5 being noon.
            /// </summary>
            /// <param name="lon"></param>
            /// <param name="body"></param>
            /// <param name="sun"></param>
            /// <returns></returns>
            public static double GetLocalTime(double lon, CelestialBody body, CelestialBody sun)
            {
                if (body.isStar) return 0.5;
                return ((lon - body.GetLongitude(sun.position - body.position, true)) % 360 / 360.0 + 1.5) % 1.0;
            }
        }

        public IEnumerator WarpIfNeeded(SpawnConfig spawnConfig)
        {
            if (BDArmorySettings.TOURNAMENT_TIMEWARP_BETWEEN_ROUNDS < 0)
            {
                var body = FlightGlobals.Bodies[spawnConfig.worldIndex];
                if (!EarlyBird.IsDayTime(spawnConfig.latitude, spawnConfig.longitude, body, 10))
                {
                    SpawnUtils.ShowSpawnPoint(spawnConfig.worldIndex, spawnConfig.latitude, spawnConfig.longitude, spawnConfig.altitude);
                    BDACompetitionMode.Instance.competitionStatus.Add($"Warping ahead to morning, then running the next round.");
                    yield return WarpAhead(EarlyBird.TimeToDaylight(
                        spawnConfig.latitude,
                        spawnConfig.longitude,
                        body,
                        10
                    )); // Warp to morning +10mins.
                }
            }
        }
        #endregion

        void SetGameUI(bool enable)
        { if (isActiveAndEnabled) StartCoroutine(SetGameUIWorker(enable)); }
        IEnumerator SetGameUIWorker(bool enable)
        {
            // On first entering flight mode, KSP issues a couple of ShowUI after a few seconds. WTF KSP devs?!
            yield return new WaitUntil(() => BDACompetitionMode.Instance is not null && (BDACompetitionMode.Instance.competitionStarting || BDACompetitionMode.Instance.competitionIsActive));
            // Also, triggering ShowUI/HideUI doesn't trigger the onShowUI/onHideUI events, so we need to fire them off ourselves.
            if (enable) { KSP.UI.UIMasterController.Instance.ShowUI(); GameEvents.onShowUI.Fire(); }
            else { KSP.UI.UIMasterController.Instance.HideUI(); GameEvents.onHideUI.Fire(); }
        }

        List<KeyValuePair<string, float>> rankedScores = new List<KeyValuePair<string, float>>();
        float lastUpdatedRankedScores = 0;
        public List<KeyValuePair<string, float>> GetRankedScores // Get a list of the scores in ranked order.
        {
            get
            {
                if (tournamentState.scores.lastUpdated > lastUpdatedRankedScores)
                {
                    rankedScores = tournamentState.scores.scores.OrderByDescending(kvp => kvp.Value).Select(kvp => new KeyValuePair<string, float>(kvp.Key, kvp.Value)).ToList();
                    lastUpdatedRankedScores = tournamentState.scores.lastUpdated;
                    if (ScoreWindow.Instance != null) ScoreWindow.Instance.ResetWindowSize();
                }
                return rankedScores;
            }
        }

        List<KeyValuePair<string, float>> rankedTeamScores = new List<KeyValuePair<string, float>>();
        float lastUpdatedRankedTeamScores = 0;
        public List<KeyValuePair<string, float>> GetRankedTeamScores
        {
            get
            {
                if (tournamentState.scores.lastUpdated > lastUpdatedRankedTeamScores)
                {
                    // Get the unique teams, then make a dictionary with team names as keys and the sum of scores as values and sort them by the scores.
                    var teamNames = tournamentState.scores.playersToTeamNames.Values.ToHashSet();
                    var teamScores = teamNames.ToDictionary(
                        teamName => teamName,
                        teamName => tournamentState.scores.scores.Where(kvp => tournamentState.scores.playersToTeamNames.ContainsKey(kvp.Key) && tournamentState.scores.playersToTeamNames[kvp.Key] == teamName).Sum(kvp => kvp.Value));
                    rankedTeamScores = teamScores.OrderByDescending(kvp => kvp.Value).ToList();
                    lastUpdatedRankedTeamScores = tournamentState.scores.lastUpdated;
                    if (ScoreWindow.Instance != null) ScoreWindow.Instance.ResetWindowSize();
                }
                return rankedTeamScores;
            }
        }

        void LogScores(bool teams)
        {
            var scores = teams ? GetRankedTeamScores : GetRankedScores;
            if (scores.Count == 0) return;
            var logsFolder = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "BDArmory", "Logs"));
            var fileName = Path.Combine(logsFolder, $"Tournament {tournamentID}", teams ? "team scores.log" : "ranked scores.log");
            var maxNameLength = scores.Max(kvp => kvp.Key.Length);
            var lines = scores.Select((kvp, rank) => $"{rank + 1,3:D} - {kvp.Key} {new string(' ', maxNameLength - kvp.Key.Length)}{kvp.Value,8:F3}").ToList();
            if (tournamentState.tournamentRoundType == TournamentRoundType.Ranked)
                lines.Insert(0, $"Tournament {tournamentID}, round {currentRound} / {BDArmorySettings.TOURNAMENT_ROUNDS}");  // Round 0 is the initial shuffled round.
            else
                lines.Insert(0, $"Tournament {tournamentID}, round {currentRound + 1} / {numberOfRounds}"); // For non-ranked rounds, start counting at 1.
            File.WriteAllLines(fileName, lines);
        }

        public Tuple<int, int, int, int> GetTournamentProgress()
        {
            if (tournamentState.tournamentRoundType == TournamentRoundType.Ranked)
                return new Tuple<int, int, int, int>(currentRound, BDArmorySettings.TOURNAMENT_ROUNDS, currentHeat + 1, numberOfHeats); // Round 0 is the initial shuffled round.
            else
                return new Tuple<int, int, int, int>(currentRound + 1, numberOfRounds, currentHeat + 1, numberOfHeats); // For non-ranked rounds, start counting at 1.
        }

        public void RecomputeScores() => tournamentState.scores.ComputeScores();

        /// <summary>
        /// Add a player to the tournament scoring while a tournament is running.
        /// </summary>
        /// <param name="player">The craft's name.</param>
        /// <param name="filename">The file the craft originated from.</param>
        /// <param name="fighter">Whether or not the vessel is a fighter.</param>
        public void AddPlayer(Vessel vessel)
        {
            if (vessel == null) return;
            var ac = vessel.ActiveController();
            if (ac.WM == null) return; // Not a valid vessel for combat.
            tournamentState.scores.AddPlayer(vessel.GetName(), ac.WM.SourceVesselURL, fighter: ac.IsFighter);
        }
    }

    /// <summary>
    /// A class to automatically load and resume a tournament upon starting KSP.
    /// Borrows heavily from the AutoLoadGame mod. 
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    public class TournamentAutoResume : MonoBehaviour
    {
        public static TournamentAutoResume Instance;
        public static bool firstRun = true;
        string savesDir;
        string savegame;
        string save = "persistent";
        string game;
        bool sceneLoaded = false;
        public static float memoryUsage
        {
            get
            {
                if (_memoryUsage > 0) return _memoryUsage;
                _memoryUsage = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() + UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
                var gfxDriver = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
                _memoryUsage += gfxDriver > 0 ? gfxDriver : 5f * (1 << 30); // Use the GfxDriver memory usage if available, otherwise estimate it at 5GB (which is a little more than what I get with no extra visual mods at ~4.5GB).
                _memoryUsage /= (1 << 30); // In GB.
                return _memoryUsage;
            }
            set { _memoryUsage = 0; } // Reset condition for calculating it again.
        }
        static float _memoryUsage;

        void Awake()
        {
            if (Instance != null || !firstRun) // Only the first loaded instance gets to run.
            {
                Destroy(this);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(this);
            GameEvents.onLevelWasLoadedGUIReady.Add(onLevelWasLoaded);
            savesDir = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "saves"));
        }

        void OnDestroy()
        {
            GameEvents.onLevelWasLoadedGUIReady.Remove(onLevelWasLoaded);
        }

        void onLevelWasLoaded(GameScenes scene)
        {
            sceneLoaded = true;
            if (scene != GameScenes.MAINMENU) return;
            if (!firstRun) return;
            firstRun = false;
            StartCoroutine(WaitForSettings());
        }

        IEnumerator WaitForSettings()
        {
            yield return new WaitForSeconds(0.5f);
            var tic = Time.realtimeSinceStartup;
            yield return new WaitUntil(() => BDArmorySettings.ready || Time.realtimeSinceStartup - tic > 30); // Wait until the settings are ready or timed out.
            Debug.Log($"[BDArmory.BDATournament]: BDArmory settings loaded, auto-load to KSC: {BDArmorySettings.AUTO_LOAD_TO_KSC}, auto-resume tournaments: {BDArmorySettings.AUTO_RESUME_TOURNAMENT}, auto-resume continuous spawn: {BDArmorySettings.AUTO_RESUME_CONTINUOUS_SPAWN}, auto-resume evolution: {BDArmorySettings.AUTO_RESUME_EVOLUTION}, generate clean save: {BDArmorySettings.GENERATE_CLEAN_SAVE}.");
            if (BDArmorySettings.AUTO_RESUME_TOURNAMENT || BDArmorySettings.AUTO_RESUME_CONTINUOUS_SPAWN || BDArmorySettings.AUTO_RESUME_EVOLUTION || BDArmorySettings.AUTO_LOAD_TO_KSC)
            { yield return StartCoroutine(AutoResumeTournament()); }
            else if (BDArmorySettings.GENERATE_CLEAN_SAVE && TryLoadCleanSlate())
            { GenerateCleanGame(false); }
        }

        IEnumerator AutoResumeTournament()
        {
            bool resumingEvolution = false;
            bool resumingTournament = false;
            bool resumingContinuousSpawn = false;
            bool generateNewTournament = false;
            EvolutionWorkingState evolutionState = null;
            if (BDArmorySettings.AUTO_RESUME_EVOLUTION) // Auto-resume evolution overrides auto-resume tournament.
            {
                evolutionState = TryLoadEvolutionState();
                resumingEvolution = evolutionState != null;
            }
            if (!resumingEvolution && BDArmorySettings.AUTO_RESUME_TOURNAMENT)
            {
                resumingTournament = TryLoadTournamentState(out generateNewTournament);
            }
            if (!(resumingEvolution || resumingTournament) && BDArmorySettings.AUTO_RESUME_CONTINUOUS_SPAWN)
            {
                resumingContinuousSpawn = TryResumingContinuousSpawn();
            }
            if (!(resumingEvolution || resumingTournament || resumingContinuousSpawn)) // Auto-Load To KSC
            {
                if (!TryLoadCleanSlate()) yield break;
            }
            // Load saved game.
            var tic = Time.time;
            sceneLoaded = false;
            if (!(BDArmorySettings.GENERATE_CLEAN_SAVE ? GenerateCleanGame() : LoadGame())) yield break;
            yield return new WaitUntil(() => sceneLoaded || Time.time - tic > 10);
            if (!sceneLoaded) { Debug.Log("[BDArmory.BDATournament]: Failed to load scene."); yield break; }
            if (!(resumingEvolution || resumingTournament || resumingContinuousSpawn)) yield break; // Just load to the KSC.
            var lastUsedWorldIndex = BDArmorySettings.VESSEL_SPAWN_WORLDINDEX; // Store the last used world index as it gets reset when entering flight mode.

            // Switch to flight mode.
            sceneLoaded = false;
            FlightDriver.StartWithNewLaunch(VesselSpawner.spawnProbeLocation, "GameData/Squad/Flags/default.png", FlightDriver.LaunchSiteName, new VesselCrewManifest()); // This triggers an error for SpaceCenterCamera2, but I don't see how to fix it and it doesn't appear to be harmful.
            tic = Time.time;
            yield return new WaitUntil(() => sceneLoaded || Time.time - tic > 10);
            if (!sceneLoaded) { Debug.Log("[BDArmory.BDATournament]: Failed to load flight scene."); yield break; }
            // Resume the tournament.
            yield return new WaitForSeconds(1);
            if (resumingEvolution) // Auto-resume evolution overrides auto-resume tournament.
            {
                tic = Time.time;
                yield return new WaitWhile(() => (BDAModuleEvolution.Instance == null && Time.time - tic < 10)); // Wait for the tournament to be loaded or time out.
                if (BDAModuleEvolution.Instance == null) yield break;
                BDArmorySetup.windowBDAToolBarEnabled = true;
                LoadedVesselSwitcher.Instance.SetVisible(true);
                BDArmorySetup.Instance.showEvolutionGUI = true;
                BDAModuleEvolution.Instance.ResumeEvolution(evolutionState);
            }
            else if (resumingTournament)
            {
                tic = Time.time;
                if (generateNewTournament)
                {
                    yield return new WaitWhile(() => (BDATournament.Instance == null && Time.time - tic < 10)); // Wait for the BDATournament instance to be started or time out.
                    if (BDATournament.Instance == null) yield break;
                    BDArmorySettings.VESSEL_SPAWN_WORLDINDEX = lastUsedWorldIndex;
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
                }
                yield return new WaitWhile(() => ((BDATournament.Instance == null || BDATournament.Instance.tournamentID == 0) && Time.time - tic < 10)); // Wait for the tournament to be loaded or time out.
                if (BDATournament.Instance == null || BDATournament.Instance.tournamentID == 0) yield break;
                BDArmorySetup.windowBDAToolBarEnabled = true;
                LoadedVesselSwitcher.Instance.SetVisible(true);
                // VesselSpawnerWindow.Instance.SetVisible(true);
                RWPSettings.SetRWP(BDArmorySettings.RUNWAY_PROJECT, BDArmorySettings.RUNWAY_PROJECT_ROUND); // Reapply the RWP settings if RWP is active as some may be overridden by the above.
                BDATournament.Instance.RunTournament();
            }
            else if (resumingContinuousSpawn)
            {
                tic = Time.time;
                yield return new WaitWhile(() => ContinuousSpawning.Instance == null && Time.time - tic < 10); // Wait up to 10s for the continuous spawning instance to be valid.
                if (ContinuousSpawning.Instance == null) yield break;
                BDArmorySetup.windowBDAToolBarEnabled = true;
                LoadedVesselSwitcher.Instance.SetVisible(true);
                VesselSpawnerWindow.Instance.SetVisible(true);
                RWPSettings.SetRWP(BDArmorySettings.RUNWAY_PROJECT, BDArmorySettings.RUNWAY_PROJECT_ROUND); // Reapply the RWP settings if RWP is active as some may be overridden by the above.
                ContinuousSpawning.Instance.SpawnVesselsContinuously(
                    new CircularSpawnConfig( // Spawn config that would be used by clicking the continuous spawn button.
                        new SpawnConfig(
                            BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
                            BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y, BDArmorySettings.VESSEL_SPAWN_ALTITUDE_,
                            true, true, 1, null, null,
                            BDArmorySettings.VESSEL_SPAWN_FILES_LOCATION
                        ),
                        BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                        BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                        BDArmorySettings.VESSEL_SPAWN_INTRATEAM_DISTANCE,
                        BDArmorySettings.VESSEL_SPAWN_REF_HEADING
                    )
                );
            }
        }

        EvolutionWorkingState TryLoadEvolutionState()
        {
            Debug.Log("[BDArmory.BDATournament]: Attempting to auto-resume evolution.");
            EvolutionWorkingState evolutionState = null;
            evolutionState = BDAModuleEvolution.LoadState();
            if (string.IsNullOrEmpty(evolutionState.savegame)) { Debug.Log($"[BDArmory.BDATournament]: No savegame found in evolution state."); return null; }
            if (string.IsNullOrEmpty(evolutionState.evolutionId) || !File.Exists(Path.Combine(BDAModuleEvolution.configDirectory, evolutionState.evolutionId + ".cfg"))) { Debug.Log($"[BDArmory.BDATournament]: No saved evolution configured."); return null; }
            savegame = Path.Combine(savesDir, evolutionState.savegame, save + ".sfs");
            game = evolutionState.savegame;
            return evolutionState;
        }
        bool TryLoadTournamentState(out bool generateNewTournament)
        {
            generateNewTournament = false;
            // Check that there is an incomplete tournament, otherwise abort.
            bool incompleteTournament = false;
            if (File.Exists(TournamentState.defaultStateFile)) // Tournament state file exists.
            {
                var tournamentState = new TournamentState();
                if (!tournamentState.LoadState(TournamentState.defaultStateFile)) return false; // Failed to load
                savegame = Path.Combine(savesDir, tournamentState.savegame, save + ".sfs");
                if (File.Exists(savegame) && tournamentState.rounds.Select(r => r.Value.Count).Sum() - tournamentState.completed.Select(c => c.Value.Count).Sum() > 0) // Tournament state includes the savegame and has some rounds remaining —> Let's try resuming it! 
                {
                    incompleteTournament = true;
                    game = tournamentState.savegame;
                }
            }
            if (!incompleteTournament && BDArmorySettings.AUTO_GENERATE_TOURNAMENT_ON_RESUME) // Generate a new tournament based on the current settings.
            {
                generateNewTournament = true;
                game = BDArmorySettings.LAST_USED_SAVEGAME;
                savegame = Path.Combine(savesDir, game, save + ".sfs");
                if (File.Exists(savegame)) // Found a usable savegame and we assume the generated tournament will be usable. (It should just show error messages in-game otherwise.)
                    incompleteTournament = true;
            }
            return incompleteTournament;
        }
        bool TryResumingContinuousSpawn()
        {
            game = BDArmorySettings.LAST_USED_SAVEGAME;
            savegame = Path.Combine(savesDir, game, save + ".sfs");
            if (!File.Exists(savegame)) return false; // Unable to find a usable savegame.
                                                      // Check if the spawn config would be valid and return success if it is.
            var AutoSpawnPath = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, VesselSpawnerBase.AutoSpawnFolder));
            var spawnPath = Path.Combine(AutoSpawnPath, BDArmorySettings.VESSEL_SPAWN_FILES_LOCATION);
            if (!Directory.Exists(spawnPath)) return false;
            if (Directory.GetFiles(spawnPath, "*.craft").Length < 2) return false;
            return true;
        }
        bool TryLoadCleanSlate()
        {
            game = BDArmorySettings.LAST_USED_SAVEGAME;
            if (string.IsNullOrEmpty(game)) game = "sandbox"; // Set the game to the default "sandbox" name if no previous name has been used.
            savegame = Path.Combine(savesDir, game, save + ".sfs");
            return File.Exists(savegame) || BDArmorySettings.GENERATE_CLEAN_SAVE;
        }

        bool GenerateCleanGame(bool startGame = true)
        {
            // Grab the scenarios from the previous persistent game.
            HighLogic.CurrentGame = GamePersistence.LoadGame("persistent", game, true, false);
            var scenarios = HighLogic.CurrentGame?.scenarios;

            if (BDArmorySettings.GENERATE_CLEAN_SAVE)
            {
                // Generate a new clean game and add in the scenarios.
                HighLogic.CurrentGame = new Game();
                HighLogic.CurrentGame.startScene = GameScenes.SPACECENTER;
                HighLogic.CurrentGame.Mode = Game.Modes.SANDBOX;
                HighLogic.SaveFolder = game;
                if (scenarios != null) foreach (var scenario in scenarios) { CheckForScenario(scenario.moduleName, scenario.targetScenes); }

                // Generate the default roster and make them all badass pilots.
                HighLogic.CurrentGame.CrewRoster = KerbalRoster.GenerateInitialCrewRoster(HighLogic.CurrentGame.Mode);
                foreach (var kerbal in HighLogic.CurrentGame.CrewRoster.Kerbals(ProtoCrewMember.RosterStatus.Available))
                {
                    kerbal.isBadass = true; // Make them badass.
                    KerbalRoster.SetExperienceTrait(kerbal, KerbalRoster.pilotTrait); // Make the kerbal a pilot (so they can use SAS properly).
                    KerbalRoster.SetExperienceLevel(kerbal, KerbalRoster.GetExperienceMaxLevel()); // Make them experienced.
                    kerbal.courage = 0.5f;
                }
            }
            else
            {
                GamePersistence.UpdateScenarioModules(HighLogic.CurrentGame);
            }
            // Update the game state and save it to the persistent save (since that's what eventually ends up getting loaded when we call Start()).
            HighLogic.CurrentGame.Updated();
            GamePersistence.SaveGame("persistent", game, SaveMode.OVERWRITE);
            if (startGame) HighLogic.CurrentGame.Start();
            return true;
        }

        bool LoadGame()
        {
            var gameNode = GamePersistence.LoadSFSFile(save, game);
            if (gameNode == null)
            {
                Debug.LogWarning($"[BDArmory.BDATournament]: Unable to load the save game: {savegame}");
                return false;
            }
            Debug.Log($"[BDArmory.BDATournament]: Loaded save game: {savegame}");
            KSPUpgradePipeline.Process(gameNode, game, SaveUpgradePipeline.LoadContext.SFS, OnLoadDialogPiplelineFinished, (opt, n) => Debug.LogWarning($"[BDArmory.BDATournament]: KSPUpgradePipeline finished with error: {savegame}"));
            return true;
        }

        void OnLoadDialogPiplelineFinished(ConfigNode node)
        {
            HighLogic.CurrentGame = GamePersistence.LoadGameCfg(node, game, true, false);
            if (HighLogic.CurrentGame == null) return;
            if (GamePersistence.UpdateScenarioModules(HighLogic.CurrentGame))
            {
                if (node != null)
                { GameEvents.onGameStatePostLoad.Fire(node); }
                GamePersistence.SaveGame(HighLogic.CurrentGame, save, game, SaveMode.OVERWRITE);
            }
            HighLogic.CurrentGame.startScene = GameScenes.SPACECENTER;
            HighLogic.SaveFolder = game;
            HighLogic.CurrentGame.Start();
        }

        /// <summary>
        /// Look for the scenario in the currently loaded assemblies and add the scenario to the requested scenes.
        /// These come from a previous persistent.sfs save.
        /// </summary>
        /// <param name="scenarioName">Name of the scenario.</param>
        /// <param name="targetScenes">The scenes the scenario should be present in.</param>
        void CheckForScenario(string scenarioName, List<GameScenes> targetScenes)
        {
            if (scenarioName == "ProgressTracking") return; // Skip "ProgressTracking", which can trigger tutorials again.
            foreach (var assy in AssemblyLoader.loadedAssemblies)
            {
                foreach (var type in assy.assembly.GetTypes())
                {
                    if (type == null) continue;
                    if (type.Name == scenarioName)
                    {
                        HighLogic.CurrentGame.AddProtoScenarioModule(type, [.. targetScenes]);
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Check the non-native memory usage and automatically quit if it's above the configured threshold.
        /// Note: only the managed (non-native) memory is checked, the amount of native memory may or may not be comparable to the amount of non-native memory. FIXME This needs checking in a long tournament.
        /// </summary>
        /// <returns></returns>
        public bool CheckMemoryUsage()
        {
            if (!(BDArmorySettings.AUTO_RESUME_TOURNAMENT || BDArmorySettings.AUTO_RESUME_EVOLUTION) || BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD > BDArmorySetup.SystemMaxMemory) return false; // Only trigger if Auto-Resume Tournaments is enabled and the Quit Memory Usage Threshold is set.
            memoryUsage = 0; // Trigger recalculation of memory usage.
            if (memoryUsage >= BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD)
            {
                if (BDACompetitionMode.Instance != null) BDACompetitionMode.Instance.competitionStatus.Add("Quitting in 3s due to memory usage threshold reached.");
                Debug.LogWarning($"[BDArmory.BDATournament]: Quitting KSP due to reaching Auto-Quit Memory Threshold: {memoryUsage} / {BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD}GB");
                StartCoroutine(AutoQuitCoroutine(3)); // Trigger quit in 3s to give the tournament coroutine time to stop and the message to be shown.
                return true;
            }
            return false;
        }

        public static void AutoQuit(float delay = 1) => Instance.StartCoroutine(Instance.AutoQuitCoroutine(delay));

        /// <summary>
        /// Automatically quit KSP after a delay.
        /// </summary>
        /// <param name="delay"></param>
        /// <returns></returns>
        IEnumerator AutoQuitCoroutine(float delay = 1)
        {
            yield return new WaitForSeconds(delay);
            SpawnUtils.CancelSpawning(); // Make sure any current spawning is stopped.
            HighLogic.LoadScene(GameScenes.MAINMENU);
            yield return new WaitForSeconds(0.5f); // Pause on the Main Menu a moment, then quit.
            Debug.Log("[BDArmory.BDATournament]: Quitting KSP.");
            Application.Quit();
        }
    }
}