using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using UnityEngine;
using KSP.Localization;

using BDArmory.Competition;
using BDArmory.CounterMeasure;
using BDArmory.Extensions;
using BDArmory.GameModes;
using BDArmory.Guidances;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.WeaponMounts;
using BDArmory.Weapons.Missiles;
using BDArmory.Weapons;

namespace BDArmory.Control
{
    public class MissileFire : PartModule
    {
        #region Declarations

        //weapons
        private List<IBDWeapon> weaponTypes = new List<IBDWeapon>();
        public IBDWeapon[] weaponArray;

        // extension for feature_engagementenvelope: specific lists by weapon engagement type
        private List<IBDWeapon> weaponTypesAir = new List<IBDWeapon>();
        private List<IBDWeapon> weaponTypesMissile = new List<IBDWeapon>();
        private List<IBDWeapon> weaponTypesGround = new List<IBDWeapon>();
        private List<IBDWeapon> weaponTypesSLW = new List<IBDWeapon>();

        [KSPField(guiActiveEditor = false, isPersistant = true, guiActive = false)] public int weaponIndex;

        //ScreenMessage armedMessage;
        ScreenMessage selectionMessage;
        string selectionText = "";

        Transform cameraTransform;

        float startTime;
        public int firedMissiles;
        public Dictionary<TargetInfo, int> missilesAway;

        public float totalHP;
        public float currentHP;

        public bool hasLoadedRippleData;
        float rippleTimer;

        public TargetSignatureData heatTarget;

        //[KSPField(isPersistant = true)]
        public float rippleRPM
        {
            get
            {
                if (rippleFire)
                {
                    return rippleDictionary[selectedWeapon.GetShortName()].rpm;
                }
                else
                {
                    return 0;
                }
            }
            set
            {
                if (selectedWeapon != null && rippleDictionary.ContainsKey(selectedWeapon.GetShortName()))
                {
                    rippleDictionary[selectedWeapon.GetShortName()].rpm = value;
                }
            }
        }

        float triggerTimer;
        Dictionary<string, int> rippleGunCount = new Dictionary<string, int>();
        Dictionary<string, int> gunRippleIndex = new Dictionary<string, int>();
        public float gunRippleRpm;

        public void incrementRippleIndex(string weaponname)
        {
            if (!gunRippleIndex.ContainsKey(weaponname)) { Debug.LogError($"[BDArmory.MissileFire]: Weapon {weaponname} on {vessel.vesselName} does not exist in the gunRippleIndex!"); return; }
            gunRippleIndex[weaponname]++;
            if (gunRippleIndex[weaponname] >= GetRippleGunCount(weaponname))
            {
                gunRippleIndex[weaponname] = 0;
            }
        }

        public int GetRippleIndex(string weaponname)
        {
            if (gunRippleIndex.TryGetValue(weaponname, out int rippleIndex))
            {
                return rippleIndex;
            }
            else return 0;
        }

        public int GetRippleGunCount(string weaponname)
        {
            if (rippleGunCount.TryGetValue(weaponname, out int rippleCount))
            {
                return rippleCount;
            }
            else return 0;
        }

        //ripple stuff
        string rippleData = string.Empty;
        Dictionary<string, RippleOption> rippleDictionary; //weapon name, ripple option
        public bool canRipple;

        //public float triggerHoldTime = 0.3f;

        //[KSPField(isPersistant = true)]

        public bool rippleFire
        {
            get
            {
                if (selectedWeapon == null) return false;
                if (rippleDictionary.ContainsKey(selectedWeapon.GetShortName()))
                {
                    return rippleDictionary[selectedWeapon.GetShortName()].rippleFire;
                }
                //rippleDictionary.Add(selectedWeapon.GetShortName(), new RippleOption(false, 650));
                return false;
            }
        }

        public void ToggleRippleFire()
        {
            if (selectedWeapon != null)
            {
                RippleOption ro;
                if (rippleDictionary.ContainsKey(selectedWeapon.GetShortName()))
                {
                    ro = rippleDictionary[selectedWeapon.GetShortName()];
                }
                else
                {
                    ro = new RippleOption(false, 650); //default to true ripple fire for guns, otherwise, false
                    if (selectedWeapon.GetWeaponClass() == WeaponClasses.Gun || selectedWeapon.GetWeaponClass() == WeaponClasses.Rocket || selectedWeapon.GetWeaponClass() == WeaponClasses.DefenseLaser)
                    {
                        ro.rippleFire = currentGun.useRippleFire;
                    }
                    rippleDictionary.Add(selectedWeapon.GetShortName(), ro);
                }

                ro.rippleFire = !ro.rippleFire;

                if (selectedWeapon.GetWeaponClass() == WeaponClasses.Gun || selectedWeapon.GetWeaponClass() == WeaponClasses.Rocket || selectedWeapon.GetWeaponClass() == WeaponClasses.DefenseLaser)
                {
                    using (var w = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                        while (w.MoveNext())
                        {
                            if (w.Current == null) continue;
                            if (w.Current.GetShortName() == selectedWeapon.GetShortName())
                                w.Current.useRippleFire = ro.rippleFire;
                        }
                }
            }
        }

        public void AGToggleRipple(KSPActionParam param)
        {
            ToggleRippleFire();
        }

        void ParseRippleOptions()
        {
            rippleDictionary = new Dictionary<string, RippleOption>();
            //Debug.Log("[BDArmory.MissileFire]: Parsing ripple options");
            if (!string.IsNullOrEmpty(rippleData))
            {
                //Debug.Log("[BDArmory.MissileFire]: Ripple data: " + rippleData);
                try
                {
                    using (IEnumerator<string> weapon = rippleData.Split(new char[] { ';' }).AsEnumerable().GetEnumerator())
                        while (weapon.MoveNext())
                        {
                            if (weapon.Current == string.Empty) continue;

                            string[] options = weapon.Current.Split(new char[] { ',' });
                            string wpnName = options[0];
                            bool rf = bool.Parse(options[1]);
                            float rpm = float.Parse(options[2]);
                            RippleOption ro = new RippleOption(rf, rpm);
                            rippleDictionary.Add(wpnName, ro);
                        }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[BDArmory.MissileFire]: Ripple data was invalid: " + e.Message);
                    rippleData = string.Empty;
                }
            }
            else
            {
                //Debug.Log("[BDArmory.MissileFire]: Ripple data is empty.");
            }
            hasLoadedRippleData = true;
        }

        void SaveRippleOptions(ConfigNode node)
        {
            if (rippleDictionary != null)
            {
                rippleData = string.Empty;
                using (Dictionary<string, RippleOption>.KeyCollection.Enumerator wpnName = rippleDictionary.Keys.GetEnumerator())
                    while (wpnName.MoveNext())
                    {
                        if (wpnName.Current == null) continue;
                        rippleData += $"{wpnName},{rippleDictionary[wpnName.Current].rippleFire},{rippleDictionary[wpnName.Current].rpm};";
                    }
                node.SetValue("RippleData", rippleData, true);
            }
            //Debug.Log("[BDArmory.MissileFire]: Saved ripple data");
        }

        public bool hasSingleFired;

        public bool engageAir = true;
        public bool engageMissile = true;
        public bool engageSrf = true;
        public bool engageSLW = true;

        public void ToggleEngageAir()
        {
            engageAir = !engageAir;
            using (var weapon = VesselModuleRegistry.GetModules<IBDWeapon>(vessel).GetEnumerator())
                while (weapon.MoveNext())
                {
                    if (weapon.Current == null) continue;
                    EngageableWeapon engageableWeapon = weapon.Current as EngageableWeapon;
                    if (engageableWeapon != null)
                    {
                        engageableWeapon.engageAir = engageAir;
                    }
                }
            UpdateList();
        }
        public void ToggleEngageMissile()
        {
            engageMissile = !engageMissile;
            using (var weapon = VesselModuleRegistry.GetModules<IBDWeapon>(vessel).GetEnumerator())
                while (weapon.MoveNext())
                {
                    if (weapon.Current == null) continue;
                    EngageableWeapon engageableWeapon = weapon.Current as EngageableWeapon;
                    if (engageableWeapon != null)
                    {
                        engageableWeapon.engageMissile = engageMissile;
                    }
                }
            UpdateList();
        }
        public void ToggleEngageSrf()
        {
            engageSrf = !engageSrf;
            using (var weapon = VesselModuleRegistry.GetModules<IBDWeapon>(vessel).GetEnumerator())
                while (weapon.MoveNext())
                {
                    if (weapon.Current == null) continue;
                    EngageableWeapon engageableWeapon = weapon.Current as EngageableWeapon;
                    if (engageableWeapon != null)
                    {
                        engageableWeapon.engageGround = engageSrf;
                    }
                }
            UpdateList();
        }
        public void ToggleEngageSLW()
        {
            engageSLW = !engageSLW;
            using (var weapon = VesselModuleRegistry.GetModules<IBDWeapon>(vessel).GetEnumerator())
                while (weapon.MoveNext())
                {
                    if (weapon.Current == null) continue;
                    EngageableWeapon engageableWeapon = weapon.Current as EngageableWeapon;
                    if (engageableWeapon != null)
                    {
                        engageableWeapon.engageSLW = engageSLW;
                    }
                }
            UpdateList();
        }

        //bomb aimer
        Part bombPart;
        Vector3 bombAimerPosition = Vector3.zero;
        Texture2D bombAimerTexture = GameDatabase.Instance.GetTexture("BDArmory/Textures/grayCircle", false);
        bool showBombAimer;

        //targeting
        private List<Vessel> loadedVessels = new List<Vessel>();
        float targetListTimer;

        //sounds
        AudioSource audioSource;
        public AudioSource warningAudioSource;
        AudioSource targetingAudioSource;
        AudioClip clickSound;
        AudioClip warningSound;
        AudioClip armOnSound;
        AudioClip armOffSound;
        AudioClip heatGrowlSound;
        bool warningSounding;

        //missile warning
        public bool missileIsIncoming;
        public float incomingMissileLastDetected = 0;
        public float incomingMissileDistance = float.MaxValue;
        public float incomingMissileTime = float.MaxValue;
        public Vessel incomingMissileVessel;

        //guard mode vars
        float targetScanTimer;
        Vessel guardTarget;
        public TargetInfo currentTarget;
        public int engagedTargets = 0;
        public List<TargetInfo> targetsAssigned; //secondary targets list
        public List<TargetInfo> missilesAssigned; //secondary missile targets list
        TargetInfo overrideTarget; //used for setting target next guard scan for stuff like assisting teammates
        float overrideTimer;

        public bool TargetOverride
        {
            get { return overrideTimer > 0; }
        }

        //AIPilot
        public IBDAIControl AI;

        // some extending related code still uses pilotAI, which is implementation specific and does not make sense to include in the interface
        private BDModulePilotAI pilotAI { get { return AI as BDModulePilotAI; } }

        public float timeBombReleased;

        //targeting pods
        public ModuleTargetingCamera mainTGP = null;
        public List<ModuleTargetingCamera> targetingPods = new List<ModuleTargetingCamera>();

        //radar
        public List<ModuleRadar> radars = new List<ModuleRadar>();
        public int MaxradarLocks = 0;
        public VesselRadarData vesselRadarData;

        public List<ModuleIRST> irsts = new List<ModuleIRST>();

        //jammers
        public List<ModuleECMJammer> jammers = new List<ModuleECMJammer>();

        //cloak generators
        public List<ModuleCloakingDevice> cloaks = new List<ModuleCloakingDevice>();

        //other modules
        public List<IBDWMModule> wmModules = new List<IBDWMModule>();

        //wingcommander
        public ModuleWingCommander wingCommander;

        //RWR
        private RadarWarningReceiver radarWarn;

        public RadarWarningReceiver rwr
        {
            get
            {
                if (!radarWarn || radarWarn.vessel != vessel)
                {
                    return null;
                }
                return radarWarn;
            }
            set { radarWarn = value; }
        }

        //GPS
        public GPSTargetInfo designatedGPSInfo;

        public Vector3d designatedGPSCoords => designatedGPSInfo.gpsCoordinates;

        //weapon slaving
        public bool slavingTurrets = false;
        public Vector3 slavedPosition;
        public Vector3 slavedVelocity;
        public Vector3 slavedAcceleration;
        public TargetSignatureData slavedTarget;

        //current weapon ref
        public MissileBase CurrentMissile;
        public MissileBase PreviousMissile;

        public ModuleWeapon currentGun
        {
            get
            {
                if (selectedWeapon != null && (selectedWeapon.GetWeaponClass() == WeaponClasses.Gun || selectedWeapon.GetWeaponClass() == WeaponClasses.Rocket || selectedWeapon.GetWeaponClass() == WeaponClasses.DefenseLaser))
                {
                    return selectedWeapon.GetPart().FindModuleImplementing<ModuleWeapon>();
                }
                else
                {
                    return null;
                }
            }
        }

        public ModuleWeapon previousGun
        {
            get
            {
                if (previousSelectedWeapon != null && (previousSelectedWeapon.GetWeaponClass() == WeaponClasses.Gun || previousSelectedWeapon.GetWeaponClass() == WeaponClasses.Rocket || previousSelectedWeapon.GetWeaponClass() == WeaponClasses.DefenseLaser))
                {
                    return previousSelectedWeapon.GetPart().FindModuleImplementing<ModuleWeapon>();
                }
                else
                {
                    return null;
                }
            }
        }

        public bool underAttack;
        float underAttackLastNotified = 0f;
        public bool underFire;
        float underFireLastNotified = 0f;

        public Vector3 incomingThreatPosition;
        public Vessel incomingThreatVessel;
        public float incomingMissDistance;
        public float incomingMissTime;
        public Vessel priorGunThreatVessel = null;
        private ViewScanResults results;

        public bool debilitated = false;

        public bool guardFiringMissile;
        public bool hasAntiRadiationOrdinance;
        public float[] antiradTargets;
        public bool antiRadTargetAcquired;
        Vector3 antiRadiationTarget;
        public bool laserPointDetected;

        ModuleTargetingCamera foundCam;

        #region KSPFields,events,actions

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_FiringInterval"),//Firing Interval
            UI_FloatRange(minValue = 0.5f, maxValue = 60f, stepIncrement = 0.5f, scene = UI_Scene.All)]
        public float targetScanInterval = 1;

        // extension for feature_engagementenvelope: burst length for guns
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_FiringBurstLength"),//Firing Burst Length
            UI_FloatRange(minValue = 0f, maxValue = 10f, stepIncrement = 0.05f, scene = UI_Scene.All)]
        public float fireBurstLength = 1;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_FiringTolerance"),//Firing Tolerance
            UI_FloatRange(minValue = 0f, maxValue = 4f, stepIncrement = 0.05f, scene = UI_Scene.All)]
        public float AutoFireCosAngleAdjustment = 1.4f; //tune Autofire angle in WM GUI

        public float adjustedAutoFireCosAngle = 0.99970f; //increased to 3 deg from 1, max increased to v1.3.8 default of 4

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_FieldOfView"),//Field of View
            UI_FloatRange(minValue = 10f, maxValue = 360f, stepIncrement = 10f, scene = UI_Scene.All)]
        public float
            guardAngle = 360;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_VisualRange"),//Visual Range
            UI_FloatRange(minValue = 100f, maxValue = 200000f, stepIncrement = 100f, scene = UI_Scene.All)]
        public float
            guardRange = 200000f;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_GunsRange"),//Guns Range
            UI_FloatRange(minValue = 0f, maxValue = 10000f, stepIncrement = 10f, scene = UI_Scene.All)]
        public float
            gunRange = 2500f;
        public float maxGunRange = 0f;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_WMWindow_MultiTargetNum"),//Max Turret Targets
            UI_FloatRange(minValue = 1, maxValue = 10, stepIncrement = 1, scene = UI_Scene.All)]
        public float multiTargetNum = 1;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_WMWindow_MultiMissileNum"),//Max Missile Targets
            UI_FloatRange(minValue = 1, maxValue = 10, stepIncrement = 1, scene = UI_Scene.All)]
        public float multiMissileTgtNum = 1;

        public const float maxAllowableMissilesOnTarget = 18f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_MissilesORTarget"),//Missiles/Target
            UI_FloatRange(minValue = 1f, maxValue = maxAllowableMissilesOnTarget, stepIncrement = 1f, scene = UI_Scene.All)]
        public float maxMissilesOnTarget = 1;

        #region TargetSettings
        [KSPField(isPersistant = true)]
        public bool targetCoM = true;

        [KSPField(isPersistant = true)]
        public bool targetCommand = false;

        [KSPField(isPersistant = true)]
        public bool targetEngine = false;

        [KSPField(isPersistant = true)]
        public bool targetWeapon = false;

        [KSPField(isPersistant = true)]
        public bool targetMass = false;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_targetSetting")]//Target Setting
        public string targetingString = Localizer.Format("#LOC_BDArmory_TargetCOM");
        [KSPEvent(guiActive = true, guiActiveEditor = true, active = true, guiName = "#LOC_BDArmory_Selecttargeting")]//Select Targeting Option
        public void SelectTargeting()
        {
            BDTargetSelector.Instance.Open(this, new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
        }
        #endregion

        #region Target Priority
        // Target priority variables
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Target Priority Toggle
            UI_Toggle(enabledText = "#LOC_BDArmory_Enabled", disabledText = "#LOC_BDArmory_Disabled", scene = UI_Scene.All),]
        public bool targetPriorityEnabled = true;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_CurrentTarget", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true), UI_Label(scene = UI_Scene.All)]
        public string TargetLabel = "";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_TargetScore", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true), UI_Label(scene = UI_Scene.All)]
        public string TargetScoreLabel = "";

        private string targetBiasLabel = Localizer.Format("#LOC_BDArmory_TargetPriority_CurrentTargetBias");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_CurrentTargetBias", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Current target bias
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetBias = 1.1f;

        private string targetPreferenceLabel = Localizer.Format("#LOC_BDArmory_TargetPriority_AirVsGround");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_AirVsGround", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Target Preference
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightAirPreference = 0f;

        private string targetRangeLabel = Localizer.Format("#LOC_BDArmory_TargetPriority_TargetProximity");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_TargetProximity", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Target Range
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightRange = 1f;

        private string targetATALabel = Localizer.Format("#LOC_BDArmory_TargetPriority_CloserAngleToTarget");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_CloserAngleToTarget", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Antenna Train Angle
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightATA = 1f;

        private string targetAoDLabel = Localizer.Format("#LOC_BDArmory_TargetPriority_AngleOverDistance");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_AngleOverDistance", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Angle/Distance
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightAoD = 0f;

        private string targetAccelLabel = Localizer.Format("#LOC_BDArmory_TargetPriority_TargetAcceleration");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_TargetAcceleration", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Target Acceleration
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightAccel = 0;

        private string targetClosureTimeLabel = Localizer.Format("#LOC_BDArmory_TargetPriority_ShorterClosingTime");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_ShorterClosingTime", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Target Closure Time
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightClosureTime = 0f;

        private string targetWeaponNumberLabel = Localizer.Format("#LOC_BDArmory_TargetPriority_TargetWeaponNumber");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_TargetWeaponNumber", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Target Weapon Number
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightWeaponNumber = 0;

        private string targetMassLabel = Localizer.Format("#LOC_BDArmory_TargetPriority_TargetMass");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_TargetMass", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Target Mass
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightMass = 0;

        private string targetFriendliesEngagingLabel = Localizer.Format("#LOC_BDArmory_TargetPriority_FewerTeammatesEngaging");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_FewerTeammatesEngaging", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Number Friendlies Engaging
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightFriendliesEngaging = 1f;

        private string targetThreatLabel = Localizer.Format("#LOC_BDArmory_TargetPriority_TargetThreat");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_TargetThreat", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Target threat
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightThreat = 1f;

        private string targetProtectTeammateLabel = Localizer.Format("#LOC_BDArmory_TargetPriority_TargetProtectTeammate");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_TargetProtectTeammate", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Protect Teammates
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightProtectTeammate = 0f;

        private string targetProtectVIPLabel = Localizer.Format("#LOC_BDArmory_TargetPriority_TargetProtectVIP");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_TargetProtectVIP", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Protect VIPs
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightProtectVIP = 0f;

        private string targetAttackVIPLabel = Localizer.Format("#LOC_BDArmory_TargetPriority_TargetAttackVIP");
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_TargetPriority_TargetAttackVIP", advancedTweakable = true, groupName = "targetPriority", groupDisplayName = "#LOC_BDArmory_TargetPriority_Settings", groupStartCollapsed = true),//Attack Enemy VIPs
            UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float targetWeightAttackVIP = 0f;
        #endregion

        #region Countermeasure Settings
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_CMThreshold", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true),// Countermeasure dispensing time threshold
            UI_FloatRange(minValue = 1f, maxValue = 60f, stepIncrement = 0.5f, scene = UI_Scene.All)]
        public float cmThreshold = 5f; // Works well

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_CMRepetition", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true),// Flare dispensing repetition
            UI_FloatRange(minValue = 1f, maxValue = 20f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float cmRepetition = 3f; // Prior default was 4

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_CMInterval", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true),// Flare dispensing interval
            UI_FloatRange(minValue = 0.1f, maxValue = 1f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float cmInterval = 0.2f; // Prior default was 0.6

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_CMWaitTime", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true),// Flare dispensing wait time
            UI_FloatRange(minValue = 0.1f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float cmWaitTime = 0.7f; // Works well

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ChaffRepetition", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true),// Chaff dispensing repetition
            UI_FloatRange(minValue = 1f, maxValue = 20f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float chaffRepetition = 2f; // Prior default was 4

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ChaffInterval", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true),// Chaff dispensing interval
            UI_FloatRange(minValue = 0.1f, maxValue = 1f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float chaffInterval = 0.5f; // Prior default was 0.6

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_ChaffWaitTime", advancedTweakable = true, groupName = "cmSettings", groupDisplayName = "#LOC_BDArmory_Countermeasure_Settings", groupStartCollapsed = true),// Chaff dispensing wait time
            UI_FloatRange(minValue = 0.1f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.All)]
        public float chaffWaitTime = 0.6f; // Works well
        #endregion

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_IsVIP", advancedTweakable = true),// Is VIP, throwback to TF Classic (Hunted Game Mode)
            UI_Toggle(enabledText = "#LOC_BDArmory_IsVIP_enabledText", disabledText = "#LOC_BDArmory_IsVIP_disabledText", scene = UI_Scene.All),]//yes--no
        public bool isVIP = false;


        public void ToggleGuardMode()
        {
            guardMode = !guardMode;

            if (!guardMode)
            {
                //disable turret firing and guard mode
                using (var weapon = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                    while (weapon.MoveNext())
                    {
                        if (weapon.Current == null) continue;
                        weapon.Current.visualTargetVessel = null;
                        weapon.Current.visualTargetPart = null;
                        weapon.Current.autoFire = false;
                        weapon.Current.aiControlled = false;
                        if (weapon.Current.isAPS)
                        {
                            weapon.Current.DisableWeapon();
                            weapon.Current.aiControlled = false;
                        }
                    }
                weaponIndex = 0;
                selectedWeapon = null;
            }
            else
            {
                using (var weapon = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                    while (weapon.MoveNext())
                    {
                        if (weapon.Current == null) continue;
                        if (weapon.Current.isAPS)
                        {
                            weapon.Current.EnableWeapon();
                            weapon.Current.aiControlled = true;
                        }
                    }
                if (radars.Count > 0)
                {
                    using (List<ModuleRadar>.Enumerator rd = radars.GetEnumerator())
                        while (rd.MoveNext())
                        {
                            if (rd.Current != null || rd.Current.canLock)
                            {
                                rd.Current.EnableRadar();
                            }
                        }
                }
                if (irsts.Count > 0)
                {
                    using (List<ModuleIRST>.Enumerator rd = irsts.GetEnumerator())
                        while (rd.MoveNext())
                        {
                            if (rd.Current != null)
                            {
                                rd.Current.EnableIRST();
                            }
                        }
                }
                if (hasAntiRadiationOrdinance)
                {
                    if (rwr && !rwr.rwrEnabled) rwr.EnableRWR();
                    if (rwr && rwr.rwrEnabled && !rwr.displayRWR) rwr.displayRWR = true;
                }
            }
        }

        [KSPAction("Toggle Guard Mode")]
        public void AGToggleGuardMode(KSPActionParam param)
        {
            ToggleGuardMode();
        }

        //[KSPField(isPersistant = true)] public bool guardMode;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_GuardMode"),//Guard Mode: 
            UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool guardMode;

        public bool targetMissiles = false;

        [KSPAction("Jettison Weapon")]
        public void AGJettisonWeapon(KSPActionParam param)
        {
            if (CurrentMissile)
            {
                using (var missile = VesselModuleRegistry.GetModules<MissileBase>(vessel).GetEnumerator())
                    while (missile.MoveNext())
                    {
                        if (missile.Current == null) continue;
                        if (missile.Current.GetShortName() == CurrentMissile.GetShortName())
                        {
                            missile.Current.Jettison();
                        }
                    }
            }
            else if (selectedWeapon != null && selectedWeapon.GetWeaponClass() == WeaponClasses.Rocket)
            {
                using (var rocket = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                    while (rocket.MoveNext())
                    {
                        if (rocket.Current == null) continue;
                        rocket.Current.Jettison();
                    }
            }
        }

        [KSPAction("Deploy Kerbal's Parachute")] // If there's an EVAing kerbal.
        public void AGDeployKerbalsParachute(KSPActionParam param)
        {
            foreach (var chute in VesselModuleRegistry.GetModules<ModuleEvaChute>(vessel))
            {
                if (chute == null) continue;
                chute.deployAltitude = (float)vessel.radarAltitude + 100f; // Current height + 100 so that it deploys immediately.
                chute.deploymentState = ModuleParachute.deploymentStates.STOWED;
                chute.Deploy();
            }
        }

        [KSPAction("Self-destruct")] // Self-destruct
        public void AGSelfDestruct(KSPActionParam param)
        {
            foreach (var part in vessel.parts)
            {
                if (part.protoModuleCrew.Count > 0)
                {
                    PartExploderSystem.AddPartToExplode(part);
                }
            }
        }

        public BDTeam Team
        {
            get
            {
                return BDTeam.Get(teamString);
            }
            set
            {
                if (!team_loaded) return;
                if (!BDArmorySetup.Instance.Teams.ContainsKey(value.Name))
                    BDArmorySetup.Instance.Teams.Add(value.Name, value);
                teamString = value.Name;
                team = value.Serialize();
            }
        }

        // Team name
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_Team")]//Team
        public string teamString = "Neutral";

        // Serialized team
        [KSPField(isPersistant = true)]
        public string team;
        private bool team_loaded = false;

        [KSPAction("Next Team")]
        public void AGNextTeam(KSPActionParam param)
        {
            NextTeam();
        }

        public delegate void ChangeTeamDelegate(MissileFire wm, BDTeam team);

        public static event ChangeTeamDelegate OnChangeTeam;

        public void SetTeam(BDTeam team)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                SetTarget(null); // Without this, friendliesEngaging never gets updated
                using (var wpnMgr = VesselModuleRegistry.GetModules<MissileFire>(vessel).GetEnumerator())
                    while (wpnMgr.MoveNext())
                    {
                        if (wpnMgr.Current == null) continue;
                        wpnMgr.Current.Team = team;
                    }

                if (vessel.gameObject.GetComponent<TargetInfo>())
                {
                    BDATargetManager.RemoveTarget(vessel.gameObject.GetComponent<TargetInfo>());
                    Destroy(vessel.gameObject.GetComponent<TargetInfo>());
                }
                OnChangeTeam?.Invoke(this, Team);
                ResetGuardInterval();
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                using (var editorPart = EditorLogic.fetch.ship.Parts.GetEnumerator())
                    while (editorPart.MoveNext())
                        using (var wpnMgr = editorPart.Current.FindModulesImplementing<MissileFire>().GetEnumerator())
                            while (wpnMgr.MoveNext())
                            {
                                if (wpnMgr.Current == null) continue;
                                wpnMgr.Current.Team = team;
                            }
            }
        }

        public void SetTeamByName(string teamName)
        {

        }

        [KSPEvent(active = true, guiActiveEditor = true, guiActive = false)]
        public void NextTeam(bool switchneutral = false)
        {
            if (!switchneutral) //standard switch behavior; don't switch to a neutral team
            {
                var teamList = new List<string> { "A", "B" };
                using (var teams = BDArmorySetup.Instance.Teams.GetEnumerator())
                    while (teams.MoveNext())
                        if (!teamList.Contains(teams.Current.Key) && !teams.Current.Value.Neutral)
                            teamList.Add(teams.Current.Key);
                teamList.Sort();
                SetTeam(BDTeam.Get(teamList[(teamList.IndexOf(Team.Name) + 1) % teamList.Count]));
            }
            else// alt-click; switch to first available neutral team
            {
                var neutralList = new List<string> { "Neutral" };
                using (var teams = BDArmorySetup.Instance.Teams.GetEnumerator())
                    while (teams.MoveNext())
                        if (!neutralList.Contains(teams.Current.Key) && teams.Current.Value.Neutral)
                            neutralList.Add(teams.Current.Key);
                neutralList.Sort();
                SetTeam(BDTeam.Get(neutralList[(neutralList.IndexOf(Team.Name) + 1) % neutralList.Count]));
            }
        }


        [KSPEvent(guiActive = false, guiActiveEditor = true, active = true, guiName = "#LOC_BDArmory_SelectTeam")]//Select Team
        public void SelectTeam()
        {
            BDTeamSelector.Instance.Open(this, new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
        }

        [KSPField(isPersistant = true)]
        public bool isArmed = false;

        [KSPAction("Arm/Disarm")]
        public void AGToggleArm(KSPActionParam param)
        {
            ToggleArm();
        }

        public void ToggleArm()
        {
            isArmed = !isArmed;
            if (isArmed) audioSource.PlayOneShot(armOnSound);
            else audioSource.PlayOneShot(armOffSound);
        }

        [KSPField(isPersistant = false, guiActive = true, guiName = "#LOC_BDArmory_Weapon")]//Weapon
        public string selectedWeaponString =
            "None";

        IBDWeapon sw;

        public IBDWeapon selectedWeapon
        {
            get
            {
                if ((sw != null && sw.GetPart().vessel == vessel) || weaponIndex <= 0) return sw;
                using (var weapon = VesselModuleRegistry.GetModules<IBDWeapon>(vessel).GetEnumerator())
                    while (weapon.MoveNext())
                    {
                        if (weapon.Current == null) continue;
                        if (weapon.Current.GetShortName() != selectedWeaponString) continue;
                        if (weapon.Current.GetWeaponClass() == WeaponClasses.Gun || weapon.Current.GetWeaponClass() == WeaponClasses.Rocket || weapon.Current.GetWeaponClass() == WeaponClasses.DefenseLaser)
                        {
                            var gun = weapon.Current.GetPart().FindModuleImplementing<ModuleWeapon>();
                            if (gun.isReloading || gun.isOverheated || gun.pointingAtSelf || !(gun.ammoCount > 0 || BDArmorySettings.INFINITE_AMMO)) continue; //instead of returning the first weapon in a weapon group, return the first weapon in a group that actually can fire
                        }
                        sw = weapon.Current;
                        break;
                    }
                return sw;
            }
            set
            {
                if (sw == value) return;
                previousSelectedWeapon = sw;
                sw = value;
                selectedWeaponString = GetWeaponName(value);
                UpdateSelectedWeaponState();
            }
        }

        IBDWeapon previousSelectedWeapon { get; set; }

        [KSPAction("Fire Missile")]
        public void AGFire(KSPActionParam param)
        {
            FireMissile();
        }

        [KSPAction("Fire Guns (Hold)")]
        public void AGFireGunsHold(KSPActionParam param)
        {
            if (weaponIndex <= 0 || (selectedWeapon.GetWeaponClass() != WeaponClasses.Gun &&
                                     selectedWeapon.GetWeaponClass() != WeaponClasses.Rocket &&
                                     selectedWeapon.GetWeaponClass() != WeaponClasses.DefenseLaser)) return;
            using (var weap = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                while (weap.MoveNext())
                {
                    if (weap.Current == null) continue;
                    if (weap.Current.weaponState != ModuleWeapon.WeaponStates.Enabled ||
                        weap.Current.GetShortName() != selectedWeapon.GetShortName()) continue;
                    weap.Current.AGFireHold(param);
                }
        }

        [KSPAction("Fire Guns (Toggle)")]
        public void AGFireGunsToggle(KSPActionParam param)
        {
            if (weaponIndex <= 0 || (selectedWeapon.GetWeaponClass() != WeaponClasses.Gun &&
                                     selectedWeapon.GetWeaponClass() != WeaponClasses.Rocket &&
                                     selectedWeapon.GetWeaponClass() != WeaponClasses.DefenseLaser)) return;
            using (var weap = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                while (weap.MoveNext())
                {
                    if (weap.Current == null) continue;
                    if (weap.Current.weaponState != ModuleWeapon.WeaponStates.Enabled ||
                        weap.Current.GetShortName() != selectedWeapon.GetShortName()) continue;
                    weap.Current.AGFireToggle(param);
                }
        }

        [KSPAction("Next Weapon")]
        public void AGCycle(KSPActionParam param)
        {
            CycleWeapon(true);
        }

        [KSPAction("Previous Weapon")]
        public void AGCycleBack(KSPActionParam param)
        {
            CycleWeapon(false);
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "#LOC_BDArmory_OpenGUI", active = true)]//Open GUI
        public void ToggleToolbarGUI()
        {
            BDArmorySetup.windowBDAToolBarEnabled = !BDArmorySetup.windowBDAToolBarEnabled;
        }

        public void SetAFCAA()
        {
            UI_FloatRange field = (UI_FloatRange)Fields["AutoFireCosAngleAdjustment"].uiControlEditor;
            field.onFieldChanged = OnAFCAAUpdated;
            // field = (UI_FloatRange)Fields["AutoFireCosAngleAdjustment"].uiControlFlight; // Not visible in flight mode, use the guard menu instead.
            // field.onFieldChanged = OnAFCAAUpdated;
            OnAFCAAUpdated(null, null);
        }

        public void OnAFCAAUpdated(BaseField field, object obj)
        {
            adjustedAutoFireCosAngle = Mathf.Cos((AutoFireCosAngleAdjustment * Mathf.Deg2Rad));
            //if (BDArmorySettings.DEBUG_LABELS) Debug.Log("[BDArmory.MissileFire]: Setting AFCAA to " + adjustedAutoFireCosAngle);
        }
        #endregion KSPFields,events,actions

        private LineRenderer lr = null;
        private StringBuilder debugString = new StringBuilder();
        #endregion Declarations

        #region KSP Events

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);

            if (HighLogic.LoadedSceneIsFlight)
            {
                SaveRippleOptions(node);
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            if (HighLogic.LoadedSceneIsFlight)
            {
                rippleData = string.Empty;
                if (node.HasValue("RippleData"))
                {
                    rippleData = node.GetValue("RippleData");
                }
                ParseRippleOptions();
            }
        }

        public override void OnAwake()
        {
            clickSound = GameDatabase.Instance.GetAudioClip("BDArmory/Sounds/click");
            warningSound = GameDatabase.Instance.GetAudioClip("BDArmory/Sounds/warning");
            armOnSound = GameDatabase.Instance.GetAudioClip("BDArmory/Sounds/armOn");
            armOffSound = GameDatabase.Instance.GetAudioClip("BDArmory/Sounds/armOff");
            heatGrowlSound = GameDatabase.Instance.GetAudioClip("BDArmory/Sounds/heatGrowl");

            //HEAT LOCKING
            heatTarget = TargetSignatureData.noTarget;
        }

        public void Start()
        {
            team_loaded = true;
            Team = BDTeam.Deserialize(team);

            UpdateMaxGuardRange();
            SetAFCAA();

            startTime = Time.time;

            if (HighLogic.LoadedSceneIsFlight)
            {
                part.force_activate();

                selectionMessage = new ScreenMessage("", 2.0f, ScreenMessageStyle.LOWER_CENTER);

                UpdateList();
                if (weaponArray.Length > 0) selectedWeapon = weaponArray[weaponIndex];
                //selectedWeaponString = GetWeaponName(selectedWeapon);

                cameraTransform = part.FindModelTransform("BDARPMCameraTransform");

                part.force_activate();
                rippleTimer = Time.time;
                targetListTimer = Time.time;

                wingCommander = part.FindModuleImplementing<ModuleWingCommander>();

                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.minDistance = 1;
                audioSource.maxDistance = 500;
                audioSource.dopplerLevel = 0;
                audioSource.spatialBlend = 1;

                warningAudioSource = gameObject.AddComponent<AudioSource>();
                warningAudioSource.minDistance = 1;
                warningAudioSource.maxDistance = 500;
                warningAudioSource.dopplerLevel = 0;
                warningAudioSource.spatialBlend = 1;

                targetingAudioSource = gameObject.AddComponent<AudioSource>();
                targetingAudioSource.minDistance = 1;
                targetingAudioSource.maxDistance = 250;
                targetingAudioSource.dopplerLevel = 0;
                targetingAudioSource.loop = true;
                targetingAudioSource.spatialBlend = 1;

                StartCoroutine(MissileWarningResetRoutine());

                if (vessel.isActiveVessel)
                {
                    BDArmorySetup.Instance.ActiveWeaponManager = this;
                    BDArmorySetup.Instance.ConfigTextFields();
                }

                UpdateVolume();
                BDArmorySetup.OnVolumeChange += UpdateVolume;
                BDArmorySetup.OnSavedSettings += ClampVisualRange;

                StartCoroutine(StartupListUpdater());
                firedMissiles = 0;
                missilesAway = new Dictionary<TargetInfo, int>();
                rippleGunCount = new Dictionary<string, int>();

                GameEvents.onVesselCreate.Add(OnVesselCreate);
                GameEvents.onPartJointBreak.Add(OnPartJointBreak);
                GameEvents.onPartDie.Add(OnPartDie);
                GameEvents.onVesselPartCountChanged.Add(UpdateMaxGunRange);
                GameEvents.onVesselPartCountChanged.Add(UpdateCurrentHP);

                totalHP = GetTotalHP();
                currentHP = totalHP;
                UpdateMaxGunRange(vessel);

                AI = VesselModuleRegistry.GetIBDAIControl(vessel, true);

                RefreshModules();
                var SF = vessel.rootPart.FindModuleImplementing<ModuleSpaceFriction>();
                if (SF == null)
                {
                    SF = (ModuleSpaceFriction)vessel.rootPart.AddModule("ModuleSpaceFriction");
                }
                //either have this added on spawn to allow vessels to respond to space hack settings getting toggled, or have the Spacefriction module it's own separate part
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onEditorPartPlaced.Add(UpdateMaxGunRange);
                GameEvents.onEditorPartDeleted.Add(UpdateMaxGunRange);
                UpdateMaxGunRange(part);
            }
            targetingString = (targetCoM ? Localizer.Format("#LOC_BDArmory_TargetCOM") + "; " : "")
                + (targetMass ? Localizer.Format("#LOC_BDArmory_Mass") + "; " : "")
                + (targetCommand ? Localizer.Format("#LOC_BDArmory_Command") + "; " : "")
                + (targetEngine ? Localizer.Format("#LOC_BDArmory_Engines") + "; " : "")
                + (targetWeapon ? Localizer.Format("#LOC_BDArmory_Weapons") + "; " : "");
        }

        void OnPartDie()
        {
            OnPartDie(part);
        }

        void OnPartDie(Part p)
        {
            if (p == part)
            {
                try
                {
                    Destroy(this); // Force this module to be removed from the gameObject as something is holding onto part references and causing a memory leak.
                    GameEvents.onPartDie.Remove(OnPartDie);
                    GameEvents.onPartJointBreak.Remove(OnPartJointBreak);
                    GameEvents.onVesselCreate.Remove(OnVesselCreate);
                }
                catch (Exception e)
                {
                    //if (BDArmorySettings.DEBUG_LABELS) Debug.Log("[BDArmory.MissileFire]: Error OnPartDie: " + e.Message);
                    Debug.Log("[BDArmory.MissileFire]: Error OnPartDie: " + e.Message);
                }
            }
            RefreshModules();
            UpdateList();
            if (vessel != null)
            {
                var TI = vessel.gameObject.GetComponent<TargetInfo>();
                if (TI != null)
                {
                    TI.UpdateTargetPartList();
                }
            }
        }

        void OnVesselCreate(Vessel v)
        {
            if (v == null) return;
            RefreshModules();
        }

        void OnPartJointBreak(PartJoint j, float breakForce)
        {
            if (!part)
            {
                GameEvents.onPartJointBreak.Remove(OnPartJointBreak);
            }
            if (vessel == null)
            {
                Destroy(this);
                return;
            }

            if ((j.Parent && j.Parent.vessel == vessel) || (j.Child && j.Child.vessel == vessel))
            {
                RefreshModules();
                UpdateList();
            }
        }

        public int GetTotalHP() // get total craft HP
        {
            int HP = 0;
            using (List<Part>.Enumerator p = vessel.parts.GetEnumerator())
                while (p.MoveNext())
                {
                    if (p.Current == null) continue;
                    if (p.Current.Modules.GetModule<MissileLauncher>()) continue; // don't grab missiles
                    if (p.Current.Modules.GetModule<ModuleDecouple>()) continue; // don't grab bits that are going to fall off
                    if (p.Current.FindParentModuleImplementing<ModuleDecouple>()) continue; // should grab ModularMissiles too
                    /*
                    if (p.Current.Modules.GetModule<HitpointTracker>() != null)
                    {
                        var hp = p.Current.Modules.GetModule<HitpointTracker>();			
                        totalHP += hp.Hitpoints;
                    }
                    */
                    ++HP;
                    // ++totalHP;
                    //Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " part count: " + totalHP);
                }
            return HP;
        }

        void UpdateCurrentHP(Vessel v)
        {
            if (v == vessel)
            { currentHP = GetTotalHP(); }
        }

        public override void OnUpdate() //shouldn't most of this stuff be in onFixedUpdate instead?
        {
            if (!HighLogic.LoadedSceneIsFlight)
            {
                return;
            }

            base.OnUpdate();

            UpdateTargetingAudio();

            if (vessel.isActiveVessel)
            {
                if (!CheckMouseIsOnGui() && isArmed && BDInputUtils.GetKey(BDInputSettingsFields.WEAP_FIRE_KEY))
                {
                    triggerTimer += Time.fixedDeltaTime;
                }
                else
                {
                    triggerTimer = 0;
                    hasSingleFired = false;
                }

                //firing missiles and rockets===
                if (!guardMode &&
                    selectedWeapon != null &&
                    (selectedWeapon.GetWeaponClass() == WeaponClasses.Missile
                     || selectedWeapon.GetWeaponClass() == WeaponClasses.Bomb
                     || selectedWeapon.GetWeaponClass() == WeaponClasses.SLW
                    ))
                {
                    canRipple = true;
                    if (!MapView.MapIsEnabled && triggerTimer > BDArmorySettings.TRIGGER_HOLD_TIME && !hasSingleFired)
                    {
                        if (rippleFire)
                        {
                            if (Time.time - rippleTimer > 60f / rippleRPM)
                            {
                                FireMissile();
                                rippleTimer = Time.time;
                            }
                        }
                        else
                        {
                            FireMissile();
                            hasSingleFired = true;
                        }
                    }
                }
                else if (!guardMode &&
                         selectedWeapon != null &&
                         ((selectedWeapon.GetWeaponClass() == WeaponClasses.Gun
                         || selectedWeapon.GetWeaponClass() == WeaponClasses.Rocket
                         || selectedWeapon.GetWeaponClass() == WeaponClasses.DefenseLaser) && currentGun.canRippleFire))//&& currentGun.roundsPerMinute < 1500)) //set this based on if the WG can ripple vs if first weapon in the WG happens to be > 1500 RPM
                {
                    canRipple = true;
                }
                else
                {
                    canRipple = false;
                }
            }
        }

        private void CalculateMissilesAway() //FIXME - add check for identically named vessels
        {
            missilesAway.Clear();
            // int tempMissilesAway = 0;
            //firedMissiles = 0;
            if (!guardMode) return;
            using (List<IBDWeapon>.Enumerator Missiles = BDATargetManager.FiredMissiles.GetEnumerator())
                while (Missiles.MoveNext())
                {
                    if (Missiles.Current == null) continue;

                    var missileBase = Missiles.Current as MissileBase;

                    if (missileBase.targetVessel == null) continue;
                    if (missileBase.SourceVessel != this.vessel) continue;
                    //if (missileBase.MissileState != MissileBase.MissileStates.PostThrust && !missileBase.HasMissed && !missileBase.HasExploded)
                    if (missileBase.HasFired && !missileBase.HasMissed && !missileBase.HasExploded || missileBase.GetWeaponClass() == WeaponClasses.Bomb) //culling post-thrust missiles makes AGMs get cleared almost immediately after launch
                    {
                        if (!missilesAway.ContainsKey(missileBase.targetVessel))
                        {
                            missilesAway.Add(missileBase.targetVessel, 1);
                        }
                        else
                        {
                            missilesAway[missileBase.targetVessel]++; //tabulate all missiles fired by the vessel at various targets; only need # missiles fired at current target forlaunching, but need all vessels with missiles targeting them for vessel targeting
                        }
                    }
                }
            if (currentTarget != null && missilesAway.ContainsKey(currentTarget)) //change to previous target?
            {
                missilesAway.TryGetValue(currentTarget, out int missiles);
                firedMissiles = missiles;
            }
            else
            {
                firedMissiles = 0;
            }
            if (!BDATargetManager.FiredMissiles.Contains(PreviousMissile)) PreviousMissile = null;
            engagedTargets = missilesAway.Count;
            //this.missilesAway = tempMissilesAway;
        }

        public override void OnFixedUpdate()
        {
            if (vessel == null) return;

            if (!vessel.packed)

            {
                if (weaponIndex >= weaponArray.Length)
                {
                    hasSingleFired = true;
                    triggerTimer = 0;

                    weaponIndex = Mathf.Clamp(weaponIndex, 0, weaponArray.Length - 1);

                    SetDeployableWeapons();
                    DisplaySelectedWeaponMessage();
                }
                if (weaponArray.Length > 0 && selectedWeapon != weaponArray[weaponIndex])
                    selectedWeapon = weaponArray[weaponIndex];

                //finding next rocket to shoot (for aimer)
                //FindNextRocket();

                //targeting
                if (weaponIndex > 0 &&
                    (selectedWeapon.GetWeaponClass() == WeaponClasses.Missile ||
                    selectedWeapon.GetWeaponClass() == WeaponClasses.SLW ||
                     selectedWeapon.GetWeaponClass() == WeaponClasses.Bomb))
                {
                    SearchForLaserPoint();
                    SearchForHeatTarget();
                    SearchForRadarSource();
                }

                CalculateMissilesAway();
            }

            if (guardMode && vessel.IsControllable)
            {
                GuardMode();
            }
            else
            {
                targetScanTimer = -100;
            }
            BombAimer();
        }

        void OnDestroy()
        {
            BDArmorySetup.OnVolumeChange -= UpdateVolume;
            BDArmorySetup.OnSavedSettings -= ClampVisualRange;
            GameEvents.onVesselCreate.Remove(OnVesselCreate);
            GameEvents.onPartJointBreak.Remove(OnPartJointBreak);
            GameEvents.onPartDie.Remove(OnPartDie);
            GameEvents.onVesselPartCountChanged.Remove(UpdateMaxGunRange);
            GameEvents.onVesselPartCountChanged.Remove(UpdateCurrentHP);
            GameEvents.onEditorPartPlaced.Remove(UpdateMaxGunRange);
            GameEvents.onEditorPartDeleted.Remove(UpdateMaxGunRange);
        }

        void ClampVisualRange()
        {
            guardRange = Mathf.Clamp(guardRange, BDArmorySettings.RUNWAY_PROJECT ? 20000 : 0, BDArmorySettings.MAX_GUARD_VISUAL_RANGE);
        }

        void OnGUI()
        {
            if (!BDArmorySettings.DEBUG_LINES && lr != null) { lr.enabled = false; }
            if (HighLogic.LoadedSceneIsFlight && vessel == FlightGlobals.ActiveVessel &&
                BDArmorySetup.GAME_UI_ENABLED && !MapView.MapIsEnabled)
            {
                if (BDArmorySettings.DEBUG_LINES)
                {
                    if (incomingMissileVessel)
                    {
                        GUIUtils.DrawLineBetweenWorldPositions(part.transform.position,
                            incomingMissileVessel.transform.position, 5, Color.cyan);
                    }
                }

                if (showBombAimer)
                {
                    MissileBase ml = CurrentMissile;
                    if (ml)
                    {
                        float size = 128;
                        Texture2D texture = BDArmorySetup.Instance.greenCircleTexture;

                        if ((ml is MissileLauncher && ((MissileLauncher)ml).guidanceActive) || ml is BDModularGuidance)
                        {
                            texture = BDArmorySetup.Instance.largeGreenCircleTexture;
                            size = 256;
                        }
                        GUIUtils.DrawTextureOnWorldPos(bombAimerPosition, texture, new Vector2(size, size), 0);
                    }
                }

                //MISSILE LOCK HUD
                MissileBase missile = CurrentMissile;
                if (missile)
                {
                    if (missile.TargetingMode == MissileBase.TargetingModes.Laser)
                    {
                        if (laserPointDetected && foundCam)
                        {
                            GUIUtils.DrawTextureOnWorldPos(foundCam.groundTargetPosition, BDArmorySetup.Instance.greenCircleTexture, new Vector2(48, 48), 1);
                        }

                        using (List<ModuleTargetingCamera>.Enumerator cam = BDATargetManager.ActiveLasers.GetEnumerator())
                            while (cam.MoveNext())
                            {
                                if (cam.Current == null) continue;
                                if (cam.Current.vessel != vessel && cam.Current.surfaceDetected && cam.Current.groundStabilized && !cam.Current.gimbalLimitReached)
                                {
                                    GUIUtils.DrawTextureOnWorldPos(cam.Current.groundTargetPosition, BDArmorySetup.Instance.greenDiamondTexture, new Vector2(18, 18), 0);
                                }
                            }
                    }
                    else if (missile.TargetingMode == MissileBase.TargetingModes.Heat)
                    {
                        MissileBase ml = CurrentMissile;
                        if (heatTarget.exists)
                        {
                            GUIUtils.DrawTextureOnWorldPos(heatTarget.position, BDArmorySetup.Instance.greenCircleTexture, new Vector2(36, 36), 3);
                            float distanceToTarget = Vector3.Distance(heatTarget.position, ml.MissileReferenceTransform.position);
                            GUIUtils.DrawTextureOnWorldPos(ml.MissileReferenceTransform.position + (distanceToTarget * ml.GetForwardTransform()), BDArmorySetup.Instance.largeGreenCircleTexture, new Vector2(128, 128), 0);
                            Vector3 fireSolution = MissileGuidance.GetAirToAirFireSolution(ml, heatTarget.position, heatTarget.velocity);
                            Vector3 fsDirection = (fireSolution - ml.MissileReferenceTransform.position).normalized;
                            GUIUtils.DrawTextureOnWorldPos(ml.MissileReferenceTransform.position + (distanceToTarget * fsDirection), BDArmorySetup.Instance.greenDotTexture, new Vector2(6, 6), 0);
                        }
                        else
                        {
                            GUIUtils.DrawTextureOnWorldPos(ml.MissileReferenceTransform.position + (2000 * ml.GetForwardTransform()), BDArmorySetup.Instance.greenCircleTexture, new Vector2(36, 36), 3);
                            GUIUtils.DrawTextureOnWorldPos(ml.MissileReferenceTransform.position + (2000 * ml.GetForwardTransform()), BDArmorySetup.Instance.largeGreenCircleTexture, new Vector2(156, 156), 0);
                        }
                    }
                    else if (missile.TargetingMode == MissileBase.TargetingModes.Radar)
                    {
                        MissileBase ml = CurrentMissile;
                        //if(radar && radar.locked)
                        if (vesselRadarData && vesselRadarData.locked)
                        {
                            float distanceToTarget = Vector3.Distance(vesselRadarData.lockedTargetData.targetData.predictedPosition, ml.MissileReferenceTransform.position);
                            GUIUtils.DrawTextureOnWorldPos(ml.MissileReferenceTransform.position + (distanceToTarget * ml.GetForwardTransform()), BDArmorySetup.Instance.dottedLargeGreenCircle, new Vector2(128, 128), 0);
                            //Vector3 fireSolution = MissileGuidance.GetAirToAirFireSolution(CurrentMissile, radar.lockedTarget.predictedPosition, radar.lockedTarget.velocity);
                            Vector3 fireSolution = MissileGuidance.GetAirToAirFireSolution(ml, vesselRadarData.lockedTargetData.targetData.predictedPosition, vesselRadarData.lockedTargetData.targetData.velocity);
                            Vector3 fsDirection = (fireSolution - ml.MissileReferenceTransform.position).normalized;
                            GUIUtils.DrawTextureOnWorldPos(ml.MissileReferenceTransform.position + (distanceToTarget * fsDirection), BDArmorySetup.Instance.greenDotTexture, new Vector2(6, 6), 0);

                            //if (BDArmorySettings.DEBUG_MISSILES)
                            if (BDArmorySettings.DEBUG_TELEMETRY)
                            {
                                string dynRangeDebug = string.Empty;
                                MissileLaunchParams dlz = MissileLaunchParams.GetDynamicLaunchParams(missile, vesselRadarData.lockedTargetData.targetData.velocity, vesselRadarData.lockedTargetData.targetData.predictedPosition);
                                dynRangeDebug += "MaxDLZ: " + dlz.maxLaunchRange;
                                dynRangeDebug += "\nMinDLZ: " + dlz.minLaunchRange;
                                GUI.Label(new Rect(800, 600, 200, 200), dynRangeDebug);
                            }
                        }
                    }
                    else if (missile.TargetingMode == MissileBase.TargetingModes.AntiRad)
                    {
                        if (rwr && rwr.rwrEnabled && rwr.displayRWR)
                        {
                            MissileLauncher ml = CurrentMissile as MissileLauncher;
                            for (int i = 0; i < rwr.pingsData.Length; i++)
                            {
                                if (rwr.pingsData[i].exists && (ml.antiradTargets.Contains(rwr.pingsData[i].signalStrength)) && Vector3.Dot(rwr.pingWorldPositions[i] - missile.transform.position, missile.GetForwardTransform()) > 0)
                                {
                                    GUIUtils.DrawTextureOnWorldPos(rwr.pingWorldPositions[i], BDArmorySetup.Instance.greenDiamondTexture, new Vector2(22, 22), 0);
                                }
                            }
                        }

                        if (antiRadTargetAcquired)
                        {
                            GUIUtils.DrawTextureOnWorldPos(antiRadiationTarget,
                                BDArmorySetup.Instance.openGreenSquare, new Vector2(22, 22), 0);
                        }
                    }
                }

                if ((missile && missile.TargetingMode == MissileBase.TargetingModes.Gps) || BDArmorySetup.Instance.showingWindowGPS)
                {
                    if (designatedGPSCoords != Vector3d.zero)
                    {
                        GUIUtils.DrawTextureOnWorldPos(VectorUtils.GetWorldSurfacePostion(designatedGPSCoords, vessel.mainBody), BDArmorySetup.Instance.greenSpikedPointCircleTexture, new Vector2(22, 22), 0);
                    }
                }
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES || BDArmorySettings.DEBUG_WEAPONS)
                    debugString.Length = 0;
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_MISSILES)
                {
                    debugString.AppendLine("Missiles away: " + firedMissiles + "; targeted vessels: " + engagedTargets);

                    if (missileIsIncoming)
                    {
                        foreach (var incomingMissile in results.incomingMissiles)
                            debugString.AppendLine("Incoming missile: " + (incomingMissile.vessel != null ? incomingMissile.vessel.vesselName + " @ " + incomingMissile.distance.ToString("0") + "m (" + incomingMissile.time.ToString("0.0") + "s)" : null));
                    }
                    if (underAttack) debugString.AppendLine("Under attack from " + (incomingThreatVessel != null ? incomingThreatVessel.vesselName : null));
                    if (underFire) debugString.AppendLine("Under fire from " + (priorGunThreatVessel != null ? priorGunThreatVessel.vesselName : null));
                    if (isChaffing) debugString.AppendLine("Chaffing");
                    if (isFlaring) debugString.AppendLine("Flaring");
                    if (isECMJamming) debugString.AppendLine("ECMJamming");
                    if (isCloaking) debugString.AppendLine("Cloaking");
                }
                if (BDArmorySettings.DEBUG_TELEMETRY || BDArmorySettings.DEBUG_WEAPONS)
                {
                    if (weaponArray != null) // Heat debugging
                    {
                        List<string> weaponHeatDebugStrings = new List<string>();
                        List<string> weaponLaserDebugStrings = new List<string>();
                        HashSet<WeaponClasses> validClasses = new HashSet<WeaponClasses> { WeaponClasses.Gun, WeaponClasses.Rocket, WeaponClasses.DefenseLaser };
                        foreach (var weaponCandidate in weaponArray)
                        {
                            if (weaponCandidate == null || !validClasses.Contains(weaponCandidate.GetWeaponClass())) continue;
                            var weapon = (ModuleWeapon)weaponCandidate;
                            weaponHeatDebugStrings.Add(String.Format(" - {0}: heat: {1,6:F1}, max: {2}, overheated: {3}", weapon.shortName, weapon.heat, weapon.maxHeat, weapon.isOverheated));
                            weaponLaserDebugStrings.Add($" - Lead Offset: {weapon.GetLeadOffset()}, FinalAimTgt: {weapon.finalAimTarget}, tgt Position: {weapon.targetPosition}, pointingAtSelf: {weapon.pointingAtSelf}, safeToFire: {weapon.safeToFire}, tgt CosAngle {weapon.targetCosAngle}, wpn CosAngle {weapon.targetAdjustedMaxCosAngle}, Wpn Autofire {weapon.autoFire}, RoF {weapon.roundsPerMinute}, MaxRoF {weapon.baseRPM}");
                        }
                        if (weaponHeatDebugStrings.Count > 0)
                        {
                            debugString.AppendLine("Weapon Heat:\n" + string.Join("\n", weaponHeatDebugStrings));
                            debugString.AppendLine("Aim debugging:\n" + string.Join("\n", weaponLaserDebugStrings));
                        }
                    }
                    GUI.Label(new Rect(200, Screen.height - 500, Screen.width / 2 - 200, 16 * debugString.Length), debugString.ToString());
                }
            }
        }

        bool CheckMouseIsOnGui()
        {
            return GUIUtils.CheckMouseIsOnGui();
        }

        #endregion KSP Events

        #region Enumerators

        IEnumerator StartupListUpdater()
        {
            while (vessel.packed || !FlightGlobals.ready)
            {
                yield return null;
                if (vessel.isActiveVessel)
                {
                    BDArmorySetup.Instance.ActiveWeaponManager = this;
                }
            }
            UpdateList();
        }

        IEnumerator MissileWarningResetRoutine()
        {
            while (enabled)
            {
                yield return new WaitUntil(() => missileIsIncoming); // Wait until missile is incoming.
                if (BDArmorySettings.DEBUG_AI) { Debug.Log("[BDArmory.MissileFire]: Triggering missile warning on " + vessel.vesselName); }
                yield return new WaitUntil(() => Time.time - incomingMissileLastDetected > 1f); // Wait until 1s after no missiles are detected.
                if (BDArmorySettings.DEBUG_AI) { Debug.Log("[BDArmory.MissileFire]: Silencing missile warning on " + vessel.vesselName); }
                missileIsIncoming = false;
            }
        }

        IEnumerator UnderFireRoutine()
        {
            underFireLastNotified = Time.time; // Update the last notification.
            if (underFire) yield break; // Already under fire, we only want 1 timer.
            underFire = true;
            if (BDArmorySettings.DEBUG_AI) { Debug.Log("[BDArmory.MissileFire]: Triggering under fire warning on " + vessel.vesselName + " by " + priorGunThreatVessel.vesselName); }
            yield return new WaitUntil(() => Time.time - underFireLastNotified > 1f); // Wait until 1s after being under fire.
            if (BDArmorySettings.DEBUG_AI) { Debug.Log("[BDArmory.MissileFire]: Silencing under fire warning on " + vessel.vesselName); }
            underFire = false;
            priorGunThreatVessel = null;
        }

        IEnumerator UnderAttackRoutine()
        {
            underAttackLastNotified = Time.time; // Update the last notification.
            if (underAttack) yield break; // Already under attack, we only want 1 timer.
            underAttack = true;
            if (BDArmorySettings.DEBUG_AI) { Debug.Log("[BDArmory.MissileFire]: Triggering under attack warning on " + vessel.vesselName + " by " + incomingThreatVessel.vesselName); }
            yield return new WaitUntil(() => Time.time - underAttackLastNotified > 1f); // Wait until 3s after being under attack.
            if (BDArmorySettings.DEBUG_AI) { Debug.Log("[BDArmory.MissileFire]: Silencing under attack warning on " + vessel.vesselName); }
            underAttack = false;
        }

        IEnumerator GuardTurretRoutine()
        {
            if (SetDeployableWeapons())
            {
                yield return new WaitForSeconds(2f);
            }

            if (gameObject.activeInHierarchy)
            //target is out of visual range, try using sensors
            {
                if (guardTarget.LandedOrSplashed)
                {
                    if (targetingPods.Count > 0)
                    {
                        using (List<ModuleTargetingCamera>.Enumerator tgp = targetingPods.GetEnumerator())
                            while (tgp.MoveNext())
                            {
                                if (tgp.Current == null) continue;
                                if (!tgp.Current.enabled || (tgp.Current.cameraEnabled && tgp.Current.groundStabilized &&
                                                             !((tgp.Current.groundTargetPosition - guardTarget.transform.position).sqrMagnitude > 20 * 20))) continue;
                                tgp.Current.EnableCamera();
                                yield return StartCoroutine(tgp.Current.PointToPositionRoutine(guardTarget.CoM));
                                //yield return StartCoroutine(tgp.Current.PointToPositionRoutine(TargetInfo.TargetCOMDispersion(guardTarget)));
                                if (!tgp.Current) continue;
                                if (tgp.Current.groundStabilized && guardTarget &&
                                    (tgp.Current.groundTargetPosition - guardTarget.transform.position).sqrMagnitude < 20 * 20)
                                {
                                    tgp.Current.slaveTurrets = true;
                                    StartGuardTurretFiring();
                                    yield break;
                                }
                                tgp.Current.DisableCamera();
                            }
                    }

                    if (!guardTarget || (guardTarget.transform.position - transform.position).sqrMagnitude > guardRange * guardRange)
                    {
                        SetTarget(null); //disengage, sensors unavailable.
                        yield break;
                    }
                }
                else
                {
                    // Turn on radars if off
                    if (!results.foundAntiRadiationMissile)
                    {
                        using (List<ModuleRadar>.Enumerator rd = radars.GetEnumerator())
                            while (rd.MoveNext())
                            {
                                if (rd.Current != null || rd.Current.canLock)
                                {
                                    rd.Current.EnableRadar();
                                }
                            }
                    }

                    // Try to lock target, or if already locked, fire on it
                    if (vesselRadarData &&
                        (!vesselRadarData.locked ||
                         (vesselRadarData.lockedTargetData.targetData.predictedPosition - guardTarget.transform.position)
                             .sqrMagnitude > 40 * 40))
                    {
                        //vesselRadarData.TryLockTarget(guardTarget.transform.position);
                        vesselRadarData.TryLockTarget(guardTarget);
                        yield return new WaitForSeconds(0.5f);
                        if (guardTarget && vesselRadarData && vesselRadarData.locked &&
                            vesselRadarData.lockedTargetData.vessel == guardTarget)
                        {
                            vesselRadarData.SlaveTurrets();
                            StartGuardTurretFiring();
                            yield break;
                        }
                    }
                    else if (guardTarget && vesselRadarData && vesselRadarData.locked &&
                            vesselRadarData.lockedTargetData.vessel == guardTarget)
                    {
                        vesselRadarData.SlaveTurrets();
                        StartGuardTurretFiring();
                        yield break;
                    }

                    if (!guardTarget || (guardTarget.transform.position - transform.position).sqrMagnitude > guardRange * guardRange)
                    {
                        SetTarget(null); //disengage, sensors unavailable.
                        yield break;
                    }
                }
            }

            StartGuardTurretFiring();
            yield break;
        }

        IEnumerator ResetMissileThreatDistanceRoutine()
        {
            yield return new WaitForSeconds(8);
            incomingMissileDistance = float.MaxValue;
            incomingMissileTime = float.MaxValue;
        }

        IEnumerator GuardMissileRoutine()
        {
            MissileBase ml = CurrentMissile;

            if (ml && !guardFiringMissile)
            {
                guardFiringMissile = true;

                if (ml.TargetingMode == MissileBase.TargetingModes.Radar && vesselRadarData)
                {
                    float BayTriggerTime = -1;
                    if (SetCargoBays())
                    {
                        BayTriggerTime = Time.time;
                        //yield return new WaitForSeconds(2f); //so this doesn't delay radar targeting stuff below
                    }

                    float attemptLockTime = Time.time;
                    while ((!vesselRadarData.locked || (vesselRadarData.lockedTargetData.vessel != guardTarget)) && Time.time - attemptLockTime < 2)
                    {
                        if (vesselRadarData.locked)
                        {
                            vesselRadarData.SwitchActiveLockedTarget(guardTarget);
                            yield return null;
                        }
                        //vesselRadarData.TryLockTarget(guardTarget.transform.position+(guardTarget.rb_velocity*Time.fixedDeltaTime));
                        vesselRadarData.TryLockTarget(guardTarget);
                        yield return new WaitForSeconds(0.25f);
                    }

                    // if (ml && AIMightDirectFire() && vesselRadarData.locked)
                    // {
                    //     SetCargoBays();
                    //     float LAstartTime = Time.time;
                    //     while (AIMightDirectFire() && Time.time - LAstartTime < 3 && !GetLaunchAuthorization(guardTarget, this))
                    //     {
                    //         yield return new WaitForFixedUpdate();
                    //     }
                    //     // yield return new WaitForSeconds(0.5f);
                    // }

                    //wait for missile turret to point at target
                    //TODO BDModularGuidance: add turret
                    MissileLauncher mlauncher = ml as MissileLauncher;
                    if (mlauncher != null)
                    {
                        if (guardTarget && ml && mlauncher.missileTurret && vesselRadarData.locked)
                        {
                            vesselRadarData.SlaveTurrets();
                            float turretStartTime = Time.time;
                            while (Time.time - turretStartTime < 5)
                            {
                                float angle = Vector3.Angle(mlauncher.missileTurret.finalTransform.forward, mlauncher.missileTurret.slavedTargetPosition - mlauncher.missileTurret.finalTransform.position);
                                if (angle < mlauncher.missileTurret.fireFOV)
                                {
                                    break;
                                    // turretStartTime -= 2 * Time.fixedDeltaTime;
                                }
                                yield return new WaitForFixedUpdate();
                            }
                        }
                    }

                    yield return null;

                    // if (ml && guardTarget && vesselRadarData.locked && (!AIMightDirectFire() || GetLaunchAuthorization(guardTarget, this)))
                    if (ml && guardTarget && vesselRadarData.locked && vesselRadarData.lockedTargetData.vessel == guardTarget && GetLaunchAuthorization(guardTarget, this))
                    {
                        if (BDArmorySettings.DEBUG_MISSILES)
                        {
                            Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " firing on target " + guardTarget.GetName());
                        }
                        if (BayTriggerTime > 0 && (Time.time - BayTriggerTime < 2)) //if bays opening, see if 2 sec for the bays to open have elapsed, if not, wait remaining time needed
                        {
                            yield return new WaitForSeconds(2 - (Time.time - BayTriggerTime));
                        }
                        FireCurrentMissile(true);
                        //StartCoroutine(MissileAwayRoutine(mlauncher));
                    }
                }
                else if (ml.TargetingMode == MissileBase.TargetingModes.Heat)
                {
                    if (vesselRadarData && vesselRadarData.locked) // FIXME This wipes radar guided missiles' targeting data when switching to a heat guided missile. Radar is used to allow heat seeking missiles with allAspect = true to lock on target and fire when the target is not within sensor FOV
                    {
                        vesselRadarData.UnlockAllTargets(); //maybe use vrd.UnlockCurrentTarget() instead?
                        vesselRadarData.UnslaveTurrets();
                    }

                    if (SetCargoBays())
                    {
                        yield return new WaitForSeconds(2f);
                    }

                    float attemptStartTime = Time.time;
                    float attemptDuration = Mathf.Max(targetScanInterval * 0.75f, 5f);
                    MissileLauncher mlauncher;
                    while (ml && guardTarget && Time.time - attemptStartTime < attemptDuration && (!heatTarget.exists || (heatTarget.predictedPosition - guardTarget.transform.position).sqrMagnitude > 40 * 40))
                    {
                        //TODO BDModularGuidance: add turret
                        //try using missile turret to lock target
                        mlauncher = ml as MissileLauncher;
                        if (mlauncher != null)
                        {
                            if (mlauncher.missileTurret)
                            {
                                mlauncher.missileTurret.slaved = true;
                                mlauncher.missileTurret.slavedTargetPosition = guardTarget.CoM;
                                mlauncher.missileTurret.SlavedAim();
                            }
                        }

                        yield return new WaitForFixedUpdate();
                    }

                    //try uncaged IR lock with radar
                    if (guardTarget && !heatTarget.exists && vesselRadarData && vesselRadarData.radarCount > 0)//Switch to IRST detection?
                    {
                        if (!vesselRadarData.locked ||
                            (vesselRadarData.lockedTargetData.targetData.predictedPosition -
                             guardTarget.transform.position).sqrMagnitude > 40 * 40)
                        {
                            //vesselRadarData.TryLockTarget(guardTarget.transform.position);
                            vesselRadarData.TryLockTarget(guardTarget);
                            yield return new WaitForSeconds(Mathf.Min(1, (targetScanInterval * 0.25f)));
                        }
                    }
                    // if (AIMightDirectFire() && ml && heatTarget.exists)
                    // {
                    //     float LAstartTime = Time.time;
                    //     while (Time.time - LAstartTime < 3 && AIMightDirectFire() && GetLaunchAuthorization(guardTarget, this))
                    //     {
                    //         yield return new WaitForFixedUpdate();
                    //     }
                    //     yield return new WaitForSeconds(0.5f);
                    // }

                    //wait for missile turret to point at target
                    mlauncher = ml as MissileLauncher;
                    if (mlauncher != null)
                    {
                        if (ml && mlauncher.missileTurret && heatTarget.exists)
                        {
                            float turretStartTime = attemptStartTime;
                            while (heatTarget.exists && Time.time - turretStartTime < Mathf.Max(targetScanInterval / 2f, 2))
                            {
                                float angle = Vector3.Angle(mlauncher.missileTurret.finalTransform.forward, mlauncher.missileTurret.slavedTargetPosition - mlauncher.missileTurret.finalTransform.position);
                                mlauncher.missileTurret.slaved = true;
                                mlauncher.missileTurret.slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(mlauncher, heatTarget.predictedPosition, heatTarget.velocity);
                                mlauncher.missileTurret.SlavedAim();

                                if (angle < mlauncher.missileTurret.fireFOV)
                                {
                                    break;
                                    // turretStartTime -= 3 * Time.fixedDeltaTime;
                                }
                                yield return new WaitForFixedUpdate();
                            }
                        }
                    }

                    yield return null;

                    // if (guardTarget && ml && heatTarget.exists && (!AIMightDirectFire() || GetLaunchAuthorization(guardTarget, this)))
                    if (guardTarget && ml && heatTarget.exists && heatTarget.vessel == guardTarget && GetLaunchAuthorization(guardTarget, this))
                    {
                        if (BDArmorySettings.DEBUG_MISSILES)
                        {
                            Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " firing on target " + guardTarget.GetName());
                        }

                        FireCurrentMissile(true);
                        //StartCoroutine(MissileAwayRoutine(mlauncher));
                    }
                    //else //event that heatTarget.exists && heatTarget != guardtarget?
                }
                else if (ml.TargetingMode == MissileBase.TargetingModes.Gps)
                {
                    designatedGPSInfo = new GPSTargetInfo(VectorUtils.WorldPositionToGeoCoords(guardTarget.CoM, vessel.mainBody), guardTarget.vesselName.Substring(0, Mathf.Min(12, guardTarget.vesselName.Length)));
                    if (SetCargoBays())
                    {
                        yield return new WaitForSeconds(2f);
                    }
                    MissileLauncher mlauncher;
                    mlauncher = ml as MissileLauncher;
                    if (mlauncher != null)
                    {
                        if (ml && mlauncher.missileTurret)
                        {
                            float turretStartTime = Time.time;
                            while (Time.time - turretStartTime < Mathf.Max(targetScanInterval / 2f, 2))
                            {
                                float angle = Vector3.Angle(mlauncher.missileTurret.finalTransform.forward, mlauncher.missileTurret.slavedTargetPosition - mlauncher.missileTurret.finalTransform.position);
                                mlauncher.missileTurret.slaved = true;
                                mlauncher.missileTurret.slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(mlauncher, designatedGPSInfo.worldPos, designatedGPSInfo.gpsVessel.Velocity());
                                mlauncher.missileTurret.SlavedAim();

                                if (angle < mlauncher.missileTurret.fireFOV)
                                {
                                    break;
                                    // turretStartTime -= 3 * Time.fixedDeltaTime;
                                }
                                yield return new WaitForFixedUpdate();
                            }
                        }
                    }
                    yield return null;
                    if (BDArmorySettings.DEBUG_MISSILES)
                    {
                        Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " firing GPS missile at " + designatedGPSInfo.worldPos);
                    }
                    FireCurrentMissile(true);
                    //if (FireCurrentMissile(true))
                    //    StartCoroutine(MissileAwayRoutine(ml)); //NEW: try to prevent launching all missile complements at once...
                }
                else if (ml.TargetingMode == MissileBase.TargetingModes.AntiRad)
                {
                    if (rwr)
                    {
                        if (!rwr.rwrEnabled) rwr.EnableRWR();
                        if (rwr.rwrEnabled && !rwr.displayRWR) rwr.displayRWR = true;
                    }

                    if (SetCargoBays())
                    {
                        yield return new WaitForSeconds(2f);
                    }

                    float attemptStartTime = Time.time;
                    float attemptDuration = targetScanInterval * 0.75f;
                    MissileLauncher mlauncher;
                    while (Time.time - attemptStartTime < attemptDuration &&
                           (!antiRadTargetAcquired || !AntiRadDistanceCheck()))
                    {
                        mlauncher = ml as MissileLauncher;
                        if (mlauncher != null)
                        {
                            if (mlauncher.missileTurret)
                            {
                                mlauncher.missileTurret.slaved = true;
                                mlauncher.missileTurret.slavedTargetPosition = guardTarget.CoM;
                                mlauncher.missileTurret.SlavedAim();
                            }
                        }
                        yield return new WaitForFixedUpdate();
                    }
                    mlauncher = ml as MissileLauncher;
                    if (mlauncher != null)
                    {
                        if (ml && mlauncher.missileTurret && antiRadTargetAcquired)
                        {
                            float turretStartTime = attemptStartTime;
                            while (antiRadTargetAcquired && Time.time - turretStartTime < Mathf.Max(targetScanInterval / 2f, 2))
                            {
                                float angle = Vector3.Angle(mlauncher.missileTurret.finalTransform.forward, mlauncher.missileTurret.slavedTargetPosition - mlauncher.missileTurret.finalTransform.position);
                                mlauncher.missileTurret.slaved = true;
                                mlauncher.missileTurret.slavedTargetPosition = MissileGuidance.GetAirToAirFireSolution(mlauncher, antiRadiationTarget, guardTarget.Velocity());
                                mlauncher.missileTurret.SlavedAim();

                                if (angle < mlauncher.missileTurret.fireFOV)
                                {
                                    break;
                                    // turretStartTime -= 3 * Time.fixedDeltaTime;
                                }
                                yield return new WaitForFixedUpdate();
                            }
                        }
                    }

                    yield return null;
                    if (ml && antiRadTargetAcquired && AntiRadDistanceCheck())
                    {
                        FireCurrentMissile(true);
                        //StartCoroutine(MissileAwayRoutine(ml));
                    }
                }
                else if (ml.TargetingMode == MissileBase.TargetingModes.Laser)
                {
                    if (SetCargoBays())
                    {
                        yield return new WaitForSeconds(2f);
                    }

                    if (targetingPods.Count > 0) //if targeting pods are available, slew them onto target and lock.
                    {
                        using (List<ModuleTargetingCamera>.Enumerator tgp = targetingPods.GetEnumerator())
                            while (tgp.MoveNext())
                            {
                                if (tgp.Current == null) continue;
                                tgp.Current.EnableCamera();
                                tgp.Current.CoMLock = true;
                                yield return StartCoroutine(tgp.Current.PointToPositionRoutine(guardTarget.CoM));
                                //if (tgp.Current.groundStabilized && (tgp.Current.GroundtargetPosition - guardTarget.transform.position).sqrMagnitude < 20 * 20) 
                                //if ((tgp.Current.groundTargetPosition - guardTarget.transform.position).sqrMagnitude < 10 * 10) 
                                //{
                                //    tgp.Current.CoMLock = true; // make the designator continue to paint target
                                //    break;
                                //}
                            }
                    }

                    //search for a laser point that corresponds with target vessel
                    float attemptStartTime = Time.time;
                    float attemptDuration = targetScanInterval * 0.75f;
                    while (Time.time - attemptStartTime < attemptDuration && (!laserPointDetected || (foundCam && (foundCam.groundTargetPosition - guardTarget.CoM).sqrMagnitude > 10 * 10)))
                    {
                        yield return new WaitForFixedUpdate();
                    }

                    MissileLauncher mlauncher = ml as MissileLauncher;
                    if (mlauncher != null)
                    {
                        if (guardTarget && ml && mlauncher.missileTurret && laserPointDetected)
                        {
                            //foundCam.SlaveTurrets();
                            float turretStartTime = attemptStartTime;
                            while (Time.time - turretStartTime < Mathf.Max(targetScanInterval / 2f, 2))
                            {
                                float angle = Vector3.Angle(mlauncher.missileTurret.finalTransform.forward, mlauncher.missileTurret.slavedTargetPosition - mlauncher.missileTurret.finalTransform.position);
                                mlauncher.missileTurret.slaved = true;
                                mlauncher.missileTurret.slavedTargetPosition = foundCam.groundTargetPosition;
                                mlauncher.missileTurret.SlavedAim();
                                if (angle < mlauncher.missileTurret.fireFOV)
                                {
                                    break;
                                    // turretStartTime -= 2 * Time.fixedDeltaTime;
                                }
                                yield return new WaitForFixedUpdate();
                            }
                        }
                    }
                    yield return null;

                    if (ml && laserPointDetected && foundCam && (foundCam.groundTargetPosition - guardTarget.CoM).sqrMagnitude < 10 * 10)
                    {
                        FireCurrentMissile(true);
                        //StartCoroutine(MissileAwayRoutine(ml));
                    }
                    else
                    {
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire]: Laser Target Error");
                    }
                }
                guardFiringMissile = false;
            }
        }

        IEnumerator GuardBombRoutine()
        {
            guardFiringMissile = true;
            bool hasSetCargoBays = false;
            float bombStartTime = Time.time;
            float bombAttemptDuration = Mathf.Max(targetScanInterval, 12f);
            float radius = CurrentMissile.GetBlastRadius() * Mathf.Min((1 + (maxMissilesOnTarget / 2f)), 1.5f);
            if (CurrentMissile.TargetingMode == MissileBase.TargetingModes.Gps && (designatedGPSInfo.worldPos - guardTarget.CoM).sqrMagnitude > CurrentMissile.GetBlastRadius() * CurrentMissile.GetBlastRadius())
            {
                //check database for target first
                float twoxsqrRad = 4f * radius * radius;
                bool foundTargetInDatabase = false;
                using (List<GPSTargetInfo>.Enumerator gps = BDATargetManager.GPSTargetList(Team).GetEnumerator())
                    while (gps.MoveNext())
                    {
                        if (!((gps.Current.worldPos - guardTarget.CoM).sqrMagnitude < twoxsqrRad)) continue;
                        designatedGPSInfo = gps.Current;
                        foundTargetInDatabase = true;
                        break;
                    }

                //no target in gps database, acquire via targeting pod
                if (!foundTargetInDatabase)
                {
                    if (targetingPods.Count > 0)
                    {
                        using (List<ModuleTargetingCamera>.Enumerator tgp = targetingPods.GetEnumerator())
                            while (tgp.MoveNext())
                            {
                                if (tgp.Current == null) continue;
                                tgp.Current.EnableCamera();
                                tgp.Current.CoMLock = true;
                                yield return StartCoroutine(tgp.Current.PointToPositionRoutine(guardTarget.CoM));

                                if (guardTarget && tgp.Current.groundStabilized && (tgp.Current.targetPointPosition - guardTarget.transform.position).sqrMagnitude < CurrentMissile.GetBlastRadius() * CurrentMissile.GetBlastRadius()) //was tgp.groundtargetposition
                                {
                                    radius = 500;
                                    designatedGPSInfo = new GPSTargetInfo(tgp.Current.bodyRelativeGTP, "Guard Target");
                                    bombStartTime = Time.time;
                                }
                                else//failed to acquire target via tgp, cancel.
                                {
                                    tgp.Current.DisableCamera();
                                    designatedGPSInfo = new GPSTargetInfo();
                                    guardFiringMissile = false;
                                    yield break;
                                }

                            }
                    }
                    else //no gps target and no tgp, cancel.
                    {
                        guardFiringMissile = false;
                        yield break;
                    }
                }
            }

            bool doProxyCheck = true;

            float prevDist = 2 * radius;
            radius = Mathf.Max(radius, 50f);
            while (guardTarget && Time.time - bombStartTime < bombAttemptDuration && weaponIndex > 0 &&
                   weaponArray[weaponIndex].GetWeaponClass() == WeaponClasses.Bomb && firedMissiles < maxMissilesOnTarget)
            {
                float targetDist = Vector3.Distance(bombAimerPosition, guardTarget.CoM);

                if (targetDist < (radius * 20f) && !hasSetCargoBays)
                {
                    SetCargoBays();
                    hasSetCargoBays = true;
                }

                if (targetDist > radius
                    || Vector3.Dot(VectorUtils.GetUpDirection(vessel.CoM), vessel.transform.forward) > 0) // roll check
                {
                    if (targetDist < Mathf.Max(radius * 2, 800f) &&
                        Vector3.Dot(guardTarget.CoM - bombAimerPosition, guardTarget.CoM - transform.position) < 0)
                    {
                        pilotAI.RequestExtend("too close to bomb", guardTarget); // Extend from target vessel.
                        break;
                    }
                    yield return null;
                }
                else
                {
                    if (doProxyCheck)
                    {
                        if (targetDist - prevDist > 0)
                        {
                            doProxyCheck = false;
                        }
                        else
                        {
                            prevDist = targetDist;
                        }
                    }

                    if (!doProxyCheck)
                    {
                        FireCurrentMissile(true);
                        timeBombReleased = Time.time;
                        yield return new WaitForSeconds(rippleFire ? 60f / rippleRPM : 0.06f);
                        if (firedMissiles >= maxMissilesOnTarget)
                        {
                            yield return new WaitForSeconds(1f);
                            if (pilotAI)
                            {
                                pilotAI.RequestExtend("bombs away!", null, radius, guardTarget.CoM); // Extend from the place the bomb is expected to fall.
                            }   //maybe something similar should be adapted for any missiles with nuke warheards...?
                        }
                    }
                    else
                    {
                        yield return null;
                    }
                }
            }

            designatedGPSInfo = new GPSTargetInfo();
            guardFiringMissile = false;
        }

        //IEnumerator MissileAwayRoutine(MissileBase ml)
        //{
        //    missilesAway++;

        //    MissileLauncher launcher = ml as MissileLauncher;
        //    if (launcher != null)
        //    {
        //        float timeStart = Time.time;
        //        float timeLimit = Mathf.Max(launcher.dropTime + launcher.cruiseTime + launcher.boostTime + 4, 10);
        //        while (ml)
        //        {
        //            if (ml.guidanceActive && Time.time - timeStart < timeLimit)
        //            {
        //                yield return null;
        //            }
        //            else
        //            {
        //                break;
        //            }

        //        }
        //    }
        //    else
        //    {
        //        while (ml)
        //        {
        //            if (ml.MissileState != MissileBase.MissileStates.PostThrust)
        //            {
        //                yield return null;

        //            }
        //            else
        //            {
        //                break;
        //            }
        //        }
        //    }

        //    missilesAway--;
        //}

        //IEnumerator BombsAwayRoutine(MissileBase ml)
        //{
        //    missilesAway++;
        //    float timeStart = Time.time;
        //    float timeLimit = 3;
        //    while (ml)
        //    {
        //        if (Time.time - timeStart < timeLimit)
        //        {
        //            yield return null;
        //        }
        //        else
        //        {
        //            break;
        //        }
        //    }
        //    missilesAway--;
        //}
        #endregion Enumerators

        #region Audio

        void UpdateVolume()
        {
            if (audioSource)
            {
                audioSource.volume = BDArmorySettings.BDARMORY_UI_VOLUME;
            }
            if (warningAudioSource)
            {
                warningAudioSource.volume = BDArmorySettings.BDARMORY_UI_VOLUME;
            }
            if (targetingAudioSource)
            {
                targetingAudioSource.volume = BDArmorySettings.BDARMORY_UI_VOLUME;
            }
        }

        void UpdateTargetingAudio()
        {
            if (BDArmorySetup.GameIsPaused)
            {
                if (targetingAudioSource.isPlaying)
                {
                    targetingAudioSource.Stop();
                }
                return;
            }

            if (selectedWeapon != null && selectedWeapon.GetWeaponClass() == WeaponClasses.Missile && vessel.isActiveVessel)
            {
                MissileBase ml = CurrentMissile;
                if (ml.TargetingMode == MissileBase.TargetingModes.Heat)
                {
                    if (targetingAudioSource.clip != heatGrowlSound)
                    {
                        targetingAudioSource.clip = heatGrowlSound;
                    }

                    if (heatTarget.exists)
                    {
                        targetingAudioSource.pitch = Mathf.MoveTowards(targetingAudioSource.pitch, 2, 8 * Time.deltaTime);
                    }
                    else
                    {
                        targetingAudioSource.pitch = Mathf.MoveTowards(targetingAudioSource.pitch, 1, 8 * Time.deltaTime);
                    }

                    if (!targetingAudioSource.isPlaying)
                    {
                        targetingAudioSource.Play();
                    }
                }
                else
                {
                    if (targetingAudioSource.isPlaying)
                    {
                        targetingAudioSource.Stop();
                    }
                }
            }
            else
            {
                targetingAudioSource.pitch = 1;
                if (targetingAudioSource.isPlaying)
                {
                    targetingAudioSource.Stop();
                }
            }
        }

        IEnumerator WarningSoundRoutine(float distance, MissileBase ml)//give distance parameter
        {
            if (distance < this.guardRange)
            {
                warningSounding = true;
                BDArmorySetup.Instance.missileWarningTime = Time.time;
                BDArmorySetup.Instance.missileWarning = true;
                warningAudioSource.pitch = distance < 800 ? 1.45f : 1f;
                warningAudioSource.PlayOneShot(warningSound);

                float waitTime = distance < 800 ? .25f : 1.5f;

                yield return new WaitForSeconds(waitTime);

                if (ml.vessel && CanSeeTarget(ml.vessel))
                {
                    BDATargetManager.ReportVessel(ml.vessel, this);
                }
            }
            warningSounding = false;
        }

        #endregion Audio

        #region CounterMeasure

        public bool isChaffing;
        public bool isFlaring;
        public bool isECMJamming;
        public bool isCloaking;

        bool isLegacyCMing;

        int cmCounter;
        int cmAmount = 5;

        public void FireAllCountermeasures(int count)
        {
            if (!isChaffing && !isFlaring && ThreatClosingTime(incomingMissileVessel) > cmThreshold)
            {
                StartCoroutine(AllCMRoutine(count));
            }
        }

        public void FireECM()
        {
            if (!isECMJamming)
            {
                StartCoroutine(ECMRoutine());
            }
        }

        public void FireOCM(bool thermalthreat)
        {
            if (!isCloaking)
            {
                StartCoroutine(CloakRoutine(thermalthreat));
            }
        }

        public void FireChaff()
        {
            if (!isChaffing && ThreatClosingTime(incomingMissileVessel) <= cmThreshold)
            {
                StartCoroutine(ChaffRoutine((int)chaffRepetition, chaffInterval));
            }
        }

        public void FireFlares()
        {
            if (!isFlaring && ThreatClosingTime(incomingMissileVessel) <= cmThreshold)
            {
                StartCoroutine(FlareRoutine((int)cmRepetition, cmInterval));
                StartCoroutine(ResetMissileThreatDistanceRoutine());
            }
        }

        IEnumerator ECMRoutine()
        {
            isECMJamming = true;
            //yield return new WaitForSeconds(UnityEngine.Random.Range(0.2f, 1f));
            using (var ecm = VesselModuleRegistry.GetModules<ModuleECMJammer>(vessel).GetEnumerator())
                while (ecm.MoveNext())
                {
                    if (ecm.Current == null) continue;
                    if (ecm.Current.jammerEnabled) continue;
                    ecm.Current.EnableJammer();
                }
            yield return new WaitForSeconds(10.0f);
            isECMJamming = false;

            using (var ecm1 = VesselModuleRegistry.GetModules<ModuleECMJammer>(vessel).GetEnumerator())
                while (ecm1.MoveNext())
                {
                    if (ecm1.Current == null) continue;
                    ecm1.Current.DisableJammer();
                }
        }

        IEnumerator CloakRoutine(bool thermalthreat)
        {
            //Debug.Log("[Cloaking] under fire! cloaking!");

            using (var ocm = VesselModuleRegistry.GetModules<ModuleCloakingDevice>(vessel).GetEnumerator())
                while (ocm.MoveNext())
                {
                    if (ocm.Current == null) continue;
                    if (ocm.Current.cloakEnabled) continue;
                    if (thermalthreat && ocm.Current.thermalReductionFactor >= 1) continue; //don't bother activating non-thermoptic camo when incoming heatseekers
                    if (!thermalthreat && ocm.Current.opticalReductionFactor >= 1) continue; //similarly, don't activate purely thermal cloaking systems if under gunfrire
                    isCloaking = true;
                    ocm.Current.EnableCloak();
                }
            yield return new WaitForSeconds(10.0f);
            isCloaking = false;

            using (var ocm1 = VesselModuleRegistry.GetModules<ModuleCloakingDevice>(vessel).GetEnumerator())
                while (ocm1.MoveNext())
                {
                    if (ocm1.Current == null) continue;
                    ocm1.Current.DisableCloak();
                }
        }

        IEnumerator ChaffRoutine(int repetition, float interval)
        {
            isChaffing = true;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " starting chaff routine");
            // yield return new WaitForSeconds(0.2f); // Reaction time delay
            for (int i = 0; i < repetition; i++)
            {
                using (var cm = VesselModuleRegistry.GetModules<CMDropper>(vessel).GetEnumerator())
                    while (cm.MoveNext())
                    {
                        if (cm.Current == null) continue;
                        if (cm.Current.cmType == CMDropper.CountermeasureTypes.Chaff)
                        {
                            cm.Current.DropCM();
                        }
                    }

                yield return new WaitForSeconds(interval);
            }
            yield return new WaitForSeconds(chaffWaitTime);
            isChaffing = false;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " ending chaff routine");
        }

        IEnumerator FlareRoutine(int repetition, float interval)
        {
            isFlaring = true;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " starting flare routine");
            // yield return new WaitForSeconds(0.2f); // Reaction time delay
            for (int i = 0; i < repetition; i++)
            {
                using (var cm = VesselModuleRegistry.GetModules<CMDropper>(vessel).GetEnumerator())
                    while (cm.MoveNext())
                    {
                        if (cm.Current == null) continue;
                        if (cm.Current.cmType == CMDropper.CountermeasureTypes.Flare)
                        {
                            cm.Current.DropCM();
                        }
                    }
                yield return new WaitForSeconds(interval);
            }
            yield return new WaitForSeconds(cmWaitTime);
            isFlaring = false;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " ending flare routine");
        }

        IEnumerator AllCMRoutine(int count)
        {
            // Use this routine for missile threats that are outside of the cmThreshold
            isFlaring = true;
            isChaffing = true;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " starting All CM routine");
            for (int i = 0; i < count; i++)
            {
                using (var cm = VesselModuleRegistry.GetModules<CMDropper>(vessel).GetEnumerator())
                    while (cm.MoveNext())
                    {
                        if (cm.Current == null) continue;
                        if ((cm.Current.cmType == CMDropper.CountermeasureTypes.Flare)
                            || (cm.Current.cmType == CMDropper.CountermeasureTypes.Chaff)
                            || (cm.Current.cmType == CMDropper.CountermeasureTypes.Smoke))
                        {
                            cm.Current.DropCM();
                        }
                    }
                yield return new WaitForSeconds(1f);
            }
            isFlaring = false;
            isChaffing = false;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " ending All CM routine");
        }

        IEnumerator LegacyCMRoutine()
        {
            isLegacyCMing = true;
            yield return new WaitForSeconds(UnityEngine.Random.Range(.2f, 1f));
            if (incomingMissileDistance < 2500)
            {
                cmAmount = Mathf.RoundToInt((2500 - incomingMissileDistance) / 400);
                using (var cm = VesselModuleRegistry.GetModules<CMDropper>(vessel).GetEnumerator())
                    while (cm.MoveNext())
                    {
                        if (cm.Current == null) continue;
                        cm.Current.DropCM();
                    }
                cmCounter++;
                if (cmCounter < cmAmount)
                {
                    yield return new WaitForSeconds(0.15f);
                }
                else
                {
                    cmCounter = 0;
                    yield return new WaitForSeconds(UnityEngine.Random.Range(.5f, 1f));
                }
            }
            isLegacyCMing = false;
        }

        public void MissileWarning(float distance, MissileBase ml)//take distance parameter
        {
            if (vessel.isActiveVessel && !warningSounding)
            {
                StartCoroutine(WarningSoundRoutine(distance, ml));
            }

            //if (BDArmorySettings.DEBUG_LABELS && distance < 1000f) Debug.Log("[BDArmory.MissileFire]: Legacy missile warning for " + vessel.vesselName + " at distance " + distance.ToString("0.0") + "m from " + ml.shortName);
            //missileIsIncoming = true;
            //incomingMissileLastDetected = Time.time;
            //incomingMissileDistance = distance;
        }

        #endregion CounterMeasure

        #region Fire

        bool FireCurrentMissile(bool checkClearance)
        {
            MissileBase missile = CurrentMissile;
            if (missile == null) return false;
            bool DisengageAfterFiring = false;
            if (missile is MissileBase)
            {
                MissileBase ml = missile;
                if (checkClearance && (!CheckBombClearance(ml) || (ml is MissileLauncher && ((MissileLauncher)ml).rotaryRail && !((MissileLauncher)ml).rotaryRail.readyMissile == ml)))
                {
                    using (var otherMissile = VesselModuleRegistry.GetModules<MissileBase>(vessel).GetEnumerator())
                        while (otherMissile.MoveNext())
                        {
                            if (otherMissile.Current == null) continue;
                            if (otherMissile.Current == ml || otherMissile.Current.GetShortName() != ml.GetShortName() ||
                                !CheckBombClearance(otherMissile.Current)) continue;
                            CurrentMissile = otherMissile.Current;
                            selectedWeapon = otherMissile.Current;
                            FireCurrentMissile(false);
                            return true;
                        }
                    CurrentMissile = ml;
                    selectedWeapon = ml;
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire]: No Clearance! Cannot fire " + CurrentMissile.name);
                    return false;
                }
                if (ml is MissileLauncher && ((MissileLauncher)ml).missileTurret)
                {
                    ((MissileLauncher)ml).missileTurret.FireMissile(((MissileLauncher)ml));
                }
                else if (ml is MissileLauncher && ((MissileLauncher)ml).rotaryRail)
                {
                    ((MissileLauncher)ml).rotaryRail.FireMissile(((MissileLauncher)ml));
                }
                else if (ml is MissileLauncher && ((MissileLauncher)ml).deployableRail)
                {
                    ((MissileLauncher)ml).deployableRail.FireMissile(((MissileLauncher)ml));
                }
                else
                {
                    SendTargetDataToMissile(ml);
                    ml.FireMissile();
                    PreviousMissile = ml;
                }

                if (guardMode)
                {
                    if (ml.GetWeaponClass() == WeaponClasses.Bomb)
                    {
                        //StartCoroutine(BombsAwayRoutine(ml));
                    }
                    if (ml.warheadType == MissileBase.WarheadTypes.EMP || ml.warheadType == MissileBase.WarheadTypes.Nuke)
                    {
                        MissileLauncher cm = missile as MissileLauncher;
                        float thrust = cm == null ? 30 : cm.thrust;
                        float timeToImpact = AIUtils.ClosestTimeToCPA(guardTarget, vessel.CoM, vessel.Velocity(), (thrust / missile.part.mass) * missile.GetForwardTransform(), 16);
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire]: Blast standoff dist: " + ml.StandOffDistance + "; time2Impact: " + timeToImpact);
                        if (ml.StandOffDistance > 0 && Vector3.Distance(transform.position + (vessel.Velocity() * timeToImpact), currentTarget.position + currentTarget.velocity) > ml.StandOffDistance) //if predicted craft position will be within blast radius when missile arrives, break off
                        {
                            DisengageAfterFiring = true;
                            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire]: Need to withdraw from projected blast zone!");
                        }
                    }
                }
                else
                {
                    if (vesselRadarData && vesselRadarData.autoCycleLockOnFire)
                    {
                        vesselRadarData.CycleActiveLock();
                    }
                }
            }
            else
            {
                SendTargetDataToMissile(missile);
                missile.FireMissile();
                PreviousMissile = missile;
            }
            CalculateMissilesAway(); // Immediately update missiles away.
            //PreviousMissile = CurrentMissile;
            UpdateList();
            if (DisengageAfterFiring)
            {
                if (pilotAI)
                {
                    pilotAI.RequestExtend("Nuke away!", guardTarget, missile.StandOffDistance * 1.25f, guardTarget.CoM); // Extend from projected detonation site if within blast radius
                }
            }
            return true;
        }

        void FireMissile()
        {
            if (weaponIndex == 0)
            {
                return;
            }

            if (selectedWeapon == null)
            {
                return;
            }
            if (guardMode && (firedMissiles >= maxMissilesOnTarget))
            {
                return;
            }
            if (selectedWeapon.GetWeaponClass() == WeaponClasses.Missile ||
            selectedWeapon.GetWeaponClass() == WeaponClasses.SLW ||
            selectedWeapon.GetWeaponClass() == WeaponClasses.Bomb)
            {
                FireCurrentMissile(true);
            }
            UpdateList();
        }

        #endregion Fire

        #region Weapon Info

        void DisplaySelectedWeaponMessage()
        {
            if (BDArmorySetup.GAME_UI_ENABLED && vessel == FlightGlobals.ActiveVessel)
            {
                ScreenMessages.RemoveMessage(selectionMessage);
                selectionMessage.textInstance = null;

                selectionText = "Selected Weapon: " + (GetWeaponName(weaponArray[weaponIndex])).ToString();
                selectionMessage.message = selectionText;
                selectionMessage.style = ScreenMessageStyle.UPPER_CENTER;

                ScreenMessages.PostScreenMessage(selectionMessage);
            }
        }

        string GetWeaponName(IBDWeapon weapon)
        {
            if (weapon == null)
            {
                return "None";
            }
            else
            {
                return weapon.GetShortName();
            }
        }

        public void UpdateList()
        {
            weaponTypes.Clear();
            // extension for feature_engagementenvelope: also clear engagement specific weapon lists
            weaponTypesAir.Clear();
            weaponTypesMissile.Clear();
            targetMissiles = false;
            weaponTypesGround.Clear();
            weaponTypesSLW.Clear();
            gunRippleIndex.Clear();
            hasAntiRadiationOrdinance = false;
            if (vessel == null || !vessel.loaded) return;

            using (var weapon = VesselModuleRegistry.GetModules<IBDWeapon>(vessel).GetEnumerator())
                while (weapon.MoveNext())
                {
                    if (weapon.Current == null) continue;
                    string weaponName = weapon.Current.GetShortName();
                    bool alreadyAdded = false;
                    using (List<IBDWeapon>.Enumerator weap = weaponTypes.GetEnumerator())
                        while (weap.MoveNext())
                        {
                            if (weap.Current == null) continue;
                            if (weap.Current.GetShortName() == weaponName)
                            {
                                alreadyAdded = true;
                                //break;
                            }
                        }

                    //dont add empty rocket pods
                    if (weapon.Current.GetWeaponClass() == WeaponClasses.Rocket &&
                    (weapon.Current.GetPart().FindModuleImplementing<ModuleWeapon>().rocketPod && !weapon.Current.GetPart().FindModuleImplementing<ModuleWeapon>().externalAmmo) &&
                    weapon.Current.GetPart().FindModuleImplementing<ModuleWeapon>().GetRocketResource().amount < 1
                    && !BDArmorySettings.INFINITE_AMMO)
                    {
                        continue;
                    }
                    //dont add APS
                    if ((weapon.Current.GetWeaponClass() == WeaponClasses.Gun || weapon.Current.GetWeaponClass() == WeaponClasses.Rocket || weapon.Current.GetWeaponClass() == WeaponClasses.DefenseLaser) &&
                        weapon.Current.GetPart().FindModuleImplementing<ModuleWeapon>().isAPS)
                    {
                        continue;
                    }
                    if (!alreadyAdded)
                    {
                        weaponTypes.Add(weapon.Current);
                    }
                    if (weapon.Current.GetWeaponClass() == WeaponClasses.Gun || weapon.Current.GetWeaponClass() == WeaponClasses.Rocket || weapon.Current.GetWeaponClass() == WeaponClasses.DefenseLaser)
                    {
                        if (!gunRippleIndex.ContainsKey(weapon.Current.GetPart().partInfo.name))
                            gunRippleIndex.Add(weapon.Current.GetPart().partInfo.name, 0);
                    }

                    EngageableWeapon engageableWeapon = weapon.Current as EngageableWeapon;

                    if (engageableWeapon != null)
                    {
                        if (engageableWeapon.GetEngageAirTargets()) weaponTypesAir.Add(weapon.Current);
                        if (engageableWeapon.GetEngageMissileTargets()) weaponTypesMissile.Add(weapon.Current); targetMissiles = true;
                        if (engageableWeapon.GetEngageGroundTargets()) weaponTypesGround.Add(weapon.Current);
                        if (engageableWeapon.GetEngageSLWTargets()) weaponTypesSLW.Add(weapon.Current);
                    }
                    else
                    {
                        weaponTypesAir.Add(weapon.Current);
                        weaponTypesMissile.Add(weapon.Current);
                        weaponTypesGround.Add(weapon.Current);
                        weaponTypesSLW.Add(weapon.Current);
                    }

                    if (weapon.Current.GetWeaponClass() == WeaponClasses.Bomb ||
                    weapon.Current.GetWeaponClass() == WeaponClasses.Missile ||
                    weapon.Current.GetWeaponClass() == WeaponClasses.SLW)
                    {
                        MissileLauncher ml = weapon.Current.GetPart().FindModuleImplementing<MissileLauncher>();
                        BDModularGuidance mmg = weapon.Current.GetPart().FindModuleImplementing<BDModularGuidance>();
                        weapon.Current.GetPart().FindModuleImplementing<MissileBase>().GetMissileCount(); // #191, Do it this way so the GetMissileCount only updates when missile fired

                        if ((ml is not null && ml.TargetingMode == MissileBase.TargetingModes.AntiRad) || (mmg is not null && mmg.TargetingMode == MissileBase.TargetingModes.AntiRad))
                        {
                            hasAntiRadiationOrdinance = true;
                            antiradTargets = OtherUtils.ParseToFloatArray(ml != null ? ml.antiradTargetTypes : "0,5"); //limited Antirad options for MMG
                        }
                    }
                }

            //weaponTypes.Sort();
            weaponTypes = weaponTypes.OrderBy(w => w.GetShortName()).ToList();

            List<IBDWeapon> tempList = new List<IBDWeapon> { null };
            tempList.AddRange(weaponTypes);

            weaponArray = tempList.ToArray();

            if (weaponIndex >= weaponArray.Length)
            {
                hasSingleFired = true;
                triggerTimer = 0;
            }
            PrepareWeapons();
        }

        private void PrepareWeapons()
        {
            if (vessel == null) return;

            weaponIndex = Mathf.Clamp(weaponIndex, 0, weaponArray.Length - 1);

            if (selectedWeapon == null || selectedWeapon.GetPart() == null || (selectedWeapon.GetPart().vessel != null && selectedWeapon.GetPart().vessel != vessel) ||
                GetWeaponName(selectedWeapon) != GetWeaponName(weaponArray[weaponIndex]))
            {
                selectedWeapon = weaponArray[weaponIndex];

                if (vessel.isActiveVessel && Time.time - startTime > 1)
                {
                    hasSingleFired = true;
                }

                if (vessel.isActiveVessel && weaponIndex != 0)
                {
                    SetDeployableWeapons();
                    DisplaySelectedWeaponMessage();
                }
            }

            if (weaponIndex == 0)
            {
                selectedWeapon = null;
                hasSingleFired = true;
            }

            MissileBase aMl = GetAsymMissile();
            if (aMl)
            {
                selectedWeapon = aMl;
            }

            MissileBase rMl = GetRotaryReadyMissile();
            if (rMl)
            {
                selectedWeapon = rMl;
            }

            UpdateSelectedWeaponState();
        }

        private void UpdateSelectedWeaponState()
        {
            if (vessel == null) return;

            MissileBase aMl = GetAsymMissile();
            if (aMl)
            {
                CurrentMissile = aMl;
            }

            MissileBase rMl = GetRotaryReadyMissile();
            if (rMl)
            {
                CurrentMissile = rMl;
            }

            if (selectedWeapon != null && (selectedWeapon.GetWeaponClass() == WeaponClasses.Bomb || selectedWeapon.GetWeaponClass() == WeaponClasses.Missile || selectedWeapon.GetWeaponClass() == WeaponClasses.SLW))
            {
                //Debug.Log("[BDArmory.MissileFire]: =====selected weapon: " + selectedWeapon.GetPart().name);
                if (!CurrentMissile || CurrentMissile.part.name != selectedWeapon.GetPart().name)
                {
                    CurrentMissile = selectedWeapon.GetPart().FindModuleImplementing<MissileBase>();
                }
            }
            else
            {
                CurrentMissile = null;
            }

            //selectedWeapon = weaponArray[weaponIndex];

            //bomb stuff
            if (selectedWeapon != null && selectedWeapon.GetWeaponClass() == WeaponClasses.Bomb)
            {
                bombPart = selectedWeapon.GetPart();
            }
            else
            {
                bombPart = null;
            }

            //gun ripple stuff
            if (selectedWeapon != null && (selectedWeapon.GetWeaponClass() == WeaponClasses.Gun || selectedWeapon.GetWeaponClass() == WeaponClasses.Rocket || selectedWeapon.GetWeaponClass() == WeaponClasses.DefenseLaser))
            //&& currentGun.useRippleFire) //currentGun.roundsPerMinute < 1500)
            {
                float counter = 0; // Used to get a count of the ripple weapons.  a float version of rippleGunCount.
                //gunRippleIndex.Clear();
                // This value will be incremented as we set the ripple weapons
                rippleGunCount.Clear();
                float weaponRpm = 0;  // used to set the rippleGunRPM

                // JDK:  this looks like it can be greatly simplified...

                #region Old Code (for reference.  remove when satisfied new code works as expected.

                //List<ModuleWeapon> tempListModuleWeapon = vessel.FindPartModulesImplementing<ModuleWeapon>();
                //foreach (ModuleWeapon weapon in tempListModuleWeapon)
                //{
                //    if (selectedWeapon.GetShortName() == weapon.GetShortName())
                //    {
                //        weapon.rippleIndex = Mathf.RoundToInt(counter);
                //        weaponRPM = weapon.roundsPerMinute;
                //        ++counter;
                //        rippleGunCount++;
                //    }
                //}
                //gunRippleRpm = weaponRPM * counter;
                //float timeDelayPerGun = 60f / (weaponRPM * counter);
                ////number of seconds between each gun firing; will reduce with increasing RPM or number of guns
                //foreach (ModuleWeapon weapon in tempListModuleWeapon)
                //{
                //    if (selectedWeapon.GetShortName() == weapon.GetShortName())
                //    {
                //        weapon.initialFireDelay = timeDelayPerGun; //set the time delay for moving to next index
                //    }
                //}

                //RippleOption ro; //ripplesetup and stuff
                //if (rippleDictionary.ContainsKey(selectedWeapon.GetShortName()))
                //{
                //    ro = rippleDictionary[selectedWeapon.GetShortName()];
                //}
                //else
                //{
                //    ro = new RippleOption(currentGun.useRippleFire, 650); //take from gun's persistant value
                //    rippleDictionary.Add(selectedWeapon.GetShortName(), ro);
                //}

                //foreach (ModuleWeapon w in vessel.FindPartModulesImplementing<ModuleWeapon>())
                //{
                //    if (w.GetShortName() == selectedWeapon.GetShortName())
                //        w.useRippleFire = ro.rippleFire;
                //}

                #endregion Old Code (for reference.  remove when satisfied new code works as expected.

                // TODO:  JDK verify new code works as expected.
                // New code, simplified.

                //First lest set the Ripple Option. Doing it first eliminates a loop.
                RippleOption ro; //ripplesetup and stuff
                if (rippleDictionary.ContainsKey(selectedWeapon.GetShortName()))
                {
                    ro = rippleDictionary[selectedWeapon.GetShortName()];
                }
                else
                {
                    ro = new RippleOption(currentGun.useRippleFire, 650); //take from gun's persistant value
                    rippleDictionary.Add(selectedWeapon.GetShortName(), ro);
                }

                //Get ripple weapon count, so we don't have to enumerate the whole list again.
                List<ModuleWeapon> rippleWeapons = new List<ModuleWeapon>();
                using (var weapCnt = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                    while (weapCnt.MoveNext())
                    {
                        if (weapCnt.Current == null) continue;
                        if (selectedWeapon.GetShortName() != weapCnt.Current.GetShortName()) continue;
                        if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                        {
                            weaponRpm = BDArmorySettings.FIRE_RATE_OVERRIDE;
                        }
                        else
                        {
                            if (!weapCnt.Current.BurstFire)
                            {
                                weaponRpm = weapCnt.Current.roundsPerMinute;
                            }
                            else
                            {
                                weaponRpm = 60 / weapCnt.Current.ReloadTime;
                            }
                        }
                        rippleWeapons.Add(weapCnt.Current);
                        counter += weaponRpm; // grab sum of weapons rpm
                    }
                gunRippleRpm = counter;

                //ripple for non-homogeneous groups needs to be setup per guntype, else a slow cannon will have the same firedelay as a fast MG
                using (List<ModuleWeapon>.Enumerator weapon = rippleWeapons.GetEnumerator())
                    while (weapon.MoveNext())
                    {
                        int GunCount = 0;
                        if (weapon.Current == null) continue;
                        weapon.Current.useRippleFire = ro.rippleFire;
                        if (!rippleGunCount.ContainsKey(weapon.Current.WeaponName)) //don't setup copies of a guntype if we've already done that
                        {
                            for (int w = 0; w < rippleWeapons.Count; w++)
                            {
                                if (weapon.Current.WeaponName == rippleWeapons[w].WeaponName)
                                {
                                    rippleWeapons[w].rippleIndex = GunCount; //this will mean that a group of two+ different RPM guns will start firing at the same time, then each subgroup will independantly ripple
                                    GunCount++;
                                }
                            }
                            rippleGunCount.Add(weapon.Current.WeaponName, GunCount);
                        }
                        weapon.Current.initialFireDelay = 60 / (weapon.Current.roundsPerMinute * (rippleGunCount[weapon.Current.WeaponName]));
                        // Debug.Log("[RIPPLEDEBUG]" + weapon.Current.WeaponName + " rippleIndex: " + weapon.Current.rippleIndex + "; initialfiredelay: " + weapon.Current.initialFireDelay);
                    }
            }

            ToggleTurret();
            SetMissileTurrets();
            SetDeployableRails();
            SetRotaryRails();
        }

        private HashSet<uint> baysOpened = new HashSet<uint>();
        private bool SetCargoBays()
        {
            if (!guardMode) return false;
            bool openingBays = false;

            if (weaponIndex > 0 && CurrentMissile && guardTarget)
            {
                if (CurrentMissile.part.ShieldedFromAirstream)
                {
                    using (var ml = VesselModuleRegistry.GetModules<MissileBase>(vessel).GetEnumerator())
                        while (ml.MoveNext())
                        {
                            if (ml.Current == null) continue;
                            if (ml.Current.part.ShieldedFromAirstream) ml.Current.inCargoBay = true;
                        }
                }

                if (uint.Parse(CurrentMissile.customBayGroup) > 0) // Missile uses a custom bay, open it to fire
                {
                    uint customBayGroup = uint.Parse(CurrentMissile.customBayGroup);
                    if (!baysOpened.Contains(customBayGroup)) // We haven't opened this bay yet
                    {
                        vessel.ActionGroups.ToggleGroup(BDACompetitionMode.KM_dictAG[(int)customBayGroup]);
                        openingBays = true;
                        baysOpened.Add(customBayGroup);
                    }
                    else
                    {
                        foreach (var bay in baysOpened.Where(e => e <= 16).ToList()) // Close other custom bays that might be open 
                        {
                            if (bay != customBayGroup)
                            {
                                vessel.ActionGroups.ToggleGroup(BDACompetitionMode.KM_dictAG[(int)bay]);
                                baysOpened.Remove(bay); // Bay is no longer open
                            }
                        }
                    }
                }
                else if (CurrentMissile.inCargoBay)
                {
                    using (var bay = VesselModuleRegistry.GetModules<ModuleCargoBay>(vessel).GetEnumerator())
                        while (bay.MoveNext())
                        {
                            if (bay.Current == null) continue;
                            if (CurrentMissile.part.airstreamShields.Contains(bay.Current))
                            {
                                ModuleAnimateGeneric anim = bay.Current.part.Modules.GetModule(bay.Current.DeployModuleIndex) as ModuleAnimateGeneric;
                                if (anim == null) continue;

                                string toggleOption = anim.Events["Toggle"].guiName;
                                if (toggleOption == "Open")
                                {
                                    if (anim)
                                    {
                                        anim.Toggle();
                                        openingBays = true;
                                        baysOpened.Add(bay.Current.GetPersistentId());
                                    }
                                }
                            }
                            else
                            {
                                if (!baysOpened.Contains(bay.Current.GetPersistentId())) continue; // Only close bays we've opened.
                                ModuleAnimateGeneric anim = bay.Current.part.Modules.GetModule(bay.Current.DeployModuleIndex) as ModuleAnimateGeneric;
                                if (anim == null) continue;

                                string toggleOption = anim.Events["Toggle"].guiName;
                                if (toggleOption == "Close")
                                {
                                    if (anim)
                                    {
                                        anim.Toggle();
                                    }
                                }
                            }
                        }
                }
                else
                {
                    using (var bay = VesselModuleRegistry.GetModules<ModuleCargoBay>(vessel).GetEnumerator()) // Close normal bays
                        while (bay.MoveNext())
                        {
                            if (bay.Current == null) continue;
                            if (!baysOpened.Contains(bay.Current.GetPersistentId())) continue; // Only close bays we've opened.
                            ModuleAnimateGeneric anim = bay.Current.part.Modules.GetModule(bay.Current.DeployModuleIndex) as ModuleAnimateGeneric;
                            if (anim == null) continue;

                            string toggleOption = anim.Events["Toggle"].guiName;
                            if (toggleOption == "Close")
                            {
                                if (anim)
                                {
                                    anim.Toggle();
                                }
                            }
                        }

                    foreach (var bay in baysOpened.Where(e => e <= 16).ToList()) // Close custom bays
                    {
                        vessel.ActionGroups.ToggleGroup(BDACompetitionMode.KM_dictAG[(int)bay]);
                        baysOpened.Remove(bay); // Bay is no longer open
                    }
                }
            }
            else
            {
                using (var bay = VesselModuleRegistry.GetModules<ModuleCargoBay>(vessel).GetEnumerator()) // Close normal bays
                    while (bay.MoveNext())
                    {
                        if (bay.Current == null) continue;
                        if (!baysOpened.Contains(bay.Current.GetPersistentId())) continue; // Only close bays we've opened.
                        ModuleAnimateGeneric anim = bay.Current.part.Modules.GetModule(bay.Current.DeployModuleIndex) as ModuleAnimateGeneric;
                        if (anim == null) continue;

                        string toggleOption = anim.Events["Toggle"].guiName;
                        if (toggleOption == "Close")
                        {
                            if (anim)
                            {
                                anim.Toggle();
                            }
                        }
                    }

                foreach (var bay in baysOpened.Where(e => e <= 16).ToList()) // Close custom bays
                {
                    vessel.ActionGroups.ToggleGroup(BDACompetitionMode.KM_dictAG[(int)bay]);
                    baysOpened.Remove(bay); // Bay is no longer open
                }
            }

            return openingBays;
        }

        private HashSet<uint> wepsDeployed = new HashSet<uint>();
        private bool SetDeployableWeapons()
        {
            bool deployingWeapon = false;

            if (weaponIndex > 0 && currentGun)
            {
                if (uint.Parse(currentGun.deployWepGroup) > 0) // Weapon uses a deploy action group, activate it to fire
                {
                    uint deployWepGroup = uint.Parse(currentGun.deployWepGroup);
                    if (!wepsDeployed.Contains(deployWepGroup)) // We haven't deployed this weapon yet
                    {
                        vessel.ActionGroups.ToggleGroup(BDACompetitionMode.KM_dictAG[(int)deployWepGroup]);
                        deployingWeapon = true;
                        wepsDeployed.Add(deployWepGroup);
                    }
                    else
                    {
                        foreach (var wep in wepsDeployed.Where(e => e <= 16).ToList()) // Store other Weapons that might be deployed 
                        {
                            if (wep != deployWepGroup)
                            {
                                vessel.ActionGroups.ToggleGroup(BDACompetitionMode.KM_dictAG[(int)wep]);
                                wepsDeployed.Remove(wep); // Weapon is no longer deployed
                            }
                        }
                    }
                }
                else
                {
                    foreach (var wep in wepsDeployed.Where(e => e <= 16).ToList()) // Store weapons
                    {
                        vessel.ActionGroups.ToggleGroup(BDACompetitionMode.KM_dictAG[(int)wep]);
                        wepsDeployed.Remove(wep); // Weapon is no longer deployed
                    }
                }
            }
            else
            {
                foreach (var wep in wepsDeployed.Where(e => e <= 16).ToList()) // Store weapons
                {
                    vessel.ActionGroups.ToggleGroup(BDACompetitionMode.KM_dictAG[(int)wep]);
                    wepsDeployed.Remove(wep); // Weapon is no longer deployed
                }
            }

            return deployingWeapon;
        }

        void SetRotaryRails()
        {
            if (weaponIndex == 0) return;

            if (selectedWeapon == null) return;

            if (
                !(selectedWeapon.GetWeaponClass() == WeaponClasses.Missile ||
                  selectedWeapon.GetWeaponClass() == WeaponClasses.Bomb ||
                  selectedWeapon.GetWeaponClass() == WeaponClasses.SLW)) return;

            if (!CurrentMissile) return;

            //TODO BDModularGuidance: Rotatory Rail?
            MissileLauncher cm = CurrentMissile as MissileLauncher;
            if (cm == null) return;
            using (var rotRail = VesselModuleRegistry.GetModules<BDRotaryRail>(vessel).GetEnumerator())
                while (rotRail.MoveNext())
                {
                    if (rotRail.Current == null) continue;
                    if (rotRail.Current.missileCount == 0)
                    {
                        //Debug.Log("[BDArmory.MissileFire]: SetRotaryRails(): rail has no missiles");
                        continue;
                    }

                    //Debug.Log("[BDArmory.MissileFire]: SetRotaryRails(): rotRail.Current.readyToFire: " + rotRail.Current.readyToFire + ", rotRail.Current.readyMissile: " + ((rotRail.Current.readyMissile != null) ? rotRail.Current.readyMissile.part.name : "null") + ", rotRail.Current.nextMissile: " + ((rotRail.Current.nextMissile != null) ? rotRail.Current.nextMissile.part.name : "null"));

                    //Debug.Log("[BDArmory.MissileFire]: current missile: " + cm.part.name);

                    if (rotRail.Current.readyToFire)
                    {
                        if (!rotRail.Current.readyMissile)
                        {
                            rotRail.Current.RotateToMissile(cm);
                            return;
                        }

                        if (rotRail.Current.readyMissile.part.name != cm.part.name)
                        {
                            rotRail.Current.RotateToMissile(cm);
                        }
                    }
                    else
                    {
                        if (!rotRail.Current.nextMissile)
                        {
                            rotRail.Current.RotateToMissile(cm);
                        }
                        else if (rotRail.Current.nextMissile.part.name != cm.part.name)
                        {
                            rotRail.Current.RotateToMissile(cm);
                        }
                    }
                }
        }

        void SetMissileTurrets()
        {
            MissileLauncher cm = CurrentMissile as MissileLauncher;
            using (var mt = VesselModuleRegistry.GetModules<MissileTurret>(vessel).GetEnumerator())
                while (mt.MoveNext())
                {
                    if (mt.Current == null) continue;
                    if (!mt.Current.isActiveAndEnabled) continue;
                    if (weaponIndex > 0 && cm && mt.Current.ContainsMissileOfType(cm) && (!mt.Current.activeMissileOnly || cm.missileTurret == mt.Current))
                    {
                        mt.Current.EnableTurret();
                    }
                    else
                    {
                        mt.Current.DisableTurret();
                    }
                }
        }
        void SetDeployableRails()
        {
            MissileLauncher cm = CurrentMissile as MissileLauncher;
            using (var mt = VesselModuleRegistry.GetModules<BDDeployableRail>(vessel).GetEnumerator())
                while (mt.MoveNext())
                {
                    if (mt.Current == null) continue;
                    if (!mt.Current.isActiveAndEnabled) continue;
                    if (weaponIndex > 0 && cm && mt.Current.ContainsMissileOfType(cm) && cm.deployableRail == mt.Current)
                    {
                        mt.Current.EnableRail();
                    }
                    else
                    {
                        mt.Current.DisableRail();
                    }
                }
        }

        public void CycleWeapon(bool forward)
        {
            if (forward) weaponIndex++;
            else weaponIndex--;
            weaponIndex = (int)Mathf.Repeat(weaponIndex, weaponArray.Length);

            hasSingleFired = true;
            triggerTimer = 0;

            UpdateList();
            SetDeployableWeapons();
            DisplaySelectedWeaponMessage();

            if (vessel.isActiveVessel && !guardMode)
            {
                audioSource.PlayOneShot(clickSound);
            }
        }

        public void CycleWeapon(int index)
        {
            if (index >= weaponArray.Length)
            {
                index = 0;
            }
            weaponIndex = index;

            UpdateList();

            if (vessel.isActiveVessel && !guardMode)
            {
                audioSource.PlayOneShot(clickSound);
                SetDeployableWeapons();
                DisplaySelectedWeaponMessage();
            }
        }

        public Part FindSym(Part p)
        {
            using (List<Part>.Enumerator pSym = p.symmetryCounterparts.GetEnumerator())
                while (pSym.MoveNext())
                {
                    if (pSym.Current == null) continue;
                    if (pSym.Current != p && pSym.Current.vessel == vessel)
                    {
                        return pSym.Current;
                    }
                }

            return null;
        }

        private MissileBase GetAsymMissile()
        {
            if (weaponIndex == 0) return null;
            if (weaponArray[weaponIndex].GetWeaponClass() == WeaponClasses.Bomb ||
                weaponArray[weaponIndex].GetWeaponClass() == WeaponClasses.Missile ||
                weaponArray[weaponIndex].GetWeaponClass() == WeaponClasses.SLW)
            {
                MissileBase firstMl = null;
                using (var ml = VesselModuleRegistry.GetModules<MissileBase>(vessel).GetEnumerator())
                    while (ml.MoveNext())
                    {
                        if (ml.Current == null) continue;
                        MissileLauncher launcher = ml.Current as MissileLauncher;
                        if (launcher != null)
                        {
                            if (weaponArray[weaponIndex].GetPart() == null || launcher.part.name != weaponArray[weaponIndex].GetPart().name) continue;
                        }
                        else
                        {
                            BDModularGuidance guidance = ml.Current as BDModularGuidance;
                            if (guidance != null)
                            { //We have set of parts not only a part
                                if (guidance.GetShortName() != weaponArray[weaponIndex]?.GetShortName()) continue;
                            }
                        }
                        if (firstMl == null) firstMl = ml.Current;

                        if (!FindSym(ml.Current.part))
                        {
                            return ml.Current;
                        }
                    }
                return firstMl;
            }
            return null;
        }

        private MissileBase GetRotaryReadyMissile()
        {
            if (weaponIndex == 0) return null;
            if (weaponArray[weaponIndex].GetWeaponClass() == WeaponClasses.Bomb ||
                weaponArray[weaponIndex].GetWeaponClass() == WeaponClasses.Missile ||
                weaponArray[weaponIndex].GetWeaponClass() == WeaponClasses.SLW)
            {
                //TODO BDModularGuidance, ModuleDrone: Implemente rotaryRail support
                MissileLauncher missile = CurrentMissile as MissileLauncher;
                if (missile == null) return null;
                if (weaponArray[weaponIndex].GetPart() != null && missile.part.name == weaponArray[weaponIndex].GetPart().name)
                {
                    if (!missile.rotaryRail)
                    {
                        return missile;
                    }
                    if (missile.rotaryRail.readyToFire && missile.rotaryRail.readyMissile == CurrentMissile)
                    {
                        return missile;
                    }
                }
                using (var ml = VesselModuleRegistry.GetModules<MissileLauncher>(vessel).GetEnumerator())
                    while (ml.MoveNext())
                    {
                        if (ml.Current == null) continue;
                        if (weaponArray[weaponIndex].GetPart() == null || ml.Current.part.name != weaponArray[weaponIndex].GetPart().name) continue;

                        if (!ml.Current.rotaryRail)
                        {
                            return ml.Current;
                        }
                        if (ml.Current.rotaryRail.readyMissile == null || ml.Current.rotaryRail.readyMissile.part == null) continue;
                        if (ml.Current.rotaryRail.readyToFire && ml.Current.rotaryRail.readyMissile.part.name == weaponArray[weaponIndex].GetPart().name)
                        {
                            return ml.Current.rotaryRail.readyMissile;
                        }
                    }
                return null;
            }
            return null;
        }

        bool CheckBombClearance(MissileBase ml)
        {
            if (!BDArmorySettings.BOMB_CLEARANCE_CHECK) return true;

            if (ml.part.ShieldedFromAirstream)
            {
                return false;
            }

            //TODO BDModularGuidance: Bombs and turrents
            MissileLauncher launcher = ml as MissileLauncher;
            if (launcher != null)
            {
                if (launcher.rotaryRail && launcher.rotaryRail.readyMissile != ml)
                {
                    return false;
                }

                if (launcher.missileTurret && !launcher.missileTurret.turretEnabled)
                {
                    return false;
                }

                const int layerMask = (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.Unknown19 | LayerMasks.Wheels);
                if (ml.dropTime >= 0.1f)
                {
                    //debug lines
                    if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DRAW_AIMERS)
                    {
                        lr = GetComponent<LineRenderer>();
                        if (!lr) { lr = gameObject.AddComponent<LineRenderer>(); }
                        lr.enabled = true;
                        lr.startWidth = .1f;
                        lr.endWidth = .1f;
                    }

                    float radius = launcher.decoupleForward ? launcher.ClearanceRadius : launcher.ClearanceLength;
                    float time = Mathf.Min(ml.dropTime, 2f);
                    Vector3 direction = ((launcher.decoupleForward
                        ? ml.MissileReferenceTransform.transform.forward
                        : -ml.MissileReferenceTransform.transform.up) * launcher.decoupleSpeed * time) +
                                        ((FlightGlobals.getGeeForceAtPosition(transform.position) - vessel.acceleration) *
                                         0.5f * time * time);
                    Vector3 crossAxis = Vector3.Cross(direction, ml.MissileReferenceTransform.transform.right).normalized;

                    float rayDistance;
                    if (launcher.thrust == 0 || launcher.cruiseThrust == 0)
                    {
                        rayDistance = 8;
                    }
                    else
                    {
                        //distance till engine starts based on grav accel and vessel accel
                        rayDistance = direction.magnitude;
                    }

                    Ray[] rays =
                    {
                        new Ray(ml.MissileReferenceTransform.position - (radius*crossAxis), direction),
                        new Ray(ml.MissileReferenceTransform.position + (radius*crossAxis), direction),
                        new Ray(ml.MissileReferenceTransform.position, direction)
                    };

                    if (lr != null && lr.enabled)
                    {
                        lr.useWorldSpace = false;
                        lr.positionCount = 4;
                        lr.SetPosition(0, transform.InverseTransformPoint(rays[0].origin));
                        lr.SetPosition(1, transform.InverseTransformPoint(rays[0].GetPoint(rayDistance)));
                        lr.SetPosition(2, transform.InverseTransformPoint(rays[1].GetPoint(rayDistance)));
                        lr.SetPosition(3, transform.InverseTransformPoint(rays[1].origin));
                    }

                    using (IEnumerator<Ray> rt = rays.AsEnumerable().GetEnumerator())
                        while (rt.MoveNext())
                        {
                            RaycastHit[] hits = Physics.RaycastAll(rt.Current, rayDistance, layerMask);
                            using (IEnumerator<RaycastHit> t1 = hits.AsEnumerable().GetEnumerator())
                                while (t1.MoveNext())
                                {
                                    Part p = t1.Current.collider.GetComponentInParent<Part>();

                                    if ((p == null || p == ml.part) && p != null) continue;
                                    if (BDArmorySettings.DEBUG_MISSILES)
                                        Debug.Log("[BDArmory.MissileFire]: RAYCAST HIT, clearance is FALSE! part=" + (p != null ? p.name : null) + ", collider=" + (p != null ? p.collider : null));
                                    return false;
                                }
                        }
                    return true;
                }

                //forward check for no-drop missiles
                RaycastHit[] hitparts = Physics.RaycastAll(new Ray(ml.MissileReferenceTransform.position, ml.GetForwardTransform()), 50, layerMask);
                using (IEnumerator<RaycastHit> t = hitparts.AsEnumerable().GetEnumerator())
                    while (t.MoveNext())
                    {
                        Part p = t.Current.collider.GetComponentInParent<Part>();
                        if ((p == null || p == ml.part) && p != null) continue;
                        if (BDArmorySettings.DEBUG_MISSILES)
                            Debug.Log("[BDArmory.MissileFire]: RAYCAST HIT, clearance is FALSE! part=" + (p != null ? p.name : null) + ", collider=" + (p != null ? p.collider : null));
                        return false;
                    }
            }
            return true;
        }

        void RefreshModules()
        {
            VesselModuleRegistry.OnVesselModified(vessel); // Make sure the registry is up-to-date.
            radars = VesselModuleRegistry.GetModules<ModuleRadar>(vessel);
            if (radars != null)
            {
                // DISABLE RADARS
                /*
                List<ModuleRadar>.Enumerator rad = radars.GetEnumerator();
                while (rad.MoveNext())
                {
                    if (rad.Current == null) continue;
                    rad.Current.EnsureVesselRadarData();
                    if (rad.Current.radarEnabled) rad.Current.EnableRadar();
                }
                rad.Dispose();
                */
                using (List<ModuleRadar>.Enumerator rd = radars.GetEnumerator())
                    while (rd.MoveNext())
                    {
                        if (rd.Current != null || rd.Current.canLock)
                        {
                            if (rd.Current.maxLocks > MaxradarLocks) MaxradarLocks = rd.Current.maxLocks;
                        }
                    }
            }
            irsts = VesselModuleRegistry.GetModules<ModuleIRST>(vessel);
            jammers = VesselModuleRegistry.GetModules<ModuleECMJammer>(vessel);
            cloaks = VesselModuleRegistry.GetModules<ModuleCloakingDevice>(vessel);
            targetingPods = VesselModuleRegistry.GetModules<ModuleTargetingCamera>(vessel);
            wmModules = VesselModuleRegistry.GetModules<IBDWMModule>(vessel);
        }

        #endregion Weapon Info

        #region Targeting

        #region Smart Targeting

        void SmartFindTarget()
        {
            var lastTarget = currentTarget;
            List<TargetInfo> targetsTried = new List<TargetInfo>();
            string targetDebugText = "";
            targetsAssigned.Clear(); //fixes fixed guns not firing if Multitargeting >1
            missilesAssigned.Clear();
            if (multiMissileTgtNum > 1 && BDATargetManager.TargetList(Team).Count > 1)
            {
                if (CurrentMissile || PreviousMissile)  //if there are multiple potential targets, see how many can be fired at with missiles
                {
                    if (firedMissiles >= maxMissilesOnTarget)
                    {
                        if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire]: max missiles on target; switching to new target!");
                        if (Vector3.Distance(transform.position + vessel.Velocity(), currentTarget.position + currentTarget.velocity) < gunRange * 0.75f) //don't swap away from current target if about to enter gunrange
                        {
                            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire]: max targets fired on, but about to enter Gun range; keeping current target");
                            return;
                        }
                        if (PreviousMissile)
                        {
                            if (PreviousMissile.TargetingMode == MissileBase.TargetingModes.Laser) //don't switch from current target if using LASMs to keep current target painted
                            {
                                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire]: max targets fired on with LASMs, keeping target painted!");
                                if (currentTarget != null) return; //don't paint a destroyed target
                            }
                            if (!(PreviousMissile.TargetingMode == MissileBase.TargetingModes.Radar && !PreviousMissile.radarLOAL))
                            {
                                //if (vesselRadarData != null) vesselRadarData.UnlockCurrentTarget();//unlock current target only if missile isn't slaved to ship radar guidance to allow new F&F lock
                                //enabling this has the radar blip off after firing missile, having it on requires waiting 2 sec for the radar do decide it needs to swap to another target, but will continue to guide current missile (assuming sufficient radar FOV)
                            }
                        }
                        heatTarget = TargetSignatureData.noTarget; //clear holdover targets when switching targets
                        antiRadiationTarget = Vector3.zero;
                    }
                }
                using (List<TargetInfo>.Enumerator target = BDATargetManager.TargetList(Team).GetEnumerator())
                {
                    while (target.MoveNext())
                    {
                        if (missilesAway.ContainsKey(target.Current))
                        {
                            if (missilesAway[target.Current] >= maxMissilesOnTarget)
                            {
                                targetsAssigned.Add(target.Current);
                                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire]: Adding " + target.Current.Vessel.GetName() + " to exclusion list; length: " + targetsAssigned.Count);
                            }
                        }
                    }
                }
                if (targetsAssigned.Count == BDATargetManager.TargetList(Team).Count) //oops, already fired missiles at all available targets
                {
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire]: max targets fired on, resetting target list!");
                    targetsAssigned.Clear(); //clear targets tried, so AI can track best current target until such time as it can fire again
                }
            }

            if (overrideTarget) //begin by checking the override target, since that takes priority
            {
                targetsTried.Add(overrideTarget);
                SetTarget(overrideTarget);
                if (SmartPickWeapon_EngagementEnvelope(overrideTarget))
                {
                    if (BDArmorySettings.DEBUG_AI)
                    {
                        Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " is engaging an override target with " + selectedWeapon);
                    }
                    overrideTimer = 15f;
                    return;
                }
                else if (BDArmorySettings.DEBUG_AI)
                {
                    Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " is engaging an override target with failed to engage its override target!");
                }
            }
            overrideTarget = null; //null the override target if it cannot be used

            TargetInfo potentialTarget = null;
            //=========HIGH PRIORITY MISSILES=============
            //first engage any missiles targeting this vessel
            if (targetMissiles)
            {
                potentialTarget = BDATargetManager.GetMissileTarget(this, true);
                if (potentialTarget)
                {
                    targetsTried.Add(potentialTarget);
                    SetTarget(potentialTarget);
                    if (SmartPickWeapon_EngagementEnvelope(potentialTarget))
                    {
                        if (BDArmorySettings.DEBUG_AI)
                        {
                            Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " is engaging incoming missile (" + potentialTarget.Vessel.vesselName + ") with " + selectedWeapon);
                        }
                        return;
                    }
                }

                //then engage any missiles that are not engaged
                potentialTarget = BDATargetManager.GetUnengagedMissileTarget(this);
                if (potentialTarget)
                {
                    targetsTried.Add(potentialTarget);
                    SetTarget(potentialTarget);
                    if (SmartPickWeapon_EngagementEnvelope(potentialTarget))
                    {
                        if (BDArmorySettings.DEBUG_AI)
                        {
                            Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " is engaging unengaged missile (" + potentialTarget.Vessel.vesselName + ") with " + selectedWeapon);
                        }
                        return;
                    }
                }
            }
            //=========END HIGH PRIORITY MISSILES=============

            //============VESSEL THREATS============
            // select target based on competition style
            if (BDArmorySettings.DEFAULT_FFA_TARGETING)
            {
                potentialTarget = BDATargetManager.GetClosestTargetWithBiasAndHysteresis(this);
                targetDebugText = " is engaging a FFA target with ";
            }
            else if (this.targetPriorityEnabled)
            {
                potentialTarget = BDATargetManager.GetHighestPriorityTarget(this);
                targetDebugText = " is engaging highest priority target (" + (potentialTarget != null ? potentialTarget.Vessel.vesselName : "null") + ") with ";
            }
            else
            {
                if (!vessel.LandedOrSplashed)
                {
                    if (pilotAI && pilotAI.IsExtending)
                    {
                        potentialTarget = BDATargetManager.GetAirToAirTargetAbortExtend(this, 1500, 0.2f);
                        targetDebugText = " is aborting extend and engaging an incoming airborne target with ";
                    }
                    else
                    {
                        potentialTarget = BDATargetManager.GetAirToAirTarget(this);
                        targetDebugText = " is engaging an airborne target with ";
                    }
                }
                potentialTarget = BDATargetManager.GetLeastEngagedTarget(this);
                targetDebugText = " is engaging the least engaged target with ";
            }

            if (potentialTarget)
            {
                targetsTried.Add(potentialTarget);
                SetTarget(potentialTarget);

                // Pick target if we have a viable weapon or target priority/FFA targeting is in use
                if ((SmartPickWeapon_EngagementEnvelope(potentialTarget) || this.targetPriorityEnabled || BDArmorySettings.DEFAULT_FFA_TARGETING) && HasWeaponsAndAmmo())
                {
                    if (BDArmorySettings.DEBUG_AI)
                    {
                        Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + targetDebugText + (selectedWeapon != null ? selectedWeapon.GetShortName() : ""));
                    }
                    //need to check that target is actually being seen, and not just being recalled due to object permanence
                    //if (CanSeeTarget(potentialTarget, false))
                    //{
                    //    BDATargetManager.ReportVessel(potentialTarget.Vessel, this); //have it so AI can see and register a target (no radar + FoV angle < 360, radar turns off due to incoming HARM, etc)
                    //} //target would already be listed as seen/radar detected via GuardScan/Radar; all CanSee does is check if the detected time is < 30s
                    return;
                }
                else if (!BDArmorySettings.DISABLE_RAMMING)
                {
                    if (!HasWeaponsAndAmmo() && pilotAI != null && pilotAI.allowRamming)
                    {
                        if (BDArmorySettings.DEBUG_AI)
                        {
                            Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + targetDebugText + "ramming.");
                        }
                        return;
                    }
                }
            }

            //then engage the closest enemy
            potentialTarget = BDATargetManager.GetClosestTarget(this);
            if (potentialTarget)
            {
                targetsTried.Add(potentialTarget);
                SetTarget(potentialTarget);
                /*
                if (CrossCheckWithRWR(potentialTarget) && TryPickAntiRad(potentialTarget))
                {
                    if (BDArmorySettings.DEBUG_LABELS)
                    {
                        Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " is engaging the closest radar target with " +
                                    selectedWeapon.GetShortName());
                    }
                    return;
                }
                */
                if (SmartPickWeapon_EngagementEnvelope(potentialTarget))
                {
                    if (BDArmorySettings.DEBUG_AI)
                    {
                        Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " is engaging the closest target (" + potentialTarget.Vessel.vesselName + ") with " + selectedWeapon.GetShortName());
                    }
                    return;
                }
            }
            //============END VESSEL THREATS============

            //============LOW PRIORITY MISSILES=========
            if (targetMissiles)
            {
                //try to engage least engaged hostile missiles first
                potentialTarget = BDATargetManager.GetMissileTarget(this);
                if (potentialTarget)
                {
                    targetsTried.Add(potentialTarget);
                    SetTarget(potentialTarget);
                    if (SmartPickWeapon_EngagementEnvelope(potentialTarget))
                    {
                        if (BDArmorySettings.DEBUG_AI)
                        {
                            Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " is engaging the least engaged missile (" + potentialTarget.Vessel.vesselName + ") with " + selectedWeapon.GetShortName());
                        }
                        return;
                    }
                }

                //then try to engage closest hostile missile
                potentialTarget = BDATargetManager.GetClosestMissileTarget(this);
                if (potentialTarget)
                {
                    targetsTried.Add(potentialTarget);
                    SetTarget(potentialTarget);
                    if (SmartPickWeapon_EngagementEnvelope(potentialTarget))
                    {
                        if (BDArmorySettings.DEBUG_AI)
                        {
                            Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " is engaging the closest hostile missile (" + potentialTarget.Vessel.vesselName + ") with " + selectedWeapon.GetShortName());
                        }
                        return;
                    }
                }
            }
            //==========END LOW PRIORITY MISSILES=============

            //if nothing works, get all remaining targets and try weapons against them
            using (List<TargetInfo>.Enumerator finalTargets = BDATargetManager.GetAllTargetsExcluding(targetsTried, this).GetEnumerator())
                while (finalTargets.MoveNext())
                {
                    if (finalTargets.Current == null) continue;
                    SetTarget(finalTargets.Current);
                    if (!SmartPickWeapon_EngagementEnvelope(finalTargets.Current)) continue;
                    if (BDArmorySettings.DEBUG_AI)
                    {
                        Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " is engaging a final target with " +
                                  selectedWeapon.GetShortName());
                    }
                    return;
                }

            //no valid targets found
            if (potentialTarget == null || selectedWeapon == null)
            {
                if (BDArmorySettings.DEBUG_AI)
                {
                    Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " is disengaging - no valid weapons - no valid targets");
                }
                CycleWeapon(0);
                SetTarget(null);

                if (vesselRadarData && vesselRadarData.locked && missilesAway.Count == 0) // Don't unlock targets while we've got missiles in the air.
                {
                    vesselRadarData.UnlockAllTargets();
                }
                return;
            }

            Debug.Log("[BDArmory.MissileFire]: Unhandled target case");
        }

        void SmartFindSecondaryTargets()
        {
            //Debug.Log("[BDArmory.MTD]: Finding 2nd targets");
            targetsAssigned.Clear();
            missilesAssigned.Clear();
            if (!currentTarget.isMissile)
            {
                targetsAssigned.Add(currentTarget);
            }
            else
            {
                missilesAssigned.Add(currentTarget);
            }
            List<TargetInfo> targetsTried = new List<TargetInfo>();

            //Secondary targeting priorities
            //1. incoming missile threats
            //2. highest priority non-targeted target
            //3. closest non-targeted target

            for (int i = 0; i < multiTargetNum; i++)
            {
                TargetInfo potentialMissileTarget = null;
                //=========MISSILES=============
                //prioritize incoming missiles
                potentialMissileTarget = BDATargetManager.GetMissileTarget(this, true);
                if (potentialMissileTarget)
                {
                    missilesAssigned.Add(potentialMissileTarget);
                    targetsTried.Add(potentialMissileTarget);
                    return;
                }
                //then provide point defense umbrella
                potentialMissileTarget = BDATargetManager.GetClosestMissileTarget(this);
                if (potentialMissileTarget)
                {
                    missilesAssigned.Add(potentialMissileTarget);
                    targetsTried.Add(potentialMissileTarget);
                    return;
                }
                potentialMissileTarget = BDATargetManager.GetUnengagedMissileTarget(this);
                if (potentialMissileTarget)
                {
                    missilesAssigned.Add(potentialMissileTarget);
                    targetsTried.Add(potentialMissileTarget);
                    return;
                }
            }

            for (int i = 0; i < multiTargetNum; i++)
            {
                TargetInfo potentialTarget = null;
                //============VESSEL THREATS============
                if (!vessel.LandedOrSplashed)
                {
                    //then engage the closest enemy
                    potentialTarget = BDATargetManager.GetHighestPriorityTarget(this);
                    if (potentialTarget)
                    {
                        targetsAssigned.Add(potentialTarget);
                        targetsTried.Add(potentialTarget);
                        return;
                    }
                    potentialTarget = BDATargetManager.GetClosestTarget(this);
                    if (BDArmorySettings.DEFAULT_FFA_TARGETING)
                    {
                        potentialTarget = BDATargetManager.GetClosestTargetWithBiasAndHysteresis(this);
                    }
                    if (potentialTarget)
                    {
                        targetsAssigned.Add(potentialTarget);
                        targetsTried.Add(potentialTarget);
                        return;
                    }
                }
                using (List<TargetInfo>.Enumerator finalTargets = BDATargetManager.GetAllTargetsExcluding(targetsTried, this).GetEnumerator())
                    while (finalTargets.MoveNext())
                    {
                        if (finalTargets.Current == null) continue;
                        targetsAssigned.Add(finalTargets.Current);
                        return;
                    }
                //else
                if (potentialTarget == null)
                {
                    return;
                }
            }
            Debug.Log("[BDArmory.MissileFire]: Unhandled secondary target case");
        }

        // Update target priority UI
        public void UpdateTargetPriorityUI(TargetInfo target)
        {
            // Return if no target
            if (target == null)
            {
                TargetScoreLabel = "";
                TargetLabel = "";
                return;
            }

            // Get UI fields
            var TargetBiasFields = Fields["targetBias"];
            var TargetRangeFields = Fields["targetWeightRange"];
            var TargetPreferenceFields = Fields["targetWeightAirPreference"];
            var TargetATAFields = Fields["targetWeightATA"];
            var TargetAoDFields = Fields["targetWeightAoD"];
            var TargetAccelFields = Fields["targetWeightAccel"];
            var TargetClosureTimeFields = Fields["targetWeightClosureTime"];
            var TargetWeaponNumberFields = Fields["targetWeightWeaponNumber"];
            var TargetMassFields = Fields["targetWeightMass"];
            var TargetFriendliesEngagingFields = Fields["targetWeightFriendliesEngaging"];
            var TargetThreatFields = Fields["targetWeightThreat"];
            var TargetProtectTeammateFields = Fields["targetWeightProtectTeammate"];
            var TargetProtectVIPFields = Fields["targetWeightProtectVIP"];
            var TargetAttackVIPFields = Fields["targetWeightAttackVIP"];

            // Calculate score values
            float targetBiasValue = targetBias;
            float targetRangeValue = target.TargetPriRange(this);
            float targetPreferencevalue = target.TargetPriEngagement(target.weaponManager);
            float targetATAValue = target.TargetPriATA(this);
            float targetAoDValue = target.TargetPriAoD(this);
            float targetAccelValue = target.TargetPriAcceleration();
            float targetClosureTimeValue = target.TargetPriClosureTime(this);
            float targetWeaponNumberValue = target.TargetPriWeapons(target.weaponManager, this);
            float targetMassValue = target.TargetPriMass(target.weaponManager, this);
            float targetFriendliesEngagingValue = target.TargetPriFriendliesEngaging(this);
            float targetThreatValue = target.TargetPriThreat(target.weaponManager, this);
            float targetProtectTeammateValue = target.TargetPriProtectTeammate(target.weaponManager, this);
            float targetProtectVIPValue = target.TargetPriProtectVIP(target.weaponManager, this);
            float targetAttackVIPValue = target.TargetPriAttackVIP(target.weaponManager);

            // Calculate total target score
            float targetScore = targetBiasValue * (
                targetWeightRange * targetRangeValue +
                targetWeightAirPreference * targetPreferencevalue +
                targetWeightATA * targetATAValue +
                targetWeightAccel * targetAccelValue +
                targetWeightClosureTime * targetClosureTimeValue +
                targetWeightWeaponNumber * targetWeaponNumberValue +
                targetWeightMass * targetMassValue +
                targetWeightFriendliesEngaging * targetFriendliesEngagingValue +
                targetWeightThreat * targetThreatValue +
                targetWeightAoD * targetAoDValue +
                targetWeightProtectTeammate * targetProtectTeammateValue +
                targetWeightProtectVIP * targetProtectVIPValue +
                targetWeightAttackVIP * targetAttackVIPValue);

            // Update GUI
            TargetBiasFields.guiName = targetBiasLabel + ": " + targetBiasValue.ToString("0.00");
            TargetRangeFields.guiName = targetRangeLabel + ": " + targetRangeValue.ToString("0.00");
            TargetPreferenceFields.guiName = targetPreferenceLabel + ": " + targetPreferencevalue.ToString("0.00");
            TargetATAFields.guiName = targetATALabel + ": " + targetATAValue.ToString("0.00");
            TargetAoDFields.guiName = targetAoDLabel + ": " + targetAoDValue.ToString("0.00");
            TargetAccelFields.guiName = targetAccelLabel + ": " + targetAccelValue.ToString("0.00");
            TargetClosureTimeFields.guiName = targetClosureTimeLabel + ": " + targetClosureTimeValue.ToString("0.00");
            TargetWeaponNumberFields.guiName = targetWeaponNumberLabel + ": " + targetWeaponNumberValue.ToString("0.00");
            TargetMassFields.guiName = targetMassLabel + ": " + targetMassValue.ToString("0.00");
            TargetFriendliesEngagingFields.guiName = targetFriendliesEngagingLabel + ": " + targetFriendliesEngagingValue.ToString("0.00");
            TargetThreatFields.guiName = targetThreatLabel + ": " + targetThreatValue.ToString("0.00");
            TargetProtectTeammateFields.guiName = targetProtectTeammateLabel + ": " + targetProtectTeammateValue.ToString("0.00");
            TargetProtectVIPFields.guiName = targetProtectVIPLabel + ": " + targetProtectVIPValue.ToString("0.00");
            TargetAttackVIPFields.guiName = targetAttackVIPLabel + ": " + targetAttackVIPValue.ToString("0.00");

            TargetScoreLabel = targetScore.ToString("0.00");
            TargetLabel = target.Vessel.GetDisplayName();
        }

        // extension for feature_engagementenvelope: new smartpickweapon method
        bool SmartPickWeapon_EngagementEnvelope(TargetInfo target)
        {
            // Part 1: Guard conditions (when not to pick a weapon)
            // ------
            if (!target)
                return false;

            if (AI != null && AI.pilotEnabled && !AI.CanEngage())
                return false;

            if ((target.isMissile) && (target.isSplashed || target.isUnderwater))
                return false; // Don't try to engage torpedos, it doesn't work

            // Part 2: check weapons against individual target types
            // ------

            float distance = Vector3.Distance(transform.position + vessel.Velocity(), target.position + target.velocity);
            IBDWeapon targetWeapon = null;
            float targetWeaponRPM = -1;
            float targetWeaponTDPS = 0;
            float targetWeaponImpact = -1;
            // float targetLaserDamage = 0;
            float targetYield = -1;
            float targetBombYield = -1;
            float targetRocketPower = -1;
            float targetRocketAccel = -1;
            int targetWeaponPriority = -1;
            bool candidateAGM = false;
            bool candidateAntiRad = false;
            if (target.isMissile)
            {
                // iterate over weaponTypesMissile and pick suitable one based on engagementRange (and dynamic launch zone for missiles)
                // Prioritize by:
                // 1. Lasers
                // 2. Guns
                // 3. AA missiles
                using (List<IBDWeapon>.Enumerator item = weaponTypesMissile.GetEnumerator())
                    while (item.MoveNext())
                    {
                        if (item.Current == null) continue;
                        // candidate, check engagement envelope
                        if (!CheckEngagementEnvelope(item.Current, distance)) continue;
                        // weapon usable, if missile continue looking for lasers/guns, else take it
                        WeaponClasses candidateClass = item.Current.GetWeaponClass();

                        if (candidateClass == WeaponClasses.DefenseLaser)
                        {
                            ModuleWeapon Laser = item.Current as ModuleWeapon;
                            float candidateYTraverse = Laser.yawRange;
                            float candidatePTraverse = Laser.maxPitch;
                            bool electrolaser = Laser.electroLaser;
                            Transform fireTransform = Laser.fireTransforms[0];

                            if (vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(fireTransform.position) < 0)) continue;

                            if (electrolaser) continue; //electrolasers useless against missiles

                            if (targetWeapon != null && (candidateYTraverse > 0 || candidatePTraverse > 0)) //prioritize turreted lasers
                            {
                                targetWeapon = item.Current;
                                break;
                            }
                            targetWeapon = item.Current; // then any laser
                            break;
                        }

                        if (candidateClass == WeaponClasses.Gun)
                        {
                            // For point defense, favor turrets and RoF
                            ModuleWeapon Gun = item.Current as ModuleWeapon;
                            float candidateRPM = Gun.roundsPerMinute;
                            float candidateYTraverse = Gun.yawRange;
                            float candidatePTraverse = Gun.maxPitch;
                            float candidateMinrange = Gun.engageRangeMin;
                            bool candidatePFuzed = Gun.eFuzeType == ModuleWeapon.FuzeTypes.Proximity || Gun.eFuzeType == ModuleWeapon.FuzeTypes.Flak;
                            bool candidateVTFuzed = Gun.eFuzeType == ModuleWeapon.FuzeTypes.Timed || Gun.eFuzeType == ModuleWeapon.FuzeTypes.Flak;
                            float Cannistershot = Gun.ProjectileCount;

                            Transform fireTransform = Gun.fireTransforms[0];

                            if (vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(fireTransform.position) < 0)) continue;
                            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                            {
                                candidateRPM = BDArmorySettings.FIRE_RATE_OVERRIDE;
                            }
                            if (candidateYTraverse > 0 || candidatePTraverse > 0)
                            {
                                candidateRPM *= 2.0f; // weight selection towards turrets
                            }
                            if (candidatePFuzed || candidateVTFuzed)
                            {
                                candidateRPM *= 1.5f; // weight selection towards flak ammo
                            }
                            if (Cannistershot > 1)
                            {
                                candidateRPM *= (1 + ((Cannistershot / 2) / 100)); // weight selection towards cluster ammo based on submunition count
                            }
                            if (candidateMinrange > distance)
                            {
                                candidateRPM *= .01f; //if within min range, massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                            }
                            if ((targetWeapon != null) && (targetWeaponRPM > candidateRPM))
                                continue; //dont replace better guns (but do replace missiles)

                            targetWeapon = item.Current;
                            targetWeaponRPM = candidateRPM;
                        }

                        if (candidateClass == WeaponClasses.Rocket)
                        {
                            // For point defense, favor turrets and RoF
                            ModuleWeapon Rocket = item.Current as ModuleWeapon;
                            float candidateRocketAccel = Rocket.thrust / Rocket.rocketMass;
                            float candidateRPM = Rocket.roundsPerMinute / 2;
                            bool candidatePFuzed = Rocket.proximityDetonation;
                            float candidateYTraverse = Rocket.yawRange;
                            float candidatePTraverse = Rocket.maxPitch;
                            float candidateMinrange = Rocket.engageRangeMin;

                            Transform fireTransform = Rocket.fireTransforms[0];

                            if (vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(fireTransform.position) < 0)) continue;
                            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                            {
                                candidateRPM = BDArmorySettings.FIRE_RATE_OVERRIDE / 2;
                            }
                            bool compareRocketRPM = false;

                            if (candidateYTraverse > 0 || candidatePTraverse > 0)
                            {
                                candidateRPM *= 2.0f; // weight selection towards turrets
                            }
                            if (targetRocketAccel < candidateRocketAccel)
                            {
                                candidateRPM *= 1.5f; //weight towards faster rockets
                            }
                            if (!candidatePFuzed)
                            {
                                candidateRPM *= 0.01f; //negatively weight against contact-fuze rockets
                            }
                            if (candidateMinrange > distance)
                            {
                                candidateRPM *= .01f; //if within min range, massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                            }
                            if ((targetWeapon != null) && targetWeapon.GetWeaponClass() == WeaponClasses.Gun)
                            {
                                compareRocketRPM = true;
                            }
                            if ((targetWeapon != null) && (targetWeaponRPM > candidateRPM))
                                continue; //dont replace better guns (but do replace missiles)
                            if ((compareRocketRPM && (targetWeaponRPM * 2) < candidateRPM) || (!compareRocketRPM && (targetWeaponRPM) < candidateRPM))
                            {
                                targetWeapon = item.Current;
                                targetRocketAccel = candidateRocketAccel;
                                targetWeaponRPM = candidateRPM;
                            }
                        }

                        if (candidateClass == WeaponClasses.Missile)
                        {
                            if (firedMissiles >= maxMissilesOnTarget) continue;// Max missiles are fired, try another weapon
                            MissileLauncher mlauncher = item.Current as MissileLauncher;
                            float candidateDetDist = 0;
                            float candidateAccel = 0; //for anti-missile, prioritize proxidetonation and accel
                            int candidatePriority = 0;
                            float candidateTDPS = 0f;

                            if (mlauncher != null)
                            {
                                if (mlauncher.TargetingMode == MissileBase.TargetingModes.Radar && radars.Count <= 0) continue; //dont select RH missiles when no radar aboard
                                if (mlauncher.TargetingMode == MissileBase.TargetingModes.Laser && targetingPods.Count <= 0) continue; //don't select LH missiles when no FLIR aboard
                                candidateDetDist = mlauncher.DetonationDistance;
                                candidateAccel = mlauncher.thrust / mlauncher.part.mass; //for anti-missile, prioritize proxidetonation and accel
                                bool EMP = mlauncher.warheadType == MissileBase.WarheadTypes.EMP;
                                candidatePriority = Mathf.RoundToInt(mlauncher.priority);

                                if (EMP) continue;
                                if (vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(mlauncher.transform.position) < 0)) continue;
                                if (targetWeapon != null && targetWeaponPriority > candidatePriority)
                                    continue; //keep higher priority weapon
                                if (candidateDetDist + candidateAccel > targetWeaponTDPS)
                                {
                                    candidateTDPS = candidateDetDist + candidateAccel; // weight selection faster missiles and larger proximity detonations that might catch an incoming missile in the blast
                                }
                            }
                            else
                            { //is modular missile
                                BDModularGuidance mm = item.Current as BDModularGuidance; //need to work out priority stuff for MMGs
                                candidateTDPS = 5000;

                                candidateDetDist = mm.warheadYield;
                                //candidateAccel = (((MissileLauncher)item.Current).thrust / ((MissileLauncher)item.Current).part.mass); 
                                candidateAccel = 1;
                                candidatePriority = Mathf.RoundToInt(mm.priority);

                                if (vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(mlauncher.transform.position) < 0)) continue;
                                if (targetWeapon != null && targetWeaponPriority > candidatePriority)
                                    continue; //keep higher priority weapon
                                if (candidateDetDist + candidateAccel > targetWeaponTDPS)
                                {
                                    candidateTDPS = candidateDetDist + candidateAccel;
                                }
                            }
                            if (distance < ((EngageableWeapon)item.Current).engageRangeMin)
                                candidateTDPS *= -1f; // if within min range, negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo

                            if ((targetWeapon != null) && (((distance < gunRange) && targetWeapon.GetWeaponClass() == WeaponClasses.Gun || targetWeapon.GetWeaponClass() == WeaponClasses.Rocket || targetWeapon.GetWeaponClass() == WeaponClasses.DefenseLaser) || (targetWeaponTDPS > candidateTDPS)))
                                continue; //dont replace guns or better missiles
                            targetWeapon = item.Current;
                            targetWeaponTDPS = candidateTDPS;
                            targetWeaponPriority = candidatePriority;
                        }
                    }
            }

            //else if (!target.isLanded)
            else if (target.isFlying)
            {
                // iterate over weaponTypesAir and pick suitable one based on engagementRange (and dynamic launch zone for missiles)
                // Prioritize by:
                // 1. AA missiles (if we're flying, otherwise use guns if we're within gun range)
                // 1. Lasers
                // 2. Guns
                // 3. rockets
                using (List<IBDWeapon>.Enumerator item = weaponTypesAir.GetEnumerator())
                    while (item.MoveNext())
                    {
                        if (item.Current == null) continue;

                        // candidate, check engagement envelope
                        if (!CheckEngagementEnvelope(item.Current, distance)) continue;
                        // weapon usable, if missile continue looking for lasers/guns, else take it
                        WeaponClasses candidateClass = item.Current.GetWeaponClass();

                        // any rocketpods work?
                        if (candidateClass == WeaponClasses.Rocket)
                        {
                            //for AA, favor higher accel and proxifuze
                            ModuleWeapon Rocket = item.Current as ModuleWeapon;
                            float candidateRocketAccel = Rocket.thrust / Rocket.rocketMass;
                            float candidateRPM = Rocket.roundsPerMinute;
                            bool candidatePFuzed = Rocket.proximityDetonation;
                            int candidatePriority = Mathf.RoundToInt(Rocket.priority);
                            float candidateYTraverse = Rocket.yawRange;
                            float candidatePTraverse = Rocket.maxPitch;
                            float candidateMinrange = Rocket.engageRangeMin;
                            Transform fireTransform = Rocket.fireTransforms[0];

                            if (vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(fireTransform.position) < 0)) continue;

                            Vector3 aimDirection = fireTransform.forward;
                            float targetCosAngle = Rocket.FiringSolutionVector != null ? Vector3.Dot(aimDirection, (Vector3)Rocket.FiringSolutionVector) : Vector3.Dot(aimDirection, (vessel.vesselTransform.position - fireTransform.position).normalized);
                            bool outsideFiringCosAngle = targetCosAngle < Rocket.targetAdjustedMaxCosAngle;

                            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                            {
                                candidateRPM = BDArmorySettings.FIRE_RATE_OVERRIDE;
                            }

                            if (targetWeapon != null && (targetWeapon.GetWeaponClass() == WeaponClasses.Missile) && (targetWeaponTDPS > 0))
                                continue; //dont replace missiles within their engage range

                            if (targetWeapon != null && targetWeaponPriority > candidatePriority)
                                continue; //dont replace a higher priority weapon with a lower priority one

                            if (candidateYTraverse > 0 || candidatePTraverse > 0)
                            {
                                candidateRPM *= 2.0f; // weight selection towards turrets
                            }

                            if (targetRocketAccel < candidateRocketAccel)
                            {
                                candidateRPM *= 1.5f; //weight towards faster rockets
                            }
                            if (candidatePFuzed)
                            {
                                candidateRPM *= 1.5f; // weight selection towards flak ammo
                            }
                            else
                            {
                                candidateRPM *= 0.5f;
                            }
                            if (outsideFiringCosAngle)
                            {
                                candidateRPM *= .01f; //if outside firing angle, massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                            }
                            if (candidateMinrange > distance)
                            {
                                candidateRPM *= .01f; //if within min range massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                            }
                            candidateRPM /= 2; //halve rocket RPm to de-weight it against guns/lasers

                            if (targetWeaponPriority < candidatePriority) //use priority gun
                            {
                                targetWeapon = item.Current;
                                targetWeaponRPM = candidateRPM;
                                targetRocketAccel = candidateRocketAccel;
                                targetWeaponPriority = candidatePriority;
                            }
                            if (targetWeaponPriority == candidatePriority) //if equal priority, use standard weighting
                            {
                                if (targetWeaponRPM < candidateRPM) //or best gun
                                {
                                    targetWeapon = item.Current;
                                    targetWeaponRPM = candidateRPM;
                                    targetRocketAccel = candidateRocketAccel;
                                    targetWeaponPriority = candidatePriority;
                                }
                            }
                        }
                        //Guns have higher priority than rockets; selected gun will override rocket selection
                        if (candidateClass == WeaponClasses.Gun)
                        {
                            // For AtA, generally favour higher RPM and turrets
                            //prioritize weapons with priority, then:
                            //if shooting fighter-sized targets, prioritize RPM
                            //if shooting larger targets - bombers/zeppelins/Ace Combat Wunderwaffen - prioritize biggest caliber
                            ModuleWeapon Gun = item.Current as ModuleWeapon;
                            float candidateRPM = Gun.roundsPerMinute;
                            bool candidateGimbal = Gun.turret;
                            float candidateTraverse = Gun.yawRange;
                            bool candidatePFuzed = Gun.eFuzeType == ModuleWeapon.FuzeTypes.Proximity || Gun.eFuzeType == ModuleWeapon.FuzeTypes.Flak;
                            bool candidateVTFuzed = Gun.eFuzeType == ModuleWeapon.FuzeTypes.Timed || Gun.eFuzeType == ModuleWeapon.FuzeTypes.Flak;
                            float Cannistershot = Gun.ProjectileCount;
                            float candidateMinrange = Gun.engageRangeMin;
                            int candidatePriority = Mathf.RoundToInt(Gun.priority);
                            float candidateRadius = currentTarget.Vessel.GetRadius();
                            float candidateCaliber = Gun.caliber;
                            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                            {
                                candidateRPM = BDArmorySettings.FIRE_RATE_OVERRIDE;
                            }
                            Transform fireTransform = Gun.fireTransforms[0];

                            if (vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(fireTransform.position) < 0)) continue;

                            Vector3 aimDirection = fireTransform.forward;
                            float targetCosAngle = Gun.FiringSolutionVector != null ? Vector3.Dot(aimDirection, (Vector3)Gun.FiringSolutionVector) : Vector3.Dot(aimDirection, (vessel.vesselTransform.position - fireTransform.position).normalized);
                            bool outsideFiringCosAngle = targetCosAngle < Gun.targetAdjustedMaxCosAngle;

                            if (targetWeapon != null && targetWeaponPriority > candidatePriority) continue; //keep higher priority weapon

                            if (candidateRadius > 8) //most fighters are, what, at most 15m in their largest dimension? That said, maybe make this configurable in the weapon PAW...
                            {//weight selection towards larger caliber bullets, modified by turrets/fuzes/range settings when shooting bombers
                                if (candidateGimbal = true && candidateTraverse > 0)
                                {
                                    candidateCaliber *= 1.5f; // weight selection towards turrets
                                }
                                if (candidatePFuzed || candidateVTFuzed)
                                {
                                    candidateCaliber *= 1.5f; // weight selection towards flak ammo
                                }
                                if (outsideFiringCosAngle)
                                {
                                    candidateCaliber *= .01f; //if outside firing angle, massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                                }
                                if (candidateMinrange > distance)
                                {
                                    candidateCaliber *= .01f; //if within min range massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                                }
                                candidateRPM = candidateCaliber * 10;
                            }
                            else //weight selection towards RoF, modified by turrets/fuzes/shot quantity/range
                            {
                                if (candidateGimbal = true && candidateTraverse > 0)
                                {
                                    candidateRPM *= 1.5f; // weight selection towards turrets
                                }
                                if (candidatePFuzed || candidateVTFuzed)
                                {
                                    candidateRPM *= 1.5f; // weight selection towards flak ammo
                                }
                                if (Cannistershot > 1)
                                {
                                    candidateRPM *= (1 + ((Cannistershot / 2) / 100)); // weight selection towards cluster ammo based on submunition count
                                }
                                if (outsideFiringCosAngle)
                                {
                                    candidateRPM *= .01f; //if outside firing angle, massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                                }
                                if (candidateMinrange > distance)
                                {
                                    candidateRPM *= .01f; //if within min range massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                                }
                            }
                            if ((targetWeapon != null) && (targetWeapon.GetWeaponClass() == WeaponClasses.Missile) && (targetWeaponTDPS > 0))
                                continue; //dont replace missiles within their engage range

                            if (targetWeaponPriority < candidatePriority) //use priority gun
                            {
                                targetWeapon = item.Current;
                                targetWeaponRPM = candidateRPM;
                                targetWeaponPriority = candidatePriority;
                            }
                            else //if equal priority, use standard weighting
                            {
                                if (targetWeaponRPM < candidateRPM)
                                {
                                    targetWeapon = item.Current;
                                    targetWeaponRPM = candidateRPM;
                                    targetWeaponPriority = candidatePriority;
                                }
                            }
                        }
                        //if lasers, lasers will override gun selection
                        if (candidateClass == WeaponClasses.DefenseLaser)
                        {
                            // For AA, favour higher power/turreted
                            ModuleWeapon Laser = item.Current as ModuleWeapon;
                            float candidateRPM = Laser.roundsPerMinute;
                            bool candidateGimbal = Laser.turret;
                            float candidateTraverse = Laser.yawRange;
                            float candidateMinrange = Laser.engageRangeMin;
                            int candidatePriority = Mathf.RoundToInt(Laser.priority);
                            bool electrolaser = Laser.electroLaser;
                            bool pulseLaser = Laser.pulseLaser;
                            float candidatePower = electrolaser ? Laser.ECPerShot / (pulseLaser ? 50 : 1) : Laser.laserDamage / (pulseLaser ? 50 : 1);

                            Transform fireTransform = Laser.fireTransforms[0];

                            if (vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(fireTransform.position) < 0)) continue;
                            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                            {
                                candidateRPM = BDArmorySettings.FIRE_RATE_OVERRIDE;
                            }

                            if (electrolaser = true && target.isDebilitated) continue; // don't select EMP weapons if craft already disabled

                            if (targetWeapon != null && targetWeaponPriority > candidatePriority)
                                continue; //keep higher priority weapon

                            candidateRPM *= candidatePower;

                            if (candidateGimbal = true && candidateTraverse > 0)
                            {
                                candidateRPM *= 1.5f; // weight selection towards turreted lasers
                            }
                            if (candidateMinrange > distance)
                            {
                                candidateRPM *= .00001f; //if within min range massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                            }
                            if (targetWeaponPriority < candidatePriority) //use priority gun
                            {
                                targetWeapon = item.Current;
                                targetWeaponRPM = candidateRPM;
                                targetWeaponPriority = candidatePriority;
                            }
                            else //if equal priority, use standard weighting
                            {
                                if (targetWeaponRPM < candidateRPM)
                                {
                                    targetWeapon = item.Current;
                                    targetWeaponRPM = candidateRPM;
                                    targetWeaponPriority = candidatePriority;
                                }
                            }
                        }
                        //projectile weapon selected, any missiles that take precedence?
                        if (candidateClass == WeaponClasses.Missile)
                        {
                            //if (firedMissiles >= maxMissilesOnTarget) continue;// Max missiles are fired, try another weapon
                            float candidateDetDist = 0;
                            float candidateTurning = 0;
                            int candidatePriority = 0;
                            float candidateTDPS = 0f;

                            MissileLauncher mlauncher = item.Current as MissileLauncher;
                            if (mlauncher != null)
                            {
                                if (mlauncher.TargetingMode == MissileBase.TargetingModes.Radar && radars.Count <= 0) continue; //dont select RH missiles when no radar aboard
                                if (mlauncher.TargetingMode == MissileBase.TargetingModes.Laser && targetingPods.Count <= 0) continue; //don't select LH missiles when no FLIR aboard
                                candidateDetDist = mlauncher.DetonationDistance;
                                candidateTurning = mlauncher.maxTurnRateDPS; //for anti-aircraft, prioritize detonation dist and turn capability
                                candidatePriority = Mathf.RoundToInt(mlauncher.priority);
                                bool EMP = mlauncher.warheadType == MissileBase.WarheadTypes.EMP;

                                if (EMP && target.isDebilitated) continue;
                                if (vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(mlauncher.transform.position) < 0)) continue;
                                if (targetWeapon != null && targetWeaponPriority > candidatePriority)
                                    continue; //keep higher priority weapon

                                if (candidateTurning > targetWeaponTDPS)
                                {
                                    candidateTDPS = candidateTurning; // weight selection towards more maneuverable missiles
                                }
                                if (candidateDetDist > 0)
                                {
                                    candidateTDPS += candidateDetDist; // weight selection towards misiles with proximity warheads
                                }
                            }
                            else
                            { //is modular missile
                                BDModularGuidance mm = item.Current as BDModularGuidance; //need to work out priority stuff for MMGs
                                candidateTDPS = 5000;
                                candidateDetDist = mm.warheadYield;
                                //candidateTurning = ((MissileLauncher)item.Current).maxTurnRateDPS; //for anti-aircraft, prioritize detonation dist and turn capability
                                candidatePriority = Mathf.RoundToInt(mm.priority);

                                if (vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(mlauncher.transform.position) < 0)) continue;
                                if (targetWeapon != null && targetWeaponPriority > candidatePriority)
                                    continue; //keep higher priority weapon

                                //if (candidateTurning > targetWeaponTDPS)
                                //{
                                //    candidateTDPS = candidateTurning; // need a way of calculating this...
                                //}
                                if (candidateDetDist > 0)
                                {
                                    candidateTDPS += candidateDetDist; // weight selection towards misiles with proximity warheads
                                }
                            }
                            if (distance < ((EngageableWeapon)item.Current).engageRangeMin || firedMissiles >= maxMissilesOnTarget)
                                candidateTDPS *= -1f; // if within min range, negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo

                            if ((!vessel.LandedOrSplashed) || ((distance > gunRange) && (vessel.LandedOrSplashed))) // If we're not airborne, we want to prioritize guns
                            {
                                if (targetWeaponPriority < candidatePriority) //use priority gun
                                {
                                    targetWeapon = item.Current;
                                    targetWeaponTDPS = candidateTDPS;
                                    targetWeaponPriority = candidatePriority;
                                }
                                else //if equal priority, use standard weighting
                                {
                                    if (targetWeaponTDPS < candidateTDPS)
                                    {
                                        targetWeapon = item.Current;
                                        targetWeaponTDPS = candidateTDPS;
                                        targetWeaponPriority = candidatePriority;
                                    }
                                }
                            }
                        }
                    }
            }
            else if (target.isLandedOrSurfaceSplashed) //for targets on surface/above 10m depth
            {
                // iterate over weaponTypesGround and pick suitable one based on engagementRange (and dynamic launch zone for missiles)
                // Prioritize by:
                // 1. ground attack missiles (cruise, gps, unguided) if target not moving
                // 2. ground attack missiles (guided) if target is moving
                // 3. Bombs / Rockets
                // 4. Guns                

                using (List<IBDWeapon>.Enumerator item = weaponTypesGround.GetEnumerator())
                    while (item.MoveNext())
                    {
                        if (item.Current == null) continue;
                        // candidate, check engagement envelope
                        if (!CheckEngagementEnvelope(item.Current, distance)) continue;
                        // weapon usable, if missile continue looking for lasers/guns, else take it
                        WeaponClasses candidateClass = item.Current.GetWeaponClass();

                        if (candidateClass == WeaponClasses.DefenseLaser) //lasers would be a suboptimal choice for strafing attacks, but if nothing else available...
                        {
                            // For Atg, favour higher power/turreted
                            ModuleWeapon Laser = item.Current as ModuleWeapon;
                            float candidateRPM = Laser.roundsPerMinute;
                            bool candidateGimbal = Laser.turret;
                            float candidateTraverse = Laser.yawRange;
                            float candidateMinrange = Laser.engageRangeMin;
                            int candidatePriority = Mathf.RoundToInt(Laser.priority);
                            bool electrolaser = Laser.electroLaser;
                            bool pulseLaser = Laser.pulseLaser;
                            bool HEpulses = Laser.HEpulses;
                            float candidatePower = electrolaser ? Laser.ECPerShot / (pulseLaser ? 50 : 1) : Laser.laserDamage / (pulseLaser ? 50 : 1);

                            Transform fireTransform = Laser.fireTransforms[0];

                            if (vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(fireTransform.position) < 0)) continue;
                            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                            {
                                candidateRPM = BDArmorySettings.FIRE_RATE_OVERRIDE;
                            }

                            if (electrolaser = true && target.isDebilitated) continue; // don't select EMP weapons if craft already disabled

                            if (targetWeapon != null && targetWeaponPriority > candidatePriority)
                                continue; //keep higher priority weapon

                            candidateRPM *= candidatePower / 1000;

                            if (candidateGimbal = true && candidateTraverse > 0)
                            {
                                candidateRPM *= 1.5f; // weight selection towards turreted lasers
                            }
                            if (HEpulses)
                            {
                                candidateRPM *= 1.5f; // weight selection towards lasers that can do blast damage
                            }
                            if (candidateMinrange > distance)
                            {
                                candidateRPM *= .00001f; //if within min range massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                            }
                            if (targetWeaponPriority < candidatePriority) //use priority gun
                            {
                                targetWeapon = item.Current;
                                targetWeaponRPM = candidateRPM;
                                targetWeaponPriority = candidatePriority;
                            }
                            else //if equal priority, use standard weighting
                            {
                                if (targetWeapon != null && targetWeapon.GetWeaponClass() == WeaponClasses.Rocket || targetWeapon.GetWeaponClass() == WeaponClasses.Gun) continue;
                                if (targetWeaponImpact < candidateRPM) //don't replace bigger guns
                                {
                                    targetWeapon = item.Current;
                                    targetWeaponImpact = candidateRPM;
                                    targetWeaponPriority = candidatePriority;
                                }
                            }
                        }

                        if (candidateClass == WeaponClasses.Gun) //iterate through guns, if nothing else, use found gun
                        {
                            if ((distance > gunRange) && (targetWeapon != null))
                                continue;
                            // For Ground Attack, favour higher blast strength
                            ModuleWeapon Gun = item.Current as ModuleWeapon;
                            float candidateRPM = Gun.roundsPerMinute;
                            float candidateImpact = Gun.bulletMass * Gun.bulletVelocity;
                            int candidatePriority = Mathf.RoundToInt(Gun.priority);
                            bool candidateGimbal = Gun.turret;
                            float candidateMinrange = Gun.engageRangeMin;
                            float candidateTraverse = Gun.yawRange * Gun.maxPitch;
                            float candidateRadius = currentTarget.Vessel.GetRadius();
                            float candidateCaliber = Gun.caliber;
                            Transform fireTransform = Gun.fireTransforms[0];

                            if (BDArmorySettings.BULLET_WATER_DRAG)
                            {
                                if (vessel.Splashed && FlightGlobals.getAltitudeAtPos(fireTransform.position) < 0) continue;
                                if (candidateCaliber < 75 && FlightGlobals.getAltitudeAtPos(target.position) + target.Vessel.GetRadius() < 0) continue; //vessel completely submerged, and not using rounds big enough to survive water impact
                            }
                            if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                            {
                                candidateRPM = BDArmorySettings.FIRE_RATE_OVERRIDE;
                            }

                            if (targetWeaponPriority > candidatePriority)
                                continue; //dont replace better guns or missiles within their engage range

                            if (candidateRadius > 4) //smmall vees target with high-ROF weapons to improve hit chance, bigger stuff use bigger guns
                            {
                                candidateRPM = candidateImpact * candidateRPM;
                            }
                            if (candidateGimbal && candidateTraverse > 0)
                            {
                                candidateRPM *= 1.5f; // weight selection towards turrets
                            }
                            if (candidateMinrange > distance)
                            {
                                candidateRPM *= .01f; //if within min range massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                            }
                            if (targetWeaponPriority < candidatePriority) //use priority gun
                            {
                                targetWeapon = item.Current;
                                targetWeaponImpact = candidateRPM;
                                targetWeaponPriority = candidatePriority;
                            }
                            else //if equal priority, use standard weighting
                            {
                                if (targetWeapon != null && targetWeapon.GetWeaponClass() == WeaponClasses.Rocket) continue;
                                if (targetWeaponImpact < candidateRPM) //don't replace bigger guns
                                {
                                    targetWeapon = item.Current;
                                    targetWeaponImpact = candidateRPM;
                                    targetWeaponPriority = candidatePriority;
                                }
                            }
                        }
                        //Any rockets we can use instead of guns?
                        if (candidateClass == WeaponClasses.Rocket)
                        {
                            ModuleWeapon Rocket = item.Current as ModuleWeapon;
                            float candidateRocketPower = Rocket.blastRadius;
                            float CandidateEndurance = Rocket.thrustTime;
                            int candidateRanking = Mathf.RoundToInt(Rocket.priority);
                            Transform fireTransform = Rocket.fireTransforms[0];

                            if (vessel.Splashed && (BDArmorySettings.BULLET_WATER_DRAG && FlightGlobals.getAltitudeAtPos(fireTransform.position) < -0))
                            {
                                if (distance > 100 * CandidateEndurance) continue;
                            }

                            if (targetWeaponPriority > candidateRanking)
                                continue; //don't select a lower priority weapon over a higher priority one

                            if (targetWeaponPriority < candidateRanking) //use priority gun
                            {
                                if (distance < candidateRocketPower) continue;// don't drop bombs when within blast radius
                                targetWeapon = item.Current;
                                targetRocketPower = candidateRocketPower;
                                targetWeaponPriority = candidateRanking;
                            }
                            else //if equal priority, use standard weighting
                            {
                                if (targetRocketPower < candidateRocketPower) //don't replace higher yield rockets
                                {
                                    if (distance < candidateRocketPower) continue;// don't drop bombs when within blast radius
                                    targetWeapon = item.Current;
                                    targetRocketPower = candidateRocketPower;
                                    targetWeaponPriority = candidateRanking;
                                }
                            }
                        }
                        //Bombs are good. any of those we can use over rockets?
                        if (candidateClass == WeaponClasses.Bomb && (!vessel.Splashed || (vessel.Splashed && vessel.altitude > currentTarget.Vessel.altitude))) //I guess depth charges would sorta apply here, but those are SLW instead
                        {
                            MissileLauncher Bomb = item.Current as MissileLauncher;
                            if (targetWeapon != null && targetWeapon.GetWeaponClass() == WeaponClasses.Missile) continue;
                            //if (firedMissiles >= maxMissilesOnTarget) continue;// Max missiles are fired, try another weapon
                            // only useful if we are flying
                            float candidateYield = Bomb.GetBlastRadius();
                            int candidateCluster = Bomb.clusterbomb;
                            bool EMP = Bomb.warheadType == MissileBase.WarheadTypes.EMP;
                            int candidatePriority = Mathf.RoundToInt(Bomb.priority);
                            double srfSpeed = currentTarget.Vessel.horizontalSrfSpeed;

                            if (EMP && target.isDebilitated) continue;
                            if (targetWeapon != null && targetWeaponPriority > candidatePriority)
                                continue; //keep higher priority weapon
                            if (distance < candidateYield)
                                continue;// don't drop bombs when within blast radius
                            bool candidateUnguided = false;
                            if (!vessel.LandedOrSplashed)
                            {
                                // Priority Sequence:
                                // - guided (JDAM)
                                // - by blast strength
                                // - find way to implement cluster bomb selection priority?

                                if (Bomb.GuidanceMode == MissileBase.GuidanceModes.None)
                                {
                                    if (targetWeaponPriority < candidatePriority) //use priority bomb
                                    {
                                        targetWeapon = item.Current;
                                        targetBombYield = candidateYield;
                                        targetWeaponPriority = candidatePriority;
                                    }
                                    else //if equal priority, use standard weighting
                                    {
                                        if (targetBombYield < candidateYield)//prioritized by biggest boom
                                        {
                                            targetWeapon = item.Current;
                                            targetBombYield = candidateYield;
                                            targetWeaponPriority = candidatePriority;
                                        }
                                    }
                                    candidateUnguided = true;
                                }
                                if (srfSpeed > 1) //prioritize cluster bombs for moving targets
                                {
                                    candidateYield *= (candidateCluster * 2);
                                    if (targetWeaponPriority < candidatePriority) //use priority bomb
                                    {
                                        targetWeapon = item.Current;
                                        targetBombYield = candidateYield;
                                        targetWeaponPriority = candidatePriority;
                                    }
                                    else //if equal priority, use standard weighting
                                    {
                                        if (targetBombYield < candidateYield)//prioritized by biggest boom
                                        {
                                            targetWeapon = item.Current;
                                            targetBombYield = candidateYield;
                                            targetWeaponPriority = candidatePriority;
                                        }
                                    }
                                }
                                if (Bomb.GuidanceMode == MissileBase.GuidanceModes.AGMBallistic)
                                {
                                    if (targetWeaponPriority < candidatePriority) //use priority bomb
                                    {
                                        targetWeapon = item.Current;
                                        targetBombYield = candidateYield;
                                        targetWeaponPriority = candidatePriority;
                                    }
                                    else //if equal priority, use standard weighting
                                    {
                                        if ((candidateUnguided ? targetBombYield / 2 : targetBombYield) < candidateYield) //prioritize biggest Boom, but preference guided bombs
                                        {
                                            targetWeapon = item.Current;
                                            targetBombYield = candidateYield;
                                            targetWeaponPriority = candidatePriority;
                                        }
                                    }
                                }
                            }
                        }
                        //Missiles are the preferred method of ground attack. use if available over other options
                        if (candidateClass == WeaponClasses.Missile) //don't use missiles underwater. That's what torpedoes are for
                        {
                            // Priority Sequence:
                            // - Antiradiation
                            // - guided missiles
                            // - by blast strength
                            // - add code to choose optimal missile based on target profile - i.e. use bigger bombs on large landcruisers, smaller bombs on small Vees that don't warrant that sort of overkill?
                            MissileLauncher Missile = item.Current as MissileLauncher;
                            if (Missile.TargetingMode == MissileBase.TargetingModes.Radar && radars.Count <= 0) continue; //dont select RH missiles when no radar aboard
                            if (Missile.TargetingMode == MissileBase.TargetingModes.Laser && targetingPods.Count <= 0) continue; //don't select LH missiles when no FLIR aboard
                            if (vessel.Splashed && FlightGlobals.getAltitudeAtPos(item.Current.GetPart().transform.position) < -2) continue;
                            //if (firedMissiles >= maxMissilesOnTarget) continue;// Max missiles are fired, try another weapon
                            float candidateYield = Missile.GetBlastRadius();
                            double srfSpeed = currentTarget.Vessel.horizontalSrfSpeed;
                            bool EMP = Missile.warheadType == MissileBase.WarheadTypes.EMP;
                            int candidatePriority = Mathf.RoundToInt(Missile.priority);

                            if (EMP && target.isDebilitated) continue;
                            //if (targetWeapon != null && targetWeapon.GetWeaponClass() == WeaponClasses.Bomb) targetYield = -1; //reset targetyield so larger bomb yields don't supercede missiles
                            if (targetWeapon != null && targetWeaponPriority > candidatePriority)
                                continue; //keep higher priority weapon
                            if (srfSpeed < 1) // set higher than 0 in case of physics jitteriness
                            {
                                if (Missile.TargetingMode == MissileBase.TargetingModes.Gps ||
                                    (Missile.GuidanceMode == MissileBase.GuidanceModes.Cruise ||
                                      Missile.GuidanceMode == MissileBase.GuidanceModes.AGMBallistic ||
                                      Missile.GuidanceMode == MissileBase.GuidanceModes.None))
                                {
                                    if (targetWeapon != null && targetYield > candidateYield) continue; //prioritize biggest Boom
                                    if (distance < Missile.engageRangeMin) continue; //select missiles we can use now
                                    targetYield = candidateYield;
                                    candidateAGM = true;
                                    targetWeapon = item.Current;
                                }
                            }
                            if (Missile.TargetingMode == MissileBase.TargetingModes.AntiRad && (rwr && rwr.rwrEnabled))
                            {// make it so this only selects antirad when hostile radar
                                for (int i = 0; i < rwr.pingsData.Length; i++)
                                {
                                    if (Missile.antiradTargets.Contains(rwr.pingsData[i].signalStrength))
                                    {
                                        if ((rwr.pingWorldPositions[i] - guardTarget.CoM).sqrMagnitude < 20 * 20) //is current target a hostile radar source?
                                        {
                                            candidateAntiRad = true;
                                            candidateYield *= 2; // Prioritize anti-rad missiles for hostile radar sources
                                        }
                                    }
                                }
                                if (candidateAntiRad)
                                {
                                    if (targetWeapon != null && targetYield > candidateYield) continue; //prioritize biggest Boom
                                    targetYield = candidateYield;
                                    targetWeapon = item.Current;
                                    targetWeaponPriority = candidatePriority;
                                    candidateAGM = true;
                                }
                            }
                            else if (Missile.TargetingMode == MissileBase.TargetingModes.Laser)
                            {
                                if (candidateAntiRad) continue; //keep antirad missile;
                                if (targetWeapon != null && targetYield > candidateYield) continue; //prioritize biggest Boom
                                candidateAGM = true;
                                targetYield = candidateYield;
                                targetWeapon = item.Current;
                                targetWeaponPriority = candidatePriority;
                            }
                            else
                            {
                                if (!candidateAGM)
                                {
                                    if (targetWeapon != null && targetYield > candidateYield) continue;
                                    targetYield = candidateYield;
                                    targetWeapon = item.Current;
                                    targetWeaponPriority = candidatePriority;
                                }
                            }
                        }

                        // TargetInfo.isLanded includes splashed but not underwater, for whatever reasons.
                        // If target is splashed, and we have torpedoes, use torpedoes, because, obviously,
                        // torpedoes are the best kind of sausage for splashed targets,
                        // almost as good as STS missiles, which we don't have.
                        if (candidateClass == WeaponClasses.SLW && target.isSplashed)
                        {
                            //if (firedMissiles >= maxMissilesOnTarget) continue;// Max missiles are fired, try another weapon
                            MissileLauncher SLW = item.Current as MissileLauncher;
                            float candidateYield = SLW.GetBlastRadius();
                            bool EMP = SLW.warheadType == MissileBase.WarheadTypes.EMP;
                            int candidatePriority = Mathf.RoundToInt(SLW.priority);

                            if (EMP && target.isDebilitated) continue;

                            // not sure on the desired selection priority algorithm, so placeholder By Yield for now
                            float droptime = SLW.dropTime;

                            if (droptime > 0 || vessel.LandedOrSplashed) //make sure it's an airdropped torpedo if flying
                            {
                                if (targetYield > candidateYield) continue;
                                if (distance < candidateYield) continue;
                                targetYield = candidateYield;
                                targetWeapon = item.Current;
                                targetWeaponPriority = candidatePriority;
                            }
                        }
                    }
                //hmm.. no MMG targeting logic for ground attack? something to add in later
            }
            else if (target.isUnderwater)
            {
                // iterate over weaponTypesSLW (Ship Launched Weapons) and pick suitable one based on engagementRange
                // Prioritize by:
                // 1. Depth Charges
                // 2. Torpedos
                using (List<IBDWeapon>.Enumerator item = weaponTypesSLW.GetEnumerator())
                    while (item.MoveNext())
                    {
                        if (item.Current == null) continue;
                        if (!CheckEngagementEnvelope(item.Current, distance)) continue;

                        WeaponClasses candidateClass = item.Current.GetWeaponClass();

                        if (candidateClass == WeaponClasses.SLW)
                        {
                            MissileLauncher SLW = item.Current as MissileLauncher;
                            if (SLW.TargetingMode == MissileBase.TargetingModes.Radar && radars.Count <= 0) continue; //dont select RH missiles when no radar aboard
                            if (SLW.TargetingMode == MissileBase.TargetingModes.Laser && targetingPods.Count <= 0) continue; //don't select LH missiles when no FLIR aboard
                            float candidateYield = SLW.GetBlastRadius();
                            bool EMP = SLW.warheadType == MissileBase.WarheadTypes.EMP;
                            int candidatePriority = Mathf.RoundToInt(SLW.priority);

                            if (targetWeapon != null && targetWeaponPriority > candidatePriority)
                                continue; //keep higher priority weapon

                            if (EMP && target.isDebilitated) continue;

                            if (!vessel.Splashed || (vessel.Splashed && vessel.altitude > currentTarget.Vessel.altitude)) //if surfaced or sumberged, but above target, try depthcharges
                            {
                                if (item.Current.GetMissileType().ToLower() == "depthcharge")
                                {
                                    if (distance < candidateYield) continue; //could add in prioritization for bigger boom, but how many different options for depth charges are there?
                                    targetWeapon = item.Current;
                                    targetWeaponPriority = candidatePriority;
                                    break;
                                }
                            }
                            else //don't use depth charges underwater
                            {
                                if (item.Current.GetMissileType().ToLower() != "torpedo") continue;
                                if (distance < candidateYield) continue; //don't use explosives within their blast radius
                                //if(firedMissiles >= maxMissilesOnTarget) continue;// Max missiles are fired, try another weapon
                                targetWeapon = item.Current;
                                targetWeaponPriority = candidatePriority;
                                break;
                            }
                            //MMG torpedo support... ?
                        }
                        if (candidateClass == WeaponClasses.Rocket)
                        {
                            ModuleWeapon Rocket = item.Current as ModuleWeapon;
                            float candidateRocketPower = Rocket.blastRadius;
                            float CandidateEndurance = Rocket.thrustTime;
                            int candidateRanking = Mathf.RoundToInt(Rocket.priority);
                            Transform fireTransform = Rocket.fireTransforms[0];

                            if (vessel.Splashed && FlightGlobals.getAltitudeAtPos(fireTransform.position) < -5)//if underwater, rockets might work, at close range
                            {
                                if (BDArmorySettings.BULLET_WATER_DRAG)
                                {
                                    if ((distance > 100 * CandidateEndurance)) continue;
                                }
                                if (targetWeaponPriority > candidateRanking)
                                    continue; //don't select a lower priority weapon over a higher priority one

                                if (targetWeaponPriority < candidateRanking) //use priority gun
                                {
                                    if (distance < candidateRocketPower) continue;// don't drop bombs when within blast radius
                                    targetWeapon = item.Current;
                                    targetRocketPower = candidateRocketPower;
                                    targetWeaponPriority = candidateRanking;
                                }
                                else //if equal priority, use standard weighting
                                {
                                    if (targetRocketPower < candidateRocketPower) //don't replace higher yield rockets
                                    {
                                        if (distance < candidateRocketPower) continue;// don't drop bombs when within blast radius
                                        targetWeapon = item.Current;
                                        targetRocketPower = candidateRocketPower;
                                        targetWeaponPriority = candidateRanking;
                                    }
                                }
                            }
                        }
                        if (candidateClass == WeaponClasses.DefenseLaser)
                        {
                            // For STS, favour higher power/turreted
                            ModuleWeapon Laser = item.Current as ModuleWeapon;
                            float candidateRPM = Laser.roundsPerMinute;
                            bool candidateGimbal = Laser.turret;
                            float candidateTraverse = Laser.yawRange;
                            float candidateMinrange = Laser.engageRangeMin;
                            float candidateMaxrange = Laser.engageRangeMax;
                            int candidatePriority = Mathf.RoundToInt(Laser.priority);
                            bool electrolaser = Laser.electroLaser;
                            bool pulseLaser = Laser.pulseLaser;
                            float candidatePower = electrolaser ? Laser.ECPerShot / (pulseLaser ? 50 : 1) : Laser.laserDamage / (pulseLaser ? 50 : 1);

                            Transform fireTransform = Laser.fireTransforms[0];

                            if (vessel.Splashed && FlightGlobals.getAltitudeAtPos(fireTransform.position) < 0)//if underwater, lasers should work, at close range
                            {
                                if (BDArmorySettings.BULLET_WATER_DRAG)
                                {
                                    if (distance > candidateMaxrange / 10) continue;
                                }
                                if (BDArmorySettings.RUNWAY_PROJECT && BDArmorySettings.RUNWAY_PROJECT_ROUND == 41)
                                {
                                    candidateRPM = BDArmorySettings.FIRE_RATE_OVERRIDE;
                                }

                                if (electrolaser) continue; // don't use lightning guns underwater

                                if (targetWeapon != null && targetWeaponPriority > candidatePriority)
                                    continue; //keep higher priority weapon

                                candidateRPM *= candidatePower;

                                if (candidateGimbal = true && candidateTraverse > 0)
                                {
                                    candidateRPM *= 1.5f; // weight selection towards turreted lasers
                                }
                                if (candidateMinrange > distance)
                                {
                                    candidateRPM *= .00001f; //if within min range massively negatively weight weapon - allows weapon to still be selected if all others lost/out of ammo
                                }
                                if (targetWeaponPriority < candidatePriority) //use priority gun
                                {
                                    targetWeapon = item.Current;
                                    targetWeaponRPM = candidateRPM;
                                    targetWeaponPriority = candidatePriority;
                                }
                                else //if equal priority, use standard weighting
                                {
                                    if (targetWeaponRPM < candidateRPM)
                                    {
                                        targetWeapon = item.Current;
                                        targetWeaponRPM = candidateRPM;
                                        targetWeaponPriority = candidatePriority;
                                    }
                                }
                            }
                        }
                    }
            }

            // return result of weapon selection
            if (targetWeapon != null)
            {
                //update the legacy lists & arrays, especially selectedWeapon and weaponIndex
                selectedWeapon = targetWeapon;
                // find it in weaponArray
                for (int i = 1; i < weaponArray.Length; i++)
                {
                    weaponIndex = i;
                    if (selectedWeapon.GetShortName() == weaponArray[weaponIndex].GetShortName())
                    {
                        break;
                    }
                }

                if (BDArmorySettings.DEBUG_AI)
                {
                    Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " - Selected weapon " + selectedWeapon.GetShortName());
                }

                PrepareWeapons();
                SetDeployableWeapons();
                DisplaySelectedWeaponMessage();
                return true;
            }
            else
            {
                if (BDArmorySettings.DEBUG_AI)
                {
                    Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " - No weapon selected for target " + target.Vessel.vesselName);
                    // Debug.Log("DEBUG target isflying:" + target.isFlying + ", isLorS:" + target.isLandedOrSurfaceSplashed + ", isUW:" + target.isUnderwater);
                    // if (target.isFlying)
                    //     foreach (var weapon in weaponTypesAir)
                    //     {
                    //         var engageableWeapon = weapon as EngageableWeapon;
                    //         Debug.Log("DEBUG flying target:" + target.Vessel + ", weapon:" + weapon + " can engage:" + CheckEngagementEnvelope(weapon, distance) + ", engageEnabled:" + engageableWeapon.engageEnabled + ", min/max:" + engageableWeapon.GetEngagementRangeMin() + "/" + engageableWeapon.GetEngagementRangeMax());
                    //     }
                    // if (target.isLandedOrSurfaceSplashed)
                    //     foreach (var weapon in weaponTypesAir)
                    //     {
                    //         var engageableWeapon = weapon as EngageableWeapon;
                    //         Debug.Log("DEBUG landed target:" + target.Vessel + ", weapon:" + weapon + " can engage:" + CheckEngagementEnvelope(weapon, distance) + ", engageEnabled:" + engageableWeapon.engageEnabled + ", min/max:" + engageableWeapon.GetEngagementRangeMin() + "/" + engageableWeapon.GetEngagementRangeMax());
                    //     }
                }
                selectedWeapon = null;
                weaponIndex = 0;
                return false;
            }
        }

        // extension for feature_engagementenvelope: check engagement parameters of the weapon if it can be used against the current target
        bool CheckEngagementEnvelope(IBDWeapon weaponCandidate, float distanceToTarget)
        {
            EngageableWeapon engageableWeapon = weaponCandidate as EngageableWeapon;

            if (engageableWeapon == null) return true;
            if (!engageableWeapon.engageEnabled) return true;
            //if (distanceToTarget < engageableWeapon.GetEngagementRangeMin()) return false; //covered in weapon select logic
            //if (distanceToTarget > engageableWeapon.GetEngagementRangeMax()) return false;
            if (distanceToTarget > (engageableWeapon.GetEngagementRangeMax() * 1.2f)) return false; //have Ai begin to preemptively lead target, instead of frantically doing so after weapon in range

            switch (weaponCandidate.GetWeaponClass())
            {
                case WeaponClasses.DefenseLaser:
                    {
                        ModuleWeapon laser = (ModuleWeapon)weaponCandidate;

                        // check yaw range of turret
                        ModuleTurret turret = laser.turret;
                        float gimbalTolerance = vessel.LandedOrSplashed ? 0 : 15;
                        if (turret != null)
                            if (!TargetInTurretRange(turret, gimbalTolerance))
                                return false;

                        // check overheat
                        if (laser.isOverheated)
                            return false;

                        if (laser.isReloading || !laser.hasGunner)
                            return false;

                        // check ammo
                        if (CheckAmmo(laser))
                        {
                            if (BDArmorySettings.DEBUG_WEAPONS)
                            {
                                Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " - Firing possible with " + weaponCandidate.GetShortName());
                            }
                            return true;
                        }
                        break;
                    }

                case WeaponClasses.Gun:
                    {
                        ModuleWeapon gun = (ModuleWeapon)weaponCandidate;

                        // check yaw range of turret
                        ModuleTurret turret = gun.turret;
                        float gimbalTolerance = vessel.LandedOrSplashed ? 0 : 15;
                        if (turret != null)
                            if (!TargetInTurretRange(turret, gimbalTolerance, null, gun))
                                return false;

                        // check overheat, reloading, ability to fire soon
                        if (gun.isOverheated)
                            return false;
                        if (gun.isReloading || !gun.hasGunner)
                            return false;
                        if (!gun.CanFireSoon())
                            return false;
                        // check ammo
                        if (CheckAmmo(gun))
                        {
                            if (BDArmorySettings.DEBUG_WEAPONS)
                            {
                                Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " - Firing possible with " + weaponCandidate.GetShortName());
                            }
                            return true;
                        }
                        break;
                    }

                case WeaponClasses.Missile:
                    {
                        MissileBase ml = (MissileBase)weaponCandidate;
                        if (distanceToTarget < engageableWeapon.GetEngagementRangeMin()) return false;
                        // lock radar if needed
                        if (ml.TargetingMode == MissileBase.TargetingModes.Radar)
                        {
                            if (results.foundAntiRadiationMissile) return false; // Don't try to fire radar missiles while we have an incoming anti-rad missile
                            using (List<ModuleRadar>.Enumerator rd = radars.GetEnumerator())
                                while (rd.MoveNext())
                                {
                                    if (rd.Current != null || rd.Current.canLock)
                                    {
                                        rd.Current.EnableRadar();
                                    }
                                }
                        }
                        // check DLZ
                        MissileLaunchParams dlz = MissileLaunchParams.GetDynamicLaunchParams(ml, guardTarget.Velocity(), guardTarget.transform.position);
                        if (vessel.srfSpeed > ml.minLaunchSpeed && distanceToTarget < dlz.maxLaunchRange && distanceToTarget > dlz.minLaunchRange)
                        {
                            return true;
                        }
                        if (BDArmorySettings.DEBUG_MISSILES)
                        {
                            Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " - Failed DLZ test: " + weaponCandidate.GetShortName() + ", distance: " + distanceToTarget + ", DLZ min/max: " + dlz.minLaunchRange + "/" + dlz.maxLaunchRange);
                        }
                        break;
                    }

                case WeaponClasses.Bomb:
                    if (distanceToTarget < engageableWeapon.GetEngagementRangeMin()) return false;
                    if (!vessel.LandedOrSplashed)
                        return true;    // TODO: bomb always allowed?
                    break;

                case WeaponClasses.Rocket:
                    {
                        ModuleWeapon rocket = (ModuleWeapon)weaponCandidate;

                        // check yaw range of turret
                        ModuleTurret turret = rocket.turret;
                        float gimbalTolerance = vessel.LandedOrSplashed ? 0 : 15;
                        if (turret != null)
                            if (!TargetInTurretRange(turret, gimbalTolerance, null, rocket))
                                return false;
                        if (rocket.isOverheated)
                            return false;
                        //check reloading and crewed
                        if (rocket.isReloading || !rocket.hasGunner)
                            return false;

                        // check ammo
                        if (CheckAmmo(rocket))
                        {
                            if (BDArmorySettings.DEBUG_WEAPONS)
                            {
                                Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " - Firing possible with " + weaponCandidate.GetShortName());
                            }
                            return true;
                        }
                        break;
                    }

                case WeaponClasses.SLW:
                    {
                        if (distanceToTarget < engageableWeapon.GetEngagementRangeMin()) return false;
                        // Enable sonar, or radar, if no sonar is found.
                        if (((MissileBase)weaponCandidate).TargetingMode == MissileBase.TargetingModes.Radar)
                            using (List<ModuleRadar>.Enumerator rd = radars.GetEnumerator())
                                while (rd.MoveNext())
                                {
                                    if (rd.Current != null || rd.Current.canLock)
                                        rd.Current.EnableRadar();
                                }
                        return true;
                    }
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return false;
        }

        public void SetTarget(TargetInfo target)
        {
            if (target) // We have a target
            {
                if (currentTarget)
                {
                    currentTarget.Disengage(this);
                }
                target.Engage(this);
                if (target != null && !target.isMissile)
                    if (pilotAI && pilotAI.IsExtending && target.Vessel != pilotAI.extendTarget)
                    {
                        pilotAI.StopExtending("changed target"); // Only stop extending if the target is different from the extending target
                    }
                currentTarget = target;
                guardTarget = target.Vessel;
                if (multiTargetNum > 1)
                {
                    SmartFindSecondaryTargets();
                }
                MissileBase ml = CurrentMissile;
                MissileBase pMl = PreviousMissile;
                if (!ml && pMl) ml = PreviousMissile; //if fired missile, then switched to guns or something
                if (ml && ml.TargetingMode == MissileBase.TargetingModes.Radar && vesselRadarData != null && (!vesselRadarData.locked || vesselRadarData.lockedTargetData.vessel != guardTarget))
                {
                    if (!vesselRadarData.locked)
                    {
                        vesselRadarData.TryLockTarget(guardTarget);
                    }
                    else
                    {
                        if (firedMissiles >= maxMissilesOnTarget && (multiMissileTgtNum > 1 && BDATargetManager.TargetList(Team).Count > 1)) //if there are multiple potential targets, see how many can be fired at with missiles
                        {
                            if (!ml.radarLOAL) //switch active lock instead of clearing locks for SARH missiles
                            {
                                //vesselRadarData.UnlockCurrentTarget();
                                vesselRadarData.TryLockTarget(guardTarget);
                            }
                            else
                                vesselRadarData.SwitchActiveLockedTarget(guardTarget);
                        }
                        else
                        {
                            vesselRadarData.UnlockCurrentTarget();
                            vesselRadarData.TryLockTarget(guardTarget);
                        }
                    }
                }
            }
            else // No target, disengage
            {
                if (currentTarget)
                {
                    currentTarget.Disengage(this);
                }
                guardTarget = null;
                currentTarget = null;
            }
        }

        #endregion Smart Targeting
        public float detectedTargetTimeout = 0;
        public bool staleTarget = false;
        public bool CanSeeTarget(TargetInfo target, bool checkForstaleTarget = true)
        {
            // fix cheating: we can see a target IF we either have a visual on it, OR it has been detected on radar/sonar
            // but to prevent AI from stopping an engagement just because a target dropped behind a small hill 5 seconds ago, clamp the timeout to 30 seconds
            // i.e. let's have at least some object permanence :)
            // If we can't directly see the target via sight or radar, AI will head to last known position of target, based on target's vector at time contact was lost,
            // with precision of estimated position degrading over time.

            // can we get a visual sight of the target?
            VesselCloakInfo vesselcamo = target.Vessel.gameObject.GetComponent<VesselCloakInfo>();
            float viewModifier = 1;
            if (vesselcamo && vesselcamo.cloakEnabled)
            {
                viewModifier = vesselcamo.opticalReductionFactor;
            }
            //Can the target be seen?
            if ((target.Vessel.transform.position - transform.position).sqrMagnitude < (guardRange * viewModifier) * (guardRange * viewModifier) &&
            Vector3.Angle(-vessel.ReferenceTransform.forward, target.Vessel.transform.position - vessel.CoM) < guardAngle / 2)
            {
                if (RadarUtils.TerrainCheck(target.Vessel.transform.position, transform.position)) //vessel behind terrain
                {
                    if (target.detectedTime.TryGetValue(Team, out float detectedTime) && Time.time - detectedTime < Mathf.Max(30, targetScanInterval))
                    {
                        //Debug.Log($"[BDArmory.MissileFire]: {target.name} last seen {Time.time - detectedTime} seconds ago. Recalling last known position");
                        detectedTargetTimeout = Time.time - detectedTime;
                        staleTarget = true;
                        return true;
                    }
                    staleTarget = true;
                    return false;
                }
                detectedTargetTimeout = 0;
                staleTarget = false;
                return true;
            }
            //target beyond visual range. Detected by radar/IRST?
            target.detected.TryGetValue(Team, out bool detected);//see if the target is actually within radar sight right now
            if (detected)
            {
                detectedTargetTimeout = 0;
                staleTarget = false;
                return true;
            }
            //carrying antirads and picking up RWR pings?
            if (rwr && rwr.rwrEnabled && rwr.displayRWR && hasAntiRadiationOrdinance)//see if RWR is picking up a ping from unseen radar source and craft has HARMs
            {
                for (int i = 0; i < rwr.pingsData.Length; i++) //using copy of antirad targets due to CanSee running before weapon selection
                {
                    if (rwr.pingsData[i].exists && antiradTargets.Contains(rwr.pingsData[i].signalStrength) && (rwr.pingWorldPositions[i] - target.position).sqrMagnitude < 20 * 20)
                    {
                        detectedTargetTimeout = 0;
                        staleTarget = false;
                        return true;
                    }
                }
            }
            //can't see target, but did we see it recently?
            if (checkForstaleTarget) //merely look to see if a target was last detected within 30s
            {
                if (target.detectedTime.TryGetValue(Team, out float detectedTime) && Time.time - detectedTime < Mathf.Max(30, targetScanInterval))
                {
                    //Debug.Log($"[BDArmory.MissileFire]: {target.name} last seen {Time.time - detectedTime} seconds ago. Recalling last known position");
                    detectedTargetTimeout = Time.time - detectedTime;
                    staleTarget = true;
                    return true;
                }
                return false; //target long gone
            }
            staleTarget = true;
            return false;
        }

        /// <summary>
        /// Override for legacy targeting only! Remove when removing legcy mode!
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public bool CanSeeTarget(Vessel target)
        {
            // can we get a visual sight of the target?
            if ((target.transform.position - transform.position).sqrMagnitude < guardRange * guardRange)
            {
                if (RadarUtils.TerrainCheck(target.transform.position, transform.position))
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        void SearchForRadarSource()
        {
            antiRadTargetAcquired = false;
            antiRadiationTarget = Vector3.zero;
            if (rwr && rwr.rwrEnabled)
            {
                float closestAngle = 360;
                MissileBase missile = CurrentMissile;

                if (!missile) return;

                float maxOffBoresight = missile.maxOffBoresight;

                if (missile.TargetingMode != MissileBase.TargetingModes.AntiRad) return;

                MissileLauncher ml = CurrentMissile as MissileLauncher;

                for (int i = 0; i < rwr.pingsData.Length; i++)
                {
                    if (rwr.pingsData[i].exists && (ml.antiradTargets.Contains(rwr.pingsData[i].signalStrength)))
                    {
                        float angle = Vector3.Angle(rwr.pingWorldPositions[i] - missile.transform.position, missile.GetForwardTransform());

                        if (angle < closestAngle && angle < maxOffBoresight)
                        {
                            closestAngle = angle;
                            antiRadiationTarget = rwr.pingWorldPositions[i];
                            antiRadTargetAcquired = true;
                        }
                    }
                }
            }
        }

        void SearchForLaserPoint()
        {
            MissileBase ml = CurrentMissile;
            if (!ml || ml.TargetingMode != MissileBase.TargetingModes.Laser)
            {
                return;
            }

            MissileLauncher launcher = ml as MissileLauncher;
            if (launcher != null)
            {
                foundCam = BDATargetManager.GetLaserTarget(launcher,
                    launcher.GuidanceMode == MissileBase.GuidanceModes.BeamRiding);
            }
            else
            {
                foundCam = BDATargetManager.GetLaserTarget((BDModularGuidance)ml, false);
            }

            if (foundCam)
            {
                laserPointDetected = true;
            }
            else
            {
                laserPointDetected = false;
            }
        }

        void SearchForHeatTarget()
        {
            if (CurrentMissile != null)
            {
                if (!CurrentMissile || CurrentMissile.TargetingMode != MissileBase.TargetingModes.Heat)
                {
                    return;
                }

                float scanRadius = CurrentMissile.lockedSensorFOV * 0.5f;
                float maxOffBoresight = CurrentMissile.maxOffBoresight * 0.85f;

                if (vesselRadarData)
                {
                    if (vesselRadarData.irstCount > 0)
                    {
                        heatTarget = vesselRadarData.activeIRTarget(); //point seeker at active target's IR return
                    }
                    else
                    {
                        if (vesselRadarData.locked) //uncaged radar lock
                            heatTarget = vesselRadarData.lockedTargetData.targetData;
                    }
                }
                Vector3 direction =
                    heatTarget.exists && Vector3.Angle(heatTarget.position - CurrentMissile.MissileReferenceTransform.position, CurrentMissile.GetForwardTransform()) < maxOffBoresight ?
                    heatTarget.predictedPosition - CurrentMissile.MissileReferenceTransform.position
                    : CurrentMissile.GetForwardTransform();

                heatTarget = BDATargetManager.GetHeatTarget(vessel, vessel, new Ray(CurrentMissile.MissileReferenceTransform.position + (50 * CurrentMissile.GetForwardTransform()), direction), TargetSignatureData.noTarget, scanRadius, CurrentMissile.heatThreshold, CurrentMissile.uncagedLock, CurrentMissile.lockedSensorFOVBias, CurrentMissile.lockedSensorVelocityBias, this);
            }
        }

        bool CrossCheckWithRWR(TargetInfo v)
        {
            bool matchFound = false;
            if (rwr && rwr.rwrEnabled)
            {
                for (int i = 0; i < rwr.pingsData.Length; i++)
                {
                    if (rwr.pingsData[i].exists && (rwr.pingWorldPositions[i] - v.position).sqrMagnitude < 20 * 20)
                    {
                        matchFound = true;
                        break;
                    }
                }
            }

            return matchFound;
        }

        public void SendTargetDataToMissile(MissileBase ml)
        { //TODO BDModularGuidance: implement all targetings on base
            if (ml.TargetingMode == MissileBase.TargetingModes.Laser && laserPointDetected)
            {
                ml.lockedCamera = foundCam;
                if (BDArmorySettings.DEBUG_MISSILES)
                    Debug.Log("[BDArmory.MissileData]: Sending targetInfo to laser Missile...");
                if (guardMode && ((foundCam.groundTargetPosition - guardTarget.CoM).sqrMagnitude < 10 * 10))
                {
                    ml.targetVessel = guardTarget.gameObject.GetComponent<TargetInfo>();
                    if (BDArmorySettings.DEBUG_MISSILES)
                        Debug.Log("[BDArmory.MissileData]: targetInfo sent for " + ml.targetVessel.Vessel.GetName());
                }
            }
            else if (ml.TargetingMode == MissileBase.TargetingModes.Gps)
            {
                if (designatedGPSCoords != Vector3d.zero)
                {
                    ml.targetGPSCoords = designatedGPSCoords;
                    ml.TargetAcquired = true;
                    if (BDArmorySettings.DEBUG_MISSILES)
                        Debug.Log("[BDArmory.MissileData]: Sending targetInfo to GPS Missile...");
                    if (guardMode && ((designatedGPSCoords - guardTarget.CoM).sqrMagnitude < 10 * 10))
                    {
                        ml.targetVessel = guardTarget.gameObject.GetComponent<TargetInfo>();
                        if (BDArmorySettings.DEBUG_MISSILES)
                            Debug.Log("[BDArmory.MissileData]: targetInfo sent for " + ml.targetVessel.Vessel.GetName());
                    }
                }
            }
            else if (ml.TargetingMode == MissileBase.TargetingModes.Heat && heatTarget.exists)
            {
                ml.heatTarget = heatTarget;
                heatTarget = TargetSignatureData.noTarget;
                if (BDArmorySettings.DEBUG_MISSILES)
                    Debug.Log("[BDArmory.MissileData]: Sending targetInfo to heat Missile...");
                ml.targetVessel = ml.heatTarget.vessel.gameObject.GetComponent<TargetInfo>();
                if (BDArmorySettings.DEBUG_MISSILES)
                    Debug.Log("[BDArmory.MissileData]: targetInfo sent for " + ml.targetVessel.Vessel.GetName());
            }
            else if (ml.TargetingMode == MissileBase.TargetingModes.Radar && vesselRadarData && vesselRadarData.locked)//&& radar && radar.lockedTarget.exists)
            {
                ml.radarTarget = vesselRadarData.lockedTargetData.targetData;
                ml.vrd = vesselRadarData;
                vesselRadarData.LastMissile = ml;
                if (BDArmorySettings.DEBUG_MISSILES)
                    Debug.Log("[BDArmory.MissileData]: Sending targetInfo to radar Missile...");
                ml.targetVessel = vesselRadarData.lockedTargetData.targetData.vessel.gameObject.GetComponent<TargetInfo>();
                if (BDArmorySettings.DEBUG_MISSILES)
                    Debug.Log("[BDArmory.MissileData]: targetInfo sent for " + ml.targetVessel.Vessel.GetName());
            }
            else if (ml.TargetingMode == MissileBase.TargetingModes.AntiRad && antiRadTargetAcquired && antiRadiationTarget != Vector3.zero)
            {
                ml.TargetAcquired = true;
                ml.targetGPSCoords = VectorUtils.WorldPositionToGeoCoords(antiRadiationTarget,
                        vessel.mainBody);
                if (BDArmorySettings.DEBUG_MISSILES)
                    Debug.Log("[BDArmory.MissileData]: Sending targetInfo to Antirad Missile...");
                if (guardMode && AntiRadDistanceCheck())
                {
                    ml.targetVessel = guardTarget.gameObject.GetComponent<TargetInfo>();
                    if (BDArmorySettings.DEBUG_MISSILES)
                        Debug.Log("[BDArmory.MissileData]: targetInfo sent for " + ml.targetVessel.Vessel.GetName());
                }
            }
            else if (ml.GetWeaponClass() == WeaponClasses.Bomb)
            {
                if (BDArmorySettings.DEBUG_MISSILES)
                    Debug.Log("[BDArmory.MissileData]: Sending targetInfo to bomb...");
                //if (guardMode && ((bombAimerPosition - guardTarget.CoM).sqrMagnitude < ml.GetBlastRadius()))
                if (guardMode && guardTarget != null)
                {
                    ml.targetVessel = guardTarget.gameObject.GetComponent<TargetInfo>();
                    if (BDArmorySettings.DEBUG_MISSILES)
                        Debug.Log("[BDArmory.MissileData]: targetInfo sent for " + ml.targetVessel.Vessel.GetName());
                }
            }
            //ml.targetVessel = currentTarget;
            if (currentTarget != null)
            {
                if (BDArmorySettings.DEBUG_MISSILES)
                    Debug.Log("[BDArmory.MULTITARGETING]: firing missile at " + currentTarget.Vessel.GetName());
            }
            else
            {
                if (BDArmorySettings.DEBUG_MISSILES)
                    Debug.Log("[BDArmory.MULTITARGETING]: firing missile null target");
            }
        }

        #endregion Targeting

        #region Guard

        public void ResetGuardInterval()
        {
            targetScanTimer = 0;
        }

        void GuardMode()
        {
            if (!gameObject.activeInHierarchy) return;
            if (BDArmorySettings.PEACE_MODE) return;

            UpdateGuardViewScan();

            //setting turrets to guard mode
            if (selectedWeapon != null && selectedWeapon != previousSelectedWeapon && (selectedWeapon.GetWeaponClass() == WeaponClasses.Gun || selectedWeapon.GetWeaponClass() == WeaponClasses.Rocket || selectedWeapon.GetWeaponClass() == WeaponClasses.DefenseLaser))
            {
                //make this not have to go every frame
                using (var weapon = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                    while (weapon.MoveNext())
                    {
                        if (weapon.Current == null) continue;
                        if (weapon.Current.GetShortName() != selectedWeapon.GetShortName()) //want to find all weapons in WeaponGroup, rather than all weapons of parttype
                        {
                            if (weapon.Current.turret != null && (weapon.Current.ammoCount > 0 || BDArmorySettings.INFINITE_AMMO)) // Put other turrets into standby instead of disabling them if they have ammo.
                            {
                                if (!weapon.Current.isAPS)
                                {
                                    weapon.Current.StandbyWeapon();
                                    weapon.Current.aiControlled = true;
                                }
                            }
                            continue;
                        }
                        weapon.Current.EnableWeapon();
                        weapon.Current.aiControlled = true;
                        if (weapon.Current.FireAngleOverride) continue; // if a weapon-specific accuracy override is present
                        weapon.Current.maxAutoFireCosAngle = adjustedAutoFireCosAngle; //user-adjustable from 0-2deg
                        weapon.Current.FiringTolerance = AutoFireCosAngleAdjustment;
                    }
            }

            if (!guardTarget && selectedWeapon != null && (selectedWeapon.GetWeaponClass() == WeaponClasses.Gun || selectedWeapon.GetWeaponClass() == WeaponClasses.Rocket || selectedWeapon.GetWeaponClass() == WeaponClasses.DefenseLaser))
            {
                using (var weapon = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                    while (weapon.MoveNext())
                    {
                        if (weapon.Current == null) continue;
                        // if (weapon.Current.GetShortName() != selectedWeapon.GetShortName()) continue; 
                        weapon.Current.autoFire = false;
                        weapon.Current.autofireShotCount = 0;
                        weapon.Current.visualTargetVessel = null;
                        weapon.Current.visualTargetPart = null;
                    }
            }
            //if (missilesAway < 0)
            //    missilesAway = 0;

            if (missileIsIncoming)
            {
                if (!isLegacyCMing)
                {
                    // StartCoroutine(LegacyCMRoutine()); // Depreciated
                }

                targetScanTimer -= Time.fixedDeltaTime; //advance scan timing (increased urgency)
            }

            // Update target priority UI
            if ((targetPriorityEnabled) && (currentTarget))
                UpdateTargetPriorityUI(currentTarget);

            //scan and acquire new target
            if (Time.time - targetScanTimer > targetScanInterval)
            {
                targetScanTimer = Time.time;

                if (!guardFiringMissile)// || (firedMissiles >= maxMissilesOnTarget && multiMissileTgtNum > 1 && BDATargetManager.TargetList(Team).Count > 1)) //grab new target, if possible
                {
                    SmartFindTarget();

                    if (guardTarget == null || selectedWeapon == null)
                    {
                        SetCargoBays();
                        SetDeployableWeapons();
                        return;
                    }

                    //firing
                    if (weaponIndex > 0)
                    {
                        if (selectedWeapon.GetWeaponClass() == WeaponClasses.Missile || selectedWeapon.GetWeaponClass() == WeaponClasses.SLW)
                        {
                            bool launchAuthorized = true;
                            bool pilotAuthorized = true;
                            //(!pilotAI || pilotAI.GetLaunchAuthorization(guardTarget, this));

                            float targetAngle = Vector3.Angle(-transform.forward, guardTarget.transform.position - transform.position);
                            float targetDistance = Vector3.Distance(currentTarget.position, transform.position);
                            MissileLaunchParams dlz = MissileLaunchParams.GetDynamicLaunchParams(CurrentMissile, guardTarget.Velocity(), guardTarget.CoM);

                            if (targetAngle > guardAngle / 2) //dont fire yet if target out of guard angle
                            {
                                launchAuthorized = false;
                            }
                            else if (targetDistance >= dlz.maxLaunchRange || targetDistance <= dlz.minLaunchRange)  //fire the missile only if target is further than missiles min launch range
                            {
                                launchAuthorized = false;
                            }

                            // Check that launch is possible before entering GuardMissileRoutine, or that missile is on a turret
                            MissileLauncher ml = CurrentMissile as MissileLauncher;
                            launchAuthorized = launchAuthorized && (GetLaunchAuthorization(guardTarget, this) || (ml is not null && ml.missileTurret));


                            if (BDArmorySettings.DEBUG_MISSILES)
                                Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " launchAuth=" + launchAuthorized + ", pilotAut=" + pilotAuthorized + ", missilesAway/Max=" + firedMissiles + "/" + maxMissilesOnTarget);

                            if (firedMissiles < maxMissilesOnTarget)
                            {
                                if (CurrentMissile.TargetingMode == MissileBase.TargetingModes.Radar && !CurrentMissile.radarLOAL && MaxradarLocks < multiMissileTgtNum)
                                {
                                    launchAuthorized = false; //don't fire SARH if radar can't support the needed radar lock
                                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire]: radar lock number exceeded to launch!");
                                }

                                if (!guardFiringMissile && launchAuthorized
                                    && (CurrentMissile != null && (CurrentMissile.TargetingMode != MissileBase.TargetingModes.Radar || (vesselRadarData != null && (!vesselRadarData.locked || vesselRadarData.lockedTargetData.vessel == guardTarget))))) // Allow firing multiple missiles at the same target. FIXME This is a stop-gap until proper multi-locking support is available.
                                {
                                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " firing missile");
                                    StartCoroutine(GuardMissileRoutine());
                                }
                            }
                            else if (BDArmorySettings.DEBUG_MISSILES)
                            {
                                Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " waiting for missile to be ready...");
                            }

                            // if (!launchAuthorized || !pilotAuthorized || missilesAway >= maxMissilesOnTarget)
                            // {
                            //     targetScanTimer -= 0.5f * targetScanInterval;
                            // }
                        }
                        else if (selectedWeapon.GetWeaponClass() == WeaponClasses.Bomb)
                        {
                            if (!guardFiringMissile)
                            {
                                StartCoroutine(GuardBombRoutine());
                            }
                        }
                        else if (selectedWeapon.GetWeaponClass() == WeaponClasses.Gun ||
                                 selectedWeapon.GetWeaponClass() == WeaponClasses.Rocket ||
                                 selectedWeapon.GetWeaponClass() == WeaponClasses.DefenseLaser)
                        {
                            StartCoroutine(GuardTurretRoutine());
                        }
                    }
                }
                SetCargoBays();
                SetDeployableWeapons();
            }

            if (overrideTimer > 0)
            {
                overrideTimer -= TimeWarp.fixedDeltaTime;
            }
            else
            {
                overrideTimer = 0;
                overrideTarget = null;
            }
        }

        void UpdateGuardViewScan()
        {
            results = RadarUtils.GuardScanInDirection(this, transform, guardAngle, guardRange);
            incomingThreatVessel = null;

            if (results.foundMissile)
            {
                if (BDArmorySettings.DEBUG_AI && (!missileIsIncoming || results.incomingMissiles[0].distance < 1000f))
                {
                    foreach (var incomingMissile in results.incomingMissiles)
                        Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " incoming missile (" + incomingMissile.vessel.vesselName + " of type " + incomingMissile.guidanceType + " from " + (incomingMissile.weaponManager != null && incomingMissile.weaponManager.vessel != null ? incomingMissile.weaponManager.vessel.vesselName : "unknown") + ") found at distance " + incomingMissile.distance + "m");
                }
                missileIsIncoming = true;
                incomingMissileLastDetected = Time.time;
                // Assign the closest missile as the main threat. FIXME In the future, we could do something more complex to handle all the incoming missiles.
                incomingMissileDistance = results.incomingMissiles[0].distance;
                incomingMissileTime = results.incomingMissiles[0].time;
                incomingThreatPosition = results.incomingMissiles[0].position;
                incomingThreatVessel = results.incomingMissiles[0].vessel;
                incomingMissileVessel = results.incomingMissiles[0].vessel;
                if (rwr && !rwr.rwrEnabled) rwr.EnableRWR();
                if (rwr && rwr.rwrEnabled && !rwr.displayRWR) rwr.displayRWR = true;

                if (results.foundHeatMissile)
                {
                    StartCoroutine(UnderAttackRoutine());

                    FireFlares();
                    FireOCM(true);
                }

                if (results.foundRadarMissile)
                {
                    StartCoroutine(UnderAttackRoutine());

                    FireChaff();
                    FireECM();
                }

                if (results.foundAGM)
                {
                    StartCoroutine(UnderAttackRoutine());

                    //do smoke CM here.
                    if (targetMissiles && guardTarget == null)
                    {
                        //targetScanTimer = Mathf.Min(targetScanInterval, Time.time - targetScanInterval + 0.5f);
                        targetScanTimer -= targetScanInterval / 2;
                    }
                }

                if (results.foundAntiRadiationMissile)
                {
                    StartCoroutine(UnderAttackRoutine());

                    // Turn off the radars
                    using (List<ModuleRadar>.Enumerator rd = radars.GetEnumerator())
                        while (rd.MoveNext())
                        {
                            if (rd.Current != null || rd.Current.canLock)
                                rd.Current.DisableRadar();
                        }
                }
            }
            else
            {
                // FIXME these shouldn't be necessary if all checks against them are guarded by missileIsIncoming.
                incomingMissileDistance = float.MaxValue;
                incomingMissileTime = float.MaxValue;
                incomingMissileVessel = null;
            }

            if (results.firingAtMe)
            {
                if (!missileIsIncoming) // Don't override incoming missile threats. FIXME In the future, we could do something more complex to handle all incoming threats.
                {
                    incomingThreatPosition = results.threatPosition;
                    incomingThreatVessel = results.threatVessel;
                }
                if (priorGunThreatVessel == results.threatVessel)
                {
                    incomingMissTime += Time.fixedDeltaTime;
                }
                else
                {
                    priorGunThreatVessel = results.threatVessel;
                    incomingMissTime = 0f;
                }
                if ((pilotAI != null && incomingMissTime >= pilotAI.evasionTimeThreshold && incomingMissDistance < pilotAI.evasionThreshold) || AI != null && pilotAI == null) // If we haven't been under fire long enough, ignore gunfire
                {
                    FireOCM(false); //enable visual coutnermeasures if under fire
                }
                if (results.threatWeaponManager != null)
                {
                    incomingMissDistance = results.missDistance + results.missDeviation;
                    TargetInfo nearbyFriendly = BDATargetManager.GetClosestFriendly(this);
                    TargetInfo nearbyThreat = BDATargetManager.GetTargetFromWeaponManager(results.threatWeaponManager);

                    if (nearbyThreat != null && nearbyThreat.weaponManager != null && nearbyFriendly != null && nearbyFriendly.weaponManager != null)
                        if (Team.IsEnemy(nearbyThreat.weaponManager.Team) && nearbyFriendly.weaponManager.Team == Team)
                        //turns out that there's no check for AI on the same team going after each other due to this.  Who knew?
                        {
                            if (nearbyThreat == currentTarget && nearbyFriendly.weaponManager.currentTarget != null)
                            //if being attacked by the current target, switch to the target that the nearby friendly was engaging instead
                            {
                                SetOverrideTarget(nearbyFriendly.weaponManager.currentTarget);
                                nearbyFriendly.weaponManager.SetOverrideTarget(nearbyThreat);
                                if (BDArmorySettings.DEBUG_AI)
                                    Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " called for help from " + nearbyFriendly.Vessel.vesselName + " and took its target in return");
                                //basically, swap targets to cover each other
                            }
                            else
                            {
                                //otherwise, continue engaging the current target for now
                                nearbyFriendly.weaponManager.SetOverrideTarget(nearbyThreat);
                                if (BDArmorySettings.DEBUG_AI)
                                    Debug.Log("[BDArmory.MissileFire]: " + vessel.vesselName + " called for help from " + nearbyFriendly.Vessel.vesselName);
                            }
                        }
                }
                StartCoroutine(UnderAttackRoutine()); //this seems to be firing all the time, not just when bullets are flying towards craft...?
                StartCoroutine(UnderFireRoutine());
            }
            else
            {
                incomingMissTime = 0f; // Reset incoming fire time
            }
        }

        public void ForceScan()
        {
            targetScanTimer = -100;
        }

        public void StartGuardTurretFiring()
        {
            if (!guardTarget) return;
            if (selectedWeapon == null) return;
            int TurretID = 0;
            int MissileID = 0;
            using (var weapon = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                while (weapon.MoveNext())
                {
                    if (weapon.Current == null) continue;
                    if (weapon.Current.GetShortName() != selectedWeapon.GetShortName())
                    {
                        if (weapon.Current.turret != null && (weapon.Current.ammoCount > 0 || BDArmorySettings.INFINITE_AMMO)) // Other turrets can just generally aim at the currently targeted vessel.
                        {
                            weapon.Current.visualTargetVessel = guardTarget;
                        }
                        continue;
                    }

                    if (multiTargetNum > 1)
                    {
                        if (weapon.Current.turret)
                        {
                            if (TurretID > Mathf.Min((targetsAssigned.Count - 1), multiTargetNum))
                            {
                                TurretID = 0; //if more turrets than targets, loop target list
                            }
                            if (targetsAssigned.Count > 0 && targetsAssigned[TurretID].Vessel != null)
                            {
                                if ((weapon.Current.engageAir && targetsAssigned[TurretID].isFlying) ||
                                    (weapon.Current.engageGround && targetsAssigned[TurretID].isLandedOrSurfaceSplashed) ||
                                    (weapon.Current.engageSLW && targetsAssigned[TurretID].isUnderwater)) //check engagement envelope
                                {
                                    if (TargetInTurretRange(weapon.Current.turret, 7, targetsAssigned[TurretID].Vessel, weapon.Current))
                                    {
                                        weapon.Current.visualTargetVessel = targetsAssigned[TurretID].Vessel; // if target within turret fire zone, assign
                                    }
                                    else //else try remaining targets
                                    {
                                        using (List<TargetInfo>.Enumerator item = targetsAssigned.GetEnumerator())
                                            while (item.MoveNext())
                                            {
                                                if (item.Current.Vessel == null) continue;
                                                if (TargetInTurretRange(weapon.Current.turret, 7, item.Current.Vessel, weapon.Current))
                                                {
                                                    weapon.Current.visualTargetVessel = item.Current.Vessel;
                                                    break;
                                                }
                                            }
                                    }
                                }
                                TurretID++;
                            }
                            if (MissileID > Mathf.Min((missilesAssigned.Count - 1), multiTargetNum))
                            {
                                MissileID = 0; //if more turrets than targets, loop target list
                            }
                            if (missilesAssigned.Count > 0 && missilesAssigned[MissileID].Vessel != null) //if missile, override non-missile target
                            {
                                if (weapon.Current.engageMissile)
                                {
                                    if (TargetInTurretRange(weapon.Current.turret, 7, missilesAssigned[MissileID].Vessel, weapon.Current))
                                    {
                                        weapon.Current.visualTargetVessel = missilesAssigned[MissileID].Vessel; // if target within turret fire zone, assign
                                    }
                                    else //assigned target outside turret arc, try the other targets on the list
                                    {
                                        using (List<TargetInfo>.Enumerator item = missilesAssigned.GetEnumerator())
                                            while (item.MoveNext())
                                            {
                                                if (item.Current.Vessel == null) continue;
                                                if (TargetInTurretRange(weapon.Current.turret, 7, item.Current.Vessel, weapon.Current))
                                                {
                                                    weapon.Current.visualTargetVessel = item.Current.Vessel;
                                                    break;
                                                }
                                            }
                                    }
                                }
                                MissileID++;
                            }
                        }
                        else
                        {
                            //weapon.Current.visualTargetVessel = guardTarget;
                            weapon.Current.visualTargetVessel = targetsAssigned[0].Vessel; //make sure all guns targeting the same target, to ensure the leadOffest is the same, and that the Ai isn't trying to use the leadOffset from a turret
                            //Debug.Log("[BDArmory.MTD]: target from list was null, defaulting to " + guardTarget.name);
                        }
                    }
                    else
                    {
                        weapon.Current.visualTargetVessel = guardTarget;
                        //Debug.Log("[BDArmory.MTD]: non-turret, assigned " + guardTarget.name);
                    }
                    weapon.Current.targetCOM = targetCoM;
                    if (targetCoM)
                    {
                        weapon.Current.targetCockpits = false;
                        weapon.Current.targetEngines = false;
                        weapon.Current.targetWeapons = false;
                        weapon.Current.targetMass = false;
                    }
                    else
                    {
                        weapon.Current.targetCockpits = targetCommand;
                        weapon.Current.targetEngines = targetEngine;
                        weapon.Current.targetWeapons = targetWeapon;
                        weapon.Current.targetMass = targetMass;
                    }

                    weapon.Current.autoFireTimer = Time.time;
                    //weapon.Current.autoFireLength = 3 * targetScanInterval / 4;
                    weapon.Current.autoFireLength = (fireBurstLength < 0.01f) ? targetScanInterval / 2f : fireBurstLength;
                }
        }

        public void SetOverrideTarget(TargetInfo target)
        {
            overrideTarget = target;
            targetScanTimer = -100;
        }

        public void UpdateMaxGuardRange()
        {
            UI_FloatRange rangeEditor = (UI_FloatRange)Fields["guardRange"].uiControlEditor;
            rangeEditor.maxValue = BDArmorySettings.MAX_GUARD_VISUAL_RANGE;
        }

        public void UpdateMaxGunRange(Vessel v)
        {
            if (v != vessel || vessel == null || !vessel.loaded || !part.isActiveAndEnabled) return;
            VesselModuleRegistry.OnVesselModified(v);
            List<WeaponClasses> gunLikeClasses = new List<WeaponClasses> { WeaponClasses.Gun, WeaponClasses.DefenseLaser, WeaponClasses.Rocket };
            maxGunRange = 10f;
            foreach (var weapon in VesselModuleRegistry.GetModules<ModuleWeapon>(vessel))
            {
                if (weapon == null) continue;
                if (gunLikeClasses.Contains(weapon.GetWeaponClass()))
                    maxGunRange = Mathf.Max(maxGunRange, weapon.maxEffectiveDistance);
            }
            UI_FloatRange rangeEditor = (UI_FloatRange)Fields["gunRange"].uiControlEditor;
            rangeEditor.maxValue = maxGunRange;
            if (BDArmorySetup.Instance.textNumFields != null && BDArmorySetup.Instance.textNumFields.ContainsKey("gunRange")) { BDArmorySetup.Instance.textNumFields["gunRange"].maxValue = maxGunRange; }
            gunRange = Mathf.Min(gunRange, maxGunRange);
            if (BDArmorySettings.DEBUG_AI) Debug.Log("[BDArmory.MissileFire]: Updating gun range of " + v.vesselName + " to " + gunRange + " of " + maxGunRange);
        }

        public void UpdateMaxGunRange(Part eventPart)
        {
            if (EditorLogic.fetch.ship == null) return;
            List<WeaponClasses> gunLikeClasses = new List<WeaponClasses> { WeaponClasses.Gun, WeaponClasses.DefenseLaser, WeaponClasses.Rocket };
            maxGunRange = 10f;
            foreach (var p in EditorLogic.fetch.ship.Parts)
            {
                foreach (var weapon in p.FindModulesImplementing<ModuleWeapon>())
                {
                    if (weapon == null) continue;
                    if (gunLikeClasses.Contains(weapon.GetWeaponClass()))
                    {
                        maxGunRange = Mathf.Max(maxGunRange, weapon.maxEffectiveDistance);
                    }
                }
            }
            UI_FloatRange rangeEditor = (UI_FloatRange)Fields["gunRange"].uiControlEditor;
            if (gunRange == rangeEditor.maxValue) { gunRange = maxGunRange; }
            rangeEditor.maxValue = maxGunRange;
            gunRange = Mathf.Min(gunRange, maxGunRange);
            if (BDArmorySettings.DEBUG_AI) Debug.Log("[BDArmory.MissileFire]: Updating gun range of " + EditorLogic.fetch.ship.shipName + " to " + gunRange + " of " + maxGunRange);
        }

        public float ThreatClosingTime(Vessel threat)
        {
            float closureTime = 3600f; // Default closure time of one hour
            if (threat) // If we weren't passed a null
            {
                float targetDistance = Vector3.Distance(threat.transform.position, vessel.transform.position);
                Vector3 currVel = (float)vessel.srfSpeed * vessel.Velocity().normalized;
                closureTime = Mathf.Clamp((float)(1 / ((threat.Velocity() - currVel).magnitude / targetDistance)), 0f, closureTime);
                // Debug.Log("[BDArmory.MissileFire]: Threat from " + threat.GetDisplayName() + " is " + closureTime.ToString("0.0") + " seconds away!");
            }
            return closureTime;
        }

        // moved from pilot AI, as it does not really do anything AI related?
        public bool GetLaunchAuthorization(Vessel targetV, MissileFire mf)
        {
            bool launchAuthorized = false;
            MissileBase missile = mf.CurrentMissile;
            if (missile != null && targetV != null)
            {
                Vector3 target = targetV.transform.position;
                if (!targetV.LandedOrSplashed)
                {
                    target = MissileGuidance.GetAirToAirFireSolution(missile, targetV);
                }

                float boresightFactor = (mf.vessel.LandedOrSplashed || targetV.LandedOrSplashed || missile.uncagedLock) ? 0.75f : 0.35f; // Allow launch at close to maxOffBoresight for ground targets or missiles with allAspect = true

                //if(missile.TargetingMode == MissileBase.TargetingModes.Gps) maxOffBoresight = 45;

                // Check that target is within maxOffBoresight now and in future time fTime
                launchAuthorized = Vector3.Angle(missile.GetForwardTransform(), target - missile.transform.position) < missile.maxOffBoresight * boresightFactor; // Launch is possible now

                if (launchAuthorized)
                {
                    float fTime = Mathf.Min(missile.dropTime, 2f);
                    Vector3 futurePos = target + (targetV.Velocity() * fTime);
                    Vector3 myFuturePos = vessel.ReferenceTransform.position + (vessel.Velocity() * fTime);
                    launchAuthorized = launchAuthorized && (Vector3.Angle(vessel.ReferenceTransform.up, futurePos - myFuturePos) < missile.maxOffBoresight * boresightFactor); // Launch is likely also possible at fTime

                }

            }

            return launchAuthorized;
        }

        /// <summary>
        /// Check if AI is online and can target the current guardTarget with direct fire weapons
        /// </summary>
        /// <returns>true if AI might fire</returns>
        bool AIMightDirectFire()
        {
            return AI != null && AI.pilotEnabled && AI.CanEngage() && guardTarget && AI.IsValidFixedWeaponTarget(guardTarget);
        }

        #endregion Guard

        #region Turret

        int CheckTurret(float distance)
        {
            if (weaponIndex == 0 || selectedWeapon == null ||
                !(selectedWeapon.GetWeaponClass() == WeaponClasses.Gun ||
                  selectedWeapon.GetWeaponClass() == WeaponClasses.DefenseLaser ||
                  selectedWeapon.GetWeaponClass() == WeaponClasses.Rocket))
            {
                return 2;
            }
            if (BDArmorySettings.DEBUG_WEAPONS)
            {
                Debug.Log("[BDArmory.MissileFire]: Checking turrets");
            }
            float finalDistance = distance;
            //vessel.LandedOrSplashed ? distance : distance/2; //decrease distance requirement if airborne

            using (var weapon = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                while (weapon.MoveNext())
                {
                    if (weapon.Current == null) continue;
                    if (weapon.Current.GetShortName() != selectedWeapon.GetShortName()) continue;
                    float gimbalTolerance = vessel.LandedOrSplashed ? 0 : 15;
                    if (((AI != null && AI.pilotEnabled && AI.CanEngage()) || (TargetInTurretRange(weapon.Current.turret, gimbalTolerance, null, weapon.Current))) && weapon.Current.maxEffectiveDistance >= finalDistance)
                    {
                        if (weapon.Current.isOverheated)
                        {
                            if (BDArmorySettings.DEBUG_WEAPONS)
                            {
                                Debug.Log("[BDArmory.MissileFire]: " + selectedWeapon + " is overheated!");
                            }
                            return -1;
                        }
                        if (weapon.Current.isReloading)
                        {
                            if (BDArmorySettings.DEBUG_WEAPONS)
                            {
                                Debug.Log("[BDArmory.MissileFire]: " + selectedWeapon + " is reloading!");
                            }
                            return -1;
                        }
                        if (!weapon.Current.hasGunner)
                        {
                            if (BDArmorySettings.DEBUG_WEAPONS)
                            {
                                Debug.Log("[BDArmory.MissileFire]: " + selectedWeapon + " has no gunner!");
                            }
                            return -1;
                        }
                        if (CheckAmmo(weapon.Current) || BDArmorySettings.INFINITE_AMMO)
                        {
                            if (BDArmorySettings.DEBUG_WEAPONS)
                            {
                                Debug.Log("[BDArmory.MissileFire]: " + selectedWeapon + " is valid!");
                            }
                            return 1;
                        }
                        if (BDArmorySettings.DEBUG_WEAPONS)
                        {
                            Debug.Log("[BDArmory.MissileFire]: " + selectedWeapon + " has no ammo.");
                        }
                        return -1;
                    }
                    if (BDArmorySettings.DEBUG_WEAPONS)
                    {
                        Debug.Log("[BDArmory.MissileFire]: " + selectedWeapon + " cannot reach target (" + distance + " vs " + weapon.Current.maxEffectiveDistance + ", yawRange: " + weapon.Current.yawRange + "). Continuing.");
                    }
                    //else return 0;
                }
            return 2;
        }

        bool TargetInTurretRange(ModuleTurret turret, float tolerance, Vessel gTarget = null, ModuleWeapon weapon = null)
        {
            if (!turret)
            {
                return false;
            }

            if (!guardTarget)
            {
                if (BDArmorySettings.DEBUG_WEAPONS)
                {
                    Debug.Log("[BDArmory.MissileFire]: Checking turret range but no guard target");
                }
                return false;
            }
            if (gTarget == null) gTarget = guardTarget;

            Transform turretTransform = turret.yawTransform.parent;
            Vector3 direction = gTarget.transform.position - turretTransform.position;
            if (weapon != null && weapon.bulletDrop) // Account for bullet drop (rough approximation not accounting for target movement).
            {
                switch (weapon.GetWeaponClass())
                {
                    case WeaponClasses.Gun:
                        {
                            var effectiveBulletSpeed = (turret.part.rb.velocity + Krakensbane.GetFrameVelocityV3f() + weapon.bulletVelocity * direction.normalized).magnitude;
                            var timeOfFlight = direction.magnitude / effectiveBulletSpeed;
                            direction -= 0.5f * FlightGlobals.getGeeForceAtPosition(vessel.transform.position) * timeOfFlight * timeOfFlight;
                            break;
                        }
                    case WeaponClasses.Rocket:
                        {
                            var effectiveRocketSpeed = (turret.part.rb.velocity + Krakensbane.GetFrameVelocityV3f() + (weapon.thrust * weapon.thrustTime / weapon.rocketMass) * direction.normalized).magnitude;
                            var timeOfFlight = direction.magnitude / effectiveRocketSpeed;
                            direction -= 0.5f * FlightGlobals.getGeeForceAtPosition(vessel.transform.position) * timeOfFlight * timeOfFlight;
                            break;
                        }
                }
            }
            Vector3 directionYaw = Vector3.ProjectOnPlane(direction, turretTransform.up);

            float angleYaw = Vector3.Angle(turretTransform.forward, directionYaw);
            float signedAnglePitch = 90 - Vector3.Angle(turretTransform.up, direction);
            bool withinPitchRange = (signedAnglePitch >= turret.minPitch - tolerance && signedAnglePitch <= turret.maxPitch + tolerance);

            if (angleYaw < (turret.yawRange / 2) + tolerance && withinPitchRange)
            {
                if (BDArmorySettings.DEBUG_WEAPONS)
                {
                    Debug.Log("[BDArmory.MissileFire]: Checking turret range - target is INSIDE gimbal limits! signedAnglePitch: " + signedAnglePitch + ", minPitch: " + turret.minPitch + ", maxPitch: " + turret.maxPitch + ", tolerance: " + tolerance);
                }
                return true;
            }
            else
            {
                if (BDArmorySettings.DEBUG_WEAPONS)
                {
                    Debug.Log("[BDArmory.MissileFire]: Checking turret range - target is OUTSIDE gimbal limits! signedAnglePitch: " + signedAnglePitch + ", minPitch: " + turret.minPitch + ", maxPitch: " + turret.maxPitch + ", angleYaw: " + angleYaw + ", tolerance: " + tolerance);
                }
                return false;
            }
        }

        public bool CheckAmmo(ModuleWeapon weapon)
        {
            string ammoName = weapon.ammoName;
            if (ammoName == "ElectricCharge") return true; // Electric charge is almost always rechargable, so weapons that use it always have ammo.
            if (BDArmorySettings.INFINITE_AMMO) //check for infinite ammo
            {
                return true;
            }
            else
            {
                using (List<Part>.Enumerator p = vessel.parts.GetEnumerator())
                    while (p.MoveNext())
                    {
                        if (p.Current == null) continue;
                        using (IEnumerator<PartResource> resource = p.Current.Resources.GetEnumerator())
                            while (resource.MoveNext())
                            {
                                if (resource.Current == null) continue;
                                if (resource.Current.resourceName != ammoName) continue;
                                if (resource.Current.amount > 0)
                                {
                                    return true;
                                }
                            }
                    }
                return false;
            }
        }

        public bool outOfAmmo = false; // Indicator for being out of ammo.
        public bool HasWeaponsAndAmmo(List<WeaponClasses> weaponClasses = null)
        { // Check if the vessel has both weapons and ammo for them. Optionally, restrict checks to a subset of the weapon classes.
            if (outOfAmmo && !BDArmorySettings.INFINITE_AMMO) return false; // It's already been checked and found to be true, don't look again.
            bool hasWeaponsAndAmmo = false;
            foreach (var weapon in VesselModuleRegistry.GetModules<IBDWeapon>(vessel))
            {
                if (weapon == null) continue; // First entry is the "no weapon" option.
                if (weaponClasses != null && !weaponClasses.Contains(weapon.GetWeaponClass())) continue; // Ignore weapon classes we're not interested in.
                if (weapon.GetWeaponClass() == WeaponClasses.Gun || weapon.GetWeaponClass() == WeaponClasses.Rocket)
                {
                    if (BDArmorySettings.INFINITE_AMMO || CheckAmmo((ModuleWeapon)weapon)) { hasWeaponsAndAmmo = true; break; } // If the gun has ammo or we're using infinite ammo, return true after cleaning up.
                }
                else { hasWeaponsAndAmmo = true; break; } // Other weapon types don't have ammo, or use electric charge, which could recharge.
            }
            outOfAmmo = !hasWeaponsAndAmmo; // Set outOfAmmo if we don't have any guns with compatible ammo.
            return hasWeaponsAndAmmo;
        }

        public int CountWeapons(List<WeaponClasses> weaponClasses = null)
        { // Count number of weapons with ammo
            int countWeaponsAndAmmo = 0;
            foreach (var weapon in VesselModuleRegistry.GetModules<IBDWeapon>(vessel))
            {
                if (weapon == null) continue; // First entry is the "no weapon" option.
                if (weaponClasses != null && !weaponClasses.Contains(weapon.GetWeaponClass())) continue; // Ignore weapon classes we're not interested in.
                if (weapon.GetWeaponClass() == WeaponClasses.Gun || weapon.GetWeaponClass() == WeaponClasses.Rocket || weapon.GetWeaponClass() == WeaponClasses.DefenseLaser)
                {
                    if (weapon.GetShortName().EndsWith("Laser")) { countWeaponsAndAmmo++; continue; } // If it's a laser (counts as a gun) consider it as having ammo and count it, since electric charge can replenish.
                    if (BDArmorySettings.INFINITE_AMMO || CheckAmmo((ModuleWeapon)weapon)) { countWeaponsAndAmmo++; } // If the gun has ammo or we're using infinite ammo, count it.
                }
                else { countWeaponsAndAmmo++; } // Other weapon types don't have ammo, or use electric charge, which could recharge, so count them.
            }
            return countWeaponsAndAmmo;
        }


        void ToggleTurret()
        {
            using (var weapon = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                while (weapon.MoveNext())
                {
                    if (weapon.Current == null) continue;
                    if (selectedWeapon == null)
                    {
                        if (!weapon.Current.isAPS) weapon.Current.DisableWeapon();
                    }
                    else if (weapon.Current.GetShortName() != selectedWeapon.GetShortName())
                    {
                        if (weapon.Current.turret != null && (weapon.Current.ammoCount > 0 || BDArmorySettings.INFINITE_AMMO)) // Put turrets in standby (tracking only) mode instead of disabling them if they have ammo.
                        {
                            if (!weapon.Current.isAPS) weapon.Current.StandbyWeapon();
                        }
                        else
                        {
                            if (!weapon.Current.isAPS) weapon.Current.DisableWeapon();
                        }
                    }
                    else
                    {
                        weapon.Current.EnableWeapon();
                    }
                }
        }

        #endregion Turret

        #region Aimer

        void BombAimer()
        {
            if (selectedWeapon == null)
            {
                showBombAimer = false;
                return;
            }
            if (!bombPart || selectedWeapon.GetPart() != bombPart)
            {
                if (selectedWeapon.GetWeaponClass() == WeaponClasses.Bomb)
                {
                    bombPart = selectedWeapon.GetPart();
                }
                else
                {
                    showBombAimer = false;
                    return;
                }
            }

            showBombAimer =
            (
                !MapView.MapIsEnabled &&
                vessel.isActiveVessel &&
                selectedWeapon != null &&
                selectedWeapon.GetWeaponClass() == WeaponClasses.Bomb &&
                bombPart != null &&
                BDArmorySettings.DRAW_AIMERS &&
                vessel.verticalSpeed < 50 &&
                AltitudeTrigger()
            );

            if (!showBombAimer && (!guardMode || weaponIndex <= 0 ||
                                   selectedWeapon.GetWeaponClass() != WeaponClasses.Bomb)) return;
            MissileBase ml = bombPart.GetComponent<MissileBase>();

            float simDeltaTime = 0.1f;
            float simTime = 0;
            Vector3 dragForce = Vector3.zero;
            Vector3 prevPos = ml.MissileReferenceTransform.position;
            Vector3 currPos = ml.MissileReferenceTransform.position;
            //Vector3 simVelocity = vessel.rb_velocity;
            Vector3 simVelocity = vessel.Velocity(); //Issue #92

            MissileLauncher launcher = ml as MissileLauncher;
            if (launcher != null)
            {
                simVelocity += launcher.decoupleSpeed *
                               (launcher.decoupleForward
                                   ? launcher.MissileReferenceTransform.forward
                                   : -launcher.MissileReferenceTransform.up);
            }
            else
            {   //TODO: BDModularGuidance review this value
                simVelocity += 5 * -launcher.MissileReferenceTransform.up;
            }

            List<Vector3> pointPositions = new List<Vector3>();
            pointPositions.Add(currPos);

            prevPos = ml.MissileReferenceTransform.position;
            currPos = ml.MissileReferenceTransform.position;

            bombAimerPosition = Vector3.zero;
            int aimerLayerMask = (int)(LayerMasks.Scenery | LayerMasks.EVA); // Why EVA?

            bool simulating = true;
            while (simulating)
            {
                prevPos = currPos;
                currPos += simVelocity * simDeltaTime;
                float atmDensity =
                    (float)
                    FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(currPos),
                        FlightGlobals.getExternalTemperature(), FlightGlobals.currentMainBody);

                simVelocity += FlightGlobals.getGeeForceAtPosition(currPos) * simDeltaTime;
                float simSpeedSquared = simVelocity.sqrMagnitude;

                launcher = ml as MissileLauncher;
                float drag = 0;
                if (launcher != null)
                {
                    drag = launcher.simpleDrag;
                    if (simTime > launcher.deployTime)
                    {
                        drag = launcher.deployedDrag;
                    }
                }
                else
                {
                    //TODO:BDModularGuidance drag calculation
                    drag = ml.vessel.parts.Sum(x => x.dragScalar);
                }

                dragForce = (0.008f * bombPart.mass) * drag * 0.5f * simSpeedSquared * atmDensity * simVelocity.normalized;
                simVelocity -= (dragForce / bombPart.mass) * simDeltaTime;

                Ray ray = new Ray(prevPos, currPos - prevPos);
                RaycastHit hitInfo;
                if (Physics.Raycast(ray, out hitInfo, Vector3.Distance(prevPos, currPos), aimerLayerMask))
                {
                    bombAimerPosition = hitInfo.point;
                    simulating = false;
                }
                else if (FlightGlobals.getAltitudeAtPos(currPos) < 0)
                {
                    bombAimerPosition = currPos -
                                        (FlightGlobals.getAltitudeAtPos(currPos) * FlightGlobals.getUpAxis());
                    simulating = false;
                }

                simTime += simDeltaTime;
                pointPositions.Add(currPos);
            }

            //debug lines
            if (BDArmorySettings.DEBUG_LINES && BDArmorySettings.DRAW_AIMERS)
            {
                Vector3[] pointsArray = pointPositions.ToArray();
                lr = GetComponent<LineRenderer>();
                if (!lr) { lr = gameObject.AddComponent<LineRenderer>(); }
                lr.enabled = true;
                lr.startWidth = .1f;
                lr.endWidth = .1f;
                lr.positionCount = pointsArray.Length;
                for (int i = 0; i < pointsArray.Length; i++)
                {
                    lr.SetPosition(i, pointsArray[i]);
                }
            }
        }

        // Check antiRad target is within 20m for stationary targets, and a scaling distance based on target speed for targets moving faster than ~60 m/s
        bool AntiRadDistanceCheck()
        {
            return (antiRadiationTarget - guardTarget.CoM).sqrMagnitude < Mathf.Max(20f * 20f, (float)guardTarget.srfSpeed * 7f);
        }

        bool AltitudeTrigger()
        {
            const float maxAlt = 10000;
            double asl = vessel.mainBody.GetAltitude(vessel.CoM);
            double radarAlt = asl - vessel.terrainAltitude;

            return radarAlt < maxAlt || asl < maxAlt;
        }

        #endregion Aimer
    }
}
