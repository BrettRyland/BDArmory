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
        public List<string> keys;
        public List<Variant> variants;
        public VariantGroup(int id, string seedName, string referenceName, List<string> keys, List<Variant> variants)
        {
            this.id = id;
            this.seedName = seedName;
            this.referenceName = referenceName;
            this.keys = keys;
            this.variants = variants;
        }
    }

    public class Variant
    {
        public string id;
        public string name;
        public List<string> keys;
        public List<float> values;
        public Variant(string id, string name, List<string> keys, List<float> values)
        {
            this.id = id;
            this.name = name;
            this.keys = keys;
            this.values = values;
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class BDAModuleEvolution : MonoBehaviour
    {
        public static BDAModuleEvolution Instance;

        private static string workingDirectory = "Autospawn";
        private static string configDirectory = string.Format("{0}/evolutions", workingDirectory);
        private static string seedDirectory = string.Format("{0}/seeds", configDirectory);

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
            engine = new VariantEngine(workingDirectory);
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
            var availableAxes = new List<string>() {
                "steerMult",
                "steerKiAdjust",
                "steerDamping",
                "DynamicDampingMin",
                "DynamicDampingMax",
                "dynamicSteerDampingFactor",
                "dynamicDampingPitch",
                "DynamicDampingPitchMin",
                "DynamicDampingPitchMax",
                "dynamicSteerDampingPitchFactor",
                "DynamicDampingYawMin",
                "DynamicDampingYawMax",
                "dynamicSteerDampingYawFactor",
                "DynamicDampingRollMin",
                "DynamicDampingRollMax",
                "dynamicSteerDampingRollFactor",
                "defaultAltitude",
                "minAltitude",
                "maxSpeed",
                "takeOffSpeed",
                "minSpeed",
                "idleSpeed",
                "maxSteer",
                "maxBank",
                "maxAllowedGForce",
                "maxAllowedAoA",
                "minEvasionTime",
                "evasionThreshold",
                "evasionTimeThreshold",
                "extendMult",
                "turnRadiusTwiddleFactorMin",
                "turnRadiusTwiddleFactorMax",
                "controlSurfaceLag"
            };

            // pick a subset of the axes to reduce 
            var dipoleAxes = new List<string>();
            for (var k = 0; k < 5; k++)
            {
                // pick a random param
                var index = (int) (new System.Random().NextDouble() * availableAxes.Count);
                var axis = availableAxes[index];
                dipoleAxes.Add(axis);
                availableAxes.RemoveAt(index);
            }

            var existingValues = dipoleAxes.Select(e =>
            {
                float result;
                if (engine.FindValue(craft, "MODULE", "BDModulePilotAI", e, out result))
                {
                    return result;
                }
                return 0f;
            });
            var variants = new List<Variant>();
            const float crystalRadius = 0.1f;
            for (var k = 0; k < dipoleAxes.Count; k++)
            {
                // generate two equal and opposite dipole variants along this axis
                var keys0 = new List<string>() { dipoleAxes[k] };
                var variant0 = engine.GenerateNode(craft, new VariantOptions(keys0, new List<float>() { 1 - crystalRadius }));
                var id0 = nextVariantId;
                var name0 = GetNextVariantName();
                if (engine.FindValue(variant0, "MODULE", "BDModulePilotAI", dipoleAxes[k], out float value0))
                {
                    variants.Add(new Variant(id0.ToString(), name0, keys0, new List<float>() { value0 }));
                    SaveVariant(variant0, name0);
                }

                var variant1 = engine.GenerateNode(craft, new VariantOptions(keys0, new List<float>() { 1 + crystalRadius }));
                var id1 = nextVariantId;
                var name1 = GetNextVariantName();
                if (engine.FindValue(variant1, "MODULE", "BDModulePilotAI", dipoleAxes[k], out float value1))
                {
                    variants.Add(new Variant(id1.ToString(), name1, keys0, new List<float>() { value1 }));
                    SaveVariant(variant1, name1);
                }
            }
            // add the original
            var referenceName = string.Format("R{0}", groupId);
            SaveVariant(craft.CreateCopy(), referenceName);

            AddVariantGroupToConfig(new VariantGroup(groupId, seedName, referenceName, dipoleAxes, variants));
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

        private string GetNextVariantName() => string.Format("V{1}", evolutionId, nextVariantId++);

        private void SaveVariant(ConfigNode variant, string name)
        {
            // explicitly assign the craft name
            variant.SetValue("ship", name);
            variant.Save(string.Format("{0}/{1}.craft", workingDirectory, name));
        }

        private void AddVariantGroupToConfig(VariantGroup group)
        {
            if( !config.HasNode("EVOLUTION") )
            {
                config.AddNode("EVOLUTION");
            }
            ConfigNode evoNode = config.GetNode("EVOLUTION");
            evoNode.SetValue("nextVariantId", nextVariantId);

            ConfigNode newGroup = config.AddNode("GROUP");
            newGroup.AddValue("id", groupId);
            newGroup.AddValue("seedName", group.seedName);
            newGroup.AddValue("keys", string.Join(", ", group.keys));

            foreach (var e in group.variants)
            {
                ConfigNode newVariant = newGroup.AddNode("VARIANT");
                newVariant.AddValue("id", e.id);
                newVariant.AddValue("name", e.name);
                newVariant.AddValue("keys", string.Join(", ", e.keys));
                newVariant.AddValue("values", string.Join(", ", e.values));
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
            Dictionary<string, float> scores = ComputeScores(activeGroup);

            // compute weighted centroid from the dipolar variants
            var maxScore = activeGroup.variants.Select(e => scores[e.name]).Max();
            var referenceScore = scores[activeGroup.referenceName];
            ConfigNode newCraft;
            if ( maxScore > referenceScore )
            {
                // found a better score in the variants; use them.
                List<float> normalizedWeights = activeGroup.variants.Select(e => scores[e.name] / maxScore).ToList();
                float[] weightedValues = new float[activeGroup.keys.Count()];
                for (var k=0; k<weightedValues.Length; k++)
                {
                    weightedValues[k] = 0;
                    for (var n=0; n<activeGroup.variants.Count(); n++)
                    {
                        var e = activeGroup.variants[n];
                        if (e.keys.Contains(activeGroup.keys[k]))
                        {
                            var index = e.keys.IndexOf(activeGroup.keys[k]);
                            weightedValues[k] += e.values[index] * normalizedWeights[n];
                        }
                    }
                }
                VariantOptions options = new VariantOptions(activeGroup.keys, weightedValues.ToList());
                newCraft = engine.GenerateNode(craft, options);
            }
            else
            {
                // all variants somehow worse; re-seed
                newCraft = craft;
            }
            newCraft.Save(string.Format("{0}/G{1}.craft", seedDirectory, activeGroup.id));
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
            var hits = scoreData.hitCounts.Values.Sum();
            var isDead = comp.DeathOrder.ContainsKey(name);
            var cleanKills = comp.whoCleanShotWho.Values.Count(e => e == name);
            var missileKills = comp.whoCleanShotWhoWithMissiles.Values.Count(e => e == name);
            var ramKills = comp.whoCleanRammedWho.Values.Count(e => e == name);
            var kills = cleanKills + missileKills + ramKills;
            float score = 0;
            float[] weights = new float[] { 1f, 0.01f, }; // 1K + 0.01H
            float[] values = new float[] { kills, hits };
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
            yield return ExecuteEvolution();
        }
    }
}
