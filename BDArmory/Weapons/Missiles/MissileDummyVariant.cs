using BDArmory.Settings;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BDArmory.Weapons.Missiles
{
    
    public static class MissileDummyVariant
    {
        /// <summary>
        /// Helpers for applying variants and non-baked textures for missiles so they're reflected in the MML.
        /// Variants are applied on selection, cannot be changed while in the MML.
        /// A part can have multiple ModulePartVariants modules (one for shape and another for texture as an example)
        /// </summary>
        const char Separator = ';';

        static string[] SplitNames(string variantNames) => string.IsNullOrEmpty(variantNames) ? new string[0] : variantNames.Split(Separator);

        /// <summary>
        /// Variants joined ';' in module order. "" if the part has no variants.
        /// </summary>
        /// <param name="part">The part for which to get the selected variant name.</param>
        /// <returns>The name of the selected variant, or an empty string if none is selected.</returns>
        public static string GetSelectedVariantName(Part part)
        {
            if (part == null || part.variants == null) return "";

            var names = new List<string>();

            foreach (var module in part.Modules.GetModules<ModulePartVariants>())
            {
                PartVariant selected = module.SelectedVariant;
                if (selected == null) continue;

                names.Add(selected.Name);
            }

            return string.Join(",", names);
        }

        /// <summary>
        /// Finds a variant by name in the part's variant list.
        /// </summary>
        /// <param name="part">The part to search.</param>
        /// <param name="variantName">The name of the variant to find.</param>
        /// <param name="index">The index of the found variant, or -1 if not found.</param>
        /// <returns>The found variant, or null if not found.</returns>
        static PartVariant FindVariant(Part part, string variantName, out int index)
        {
            index = -1;
            if (part == null || part.variants == null || string.IsNullOrEmpty(variantName)) return null;
            var list = part.variants.variantList;
            if (list == null) return null;
            for (int i = 0; i < list.Count; ++i)
            {
                if (list[i] != null && list[i].Name == variantName)
                {
                    index = i;
                    return list[i];
                }
            }
            return null;
        }

        /// <summary>
        /// Determines whether any of the named variants exist on any of the part's variant modules.
        /// </summary>
        /// <param name="part">The part to search.</param>
        /// <param name="variantNames">The names of the variants to find.</param>
        /// <returns>true if any of the variants exist; otherwise, false.</returns>
        public static bool HasVariant(Part part, string variantNames)
        {
            if (part == null) return false;

            var names = SplitNames(variantNames);
            foreach (var name in names)
                if (FindVariant(part, name, out _) != null) return true;

            return false;
        }

        /// <summary>
        /// Applies the selected variants from the part to the instantiated model.
        /// </summary>
        /// <param name="missilePrefab">The missile prefab containing the variants.</param>
        /// <param name="model">The instantiated model to apply variants to.</param>
        /// <param name="variantNames">The names of the variants to apply.</param>
        /// <returns>true if the variants were applied successfully; otherwise, false.</returns>
        public static bool ApplyToModel(Part missilePrefab, GameObject model, string variantNames)
        {
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileDummyVariant]: Applying '{variantNames}' on {missilePrefab?.name}");
            if (model == null || !HasVariant(missilePrefab, variantNames)) return false;
            var names = SplitNames(variantNames);

            try // Texture sets and colours.
            {
                PartVariant variant = null;
                int index = -1;
                foreach (var name in names)
                {
                    variant = FindVariant(missilePrefab, name, out index);
                    if (variant == null) continue;

                    var materials = new List<Material>();
                    foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer == null) continue;
                        var instances = renderer.materials;
                        renderer.materials = instances;
                        materials.AddRange(instances);
                    }

                    ModulePartVariants.ApplyVariant(
                        missilePrefab, model.transform, variant, materials.ToArray(), false, index);
                }
                if (variant != null)
                {
                    var materials = new List<Material>();
                    foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer == null) continue;
                        var instances = renderer.materials; 
                        renderer.materials = instances;
                        materials.AddRange(instances);
                    }
                    ModulePartVariants.ApplyVariant(missilePrefab, model.transform, variant, materials.ToArray(), false, index);
                } 
                else
                {
                    Debug.LogWarning($"[BDArmory.MissileDummyVariant]: No variant found for '{variantNames}' on {missilePrefab.name}.");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BDArmory.MissileDummyVariant]: Failed to apply textures of variants '{variantNames}' to {model.name}: {e.Message}");
            }

            try 
            {
                // Toggle gameobjects based on the variant
                ApplyGameObjects(missilePrefab, model, names);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BDArmory.MissileDummyVariant]: Failed to apply game objects of variants '{variantNames}' to {model.name}: {e.Message}");
            }
            return true;
        }

        /// <summary>
        /// Enable/disable model objects according to the variants in the part config
        /// </summary>
        /// <param name="missilePrefab">The missile prefab containing the variants.</param>
        /// <param name="model">The instantiated model to apply variants to.</param>
        /// <param name="names">The names of the variants to apply.</param>
        static void ApplyGameObjects(Part missilePrefab, GameObject model, string[] names)
        {
            var partConfig = missilePrefab.partInfo?.partConfig ?? missilePrefab.partInfo?.partUrlConfig?.config;
            if (partConfig == null) return;

            int moduleCount = 0;
            foreach (ConfigNode module in partConfig.GetNodes("MODULE"))
                if (module.GetValue("name") == "ModulePartVariants") ++moduleCount;
            bool pairByPosition = moduleCount == names.Length;

            Dictionary<string, List<Transform>> transformsByName = null; // Built lazily.
            int moduleIndex = -1;
            foreach (ConfigNode module in partConfig.GetNodes("MODULE"))
            {
                if (module.GetValue("name") != "ModulePartVariants") continue;
                ++moduleIndex;
                foreach (ConfigNode variantNode in module.GetNodes("VARIANT"))
                {
                    var variantName = variantNode.GetValue("name");
                    bool selected = pairByPosition ? names[moduleIndex] == variantName : Array.IndexOf(names, variantName) >= 0;
                    if (!selected) continue; 
                    var gameObjects = variantNode.GetNode("GAMEOBJECTS");
                    if (gameObjects == null) continue;
                    if (transformsByName == null)
                    {
                        transformsByName = new Dictionary<string, List<Transform>>();
                        foreach (var t in model.GetComponentsInChildren<Transform>(true))
                        {
                            if (!transformsByName.TryGetValue(t.name, out var list)) transformsByName[t.name] = list = new List<Transform>();
                            list.Add(t);
                        }
                    }
                    foreach (ConfigNode.Value entry in gameObjects.values)
                    {
                        if (!bool.TryParse(entry.value, out bool active)) continue;
                        if (transformsByName.TryGetValue(entry.name, out var matches))
                        {
                            foreach (var t in matches) t.gameObject.SetActive(active);
                        }
                        else if (BDArmorySettings.DEBUG_MISSILES)
                        {
                            Debug.Log($"[BDArmory.MissileDummyVariant]: Variant '{variantName}' refers to '{entry.name}', which isn't in the model {model.name}.");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Applies model textures to the instantiated model based on the part's variant configuration.
        /// Materials from MODEL{} are applied first so variant textures take precedence.
        /// </summary>
        /// <param name="missilePrefab">The missile prefab containing the variant configuration.</param>
        /// <param name="model">The instantiated model to apply textures to.</param>
        /// <param name="modelPath">The path to the model in the part configuration.</param>
        public static void ApplyModelTextures(Part missilePrefab, GameObject model, string modelPath)
        {
            var partConfig = missilePrefab?.partInfo?.partConfig ?? missilePrefab?.partInfo?.partUrlConfig?.config;
            if (partConfig == null || model == null) return;
            try
            {
                ConfigNode modelNode = null;
                foreach (ConfigNode node in partConfig.GetNodes("MODEL"))
                {
                    if (modelNode == null) modelNode = node; 
                    if (node.GetValue("model") == modelPath) { modelNode = node; break; }
                }
                if (modelNode == null) return;

                var replacements = new List<KeyValuePair<string, string>>();
                foreach (var entry in modelNode.GetValues("texture"))
                {
                    var split = entry.Split(',');
                    if (split.Length < 2) continue;
                    replacements.Add(new KeyValuePair<string, string>(split[0].Trim(), split[1].Trim()));
                }
                if (replacements.Count == 0) return;

                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null) continue;
                    var materials = renderer.materials; // Instantiates.
                    foreach (var material in materials)
                    {
                        if (material == null) continue;
                        foreach (var property in material.GetTexturePropertyNames())
                        {
                            var texture = material.GetTexture(property);
                            if (texture == null) continue;
                            foreach (var replacement in replacements)
                            {
                                if (!TextureNameMatches(texture.name, replacement.Key)) continue;
                                var newTexture = GameDatabase.Instance.GetTexture(replacement.Value, property == "_BumpMap");
                                if (newTexture != null) material.SetTexture(property, newTexture);
                                else Debug.LogWarning($"[BDArmory.MissileDummyVariant]: Replacement texture '{replacement.Value}' for '{replacement.Key}' on {missilePrefab.name} not found.");
                                break;
                            }
                        }
                    }
                    renderer.materials = materials;
                }
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileDummyVariant]: Applied {replacements.Count} MODEL texture replacement(s) for {missilePrefab.name}.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BDArmory.MissileDummyVariant]: Failed to apply MODEL textures of {missilePrefab.name} to {model.name}: {e.Message}");
            }
        }

        /// <summary>
        /// Checks if a loaded texture is the one a MODEL texture entry refers to.
        /// </summary>
        /// <param name="textureName">The name of the loaded texture.</param>
        /// <param name="original">The original texture name to compare against.</param>
        /// <returns>True if the texture names match, false otherwise.</returns>
        static bool TextureNameMatches(string textureName, string original)
        {
            if (string.IsNullOrEmpty(textureName) || string.IsNullOrEmpty(original)) return false;
            return string.Equals(Path.GetFileNameWithoutExtension(textureName), Path.GetFileNameWithoutExtension(original), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Selects the variants in a part snapshot so a launched part has them applied.  
        /// </summary>  
        /// <param name="partNode">The part node containing the variant configuration.</param>
        /// <param name="missilePrefab">The missile prefab containing the variant configuration.</param>
        /// <param name="variantNames">A comma-separated string of variant names to apply.</param>
        public static void ApplyToSnapshot(ConfigNode partNode, Part missilePrefab, string variantNames)
        {
            if (partNode == null || !HasVariant(missilePrefab, variantNames)) return;
            var names = SplitNames(variantNames);
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                partNode.SetValue("variantName", name, true);
                break;
            }
            int moduleIndex = 0;
            foreach (var module in partNode.GetNodes("MODULE"))
            {
                if (module.GetValue("name") != "ModulePartVariants") continue;
                if (moduleIndex < names.Length && !string.IsNullOrEmpty(names[moduleIndex]))
                    module.SetValue("selectedVariant", names[moduleIndex], true);
                ++moduleIndex;
            }
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.MissileDummyVariant]: Selected variants '{variantNames}' for spawned {missilePrefab.name}.");
        }
    }
}
