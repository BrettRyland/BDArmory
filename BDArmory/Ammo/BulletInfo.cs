using BDArmory.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static BDArmory.Bullets.PooledBullet;

namespace BDArmory.Bullets
{
    public class BulletInfo
    {
        public string name { get; private set; }
        public string DisplayName { get; private set; }
        public bool useDisplayName { get; private set; }
        public float caliber { get; private set; }
        public float bulletMass { get; private set; }
        public float bulletVelocity { get; private set; }
        public string explosive { get; private set; } //left for legacy support
        public bool incendiary { get; private set; } //left for legacy support
                                                     //public string attributeTags { get; private set; } //replace this with a string? tags to add: HE, incendiary, EMP, nuclear, beehive, homing, massmod, impulse; 
                                                     //nuclear can use tntmass for kT, beehive can use submunition#, would need submunition bulletType, homing would need degrees/s, massmod needs mass mod, impulse needs impulse
        public bool EMP { get; private set; }
        public bool nuclear { get; private set; }
        public bool beehive { get; private set; }
        public string subMunitionType { get; private set; }
        public float tntMass { get; private set; }
        public float massMod { get; private set; }
        public float impulse { get; private set; }
        public string fuzeType { get; private set; }
        public float guidanceDPS { get; private set; }
        public float guidanceRange { get; private set; }
        public int projectileCount { get; private set; }
        public float subProjectileDispersion { get; private set; }
        public float projectileTTL { get; private set; }
        public float apBulletMod { get; private set; }
        public string bulletDragTypeName { get; private set; }
        public Color projectileColorC { get; private set; }
        public string projectileColor { get; private set; }
        public Color startColorC { get; private set; }
        public string startColor { get; private set; }
        public bool fadeColor { get; private set; }
        // Parsed types
        public PooledBulletTypes eHEType { get; private set; }
        public BulletFuzeTypes eFuzeType { get; private set; }
        public float fuzeSensitivity { get; private set; } = -1f;
        public float fuzeDelay { get; private set; } = -1f;
        public BulletDragTypes bulletDragType { get; private set; }
        // Calculated Values
        public float bulletBallisticCoefficient { get; private set; }
        public bool sabot { get; private set; }

        public static BulletInfos bullets;
        public static HashSet<string> bulletNames;
        public static BulletInfo defaultBullet;

        // Fixes for old configs
        private static readonly List<(string, string)> oldSubmunitionConfigs = [];

        public BulletInfo(string name, string DisplayName, bool useDisplayName, float caliber, float bulletVelocity, float bulletMass,
                          string explosive, bool incendiary, float tntMass, bool EMP, bool nuclear, bool beehive, string subMunitionType, float massMod, float impulse, string fuzeType, float guidanceDPS, float guidanceRange,
                          float apBulletDmg, int projectileCount, float subProjectileDispersion, float projectileTTL, string bulletDragTypeName, string projectileColor, string startColor, bool fadeColor)
        {
            this.name = name;
            this.DisplayName = string.IsNullOrEmpty(DisplayName) ? name : DisplayName;
            this.useDisplayName = useDisplayName;
            this.caliber = caliber;
            this.bulletVelocity = bulletVelocity;
            this.bulletMass = bulletMass;
            this.explosive = explosive;
            this.incendiary = incendiary;
            this.tntMass = tntMass;
            this.EMP = EMP;
            this.nuclear = nuclear;
            this.beehive = beehive;
            this.subMunitionType = subMunitionType;
            this.massMod = massMod;
            this.impulse = impulse;
            this.fuzeType = fuzeType;
            this.guidanceDPS = guidanceDPS;
            this.guidanceRange = guidanceRange;
            this.apBulletMod = apBulletDmg;
            this.projectileCount = projectileCount;
            this.subProjectileDispersion = subProjectileDispersion;
            this.projectileTTL = projectileTTL;
            this.bulletDragTypeName = bulletDragTypeName;
            this.projectileColor = projectileColor;
            this.projectileColorC = GUIUtils.ParseColor255(projectileColor);
            this.startColor = startColor;
            this.startColorC = GUIUtils.ParseColor255(startColor);
            this.fadeColor = fadeColor;
        }

        public static void Load()
        {
            if (bullets != null) return; // Only load the bullet defs once on startup.
            bullets = new BulletInfos();
            if (bulletNames == null) bulletNames = new HashSet<string>();
            UrlDir.UrlConfig[] nodes = GameDatabase.Instance.GetConfigs("BULLET");
            ConfigNode node;

            // First locate BDA's default bullet definition so we can fill in missing fields.
            if (defaultBullet == null)
                for (int i = 0; i < nodes.Length; ++i)
                {
                    if (nodes[i].parent.name != "BD_Bullets") continue; // Ignore other config files.
                    node = nodes[i].config;
                    if (!node.HasValue("name") || (string)ParseField(nodes[i].config, "name", typeof(string)) != "def") continue; // Ignore other configs.
                    if (nodes[i].parent.parent == null || nodes[i].parent.parent.parent == null || nodes[i].parent.parent.parent.name != "BDArmory") continue;
                    Debug.Log($"[BDArmory.BulletInfo]: Parsing default bullet definition from {nodes[i].parent.name}");
                    //tagsList = Misc.BDAcTools.ParseNames((string)ParseField(node, "attributeTags", typeof(string))); //would prefer not to do a rocketInfo and have separate node fields for every attribute
                    defaultBullet = ParseBulletInfoNode(node, "def");
                    defaultBullet.projectileCount = Math.Max(defaultBullet.projectileCount, 1);
                    defaultBullet.subProjectileDispersion = -1f;
                    defaultBullet.ParseTypes();
                    defaultBullet.PreCalcData();
                    bullets.Add(defaultBullet);
                    bulletNames.Add("def");
                    break;
                }
            if (defaultBullet == null) throw new ArgumentException("Failed to find BDArmory's default bullet definition.", "defaultBullet");

            // Now add in the rest of the bullets.
            for (int i = 0; i < nodes.Length; i++)
            {
                AddBulletInfo(nodes[i]);
            }
            PostProcessOldSubmunitionConfigs();
        }

        private static void AddBulletInfo(UrlDir.UrlConfig nodeURL)
        {
            string name_ = "";
            try
            {
                ConfigNode node = nodeURL.config;
                name_ = (string)ParseField(node, "name", typeof(string));
                string parentName = nodeURL.parent.name != "part" ? nodeURL.parent.name : nodeURL.parent.parent.name;
                if (bulletNames.Contains(name_)) // Avoid duplicates.
                {
                    if (parentName != "BD_Bullets" || name_ != "def") // Don't report the default bullet definition as a duplicate.
                        Debug.LogError($"[BDArmory.BulletInfo]: Bullet definition {name_} from {parentName} already exists, skipping.");
                    return;
                }
                Debug.Log($"[BDArmory.BulletInfo]: Parsing definition of bullet {name_} from {parentName}");
                BulletInfo tempBullet = ParseBulletInfoNode(node, name_);
                tempBullet.ParseTypes();
                tempBullet.PreCalcData();
                bullets.Add(tempBullet);
                bulletNames.Add(name_);
            }
            catch (Exception e)
            {
                Debug.LogError($"[BDArmory.BulletInfo]: Error Loading Bullet Config '{name_}' | {e.ToString()}");
            }
        }

        private static BulletInfo ParseBulletInfoNode(ConfigNode node, string name_)
        {
            return new BulletInfo(
                        name_,
                        (string)ParseField(node, nameof(DisplayName), typeof(string)),
                        (bool)ParseField(node, nameof(useDisplayName), typeof(bool)),
                        (float)ParseField(node, nameof(caliber), typeof(float)),
                        (float)ParseField(node, nameof(bulletVelocity), typeof(float)),
                        (float)ParseField(node, nameof(bulletMass), typeof(float)),
                        (string)ParseField(node, nameof(explosive), typeof(string)),
                        (bool)ParseField(node, nameof(incendiary), typeof(bool)),
                        (float)ParseField(node, nameof(tntMass), typeof(float)),
                        (bool)ParseField(node, nameof(EMP), typeof(bool)),
                        (bool)ParseField(node, nameof(nuclear), typeof(bool)),
                        (bool)ParseField(node, nameof(beehive), typeof(bool)),
                        (string)ParseField(node, nameof(subMunitionType), typeof(string)),
                        (float)ParseField(node, nameof(massMod), typeof(float)),
                        (float)ParseField(node, nameof(impulse), typeof(float)),
                        (string)ParseField(node, nameof(fuzeType), typeof(string)),
                        (float)ParseField(node, nameof(guidanceDPS), typeof(float)),
                        (float)ParseField(node, nameof(guidanceRange), typeof(float)),
                        (float)ParseField(node, nameof(apBulletMod), typeof(float)),
                        (int)ParseField(node, nameof(projectileCount), typeof(int)),
                        (float)ParseField(node, nameof(subProjectileDispersion), typeof(float)),
                        (float)ParseField(node, nameof(projectileTTL), typeof(float)),
                        (string)ParseField(node, nameof(bulletDragTypeName), typeof(string)),
                        (string)ParseField(node, nameof(projectileColor), typeof(string)),
                        (string)ParseField(node, nameof(startColor), typeof(string)),
                        (bool)ParseField(node, nameof(fadeColor), typeof(bool))
                        );
        }

        private static object ParseField(ConfigNode node, string field, Type type)
        {
            try
            {
                if (!node.HasValue(field))
                {
                    throw new ArgumentNullException(field, "Field '" + field + "' is missing.");
                }
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
                if (defaultBullet != null)
                {
                    // Give a warning about the missing or invalid value, then use the default value using reflection to find the field.
                    if (field == "DisplayName") return string.Empty;
                    var defaultValue = typeof(BulletInfo).GetProperty(field == "DisplayName" ? "name" : field, BindingFlags.Public | BindingFlags.Instance).GetValue(defaultBullet); //this is returning the def bullet name, not current bullet name
                    if (field == "EMP" || field == "nuclear" || field == "beehive" || field == "subMunitionType" || field == "massMod" || field == "impulse" || field == "subProjectileDispersion" || field == "guidanceDPS" ||field == "projectileTTL" || (field == "projectileCount" && node.HasValue("subProjectileDispersion")))
                    {
                        //not having these throw an error message since these are all optional and default to false, prevents bullet defs from bloating like rockets did
                        //Future SI - apply this to rocket, mutator defs
                    }
                    else if (field == "projectileCount" && node.HasValue("subProjectileCount")) // Old projectile/subprojectile bullet def
                    {
                        try
                        {
                            string name = (string)ParseField(node, "name", typeof(string));
                            int projectileCount = (int)ParseField(node, "subProjectileCount", type); // Treat the subProjectileCount as projectileCount.
                            Debug.LogWarning($"[BDArmory.BulletInfo]: Old bullet def detected for {name}, using subProjectileCount ({projectileCount}) for projectileCount. Please upgrade your mod's bullet defs.");
                            if (node.HasValue("subMunitionType")) oldSubmunitionConfigs.Add((name, (string)ParseField(node, "subMunitionType", typeof(string))));
                            return projectileCount;
                        }
                        catch (Exception e2)
                        {
                            Debug.LogError($"[BDArmory.BulletInfo]: Old bullet def detected, but failed to parse subProjectileCount. Using default value of {defaultValue} for projectileCount.\n{e2}");
                            return defaultValue;
                        }
                    }
                    else
                    {
                        string name = "unknown";
                        try { name = (string)ParseField(node, "name", typeof(string)); } catch { }
                        Debug.LogWarning($"[BDArmory.BulletInfo]: Using default value of {defaultValue} for {field} of {name} | {e.Message}");
                    }
                    return defaultValue;
                }
                else
                    throw;
            }
        }

        private static void PostProcessOldSubmunitionConfigs()
        {
            if (oldSubmunitionConfigs.Count == 0) return;
            Debug.LogWarning($"[BDArmory.BulletInfo]: Attempting to correct bullet definitions with old submunition configs. This may cause irregularities or failures in weapons using these bullet definitions. Please upgrade your configs ASAP.");
            try
            {
                foreach (var pair in oldSubmunitionConfigs)
                {
                    if (!bullets.Exists(b => b.name == pair.Item1) || !bullets.Exists(b => b.name == pair.Item2))
                    {
                        Debug.LogWarning($"[BDArmory.BulletInfo]: One or more of {pair.Item1} and {pair.Item2} is missing from the bullet definitions, unable to correct the config.");
                        continue;
                    }
                    var bullet = bullets[pair.Item1];
                    var submunition = bullets[pair.Item2];
                    if (bullet.projectileCount == 1 && submunition.projectileCount > 1)
                    {
                        bullet.subMunitionType += $"; {submunition.projectileCount}";
                        Debug.LogWarning($"[BDArmory.BulletInfo]: Updating {bullet.name} to have {submunition.projectileCount} sub-projectiles of type {submunition.name}");
                    }
                    if (submunition.projectileCount != 1)
                    {
                        submunition.projectileCount = 1; // Submunitions shouldn't contain multiple projectiles (no recursion).
                        Debug.LogWarning($"[BDArmory.BulletInfo]: Updating {submunition.name} to be a single projectile.");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[BDArmory.BulletInfo]: Failed to post-process old submunition configs, expect irregularities or failures: {e}");
            }
            oldSubmunitionConfigs.Clear();
        }

        private void ParseTypes()
        {
            if (tntMass > 0)
                eHEType = explosive.ToLower() switch
                {
                    "standard" or "true" => PooledBulletTypes.Explosive,
                    "shaped" => PooledBulletTypes.Shaped,
                    _ => PooledBulletTypes.Slug
                };
            else
                eHEType = PooledBulletTypes.Slug;

            bulletDragType = bulletDragTypeName.ToLower() switch
            {
                "none" => BulletDragTypes.None,
                "numericalintegration" => BulletDragTypes.NumericalIntegration,
                "analyticestimate" => BulletDragTypes.AnalyticEstimate,
                _ => BulletDragTypes.AnalyticEstimate
            };

            if (tntMass > 0 || beehive)
            {
                string[] fuzeStrings = fuzeType.Split([',']);
                if (fuzeStrings.Length > 0)
                {
                    eFuzeType = fuzeStrings[0].ToLower() switch
                    {
                        //Anti-Air fuzes
                        "timed" => BulletFuzeTypes.Timed,
                        "proximity" => BulletFuzeTypes.Proximity,
                        "flak" => BulletFuzeTypes.Flak,
                        //Anti-Armor fuzes
                        "delay" => BulletFuzeTypes.Delay,
                        "penetrating" => BulletFuzeTypes.Penetrating,
                        "impact" => BulletFuzeTypes.Impact,
                        "none" => beehive ? BulletFuzeTypes.Timed : BulletFuzeTypes.Impact,
                        _ => beehive ? BulletFuzeTypes.Timed : BulletFuzeTypes.Impact
                    };
                }
                else
                {
                    eFuzeType = beehive ? BulletFuzeTypes.Timed : BulletFuzeTypes.Impact;
                }
                

                if (eFuzeType == BulletFuzeTypes.Delay && fuzeStrings.Length > 1)
                {
                    if (float.TryParse(fuzeStrings[1], out float temp))
                        fuzeDelay = temp;
                }
                else if (eFuzeType == BulletFuzeTypes.Penetrating && fuzeStrings.Length > 1)
                {
                    if (float.TryParse(fuzeStrings[1], out float temp))
                        fuzeDelay = temp;
                    if (fuzeStrings.Length > 2 && float.TryParse(fuzeStrings[2], out temp))
                        fuzeSensitivity = temp;
                }
            }
            else
            {
                eFuzeType = BulletFuzeTypes.None;
            }
        }

        private void PreCalcData()
        {
            bulletBallisticCoefficient = PooledBullet.calcBulletBallisticCoefficient(caliber, bulletMass);

            sabot = (eHEType == PooledBulletTypes.Slug && PooledBullet.isSabot(bulletMass, caliber));
        }
    }

    public class BulletInfos : List<BulletInfo>
    {
        public BulletInfo this[string name]
        {
            get { return Find((value) => { return value.name == name; }); }
        }
    }
}
