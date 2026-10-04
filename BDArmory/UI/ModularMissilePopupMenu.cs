using System;
using System.Collections.Generic;
using UnityEngine;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.Weapons.Missiles;

namespace BDArmory.UI
{
    /// <summary>
    /// Scalable popup editor for modular missiles (BDModularGuidance).
    /// Rows are descriptors (MissileSettingItem); adding a parameter = one line
    /// in BuildSettings(). The render loop never changes.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.FlightAndEditor, false)]
    public class ModularMissilePopupMenu : MonoBehaviour
    {
        public const int TabMain = 0;
        public const int TabStages = 1;
        public const int TabAdvanced = 2;
        public const int TabPayload = 3;

        private static readonly string[] TabKeys =
        {
            "#LOC_BDArmory_Tab_Main", "#LOC_BDArmory_Tab_Stages",
            "#LOC_BDArmory_Tab_Advanced", "#LOC_BDArmory_Tab_Payload"
        };

        private static readonly string[] GuidanceOptions = { "AAM", "AGM/STS", "Cruise", "Ballistic", "PN", "APN", "Orbital", "AAM Loft" };

        public static ModularMissilePopupMenu Instance;

        private bool open;
        private BDModularGuidance targetModule;
        private readonly List<MissileSettingItem> settings = new List<MissileSettingItem>();
        private readonly List<MissileSettingItem> visible = new List<MissileSettingItem>();
        private Vector2 scrollPos;
        private Rect windowRect = new Rect(150, 120, 560, 600);
        private int currentTab;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        public void Open(BDModularGuidance module)
        {
            if (module == null) return;
            targetModule = module;
            currentTab = TabMain;
            scrollPos = Vector2.zero;
            BuildSettings();
            windowRect.x = Mathf.Clamp(windowRect.x, 0, Mathf.Max(0, Screen.width - 200));
            windowRect.y = Mathf.Clamp(windowRect.y, 0, Mathf.Max(0, Screen.height - 200));
            open = true;
        }

        public void Close() { open = false; targetModule = null; }

        private static string OffOrF0(float v) { return v < 0f ? "Off" : v.ToString("F0"); }

        void BuildSettings()
        {
            settings.Clear();
            if (targetModule == null) return;
            var m = targetModule;
            bool inEditor = HighLogic.LoadedSceneIsEditor;
            bool inFlight = HighLogic.LoadedSceneIsFlight;

            // ---------- Main: Name ----------
            settings.Add(new SectionItem(TabMain, "#LOC_BDArmory_Sec_Name"));
            settings.Add(MissileSettingItem.Text(TabMain, "WeaponName", "#LOC_BDArmory_WeaponName",
                () => m.WeaponName ?? "", v => m.SetWeaponName(v)));

            // ---------- Main: Modes ----------
            settings.Add(new SectionItem(TabMain, "#LOC_BDArmory_Sec_Modes"));
            string[] targetingNames = Enum.GetNames(typeof(MissileBase.TargetingModes));
            settings.Add(MissileSettingItem.Choice(TabMain, "GuidanceMode", "#LOC_BDArmory_GuidanceMode",
                new[] { "1", "2", "3", "4", "5", "6", "7", "8" }, GuidanceOptions,
                () => m.GuidanceIndex - 1, v => m.SetGuidanceMode(v + 1),
                () => inEditor));
            settings.Add(MissileSettingItem.Choice(TabMain, "TargetingMode", "#LOC_BDArmory_TargetingMode",
                targetingNames, targetingNames,
                () => Array.IndexOf(targetingNames, m.TargetingMode.ToString()), v => m.SetTargetingMode(targetingNames[v]),
                () => inEditor));
            settings.Add(MissileSettingItem.Button(TabMain, "FireMissile", "#LOC_BDArmory_FireMissile",
                () => m.FireMissile(), null, () => inFlight && !m.HasFired));
            settings.Add(MissileSettingItem.Button(TabMain, "Jettison", "#LOC_BDArmory_Jettison",
                () => m.Jettison(), null, () => inFlight && !m.HasFired));

            // ---------- Main: Sensors ----------
            settings.Add(new SectionItem(TabMain, "#LOC_BDArmory_Sec_Sensors"));
            settings.Add(MissileSettingItem.Slider(TabMain, "ActiveRadarRange", "#LOC_BDArmory_ActiveRadarRange",
                () => m.ActiveRadarRange, v => m.ActiveRadarRange = v, 0f, 50000f, 1000f, v => v.ToString("F0")));
            settings.Add(MissileSettingItem.Slider(TabMain, "ChaffEffectivity", "#LOC_BDArmory_ChaffFactor",
                () => m.ChaffEffectivity, v => m.ChaffEffectivity = v, 0f, 2f, 0.1f));
            settings.Add(MissileSettingItem.Slider(TabMain, "maxOffBoresight", "#LOC_BDArmory_MaxOffBoresight",
                () => m.maxOffBoresight, v => m.maxOffBoresight = v, 0f, 180f, 5f, v => v.ToString("F0")));
            settings.Add(MissileSettingItem.Slider(TabMain, "missileFireAngle", "#LOC_BDArmory_FiringAngle",
                () => m.missileFireAngle, v => m.missileFireAngle = v, 1f, 90f, 1f, v => v.ToString("F0"),
                () => m.maxOffBoresight > 0f));
            settings.Add(MissileSettingItem.Toggle(TabMain, "HasIFF", "#LOC_BDArmory_MissileIFF",
                () => m.HasIFF, v => m.HasIFF = v));

            // ---------- Main: Steering ----------
            settings.Add(new SectionItem(TabMain, "#LOC_BDArmory_Sec_Steering",
                () => m.GuidanceMode != MissileBase.GuidanceModes.Orbital));
            settings.Add(MissileSettingItem.Slider(TabMain, "MaxSteer", "#LOC_BDArmory_AI_SteerLimiter",
                () => m.MaxSteer, v => m.MaxSteer = v, 0.1f, 1f, 0.05f,
                visibleCondition: () => m.GuidanceMode != MissileBase.GuidanceModes.Orbital));
            settings.Add(MissileSettingItem.Slider(TabMain, "SteerDamping", "#LOC_BDArmory_AI_SteerDamping",
                () => m.SteerDamping, v => m.SteerDamping = v, 0f, 20f, 0.05f,
                visibleCondition: () => m.GuidanceMode != MissileBase.GuidanceModes.Orbital));
            settings.Add(MissileSettingItem.Slider(TabMain, "SteerMult", "#LOC_BDArmory_AI_SteerPower",
                () => m.SteerMult, v => m.SteerMult = v, 0.1f, 20f, 0.1f,
                visibleCondition: () => m.GuidanceMode != MissileBase.GuidanceModes.Orbital));
            settings.Add(MissileSettingItem.Toggle(TabMain, "RollCorrection", "#LOC_BDArmory_RollCorrection",
                () => m.RollCorrection, v => m.RollCorrection = v,
                () => m.GuidanceMode != MissileBase.GuidanceModes.Orbital));

            // ---------- Main: Loft / Cruise / Ballistic ----------
            settings.Add(new SectionItem(TabMain, "#LOC_BDArmory_Sec_Cruise",
                () => m.GuidanceMode == MissileBase.GuidanceModes.Cruise));
            settings.Add(MissileSettingItem.Slider(TabMain, "CruiseAltitude", "#LOC_BDArmory_CruiseAltitude",
                () => m.CruiseAltitude, v => m.CruiseAltitude = v, 5f, 500f, 5f, v => v.ToString("F0"),
                () => m.GuidanceMode == MissileBase.GuidanceModes.Cruise));
            settings.Add(MissileSettingItem.Slider(TabMain, "CruiseSpeed", "#LOC_BDArmory_Missile_CruiseSpeed",
                () => m.CruiseSpeed, v => m.CruiseSpeed = v, 100f, 6000f, 50f, v => v.ToString("F0"),
                () => m.GuidanceMode == MissileBase.GuidanceModes.Cruise));
            settings.Add(MissileSettingItem.Toggle(TabMain, "CruisePopup", "#LOC_BDArmory_CruisePopup",
                () => m.CruisePopup, v => m.CruisePopup = v,
                () => m.GuidanceMode == MissileBase.GuidanceModes.Cruise));
            settings.Add(MissileSettingItem.Slider(TabMain, "CruisePopupAngle", "#LOC_BDArmory_CruisePopup",
                () => m.CruisePopupAngle, v => m.CruisePopupAngle = v, 0f, 90f, 1f, v => v.ToString("F0"),
                () => m.GuidanceMode == MissileBase.GuidanceModes.Cruise && m.CruisePopup));
            settings.Add(MissileSettingItem.Slider(TabMain, "CruisePopupAltitude", "#LOC_BDArmory_CruisePopup",
                () => m.CruisePopupAltitude, v => m.CruisePopupAltitude = v, 0f, 5000f, 50f, v => v.ToString("F0"),
                () => m.GuidanceMode == MissileBase.GuidanceModes.Cruise && m.CruisePopup));
            settings.Add(MissileSettingItem.Slider(TabMain, "CruisePopupRange", "#LOC_BDArmory_CruisePopup",
                () => m.CruisePopupRange, v => m.CruisePopupRange = v, 0f, 20000f, 100f, v => v.ToString("F0"),
                () => m.GuidanceMode == MissileBase.GuidanceModes.Cruise && m.CruisePopup));

            // ---------- Main: Ballistic ----------
            settings.Add(new SectionItem(TabMain, "#LOC_BDArmory_Sec_Ballistic",
                () => m.GuidanceMode == MissileBase.GuidanceModes.AGMBallistic));
            settings.Add(MissileSettingItem.Slider(TabMain, "BallisticAngle", "#LOC_BDArmory_BallisticAnglePath",
                () => m.BallisticAngle, v => m.BallisticAngle = v, 5f, 60f, 5f, v => v.ToString("F0"),
                () => m.GuidanceMode == MissileBase.GuidanceModes.AGMBallistic));

            // ---------- Main: Loft ----------
            settings.Add(new SectionItem(TabMain, "#LOC_BDArmory_Sec_Loft",
                () => m.GuidanceMode == MissileBase.GuidanceModes.AAMLoft));
            settings.Add(MissileSettingItem.Slider(TabMain, "LoftMaxAltitude", "#LOC_BDArmory_LoftMaxAltitude",
                () => m.LoftMaxAltitude, v => m.LoftMaxAltitude = v, 5000f, 30000f, 100f, v => v.ToString("F0"),
                () => m.GuidanceMode == MissileBase.GuidanceModes.AAMLoft));
            settings.Add(MissileSettingItem.Slider(TabMain, "LoftAltitudeAdvMax", "#LOC_BDArmory_LoftAltitudeAdvMax",
                () => m.LoftAltitudeAdvMax, v => m.LoftAltitudeAdvMax = v, 500f, 10000f, 100f, v => v.ToString("F0"),
                () => m.GuidanceMode == MissileBase.GuidanceModes.AAMLoft));
            settings.Add(MissileSettingItem.Slider(TabMain, "LoftMinAltitude", "#LOC_BDArmory_LoftMinAltitude",
                () => m.LoftMinAltitude, v => m.LoftMinAltitude = v, 0f, 10000f, 100f, v => v.ToString("F0"),
                () => m.GuidanceMode == MissileBase.GuidanceModes.AAMLoft));
            settings.Add(MissileSettingItem.Slider(TabMain, "LoftAngle", "#LOC_BDArmory_LoftAngle",
                () => m.LoftAngle, v => m.LoftAngle = v, 0f, 90f, 0.5f, v => v.ToString("F1"),
                () => m.GuidanceMode == MissileBase.GuidanceModes.AAMLoft && GameSettings.ADVANCED_TWEAKABLES));
            settings.Add(MissileSettingItem.Slider(TabMain, "LoftTermAngle", "#LOC_BDArmory_LoftTermAngle",
                () => m.LoftTermAngle, v => m.LoftTermAngle = v, 0f, 90f, 0.5f, v => v.ToString("F1"),
                () => m.GuidanceMode == MissileBase.GuidanceModes.AAMLoft && GameSettings.ADVANCED_TWEAKABLES));

            settings.Add(new SectionItem(TabMain, "#LOC_BDArmory_Sec_Kappa",
                () => m.GuidanceMode == MissileBase.GuidanceModes.Kappa));
            settings.Add(MissileSettingItem.Slider(TabMain, "kappaAngle", "#LOC_BDArmory_KappaAngle",
                () => m.kappaAngle, v => m.kappaAngle = v, 0f, 90f, 0.5f, v => v.ToString("F1"),
                () => m.GuidanceMode == MissileBase.GuidanceModes.Kappa));

            // ---------- Main: Launch range ----------
            settings.Add(new SectionItem(TabMain, "#LOC_BDArmory_Sec_LaunchRange"));
            settings.Add(MissileSettingItem.Slider(TabMain, "minStaticLaunchRange", "#LOC_BDArmory_MinStaticLaunchRange",
                () => m.minStaticLaunchRange, v => m.minStaticLaunchRange = v, 10f, 4000f, 100f, v => v.ToString("F0")));
            settings.Add(MissileSettingItem.Slider(TabMain, "maxStaticLaunchRange", "#LOC_BDArmory_MaxStaticLaunchRange",
                () => m.maxStaticLaunchRange, v => m.maxStaticLaunchRange = v, 5000f, 50000f, 1000f, v => v.ToString("F0")));
            settings.Add(MissileSettingItem.Toggle(TabMain, "UseStaticMaxLaunchRange", "#LOC_BDArmory_UseStaticMaxLaunchRange",
                () => m.UseStaticMaxLaunchRange, v => m.UseStaticMaxLaunchRange = v));

            settings.Add(new SectionItem(TabMain, "#LOC_BDArmory_Sec_Terminal",
                () => m.terminalHoming || m.GuidanceMode == MissileBase.GuidanceModes.AAMLoft));
            settings.Add(MissileSettingItem.Toggle(TabMain, "terminalHoming", "#LOC_BDArmory_TerminalHoming_Enable",
                () => m.terminalHoming, v => m.terminalHoming = v));
            settings.Add(MissileSettingItem.Slider(TabMain, "terminalHomingRange", "#LOC_BDArmory_terminalHomingRange",
                () => m.terminalHomingRange, v => m.terminalHomingRange = v, 0f, 40000f, 500f, v => v.ToString("F0"),
                () => m.terminalHoming || m.GuidanceMode == MissileBase.GuidanceModes.AAMLoft));

            // ---------- Main: Warhead ----------
            settings.Add(new SectionItem(TabMain, "#LOC_BDArmory_Sec_Warhead"));
            settings.Add(MissileSettingItem.Slider(TabMain, "DetonationDistance", "#LOC_BDArmory_DetonationDistanceOverride",
                () => m.DetonationDistance, v => m.DetonationDistance = v, -1f, 1000f, 10f, OffOrF0));
            settings.Add(MissileSettingItem.Toggle(TabMain, "DetonateAtMinimumDistance", "#LOC_BDArmory_DetonateAtMinimumDistance",
                () => m.DetonateAtMinimumDistance, v => m.DetonateAtMinimumDistance = v));
            settings.Add(MissileSettingItem.Slider(TabMain, "detonationTime", "#LOC_BDArmory_DetonationTime",
                () => m.detonationTime, v => m.detonationTime = v, 2f, 30f, 0.5f, v => v.ToString("F1"),
                () => m.isTimed));

            settings.Add(new SectionItem(TabStages, "#LOC_BDArmory_Sec_Stages"));
            settings.Add(MissileSettingItem.Slider(TabStages, "StagesNumber", "#LOC_BDArmory_StagesNumber",
                () => m.StagesNumber, v => m.StagesNumber = v, 1f, 9f, 1f, v => v.ToString("F0")));
            settings.Add(MissileSettingItem.Slider(TabStages, "StageToTriggerOnProximity", "#LOC_BDArmory_StageToTriggerOnProximity",
                () => m.StageToTriggerOnProximity, v => m.StageToTriggerOnProximity = v, 0f, 6f, 1f, v => v.ToString("F0")));
            settings.Add(MissileSettingItem.Slider(TabStages, "timeBetweenStages", "#LOC_BDArmory_TimeBetweenStages",
                () => m.timeBetweenStages, v => m.timeBetweenStages = v, 0f, 5f, 0.5f, v => v.ToString("F1")));
            settings.Add(MissileSettingItem.Slider(TabStages, "MinSpeedGuidance", "#LOC_BDArmory_AI_MinSpeedGuidance",
                () => m.MinSpeedGuidance, v => m.MinSpeedGuidance = v, 0f, 1000f, 50f, v => v.ToString("F0")));
            settings.Add(MissileSettingItem.Slider(TabStages, "MaxSpeed", "#LOC_BDArmory_AI_MaxSpeed",
                () => m.MaxSpeed, v => m.MaxSpeed = v, 200f, 10000f, 100f, v => v.ToString("F0"),
                () => m.GuidanceMode == MissileBase.GuidanceModes.Orbital));
            settings.Add(MissileSettingItem.Slider(TabStages, "dropTime", "#LOC_BDArmory_DropTime",
                () => m.dropTime, v => m.dropTime = v, 0f, 5f, 0.1f, v => v.ToString("F1")));

            // ---------- Advanced (fine-tuning duplicates, same categories) ----------
            bool isCruise = m.GuidanceMode == MissileBase.GuidanceModes.Cruise;
            bool isBallistic = m.GuidanceMode == MissileBase.GuidanceModes.AGMBallistic;
            bool isLoft = m.GuidanceMode == MissileBase.GuidanceModes.AAMLoft;
            settings.Add(new SectionItem(TabAdvanced, "#LOC_BDArmory_Sec_Cruise", () => isCruise));
            settings.Add(MissileSettingItem.Slider(TabAdvanced, "CruisePredictionTime_adv", "#LOC_BDArmory_CruisePredictionTime",
                () => m.CruisePredictionTime, v => m.CruisePredictionTime = v, 1f, 15f, 1f, v => v.ToString("F0"),
                () => isCruise));
            settings.Add(new SectionItem(TabAdvanced, "#LOC_BDArmory_Sec_Ballistic", () => isBallistic));
            settings.Add(MissileSettingItem.Slider(TabAdvanced, "BallisticOverShootFactor_adv", "#LOC_BDArmory_BallisticOvershootFactor",
                () => m.BallisticOverShootFactor, v => m.BallisticOverShootFactor = v, 0.5f, 1.5f, 0.01f, null,
                () => isBallistic));
            settings.Add(new SectionItem(TabAdvanced, "#LOC_BDArmory_Sec_Loft", () => isLoft));
            settings.Add(MissileSettingItem.Slider(TabAdvanced, "LoftRangeOverride_adv", "#LOC_BDArmory_LoftRangeOverride",
                () => m.LoftRangeOverride, v => m.LoftRangeOverride = v, 0f, 40000f, 500f, v => v.ToString("F0"),
                () => isLoft));
            settings.Add(MissileSettingItem.Slider(TabAdvanced, "LoftRangeFac_adv", "#LOC_BDArmory_LoftRangeFac",
                () => m.LoftRangeFac, v => m.LoftRangeFac = v, 0.1f, 5f, 0.01f, null,
                () => isLoft));
            settings.Add(MissileSettingItem.Slider(TabAdvanced, "LoftVelComp_adv", "#LOC_BDArmory_LoftVelComp",
                () => m.LoftVelComp, v => m.LoftVelComp = v, -2f, 2f, 0.01f, null,
                () => isLoft));
            settings.Add(MissileSettingItem.Slider(TabAdvanced, "LoftVertVelComp_adv", "#LOC_BDArmory_LoftVertVelComp",
                () => m.LoftVertVelComp, v => m.LoftVertVelComp = v, -2f, 2f, 0.01f, null,
                () => isLoft));
            settings.Add(new SectionItem(TabAdvanced, "#LOC_BDArmory_Sec_Sensors"));
            settings.Add(MissileSettingItem.Slider(TabAdvanced, "MissileCMRange_adv", "#LOC_BDArmory_MissileCMRange",
                () => m.MissileCMRange, v => m.MissileCMRange = v, -1f, 10000f, 500f, OffOrF0));
            settings.Add(MissileSettingItem.Slider(TabAdvanced, "MissileCMInterval_adv", "#LOC_BDArmory_MissileCMInterval",
                () => m.MissileCMInterval, v => m.MissileCMInterval = v, 0f, 5f, 0.05f));
            settings.Add(new SectionItem(TabAdvanced, "#LOC_BDArmory_Sec_Steering",
                () => m.GuidanceMode != MissileBase.GuidanceModes.Orbital));
            settings.Add(MissileSettingItem.Toggle(TabAdvanced, "rangeBasedDnP_adv", "#LOC_BDArmory_AI_RangeBasedDnP",
                () => m.rangeBasedDnP, v => m.rangeBasedDnP = v,
                () => m.GuidanceMode != MissileBase.GuidanceModes.Orbital));
            settings.Add(MissileSettingItem.Toggle(TabAdvanced, "dnpUseRangePercent_adv", "#LOC_BDArmory_AI_DnPUsePercent",
                () => m.dnpUseRangePercent, v => m.dnpUseRangePercent = v,
                () => m.rangeBasedDnP && m.GuidanceMode != MissileBase.GuidanceModes.Orbital));
            settings.Add(MissileSettingItem.Slider(TabAdvanced, "dnpFarRange_adv", "#LOC_BDArmory_AI_DnPFarRange",
                () => m.dnpFarRange, v => m.dnpFarRange = v, 0f, 50000f, 100f, v => v.ToString("F0"),
                () => m.rangeBasedDnP && !m.dnpUseRangePercent && m.GuidanceMode != MissileBase.GuidanceModes.Orbital));
            settings.Add(MissileSettingItem.Slider(TabAdvanced, "dnpNearRange_adv", "#LOC_BDArmory_AI_DnPNearRange",
                () => m.dnpNearRange, v => m.dnpNearRange = v, 0f, 50000f, 100f, v => v.ToString("F0"),
                () => m.rangeBasedDnP && !m.dnpUseRangePercent && m.GuidanceMode != MissileBase.GuidanceModes.Orbital));
            settings.Add(MissileSettingItem.Slider(TabAdvanced, "dnpFarRangePct_adv", "#LOC_BDArmory_AI_DnPFarRangePct",
                () => m.dnpFarRangePct, v => m.dnpFarRangePct = v, 0f, 100f, 1f, v => v.ToString("F0"),
                () => m.rangeBasedDnP && m.dnpUseRangePercent && m.GuidanceMode != MissileBase.GuidanceModes.Orbital));
            settings.Add(MissileSettingItem.Slider(TabAdvanced, "dnpNearRangePct_adv", "#LOC_BDArmory_AI_DnPNearRangePct",
                () => m.dnpNearRangePct, v => m.dnpNearRangePct = v, 0f, 100f, 1f, v => v.ToString("F0"),
                () => m.rangeBasedDnP && m.dnpUseRangePercent && m.GuidanceMode != MissileBase.GuidanceModes.Orbital));
            settings.Add(MissileSettingItem.Slider(TabAdvanced, "dnpFarDamp_adv", "#LOC_BDArmory_AI_DnPFarDamp",
                () => m.dnpFarDamp, v => m.dnpFarDamp = v, 0f, 50f, 0.5f, null,
                () => m.rangeBasedDnP && m.GuidanceMode != MissileBase.GuidanceModes.Orbital));
            settings.Add(MissileSettingItem.Slider(TabAdvanced, "dnpFarPow_adv", "#LOC_BDArmory_AI_DnPFarPow",
                () => m.dnpFarPow, v => m.dnpFarPow = v, 0f, 50f, 0.5f, null,
                () => m.rangeBasedDnP && m.GuidanceMode != MissileBase.GuidanceModes.Orbital));
            settings.Add(MissileSettingItem.Slider(TabAdvanced, "dnpNearDamp_adv", "#LOC_BDArmory_AI_DnPNearDamp",
                () => m.dnpNearDamp, v => m.dnpNearDamp = v, 0f, 50f, 0.5f, null,
                () => m.rangeBasedDnP && m.GuidanceMode != MissileBase.GuidanceModes.Orbital));
            settings.Add(MissileSettingItem.Slider(TabAdvanced, "dnpNearPow_adv", "#LOC_BDArmory_AI_DnPNearPow",
                () => m.dnpNearPow, v => m.dnpNearPow = v, 0f, 50f, 0.5f, null,
                () => m.rangeBasedDnP && m.GuidanceMode != MissileBase.GuidanceModes.Orbital));
            settings.Add(MissileSettingItem.Slider(TabAdvanced, "clearanceRadius_adv", "#LOC_BDArmory_ClearanceRadius",
                () => m.clearanceRadius, v => m.clearanceRadius = v, 0f, 5f, 0.05f));
            settings.Add(MissileSettingItem.Slider(TabAdvanced, "clearanceLength_adv", "#LOC_BDArmory_ClearanceLength",
                () => m.clearanceLength, v => m.clearanceLength = v, 0f, 5f, 0.05f));

            // ---------- Payload (unchanged) ----------
            settings.Add(new SectionItem(TabPayload, "#LOC_BDArmory_Sec_Payload"));
            settings.Add(MissileSettingItem.Slider(TabPayload, "customTurretID", "#LOC_BDArmory_TurretID",
                () => m.customTurretID, v => m.customTurretID = v, 0f, 20f, 1f, v => v.ToString("F0")));
            settings.Add(MissileSettingItem.Toggle(TabPayload, "customTurretLoft", "#LOC_BDArmory_TurretLoft",
                () => m.customTurretLoft, v => m.customTurretLoft = v,
                () => m.customTurretID > 0f));
            settings.Add(MissileSettingItem.Slider(TabPayload, "customTurretLoftFac", "#LOC_BDArmory_TurretLoftFac",
                () => m.customTurretLoftFac, v => m.customTurretLoftFac = v, 0f, 1f, 0.05f, null,
                () => m.customTurretID > 0f));
            settings.Add(MissileSettingItem.Toggle(TabPayload, "inCargoBay", "#LOC_BDArmory_InCargoBay",
                () => m.inCargoBay, v => m.inCargoBay = v));
            settings.Add(MissileSettingItem.Slider(TabPayload, "priority", "#LOC_BDArmory_FiringPriority",
                () => m.priority, v => m.priority = v, 0f, 10f, 1f, v => v.ToString("F0")));
        }

        void OnGUI()
        {
            if (!open || targetModule == null) return;
            if (targetModule.part == null) { Close(); return; }
            BDArmorySetup.SetGUIOpacity();
            if (BDArmorySettings.UI_SCALE_ACTUAL != 1f)
                GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, windowRect.position);
            windowRect = GUI.Window(987654, windowRect, DrawWindow,
                StringUtils.Localize("#LOC_BDArmory_ModularMissileSettings"), BDArmorySetup.BDGuiSkin.window);
            BDArmorySetup.SetGUIOpacity(false);
        }

        void DrawWindow(int id)
        {
            if (GUI.Button(new Rect(windowRect.width - 38, 4, 30, 18), "X", BDArmorySetup.BDGuiSkin.button))
            { Close(); return; }
            GUI.DragWindow(new Rect(0, 0, windowRect.width - 44, 24));

            float tabW = (windowRect.width - 16) / TabKeys.Length;
            for (int i = 0; i < TabKeys.Length; i++)
            {
                GUIStyle st = i == currentTab ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle;
                if (GUI.Button(new Rect(8 + i * tabW, 26, tabW - 3, 22), StringUtils.Localize(TabKeys[i]), st))
                { currentTab = i; scrollPos = Vector2.zero; }
            }

            visible.Clear();
            foreach (var s in settings)
            {
                if (s.tab != currentTab) continue;
                bool show;
                try { show = s.visibleCondition == null || s.visibleCondition(); }
                catch { show = true; }
                if (show) visible.Add(s);
            }

            float contentH = 6f;
            foreach (var s in visible)
                contentH += s is SectionItem ? 28f : RowHeight(s) ;

            float w = windowRect.width - 28;
            float viewH = windowRect.height - 92;
            scrollPos = GUI.BeginScrollView(new Rect(8, 52, windowRect.width - 16, viewH), scrollPos,
                new Rect(0, 0, w, Math.Max(viewH, contentH)));

            float y = 4f;
            foreach (var s in visible)
            {
                if (s is SectionItem)
                {
                    GUI.Box(new Rect(2, y, w - 4, 24), "");
                    GUI.Label(new Rect(10, y + 3, w - 20, 20),
                        StringUtils.Localize(s.labelKey), BDArmorySetup.BDGuiSkin.label);
                    y += 28f;
                    continue;
                }
                DrawRow(s, w, ref y);
            }
            GUI.EndScrollView();
        }

        static float RowHeight(MissileSettingItem s)
        {
            switch (s.type)
            {
                case MissileSettingType.FloatRange: return 30f;
                case MissileSettingType.Bool: return 26f;
                case MissileSettingType.Action: return 30f;
                case MissileSettingType.Choice: return 30f;
                case MissileSettingType.Text: return 30f;
                default: return 30f;
            }
        }

        void DrawRow(MissileSettingItem item, float w, ref float y)
        {
            GUI.Label(new Rect(10, y + 2, 250, 20),
                StringUtils.Localize(item.labelKey), BDArmorySetup.BDGuiSkin.label);
            try
            {
                if (item.type == MissileSettingType.FloatRange && item.getFloat != null && item.setFloat != null)
                {
                    float val = item.getFloat();
                    float step = item.step > 0f ? item.step : 0.1f;
                    float newVal = GUI.HorizontalSlider(new Rect(268, y + 6, w - 268 - 72, 16), val, item.min, item.max);
                    newVal = Mathf.Clamp(Mathf.Round(newVal / step) * step, item.min, item.max);
                    if (Math.Abs(newVal - val) > 0.0001f) item.setFloat(newVal);
                    string txt = item.format != null ? item.format(item.getFloat()) : item.getFloat().ToString("F2");
                    GUI.Label(new Rect(w - 64, y + 2, 60, 20), txt, BDArmorySetup.BDGuiSkin.label);
                    y += 30f;
                }
                else if (item.type == MissileSettingType.Bool && item.getBool != null && item.setBool != null)
                {
                    bool cur = item.getBool();
                    bool nv = GUI.Toggle(new Rect(268, y + 3, 50, 18), cur, "");
                    if (nv != cur) item.setBool(nv);
                    GUI.Label(new Rect(322, y + 2, 140, 20),
                        StringUtils.Localize(cur ? "#LOC_BDArmory_Enabled" : "#LOC_BDArmory_Disabled"),
                        BDArmorySetup.BDGuiSkin.label);
                    y += 26f;
                }
                else if (item.type == MissileSettingType.Action && item.onAction != null)
                {
                    if (GUI.Button(new Rect(268, y + 2, 150, 22),
                        StringUtils.Localize(item.labelKey), BDArmorySetup.BDGuiSkin.button))
                        item.onAction();
                    if (item.getActionStatus != null)
                        GUI.Label(new Rect(424, y + 2, w - 428, 20),
                            item.getActionStatus(), BDArmorySetup.BDGuiSkin.label);
                    y += 30f;
                }
                else if (item.type == MissileSettingType.Choice && item.getChoice != null && item.setChoice != null
                    && item.choiceOptions != null && item.choiceOptions.Length > 0)
                {
                    int n = item.choiceOptions.Length;
                    int cur = Mathf.Clamp(item.getChoice(), 0, n - 1);
                    string[] disp = item.choiceDisplay != null && item.choiceDisplay.Length == n
                        ? item.choiceDisplay : item.choiceOptions;
                    if (GUI.Button(new Rect(268, y + 2, 26, 22), "<", BDArmorySetup.BDGuiSkin.button))
                        item.setChoice((cur + n - 1) % n);
                    GUI.Label(new Rect(298, y + 2, w - 298 - 34, 20), disp[cur], BDArmorySetup.BDGuiSkin.label);
                    if (GUI.Button(new Rect(w - 30, y + 2, 26, 22), ">", BDArmorySetup.BDGuiSkin.button))
                        item.setChoice((cur + 1) % n);
                    y += 30f;
                }
                else if (item.type == MissileSettingType.Text && item.getText != null && item.setText != null)
                {
                    string cur;
                    try { cur = item.getText() ?? ""; }
                    catch { cur = ""; }
                    string nv = GUI.TextField(new Rect(268, y + 2, w - 268 - 4, 22), cur, BDArmorySetup.BDGuiSkin.textField);
                    if (nv != cur) item.setText(nv);
                    y += 30f;
                }
                else y += 30f;
            }
            catch { y += 30f; }
        }
    }
}
