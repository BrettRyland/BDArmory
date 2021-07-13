using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BDArmory.Evolution
{
    public class VariantEngine
    {
        const float crystalRadius = 0.1f;

        private string weightMapFile;

        private Dictionary<string, float> mutationWeightMap = new Dictionary<string, float>();

        private Dictionary<string, ConfigNode> nodeMap = new Dictionary<string, ConfigNode>();

        List<string> includedModules = new List<string>() { "ModuleGimbal", "ModuleControlSurface", "BDModulePilotAI", "MissileFire" };

        List<string> includedParams = new List<string>()
        {
            "steerMult",                        // ModulePilot
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
            "maxAltitude",
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
            "controlSurfaceLag",
            "targetScanInterval",               // ModuleWeapon
            "fireBurstLength",
            "guardAngle",
            "guardRange",
            "gunRange",
            //"maxMissilesOnTarget",
            "targetBias",
            "targetWeightRange",
            "targetWeightATA",
            "targetWeightAoD",
            "targetWeightAccel",
            "targetWeightClosureTime",
            "targetWeightWeaponNumber",
            "targetWeightMass",
            "targetWeightFriendliesEngaging",
            "targetWeightThreat",
            //"cmThreshold",
            //"cmRepetition",
            //"cmInterval",
            //"cmWaitTime",
            "gimbalLimiter",                    // ModuleGimbal
            "authorityLimiter"                  // ModuleControlSurface
        };

        public void Configure(ConfigNode craft, string weightMapFile)
        {
            this.weightMapFile = weightMapFile;

            // build map for higher performance access
            BuildNodeMap(craft);

            // try to load existing weight map file
            try
            {
                ConfigNode weightMapNode = ConfigNode.Load(weightMapFile);
                LoadWeightMap(weightMapNode);
            }
            catch(Exception)
            {
                // otherwise init with random weights
                InitializeWeightMap(craft);
                SaveWeightMap();
            }
        }

        public void Feedback(string key, float weight)
        {
            string[] components = key.Split('/');
            if(components.Length != 3)
            {
                Debug.Log(string.Format("Evolution VariantEngine Feedback {0} => {1}", key, weight));
                return;
            }
            string part = components[0], module = components[1], param = components[2];
            Backpropagate(part, module, param, weight);

            if( !SaveWeightMap() )
            {
                Debug.Log("Evolution VariantEngine failed to save weight map");
            }
        }

        private void BuildNodeMap(ConfigNode craft)
        {
            Debug.Log("Evolution VariantEngine BuildNodeMap");
            nodeMap.Clear();

            // use a fifo queue to recurse through the tree
            List<ConfigNode> nodeQueue = new List<ConfigNode>();
            nodeQueue.Add(craft);

            while( nodeQueue.Count > 0 )
            {
                var nextNode = nodeQueue[0];
                nodeQueue.RemoveAt(0);
                if( nextNode == null )
                {
                    Debug.Log("Evolution VariantEngine weird null nextNode");
                    break;
                }

                // for part nodes, insert into map
                if( nextNode.name == "PART" )
                {
                    // insert node into map
                    var partName = nextNode.GetValue("part");
                    if (nodeMap.ContainsKey(partName))
                    {
                        Debug.Log(string.Format("Evolution VariantEngine found duplicate part {0}", partName));
                        break;
                    }
                    nodeMap[partName] = nextNode;
                }

                // add children to the queue
                foreach (var node in nextNode.GetNodes().Where(e => e.name == "PART"))
                {
                    nodeQueue.Add(node);
                }
            }
        }

        public ConfigNode GetNode(string partName)
        {
            return nodeMap[partName];
        }

        public List<ConfigNode> GetNodes(List<string> partNames)
        {
            List<ConfigNode> results = new List<ConfigNode>();
            foreach (var partName in partNames)
            {
                var node = nodeMap[partName];
                if (node != null)
                {
                    results.Add(node);
                }
            }
            return results;
        }

        private void LoadWeightMap(ConfigNode weightMapNode)
        {
            Debug.Log("Evolution VariantEngine LoadWeightMap");
            // start with a fresh map
            mutationWeightMap.Clear();

            // extract weights from the map
            foreach (var key in weightMapNode.values.DistinctNames())
            {
                var value = weightMapNode.GetValue(key);
                try
                {
                    mutationWeightMap[key] = float.Parse(value);
                }
                catch (Exception e)
                {
                    Debug.Log(string.Format("Evolution VariantEngine failed to parse value {0} for key {1}: {2}", value, key, e));
                }
            }
        }

        private bool SaveWeightMap()
        {
            Debug.Log(string.Format("Evolution VariantEngine SaveWeightMap to {0}", weightMapFile));
            ConfigNode weights = new ConfigNode();
            foreach (var key in mutationWeightMap.Keys)
            {
                weights.AddValue(key, mutationWeightMap[key]);
            }
            return weights.Save(weightMapFile);
        }

        private void InitializeWeightMap(ConfigNode craft, bool shouldRandomize = true)
        {
            Debug.Log("Evolution VariantEngine InitializeWeightMap");
            string[] paramModules = new string[] { "BDModulePilotAI", "MissileFire" };
            // start with a fresh map
            mutationWeightMap.Clear();

            var rng = new System.Random();
            // find all parts
            List<ConfigNode> foundParts = new List<ConfigNode>();
            FindMatchingNode(craft, "PART", foundParts);
            //Debug.Log(string.Format("Evolution VariantEngine init found {0} parts", foundParts.Count));
            foreach (var part in foundParts)
            {
                List<ConfigNode> foundModules = new List<ConfigNode>();
                FindMatchingNode(part, "MODULE", foundModules);
                var filteredModules = foundModules.Where(e => paramModules.Contains(e.GetValue("name"))).ToList();
                //Debug.Log(string.Format("Evolution VariantEngine init part {0} found {1} modules", part.GetValue("part"), foundModules.Count));
                foreach (var module in filteredModules)
                {
                    var filteredValues = includedParams.Where(e => module.HasValue(e)).ToList();
                    //Debug.Log(string.Format("Evolution VariantEngine init part {0} module {1} found {2} params", part.GetValue("part"), module.GetValue("name"), filteredValues.Count));
                    foreach (var param in filteredValues)
                    {
                        var key = MutationKey(part.GetValue("part"), module.GetValue("name"), param);
                        mutationWeightMap[key] = 1.0f;
                    }
                }
            }

            // check for control surfaces
            CheckSymmetry(craft, "ModuleControlSurface", "authorityLimiter");

            // check for engine gimbals
            CheckSymmetry(craft, "ModuleGimbal", "gimbalLimiter");

            if ( shouldRandomize )
            {
                Debug.Log(string.Format("Evolution VariantEngine randomizing weight map with {0} keys", mutationWeightMap.Count));
                // randomize weights slightly
                var keys = mutationWeightMap.Keys.ToList();
                foreach (var key in keys)
                {
                    mutationWeightMap[key] += (float)rng.Next(0, 100) / 10000.0f - 0.005f;
                }
            }
        }

        private void CheckSymmetry(ConfigNode craft, string moduleName, string paramName)
        {
            List<ConfigNode> foundModules = FindModuleNodes(craft, moduleName);
            foreach (var node in foundModules)
            {
                var parentPartNode = FindParentPart(craft, node);
                var partName = parentPartNode.GetValue("part");
                // check for symmetry grouping
                if (parentPartNode.HasValue("sym"))
                {
                    // is it mirror or radial symmetry?
                    if (parentPartNode.GetValue("symMethod") == "Radial")
                    {
                        Debug.Log(string.Format("Evolution VariantEngine RadialSymmetry for {0}", partName));
                        // multiple other parts
                        List<string> symParts = parentPartNode.GetValues("sym").ToList();
                        symParts.Add(partName);
                        var aggPartName = string.Join(",", symParts.OrderBy(e => e));
                        var key = MutationKey(aggPartName, moduleName, paramName);
                        mutationWeightMap[key] = 1.0f;
                    }
                    else
                    {
                        Debug.Log(string.Format("Evolution VariantEngine MirrorSymmetry for {0}", partName));
                        // just one other part
                        var siblingPartName = parentPartNode.GetValue("sym");
                        string[] aggParts = new string[] { partName, siblingPartName };
                        var aggPartName = string.Join(",", aggParts.OrderBy(e => e));
                        var key = MutationKey(aggPartName, moduleName, paramName);
                        mutationWeightMap[key] = 1.0f;
                    }
                }
                else
                {
                    Debug.Log(string.Format("Evolution VariantEngine NoSymmetry for {0} with {1}", partName, parentPartNode.GetValues("sym")));
                    // no symmetry, just one part
                    var key = MutationKey(partName, moduleName, paramName);
                    mutationWeightMap[key] = 1.0f;
                }
            }
        }

        public void Backpropagate(string part, string module, string param, float weight)
        {
            var key = MutationKey(part, module, param);
            var clampedWeight = Math.Max(-1, Math.Min(weight, 1));
            var multiplier = 1.0f + (float)(2.0*Math.Atan(clampedWeight)/Math.PI);
            Debug.Log(string.Format("Evolution VariantEngine Backpropagate {0} => {1} ({2})", key, clampedWeight, multiplier));
            mutationWeightMap[key] *= multiplier;
        }

        public string MutationKey(string part, string module, string param)
        {
            return string.Format("{0}/{1}/{2}", part, module, param);
        }

        // THE NEW WAY
        public List<VariantMutation> GenerateMutations(int mutationsPerGroup)
        {
            List<VariantMutation> mutations = new List<VariantMutation>();
            // order the mutation weight map by weight and select N elements
            List<string> bestOptions = mutationWeightMap
                .OrderByDescending(e => e.Value)
                .Select(e => e.Key)
                .Take(mutationsPerGroup)
                .ToList();
            foreach (var e in bestOptions)
            {
                mutations.AddRange(KeyToMutations(e));
            }
            return mutations;
        }

        private List<VariantMutation> KeyToMutations(string key)
        {
            string part;
            string module;
            string param;
            string[] components = key.Split('/');
            if( components.Length != 3 )
            {
                throw new Exception(string.Format("VariantEngine::KeyToMutation wrong number of key components: {0}", key));
            }
            part = components[0];
            module = components[1];
            param = components[2];

            switch (module)
            {
                case "MissileFire":
                    return GenerateWeaponManagerNudgeMutation(param, key);
                case "BDModulePilotAI":
                    return GeneratePilotAINudgeMutation(param, key);
                case "ModuleControlSurface":
                    return GenerateControlSurfaceMutation(part, key);
                    //break;
                case "ModuleGimbal":
                    return GenerateEngineGimbalMutation(part, key);
                    //break;
            }
            throw new Exception(string.Format("VariantEngine bad key: {0}", key));
        }

        // THE OLD WAY
        //public List<VariantMutation> GenerateMutations(ConfigNode craft, int mutationsPerGroup)
        //{
        //    List<VariantMutation> mutations = new List<VariantMutation>();
        //    while( mutations.Count() < mutationsPerGroup )
        //    {
        //        var remainingMutations = mutationsPerGroup - mutations.Count();
        //        var guess = UnityEngine.Random.Range(0, 100);
        //        if (guess < 25)
        //        {
        //            // sometimes mutate control surfaces
        //            var csMutations = GenerateControlSurfaceMutation(craft);
        //            mutations.AddRange(csMutations);
        //        }
        //        else if( guess < 50 )
        //        {
        //            // sometimes mutate engine gimbal
        //            var egMutations = GenerateEngineGimbalMutation(craft);
        //            mutations.AddRange(egMutations);
        //        }
        //        else if( guess < 75 )
        //        {
        //            // sometimes mutate weapon manager
        //            var wmMutations = GenerateWeaponManagerMutation(remainingMutations);
        //            mutations.AddRange(wmMutations);
        //        }
        //        else
        //        {
        //            // sometimes mutate pilot AI
        //            var aiMutations = GeneratePilotAIMutation(remainingMutations);
        //            mutations.AddRange(aiMutations);
        //        }
        //    }
        //    return mutations;
        //}

        //private List<VariantMutation> GeneratePilotAIMutation(int count)
        //{
        //    var availableAxes = new List<string>() {
        //        "steerMult",
        //        "steerKiAdjust",
        //        "steerDamping",
        //        //"DynamicDampingMin",
        //        //"DynamicDampingMax",
        //        //"dynamicSteerDampingFactor",
        //        //"dynamicDampingPitch",
        //        //"DynamicDampingPitchMin",
        //        //"DynamicDampingPitchMax",
        //        //"dynamicSteerDampingPitchFactor",
        //        //"DynamicDampingYawMin",
        //        //"DynamicDampingYawMax",
        //        //"dynamicSteerDampingYawFactor",
        //        //"DynamicDampingRollMin",
        //        //"DynamicDampingRollMax",
        //        //"dynamicSteerDampingRollFactor",
        //        "defaultAltitude",
        //        "minAltitude",
        //        "maxSpeed",
        //        "takeOffSpeed",
        //        "minSpeed",
        //        "idleSpeed",
        //        "maxSteer",
        //        "maxBank",
        //        "maxAllowedGForce",
        //        "maxAllowedAoA",
        //        "minEvasionTime",
        //        "evasionThreshold",
        //        "evasionTimeThreshold",
        //        "extendMult",
        //        "turnRadiusTwiddleFactorMin",
        //        "turnRadiusTwiddleFactorMax",
        //        "controlSurfaceLag"
        //    };

        //    var results = new List<VariantMutation>();
        //    for (var k=0;k<count;k++)
        //    {
        //        var index = (int) UnityEngine.Random.Range(0, availableAxes.Count);
        //        var positivePole = new PilotAINudgeMutation(paramName: availableAxes[index], modifier: crystalRadius);
        //        results.Add(positivePole);
        //        var negativePole = new PilotAINudgeMutation(paramName: availableAxes[index], modifier: -crystalRadius);
        //        results.Add(negativePole);
        //        availableAxes.RemoveAt(index);
        //    }
        //    return results;
        //}

        private List<VariantMutation> GeneratePilotAINudgeMutation(string paramName, string key)
        {
            List<VariantMutation> results = new List<VariantMutation>();
            var positivePole = new PilotAINudgeMutation(paramName: paramName, modifier: crystalRadius, key, 1);
            results.Add(positivePole);
            var negativePole = new PilotAINudgeMutation(paramName: paramName, modifier: -crystalRadius, key, -1);
            results.Add(negativePole);
            return results;
        }

        //private List<VariantMutation> GenerateWeaponManagerMutation(int count)
        //{
        //    var availableAxes = new List<string>() {
        //        //"targetScanInterval",
        //        //"fireBurstLength",
        //        //"guardAngle",
        //        //"guardRange",
        //        "gunRange",
        //        //"maxMissilesOnTarget",
        //        "targetBias",
        //        "targetWeightRange",
        //        "targetWeightATA",
        //        "targetWeightAoD",
        //        "targetWeightAccel",
        //        "targetWeightClosureTime",
        //        "targetWeightWeaponNumber",
        //        "targetWeightMass",
        //        "targetWeightFriendliesEngaging",
        //        "targetWeightThreat",
        //        //"cmThreshold",
        //        //"cmRepetition",
        //        //"cmInterval",
        //        //"cmWaitTime"
        //    };

        //    var results = new List<VariantMutation>();
        //    for (var k=0;k<count;k++)
        //    {
        //        var index = (int)UnityEngine.Random.Range(0, availableAxes.Count);
        //        var positivePole = new WeaponManagerNudgeMutation(paramName: availableAxes[index], modifier: crystalRadius);
        //        results.Add(positivePole);
        //        var negativePole = new WeaponManagerNudgeMutation(paramName: availableAxes[index], modifier: -crystalRadius);
        //        results.Add(negativePole);
        //        availableAxes.RemoveAt(index);
        //    }
        //    return results;
        //}

        private List<VariantMutation> GenerateWeaponManagerNudgeMutation(string paramName, string key)
        {
            List<VariantMutation> results = new List<VariantMutation>();
            var positivePole = new WeaponManagerNudgeMutation(paramName: paramName, modifier: crystalRadius, key, 1);
            results.Add(positivePole);
            var negativePole = new WeaponManagerNudgeMutation(paramName: paramName, modifier: -crystalRadius, key, -1);
            results.Add(negativePole);
            return results;
        }


        //private int CraftControlSurfaceCount(ConfigNode craft)
        //{
        //    List<ConfigNode> modules = FindModuleNodes(craft, "ModuleControlSurface");
        //    return modules.Count;
        //}

        //private List<VariantMutation> GenerateControlSurfaceMutation(ConfigNode craft)
        //{
        //    var results = new List<VariantMutation>();
        //    // TODO: find a control surface to mutate
        //    List<ConfigNode> modules = FindModuleNodes(craft, "ModuleControlSurface");
        //    int axisMask;
        //    var maskRandomizer = UnityEngine.Random.Range(0, 100);
        //    if (maskRandomizer < 33)
        //    {
        //        axisMask = ControlSurfaceNudgeMutation.MASK_ROLL;
        //    }
        //    else if (maskRandomizer < 66)
        //    {
        //        axisMask = ControlSurfaceNudgeMutation.MASK_PITCH;
        //    }
        //    else
        //    {
        //        axisMask = ControlSurfaceNudgeMutation.MASK_YAW;
        //    }
        //    var positivePole = new ControlSurfaceNudgeMutation("authorityLimiter", crystalRadius, axisMask);
        //    var negativePole = new ControlSurfaceNudgeMutation("authorityLimiter", -crystalRadius, axisMask);
        //    results.Add(positivePole);
        //    results.Add(negativePole);
        //    return results;
        //}

        private List<VariantMutation> GenerateControlSurfaceMutation(string parts, string key)
        {
            string[] partNames = parts.Split(',');
            var positivePole = new ControlSurfaceNudgeMutation(partNames, "authorityLimiter", crystalRadius, key, 1);
            var negativePole = new ControlSurfaceNudgeMutation(partNames, "authorityLimiter", -crystalRadius, key, -1);
            var results = new List<VariantMutation>() { positivePole, negativePole };
            return results;
        }

        private bool CraftHasEngineGimbal(ConfigNode craft)
        {
            List<ConfigNode> gimbals = FindModuleNodes(craft, "ModuleGimbal");
            return gimbals.Count != 0;
        }

        //private List<VariantMutation> GenerateEngineGimbalMutation(ConfigNode craft)
        //{
        //    var results = new List<VariantMutation>();
        //    // TODO: find a engine gimbal to mutate
        //    List<ConfigNode> modules = FindModuleNodes(craft, "ModuleGimbal");
        //    int axisMask;
        //    var maskRandomizer = UnityEngine.Random.Range(0, 100);
        //    if (maskRandomizer < 33)
        //    {
        //        axisMask = ControlSurfaceNudgeMutation.MASK_ROLL;
        //    }
        //    else if (maskRandomizer < 66)
        //    {
        //        axisMask = ControlSurfaceNudgeMutation.MASK_PITCH;
        //    }
        //    else
        //    {
        //        axisMask = ControlSurfaceNudgeMutation.MASK_YAW;
        //    }
        //    var positivePole = new EngineGimbalNudgeMutation("gimbalLimiter", crystalRadius, axisMask);
        //    var negativePole = new EngineGimbalNudgeMutation("gimbalLimiter", -crystalRadius, axisMask);
        //    results.Add(positivePole);
        //    results.Add(negativePole);
        //    return results;
        //}

        private List<VariantMutation> GenerateEngineGimbalMutation(string parts, string key)
        {
            string[] partNames = parts.Split(',');
            var results = new List<VariantMutation>();
            var positivePole = new EngineGimbalNudgeMutation(partNames, "gimbalLimiter", crystalRadius, key, 1);
            var negativePole = new EngineGimbalNudgeMutation(partNames, "gimbalLimiter", -crystalRadius, key, -1);
            results.Add(positivePole);
            results.Add(negativePole);
            return results;
        }

        public ConfigNode GenerateNode(ConfigNode source, VariantOptions options)
        {
            // make a copy of the source and modify the copy
            var result = source.CreateCopy();

            foreach (var mutation in options.mutations)
            {
                mutation.Apply(result, this);
            }

            // return modified copy
            return result;
        }

        public bool FindValue(ConfigNode node, string nodeType, string nodeName, string paramName, out float result)
        {
            if (node.name == nodeType && node.HasValue("name") && node.GetValue("name").StartsWith(nodeName) && node.HasValue(paramName))
            {
                return float.TryParse(node.GetValue(paramName), out result);
            }
            foreach (var child in node.nodes)
            {
                if (FindValue((ConfigNode)child, nodeType, nodeName, paramName, out result))
                {
                    return true;
                }
            }
            result = 0;
            return false;
        }

        public List<ConfigNode> FindPartNodes(ConfigNode source, string partName)
        {
            List<ConfigNode> matchingParts = new List<ConfigNode>();
            FindMatchingNode(source, "PART", "part", partName, matchingParts);
            return matchingParts;
        }

        public List<ConfigNode> FindModuleNodes(ConfigNode source, string moduleName)
        {
            List<ConfigNode> matchingModules = new List<ConfigNode>();
            FindMatchingNode(source, "MODULE", "name", moduleName, matchingModules);
            return matchingModules;
        }

        public ConfigNode FindParentPart(ConfigNode rootNode, ConfigNode node)
        {
            if( rootNode.name == "PART" )
            {
                foreach (var child in rootNode.nodes)
                {
                    if( child == node )
                    {
                        return rootNode;
                    }
                }
            }
            foreach (var child in rootNode.nodes)
            {
                var found = FindParentPart((ConfigNode)child, node);
                if( found != null )
                {
                    return found;
                }
            }
            return null;
        }

        private void FindMatchingNode(ConfigNode source, string nodeType, string nodeParam, string nodeName, List<ConfigNode> found)
        {
            if (source.name == nodeType && source.HasValue(nodeParam) && source.GetValue(nodeParam).StartsWith(nodeName))
            {
                found.Add(source);
            }
            foreach (var child in source.GetNodes())
            {
                FindMatchingNode(child, nodeType, nodeParam, nodeName, found);
            }
        }

        private void FindMatchingNode(ConfigNode source, string nodeType, List<ConfigNode> found)
        {
            if( source.name == nodeType)
            {
                found.Add(source);
            }
            foreach (var child in source.GetNodes())
            {
                FindMatchingNode(child, nodeType, found);
            }
        }

        public bool MutateNode(ConfigNode node, string key, float value)
        {
            if (node.HasValue(key))
            {
                node.SetValue(key, value);
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool NudgeNode(ConfigNode node, string key, float modifier)
        {
            if (node.HasValue(key) && float.TryParse(node.GetValue(key), out float existingValue))
            {
                node.SetValue(key, existingValue * (1 + modifier));
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    public class VariantOptions
    {
        public List<VariantMutation> mutations;
        public VariantOptions(List<VariantMutation> mutations)
        {
            this.mutations = mutations;
        }
    }

}
