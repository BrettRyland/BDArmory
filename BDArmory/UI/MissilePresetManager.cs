using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using BDArmory.Weapons.Missiles;

namespace BDArmory.UI
{
    /// <summary>
    /// Save/load named guidance presets for modular missiles.
    /// Stored as ConfigNode file under PluginData (survives DLL redeploys).
    /// </summary>
    public static class MissilePresetManager
    {
        public static string PresetsFile =>
            Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "BDArmory", "PluginData", "MissilePresets.cfg"));

        private static readonly string[] FloatFields =
        {
            "ActiveRadarRange", "ChaffEffectivity", "maxOffBoresight", "missileFireAngle",
            "MissileCMRange", "MissileCMInterval", "terminalHomingRange", "TerminalHomingRange",
            "MaxSteer", "SteerDamping", "SteerMult",
            "dnpFarRange", "dnpNearRange", "dnpFarRangePct", "dnpNearRangePct",
            "dnpFarDamp", "dnpFarPow", "dnpNearDamp", "dnpNearPow",
            "minStaticLaunchRange", "maxStaticLaunchRange",
            "StagesNumber", "StageToTriggerOnProximity", "timeBetweenStages",
            "MinSpeedGuidance", "MaxSpeed", "gpsUpdates", "clearanceRadius", "clearanceLength", "dropTime",
            "CruiseAltitude", "CruiseSpeed", "CruisePredictionTime",
            "CruisePopupAngle", "CruisePopupAltitude", "CruisePopupRange",
            "BallisticOverShootFactor", "BallisticAngle",
            "LoftMaxAltitude", "LoftRangeOverride", "LoftAltitudeAdvMax", "LoftMinAltitude",
            "LoftAngle", "LoftTermAngle", "LoftRangeFac", "LoftVelComp", "LoftVertVelComp",
            "kappaAngle", "DetonationDistance", "detonationTime",
            "customTurretID", "customTurretLoftFac", "priority"
        };

        private static readonly string[] BoolFields =
        {
            "HasIFF", "terminalHoming",
            "rangeBasedDnP", "dnpUseRangePercent", "RollCorrection",
            "UseStaticMaxLaunchRange", "CruisePopup",
            "DetonateAtMinimumDistance", "customTurretLoft", "inCargoBay"
        };

        public static List<string> GetPresetNames()
        {
            var names = new List<string>();
            try
            {
                ConfigNode file = ConfigNode.Load(PresetsFile);
                ConfigNode root = file != null ? file.GetNode("BDAMISSILEPRESETS") : null;
                if (root == null) return names;
                foreach (ConfigNode n in root.GetNodes("PRESET"))
                {
                    string nm = n.GetValue("name");
                    if (!string.IsNullOrEmpty(nm)) names.Add(nm);
                }
            }
            catch { }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public static void SavePreset(string name, BDModularGuidance m)
        {
            if (string.IsNullOrEmpty(name) || m == null) return;
            try
            {
                ConfigNode file = ConfigNode.Load(PresetsFile) ?? new ConfigNode();
                ConfigNode root = file.GetNode("BDAMISSILEPRESETS") ?? file.AddNode("BDAMISSILEPRESETS");
                ConfigNode node = null;
                foreach (ConfigNode n in root.GetNodes("PRESET"))
                {
                    if (string.Equals(n.GetValue("name"), name, StringComparison.OrdinalIgnoreCase)) { node = n; break; }
                }
                if (node == null) { node = root.AddNode("PRESET"); }
                node.ClearData();
                node.AddValue("name", name);
                node.AddValue("GuidanceIndex", m.GuidanceIndex);
                node.AddValue("TargetingMode", m.TargetingMode.ToString());
                foreach (string f in FloatFields)
                {
                    var field = m.Fields[f];
                    if (field == null) continue;
                    try
                    {
                        float v = Convert.ToSingle(field.GetValue(m), CultureInfo.InvariantCulture);
                        node.AddValue(f, v.ToString("R", CultureInfo.InvariantCulture));
                    }
                    catch { }
                }
                foreach (string f in BoolFields)
                {
                    var field = m.Fields[f];
                    if (field == null) continue;
                    try
                    {
                        bool v = Convert.ToBoolean(field.GetValue(m), CultureInfo.InvariantCulture);
                        node.AddValue(f, v ? "True" : "False");
                    }
                    catch { }
                }
                file.Save(PresetsFile);
            }
            catch (Exception e) { Debug.Log("[BDArmory.MissilePresetManager]: SavePreset failed: " + e.Message); }
        }

        public static bool LoadPreset(string name, BDModularGuidance m)
        {
            if (string.IsNullOrEmpty(name) || m == null) return false;
            try
            {
                ConfigNode file = ConfigNode.Load(PresetsFile);
                ConfigNode root = file != null ? file.GetNode("BDAMISSILEPRESETS") : null;
                if (root == null) return false;
                ConfigNode node = null;
                foreach (ConfigNode n in root.GetNodes("PRESET"))
                {
                    if (string.Equals(n.GetValue("name"), name, StringComparison.OrdinalIgnoreCase)) { node = n; break; }
                }
                if (node == null) return false;
                string gi = node.GetValue("GuidanceIndex");
                int gidx;
                if (int.TryParse(gi, out gidx)) m.SetGuidanceMode(gidx);
                string tm = node.GetValue("TargetingMode");
                if (!string.IsNullOrEmpty(tm)) m.SetTargetingMode(tm);
                foreach (string f in FloatFields)
                {
                    string s = node.GetValue(f);
                    float v;
                    if (string.IsNullOrEmpty(s) || !float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                    var field = m.Fields[f];
                    if (field == null) continue;
                    try { field.SetValue(v, m); } catch { }
                }
                foreach (string f in BoolFields)
                {
                    string s = node.GetValue(f);
                    if (string.IsNullOrEmpty(s)) continue;
                    bool v = s.Equals("True", StringComparison.OrdinalIgnoreCase) || s == "1";
                    var field = m.Fields[f];
                    if (field == null) continue;
                    try { field.SetValue(v, m); } catch { }
                }
                m.RefreshGuidanceModePublic();
                return true;
            }
            catch (Exception e) { Debug.Log("[BDArmory.MissilePresetManager]: LoadPreset failed: " + e.Message); }
            return false;
        }

        public static void DeletePreset(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                ConfigNode file = ConfigNode.Load(PresetsFile);
                ConfigNode root = file != null ? file.GetNode("BDAMISSILEPRESETS") : null;
                if (root == null) return;
                foreach (ConfigNode n in root.GetNodes("PRESET"))
                {
                    if (string.Equals(n.GetValue("name"), name, StringComparison.OrdinalIgnoreCase))
                    {
                        root.RemoveNode(n);
                        break;
                    }
                }
                file.Save(PresetsFile);
            }
            catch (Exception e) { Debug.Log("[BDArmory.MissilePresetManager]: DeletePreset failed: " + e.Message); }
        }
    }
}
