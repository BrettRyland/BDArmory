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
            string[] components = key.Split(',');
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

        private void LoadWeightMap(ConfigNode weightMapNode)
        {
            Debug.Log("Evolution VariantEngine LoadWeightMap");
            // start with a fresh map
            mutationWeightMap.Clear();

            // extract weights from the map
            foreach (var key in weightMapNode.GetValues())
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
            // start with a fresh map
            mutationWeightMap.Clear();

            var rng = new System.Random();
            // find all parts
            List<ConfigNode> foundParts = new List<ConfigNode>();
            FindMatchingNode(craft, "PART", foundParts);
            Debug.Log(string.Format("Evolution VariantEngine init found {0} parts", foundParts.Count));
            foreach (var part in foundParts)
            {
                List<ConfigNode> foundModules = new List<ConfigNode>();
                FindMatchingNode(part, "MODULE", foundModules);
                var filteredModules = foundModules.Where(e => includedModules.Contains(e.GetValue("name"))).ToList();
                Debug.Log(string.Format("Evolution VariantEngine init part {0} found {1} modules", part.GetValue("part"), foundModules.Count));
                foreach (var module in filteredModules)
                {
                    var filteredValues = includedParams.Where(e => module.HasValue(e)).ToList();
                    Debug.Log(string.Format("Evolution VariantEngine init part {0} module {1} found {2} params", part.GetValue("part"), module.GetValue("name"), filteredValues.Count));
                    foreach (var param in filteredValues)
                    {
                        var key = MutationKey(part.GetValue("part"), module.GetValue("name"), param);
                        mutationWeightMap[key] = 1.0f;
                    }
                }
            }

            if( shouldRandomize )
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
        public List<VariantMutation> GenerateMutations(ConfigNode craft, int mutationsPerGroup)
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
                    return GenerateWeaponManagerNudgeMutation(param);
                case "BDModulePilotAI":
                    return GeneratePilotAINudgeMutation(param);
                case "ModuleControlSurface":
                    return GenerateControlSurfaceMutation(ControlSurfaceNudgeMutation.MASK_PITCH);
                    //break;
                case "ModuleGimbal":
                    return GenerateEngineGimbalMutation(ControlSurfaceNudgeMutation.MASK_PITCH);
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

        private List<VariantMutation> GeneratePilotAINudgeMutation(string paramName)
        {
            List<VariantMutation> results = new List<VariantMutation>();
            var positivePole = new PilotAINudgeMutation(paramName: paramName, modifier: crystalRadius);
            results.Add(positivePole);
            var negativePole = new PilotAINudgeMutation(paramName: paramName, modifier: -crystalRadius);
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

        private List<VariantMutation> GenerateWeaponManagerNudgeMutation(string paramName)
        {
            List<VariantMutation> results = new List<VariantMutation>();
            var positivePole = new WeaponManagerNudgeMutation(paramName: paramName, modifier: crystalRadius);
            results.Add(positivePole);
            var negativePole = new WeaponManagerNudgeMutation(paramName: paramName, modifier: -crystalRadius);
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

        private List<VariantMutation> GenerateControlSurfaceMutation(int axisMask)
        {
            var positivePole = new ControlSurfaceNudgeMutation("authorityLimiter", crystalRadius, axisMask);
            var negativePole = new ControlSurfaceNudgeMutation("authorityLimiter", -crystalRadius, axisMask);
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

        private List<VariantMutation> GenerateEngineGimbalMutation(int axisMask)
        {
            var results = new List<VariantMutation>();
            var positivePole = new EngineGimbalNudgeMutation("gimbalLimiter", crystalRadius, axisMask);
            var negativePole = new EngineGimbalNudgeMutation("gimbalLimiter", -crystalRadius, axisMask);
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
