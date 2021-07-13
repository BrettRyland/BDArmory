using System;
using System.Collections.Generic;
using UnityEngine;

namespace BDArmory.Evolution
{
    public class ControlSurfaceAxisNudgeMutation : VariantMutation
    {
        const string moduleName = "ModuleControlSurface";

        public static int MASK_ROLL = 0x01;
        public static int MASK_PITCH = 0x02;
        public static int MASK_YAW = 0x04;
        // TODO: part name filter
        public string paramName;
        public float modifier;
        public int axisMask;
        public string key;
        public int direction;
        private List<MutatedPart> mutatedParts = new List<MutatedPart>();
        public ControlSurfaceAxisNudgeMutation(string paramName, float modifier, int axisMask, string key, int direction)
        {
            this.paramName = paramName;
            this.modifier = modifier;
            this.axisMask = axisMask;
            this.key = key;
            this.direction = direction;
        }

        public void Apply(ConfigNode craft, VariantEngine engine)
        {
            Debug.Log("Evolution ControlSurfaceNudgeMutation applying");
            List<ConfigNode> matchingNodes = engine.FindModuleNodes(craft, moduleName);
            foreach (var node in matchingNodes)
            {
                MutateIfNeeded(node, craft, engine);
            }
        }

        public Variant GetVariant(string id, string name)
        {
            return new Variant(id, name, mutatedParts, key, direction);
        }

        private void MutateIfNeeded(ConfigNode node, ConfigNode craft, VariantEngine engine)
        {
            // check axis mask for included axes
            bool shouldMutate = false;
            if( (axisMask & MASK_ROLL) == MASK_ROLL )
            {
                if (node.HasValue("ignoreRoll") && node.GetValue("ignoreRoll") == "False" )
                {
                    shouldMutate = true;
                }
            }
            if ( (axisMask & MASK_PITCH) == MASK_PITCH)
            {
                if (node.HasValue("ignorePitch") && node.GetValue("ignorePitch") == "False")
                { 
                    shouldMutate = true;
                }
            }
            if ((axisMask & MASK_YAW) == MASK_YAW)
            {
                if (node.HasValue("ignoreRoll") && node.GetValue("ignoreRoll") == "False")
                {
                    shouldMutate = true;
                }
            }
            if (shouldMutate)
            {
                float existingValue;
                float.TryParse(node.GetValue(paramName), out existingValue);
                Debug.Log(string.Format("Evolution ControlSurfaceNudgeMutation found existing value {0} = {1}", paramName, existingValue));
                if (engine.NudgeNode(node, paramName, modifier))
                {
                    ConfigNode partNode = engine.FindParentPart(craft, node);
                    if( partNode == null )
                    {
                        Debug.Log("Evolution ControlSurfaceNudgeMutation failed to find parent part for module");
                        return;
                    }
                    string partName = partNode.GetValue("part");
                    var value = existingValue * (1 + modifier);
                    Debug.Log(string.Format("Evolution ControlSurfaceNudgeMutation mutated part {0}, module {1}, param {2}, existing: {3}, value: {4}", partName, moduleName, paramName, existingValue, value));
                    mutatedParts.Add(new MutatedPart(partName, moduleName, paramName, existingValue, value));
                }
                else
                {
                    Debug.Log(string.Format("Evolution ControlSurfaceNudgeMutation unable to mutate {0}", paramName));
                }
            }
        }
    }
}
