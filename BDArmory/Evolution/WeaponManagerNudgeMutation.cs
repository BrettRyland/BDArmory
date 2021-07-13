using System;
using System.Collections.Generic;
using UnityEngine;

namespace BDArmory.Evolution
{
    public class WeaponManagerNudgeMutation : VariantMutation
    {
        const string moduleName = "MissileFire";

        public string paramName;
        public float modifier;
        public string key;
        public int direction;
        private List<MutatedPart> mutatedParts = new List<MutatedPart>();
        public WeaponManagerNudgeMutation(string paramName, float modifier, string key, int direction)
        {
            this.paramName = paramName;
            this.modifier = modifier;
            this.key = key;
            this.direction = direction;
        }

        public void Apply(ConfigNode craft, VariantEngine engine)
        {
            Debug.Log("Evolution WeaponManagerNudgeMutation applying");
            List<ConfigNode> matchingNodes = engine.FindModuleNodes(craft, "MissileFire");
            if (matchingNodes.Count == 1)
            {
                Debug.Log("Evolution WeaponManagerNudgeMutation found module");
                var node = matchingNodes[0];
                float existingValue;
                float.TryParse(node.GetValue(paramName), out existingValue);
                Debug.Log(string.Format("Evolution WeaponManagerNudgeMutation found existing value {0} = {1}", paramName, existingValue));
                if (engine.NudgeNode(node, paramName, modifier))
                {
                    ConfigNode partNode = engine.FindParentPart(craft, node);
                    if( partNode == null )
                    {
                        Debug.Log("Evolution WeaponManagerNudgeMutation failed to find parent part for module");
                        return;
                    }
                    string partName = partNode.GetValue("part");
                    var value = existingValue * (1 + modifier);
                    Debug.Log(string.Format("Evolution WeaponManagerNudgeMutation mutated part {0}, module {1}, param {2}, existing: {3}, value: {4}", partName, moduleName, paramName, existingValue, value));
                    mutatedParts.Add(new MutatedPart(partName, moduleName, paramName, existingValue, value));
                }
                else
                {
                    Debug.Log(string.Format("Evolution WeaponManagerNudgeMutation unable to mutate {0}", paramName));
                }
            }
            else
            {
                Debug.Log("Evolution WeaponManagerNudgeMutation wrong number of weapon managers");
            }
        }

        public Variant GetVariant(string id, string name)
        {
            return new Variant(id, name, mutatedParts, key, direction);
        }
    }
}
