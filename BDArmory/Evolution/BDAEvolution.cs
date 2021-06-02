using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BDArmory.Control;
using BDArmory.Core;
using BDArmory.Core.Extension;
using BDArmory.Evolution;
using BDArmory.UI;
using UnityEngine;

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
        public Variant(string id, string name, List<MutatedPart> mutatedParts)
        {
            this.id = id;
            this.name = name;
            this.mutatedParts = mutatedParts;
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

        private static string workingDirectory = "Autospawn";
        private static string configDirectory = string.Format("{0}/evolutions", workingDirectory);
        private static string seedDirectory = string.Format("{0}/seeds", configDirectory);
        private static string adversaryDirectory = string.Format("{0}/adversaries", configDirectory);

        private Coroutine evoCoroutine = null;

        private EvolutionStatus status = EvolutionStatus.Idle;
        public EvolutionStatus Status() { return status; }

        private EvolutionState evolutionState = null;

        private VariantEngine engine = null;

        // config node for evolution details
        private ConfigNode config = null;

        // root node of the active seed craft
        private ConfigNode craft = null;

        // evolution id
        private string evolutionId = null;
        public string GetId() { return evolutionId; }

        // group id
        private int groupId = 0;
        public int GetGroupId() { return groupId; }

        // next variant id
        private int nextVariantId = 0;

        private VariantOptions options;

        void Awake()
        {
            Debug.Log("Evolution awake");
            if (Instance)
            {
                Destroy(Instance);
            }

            Instance = this;
        }

        private void Start()
        {
            Debug.Log("Evolution start");
            engine = new VariantEngine();
            EvolutionWindow.Instance.ShowWindow();
        }

        public void StartEvolution()
        {
            if( evoCoroutine != null )
            {
                Debug.Log("Evolution already running");
                return;
            }
            Debug.Log("Evolution starting");
            status = EvolutionStatus.Preparing;

            // initialize evolution
            nextVariantId = 1;
            groupId = 1;
            evolutionId = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
            evolutionState = new EvolutionState(evolutionId, status, new List<VariantGroup>());

            // create new config
            CreateEvolutionConfig();

            evoCoroutine = StartCoroutine(ExecuteEvolution());
        }

        public void StopEvolution()
        {
            if( evoCoroutine == null )
            {
                Debug.Log("Evolution not running");
                return;
            }
            Debug.Log("Evolution stopping");
            status = EvolutionStatus.Idle;

            StopCoroutine(evoCoroutine);
            evoCoroutine = null;
        }

        private void CreateEvolutionConfig()
        {
            string configFile = string.Format("{0}/{1}.cfg", configDirectory, evolutionId);
            ConfigNode existing = ConfigNode.Load(configFile);
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
        }

        private IEnumerator ExecuteEvolution()
        {
            // 1. generate variants for the latest seed craft
            // 2. run tournament
            // 3. compute weighted centroid variant
            // 4. repeat from 1

            status = EvolutionStatus.GeneratingVariants;
            GenerateVariants();

            status = EvolutionStatus.RunningTournament;
            yield return ExecuteTournament();

            status = EvolutionStatus.ProcessingResults;
            InterpretResults();

            status = EvolutionStatus.Preparing;
            yield return RepeatUntilStable();
        }

        private void GenerateVariants()
        {
            ClearWorkingDirectory();

            var seedName = LoadSeedCraft();

            // generate dipolar variants for all primary axes
            var mutations = engine.GenerateMutations(craft);
            List<Variant> variants = new List<Variant>();
            foreach (var mutation in mutations)
            {
                ConfigNode newVariant = craft.CreateCopy();
                mutation.Apply(newVariant, engine);
                var id = nextVariantId;
                var name = GetNextVariantName();
                variants.Add(mutation.GetVariant(id.ToString(), name));
                SaveVariant(newVariant, name);
            }

            //// pick a subset of the axes to reduce 
            //var dipoleAxes = new List<string>();
            //for (var k = 0; k < 5; k++)
            //{
            //    // pick a random param
            //    var index = (int) (new System.Random().NextDouble() * availableAxes.Count);
            //    var axis = availableAxes[index];
            //    dipoleAxes.Add(axis);
            //    availableAxes.RemoveAt(index);
            //}

            //var existingValues = dipoleAxes.Select(e =>
            //{
            //    float result;
            //    if (engine.FindValue(craft, "MODULE", "BDModulePilotAI", e, out result))
            //    {
            //        return result;
            //    }
            //    return 0f;
            //}).ToList();
            //var variants = new List<Variant>();
            //for (var k = 0; k < dipoleAxes.Count; k++)
            //{
            //    // generate two equal and opposite dipole variants along this axis
            //    var keys0 = new List<string>() { dipoleAxes[k] };
            //    var values0 = new List<float>() { existingValues[k] * (1 - crystalRadius) };
            //    var variant0 = engine.GenerateNode(craft, new VariantOptions(keys0, values0));
            //    var id0 = nextVariantId;
            //    var name0 = GetNextVariantName();
            //    variants.Add(new Variant(id0.ToString(), name0, keys0, values0));
            //    SaveVariant(variant0, name0);

            //    var values1 = new List<float>() { existingValues[k] * (1 + crystalRadius) };
            //    var variant1 = engine.GenerateNode(craft, new VariantOptions(keys0, values1));
            //    var id1 = nextVariantId;
            //    var name1 = GetNextVariantName();
            //    variants.Add(new Variant(id1.ToString(), name1, keys0, values1));
            //    SaveVariant(variant1, name1);
            //}
            // add the original
            var referenceName = string.Format("R{0}", groupId);
            SaveVariant(craft.CreateCopy(), referenceName);

            // select random adversary
            LoadAdversaryCraft();

            AddVariantGroupToConfig(new VariantGroup(groupId, seedName, referenceName, variants));
        }

        // deletes all craft files in the working directory
        private void ClearWorkingDirectory()
        {
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
            var latestSeed = seeds.OrderBy(e => e.CreationTimeUtc).Last().Name;
            Debug.Log(string.Format("Evolution using latest seed: {0}", latestSeed));
            ConfigNode node = ConfigNode.Load(string.Format("{0}/{1}", seedDirectory, latestSeed));
            this.craft = node;
            return latestSeed;
        }

        // attempts to load an adversary craft into the group
        private void LoadAdversaryCraft()
        {
            var info = new DirectoryInfo(adversaryDirectory);
            var adversaries = info.GetFiles("*.craft").ToList();
            if( adversaries.Count == 0 )
            {
                Debug.Log("Evolution no adversaries found");
                return;
            }
            var index = UnityEngine.Random.Range(0, adversaries.Count);
            var randomAdversary = adversaries[index].Name;
            Debug.Log(string.Format("Evolution using random adversary: {0}", randomAdversary));
            ConfigNode node = ConfigNode.Load(string.Format("{0}/{1}", adversaryDirectory, randomAdversary));
            node.Save(string.Format("{0}/{1}", workingDirectory, randomAdversary));
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

            if( !config.HasNode("EVOLUTION") )
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

            string configFile = string.Format("{0}/{1}.cfg", configDirectory, evolutionId);
            config.Save(configFile);
        }

        private IEnumerator ExecuteTournament()
        {
            var spawner = VesselSpawner.Instance;
            var spawnConfig = new VesselSpawner.SpawnConfig(
                BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                BDArmorySettings.VESSEL_SPAWN_ALTITUDE,
                BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                BDArmorySettings.VESSEL_SPAWN_EASE_IN_SPEED,
                true,
                true,
                0,
                null,
                null);
            spawner.SpawnAllVesselsOnce(spawnConfig);
            while (spawner.vesselsSpawning)
                yield return new WaitForFixedUpdate();
            if (!spawner.vesselSpawnSuccess)
            {
                Debug.Log("[Evolution] Vessel spawning failed.");
                yield break;
            }
            yield return new WaitForFixedUpdate();

            BDACompetitionMode.Instance.StartCompetitionMode(0);
            yield return new WaitForSeconds(5); // wait 5sec for stability

            while (BDACompetitionMode.Instance.competitionIsActive)
            {
                // Wait for the competition to finish 
                yield return new WaitForSeconds(1);
            }
        }

        private void InterpretResults()
        {
            // compute scores for the dipolar variants
            var activeGroup = evolutionState.groups.Last();
            Debug.Log(string.Format("Evolution compute scores for {0}", activeGroup.id));
            Dictionary<string, float> scores = ComputeScores(activeGroup);

            // compute weighted centroid from the dipolar variants
            Debug.Log(string.Format("Evolution compute weighted centroid for {0}", activeGroup.id));
            var maxScore = activeGroup.variants.Select(e => scores[e.name]).Max();
            var referenceScore = scores[activeGroup.referenceName];
            if ( maxScore > 0 && maxScore > referenceScore )
            {
                ConfigNode newCraft = craft.CreateCopy();

                // compute weighted contributions
                // map of part/module/param => delta
                Dictionary<string, Dictionary<string, Dictionary<string, float>>> agg = new Dictionary<string, Dictionary<string, Dictionary<string, float>>>();
                Dictionary<string, Dictionary<string, Dictionary<string, float>>> rvals = new Dictionary<string, Dictionary<string, Dictionary<string, float>>>();
                foreach (var variant in activeGroup.variants)
                {
                    // normalize scores for weighted contribution
                    var score = scores[variant.name] / maxScore;
                    foreach (var part in variant.mutatedParts)
                    {
                        var partContribution = part.value - part.referenceValue;
                        var weightedContribution = partContribution * score;
                        Debug.Log(string.Format("Evolution variant {0} score: {1}, part: {2}, module: {3}, key: {4}, value: {5}, ref: {6}", variant.name, score, part.partName, part.moduleName, part.paramName, part.value, part.referenceValue));
                        if ( agg.ContainsKey(part.partName) )
                        {
                            if( agg[part.partName].ContainsKey(part.moduleName) )
                            {
                                if( agg[part.partName][part.moduleName].ContainsKey(part.paramName) )
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

                Debug.Log(string.Format("Evolution synthesizing new generation from {0} parts", agg.Keys.Count));
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
                                    Debug.Log(string.Format("Evolution mutated part: {0}, module: {1}, key: {2}, value: {3}", part, module, param, newValue));
                                    engine.MutateNode(moduleNodes[0], param, newValue);
                                }
                                else
                                {
                                    Debug.Log(string.Format("Evolution failed to find module {0}", module));
                                }
                            }
                            else
                            {
                                Debug.Log(string.Format("Evolution failed to find part {0}", part));
                            }
                        }
                    }
                }
                Debug.Log("Evolution synthesizing new generation");

                // found a better score in the variants; use them.
                //List<float> normalizedWeights = activeGroup.variants.Select(e => scores[e.name] / maxScore).ToList();
                //float[] weightedValues = new float[activeGroup.keys.Count()];
                //for (var k=0; k<weightedValues.Length; k++)
                //{
                //    Debug.Log(string.Format("Evolution centroid key {0}", activeGroup.keys[k]));
                //    weightedValues[k] = 0;
                //    for (var n=0; n<activeGroup.variants.Count(); n++)
                //    {
                //        var e = activeGroup.variants[n];
                //        if (e.keys.Contains(activeGroup.keys[k]))
                //        {
                //            var index = e.keys.IndexOf(activeGroup.keys[k]);
                //            var contribution = (e.values[index] - activeGroup.referenceValues[k]) * normalizedWeights[n];
                //            Debug.Log(string.Format("Evolution variant: {0}, weight: {1}, value: {2}, contribution: {3}", activeGroup.keys[k], normalizedWeights[n], e.values[index], contribution));
                //            weightedValues[k] += contribution;
                //        }
                //    }
                //    Debug.Log(string.Format("Evolution computed key: {0}, value: {1}, referenceValue: {2}", activeGroup.keys[k], weightedValues[k], activeGroup.referenceValues[k]));
                //    weightedValues[k] += activeGroup.referenceValues[k];
                //}
                //VariantOptions options = new VariantOptions(activeGroup.keys, weightedValues.ToList());
                //newCraft = engine.GenerateNode(craft, options);
                Debug.Log(string.Format("Evolution save result for {0}", activeGroup.id));
                newCraft.Save(string.Format("{0}/G{1}.craft", seedDirectory, activeGroup.id));
            }
            else
            {
                // all variants somehow worse; re-seed
                Debug.Log(string.Format("Evolution bad seed for {0}", activeGroup.id));
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
            var comp = BDACompetitionMode.Instance;
            var scoreData = comp.Scores[name];
            var shots = scoreData.shotsFired;
            var hits = scoreData.hitCounts.Values.Sum();
            var accuracy = shots > 0 ? (float)hits / (float)shots : 0;
            var isDead = comp.DeathOrder.ContainsKey(name);
            var cleanKills = comp.whoCleanShotWho.Values.Count(e => e == name);
            var missileKills = comp.whoCleanShotWhoWithMissiles.Values.Count(e => e == name);
            var ramKills = comp.whoCleanRammedWho.Values.Count(e => e == name);
            var kills = cleanKills + missileKills + ramKills;
            float score = 0;
            // score is a combination of kills, shots on target, hits, and accuracy
            float[] weights = new float[] { 1f, 0.002f, 0.01f, 5f };
            float[] values = new float[] { kills, shots, hits, accuracy };
            for (var k=0; k<weights.Length; k++)
            {
                score += weights[k] * values[k];
            }
            Debug.Log(string.Format("Evolution ScoreForPlayer({0} => {1})", name, score));
            return score;
        }

        private IEnumerator RepeatUntilStable()
        {
            // TODO: evaluate stability and decide to continue or done
            // for now, just continue until manually canceled
            groupId += 1;
            Debug.Log(string.Format("Evolution next group {0}", groupId));
            yield return ExecuteEvolution();
        }
    }
}
