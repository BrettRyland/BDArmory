using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

using BDArmory.Competition;
using BDArmory.Settings;
using BDArmory.VesselSpawning;

namespace BDArmory.Evolution
{
    public enum EvolutionStatus
    {
        Idle,
        Preparing,
        GeneratingVariants,
        RunningTournament,
        ProcessingResults,
    }

    public class EvolutionState
    {
        public string id;
        public EvolutionStatus status;
        public List<VariantGroup> groups;
        public EvolutionState(string id, EvolutionStatus status, List<VariantGroup> groups)
        {
            this.id = id;
            this.status = status;
            this.groups = groups;
        }
    }

    public class EvolutionWorkingState
    {
        public string savegame;
        public string evolutionId;
        public CircularSpawnConfig spawnConfig;
        // public Dictionary<string, Dictionary<string, float>> aggregateScores;
    }

    public class VariantGroup
    {
        public int id;
        public string seedName;
        public string referenceName;
        public List<Variant> variants;
        public VariantGroup(int id, string seedName, string referenceName, List<Variant> variants)
        {
            this.id = id;
            this.seedName = seedName;
            this.referenceName = referenceName;
            this.variants = variants;
        }
    }

    public class Variant
    {
        public string id;
        public string name;
        public List<MutatedPart> mutatedParts;
        public string key;
        public int direction;
        public Variant(string id, string name, List<MutatedPart> mutatedParts, string key, int direction)
        {
            this.id = id;
            this.name = name;
            this.mutatedParts = mutatedParts;
            this.key = key;
            this.direction = direction;
        }
    }

    public class MutatedPart
    {
        public string partName;
        public string moduleName;
        public string paramName;
        public float referenceValue;
        public float value;
        public MutatedPart(string partName, string moduleName, string paramName, float referenceValue, float value)
        {
            this.partName = partName;
            this.moduleName = moduleName;
            this.paramName = paramName;
            this.referenceValue = referenceValue;
            this.value = value;
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class BDAModuleEvolution : MonoBehaviour
    {
        public static BDAModuleEvolution Instance;

        public static string configDirectory;
        private static string workingDirectory;
        private static string seedDirectory;
        private static string adversaryDirectory;
        private static string weightMapFile;
        private static string stateFile;

        private Coroutine evoCoroutine = null;

        private EvolutionStatus status = EvolutionStatus.Idle;
        public EvolutionStatus Status() { return status; }

        private EvolutionState evolutionState = null;

        // Current active variant group (used to render in EvolutionWindow or similar)
        public VariantGroup ActiveVariantGroup() {return evolutionState.groups.Last();}

        private VariantEngine engine = null;

        // Spawn settings
        private static CircularSpawnConfig spawnConfig;

        // config node for evolution details
        private ConfigNode config = null;

        // root node of the active seed craft
        private ConfigNode craft = null;

        // evolution id
        private string evolutionId = null;
        public string EvolutionId { get { return evolutionId; } }

        // group id
        private int groupId = 0;
        public int GroupId { get { return groupId; } }

        // next variant id
        private int nextVariantId = 0;

        private int heat = 0;
        public int Heat { get { return heat; } }

        // private VariantOptions options;

        private static Dictionary<string, Dictionary<string, float>> aggregateScores = new Dictionary<string, Dictionary<string, float>>();

        public static void ConfigurePaths()
        {
            configDirectory = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "AutoSpawn", "evolutions"));
            workingDirectory = Path.Combine(configDirectory, "working");
            seedDirectory = Path.Combine(configDirectory, "seeds");
            adversaryDirectory = Path.Combine(configDirectory, "adversaries");
            weightMapFile = Path.Combine(configDirectory, "weights.cfg");
            stateFile = Path.Combine(configDirectory, "evolution.state");
        }

        void Awake()
        {
            // Debug.Log("[BDArmory.BDAEvolution]: Evolution awake");
            if (Instance)
            {
                Destroy(Instance);
            }

            Instance = this;
            if (string.IsNullOrEmpty(configDirectory)) ConfigurePaths();
        }

        private void Start()
        {
            // Debug.Log("[BDArmory.BDAEvolution]: Evolution start");
            engine = new VariantEngine();
        }

        private void OnDestroy()
        {
            SaveState();
        }

        public void StartEvolution()
        {
            if (evoCoroutine != null)
            {
                // Debug.Log("[BDArmory.BDAEvolution]: Evolution already running");
                return;
            }
            // Debug.Log("[BDArmory.BDAEvolution]: Evolution starting");
            status = EvolutionStatus.Preparing;

            // initialize evolution
            nextVariantId = 1;
            groupId = 1;
            evolutionId = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
            spawnConfig = new CircularSpawnConfig(
                new SpawnConfig(
                    BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
                    BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                    BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                    BDArmorySettings.VESSEL_SPAWN_ALTITUDE,
                    true,
                    true,
                    0,
                    null,
                    null,
                    workingDirectory
                ),
                BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                BDArmorySettings.VESSEL_SPAWN_INTRATEAM_DISTANCE,
                BDArmorySettings.VESSEL_SPAWN_REF_HEADING
            );
            evolutionState = new EvolutionState(evolutionId, status, new List<VariantGroup>());

            // create new config
            CreateEvolutionConfig();

            evoCoroutine = StartCoroutine(ExecuteEvolution());
        }

        public void ResumeEvolution(EvolutionWorkingState state)
        {
            if (state == null) return; // No valid state given.
            if (evoCoroutine != null) return; // Already running.

            // Copy state to local state.
            evolutionId = state.evolutionId;
            spawnConfig = state.spawnConfig;
            evolutionState = new EvolutionState(evolutionId, EvolutionStatus.Preparing, new List<VariantGroup>());
            var configFile = Path.Combine(configDirectory, evolutionId + ".cfg");
            ConfigNode existing = null;
            if (File.Exists(configFile)) existing = ConfigNode.Load(configFile);
            if (existing == null || !existing.HasNode("EVOLUTION"))
            {
                Debug.Log($"[BDArmory.BDAEvolution]: No pre-existing evolution found, starting a new one.");
                StartEvolution(); // No pre-existing evolution, start a new one.
                return;
            }
            ConfigNode evoNode = existing.GetNode("EVOLUTION");
            // groupId = int.Parse(evoNode.GetValue("groupId"));
            nextVariantId = int.Parse(evoNode.GetValue("nextVariantId"));
            foreach (var groupNode in existing.GetNodes("GROUP"))
            {
                groupId = int.Parse(groupNode.GetValue("id"));
                var seedName = groupNode.GetValue("seedName");
                var referenceName = groupNode.GetValue("referenceName");
                VariantGroup variantGroup = new VariantGroup(groupId, seedName, referenceName, new List<Variant>());

                foreach (var variantNode in groupNode.GetNodes("VARIANT"))
                {
                    var varId = variantNode.GetValue("id");
                    var varName = variantNode.GetValue("name");
                    var variant = new Variant(varId, varName, new List<MutatedPart>(), "", 0); // key and direction don't seem to be used.

                    foreach (var partNode in variantNode.GetNodes("MUTATION"))
                    {
                        var partName = partNode.GetValue("partName");
                        var moduleName = partNode.GetValue("moduleName");
                        var paramName = partNode.GetValue("paramName");
                        var referenceValue = float.Parse(partNode.GetValue("referenceValue"));
                        var value = float.Parse(partNode.GetValue("value"));
                        variant.mutatedParts.Add(new MutatedPart(partName, moduleName, paramName, referenceValue, value));
                    }
                    variantGroup.variants.Add(variant);
                }
                evolutionState.groups.Add(variantGroup);
            }
            this.config = existing;
            ++groupId;
            Debug.Log($"[BDArmory.BDAEvolution]: Resuming evolutionId: {evolutionId}, groupId: {groupId}");

            // Resume running.
            evoCoroutine = StartCoroutine(ExecuteEvolution());
        }

        public void StopEvolution()
        {
            if (evoCoroutine == null)
            {
                // Debug.Log("[BDArmory.BDAEvolution]: Evolution not running");
                return;
            }
            // Debug.Log("[BDArmory.BDAEvolution]: Evolution stopping");
            status = EvolutionStatus.Idle;

            StopCoroutine(evoCoroutine);
            evoCoroutine = null;
        }

        private void CreateEvolutionConfig()
        {
            string configFile = string.Format("{0}/{1}.cfg", configDirectory, evolutionId);
            ConfigNode existing = null;
            if (File.Exists(configFile)) existing = ConfigNode.Load(configFile);
            if (existing == null)
            {
                existing = new ConfigNode();
            }
            if (!existing.HasNode("EVOLUTION"))
            {
                existing.AddNode("EVOLUTION");
            }
            ConfigNode evoNode = existing.GetNode("EVOLUTION");
            evoNode.AddValue("id", evolutionId);
            evoNode.AddValue("groupId", groupId);
            evoNode.AddValue("nextVariantId", nextVariantId);
            existing.Save(configFile);
            this.config = existing;
            SaveState();
        }

        private void SaveState()
        {
            if (spawnConfig == null) return; // No spawn config means it hasn't been runnning.
            spawnConfig.craftFiles = null; // We don't want to include the specific craft files in the spawn config.
            spawnConfig.teamCounts = null;
            var workingState = new EvolutionWorkingState
            {
                savegame = HighLogic.SaveFolder,
                evolutionId = evolutionId,
                spawnConfig = spawnConfig,
                // aggregateScores = aggregateScores
            };
            // Write everything to file.
            File.WriteAllLines(stateFile, new List<string>{
                JsonUtility.ToJson(workingState),
                JsonUtility.ToJson(workingState.spawnConfig), // We need to do this separately as Unity's JSON is really simplistic.
            });
        }

        public static EvolutionWorkingState LoadState()
        {
            var state = new EvolutionWorkingState();
            if (string.IsNullOrEmpty(configDirectory)) ConfigurePaths();
            if (File.Exists(stateFile))
            {
                try
                {
                    var strings = File.ReadAllLines(stateFile);
                    state = JsonUtility.FromJson<EvolutionWorkingState>(strings[0]);
                    state.spawnConfig = JsonUtility.FromJson<CircularSpawnConfig>(strings[1]);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BDArmory.BDAEvolution]: Failure to properly read evolution state file: " + e.Message);
                }
            }
            else
            { Debug.LogError($"[BDArmory.BDAEvolution]: Failed to find evolution.state file {stateFile}"); }
            return state;
        }

        private IEnumerator ExecuteEvolution()
        {
            // 1. generate variants for the latest seed craft
            // 2. run tournament
            // 3. compute weighted centroid variant
            // 4. repeat from 1

            status = EvolutionStatus.Preparing;
            while (status != EvolutionStatus.Idle) // Avoid unnecessary recursion.
            {
                Debug.Log(string.Format("[BDArmory.BDAEvolution]: Evolution next group {0}", groupId));

                status = EvolutionStatus.GeneratingVariants;
                GenerateVariants();

                status = EvolutionStatus.RunningTournament;
                yield return ExecuteTournament();

                status = EvolutionStatus.ProcessingResults;
                InterpretResults();

                if (TournamentAutoResume.Instance != null && TournamentAutoResume.Instance.CheckMemoryUsage()) yield break; // Auto-Quit before the next variants are generated.

                ++groupId;
            }
        }

        private void GenerateVariants()
        {
            ClearWorkingDirectory();

            var seedName = LoadSeedCraft();
            engine.Configure(craft, weightMapFile);

            // add the original
            // Note: should be called first before variants are created since there appears to be some mutation operations that are not fully isolated (debugging WIP)
            // Saving first also allows avoidance of needing to use craft.CreateCopy() for the reference craft
            var referenceName = string.Format("R{0}", groupId);
            SaveVariant(craft, referenceName);

            // generate dipolar variants for all primary axes
            var mutations = engine.GenerateMutations(BDArmorySettings.EVOLUTION_MUTATIONS_PER_HEAT);
            List<Variant> variants = new List<Variant>();
            foreach (var mutation in mutations)
            {
                ConfigNode newVariant = mutation.Apply(craft, engine);
                var id = nextVariantId;
                var name = GetNextVariantName();
                variants.Add(mutation.GetVariant(id.ToString(), name));
                SaveVariant(newVariant, name);
            }

            // select random adversary
            LoadAdversaryCraft();

            AddVariantGroupToConfig(new VariantGroup(groupId, seedName, referenceName, variants));
        }

        // deletes all craft files in the working directory
        private void ClearWorkingDirectory()
        {
            if (!Directory.Exists(workingDirectory)) Directory.CreateDirectory(workingDirectory);
            var info = new DirectoryInfo(workingDirectory);
            var files = info.GetFiles("*.craft").ToList();
            foreach (var file in files)
            {
                file.Delete();
            }
        }

        // attempts to load the latest seed craft and store it in memory
        private string LoadSeedCraft()
        {
            var info = new DirectoryInfo(seedDirectory);
            var seeds = info.GetFiles("*.craft").ToList();
            // TODO:
            // Pre-existing seed files that are overwritten keep the old creation time
            // (e.g. if the evolution program is stopped and restarted or started again on a new save) of seeds 
            // Need to check to make sure previous seed name doesn't exist when saving to avoid this
            // (can't use file modification time since LastWriteTime is only available on windows NT file systems)
            var latestSeed = seeds.OrderBy(e => e.CreationTimeUtc).Last().Name;
            Debug.Log(string.Format("[BDArmory.BDAEvolution]: Evolution using latest seed: {0}", latestSeed));
            ConfigNode node = ConfigNode.Load(string.Format("{0}/{1}", seedDirectory, latestSeed));
            this.craft = node;
            return latestSeed;
        }

        // attempts to load an adversary craft into the group
        private void LoadAdversaryCraft()
        {
            var info = new DirectoryInfo(adversaryDirectory);
            var adversaries = info.GetFiles("*.craft").ToList();
            if (adversaries.Count == 0)
            {
                Debug.Log("[BDArmory.BDAEvolution]: Evolution no adversaries found");
                return;
            }
            else if (adversaries.Count < BDArmorySettings.EVOLUTION_ANTAGONISTS_PER_HEAT)
            {
                Debug.Log("[BDArmory.BDAEvolution]: Evolution using all available adversaries");
                foreach (var a in adversaries)
                {
                    ConfigNode adversaryNode = ConfigNode.Load(string.Format("{0}/{1}", adversaryDirectory, a));
                    adversaryNode.Save(string.Format("{0}/{1}", workingDirectory, a));
                }
                return;
            }
            else
            {
                for (var k = 0; k < BDArmorySettings.EVOLUTION_ANTAGONISTS_PER_HEAT; k++)
                {
                    var index = UnityEngine.Random.Range(0, adversaries.Count);
                    var randomAdversary = adversaries[index].Name;
                    adversaries.RemoveAt(index);
                    Debug.Log(string.Format("[BDArmory.BDAEvolution]: Evolution using random adversary: {0}", randomAdversary));
                    ConfigNode node = ConfigNode.Load(string.Format("{0}/{1}", adversaryDirectory, randomAdversary));
                    node.Save(string.Format("{0}/{1}", workingDirectory, randomAdversary));
                }
            }
        }

        private string GetNextVariantName() => string.Format("V{1}", evolutionId, nextVariantId++);

        private void SaveVariant(ConfigNode variant, string name)
        {
            // explicitly assign the craft name
            variant.SetValue("ship", name);
            variant.Save(string.Format("{0}/{1}.craft", workingDirectory, name));
        }

        private void AddVariantGroupToConfig(VariantGroup group)
        {
            evolutionState.groups.Add(group);

            if (!config.HasNode("EVOLUTION"))
            {
                config.AddNode("EVOLUTION");
            }
            ConfigNode evoNode = config.GetNode("EVOLUTION");
            evoNode.SetValue("nextVariantId", nextVariantId);

            ConfigNode newGroup = config.AddNode("GROUP");
            newGroup.AddValue("id", groupId);
            newGroup.AddValue("seedName", group.seedName);
            newGroup.AddValue("referenceName", group.referenceName);

            foreach (var e in group.variants)
            {
                ConfigNode newVariant = newGroup.AddNode("VARIANT");
                newVariant.AddValue("id", e.id);
                newVariant.AddValue("name", e.name);
                foreach (var p in e.mutatedParts)
                {
                    ConfigNode newMutatedPart = newVariant.AddNode("MUTATION");
                    newMutatedPart.AddValue("partName", p.partName);
                    newMutatedPart.AddValue("moduleName", p.moduleName);
                    newMutatedPart.AddValue("paramName", p.paramName);
                    newMutatedPart.AddValue("referenceValue", p.referenceValue);
                    newMutatedPart.AddValue("value", p.value);
                }
            }

            var configFile = Path.Combine(configDirectory, evolutionId + ".cfg");
            config.Save(configFile);
        }

        private IEnumerator ExecuteTournament()
        {
            var spawner = CircularSpawning.Instance;

            // clear scores
            aggregateScores.Clear();

            var comp = BDACompetitionMode.Instance;
            var specialKills = new HashSet<AliveState> { AliveState.CleanKill, AliveState.HeadShot, AliveState.KillSteal };

            // run N tournaments and aggregate their scores
            for (heat = 0; heat < BDArmorySettings.EVOLUTION_HEATS_PER_GROUP; heat++)
            {
                var wait = new WaitForFixedUpdate();
                spawnConfig.craftFiles = null; // We don't want to include the specific craft files in the spawn config.
                spawnConfig.teamCounts = null;
                SpawnUtils.ResetVesselNamingDeconfliction();
                spawner.SpawnAllVesselsOnce(spawnConfig);
                while (spawner.vesselsSpawning)
                    yield return wait;
                if (!spawner.vesselSpawnSuccess)
                {
                    Debug.Log("[BDArmory.BDAEvolution]: Vessel spawning failed.");
                    yield break;
                }
                yield return wait;

                BDACompetitionMode.Instance.StartCompetitionMode(BDArmorySettings.COMPETITION_DISTANCE, BDArmorySettings.COMPETITION_START_DESPITE_FAILURES);
                yield return new WaitForSeconds(5); // wait 5sec for stability

                while (BDACompetitionMode.Instance.competitionStarting || BDACompetitionMode.Instance.competitionIsActive)
                {
                    // Wait for the competition to finish 
                    yield return new WaitForSeconds(1);
                }

                // aggregate scores
                var scores = comp.Scores.ScoreData;
                var activeGroup = evolutionState.groups.Last();
                List<string> playerNames = new List<string>();
                playerNames.AddRange(activeGroup.variants.Select(e => e.name));
                playerNames.Add(activeGroup.referenceName);
                foreach (var name in playerNames)
                {
                    if (!aggregateScores.ContainsKey(name))
                    {
                        aggregateScores[name] = new Dictionary<string, float>();
                    }
                    if (!scores.ContainsKey(name))
                    {
                        Debug.LogError($"[BDArmory.BDAEvolution]: Variant {name} missing from scores! Valid names were " + string.Join("; ", scores.Keys));
                        continue;
                    }
                    var scoreData = scores[name];
                    var kills = scores.Values.Count(e => specialKills.Contains(e.aliveState) && e.lastPersonWhoDamagedMe == name);
                    if (aggregateScores[name].ContainsKey("kills"))
                    {
                        aggregateScores[name]["kills"] += kills;
                    }
                    else
                    {
                        aggregateScores[name]["kills"] = kills;
                    }
                    if (aggregateScores[name].ContainsKey("hits"))
                    {
                        aggregateScores[name]["hits"] += scoreData.hits + scoreData.rocketStrikes;
                    }
                    else
                    {
                        aggregateScores[name]["hits"] = scoreData.hits + scoreData.rocketStrikes;
                    }
                    if (aggregateScores[name].ContainsKey("shots"))
                    {
                        aggregateScores[name]["shots"] += scoreData.shotsFired + scoreData.rocketsFired;
                    }
                    else
                    {
                        aggregateScores[name]["shots"] = scoreData.shotsFired + scoreData.rocketsFired;
                    }
                    Debug.Log(string.Format("[BDArmory.BDAEvolution]: Evolution aggregated score data for {0}. kills: {1}, hits: {2}, shots: {3}", name, aggregateScores[name]["kills"], aggregateScores[name]["hits"], aggregateScores[name]["shots"]));
                }
            }
        }

        private void InterpretResults()
        {
            // compute scores for the dipolar variants
            var activeGroup = evolutionState.groups.Last();
            Debug.Log(string.Format("[BDArmory.BDAEvolution]: Evolution compute scores for {0}", activeGroup.id));
            Dictionary<string, float> scores = ComputeScores(activeGroup);

            // compute weighted centroid from the dipolar variants
            Debug.Log(string.Format("[BDArmory.BDAEvolution]: Evolution compute weighted centroid for {0}", activeGroup.id));
            var maxScore = activeGroup.variants.Select(e => scores[e.name]).Max();
            var referenceScore = scores[activeGroup.referenceName];
            if (maxScore > 0 && maxScore > referenceScore)
            {
                ConfigNode newCraft = craft.CreateCopy();

                // compute weighted contributions
                // map of part/module/param => delta
                Dictionary<string, Dictionary<string, Dictionary<string, float>>> agg = new Dictionary<string, Dictionary<string, Dictionary<string, float>>>();
                Dictionary<string, Dictionary<string, Dictionary<string, float>>> rvals = new Dictionary<string, Dictionary<string, Dictionary<string, float>>>();

                // feedback is based on the scores for each axis
                Dictionary<string, Dictionary<int, float>> axisScores = new Dictionary<string, Dictionary<int, float>>();

                foreach (var variant in activeGroup.variants)
                {
                    // normalize scores for weighted contribution
                    // TODO: this is probably a bug. basis should likely be referenceScore.
                    var score = scores[variant.name] / maxScore;

                    // track feedback score
                    if (!axisScores.ContainsKey(variant.key))
                    {
                        axisScores[variant.key] = new Dictionary<int, float>();
                    }
                    axisScores[variant.key][variant.direction] = scores[variant.name] - referenceScore;

                    foreach (var part in variant.mutatedParts)
                    {
                        var partContribution = part.value - part.referenceValue;
                        var weightedContribution = partContribution * score;
                        Debug.Log(string.Format("[BDArmory.BDAEvolution]: Evolution variant {0} score: {1}, part: {2}, module: {3}, key: {4}, value: {5}, ref: {6}", variant.name, score, part.partName, part.moduleName, part.paramName, part.value, part.referenceValue));
                        if (agg.ContainsKey(part.partName))
                        {
                            if (agg[part.partName].ContainsKey(part.moduleName))
                            {
                                if (agg[part.partName][part.moduleName].ContainsKey(part.paramName))
                                {
                                    agg[part.partName][part.moduleName][part.paramName] += weightedContribution;
                                }
                                else
                                {
                                    agg[part.partName][part.moduleName][part.paramName] = weightedContribution;

                                    rvals[part.partName][part.moduleName][part.paramName] = part.referenceValue;
                                }
                            }
                            else
                            {
                                agg[part.partName][part.moduleName] = new Dictionary<string, float>();
                                agg[part.partName][part.moduleName][part.paramName] = weightedContribution;

                                rvals[part.partName][part.moduleName] = new Dictionary<string, float>();
                                rvals[part.partName][part.moduleName][part.paramName] = part.referenceValue;
                            }
                        }
                        else
                        {
                            agg[part.partName] = new Dictionary<string, Dictionary<string, float>>();
                            agg[part.partName][part.moduleName] = new Dictionary<string, float>();
                            agg[part.partName][part.moduleName][part.paramName] = weightedContribution;

                            rvals[part.partName] = new Dictionary<string, Dictionary<string, float>>();
                            rvals[part.partName][part.moduleName] = new Dictionary<string, float>();
                            rvals[part.partName][part.moduleName][part.paramName] = part.referenceValue;
                        }
                    }
                }

                // compute feedback for each axis
                foreach (var key in axisScores.Keys)
                {
                    if (axisScores[key].Count == 2)
                    {
                        // compute simple xor(negative < 0, positive > 0)
                        var negativeCondition = axisScores[key][-1] < 0;
                        var positiveCondition = axisScores[key][1] > 0;
                        if ((negativeCondition && !positiveCondition) || (!negativeCondition && positiveCondition))
                        {
                            // confirmed linearity
                            engine.Feedback(key, 0.25f);
                        }
                        else
                        {
                            // confirmed absence of linearity
                            engine.Feedback(key, -0.25f);
                        }
                    }
                    else
                    {
                        Debug.Log(string.Format("[BDArmory.BDAEvolution]: Evolution wrong score count computing feedback for {0}", key));
                    }
                }

                Debug.Log(string.Format("[BDArmory.BDAEvolution]: Evolution synthesizing new generation from {0} parts", agg.Keys.Count));
                foreach (var part in agg.Keys)
                {
                    foreach (var module in agg[part].Keys)
                    {
                        foreach (var param in agg[part][module].Keys)
                        {
                            var newValue = agg[part][module][param] + rvals[part][module][param];
                            List<ConfigNode> partNodes = engine.FindPartNodes(newCraft, part);
                            if (partNodes.Count > 0)
                            {
                                List<ConfigNode> moduleNodes = engine.FindModuleNodes(partNodes[0], module);
                                if (moduleNodes.Count > 0)
                                {
                                    Debug.Log(string.Format("[BDArmory.BDAEvolution]: Evolution mutated part: {0}, module: {1}, key: {2}, value: {3}", part, module, param, newValue));
                                    engine.MutateNode(moduleNodes[0], param, newValue);
                                }
                                else
                                {
                                    Debug.Log(string.Format("[BDArmory.BDAEvolution]: Evolution failed to find module {0}", module));
                                }
                            }
                            else
                            {
                                Debug.Log(string.Format("[BDArmory.BDAEvolution]: Evolution failed to find part {0}", part));
                            }
                        }
                    }
                }

                Debug.Log(string.Format("[BDArmory.BDAEvolution]: Evolution save result for {0}", activeGroup.id));
                newCraft.Save(string.Format("{0}/G{1}.craft", seedDirectory, activeGroup.id));
            }
            else
            {
                // all variants somehow worse; re-seed
                Debug.Log(string.Format("[BDArmory.BDAEvolution]: Evolution bad seed for {0}", activeGroup.id));
                // downvote all variant axes
                foreach (var variant in activeGroup.variants)
                {
                    engine.Feedback(variant.key, -0.25f);
                }
            }
        }

        private Dictionary<string, float> ComputeScores(VariantGroup group)
        {
            // compute a score for each variant
            var results = new Dictionary<string, float>();
            foreach (var p in group.variants)
            {
                results[p.name] = ScoreForPlayer(p.name);
            }
            // also compute a score for the reference craft
            results[group.referenceName] = ScoreForPlayer(group.referenceName);
            return results;
        }

        private float ScoreForPlayer(string name)
        {
            var kills = aggregateScores[name]["kills"];
            var hits = aggregateScores[name]["hits"];
            var shots = aggregateScores[name]["shots"];
            var accuracy = Mathf.Clamp(shots > 0 ? (float)hits / (float)shots : 0, 0, 1);
            float score = 0;
            // score is a combination of kills, shots on target, hits, and accuracy
            float[] weights = new float[] { 1f, 0.002f, 0.01f, 5f };
            float[] values = new float[] { kills, shots, hits, accuracy };
            for (var k = 0; k < weights.Length; k++)
            {
                score += weights[k] * values[k];
            }
            Debug.Log(string.Format("[BDArmory.BDAEvolution]: Evolution ScoreForPlayer({0} => {1}) raw: [{2}, {3}, {4}, {5}]", name, score, kills, shots, hits, accuracy));
            return score;
        }
    }
}
