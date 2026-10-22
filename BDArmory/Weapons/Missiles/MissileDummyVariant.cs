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

        // Empty entries are kept so names stay aligned (by position) with the part's variant modules.
        static string[] SplitNames(string variantNames) => string.IsNullOrEmpty(variantNames) ? new string[0] : variantNames.Split(Separator);


        /// <summary>
        /// The part's variant modules in module order (a part can have multiple and part.variants only gives the first).
        /// </summary>
        static List<ModulePartVariants> GetVariantModules(Part part) =>
            part != null && part.Modules != null ? part.Modules.GetModules<ModulePartVariants>() : new List<ModulePartVariants>();


        /// <summary>
        /// The selected variant of each ModulePartVariants module joined by ';' in module order.
        /// "" if the part has no variant modules.
        /// </summary>
        /// <param name="part">The part for which to get the selected variant names.</param>
        /// <returns>The selected variant names, or an empty string if none are selected.</returns>
        public static string GetSelectedVariantName(Part part)
        {
            List<ModulePartVariants> modules = GetVariantModules(part);
            if (modules.Count == 0) return "";

            List<string> names = new List<string>(modules.Count);
            bool any = false;
            foreach (ModulePartVariants module in modules)
            {
                PartVariant selected = module != null ? module.SelectedVariant : null;
                string name = selected != null && !string.IsNullOrEmpty(selected.Name) ? selected.Name : "";
                names.Add(name);
                any |= name.Length > 0;
            }
            return any ? string.Join(Separator.ToString(), names) : "";
        }

        /// <summary>
        /// Finds a variant by name in a single variant module.
        /// </summary>
        static PartVariant FindInModule(ModulePartVariants module, string variantName, out int index)
        {
            index = -1;
            if (module == null || module.variantList == null) return null;
            List<PartVariant> list = module.variantList;
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
        /// Finds a variant by name in the part's variant modules, trying the module at <paramref name="slot"/> first.
        /// </summary>
        /// <param name="part">The part to search.</param>
        /// <param name="variantName">The name of the variant to find.</param>
        /// <param name="slot">The index of the module the name was selected from, or -1 to search all modules in order.</param>
        /// <param name="index">The index of the variant within its module's list, or -1 if not found.</param>
        /// <returns>The found variant, or null if not found.</returns>
        static PartVariant FindVariant(Part part, string variantName, int slot, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(variantName)) return null;
            List<ModulePartVariants> modules = GetVariantModules(part);
            PartVariant found;
            if (slot >= 0 && slot < modules.Count && (found = FindInModule(modules[slot], variantName, out index)) != null) return found;
            foreach (ModulePartVariants module in modules)
                if ((found = FindInModule(module, variantName, out index)) != null) return found;
            return null;
        }

        /// <summary>
        /// Determines whether any of the named variants exist on any of the part's variant modules.
        /// </summary>
        /// <param name="part">The part to search.</param>
        /// <param name="variantNames">The ';'-separated names of the variants to find.</param>
        /// <returns>true if any of the variants exist; otherwise, false.</returns>
        public static bool HasVariant(Part part, string variantNames)
        {
            if (part == null) return false;
            string[] names = SplitNames(variantNames);
            for (int i = 0; i < names.Length; ++i)
                if (FindVariant(part, names[i], i, out _) != null) return true;
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
            string[] names = SplitNames(variantNames);

            try // Texture sets and colours.
            {
                int applied = 0;
                for (int i = 0; i < names.Length; ++i) // One name per variant module, in module order.
                {
                    int index;
                    PartVariant variant = FindVariant(missilePrefab, names[i], i, out index);
                    if (variant == null) continue;

                    List<Material> materials = new List<Material>();
                    foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer == null) continue;
                        Material[] instances = renderer.materials;
                        renderer.materials = instances;
                        materials.AddRange(instances);
                    }

                    try // Isolate each module's variant so one failure doesn't stop the others.
                    {
                        ModulePartVariants.ApplyVariant(
                            missilePrefab, model.transform, variant, materials.ToArray(), false, index);
                        ++applied;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[BDArmory.MissileDummyVariant]: ApplyVariant failed for '{names[i]}' (module {i}, index {index}) on {missilePrefab.name} / {model.name}: {e}");
                    }
                }
                if (applied == 0)
                {
                    Debug.LogWarning($"[BDArmory.MissileDummyVariant]: No variant found for '{variantNames}' on {missilePrefab.name}.");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BDArmory.MissileDummyVariant]: Failed to apply textures of variants '{variantNames}' to {model.name}: {e}");
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
            ConfigNode partConfig = missilePrefab.partInfo?.partConfig ?? missilePrefab.partInfo?.partUrlConfig?.config;
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
                    string variantName = variantNode.GetValue("name");
                    bool selected = pairByPosition ? names[moduleIndex] == variantName : Array.IndexOf(names, variantName) >= 0;
                    if (!selected) continue; 
                    ConfigNode gameObjects = variantNode.GetNode("GAMEOBJECTS");
                    if (gameObjects == null) continue;
                    if (transformsByName == null)
                    {
                        transformsByName = new Dictionary<string, List<Transform>>();
                        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                        {
                            if (!transformsByName.TryGetValue(t.name, out List<Transform> list)) transformsByName[t.name] = list = new List<Transform>();
                            list.Add(t);
                        }
                    }
                    foreach (ConfigNode.Value entry in gameObjects.values)
                    {
                        if (!bool.TryParse(entry.value, out bool active)) continue;
                        if (transformsByName.TryGetValue(entry.name, out List<Transform> matches))
                        {
                            foreach (Transform t in matches) t.gameObject.SetActive(active);
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
            ConfigNode partConfig = missilePrefab?.partInfo?.partConfig ?? missilePrefab?.partInfo?.partUrlConfig?.config;
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

                List<KeyValuePair<string, string>> replacements = new List<KeyValuePair<string, string>>();
                foreach (string entry in modelNode.GetValues("texture"))
                {
                    string[] split = entry.Split(',');
                    if (split.Length < 2) continue;
                    replacements.Add(new KeyValuePair<string, string>(split[0].Trim(), split[1].Trim()));
                }
                if (replacements.Count == 0) return;

                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null) continue;
                    Material[] materials = renderer.materials; // Instantiates.
                    foreach (Material material in materials)
                    {
                        if (material == null) continue;
                        foreach (string property in material.GetTexturePropertyNames())
                        {
                            Texture texture = material.GetTexture(property);
                            if (texture == null) continue;
                            foreach (KeyValuePair<string, string> replacement in replacements)
                            {
                                if (!TextureNameMatches(texture.name, replacement.Key)) continue;
                                Texture2D newTexture = GameDatabase.Instance.GetTexture(replacement.Value, property == "_BumpMap");
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
            string[] names = SplitNames(variantNames);
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                partNode.SetValue("variantName", name, true);
                break;
            }
            int moduleIndex = 0;
            foreach (ConfigNode module in partNode.GetNodes("MODULE"))
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
