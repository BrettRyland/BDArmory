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

        public List<VariantMutation> GenerateMutations(ConfigNode craft)
        {
            List<VariantMutation> mutations = new List<VariantMutation>();
            const int mutationsPerGroup = 10;

            while( mutations.Count() < mutationsPerGroup )
            {
                var guess = UnityEngine.Random.Range(0, 100);
                if (guess < 25)
                {
                    // sometimes mutate control surfaces
                    var csMutations = GenerateControlSurfaceMutation(craft);
                    mutations.AddRange(csMutations);
                }
                else if( guess < 50 )
                {
                    // sometimes mutate engine gimbal
                    var egMutations = GenerateEngineGimbalMutation(craft);
                    mutations.AddRange(egMutations);
                }
                else if( guess < 75 )
                {
                    // sometimes mutate weapon manager
                    var wmMutations = GenerateWeaponManagerMutation(1);
                    mutations.AddRange(wmMutations);
                }
                else
                {
                    // sometimes mutate pilot AI
                    var aiMutations = GeneratePilotAIMutation(1);
                    mutations.AddRange(aiMutations);
                }
            }
            return mutations;
        }

        private List<VariantMutation> GeneratePilotAIMutation(int count)
        {
            var availableAxes = new List<string>() {
                "steerMult",
                "steerKiAdjust",
                "steerDamping",
                //"DynamicDampingMin",
                //"DynamicDampingMax",
                //"dynamicSteerDampingFactor",
                //"dynamicDampingPitch",
                //"DynamicDampingPitchMin",
                //"DynamicDampingPitchMax",
                //"dynamicSteerDampingPitchFactor",
                //"DynamicDampingYawMin",
                //"DynamicDampingYawMax",
                //"dynamicSteerDampingYawFactor",
                //"DynamicDampingRollMin",
                //"DynamicDampingRollMax",
                //"dynamicSteerDampingRollFactor",
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

            var results = new List<VariantMutation>();
            for (var k=0;k<count;k++)
            {
                var index = (int) UnityEngine.Random.Range(0, availableAxes.Count);
                var positivePole = new PilotAINudgeMutation(paramName: availableAxes[index], modifier: crystalRadius);
                results.Add(positivePole);
                var negativePole = new PilotAINudgeMutation(paramName: availableAxes[index], modifier: -crystalRadius);
                results.Add(negativePole);
                availableAxes.RemoveAt(index);
            }
            return results;
        }

        private List<VariantMutation> GenerateWeaponManagerMutation(int count)
        {
            var availableAxes = new List<string>() {
                //"targetScanInterval",
                //"fireBurstLength",
                //"guardAngle",
                //"guardRange",
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
                //"cmWaitTime"
            };

            var results = new List<VariantMutation>();
            for (var k=0;k<count;k++)
            {
                var index = (int)UnityEngine.Random.Range(0, availableAxes.Count);
                var positivePole = new WeaponManagerNudgeMutation(paramName: availableAxes[index], modifier: crystalRadius);
                results.Add(positivePole);
                var negativePole = new WeaponManagerNudgeMutation(paramName: availableAxes[index], modifier: -crystalRadius);
                results.Add(negativePole);
                availableAxes.RemoveAt(index);
            }
            return results;
        }

        private int CraftControlSurfaceCount(ConfigNode craft)
        {
            List<ConfigNode> modules = FindModuleNodes(craft, "ModuleControlSurface");
            return modules.Count;
        }

        private List<VariantMutation> GenerateControlSurfaceMutation(ConfigNode craft)
        {
            var results = new List<VariantMutation>();
            // TODO: find a control surface to mutate
            List<ConfigNode> modules = FindModuleNodes(craft, "ModuleControlSurface");
            int axisMask;
            var maskRandomizer = UnityEngine.Random.Range(0, 100);
            if (maskRandomizer < 33)
            {
                axisMask = ControlSurfaceNudgeMutation.MASK_ROLL;
            }
            else if (maskRandomizer < 66)
            {
                axisMask = ControlSurfaceNudgeMutation.MASK_PITCH;
            }
            else
            {
                axisMask = ControlSurfaceNudgeMutation.MASK_YAW;
            }
            var positivePole = new ControlSurfaceNudgeMutation("authorityLimiter", crystalRadius, axisMask);
            var negativePole = new ControlSurfaceNudgeMutation("authorityLimiter", -crystalRadius, axisMask);
            results.Add(positivePole);
            results.Add(negativePole);
            return results;
        }

        private bool CraftHasEngineGimbal(ConfigNode craft)
        {
            List<ConfigNode> gimbals = FindModuleNodes(craft, "ModuleGimbal");
            return gimbals.Count != 0;
        }

        private List<VariantMutation> GenerateEngineGimbalMutation(ConfigNode craft)
        {
            var results = new List<VariantMutation>();
            // TODO: find a engine gimbal to mutate
            List<ConfigNode> modules = FindModuleNodes(craft, "ModuleGimbal");
            int axisMask;
            var maskRandomizer = UnityEngine.Random.Range(0, 100);
            if (maskRandomizer < 33)
            {
                axisMask = ControlSurfaceNudgeMutation.MASK_ROLL;
            }
            else if (maskRandomizer < 66)
            {
                axisMask = ControlSurfaceNudgeMutation.MASK_PITCH;
            }
            else
            {
                axisMask = ControlSurfaceNudgeMutation.MASK_YAW;
            }
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
