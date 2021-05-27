using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BDArmory.Evolution
{
    public class VariantEngine
    {
        private string workingDirectory;
        public VariantEngine(string dir)
        {
            this.workingDirectory = dir;
        }

        public string Generate(string source, VariantOptions options)
        {
            // reject mismatched key/value counts
            var mismatchCounts = options.keys.Count != options.values.Count;
            // reject empty options
            var emptyOptions = options.keys.Count == 0 || options.values.Count == 0;
            if (mismatchCounts || emptyOptions)
            {
                return source;
            }

            List<string> lines = new List<string>(source.Split('\n'));
            // for each key in the provided options, run regex replace
            for (var k = 0; k < options.keys.Count; k++)
            {
                lines = (List<string>)lines.Select(x => ReplaceLine(x, options.keys[k], options.values[k]));
            }
            return string.Join("\n", lines);
        }

        private string ReplaceLine(string line, string key, float value)
        {
            var pattern = string.Format("(.*{0} = )(.+)", key);
            var match = Regex.Match(line, pattern);
            if (match.Captures.Count > 1 && float.TryParse(match.Captures[1].ToString(), out float fValue))
            {
                return Regex.Replace(line, pattern, string.Format("$1{0}", fValue * value));
            }
            else
            {
                return line;
            }
        }

        public ConfigNode GenerateNode(ConfigNode source, VariantOptions options)
        {
            // make a copy of the source and modify the copy
            var result = source.CreateCopy();

            for (var k = 0; k < options.keys.Count; k++)
            {
                var key = options.keys[k];
                var value = options.values[k];
                List<ConfigNode> matchingNodes = new List<ConfigNode>();
                // TODO: support params outside pilot AI module
                FindMatchingNode(result, "MODULE", "BDModulePilotAI", key, matchingNodes);
                foreach (var node in matchingNodes)
                {
                    MutateNode(node, key, value);
                }
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

        public void FindMatchingNode(ConfigNode source, string nodeType, string nodeName, string paramName, List<ConfigNode> found)
        {
            if (source.name == nodeType && source.HasValue("name") && source.GetValue("name").StartsWith(nodeName) && source.HasValue(paramName))
            {
                found.Add(source);
            }
            foreach (var child in source.GetNodes())
            {
                FindMatchingNode(child, nodeType, nodeName, paramName, found);
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
        public List<string> keys;
        public List<float> values;
        public VariantOptions(List<string> keys, List<float> values)
        {
            this.keys = keys;
            this.values = values;
        }
    }

    public class VariantDescriptor
    {
        public string partName;
        public string moduleName;
        public string paramName;
        public float valueModifier;
        public VariantDescriptor(string part,
            string module,
            string param,
            float modifier)
        {
            this.partName = part;
            this.moduleName = module;
            this.paramName = param;
            this.valueModifier = modifier;
        }
    }

}
