using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

using BDArmory.Utils;

namespace BDArmory.Armor
{
    public class ArmorInfo
    {
        public string name { get; private set; }

        public string localizedName { get; private set; } //display name
        public float Density { get; private set; } //mass kg/m3 lighter is better. Or is it?
        public float Strength { get; private set; } //in MPa, yieldstrength for material, controls fail point for material when projectile can penetrate. Higher is better
        public float Hardness { get; private set; } //hardness, in MPa, of material. Controls how much deformation impacting projectiles experience
        public float Yield { get; private set; } // Yield strength of material. Only needed while loading, but needs to be here for reflection if an armor definition is missing it.
        public float YoungModulus { get; private set; } // Young's Modulus of material. Only needed while loading, but needs to be here for reflection if an armor definition is missing it.
        public float Ductility { get; private set; } //measure of ductility, 0 is hardened ceramic, 100 is rubber. Mild steel is about 15. ideally should be around 15-25. 
                                                     //Too low, and armor is brittle. Too High, and armor cannot effectively stop projectiles in reasonable distance
        public float Diffusivity { get; private set; } //ability to disperse electrical/thermal energy when material is subject to laser/EMP attack. Higher is better
        public float SafeUseTemp { get; private set; } //In Kelvin, determines max temp armor retains full mechanical properties
        public float radarReflectivity { get; private set; } //radar stealthiness
        public float magneticSusceptability { get; private set; } //magnetic stealthiness
        public float Cost { get; private set; }

        public float vFactor { get; private set; }

        
        public float muParam1 { get; private set; }
        public float muParam2 { get; private set; }
        public float muParam3 { get; private set; }
        public float muParam1S { get; private set; }
        public float muParam2S { get; private set; }
        public float muParam3S { get; private set; }
        public float HEEquiv { get; private set; }
        public float HEATEquiv { get; private set; }


        //public bool Reactive {get; private set; } have a reactive armor bool?

        public static ArmorInfos armors;
        public static List<string> armorNames;
        public static ArmorInfo defaultArmor;

        public ArmorInfo(string name, string localizedName, float Density, float Strength, float Hardness, float yield, float youngModulus, float Ductility, float Diffusivity, float SafeUseTemp, float Stealth, float Mag, float Cost, float defaultPenShrapnel, float defaultPenHEAT)
        {
            this.name = name;
            this.localizedName = (localizedName == "def" ? name : localizedName);
            this.Density = Density;
            this.Strength = Strength;
            this.Hardness = Hardness;
            this.Yield = yield;
            this.YoungModulus = youngModulus;
            this.Ductility = Ductility;
            this.Diffusivity = Diffusivity;
            this.SafeUseTemp = SafeUseTemp;
            this.radarReflectivity = Stealth;
            this.magneticSusceptability = Mag;
            this.Cost = Cost;

            // Since we don't actually need yield and youngModulus we'll just calculate
            // vFactor and discard those two. vFactor is simply the density of the armor
            // divided by two times the resistance of the armor H, calculated using Tate's
            // formula, found either in his 1986 paper or in the publically available US
            // Army technical memorandum "TERMINAL  BALLISTICS TEST AND ANALYSIS
            // GUIDELINES FOR THE PENETRATION MECHANICS BRANCH" (ADA246922) on page 104
            // of the PDF, listed as Advancing Cavity (Tate 1986a)", this is used
            // throughout the equations proposed by Frank and Zook as special case
            // solutions to the model proposed by Tate and Alekseevskii
            this.vFactor = Density / (2.0f * (yield * (2.0f / 3.0f + Mathf.Log((2.0f *
                youngModulus * 1000f) / (3.0f * yield))) * 1000000.0f));

            // mu is the sqrt of the ratio of armor density to projectile density
            // We don't actually need mu itself or the following variants of it, just
            // the muParams so we'll calculate those instead.
            float muSquared = Density / (11340.0f);
            float mu = BDAMath.Sqrt(muSquared);
            float muInverse = 1.0f / mu;
            float muInverseSquared = 1.0f / muSquared;

            // These parameters are all used in the equations proposed by Frank and Zook
            // in their 1987 paper "ENERGY-EFFICIENT PENETRATION AND PERFORATION OF
            // TARGETS IN THE HYPERVELOCITY REGIME". These are all based on the constant
            // mu, explained above. We are pre-calculating these terms in the function in
            // order to optimize the performance of the equation
            this.muParam1 = muInverse / (1.0f + mu);
            this.muParam2 = muInverse;
            this.muParam3 = (muInverseSquared + 1.0f / 3.0f);

            // Doing the same thing as above but with the sabot density instead. Note that
            // if we ever think about having custom round density's then we're going to
            // have to build a dictionary instead using all available armor types and
            // projectiles so as to maintain performance as proposed by DocNappers
            muSquared = Density / (19000.0f);
            mu = BDAMath.Sqrt(muSquared);
            muInverse = 1.0f / mu;
            muInverseSquared = 1.0f / muSquared;

            this.muParam1S = muInverse / (1.0f + mu);
            this.muParam2S = muInverse;
            this.muParam3S = (muInverseSquared + 1.0f / 3.0f);

            this.HEEquiv = defaultPenShrapnel / ProjectileUtils.CalculatePenetration(15, 430, 0.02f, 1, Strength, this.vFactor, this.muParam1, this.muParam2, this.muParam3);
            this.HEATEquiv = defaultPenHEAT / ProjectileUtils.CalculatePenetration(6, 5000, 0.13098f, 1, Strength, this.vFactor, this.muParam1, this.muParam2, this.muParam3);
        }

        static UrlDir BDArmoryDir;

        public static void Load()
        {
            if (armors != null) return; // Only load the armor defs once on startup.
            armors = new ArmorInfos();
            if (armorNames == null) armorNames = new List<string>();
            UrlDir.UrlConfig[] nodes = GameDatabase.Instance.GetConfigs("ARMOR");
            ConfigNode node;

            // Based on average piece of shrapnel
            float defaultPenShrapnel = ProjectileUtils.CalculatePenetration(15, 430, 0.02f, 1);
            // Based on 120x570 mm NATO HEAT shell
            float defaultPenHEAT = ProjectileUtils.CalculatePenetration(6, 5000, 0.13098f, 1);

            // First locate BDA's default armor definition so we can fill in missing fields.
            if (defaultArmor == null)
                for (int i = 0; i < nodes.Length; ++i)
                {
                    if (nodes[i].parent.name != "BD_Armors") continue; // Ignore other config files.
                    node = nodes[i].config;
                    if (!node.HasValue("name") || (string)ParseField(nodes[i].config, "name", typeof(string)) != "def") continue; // Ignore other configs.
                    if (nodes[i].parent.parent.parent == null || nodes[i].parent.parent.parent.name != "BDArmory") continue;
                    BDArmoryDir = nodes[i].parent.parent.parent;
                    defaultArmor = ParseArmorInfoNode(node, "def", defaultPenShrapnel, defaultPenHEAT);
                    armors.Add(defaultArmor);
                    armorNames.Add("def");
                    break;
                }
            if (defaultArmor == null) throw new ArgumentException("Failed to find BDArmory's default armor definition.", "defaultArmor");

            // Now add in the rest of the materials.
            for (int i = 0; i < nodes.Length; i++)
            {
                AddArmorInfo(nodes[i], defaultPenShrapnel, defaultPenHEAT, true);
            }
            for (int i = 0; i < nodes.Length; i++)
            {
                AddArmorInfo(nodes[i], defaultPenShrapnel, defaultPenHEAT, false);
            }
            //once armors are loaded, remove the def armor so it isn't found in later list parsings by HitpointTracker when updating parts armor
            armors.Remove(defaultArmor);
            armorNames.Remove("def");
        }

        private static void AddArmorInfo(UrlDir.UrlConfig nodeURL, float defaultPenShrapnel, float defaultPenHEAT, bool loadBDArmors)
        {
            string name_ = "";
            try
            {
                ConfigNode node = nodeURL.config;
                if (loadBDArmors == (nodeURL.parent.parent.parent == null || (nodeURL.parent.parent.parent != BDArmoryDir) || (nodeURL.parent.name != "BD_Armors"))) return;
                name_ = (string)ParseField(node, "name", typeof(string));
                if (armorNames.Contains(name_)) // Avoid duplicates.
                {
                    if (nodeURL.parent.name != "BD_Armors" || name_ != "def") // Don't report the default bullet definition as a duplicate.
                        Debug.LogError($"[BDArmory.ArmorInfo]: Armor definition {name_} from {nodeURL.parent.name} already exists, skipping.");
                    return;
                }
                Debug.Log($"[BDArmory.ArmorInfo]: Parsing definition of armor {name_} from {nodeURL.parent.parent.name}");
                armors.Add(
                    ParseArmorInfoNode(node, name_, defaultPenShrapnel, defaultPenHEAT)
                );
                armorNames.Add(name_);
            }
            catch (Exception e)
            {
                Debug.LogError($"[BDArmory.ArmorInfo]: Error Loading Armor Config '{name_}' | {e.ToString()}");
            }
        }

        private static ArmorInfo ParseArmorInfoNode(ConfigNode node, string name_, float defaultPenShrapnel, float defaultPenHEAT)
        {
            return new ArmorInfo(
                        name_,
                    (string)ParseField(node, nameof(localizedName), typeof(string)),
                    (float)ParseField(node, nameof(Density), typeof(float)),
                    (float)ParseField(node, nameof(Strength), typeof(float)),
                    (float)ParseField(node, nameof(Hardness), typeof(float)),
                    (float)ParseField(node, nameof(Yield), typeof(float)),
                    (float)ParseField(node, nameof(YoungModulus), typeof(float)),
                    (float)ParseField(node, nameof(Ductility), typeof(float)),
                    (float)ParseField(node, nameof(Diffusivity), typeof(float)),
                    (float)ParseField(node, nameof(SafeUseTemp), typeof(float)),
                    (float)ParseField(node, nameof(radarReflectivity), typeof(float)),
                    (float)ParseField(node, nameof(magneticSusceptability), typeof(float)),
                    (float)ParseField(node, nameof(Cost), typeof(float)),
                    defaultPenShrapnel,
                    defaultPenHEAT
                    );
        }

        private static object ParseField(ConfigNode node, string field, Type type)
        {
            try
            {
                if (!node.HasValue(field))
                    throw new ArgumentNullException(field, $"Field '{field}' is missing.");
                var value = node.GetValue(field);
                try
                {
                    if (type == typeof(string))
                    { return value; }
                    else if (type == typeof(bool))
                    { return bool.Parse(value); }
                    else if (type == typeof(int))
                    { return int.Parse(value); }
                    else if (type == typeof(float))
                    { return float.Parse(value); }
                    else
                    { throw new ArgumentException("Invalid type specified."); }
                }
                catch (Exception e)
                { throw new ArgumentException($"Field '{field}': '{value}' could not be parsed as '{type.ToString()}' | {e.ToString()}", field); }
            }
            catch (Exception e)
            {
                if (defaultArmor != null)
                {
                    // Give a warning about the missing or invalid value, then use the default value using reflection to find the field.
                    var defaultValue = typeof(ArmorInfo).GetProperty(field, BindingFlags.Public | BindingFlags.Instance).GetValue(defaultArmor);
                    Debug.LogError($"[BDArmory.ArmorInfo]: Using default value of {defaultValue.ToString()} for {field} | {e.ToString()}");
                    return defaultValue;
                }
                else
                    throw;
            }
        }
    }

    public class ArmorInfos : List<ArmorInfo>
    {
        public ArmorInfo this[string name]
        {
            get { return Find((value) => { return value.name == name; }); }
        }
    }
}