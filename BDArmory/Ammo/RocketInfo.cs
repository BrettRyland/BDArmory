using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BDArmory.Bullets
{
    public class RocketInfo
    {
        public string name { get; private set; }
        public string DisplayName { get; private set; }
        public bool useDisplayName { get; private set; }
        public float rocketMass { get; private set; }
        public float caliber { get; private set; }
        public float apMod { get; private set; }
        public float thrust { get; private set; }
        public float thrustTime { get; private set; }
        public float lifeTime { get; private set; } = 10f; // Need this here for trajectory sim timing. Could make it a proper config value.
        public bool shaped { get; private set; }
        public bool flak { get; private set; }
        public bool EMP { get; private set; }
        public bool choker { get; private set; }
        public bool gravitic { get; private set; }
        public bool impulse { get; private set; }
        public float massMod { get; private set; }
        public float force { get; private set; }
        public bool explosive { get; private set; }
        public bool incendiary { get; private set; }
        public float tntMass { get; private set; }
        public bool nuclear { get; private set; }
        public bool beehive { get; private set; }
        public string subMunitionType { get; private set; }
        public int projectileCount { get; private set; }
        public float thrustDeviation { get; private set; }
        public string rocketModelPath { get; private set; }

        public static RocketInfos rockets;
        public static HashSet<string> rocketNames;
        public static RocketInfo defaultRocket;

        public RocketInfo(string name, string DisplayName, bool useDisplayName, float rocketMass, float caliber, float apMod, float thrust, float thrustTime,
                         bool shaped, bool flak, bool EMP, bool choker, bool gravitic, bool impulse, float massMod, float force, bool explosive, bool incendiary, float tntMass, bool nuclear, bool beehive, string subMunitionType, int projectileCount, float thrustDeviation, string rocketModelPath)
        {
            this.name = name;
            this.DisplayName = string.IsNullOrEmpty(DisplayName) ? name : DisplayName;
            this.useDisplayName = useDisplayName;
            this.rocketMass = rocketMass;
            this.caliber = caliber;
            this.apMod = apMod;
            this.thrust = thrust;
            this.thrustTime = thrustTime;
            this.shaped = shaped;
            this.flak = flak;
            this.EMP = EMP;
            this.choker = choker;
            this.gravitic = gravitic;
            this.impulse = impulse;
            this.massMod = massMod;
            this.force = force;
            this.explosive = explosive;
            this.incendiary = incendiary;
            this.tntMass = tntMass;
            this.nuclear = nuclear;
            this.beehive = beehive;
            this.subMunitionType = subMunitionType;
            this.projectileCount = projectileCount;
            this.thrustDeviation = thrustDeviation;
            this.rocketModelPath = rocketModelPath;
        }

        public static void Load()
        {
            if (rockets != null) return; // Only load them once on startup.
            rockets = new RocketInfos();
            if (rocketNames == null) rocketNames = new HashSet<string>();
            UrlDir.UrlConfig[] nodes = GameDatabase.Instance.GetConfigs("ROCKET");
            ConfigNode node;

            // First locate BDA's default rocket definition so we can fill in missing fields.
            if (defaultRocket == null)
                for (int i = 0; i < nodes.Length; ++i)
                {
                    if (nodes[i].parent.name != "BD_Rockets") continue; // Ignore other config files.
                    node = nodes[i].config;
                    if (!node.HasValue("name") || (string)ParseField(nodes[i].config, "name", typeof(string)) != "def") continue; // Ignore other configs.
                    if (nodes[i].parent.parent == null || nodes[i].parent.parent.parent == null || nodes[i].parent.parent.parent.name != "BDArmory") continue;
                    Debug.Log("[BDArmory.RocketInfo]: Parsing default rocket definition from " + nodes[i].parent.name);
                    defaultRocket = ParseRocketInfoNode(node, "def");
                    defaultRocket.projectileCount = Math.Max(defaultRocket.projectileCount, 1);
                    rockets.Add(defaultRocket);
                    rocketNames.Add("def");
                    break;
                }
            if (defaultRocket == null) throw new ArgumentException("Failed to find BDArmory's default rocket definition.", "defaultRocket");

            // Now add in the rest of the rockets.
            for (int i = 0; i < nodes.Length; i++)
            {
                AddRocketInfo(nodes[i]);
            }
        }

        private static void AddRocketInfo(UrlDir.UrlConfig nodeURL)
        {
            string name_ = "";
            try
            {
                ConfigNode node = nodeURL.config;
                name_ = (string)ParseField(node, "name", typeof(string));
                if (rocketNames.Contains(name_)) // Avoid duplicates.
                {
                    if (nodeURL.parent.name != "BD_Rockets" || name_ != "def") // Don't report the default bullet definition as a duplicate.
                        Debug.LogError($"[BDArmory.RocketInfo]: Rocket definition {name_} from {nodeURL.parent.name} already exists, skipping.");
                    return;
                }
                Debug.Log($"[BDArmory.RocketInfo]: Parsing definition of rocket {name_} from {nodeURL.parent.name}");
                rockets.Add(
                    ParseRocketInfoNode(node, name_)
                );
                rocketNames.Add(name_);
            }
            catch (Exception e)
            {
                Debug.LogError($"[BDArmory.RocketInfo]: Error Loading Rocket Config '{name_}' | {e.ToString()}");
            }
        }

        private static RocketInfo ParseRocketInfoNode(ConfigNode node, string name_)
        {
            return new RocketInfo(
                            name_,
                            (string)ParseField(node, nameof(DisplayName), typeof(string)),
                            (bool)ParseField(node, nameof(useDisplayName), typeof(bool)),
                            (float)ParseField(node, nameof(rocketMass), typeof(float)),
                            (float)ParseField(node, nameof(caliber), typeof(float)),
                            (float)ParseField(node, nameof(apMod), typeof(float)),
                            (float)ParseField(node, nameof(thrust), typeof(float)),
                            (float)ParseField(node, nameof(thrustTime), typeof(float)),
                            (bool)ParseField(node, nameof(shaped), typeof(bool)),
                            (bool)ParseField(node, nameof(flak), typeof(bool)),
                            (bool)ParseField(node, nameof(EMP), typeof(bool)),
                            (bool)ParseField(node, nameof(choker), typeof(bool)),
                            (bool)ParseField(node, nameof(gravitic), typeof(bool)),
                            (bool)ParseField(node, nameof(impulse), typeof(bool)),
                            (float)ParseField(node, nameof(massMod), typeof(float)),
                            (float)ParseField(node, nameof(force), typeof(float)),
                            (bool)ParseField(node, nameof(explosive), typeof(bool)),
                            (bool)ParseField(node, nameof(incendiary), typeof(bool)),
                            (float)ParseField(node, nameof(tntMass), typeof(float)),
                            (bool)ParseField(node, nameof(nuclear), typeof(bool)),
                            (bool)ParseField(node, nameof(beehive), typeof(bool)),
                            (string)ParseField(node, nameof(subMunitionType), typeof(string)),
                            (int)ParseField(node, nameof(projectileCount), typeof(int)),
                            (float)ParseField(node, nameof(thrustDeviation), typeof(float)),
                            (string)ParseField(node, nameof(rocketModelPath), typeof(string))
                        );
        }

        private static object ParseField(ConfigNode node, string field, Type type)
        {
            try
            {
                if (!node.HasValue(field))
                    throw new ArgumentNullException(field, "Field '" + field + "' is missing.");
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
                { throw new ArgumentException($"Field '{field}': '{value}' could not be parsed as '{type}' | {e.Message}", field); }
            }
            catch (Exception e)
            {
                if (field == "name") throw; // Sanity check for field "name" to avoid potential stack overflow.
                if (defaultRocket != null)
                {
                    // Give a warning about the missing or invalid value, then use the default value using reflection to find the field.
                    if (field == "DisplayName") return string.Empty;
                    var defaultValue = typeof(RocketInfo).GetProperty(field == "DisplayName" ? "name" : field, BindingFlags.Public | BindingFlags.Instance).GetValue(defaultRocket);

                    if (field == "apMod" || field == "EMP" || field == "nuclear" || field == "beehive" || field == "subMunitionType" || field == "choker" || field == "gravitic" || field == "impulse" || field == "massMod" || field == "force")
                    {
                        //not having these throw an error message since these are all optional and default to false, prevents bullet defs from bloating like rockets did
                    }
                    else
                    {
                        string name = "unknown";
                        try { name = (string)ParseField(node, "name", typeof(string)); } catch { }
                        Debug.LogError($"[BDArmory.BulletInfo]: Using default value of {defaultValue} for {field} of {name} | {e.Message}");
                    }
                    return defaultValue;
                }
                else
                    throw;
            }
        }
    }

    public class RocketInfos : List<RocketInfo>
    {
        public RocketInfo this[string name]
        {
            get { return Find((value) => { return value.name == name; }); }
        }
    }
}
